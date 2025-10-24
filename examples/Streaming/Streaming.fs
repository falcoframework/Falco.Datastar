open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.FSharp.Core
open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Falco.Datastar.SignalPath

type User = Guid
type UserState = string

let userDisplays = ConcurrentDictionary<User, UserState>()

[<CLIMutable>]
type StreamSignals =
    { User:User
      Display:UserState }

module ElementIds =
    let [<Literal>] streamView = "streamView"

module SignalPath =
    let userName = sp"user"
    let displayType = sp"display"

module UserState =
    let [<Literal>] displayBadApple = "Watching: Bad Apple"
    let [<Literal>] displayUsers = "Users"
    let [<Literal>] loggedOff = "Logged Off"

let handleIndex ctx = task {
    let html (user:User) =
        Elem.html [] [
            Elem.head [ Attr.title "Streaming" ] [
                Ds.cdnScript
                Elem.script [ Attr.type' "module"; Attr.src "datastar-inspector.js" ] []
            ]
            Elem.body [
                Ds.signal (SignalPath.userName, user)
                Ds.signal (SignalPath.displayType, UserState.displayBadApple)
                Ds.filterOnSignalPatch (SignalsFilter.Include SignalPath.displayType)
                Ds.onSignalPatch (Ds.get "/channel")
                Ds.safariStreamingFix
            ] [
                Elem.div [ Ds.onLoad (Ds.get "/stream") ] []

                Text.h1 "Example: Streaming"

                Elem.input [ Attr.id "streamChannel"
                             Attr.typeRadio
                             Attr.value UserState.displayBadApple
                             Ds.bind SignalPath.displayType ]
                Elem.label [ Attr.for' "streamDisplayBadApple" ] [ Text.raw "Bad Apple" ]

                Elem.input [ Attr.id "streamChannel"
                             Attr.typeRadio
                             Attr.value UserState.displayUsers
                             Ds.bind SignalPath.displayType ]
                Elem.label [ Attr.for' "streamDisplayGuids" ] [ Text.raw "Viewers" ]

                Elem.div [ Attr.id ElementIds.streamView ] []

                Elem.create "datastar-inspector" [] []
            ]
        ]
    return Response.ofHtml (html (Guid.NewGuid())) ctx
    }

let handleViewChange ctx = task {
    let! signals = Request.getSignals<StreamSignals> ctx
    match signals with
    | ValueNone -> ()
    | ValueSome signals -> userDisplays.AddOrUpdate (signals.User, signals.Display, Func<User, UserState, UserState>(fun _ _ -> signals.Display)) |> ignore
    }

let handleStream ctx = task {
    Response.sseStartResponse ctx |> ignore
    let! signals = Request.getSignals<StreamSignals> ctx
    let signalUser =
        match signals with
        | ValueSome signals -> signals.User
        | ValueNone -> failwith "no user"

    // reset to displaying BadApple
    //  when the browser tab is hidden and re-displayed, /stream request is made again with out-dated signal values
    //  we could create more states for the user to restore the original view, but it wasn't the focus of the demo
    userDisplays.AddOrUpdate (signalUser, UserState.displayBadApple, Func<User, UserState, UserState>(fun user userState -> UserState.displayBadApple)) |> ignore
    do! Response.ssePatchSignal ctx SignalPath.displayType UserState.displayBadApple

    try
        while true do
            let _, streamDisplay = userDisplays.TryGetValue(signalUser)

            let patch =
                match streamDisplay with
                | UserState.displayUsers ->
                    Elem.pre [ Attr.id ElementIds.streamView ] [
                        Text.raw (
                            userDisplays
                            |> Seq.map (fun ud -> (ud.Key, ud.Value))
                            |> Seq.map (fun (user, display) -> ((if user = signalUser then "YOU" else user.ToString()), display ))
                            |> Seq.map (fun (user, display) -> $"{user}: {display}") |> String.concat Environment.NewLine)
                    ]
                | _ ->
                    Elem.pre [ Attr.id ElementIds.streamView ] [
                        Text.raw (Animation.getCurrentBadAppleFrame ())
                    ]

            do! Response.sseHtmlElements ctx patch
            do! Task.Delay (TimeSpan.FromSeconds(10L), ctx.RequestAborted)
    finally
        userDisplays.AddOrUpdate (signalUser, UserState.displayBadApple, Func<User, UserState, UserState>(fun _ _ -> UserState.loggedOff)) |> ignore
    return ()
    }

let wapp = WebApplication.Create()
wapp.UseStaticFiles() |> ignore

let endpoints =
    [ get "/" (fun ctx -> handleIndex ctx)
      get "/stream" (fun ctx -> handleStream ctx)
      get "/channel" (fun ctx -> handleViewChange ctx) ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
