namespace Falco.Datastar

open System
open System.Buffers
open System.Globalization
open System.Text
open System.Text.RegularExpressions

module internal String =
    let newLines = [| "\r\n"; "\n"; "\r" |]

    /// A copy of Datastar's own kebab function (library/src/utils/text.ts). Rocket uses it to turn a prop name into an attribute name.
    /// Unlike toKebab, it also splits acronyms and digits: "innerHTML" becomes "inner-html" and "pos3d" becomes "pos-3-d".
    let computeDatastarKebab (value:string) =
        let replace (pattern:string) (replacement:string) (options:RegexOptions) (input:string) =
            Regex.Replace(input, pattern, replacement, options)
        // CultureInvariant on the two case-insensitive ones: the JavaScript they copy has no culture, but .NET's
        // IgnoreCase alone folds by the current culture, so under tr-TR an I no longer matches [a-z] and "row1I"
        // would become "row1i" where Rocket reads "row1-i". The cache would then keep the wrong attribute name.
        let ignoreCase = RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant
        value
        |> replace "([A-Z]+)([A-Z][a-z])" "$1-$2" RegexOptions.None
        |> replace "([a-z0-9])([A-Z])" "$1-$2" RegexOptions.None
        |> replace "([a-z])([0-9]+)" "$1-$2" ignoreCase
        |> replace "([0-9]+)([a-z])" "$1-$2" ignoreCase
        |> replace "[\\s_]+" "-" RegexOptions.None
        |> fun kebab -> kebab.ToLowerInvariant()

    /// Prop names are written in code and repeat on every render, so each one is worked out once. The cache stops growing at 1000 names, in case a name ever comes from user input.
    let kebabCache = System.Collections.Concurrent.ConcurrentDictionary<string, string>()

    let datastarKebab (value:string) =
        match kebabCache.TryGetValue value with
        | true, kebab -> kebab
        | false, _ ->
            let kebab = computeDatastarKebab value
            if kebabCache.Count < 1000 then kebabCache.TryAdd(value, kebab) |> ignore
            kebab
    let split (delimiters:string seq) (line:string) = line.Split(delimiters |> Seq.toArray, StringSplitOptions.None)
    let IsPopulated = String.IsNullOrWhiteSpace >> not
    let toKebab (pascalString:string) =
        (StringBuilder(), pascalString.ToCharArray())
        ||> Seq.fold (fun stringBuilder chr ->
            if Char.IsUpper(chr)
            then stringBuilder.Append("-").Append(Char.ToLower(chr))
            else stringBuilder.Append(chr)
            )
        |> _.Replace("-", "", 0, 1).ToString()

