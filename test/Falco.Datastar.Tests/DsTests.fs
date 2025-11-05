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
