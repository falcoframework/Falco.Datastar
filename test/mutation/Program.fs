namespace Falco.Datastar.Mutation

open System
open System.IO

/// Mutation testing, driven from the command line.
module Program =

    /// <summary>
    /// Proves that the unit tests catch the mistakes this library could plausibly make.
    ///
    /// A suite can be green while the code it covers is wrong: a test asserts the output, and if the code drifts the
    /// assertion drifts with it, or the case is never reached. This makes a small, deliberate mistake in the source and
    /// runs the whole suite. A failing test kills the mutant, which shows the suite has teeth. A mutant that survives is
    /// a gap, and it says which test is missing.
    ///
    /// It is a separate program because a mutant rewrites the library and the tests have to run against the rebuilt
    /// assembly, which the test process cannot do while it is holding that assembly open.
    ///
    /// Stryker, the usual tool for .NET, does not support F# projects. On this solution it fails while reading the
    /// projects, because it resolves them the way it resolves C# ones.
    ///
    /// How to read the result: the run prints how many lines of the library its mutants actually rewrite, and it is a
    /// small share of it. This is a tripwire against a regression on those specific lines, not a measurement of how much
    /// of the library the tests reach.
    ///
    /// Usage, from the repository root:
    ///   dotnet run --project test/mutation              every mutant
    ///   dotnet run --project test/mutation -- setAll    only mutants whose name contains "setAll"
    ///   dotnet run --project test/mutation -- check     check the list against the source, building nothing
    ///   dotnet run --project test/mutation -- clean     put the source back, after a run that was killed
    ///
    /// The source is restored after every mutant, and again at the end whatever happens, so an interrupted run never
    /// leaves the working tree mutated. A run that is killed outright is the one case that cannot clean up after itself,
    /// so "clean" puts the source back from the backups it left behind, and a run refuses to start while any backup is
    /// left over. `git diff` is the check that the tree is as it should be.
    ///
    /// This rewrites files in src, so do not run it alongside anything else that builds this repository.
    /// </summary>
    [<EntryPoint>]
    let main arguments =
        let isClean = arguments |> Array.contains "clean"
        let isCheck = arguments |> Array.contains "check"

        if isClean then
            Runner.cleanUpAfterAKilledRun () |> ignore
            0
        elif isCheck then
            if Runner.checkTheList () then 0 else 1
        else
            // A backup file left over means an earlier run was killed, so the tree may already be mutated.
            let leftover = Directory.GetFiles("src/Falco.Datastar", "*.mutation-backup")
            if leftover.Length > 0 then
                eprintfn
                    "There are %d backup file(s) from a run that was killed, so the source may still be mutated. Run with clean first."
                    leftover.Length
                1
            else
                // A bare word is a filter on the mutant names, so a run can be narrowed while it is being fixed
                let filter: string option =
                    arguments |> Array.tryFind (fun a -> a <> "clean" && a <> "check")
                Runner.runMutants filter
