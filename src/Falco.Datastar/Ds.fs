namespace Falco.Datastar

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Web
open Falco.Markup
open StarFederation.Datastar.FSharp

[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Ds =
    /// <summary>
    /// The Datastar release that <see cref="cdnSrc"/> and <see cref="rocketCdnSrc"/> point at
    /// </summary>
    static member datastarVersion = "v1.0.4"

    /// <summary>
    /// The standard Datastar script on the jsDelivr CDN, for the release in <see cref="datastarVersion"/>
    /// </summary>
    static member cdnSrc =
        $"https://cdn.jsdelivr.net/gh/starfederation/datastar@{Ds.datastarVersion}/bundles/datastar.js"

    /// <summary>
    /// Shorthand for `Elem.script [ Attr.type' "module"; Attr.src cdnSrc ] []`
    /// </summary>
    /// <returns>Attribute</returns>
    static member cdnScript =
        Elem.script [ Attr.type' "module"; Attr.src Ds.cdnSrc ] []

    /// <summary>
    /// The Datastar script that includes Rocket (Datastar's web components) on the jsDelivr CDN, for the release in <see cref="datastarVersion"/>.
    /// It contains everything in <see cref="cdnSrc"/>, so load this one or that one, not both.
    /// </summary>
    static member rocketCdnSrc =
        $"https://cdn.jsdelivr.net/gh/starfederation/datastar@{Ds.datastarVersion}/bundles/datastar-rocket.js"

    /// <summary>
    /// Shorthand for `Elem.script [ Attr.type' "module"; Attr.src rocketCdnSrc ] []`
    /// </summary>
    static member rocketCdnScript =
        Elem.script [ Attr.type' "module"; Attr.src Ds.rocketCdnSrc ] []

    /// <summary>
    /// Patches a signal into the existing signals with the given value.
    /// Has an optional ifMissing flag. https://data-star.dev/reference/attributes#data-signals
    /// </summary>
    /// <param name="signalPath">The path to add. Prefix with underscore to keep the signal local to the browser, and not returned in a @get, @post, etc</param>
    /// <param name="signalValue">The initial value to set the signal</param>
    /// <param name="ifMissing">Signal is only merged if it doesn't already exist</param>
    /// <returns>Attribute</returns>
    static member signal<'T> (signalPath:SignalPath, signalValue:'T, ?ifMissing) =
        DsAttr.start "signals"
        |> DsAttr.addSignalPathTarget signalPath
        |> DsAttr.addModifierNameIf "ifmissing" (defaultArg ifMissing false)
        |> DsAttr.addValue (Js.literal signalValue)
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
    static member inline attr' (attributeName, expression) =
        DsAttr.create ("attr", targetName = attributeName, value = expression)

    /// <summary>
    /// Binds a signal to an element's value. Can be added to any element on which data can be input.
    /// input, textarea, select, checkbox, radio, and web components.
    /// https://data-star.dev/reference/attributes#data-bind
    /// </summary>
    /// <param name="signalPath">The signal to bind to</param>
    /// <returns>Attribute</returns>
    static member inline bind signalPath =
        DsAttr.createSp ("bind", signalPath)

    /// <summary>
    /// Binds a signal to a property of a custom element or web component, instead of its default value or attribute.
    /// Write the property name in kebab-case, because the HTML parser lowercases attribute names and Datastar turns the name back into camelCase.
    /// A name with a capital letter in it does not work: Datastar only removes the hyphens, so "someProp" would be read as "someprop".
    /// https://data-star.dev/reference/attributes#data-bind
    /// </summary>
    /// <param name="signalPath">The signal to bind to</param>
    /// <param name="propName">The element property to bind, e.g. "checked" or "some-prop"</param>
    /// <param name="events">The events that copy the property into the signal. If you leave this out, Datastar uses the element's default events</param>
    /// <returns>Attribute</returns>
    static member bindProp (signalPath:SignalPath, propName:string, ?events:string list) =
        propName |> Guard.notBlank "propName" "Ds.bindProp needs the name of an element property, such as \"checked\". An empty name makes Datastar throw BindPropNameMissing."
        events |> Option.iter (List.iter (Guard.notBlank "events" "Ds.bindProp was given an empty event name. Datastar listens for each name that follows __event, so an empty one listens for nothing. Remove it, or pass names such as [ \"input\"; \"change\" ]."))
        DsAttr.start "bind"
        |> DsAttr.addTarget (signalPath |> SignalPath.kebabValue)
        |> DsAttr.addModifierOption (events |> Option.filter (List.isEmpty >> not) |> Option.map (fun names -> { Name = "event"; Tags = names }) |> Option.toValueOption)
        |> DsAttr.addModifier { Name = "prop"; Tags = [ String.toKebab propName ] }
        |> DsAttr.create

    /// <summary>
    /// Binds a signal to an element and copies the element's value into the signal on the events you list, instead of its default events.
    /// The HTML parser lowercases attribute names, so a custom event name must be all lowercase to work here.
    /// https://data-star.dev/reference/attributes#data-bind
    /// </summary>
    /// <param name="signalPath">The signal to bind to</param>
    /// <param name="firstEvent">An event that copies the element's value into the signal, e.g. "input". At least one is needed, because with none Datastar never syncs the signal</param>
    /// <param name="otherEvents">More events, e.g. [ "change" ]</param>
    /// <returns>Attribute</returns>
    static member bindEvent (signalPath:SignalPath, firstEvent:string, ?otherEvents:string list) =
        let events = firstEvent :: defaultArg otherEvents []
        events |> List.iter (Guard.notBlank "firstEvent" "Ds.bindEvent needs an event name, such as \"input\". With no events Datastar never syncs the signal.")
        DsAttr.start "bind"
        |> DsAttr.addTarget (signalPath |> SignalPath.kebabValue)
        |> DsAttr.addModifier { Name = "event"; Tags = events }
        |> DsAttr.create

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
    static member inline style (styleProperty, propertyValueExpression) =
        DsAttr.create ("style", targetName = styleProperty, value = propertyValueExpression)

    /// <summary>
    /// Bind the content text of the element to an expression.
    /// https://data-star.dev/reference/attributes#data-text
    /// </summary>
    /// <param name="expression">Expression to be evaluated, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member inline text expression =
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
    /// e.g. data-on:click="$signalRefName.value='newValue'".
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
    /// Shows or hides an element based on the truthiness of an expression.
    /// https://data-star.dev/reference/attributes#data-show
    /// </summary>
    /// <param name="boolExpression">The expression that will be evaluated; if true = the element is visible, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Attribute</returns>
    static member inline show boolExpression =
        DsAttr.create ("show", value = boolExpression)

    /// <summary>
    /// Execute an expression on page load and whenever any signals in the expression change.
    /// </summary>
    /// <param name="expression">The expression to fire</param>
    /// <returns>Attribute</returns>
    static member inline effect (expression:string) =
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
    /// It’s possible to tell Datastar to ignore an element and its descendants by placing a data-ignore attribute on it.
    /// This can be useful for preventing naming conflicts with third-party libraries.
    /// https://data-star.dev/reference/attributes#data-ignore
    /// </summary>
    /// <returns>Attribute</returns>
    static member inline ignore =
        DsAttr.create "ignore"

    /// <summary>
    /// Datastar walks the entire DOM and applies plugins to each element it encounters.
    /// It’s possible to tell Datastar to ignore an element and its descendants by placing a data-ignore attribute on it.
    /// This can be useful for preventing naming conflicts with third-party libraries.
    /// This only ignores the element it is attached to.
    /// https://data-star.dev/reference/attributes#data-ignore
    /// </summary>
    /// <returns>Attribute</returns>
    static member ignoreSelf =
        DsAttr.start "ignore"
        |> DsAttr.addModifier { Name="self"; Tags = [] }
        |> DsAttr.create

    /// <summary>
    /// Similar to the Ds.ignore, the data-ignore-morph attribute tells the PatchElements watcher to skip processing an element and its children when morphing elements.
    /// An element is skipped when both the element on the page and the element the server sent have the attribute, or when the element on the page is inside an element that has it.
    /// https://data-star.dev/reference/attributes#data-ignore-morph
    /// </summary>
    /// <returns>Attribute</returns>
    static member inline ignoreMorph =
        DsAttr.create "ignore-morph"

    /// <summary>
    /// Sets the text content of an element to a reactive JSON stringified version of signals. Useful for troubleshooting.
    /// https://data-star.dev/reference/attributes#data-json-signals
    /// </summary>
    static member inline jsonSignals =
        DsAttr.create "json-signals"

    /// <summary>
    /// Sets the text content of an element to a reactive JSON stringified version of signals. Useful for troubleshooting.
    /// https://data-star.dev/reference/attributes#data-json-signals
    /// </summary>
    /// <param name="signalsFilter">Regex of signal paths to be included and excluded</param>
    /// <param name="terse">Single line output</param>
    static member jsonSignalsOptions (?signalsFilter:SignalsFilter, ?terse:bool) =
        let addSignalsFilter signalsFilter dsAttr =
            if signalsFilter <> SignalsFilter.None
            then dsAttr |> DsAttr.addValue (signalsFilter |> SignalsFilter.Serialize)
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
    static member onClick (expression:string, ?eventModifiers) =
        Ds.onEvent ("click", expression, ?eventModifiers = eventModifiers)

    /// <summary>
    /// Fires the expression when the element is loaded.
    /// https://data-star.dev/reference/attributes#data-init
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="delayMs">The time to wait before executing the expression in milliseconds; default = 0</param>
    /// <param name="viewTransition">Wrap expression in document.startViewTransition(); default = false</param>
    /// <returns>Attribute</returns>
    static member onInit (expression, ?delayMs, ?viewTransition) =
        DsAttr.start "init"
        |> DsAttr.addModifierOption (delayMs |> Option.toValueOption |> ValueOption.map DsAttrModifier.DelayMs)
        |> DsAttr.addModifierNameIf "viewtransition" (defaultArg viewTransition false)
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Evaluates the expression on a steady interval
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="intervalMs">The time between each evaluation, in milliseconds</param>
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
    /// Fires the expression when a signal is changed. Filter using Ds.onSignalPatchFilter
    /// https://data-star.dev/reference/attributes#data-on-signal-patch
    /// </summary>
    /// <param name="expression">The expression to evaluate when the event is triggered; https://data-star.dev/guide/datastar_expressions</param>
    /// <param name="delayMs">The time to wait before executing the expression in milliseconds; default = 0</param>
    /// <param name="debounce"></param>
    /// <param name="throttle"></param>
    /// <returns>Attribute</returns>
    static member onSignalPatch (expression, ?delayMs:int, ?debounce:Debounce, ?throttle:Throttle) =
        DsAttr.start "on-signal-patch"
        |> DsAttr.addModifierOption (delayMs |> Option.toValueOption |> ValueOption.map DsAttrModifier.DelayMs)
        |> DsAttr.addModifierOption (debounce |> Option.toValueOption |> ValueOption.map DsAttrModifier.Debounce)
        |> DsAttr.addModifierOption (throttle |> Option.toValueOption |> ValueOption.map DsAttrModifier.Throttle)
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Filter the signals that cause Ds.onSignalPatch to fire
    /// https://data-star.dev/reference/attributes#data-on-signal-patch-filter
    /// </summary>
    /// <param name="signalsFilter">Regex of signal paths to be included and excluded</param>
    /// <returns>Attribute</returns>
    static member onSignalPatchFilter (signalsFilter:SignalsFilter) =
        DsAttr.start "on-signal-patch-filter"
        |> DsAttr.addValue (signalsFilter |> SignalsFilter.Serialize)
        |> DsAttr.create

    /// <summary>
    /// Runs an expression when the element intersects with the viewport.
    /// https://data-star.dev/reference/attributes#data-on-intersect
    /// </summary>
    /// <param name="expression">Expression to run when element is intersected</param>
    /// <param name="visibility">Sets it to trigger only if the element is exited, or half or fully viewed</param>
    /// <param name="onlyOnce">Only triggers the event once</param>
    /// <param name="delayMs">The time to wait before executing the expression in milliseconds; default = 0</param>
    /// <param name="debounce">Debounce the event listener</param>
    /// <param name="throttle">Throttle the event listener</param>
    /// <param name="viewTransition">Wrap expression in document.startViewTransition(); default = false</param>
    /// <param name="threshold">Triggers when the element is visible by a certain percentage (0-100)</param>
    /// <returns>Attribute</returns>
    static member onIntersect (expression, ?visibility, ?onlyOnce, ?delayMs:int, ?debounce:Debounce, ?throttle:Throttle, ?viewTransition:bool, ?threshold:int) =
        DsAttr.start "on-intersect"
        |> (fun dsAttr ->
            match visibility with
            | Some vis when vis = IntersectsVisibility.Exit -> DsAttr.addModifierName "exit" dsAttr
            | Some vis when vis = IntersectsVisibility.Full -> DsAttr.addModifierName "full" dsAttr
            | Some vis when vis = IntersectsVisibility.Half -> DsAttr.addModifierName "half" dsAttr
            | _ -> dsAttr
            )
        |> DsAttr.addModifierNameIf "once" (defaultArg onlyOnce false)
        |> DsAttr.addModifierNameIf "viewtransition" (defaultArg viewTransition false)
        |> DsAttr.addModifierOption (delayMs |> Option.toValueOption |> ValueOption.map DsAttrModifier.DelayMs)
        |> DsAttr.addModifierOption (debounce |> Option.toValueOption |> ValueOption.map DsAttrModifier.Debounce)
        |> DsAttr.addModifierOption (throttle |> Option.toValueOption |> ValueOption.map DsAttrModifier.Throttle)
        |> DsAttr.addModifierOption (threshold |> Option.toValueOption |> ValueOption.map (fun vv -> DsAttrModifier.Threshold (Math.Clamp(vv, 0, 100))))
        |> DsAttr.addValue expression
        |> DsAttr.create

    /// <summary>
    /// Actions
    /// </summary>
    static member private backendAction (actionOptions:RequestOptions voption) (action:BackendAction) =
        BackendActionExpression.render actionOptions action

    /// <summary>
    /// Creates a @get action for an expression with options. The action sends a GET request with the given url.
    /// Signals will be sent as a query parameter.
    /// https://data-star.dev/reference/actions#get
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member get (url, ?options) =
        Ds.backendAction (options |> Option.toValueOption) (Get url)

    /// <summary>
    /// Creates a @post action for an expression. The action sends a POST request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#post
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member post (url, ?options) =
        Ds.backendAction (options |> Option.toValueOption) (Post url)

    /// <summary>
    /// Creates a @put action for an expression. The action sends a PUT request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#put
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member put (url, ?options) =
        Ds.backendAction (options |> Option.toValueOption) (Put url)

    /// <summary>
    /// Creates a @patch action for an expression. The action sends a PATCH request to the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#patch
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member patch (url, ?options) =
        Ds.backendAction (options |> Option.toValueOption) (Patch url)

    /// <summary>
    /// Creates a @delete action for an expression. The action sends a DELETE request to the given url.
    /// Signals are sent in the `datastar` query parameter, as for @get, because a DELETE request has no body.
    /// https://data-star.dev/reference/actions#delete
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member delete (url, ?options) =
        Ds.backendAction (options |> Option.toValueOption) (Delete url)

    /// <summary>
    /// Creates a @query action for an expression with options. The action sends a QUERY request with the given url.
    /// Signals are sent with the body of the request.
    /// https://data-star.dev/reference/actions#query
    /// https://data-star.dev/reference/actions#options
    /// </summary>
    /// <returns>Expression</returns>
    static member query (url, ?options) =
        Ds.backendAction (options |> Option.toValueOption) (Query url)

    /// <summary>
    /// @setAll(): sets every signal that matches the filter to the value. With no filter, it sets every signal.
    /// https://data-star.dev/reference/actions#setall
    /// </summary>
    /// <param name="value">The value to set. Strings are quoted in the expression; numbers and booleans are not</param>
    /// <param name="signalsFilter">Regex of signal paths to be included and excluded</param>
    /// <returns>Expression</returns>
    static member setAllFiltered<'T> (value:'T, signalsFilter:SignalsFilter) =
        FilterActionExpression.setAll value signalsFilter

    /// <summary>
    /// @setAll(), set all the signals that start with the prefix to the value provided.
    /// https://data-star.dev/reference/actions#setall
    /// </summary>
    /// <param name="signalsPathPrefix">All signals to set that have this prefix, e.g. 'foo.'</param>
    /// <param name="value">The value to set. Strings are quoted in the expression; numbers and booleans are not</param>
    /// <returns>Expression</returns>
    static member setAll<'T> (signalsPathPrefix:string, value:'T) =
        Ds.setAllFiltered (value, SignalsFilter.Prefix signalsPathPrefix)

    /// <summary>
    /// @toggleAll(), toggle all the signals that start with the prefix.
    /// https://data-star.dev/reference/actions#toggleall
    /// </summary>
    /// <param name="signalsPathPrefix">All signals to toggle that have this prefix, e.g. 'foo.'</param>
    /// <returns>Expression</returns>
    static member toggleAll (signalsPathPrefix:string) =
        Ds.toggleAllFiltered (SignalsFilter.Prefix signalsPathPrefix)

    /// <summary>
    /// @toggleAll(): toggles every signal that matches the filter. With no filter, it toggles every signal.
    /// https://data-star.dev/reference/actions#toggleall
    /// </summary>
    /// <param name="signalsFilter">Regex of signal paths to be included and excluded</param>
    /// <returns>Expression</returns>
    static member toggleAllFiltered (signalsFilter:SignalsFilter) =
        FilterActionExpression.toggleAll signalsFilter

    /// <summary>
    /// @peek(): evaluates the expression without subscribing to the signals it reads.
    /// Use it in Ds.effect or Ds.computed to read a signal without re-running when that signal changes.
    /// https://data-star.dev/reference/actions#peek
    /// </summary>
    /// <param name="expression">Expression to evaluate, https://data-star.dev/guide/datastar_expressions</param>
    /// <returns>Expression</returns>
    static member peek (expression:string) =
        $"@peek(() => {expression})"

    /// <summary>
    /// Method for joining strings with " ; " to simplify multi-line expressions
    /// </summary>
    /// <param name="expressions"></param>
    static member expression (expressions:string seq) =
        expressions |> String.concat " ; "

    /// <summary>
    /// Changes the casing of the name an attribute creates, with Datastar's __case modifier.
    /// Datastar reads the name from an HTML attribute, and the HTML parser lowercases attribute names, so without this a signal is camelCase
    /// (a class name or an event name is kebab-case). Use it when your server uses another style, such as snake_case JSON.
    /// It works on Ds.bind, Ds.class', Ds.computed, Ds.indicator, Ds.onEvent and Ds.signal, which put the name in the attribute's key.
    /// It does nothing on Ds.ref and Ds.signals, which put the name or the object in the attribute's value, or on attributes that create no name.
    /// https://data-star.dev/reference/attributes#data-signals
    /// </summary>
    /// <param name="caseStyle">The casing to use</param>
    /// <param name="attribute">The attribute to change, e.g. <c>Ds.signal (sp"myValue", 1)</c></param>
    /// <returns>Attribute</returns>
    static member withCase (caseStyle:CaseStyle) (attribute:XmlAttribute) =
        let modifier = "__case." + CaseStyle.Serialize caseStyle
        match attribute with
        | KeyValueAttr (key, value) -> KeyValueAttr (key + modifier, value)
        | NonValueAttr key -> NonValueAttr (key + modifier)

    // Typed versions. They take signals, expressions and statements written in F#, so the compiler checks what the JavaScript strings above cannot:
    // that a signal exists, that a condition is a boolean, and that a value has the type of its signal.

    /// <summary>
    /// Bind the content text of the element to an expression.
    /// https://data-star.dev/reference/attributes#data-text
    /// </summary>
    /// <param name="expression">An expression of any type; Datastar shows it as text</param>
    /// <returns>Attribute</returns>
    static member text (expression:Expr<'T>) =
        DsAttr.create ("text", value = Expr.toString expression)

    /// <summary>
    /// Shows or hides an element based on a boolean expression.
    /// https://data-star.dev/reference/attributes#data-show
    /// </summary>
    /// <param name="condition">If it is true the element is visible</param>
    /// <returns>Attribute</returns>
    static member show (condition:Expr<bool>) =
        DsAttr.create ("show", value = Expr.toString condition)

    /// <summary>
    /// Adds or removes a class from the element based on a boolean expression.
    /// https://data-star.dev/reference/attributes#data-class
    /// </summary>
    /// <param name="className">Name of the class to add or remove</param>
    /// <param name="condition">If it is true the class is added, otherwise it is removed</param>
    /// <returns>Attribute</returns>
    static member class' (className:string, condition:Expr<bool>) =
        Ds.class' (className, Expr.toString condition)

    /// <summary>
    /// Bind an element's attribute value to an expression.
    /// https://data-star.dev/reference/attributes#data-attr
    /// </summary>
    /// <param name="attributeName">An HTML element attribute</param>
    /// <param name="expression">The value to give the attribute</param>
    /// <returns>Attribute</returns>
    static member attr' (attributeName:string, expression:Expr<'T>) =
        Ds.attr' (attributeName, Expr.toString expression)

    /// <summary>
    /// Sets the value of the inline CSS styles on an element based on an expression
    /// </summary>
    /// <param name="styleProperty">The style to set</param>
    /// <param name="propertyValue">The value to give the style property</param>
    /// <returns>Attribute</returns>
    static member style (styleProperty:string, propertyValue:Expr<'T>) =
        Ds.style (styleProperty, Expr.toString propertyValue)

    /// <summary>
    /// Creates a signal with a starting value of the signal's type. A text value is escaped.
    /// A browser signal stays in the browser, and a server signal is sent with requests: see <see cref="SignalScope"/>.
    /// A Rocket component signal is scoped to the component instance by Rocket when this is inside the component.
    /// https://data-star.dev/reference/attributes#data-signals
    /// </summary>
    /// <param name="signal">The signal to create</param>
    /// <param name="signalValue">The starting value</param>
    /// <param name="ifMissing">The signal is only created if it does not exist</param>
    /// <returns>Attribute</returns>
    static member signal (signal:Signal<'T>, signalValue:'T, ?ifMissing) =
        Ds.signal (SignalPath.create (Signal.path signal), signalValue, ?ifMissing = ifMissing)

    /// <summary>
    /// Creates a read-only signal that is computed from an expression of the signal's type.
    /// https://data-star.dev/reference/attributes#data-computed
    /// </summary>
    /// <param name="signal">The signal to create</param>
    /// <param name="expression">The expression that gives its value</param>
    /// <returns>Attribute</returns>
    static member computed (signal:Signal<'T>, expression:Expr<'T>) =
        Ds.computed (SignalPath.create (Signal.path signal), Expr.toString expression)

    /// <summary>
    /// Binds a signal to an element's value.
    /// https://data-star.dev/reference/attributes#data-bind
    /// </summary>
    /// <param name="signal">The signal to bind to. Datastar sends a server signal with requests, which is how a form input reaches the backend</param>
    /// <returns>Attribute</returns>
    static member bind (signal:Signal<'T>) =
        DsAttr.createSp ("bind", SignalPath.create (Signal.path signal))

    /// <summary>
    /// Creates a boolean signal that is true while a server request is in flight. Place it in the same element as the action that makes the request.
    /// https://data-star.dev/reference/attributes#data-indicator
    /// </summary>
    /// <param name="signal">The signal to create. A browser signal is the right choice, because the server does not need it</param>
    /// <returns>Attribute</returns>
    static member indicator (signal:Signal<bool>) =
        Ds.indicator (SignalPath.create (Signal.path signal))

    /// <summary>
    /// Attaches an event listener to an element, and runs the statement when the event happens.
    /// https://data-star.dev/reference/attributes#data-on
    /// </summary>
    /// <param name="eventName">The event to listen to</param>
    /// <param name="statement">What to do when the event happens</param>
    /// <param name="eventModifiers">To modify the behavior of the event</param>
    /// <returns>Attribute</returns>
    static member onEvent (eventName:string, statement:Stmt, ?eventModifiers:OnEventModifier list) =
        Ds.onEvent (eventName, Stmt.toString statement, ?eventModifiers = eventModifiers)

    /// <summary>
    /// Adds an on-click listener to the element and runs the statement.
    /// https://data-star.dev/reference/attributes#data-on
    /// </summary>
    /// <param name="statement">What to do when the element is clicked</param>
    /// <param name="eventModifiers">To modify the behavior of the event</param>
    /// <returns>Attribute</returns>
    static member onClick (statement:Stmt, ?eventModifiers:OnEventModifier list) =
        Ds.onEvent ("click", statement, ?eventModifiers = eventModifiers)

    /// <summary>
    /// Runs the statement when the element is loaded.
    /// https://data-star.dev/reference/attributes#data-init
    /// </summary>
    /// <param name="statement">What to do</param>
    /// <param name="delayMs">The time to wait before running it in milliseconds; default = 0</param>
    /// <param name="viewTransition">Wrap it in document.startViewTransition(); default = false</param>
    /// <returns>Attribute</returns>
    static member onInit (statement:Stmt, ?delayMs:int, ?viewTransition:bool) =
        Ds.onInit (Stmt.toString statement, ?delayMs = delayMs, ?viewTransition = viewTransition)

    /// <summary>
    /// Runs the statement on page load and whenever any signal it reads changes.
    /// </summary>
    /// <param name="statement">What to do</param>
    /// <returns>Attribute</returns>
    static member effect (statement:Stmt) =
        Ds.effect (Stmt.toString statement)

    /// <summary>
    /// Runs the statement on a steady interval
    /// </summary>
    /// <param name="statement">What to do</param>
    /// <param name="intervalMs">The time between each run</param>
    /// <param name="leading">Run it first immediately; default = false</param>
    /// <param name="viewTransition">Wrap it in document.startViewTransition(); default = false</param>
    /// <returns>Attribute</returns>
    static member onInterval (statement:Stmt, intervalMs:int, ?leading:bool, ?viewTransition:bool) =
        Ds.onInterval (Stmt.toString statement, intervalMs, ?leading = leading, ?viewTransition = viewTransition)

    /// <summary>
    /// Runs the statement when the element intersects with the viewport.
    /// https://data-star.dev/reference/attributes#data-on-intersect
    /// </summary>
    /// <param name="statement">What to do</param>
    /// <param name="visibility">Sets it to trigger only if the element is exited, or half or fully viewed</param>
    /// <param name="onlyOnce">Only triggers the event once</param>
    /// <param name="delayMs">The time to wait before running it in milliseconds; default = 0</param>
    /// <param name="debounce">Debounce the event listener</param>
    /// <param name="throttle">Throttle the event listener</param>
    /// <param name="viewTransition">Wrap it in document.startViewTransition(); default = false</param>
    /// <param name="threshold">Triggers when the element is visible by a certain percentage (0-100)</param>
    /// <returns>Attribute</returns>
    static member onIntersect (statement:Stmt, ?visibility:IntersectsVisibility, ?onlyOnce:bool, ?delayMs:int, ?debounce:Debounce, ?throttle:Throttle, ?viewTransition:bool, ?threshold:int) =
        Ds.onIntersect (Stmt.toString statement, ?visibility = visibility, ?onlyOnce = onlyOnce, ?delayMs = delayMs, ?debounce = debounce, ?throttle = throttle, ?viewTransition = viewTransition, ?threshold = threshold)

    /// <summary>
    /// Runs the statement when a signal is changed. Filter using Ds.onSignalPatchFilter
    /// https://data-star.dev/reference/attributes#data-on-signal-patch
    /// </summary>
    /// <param name="statement">What to do</param>
    /// <param name="delayMs">The time to wait before running it in milliseconds; default = 0</param>
    /// <param name="debounce">Debounce the event listener</param>
    /// <param name="throttle">Throttle the event listener</param>
    /// <returns>Attribute</returns>
    static member onSignalPatch (statement:Stmt, ?delayMs:int, ?debounce:Debounce, ?throttle:Throttle) =
        Ds.onSignalPatch (Stmt.toString statement, ?delayMs = delayMs, ?debounce = debounce, ?throttle = throttle)

    /// <summary>
    /// Gives Datastar the nonce of your Content Security Policy, so that it works on a page whose policy does not allow unsafe-eval.
    /// Put it on the &lt;html&gt; element. Datastar reads it once and then removes the attribute.
    /// The nonce must match the one in your policy's script-src, and it must not be empty. The script tag that loads Datastar needs it too, unless your policy already allows that source.
    /// CSP mode does not make expressions safe to use with untrusted content: pass user values through signals, not into the text of an expression.
    /// https://data-star.dev/reference/security#csp-mode
    /// </summary>
    /// <param name="nonce">The nonce for this response. Generate a new one for every response</param>
    /// <returns>Attribute</returns>
    static member nonce (nonce:string) =
        nonce |> Guard.notBlank "nonce" "Ds.nonce needs a nonce that is not empty. When Datastar loads with an empty data-nonce it throws NonceRequired, and then none of Datastar works. Generate a new random value for every response."
        DsAttr.create ("nonce", value = Js.attrEncode nonce)

    /// <summary>
    /// An attribute that should be added to the &lt;body&gt; when creating a streaming app to avoid the issue explained here:
    /// https://stackoverflow.com/questions/8788802/prevent-safari-loading-from-cache-when-back-button-is-clicked
    /// </summary>
    static member safariStreamingFix =
        DsAttr.startEvent "pageshow"
        |> DsAttr.addModifier { Name = "window"; Tags = [] }
        |> DsAttr.addValue "evt?.persisted && window.location.reload()"
        |> DsAttr.create
