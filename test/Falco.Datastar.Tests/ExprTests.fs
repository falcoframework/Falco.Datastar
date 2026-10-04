namespace Falco.Datastar.Tests

open System
open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

// Typed signals, expressions and statements. They replace the JavaScript strings in Ds.text, Ds.show, Ds.onClick and the like.
// The comments name the rule of the Tao of Datastar that a case protects.
module ExprTests =
    let private count = Signal.browser<int> "count"
    let private menuOpen = Signal.browser<bool> "menuOpen"
    let private firstName = Signal.server<string> "form.firstName"
    let private isOn = Signal.rocket<bool> "on"

    // Signals: where a signal lives decides whether Datastar sends it to the server

    [<Fact>]
    let ``A browser signal is written with the underscore that keeps it out of requests`` () =
        Signal.path count |> should equal "_count"
        Expr.toString (Expr.read count) |> should equal "$_count"

    [<Fact>]
    let ``A server signal keeps its name, so Datastar sends it with requests`` () =
        Signal.path firstName |> should equal "form.firstName"
        Expr.toString (Expr.read firstName) |> should equal "$form.firstName"

    [<Fact>]
    let ``A Rocket component signal is read with two dollar signs, as Rocket expects`` () =
        Expr.toString (Expr.read isOn) |> should equal "$$on"

    [<Fact>]
    let ``A dotted browser signal keeps the whole group out of requests`` () =
        Signal.path (Signal.browser<string> "form.tab") |> should equal "_form.tab"

    [<Fact>]
    let ``A signal remembers where it lives`` () =
        Signal.scope count |> should equal SignalScope.Browser
        Signal.scope firstName |> should equal SignalScope.Server
        Signal.scope isOn |> should equal SignalScope.RocketComponent

    let private problemOf scope name =
        match Signal.tryCreate<int> scope name with
        | Error error -> error
        | Ok _ -> failwith $"expected an error for '{name}'"

    [<Fact>]
    let ``A server signal cannot start with an underscore, because Datastar would keep it in the browser`` () =
        let error = problemOf SignalScope.Server "_secret"
        error |> should equal (SignalNameError.StartsWithUnderscore (SignalScope.Server, "_secret"))
        error.Message |> should equal "The signal name '_secret' starts with an underscore. Datastar keeps a signal like that in the browser and never sends it to the server. Use Signal.browser for a browser-only signal, and write the name without the underscore. Use a name without an underscore for a signal you want to send."

    [<Fact>]
    let ``A browser or Rocket signal is told to leave the underscore out`` () =
        (problemOf SignalScope.Browser "_x").Message |> should equal "The signal name '_x' starts with an underscore. Signal.browser adds the underscore itself, so write the name without it."
        (problemOf SignalScope.RocketComponent "_x").Message |> should equal "The signal name '_x' starts with an underscore. Write the name without it."

    [<Fact>]
    let ``A name with a hyphen is refused, because an expression reads it as minus`` () =
        let error = problemOf SignalScope.Browser "my-count"
        error |> should equal (SignalNameError.HasHyphen "my-count")
        error.Message |> should equal "The signal name 'my-count' has a hyphen. In an expression Datastar reads a hyphen as minus. Use camelCase instead, for example 'myCount'."

    [<Theory>]
    // A part that starts with a letter and is made of letters, digits and underscores is the whole rule, so anything
    // else has to be refused. A dollar sign is the one that matters: it is what an expression uses for a signal.
    [<InlineData("a$b")>]
    [<InlineData("$a")>]
    [<InlineData("a$b.c")>]
    [<InlineData("a.b$c")>]
    let ``A name with a dollar sign in it is refused`` (name: string) =
        problemOf SignalScope.Browser name |> should equal (SignalNameError.NotAPath name)

    [<Fact>]
    let ``A blank name is refused`` () =
        problemOf SignalScope.Browser " " |> should equal SignalNameError.Blank
        (problemOf SignalScope.Browser "").Message |> should equal "A signal needs a name. Give it a camelCase name such as 'menuOpen', or a dotted one such as 'form.firstName'."

    // Datastar reads the name of a signal from an attribute name. HTML makes that lower case, and Datastar reads '__' as the start of a modifier.
    // A name that would be read differently from the way an expression writes it is refused, so the two cannot disagree.

    [<Fact>]
    let ``A part that starts with a capital letter is refused, because the attribute name would make it lower case`` () =
        let error = problemOf SignalScope.Server "form.First"
        error |> should equal (SignalNameError.StartsWithCapital "form.First")
        error.Message |> should equal "The signal name 'form.First' has a part that starts with a capital letter. Datastar reads the name from an attribute name, and HTML makes attribute names lower case, so the signal would be called 'form.first' there but 'form.First' in an expression. Start each part with a lower case letter, for example 'form.first'."

    [<Fact>]
    let ``A name with two underscores in a row is refused, because Datastar reads them as a modifier`` () =
        let error = problemOf SignalScope.Server "a__b"
        error |> should equal (SignalNameError.HasDoubleUnderscore "a__b")
        error.Message |> should equal "The signal name 'a__b' has two underscores in a row. Datastar reads '__' in an attribute name as the start of a modifier, so it would read 'a__b' as 'a' with a modifier. Use a single underscore, or camelCase."

    [<Fact>]
    let ``A part that ends with an underscore is refused, because it would run into a modifier`` () =
        let error = problemOf SignalScope.Server "a_"
        error |> should equal (SignalNameError.EndsWithUnderscore "a_")
        error.Message |> should equal "The signal name 'a_' has a part that ends with an underscore. Datastar would read that underscore together with the two that start a modifier, and lose part of the name. Remove the trailing underscore."

    // The trailing-underscore case is caught by its own guard, before the rule that reads a name as a path is reached.
    // Without the guard the name would still be refused, but as NotAPath, which does not say what is wrong with it.
    // An underscore in the middle of a part is fine, because it is followed by a letter.
    [<Theory>]
    [<InlineData("a_")>]
    [<InlineData("form.a_")>]
    [<InlineData("a_1_b_")>]
    let ``A name that ends with an underscore is refused for that reason, not as a name that is not a path`` (name: string) =
        problemOf SignalScope.Server name
        |> function
            | SignalNameError.EndsWithUnderscore reported -> reported |> should equal name
            | other -> failwith $"expected EndsWithUnderscore for '{name}', got {other}"

    [<Theory>]
    [<InlineData("a_b")>]
    [<InlineData("form.a_b")>]
    [<InlineData("a_1")>]
    [<InlineData("a_b1_c2")>]
    let ``An underscore in the middle of a name part is accepted`` (name: string) =
        match Signal.tryCreate<int> SignalScope.Server name with
        | Ok signal -> Signal.path signal |> should equal name
        | Error error -> failwith error.Message

    [<Fact>]
    let ``A name that ends with a line break is refused, although a dollar sign in a pattern would let it through`` () =
        problemOf SignalScope.Server "menu\n" |> should equal (SignalNameError.NotAPath "menu\n")

    [<Theory>]
    [<InlineData("1abc")>]
    [<InlineData("a b")>]
    [<InlineData("a..b")>]
    [<InlineData("a.")>]
    [<InlineData("$a")>]
    [<InlineData("a$")>]
    let ``A name that Datastar cannot read as a signal path is refused`` (name: string) =
        problemOf SignalScope.Browser name |> should equal (SignalNameError.NotAPath name)

    [<Theory>]
    [<InlineData("count")>]
    [<InlineData("menuOpen")>]
    [<InlineData("form.firstName")>]
    [<InlineData("first_name")>]
    [<InlineData("a1")>]
    [<InlineData("a_1")>]
    let ``A name that Datastar reads as written is accepted`` (name: string) =
        match Signal.tryCreate<int> SignalScope.Server name with
        | Ok signal -> Signal.path signal |> should equal name
        | Error error -> failwith error.Message

    [<Fact>]
    let ``Signal.browser raises with the same message when the name is invalid`` () =
        let raised = Assert.Throws<ArgumentException>(fun () -> Signal.browser<int> "my-count" |> ignore)
        raised.Message |> should haveSubstring "Use camelCase instead, for example 'myCount'."

    /// What Datastar 1.0.4 does with the key of a data-signals attribute: it splits at '__' and applies its camel case to what is left (library/src/utils/text.ts).
    let private datastarSignalName (attribute:string) =
        let key = attribute.Substring("data-signals:".Length)
        let name = key.Split("__").[0]
        Text.RegularExpressions.Regex.Replace(name, "-[a-z]", fun found -> found.Value.Substring(1).ToUpperInvariant())

    [<Fact>]
    let ``Every name that is accepted is read by Datastar as the name that an expression uses`` () =
        Dst.run "Every name that is accepted" (fun generator ->
            // A longer name than the shared generator draws, so that a deep dotted path is covered as well as a short one
            let alphabet = [| "a"; "b"; "c"; "X"; "Y"; "Z"; "0"; "1"; "9"; "_"; "-"; "."; "$"; " " |]
            // Rocket is not here: a Rocket signal cannot be written as an attribute name, which the tests above say
            let scopes = [| SignalScope.Browser; SignalScope.Server |]
            let mutable accepted = 0
            for _ in 1 .. 1200 do
                let name =
                    [ for _ in 1 .. Dst.intBetween 1 9 generator -> alphabet.[Dst.intBelow alphabet.Length generator] ]
                    |> String.concat ""
                match Signal.tryCreate<int> (Dst.pick generator scopes) name with
                | Error _ -> ()
                | Ok signal ->
                    accepted <- accepted + 1
                    // A modifier that the library adds is still a modifier, and does not change the name
                    let rendered = renderAttr (Ds.signal (signal, 1, ifMissing = true))
                    let attribute = rendered.Substring("<div ".Length, rendered.IndexOf '=' - "<div ".Length)
                    datastarSignalName attribute |> should equal (Signal.path signal)
                    attribute |> should endWith "__ifmissing"
            // The alphabet is mostly bad characters, so a run that accepted nothing would prove nothing
            accepted |> should be (greaterThan 20))

    // These two are the examples the README shows. A test that pins them keeps the documentation and the output in step.

    [<Fact>]
    let ``README example: dividing an int signal gives a whole number`` () =
        // binary puts one pair of parentheses around the operation, and Math.trunc goes in front of it
        Expr.toString (Expr.divide (Expr.read count) (Expr.int 5)) |> should equal "Math.trunc($_count / 5)"

    [<Fact>]
    let ``README example: several statements in one attribute`` () =
        Stmt.toString (Stmt.all [ Stmt.set count (Expr.int 0); Stmt.toggle menuOpen ])
        |> should equal "$_count = 0; $_menuOpen = !$_menuOpen"

    // Expressions

    [<Fact>]
    let ``Literals are written as JavaScript literals`` () =
        Expr.toString (Expr.int 5) |> should equal "5"
        Expr.toString (Expr.int -3) |> should equal "(-3)"
        Expr.toString (Expr.float 1.5) |> should equal "1.5"
        Expr.toString (Expr.bool true) |> should equal "true"
        Expr.toString (Expr.string "hi") |> should equal "'hi'"

    [<Fact>]
    let ``A float is written with a dot whatever the current culture is`` () =
        withCommaDecimalCulture (fun () -> Expr.toString (Expr.float 1.5) |> should equal "1.5")

    [<Fact>]
    let ``A float that is not a finite number is still a JavaScript literal`` () =
        Expr.toString (Expr.float Double.NaN) |> should equal "NaN"
        Expr.toString (Expr.float Double.PositiveInfinity) |> should equal "Infinity"
        Expr.toString (Expr.float Double.NegativeInfinity) |> should equal "(-Infinity)"

    [<Fact>]
    let ``A string literal cannot break out of its quotes or of the attribute`` () =
        Expr.toString (Expr.string "it's \"q\" <b>")
        |> should equal """'it\'s &quot;q&quot; &lt;b&gt;'"""
        Expr.toString (Expr.string "x');alert(1);//")
        |> should equal """'x\');alert(1);//'"""

    [<Fact>]
    let ``Arithmetic is written with spaces, because $a-1 would be read as a signal called a-1`` () =
        Expr.toString (Expr.subtract (Expr.read count) (Expr.int 1)) |> should equal "($_count - 1)"
        Expr.toString (Expr.add (Expr.read count) (Expr.int 1)) |> should equal "($_count + 1)"
        Expr.toString (Expr.multiply (Expr.read count) (Expr.int 2)) |> should equal "($_count * 2)"
        Expr.toString (Expr.remainder (Expr.read count) (Expr.int 3)) |> should equal "($_count % 3)"

    [<Fact>]
    let ``Dividing whole numbers gives a whole number, because a JavaScript number has no integer type`` () =
        // 7 / 2 is 3.5 in JavaScript, and an int signal must not hold that
        Expr.toString (Expr.divide (Expr.read count) (Expr.int 2)) |> should equal "Math.trunc($_count / 2)"
        Expr.toString (Expr.divide (Expr.read (Signal.browser<int64> "big")) (Expr.unsafeRaw<int64> "2")) |> should equal "Math.trunc($_big / 2)"

    [<Fact>]
    let ``Dividing other numbers keeps the fraction`` () =
        Expr.toString (Expr.divide (Expr.read (Signal.browser<float> "ratio")) (Expr.float 2.0)) |> should equal "($_ratio / 2)"
        Expr.toString (Expr.divide (Expr.read (Signal.browser<decimal> "price")) (Expr.unsafeRaw<decimal> "4")) |> should equal "($_price / 4)"

    [<Fact>]
    let ``Comparisons use the strict JavaScript operators`` () =
        Expr.toString (Expr.greater (Expr.read count) (Expr.int 5)) |> should equal "($_count > 5)"
        Expr.toString (Expr.less (Expr.read count) (Expr.int 5)) |> should equal "($_count < 5)"
        Expr.toString (Expr.atLeast (Expr.read count) (Expr.int 5)) |> should equal "($_count >= 5)"
        Expr.toString (Expr.atMost (Expr.read count) (Expr.int 5)) |> should equal "($_count <= 5)"
        Expr.toString (Expr.equal (Expr.read count) (Expr.int 5)) |> should equal "($_count === 5)"
        Expr.toString (Expr.notEqual (Expr.read count) (Expr.int 5)) |> should equal "($_count !== 5)"

    [<Fact>]
    let ``Boolean expressions combine`` () =
        let big = Expr.greater (Expr.read count) (Expr.int 5)
        Expr.toString (Expr.andAlso big (Expr.read menuOpen)) |> should equal "(($_count > 5) && $_menuOpen)"
        Expr.toString (Expr.orElse big (Expr.read menuOpen)) |> should equal "(($_count > 5) || $_menuOpen)"
        Expr.toString (Expr.negate (Expr.read menuOpen)) |> should equal "(!$_menuOpen)"

    [<Fact>]
    let ``A condition picks one of two values of the same type`` () =
        Expr.toString (Expr.ifElse (Expr.read menuOpen) (Expr.string "true") (Expr.string "false"))
        |> should equal "($_menuOpen ? 'true' : 'false')"

    [<Fact>]
    let ``Text is joined with plus, and empty text is an empty string`` () =
        Expr.toString (Expr.concat [ Expr.string "Hello "; Expr.read firstName; Expr.string "!" ])
        |> should equal "('Hello ' + $form.firstName + '!')"
        Expr.toString (Expr.concat []) |> should equal "''"

    [<Fact>]
    let ``Any value can be turned into text`` () =
        Expr.toString (Expr.toText (Expr.read count)) |> should equal "String($_count)"

    [<Fact>]
    let ``unsafeRaw passes JavaScript through, for what the typed functions do not cover`` () =
        Expr.toString (Expr.unsafeRaw<string> "evt.key") |> should equal "evt.key"
        Expr.toString (Expr.unsafeRaw<int> "$count") |> should equal "$count"

    [<Fact>]
    let ``unsafeRaw puts JavaScript that is more than a name in parentheses, so an operator applies to all of it`` () =
        let either = Expr.unsafeRaw<bool> "$a || $b"
        Expr.toString either |> should equal "($a || $b)"
        Expr.toString (Expr.negate either) |> should equal "(!($a || $b))"
        Expr.toString (Expr.andAlso either (Expr.bool true)) |> should equal "(($a || $b) && true)"

    [<Fact>]
    let ``unsafeRaw escapes the JavaScript for the attribute, like every other expression`` () =
        Expr.toString (Expr.unsafeRaw<bool> "$a && \"x\" < 1") |> should equal "($a &amp;&amp; &quot;x&quot; &lt; 1)"
        Stmt.toString (Stmt.unsafeRaw "$a = \"x\"") |> should equal "$a = &quot;x&quot;"
        renderAttr (Ds.onClick (Stmt.unsafeRaw "$a = \"x\"")) |> should equal """<div data-on:click="$a = &quot;x&quot;"></div>"""

    [<Fact>]
    let ``Stmt.all needs a statement, because an empty expression makes Datastar throw`` () =
        let error = Assert.Throws<ArgumentException>(fun () -> Stmt.all [] |> ignore)
        error.Message |> should haveSubstring "Stmt.all needs at least one statement."

    // Statements

    [<Fact>]
    let ``A statement sets a signal to an expression of the same type`` () =
        Stmt.toString (Stmt.set count (Expr.add (Expr.read count) (Expr.int 1))) |> should equal "$_count = ($_count + 1)"
        Stmt.toString (Stmt.set isOn (Expr.bool true)) |> should equal "$$on = true"

    [<Fact>]
    let ``A statement can flip a boolean signal`` () =
        Stmt.toString (Stmt.toggle menuOpen) |> should equal "$_menuOpen = !$_menuOpen"

    [<Fact>]
    let ``Statements run one after another`` () =
        Stmt.toString (Stmt.all [ Stmt.toggle menuOpen; Stmt.set count (Expr.int 0) ])
        |> should equal "$_menuOpen = !$_menuOpen; $_count = 0"

    [<Fact>]
    let ``Backend actions are statements`` () =
        Stmt.toString (Stmt.get "/items") |> should equal "@get('/items')"
        Stmt.toString (Stmt.post "/items") |> should equal "@post('/items')"
        Stmt.toString (Stmt.put "/items") |> should equal "@put('/items')"
        Stmt.toString (Stmt.patch "/items") |> should equal "@patch('/items')"
        Stmt.toString (Stmt.delete "/items") |> should equal "@delete('/items')"
        Stmt.toString (Stmt.query "/items") |> should equal "@query('/items')"

    [<Fact>]
    let ``A URL cannot break out of its quotes`` () =
        Stmt.toString (Stmt.get "/x');alert(1);//") |> should equal """@get('/x\');alert(1);//')"""

    [<Fact>]
    let ``Backend actions take request options, and write the same as the string helpers`` () =
        let options = { RequestOptions.Defaults with Retry = OnError }
        Stmt.toString (Stmt.postWith "/items" options) |> should equal (Ds.post ("/items", options))

    [<Fact>]
    let ``setAll and toggleAll are statements, and write the same as the string helpers`` () =
        Stmt.toString (Stmt.setAll "foo." true) |> should equal (Ds.setAll ("foo.", true))
        Stmt.toString (Stmt.setAll "foo." 5) |> should equal """@setAll(5, { include: /^foo\./ })"""
        Stmt.toString (Stmt.toggleAll "foo.") |> should equal (Ds.toggleAll "foo.")
        Stmt.toString (Stmt.setAllWhere SignalsFilter.None false) |> should equal "@setAll(false)"
        Stmt.toString (Stmt.toggleAllWhere SignalsFilter.None) |> should equal "@toggleAll()"

    [<Fact>]
    let ``peek reads a value without subscribing to the signals in it`` () =
        Expr.toString (Expr.peek (Expr.read count)) |> should equal "@peek(() => $_count)"
        Stmt.toString (Stmt.set menuOpen (Expr.greater (Expr.peek (Expr.read count)) (Expr.int 0)))
        |> should equal "$_menuOpen = (@peek(() => $_count) > 0)"

    // Attributes take typed values

    [<Fact>]
    let ``Ds.text, Ds.show and Ds.class' take expressions`` () =
        renderAttr (Ds.text (Expr.read count)) |> should equal """<div data-text="$_count"></div>"""
        renderAttr (Ds.show (Expr.read menuOpen)) |> should equal """<div data-show="$_menuOpen"></div>"""
        renderAttr (Ds.class' ("active", Expr.read menuOpen)) |> should equal """<div data-class:active="$_menuOpen"></div>"""

    [<Fact>]
    let ``Ds.attr' and Ds.style take expressions`` () =
        renderAttr (Ds.attr' ("aria-expanded", Expr.ifElse (Expr.read menuOpen) (Expr.string "true") (Expr.string "false")))
        |> should equal """<div data-attr:aria-expanded="($_menuOpen ? 'true' : 'false')"></div>"""
        renderAttr (Ds.style ("opacity", Expr.read count))
        |> should equal """<div data-style:opacity="$_count"></div>"""

    [<Fact>]
    let ``Ds.signal creates a signal with a value of its type`` () =
        renderAttr (Ds.signal (count, 0)) |> should equal """<div data-signals:_count="0"></div>"""
        renderAttr (Ds.signal (menuOpen, false)) |> should equal """<div data-signals:_menu-open="false"></div>"""
        renderAttr (Ds.signal (firstName, "Ada")) |> should equal """<div data-signals:form.first-name="'Ada'"></div>"""

    [<Fact>]
    let ``Ds.signal refuses a Rocket component signal, because the name would mean a page signal`` () =
        // Rocket scopes an attribute's signal name to one instance only for a signal the component declared with
        // $$('name', value) in its setup, and only inside that component. Anywhere else the name would be a page
        // signal, and two components would quietly share it, so it is refused rather than written.
        let refused = Assert.Throws<ArgumentException>(fun () -> Ds.signal (isOn, false) |> ignore)
        let message = refused.Message
        if not (message.Contains "Rocket component signal") then failwith message
        if not (message.Contains "two components would share it") then failwith message

    [<Fact>]
    let ``Ds.bind, Ds.computed and Ds.indicator refuse a Rocket component signal too`` () =
        for name, build in
            [ "Ds.bind", fun () -> Ds.bind (isOn : Signal<bool>) |> ignore
              "Ds.computed", fun () -> Ds.computed (isOn, Expr.bool true) |> ignore
              "Ds.indicator", fun () -> Ds.indicator (Signal.rocket<bool> "open") |> ignore ] do
            let refused = Assert.Throws<ArgumentException>(build)
            let message = refused.Message
            if not (message.Contains name) then failwith $"the message does not name the helper: {message}"

    [<Fact>]
    let ``a Rocket signal still reads as two dollars in an expression, which is how it is used`` () =
        // The refusal is only about attribute names. Reading it in an expression is the thing Rocket scopes, so it works
        Expr.toString (Expr.read isOn) |> should equal "$$on"
        Stmt.toString (Stmt.set isOn (Expr.bool true)) |> should equal "$$on = true"

    [<Fact>]
    let ``Ds.signal escapes a text value`` () =
        renderAttr (Ds.signal (firstName, "it's \"q\""))
        |> should equal """<div data-signals:form.first-name="'it\'s &quot;q&quot;'"></div>"""

    [<Fact>]
    let ``Ds.bind and Ds.indicator take signals`` () =
        renderAttr (Ds.bind firstName) |> should equal """<div data-bind:form.first-name></div>"""
        renderAttr (Ds.indicator (Signal.browser<bool> "loading")) |> should equal """<div data-indicator:_loading></div>"""

    [<Fact>]
    let ``Ds.computed takes a signal and an expression of its type`` () =
        let total = Signal.server<int> "total"
        renderAttr (Ds.computed (total, Expr.multiply (Expr.read count) (Expr.int 2)))
        |> should equal """<div data-computed:total="($_count * 2)"></div>"""

    [<Fact>]
    let ``Events take statements`` () =
        renderAttr (Ds.onClick (Stmt.toggle menuOpen)) |> should equal """<div data-on:click="$_menuOpen = !$_menuOpen"></div>"""
        renderAttr (Ds.onEvent ("keydown", Stmt.set count (Expr.int 0), [ Window ]))
        |> should equal """<div data-on:keydown__window="$_count = 0"></div>"""
        renderAttr (Ds.onInit (Stmt.get "/stream")) |> should equal """<div data-init="@get('/stream')"></div>"""
        renderAttr (Ds.effect (Stmt.set count (Expr.int 1))) |> should equal """<div data-effect="$_count = 1"></div>"""

    [<Fact>]
    let ``Timed events take statements`` () =
        renderAttr (Ds.onInterval (Stmt.get "/tick", 2000)) |> should equal """<div data-on-interval__duration.2000ms="@get('/tick')"></div>"""
        renderAttr (Ds.onIntersect (Stmt.get "/more", onlyOnce = true)) |> should equal """<div data-on-intersect__once="@get('/more')"></div>"""

    // Rocket

    [<Fact>]
    let ``Rocket.templateIf takes an expression`` () =
        renderNode (Rocket.templateIf (Expr.read isOn, [ Text.raw "on" ]))
        |> should equal """<template data-if="$$on">on</template>"""
        renderNode (Rocket.templateElseIf (Expr.negate (Expr.read isOn), [ Text.raw "off" ]))
        |> should equal """<template data-else-if="(!$$on)">off</template>"""

    [<Fact>]
    let ``Rocket.forEach gives the rows a typed item and index, so nothing is matched by name`` () =
        let items = Signal.rocket<string list> "items"
        renderNode (Rocket.forEach (Expr.read items, fun item index -> [ Elem.li [ Ds.text (Expr.concat [ Expr.toText index; Expr.string ": "; item ]) ] [] ]))
        |> should equal """<template data-for="$$items"><li data-text="(String(i) + ': ' + item)"></li></template>"""

    [<Fact>]
    let ``Rocket.forEach can name the item and the index`` () =
        let items = Signal.rocket<string list> "items"
        renderNode (Rocket.forEach (Expr.read items, (fun item _ -> [ Elem.li [ Ds.text item ] [] ]), itemName = "entry", indexName = "n"))
        |> should equal """<template data-for="entry, n in $$items"><li data-text="entry"></li></template>"""
