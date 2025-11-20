open Falco
open Falco.Datastar.Selector
open Falco.Datastar.SignalPath
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

module View =
    let template content =
        Elem.html [ Attr.lang "en" ] [
            Elem.head [] [ Ds.cdnScript ]
            Elem.body [] content
            ]

module App =
    let handleIndex : HttpHandler =
        let checkbox name =
            [ Text.raw $"{name}:"; Elem.input [ Attr.type' "checkbox"; Attr.name "checkboxes"; Attr.value name ] ]
        let html =
            View.template [
                Text.h1 "Example: Input Form"
                Elem.div [] [
                    Elem.form [ Attr.id "myform" ] [
                        yield! checkbox "foo"
                        yield! checkbox "bar"
                        yield! checkbox "baz"
                        Elem.button [ Ds.onClick (Ds.get("/endpoint1", RequestOptions.With(Form))) ] [ Text.raw "Submit GET Request" ]
                        Elem.button [ Ds.onClick (Ds.post("/endpoint1", RequestOptions.With(Form))) ] [ Text.raw "Submit POST Request" ]
                        ]
                    Elem.button [ Ds.onClick (Ds.get("/endpoint1", RequestOptions.With(SelectedForm (sel"#myform")))) ] [
                        Text.raw "Submit GET request from outside the form"
                        ]
                    ]
                Elem.hr []
                Elem.div [] [
                    Elem.form [ Ds.onEvent ("submit", (Ds.post ("/endpoint2", RequestOptions.With(Form)))) ] [
                        Text.raw "foo:"
                        Elem.input [ Attr.type' "text"; Attr.name "foo"; Attr.required ]
                        Elem.button [] [ Text.raw "Submit Form" ]
                        ]
                    ]
                ]
        Response.ofHtml html

    let handleEndpointOne (getForm:HttpContext -> RequestData) : HttpHandler = (fun ctx -> task {
        let method = ctx.Request.Method
        let form = ctx |> getForm
        let foo = form.GetStringList("checkboxes")

        let alertString = $"Form data received via {method} request: checkboxes = {foo}"
        let alertScript = $"alert('{alertString}')"

        return Response.ofExecuteScript alertScript ctx
        })

    let handleEndpointTwo (getForm:HttpContext -> RequestData): HttpHandler = (fun ctx -> task {
        let method = ctx.Request.Method
        let form = ctx |> getForm
        let foo = form.GetString("foo")

        let alertString = $"Form data received via {method} request: foo = {foo}"
        let alertScript = $"alert('{alertString}')"

        return Response.ofExecuteScript alertScript ctx
        })


[<EntryPoint>]
let main args =
    let wapp = WebApplication.Create()

    let endpoints =
        [
            get "/" App.handleIndex
            all "/endpoint1" [
                GET, (App.handleEndpointOne Request.getQuery)
                POST, (App.handleEndpointOne (fun ctx -> (ctx |> Request.getForm).Result))
                ]
            all "/endpoint2" [
                GET, (App.handleEndpointTwo Request.getQuery)
                POST, (App.handleEndpointTwo (fun ctx -> (ctx |> Request.getForm).Result))
                ]
        ]

    wapp.UseRouting()
        .UseFalco(endpoints)
        .Run()
    0 // Exit code
