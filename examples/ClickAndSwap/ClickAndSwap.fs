open Falco
open Falco.Datastar.SignalPath
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder

module View =
    let template content =
        Elem.html [ Attr.lang "en" ] [
            Elem.head [] [ Ds.cdnScript ]
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
