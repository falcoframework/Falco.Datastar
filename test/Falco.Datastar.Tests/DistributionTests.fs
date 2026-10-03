namespace Falco.Datastar.Tests

open System
open System.Collections.Generic
open Falco.Datastar
open FsUnit.Xunit
open Xunit

/// What the generators in `Fragments` actually produce.
///
/// A generator that cannot reach the interesting cases is a generator that finds nothing, and a green run over one is
/// worse than no run, because it reads as coverage. These tests measure the distribution rather than assert that a
/// property holds, so a change to the weights shows up as a number instead of as a test that quietly stopped
/// generating anything.
///
/// They also pin two things that are easy to get wrong and invisible when they are: that a seed always gives the same
/// output, so a failure replays, and that the generator is not silently collapsing to a handful of inputs.
module DistributionTests =

    /// How many of the given fragments a long run of generated texts reached
    let private reachedOf (fragments: string list) (draws: int) =
        let random = Random 12345
        let seen = HashSet<string>()
        for _ in 1 .. draws do
            let text = Fragments.hostileText random
            for fragment in fragments do
                if text.Contains fragment then
                    seen.Add fragment |> ignore
        fragments |> List.filter (fun fragment -> not (seen.Contains fragment))

    [<Fact>]
    let ``every group of fragments is reached by a long run`` () =
        // Each group is drawn by weight, so a run that never reaches one means the weighting is broken
        for (fragments, _) in Fragments.weights do
            let missing = reachedOf (fragments |> Array.toList) 3000
            if not missing.IsEmpty then
                let joined = String.Join(", ", missing)
                failwith $"a long run never generated these, so nothing tests that group: {joined}"
        // And the weights have to actually differ, or they are decoration
        let distinct = Fragments.weights |> List.map snd |> List.distinct
        distinct |> List.length |> should be (greaterThan 1)

    [<Fact>]
    let ``the dangerous fragments are all reached, not only the harmless ones`` () =
        let dangerous =
            [ "\""; "'"; "<"; ">"; "&"; "\000"; "\r"; "\u2028"; "\u2029"; "__"; "&amp;"; "</script>"
              "\"; onmouseover=\""; "');alert(1);//"; "<img src=x onerror=alert(1)>" ]
        let missing = reachedOf dangerous 3000
        if not missing.IsEmpty then
            let joined = String.Join(", ", missing)
            failwith $"these were never generated, so nothing tests them: {joined}"

    [<Fact>]
    let ``ordinary text is still the common case, so the dangerous ones are found among real input`` () =
        // If almost everything were punctuation, a green run would say nothing about a page built from real text
        let random = Random 999
        let ordinaryDraws =
            [ for _ in 1 .. 3000 do
                let text = Fragments.fragment random
                if Fragments.ordinary |> Array.contains text then 1 else 0 ]
            |> List.sum
        ordinaryDraws |> should be (greaterThan 300)

    [<Fact>]
    let ``the same seed always produces the same text, so a failure replays`` () =
        // This is what makes the seed in a failure message worth anything
        let first = Random 1234 |> fun r -> [ for _ in 1 .. 50 -> Fragments.hostileText r ]
        let second = Random 1234 |> fun r -> [ for _ in 1 .. 50 -> Fragments.hostileText r ]
        second |> should equal first
        let other = Random 5678 |> fun r -> [ for _ in 1 .. 50 -> Fragments.hostileText r ]
        // A different seed has to give different text, or the seed never reaches the generator at all
        other |> should not' (equal first)

    [<Fact>]
    let ``a long run produces many different texts, not a few shapes repeated`` () =
        // A generator that keeps returning the same handful proves nothing however many draws it makes
        let random = Random 4242
        let texts =
            [ for _ in 1 .. 1000 -> Fragments.hostileText random ] |> Set.ofList
        texts.Count |> should be (greaterThan 300)

    [<Fact>]
    let ``signal names are a mix of accepted and refused, so the name rules are tested from both sides`` () =
        Dst.run "signal names are a mix of accepted and refused" (fun random ->
            let mutable accepted = 0
            let mutable refused = 0
            for _ in 1 .. 600 do
                match Signal.tryCreate<int> SignalScope.Server (Fragments.signalName random) with
                | Ok _ -> accepted <- accepted + 1
                | Error _ -> refused <- refused + 1
            accepted |> should be (greaterThan 20)
            refused |> should be (greaterThan 20))
