# Unit tests

Run them with `dotnet test test/Falco.Datastar.Tests -c Release`. The browser tests are in `test/Falco.Datastar.E2E`.

## Property tests

`PropertyTests.fs` states a rule that has to hold for every input and lets [FsCheck](https://fscheck.github.io/) search for an input that breaks it. Where a named test checks one case, a property checks all of them, and it finds the case nobody thought of. A counterexample is printed with its shrunk values and can be replayed.

The rules are the ones the library's own reasoning rests on:

| Rule | Why it matters |
| --- | --- |
| Text in a signal, and a URL, is read back exactly through the HTML parser and then the JavaScript parser | The library does not escape attribute values itself, so this is what keeps text from a user as text |
| An attribute value never keeps a quote, a bracket or a carriage return | A quote would end the attribute and add attributes of its own |
| Every signal name the library accepts is read by Datastar as the name an expression uses | If the two ever disagree, half of a page talks to one signal and the other half to another |
| Arithmetic, comparisons and conditions keep their parentheses | An operator next to one that lost them would reach into it and change the result |
| Dividing whole numbers is truncated, and a float is written with a dot | An `int` signal must not hold 3.5, and a server whose culture uses a comma must not write `1,5` |

When a property fails, fix the library or the property, and add a plain named test for the counterexample, so the case is checked on every run and not only when FsCheck happens to find it again.

## Mutation testing

A suite can be green while the code it covers is wrong: the assertion drifts with the code, or the case is never reached. Mutation testing attacks that. It makes a small, deliberate mistake in the source and runs the whole suite. A failing test kills the mutant, which shows the suite has teeth. A mutant that survives is a gap, and it says which test is missing.

```sh
dotnet run --project test/mutation              # every mutant
dotnet run --project test/mutation -- setAll    # only mutants whose name contains "setAll"
dotnet run --project test/mutation -- check     # check the list against the source, building nothing
dotnet run --project test/mutation -- clean     # put the source back, after a run that was killed
```

It exits non-zero if any mutant survives, or if a mutant no longer compiles or its text has moved, because both of those
mean the list itself needs updating. `check` does that part on its own: it confirms that every mutant still finds its text,
exactly once, that its replacement really differs, and that no two of them change the same text. It builds nothing and takes
a second, so it is the thing to run after editing either the library or the list. It also reports which files no mutant
touches, so that "all killed" is not read as more than it is.

Stryker, the usual tool for .NET, does not support F# projects. On this solution it fails while reading the projects,
because it resolves them the way it resolves C# ones. So `test/mutation` is the mechanism. It is a separate program
because a mutant rewrites the library and the tests have to run against the rebuilt assembly, which the test process
cannot do while holding that assembly open, and it is deliberately not in the solution so nothing depends on it.

Each mutant is one exact piece of source rewritten into something subtly wrong that still compiles, which is the kind of
mistake a human actually makes. The list is written by hand rather than generated, because a targeted list of realistic
mistakes is worth more than a large generated one. A mutant the tests cannot tell from the original is marked `Equivalent`,
because that is a problem with the list rather than with the tests and is reported apart from a real gap.

**The program changes files in `src`, so do not run other builds or tests at the same time.** It restores the source after
every mutant and at the end whatever happens. A run that is killed outright cannot restore it, so `clean` puts it back from
the backups, and a run refuses to start while any backup is left over. `git diff` is the check that the tree is as it should be.

## Simulation tests

Several files simulate the library's inputs and check rules that must always hold. They are deterministic: all of their randomness comes from a seed, so a failure can be replayed exactly. See `Dst.fs`.

| File | What it simulates | The rule it checks |
| --- | --- | --- |
| `DstHtmlTests.fs` | Hostile text passed to every helper that writes an attribute or a template element | The call is refused with an `ArgumentException`, or a real HTML5 parser ([AngleSharp](https://github.com/AngleSharp/AngleSharp)) reads one element with exactly the attributes that were generated, and gives back the text they stand for |
| `DstManifestTests.fs`, the parser | Manifests that are changed in random places or damaged | `RocketManifest.parse` gives a result and never throws |
| `DstManifestTests.fs`, the stream | A request body that arrives in chunks of random sizes, fails partway, is cancelled partway, or is at the edge of the 1 MiB limit | `Request.getRocketManifests` gives the same result however the body arrives, never reads more than the limit and one chunk, gives `ConnectionFailed` or `Cancelled` for a body that fails or is cancelled, and never gives a result for half a body |
| `EscapingTests.fs`, `ExprTests.fs`, `RequestOptionsTests.fs` | Random text, signal names and request options | The escaping is read back as the same text by a browser and a JavaScript parser, every accepted signal name is read by Datastar as written, and every set of options is valid JSON |

By default each test runs the seeds 1 to 25, so a normal run is fast and always the same. To look for rare cases, run more seeds. To replay a failure, run its seed:

```sh
DST_SEEDS=2000 dotnet test test/Falco.Datastar.Tests -c Release   # seeds 1 to 2000
DST_SEED=417 dotnet test test/Falco.Datastar.Tests -c Release     # only seed 417
```

A failure message names the seed and the command that replays it, and `DstHtmlTests` also prints the HTML that broke the rule.
The **dst** workflow in the Actions tab runs many seeds on GitHub. It only runs when you start it.

When a simulation finds a bug, fix it, and add a plain test for that case, so that the fix does not depend on a seed reaching it.

### The fragments, and proving they are reached

`Fragments.fs` is where the simulation tests get their text. Its fragments are grouped by what they attack — text that
tries to end an attribute, text that tries to close the element, text that ends a JavaScript string, the characters a
parser rewrites, and so on — and each group is weighted.

The weights are not a matter of taste, and that is the point. An unweighted corpus of hostile fragments produces mostly
harmless letters, so the fragments that actually break a parser are almost never drawn, and a green run then says nothing.
`DistributionTests` measures what a long run reaches, so a change to the weights shows up as a number rather than as a test
that quietly stopped generating anything.

Those tests also pin that a seed always gives the same output, so the seed in a failure message is worth something, and that
a long run produces many different texts rather than a few shapes repeated.

Each of those checks is a guard, and a guard is only worth having if it can fail. Set a group's weight to `0` and the tests
name the fragments that are no longer generated.

