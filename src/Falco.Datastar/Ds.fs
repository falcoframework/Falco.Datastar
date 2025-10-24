namespace Falco.Datastar

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Web
open Falco.Markup
open StarFederation.Datastar.FSharp

[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Ds =
    static member cdnSrc =
        @"https://cdn.jsdelivr.net/gh/starfederation/datastar@1.0.0-RC.5/bundles/datastar.js"

    /// <summary>
    /// Shorthand for `Elem.script [ Attr.type' "module"; Attr.src cdnSrc ] []`
    /// </summary>
    /// <returns>Attribute</returns>
    static member cdnScript =
        Elem.script [ Attr.type' "module"; Attr.src Ds.cdnSrc ] []

    /// <summary>
    /// Patches a signal into the existing signals with the given value.
    /// Has an optional ifMissing flag. https://data-star.dev/reference/attributes#data-signals
    /// </summary>
    /// <param name="signalPath">The path to add. Prefix with underscore to keep the signal local to the browser, and not returned in a @get, @post, etc</param>
    /// <param name="signalValue">The initial value to set the signal</param>
    /// <param name="ifMissing">Signal is only merged if it doesn't already exist</param>
    /// <returns>Attribute</returns>
    static member inline signal<'T> (signalPath:SignalPath, signalValue:'T, ?ifMissing) =
        let zz = JsonSerializerOptions()
        DsAttr.start "signals"
        |> DsAttr.addSignalPathTarget signalPath
        |> DsAttr.addModifierNameIf "ifmissing" (defaultArg ifMissing false)
        |> DsAttr.addValue (
            match typeof<'T> with
            | t when t = typeof<string> -> "'" + signalValue.ToString() + "'"
            | _ -> JsonValue.Create<'T>(signalValue).ToJsonString())
        |> DsAttr.create

    /// <summary>
    /// Patches one or more signals into the existing signals.
    /// https://data-star.dev/reference/attributes#data-signals
    /// </summary>
    /// <param name="signals">An object that will be serialized via System.Text.JsonSerializer.Serialize()
    /// Prefix signal paths with underscore to keep the signal local t the browser</param>
    /// <param name="ifMissing">Signals are only merged if it doesn't already exist</param>
    /// <param name="options">Optional options to be passed to the JSON serializer</param>
    /// <returns>Attribute</returns>
    static member signals (signals, ?ifMissing, ?options:JsonSerializerOptions) =
        let options' = defaultArg options JsonSerializerOptions.SignalsDefault
        DsAttr.start "signals"
        |> DsAttr.addModifierNameIf "ifmissing" (defaultArg ifMissing false)
        |> DsAttr.addValue (HttpUtility.HtmlEncode(JsonSerializer.Serialize (signals, options')))
        |> DsAttr.create

    /// <summary>
    /// Bind an element's attribute value to an expression.
    /// https://data-star.dev/reference/attributes#data-attr
    /// </summary>
    /// <param name="attributeName">An HTML element attribute</param>
    /// <param name="expression">Expression to be evaluated and assigned to the attribute, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member attr' (attributeName, expression) =
        DsAttr.create ("attr", targetName = attributeName, value = expression)

    /// <summary>
    /// Binds a signal to an element's value. Can be added to any element on which data can be input.
    /// input, textarea, select, checkbox, radio, and web components.
    /// https://data-star.dev/reference/attributes#data-bind
    /// </summary>
    /// <param name="signalPath">The signal to bind to</param>
    /// <returns>Attribute</returns>
    static member bind signalPath =
        DsAttr.createSp ("bind", signalPath)

    /// <summary>
    /// Adds or removes a class from the element based on an expression.
    /// https://data-star.dev/reference/attributes#data-class
    /// </summary>
    /// <param name="className">Name of the class to add or remove</param>
    /// <param name="boolExpression">Expression to evaluate; if true, then the class is added; otherwise, removed. https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member class' (className, boolExpression) =
        DsAttr.start "class"
        |> DsAttr.addTarget className
        |> DsAttr.addValue boolExpression
        |> DsAttr.create

    /// <summary>
    /// Sets the value of the inline CSS styles on an element based on an expression
    /// </summary>
    /// <param name="styleProperty">The style to set, https://www.w3schools.com/cssref/index.php</param>
    /// <param name="propertyValueExpression">Expression to be evaluated and assigned to the style property, https://data-star.dev/guide/datastar_expressions</param>
    static member style (styleProperty, propertyValueExpression) =
        DsAttr.create ("style", targetName = styleProperty, value = propertyValueExpression)

    /// <summary>
    /// Bind the content text of the element to an expression.
    /// https://data-star.dev/reference/attributes#data-text
    /// </summary>
    /// <param name="expression">Expression to be evaluated, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member text expression =
        DsAttr.create ("text", value = expression)

    /// <summary>
    /// Creates a readonly signal that is computed based on an expression.
    /// The signalPath must not be used as for performing actions; use Ds.effect instead.
    /// https://data-star.dev/reference/attributes#data-computed
    /// </summary>
    /// <param name="signalPath">Name of signal to contain the expression</param>
    /// <param name="expression">Expression to be evaluated, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member computed (signalPath, expression) =
        DsAttr.start "computed"
        |> DsAttr.addSignalPathTarget signalPath
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Create a signal that refers to the HTML element it is assigned to; after a data-ref is created, you can access attributes of the element.
    /// e.g. data-on-click="$signalRefName.value='newValue'".
    /// Note: that if an element's attribute changes, the expressions containing this signal will not fire.
    /// https://data-star.dev/reference/attributes#data-ref
    /// </summary>
    /// <param name="signalPath">Name of signal to contain the HTML element</param>
    /// <returns>Attribute</returns>
    static member ref signalPath =
        DsAttr.start "ref"
        |> DsAttr.addValue signalPath
        |> DsAttr.create

    /// <summary>
    /// Show or hides an element based on an expressions "true-ness".
    /// https://data-star.dev/reference/attributes#data-show
    /// </summary>
    /// <param name="boolExpression">The expression that will be evaluated; if true = the element is visible, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member show boolExpression =
        DsAttr.create ("show", value = boolExpression)

    /// <summary>
    /// Execute an expression on page load and whenever any signals in the expression change.
    /// </summary>
    /// <param name="expression">The expression to fire</param>
    /// <returns>Attribute</returns>
    static member effect (expression:string) =
        DsAttr.create ("effect", value = expression)

    /// <summary>
    /// This will create a signal and set its value to `true` while a server request is in flight, otherwise `false`.
    /// Place this in the same element as a Ds.get, Ds.post, etc
    /// https://data-star.dev/reference/attributes#data-indicator
    /// </summary>
    /// <param name="signalPath">The name of the signal to create</param>
    /// <returns>Attribute</returns>
    static member indicator signalPath =
        DsAttr.start "indicator"
        |> DsAttr.addSignalPathTarget signalPath
        |> DsAttr.create

    /// <summary>
    /// Preserves the value of an attribute when morphing DOM elements.
    /// https://data-star.dev/reference/attributes#data-preserve-attr
    /// </summary>
    /// <param name="attributeName">Space delimited list of attributes you want retained on patch elements</param>
    static member preserveAttr attributeName =
        DsAttr.start "preserve-attr"
        |> DsAttr.addValue attributeName
        |> DsAttr.create

    /// <summary>
    /// Datastar walks the entire DOM and applies plugins to each element it encounters.
    /// It’s possible to tell Datastar to ignore an element and its descendants by placing a data-star-ignore attribute on it.
    /// This can be useful for preventing naming conflicts with third-party libraries.
    /// https://data-star.dev/reference/attributes#data-ignore
    /// </summary>
    /// <returns>Attribute</returns>
    static member ignore =
        DsAttr.create "ignore"

    /// <summary>
    /// Datastar walks the entire DOM and applies plugins to each element it encounters.
    /// It’s possible to tell Datastar to ignore an element and its descendants by placing a data-star-ignore attribute on it.
    /// This can be useful for preventing naming conflicts with third-party libraries.
    /// This only ignores the element it is attached to.
    /// https://data-star.dev/reference/attributes#data-ignore
    /// </summary>
    /// <returns>Attribute</returns>
    static member ignoreThis =
        DsAttr.start "ignore"
        |> DsAttr.addModifier { Name="self"; Tags = [] }
        |> DsAttr.create

    /// <summary>
    /// Similar to the Ds.ignore, the data-ignore-morph attribute tells the PatchElements watcher to skip processing an element and its children when morphing elements.
    /// https://data-star.dev/reference/attributes#data-ignore-morph
    /// </summary>
    /// <returns>Attribute</returns>
    static member ignoreMorph =
        DsAttr.create "ignore-morph"

    /// <summary>
    /// Sets the text content of an element to a reactive JSON stringified version of signals. Useful for troubleshooting.
    /// https://data-star.dev/reference/attributes#data-json-signals
    /// </summary>
    static member jsonSignals =
        DsAttr.create "json-signals"

    /// <summary>
    /// Sets the text content of an element to a reactive JSON stringified version of signals. Useful for troubleshooting.
    /// https://data-star.dev/reference/attributes#data-json-signals
    /// </summary>
    /// <param name="signalsFilter">Regex of signal paths to be included and excluded</param>
    /// <param name="terse">Single line output</param>
    static member jsonSignalsOptions (?signalsFilter:SignalsFilter, ?terse:bool) =
        let addSignalsFilter signalsFilter dsAttr =
            if signalsFilter = SignalsFilter.None
            then dsAttr |> DsAttr.addValue (signalsFilter |> SignalsFilter.serialize)
            else dsAttr
        DsAttr.start "json-signals"
        |> DsAttr.addModifierNameIf "terse" (defaultArg terse false)
        |> addSignalsFilter (defaultArg signalsFilter SignalsFilter.None)
        |> DsAttr.create

    /// <summary>
    /// Attaches an event listener to an element, executing the expression whenever the event is triggered.
    /// https://data-star.dev/reference/attributes#data-on
    /// </summary>
    /// <param name="eventName">The event to listen to, i.e. https://developer.mozilla.org/en-US/docs/Web/Events</param>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="eventModifiers">To modify the behavior of the event</param>
    /// <returns>Attribute</returns>
    static member onEvent (eventName, expression, ?eventModifiers:OnEventModifier list) =
        DsAttr.startEvent eventName
        |> (fun dsAttr ->  // event modifiers
            (defaultArg eventModifiers [])
            |> List.fold (fun dsAttr eventModifier -> DsAttr.addModifier (DsAttrModifier.OnEventModifier eventModifier) dsAttr) dsAttr
            )
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Adds an on-click listener to the element and executes the expression.
    /// https://data-star.dev/reference/attributes#data-on
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="eventModifiers">To modify the behavior of the event</param>
    /// <returns>Attribute</returns>
    static member onClick (expression, ?eventModifiers) =
        Ds.onEvent ("click", expression, ?eventModifiers = eventModifiers)

    /// <summary>
    /// Fires the expression when the element is loaded.
    /// https://data-star.dev/reference/attributes#data-on-load
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="delayMs">The time to wait before executing the expression in milliseconds; default = 0</param>
    /// <param name="viewTransition">Wrap expression in document.startViewTransition(); default = false</param>
    /// <returns>Attribute</returns>
    static member onLoad (expression, ?delayMs, ?viewTransition) =
        DsAttr.start "on-load"
        |> DsAttr.addModifierOption (delayMs |> Option.map DsAttrModifier.DelayMs)
        |> DsAttr.addModifierNameIf "viewtransition" (defaultArg viewTransition false)
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Evaluates the expression on a steady interval
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="intervalMs">The time between each evaluation; default = 1000</param>
    /// <param name="leading">Execute the first interval immediately; default = false</param>
    /// <param name="viewTransition">Wrap expression in document.startViewTransition(); default = false</param>
    /// <returns>Attribute</returns>
    static member onInterval (expression, intervalMs, ?leading, ?viewTransition) =
        DsAttr.start "on-interval"
        |> DsAttr.addModifierNameIf "viewtransition" (defaultArg viewTransition false)
        |> DsAttr.addModifier (DsAttrModifier.DurationMs (intervalMs, (defaultArg leading false)))
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Fires the expression when a signal is changed. Filter using Ds.filterOnSignalPatch
    /// hhttps://data-star.dev/reference/attributes#data-on-signal-patch
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="delayMs">The time to wait before executing the expression in milliseconds; default = 0</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    /// <returns>Attribute</returns>
    static member onSignalPatch (expression, ?delayMs:int, ?debounce:Debounce, ?throttle:Throttle) =
        DsAttr.start "on-signal-patch"
        |> DsAttr.addModifierOption (delayMs |> Option.map DsAttrModifier.DelayMs)
        |> DsAttr.addModifierOption (debounce |> Option.map DsAttrModifier.Debounce)
        |> DsAttr.addModifierOption (throttle |> Option.map DsAttrModifier.Throttle)
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Filter the signals that cause Ds.onSignalPatch to fire
    /// https://data-star.dev/reference/attributes#data-on-signal-patch-filter
    /// </summary>
    /// <param name="signalsFilter">Regex of signal paths to be included and excluded</param>
    /// <returns>Attribute</returns>
    static member filterOnSignalPatch (signalsFilter:SignalsFilter) =
        DsAttr.start "on-signal-patch-filter"
        |> DsAttr.addValue (signalsFilter |> SignalsFilter.serialize)
        |> DsAttr.create

    /// <summary>
    /// Runs an expression when the element intersects with the viewport.
    /// https://data-star.dev/reference/attributes#data-on-intersect
    /// </summary>
    /// <param name="expression">Expression to run when element is intersected</param>
    /// <param name="visibility">Sets it to trigger only if the element is half or fully viewed</param>
    /// <param name="onlyOnce">Only triggers the event once</param>
    /// <param name="delayMs">The time to wait before executing the expression in milliseconds; default = 0</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    /// <param name="viewTransition">Wrap expression in document.startViewTransition(); default = false</param>
    /// <returns>Attribute</returns>
    static member onIntersect (expression, ?visibility, ?onlyOnce, ?delayMs:int, ?debounce:Debounce, ?throttle:Throttle, ?viewTransition:bool) =
        DsAttr.start "on-intersect"
        |> (fun dsAttr ->
            match visibility with
            | Some vis when vis = IntersectsVisibility.Full -> DsAttr.addModifierName "full" dsAttr
            | Some vis when vis = IntersectsVisibility.Half -> DsAttr.addModifierName "half" dsAttr
            | _ -> dsAttr
            )
        |> DsAttr.addModifierNameIf "once" (defaultArg onlyOnce false)
        |> DsAttr.addModifierNameIf "viewtransition" (defaultArg viewTransition false)
        |> DsAttr.addModifierOption (delayMs |> Option.map DsAttrModifier.DelayMs)
        |> DsAttr.addModifierOption (debounce |> Option.map DsAttrModifier.Debounce)
        |> DsAttr.addModifierOption (throttle |> Option.map DsAttrModifier.Throttle)
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Actions
    /// </summary>
    static member private backendAction actionOptions action =
        match (action, actionOptions) with
        | Get url, None -> $@"@get('{url}')"
        | Get url, Some options -> $"@get('{url}','{options |> RequestOptions.Serialize}')"
        | Post url, None -> $@"@post('{url}')"
        | Post url, Some options -> $"@post('{url}','{options |> RequestOptions.Serialize}')"
        | Put url, None -> $@"@put('{url}')"
        | Put url, Some options -> $"@put('{url}','{options |> RequestOptions.Serialize}')"
        | Patch url, None -> $@"@patch('{url}')"
        | Patch url, Some options -> $"@patch('{url}','{options |> RequestOptions.Serialize}')"
        | Delete url, None -> $@"@delete('{url}')"
        | Delete url, Some options -> $"@delete('{url}','{options |> RequestOptions.Serialize}')"

    /// <summary>
    /// Creates a @get action for an expression with options. The action sends a GET request with the given url.
    /// Signals will be sent as a query parameter.
    /// https://data-star.dev/reference/actions#get
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member get (url, ?options) =
        Ds.backendAction options (Get url)

    /// <summary>
    /// Creates a @post action for an expression. The action sends a POST request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#post
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member post (url, ?options) =
        Ds.backendAction options (Post url)

    /// <summary>
    /// Creates a @put action for an expression. The action sends a PUT request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#put
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member put (url, ?options) =
        Ds.backendAction options (Put url)

    /// <summary>
    /// Creates a @patch action for an expression. The action sends a PATCH request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#patch
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member patch (url, ?options) =
        Ds.backendAction options (Patch url)

    /// <summary>
    /// Creates a @delete action for an expression. The action sends a DELETE request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#delete
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member delete (url, ?options) =
        Ds.backendAction options (Delete url)

    /// <summary>
    /// @setall(), set all the signals that start with the prefix to the expression provided.
    /// https://data-star.dev/reference/actions#setall
    /// </summary>
    /// <param name="signalsPathPrefix">All signals to set that have this prefix, e.g. 'foo.'</param>
    /// <param name="value">Value to set</param>
    /// <returns>Expression</returns>
    static member inline setAll<'T> (signalsPathPrefix:string, value:'T) =
        match box value with
        | :? Boolean as value' -> $"@setAll('{signalsPathPrefix}', {value'.ToString().ToLower()})"
        | :? string as value' -> $"@setAll('{signalsPathPrefix}', '{value'}')"
        | value' -> $"@setAll('{signalsPathPrefix}', '{value'}')"

    /// <summary>
    /// @toggleAll(), toggle all the signals that start with the prefix.
    /// https://data-star.dev/reference/actions#toggleall
    /// </summary>
    /// <param name="signalsPathPrefix">All signals to toggle that have this prefix, e.g. 'foo.'</param>
    /// <returns>Expression</returns>
    static member toggleAll (signalsPathPrefix:string) =
        $"@toggleAll('{signalsPathPrefix}')"

    /// <summary>
    /// Method for joining strings with " ; " to simplify multi-line expressions
    /// </summary>
    /// <param name="expressions"></param>
    static member expression (expressions:string seq) =
        expressions |> String.concat " ; "

    /// <summary>
    /// An attribute that should be added to the &lt;body&gt; when creating a streaming app to avoid the issue explained here:
    /// https://stackoverflow.com/questions/8788802/prevent-safari-loading-from-cache-when-back-button-is-clicked
    /// </summary>
    static member safariStreamingFix =
        Attr.create "data-on-pageshow.window" "evt?.persisted && window.location.reload()"
