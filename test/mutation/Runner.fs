namespace Falco.Datastar.Mutation

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

/// Runs the mutants against the unit tests, and reports what the suite catches and what it does not.
module Runner =

    let private sourceDirectory = "src/Falco.Datastar"
    let private libraryProject = "src/Falco.Datastar"
    let private testProject = "test/Falco.Datastar.Tests"
    let private backupSuffix = ".mutation-backup"

    /// Runs a command from the repository root and gives back its exit code
    let private run (executable: string) (arguments: string) =
        let info =
            ProcessStartInfo(executable, arguments)
            |> fun i ->
                i.WorkingDirectory <- Directory.GetCurrentDirectory()
                i.RedirectStandardOutput <- true
                i.RedirectStandardError <- true
                i

        use started = Process.Start info
        // Both streams have to be drained before waiting, or a full pipe deadlocks the child
        let output = started.StandardOutput.ReadToEnd() + started.StandardError.ReadToEnd()
        started.WaitForExit()
        started.ExitCode

    /// A path under the library, so every file is named the same way in the list and here
    let private sourceFile (name: string) = Path.Combine(sourceDirectory, name)

    /// Copies each file a mutant touches, so it can be put back afterwards
    let private backUp (chosen: Mutant list) =
        chosen
        |> List.map (fun mutant -> sourceFile mutant.File)
        |> List.distinct
        |> List.map (fun file ->
            let backup = file + backupSuffix
            File.Copy(file, backup, true)
            file, backup)

    let private restore (backups: (string * string) list) =
        for (original, backup) in backups do
            File.Copy(backup, original, true)

    let private removeBackups (backups: (string * string) list) =
        for (_, backup) in backups do
            if File.Exists backup then File.Delete backup

    /// Puts the source back from the backups of a run that was killed before it could clean up after itself.
    /// A run that is killed outright cannot restore anything itself, so without this the next build would inherit a
    /// mutant. It returns whether there was anything to restore.
    let cleanUpAfterAKilledRun () =
        let backups = Directory.GetFiles(sourceDirectory, "*" + backupSuffix)
        for backup in backups do
            File.Copy(backup, backup.Replace(backupSuffix, ""), true)
            File.Delete backup
        printfn "Restored %d file(s) from a previous run that was killed" backups.Length
        backups.Length > 0

    /// Checks the list against the source: that each Find is there to be changed, that the change is a real one, and
    /// that no two of them change the same text. Nothing is built, so this takes a second, and it is the thing to run
    /// after editing either the library or this list.
    let checkTheList () =
        let problems = ResizeArray<string>()
        let seen = Dictionary<string, string>(StringComparer.Ordinal)

        for mutant in Mutants.all do
            let file = sourceFile mutant.File
            let original = if File.Exists file then File.ReadAllText file else ""

            if not (original.Contains mutant.Find) then
                problems.Add(
                    $"{mutant.Name}: the text it changes is not in {mutant.File}, so it is not testing anything. Update the list."
                )
            elif mutant.Find = mutant.Replace then
                problems.Add($"{mutant.Name}: it changes nothing.")
            else
                // The text has to appear once, so a mutant cannot quietly rewrite the same line somewhere else
                let occurrences = Regex.Matches(original, Regex.Escape mutant.Find).Count
                if occurrences <> 1 then
                    problems.Add(
                        $"{mutant.Name}: the text it changes appears {occurrences} times in {mutant.File}, so it changes more than one place."
                    )

            match seen.TryGetValue mutant.Find with
            | true, other -> problems.Add($"{mutant.Name}: it changes the same text as '{other}'.")
            | _ -> seen.[mutant.Find] <- mutant.Name

        // A file with no mutant at all is not a problem with this list, but saying so stops anybody reading
        // "all killed" as meaning the whole library is covered.
        let untouched =
            Directory.GetFiles(sourceDirectory, "*.fs")
            |> Array.map Path.GetFileName
            |> Set.ofArray
            |> Set.difference (Mutants.filesTouched |> Set.ofList)
            |> Set.toArray
            |> Array.sort

        printfn "checked %d mutants, %d problem(s)" Mutants.all.Length problems.Count
        for problem in problems do
            printfn "  %s" problem
        if untouched.Length > 0 then printfn "no mutant touches: %s" (String.Join(", ", untouched))

        problems.Count = 0

    /// How many lines of the library the mutants actually rewrite, so that the number killed is not read as more than
    /// it is. This is a hand-picked set of mistakes on hand-picked lines: a tripwire, not a coverage figure.
    let private lineCoverage (chosen: Mutant list) =
        let rewritten = chosen |> List.sumBy (fun m -> m.Find.Split('\n').Length)
        let total =
            Directory.GetFiles(sourceDirectory, "*.fs")
            |> Array.sumBy (fun file -> File.ReadAllLines(file).Length)
        rewritten, total

    /// Runs every chosen mutant, or only the ones whose name contains the given text
    let runMutants (filter: string option) =
        let chosen =
            match filter with
            | Some text when not (String.IsNullOrWhiteSpace text) ->
                Mutants.all
                |> List.filter (fun m -> m.Name.ToLowerInvariant().Contains(text.ToLowerInvariant()))
            | _ -> Mutants.all

        if List.isEmpty chosen then
            eprintfn "No mutant matches that filter."
            1
        else
            printfn "Running %d of %d mutants" chosen.Length Mutants.all.Length

            let backups = backUp chosen
            let mutable killed = 0
            let mutable equivalent = 0
            let survivors = ResizeArray<string>()
            let mistakes = ResizeArray<string>()

            try
                for mutant in chosen do
                    let file = sourceFile mutant.File
                    let original = File.ReadAllText file

                    if not (original.Contains mutant.Find) then
                        mistakes.Add(
                            $"{mutant.Name}: the text it changes is no longer in {mutant.File}. Run with --check."
                        )
                    else
                        File.WriteAllText(file, original.Replace(mutant.Find, mutant.Replace))
                        try
                            // Building first means a mutant that does not compile is reported as such, not as a pass
                            let built = run "dotnet" (sprintf "build %s -c Release --nologo -v quiet" libraryProject)

                            if built <> 0 then
                                // A change that does not compile is caught by the build, not by a test, so it proves nothing
                                mistakes.Add(
                                    $"{mutant.Name}: it does not compile, so it is not a mistake a test could catch."
                                )
                                printfn "  %-64s does not compile" mutant.Name
                            else
                                let tested =
                                    run
                                        "dotnet"
                                        (sprintf "test %s -c Release --framework net10.0 --nologo -v quiet" testProject)

                                if tested <> 0 then
                                    killed <- killed + 1
                                    printfn "  %-64s killed" mutant.Name
                                elif mutant.Equivalent then
                                    // A mutant the tests cannot tell from the original is a problem with this list,
                                    // not with the tests, and is reported apart from a gap
                                    equivalent <- equivalent + 1
                                    printfn "  %-64s equivalent, cannot be killed" mutant.Name
                                else
                                    survivors.Add(sprintf "  %s\n    breaks: %s" mutant.Name mutant.Breaks)
                                    printfn "  %-64s SURVIVED" mutant.Name
                        finally
                            // Put the source back before the next mutant, so each one is applied to the original
                            File.WriteAllText(file, original)

                restore backups
                removeBackups backups

                printfn ""
                printfn "killed %d of %d" killed chosen.Length
                if equivalent > 0 then
                    printfn "%d were equivalent, so no test can tell them from the original" equivalent

                let rewritten, total = lineCoverage chosen
                let share = 100.0 * float rewritten / float total
                printfn
                    "these rewrite %d of the %d lines in the library (%.1f%%), so this is a tripwire on those lines and not a coverage figure"
                    rewritten
                    total
                    share

                if mistakes.Count > 0 then
                    printfn ""
                    printfn "These mutants are mistakes in the list, not gaps in the tests:"
                    for problem in mistakes do
                        printfn "  %s" problem

                if survivors.Count > 0 then
                    printfn ""
                    printfn "These mutants survived, so the test suite does not catch them. Each one is a test to write:"
                    for survivor in survivors do
                        printfn "%s" survivor

                if mistakes.Count > 0 || survivors.Count > 0 then 1 else 0
            with error ->
                // A crash must not leave a mutated source tree behind
                restore backups
                removeBackups backups
                eprintfn "%s" error.Message
                2
