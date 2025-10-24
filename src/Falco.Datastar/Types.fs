namespace Falco.Datastar

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Web
open Falco.Markup
open StarFederation.Datastar.FSharp

module Constants =
    let mutable dataSlugPrefix = "data"

type SignalsFilter =
    { IncludePattern : string voption
      ExcludePattern : string voption }
    static member None = { IncludePattern = ValueNone; ExcludePattern = ValueNone }
    static member Include pattern =  { IncludePattern = ValueSome pattern; ExcludePattern = ValueNone }
    static member Exclude pattern =  { IncludePattern = ValueNone; ExcludePattern = ValueSome pattern }
    static member serialize (signalFilter:SignalsFilter) =
        if signalFilter = SignalsFilter.None then
            ""
        else
            StringBuilder()
            |> _.Append("{ ")
            |> (fun sb ->
                let _ =
                    match signalFilter.IncludePattern with
                    | ValueSome includeExp -> sb.Append($"include: /{includeExp}/")
                    | _ -> sb
                let _ =
                    match signalFilter.ExcludePattern with
                    | ValueSome excludeExp -> sb.Append($"exclude: /{excludeExp}'")
                    | _ -> sb
                sb
                )
            |> _.Append(" }")
            |> _.ToString()

module SignalsFilter =
    let sf (includePattern:string) = SignalsFilter.Include includePattern

module SignalPath =
    let sp = SignalPath.create

    let getSignalFromJson<'T> (signalPath:SignalPath) (jsonDocument:JsonDocument) =
        let getSignalCore (jsonElement:JsonElement) (signalPath:SignalPath) =
            signalPath
            |> SignalPath.keys
            |> Seq.fold (fun (currentJsonElementOpt:JsonElement voption) (key:string) ->
                currentJsonElementOpt
                |> ValueOption.bind (fun (jsonElement:JsonElement) ->
                    match jsonElement.TryGetProperty(key) with
                    | false, _ -> ValueNone
                    | true, jsonElement -> ValueSome jsonElement
                    )
                ) (ValueSome jsonElement)
        try
            getSignalCore jsonDocument.RootElement signalPath |> ValueOption.map _.Deserialize<'T>()
        with | _ -> ValueNone

    let createJsonNodeFromPathAndValue<'T> signalPath (signalValue:'T) =
        signalPath
        |> SignalPath.keys
        |> Seq.rev
        |> Seq.fold (fun json key ->
            JsonObject([ KeyValuePair<string, JsonNode> (key, json) ]) :> JsonNode
            ) (JsonValue.Create(signalValue) :> JsonNode)

module Selector =
    let sel = Selector.create

type IntersectsVisibility =
    /// Triggers when half of the element is visible
    | Half
    /// Triggers when the full element is visible
    | Full

type BackendAction =
    | Get of url:string
    | Post of url:string
    | Put of url:string
    | Patch of url:string
    | Delete of url:string

type ContentType =
    | Json
    | Form of string voption

