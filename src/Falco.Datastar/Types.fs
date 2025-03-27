namespace Falco.Datastar

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Web

module Consts =
    let mutable dataSlugPrefix = "data"

type IntersectsVisibility =
    /// <summary>
    /// Triggers when half of the element is visible
    /// </summary>
    | Half
    /// <summary>
    /// Triggers when the full element is visible
    /// </summary>
    | Full

type ScrollIntoViewAnimation =
    /// <summary>
    /// Scrolling is animated smoothly
    /// </summary>
    | Smooth
    /// <summary>
    /// Scrolling is instant
    /// </summary>
    | Instant
    /// <summary>
    /// Scrolling is determined by the computed CSS `scroll-behavior` property
    /// </summary>
    | Auto

type ScrollIntoViewWhere =
    /// <summary>
    /// Scrolls to the top of the element
    /// </summary>
    | Top
    /// <summary>
    /// Scrolls to the left of the element
    /// </summary>
    | Left
    /// <summary>
    /// Scrolls to the horizontal center of the element
    /// </summary>
    | Center
    /// <summary>
    /// Scrolls to the bottom of the element
    /// </summary>
    | Bottom
    /// <summary>
    /// Scrolls to the right of the element
    /// </summary>
    | Right
    /// <summary>
    /// Scrolls to the nearest vertical or horizontal edge of the element
    /// </summary>
    | Edge

type BackendAction =
    | Get of url:string
    | Post of url:string
    | Put of url:string
    | Patch of url:string
    | Delete of url:string

type ContentType =
    | Json
    | Form of string voption

type RequestOptions =
    { ContentType: ContentType
      IncludeLocal: bool
      Headers: (string*string) list
      OpenWhenHidden: bool
      RetryInterval: TimeSpan
      RetryScaler: float
      RetryMaxWait: TimeSpan
      RetryMaxCount: int
      Abort: obj }

    static member Defaults =
        { ContentType = Json
          IncludeLocal = false
          Headers = []
          OpenWhenHidden = false
          RetryInterval = TimeSpan.FromSeconds(1.0)
          RetryScaler = 2.0
          RetryMaxWait = TimeSpan.FromSeconds(30.0)
          RetryMaxCount = 10
          Abort = null }

    static member internal Serialize (backendActionOptions:RequestOptions) =
        let jsonObject = JsonObject()
        match backendActionOptions.ContentType with
        | _ when backendActionOptions.ContentType = RequestOptions.Defaults.ContentType -> ()
        | Json -> jsonObject.Add("contentType", "json")
        | Form formSelector ->
            jsonObject.Add("contentType", "form")
            match formSelector with
            | ValueNone -> ()
            | ValueSome formSelector' -> jsonObject.Add("selector", formSelector')

        if backendActionOptions.IncludeLocal <> RequestOptions.Defaults.IncludeLocal then
            jsonObject.Add("includeLocal", backendActionOptions.IncludeLocal.ToString().ToLower())

        if backendActionOptions.Headers.Length > 0 then
            let headerObject = JsonObject()
            backendActionOptions.Headers |> List.iter headerObject.Add
            jsonObject.Add("headers", headerObject)

        if backendActionOptions.OpenWhenHidden <> RequestOptions.Defaults.OpenWhenHidden then
            jsonObject.Add("openWhenHidden", backendActionOptions.OpenWhenHidden.ToString().ToLower())

        if backendActionOptions.RetryInterval <> RequestOptions.Defaults.RetryInterval then
            jsonObject.Add("retryInterval", backendActionOptions.RetryInterval.TotalMilliseconds)

        if backendActionOptions.RetryScaler <> RequestOptions.Defaults.RetryScaler then
            jsonObject.Add("retryScaler", backendActionOptions.RetryScaler)

        if backendActionOptions.RetryMaxWait <> RequestOptions.Defaults.RetryMaxWait then
            jsonObject.Add("retryMaxWaitMs", backendActionOptions.RetryMaxWait.TotalMilliseconds)

        if backendActionOptions.RetryMaxCount <> RequestOptions.Defaults.RetryMaxCount then
            jsonObject.Add("retryMaxCount", backendActionOptions.RetryMaxCount)

        if backendActionOptions.Abort <> null then
            jsonObject.Add("abort", JsonSerializer.Serialize backendActionOptions.Abort)

        let options = JsonSerializerOptions()
        options.WriteIndented <- false
        HttpUtility.HtmlEncode(jsonObject.ToJsonString(options))