/// Builds JavaScript literals that are safe inside a double-quoted HTML attribute.
/// Falco.Markup does not escape attribute values, so anything put into an expression must be escaped here.
module internal Js =
    /// The characters that need escaping in an attribute, and in a single-quoted JavaScript string inside one.
    /// Most text has none of them, and then it is returned as it is, without a copy.
    /// An HTML parser changes a carriage return in an attribute value into a line feed, and a NUL character into U+FFFD, so those two are written as escapes too.
    let attributeSpecials = SearchValues.Create "&<>\"\r\000"
    let stringSpecials = SearchValues.Create "\\'\n\r\000&<>\""

    let attrEncode (value:string) =
        match value.AsSpan().IndexOfAny attributeSpecials with
        | -1 -> value
        | first ->
            let builder = StringBuilder(value.Length + 16).Append(value, 0, first)
            for index in first .. value.Length - 1 do
                match value.[index] with
                | '&' -> builder.Append "&amp;" |> ignore
                | '<' -> builder.Append "&lt;" |> ignore
                | '>' -> builder.Append "&gt;" |> ignore
                | '"' -> builder.Append "&quot;" |> ignore
                // A carriage return written as a character reference is kept. A NUL cannot be kept in an attribute, so it is written as the character that HTML would give it.
                | '\r' -> builder.Append "&#13;" |> ignore
                | '\000' -> builder.Append '\uFFFD' |> ignore
                | other -> builder.Append other |> ignore
            builder.ToString()

    let stringLiteral (value:string) =
        match value.AsSpan().IndexOfAny stringSpecials with
        | -1 -> String.Concat("'", value, "'")
        | first ->
            let builder = StringBuilder(value.Length + 18).Append('\'').Append(value, 0, first)
            for index in first .. value.Length - 1 do
                match value.[index] with
                | '\\' -> builder.Append "\\\\" |> ignore
                | '\'' -> builder.Append "\\'" |> ignore
                | '\n' -> builder.Append "\\n" |> ignore
                | '\r' -> builder.Append "\\r" |> ignore
                | '\000' -> builder.Append "\\u0000" |> ignore
                | '&' -> builder.Append "&amp;" |> ignore
                | '<' -> builder.Append "&lt;" |> ignore
                | '>' -> builder.Append "&gt;" |> ignore
                | '"' -> builder.Append "&quot;" |> ignore
                | other -> builder.Append other |> ignore
            builder.Append('\'').ToString()

    /// A regular expression source as a JavaScript regular expression literal, such as /a\/b/. A literal cannot hold a slash or a line break as they are,
    /// so they are escaped. A backslash and the character after it are kept together, so an escape that is already there stays as it is.
    /// The text is not encoded for an attribute.
    let regexLiteral (pattern:string) =
        let builder = StringBuilder(pattern.Length + 4).Append('/')
        let mutable afterBackslash = false
        for character in pattern do
            match afterBackslash, character with
            | true, '\n' -> builder.Append 'n' |> ignore; afterBackslash <- false
            | true, '\r' -> builder.Append 'r' |> ignore; afterBackslash <- false
            // U+2028 and U+2029 are line terminators, and a JavaScript regular expression literal cannot contain one.
            // Handling them here rather than letting them fall to the catch-all is the whole point: a backslash in
            // front of one would otherwise leave it raw and produce a literal that does not parse.
            | true, '\u2028' -> builder.Append "u2028" |> ignore; afterBackslash <- false
            | true, '\u2029' -> builder.Append "u2029" |> ignore; afterBackslash <- false
            | true, other -> builder.Append other |> ignore; afterBackslash <- false
            | false, '\\' -> builder.Append '\\' |> ignore; afterBackslash <- true
            | false, '/' -> builder.Append "\\/" |> ignore
            | false, '\n' -> builder.Append "\\n" |> ignore
            | false, '\r' -> builder.Append "\\r" |> ignore
            | false, '\000' -> builder.Append "\\u0000" |> ignore
            | false, '\u2028' -> builder.Append "\\u2028" |> ignore
            | false, '\u2029' -> builder.Append "\\u2029" |> ignore
            | false, other -> builder.Append other |> ignore
        if afterBackslash then
            raise (ArgumentException($"The pattern '{pattern}' ends with a backslash, so it is not a regular expression. Remove the backslash, or write two of them to match a backslash."))
        builder.Append('/').ToString()

    /// A regular expression source that goes in a JSON string. Datastar removes a slash at the start and at the end of a string pattern,
    /// as if it were a literal like /a/, so a slash that is part of the pattern is protected by an empty group.
    let regexString (pattern:string) =
        let start = match pattern.StartsWith '/' with | true -> "(?:)" | false -> ""
        let finish = match pattern.EndsWith '/' with | true -> "(?:)" | false -> ""
        start + pattern + finish

    /// camelCase names, to match the JavaScript objects Rocket props are read into. This is one shared instance, because creating options on every call is slow.
    let webJsonOptions = System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)

    /// A JavaScript number. NaN and the infinities are literals in JavaScript, so this never raises, and it always uses a dot.
    let number (value:float) =
        match Double.IsNaN value, Double.IsPositiveInfinity value, Double.IsNegativeInfinity value with
        | true, _, _ -> "NaN"
        | _, true, _ -> "Infinity"
        | _, _, true -> "-Infinity"
        | _ -> value.ToString("R", CultureInfo.InvariantCulture)

    /// Strings become single-quoted literals. Numbers, including NaN and the infinities, and booleans are written as they are. Everything else is written as JSON
    let literal<'T> (value:'T) =
        match box value with
        | :? string as text -> stringLiteral text
        | :? float as decimalNumber -> number decimalNumber
        | :? float32 as singleNumber when Single.IsFinite singleNumber -> singleNumber.ToString("R", CultureInfo.InvariantCulture)
        | :? float32 as singleNumber -> number (float singleNumber)
        | _ -> System.Text.Json.JsonSerializer.Serialize<'T>(value) |> attrEncode

