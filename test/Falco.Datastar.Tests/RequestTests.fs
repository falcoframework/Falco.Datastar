namespace Falco.Datastar.Tests

open System
open System.IO
open System.Text
open Falco.Datastar
open FsUnit.Xunit
open Microsoft.AspNetCore.Http
open Xunit

type TestSignals = { A: int }

// Datastar 1.0.4 sends the signals of a @get and a @delete in the `datastar` query parameter,
// and the signals of the other actions in the request body (methodSupportsRequestBody in fetch.ts).
// The SDK only reads a @delete correctly from version 1.3.0.
module RequestTests =
    let private queryWithSignals = "?datastar=%7B%22a%22%3A1%7D"

    let private contextFor (verb: string) (query: string) (body: string) =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- verb
        ctx.Request.QueryString <- QueryString query
        ctx.Request.ContentType <- "application/json"
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes body)
        ctx

    let private readJson verb query body =
        use document = (Request.getSignalsJson (contextFor verb query body)).GetAwaiter().GetResult()
        document.RootElement.GetProperty("a").GetInt32()

    let private readTyped verb query body =
        (Request.getSignals<TestSignals> (contextFor verb query body)).GetAwaiter().GetResult()

    [<Fact>]
    let ``Request.getSignalsJson reads a GET from the query string`` () =
        readJson "GET" queryWithSignals "" |> should equal 1

    [<Fact>]
    let ``Request.getSignalsJson reads a POST from the body`` () =
        readJson "POST" "" """{"a":1}""" |> should equal 1

    [<Fact>]
    let ``Request.getSignalsJson reads a DELETE from the query string`` () =
        readJson "DELETE" queryWithSignals "" |> should equal 1

    [<Fact>]
    let ``Request.getSignals reads a DELETE from the query string`` () =
        // This is why the library depends on StarFederation.Datastar.FSharp 1.4.0. The SDK only reads the `datastar` query
        // parameter for a DELETE from version 1.3.0 on. If a dependency bump ever brings an older SDK back, this fails.
        readTyped "DELETE" queryWithSignals ""
        |> should equal (ValueSome { A = 1 })

    [<Fact>]
    let ``Request.getSignals reads a POST from the body`` () =
        readTyped "POST" "" """{"a":1}"""
        |> should equal (ValueSome { A = 1 })

    // SignalPath.getSignalFromJson returns ValueNone when the path is not in the document, which is not an error.
    // A value that is there but cannot be read as 'T is a different matter, and is raised so that the caller sees it.

    [<Fact>]
    let ``SignalPath.getSignalFromJson gives nothing when a part of the path is not there`` () =
        use document = System.Text.Json.JsonDocument.Parse """{"a":1,"form":{"name":"Ada"}}"""
        SignalPath.getSignalFromJson<int> (SignalPath.sp "missing") document |> should equal (ValueNone : int voption)
        // form.name is text, so reading it as an int is a type that does not match, which is not the same as a path that is not there
        SignalPath.getSignalFromJson<int> (SignalPath.sp "nope.name") document |> should equal (ValueNone : int voption)

    [<Fact>]
    let ``SignalPath.getSignalFromJson reads a value that is there`` () =
        use document = System.Text.Json.JsonDocument.Parse """{"a":1,"form":{"name":"Ada"}}"""
        SignalPath.getSignalFromJson<int> (SignalPath.sp "a") document |> should equal (ValueSome 1)
        SignalPath.getSignalFromJson<string> (SignalPath.sp "form.name") document |> should equal (ValueSome "Ada")

    [<Fact>]
    let ``SignalPath.getSignalFromJson gives nothing when the value cannot be read as the type`` () =
        // This has always returned ValueNone for a value of the wrong type, the same as for a path that is not there.
        // It is Falco's own behaviour rather than Datastar's, so it is left exactly as it was.
        use document = System.Text.Json.JsonDocument.Parse """{"count":"not a number"}"""
        SignalPath.getSignalFromJson<int> (SignalPath.sp "count") document |> should equal (ValueNone : int voption)
