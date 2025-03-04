open System
open System.Collections.Generic
open System.Threading
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
    | Users
    | Off of SemaphoreSlim
    | LoggedOff

let connectedUsers = Dictionary<User, StreamDisplay>()

module Consts =
    let [<Literal>] streamViewId = "streamView"
    let [<Literal>] userSignalName = "user"
    let [<Literal>] displaySignalName = "display"

    let [<Literal>] displaySignalValueBadApple = "badapple"
    let [<Literal>] displaySignalValueUsers = "users"
    let [<Literal>] displaySignalValueOff = "off"

let badAppleFrames = Animation.badAppleFrames
let mutable currentBadAppleFrame = 0
let totalBadAppleFrames = badAppleFrames |> Array.length
backgroundTask {
    while true do
        currentBadAppleFrame <- (currentBadAppleFrame + 1) % totalBadAppleFrames
        do! Task.Delay(TimeSpan.FromMilliseconds(50))
} |> ignore

let handleIndex ctx = task {
    let html (user:User) =
        Elem.html [] [
            Elem.head [ Attr.title "Streaming" ] [ Ds.cdnScript ]
            Elem.body [
                Ds.signal (Consts.userSignalName, user)
                Ds.signal (Consts.displaySignalName, Consts.displaySignalValueOff)
                Ds.onSignalChanged (Consts.displaySignalName, Ds.post "/channel")
                Ds.persistSignals [ Consts.userSignalName ]
                Ds.safariStreamingFix
            ] [
                Elem.div [ Ds.onLoad (Ds.get "/stream"); Ds.indicator (sp"_streamOpen") ] []

                Text.h1 "Example: Streaming"

                Elem.input [ Attr.id "streamDisplayBadApple"; Attr.typeRadio; Attr.value Consts.displaySignalValueBadApple
                             Ds.bind Consts.displaySignalName ]
                Elem.label [ Attr.for' "streamDisplayBadApple" ] [ Text.raw "Bad Apple" ]

                Elem.input [ Attr.id "streamDisplayGuids"; Attr.typeRadio; Attr.value Consts.displaySignalValueUsers
                             Ds.bind Consts.displaySignalName ]
                Elem.label [ Attr.for' "streamDisplayGuids" ] [ Text.raw "Users" ]

                Elem.input [ Attr.id "streamDisplayOff"; Attr.typeRadio; Attr.value Consts.displaySignalValueOff
                             Ds.bind Consts.displaySignalName ]
                Elem.label [ Attr.for' "streamDisplayOff" ] [ Text.raw "Off" ]

                Elem.div [ Attr.id Consts.streamViewId ] []
            ]
        ]
    return Response.ofHtml (html (Guid.NewGuid())) ctx
    }

let handleViewChange () : HttpHandler = (fun ctx -> task {
    let! user = Request.getSignal<User> (ctx, Consts.userSignalName)
    let! display = Request.getSignal<string> (ctx, Consts.displaySignalName)
    try
        let user = user |> ValueOption.get
        let display = display |> ValueOption.get

        if not <| connectedUsers.ContainsKey(user) then
            connectedUsers[user] <- LoggedOff

        match connectedUsers[user] with
        | Off semaphore -> semaphore.Release() |> ignore
        | _ -> ()
        match display with
        | Consts.displaySignalValueBadApple -> connectedUsers[user] <- BadApple
        | Consts.displaySignalValueUsers -> connectedUsers[user] <- Users
        | Consts.displaySignalValueOff -> connectedUsers[user] <- Off (new SemaphoreSlim(0))
    //with | _ -> ()
    finally
        ()

    })

let handleStream () : HttpHandler = (fun ctx -> task {
    let sseHandler = Response.startServerSentEventStream ctx

    let! user = Request.getSignal<Guid> (ctx, "user")
    let user = user |> ValueOption.get

    if not <| connectedUsers.ContainsKey(user) || connectedUsers[user].IsLoggedOff then
        do! Response.sseMergeSignal (sseHandler, Consts.displaySignalName, Consts.displaySignalValueOff)
        connectedUsers[user] <- Off (new SemaphoreSlim(0))

    try
        try
            while not <| ctx.RequestAborted.IsCancellationRequested do
                match connectedUsers[user] with
                | Off semaphoreSlim ->
                    do! Response.sseHtmlFragments (sseHandler, Elem.pre [ Attr.id Consts.streamViewId ] [ Text.raw "OFF!" ])
                    do! semaphoreSlim.WaitAsync(ctx.RequestAborted)
                    try
                        semaphoreSlim.Dispose()
                    with | _ -> ()
                | Users ->
                    let users = connectedUsers |> Seq.filter (fun user -> user.Value.IsLoggedOff |> not) |> Seq.map _.Key.ToString() |> String.concat "\r\n"
                    do! Response.sseHtmlFragments (sseHandler, Elem.pre [ Attr.id Consts.streamViewId ] [ Text.raw users ] )
                    do! Task.Delay(TimeSpan.FromSeconds 1L, ctx.RequestAborted)
                | BadApple ->
                    do! Response.sseHtmlFragments (sseHandler, Elem.pre [ Attr.id Consts.streamViewId ] [ Text.raw badAppleFrames[currentBadAppleFrame] ])
                    do! Task.Delay(TimeSpan.FromMilliseconds(50), ctx.RequestAborted)
                | _ ->
                    Console.WriteLine "oh geez"
                    failwith "oh geez"

        with
        | :? OperationCanceledException -> ()
    finally
        connectedUsers[user] <- LoggedOff
    })

let wapp = WebApplication.Create()

let endpoints =
    [
        get "/" (fun ctx -> handleIndex ctx)
        get "/stream" (handleStream())
        post "/channel" (handleViewChange())
    ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
