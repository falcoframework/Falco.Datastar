namespace Falco.Datastar

open System
open System.Globalization
open System.Text.Json

/// The codec a Rocket component gave a prop. Write it with its type name, e.g. RocketPropType.Number.
/// Rocket may add codecs, so a name this library does not know is kept as Other.
[<RequireQualifiedAccess>]
type RocketPropType =
    | String
    | Number
    | Boolean
    | Date
    | Json
    | Js
    | Binary
    | Array
    | Tuple
    | Object
    | OneOf
    | Custom
    | Other of string

/// Whether a component event is a plain event or a custom event with a payload. Write it with its type name.
[<RequireQualifiedAccess>]
type RocketEventKind =
    | Event
    | CustomEvent
    | Other of string

/// The documentation a component author gave a prop with the docs(...) method of its codec
type RocketPropDocs =
    { Description: string voption
      Label: string voption
      /// The kind of control a tool should show: auto, text, textarea, number, boolean or select
      Control: string voption
      Placeholder: string voption }

/// A prop of a Rocket component, as Rocket described it in a manifest
type RocketManifestProp =
    { Name: string
      /// The attribute the prop is read from, e.g. max-count for maxCount
      Attribute: string
      Type: RocketPropType
      /// The value the prop has when the attribute is missing, as JSON. It is null when the component's codec has no default
      Default: JsonElement
      Required: bool
      /// The values a oneOf prop allows
      Values: JsonElement list voption
      Docs: RocketPropDocs voption }

/// A slot of a Rocket component, as its author documented it
type RocketManifestSlot =
    { Name: string
      Description: string voption }

/// An event of a Rocket component, as its author documented it
type RocketManifestEvent =
    { Name: string
      Kind: RocketEventKind
      Bubbles: bool voption
      Composed: bool voption
      Description: string voption }

/// One Rocket component in a manifest
type RocketManifestComponent =
    { Tag: string
      Props: RocketManifestProp list
      Slots: RocketManifestSlot list
      Events: RocketManifestEvent list }

/// What Rocket's publishRocketManifests posts: every component the page defined, sorted by tag
type RocketManifestDocument =
    { Version: int
      GeneratedAt: DateTimeOffset
      Components: RocketManifestComponent list }

/// Why a manifest could not be read. The first problem found is the one reported.
/// A case with a place says where in the manifest the problem is, for example: the prop "count" of my-card.
[<RequireQualifiedAccess>]
type RocketManifestError =
    /// The text is not JSON
    | NotJson of reason:string
    /// The body is larger than the limit, so it was not read
    | TooLarge of limitBytes:int
    /// A property that the manifest needs is not there
    | Missing of property:string * place:string
    /// A property is there, but it is not the kind of value that it should be, such as text, a number, a date or a list
    | WrongKind of property:string * expected:string * place:string
    /// The manifest has a version that this library does not read
    | UnsupportedVersion of found:int * supported:int
    /// The body is JSON, but it is not an object
    | NotAnObject
    /// The connection failed before the whole body was read, for example because the browser closed it
    | ConnectionFailed of reason:string
    /// The request was cancelled before the whole body was read
    | Cancelled
    with
    /// A message for a person, that says what is wrong. When the fix is not obvious, it says what to do
    member error.Message =
        match error with
        | ConnectionFailed reason -> $"The request body could not be read: {reason}. The connection probably closed before the page finished posting, so there may be nobody to answer."
        | Cancelled -> "The request was cancelled before the whole body was read, so there may be nobody to answer."
        | NotAnObject -> "The manifest must be a JSON object with a version, a generatedAt and a list of components, but the body is something else. Check that the request comes from publishRocketManifests."
        | RocketManifestError.NotJson reason -> $"The manifest is not valid JSON: {reason}"
        | RocketManifestError.TooLarge limitBytes ->
            $"The manifest is larger than {limitBytes / 1024 / 1024} MiB, so it was not read. The manifest of a page is far smaller than that. Check what is posting to this endpoint."
        | RocketManifestError.Missing (property, place) -> $"The manifest has no \"{property}\" in {place}"
        | RocketManifestError.WrongKind (property, expected, place) -> $"The \"{property}\" in {place} is not {expected}"
        | RocketManifestError.UnsupportedVersion (found, supported) ->
            $"This library reads Rocket manifest version {supported}, but the document is version {found}. Update Falco.Datastar to a version that reads it, or check that the page and this server use compatible versions of Datastar."