/// Request Options for backend action plugins
/// https://data-star.dev/reference/action_plugins
type RequestOptions =
    {
      /// The type of content to send. A value of json sends all signals in a JSON request.
      /// A value of form tells the action to look for the closest form to the element on which it is placed
      /// (unless a selector option is provided), perform validation on the form elements,
      /// and send them to the backend using a form request (no signals are sent). Defaults to json.
      ContentType: ContentType

      /// Filter object utilizing regular expressions for which signals to send
      FilterSignals: SignalsFilter

      /// Specifies a form to send when the ContentType is set to Form.
      /// If set to ValueNone, the closest form is used. Defaults to ValueNone.
      Selector: Selector voption

      /// HTTP Headers to send with the request.
      Headers: (string*string) list

      /// Whether to keep the connection open when the page is hidden. Useful for dashboards
      /// but can cause a drain on battery life and other resources when enabled. Defaults to false.
      OpenWhenHidden: bool

      /// The retry interval in milliseconds. Defaults to 1 second
      RetryInterval: TimeSpan

      /// A numeric multiplier applied to scale retry wait times. Defaults to 2.
      RetryScaler: float

      /// The maximum allowable wait time in milliseconds between retries. Defaults to 30 seconds.
      RetryMaxWait: TimeSpan

      /// The maximum number of retry attempts. Defaults to 10.
      RetryMaxCount: int

      /// An AbortSignal object that can be used to cancel the request.
      /// https://developer.mozilla.org/en-US/docs/Web/API/AbortSignal
      Abort: obj }

    static member Defaults =
        { ContentType = Json
          FilterSignals = SignalsFilter.None
          Selector = ValueNone
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

        if backendActionOptions.FilterSignals <> SignalsFilter.None then
            jsonObject.Add("includeLocal", backendActionOptions.FilterSignals |> SignalsFilter.serialize |> JsonNode.Parse)

        if backendActionOptions.Selector.IsValueSome then
            let selector = backendActionOptions.Selector |> ValueOption.get
            jsonObject.Add("selector", selector)

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

type Debounce =
    { TimeSpan:TimeSpan
      Leading:bool
      NoTrail:bool }
    static member With (timeSpan:TimeSpan, ?leading:bool, ?noTrail:bool) =
        { TimeSpan = timeSpan; Leading = (defaultArg leading false); NoTrail = (defaultArg noTrail false) }
    static member With (milliseconds:int, ?leading:bool, ?noTrail:bool) =
        { TimeSpan = TimeSpan.FromMilliseconds(milliseconds); Leading = (defaultArg leading false); NoTrail = (defaultArg noTrail false) }

type Throttle =
    { TimeSpan:TimeSpan
      NoLeading:bool
      Trail:bool }
    static member With (timeSpan:TimeSpan, ?noLeading:bool, ?trail:bool) =
        { TimeSpan = timeSpan; NoLeading = (defaultArg noLeading false); Trail = (defaultArg trail false) }
    static member With (milliseconds:int, ?noLeading:bool, ?trail:bool) =
        { TimeSpan = TimeSpan.FromMilliseconds(milliseconds); NoLeading = (defaultArg noLeading false); Trail = (defaultArg trail false) }

type OnEventModifier =
    /// Trigger event once. Can only be used with the built-in events
    | Once
    /// Do not call `preventDefault` on the event listener. Can only be used with the built-in events
    | Passive
    /// Use a capture event listener. Can only be used with the built-in events
    | Capture
    /// Delay the event listener by a Timespan
    | Delay of TimeSpan
    /// Delay the event listener by milliseconds
    | DelayMs of int
    /// Debounce the event listener; new events after an initial event, within a TimeSpan, are ignored.
    | Debounce of Debounce
    /// Throttle the event listener; only fires the last event within a TimeSpan.
    | Throttle of Throttle
    /// Attaches the event listener to the window element.
    | Window
    /// Triggers the event when it occurs outside the element.
    | Outside
    /// Call `preventDefault` on the event listener
    | Prevent
    /// Calls `stopPropagation` on the event listener.
    | Stop
    /// Wrap the expression in document.startViewTransition(), if View Transition API is available
    | ViewTransition

/// <summary>
/// Modifier for a DsAttr. &lt;data-...__Name.Tag.Tag=...&gt;
/// </summary>
type DsAttrModifier =
    { Name:string
      Tags:string list }
    with
    static member Delay (delay:TimeSpan) =
        { Name = "delay"; Tags = [ $"{delay.TotalMilliseconds}ms" ] }

    static member DelayMs (delay:int) =
        { Name = "delay"; Tags = [ $"{delay}ms" ] }

    static member DurationMs (duration:int, leading:bool) =
        { Name = "duration"
          Tags = [
              $"{duration}ms"
              if leading then "leading"
          ] }

    static member Throttle (throttle:Throttle) =
        { Name = "throttle"
          Tags = [
            $"{throttle.TimeSpan.TotalMilliseconds}ms"
            if throttle.NoLeading then "noleading"
            if throttle.Trail then "trail"
          ] }

    static member Debounce (debounce:Debounce) =
        { Name = "debounce"
          Tags = [
            $"{debounce.TimeSpan.TotalMilliseconds}ms"
            if debounce.Leading then "leading"
            if debounce.NoTrail then "notrail"
          ] }

    static member OnEventModifier (onEventModifier:OnEventModifier) =
        match onEventModifier with
        | Once -> { Name = "once"; Tags = [] }
        | Passive -> { Name = "passive"; Tags = [] }
        | Capture -> { Name = "capture"; Tags = [] }
        | Delay delay -> (DsAttrModifier.Delay delay)
        | DelayMs ms -> { Name = "delay"; Tags = [ $"{ms}ms" ] }
        | Debounce debounce -> (DsAttrModifier.Debounce debounce)
        | Throttle throttle -> (DsAttrModifier.Throttle throttle)
        | ViewTransition -> { Name = "viewtransition"; Tags = [] }
        | Window -> { Name = "window"; Tags = [] }
        | Outside -> { Name = "outside"; Tags = [] }
        | Prevent -> { Name = "prevent"; Tags = [] }
        | Stop -> { Name = "stop"; Tags = [] }

/// <summary>
/// &lt;data-Name-Target__Modifiers="Value"&gt;
/// </summary>
type DsAttr =
    { Name:string
      Target:string voption
      Modifiers:DsAttrModifier list
      HasCaseModifier:bool
      Value:string voption }
    with
    static member private removeOnRegex = Regex("(^on-?|^data-on-?)", RegexOptions.Compiled)

    static member inline start name =
        { Name = name; Target = ValueNone; Modifiers = []; Value = ValueNone; HasCaseModifier = false }

    static member startEvent eventName =
        let removeOn str =
            match str with
            | "online" -> "online"
            | str -> DsAttr.removeOnRegex.Replace(str, "")
        { Name = $"on-{(removeOn eventName)}"; Target = ValueNone; Modifiers = []; Value = ValueNone; HasCaseModifier = false }

    static member inline addTarget name dsAttr=
        { dsAttr with Target = ValueSome name }

    static member addSignalPathTarget (signalPath:SignalPath) =
        signalPath
        |> SignalPath.keys
        |> Seq.map SignalPath.kebabValue
        |> String.concat "."
        |> DsAttr.addTarget

    static member inline addModifier modifier  dsAttr =
        { dsAttr with Modifiers = (modifier :: dsAttr.Modifiers) }

    static member inline addModifierOption modifierOption dsAttr =
        match modifierOption with
        | Some modifier -> DsAttr.addModifier modifier dsAttr
        | None -> dsAttr

    static member inline addModifierName modifierName =
        DsAttr.addModifier { Name = modifierName; Tags = [] }

    static member inline addModifierNameIf modifierName bool dsAttr =
        if bool
        then DsAttr.addModifierName modifierName dsAttr
        else dsAttr

    static member inline addValue (value:string) dsAttr =
        { dsAttr with Value = ValueSome value }

    static member inline generateKey dsAttr =
        StringBuilder()
        |> _.Append(Constants.dataSlugPrefix) |> _.Append('-')
        |> _.Append(dsAttr.Name)
        |> (fun sb ->
            match dsAttr.Target with
            | ValueNone -> sb
            | ValueSome target -> sb.Append('-') |> _.Append(target)
            )
        |> (fun sb ->
            match dsAttr.Modifiers with
            | [] -> sb
            | modifiers ->
                for modifier in modifiers do
                    sb.Append("__") |> _.Append(modifier.Name) |> ignore
                    for tag in modifier.Tags do
                        sb.Append('.') |> _.Append(tag) |> ignore
                sb
            )
        |> _.ToString()

    static member create dsAttr =
        let dsAttrKey = dsAttr |> DsAttr.generateKey
        match dsAttr.Value with
        | ValueSome value -> Attr.create dsAttrKey value
        | ValueNone -> Attr.createBool dsAttrKey

    static member inline create (name, ?targetName, ?value, ?hasCaseModifier) =
        { Name = name
          Target = targetName |> Option.toValueOption
          Modifiers = []
          Value = value |> Option.toValueOption
          HasCaseModifier = (defaultArg hasCaseModifier false) }
        |> DsAttr.create

    static member inline createSp (name, signalPath, ?value, ?hasCaseModifier) =
        { Name = name
          Target =
              signalPath
              |> SignalPath.kebabValue
              |> ValueSome
          Modifiers = []
          Value = value |> Option.toValueOption
          HasCaseModifier = (defaultArg hasCaseModifier false) }
        |> DsAttr.create

