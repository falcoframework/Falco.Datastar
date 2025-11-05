open System
open System.Threading.Tasks
open Falco
open Falco.Datastar.Selector
open Falco.Datastar.SignalPath
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder
open StarFederation.Datastar.FSharp

let nMoreAgents = 5

[<CLIMutable>]
type InnerSignals =
    { lastAgentShown: int }

[<CLIMutable>]
type MySignals =
    { InnerSignals: InnerSignals }

let handleIndex: HttpHandler =
    let html =
        Elem.html
            []
            [ Elem.head [] [ Ds.cdnScript ]
              Elem.body
                  [ Ds.signal (sp "innerSignals.lastAgentShown", 0) ]
                  [ Text.h1 "Example: Click to Load"
                    Elem.div [ Attr.id "loadMoreAgentsButton" ] []
                    Elem.table
                        []
                        [ Elem.thead
                              []
                              [ Elem.th [] [ Text.raw "Name" ]
                                Elem.th [] [ Text.raw "Email" ]
                                Elem.th [] [ Text.raw "ID" ] ]
                          Elem.tbody [ Attr.id "agent_rows"; Ds.onInit (Ds.get "/moreAgents") ] [] ] ] ]

    Response.ofHtml html

let appendRowsFragmentOptions =
    { PatchElementsOptions.Defaults with
        Selector = ValueSome(sel "#agent_rows")
        PatchMode = Append }

let handleMoreAgents: HttpHandler =
    (fun ctx ->
        task {
            // start the text/event-stream
            do! Response.sseStartResponse ctx

            // get the last agent shown number
            let! signals = Request.getSignals<MySignals> ctx
            let lastAgentShown = signals |> ValueOption.get |> _.InnerSignals.lastAgentShown

            // remove the button - this will not exist on the first call
            do! Response.sseRemoveElement ctx (sel "#loadMoreAgentsButton")

            // N more agents
            do!
                seq { (lastAgentShown + 1) .. (lastAgentShown + nMoreAgents) }
                |> Seq.map (fun num ->
                    Elem.tr []
                        [ Elem.td [] [ Text.raw "Agent Smith" ]
                          Elem.td [] [ Text.raw $"agent{num}@null.org" ]
                          Elem.td [] [ Text.raw (Guid.NewGuid().ToString("n")) ] ])
                |> Seq.map (Response.sseHtmlElementsOptions ctx appendRowsFragmentOptions)
                |> Task.WhenAll

            // restore load more agents button
            let button =
                Elem.tr []
                    [ Elem.td
                        [ Attr.id "loadMoreAgentsButton"; Attr.colspan "3" ]
                        [ Elem.button [ Ds.onClick (Ds.get "/moreAgents") ] [ Text.raw "Load More Agents..." ] ] ]

            do! Response.sseHtmlElementsOptions ctx appendRowsFragmentOptions button

            // update the lastAgentShown count
            do! Response.ssePatchSignal ctx (sp "lastAgentShown") (lastAgentShown + nMoreAgents)
        })

let wApp = WebApplication.Create()

let endpoints =
    [ get "/" handleIndex
      get "/moreAgents" handleMoreAgents ]

wApp.UseRouting().UseFalco(endpoints).Run()
