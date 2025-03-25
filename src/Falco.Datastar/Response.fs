namespace Falco.Datastar

open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Falco
open Falco.Markup
open Falco.Markup.Text
open Microsoft.AspNetCore.Http
open StarFederation.Datastar
open StarFederation.Datastar.Scripts.BrowserConsoleAction

[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Response =

    /// <summary>
    /// Starts the 'text/event-stream' and returns a ServerSentEventHttpHandlers to be used with Response.sse* functions
    /// </summary>
    /// <param name="ctx">HttpContext</param>
    static member startServerSentEventStream (ctx:HttpContext) =
        let sseHandler = ServerSentEventHttpHandlers ctx.Response
        sseHandler.StartResponse() |> ignore
        sseHandler

    //////////////////////////////////////////////////////////////////
    // SSE RESPONSES

    /// <summary>
    /// Sends an HTML fragments ServerSideEvent
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="fragments">Fragments to send</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseHtmlFragments (sseHandler, fragments, ?sseOptions) =
        ServerSentEventGenerator.mergeFragments (sseHandler, (renderHtml fragments), ?options=sseOptions)

    /// <summary>
    /// Sends an HTML fragments ServerSideEvent
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="fragments">Fragments to send</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseHtmlStringFragments (sseHandler, fragments, ?sseOptions) =
        ServerSentEventGenerator.mergeFragments (sseHandler, fragments, ?options=sseOptions)

    /// <summary>
    /// Sends signals to be merged with signals on the client
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="signals">Signals to merge</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseMergeSignals (sseHandler, signals, ?sseOptions) =
        ServerSentEventGenerator.mergeSignals (sseHandler, signals, ?options=sseOptions)

    /// <summary>
    /// Sends signals to be merged with signals on the client
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="signalPath">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="signalValue">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseMergeSignal<'T> (sseHandler, signalPath:SignalPath, signalValue:'T, ?sseOptions) =
        let signalUpdate = (signalPath, signalValue) ||> SignalPath.createJsonNodePathToValue |> _.ToJsonString()
        ServerSentEventGenerator.mergeSignals (sseHandler, signalUpdate, ?options=sseOptions)

    /// <summary>
    /// Sends signals to be merged with signals on the client
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="signals">Signals to merge</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    /// <param name="jsonSerializerOptions">Options for the serializer</param>
    static member sseMergeSignals<'T> (sseHandler, signals:'T, ?sseOptions, ?jsonSerializerOptions) =
        let jsonSerializerOptions = defaultArg jsonSerializerOptions JsonSerializerOptions.Default
        let signals = JsonSerializer.Serialize(signals, jsonSerializerOptions)
        ServerSentEventGenerator.mergeSignals (sseHandler, signals, ?options=sseOptions)

    /// <summary>
    /// Remove an HTML fragment from the client
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="selector">The selector to remove on the client DOM</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseRemoveFragments (sseHandler, selector, ?sseOptions) =
        ServerSentEventGenerator.removeFragments (sseHandler, selector, ?options=sseOptions)

    /// <summary>
    /// Remove signals from client
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="signalPaths">The paths to the signals that should be removed from the signals on the client</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseRemoveSignals (sseHandler, signalPaths, ?sseOptions) =
        ServerSentEventGenerator.removeSignals (sseHandler, signalPaths, ?options=sseOptions)

    /// <summary>
    /// Execute a Javascript on the client
    /// </summary>
    /// <param name="sseHandler">ServerSentEventHttpHandlers from Response.startServerSentEventStream</param>
    /// <param name="script">Javascript to run on the client</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member sseExecuteScript (sseHandler, script, ?sseOptions) =
        ServerSentEventGenerator.executeScript (sseHandler, script, ?options=sseOptions)

    //////////////////////////////////////////////////////////////////
    // OF RESPONSES

    /// <summary>
    /// Responds with an HTML fragments ServerSideEvent
    /// </summary>
    /// <param name="fragments">Fragments to send</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofHtmlFragments (fragments, ?sseOptions) : HttpHandler = (fun ctx ->
        ServerSentEventGenerator.mergeFragments ((Response.startServerSentEventStream ctx), (renderHtml fragments), ?options=sseOptions)
        )

    /// <summary>
    /// Responds with an HTML fragments ServerSideEvent
    /// </summary>
    /// <param name="fragments">Fragments to send</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofHtmlStringFragments (fragments, ?sseOptions) : HttpHandler = (fun ctx ->
        ServerSentEventGenerator.mergeFragments ((Response.startServerSentEventStream ctx), fragments, ?options=sseOptions)
        )

    /// <summary>
    /// Responds with signals to be merged with signals on the client
    /// </summary>
    /// <param name="signals">Signals to merge</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofMergeSignals (signals, ?sseOptions) : HttpHandler = (fun ctx ->
        ServerSentEventGenerator.mergeSignals ((Response.startServerSentEventStream ctx), signals, ?options=sseOptions)
        )

    /// <summary>
    /// Sends signals in a ServerSideEvent to be merged with signals on the client
    /// </summary>
    /// <param name="signals">Signals to merge</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    /// <param name="jsonSerializerOptions">Options for the serializer</param>
    static member ofMergeSignals<'T> (signals:'T, ?sseOptions, ?jsonSerializerOptions) : HttpHandler = (fun ctx ->
        let jsonSerializerOptions = defaultArg jsonSerializerOptions JsonSerializerOptions.Default
        let signals = JsonSerializer.Serialize(signals, jsonSerializerOptions)
        ServerSentEventGenerator.mergeSignals ((Response.startServerSentEventStream ctx), signals, ?options=sseOptions)
        )

    /// <summary>
    /// Merge a single signal on the client
    /// </summary>
    /// <param name="signalPath">Path to the signal you want to update</param>
    /// <param name="signalValue">The value to set it to</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofMergeSignal<'T> (signalPath, signalValue:'T, ?sseOptions) : HttpHandler = (fun ctx ->
        let signalUpdate = (signalPath, signalValue) ||> SignalPath.createJsonNodePathToValue |> _.ToJsonString()
        ServerSentEventGenerator.mergeSignals ((Response.startServerSentEventStream ctx), signalUpdate, ?options=sseOptions)
        )

    /// <summary>
    /// Remove an HTML fragment from the client
    /// </summary>
    /// <param name="selector">The selector to remove on the client DOM</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofRemoveFragments (selector, ?sseOptions) : HttpHandler = (fun ctx ->
        ServerSentEventGenerator.removeFragments ((Response.startServerSentEventStream ctx), selector, ?options=sseOptions)
        )

    /// <summary>
    /// Remove signals from client
    /// </summary>
    /// <param name="signalPaths">The paths to the signals that should be removed from the signals on the client</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofRemoveSignals (signalPaths, ?sseOptions) : HttpHandler = (fun ctx ->
        ServerSentEventGenerator.removeSignals ((Response.startServerSentEventStream ctx), signalPaths, ?options=sseOptions)
        )

    /// <summary>
    /// Execute a Javascript on the client
    /// </summary>
    /// <param name="script">Javascript to run on the client</param>
    /// <param name="sseOptions">ServerSentEvent Options</param>
    static member ofExecuteScript (script, ?sseOptions) : HttpHandler = (fun ctx ->
        ServerSentEventGenerator.executeScript ((Response.startServerSentEventStream ctx), script, ?options=sseOptions)
        )
