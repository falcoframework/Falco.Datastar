namespace Falco.Datastar

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open StarFederation.Datastar

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

    static member defaults =
        { ContentType = Json
          IncludeLocal = false
          Headers = []
          OpenWhenHidden = false
          RetryInterval = TimeSpan.FromSeconds((float)1)
          RetryScaler = 2.0
          RetryMaxWait = TimeSpan.FromSeconds((float)30)
          RetryMaxCount = 10
          Abort = null }

    static member serialized (backendActionOptions:RequestOptions) =
        let jsonObject = JsonObject()
        match backendActionOptions.ContentType with
        | _ when backendActionOptions.ContentType = RequestOptions.defaults.ContentType -> ()
        | Json -> jsonObject.Add("contentType", "json")
        | Form formSelector ->
            jsonObject.Add("contentType", "form")
            match formSelector with
            | ValueNone -> ()
            | ValueSome formSelector' -> jsonObject.Add("selector", formSelector')

        if backendActionOptions.IncludeLocal <> RequestOptions.defaults.IncludeLocal then
            jsonObject.Add("includeLocal", backendActionOptions.IncludeLocal.ToString().ToLower())

        if backendActionOptions.Headers.Length > 0 then
            let headerObject = JsonObject()
            backendActionOptions.Headers |> List.iter headerObject.Add
            jsonObject.Add("headers", headerObject)

        if backendActionOptions.OpenWhenHidden <> RequestOptions.defaults.OpenWhenHidden then
            jsonObject.Add("openWhenHidden", backendActionOptions.OpenWhenHidden.ToString().ToLower())

        if backendActionOptions.RetryInterval <> RequestOptions.defaults.RetryInterval then
            jsonObject.Add("retryInterval", backendActionOptions.RetryInterval.Milliseconds)

        if backendActionOptions.RetryScaler <> RequestOptions.defaults.RetryScaler then
            jsonObject.Add("retryScaler", backendActionOptions.RetryScaler)

        if backendActionOptions.RetryMaxWait <> RequestOptions.defaults.RetryMaxWait then
            jsonObject.Add("retryMaxWaitMs", backendActionOptions.RetryMaxWait.Milliseconds)

        if backendActionOptions.RetryMaxCount <> RequestOptions.defaults.RetryMaxCount then
            jsonObject.Add("retryMaxCount", backendActionOptions.RetryMaxCount)

        if backendActionOptions.Abort <> null then
            jsonObject.Add("abort", JsonSerializer.Serialize backendActionOptions.Abort)

        jsonObject.ToString()

type Debounce private (timeSpan:TimeSpan, leading:bool, noTrail:bool) =
    member _.serialize =
        seq {
            $"debounce.{timeSpan.Milliseconds}ms"
            if leading then "leading"
            if noTrail then "notrail"
        } |> String.concat "."
    static member With (timeSpan:TimeSpan, ?leading:bool, ?noTrail:bool) =
        let leading = defaultArg leading false
        let noTrail = defaultArg noTrail false
        OnEventModifier.Debounce (Debounce (timeSpan, leading, noTrail))

and Throttle private (timeSpan:TimeSpan, noLeading:bool, trail:bool) =
    member _.serialize =
        seq {
            $"throttle.{timeSpan.Milliseconds}ms"
            if noLeading then "noleading"
            if trail then "trail"
        } |> String.concat "."
    static member With (timeSpan:TimeSpan, ?noLeading:bool, ?trail:bool) =
        let noLeading = defaultArg noLeading false
        let trail = defaultArg trail false
        OnEventModifier.Throttle (Throttle (timeSpan, noLeading, trail))

and Duration private (timeSpan:TimeSpan, leading:bool) =
    member _.serialize =
        seq {
            $"duration.{timeSpan.Milliseconds}ms"
            if leading then "leading"
        } |> String.concat "."
    static member With (timeSpan, ?leading) =
        let leading = defaultArg leading false
        OnEventModifier.Duration (Duration (timeSpan, leading))
    static member Default = Duration.With(TimeSpan.FromSeconds 1)

and OnEventModifier =
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
    /// Delay the event listener before firing.
    /// </summary>
    | Delay of TimeSpan
    /// <summary>
    /// Debounce the event listener; new events after an initial event, within a TimeSpan, are ignored.
    /// </summary>
    | Debounce of Debounce
    /// <summary>
    /// Throttle the event listener; only fires the last event within a TimeSpan.
    /// </summary>
    | Throttle of Throttle
    /// <summary>
    /// Sets the interval duration for `data-on-interval`
    /// </summary>
    | Duration of Duration
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
    static member internal serialize eventModifiers =
        match eventModifiers with
        | Once -> "once"
        | Passive -> "passive"
        | Capture -> "capture"
        | Delay timeSpan -> $"delay.{timeSpan.Milliseconds}ms"
        | Debounce debounce -> debounce.serialize
        | Throttle throttle -> throttle.serialize
        | Duration duration -> duration.serialize
        | ViewTransition -> "viewtransition"
        | Window -> "window"
        | Outside -> "outside"
        | Prevent -> "prevent"
        | Stop -> "stop"

/// <summary>
/// https://data-star.dev/reference/attribute_plugins#data-on
/// </summary>
type OnEvent =
    /// <summary>
    /// Triggered when the element is clicked
    /// </summary>
    | Click
    /// <summary>
    /// Triggered when the page loads
    /// </summary>
    | Load
    /// <summary>
    /// Triggered every 1 second; can be modified with Duration.With(TimeSpan.FromSeconds _)
    /// </summary>
    | Interval
    /// <summary>
    /// Triggered on every requestAnimationFrame event. https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame
    /// </summary>
    | RequestAnimationFrame
    /// <summary>
    /// Triggered when any signal changes
    /// </summary>
    | SignalsChanged
    /// <summary>
    /// Triggered when a specific signal changes
    /// </summary>
    | SignalChanged of signalPath:SignalPath
    /// <summary>
    /// A custom event; https://developer.mozilla.org/en-US/docs/Web/Events
    /// </summary>
    | Other of string
    with
    static member private removeOn str = Regex.Replace(str, "^on-?", "")

    static member internal serialize event =
        match event with
        | Click -> $"{Consts.dataSlugPrefix}-on-click"
        | Load -> $"{Consts.dataSlugPrefix}-on-load"
        | Interval -> $"{Consts.dataSlugPrefix}-on-interval"
        | RequestAnimationFrame -> $"{Consts.dataSlugPrefix}-on-raf"
        | SignalsChanged -> $"{Consts.dataSlugPrefix}-on-signals-change"
        | SignalChanged signalPath -> $"{Consts.dataSlugPrefix}-on-signals-change-{signalPath |> SignalPath.value |> _.ToKebab()}"
        | Other event -> $"{Consts.dataSlugPrefix}-on-{event |> _.ToKebab() |> OnEvent.removeOn}"

    static member internal serializeWithModifiers (eventModifiers:OnEventModifier list) (event:OnEvent) =
        (OnEvent.serialize event) + (Utility.slugifyModifiers OnEventModifier.serialize "__" eventModifiers)
