namespace Falco.Datastar.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open Falco.Datastar
open FsUnit.Xunit
open Microsoft.AspNetCore.Http
open Xunit

module RocketManifestTests =
    // The body that Datastar 1.0.4's publishRocketManifests posted from a real browser, for two components
    let private posted = """{"version":1,"generatedAt":"2026-09-21T17:42:04.838Z","components":[{"tag":"demo-card","props":[{"name":"title","attribute":"title","type":"string","default":"Card","required":false,"docs":{"description":"The card title.","label":"Title","control":"text","placeholder":"Type a title"}},{"name":"maxCount","attribute":"max-count","type":"number","default":3,"required":false},{"name":"open","attribute":"open","type":"boolean","default":false,"required":false},{"name":"due","attribute":"due","type":"date","default":"2026-09-21T17:42:04.539Z","required":false},{"name":"settings","attribute":"settings","type":"json","default":{"a":1},"required":false},{"name":"payload","attribute":"payload","type":"binary","default":{},"required":false},{"name":"tags","attribute":"tags","type":"array","default":[],"required":false},{"name":"profile","attribute":"profile","type":"object","default":{"name":"Anonymous","age":0},"required":false},{"name":"theme","attribute":"theme","type":"oneOf","default":"system","required":false,"values":["light","dark","system"]}],"slots":[{"name":"default","description":"Body."},{"name":"footer"}],"events":[{"kind":"custom-event","name":"close","bubbles":true,"composed":true,"description":"Dismiss."},{"kind":"event","name":"ready"}]},{"tag":"demo-plain","props":[],"slots":[],"events":[]}]}"""

    let private parsed () =
        match RocketManifest.parse posted with
        | Ok document -> document
        | Error error -> failwith error.Message

    let private component' tag =
        (parsed ()).Components |> List.find (fun c -> c.Tag = tag)

    let private prop tag name =
        (component' tag).Props |> List.find (fun p -> p.Name = name)

    [<Fact>]
    let ``RocketManifest.parse reads the version, the time and the components in order`` () =
        let document = parsed ()
        document.Version |> should equal 1
        // The reader parses the timestamp with the invariant culture, so the expected value is parsed the same way.
        // Parsing it with the current culture would make this test fail on a machine whose culture reads a date
        // differently, which is a bug in the test rather than in the reader.
        document.GeneratedAt
        |> should equal (DateTimeOffset.Parse("2026-09-21T17:42:04.838Z", Globalization.CultureInfo.InvariantCulture))
        document.Components |> List.map (fun c -> c.Tag) |> should equal [ "demo-card"; "demo-plain" ]

    [<Fact>]
    let ``RocketManifest.parse reads a component without props, slots or events`` () =
        let plain = component' "demo-plain"
        plain.Props |> should be Empty
        plain.Slots |> should be Empty
        plain.Events |> should be Empty

    [<Fact>]
    let ``RocketManifest.parse gives each prop the attribute name Rocket derived`` () =
        (component' "demo-card").Props
        |> List.map (fun p -> p.Name, p.Attribute)
        |> should equal [ "title", "title"; "maxCount", "max-count"; "open", "open"; "due", "due"; "settings", "settings"
                          "payload", "payload"; "tags", "tags"; "profile", "profile"; "theme", "theme" ]

    [<Fact>]
    let ``RocketManifest.parse maps the codec name of each prop to a case`` () =
        (component' "demo-card").Props
        |> List.map (fun p -> p.Type)
        |> should equal [ RocketPropType.String; RocketPropType.Number; RocketPropType.Boolean; RocketPropType.Date; RocketPropType.Json
                          RocketPropType.Binary; RocketPropType.Array; RocketPropType.Object; RocketPropType.OneOf ]

    [<Fact>]
    let ``RocketManifest.parse keeps a codec name it does not know instead of failing`` () =
        let json = """{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z","components":[{"tag":"x-y","props":[{"name":"p","attribute":"p","type":"hologram","default":null,"required":false}],"slots":[],"events":[]}]}"""
        match RocketManifest.parse json with
        | Ok document -> document.Components.Head.Props.Head.Type |> should equal (RocketPropType.Other "hologram")
        | Error error -> failwith error.Message

    [<Fact>]
    let ``RocketManifest.parse keeps the default of a prop as JSON`` () =
        (prop "demo-card" "title").Default.GetString() |> should equal "Card"
        (prop "demo-card" "maxCount").Default.GetInt32() |> should equal 3
        (prop "demo-card" "open").Default.GetBoolean() |> should equal false
        (prop "demo-card" "settings").Default.GetProperty("a").GetInt32() |> should equal 1
        (prop "demo-card" "profile").Default.GetProperty("name").GetString() |> should equal "Anonymous"

    [<Fact>]
    let ``RocketManifest.parse reads the allowed values of a oneOf prop`` () =
        (prop "demo-card" "theme").Values
        |> ValueOption.map (List.map (fun v -> v.GetString()))
        |> should equal (ValueSome [ "light"; "dark"; "system" ])
        (prop "demo-card" "title").Values |> should equal (ValueNone : JsonElement list voption)

    [<Fact>]
    let ``RocketManifest.parse reads the documentation of a prop when the component gave it`` () =
        let docs = (prop "demo-card" "title").Docs
        docs |> ValueOption.map (fun d -> d.Label) |> should equal (ValueSome (ValueSome "Title"))
        docs |> ValueOption.map (fun d -> d.Description) |> should equal (ValueSome (ValueSome "The card title."))
        docs |> ValueOption.map (fun d -> d.Control) |> should equal (ValueSome (ValueSome "text"))
        docs |> ValueOption.map (fun d -> d.Placeholder) |> should equal (ValueSome (ValueSome "Type a title"))
        (prop "demo-card" "maxCount").Docs |> should equal (ValueNone : RocketPropDocs voption)

    [<Fact>]
    let ``RocketManifest.parse reads slots with and without a description`` () =
        (component' "demo-card").Slots
        |> List.map (fun s -> s.Name, s.Description)
        |> should equal [ "default", ValueSome "Body."; "footer", ValueNone ]

    [<Fact>]
    let ``RocketManifest.parse reads events, including the ones with no description`` () =
        let events = (component' "demo-card").Events
        events |> List.map (fun e -> e.Name) |> should equal [ "close"; "ready" ]
        events |> List.map (fun e -> e.Kind) |> should equal [ RocketEventKind.CustomEvent; RocketEventKind.Event ]
        events |> List.map (fun e -> e.Bubbles) |> should equal [ ValueSome true; ValueNone ]
        events |> List.map (fun e -> e.Composed) |> should equal [ ValueSome true; ValueNone ]
        events |> List.map (fun e -> e.Description) |> should equal [ ValueSome "Dismiss."; ValueNone ]

    [<Fact>]
    let ``RocketManifest.parse returns the version it reads and the version it got when they differ`` () =
        RocketManifest.parse """{"version":2,"generatedAt":"2026-01-01T00:00:00.000Z","components":[]}"""
        |> should equal (Error (RocketManifestError.UnsupportedVersion (2, 1)) : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``RocketManifest.parse returns an error for text that is not JSON`` () =
        match RocketManifest.parse "not json" with
        | Error (RocketManifestError.NotJson _) -> ()
        | other -> failwith $"expected NotJson, got %A{other}"

    [<Fact>]
    let ``RocketManifest.parse names the property that is missing`` () =
        RocketManifest.parse """{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z"}"""
        |> should equal (Error (RocketManifestError.Missing ("components", "the manifest")) : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``Request.getRocketManifests reads the body of a POST`` () =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- "POST"
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes posted)
        match (Request.getRocketManifests ctx).GetAwaiter().GetResult() with
        | Ok document -> document.Components |> List.length |> should equal 2
        | Error error -> failwith error.Message

    let private wrap (component':string) =
        $"""{{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z","components":[{component'}]}}"""

    [<Fact>]
    let ``RocketManifest.parse returns an error, and does not throw, when components is not a list`` () =
        RocketManifest.parse """{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z","components":5}"""
        |> should equal (Error (RocketManifestError.WrongKind ("components", "a list", "the manifest")) : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``RocketManifest.parse names the prop and the component when a prop property is missing`` () =
        RocketManifest.parse (wrap """{"tag":"my-card","props":[{"name":"count","type":"number","default":0}]}""")
        |> should equal (Error (RocketManifestError.Missing ("attribute", "the prop \"count\" of my-card")) : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``RocketManifest.parse gives the position of a prop that has no name`` () =
        RocketManifest.parse (wrap """{"tag":"my-card","props":[{"name":"a","attribute":"a","type":"string","default":""},{"attribute":"b","type":"string","default":""}]}""")
        |> should equal (Error (RocketManifestError.Missing ("name", "prop 2 of my-card")) : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``Request.getRocketManifests refuses a body that is larger than one mebibyte`` () =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- "POST"
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes (String('x', 1024 * 1024 + 1)))
        (Request.getRocketManifests ctx).GetAwaiter().GetResult()
        |> should equal (Error (RocketManifestError.TooLarge (1024 * 1024)) : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``RocketManifestError.Message says what is wrong and what to do about it`` () =
        (RocketManifestError.UnsupportedVersion (2, 1)).Message |> should haveSubstring "reads Rocket manifest version 1, but the document is version 2"
        (RocketManifestError.UnsupportedVersion (2, 1)).Message |> should haveSubstring "Update Falco.Datastar"
        (RocketManifestError.Missing ("attribute", "the prop \"count\" of my-card")).Message |> should equal "The manifest has no \"attribute\" in the prop \"count\" of my-card"
        (RocketManifestError.WrongKind ("components", "a list", "the manifest")).Message |> should equal "The \"components\" in the manifest is not a list"
        (RocketManifestError.TooLarge (1024 * 1024)).Message |> should haveSubstring "larger than 1 MiB"
        (RocketManifestError.NotJson "bad").Message |> should equal "The manifest is not valid JSON: bad"
        (RocketManifestError.ConnectionFailed "The connection was reset").Message
        |> should equal "The request body could not be read: The connection was reset. The connection probably closed before the page finished posting, so there may be nobody to answer."
        RocketManifestError.Cancelled.Message |> should equal "The request was cancelled before the whole body was read, so there may be nobody to answer."
        RocketManifestError.NotAnObject.Message |> should haveSubstring "must be a JSON object"

    let private errorOf (result:Result<RocketManifestDocument, RocketManifestError>) =
        match result with
        | Error error -> error
        | Ok _ -> failwith "expected an error"

    let private propOf (props:string) =
        match RocketManifest.parse (wrap $"""{{"tag":"a-b","props":[{props}]}}""") with
        | Ok document -> document.Components.Head.Props.Head
        | Error error -> failwith error.Message

    [<Fact>]
    let ``RocketManifest.parse reads a prop that is required`` () =
        (propOf """{"name":"p","attribute":"p","type":"string","default":"","required":true}""").Required |> should equal true

    [<Fact>]
    let ``RocketManifest.parse reads every codec name, and keeps one it does not know`` () =
        let typeOf name = (propOf $"""{{"name":"p","attribute":"p","type":"{name}","default":null}}""").Type
        typeOf "tuple" |> should equal RocketPropType.Tuple
        typeOf "js" |> should equal RocketPropType.Js
        typeOf "custom" |> should equal RocketPropType.Custom
        typeOf "binary" |> should equal RocketPropType.Binary
        typeOf "hologram" |> should equal (RocketPropType.Other "hologram")

    [<Fact>]
    let ``RocketManifest.parse keeps an event kind it does not know`` () =
        match RocketManifest.parse (wrap """{"tag":"a-b","events":[{"name":"x","kind":"weird"}]}""") with
        | Ok document -> document.Components.Head.Events.Head.Kind |> should equal (RocketEventKind.Other "weird")
        | Error error -> failwith error.Message

    [<Fact>]
    let ``RocketManifest.parse reads a prop whose codec has no default as a null default`` () =
        // A codec whose decode gives undefined for a missing attribute leaves the key out of the JSON
        (propOf """{"name":"p","attribute":"p","type":"custom"}""").Default.ValueKind |> should equal JsonValueKind.Null

    [<Fact>]
    let ``RocketManifest.parse says which entry has no name`` () =
        errorOf (RocketManifest.parse (wrap """{"tag":"a-b","slots":[{"name":"x"},{"description":"d"}]}"""))
        |> should equal (RocketManifestError.Missing ("name", "slot 2 of a-b"))
        errorOf (RocketManifest.parse (wrap """{"tag":"a-b","events":[{"kind":"event"}]}"""))
        |> should equal (RocketManifestError.Missing ("name", "event 1 of a-b"))
        errorOf (RocketManifest.parse (wrap """{"props":[]}"""))
        |> should equal (RocketManifestError.Missing ("tag", "component 1"))

    [<Fact>]
    let ``RocketManifest.parse refuses props, slots and events that are not lists, instead of reading them as empty`` () =
        errorOf (RocketManifest.parse (wrap """{"tag":"a-b","props":"oops"}"""))
        |> should equal (RocketManifestError.WrongKind ("props", "a list", "the component \"a-b\""))
        errorOf (RocketManifest.parse (wrap """{"tag":"a-b","slots":{}}"""))
        |> should equal (RocketManifestError.WrongKind ("slots", "a list", "the component \"a-b\""))
        errorOf (RocketManifest.parse (wrap """{"tag":"a-b","events":5}"""))
        |> should equal (RocketManifestError.WrongKind ("events", "a list", "the component \"a-b\""))

    [<Fact>]
    let ``RocketManifest.parse says what is wrong with the version and the time`` () =
        errorOf (RocketManifest.parse """{"version":"1","generatedAt":"2026-01-01T00:00:00Z","components":[]}""")
        |> should equal (RocketManifestError.WrongKind ("version", "a number", "the manifest"))
        errorOf (RocketManifest.parse """{"version":1.5,"generatedAt":"2026-01-01T00:00:00Z","components":[]}""")
        |> should equal (RocketManifestError.WrongKind ("version", "a whole number", "the manifest"))
        errorOf (RocketManifest.parse """{"version":1,"generatedAt":"yesterday","components":[]}""")
        |> should equal (RocketManifestError.WrongKind ("generatedAt", "a date", "the manifest"))
        errorOf (RocketManifest.parse """{"version":1,"generatedAt":5,"components":[]}""")
        |> should equal (RocketManifestError.WrongKind ("generatedAt", "text", "the manifest"))

    [<Fact>]
    let ``RocketManifest.parse refuses JSON that is not an object`` () =
        for json in [ "[]"; "null"; "5"; "\"text\"" ] do
            errorOf (RocketManifest.parse json) |> should equal RocketManifestError.NotAnObject

    [<Fact>]
    let ``RocketManifest.parse returns an error, and does not throw, for an empty body, no text or a broken string`` () =
        (match errorOf (RocketManifest.parse "") with | RocketManifestError.NotJson _ -> true | _ -> false) |> should equal true
        errorOf (RocketManifest.parse null) |> should equal (RocketManifestError.NotJson "there is no text to read")
        // Half of a surrogate pair is JSON, but .NET cannot turn it into text
        (match errorOf (RocketManifest.parse """{"version":1,"generatedAt":"\ud800","components":[]}""") with | RocketManifestError.NotJson _ -> true | _ -> false)
        |> should equal true
        // The text itself has half of a surrogate pair, so it cannot be turned into UTF-8. It is built here, because the F# compiler changes such a character in a string literal.
        let broken = "{\"version\":1,\"generatedAt\":\"" + string '\ud800' + "\",\"components\":[]}"
        (match errorOf (RocketManifest.parse broken) with | RocketManifestError.NotJson _ -> true | _ -> false)
        |> should equal true

    /// A stream that has as many bytes as you ask for, and counts how many were read
    type private EndlessStream(length:int64) =
        inherit Stream()
        let mutable read = 0L
        member _.BytesRead = read
        override _.CanRead = true
        override _.CanSeek = false
        override _.CanWrite = false
        override _.Length = length
        override _.Position with get () = read and set _ = raise (NotSupportedException())
        override _.Flush () = ()
        override _.Seek (_, _) = raise (NotSupportedException())
        override _.SetLength _ = raise (NotSupportedException())
        override _.Write (_, _, _) = raise (NotSupportedException())
        override _.Read (buffer:byte[], offset:int, count:int) =
            let available = int (min (int64 count) (length - read))
            Array.Fill(buffer, byte 'x', offset, available)
            read <- read + int64 available
            available

    [<Fact>]
    let ``Request.getRocketManifests stops reading a large body soon after the limit`` () =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- "POST"
        let body = new EndlessStream(10L * 1024L * 1024L)
        ctx.Request.Body <- body
        (Request.getRocketManifests ctx).GetAwaiter().GetResult()
        |> should equal (Error (RocketManifestError.TooLarge (1024 * 1024)) : Result<RocketManifestDocument, RocketManifestError>)
        // One mebibyte and the chunk that went over it
        body.BytesRead |> should be (lessThanOrEqualTo (1024L * 1024L + 8192L))

    [<Fact>]
    let ``Request.getRocketManifests reads a body of exactly one mebibyte`` () =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- "POST"
        ctx.Request.Body <- new EndlessStream(1024L * 1024L)
        match (Request.getRocketManifests ctx).GetAwaiter().GetResult() with
        | Error (RocketManifestError.NotJson _) -> ()
        | other -> failwith $"expected the body to be read and refused as not JSON, got %A{other}"

    /// A body that fails with the exception it is given, on the first read
    type private FailingStream(error:exn) =
        inherit Stream()
        override _.CanRead = true
        override _.CanSeek = false
        override _.CanWrite = false
        override _.Length = raise (NotSupportedException())
        override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
        override _.Flush () = ()
        override _.Seek (_, _) = raise (NotSupportedException())
        override _.SetLength _ = raise (NotSupportedException())
        override _.Write (_, _, _) = raise (NotSupportedException())
        override _.Read (_, _, _) = raise error

    let private bodyThatFailsWith (error:exn) =
        let ctx = DefaultHttpContext()
        ctx.Request.Body <- new FailingStream(error)
        ctx

    [<Fact>]
    let ``Request.getRocketManifests gives ConnectionFailed for what ASP.NET Core throws for a bad or oversized request body`` () =
        // BadHttpRequestException is an IOException. Kestrel throws it for a body that is malformed, too slow or larger than its own limit.
        let ctx = bodyThatFailsWith (BadHttpRequestException("Request body too large.", 413))
        (Request.getRocketManifests ctx).GetAwaiter().GetResult()
        |> should equal (Error (RocketManifestError.ConnectionFailed "Request body too large.") : Result<RocketManifestDocument, RocketManifestError>)

    [<Fact>]
    let ``Request.getRocketManifests still throws for a mistake in the code, so that it is not hidden`` () =
        let ctx = bodyThatFailsWith (InvalidOperationException "The body was already read")
        let error = Assert.Throws<InvalidOperationException>(fun () -> (Request.getRocketManifests ctx).GetAwaiter().GetResult() |> ignore)
        error.Message |> should equal "The body was already read"

