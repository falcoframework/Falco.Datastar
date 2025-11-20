namespace Falco.Datastar.Tests

open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

[<AutoOpen>]
module private Common =
    let testElem attr =
        Elem.div attr [ Text.raw "div" ]
        |> renderNode

module DsTests =
    [<Fact>]
    let ``Ds.bind should create an attribute`` () =
        testElem [ Ds.bind "signalPath" ]
        |> should equal """<div data-bind:signal-path>div</div>"""

    [<Fact>]
    let ``Ds.post`` () =
        Ds.post "/channel"
        |> should equal """@post('/channel')"""

    [<Fact>]
    let ``Ds.post with Form `` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = Form })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;})"""

    [<Fact>]
    let ``Ds.post with SelectedForm `` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = (SelectedForm "myForm") })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;,&quot;selector&quot;:&quot;myForm&quot;})"""
