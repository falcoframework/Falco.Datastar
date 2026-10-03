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
    /// Includes every signal whose path starts with the prefix, e.g. "form." matches "form.name" but not "formal"
    static member Prefix (prefix:string) =
        SignalsFilter.Include ("^" + Regex.Escape prefix)
    /// True when the filter has neither an include nor an exclude pattern. It does not allocate, unlike comparing with SignalsFilter.None
    static member internal IsNone (signalFilter:SignalsFilter) =
        signalFilter.IncludePattern = ValueNone && signalFilter.ExcludePattern = ValueNone
    /// The filter as a JavaScript object that Datastar reads, such as { include: /^form\./ }, ready to put in an attribute.
    /// A pattern is a regular expression without the slashes around it. A slash inside it is escaped for you.
    static member Serialize (signalFilter:SignalsFilter) =
        if SignalsFilter.IsNone signalFilter then
            ""
        else
            let filters = seq {
                match signalFilter.IncludePattern with
                | ValueSome pattern -> $"include: {Js.regexLiteral pattern}"
                | ValueNone -> ()
                match signalFilter.ExcludePattern with
                | ValueSome pattern -> $"exclude: {Js.regexLiteral pattern}"
                | ValueNone -> ()
                }
            Js.attrEncode ("{ " + String.Join(',', filters) + " }")

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
        // A part of the path that is not there is not an error: the signal simply has no value here.
        // A value that cannot be read as 'T is a different matter, and is left to the caller to see.
        match getSignalCore jsonDocument.RootElement signalPath with
        | ValueNone -> ValueNone
        | ValueSome element -> ValueSome (element.Deserialize<'T>())

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
    /// Triggers on exit
    | Exit
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
    /// The HTTP QUERY method. Like GET, it is safe to repeat, but it can carry a body
    | Query of url:string

type ContentType =
    /// default, filtered signals; default
    | Json
    /// sends a custom object as the request payload instead of the default, filtered signals
    | CustomJson of obj
    /// validates inputs of closest form and sends them to the backend
    | Form
    /// similar to Form, but specify the form id to send
    | SelectedForm of StarFederation.Datastar.FSharp.Selector

type Retry =
    /// retry on network errors; default
    | OnAuto
    /// retries on 4xx and 5xx responses
    | OnError
    /// retries on all non-204 responses, except redirects
    | OnAlways
    /// does not retry a response that is not 200. Datastar 1.0.4 still retries after a network error, up to RetryMaxCount times
    | OnNever
    with
    static member internal Serialize (retry:Retry) =
        match retry with
        | OnAuto -> "auto"
        | OnError -> "error"
        | OnAlways -> "always"
        | OnNever -> "never"

type RequestCancellation =
    /// cancels an earlier request with the same method and URL; default
    | Auto
    /// allows concurrent requests
    | Disabled
    /// like Auto, but also cancels the request when the element it is on is removed from the DOM
    | Cleanup
    /// A signal that holds an AbortController, e.g. (AbortController "$controller"). Its name is written as code, so it starts with '$'.
    /// https://data-star.dev/reference/actions#request-cancellation
    | AbortController of string
    with
    static member internal Serialize (requestCancellation:RequestCancellation) =
        match requestCancellation with
        | Auto -> "auto"
        | Disabled -> "disabled"
        | Cleanup -> "cleanup"
        | AbortController controller -> controller

type ResponseOverrideMode =
    | Outer
    | Inner
    | Remove
    | Replace
    | Prepend
    | Append
    | Before
    | After

module internal RequestJson =
    /// One shared instance, because creating options on every call is slow
    let options = JsonSerializerOptions(WriteIndented = false)

/// Request Options for backend action plugins
/// https://data-star.dev/reference/action_plugins
type RequestOptions = {
      /// The type of content to send. A value of json sends all signals in a JSON request.
      /// A value of form tells the action to look for the closest form to the element on which it is placed
      /// (unless a selector option is provided), perform validation on the form elements,
      /// and send them to the backend using a form request (no signals are sent). Defaults to json.
      ContentType: ContentType

      /// Filter object utilizing regular expressions for which signals to send
      FilterSignals: SignalsFilter

      /// HTTP Headers to send with the request.
      Headers: (string * string) list

      /// Whether to keep the connection open when the page is hidden. Useful for dashboards
      /// but can cause a drain on battery life and other resources when enabled.
      /// Not set by default, so Datastar chooses: false for @get and true for the other actions.
      OpenWhenHidden: bool voption

      /// Determines on what to retry; auto, error, always, never
      Retry: Retry

      /// The wait before the first retry. Datastar reads it in milliseconds, and it is sent as that. Defaults to 1 second
      RetryInterval: TimeSpan

      /// A numeric multiplier applied to scale retry wait times. Defaults to 2.
      RetryScaler: float

      /// The longest wait between retries. Sent in milliseconds. Defaults to 30 seconds.
      RetryMaxWait: TimeSpan

      /// The maximum number of retry attempts. Defaults to 10.
      RetryMaxCount: int

      /// What happens to an earlier request with the same method and URL. AbortController lets you cancel it from your own code.
      /// https://data-star.dev/reference/actions#request-cancellation
      RequestCancellation: RequestCancellation
      }
    with
    static member Defaults = RequestOptionsDefaults.Value

    static member inline With contentType = { RequestOptions.Defaults with ContentType = contentType }

    /// The options as a JavaScript object, ready to put in an attribute. Only what differs from Datastar's own defaults is written.
    /// Each option is written as a JSON value, except an AbortController, which is the name of a signal that holds one.
    static member internal Serialize (options:RequestOptions) =
        let defaults = RequestOptions.Defaults
        let written = ResizeArray<string>()
        let add (name:string) (javaScript:string) = written.Add $"\"{name}\":{javaScript}"
        let json (value:'T) = JsonSerializer.Serialize(value, RequestJson.options)

        match options.ContentType with
        | _ when options.ContentType = defaults.ContentType -> ()
        | Form -> add "contentType" (json "form")
        | SelectedForm formSelector ->
            add "contentType" (json "form")
            add "selector" (json (string formSelector))
        | CustomJson customJson ->
            add "contentType" (json "json")
            match isNull (box customJson) with
            | true -> add "payload" "null"
            | false ->
                add "payload" (JsonSerializer.SerializeToNode(customJson, JsonSerializerOptions.SignalsDefault).ToJsonString RequestJson.options)
        | Json -> add "contentType" (json "json")

        if not (SignalsFilter.IsNone options.FilterSignals) then
            // Datastar only excludes signals that start with an underscore when it is not given an exclude of its own, so a filter keeps that rule
            let excludeAlso (pattern:string) = "(^|\\.)_|(?:" + pattern + ")"
            let parts =
                [ match options.FilterSignals.IncludePattern with
                  | ValueSome pattern -> "\"include\":" + json (Js.regexString pattern)
                  | ValueNone -> ()
                  match options.FilterSignals.ExcludePattern with
                  | ValueSome pattern -> "\"exclude\":" + json (excludeAlso pattern)
                  | ValueNone -> () ]
            add "filterSignals" ("{" + String.Join(",", parts) + "}")

        if not options.Headers.IsEmpty then
            let names = HashSet<string>(StringComparer.OrdinalIgnoreCase)
            for name, _ in options.Headers do
                if not (names.Add name) then
                    raise (ArgumentException($"RequestOptions.Headers has the name \"{name}\" more than once. A request sends each header name once, so put the values in one header, separated by commas."))
            add "headers" ("{" + String.Join(",", options.Headers |> List.map (fun (name, value) -> $"{json name}:{json value}")) + "}")

        options.OpenWhenHidden
        |> ValueOption.iter (fun openWhenHidden -> add "openWhenHidden" (match openWhenHidden with | true -> "true" | false -> "false"))

        if options.Retry <> defaults.Retry then
            add "retry" (json (Retry.Serialize options.Retry))

        if options.RetryInterval <> defaults.RetryInterval then
            add "retryInterval" (json options.RetryInterval.TotalMilliseconds)

        if options.RetryScaler <> defaults.RetryScaler then
            if not (Double.IsFinite options.RetryScaler) then
                raise (ArgumentException($"RequestOptions.RetryScaler must be a finite number, but it is {Js.number options.RetryScaler}. Datastar multiplies the wait by it after every retry. Leave it at 2, or use a number such as 1.5."))
            add "retryScaler" (json options.RetryScaler)

        if options.RetryMaxWait <> defaults.RetryMaxWait then
            add "retryMaxWait" (json options.RetryMaxWait.TotalMilliseconds)

        if options.RetryMaxCount <> defaults.RetryMaxCount then
            add "retryMaxCount" (json options.RetryMaxCount)

        if options.RequestCancellation <> defaults.RequestCancellation then
            match options.RequestCancellation with
            | AbortController controller when String.IsNullOrWhiteSpace controller ->
                raise (ArgumentException("RequestOptions.RequestCancellation is AbortController without a name. Write the signal that holds the controller, for example AbortController \"$controller\", or use Auto."))
            | AbortController controller ->
                // Datastar only accepts an AbortController object, so the name is written as code and not as text.
                // It is a signal name, which is a chain of path segments, so it is checked before it goes in as code.
                Guard.signalReference "RequestOptions.RequestCancellation" controller
                add "requestCancellation" controller
            | other -> add "requestCancellation" (json (RequestCancellation.Serialize other))

        HttpUtility.HtmlEncode("{" + String.Join(",", written) + "}")

/// The one copy of RequestOptions.Defaults. A property that builds a new record is read about ten times for every request option that is written.
and internal RequestOptionsDefaults private () =
    static member val Value : RequestOptions =
        { ContentType = Json
          FilterSignals = SignalsFilter.None
          Headers = []
          OpenWhenHidden = ValueNone
          Retry = Retry.OnAuto
          RetryInterval = TimeSpan.FromSeconds(1.0)
          RetryScaler = 2.0
          RetryMaxWait = TimeSpan.FromSeconds(30.0)
          RetryMaxCount = 10
          RequestCancellation = Auto } with get

type Debounce =
    { TimeSpan:TimeSpan
      Leading:bool
      NoTrailing:bool }
    static member inline With (timeSpan:TimeSpan, ?leading:bool, ?noTrailing:bool) =
        { TimeSpan = timeSpan; Leading = (defaultArg leading false); NoTrailing = (defaultArg noTrailing false) }
    static member inline With (milliseconds:float, ?leading:bool, ?noTrailing:bool) =
        { TimeSpan = TimeSpan.FromMilliseconds(milliseconds); Leading = (defaultArg leading false); NoTrailing = (defaultArg noTrailing false) }

type Throttle =
    { TimeSpan:TimeSpan
      NoLeading:bool
      Trailing:bool }
    static member inline With (timeSpan:TimeSpan, ?noLeading:bool, ?trailing:bool) =
        { TimeSpan = timeSpan; NoLeading = (defaultArg noLeading false); Trailing = (defaultArg trailing false) }
    static member inline With (milliseconds:float, ?noLeading:bool, ?trailing:bool) =
        { TimeSpan = TimeSpan.FromMilliseconds(milliseconds); NoLeading = (defaultArg noLeading false); Trailing = (defaultArg trailing false) }

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
    /// Attaches the event listener to the document.
    | Document
    /// Triggers the event when it occurs outside the element.
    | Outside
    /// Call `preventDefault` on the event listener
    | Prevent
    /// Calls `stopPropagation` on the event listener.
    | Stop
    /// Wrap the expression in document.startViewTransition(), if View Transition API is available
    | ViewTransition

/// The casing Datastar gives to the name an attribute creates. Write it with its type name, e.g. CaseStyle.Snake
[<RequireQualifiedAccess>]
type CaseStyle =
    /// mySignal
    | Camel
    /// my-signal
    | Kebab
    /// my_signal
    | Snake
    /// MySignal
    | Pascal
    with
    static member internal Serialize (caseStyle:CaseStyle) =
        match caseStyle with
        | CaseStyle.Camel -> "camel"
        | CaseStyle.Kebab -> "kebab"
        | CaseStyle.Snake -> "snake"
        | CaseStyle.Pascal -> "pascal"

/// <summary>
/// Modifier for a DsAttr. &lt;data-...__Name.Tag.Tag=...&gt;
/// </summary>
type DsAttrModifier =
    { Name:string
      Tags:string list }
    with
    static member inline Delay (delay:TimeSpan) =
        { Name = "delay"; Tags = [ $"{delay.TotalMilliseconds}ms" ] }

    static member inline DelayMs (delay:int) =
        { Name = "delay"; Tags = [ $"{delay}ms" ] }

    static member inline DurationMs (duration:int, leading:bool) =
        { Name = "duration"
          Tags = [
              $"{duration}ms"
              if leading then "leading"
          ] }

    static member inline Throttle (throttle:Throttle) =
        { Name = "throttle"
          Tags = [
            $"{throttle.TimeSpan.TotalMilliseconds}ms"
            if throttle.NoLeading then "noleading"
            if throttle.Trailing then "trailing"
          ] }

    static member inline Debounce (debounce:Debounce) =
        { Name = "debounce"
          Tags = [
            $"{debounce.TimeSpan.TotalMilliseconds}ms"
            if debounce.Leading then "leading"
            if debounce.NoTrailing then "notrailing"
          ] }

    static member inline Threshold (threshold:int) =
        { Name = "threshold"
          Tags = [ $"{threshold}" ] }

    static member inline OnEventModifier (onEventModifier:OnEventModifier) =
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
        | Document -> { Name = "document"; Tags = [] }
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
    /// What the target of an attribute is, for the message when it cannot be used
    static member internal describeTarget (attributeName:string) =
        match attributeName with
        | "class" -> "class name"
        | "on" -> "event name"
        | "attr" -> "attribute name"
        | "style" -> "style property"
        | _ -> "name"

    static member inline start name =
        { Name = name; Target = ValueNone; Modifiers = []; Value = ValueNone; HasCaseModifier = false }

    static member inline startEvent eventName =
        { Name = $"on"; Target = ValueSome eventName; Modifiers = []; Value = ValueNone; HasCaseModifier = false }

    static member inline addTarget name dsAttr=
        { dsAttr with Target = ValueSome name }

    static member inline addSignalPathTarget (signalPath:SignalPath) =
        signalPath
        |> SignalPath.keys
        |> Seq.map SignalPath.kebabValue
        |> String.concat "."
        |> DsAttr.addTarget

    static member inline addModifier modifier  dsAttr =
        { dsAttr with Modifiers = (modifier :: dsAttr.Modifiers) }

    static member inline addModifierOption modifierOption dsAttr =
        match modifierOption with
        | ValueSome modifier -> DsAttr.addModifier modifier dsAttr
        | ValueNone -> dsAttr

    static member inline addModifierName modifierName =
        DsAttr.addModifier { Name = modifierName; Tags = [] }

    static member inline addModifierNameIf modifierName bool dsAttr =
        if bool
        then DsAttr.addModifierName modifierName dsAttr
        else dsAttr

    static member inline addValue (value:string) dsAttr =
        { dsAttr with Value = ValueSome value }

    static member generateKey dsAttr =
        // Every part of the key goes into the name of an attribute, and Falco.Markup does not escape an attribute name.
        // A quote in any of them would end the name early and could add attributes of its own.
        dsAttr.Target |> ValueOption.iter (Guard.attributeName (DsAttr.describeTarget dsAttr.Name) (not dsAttr.Modifiers.IsEmpty))
        Guard.attributeName "attribute name" false dsAttr.Name
        StringBuilder()
        |> _.Append(Constants.dataSlugPrefix) |> _.Append('-')
        |> _.Append(dsAttr.Name)
        |> (fun sb ->
            match dsAttr.Target with
            | ValueNone -> sb
            | ValueSome target -> sb.Append(':') |> _.Append(target)
            )
        |> (fun sb ->
            match dsAttr.Modifiers with
            | [] -> sb
            | modifiers ->
                for modifier in modifiers do
                    Guard.attributeName "modifier name" false modifier.Name
                    sb.Append("__") |> _.Append(modifier.Name) |> ignore
                    for tag in modifier.Tags do
                        Guard.attributeName "modifier value" false tag
                        sb.Append('.') |> _.Append(tag) |> ignore
                sb
            )
        |> _.ToString()

    static member inline create dsAttr =
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
