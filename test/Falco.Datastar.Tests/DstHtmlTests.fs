namespace Falco.Datastar.Tests

open System
open System.Text.Json
open System.Web
open AngleSharp.Html.Parser
open Falco.Datastar
open Falco.Datastar.SignalPath
open Falco.Markup
open FsUnit.Xunit
open Xunit

// Deterministic simulation of the library's output against a real HTML5 parser (AngleSharp).
//
// Each case builds attributes or template elements from hostile strings: quotes, angle brackets, entities, line breaks, double underscores,
// and text that tries to add attributes or elements. The rule is that a case is either refused with an ArgumentException,
// or it renders HTML that the parser reads as the one element, with exactly the attributes that were generated and nothing else.
// Where the text that an attribute stands for is known, the parser has to give it back exactly.
//
// The seed of a failing case is in the message. See Dst.fs for how to run more seeds and how to replay one.
module DstHtmlTests =
    let private pieces =
        [| "a"; "b"; "x"; "1"; " "; "\""; "'"; "<"; ">"; "&"; "&amp;"; "&quot;"; "&#x27;"; "="; "/"; "\\"; "\n"; "\r"; "\r\n"; "\t"; "\u000C"; "\000"
           "__"; "_"; "-"; "."; ":"; "$"; "{"; "}"; "("; ")"; ";"; "`"; " "; " "; "é"; "😀"; "</script>"; "<!--"; "-->"
           "\" onmouseover=\""; "' onmouseover='"; "><img src=x onerror=alert(1)>"; "/>"; "javascript:" |]

    let private hostile (generator:Generator) =
        String.Concat(Array.init (Dst.intBetween 0 6 generator) (fun _ -> Dst.pick generator pieces))

    let private safeNames = [| "a"; "card"; "my-class"; "aria-label"; "is.active"; "x1"; "b_c" |]

    /// Mostly a name that is fine, and sometimes a hostile one, so that both the accepted and the refused paths run
    let private nameLike (generator:Generator) =
        match Dst.intBelow 100 generator < 65 with
        | true -> Dst.pick generator safeNames
        | false -> hostile generator

    /// A signal path, or nothing when the SDK refuses the name
    let private signalPath (generator:Generator) =
        try Some (sp (nameLike generator)) with _ -> None

    let private keyOf attribute =
        match attribute with
        | KeyValueAttr (key, _) -> key
        | NonValueAttr key -> key

    /// What the parser is expected to read for an attribute: the value with its HTML escapes undone
    let private valueOf attribute =
        match attribute with
        | KeyValueAttr (_, value) -> HttpUtility.HtmlDecode value
        | NonValueAttr _ -> ""

    /// The check gets the attributes the parser read, by lower case name, and the raw attributes that were generated
    type private Check = Map<string, string> -> unit

    let private nothingMore : Check = ignore

    let private jsonOf (value:string) = JsonDocument.Parse value

    /// HTML cannot keep a NUL character in an attribute value. The library writes the character that a parser would give it, U+FFFD.
    let private asAttributeText (text:string) = text.Replace('\000', '\uFFFD')

    // Cases that build attributes. Each returns the attributes, and a check of what the attribute stands for.

    let private attributeCases : (string * (Generator -> XmlAttribute list * Check)) list =
        [ "Ds.class'", fun r -> [ Ds.class' (nameLike r, "$a") ], nothingMore
          "Ds.attr'", fun r -> [ Ds.attr' (nameLike r, "$a") ], nothingMore
          "Ds.style", fun r -> [ Ds.style (nameLike r, "'x'") ], nothingMore
          "Ds.onEvent", fun r -> [ Ds.onEvent (nameLike r, "$a", [ Window ]) ], nothingMore
          "Ds.onEvent with a typed statement", fun r -> [ Ds.onEvent (nameLike r, Stmt.unsafeRaw (hostile r)) ], nothingMore
          "Ds.text with a typed string", fun r ->
              let text = hostile r
              [ Ds.text (Expr.string text) ], (fun parsed -> readJsLiteral parsed.["data-text"] |> should equal text)
          "Ds.text with unsafeRaw", fun r -> [ Ds.text (Expr.unsafeRaw<string> (hostile r)) ], nothingMore
          "Ds.attr' with a typed string", fun r ->
              let text = hostile r
              let attribute = Ds.attr' ("title", Expr.string text)
              [ attribute ], (fun parsed -> readJsLiteral parsed.["data-attr:title"] |> should equal text)
          "Ds.signal with a string value", fun r ->
              match signalPath r with
              | None -> [], nothingMore
              | Some path ->
                  let text = hostile r
                  let attribute = Ds.signal (path, text)
                  [ attribute ], (fun parsed -> readJsLiteral parsed.[(keyOf attribute).ToLowerInvariant()] |> should equal text)
          "Ds.signals", fun r ->
              let text = hostile r
              [ Ds.signals {| a = text |} ], (fun parsed -> (jsonOf parsed.["data-signals"]).RootElement.GetProperty("a").GetString() |> should equal text)
          "Ds.onClick with a URL", fun r ->
              let url = hostile r
              [ Ds.onClick (Ds.get url) ], (fun parsed ->
                  let action = parsed.["data-on:click"]
                  readJsLiteral (action.Substring("@get(".Length, action.Length - "@get(".Length - ")".Length)) |> should equal url)
          "Ds.onClick with request options", fun r ->
              let text = hostile r
              let options =
                  { RequestOptions.Defaults with
                      ContentType = Dst.pick r [| Json; Form; SelectedForm text; CustomJson {| x = text |} |]
                      FilterSignals = Dst.pick r [| SignalsFilter.None; SignalsFilter.Include text; SignalsFilter.Exclude text |]
                      Headers = Dst.pick r [| []; [ "X-A", text ] |]
                      RetryScaler = Dst.pick r [| 2.0; 1.5 |] }
              [ Ds.onClick (Ds.post ("/x", options)) ], (fun parsed ->
                  let action = parsed.["data-on:click"]
                  use json = jsonOf (action.Substring(action.IndexOf("',", StringComparison.Ordinal) + 2).TrimEnd ')')
                  match json.RootElement.TryGetProperty "headers" with
                  | true, headers -> headers.GetProperty("X-A").GetString() |> should equal text
                  | false, _ -> ())
          "Ds.onSignalPatchFilter", fun r ->
              [ Ds.onSignalPatchFilter (Dst.pick r [| SignalsFilter.Include (hostile r); SignalsFilter.Exclude (hostile r); SignalsFilter.Prefix (hostile r) |]) ], nothingMore
          "Ds.jsonSignalsOptions", fun r -> [ Ds.jsonSignalsOptions (SignalsFilter.Include (hostile r)) ], nothingMore
          "Ds.onClick with setAll and a filter", fun r -> [ Ds.onClick (Ds.setAllFiltered (hostile r, SignalsFilter.Include (hostile r))) ], nothingMore
          "Ds.onClick with a typed post", fun r -> [ Ds.onClick (Stmt.post (hostile r)) ], nothingMore
          "Ds.bindProp", fun r ->
              match signalPath r with
              | None -> [], nothingMore
              | Some path -> [ Ds.bindProp (path, nameLike r, [ nameLike r ]) ], nothingMore
          "Ds.bindEvent", fun r ->
              match signalPath r with
              | None -> [], nothingMore
              | Some path -> [ Ds.bindEvent (path, nameLike r, [ nameLike r ]) ], nothingMore
          "Ds.bind", fun r ->
              match signalPath r with
              | None -> [], nothingMore
              | Some path -> [ Ds.bind path ], nothingMore
          "Ds.nonce", fun r ->
              let nonce = hostile r
              [ Ds.nonce nonce ], (fun parsed -> parsed.["data-nonce"] |> should equal (asAttributeText nonce))
          "Rocket.propString", fun r ->
              let text = hostile r
              [ Rocket.propString ("label", text) ], (fun parsed -> parsed.["label"] |> should equal (asAttributeText text))
          "Rocket.propString with a hostile name", fun r -> [ Rocket.propString (nameLike r, "x") ], nothingMore
          "Rocket.propJson", fun r ->
              let text = hostile r
              [ Rocket.propJson ("settings", {| a = text |}) ], (fun parsed -> (jsonOf parsed.["settings"]).RootElement.GetProperty("a").GetString() |> should equal text)
          "Rocket.propNumber, propBool, propDate, propBin", fun r ->
              [ Rocket.propNumber ("count", Generator.fraction r * 1000.0)
                Rocket.propBool ("open", Dst.intBelow 2 r = 0)
                Rocket.propDate ("when", DateTimeOffset.FromUnixTimeSeconds(int64 (Dst.intBelow 2_000_000_000 r)))
                Rocket.propBin ("payload", Array.init (Dst.intBelow 20 r) (fun _ -> byte (Dst.intBelow 256 r))) ], nothingMore ]

    // Cases that build a template element

    let private nodeCases : (string * (Generator -> XmlNode * Check)) list =
        [ "Rocket.templateIf", fun r ->
              let text = hostile r
              Rocket.templateIf (text, []), (fun parsed -> parsed.["data-if"] |> should equal (asAttributeText text))
          "Rocket.templateElseIf", fun r ->
              let text = hostile r
              Rocket.templateElseIf (text, []), (fun parsed -> parsed.["data-else-if"] |> should equal (asAttributeText text))
          "Rocket.templateFor", fun r ->
              let text = hostile r
              Rocket.templateFor (text, []), (fun parsed -> parsed.["data-for"] |> should equal (asAttributeText text))
          "Rocket.templateIf with a typed string", fun r ->
              let text = hostile r
              Rocket.templateIf (Expr.equal (Expr.read (Signal.rocket<string> "n")) (Expr.string text), []), (fun parsed ->
                  let condition = parsed.["data-if"]
                  readJsLiteral (condition.Substring("($$n === ".Length, condition.Length - "($$n === ".Length - ")".Length)) |> should equal text)
          "Rocket.forEach with unsafeRaw", fun r -> Rocket.forEach (Expr.unsafeRaw<string list> (hostile r), (fun _ _ -> [])), nothingMore ]

    let private parse (html:string) = HtmlParser().ParseDocument html

    /// The rule: one element, with no child nodes, that has the attributes that were generated, in order, with their values
    let private assertShape (node:XmlNode) (check:Check) =
        let tag, attributes =
            match node with
            | ParentNode ((tag, attributes), _) -> tag, attributes
            | SelfClosingNode (tag, attributes) -> tag, attributes
            | TextNode _ -> failwith "a case has to build an element"
        let html = renderNode node
        let document = parse html
        try
            // A template at the start goes into the head, and other elements into the body. Either way there is one node, and it is the element.
            (document.Head.ChildNodes.Length + document.Body.ChildNodes.Length) |> should equal 1
            let element = Seq.append document.Head.Children document.Body.Children |> Seq.exactlyOne
            element.LocalName |> should equal tag
            // A template keeps its children in a content fragment, and there are none. Any other element has no child nodes at all.
            (match element with
             | :? AngleSharp.Html.Dom.IHtmlTemplateElement as template -> template.Content.ChildNodes.Length |> should equal 0
             | _ -> element.ChildNodes.Length |> should equal 0)
            let read = element.Attributes |> Seq.map (fun attribute -> attribute.Name, attribute.Value) |> List.ofSeq
            // The parser lower-cases attribute names
            let expected = attributes |> List.map (fun attribute -> (keyOf attribute).ToLowerInvariant(), valueOf attribute)
            read |> should equal expected
            check (Map.ofList read)
        with error ->
            raise (Exception($"HTML that broke the rule: {html}{Environment.NewLine}{error.Message}", error))

    [<Fact>]
    let ``Every attribute or element built from hostile text is refused, or is read by an HTML parser as exactly what was generated`` () =
        Dst.run "Every attribute or element built from hostile text" (fun generator ->
            let mutable accepted = 0
            let mutable refused = 0
            for _ in 1 .. 400 do
                // Attributes: one to three of them on one div. A name that two of them share is dropped by the parser, so those cases are skipped.
                let chosen = Array.init (Dst.intBetween 1 4 generator) (fun _ -> Dst.pick generator (Array.ofList attributeCases))
                try
                    let built = chosen |> Array.map (fun (name, build) -> name, build generator)
                    let attributes = built |> Array.collect (fun (_, (attributes, _)) -> Array.ofList attributes) |> List.ofArray
                    let keys = attributes |> List.map (fun attribute -> (keyOf attribute).ToLowerInvariant())
                    if List.length keys = List.length (List.distinct keys) && not attributes.IsEmpty then
                        let check parsed = for _, (_, check) in built do check parsed
                        try
                            assertShape (Elem.div attributes []) check
                        with error ->
                            let names = String.Join(", ", chosen |> Array.map fst)
                            raise (Exception($"Cases: {names}{Environment.NewLine}{error.Message}", error))
                        accepted <- accepted + 1
                with :? ArgumentException -> refused <- refused + 1

                // Elements
                let name, build = Dst.pick generator (Array.ofList nodeCases)
                try
                    let node, check = build generator
                    try
                        assertShape node check
                    with error ->
                        raise (Exception($"Case: {name}{Environment.NewLine}{error.Message}", error))
                    accepted <- accepted + 1
                with :? ArgumentException -> refused <- refused + 1
            // A run where nothing was accepted, or nothing was refused, would prove little
            accepted |> should be (greaterThan 100)
            refused |> should be (greaterThan 5))
