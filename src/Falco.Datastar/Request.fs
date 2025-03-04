namespace Falco.Datastar

open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.FSharp.Core
open StarFederation.Datastar

type Request =

    /// <summary>
    /// Get the signal values in JSON string format; '{}' if no signals read
    /// </summary>
    /// <param name="ctx">HttpContext</param>
    static member getSignals (ctx:HttpContext) = task {
        let signalsHandler: IReadSignals = SignalsHttpHandlers ctx.Request
        let! signalsRaw = signalsHandler.ReadSignals()
        return signalsRaw |> ValueOption.map Signals.value |> ValueOption.defaultValue "{}"
        }

    /// <summary>
    /// Deserialize the signal values into 'T; deserializes '{}' if no signals read
    /// </summary>
    /// <param name="ctx">HttpContext</param>
    /// <param name="options">optional JsonSerializerOptions for System.Text.Json.JsonSerializer</param>
    static member getSignals<'T> (ctx:HttpContext, ?options:JsonSerializerOptions) : Task<'T> = task {
        let options = defaultArg options JsonSerializerOptions.Default
        let signalsHandler: IReadSignals = SignalsHttpHandlers ctx.Request
        let! signalsRaw = signalsHandler.ReadSignals()
        let signalsRaw' = signalsRaw |> ValueOption.defaultValue "{}"
        return JsonSerializer.Deserialize<'T>(signalsRaw', options)
        }

    /// <summary>
    /// Deserialize the signal value at a path into 'T; ValueNone if no signals or none found at path
    /// </summary>
    /// <param name="ctx">HttpContext</param>
    /// <param name="signalPath">Path to retrieve</param>
    /// <param name="options">optional JsonSerializerOptions for System.Text.Json.JsonElement.Deserialize</param>
    static member getSignal<'T> (ctx, signalPath, ?options:JsonSerializerOptions) = task {
        let options = defaultArg options JsonSerializerOptions.Default
        let! signalsJson = Request.getSignals ctx
        let signalPathKeys = signalPath |> SignalPath.keys |> Array.toList
        let value =
            try
                use jsonDocument = JsonDocument.Parse(signalsJson)
                let jsonElement = Utility.jsonElementAtPath jsonDocument.RootElement signalPathKeys
                jsonElement |> ValueOption.map _.Deserialize<'T>(options)
            with | _ -> ValueNone
        return value
        }
