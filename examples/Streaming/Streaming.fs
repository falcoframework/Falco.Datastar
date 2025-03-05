open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open StarFederation.Datastar.SignalPath

type User = Guid
type StreamDisplay =
    | BadApple
    | OnlineUsers
    | LoggedOff

let userDisplays = ConcurrentDictionary<User, StreamDisplay>()

module Consts =
    let [<Literal>] streamViewId = "streamView"
    let [<Literal>] userSignalName = "user"
    let [<Literal>] displaySignalName = "display"

    let [<Literal>] displaySignalValueBadApple = "badapple"
    let [<Literal>] displaySignalValueUsers = "users"

let handleIndex ctx = task {
    let html (user:User) =
        Elem.html [] [
            Elem.head [ Attr.title "Streaming" ] [ Ds.cdnScript ]
            Elem.body [
                Ds.signal (Consts.userSignalName, user)
                Ds.signal (Consts.displaySignalName, Consts.displaySignalValueBadApple)
                Ds.onSignalChanged (Consts.displaySignalName, Ds.post "/channel")
                Ds.persistSignals ([ Consts.userSignalName ], inSession = true)
                Ds.safariStreamingFix
            ] [
                Elem.div [ Ds.onLoad (Ds.get "/stream"); Ds.indicator (sp"_streamOpen") ] []

                Text.h1 "Example: Streaming"

                Elem.input [ Attr.id "streamDisplayBadApple"; Attr.typeRadio; Attr.value Consts.displaySignalValueBadApple
                             Ds.bind Consts.displaySignalName ]
                Elem.label [ Attr.for' "streamDisplayBadApple" ] [ Text.raw "Bad Apple" ]

                Elem.input [ Attr.id "streamDisplayGuids"; Attr.typeRadio; Attr.value Consts.displaySignalValueUsers
                             Ds.bind Consts.displaySignalName ]
                Elem.label [ Attr.for' "streamDisplayGuids" ] [ Text.raw "Viewers" ]

                Elem.div [ Attr.id Consts.streamViewId ] []
            ]
        ]
    return Response.ofHtml (html (Guid.NewGuid())) ctx
    }

let handleViewChange : HttpHandler = (fun ctx -> task {
    let! user = Request.getSignal<User> (ctx, Consts.userSignalName)
    let! display = Request.getSignal<string> (ctx, Consts.displaySignalName)
    let user = user |> ValueOption.get
    let display = display |> ValueOption.get

    match display with
    | Consts.displaySignalValueBadApple -> userDisplays.AddOrUpdate(user, BadApple, (fun _ _ -> BadApple)) |> ignore
    | Consts.displaySignalValueUsers -> userDisplays.AddOrUpdate(user, OnlineUsers, (fun _ _ -> OnlineUsers)) |> ignore
    | _ -> ()
    })

let handleStream : HttpHandler = (fun ctx -> task {
    let sseHandler = Response.startServerSentEventStream ctx

    let! user = Request.getSignal<Guid> (ctx, "user")
    let user = user |> ValueOption.get

    do! handleViewChange ctx

    try
        try
            while not <| ctx.RequestAborted.IsCancellationRequested do
                let userDisplay = userDisplays.GetOrAdd(user, BadApple)
                match userDisplay with
                | OnlineUsers ->
                    let users = userDisplays |> Seq.filter (fun user -> user.Value.IsLoggedOff |> not) |> Seq.map _.Key.ToString() |> String.concat "\n"
                    do! Response.sseHtmlFragments (sseHandler, Elem.pre [ Attr.id Consts.streamViewId ] [ Text.raw users ] )
                    do! Task.Delay(TimeSpan.FromSeconds 1L, ctx.RequestAborted)
                | BadApple ->
                    do! Response.sseHtmlFragments (sseHandler, Elem.pre [ Attr.id Consts.streamViewId ] [ Text.raw (Animation.getCurrentBadAppleFrame())  ])
                    do! Task.Delay(TimeSpan.FromMilliseconds(50), ctx.RequestAborted)
                | LoggedOff ->
                    raise (OperationCanceledException())
        with
        | :? OperationCanceledException -> ()
    finally
        userDisplays[user] <- LoggedOff
    })

let wapp = WebApplication.Create()

let endpoints =
    [
        get "/" (fun ctx -> handleIndex ctx)
        get "/stream" handleStream
        post "/channel" handleViewChange
    ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
