#!/usr/bin/env dotnet fsi

/// Type-checks every F# code block in README.md and CHANGELOG.md against the real library.
///
/// The README is what a maintainer reads to decide whether this binding can be trusted, and an example that no longer
/// compiles is the fastest way to lose them. Each block is checked here against the built library, so a rename, a changed
/// signature, or an example written against a function that has gone away is caught before a reviewer finds it.
///
/// A block is checked when it stands on its own, which means it starts a whole item such as `let demo = ...`. A block that
/// only shows rendered HTML, or a fragment of an expression, is not a whole item; mark one that is a fragment on purpose
/// with the comment README-EXCERPT so skipping it is deliberate rather than accidental.
///
/// This type-checks. It does not run the examples, so a snippet that compiles but does the wrong thing is still possible.
///
/// A block that continues from an earlier one cannot be checked alone, so it is listed in `excerpts` with the reason.
///
///   dotnet fsi test/readme-examples.fsx            check them
///   dotnet fsi test/readme-examples.fsx --verbose  name each block, so a failure can be found in the Markdown

open System
open System.IO
open System.Text.RegularExpressions

/// The repository root. This file lives in test/, so the documents are one level up.
let repository = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let documents = [ "README.md"; "CHANGELOG.md" ]

/// Where the test project was built, which has every assembly an example may name
let private libraryBin = Path.Combine(repository, "test", "Falco.Datastar.Tests", "bin", "Release", "net10.0")

/// Where the SDK put its shared frameworks, which is not always /usr/share/dotnet
let private dotnetRoot =
    let fromEnvironment =
        match Environment.GetEnvironmentVariable "DOTNET_ROOT" with
        | null | "" -> None
        | root -> Some root
    let fromWhere =
        // `command -v dotnet`, then the parent of the directory it is in
        let start = Diagnostics.ProcessStartInfo("sh", "-c \"command -v dotnet\"")
        start.RedirectStandardOutput <- true
        use located = Diagnostics.Process.Start start
        let path = located.StandardOutput.ReadToEnd().Trim()
        located.WaitForExit()
        if path = "" then None else Some(Path.GetDirectoryName(Path.GetDirectoryName path))
    match fromEnvironment with
    | Some root -> root
    | None -> defaultArg fromWhere "/usr/share/dotnet"

let private sharedFrameworkDirectory (name: string) =
    Directory.GetDirectories(Path.Combine(dotnetRoot, "shared", name))
    |> Array.filter (fun path -> (Path.GetFileName path).StartsWith "10.")
    |> Array.sortDescending
    |> Array.truncate 1
    |> Array.tryHead

/// The references each checked block is compiled against, as the `#r` lines that go in front of it.
///
/// The ASP.NET Core assemblies are not in the test project's output, because it is a library rather than a web app, and
/// `dotnet fsi` cannot be given a framework reference. They are taken from the shared framework instead, which is the
/// same set of assemblies a Falco web app compiles against.
let private referenceLines () =
    if not (Directory.Exists libraryBin) then
        failwithf "build the test project first, there is no %s" libraryBin

    let packageAssemblies =
        Directory.GetFiles(libraryBin, "*.dll")
        |> Array.filter (fun path -> Path.GetFileName path <> "Falco.Datastar.Tests.dll")

    let frameworkAssemblies =
        match sharedFrameworkDirectory "Microsoft.AspNetCore.App" with
        | None -> [||]
        | Some framework -> Directory.GetFiles(framework, "*.dll")

    // Sorted so the file is the same every run, and a failure is reproducible
    Array.append packageAssemblies frameworkAssemblies
    |> Array.distinct
    |> Array.sort
    |> Array.map (fun path -> sprintf "#r @\"%s\"" path)

/// The `fsharp` blocks of each document, with the line each one starts on
let private examples () =
    documents
    |> List.map (fun name -> Path.Combine(repository, name))
    |> List.filter File.Exists
    |> List.collect (fun path ->
        let text = File.ReadAllText path
        Regex.Matches(text, "(?ms)^```fsharp\r?\n(.*?)^```")
        |> Seq.cast<Match>
        |> Seq.map (fun block ->
            Path.GetFileName path, text.Substring(0, block.Index).Split('\n').Length, block.Groups.[1].Value)
        |> Seq.toList)

