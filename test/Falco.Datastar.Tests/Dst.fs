namespace Falco.Datastar.Tests

open System

/// <summary>
/// Deterministic simulation testing. A randomized test takes all of its randomness from one seed, so a failure can be
/// replayed exactly.
/// </summary>
/// <remarks>
/// <para>
/// - By default each test runs the seeds 1 to 25, so a normal run is fast and the same every time.
/// - DST_SEEDS=2000 runs the seeds 1 to 2000, to look for rare cases.
/// - DST_SEED=417 runs only seed 417, to replay a failure.
/// </para>
/// <para>
/// The generator is <see cref="Generator"/>, not <c>System.Random</c>, so a replay means the same case on any runtime
/// and after any upgrade. <c>GeneratorTests</c> pins the values a seed gives, so that cannot drift unnoticed.
/// </para>
/// <para>
/// A failure message names the seed and the command that replays it.
/// </para>
/// </remarks>
module Dst =

    let private variable (name: string) =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> None
        | value ->
            match Int32.TryParse value with
            | true, parsed when parsed > 0 -> Some parsed
            // A seed count that is not a number is a mistake in the command, and quietly running 25 seeds would hide it
            | _ -> failwith $"{name} is '{value}', which is not a count of seeds of at least 1"

    /// The seeds that a test runs
    let seeds () =
        match variable "DST_SEED", variable "DST_SEEDS" with
        | Some seed, _ -> [ seed ]
        | None, Some count -> [ 1 .. count ]
        | None, None -> [ 1 .. 25 ]

    /// Runs the test once for each seed, with a generator that starts from the seed.
    /// <c>testName</c> is a part of the name of the xunit test, so that the message can say how to run it again.
    let run (testName: string) (test: Generator -> unit) =
        for seed in seeds () do
            try
                test (Generator.ofSeed seed)
            with error ->
                raise (
                    Exception(
                        $"DST failure with seed {seed}. Replay it with: DST_SEED={seed} dotnet test test/Falco.Datastar.Tests -c Release --filter \"FullyQualifiedName~{testName}\"{Environment.NewLine}{error.Message}",
                        error
                    )
                )

    /// One of the choices
    let pick<'T> (generator: Generator) (choices: 'T array) = Generator.pick choices generator

    /// A value in [0, bound)
    let intBelow bound generator = Generator.intBelow bound generator

    /// A value in [lowInclusive, highExclusive)
    let intBetween lowInclusive highExclusive generator =
        Generator.intBetween lowInclusive highExclusive generator
