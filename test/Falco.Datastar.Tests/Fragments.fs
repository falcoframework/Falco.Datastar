namespace Falco.Datastar.Tests

open System

/// The pieces the simulation tests build their inputs from.
///
/// The rule that shapes this file, and it is worth stating because getting it wrong makes a green run meaningless:
/// a generator that cannot reach the interesting cases is a generator that finds nothing. A corpus of hostile
/// fragments averaged uniformly will produce mostly harmless letters, so the fragments that break a parser are
/// almost never drawn.
///
/// So the fragments are grouped by what they attack and weighted, and `DistributionTests` measures what is actually
/// reached, so a change to the weights shows up as a number rather than as a test that quietly stopped covering
/// anything. A guard is only worth anything if you can show it bites.
///
/// The groups, and what each one attacks:
/// - `ordinary` text, which is what most real input looks like and what most of a run should be
/// - `attributeBreak`, text that tries to end an attribute and add one of its own
/// - `elementBreak`, text that tries to close the element that holds the attribute
/// - `quoteEnd`, text that ends a JavaScript string literal
/// - `control`, the characters a parser rewrites or cannot keep at all
/// - `lineBreak`, the characters that end a JavaScript string but not a regular expression literal
/// - `datastarName`, text that collides with how Datastar reads an attribute name
/// - `entity`, text that already looks like a character reference
module Fragments =

    /// Fragments that are just text. Most of a run should be this, so that the other cases are found among them
    /// rather than in a stream of nothing but punctuation.
    let ordinary =
        [| "a"; "b"; "z"; "A"; "0"; "9"; "hello"; "Ada"; "count"; "form"; "menu"; "café"; "日本語"; "😀"; " " |]

    /// Text that tries to end an attribute value and start an attribute of its own, which is what an injection is.
    let attributeBreak =
        [| "\""; "'"; "\"; onmouseover=\""; "\"><script>"; "\" autofocus onfocus=\""; "x\" onerror=\"alert(1)" |]

    /// Text that tries to close the element or the document that holds the attribute.
    let elementBreak =
        [| "<"; ">"; "</script>"; "</style>"; "<!--"; "-->"; "<img src=x onerror=alert(1)>"; "/>"; "<svg/onload=alert(1)>" |]

    /// Text that ends a single-quoted JavaScript string, after which the rest of it is code.
    let quoteEnd =
        [| "'"; "\'"; "'; alert(1); //"; "');alert(1);//"; "\\'"; "${alert(1)}" |]

    /// The characters an HTML parser rewrites or cannot keep: a carriage return becomes a line feed, and a NUL
    /// becomes U+FFFD. A library that writes one of these as itself has changed the text.
    let control =
        [| "\000"; "\r"; "\n"; "\r\n"; "\t"; "\u000C"; "\u0085"; "\u00A0"; "\u0000" |]

    /// The characters that end a JavaScript string literal. A regular expression literal can hold them, which is
    /// why they are escaped there rather than removed.
    let lineBreak =
        [| "\u2028"; "\u2029"; "\u0085" |]

    /// Text that collides with how Datastar reads an attribute name: a modifier is introduced with two underscores,
    /// and a name is split at a dot.
    let datastarName =
        [| "__"; "___"; "__proto__"; "a.b"; "a..b"; "_"; "__x"; "x__" |]

    /// Text that already looks like a character reference, so escaping it once is not enough.
    let entity =
        [| "&amp;"; "&quot;"; "&#x27;"; "&#39;"; "&lt;"; "&nbsp;"; "&#13;"; "&" |]

    /// Every group, with the weight each one is drawn at. The weights are deliberate and are measured by
    /// `DistributionTests`, so they are not a matter of taste. The type is named, because the lambda in `fragment`
    /// takes a group apart and the compiler needs to know which part is the fragments and which is the weight.
    let private groups: (string array * int) list =
        [ ordinary, 60
          attributeBreak, 9
          elementBreak, 8
          quoteEnd, 8
          control, 6
          lineBreak, 3
          datastarName, 4
          entity, 2 ]

    /// The groups and their weights, for a test that reports how often each one was reached
    let weights = groups

    /// Picks one fragment from one group, chosen by weight. Every group is reachable, so a case that a parser
    /// mishandles is generated rather than merely possible.
    let fragment (generator: Generator) =
        let total = groups |> List.sumBy snd
        let mutable roll = Generator.intBelow total generator
        let mutable chosen = ""
        // The groups are walked in order, spending the roll as it goes, so a group's share of the range is its weight
        for (fragments, weight) in groups do
            if chosen = "" then
                if roll < weight then
                    chosen <- fragments.[Generator.intBelow fragments.Length generator]
                else
                    roll <- roll - weight
        chosen

    /// Text of zero to a few fragments. Length is weighted short, because the interesting cases are usually short
    /// and a long run of them rarely finds anything new.
    let hostileText (generator: Generator) =
        let howMany =
            match Generator.intBelow 10 generator with
            | 0 | 1 -> 0
            | 2 | 3 | 4 | 5 | 6 -> 1
            | 7 | 8 -> 2
            | _ -> 3 + Generator.intBelow 3 generator
        String.Join("", [ for _ in 1 .. howMany -> fragment generator ])

    /// A signal name, built from the pieces the name rules care about, so that some are accepted and some are not.
    let signalName (generator: Generator) =
        let pieces = [| "a"; "x"; "count"; "menuOpen"; "form"; "firstName"; "1"; "0"; "_"; "-"; "."; "$"; " "; "A" |]
        let howMany = Generator.intBetween 1 5 generator
        String.Join("", [ for _ in 1 .. howMany -> pieces.[Generator.intBelow pieces.Length generator] ])

    /// Every fragment in every group, so a test can assert each one is reachable rather than only that some are
    let everyFragment: string array =
        groups
        |> List.collect (fun (fragments: string array, _: int) -> fragments |> Array.toList)
        |> List.toArray
