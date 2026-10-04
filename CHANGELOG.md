# Changelog

## 1.4.0

### What is new

- Typed signals, expressions and statements: `Signal.browser`, `Signal.server`, `Signal.rocket`, `Expr` and `Stmt`, and overloads of the `Ds` attributes that take them.
- Rocket helpers: props, component signals, template directives and a reader for the manifest that `publishRocketManifests` posts.
- `Ds.nonce` for Content Security Policy mode, `Ds.withCase` for the `__case` modifier, `Ds.bindProp` and `Ds.bindEvent`, `Ds.query`, `Ds.peek`, `Ds.setAllFiltered` and `Ds.toggleAllFiltered`.
- `RequestCancellation.Cleanup` and `OnEventModifier.Document`, and, from SDK 1.4.0, `PatchElementsOptions.ViewTransitionSelector`.
- Datastar 1.0.4 is the default script, and Rocket has its own script helper, `Ds.rocketCdnScript`.
- Names are checked. A signal name that Datastar would read differently from an expression, and a name that could end an attribute name early, raise an error that says what to write. See [names that are refused](#names-that-are-refused).
- Request options that never worked now do: `FilterSignals` and `AbortController`. See the output changes below.
- The test suite covers every rule in four ways: named tests that pin the output, property tests that hold for any input, deterministic simulation tests seeded so a failure replays, and mutation tests that make a deliberate mistake in the source and require the suite to notice. Nothing in the public API changes because of this.

### Upgrading

This section covers upgrading from version 1.3.0 or earlier. It lists what can stop your code compiling, what can produce warnings, what can clash with your own names, what changes the output without any compiler message, what changes in your dependencies, and what changes in Datastar itself.

#### Changes that stop your code compiling

**`OpenWhenHidden` is now `bool voption`.** The compiler reports error FS0001. Wrap the value in `ValueSome`:

```fsharp
// before
{ RequestOptions.Defaults with OpenWhenHidden = true }

// after
{ RequestOptions.Defaults with OpenWhenHidden = ValueSome true }
```

Take care with `OpenWhenHidden = false`. The old code never sent it, so `@post`, `@put`, `@patch` and `@delete` ignored it and kept running while the page was hidden, because that is Datastar's default for them.
`ValueSome false` is sent, so those requests are now cancelled when the page is hidden and started again when it is visible.
If you want the old behaviour, delete the line. On a `@get`, `ValueSome false` is the same as leaving it out.

**Overloads.** The typed `Ds.text`, `Ds.show`, `Ds.class'`, `Ds.signal`, `Ds.computed`, `Ds.indicator`, `Ds.onEvent`, `Ds.onClick`, `Ds.onInit`, `Ds.effect`, `Ds.onInterval`, `Ds.onIntersect` and `Ds.onSignalPatch` are overloads of the string versions.
Code that passes an argument whose type is not yet known, such as `let click handler = Elem.button [ Ds.onClick handler ] []`, or `List.map Ds.show`, now fails with error FS0041, "A unique overload for method could not be determined based on type information prior to this program point".
Add a type annotation: `let click (handler: string) = ...`. Code that passes a string or a typed value directly needs no change. `Ds.signal` is also no longer `inline`, which changes nothing for callers.
The examples above are compiled as part of the test suite, so the compiler checks this advice stays true.

#### Changes that produce warnings

Three types have a new case: `BackendAction.Query`, `RequestCancellation.Cleanup` and `OnEventModifier.Document`.
A `match` that lists every case of one of these types now gets warning FS0025, which is an error if your project treats warnings as errors.
Add a case for the new value, or a `_` case. Until you do, a value that reaches the `match` without a case raises `MatchFailureException`.

#### Name clashes

`open Falco.Datastar` now brings `Query`, `Cleanup` and `Document` into scope as union cases, and `Expr`, `Stmt` and `Signal` into scope as types and modules. `Expr` is also the name of `Microsoft.FSharp.Quotations.Expr`.
If your own code declares a union case with one of these names, and you open `Falco.Datastar` *after* that declaration, your name now means Falco's case. You get errors such as "expected to have type `Msg` but here has type `BackendAction`".
Either move `open Falco.Datastar` above your type, or put the type name in front of the case:

```fsharp
match msg with
| Msg.Query text -> text
| Msg.Cleanup -> "cleaned up"
```

A module or type of your own called `Rocket` does not clash.

#### Changes that compile but produce different output

**Text in expressions is escaped.** `Ds.get`, `Ds.post`, `Ds.put`, `Ds.patch`, `Ds.delete` and `Ds.query` now escape the URL, and `Ds.signal` escapes its value.
A URL such as `/items?a=1&b=2` is written with `&amp;`, which the browser reads as `&`. Before, a `'` in a URL or in a text value broke the expression, and text from a user could run code.
Tests that compare the generated text may need new expected values.

**`Ds.setAll` and `Ds.toggleAll`.** They used to write `@setAll('foo.', true)` and `@toggleAll('foo.')`. Datastar reads `@setAll(value, filter)` and `@toggleAll(filter)`, so the prefix was being taken as the value and the value as the filter, and neither call did anything. They now write `@setAll(true, { include: /^foo\./ })` and `@toggleAll({ include: /^foo\./ })`. Your code needs no change.

The prefix is also escaped now, so a prefix that contains a character with a meaning in a regular expression is matched literally. `Ds.setAll("foo.", true)` is unchanged, because a trailing dot matches itself.

**`RequestOptions.Retry`.** It used to be ignored, because the library never wrote it. `Retry = OnError`, `OnAlways` and `OnNever` are now sent, so if your code sets one of them, the retry behaviour of that request changes.
`OnNever` stops the retries of a response that is not 200. Datastar 1.0.4 still retries after a network error, up to `RetryMaxCount` times.

**`RequestOptions.RetryMaxWait`.** It was written as `retryMaxWaitMs`. Datastar stopped reading that name in 1.0.0 (RC.8 and earlier read it), so the setting was ignored. It is now written as `retryMaxWait`, which Datastar 1.0.4 reads.

**`ContentType = CustomJson obj`.** It used to send an option called `override`, which Datastar does not have, so Datastar ignored it and sent the signals as usual.
It now sends your object as the request body. If your server code expects the signals, change it to expect your object, or stop using `CustomJson`.

**`OpenWhenHidden = ValueSome true`** is written as the JSON boolean `true`. It used to be written as the text `"true"`.

**`FilterSignals`, `AbortController` and `Headers`.** `FilterSignals` with any pattern used to raise `JsonReaderException`, because the filter text is not JSON. It is now sent as `{"filterSignals":{"include":"^foo"}}`.
A filter that has an exclude but no include keeps Datastar's rule that signals whose names start with an underscore stay in the browser, which Datastar would otherwise drop when it is given an exclude of its own.
`AbortController "$controller"` used to be sent as the text `"$controller"`, which Datastar ignores. It is now sent as the signal `$controller`. A `RetryScaler` that is not a number, and an `AbortController` without a name, raise an `ArgumentException` that says what to write.

**A header name that is given twice now raises**, where before it was written out. The check ignores case, so `[ "X-A", "1"; "x-a", "2" ]` used to produce both keys and now raises an `ArgumentException`: a request sends each header name once, and the browser cannot tell you which one won. Put the values in one header, separated by commas.

**`RequestCancellation = AbortController` is checked.** The name goes into the page as code, because Datastar only accepts an `AbortController` object and not the name of one. It is now checked to be the name of a signal, such as `"$controller"`, `"$_controller"` or `"$form.controller"`, and anything else raises an `ArgumentException` that says so.
Before this, any text could be written as code there. If you wrote the name of a variable that is not a signal, such as `AbortController "$ctl"` where `$ctl` is a local, that code raised an `ArgumentException` from now on. Declare it as a signal with `data-signals:_ctl="new AbortController()"`.

**`ContentType = CustomJson null`** now sends `"payload": null`. It used to raise a `NullReferenceException` from inside the serializer.

**Signals filters.** `SignalsFilter.Serialize` now returns text that is ready for an attribute, so if you put its result in an attribute yourself, do not encode it again. `SignalsFilter.Include`, `Exclude` and `Prefix` patterns are regular expressions without the slashes around them. The library escapes a slash and a line break inside the pattern, and encodes the pattern for the attribute.
`Ds.onSignalPatchFilter` and `Ds.jsonSignalsOptions` used to write the pattern as it was, so a quote in it ended the attribute. A pattern that you wrote with slashes around it, such as `"/foo/"`, is now a pattern for the text `/foo/`. Remove the slashes.

**Carriage returns and NUL characters in attribute values.** An HTML parser changes a carriage return in an attribute value into a line feed, and a NUL character into U+FFFD, so `Rocket.propString ("label", "a\rb")` arrived in the browser as different text.
A carriage return is now written as `&#13;`, which a parser keeps. HTML cannot keep a NUL in an attribute, so it is written as U+FFFD, which is what a browser would read. In a JavaScript string literal, a NUL is written as `\u0000`.

**`Ds.safariStreamingFix`** wrote `data-on:pageshow.window`. Datastar 1.0.4 reads modifiers after `__`, so that attribute listened for an event called `pageshow.window` and never ran. It now writes `data-on:pageshow__window`.

**Expressions built with `Expr.unsafeRaw` and `Stmt.unsafeRaw`** are escaped for the attribute, and `Expr.unsafeRaw` puts text that is more than a name in parentheses. `Expr.divide` on whole numbers cuts the result to a whole number.

**`Ds.cdnSrc` and `Ds.cdnScript`.** They now load Datastar 1.0.4. They loaded 1.0.0-RC.7 before, so your pages move across several Datastar releases.
The changes in Datastar that can affect your pages are listed under [Changes in Datastar itself](#changes-in-datastar-itself), and the full list is in the [Datastar release notes](https://github.com/starfederation/datastar/releases).
To stay on the old script for now, write the tag yourself. Note that some things need the newer script: `Ds.bindProp`, `Ds.bindEvent` and `OnEventModifier.Document` need 1.0.0, `Ds.nonce` needs 1.0.3, and the Rocket helpers need the 1.0.4 bundle. `Ds.query`, `RequestCancellation = Cleanup` and `RequestOptions.RetryMaxWait` need a script newer than RC.7 too.

```fsharp
Elem.script [ Attr.type' "module"; Attr.src "https://cdn.jsdelivr.net/gh/starfederation/datastar@1.0.0-RC.7/bundles/datastar.js" ] []
```

#### Names that are refused

Some names used to be accepted and never worked. They now raise an `ArgumentException` that says what to write.

- A name that goes into an attribute name, such as the class in `Ds.class'`, the event in `Ds.onEvent`, the attribute in `Ds.attr'`, the property in `Ds.style`, a Rocket prop name, and the events of `Ds.bindEvent`: it cannot be empty, contain whitespace, a quote, `=`, `/`, `<` or `>`, or contain a double underscore. Datastar reads `__` as the start of a modifier, so `Ds.class' ("card__title", ...)` toggled a class called `card`.
  The name of the plugin itself and the name of each modifier are checked the same way, so building a `DsAttr` by hand cannot put a quote into an attribute name.
- A typed signal name, from `Signal.browser`, `Signal.server`, `Signal.rocket` and `Signal.tryCreate`: a part cannot start with a capital letter, end with an underscore, or have two underscores in a row. HTML makes attribute names lower case, so `Signal.server<int> "Menu"` was declared as `menu` and read as `$Menu`.
- `Rocket.forEach` needs item and index names that are JavaScript identifiers, and `Stmt.all` needs at least one statement.

**A `Signal.rocket` is no longer accepted where a signal name goes into an attribute.** `Ds.signal`, `Ds.bind`, `Ds.computed` and `Ds.indicator` raise an `ArgumentException` for one. The typed overloads used to write it as `data-signals:count`, which is the same text a `Signal.server "count"` writes: Rocket rewrites such an attribute to the component's own signal only for a signal the component declared with `$$('name', value)` in its setup, and only inside that component, so anywhere else the name quietly became a page signal and two components would share it. Reading a Rocket signal in an expression is unchanged and still writes `$$name`.

Inside a component, use `Rocket.local` in an expression, or declare the signal in the component's `setup` and bind to it with `Ds.bind "name"`. To write the initial value, use `Ds.signal (SignalPath.sp "name", value)`, which is what the string helper has always done.

`Signal.tryCreate` returns a `SignalNameError` instead of text. `RocketManifest.parse` and `Request.getRocketManifests` return a `RocketManifestError`. `Request.getRocketManifests` also returns `ConnectionFailed` and `Cancelled` when the connection fails or the request is cancelled, instead of throwing.
`SignalScope`, `SignalNameError` and `RocketManifestError` are `RequireQualifiedAccess`, so their cases do not clash with your names.

**`Ds.ref` now escapes the signal name.** It wrote the name into the attribute's value with no escaping, and Falco.Markup does not escape attribute values, so a name containing a quote ended the attribute and added attributes of its own. `Ds.ref "a\" onmouseover=\"x"` rendered as `<div data-ref="a" onmouseover="x"></div>`. The name is now escaped, the way `Ds.nonce` already was. A name with no quote, angle bracket or ampersand in it, which is every name that was working before, renders exactly as it did.

#### Changes to your dependencies

`Falco.Datastar` now depends on `StarFederation.Datastar.FSharp` 1.4.0. It depended on 1.2.0. Your project picks up the new version by itself, and three things change.

Reading signals for a `@delete` now works. Datastar sends them in the `datastar` query parameter, and versions 1.2.0 and 1.2.1 of the SDK only read that parameter for a `GET`, so `Request.getSignals` and `Request.getSignalsJson` came back empty for a `@delete`. Version 1.3.0 added `DELETE` to that check. If you worked around this by reading the query string yourself, you can remove the workaround.

If your own project references `StarFederation.Datastar.FSharp` directly at a version below 1.3.0, NuGet only warns (`NU1605`, "Detected package downgrade") and uses your older version, and the `@delete` problem comes back. Raise that reference to 1.4.0, or remove it.

`PatchElementsOptions` has a new field, `ViewTransitionSelector`, for scoped view transitions. `PatchElementsOptions.Defaults with ...` needs no change, but code that calls the constructor must pass the new argument.
Everything the SDK sends is otherwise the same as before.

#### Changes in Datastar itself

Moving from the RC.7 script to 1.0.4 changes how some pages behave, whatever your F# code does. These are the changes from the [release notes](https://github.com/starfederation/datastar/releases) that can affect a page built with this library.

**A `$name` inside quotes is no longer replaced (RC.8).** In an expression, `'Hello $name'` now stays as the text `Hello $name`. Join the parts instead, as in `'Hello ' + $name`, or use a template literal, where `${...}` is still replaced: `` `Hello ${$name}` ``. Since 1.0.4 the same holds for `@action(` inside a string. Both forms were checked in a browser.

**A request is no longer cancelled when its element is removed (RC.8).** `RequestCancellation.Auto` used to do that. Use `RequestCancellation = Cleanup` if you want it.

**Requests with the same method and URL cancel each other (1.0.2).** Starting a `@get('/items')` cancels an earlier `@get('/items')` that is still running, even when a different element started it. Use `RequestCancellation = Disabled` to let both run.

**A `@get` or `@delete` has no body and no `Content-Type` header (1.0.0).** Server code that looked for `application/json` on those requests will no longer find it. The signals are in the `datastar` query parameter.

**Checkboxes and radio buttons update the signal on `input` (1.0.2).** `Ds.bind` used to use `change` for them. Use `Ds.bindEvent` to choose the events yourself.

**A patch that changes an input's property fires `datastar-prop-change` and not `change` (1.0.0).** If you relied on the native `change` event after a server patch, listen for the new event with `Ds.onEvent ("datastar-prop-change", ...)`.

**A retried request sends the current signals (RC.8 and 1.0.3).** Before, a retry could send the values the signals had when the first attempt started.
