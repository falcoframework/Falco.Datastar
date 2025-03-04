open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder
open StarFederation.Datastar.SignalPath

module View =
    let template content =
        Elem.html [ Attr.lang "en" ] [
            Elem.head [] [
                Ds.cdnScript
                Elem.link [ Attr.href "style.css"; Attr.rel "stylesheet"; Attr.type' "text/css" ]
            ]
            Elem.body [ Ds.signal (sp"showWayToGo", false) ]
                content ]

    module Components =
        let clicker =
            Elem.button
                [ Ds.onClick "$showWayToGo = !$showWayToGo" ]
                [ Text.raw "Click Me" ]

module App =
    let handleIndex : HttpHandler =
        let html =
            View.template [
                Text.h1 "Example: Click & Swap"
                Elem.h2 [ Ds.show "$showWayToGo" ] [ Text.raw "Way to go! You clicked it!" ]
                View.Components.clicker ]

        Response.ofHtml html

[<EntryPoint>]
let main args =
    let wapp = WebApplication.Create()

    let endpoints =
        [
            get "/" App.handleIndex
        ]

    wapp.UseRouting()
        .UseFalco(endpoints)
        .Run()
    0 // Exit code
