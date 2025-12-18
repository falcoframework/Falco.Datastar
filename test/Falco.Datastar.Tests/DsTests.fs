namespace Falco.Datastar.Tests

open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

[<AutoOpen>]
module private Common =
    let renderAttr attr =
        Elem.div [ attr ] [ ]
        |> renderNode

module DsTests =
    [<Fact>]
    let ``Ds.bind should create an attribute`` () =
        renderAttr (Ds.bind "signalPath")
        |> should equal """<div data-bind:signal-path></div>"""

    [<Fact>]
    let ``Ds.post`` () =
        Ds.post "/channel"
        |> should equal """@post('/channel')"""

    [<Fact>]
    let ``Ds.post with Form`` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = Form })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;})"""

    [<Fact>]
    let ``Ds.post with SelectedForm`` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = (SelectedForm "myForm") })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;,&quot;selector&quot;:&quot;myForm&quot;})"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Exclude`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueNone; ExcludePattern = ValueSome "files" }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ exclude: /files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Include`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files"; ExcludePattern = ValueNone }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ include: /files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Both`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files$"; ExcludePattern = ValueSome "^files" }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ include: /files$/,exclude: /^files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Terse`` () =
        renderAttr (Ds.jsonSignalsOptions (terse = true))
        |> should equal """<div data-json-signals__terse></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Terse and Both Filters`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files$"; ExcludePattern = ValueSome "^files" }
        renderAttr (Ds.jsonSignalsOptions (filterFiles, terse = true))
        |> should equal """<div data-json-signals__terse="{ include: /files$/,exclude: /^files/ }"></div>"""

    [<Fact>]
    let ``Ds.onIntersect No Options`` () =
        renderAttr (Ds.onIntersect "@get('/hello')")
        |> should equal """<div data-on-intersect="@get('/hello')"></div>"""

    [<Fact>]
    let ``Ds.onIntersect Threshold`` () =
        renderAttr (Ds.onIntersect ("@get('/hello')", threshold=50))
        |> should equal """<div data-on-intersect__threshold.50="@get('/hello')"></div>"""

    [<Fact>]
    let ``Ds.style`` () =
        renderAttr (Ds.style ("display", "$hiding && 'none'"))
        |> should equal """<div data-style:display="$hiding && 'none'"></div>"""

