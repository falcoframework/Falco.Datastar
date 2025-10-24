[<RequireQualifiedAccess>]
module Falco.Datastar.Request

open System.Text.Json
open Microsoft.AspNetCore.Http
open StarFederation.Datastar.FSharp

/// <summary>
/// Deserialize the signals into 'T, using case-insensitive JsonSerializerOptions. Can only call this once per request
/// </summary>
/// <param name="ctx">HttpContext</param>
let getSignals<'T> (ctx:HttpContext) =
    ServerSentEventGenerator.ReadSignalsAsync<'T> ctx.Request

/// <summary>
/// Deserialize the signals into 'T, using provided JsonSerializerOptions. Can only call this once per request
/// </summary>
/// <param name="jsonSerializerOptions"></param>
/// <param name="ctx">HttpContext</param>
let getSignalsOptions<'T> (jsonSerializerOptions:JsonSerializerOptions) (ctx:HttpContext)=
    ServerSentEventGenerator.ReadSignalsAsync<'T> (ctx.Request, jsonSerializerOptions)

/// <summary>
/// Retrieve a JsonDocument of the Signals. Can only call this once per request
/// </summary>
/// <param name="ctx">HttpContext</param>
let getSignalsJson (ctx:HttpContext) =
    JsonDocument.ParseAsync (ServerSentEventGenerator.GetSignalsStream(ctx.Request), JsonDocumentOptions(), ctx.RequestAborted)
