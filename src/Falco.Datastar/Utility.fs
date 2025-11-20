namespace Falco.Datastar

open System
open System.Text

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

module internal Bool =
    let inline eitherOr trueThing falseThing bool =
        match bool with
        | true -> trueThing
        | _ -> falseThing

module Option =
    let toValueOption = function
        | Some value -> ValueSome value
        | None -> ValueNone
