open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder

let handleIndex : HttpHandler =
    let html =
        Elem.html [] [
            Elem.head [] [ Ds.cdnScript ]
            Elem.body [] [
                Text.h1 "Example: Hello World"
                Elem.button
                    [ Attr.id "hello"; Ds.onClick (Ds.get "/click") ]
                    [ Text.raw "Click Me" ]
            ]
        ]
    Response.ofHtml html

let handleClick : HttpHandler =
    // create an html fragment which will replace the button with the same `id`
    let html = Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hello, World, from the Server!" ]
    Response.ofHtmlFragments html

let wapp = WebApplication.Create()

let endpoints =
    [
        get "/" handleIndex
        get "/click" handleClick
    ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