/// <summary>
/// Reads the document that Rocket's <c>publishRocketManifests</c> posts to your server: one entry for every component the page defined,
/// with its props (from their codecs), slots and events. A docs build or a component registry can store it.
/// https://github.com/starfederation/datastar/blob/v1.0.4/library/src/rocket/runtime.ts
/// </summary>
[<RequireQualifiedAccess>]
module RocketManifest =
    let private property (name:string) (element:JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty name with
            | true, value -> ValueSome value
            | false, _ -> ValueNone
        | _ -> ValueNone

    let private required (name:string) (place:string) (element:JsonElement) =
        match property name element with
        | ValueSome value -> Ok value
        | ValueNone -> Error (RocketManifestError.Missing (name, place))

    let private text (name:string) (place:string) (element:JsonElement) =
        required name place element
        |> Result.bind (fun value ->
            match value.ValueKind with
            | JsonValueKind.String -> Ok (value.GetString())
            | _ -> Error (RocketManifestError.WrongKind (name, "text", place)))

    let private jsonNull = JsonDocument.Parse("null").RootElement

    // A property that is missing is not an error, but a property that is there with the wrong kind of value is.
    // Reporting ValueNone for "required":"yes" would say the prop is not required, which is a different claim from the
    // one the manifest makes, and the reader could not tell them apart.
    let private optionalText (name:string) (place:string) (element:JsonElement) =
        match property name element with
        | ValueNone -> Ok ValueNone
        | ValueSome value when value.ValueKind = JsonValueKind.String -> Ok (ValueSome (value.GetString()))
        | ValueSome value when value.ValueKind = JsonValueKind.Null -> Ok ValueNone
        | ValueSome _ -> Error (RocketManifestError.WrongKind (name, "text", place))

    let private optionalBool (name:string) (place:string) (element:JsonElement) =
        match property name element with
        | ValueNone -> Ok ValueNone
        | ValueSome value when value.ValueKind = JsonValueKind.True -> Ok (ValueSome true)
        | ValueSome value when value.ValueKind = JsonValueKind.False -> Ok (ValueSome false)
        | ValueSome value when value.ValueKind = JsonValueKind.Null -> Ok ValueNone
        | ValueSome _ -> Error (RocketManifestError.WrongKind (name, "true or false", place))

    let private items (name:string) (place:string) (element:JsonElement) =
        match property name element with
        | ValueNone -> Ok []
        | ValueSome value when value.ValueKind = JsonValueKind.Array -> Ok (value.EnumerateArray() |> List.ofSeq)
        | ValueSome _ -> Error (RocketManifestError.WrongKind (name, "a list", place))

    /// Reads every element and stops at the first error. The reader is given the position of the element, counting from 1, to say where an error is.
    let private readAll (read:int -> JsonElement -> Result<'T, RocketManifestError>) (elements:JsonElement list) =
        elements
        |> List.indexed
        |> List.fold (fun collected (index, element) -> collected |> Result.bind (fun readSoFar -> read (index + 1) element |> Result.map (fun item -> item :: readSoFar))) (Ok [])
        |> Result.map List.rev

    /// Says which entry an error is about: by its name when it has one, and by its position when it does not.
    let private describe (kind:string) (owner:string) (position:int) (element:JsonElement) =
        // The name is only used to make a better error message, so a name of the wrong kind falls back to the
        // position rather than failing the whole read.
        match property "name" element with
        | ValueSome name when name.ValueKind = JsonValueKind.String -> $"the {kind} \"{name.GetString()}\" of {owner}"
        | _ -> $"{kind} {position} of {owner}"

    let private propType (name:string) =
        match name with
        | "string" -> RocketPropType.String
        | "number" -> RocketPropType.Number
        | "boolean" -> RocketPropType.Boolean
        | "date" -> RocketPropType.Date
        | "json" -> RocketPropType.Json
        | "js" -> RocketPropType.Js
        | "binary" -> RocketPropType.Binary
        | "array" -> RocketPropType.Array
        | "tuple" -> RocketPropType.Tuple
        | "object" -> RocketPropType.Object
        | "oneOf" -> RocketPropType.OneOf
        | "custom" -> RocketPropType.Custom
        | other -> RocketPropType.Other other

    let private eventKind (name:string) =
        match name with
        | "event" -> RocketEventKind.Event
        | "custom-event" -> RocketEventKind.CustomEvent
        | other -> RocketEventKind.Other other

    /// Reads properties in order and stops at the first error, the way a required property is read. The values are
    /// boxed into a list because a list is the only F# collection that can hold a different type per element; the
    /// caller unboxes them where it builds the record, where each one is known to be the type it read.
    let private readInOrder (reads:Result<obj, RocketManifestError> list) =
        reads
        |> List.fold
            (fun readSoFar next -> readSoFar |> Result.bind (fun soFar -> next |> Result.map (fun value -> value :: soFar)))
            (Ok [])
        |> Result.map List.rev

    let private readDocs (place:string) (element:JsonElement) =
        match property "docs" element with
        | ValueSome docs when docs.ValueKind = JsonValueKind.Object ->
            [ "description"; "label"; "control"; "placeholder" ]
            |> List.map (fun name -> optionalText name place docs |> Result.map box)
            |> readInOrder
            |> Result.map (fun values ->
                ValueSome { Description = values.[0] :?> string voption
                            Label = values.[1] :?> string voption
                            Control = values.[2] :?> string voption
                            Placeholder = values.[3] :?> string voption })
        | _ -> Ok ValueNone

    let private readProp (tag:string) (position:int) (element:JsonElement) =
        let place = describe "prop" tag position element
        [ text "name" place element |> Result.map box
          text "attribute" place element |> Result.map box
          text "type" place element |> Result.map box
          optionalBool "required" place element |> Result.map box
          readDocs place element |> Result.map box ]
        |> readInOrder
        |> Result.map (fun values ->
            let name = values.[0] :?> string
            let attribute = values.[1] :?> string
            let typeName = values.[2] :?> string
            let required = values.[3] :?> bool voption
            let docs = values.[4] :?> RocketPropDocs voption
            // A codec with no default leaves the key out of the JSON, so a missing one is a null default
            let defaultValue = property "default" element |> ValueOption.defaultValue jsonNull
            { Name = name
              Attribute = attribute
              Type = propType typeName
              Default = defaultValue.Clone()
              Required = required |> ValueOption.defaultValue false
              Values =
                match property "values" element with
                | ValueSome values when values.ValueKind = JsonValueKind.Array ->
                    ValueSome (values.EnumerateArray() |> Seq.map (fun value -> value.Clone()) |> List.ofSeq)
                | _ -> ValueNone
              Docs = docs })

    let private readSlot (tag:string) (position:int) (element:JsonElement) =
        let place = describe "slot" tag position element
        [ text "name" place element |> Result.map box
          optionalText "description" place element |> Result.map box ]
        |> readInOrder
        |> Result.map (fun values ->
            { Name = values.[0] :?> string
              Description = values.[1] :?> string voption })

    let private readEvent (tag:string) (position:int) (element:JsonElement) =
        let place = describe "event" tag position element
        [ text "name" place element |> Result.map box
          optionalText "kind" place element |> Result.map box
          optionalBool "bubbles" place element |> Result.map box
          optionalBool "composed" place element |> Result.map box
          optionalText "description" place element |> Result.map box ]
        |> readInOrder
        |> Result.map (fun values ->
            let name = values.[0] :?> string
            let kind = values.[1] :?> string voption
            { Name = name
              Kind = kind |> ValueOption.map eventKind |> ValueOption.defaultValue RocketEventKind.Event
              Bubbles = values.[2] :?> bool voption
              Composed = values.[3] :?> bool voption
              Description = values.[4] :?> string voption })

    let private readComponent (position:int) (element:JsonElement) =
        text "tag" $"component {position}" element
        |> Result.bind (fun tag ->
            let place = $"the component \"{tag}\""
            items "props" place element
            |> Result.bind (readAll (readProp tag))
            |> Result.bind (fun props ->
                items "slots" place element
                |> Result.bind (readAll (readSlot tag))
                |> Result.bind (fun slots ->
                    items "events" place element
                    |> Result.bind (readAll (readEvent tag))
                    |> Result.map (fun events -> { Tag = tag; Props = props; Slots = slots; Events = events }))))

    let private supportedVersion = 1

    let private readVersion (root:JsonElement) =
        required "version" "the manifest" root
        |> Result.bind (fun version ->
            match version.ValueKind with
            | JsonValueKind.Number ->
                match version.TryGetInt32() with
                | true, number when number = supportedVersion -> Ok number
                | true, number -> Error (RocketManifestError.UnsupportedVersion (number, supportedVersion))
                | false, _ -> Error (RocketManifestError.WrongKind ("version", "a whole number", "the manifest"))
            | _ -> Error (RocketManifestError.WrongKind ("version", "a number", "the manifest")))

    let private readGeneratedAt (root:JsonElement) =
        text "generatedAt" "the manifest" root
        |> Result.bind (fun generatedAt ->
            match DateTimeOffset.TryParse(generatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, moment -> Ok moment
            | false, _ -> Error (RocketManifestError.WrongKind ("generatedAt", "a date", "the manifest")))

    let private readComponents (root:JsonElement) =
        required "components" "the manifest" root
        |> Result.bind (fun components ->
            match components.ValueKind with
            | JsonValueKind.Array -> readAll readComponent (components.EnumerateArray() |> List.ofSeq)
            | _ -> Error (RocketManifestError.WrongKind ("components", "a list", "the manifest")))

    let private readDocument (root:JsonElement) =
        match root.ValueKind with
        | JsonValueKind.Object ->
            readVersion root
            |> Result.bind (fun version ->
                readGeneratedAt root
                |> Result.bind (fun generatedAt ->
                    readComponents root
                    |> Result.map (fun components -> { Version = version; GeneratedAt = generatedAt; Components = components })))
        | _ -> Error RocketManifestError.NotAnObject

    /// <summary>
    /// Reads the JSON that <c>publishRocketManifests</c> posted. It returns an error when the text is not JSON,
    /// when a required property is missing or is the wrong kind of value, or when the document has a version other than 1.
    /// </summary>
    /// <param name="json">The body of the request</param>
    let parse (json:string) : Result<RocketManifestDocument, RocketManifestError> =
        match isNull json with
        | true -> Error (RocketManifestError.NotJson "there is no text to read")
        | false ->
            try
                use document = JsonDocument.Parse json
                readDocument document.RootElement
            with
            | :? JsonException as error -> Error (RocketManifestError.NotJson error.Message)
            // A string with half of a surrogate pair is JSON that .NET cannot turn into text
            | :? InvalidOperationException as error -> Error (RocketManifestError.NotJson error.Message)
            // The text that is passed in can have half of a surrogate pair too, and then it cannot be turned into UTF-8
            | :? ArgumentException as error -> Error (RocketManifestError.NotJson error.Message)
