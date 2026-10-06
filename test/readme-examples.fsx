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
open System.Threading

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

/// The references each checked block is compiled against, as the `--reference` paths fsc takes.
///
/// The ASP.NET Core assemblies are not in the test project's output, because it is a library rather than a web app, and
/// a bare fsc cannot be given a framework reference. They are taken from the shared framework instead, which is the same
/// set of assemblies a Falco web app compiles against.
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
    |> Array.map (fun path -> sprintf "--reference:%s" path)

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

/// A guard against the checker quietly becoming a checker of nothing.
///
/// The first version of this skipped every block that did not begin with `let`, which was 58 of the 71 blocks in the
/// README, and still printed a line saying how many it had checked. So the counts are asserted rather than printed: a
/// change to the Markdown that stops this from finding blocks is a failure here, not a green run that proves less.
///
/// The floor is not a guess about how many blocks there should be. It is well under the current count, so adding
/// examples is fine; what fails is a drop large enough to suggest the extraction or the marker logic changed.
let private minimumBlocksExpected = 40

if toCheck.Length < minimumBlocksExpected then
    failwithf
        "only %d example blocks would be checked, which is far fewer than the %d expected; either the fence pattern no \
         longer matches the Markdown or blocks are being skipped without saying so"
        toCheck.Length
        minimumBlocksExpected

if toCheck.IsEmpty then
    failwith "no example blocks were found, so nothing was checked; is the path to the documents wrong?"

/// The opening lines every block is given. A reader copies one block on its own, so each has to name the modules it uses.
///
/// `System` and `FSharp.Core` are here because an example that writes a time span or a list expects them to be there, the
/// way they are in every F# script and in a file's auto-opens. They are deliberately generous: a block that fails should
/// fail because the example is wrong, not because the checker forgot an open. A block that needs an open beyond these
/// names it, which is the point of the check.
let private openingLines =
    [ "open System"
      "open Falco"
      "open Falco.Markup"
      "open Falco.Routing"
      "open Falco.Datastar"
      "open Falco.Datastar.SignalPath"
      "open Falco.Datastar.Selector"
      "open Microsoft.AspNetCore.Builder"
      "open Microsoft.AspNetCore.ResponseCompression"
      "open Microsoft.Extensions.DependencyInjection"
      "open StarFederation.Datastar.FSharp" ]

/// The F# compiler that ships with the SDK, which compiles and never evaluates, unlike fsi.
///
/// The newest SDK is used, so the compiler that reads the README is the one the library itself is built with. A block that
/// needs a newer language feature than an older compiler has is a documentation problem worth seeing.
///
/// The versions are compared as numbers rather than as text. Sorting the directory names puts "9.0.301" above "10.0.401",
/// because "9" is the larger character, so a plain sort picks the oldest compiler on the machine and quietly checks the
/// examples against a language version the project does not even target.
let private fscPath =
    let sdk = Path.Combine(dotnetRoot, "sdk")
    if not (Directory.Exists sdk) then
        failwithf "no SDK at %s, so there is no F# compiler to check the examples with" sdk

    /// The version as a comparable list of numbers, or None for a name that is not a release version
    let releaseVersion (name: string) =
        let parts = name.Split '.'
        if parts.Length < 2 then None
        elif parts |> Array.exists (fun part -> part.Contains "-" || part.Contains "+") then None
        else
            let numbers =
                parts
                |> Array.map (fun part ->
                    match System.Int32.TryParse part with
                    | true, number -> Some number
                    | _ -> None)
            // Every part has to be a number, so a directory such as "10.0.4-preview" is skipped rather than half read.
            if numbers |> Array.exists Option.isNone then None
            else Some(numbers |> Array.choose id)

    Directory.GetDirectories(sdk)
    |> Array.choose (fun path ->
        match releaseVersion (Path.GetFileName path) with
        | None -> None
        | Some version -> Some(version, path))
    |> Array.sortByDescending fst
    |> Array.tryFind (fun (_, path) -> File.Exists(Path.Combine(path, "FSharp", "fsc.dll")))
    |> Option.map (fun (_, path) -> Path.Combine(path, "FSharp", "fsc.dll"))
    |> Option.defaultWith (fun () -> failwithf "no F# compiler found under %s" sdk)

/// Distinguishes the scratch file each block is compiled from, so two blocks cannot share a file name
let mutable private scratchCounter = 0

/// Type-checks one block with the F# compiler, and returns the errors it gave.
///
/// Each README example opens the modules it needs, because a reader copies one block on its own and it has to work. So the
/// check gives a block the same opening lines a getting-started page gives, and reports which ones were missing when it
/// fails, rather than a wall of "the value is not defined" for every name.
///
/// The block is compiled, not run. `dotnet fsi` was used first and does not do that: --use loads the file and evaluates
/// it, so the getting-started example called .Run(), bound port 5000, and the checker hung there until it was killed,
/// which is what made an earlier version of this take half an hour. fsc compiles and never evaluates, so an example that
/// starts a web server, listens on a port, or writes a file costs nothing here. It matters that the script is named .fs
/// and wrapped in a module: fsc takes a program, not a script.
///
/// One compiler process per block. fsc takes several inputs at once, but every input shares one program, so two blocks
/// could see each other's definitions; a block that compiles only because an earlier one defined a name is a block that
/// does not work for a reader. Each block on its own is the property worth having.
///
/// stderr is read as it arrives, because a pipe nothing drains fills up and stops the child answering.
let private check (code: string) =
    // The file name carries a counter rather than a hash of the code: two blocks with identical text would otherwise
    // share one file name, and the second would overwrite the first while its compiler process is still reading it.
    let number = Interlocked.Increment &scratchCounter
    let scratch = Path.Combine(Path.GetTempPath(), sprintf "readme-example-%d.fs" number)
    File.WriteAllText(scratch, String.concat "\n" ([ "module ReadmeExample" ] @ openingLines @ [ code ]))

    // The arguments are passed as a list rather than joined into one string, so a path containing a space stays one
    // argument. Joining is how a checkout under "C:\Program Files" or "/home/a b" would fail on a machine that has one.
    let arguments =
        [ yield "--targetprofile:netcore"
          yield sprintf "--out:%s" (scratch + ".dll")
          yield "--nowarn:FS0064,FS0049,FS0025,FS1182,FS3370"
          yield! referenceLines ()
          yield scratch ]

    let start = Diagnostics.ProcessStartInfo("dotnet")
    // Everything goes on the list rather than into a joined Arguments string, so a path containing a space stays one
    // argument. The BCL refuses both being set, so Arguments is left alone and only the list is used.
    start.ArgumentList.Add fscPath
    for argument in arguments do
        start.ArgumentList.Add argument
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    use started = Diagnostics.Process.Start start
    let output = started.StandardOutput.ReadToEndAsync()
    let errors = started.StandardError.ReadToEndAsync()
    started.WaitForExit()
    let text = (output.Result + "\n" + errors.Result).Trim()
    for path in [ scratch; scratch + ".dll"; scratch + ".pdb" ] do
        if File.Exists path then File.Delete path
    // fsc reports its version banner on success too, so the exit code decides, not whether there was output.
    if started.ExitCode = 0 then [||]
    // The scratch path is noise in a message about a README line, so it is replaced with a name a reader can act on.
    else [| text.Replace(scratch, "the example") |]


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
    // A nonzero exit, because printing a failure and exiting zero is a green build. This runs in CI, and the whole point
    // of checking the examples on every build is that a broken one stops the build.
    exit 1