/// Refuses input that Datastar cannot use, with a message that says what to do
module internal Guard =
    /// Characters that end an attribute name in HTML: whitespace, control characters, and the punctuation ", ', `, <, >, / and =
    let private nameEnders =
        let whitespaceAndControl =
            [ yield! seq { 0 .. 32 }
              yield! seq { 127 .. 160 }
              yield 0x1680
              yield! seq { 0x2000 .. 0x200A }
              yield! [ 0x2028; 0x2029; 0x202F; 0x205F; 0x3000 ] ]
            |> List.map char
        SearchValues.Create(String(Array.ofList whitespaceAndControl) + "\"'`<>/=")

    let notBlank (parameter:string) (message:string) (value:string) =
        if String.IsNullOrWhiteSpace value then raise (ArgumentException(message, parameter))

    /// Refuses text that is not a JavaScript identifier, such as item or $row. <c>message</c> says what was expected, and the text that was given is added to it.
    let javaScriptIdentifier (parameter:string) (message:string) (value:string) =
        if isNull value || not (Regex.IsMatch(value, @"^[A-Za-z_$][A-Za-z0-9_$]*\z")) then
            raise (ArgumentException($"{message}, but it is '{value}'.", parameter))

    /// A signal reference as an expression writes it: one or more dollars, then parts made of letters, digits and underscores, separated by dots.
    /// This is checked where such a name goes into the output as code rather than as text, so that it cannot bring anything else with it.
    /// Each part starts with a letter, an underscore or a dollar, because that is how Datastar's own parser reads a signal reference.
    let private signalReferencePattern = Regex(@"^\$+[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\z", RegexOptions.Compiled)

    let signalReference (parameter:string) (value:string) =
        if isNull value || not (signalReferencePattern.IsMatch value) then
            raise (ArgumentException($"The signal reference '{value}' cannot be used here. It is written into the page as code, so it has to be the name of a signal, such as \"$controller\", \"$_controller\" or \"$form.controller\". It cannot have whitespace, an operator, a quote or any other text.", parameter))

    let private refuseName (what:string) (name:string) (reason:string) (remedy:string) : unit =
        raise (ArgumentException($"The {what} '{name}' cannot be used in a data- attribute name, because {reason}. {remedy}"))

    /// Refuses a name that cannot go into the name of an attribute as it is. Falco.Markup does not escape attribute names, so a quote or a space
    /// would end the name early and could add attributes of its own. Datastar reads a double underscore as the start of a modifier.
    /// <c>what</c> says what the name is for, such as "class name". <c>hasModifiers</c> is true when modifiers follow the name.
    /// This runs for every attribute that is rendered, so it allocates nothing when the name is fine.
    let attributeName (what:string) (hasModifiers:bool) (name:string) =
        if String.IsNullOrWhiteSpace name then
            refuseName what name "it is empty" "Write a name."
        match name.AsSpan().IndexOfAny nameEnders with
        | -1 -> ()
        | position ->
            let character = name.[position]
            let isSpace = Char.IsWhiteSpace character || Char.IsControl character
            let described = match isSpace with | true -> "whitespace or a control character" | false -> $"'{character}'"
            refuseName what name $"it contains {described}" "HTML ends an attribute name there, and what follows would become attributes of their own. Use letters, digits, '-', '.' and ':'."
        match (match name.Contains '_' with | true -> name.IndexOf("__", StringComparison.Ordinal) | false -> -1) with
        | -1 -> ()
        | position ->
            let classHint = match what with | "class name" -> " To toggle a class like this, write the object form yourself, for example Attr.create \"data-class\" \"{'card__title': $isActive}\"." | _ -> ""
            refuseName what name "it contains '__'" $"Datastar reads '__' in an attribute name as the start of a modifier, so it would read '{name}' as '{name.Substring(0, position)}' with the modifier '{name.Substring(position + 2)}'. Choose a name without '__'.{classHint}"
        if hasModifiers && name.EndsWith '_' then
            refuseName what name "it ends with an underscore next to a modifier" "Datastar would read the underscore together with the two that start the modifier, and lose part of the name. Remove the trailing underscore."

module internal Bool =
    let inline eitherOr trueThing falseThing bool =
        match bool with
        | true -> trueThing
        | _ -> falseThing

module Option =
    let toValueOption = function
        | Some value -> ValueSome value
        | None -> ValueNone
