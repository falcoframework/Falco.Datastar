namespace Falco.Datastar

open System
open System.Numerics
open System.Text.Json
open System.Text.Json.Nodes
open System.Web
open Falco.Markup
open StarFederation.Datastar

[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Ds =
    static member cdnSrc = $@"https://cdn.jsdelivr.net/gh/starfederation/datastar@v{Consts.Version}/bundles/datastar.js"

    /// <summary>
    /// Shorthand for `Elem.script [ Attr.type' "module"; Attr.src cdnSrc ] []`
    /// </summary>
    /// <returns>Attribute</returns>
    static member cdnScript =
        Elem.script [ Attr.type' "module"; Attr.src Ds.cdnSrc ] []

    /// <summary>
    /// Merges a signal into the existing signals with the given value.
    /// Has an optional ifMissing flag. https://data-star.dev/reference/attribute_plugins#modifiers
    /// https://data-star.dev/reference/attribute_plugins#data-signals
    /// </summary>
    /// <param name="signalPath">The path to add; kebab-case will be converted to pascal-case on return.
    /// Prefix with underscore to keep the signal local to the browser</param>
    /// <param name="signalValue">The initial value to set the signal</param>
    /// <param name="ifMissing">Signal is only merged if it doesn't already exist</param>
    /// <returns>Attribute</returns>
    static member signal<'T> (signalPath:SignalPath, signalValue:'T, ?ifMissing) =
        let ifMissing' = defaultArg ifMissing false |> Bool.eitherOr "__ifmissing" ""
        let signalValue' = JsonValue.Create(signalValue).ToJsonString()
        Attr.createBool $"{Consts.dataSlugPrefix}-signals-{signalPath |> SignalPath.kebabValue}{ifMissing'}='{signalValue'}'"

    /// <summary>
    /// Merges one or more signals into the existing signals.
    /// https://data-star.dev/reference/attribute_plugins#data-signals
    /// </summary>
    /// <param name="signals">An object that will be serialized via System.Text.JsonSerializer.Serialize()
    /// Prefix signal paths with underscore to keep the signal local to the browser</param>
    /// <param name="ifMissing">Signals are only merged if it doesn't already exist</param>
    /// <param name="options">Optional options to be passed to the serializer</param>
    /// <returns>Attribute</returns>
    static member signals (signals, ?ifMissing, ?options:JsonSerializerOptions) =
        let ifMissing' = defaultArg ifMissing false |> Bool.eitherOr "__ifmissing" ""
        let options = defaultArg options JsonSerializerOptions.Default
        Attr.create $"{Consts.dataSlugPrefix}-signals{ifMissing'}" (HttpUtility.HtmlEncode (JsonSerializer.Serialize (signals, options)))

    /// <summary>
    /// Bind an HTML attribute's value to an expression.
    /// https://data-star.dev/reference/attribute_plugins#data-attr
    /// </summary>
    /// <param name="attributeName">An HTML element attribute</param>
    /// <param name="expression">Expression to be evaluated, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member attr' (attributeName:string, expression) =
        Attr.create $"{Consts.dataSlugPrefix}-attr-{attributeName}" expression

    /// <summary>
    /// Binds a signal to an element's value. Can be added to any element on which data can be input.
    /// input, textarea, select, checkbox, radio, and web components.
    /// https://data-star.dev/reference/attribute_plugins#data-bind
    /// </summary>
    /// <param name="signalPath">The signal to bind to</param>
    /// <returns>Attribute</returns>
    static member bind (signalPath:SignalPath) =
        Attr.createBool $"{Consts.dataSlugPrefix}-bind-{signalPath |> SignalPath.kebabValue}"

    /// <summary>
    /// Adds or removes a class from the element based on an expression.
    /// https://data-star.dev/reference/attribute_plugins#data-class
    /// </summary>
    /// <param name="className">Name of the class to add or remove</param>
    /// <param name="boolExpression">Expression to evaluate; if true, then the class is added; otherwise, removed. https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member class' (className, boolExpression) =
        Attr.create $"{Consts.dataSlugPrefix}-class-{className}" boolExpression

    /// <summary>
    /// Bind the content text of the element to an expression.
    /// https://data-star.dev/reference/attribute_plugins#data-text
    /// </summary>
    /// <param name="expression">Expression to be evaluated, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member text expression =
        Attr.create $"{Consts.dataSlugPrefix}-text" expression

    /// <summary>
    /// Create a signal that is computed from an expression of other signals.
    /// https://data-star.dev/reference/attribute_plugins#data-computed
    /// </summary>
    /// <param name="signalPath">Name of signal to contain the expression</param>
    /// <param name="expression">Expression to be evaluated, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member computed (signalPath, expression) =
        Attr.create $"{Consts.dataSlugPrefix}-computed-{signalPath |> SignalPath.kebabValue}" expression

    /// <summary>
    /// Create a signal that refers to the HTML element it is assigned to; after a data-ref is created, you can access attributes of the element.
    /// e.g. data-on-click="$signalRefName.value='newValue'".
    /// Note: that if an element's attribute changes, the expressions containing this signal will not fire.
    /// https://data-star.dev/reference/attribute_plugins#data-ref
    /// </summary>
    /// <param name="signalPath">Name of signal to contain the HTML element</param>
    /// <returns>Attribute</returns>
    static member ref signalPath =
        Attr.create $"{Consts.dataSlugPrefix}-ref" (signalPath |> SignalPath.value)

    /// <summary>
    /// Persists signals in storage. Useful for storing values between page loads
    /// https://data-star.dev/reference/attribute_plugins#data-persist
    /// </summary>
    /// <param name="inSession">Store the signals in session storage; default = local</param>
    /// <returns>Attribute</returns>
    static member persistAllSignals ?inSession =
        let inSession' = defaultArg inSession false |> Bool.eitherOr "__session" ""
        Attr.createBool $"{Consts.dataSlugPrefix}-persist{inSession'}"

    /// <summary>
    /// Persists signals in storage. Useful for storing values between page loads.
    /// https://data-star.dev/reference/attribute_plugins#data-persist
    /// </summary>
    /// <param name="signalPaths">A list of signal paths that should be saved to storage</param>
    /// <param name="inSession">Store the signals in session storage; default = local</param>
    /// <returns>Attribute</returns>
    static member persistSignals (signalPaths, ?inSession) =
        let inSession' = defaultArg inSession false |> Bool.eitherOr "__session" ""
        Attr.create $"{Consts.dataSlugPrefix}-persist{inSession'}" (signalPaths |> Seq.map SignalPath.value |> String.concat " ")

    /// <summary>
    /// Replaces the url in the browser's address bar without reloading the page.
    /// https://data-star.dev/reference/attribute_plugins#data-replace-url
    /// </summary>
    /// <param name="urlExpression">An evaluated expression where the value replaces the address in the browser's address bar</param>
    /// <returns>Attribute</returns>
    static member replaceUrl urlExpression =
        Attr.create $"{Consts.dataSlugPrefix}-replace-url" urlExpression

    /// <summary>
    /// Allows you to add custom validity to an input element using an expression.
    /// The expression must evaluate to a string that will be set as the custom validity message.
    /// If the string is empty, the input is considered valid.
    /// https://data-star.dev/reference/attribute_plugins#data-custom-validity
    /// </summary>
    /// <param name="validityExpression">An expression that must evaluate to a string.
    /// If the string is non-empty, then the input is considered invalid and the string is the reported message; otherwise,
    /// the input is considered valid</param>
    /// <returns>Attribute</returns>
    static member customValidity validityExpression =
        Attr.create $"{Consts.dataSlugPrefix}-custom-validity" validityExpression

    /// <summary>
    /// Scrolls the element into view. Useful when updating the DOM from the backend, and you want to scroll to the new content.
    /// https://data-star.dev/reference/attribute_plugins#data-scroll-into-view
    /// </summary>
    /// <param name="animation">Can scroll Smooth, Instant, or Auto (dictated by CSS `scroll-behavior`)</param>
    /// <param name="horizontal">Where to scroll to in the horizontal direction; Left, Center, Right, Edge</param>
    /// <param name="vertical">Where to scroll to in the vertical direction; Top, Center, Bottom, Edge</param>
    /// <param name="focus">Optional bring element into focus after scrolling</param>
    /// <returns>Attribute</returns>
    static member scrollIntoView (animation:ScrollIntoViewAnimation, horizontal:ScrollIntoViewWhere, vertical:ScrollIntoViewWhere, ?focus:bool) =
        let animation' =
            match animation with
            | Smooth -> "__smooth" | Instant -> "__instant" | Auto -> "__auto"
        let horizontal' =
            match horizontal with
            | Left -> "__hstart" | Center -> "__hcenter" | Right -> "__hend" | Edge -> "__hnearest"
            | other -> failwith $"Invalid horizontal position for Ds.scrollIntoView: {other}"
        let vertical' =
            match vertical with
            | Top -> "__vstart" | Center -> "__vcenter" | Bottom -> "__vend" | Edge -> "__vnearest"
            | other -> failwith $"Invalid vertical position for Ds.scrollIntoView: {other}"
        let focus' = defaultArg focus false |> (fun focus -> if focus then "__focus" else "")
        Attr.createBool $"{Consts.dataSlugPrefix}-scroll-into-view{animation'}{horizontal'}{vertical'}{focus'}"

    /// <summary>
    /// Show or hides an element based on an expressions "true-ness".
    /// https://data-star.dev/reference/attribute_plugins#data-show
    /// </summary>
    /// <param name="boolExpression">The expression that will be evaluated; if true = the element is visible</param>
    /// <returns>Attribute</returns>
    static member show boolExpression =
        Attr.create $"{Consts.dataSlugPrefix}-show" boolExpression

    /// <summary>
    /// Sets the `view-transition-name` style attribute explicitly.
    /// https://data-star.dev/reference/attribute_plugins#data-view-transition
    /// </summary>
    /// <param name="expression">What to set the `view-transition-name`</param>
    /// <returns>Attribute</returns>
    static member viewTransition expression =
        Attr.create $"{Consts.dataSlugPrefix}-view-transition" expression

    /// <summary>
    /// This will create a signal and set its value to `true` while a server request is in flight, otherwise `false`.
    /// Place this in the same element as a Ds.get, Ds.post, etc
    /// https://data-star.dev/reference/attribute_plugins#data-indicator
    /// </summary>
    /// <param name="signalPath">The name of the signal to create</param>
    /// <returns>Attribute</returns>
    static member indicator signalPath =
        Attr.createBool $"{Consts.dataSlugPrefix}-indicator-{signalPath |> SignalPath.kebabValue}"

    /// <summary>
    /// Datastar walks the entire DOM and applies plugins to each element it encounters.
    /// It’s possible to tell Datastar to ignore an element and its descendants by placing a data-star-ignore attribute on it.
    /// This can be useful for preventing naming conflicts with third-party libraries.
    /// https://data-star.dev/reference/attribute_plugins#ignoring-elements
    /// </summary>
    /// <returns>Attribute</returns>
    static member ignore =
        Attr.createBool $"{Consts.dataSlugPrefix}-star-ignore"

    /// <summary>
    /// Datastar walks the entire DOM and applies plugins to each element it encounters.
    /// It’s possible to tell Datastar to ignore an element and its descendants by placing a data-star-ignore attribute on it.
    /// This can be useful for preventing naming conflicts with third-party libraries.
    /// This only ignores the element it is attached to.
    /// https://data-star.dev/reference/attribute_plugins#ignoring-elements
    /// </summary>
    /// <returns>Attribute</returns>
    static member ignoreThis =
        Attr.createBool $"{Consts.dataSlugPrefix}-star-ignore__self"

    /// <summary>
    /// Attaches an event listener to an element, executing the expression whenever the event is triggered.
    /// https://data-star.dev/reference/attribute_plugins#data-on
    /// </summary>
    /// <param name="eventName">The event to listen to, i.e. https://developer.mozilla.org/en-US/docs/Web/Events</param>
    /// <param name="expression">The expression to evaluate when the event is triggered. https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="modifiers">To modify the behavior of the event. https://data-star.dev/reference/attribute_plugins#modifiers-1</param>
    /// <returns>Attribute</returns>
    static member onEvent (eventName, expression, ?modifiers:OnEventModifier list) =
        let modifiers' = defaultArg modifiers []
        Attr.create (OnEvent.Serialize(eventName, modifiers')) expression

    /// <summary>
    /// Adds an on-click listener to the element and executes the expression.
    /// https://data-star.dev/reference/attribute_plugins#data-on
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered. https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="modifiers">To modify the behavior of the event. https://data-star.dev/reference/attribute_plugins#modifiers-1</param>
    /// <returns>Attribute</returns>
    static member onClick (expression, ?modifiers) =
        Ds.onEvent ("on-click", expression, ?modifiers = modifiers)

    /// <summary>
    /// Fires the expression when the element is loaded.
    /// https://data-star.dev/reference/attribute_plugins#special-events
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered. https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="delay">The time to wait before executing the expression</param>
    /// <returns>Attribute</returns>
    static member onLoad (expression, ?delay) =
        let delay = defaultArg delay TimeSpan.Zero
        let delay' = delay = TimeSpan.Zero |> Bool.eitherOr "" $"__delay.{delay.TotalMilliseconds}ms"
        Attr.create $"{Consts.dataSlugPrefix}-on-load{delay'}" expression

    /// <summary>
    /// Fires the expression when the element is loaded.
    /// https://data-star.dev/reference/attribute_plugins#special-events
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered. https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    /// <returns>Attribute</returns>
    static member onAnySignalChange (expression, ?debounce:Debounce, ?throttle:Throttle) =
        let debounce' = debounce |> Debounce.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        let throttle' = throttle |> Throttle.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        Attr.create $"{Consts.dataSlugPrefix}-on-signal-change{debounce'}{throttle'}" expression

    /// <summary>
    /// Fires the expression when the element is loaded.
    /// https://data-star.dev/reference/attribute_plugins#special-events
    /// </summary>
    /// <param name="signalPath">The signal to watch for a change</param>
    /// <param name="expression">The expression to evaluate when the event is triggered. https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    /// <returns>Attribute</returns>
    static member onSignalChange (signalPath:SignalPath, expression, ?debounce:Debounce, ?throttle:Throttle) =
        let debounce' = debounce |> Debounce.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        let throttle' = throttle |> Throttle.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        Attr.create $"{Consts.dataSlugPrefix}-on-signal-change-{signalPath |> SignalPath.kebabValue}{debounce'}{throttle'}" expression

    /// <summary>
    /// Evaluates the expression on a steady interval
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered. https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="interval">The time between each evaluation; default = Duration.With(TimeSpan.FromSeconds 1, false)</param>
    /// <param name="leading">Execute the first interval immediately; default = false</param>
    /// <returns>Attribute</returns>
    static member onInterval (expression, ?interval:TimeSpan, ?leading) =
        let leading' = defaultArg leading false |> Bool.eitherOr ".leading" ""
        let interval' = interval |> Option.map (fun int' -> $"__duration.{int'.TotalMilliseconds}ms{leading'}") |> Option.defaultValue ""
        Attr.create $"{Consts.dataSlugPrefix}-on-interval{interval'}" expression

    /// <summary>
    /// Runs an expression when the element intersects with the viewport.
    /// https://data-star.dev/reference/attribute_plugins#data-on-intersects
    /// </summary>
    /// <param name="expression">Expression to run when element is intersected</param>
    /// <param name="visibility">Sets it to trigger only if the element is half or fully viewed</param>
    /// <param name="onlyOnce">Only triggers the event once</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    /// <returns>Attribute</returns>
    static member onIntersect (expression, ?visibility, ?onlyOnce, ?debounce:Debounce, ?throttle:Throttle) =
        let visibility' =
            match visibility with
            | Some Half -> "__half"
            | Some Full -> "__full"
            | _ -> ""
        let onlyOnce' = defaultArg onlyOnce false |> Bool.eitherOr "__once" ""
        let debounce' = debounce |> Debounce.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        let throttle' = throttle |> Throttle.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        Attr.create $"{Consts.dataSlugPrefix}-on-intersect{visibility'}{onlyOnce'}{debounce'}{throttle'}" expression

    /// <summary>
    /// Runs an expression on every request animation frame event.
    /// https://data-star.dev/reference/attribute_plugins#data-on-raf
    /// </summary>
    /// <param name="expression">Expression to run on every request animation frame event</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    static member onRequestAnimationFrame (expression, ?debounce:Debounce, ?throttle:Throttle) =
        let debounce' = debounce |> Debounce.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        let throttle' = throttle |> Throttle.SerializeOption |> Option.map (fun str -> $"__{str}") |> Option.defaultValue ""
        Attr.create $"{Consts.dataSlugPrefix}-on-raf{debounce'}{throttle'}" expression

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
    /// https://data-star.dev/reference/action_plugins#get
    /// https://data-star.dev/reference/action_plugins#options
    /// </summary>
    /// <returns>Expression</returns>
    static member get (url, ?options) = Ds.backendAction options (Get url)
    /// <summary>
    /// Creates a @post action for an expression. The action sends a POST request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/action_plugins#post
    /// https://data-star.dev/reference/action_plugins#options
    /// </summary>
    /// <returns>Expression</returns>
    static member post (url, ?options) = Ds.backendAction options (Post url)
    /// <summary>
    /// Creates a @put action for an expression. The action sends a PUT request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/action_plugins#put
    /// https://data-star.dev/reference/action_plugins#options
    /// </summary>
    /// <returns>Expression</returns>
    static member put (url, ?options) = Ds.backendAction options (Put url)
    /// <summary>
    /// Creates a @patch action for an expression. The action sends a PATCH request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/action_plugins#patch
    /// https://data-star.dev/reference/action_plugins#options
    /// </summary>
    /// <returns>Expression</returns>
    static member patch (url, ?options) = Ds.backendAction options (Patch url)
    /// <summary>
    /// Creates a @delete action for an expression. The action sends a DELETE request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/action_plugins#delete
    /// https://data-star.dev/reference/action_plugins#options
    /// </summary>
    /// <returns>Expression</returns>
    static member delete (url, ?options) = Ds.backendAction options (Delete url)

    /// <summary>
    /// @clipboard(), copies an evaluated expression to the clipboard.
    /// https://data-star.dev/reference/action_plugins#clipboard
    /// </summary>
    /// <returns>Expression</returns>
    static member clipboard valueExpression = $"@clipboard({valueExpression})"

    /// <summary>
    /// @setall(), set all the signals that start with the prefix to the expression provided.
    /// https://data-star.dev/reference/action_plugins#setall
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
    /// https://data-star.dev/reference/action_plugins#toggleall
    /// </summary>
    /// <param name="signalsPathPrefix">All signals to toggle that have this prefix, e.g. 'foo.'</param>
    /// <returns>Expression</returns>
    static member toggleAll (signalsPathPrefix:string) =
        $"@toggleAll('{signalsPathPrefix}')"

    /// <summary>
    /// Make a value linear interpolate from an original range to new one.
    /// </summary>
    /// <param name="valueExpr">An expression that evaluates to a number that falls in the old Range</param>
    /// <param name="oldMinExpr">Start of range we are in</param>
    /// <param name="oldMaxExpr">End of range we are in</param>
    /// <param name="newMinExpr">Start of range to interpolate to</param>
    /// <param name="newMaxExpr">End of range to interpolate to</param>
    /// <param name="shouldClamp">Should we clamp; default = false</param>
    /// <param name="shouldRound">Should we round the returned value; default = false</param>
    /// <returns>Expression</returns>
    static member fit (valueExpr:string, oldMinExpr:string, oldMaxExpr:string, newMinExpr:string, newMaxExpr:string, ?shouldClamp, ?shouldRound) =
        let shouldClamp = defaultArg shouldClamp false |> _.ToString().ToLower()
        let shouldRound = defaultArg shouldRound false |> _.ToString().ToLower()
        $"@fit({valueExpr}, {oldMinExpr}, {oldMaxExpr}, {newMinExpr}, {newMaxExpr}, {shouldClamp}, {shouldRound})"

    /// <summary>
    /// Make a value linear interpolate from an original range to new one.
    /// </summary>
    /// <param name="valueExpr">An expression that evaluates to a number that falls in the old Range</param>
    /// <param name="oldRange">Range we are in</param>
    /// <param name="newRange">Range we want to interpolate onto</param>
    /// <param name="shouldClamp">Should we clamp; default = false</param>
    /// <param name="shouldRound">Should we round the returned value; default = false</param>
    /// <returns>Expression</returns>
    static member inline fit<'T, 'TNum when 'T :> INumber<'TNum>>(valueExpr:string, oldRange:'T*'T, newRange:'T*'T, ?shouldClamp, ?shouldRound) =
        let shouldClamp = defaultArg shouldClamp false |> _.ToString().ToLower()
        let shouldRound = defaultArg shouldRound false |> _.ToString().ToLower()
        $"@fit({valueExpr}, {oldRange |> fst}, {oldRange |> snd}, {newRange |> fst}, {newRange |> snd}, {shouldClamp}, {shouldRound})"

    /// <summary>
    /// Method for joining strings with " ; " to simplify multi-line expressions
    /// </summary>
    /// <param name="expressions"></param>
    static member expression (expressions:string seq) =
        expressions |> String.concat " ; "

    /// <summary>
    /// An attribute that should be placed in the &lt;body&gt; when creating a streaming app to avoid the issue explained here:
    /// https://stackoverflow.com/questions/8788802/prevent-safari-loading-from-cache-when-back-button-is-clicked
    /// </summary>
    static member safariStreamingFix = Attr.create "data-on-pageshow.window" "evt?.persisted && window.location.reload()"
