namespace Falco.Datastar.Mutation

open System

/// <summary>
/// One deliberate mistake, written against one exact piece of the library's source.
/// </summary>
/// <remarks>
/// <para>
/// A mutant names what it is meant to break, so a survivor can say which test is missing rather than only that
/// something is wrong.
/// </para>
/// <para>
/// <see cref="Equivalent"/> marks a change the tests cannot tell from the original, such as relaxing a rule that another
/// guard already covers. That is a problem with this list rather than with the tests, and it is reported separately so
/// that a contributor who adds one is not left with a harness that can never go green.
/// </para>
/// </remarks>
type Mutant =
    { Name: string
      Breaks: string
      File: string
      /// The exact text to replace. It must appear exactly once in <see cref="File"/>, or the check refuses it.
      Find: string
      Replace: string
      Equivalent: bool }

/// <summary>
/// The mutants, grouped by the part of the library each one attacks.
/// </summary>
/// <remarks>
/// <para>
/// These are written by hand rather than generated, because a targeted list of realistic mistakes is worth more than a
/// large generated one: a survivor is a specific thing to fix, and every entry says what it was trying to break.
/// </para>
/// <para>
/// A verbatim F# string cannot end a line with a quote, and most of the code in this library does, so a run of source
/// lines is written with <c>lines</c> rather than as one literal. That is the reason this file looks the way it does.
/// </para>
/// </remarks>
module Mutants =

    /// Several source lines as one string
    let private lines (parts: string list) = String.Join("\n", parts)

    /// Builds a mutant that is not equivalent to the original
    let private m name breaks fileName find replace =
        { Name = name
          Breaks = breaks
          File = fileName
          Find = find
          Replace = replace
          Equivalent = false }

    /// Builds a mutant that cannot be told from the original by any test, and says why it is harmless
    let private equivalent name why fileName find replace =
        { Name = name
          Breaks = why
          File = fileName
          Find = find
          Replace = replace
          Equivalent = true }

    let private escaping =
        [ m
              "attrEncode leaves an ampersand alone"
              "an ampersand in a value would be read as a character reference, or start one"
              "Utility.fs"
              (lines
                  [ "                match value.[index] with"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore"
                    "                | '<' -> builder.Append \"&lt;\" |> ignore" ])
              (lines
                  [ "                match value.[index] with"
                    "                | '&' -> builder.Append '&' |> ignore"
                    "                | '<' -> builder.Append \"&lt;\" |> ignore" ])

          m
              "attrEncode leaves a double quote alone"
              "a quote in a value would end the attribute and add attributes of its own"
              "Utility.fs"
              (lines
                  [ "                match value.[index] with"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore"
                    "                | '<' -> builder.Append \"&lt;\" |> ignore"
                    "                | '>' -> builder.Append \"&gt;\" |> ignore"
                    "                | '\"' -> builder.Append \"&quot;\" |> ignore" ])
              (lines
                  [ "                match value.[index] with"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore"
                    "                | '<' -> builder.Append \"&lt;\" |> ignore"
                    "                | '>' -> builder.Append \"&gt;\" |> ignore"
                    "                | '\"' -> builder.Append '\"' |> ignore" ])

          m
              "attrEncode leaves an angle bracket alone"
              "text in a value could close the element it is in"
              "Utility.fs"
              (lines
                  [ "                match value.[index] with"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore"
                    "                | '<' -> builder.Append \"&lt;\" |> ignore"
                    "                | '>' -> builder.Append \"&gt;\" |> ignore" ])
              (lines
                  [ "                match value.[index] with"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore"
                    "                | '<' -> builder.Append '<' |> ignore"
                    "                | '>' -> builder.Append \"&gt;\" |> ignore" ])

          m
              "attrEncode leaves a greater-than alone"
              "text in a value could end the tag that holds the attribute"
              "Utility.fs"
              (lines
                  [ "                | '>' -> builder.Append \"&gt;\" |> ignore"
                    "                | '\"' -> builder.Append \"&quot;\" |> ignore"
                    "                // A carriage return written as a character reference is kept. A NUL cannot be kept in an attribute, so it is written as the character that HTML would give it." ])
              (lines
                  [ "                | '>' -> builder.Append '>' |> ignore"
                    "                | '\"' -> builder.Append \"&quot;\" |> ignore"
                    "                // A carriage return written as a character reference is kept. A NUL cannot be kept in an attribute, so it is written as the character that HTML would give it." ])

          m
              "attrEncode stops special-casing a carriage return"
              "an HTML parser changes a carriage return in an attribute into a line feed"
              "Utility.fs"
              "                | '\\r' -> builder.Append \"&#13;\" |> ignore"
              "                | '\\r' -> builder.Append '\\r' |> ignore"

          m
              "attrEncode stops special-casing a NUL character"
              "HTML cannot keep a NUL in an attribute, so it must become the character a parser would give"
              "Utility.fs"
              "                | '\\000' -> builder.Append '\\uFFFD' |> ignore"
              "                | '\\000' -> builder.Append '\\000' |> ignore"

          m
              "attrEncode stops looking for the characters it has to escape"
              "every value with a character that needs escaping would go out unescaped"
              "Utility.fs"
              "    let attributeSpecials = SearchValues.Create \"&<>\\\"\\r\\000\""
              "    let attributeSpecials = SearchValues.Create \"z\""

          m
              "stringLiteral stops escaping a single quote"
              "a quote in a text value would end the JavaScript string, and the rest of it would run"
              "Utility.fs"
              "        | '\\'' -> builder.Append \"\\\\'\" |> ignore"
              "        | '\\'' -> builder.Append '\\'' |> ignore"

          m
              "stringLiteral stops escaping a backslash"
              "a backslash in a text value would escape whatever follows it"
              "Utility.fs"
              "        | '\\\\' -> builder.Append \"\\\\\\\\\" |> ignore"
              "        | '\\\\' -> builder.Append '\\\\' |> ignore"

          m
              "stringLiteral stops escaping a line feed"
              "a line feed ends a JavaScript string, so the rest of the value would run as code"
              "Utility.fs"
              (lines
                  [ "                | '\\n' -> builder.Append \"\\\\n\" |> ignore"
                    "                | '\\r' -> builder.Append \"\\\\r\" |> ignore"
                    "                | '\\000' -> builder.Append \"\\\\u0000\" |> ignore" ])
              (lines
                  [ "                | '\\n' -> builder.Append '\\n' |> ignore"
                    "                | '\\r' -> builder.Append \"\\\\r\" |> ignore"
                    "                | '\\000' -> builder.Append \"\\\\u0000\" |> ignore" ])

          m
              "stringLiteral stops escaping a carriage return"
              "a carriage return ends a JavaScript string"
              "Utility.fs"
              (lines
                  [ "                | '\\r' -> builder.Append \"\\\\r\" |> ignore"
                    "                | '\\000' -> builder.Append \"\\\\u0000\" |> ignore"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore" ])
              (lines
                  [ "                | '\\r' -> builder.Append '\\r' |> ignore"
                    "                | '\\000' -> builder.Append \"\\\\u0000\" |> ignore"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore" ])

          m
              "stringLiteral stops escaping a NUL character"
              "a NUL cannot be written in a string literal, so it has to become an escape"
              "Utility.fs"
              (lines
                  [ "                | '\\000' -> builder.Append \"\\\\u0000\" |> ignore"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore" ])
              (lines
                  [ "                | '\\000' -> builder.Append '\\000' |> ignore"
                    "                | '&' -> builder.Append \"&amp;\" |> ignore" ])

          m
              "regexLiteral stops escaping a slash"
              "a slash in a signals filter would end the regular expression literal early"
              "Utility.fs"
              "            | false, '/' -> builder.Append \"\\\\/\" |> ignore"
              "            | false, '/' -> builder.Append '/' |> ignore"

          m
              "regexLiteral stops escaping a line separator"
              "U+2028 ends a JavaScript string literal, so a filter could break out of one"
              "Utility.fs"
              "            | false, '\\u2028' -> builder.Append \"\\\\u2028\" |> ignore"
              "            | false, '\\u2028' -> builder.Append '\\u2028' |> ignore"

          m
              "a signals filter stops protecting a trailing slash"
              "Datastar strips a trailing slash from a string pattern, so the filter would match something else"
              "Utility.fs"
              "        let finish = match pattern.EndsWith '/' with | true -> \"(?:)\" | false -> \"\""
              "        let finish = \"\""

          m
              "a signals filter stops protecting a leading slash"
              "Datastar would strip a leading slash from a string pattern and match something else"
              "Utility.fs"
              "        let start = match pattern.StartsWith '/' with | true -> \"(?:)\" | false -> \"\""
              "        let start = \"\"" ]

    // A name Datastar cannot read has to be refused, or the attribute and the expression disagree about which signal is
    // meant, and the two halves of a page stop talking to each other.
    let private signalNames =
        [ m
              "a hyphen in a signal name is allowed"
              "Signal would accept myCount-x, which an expression reads as a subtraction"
              "Expr.fs"
              "        | false when name.Contains '-' -> ValueSome (SignalNameError.HasHyphen name)"
              "        | false when false -> ValueSome (SignalNameError.HasHyphen name)"

          m
              "a capital letter at the start of a signal name part is allowed"
              "Signal would accept Menu, which the attribute name makes menu but the expression calls Menu"
              "Expr.fs"
              "        | false when parts |> Array.exists (fun part -> part.Length > 0 && Char.IsAsciiLetterUpper part.[0]) -> ValueSome (SignalNameError.StartsWithCapital name)"
              "        | false when false -> ValueSome (SignalNameError.StartsWithCapital name)"

          m
              "two underscores in a row in a signal name are allowed"
              "Signal would accept a__b, which Datastar reads as a name with a modifier"
              "Expr.fs"
              "        | false when name.Contains \"__\" -> ValueSome (SignalNameError.HasDoubleUnderscore name)"
              "        | false when false -> ValueSome (SignalNameError.HasDoubleUnderscore name)"

          m
              "an underscore at the end of a signal name part is allowed"
              "Signal would accept a_, whose underscore runs into the two that start a modifier"
              "Expr.fs"
              "        | false when parts |> Array.exists (fun part -> part.EndsWith '_') -> ValueSome (SignalNameError.EndsWithUnderscore name)"
              "        | false when false -> ValueSome (SignalNameError.EndsWithUnderscore name)"

          m
              "a signal name that is not a path is allowed"
              "Signal would accept 1abc and a..b, which Datastar cannot read as a signal"
              "Expr.fs"
              "        | false when parts |> Array.forall segment.IsMatch |> not -> ValueSome (SignalNameError.NotAPath name)"
              "        | false when false -> ValueSome (SignalNameError.NotAPath name)"

          m
              "an underscore at the start of a signal name is allowed"
              "Signal would accept _secret for a server signal, and Datastar keeps such a signal in the browser"
              "Expr.fs"
              "        | false when startsWithUnderscore name -> ValueSome (SignalNameError.StartsWithUnderscore (scope, name))"
              "        | false when false -> ValueSome (SignalNameError.StartsWithUnderscore (scope, name))"

          m
              "a blank signal name is allowed"
              "Signal would accept a name of spaces and build an attribute with no name in it"
              "Expr.fs"
              (lines
                  [ "        match String.IsNullOrWhiteSpace name with"
                    "        | true -> ValueSome SignalNameError.Blank" ])
              (lines
                  [ "        match String.IsNullOrWhiteSpace name with"
                    "        | true -> ValueNone" ])

          m
              "the segment rule lets a name start with a digit"
              "Signal would accept 1abc, which Datastar cannot read as a path"
              "Expr.fs"
              "    /// \\z is the end of the text. A dollar sign would also match before a final line break.\n    let internal segment = Regex(@\"^[a-z](?:[A-Za-z0-9]|_(?=[A-Za-z0-9]))*\\z\", RegexOptions.Compiled)"
              "    /// \\z is the end of the text. A dollar sign would also match before a final line break.\n    let internal segment = Regex(@\"^[a-z0-9](?:[A-Za-z0-9]|_(?=[A-Za-z0-9]))*\\z\", RegexOptions.Compiled)"

          m
              "the segment rule lets a name hold a dollar sign"
              "Signal would accept a$b, which is not a signal path at all"
              "Expr.fs"
              "    let internal segment = Regex(@\"^[a-z](?:[A-Za-z0-9]|_(?=[A-Za-z0-9]))*\\z\", RegexOptions.Compiled)\n\n    let internal startsWithUnderscore (name:string) =\n        name.Split('.') |> Array.exists (fun part -> part.StartsWith '_')\n\n    /// The reason a name cannot be used for a signal, or nothing when it can"
              "    let internal segment = Regex(@\"^[a-z](?:[A-Za-z0-9$]|_(?=[A-Za-z0-9]))*\\z\", RegexOptions.Compiled)\n\n    let internal startsWithUnderscore (name:string) =\n        name.Split('.') |> Array.exists (fun part -> part.StartsWith '_')\n\n    /// The reason a name cannot be used for a signal, or nothing when it can"

          m
              "the segment rule stops anchoring the end with a real end of line"
              "a name ending in a line break would be accepted, and \\z is what stops that"
              "Expr.fs"
              "    let internal segment = Regex(@\"^[a-z](?:[A-Za-z0-9]|_(?=[A-Za-z0-9]))*\\z\", RegexOptions.Compiled)\n\n    let internal startsWithUnderscore"
              "    let internal segment = Regex(@\"^[a-z](?:[A-Za-z0-9]|_(?=[A-Za-z0-9]))*$\", RegexOptions.Compiled)\n\n    let internal startsWithUnderscore" ]

    // Falco.Markup does not escape attribute names either, so a quote in one adds attributes of its own.
    let private attributeNames =
        [ m
              "a double quote is allowed in an attribute name"
              "Ds.class' with a hostile class name would add an attribute of its own to the element"
              "Utility.fs"
              "        SearchValues.Create(String(Array.ofList whitespaceAndControl) + \"\\\"'`<>/=\")"
              "        SearchValues.Create(String(Array.ofList whitespaceAndControl) + \"'`>/=\")"

          m
              "whitespace is allowed in an attribute name"
              "Ds.onEvent with a name that has a space in it would end the attribute name early"
              "Utility.fs"
              "            [ yield! seq { 0 .. 32 }"
              "            [ yield! seq { 33 .. 32 }"

          m
              "a double underscore is allowed in an attribute name"
              "card__title would toggle a class called card, because Datastar reads __ as a modifier"
              "Utility.fs"
              "        match (match name.Contains '_' with | true -> name.IndexOf(\"__\", StringComparison.Ordinal) | false -> -1) with\n        | -1 -> ()"
              "        match -1 with\n        | -1 -> ()"

          m
              "the name of a modifier is not checked"
              "building a DsAttr by hand with a quote in a modifier name would add an attribute"
              "Types.fs"
              "                    Guard.attributeName \"modifier name\" false modifier.Name"
              "                    ignore modifier.Name"

          m
              "the plugin name is not checked"
              "building a DsAttr by hand with a quote in the plugin name would add an attribute"
              "Types.fs"
              "        Guard.attributeName \"attribute name\" false dsAttr.Name"
              "        ignore dsAttr.Name"

          m
              "an attribute is written with the wrong slug prefix"
              "every attribute would start with datax- and Datastar would ignore it"
              "Types.fs"
              "        |> _.Append(Constants.dataSlugPrefix) |> _.Append('-')"
              "        |> _.Append(\"dat\") |> _.Append('-')"

          m
              "the separator before an attribute target is a slash"
              "data-on would lose its colon and Datastar would not read the target"
              "Types.fs"
              "            | ValueSome target -> sb.Append(':') |> _.Append(target)"
              "            | ValueSome target -> sb.Append('/') |> _.Append(target)" ]

    // An option that is written when it should not be, or that is left out when it should be, changes what the browser
    // does without any compiler message.
    let private requestOptions =
        [ m
              "filterSignals drops the rule that keeps browser signals out of requests"
              "an exclude would send signals whose names start with an underscore to the server"
              "Types.fs"
              "            let excludeAlso (pattern:string) = \"(^|\\\\.)_|(?:\" + pattern + \")\""
              "            let excludeAlso (pattern:string) = \"(?:\" + pattern + \")\""

          m
              "Retry is written even when it is the default"
              "every action would carry options that Datastar would apply anyway"
              "Types.fs"
              "        if options.Retry <> defaults.Retry then\n            add \"retry\" (json (Retry.Serialize options.Retry))"
              "        add \"retry\" (json (Retry.Serialize options.Retry))"

          m
              "RetryMaxWait is never written"
              "a RetryMaxWait would be ignored by Datastar, which is what the changelog says used to happen"
              "Types.fs"
              "        if options.RetryMaxWait <> defaults.RetryMaxWait then\n            add \"retryMaxWait\" (json options.RetryMaxWait.TotalMilliseconds)"
              "        ()"

          m
              "an AbortController name is not checked"
              "any text could be written as code in the page, where a quote would run as JavaScript"
              "Types.fs"
              "                Guard.signalReference \"RequestOptions.RequestCancellation\" controller"
              "                ignore controller"

          m
              "RetryScaler is not checked for being a finite number"
              "a RetryScaler of NaN or an infinity would be written into the page"
              "Types.fs"
              "            if not (Double.IsFinite options.RetryScaler) then"
              "            if false then"

          m
              "a repeated header name is not refused"
              "two values for one header name would be written, and a request sends the name once"
              "Types.fs"
              "                if not (names.Add name) then"
              "                if false then"

          m
              "an empty AbortController name is not refused"
              "an AbortController with no name would be written into the page"
              "Types.fs"
              "            | AbortController controller when String.IsNullOrWhiteSpace controller ->"
              "            | AbortController controller when false ->" ]

    // An operator that changes what the browser does, and a parenthesis that stops a later operator reaching in.
    let private expressions =
        [ m
              "dividing whole numbers keeps the fraction"
              "an int signal would hold 3.5, which no other expression would expect"
              "Expr.fs"
              "        | Expr quotient, true -> Expr $\"Math.trunc{quotient}\""
              "        | Expr quotient, true -> Expr quotient"

          m
              "subtraction becomes addition"
              "count - 1 would raise the count instead of lowering it"
              "Expr.fs"
              "    let subtract (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable = binary \"-\" left right"
              "    let subtract (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable = binary \"+\" left right"

          m
              "equality becomes a loose comparison"
              "1 would equal '1', so a condition would pass on the wrong value"
              "Expr.fs"
              "    let equal (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : equality = binary \"===\" left right"
              "    let equal (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : equality = binary \"==\" left right"

          m
              "notEqual stops being strict"
              "1 would not equal '1', so a condition would pass on the wrong value"
              "Expr.fs"
              "    let notEqual (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : equality = binary \"!==\" left right"
              "    let notEqual (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : equality = binary \"!=\" left right"

          m
              "negation loses its parentheses"
              "an operator next to it would apply to only part of the expression"
              "Expr.fs"
              "    let negate (Expr value:Expr<bool>) : Expr<bool> = Expr $\"(!{value})\""
              "    let negate (Expr value:Expr<bool>) : Expr<bool> = Expr $\"!{value}\""

          m
              "andAlso becomes orElse"
              "a condition that needs both to be true would fire when only one is"
              "Expr.fs"
              "    let andAlso (left:Expr<bool>) (right:Expr<bool>) : Expr<bool> = binary \"&&\" left right"
              "    let andAlso (left:Expr<bool>) (right:Expr<bool>) : Expr<bool> = binary \"||\" left right"

          m
              "orElse becomes andAlso"
              "a condition that needs only one would need both"
              "Expr.fs"
              "    let orElse (left:Expr<bool>) (right:Expr<bool>) : Expr<bool> = binary \"||\" left right"
              "    let orElse (left:Expr<bool>) (right:Expr<bool>) : Expr<bool> = binary \"&&\" left right"

          m
              "a binary operator loses its parentheses"
              "a nested operation would bind to only one operand"
              "Expr.fs"
              "    let internal binary (operator:string) (Expr left) (Expr right) = Expr $\"({left} {operator} {right})\""
              "    let internal binary (operator:string) (Expr left) (Expr right) = Expr $\"{left} {operator} {right}\""

          m
              "a negative number loses its parentheses"
              "an operator next to it would change its meaning"
              "Expr.fs"
              "    let int (value:int) : Expr<int> = Expr (match value < 0 with | true -> $\"({value})\" | false -> string value)"
              "    let int (value:int) : Expr<int> = Expr (string value)"

          m
              "a float loses its culture invariant"
              "1.5 would be written as 1,5, which the browser cannot read as a number"
              "Utility.fs"
              "        | _ -> value.ToString(\"R\", CultureInfo.InvariantCulture)"
              "        | _ -> value.ToString(\"R\")"

          m
              "a string value stops being quoted"
              "a text value would be written as a bare name and read as a signal"
              "Utility.fs"
              "        | -1 -> String.Concat(\"'\", value, \"'\")"
              "        | -1 -> value" ]

    // A separator that Datastar reads differently changes what runs at all, and a backend action takes its arguments in
    // a fixed order, so getting the order wrong is silent.
    let private statements =
        [ m
              "Stmt.all joins with a comma"
              "several statements in one attribute would stop being separate statements"
              "Expr.fs"
              "        | _ -> Stmt (String.Join(\"; \", statements |> List.map toString))"
              "        | _ -> Stmt (String.Join(\", \", statements |> List.map toString))"

          m
              "Stmt.all allows an empty list"
              "an empty expression would reach Datastar, which throws on it"
              "Expr.fs"
              "        | [] -> raise (ArgumentException(\"Stmt.all needs at least one statement. An attribute with an empty expression makes Datastar throw ValueRequired, so leave the attribute out instead.\", \"statements\"))"
              "        | [] -> Stmt \"\" "

          m
              "toggle stops flipping the signal"
              "a menu would open once and never close"
              "Expr.fs"
              "    let toggle (signal:Signal<bool>) : Stmt = Stmt $\"{Signal.reference signal} = !{Signal.reference signal}\""
              "    let toggle (signal:Signal<bool>) : Stmt = Stmt $\"{Signal.reference signal} = {Signal.reference signal}\""

          m
              "setAll puts the filter before the value"
              "@setAll would be called with its arguments swapped and would do nothing"
              "Expr.fs"
              "        | false -> $\"@setAll({Js.literal value}, {SignalsFilter.Serialize filter})\""
              "        | false -> $\"@setAll({SignalsFilter.Serialize filter}, {Js.literal value})\""

          m
              "toggleAll is called with a value as well"
              "@toggleAll takes only a filter, so the call would do nothing"
              "Expr.fs"
              "        | false -> $\"@toggleAll({SignalsFilter.Serialize filter})\""
              "        | false -> $\"@toggleAll(1, {SignalsFilter.Serialize filter})\""

          m
              "a request action names the wrong verb"
              "a @delete would be sent as a @get, which is a different and safe method"
              "Expr.fs"
              "            | Delete url -> \"delete\", url"
              "            | Delete url -> \"get\", url"

          m
              "a URL stops being escaped"
              "a quote in a URL would end the JavaScript string that holds it"
              "Expr.fs"
              "        | ValueNone -> $\"@{name}({Js.stringLiteral url})\""
              "        | ValueNone -> $\"@{name}('{url}')\"" ]

    // Ds.fs is the public surface: the functions a page is built from. A plugin name written even slightly wrong is an
    // attribute Datastar simply does not run, and nothing else would notice. Each entry anchors on the line the name is
    // on together with the line after it, so it names one helper rather than a shape several of them share.
    //
    // A helper with a typed overload and a string overload appears twice in this file, so mutating one of them still
    // leaves the other correct and the mutant survives. Those are listed twice, once per overload, so that each of them
    // has to be killed on its own.
    let private dsPluginNames =
        [ m "Ds.attr writes the wrong plugin name" "a page would carry data-attrx, so no attribute would be bound" "Ds.fs"
            "        DsAttr.create (\"attr\", targetName = attributeName, value = expression)"
            "        DsAttr.create (\"attrx\", targetName = attributeName, value = expression)"

          m "Ds.style writes the wrong plugin name" "a page would carry data-stylex, so no style would be set" "Ds.fs"
            "        DsAttr.create (\"style\", targetName = styleProperty, value = propertyValueExpression)"
            "        DsAttr.create (\"stylex\", targetName = styleProperty, value = propertyValueExpression)"

          m "Ds.text writes the wrong plugin name" "a page would carry data-textx, so no text would be bound" "Ds.fs"
            "        DsAttr.create (\"text\", value = expression)"
            "        DsAttr.create (\"textx\", value = expression)"

          m "Ds.show writes the wrong plugin name" "a page would carry data-showx, so the element would never hide" "Ds.fs"
            "        DsAttr.create (\"show\", value = boolExpression)"
            "        DsAttr.create (\"showx\", value = boolExpression)"

          // The typed overloads of the two above, which carry the same plugin name on a different line
          m "the typed Ds.text writes the wrong plugin name" "a page would carry data-textx, so no text would be bound" "Ds.fs"
            "        DsAttr.create (\"text\", value = Expr.toString expression)"
            "        DsAttr.create (\"textx\", value = Expr.toString expression)"

          m "the typed Ds.show writes the wrong plugin name" "a page would carry data-showx, so the element would never hide" "Ds.fs"
            "        DsAttr.create (\"show\", value = Expr.toString condition)"
            "        DsAttr.create (\"showx\", value = Expr.toString condition)"

          m "Ds.effect writes the wrong plugin name" "a page would carry data-effectx, so the effect would never run" "Ds.fs"
            "        DsAttr.create (\"effect\", value = expression)"
            "        DsAttr.create (\"effectx\", value = expression)"

          m "Ds.nonce writes the wrong plugin name" "the nonce would be on an attribute Datastar never reads" "Ds.fs"
            "        DsAttr.create (\"nonce\", value = Js.attrEncode nonce)"
            "        DsAttr.create (\"noncex\", value = Js.attrEncode nonce)"

          m "Ds.class' writes the wrong plugin name" "a page would carry data-classx, so no class would be toggled" "Ds.fs"
            "        DsAttr.start \"class\"\n        |> DsAttr.addTarget className"
            "        DsAttr.start \"classx\"\n        |> DsAttr.addTarget className"

          m "Ds.computed writes the wrong plugin name" "the computed signal would never be created" "Ds.fs"
            "        DsAttr.start \"computed\"\n        |> DsAttr.addSignalPathTarget signalPath"
            "        DsAttr.start \"computedx\"\n        |> DsAttr.addSignalPathTarget signalPath"

          m "Ds.ref writes the wrong plugin name" "the element reference would never be created" "Ds.fs"
            "        DsAttr.start \"ref\"\n        |> DsAttr.addValue (Js.attrEncode signalPath)"
            "        DsAttr.start \"refx\"\n        |> DsAttr.addValue (Js.attrEncode signalPath)"

          m "Ds.ref stops escaping the signal name"
            "a quote in the name would end the attribute and add attributes of its own"
            "Ds.fs"
            "        |> DsAttr.addValue (Js.attrEncode signalPath)"
            "        |> DsAttr.addValue signalPath"

          m "Ds.indicator writes the wrong plugin name" "the loading signal would never be created" "Ds.fs"
            "        DsAttr.start \"indicator\"\n        |> DsAttr.addSignalPathTarget signalPath"
            "        DsAttr.start \"indicatorx\"\n        |> DsAttr.addSignalPathTarget signalPath"

          m "Ds.preserveAttr writes the wrong plugin name" "nothing would be preserved across a morph" "Ds.fs"
            "        DsAttr.start \"preserve-attr\"\n        |> DsAttr.addValue attributeName"
            "        DsAttr.start \"preserve-attrx\"\n        |> DsAttr.addValue attributeName"

          m "Ds.ignoreSelf writes the wrong plugin name" "Datastar would walk the element instead of skipping it" "Ds.fs"
            "        DsAttr.start \"ignore\"\n        |> DsAttr.addModifier { Name=\"self\"; Tags = [] }"
            "        DsAttr.start \"ignorex\"\n        |> DsAttr.addModifier { Name=\"self\"; Tags = [] }"

          m "Ds.jsonSignalsOptions writes the wrong plugin name" "the debug output would never be written" "Ds.fs"
            "        DsAttr.start \"json-signals\"\n        |> DsAttr.addModifierNameIf \"terse\""
            "        DsAttr.start \"json-signalsx\"\n        |> DsAttr.addModifierNameIf \"terse\""

          m "Ds.onInit writes the wrong plugin name" "the expression would never run when the page loads" "Ds.fs"
            "        DsAttr.start \"init\"\n        |> DsAttr.addModifierOption (delayMs"
            "        DsAttr.start \"initx\"\n        |> DsAttr.addModifierOption (delayMs"

          m "Ds.onInterval writes the wrong plugin name" "the interval would never fire" "Ds.fs"
            "        DsAttr.start \"on-interval\"\n        |> DsAttr.addModifierNameIf \"viewtransition\""
            "        DsAttr.start \"on-intervalx\"\n        |> DsAttr.addModifierNameIf \"viewtransition\""

          m "Ds.onSignalPatch writes the wrong plugin name" "the expression would never run when a signal is patched" "Ds.fs"
            "        DsAttr.start \"on-signal-patch\"\n        |> DsAttr.addModifierOption (delayMs"
            "        DsAttr.start \"on-signal-patchx\"\n        |> DsAttr.addModifierOption (delayMs"

          m "Ds.onSignalPatchFilter writes the wrong plugin name" "the filter would not be read by the patch attribute" "Ds.fs"
            "        DsAttr.start \"on-signal-patch-filter\"\n        |> DsAttr.addValue (signalsFilter |> SignalsFilter.Serialize)"
            "        DsAttr.start \"on-signal-patch-filterx\"\n        |> DsAttr.addValue (signalsFilter |> SignalsFilter.Serialize)"

          m "Ds.onIntersect writes the wrong plugin name" "the expression would never run when the element scrolls into view" "Ds.fs"
            "        DsAttr.start \"on-intersect\"\n        |> (fun dsAttr ->"
            "        DsAttr.start \"on-intersectx\"\n        |> (fun dsAttr ->"

          m "Ds.bindProp writes the wrong plugin name" "the signal would not be bound to the property" "Ds.fs"
            "        DsAttr.start \"bind\"\n        |> DsAttr.addTarget (signalPath |> SignalPath.kebabValue)\n        |> DsAttr.addModifierOption (events |> Option.filter"
            "        DsAttr.start \"bindx\"\n        |> DsAttr.addTarget (signalPath |> SignalPath.kebabValue)\n        |> DsAttr.addModifierOption (events |> Option.filter"

          m "Ds.bindEvent writes the wrong plugin name" "the events that sync the value would never be attached" "Ds.fs"
            "        DsAttr.start \"bind\"\n        |> DsAttr.addTarget (signalPath |> SignalPath.kebabValue)\n        |> DsAttr.addModifier { Name = \"event\"; Tags = events }"
            "        DsAttr.start \"bindx\"\n        |> DsAttr.addTarget (signalPath |> SignalPath.kebabValue)\n        |> DsAttr.addModifier { Name = \"event\"; Tags = events }"

          m "Ds.signal writes the wrong plugin name" "the signal would never be created" "Ds.fs"
            "        DsAttr.start \"signals\"\n        |> DsAttr.addSignalPathTarget signalPath\n        |> DsAttr.addModifierNameIf \"ifmissing\" (defaultArg ifMissing false)\n        |> DsAttr.addValue (Js.literal signalValue)"
            "        DsAttr.start \"signalsx\"\n        |> DsAttr.addSignalPathTarget signalPath\n        |> DsAttr.addModifierNameIf \"ifmissing\" (defaultArg ifMissing false)\n        |> DsAttr.addValue (Js.literal signalValue)" ]

    // Rocket.fs writes what a component reads from its attributes, and the names of its template directives.
    let private rocket =
        [ m
              "a Rocket prop name is not kebab-cased"
              "the attribute would be maxCount, which the HTML parser lowercases and Rocket would not find"
              "Rocket.fs"
              "        let attributeName = String.datastarKebab name"
              "        let attributeName = name"

          m
              "a Rocket local signal loses one of its dollars"
              "the expression would name a page signal instead of the component's own"
              "Rocket.fs"
              "        \"$$\" + name"
              "        \"$\" + name"

          m
              "a Rocket call is written without its at sign"
              "Rocket would not treat the expression as a call to a component action"
              "Rocket.fs"
              "        $\"@{name}({arguments})\""
              "        $\"{name}({arguments})\""

          m
              "a Rocket template for directive is named data-forEach"
              "Rocket looks for data-for, so the template would never repeat"
              "Rocket.fs"
              "        Elem.template [ Attr.create \"data-for\" safeExpression ] children"
              "        Elem.template [ Attr.create \"data-forEach\" safeExpression ] children"

          m
              "a Rocket else branch is written as data-if"
              "the else branch of a conditional would be treated as another condition"
              "Rocket.fs"
              "        Elem.template [ Attr.createBool \"data-else\" ] children"
              "        Elem.template [ Attr.createBool \"data-if\" ] children" ]

    // Request.fs reads a body that arrives over a connection, so the limit and the chunk size decide what it accepts.
    let private requestBody =
        [ m
              "the manifest limit is ten mebibytes instead of one"
              "a body of two mebibytes would be read whole, which is what the limit is there to stop"
              "Request.fs"
              "let private maxManifestBytes = 1024 * 1024"
              "let private maxManifestBytes = 10 * 1024 * 1024"

          m
              "a cancelled request is reported as a failure to connect"
              "a page that went away would be told its connection broke, which is a different thing to act on"
              "Request.fs"
              "        | :? OperationCanceledException -> return Error RocketManifestError.Cancelled"
              "        | :? OperationCanceledException -> return Error (RocketManifestError.ConnectionFailed \"cancelled\")" ]

    // Response.fs is what a browser receives, so each of these changes the wire format rather than the library's output.
    let private responses =
        [ m
              "a response starts the stream twice"
              "the headers would be written twice, which a browser would reject"
              "Response.fs"
              "let ofHtmlElements (elements:XmlNode) =\n    (fun ctx -> nu (task {\n        do! sseStartResponse ctx\n        return! sseHtmlElements ctx elements\n    }))"
              "let ofHtmlElements (elements:XmlNode) =\n    (fun ctx -> nu (task {\n        do! sseStartResponse ctx\n        do! sseStartResponse ctx\n        return! sseHtmlElements ctx elements\n    }))"

          m
              "ofHtmlElementsOptions ignores the options the caller passed"
              "a selector or a patch mode the caller asked for would be ignored, and the defaults used"
              "Response.fs"
              "let ofHtmlElementsOptions (options:PatchElementsOptions) (elements:XmlNode) =\n    (fun ctx -> nu (task {\n        do! sseStartResponse ctx\n        return! sseHtmlElementsOptions ctx options elements\n    }))"
              "let ofHtmlElementsOptions (options:PatchElementsOptions) (elements:XmlNode) =\n    (fun ctx -> nu (task {\n        do! sseStartResponse ctx\n        return! sseHtmlElements ctx elements\n    }))"

          m
              "ssePatchSignalsOptions ignores the serializer options the caller passed"
              "property names would keep their F# casing instead of the casing the caller asked for"
              "Response.fs"
              "    ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, (signals, jsonSerializerOptions) |> JsonSerializer.Serialize, patchSignalsOptions)"
              "    ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, JsonSerializer.Serialize(signals), patchSignalsOptions)"

          m
              "ofRemoveElementOptions ignores the options the caller passed"
              "whatever the caller set on the options would be ignored"
              "Response.fs"
              "let ofRemoveElementOptions (options:RemoveElementOptions) (selector:Selector) =\n    (fun ctx -> nu (task {\n        do! sseStartResponse ctx\n        return! sseRemoveElementOptions ctx options selector\n    }))"
              "let ofRemoveElementOptions (options:RemoveElementOptions) (selector:Selector) =\n    (fun ctx -> nu (task {\n        do! sseStartResponse ctx\n        return! sseRemoveElement ctx selector\n    }))" ]

    // RocketManifest.fs reads a document that a page posted, so a version or a default that is off means a document
    // the library does not understand. This file had no mutants at all until the run reported it, which is the whole
    // reason the report exists: a safety net that cannot fire is the same as no safety net.
    let private manifest =
        [ m
              "the manifest reader accepts any version"
              "a manifest this library cannot read would be parsed as if it could"
              "RocketManifest.fs"
              "                | true, number when number = supportedVersion -> Ok number"
              "                | true, number when number >= 0 -> Ok number"

          m
              "a missing property in the manifest is not an error"
              "a manifest with no tag or no name would be read as an empty component"
              "RocketManifest.fs"
              "            | true, value -> ValueSome value\n            | false, _ -> ValueNone\n        | _ -> ValueNone"
              "            | true, value -> ValueSome value\n            | false, _ -> ValueSome (JsonDocument.Parse(\"null\").RootElement)\n        | _ -> ValueNone"

          m
              "the manifest reader stops checking the generatedAt date"
              "a manifest with no date would be read as if it had one"
              "RocketManifest.fs"
              "            | true, moment -> Ok moment"
              "            | true, moment -> Ok (DateTimeOffset.MinValue)"

          m
              "a codec name this library does not know is refused"
              "a newer Rocket that adds a codec would break the manifest reader instead of keeping the name"
              "RocketManifest.fs"
              "        | other -> RocketPropType.Other other"
              "        | other -> raise (ArgumentException(other))"

          m
              "an unknown event kind is refused"
              "a newer Rocket that adds a kind of event would break the manifest reader instead of keeping the name"
              "RocketManifest.fs"
              "        | other -> RocketEventKind.Other other"
              "        | other -> raise (ArgumentException(other))" ]

    /// Every mutant, in the order they are run
    let all: Mutant list =
        escaping
        @ signalNames
        @ attributeNames
        @ requestOptions
        @ expressions
        @ statements
        @ dsPluginNames
        @ rocket
        @ requestBody
        @ responses
        @ manifest

    /// The files the list touches, so a run can say which ones it says nothing about
    let filesTouched =
        all |> List.map (fun mutant -> mutant.File) |> List.distinct |> List.sort