[<Sealed>]
type Debounce(timeSpan:TimeSpan, leading:bool, noTrail:bool) =
    member private _.timeSpan = timeSpan
    member private _.leading = leading
    member private _.noTrail = noTrail
    static member Serialize (debounce:Debounce) =
        let leading' = debounce.leading |> Bool.eitherOr ".leading" ""
        let noTrail' = debounce.noTrail |> Bool.eitherOr ".notrail" ""
        debounce.timeSpan = TimeSpan.Zero |> Bool.eitherOr "" $"debounce.{debounce.timeSpan.TotalMilliseconds}ms{leading'}{noTrail'}"
    static member SerializeOption (debounceOpt:Debounce option) =
        match debounceOpt with
        | None -> None
        | Some debounce when debounce.timeSpan = TimeSpan.Zero -> None
        | Some debounce -> Some (Debounce.Serialize debounce)
    static member With (timeSpan:TimeSpan, ?leading:bool, ?noTrail:bool) =
        let leading = defaultArg leading false
        let noTrail = defaultArg noTrail false
        Debounce(timeSpan, leading, noTrail)

[<Sealed>]
type Throttle(timeSpan:TimeSpan, noLeading:bool, trail:bool) =
    member private _.timeSpan = timeSpan
    member private _.noLeading = noLeading
    member private _.trail = trail
    static member Serialize (throttle:Throttle) =
        let noLeading' = throttle.noLeading |> Bool.eitherOr ".noleading" ""
        let trail' = throttle.trail |> Bool.eitherOr ".trail" ""
        throttle.timeSpan = TimeSpan.Zero |> Bool.eitherOr "" $"throttle.{throttle.timeSpan.TotalMilliseconds}ms{noLeading'}{trail'}"
    static member SerializeOption (throttleOpt:Throttle option) =
        match throttleOpt with
        | None -> None
        | Some throttle when throttle.timeSpan = TimeSpan.Zero -> None
        | Some throttle -> Some (Throttle.Serialize throttle)
    static member With (timeSpan:TimeSpan, ?noLeading:bool, ?trail:bool) =
        let leading = defaultArg noLeading false
        let noTrail = defaultArg trail false
        Throttle(timeSpan, leading, noTrail)

type OnEventModifier =
    /// <summary>
    /// Trigger event once. Can only be used with the built-in events (https://data-star.dev/reference/attribute_plugins#special-events).
    /// </summary>
    | Once
    /// <summary>
    /// Do not call `preventDefault` on the event listener. Can only be used with the built-in events (https://data-star.dev/reference/attribute_plugins#special-events).
    /// </summary>
    | Passive
    /// <summary>
    /// Use a capture event listener. Can only be used with the built-in events (https://data-star.dev/reference/attribute_plugins#special-events).
    /// </summary>
    | Capture
    /// <summary>
    /// Debounce the event listener; new events after an initial event, within a TimeSpan, are ignored.
    /// </summary>
    | Debounce of Debounce
    /// <summary>
    /// Throttle the event listener; only fires the last event within a TimeSpan.
    /// </summary>
    | Throttle of Throttle
    /// <summary>
    /// Wraps the expression in `document.startViewTransition()` when the View Transition API is available.
    /// </summary>
    | ViewTransition
    /// <summary>
    /// Attaches the event listener to the window element.
    /// </summary>
    | Window
    /// <summary>
    /// Triggers the event when it occurs outside the element.
    /// </summary>
    | Outside
    /// <summary>
    /// Call `preventDefault` on the event listener
    /// </summary>
    | Prevent
    /// <summary>
    /// Calls `stopPropagation` on the event listener.
    /// </summary>
    | Stop
    with
    static member internal Serialize eventModifier =
        match eventModifier with
        | Once -> "once"
        | Passive -> "passive"
        | Capture -> "capture"
        | Debounce debounce -> (Debounce.Serialize debounce)
        | Throttle throttle -> (Throttle.Serialize throttle)
        | ViewTransition -> "viewtransition"
        | Window -> "window"
        | Outside -> "outside"
        | Prevent -> "prevent"
        | Stop -> "stop"

/// <summary>
/// https://data-star.dev/reference/attribute_plugins#data-on
/// </summary>
type internal OnEvent =
    static member private removeOnRegex = Regex("(^on-?|^data-on-?)", RegexOptions.Compiled)
    static member private removeOn str =
        match str with
        | "online" -> "online"
        | str -> OnEvent.removeOnRegex.Replace(str, "")
    static member internal Serialize (event:string, eventModifiers:OnEventModifier list) =
        let event' = (event, "") |> OnEvent.removeOnRegex.Replace
        let eventModifiers' =
            eventModifiers
            |> List.map OnEventModifier.Serialize
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> String.concat "__"
        $"data-on-{event'}{eventModifiers'}"
