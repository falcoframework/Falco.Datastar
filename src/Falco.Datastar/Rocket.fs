namespace Falco.Datastar

open System
open System.Globalization
open System.Numerics
open System.Text.Json
open Falco.Markup

/// <summary>
/// Server-side helpers for Rocket, Datastar's web components. Load Rocket with <see cref="Ds.rocketCdnScript"/>.
/// Components are written in JavaScript with <c>rocket(tag, definition)</c>. These helpers cover what the server renders:
/// the props a component reads from its attributes, the signals and actions its children can use, and its template directives.
/// They only write the values you pass, so Rocket's own defaults still apply.
/// Datastar's documentation says that Rocket is in beta and that its API is subject to change: https://data-star.dev/reference/rocket
/// https://github.com/starfederation/datastar/tree/v1.0.4/library/src/rocket
/// </summary>
[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Rocket =
    /// <summary>
    /// A signal that belongs to one instance of a component, for use in expressions inside it, e.g. <c>Ds.text (Rocket.local "count")</c>.
    /// Rocket rewrites the name to a path that is unique to the instance, so two instances of the same component do not share the signal.
    /// </summary>
    /// <param name="name">The signal's name. Declare it in the component's setup with <c>$$('name', initialValue)</c></param>
    /// <returns>Expression</returns>
    static member local (name:string) =
        "$$" + name

    /// <summary>
    /// Calls an action of the component that contains the expression, e.g. <c>Ds.onClick (Rocket.call "flip")</c>.
    /// Rocket looks for a component action with that name first, and then for a Datastar action.
    /// </summary>
    /// <param name="name">The action's name. Register it in the component's setup with <c>action('name', fn)</c></param>
    /// <param name="args">Expressions to pass to the action</param>
    /// <returns>Expression</returns>
    static member call (name:string, ?args:string list) =
        let arguments = defaultArg args [] |> String.concat ", "
        $"@{name}({arguments})"

    /// <summary>
    /// Makes a bind, computed or indicator attribute use the page's signal instead of the component's own.
    /// Rocket normally ties these attributes to the component instance. This opts one attribute out, using Rocket's <c>__root</c> modifier.
    /// It does not work on <c>data-signals</c>, which Rocket always ties to the instance. It has no effect on <c>data-ref</c> either:
    /// Datastar 1.0.4 turns every ref in a component into a component reference before it looks for <c>__root</c>, although Datastar's Rocket reference says otherwise.
    /// </summary>
    /// <param name="attribute">The attribute to opt out, e.g. <c>Ds.bind "query"</c></param>
    /// <returns>Attribute</returns>
    static member root (attribute:XmlAttribute) =
        match attribute with
        | KeyValueAttr (key, value) -> KeyValueAttr (key + "__root", value)
        | NonValueAttr key -> NonValueAttr (key + "__root")

    static member private prop (name:string, encoded:string) =
        let attributeName = String.datastarKebab name
        Guard.attributeName "prop name" false attributeName
        Attr.create attributeName (Js.attrEncode encoded)

    /// <summary>
    /// A string prop. The attribute name is the prop name converted the way Rocket converts it, e.g. <c>maxCount</c> becomes <c>max-count</c>.
    /// </summary>
    /// <param name="name">The prop name as the component defines it, e.g. "label"</param>
    /// <param name="value">The text. It is escaped for use in an attribute. A carriage return is written as a character reference, because a parser would change it into a line feed. HTML cannot keep a NUL character in an attribute, so it is written as U+FFFD, which is what a browser would read</param>
    /// <returns>Attribute</returns>
    static member propString (name:string, value:string) =
        Rocket.prop (name, value)

    /// <summary>
    /// A number prop. It is written with the invariant culture, because the browser always reads a dot as the decimal separator, whatever the server's culture is.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">Any numeric type. A value Rocket's number codec cannot read is refused: the codec runs Number(value) and turns anything that is not a finite number into 0, so writing one here would reach the component as a different value rather than as an error</param>
    /// <returns>Attribute</returns>
    static member propNumber<'T when 'T :> INumberBase<'T>> (name:string, value:'T) =
        // Boxed, because a runtime type test on a type variable the compiler cannot pin down is not allowed.
        let boxed = box value
        let text = (value :> IFormattable).ToString(null, CultureInfo.InvariantCulture)
        // Number("NaN") and Number("Infinity") are not finite, so the component reads 0. Say so here instead.
        let finite =
            match boxed with
            | :? float as f -> Double.IsFinite f
            | :? float32 as f -> Single.IsFinite f
            | _ -> true
        if not finite then
            invalidArg (nameof value) $"'{text}' is not a finite number. Rocket's number codec reads Number(value) and turns anything that is not finite into 0, so the component would get 0 rather than the value written here."
        Rocket.prop (name, text)

    /// <summary>
    /// A boolean prop. It is always written, as "true" or "false". If the attribute were left out, the component would use the prop's default, which might be true.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The value</param>
    /// <returns>Attribute</returns>
    static member propBool (name:string, value:bool) =
        Rocket.prop (name, Bool.eitherOr "true" "false" value)

    /// <summary>
    /// A date prop, written as UTC ISO 8601 with milliseconds, the same format as JavaScript's <c>Date.toISOString()</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The moment in time. It is converted to UTC</param>
    /// <returns>Attribute</returns>
    static member propDate (name:string, value:DateTimeOffset) =
        Rocket.prop (name, value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))

    /// <summary>
    /// A structured prop, written as JSON. It works for Rocket's json, array, tuple, object and oneOf codecs.
    /// Property names are camelCase, like the JavaScript objects the component reads them into, unless you pass your own options.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The value to serialize with System.Text.Json. The result is escaped for use in an attribute</param>
    /// <param name="options">Options for the JSON serializer</param>
    /// <returns>Attribute</returns>
    static member propJson<'T> (name:string, value:'T, ?options:JsonSerializerOptions) =
        Rocket.prop (name, JsonSerializer.Serialize<'T>(value, defaultArg options Js.webJsonOptions))

    /// <summary>
    /// A binary prop, written as base64. The component's bin codec decodes it with <c>atob</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The bytes</param>
    /// <returns>Attribute</returns>
    static member propBin (name:string, value:byte[]) =
        Rocket.prop (name, Convert.ToBase64String value)

    /// <summary>
    /// Repeats the children once for each item in a signal or expression, e.g. <c>Rocket.templateFor (Rocket.local "todos", [ ... ], item = "todo")</c>.
    /// Rocket calls the item <c>item</c> and the index <c>i</c> unless you give other names, and this method only writes names you pass.
    /// The attribute is always <c>data-for</c>, even if you set a different attribute prefix, because Rocket does not support a prefix on its template directives.
    /// Rocket runs the directive on the server-rendered children of a light-DOM component when the page loads. In an open or closed shadow-DOM component it only runs for children that a later server patch sends.
    /// </summary>
    /// <param name="source">An expression that gives an array, an iterable or a string</param>
    /// <param name="children">The content of one row. It can use the item and the index by their names</param>
    /// <param name="item">The item's name. If you pass only <paramref name="index"/>, the item is called <c>item</c></param>
    /// <param name="index">The index's name</param>
    /// <returns>Element</returns>
    static member templateFor (source:string, children:XmlNode list, ?item:string, ?index:string) =
        Rocket.templateForEncoded (Js.attrEncode (Rocket.forExpression (source, item, index)), children)

    /// The text of a data-for attribute: an optional item name and index name, and the source
    /// A template element with a directive whose text is already safe for an attribute
    static member private directive (name:string, safeExpression:string, children:XmlNode list) =
        Elem.template [ Attr.create name safeExpression ] children

    static member private forExpression (source:string, item:string option, index:string option) =
        match item, index with
        | None, None -> source
        | Some item', None -> $"{item'} in {source}"
        | None, Some index' -> $"item, {index'} in {source}"
        | Some item', Some index' -> $"{item'}, {index'} in {source}"

    /// The typed overloads build text that is already safe for an attribute, so it must not be encoded again
    static member private templateForEncoded (safeExpression:string, children:XmlNode list) =
        Elem.template [ Attr.create "data-for" safeExpression ] children

    /// <summary>
    /// Renders the children only while the condition is true. To make a chain, put <see cref="templateElseIf"/> and <see cref="templateElse"/> elements directly after it.
    /// The attribute is always <c>data-if</c>, even if you set a different attribute prefix, because Rocket does not support a prefix on its template directives.
    /// Rocket runs the directive on the server-rendered children of a light-DOM component when the page loads. In an open or closed shadow-DOM component it only runs for children that a later server patch sends.
    /// </summary>
    /// <param name="condition">An expression</param>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateIf (condition:string, children:XmlNode list) =
        Rocket.directive ("data-if", Js.attrEncode condition, children)

    /// <summary>
    /// The next branch of a chain that starts with <see cref="templateIf"/>. It must come directly after a templateIf or another templateElseIf.
    /// </summary>
    /// <param name="condition">An expression</param>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateElseIf (condition:string, children:XmlNode list) =
        Rocket.directive ("data-else-if", Js.attrEncode condition, children)

    /// <summary>
    /// The last branch of a chain. It must come directly after a templateIf or a templateElseIf.
    /// </summary>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateElse (children:XmlNode list) =
        Elem.template [ Attr.createBool "data-else" ] children

    /// <summary>
    /// Renders the children only while the condition is true. This is <see cref="templateIf"/> with a boolean expression.
    /// </summary>
    /// <param name="condition">A boolean expression, e.g. <c>Expr.read (Signal.rocket&lt;bool&gt; "on")</c></param>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateIf (condition:Expr<bool>, children:XmlNode list) =
        Rocket.directive ("data-if", Expr.toString condition, children)

    /// <summary>
    /// The next branch of a chain. This is <see cref="templateElseIf"/> with a boolean expression.
    /// </summary>
    /// <param name="condition">A boolean expression</param>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateElseIf (condition:Expr<bool>, children:XmlNode list) =
        Rocket.directive ("data-else-if", Expr.toString condition, children)

    /// <summary>
    /// Repeats a row once for each item in a list. The function that builds the row receives the item and the index as typed expressions,
    /// so the row cannot refer to a name that the loop does not define.
    /// Rocket calls the item <c>item</c> and the index <c>i</c> unless you pass other names.
    /// Rocket runs the directive on the server-rendered children of a light-DOM component when the page loads. In an open or closed shadow-DOM component it only runs for children that a later server patch sends.
    /// </summary>
    /// <param name="source">An expression that gives the list, e.g. <c>Expr.read (Signal.rocket&lt;string list&gt; "items")</c></param>
    /// <param name="row">Builds the content of one row from the item and the index</param>
    /// <param name="itemName">The name for the item; it must be a JavaScript identifier</param>
    /// <param name="indexName">The name for the index; it must be a JavaScript identifier</param>
    /// <returns>Element</returns>
    static member forEach (source:Expr<'T list>, row:Expr<'T> -> Expr<int> -> XmlNode list, ?itemName:string, ?indexName:string) =
        itemName |> Option.iter (Guard.javaScriptIdentifier "itemName" "Rocket.forEach needs an item name that is a JavaScript identifier, such as \"entry\"")
        indexName |> Option.iter (Guard.javaScriptIdentifier "indexName" "Rocket.forEach needs an index name that is a JavaScript identifier, such as \"n\"")
        let item = Expr.unsafeRaw<'T> (defaultArg itemName "item")
        let index = Expr.unsafeRaw<int> (defaultArg indexName "i")
        Rocket.templateForEncoded (Rocket.forExpression (Expr.toString source, itemName, indexName), row item index)
