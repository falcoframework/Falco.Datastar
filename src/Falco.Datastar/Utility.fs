namespace Falco.Datastar

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Text
open System.Text.Json

module internal String =
    let newLines = [| "\r\n"; "\n"; "\r" |]
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

type StringExt() =
    [<Extension>]
    static member ToKebab (string:string) = string |> String.toKebab

module internal Bool =
    let inline eitherOr trueThing falseThing bool =
        match bool with
        | true -> trueThing
        | _ -> falseThing

module internal Utility =

    /// <summary>
    /// Takes a list of items, calls the serializer on each, joins them together with the separator and prefixes with the separator
    /// Will return empty string if the list is empty or serializes to empty strings
    /// </summary>
    let slugifyModifiers<'T> (itemSerializer:'T -> string) (separator:string) (items:'T list) =
        let serializedList =
            items
            |> List.map itemSerializer
            |> List.distinct
            |> List.where String.IsPopulated
        if serializedList.Length > 0
        then separator + (serializedList |> String.concat separator)
        else ""

    let rec jsonElementAtPath (jsonElement:JsonElement) (path:string list) =
        try
            match path with
            | [] -> ValueNone
            | [ key ] ->
                let didGetProperty, property = jsonElement.TryGetProperty(key)
                if didGetProperty then ValueSome property else ValueNone
            | key :: keys ->
                let didGetProperty, property = jsonElement.TryGetProperty(key)
                if didGetProperty then (jsonElementAtPath property keys) else ValueNone
        with _ -> ValueNone



    let numberTypes = HashSet<System.Type>(seq {
        typedefof<SByte>;  typedefof<Byte>
        typedefof<Int16>;  typedefof<UInt16>
        typedefof<Int32>;  typedefof<UInt32>
        typedefof<Int64>;  typedefof<UInt64>
        typedefof<IntPtr>; typedefof<UIntPtr>
        typedefof<Single>; typedefof<Double>; typedefof<Decimal>
    })
