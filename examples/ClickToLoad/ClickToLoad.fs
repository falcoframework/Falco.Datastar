open System
open System.Threading.Tasks
open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder
open StarFederation.Datastar
open StarFederation.Datastar.Selector
open StarFederation.Datastar.SignalPath

let nMoreAgents = 5

let handleIndex : HttpHandler =
    let html =
        Elem.html [] [
            Elem.head [] [ Ds.cdnScript ]
            Elem.body [ Ds.signal (sp"lastAgentShown", 0) ] [
                Text.h1 "Example: Click to Load"
                Elem.table [] [
                    Elem.thead [] [
                        Elem.th [] [ Text.raw "Name" ]
                        Elem.th [] [ Text.raw "Email" ]
                        Elem.th [] [ Text.raw "ID" ]
                    ]
                    Elem.tbody [ Attr.id "agent_rows"; Ds.onLoad (Ds.get "/moreAgents") ] []
                ]
            ]
        ]
    Response.ofHtml html

let appendRowsFragmentOptions =
    { MergeFragmentsOptions.defaults
        with Selector = ValueSome (sel"#agent_rows")
             MergeMode = Append
             SettleDuration = TimeSpan.FromSeconds(1L) }

let handleMoreAgents : HttpHandler = (fun ctx -> task {
    // start the text/event-stream
    let sse = Response.startServerSentEventStream ctx

    // get the last agent shown number
    let! lastAgentShown = Request.getSignal<int> (ctx, sp"lastAgentShown")
    let lastAgentShown = lastAgentShown |> ValueOption.get

    // remove the button
    do! Response.sseRemoveFragments (sse, "#loadMoreAgentsButton", { RemoveFragmentsOptions.defaults with SettleDuration = TimeSpan.Zero })

    // N more agents
    do!
        seq { (lastAgentShown + 1) .. (lastAgentShown + nMoreAgents) }
        |> Seq.map (fun num ->
            Elem.tr [] [
                Elem.td [] [ Text.raw "Agent Smith" ]
                Elem.td [] [ Text.raw $"agent{num}@null.org" ]
                Elem.td [] [ Text.raw (Guid.NewGuid().ToString("n")) ]
            ])
        |> Seq.map (fun rowHtml -> Response.sseHtmlFragments (sse, rowHtml, appendRowsFragmentOptions))
        |> Task.WhenAll

    // restore load more agents button
    let button =
        Elem.tr [] [
            Elem.td [ Attr.id "loadMoreAgentsButton"; Attr.colspan "3" ] [
                Elem.button [ Ds.onClick (Ds.get "/moreAgents") ] [ Text.raw "Load More Agents..." ]
            ]
        ]
    do! Response.sseHtmlFragments (sse, button, appendRowsFragmentOptions)

    // update the lastAgentShown count
    do! Response.sseMergeSignals (sse, $@" {{ ""lastAgentShown"": {lastAgentShown + nMoreAgents} }} ")
    })

let wApp = WebApplication.Create()

let endpoints =
    [
        get "/" handleIndex
        get "/moreAgents" handleMoreAgents
    ]

wApp.UseRouting()
    .UseFalco(endpoints)
    .Run()
