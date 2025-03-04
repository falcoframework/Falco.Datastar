open Falco
open Falco.Routing
open Falco.Datastar
open Falco.Markup
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open StarFederation.Datastar.SignalPath

let ofMainBody : HttpHandler =
    let htmlXml =
        Elem.html [] [
            Elem.head [] [
                Elem.title [] [ Text.raw "Click to Edit" ]
                Ds.cdnScript
            ]
            Elem.body [] [
                Text.h1 "Example: Click to Edit"
                Elem.div [ Attr.id "contact"; Ds.onLoad (Ds.get "clickToEdit/view") ] []
            ]
        ]
    Response.ofHtml htmlXml

module clickToEdit =

    type MySignals() =
        member val firstName = "John" with get, set
        member val lastName = "Doe" with get, set
        member val email = "john@doe.com" with get, set

    let readonlyFragment : HttpHandler = (fun ctx -> task {
        let! signals = Request.getSignals<MySignals>(ctx)
        let fragment =
            Elem.div [ Attr.id "contact"; Ds.signals signals ] [
                Elem.label [] [ Text.raw "First Name:" ]; Elem.span [ Ds.text "$firstName" ] []; Elem.br []
                Elem.label [] [ Text.raw "Last Name:" ];  Elem.span [ Ds.text "$lastName" ] []; Elem.br []
                Elem.label [] [ Text.raw "Email:" ];      Elem.span [ Ds.text "$email" ] []; Elem.br []
                Elem.button [ Ds.onClick (Ds.get "clickToEdit/edit") ] [ Text.raw "Edit" ]
                Elem.button [ Ds.onClick (Ds.get "clickToEdit/reset") ] [ Text.raw "Reset" ]
            ]
        return Response.ofHtmlFragments fragment ctx
        })

    let editFragment : HttpHandler = (fun ctx -> task {
        let! signals = Request.getSignals<MySignals>(ctx)
        let fragment =
            Elem.div [ Attr.id "contact"; Ds.signals signals ] [
                Elem.label [] [ Text.raw "First Name:" ]; Elem.input [ Attr.type' "text"; Ds.bind (sp"firstName") ]; Elem.br []
                Elem.label [] [ Text.raw "Last Name:" ];  Elem.input [ Attr.type' "text"; Ds.bind (sp"lastName") ]; Elem.br []
                Elem.label [] [ Text.raw "Email:" ];      Elem.input [ Attr.type' "text"; Ds.bind (sp"email") ]; Elem.br []
                Elem.button [ Ds.onClick (Ds.put "clickToEdit/save") ] [ Text.raw "Save" ]
                Elem.button [ Ds.onClick (Ds.get "clickToEdit/reset") ] [ Text.raw "Reset" ]
            ]
        return Response.ofHtmlFragments fragment ctx
        })

    let resetSignals : HttpHandler = Response.ofMergeSignals (MySignals())

    let save (ctx:HttpContext) =
        // TODO: get the signals and save to a database
        ()

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let app = builder.Build()

    let endpoints = [
        get "/" (Response.redirectTemporarily "/clickToEdit")
        get "/clickToEdit" ofMainBody
        get "/clickToEdit/view" clickToEdit.readonlyFragment
        get "/clickToEdit/edit" clickToEdit.editFragment
        get "/clickToEdit/reset" clickToEdit.resetSignals
        put "/clickToEdit/save" (fun ctx -> clickToEdit.save ctx; clickToEdit.readonlyFragment ctx)
        ]

    app.UseRouting().UseFalco(endpoints) |> ignore
    app.Run()

    0 // Exit code


