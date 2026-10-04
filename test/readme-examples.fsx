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

/// The `fsharp` blocks of each document, with the file, the line each one starts on, and whether the block asked to be
/// left alone with a README-EXCERPT comment.
let private examples () =
    documents
    |> List.map (fun name -> Path.Combine(repository, name))
    |> List.filter File.Exists
    |> List.collect (fun path ->
        let text = File.ReadAllText path
        Regex.Matches(text, "(?ms)^```fsharp\r?\n(.*?)^```")
        |> Seq.cast<Match>
        |> Seq.map (fun block ->
            let code = block.Groups.[1].Value
            // The marker is read from the block itself, so marking one is visible where a reader will look for it.
            let isExcerpt = code.Contains "README-EXCERPT"
            Path.GetFileName path, text.Substring(0, block.Index).Split('\n').Length, code, isExcerpt)
        |> Seq.toList)

/// The blocks to check. Every block is checked unless it carries the README-EXCERPT marker, so a block that cannot
/// stand on its own has to say so where the reader can see it, and a block that stops needing to says so by deleting
/// one comment line.
///
/// Nothing else decides what is skipped. Deciding from the shape of the code skipped most of the README without saying
/// so: a block beginning `open`, `type`, `match`, or any expression rather than `let` was quietly left unchecked while
/// the output said how many blocks were checked, which read as coverage.
let private toCheck =
    examples ()
    |> List.filter (fun (_, _, _, isExcerpt) -> not isExcerpt)

/// The blocks that asked to be left alone, with the reason written in the marker. Listed so the output says what was
/// not checked, rather than only what was.
let private excerpts =
    examples ()
    |> List.filter (fun (_, _, _, isExcerpt) -> isExcerpt)
    |> List.map (fun (file, line, code, _) ->
        // The reason is the block's own comment, so that marking a block says why in the place a reader will look.
        let reason =
            code.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun source -> source.Trim())
            |> Array.filter (fun source -> source.StartsWith "//")
            |> Array.map (fun source -> source.Substring(2).Trim())
            |> String.concat " "
        file, line, reason)

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
        // `dotnet fsi --use:file.fsx` loads the file into a session. Loading type-checks it, which is what this is for,
        // and the script is then never evaluated, so a block that calls .Run() does not start a web server. Running the
        // file instead, as this did, bound port 5000 and every block after it failed with AddressInUseException.
        let start = Diagnostics.ProcessStartInfo("dotnet", sprintf "fsi --nologo --quiet --use:%s" scratch)
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        use started = Diagnostics.Process.Start start
        // Both reads start before either is awaited. Reading stdout to the end first deadlocks as soon as stderr
        // fills its pipe while nothing is draining it, which a block with many errors does. The child deadlocks
        // instead of this one, and CI hangs rather than reporting.
        let output = started.StandardOutput.ReadToEndAsync()
        let errors = started.StandardError.ReadToEndAsync()
        started.WaitForExit()
        let succeeded = started.ExitCode = 0
        let text = (output.Result + errors.Result).Trim()
        File.Delete scratch
        if succeeded then [||] else [| text |]

    attempt opening


let verbose =
    Environment.GetCommandLineArgs() |> Array.exists (fun argument -> argument = "--verbose")

// Always reported, not only with --verbose: a block that is not checked is a gap in what this proves, and the only way
// to see the whole picture is for it to be in the output every run.
if excerpts.Length > 0 then
    printfn "not checked, marked README-EXCERPT:"
    for (file, line, reason) in excerpts do
        printfn "  %s:%d  %s" file line reason

if verbose then
    printfn "checking:"
    for (file, line, _, _) in toCheck do
        printfn "  %s:%d" file line

let broken =
    toCheck
    |> List.choose (fun (file, line, code, _) ->
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
