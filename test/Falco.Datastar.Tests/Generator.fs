namespace Falco.Datastar.Tests

open System

/// <summary>
/// The generator the simulation tests draw from.
/// </summary>
/// <remarks>
/// <para>
/// It is SplitMix64 rather than <c>System.Random</c>, and that is the whole reason it exists. .NET does not promise to
/// keep <c>Random</c>'s algorithm, so a seed that reproduces a failure today may not after a runtime upgrade. The
/// algorithm here is written out, so a seed means the same case today and in two years on a runtime nobody has met.
/// </para>
/// <para>
/// The first few values for a seed are pinned in <c>GeneratorTests</c>, because a generator that silently changes is
/// worse than one that is obviously wrong: every replay in the issue tracker would become a lie.
/// </para>
/// </remarks>
type Generator =
    { /// The state, in a cell so that drawing advances it
      State: uint64 ref }

[<RequireQualifiedAccess>]
module Generator =

    /// The mixing function of SplitMix64 (Steele, Lea and Flood, 2014). The finalizer spreads the high bits down, so
    /// that the low bits a small bound is taken from are as good as the high ones.
    let private mix (z: uint64) =
        let first = (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        let second = (first ^^^ (first >>> 27)) * 0x94D049BB133111EBUL
        second ^^^ (second >>> 31)

    /// A generator that starts from a seed. The same seed always gives the same sequence.
    let ofSeed (seed: int) : Generator =
        { State = ref (uint64 seed) }

    /// The next value in the sequence
    let next (generator: Generator) =
        let advanced = generator.State.Value + 0x9E3779B97F4A7C15UL
        generator.State.Value <- advanced
        mix advanced

    /// A value in [0, bound). A bound of one or less has only one answer, so it does not draw at all.
    let intBelow (bound: int) (generator: Generator) =
        if bound <= 1 then 0
        else int (next generator % uint64 bound)

    /// A value in [lowInclusive, highExclusive)
    let intBetween (lowInclusive: int) (highExclusive: int) (generator: Generator) =
        if highExclusive <= lowInclusive then lowInclusive
        else lowInclusive + intBelow (highExclusive - lowInclusive) generator

    /// One of the choices
    let pick<'T> (choices: 'T array) (generator: Generator) =
        choices.[intBelow choices.Length generator]

    /// A fraction in [0, 1), so that a test can spread a value over a range
    let fraction (generator: Generator) = float (next generator % 1_000_000_000UL) / 1_000_000_000.0
