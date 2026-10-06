namespace Falco.Datastar.Tests

open System
open System.Web
open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

// The string helpers put text into a JavaScript expression inside an HTML attribute.
// Anything from a user has to stay text, and states that Datastar cannot use must not be writable.
module EscapingTests =
    [<Fact>]
    let ``A URL cannot break out of its quotes in a backend action`` () =
        Ds.get "/x');alert(1);//" |> should equal """@get('/x\');alert(1);//')"""
        Ds.post "/x');alert(1);//" |> should equal """@post('/x\');alert(1);//')"""
        Ds.delete "/x');alert(1);//" |> should equal """@delete('/x\');alert(1);//')"""

    [<Fact>]
    let ``A URL with an ampersand stays one URL after the browser reads the attribute`` () =
        Ds.get "/items?a=1&b=2" |> should equal "@get('/items?a=1&amp;b=2')"

    [<Fact>]
    let ``A text value of Ds.signal is escaped`` () =
        renderAttr (Ds.signal (SignalPath.sp "a", "it's \" <b>"))
        |> should equal """<div data-signals:a="'it\'s &quot; &lt;b&gt;'"></div>"""

    [<Fact>]
    let ``A structured value of Ds.signal cannot break out of the attribute`` () =
        renderAttr (Ds.signal (SignalPath.sp "a", {| name = "x\"y" |}))
        |> should equal """<div data-signals:a="{&quot;name&quot;:&quot;x\u0022y&quot;}"></div>"""

    [<Fact>]
    let ``Numbers and booleans in Ds.signal are unchanged`` () =
        renderAttr (Ds.signal (SignalPath.sp "a", 5)) |> should equal """<div data-signals:a="5"></div>"""
        renderAttr (Ds.signal (SignalPath.sp "a", true)) |> should equal """<div data-signals:a="true"></div>"""

    [<Fact>]
    let ``A number that is not finite is a JavaScript literal and does not raise`` () =
        Ds.setAll ("a.", Double.NaN) |> should equal """@setAll(NaN, { include: /^a\./ })"""
        Ds.setAll ("a.", Double.PositiveInfinity) |> should equal """@setAll(Infinity, { include: /^a\./ })"""
        Ds.setAll ("a.", Double.NegativeInfinity) |> should equal """@setAll(-Infinity, { include: /^a\./ })"""

    [<Fact>]
    let ``Ds.nonce refuses an empty nonce, because Datastar stops at load when the nonce is empty`` () =
        let raised = Assert.Throws<ArgumentException>(fun () -> Ds.nonce "" |> ignore)
        raised.Message |> should haveSubstring "NonceRequired"
        Assert.Throws<ArgumentException>(fun () -> Ds.nonce "   " |> ignore) |> ignore

    [<Fact>]
    let ``Ds.bindEvent writes the first event and the others after it`` () =
        renderAttr (Ds.bindEvent (SignalPath.sp "val", "input"))
        |> should equal """<div data-bind:val__event.input></div>"""
        renderAttr (Ds.bindEvent (SignalPath.sp "val", "input", [ "change" ]))
        |> should equal """<div data-bind:val__event.input.change></div>"""

    [<Fact>]
    let ``Ds.bindEvent and Ds.bindProp refuse an empty name, and say what to write`` () =
        let noEvent = Assert.Throws<ArgumentException>(fun () -> Ds.bindEvent (SignalPath.sp "val", " ") |> ignore)
        noEvent.Message |> should startWith "Ds.bindEvent needs an event name, such as \"input\". With no events Datastar never syncs the signal."
        let noProp = Assert.Throws<ArgumentException>(fun () -> Ds.bindProp (SignalPath.sp "val", "") |> ignore)
        noProp.Message |> should startWith "Ds.bindProp needs the name of an element property, such as \"checked\". An empty name makes Datastar throw BindPropNameMissing."
        let emptyEvent = Assert.Throws<ArgumentException>(fun () -> Ds.bindProp (SignalPath.sp "val", "checked", [ "input"; "" ]) |> ignore)
        emptyEvent.Message |> should startWith "Ds.bindProp was given an empty event name. Datastar listens for each name that follows __event, so an empty one listens for nothing."

    [<Fact>]
    let ``Ds.bindProp with an empty list of events writes no event modifier, so Datastar uses the default events`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "checked", []))
        |> should equal """<div data-bind:val__prop.checked></div>"""

    // The escaping has a fast path for text that needs none. This compares it with the plain chain of replacements it stands in for.
    [<Fact>]
    let ``Escaping gives the same result as the plain chain of replacements, for many strings`` () =
        let reference (value: string) =
            value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r")
                 .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
        let alphabet = [| 'a'; 'Z'; '0'; ' '; '/'; '?'; '='; '\\'; '\''; '\n'; '\r'; '&'; '<'; '>'; '"'; '$'; '@'; '(' ; ')'; ';'; '\u2028'; 'é' |]
        Dst.run "Escaping gives the same result" (fun generator ->
            for _ in 1 .. 120 do
                let text = String(Array.init (Dst.intBetween 0 24 generator) (fun _ -> alphabet.[Dst.intBelow alphabet.Length generator]))
                Expr.toString (Expr.string text) |> should equal ("'" + reference text + "'")
                Ds.get text |> should equal ("@get('" + reference text + "')"))

    [<Fact>]
    let ``Text that goes through the escaping is read back as the same text by a browser and a JavaScript parser`` () =
        let alphabet = [| 'a'; 'Z'; '0'; ' '; '\t'; '/'; '='; '\\'; '\''; '\n'; '\r'; '&'; '<'; '>'; '"'; '$'; '@'; ';'; '\u2028'; 'é'; '#'; '%'; '`' |]
        Dst.run "Text that goes through the escaping" (fun generator ->
            for _ in 1 .. 200 do
                let text = String(Array.init (Dst.intBetween 0 30 generator) (fun _ -> alphabet.[Dst.intBelow alphabet.Length generator]))
                let rendered = renderAttr (Ds.text (Expr.string text))
                // <div data-text="VALUE"></div>
                let value = rendered.Substring("<div data-text=\"".Length, rendered.Length - "<div data-text=\"".Length - "\"></div>".Length)
                value |> should not' (contain '"')
                value |> should not' (contain '<')
                value |> should not' (contain '>')
                readBack value |> should equal text)

    [<Fact>]
    let ``A URL goes through the same escaping, so a browser reads it back as written`` () =
        let alphabet = [| 'a'; '/'; '?'; '='; '&'; '\''; '"'; '<'; ')'; '\\'; ' ' |]
        Dst.run "A URL goes through the same escaping" (fun generator ->
            for _ in 1 .. 80 do
                let url = String(Array.init (Dst.intBetween 1 20 generator) (fun _ -> alphabet.[Dst.intBelow alphabet.Length generator]))
                let action = Ds.get url
                // @get('URL')
                let literal = action.Substring("@get(".Length, action.Length - "@get(".Length - ")".Length)
                readBack literal |> should equal url)

    // Signals filters are regular expressions inside a JavaScript object literal, inside an attribute

    [<Fact>]
    let ``A slash in a signals filter is escaped, so the regular expression literal stays valid`` () =
        SignalsFilter.Serialize (SignalsFilter.Include "a/b") |> should equal @"{ include: /a\/b/ }"
        SignalsFilter.Serialize (SignalsFilter.Exclude "a/b") |> should equal @"{ exclude: /a\/b/ }"
        // Already escaped, so it stays as it is
        SignalsFilter.Serialize (SignalsFilter.Include @"a\/b") |> should equal @"{ include: /a\/b/ }"
        SignalsFilter.Serialize (SignalsFilter.Prefix "a/") |> should equal @"{ include: /^a\// }"

    [<Fact>]
    let ``A line break in a signals filter is written as an escape, because a literal cannot contain one`` () =
        SignalsFilter.Serialize (SignalsFilter.Include "a\nb\r") |> should equal @"{ include: /a\nb\r/ }"

    // U+2028 and U+2029 are line terminators. A JavaScript string ends at one, and a JavaScript regular expression
    // literal cannot contain one at all, so both have to be written as an escape.
    [<Fact>]
    let ``A line separator in a signals filter is written as an escape, because it ends a JavaScript string`` () =
        SignalsFilter.Serialize (SignalsFilter.Include "a\u2028b") |> should equal @"{ include: /a\u2028b/ }"
        SignalsFilter.Serialize (SignalsFilter.Include "a\u2029b") |> should equal @"{ include: /a\u2029b/ }"

    [<Fact>]
    let ``A line separator after a backslash in a signals filter is written as an escape too`` () =
        // A backslash and the character after it are kept together, so an escape that is already there survives. The
        // line separators have to be caught on that path as well: falling through to the catch-all left one raw, and
        // a regular expression literal with a line terminator in it is a SyntaxError, so the whole expression failed.
        // The backslash is kept and the separator becomes u2028 after it, which is a valid escape.
        SignalsFilter.Serialize (SignalsFilter.Include "a\\\u2028b") |> should equal @"{ include: /a\u2028b/ }"
        SignalsFilter.Serialize (SignalsFilter.Include "a\\\u2029b") |> should equal @"{ include: /a\u2029b/ }"

    [<Fact>]
    let ``A line separator in a signals filter never reaches the page as itself`` () =
        // A browser decodes the attribute and Datastar evaluates it, so an unescaped separator would end the expression
        let prefix = "<div data-on-signal-patch-filter=\""
        let suffix = "\"></div>"
        for separator in [ '\u2028'; '\u2029' ] do
            let rendered = renderAttr (Ds.onSignalPatchFilter (SignalsFilter.Include (string separator)))
            let from = rendered.IndexOf prefix + prefix.Length
            let value = HttpUtility.HtmlDecode(rendered.Substring(from, rendered.Length - from - suffix.Length))
            if value.Contains(string separator) then failwith "the separator reached the page as itself"
            if not (value.Contains "\\u") then failwith $"no escape was written for the separator: {value}"

    [<Fact>]
    let ``A NUL character in a signals filter is written as an escape`` () =
        SignalsFilter.Serialize (SignalsFilter.Include "a\000b") |> should equal @"{ include: /a\u0000b/ }"

    [<Fact>]
    let ``A signals filter cannot break out of its attribute`` () =
        renderAttr (Ds.onSignalPatchFilter (SignalsFilter.Include "a\"b"))
        |> should equal """<div data-on-signal-patch-filter="{ include: /a&quot;b/ }"></div>"""
        renderAttr (Ds.jsonSignalsOptions (SignalsFilter.Exclude "a\"b<"))
        |> should equal """<div data-json-signals="{ exclude: /a&quot;b&lt;/ }"></div>"""

    [<Fact>]
    let ``A signals filter in setAll and toggleAll is encoded once`` () =
        Ds.setAllFiltered (1, SignalsFilter.Include "a\"b") |> should equal "@setAll(1, { include: /a&quot;b/ })"
        Ds.toggleAllFiltered (SignalsFilter.Include "a\"b") |> should equal "@toggleAll({ include: /a&quot;b/ })"

    [<Fact>]
    let ``Ds.signals writes JSON that cannot break out of its attribute`` () =
        renderAttr (Ds.signals {| a = "\"x\" <b> &" |})
        |> should equal """<div data-signals="{&quot;a&quot;:&quot;\u0022x\u0022 \u003Cb\u003E \u0026&quot;}"></div>"""

    // Falco.Markup does not escape attribute names. A name that comes from data must not be able to end the name and add attributes of its own.

    let private refusalOf (build: unit -> Falco.Markup.XmlAttribute) =
        (Assert.Throws<ArgumentException>(fun () -> build () |> ignore)).Message

    [<Fact>]
    let ``A class name cannot end the attribute name and add attributes of its own`` () =
        refusalOf (fun () -> Ds.class' ("x\" onmouseover=\"alert(1)\" y=\"", "$a"))
        |> should equal """The class name 'x" onmouseover="alert(1)" y="' cannot be used in a data- attribute name, because it contains '"'. HTML ends an attribute name there, and what follows would become attributes of their own. Use letters, digits, '-', '.' and ':'."""

    [<Fact>]
    let ``Every name that goes into an attribute name is checked`` () =
        refusalOf (fun () -> Ds.onEvent ("a b", "$x")) |> should haveSubstring "The event name 'a b' cannot be used in a data- attribute name, because it contains whitespace"
        refusalOf (fun () -> Ds.attr' ("a>b", "$x")) |> should haveSubstring "The attribute name 'a>b' cannot be used"
        refusalOf (fun () -> Ds.style ("a=b", "$x")) |> should haveSubstring "The style property 'a=b' cannot be used"
        refusalOf (fun () -> Elem.create "x-y" [ Rocket.propString ("a\"b", "x") ] [] |> ignore; Rocket.propString ("a\"b", "x"))
        |> should haveSubstring "The prop name 'a\"b' cannot be used"

    [<Fact>]
    let ``A modifier value cannot end the attribute name either`` () =
        refusalOf (fun () -> Ds.bindEvent (SignalPath.sp "val", "input\" onclick=\"x"))
        |> should haveSubstring "The modifier value 'input\" onclick=\"x' cannot be used"
        refusalOf (fun () -> Ds.bindProp (SignalPath.sp "val", "a b"))
        |> should haveSubstring "The modifier value 'a b' cannot be used"

    [<Fact>]
    let ``Whitespace and control characters that are not ASCII are refused too`` () =
        for character in [ '\u00A0'; '\u2003'; '\u3000'; '\u0085'; '\u007F'; '\u2028'; '\t'; '\n'; '\f' ] do
            refusalOf (fun () -> Ds.class' ($"a{character}b", "$a"))
            |> should haveSubstring "because it contains whitespace or a control character"
        for character in [ '"'; '\''; '`'; '<'; '>'; '/'; '=' ] do
            refusalOf (fun () -> Ds.class' ($"a{character}b", "$a"))
            |> should haveSubstring $"because it contains '{character}'"

    [<Fact>]
    let ``An empty name is refused, because Datastar would read an attribute with no name`` () =
        refusalOf (fun () -> Ds.onEvent ("", "$a"))
        |> should equal "The event name '' cannot be used in a data- attribute name, because it is empty. Write a name."

    [<Fact>]
    let ``A double underscore is refused, because Datastar reads it as a modifier`` () =
        refusalOf (fun () -> Ds.class' ("card__title", "$a"))
        |> should equal """The class name 'card__title' cannot be used in a data- attribute name, because it contains '__'. Datastar reads '__' in an attribute name as the start of a modifier, so it would read 'card__title' as 'card' with the modifier 'title'. Choose a name without '__'. To toggle a class like this, write the object form yourself, for example Attr.create "data-class" "{'card__title': $isActive}"."""
        refusalOf (fun () -> Ds.attr' ("a__b", "$a")) |> should haveSubstring "Choose a name without '__'."

    [<Fact>]
    let ``A name that ends with an underscore is refused when a modifier follows it`` () =
        refusalOf (fun () -> Ds.signal (SignalPath.sp "a_", 1, ifMissing = true))
        |> should equal "The name 'a_' cannot be used in a data- attribute name, because it ends with an underscore next to a modifier. Datastar would read the underscore together with the two that start the modifier, and lose part of the name. Remove the trailing underscore."
        // Without a modifier there is nothing for it to run into
        renderAttr (Ds.signal (SignalPath.sp "a_", 1)) |> should equal """<div data-signals:a_="1"></div>"""

    [<Fact>]
    let ``Names that HTML and Datastar read as written are accepted`` () =
        renderAttr (Ds.class' ("is-active", "$a")) |> should equal """<div data-class:is-active="$a"></div>"""
        renderAttr (Ds.attr' ("aria-label", "$a")) |> should equal """<div data-attr:aria-label="$a"></div>"""
        renderAttr (Ds.attr' ("xlink:href", "$a")) |> should equal """<div data-attr:xlink:href="$a"></div>"""
        renderAttr (Ds.onEvent ("my-event", "$a")) |> should equal """<div data-on:my-event="$a"></div>"""
        renderAttr (Ds.style ("background-color", "'red'")) |> should equal """<div data-style:background-color="'red'"></div>"""

    [<Fact>]
    let ``The Safari back-button fix listens for pageshow on the window with the modifier syntax of Datastar 1.0.4`` () =
        // Datastar reads modifiers after '__'. A dot after the event name would make the event be called pageshow.window
        renderAttr Ds.safariStreamingFix
        |> should equal """<div data-on:pageshow__window="evt?.persisted && window.location.reload()"></div>"""

    [<Fact>]
    let ``Rocket.forEach only takes names that are JavaScript identifiers`` () =
        let refused = Assert.Throws<ArgumentException>(fun () -> Rocket.forEach (Expr.read (Signal.rocket<string list> "items"), (fun _ _ -> []), itemName = "x in y; alert(1)//") |> ignore)
        refused.Message |> should startWith "Rocket.forEach needs an item name that is a JavaScript identifier, such as \"entry\", but it is 'x in y; alert(1)//'."
        refused.ParamName |> should equal "itemName"
        let refusedIndex = Assert.Throws<ArgumentException>(fun () -> Rocket.forEach (Expr.read (Signal.rocket<string list> "items"), (fun _ _ -> []), indexName = "a b") |> ignore)
        refusedIndex.Message |> should startWith "Rocket.forEach needs an index name that is a JavaScript identifier, such as \"n\", but it is 'a b'."
        Rocket.forEach (Expr.read (Signal.rocket<string list> "items"), (fun _ _ -> []), itemName = "$row", indexName = "_n")
        |> renderNode |> should equal """<template data-for="$row, _n in $$items"></template>"""

    // Found by the simulation in DstHtmlTests. An HTML parser changes a carriage return in an attribute value into a line feed,
    // and a NUL character into U+FFFD, so a value that had one arrived in the browser as different text.

    [<Fact>]
    let ``A carriage return in an attribute value is written as a character reference, which a parser keeps`` () =
        renderAttr (Rocket.propString ("label", "a\rb\r\nc")) |> should equal """<div label="a&#13;b&#13;
c"></div>"""
        renderAttr (Ds.nonce "a\rb") |> should equal """<div data-nonce="a&#13;b"></div>"""

    [<Fact>]
    let ``A NUL character is written as U+FFFD in an attribute value, and as an escape in a string literal`` () =
        renderAttr (Rocket.propString ("label", "a\000b")) |> should equal "<div label=\"a\uFFFDb\"></div>"
        Expr.toString (Expr.string "a\000b") |> should equal @"'a\u0000b'"
        readJsLiteral (Expr.toString (Expr.string "a\000b")) |> should equal "a\000b"
        SignalsFilter.Serialize (SignalsFilter.Include "a\000b") |> should equal @"{ include: /a\u0000b/ }"