/// A block stands on its own when it starts an item, rather than showing HTML or the middle of an expression
let private isWholeItem (code: string) = code.TrimStart().StartsWith "let "

/// Some blocks are written to continue from earlier ones, so they name something the reader has just seen rather than
/// defining it. Those cannot be checked on their own and are listed here with why. Each is a deliberate exclusion, so
/// that a block that stops being one of these stops being skipped.
let private excerpts =
    [ // Uses the handleIndex defined in the example above it
      "let endpoints ="
      // Uses a counter the reader is expected to have of their own
      "let handleUpdates : HttpHandler"
      // Continues a handler whose context was named in an earlier block
      "let nonce = \"...\""
      // Wraps a handler in parentheses, which is an expression and not an item
      "let httpHandler : HttpHandler = (fun ctx"
      // Continues the patch options example, which ends in a call
      "let appendRows ="
      // Wraps a handler in parentheses, as the example above it does
      "let handleStream = (fun ctx" ]

/// Whether a block is one of those, matched on its first line so that a reworded body does not change the answer
let private isExcerpt (code: string) =
    let firstLine = code.TrimStart().Split('\n').[0].Trim()
    excerpts |> List.exists (fun opening -> firstLine.StartsWith opening)

/// The blocks to check. A document that has gone missing, or that lost its examples, would make this silently empty,
/// so the count is checked rather than trusted.
let private toCheck =
    examples ()
    |> List.filter (fun (_, _, code) -> isWholeItem code && not (isExcerpt code))

printfn "checking %d example blocks from %s" toCheck.Length (String.concat ", " documents)

if toCheck.IsEmpty then
    failwith "no example blocks were found, so nothing was checked; is the path to the documents wrong?"

/// Type-checks one block by running it through the F# compiler, and returns the errors it gave.
///
/// Each README example opens the modules it needs, because a reader copies one block on its own and it has to work. So the
/// check gives a block the same opening lines a getting-started page gives, and reports which ones were missing when it
/// fails, rather than a wall of "the value is not defined" for every name.
let private check (code: string) =
    let opening =
        [ "open Falco"
          "open Falco.Markup"
          "open Falco.Routing"
          "open Falco.Datastar"
          "open Falco.Datastar.SignalPath"
          "open Microsoft.AspNetCore.Builder"
          "open Microsoft.AspNetCore.ResponseCompression"
          "open Microsoft.Extensions.DependencyInjection"
          "open StarFederation.Datastar.FSharp" ]

    let attempt (before: string list) =
        let scratch = Path.Combine(Path.GetTempPath(), "readme-example.fsx")
        let contents = String.concat "\n" ((referenceLines () |> List.ofArray) @ before @ [ code ])
        File.WriteAllText(scratch, contents)
        let start = Diagnostics.ProcessStartInfo("dotnet", sprintf "fsi --nologo --quiet %s" scratch)
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        use started = Diagnostics.Process.Start start
        let output = started.StandardOutput.ReadToEnd()
        let errors = started.StandardError.ReadToEnd()
        started.WaitForExit()
        let succeeded = started.ExitCode = 0
        File.Delete scratch
        if succeeded then [||] else [| (output + errors).Trim() |]

    attempt opening


let verbose =
    Environment.GetCommandLineArgs() |> Array.exists (fun argument -> argument = "--verbose")
if verbose then
    for (file, line, _) in toCheck do
        printfn "  %s:%d" file line

let broken =
    toCheck
    |> List.choose (fun (file, line, code) ->
        match check code with
        | [||] -> None
        | errors -> Some(file, line, errors))

match broken with
| [] ->
    printfn "all of them type-check"
| _ ->
    printfn ""
    for (file, line, errors) in broken do
        printfn "  %s:%d" file line
        for error in errors do
            printfn "%s" error
    printfn ""
    printfn "%d of %d example blocks do not type-check" broken.Length toCheck.Length
