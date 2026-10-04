namespace Falco.Datastar.Tests

open System
open System.Globalization
open Falco.Markup

[<AutoOpen>]
module TestHelpers =
    /// An attribute on a div, as the HTML it renders
    let renderAttr attr =
        Elem.div [ attr ] []
        |> renderNode

    /// Runs the function in a culture that writes 1.5 as 1,5, and puts the old culture back afterwards.
    /// The culture is built from the invariant one so the tests do not need ICU, which slim CI images lack.
    let withCommaDecimalCulture (run: unit -> unit) =
        let commaDecimal = CultureInfo.InvariantCulture.Clone() :?> CultureInfo
        commaDecimal.NumberFormat.NumberDecimalSeparator <- ","
        let original = CultureInfo.CurrentCulture
        try
            CultureInfo.CurrentCulture <- commaDecimal
            run ()
        finally
            CultureInfo.CurrentCulture <- original

    /// Runs the function in Turkish, where "I" lowercases to the dotless i U+0131. That one difference makes .NET's
    /// culture-sensitive case-insensitive Regex stop matching an I against [a-z], while JavaScript, which has no
    /// culture, keeps matching it. Returns false, without running, when the runtime has no ICU data to build the
    /// culture, so a slim CI image skips the test rather than passing it for the wrong reason.
    let tryWithTurkishCulture (run: unit -> unit) =
        let turkish =
            try Some (CultureInfo.GetCultureInfo "tr-TR")
            with :? CultureNotFoundException -> None
        match turkish with
        | None -> false
        | Some turkish ->
            let original = CultureInfo.CurrentCulture
            try
                CultureInfo.CurrentCulture <- turkish
                // Confirm the runtime really does fold the I to the dotless i, or the test would prove nothing.
                if Char.ToLower 'I' <> '\u0131' then
                    false
                else
                    run ()
                    true
            finally
                CultureInfo.CurrentCulture <- original

    /// What a JavaScript parser does with a single-quoted string literal such as 'it\'s': the text that it stands for.
    /// It fails when the literal is not valid, which is when an unescaped quote ends it early or a raw line break is in it.
    let readJsLiteral (literal:string) =
        let body = literal.Substring(1, literal.Length - 2)
        let text = System.Text.StringBuilder()
        let mutable index = 0
        while index < body.Length do
            match body.[index] with
            | '\\' when body.[index + 1] = 'u' ->
                text.Append(char (Convert.ToInt32(body.Substring(index + 2, 4), 16))) |> ignore
                index <- index + 6
            | '\\' ->
                text.Append(match body.[index + 1] with | 'n' -> '\n' | 'r' -> '\r' | other -> other) |> ignore
                index <- index + 2
            | '\'' -> failwith $"an unescaped quote ends the string early in {literal}"
            | '\n' | '\r' -> failwith $"a raw line break is not allowed in a string literal in {literal}"
            | other ->
                text.Append other |> ignore
                index <- index + 1
        text.ToString()

    /// What a browser does with the text of an attribute, and then what a JavaScript parser does with the string literal in it
    let readBack (attributeValue:string) =
        readJsLiteral (System.Web.HttpUtility.HtmlDecode attributeValue)

