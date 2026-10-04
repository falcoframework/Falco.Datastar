[<RequireQualifiedAccess>]
module Falco.Datastar.Request

open System
open System.IO
open System.Text
open System.Text.Json
open Microsoft.AspNetCore.Http
open StarFederation.Datastar.FSharp

/// UTF-8 that raises on a byte it cannot decode. Encoding.UTF8 itself substitutes U+FFFD and carries on, which would
/// turn bytes that are not valid UTF-8 into a manifest that parses, with a tag name or a doc string quietly altered.
let private StrictUtf8 = UTF8Encoding(false, true)

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

/// The largest manifest body that getRocketManifests reads. The manifest of a whole page is far smaller than this.
let private maxManifestBytes = 1024 * 1024

/// <summary>
/// Read the manifest that Rocket's publishRocketManifests posts to your server. It returns a RocketManifestError when the body is not a manifest this library can read,
/// when it is larger than 1 MiB (the rest of the body is not read), when the connection fails before the whole body arrives (ConnectionFailed),
/// and when the request is cancelled (Cancelled). It does not throw for any of these. Anything else is a mistake in the code, and it does throw.
/// Can only call this once per request
/// </summary>
/// <param name="ctx">HttpContext</param>
let getRocketManifests (ctx:HttpContext) =
    task {
        try
            use body = new MemoryStream()
            let chunk : byte array = Array.zeroCreate 8192
            let mutable finished = false
            while not finished && body.Length <= int64 maxManifestBytes do
                let! count = ctx.Request.Body.ReadAsync(Memory<byte>(chunk), ctx.RequestAborted)
                match count with
                | 0 -> finished <- true
                | count -> body.Write(chunk, 0, count)
            match body.Length > int64 maxManifestBytes with
            | true ->
                return Error (RocketManifestError.TooLarge maxManifestBytes)
            | false ->
                // A strict decoder: Encoding.UTF8 replaces a malformed byte with U+FFFD, so a tag name or a
                // documentation string that was not valid UTF-8 would arrive altered and then be accepted as a
                // manifest. Bytes that are not valid UTF-8 are not JSON here, so they are reported as NotJson.
                try
                    let text = StrictUtf8.GetString(body.GetBuffer(), 0, int body.Length)
                    return RocketManifest.parse text
                with :? DecoderFallbackException as error ->
                    return Error (RocketManifestError.NotJson $"the request body is not valid UTF-8: {error.Message}")
        with
        | :? OperationCanceledException -> return Error RocketManifestError.Cancelled
        // ASP.NET Core's BadHttpRequestException and the errors of a connection that was reset are IOExceptions too
        | :? IOException as error -> return Error (RocketManifestError.ConnectionFailed error.Message)
    }
