[<RequireQualifiedAccess>]
module Falco.Datastar.Response

open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open StarFederation.Datastar.FSharp
open Falco.Markup

//////////////////////////////////////////////////////////////////
// SSE RESPONSES

/// Send the text/event-stream header with additional headers; after, all other events can follow
let sseStartResponseWithHeaders (ctx:HttpContext) (additionalHeaders:KeyValuePair<string, StringValues> seq) =
    ServerSentEventGenerator.StartServerEventStreamAsync (ctx.Response, additionalHeaders)

/// Send the text/event-stream response header; after, all other events can follow
let sseStartResponse (ctx:HttpContext) =
    ServerSentEventGenerator.StartServerEventStreamAsync ctx.Response

/// Patch an HTML DOM with elements
let sseHtmlElementsOptions (ctx:HttpContext) (options:PatchElementsOptions) (elements:XmlNode) =
    ServerSentEventGenerator.PatchElementsAsync (ctx.Response, renderHtml elements, options)

/// Patch an HTML DOM with elements
let sseHtmlElements (ctx:HttpContext) (elements:XmlNode) =
    ServerSentEventGenerator.PatchElementsAsync (ctx.Response, renderHtml elements)

/// Patch HTML DOM with raw elements
let sseStringElementsOptions (ctx:HttpContext) (options:PatchElementsOptions) (elements:string) =
    ServerSentEventGenerator.PatchElementsAsync (ctx.Response, elements, options)

/// Patch HTML DOM with raw elements
let sseStringElements (ctx:HttpContext) (elements:string) =
    ServerSentEventGenerator.PatchElementsAsync (ctx.Response, elements)

/// Patch Datastar Signals
let ssePatchSignalsOptions<'T> (ctx:HttpContext) (patchSignalsOptions:PatchSignalsOptions) (jsonSerializerOptions:JsonSerializerOptions) (signals:'T) =
    ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, (signals, jsonSerializerOptions) |> JsonSerializer.Serialize, patchSignalsOptions)

/// Patch Datastar Signals
let ssePatchSignals<'T> (ctx:HttpContext) (signals:'T) =
    ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, signals |> JsonSerializer.Serialize)

// Patch Datastar Signals with a SignalPath -> value
let ssePatchSignalOptions<'T> (ctx:HttpContext) (patchSignalOptions:PatchSignalsOptions) (signalPath:SignalPath) (signalValue:'T) =
    let signalPatch = (signalPath, signalValue) ||> SignalPath.createJsonNodeFromPathAndValue |> _.ToJsonString(JsonSerializerOptions.SignalsDefault)
    ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, signalPatch, patchSignalOptions)

// Patch Datastar Signals with a SignalPath -> value
let ssePatchSignal<'T> (ctx:HttpContext) (signalPath:SignalPath) (signalValue:'T) =
    let signalPatch = (signalPath, signalValue) ||> SignalPath.createJsonNodeFromPathAndValue |> _.ToJsonString()
    ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, signalPatch)

/// Remove an HTML Element by its selector; or multiple if comma separated
let sseRemoveElementOptions (ctx:HttpContext) (options:RemoveElementOptions) (selector:Selector) =
    ServerSentEventGenerator.RemoveElementAsync (ctx.Response, selector, options)

/// Remove an HTML Element by its selector; or multiple if comma separated
let sseRemoveElement (ctx:HttpContext) (selector:Selector) =
    ServerSentEventGenerator.RemoveElementAsync (ctx.Response, selector)

/// Execute Javascript on the client
let sseExecuteScriptOptions (ctx:HttpContext) (options:ExecuteScriptOptions)  (script:string) =
    ServerSentEventGenerator.ExecuteScriptAsync (ctx.Response, script, options)

/// Execute Javascript on the client
let sseExecuteScript (ctx:HttpContext) (script:string) =
    ServerSentEventGenerator.ExecuteScriptAsync (ctx.Response, script)

//////////////////////////////////////////////////////////////////
// OF RESPONSES

let internal nu task = task :> Task  // removes <unit> and converts Task<unit> to Task

/// Patch an HTML DOM with elements
let ofHtmlElementsOptions (options:PatchElementsOptions) (elements:XmlNode) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseHtmlElementsOptions ctx options elements
    }))

/// Patch an HTML DOM with elements
let ofHtmlElements (elements:XmlNode) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseHtmlElements ctx elements
    }))

/// Patch HTML DOM with raw elements
let ofHtmlStringElementsOptions (options:PatchElementsOptions) (elements:string) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseStringElementsOptions ctx options elements
    }))

/// Patch HTML DOM with raw elements
let ofHtmlStringElements (elements:string) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseStringElements ctx elements
    }))

/// Patch Datastar Signals
let ofPatchSignalsOptions<'T> (patchSignalOptions:PatchSignalsOptions) (jsonSerializerOptions:JsonSerializerOptions) (signals:'T) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! ssePatchSignalsOptions ctx patchSignalOptions jsonSerializerOptions signals
    }))

/// Patch Datastar Signals
let ofPatchSignals<'T> (signals:'T) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! ssePatchSignals ctx signals
    }))

/// Patch Datastar Signals
let ofPatchSignalOptions<'T> (options:PatchSignalsOptions) (signalPath:SignalPath) (signalValue:'T) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! ssePatchSignalOptions ctx options signalPath signalValue
    }))

/// Patch Datastar Signals
let ofPatchSignal<'T> (signalPath:SignalPath) (signalValue:'T) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! ssePatchSignal ctx signalPath signalValue
    }))

/// Remove an HTML Element by its selector; or multiple if comma separated
let ofRemoveElementOptions (options:RemoveElementOptions) (selector:Selector) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseRemoveElementOptions ctx options selector
    }))

/// Remove an HTML Element by its selector; or multiple if comma separated
let ofRemoveElement (selector:Selector) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseRemoveElement ctx selector
    }))

/// Execute Javascript on the client
let ofExecuteScriptOptions (options:ExecuteScriptOptions) (script:string) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseExecuteScriptOptions ctx options script
    }))

/// Execute Javascript on the client
let ofExecuteScript (script:string) =
    (fun ctx -> nu (task {
        do! sseStartResponse ctx
        return! sseExecuteScript ctx script
    }))
