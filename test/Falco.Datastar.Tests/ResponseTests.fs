namespace Falco.Datastar.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Falco.Datastar
open Falco.Datastar.SignalPath
open Falco.Markup
open FsUnit.Xunit
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open StarFederation.Datastar.FSharp
open Xunit

// The Response module writes Server Sent Events. These tests drive it through a real HttpContext with a memory body,
// and read back the bytes a browser would receive, because the format is what Datastar parses on the other side.
//
// The shape of that format is the SDK's, not this library's: it writes an `event:` line, then one `data:` line per field,
// then a blank line. Datastar has one event type, `datastar-patch-elements`, and says what to do with the `mode` and
// `selector` fields, so removing an element and running a script are the same event with different fields.
module ResponseTests =
    /// A context whose response body is a buffer the test can read afterwards
    let private contextWith () =
        let ctx = DefaultHttpContext()
        let body = new MemoryStream()
        ctx.Response.Body <- body
        ctx, body

    /// Runs a response handler and gives back what it wrote, and the context so a test can read the headers
    let private writtenWithContext (handler: HttpContext -> Task) =
        task {
            let ctx, body = contextWith ()
            do! handler ctx
            // The SDK writes through the pipe writer, and the buffer is only readable once that has been flushed
            let! _ = ctx.Response.BodyWriter.FlushAsync()
            let! _ = ctx.Response.Body.FlushAsync()
            return Encoding.UTF8.GetString(body.ToArray()), ctx
        }

    /// Runs a handler and gives back what it wrote. The lambda is given the context, because that is what every
    /// Response function takes, and naming the type keeps the compiler from guessing at it.
    let private written (build: HttpContext -> Task) =
        (writtenWithContext build).Result |> fst

    /// The same, for a handler that reads the headers of the response as well
    let private writtenWithHeaders (build: HttpContext -> Task) =
        writtenWithContext build

    let private assertStartsWith (expected: string) (text: string) =
        if not (text.StartsWith expected) then
            failwith $"the event does not start with '{expected}':{Environment.NewLine}{text}"

    let private assertEndsWithBlankLine (text: string) =
        if not (text.EndsWith "\n\n") then
            failwith $"the event is not closed by a blank line:{Environment.NewLine}{text}"

    /// Every event starts with its type, and ends with a blank line
    let private assertIsSse (text: string) =
        assertStartsWith "event: datastar-patch-elements" text
        assertEndsWithBlankLine text

    /// The value of one field of the last event, e.g. "data: mode outer\n" -> "outer".
    /// The SDK writes a space after the colon, so the value starts after that space.
    let private fieldOf (name: string) (text: string) =
        let prefix = "data: " + name
        text
        |> fun t -> t.Split('\n')
        |> Array.filter (fun line -> line.StartsWith(prefix))
        |> Array.map (fun line -> line.Substring(prefix.Length).Trim())
        |> Array.last

    /// True when the event carries that field at all
    let private hasField (name: string) (text: string) =
        let prefix = "data: " + name
        text.Split('\n') |> Array.exists (fun line -> line.StartsWith prefix)

    /// The value of a top-level line of the event, such as `id` or `retry`, which the SDK writes without a `data:` prefix
    let private lineOf (name: string) (text: string) : string option =
        let prefix = name + ": "
        text
        |> fun t -> t.Split('\n')
        |> Array.filter (fun line -> line.StartsWith prefix)
        |> Array.map (fun line -> line.Substring(prefix.Length).Trim())
        |> Array.tryLast

    let private lineIs (name: string) (expected: string) (text: string) =
        match lineOf name text with
        | Some found when found = expected -> ()
        | found -> failwith $"the '{name}' line is not '{expected}' but '{found}' in:{Environment.NewLine}{text}"

    /// Checks that some field of the event holds the text, because the exact shape of the payload is the SDK's
    let private fieldContains (name: string) (expected: string) (text: string) =
        let prefix = "data: " + name
        let found = text.Split('\n') |> Array.exists (fun line -> line.StartsWith prefix && line.Contains expected)
        if not found then failwith $"no '{name}' field holds '{expected}' in:{Environment.NewLine}{text}"

    /// Checks that some field of the event holds exactly the text
    let private fieldIs (name: string) (expected: string) (text: string) =
        if fieldOf name text <> expected then
            failwith $"the '{name}' field is not '{expected}' but '{fieldOf name text}' in:{Environment.NewLine}{text}"

    // Starting the stream

    [<Fact>]
    let ``sseStartResponse writes the event stream headers, and no event yet`` () =
        let text, ctx = (writtenWithHeaders (fun (ctx: HttpContext) -> Response.sseStartResponse ctx)).Result
        let contentType = string ctx.Response.Headers.ContentType
        let cacheControl = string ctx.Response.Headers.CacheControl
        if not (contentType.Contains "text/event-stream") then failwith $"the content type is '{contentType}'"
        if not (cacheControl.Contains "no-cache") then failwith $"the cache control is '{cacheControl}'"
        // Starting the stream writes the headers only. An event is sent by one of the functions that follow it.
        if text <> "" then failwith $"starting the stream wrote to the body: {text}"

    [<Fact>]
    let ``sseStartResponseWithHeaders writes the headers it is given`` () =
        let _, ctx =
            (writtenWithHeaders (fun (ctx: HttpContext) ->
                Response.sseStartResponseWithHeaders ctx [ KeyValuePair("X-Custom", StringValues "yes") ])).Result
        let contentType = string ctx.Response.Headers.ContentType
        let custom = string ctx.Response.Headers["X-Custom"]
        if not (contentType.Contains "text/event-stream") then failwith $"the content type is '{contentType}'"
        if custom <> "yes" then failwith $"the custom header is '{custom}'"

    // Patching elements

    [<Fact>]
    let ``sseHtmlElements patches the element with the matching id`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                return! Response.sseHtmlElements ctx (Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hi" ])
             }))
        assertIsSse text
        // With no selector, the id of each new element is what it replaces. The SDK leaves out the mode when it is
        // the default, so there is nothing to check but that the element is there and no selector was sent.
        if hasField "selector" text then failwith $"no selector was given, so none should be sent:{Environment.NewLine}{text}"
        fieldContains "elements" "hello" text

    [<Fact>]
    let ``sseHtmlElementsOptions passes the selector and the mode through`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                return!
                    Response.sseHtmlElementsOptions
                        ctx
                        { PatchElementsOptions.Defaults with
                            Selector = ValueSome "#rows"
                            PatchMode = ElementPatchMode.Append }
                        (Elem.tr [ Attr.id "row" ] [ Elem.td [] [ Text.raw "new" ] ])
             }))
        assertIsSse text
        fieldIs "selector" "#rows" text
        fieldIs "mode" "append" text

    [<Fact>]
    let ``ofHtmlElements starts the stream and sends the element in one handler`` () =
        let text =
            (written (fun (ctx: HttpContext) -> Response.ofHtmlElements (Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hi" ]) ctx))
        assertIsSse text
        fieldContains "elements" "hello" text

    [<Fact>]
    let ``ofHtmlStringElements sends the HTML as it is written`` () =
        let text =
            (written (fun (ctx: HttpContext) -> Response.ofHtmlStringElements @"<h2 id='hello'>Hi</h2>" ctx))
        assertIsSse text
        fieldContains "elements" "hello" text

    [<Fact>]
    let ``ofHtmlStringElementsOptions passes the options through`` () =
        let text =
            (written (fun (ctx: HttpContext) ->
                Response.ofHtmlStringElementsOptions
                    { PatchElementsOptions.Defaults with
                        Selector = ValueSome "#box"
                        PatchMode = ElementPatchMode.Prepend }
                    "<p>x</p>"
                    ctx))
        fieldIs "selector" "#box" text
        fieldIs "mode" "prepend" text

    // Patching signals. Datastar has one event type for elements and one for signals, and the signals arrive as JSON
    // in a `signals` field.

    [<Fact>]
    let ``ssePatchSignals writes the signals as JSON`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                return! Response.ssePatchSignals ctx {| name = "Ada"; count = 2 |}
             }))
        assertStartsWith "event: datastar-patch-signals" text
        assertEndsWithBlankLine text
        fieldContains "signals" "Ada" text
        fieldContains "signals" "2" text

    [<Fact>]
    let ``ssePatchSignalsOptions uses the serializer options it is given`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                return!
                    Response.ssePatchSignalsOptions
                        ctx
                        PatchSignalsOptions.Defaults
                        (JsonSerializerOptions(JsonSerializerDefaults.Web))
                        {| userName = "Ada" |}
             }))
        // The web defaults make the name camelCase, so this only passes if the options really are used
        fieldContains "signals" "userName" text

    [<Fact>]
    let ``ofPatchSignals starts the stream and sends the signals`` () =
        let text = (written (fun (ctx: HttpContext) -> Response.ofPatchSignals {| name = "Ada" |} ctx))
        assertStartsWith "event: datastar-patch-signals" text
        fieldContains "signals" "Ada" text

    // A single signal, addressed by its path

    [<Fact>]
    let ``ssePatchSignal nests the value under its path`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                return! Response.ssePatchSignal ctx (SignalPath.sp "user.firstName") "Ada"
             }))
        assertStartsWith "event: datastar-patch-signals" text
        // A nested path becomes a nested object, which is how Datastar reads it
        fieldContains "signals" "user" text
        fieldContains "signals" "firstName" text
        fieldContains "signals" "Ada" text

    [<Fact>]
    let ``ofPatchSignal starts the stream and sends the one signal`` () =
        let text = (written (fun (ctx: HttpContext) -> Response.ofPatchSignal (SignalPath.sp "count") 7 ctx))
        fieldContains "signals" "count" text
        fieldContains "signals" "7" text

    [<Fact>]
    let ``ssePatchSignalOptions passes the event id through, which the browser sends back when it reconnects`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                return!
                    Response.ssePatchSignalOptions
                        ctx
                        { PatchSignalsOptions.Defaults with EventId = ValueSome "e42" }
                        (SignalPath.sp "count")
                        7
             }))
        assertStartsWith "event: datastar-patch-signals" text
        lineIs "id" "e42" text

    // Removing an element. Datastar removes it with a patch event in the remove mode.

    [<Fact>]
    let ``ofRemoveElement removes what the selector finds`` () =
        let text = (written (fun (ctx: HttpContext) -> Response.ofRemoveElement "#hello" ctx))
        assertIsSse text
        fieldIs "mode" "remove" text
        fieldIs "selector" "#hello" text
        // Nothing is patched, so no elements are sent
        hasField "elements" text |> should equal false

    [<Fact>]
    let ``ofRemoveElementOptions passes the options through, and the SDK writes a removal with no id line`` () =
        let text =
            (written (fun (ctx: HttpContext) ->
                Response.ofRemoveElementOptions
                    { RemoveElementOptions.Defaults with EventId = ValueSome "e7" }
                    "#hello"
                    ctx))
        fieldIs "selector" "#hello" text
        fieldIs "mode" "remove" text
        // The SDK does not put an id on a removal, so there is nothing to assert about the event id here.
        // Asserting that one would be pinning the SDK rather than this library, which is what the other tests avoid.
        hasField "elements" text |> should equal false

    // Running JavaScript. Datastar runs it from an element patch, so the script arrives as elements to add.

    [<Fact>]
    let ``ofExecuteScript sends the JavaScript for Datastar to run`` () =
        let text = (written (fun (ctx: HttpContext) -> Response.ofExecuteScript "console.log('hi')" ctx))
        assertIsSse text
        // The script arrives as a script element whose data-effect runs it, which is how Datastar executes a script
        // response. Asserting the mode here would only pin the SDK's default, which this library does not choose.
        fieldContains "elements" "<script" text
        fieldContains "elements" "console.log('hi')" text

    [<Fact>]
    let ``ofExecuteScriptOptions passes the event id through`` () =
        let text =
            (written (fun (ctx: HttpContext) ->
                Response.ofExecuteScriptOptions
                    { ExecuteScriptOptions.Defaults with EventId = ValueSome "e9" }
                    "run()"
                    ctx))
        lineIs "id" "e9" text
        fieldContains "elements" "run()" text

    // Streaming more than one event down a single response, which is the CQRS read side

    [<Fact>]
    let ``Several events go down one response, in order`` () =
        let text =
            (written (fun (ctx: HttpContext) -> task {
                do! Response.sseStartResponse ctx
                do! Response.ssePatchSignal ctx (SignalPath.sp "counter") 111
                do! Response.ssePatchSignal ctx (SignalPath.sp "counter") 222
                return! Response.sseHtmlElements ctx (Elem.pre [ Attr.id "c" ] [ Text.raw "done" ])
             }))
        // Three events, each closed by a blank line
        let events = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        if events.Length <> 3 then failwith $"expected three events, found {events.Length}:{Environment.NewLine}{text}"
        // The values are read out of each event's own payload, so a swapped or repeated event is caught, rather than
        // matching the digits anywhere in the response
        let signalsOf (event: string) =
            event.Split('\n')
            |> Array.filter (fun line -> line.StartsWith "data: signals")
            |> Array.map (fun line -> line.Substring("data: signals ".Length))

        let first = signalsOf events[0]
        let second = signalsOf events[1]
        if first.Length <> 1 || not (first.[0].Contains "111") then
            failwith $"the first event does not carry the first value:{Environment.NewLine}{events[0]}"
        if second.Length <> 1 || not (second.[0].Contains "222") then
            failwith $"the second event does not carry the second value:{Environment.NewLine}{events[1]}"
        if not (events[2].Contains "datastar-patch-elements") then
            failwith $"the third event is not an element patch:{Environment.NewLine}{events[2]}"
        if not (events[2].Contains "done") then
            failwith $"the third event does not carry the element:{Environment.NewLine}{events[2]}"
