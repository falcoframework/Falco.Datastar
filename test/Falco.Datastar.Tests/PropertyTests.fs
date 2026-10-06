namespace Falco.Datastar.Tests

open System
open System.Text.RegularExpressions
open System.Web
open Falco.Datastar
open Falco.Markup
open FsCheck.Xunit
open FsUnit.Xunit
open Xunit

/// Property-based tests.
///
/// The other files check named cases: this URL escapes like this, this name is refused with that message. A property
/// test states a rule that has to hold for every input, and FsCheck searches for an input that breaks it. It finds the
/// case nobody thought of, and it keeps the seed of any failure so the case can be replayed.
///
/// The rules here are the ones the library's own reasoning depends on, so a counterexample is not a curiosity: it means
/// the escaping, the name rules or the arithmetic are wrong in a way that a hand-written case would have missed.
module PropertyTests =
    /// The plain chain of replacements that Js.stringLiteral stands in for on its fast path.
    /// It has to agree with what the library does, including the characters that a literal cannot hold at all:
    /// a NUL becomes \u0000 and the four HTML characters are written as references.
    let private reference (value: string) =
        value
            .Replace("\\", "\\\\").Replace("'", "\\'")
            .Replace("\n", "\\n").Replace("\r", "\\r")
            .Replace("\000", "\\u0000")
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

    /// The value of a rendered attribute, by the rendered element and the attribute's name
    let private attributeValue (rendered: string) (name: string) =
        let prefix = $"<div {name}=\""
        let suffix = "\"></div>"
        let start = rendered.IndexOf prefix
        if start < 0 then
            failwith $"the rendered element has no attribute called {name}: {rendered}"
        let from = start + prefix.Length
        rendered.Substring(from, rendered.Length - from - suffix.Length)

    // Escaping. The rule is that anything the library writes can be read back as what went in.
    // This is the property that the security of the library rests on, so it is checked for arbitrary text, not for examples.

    [<Property>]
    let ``Text in a signal is read back exactly, through the HTML parser and then the JavaScript parser`` (text: string) =
        let rendered = renderAttr (Ds.text (Expr.string text))
        readBack (attributeValue rendered "data-text") |> should equal text

    [<Property>]
    let ``A URL is read back exactly`` (url: string) =
        let action = Ds.post url
        let literal = action.Substring("@post(".Length, action.Length - "@post(".Length - ")".Length)
        readBack literal |> should equal url

    [<Property>]
    let ``A backend action writes the verb and an escaped URL`` (url: string) =
        Ds.post url |> should equal ("@post('" + reference url + "')")

    [<Property>]
    let ``An attribute value never has a quote, a bracket or a carriage return left in it`` (text: string) =
        let value = attributeValue (renderAttr (Ds.text (Expr.string text))) "data-text"
        for character in [ '"'; '<'; '>'; '\r' ] do
            if value.Contains character then
                failwith $"the rendered attribute still has '{character}' in it: {value}"

    [<Property>]
    let ``A Rocket prop is escaped for the attribute, and a parser reads back the text it stands for`` (text: string) =
        let rendered = renderAttr (Rocket.propString ("label", text))
        // Read the attribute back the way a browser does: an HTML parser undoes the escapes
        let value = HttpUtility.HtmlDecode(attributeValue rendered "label")
        // A carriage return is written as a character reference, so a parser keeps it, and a NUL cannot be kept at all
        // so it becomes the character a parser would give it
        value |> should equal (text.Replace('\000', '\uFFFD'))

    [<Property>]
    let ``The escaping is the same as the plain chain of replacements, for any text`` (text: string) =
        Expr.toString (Expr.string text) |> should equal ("'" + reference text + "'")

    // Signal names. The rule is that a name the library accepts is read by Datastar as the name an expression uses,
    // because if the two ever disagree then half of a page talks to one signal and the other half to another.

    /// What Datastar 1.0.4 does with the key of a data-signals attribute: it splits at '__' and applies its camel case
    /// to what is left (library/src/utils/text.ts, caseFns.camel).
    let private datastarSignalName (attribute: string) =
        let key = attribute.Substring("data-signals:".Length)
        let name = key.Split("__").[0]
        Regex.Replace(name, "-[a-z]", fun found -> found.Value.Substring(1).ToUpperInvariant())

    // These three rules only say something when the name is one the library accepts. FsCheck's default string generator
    // produces random bytes, and only a handful of names in a thousand survive the rules, so a property written against
    // it would assert nothing on most runs. So these draw their names from `Fragments.signalName`, which is built from
    // the pieces the name rules care about, and each one counts what it accepted and fails when there were too few.
    // Otherwise a generator that produced nothing usable would look like a pass.

    // Rocket is not here: a Rocket signal cannot be written as an attribute name, which the tests below say
    let private scopes = [| SignalScope.Browser; SignalScope.Server |]

    /// The three rules below are only worth anything if the names they run over are a mix of accepted and refused.
    /// This measures that, so that a change to the generator or to the rules cannot quietly make them vacuous.
    [<Fact>]
    let ``the signal name generator produces names that are both accepted and refused`` () =
        let generator = Generator.ofSeed 1
        let mutable accepted = 0
        let mutable refused = 0
        for _ in 1 .. 600 do
            let name = Fragments.signalName generator
            match Signal.tryCreate<int> SignalScope.Server name with
            | Ok _ -> accepted <- accepted + 1
            | Error _ -> refused <- refused + 1
        // Both outcomes have to happen, or the rules below are only ever testing one path
        accepted |> should be (greaterThan 20)
        refused |> should be (greaterThan 20)

    [<Fact>]
    let ``Every signal name that is accepted is read by Datastar as the name an expression uses`` () =
        Dst.run "Every signal name that is accepted" (fun generator ->
            let mutable accepted = 0
            for _ in 1 .. 600 do
                let name = Fragments.signalName generator
                for scope in scopes do
                    match Signal.tryCreate<int> scope name with
                    | Error _ -> ()
                    | Ok signal ->
                        accepted <- accepted + 1
                        let rendered = renderAttr (Ds.signal (signal, 1, ifMissing = true))
                        let attribute = rendered.Substring("<div ".Length, rendered.IndexOf '=' - "<div ".Length)
                        datastarSignalName attribute
                        |> should equal (Signal.path signal)
            // A run in which nothing was accepted would prove nothing, because a refused name cannot disagree with anything
            accepted |> should be (greaterThan 20))

    [<Fact>]
    let ``A signal name that is refused always says what to write`` () =
        Dst.run "A signal name that is refused" (fun generator ->
            let mutable refused = 0
            for _ in 1 .. 600 do
                let name = Fragments.signalName generator
                match Signal.tryCreate<int> SignalScope.Server name with
                | Ok _ -> ()
                | Error error ->
                    refused <- refused + 1
                    // Every message says which signal it is about and what to do, so a person can act on it without the source
                    error.Message |> should not' (String.IsNullOrWhiteSpace "")
                    if not (error.Message.Contains "signal" || error.Message.Contains "name") then
                        failwith $"the message does not say what it is about, for '{name}': {error.Message}"
            refused |> should be (greaterThan 20))

    [<Fact>]
    let ``A browser signal is written with an underscore and a server signal is not`` () =
        Dst.run "A browser signal is written with an underscore" (fun generator ->
            let mutable accepted = 0
            for _ in 1 .. 600 do
                let name = Fragments.signalName generator
                match Signal.tryCreate<int> SignalScope.Browser name, Signal.tryCreate<int> SignalScope.Server name with
                | Ok browser, _ ->
                    accepted <- accepted + 1
                    Signal.path browser |> should equal ("_" + name)
                | _, Ok server ->
                    accepted <- accepted + 1
                    Signal.path server |> should equal name
                | _ -> ()
            accepted |> should be (greaterThan 20))

    // Expressions. The rules are what the operators promise, and they hold for every input, not for the examples.

    [<Property>]
    let ``An arithmetic expression is wrapped in one pair of parentheses`` (a: int) (b: int) =
        for text in [ Expr.toString (Expr.add (Expr.int a) (Expr.int b))
                      Expr.toString (Expr.subtract (Expr.int a) (Expr.int b))
                      Expr.toString (Expr.multiply (Expr.int a) (Expr.int b))
                      Expr.toString (Expr.remainder (Expr.int a) (Expr.int b)) ] do
            // It starts and ends with a parenthesis, so a neighbouring operator cannot reach into it
            if not (text.StartsWith "(" && text.EndsWith ")") then
                failwith $"the expression is not wrapped in one pair of parentheses: {text}"
            // The two operands are separated by spaces, because Datastar reads $a-1 as a signal called a-1
            if not (text.Contains " + " || text.Contains " - " || text.Contains " * " || text.Contains " % ") then
                failwith $"the operator is not surrounded by spaces: {text}"

    [<Property>]
    let ``Dividing whole numbers is always truncated`` (a: int) (b: int) =
        let text = Expr.toString (Expr.divide (Expr.int a) (Expr.int b))
        if not (text.StartsWith "Math.trunc(") then
            failwith $"dividing whole numbers does not truncate: {text}"

    [<Property>]
    let ``A comparison is always a strict JavaScript comparison`` (a: int) (b: int) =
        let equal = Expr.toString (Expr.equal (Expr.int a) (Expr.int b))
        let notEqual = Expr.toString (Expr.notEqual (Expr.int a) (Expr.int b))
        if not (equal.Contains "===") then failwith $"equality is not strict: {equal}"
        if not (notEqual.Contains "!==") then failwith $"inequality is not strict: {notEqual}"
        // A strict comparison has three characters, so == and != can never appear on their own
        if equal.Contains "==" && not (equal.Contains "===") then failwith $"equality is loose: {equal}"

    [<Property>]
    let ``A boolean expression keeps its parentheses, so a neighbouring operator cannot change its meaning`` (a: bool) (b: bool) =
        for text in [ Expr.toString (Expr.andAlso (Expr.bool a) (Expr.bool b))
                      Expr.toString (Expr.orElse (Expr.bool a) (Expr.bool b)) ] do
            if not (text.StartsWith "(" && text.EndsWith ")") then
                failwith $"the condition is not wrapped in parentheses: {text}"
        let negated = Expr.toString (Expr.negate (Expr.bool a))
        if not (negated.StartsWith "(" && negated.EndsWith ")") then
            failwith $"the negated condition is not wrapped in parentheses: {negated}"

    [<Property>]
    let ``A negative number keeps its parentheses`` (value: int) =
        let text = Expr.toString (Expr.int value)
        if value < 0 && not (text.StartsWith "(" && text.EndsWith ")") then
            failwith $"a negative number is not wrapped in parentheses: {text}"
        if value >= 0 && text.StartsWith "(" then
            failwith $"a number that is not negative was wrapped in parentheses: {text}"

    [<Property>]
    let ``A float is written with a dot whatever the current culture is`` (value: float) =
        let invariant = Expr.toString (Expr.float value)
        if invariant.Contains "," then
            failwith $"a float was written with a comma: {invariant}"
        if Double.IsFinite value then
            // The same value written under a culture that uses a comma has to come out the same way
            withCommaDecimalCulture (fun () -> Expr.toString (Expr.float value) |> should equal invariant)

    // Statements. Joining several has to keep each one's text and put only a separator between them.

    // The count is folded into 1 to 6 before it is used, and abs on Int32.MinValue would raise, so it is made positive
    // with a modulo instead. Every value maps to a count, so the whole input space is still covered.
    [<Property>]
    let ``Joining statements inserts only a separator`` (count: int) =
        let howMany = (count % 6 + 6) % 6 + 1
        let statements =
            [ for n in 1 .. howMany -> Stmt.set (Signal.browser<int> ("s" + string n)) (Expr.int n) ]
        let expected = statements |> List.map Stmt.toString
        let joined = Stmt.toString (Stmt.all statements)
        // There is exactly one separator between each pair, and nothing else between them
        joined |> should equal (String.Join("; ", expected))
        joined.Split(';').Length |> should equal howMany
        // Each statement's own text is still there, unchanged
        expected |> List.iter (fun text -> if not (joined.Contains text) then failwith $"'{text}' is missing from '{joined}'")
