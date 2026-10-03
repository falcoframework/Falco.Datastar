namespace Falco.Datastar.Tests

open System
open FsUnit.Xunit
open Xunit

/// What the generator itself does.
///
/// A generator that quietly changes is worse than one that is obviously broken: every replay in an issue tracker quietly
/// becomes a lie, and a simulation that no longer explores the same space stops finding what it found before. So the
/// values for a seed are pinned here, and these tests fail loudly if the algorithm ever moves.
module GeneratorTests =

    /// The first values the generator gives for a seed. These were taken from the implementation and are here to be
    /// compared against: if the algorithm changes, this test is what notices.
    let private firstValues (seed: int) (bound: int) (howMany: int) =
        let generator = Generator.ofSeed seed
        [ for _ in 1 .. howMany -> Generator.intBelow bound generator ]

    [<Fact>]
    let ``a seed gives the same values every time`` () =
        firstValues 1 100 8 |> should equal (firstValues 1 100 8)

    [<Fact>]
    let ``different seeds give different values`` () =
        firstValues 1 100 8 |> should not' (equal (firstValues 2 100 8))

    // The exact values, so that a change to the mixing function cannot pass unnoticed. They were produced by the
    // implementation in the REPL and are what every replay since has been recorded against.
    [<Fact>]
    let ``the sequence for seed 1 is what replays are recorded against`` () =
        firstValues 1 100 5 |> should equal [ 65; 19; 90; 35; 61 ]

    [<Fact>]
    let ``the sequence for seed 417 is what replays are recorded against`` () =
        // 417 is the seed the documentation uses as an example
        firstValues 417 100 5 |> should equal [ 2; 24; 43; 15; 98 ]

    [<Fact>]
    let ``every value is inside the bound asked for`` () =
        for bound in [ 1; 2; 7; 10; 100 ] do
            let values = firstValues 99 bound 2000
            if not (values |> List.forall (fun n -> n >= 0 && n < bound)) then
                failwith $"a value fell outside [0, {bound})"

    [<Fact>]
    let ``a small bound still gives every value in it, so the generator does not collapse`` () =
        // A generator that always returned 0 would satisfy the bound and find nothing
        let seen = firstValues 7 10 5000 |> Set.ofList
        seen |> should equal (Set.ofList [ 0; 1; 2; 3; 4; 5; 6; 7; 8; 9 ])

    [<Fact>]
    let ``the values are spread rather than clustered`` () =
        // Each of the ten buckets should get roughly a tenth of the draws, not all of them in a few
        let counts = firstValues 12345 10 10000 |> List.countBy id |> Map.ofList
        for bucket in 0 .. 9 do
            let count = counts |> Map.tryFind bucket |> Option.defaultValue 0
            // A tenth is 1000. Anything between 700 and 1300 is close enough that the mix is spreading the bits.
            if count < 700 || count > 1300 then
                failwith $"bucket {bucket} got {count} of 10000 draws, which is not about a tenth"

    [<Fact>]
    let ``a bound of one or less does not draw, so it cannot run off the end of the sequence`` () =
        let before = firstValues 3 100 1
        Generator.intBelow 1 (Generator.ofSeed 3) |> should equal 0
        Generator.intBelow 0 (Generator.ofSeed 3) |> should equal 0
        Generator.intBelow (-5) (Generator.ofSeed 3) |> should equal 0
        // And drawing for another bound afterwards is unaffected, because those calls did not consume anything
        firstValues 3 100 1 |> should equal before

    [<Fact>]
    let ``intBetween stays within its range, including when the range is empty or reversed`` () =
        let generator = Generator.ofSeed 5
        for _ in 1 .. 1000 do
            Generator.intBetween 3 7 generator |> should be (greaterThanOrEqualTo 3)
        Generator.intBetween 4 4 (Generator.ofSeed 5) |> should equal 4
        Generator.intBetween 7 3 (Generator.ofSeed 5) |> should equal 7

    [<Fact>]
    let ``pick chooses from the array it is given`` () =
        let choices = [| "a"; "b"; "c" |]
        let generator = Generator.ofSeed 11
        let drawn = [ for _ in 1 .. 1000 -> Generator.pick choices generator ]
        drawn |> List.distinct |> Set.ofList |> should equal (Set.ofList [ "a"; "b"; "c" ])
