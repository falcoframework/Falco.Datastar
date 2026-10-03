# Falco.Datastar

[![NuGet Version](https://img.shields.io/nuget/v/Falco.Datastar.svg)](https://www.nuget.org/packages/Falco.Datastar)
[![build](https://github.com/falcoframework/Falco.Datastar/actions/workflows/build.yml/badge.svg)](https://github.com/falcoframework/Falco.Datastar/actions/workflows/build.yml)

```fsharp
open Falco.Markup
open Falco.Datastar

let demo =
    Elem.button
        [ Attr.id "replace_me"
          Ds.onClick (Ds.get "/click-me") ]
        [ Text.raw "Reset" ]
```

[Falco.Datastar](https://github.com/falcoframework/Falco.Datastar) brings type-safe [Datastar](https://data-star.dev) support to [Falco](https://github.com/falcoframework/Falco).
It provides a complete mapping of all [attribute plugins](https://data-star.dev/reference/attributes) and [action plugins](https://data-star.dev/reference/actions).
As well as helpers for retrieving the signals and responding with Datastar Server Side Events.

Upgrading from version 1.3.0 or earlier? See the [changelog](https://github.com/falcoframework/Falco.Datastar/blob/main/CHANGELOG.md).

## Key Features
- Idiomatic mapping of `data-*` attributes (e.g. `data-text`, `data-bind`, `data-signals`, etc.).
- Helper functions for reading signals and responding with Datastar Server Side Events.

## Design Goals
- Create a self-documenting way to integrate Datastar into Falco applications.
- Provide type safety without over-abstracting.

## Getting Started

First off, for any questions or criticisms of this library or [Datastar](http://data-star.dev) in general,
please join our [Discord](https://discord.com/channels/1296224603642925098/1334541716497109042), where we are definitely not a cult.

This guide assumes you have a [Falco](https://github.com/falcoframework/Falco) project setup. If you don't, you can create a new Falco project using the following commands.
The full code for this guide can be found in the [Hello World example](https://github.com/falcoframework/Falco.Datastar/tree/main/examples/HelloWorld).

```shell
> dotnet new web -lang F# -o HelloWorld
> cd HelloWorld
```

Install the nuget package:
```shell
> dotnet add package Falco
> dotnet add package Falco.Datastar
```

Remove any `*.fs` files created automatically, create a new file named `Program.fs` and set the contents to the following:

```fsharp
open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder

let wapp = WebApplication.Create()

let endpoints = [ ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
```

`Ds.cdnScript` loads Datastar from the jsDelivr CDN. It is fixed to the release named in `Ds.datastarVersion`, currently Datastar 1.0.4.
To use [Rocket](https://github.com/starfederation/datastar/tree/v1.0.4/library/src/rocket), Datastar's web components, load `Ds.rocketCdnScript` instead. It is the same release with Rocket included, so load one script or the other, not both.

Now, let's incorporate Datastar into our Falco application. First, we'll define a simple route that returns a button that, when clicked, will
merge an HTML fragment from a GET request.

```fsharp
let handleIndex : HttpHandler =
    let html =
        Elem.html [] [
            Elem.head [] [ Ds.cdnScript ]
            Elem.body [] [
                Text.h1 "Example: Hello World"
                Elem.button
                    [ Attr.id "hello"; Ds.onClick (Ds.get "/click") ]
                    [ Text.raw "Click Me" ]
            ]
        ]
    Response.ofHtml html
```

Next, we'll define a handler for the click event that will return an HTML element from the server to replace the HTML of the button; note the `#hello`.

```fsharp
let handleClick : HttpHandler =
    let html = Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hello, World, from the Server!" ]
    Response.ofHtmlElements html
```

And lastly, we'll make Falco aware of these routes by adding them to the `endpoints` list.

```fsharp
let endpoints =
    [ get "/" handleIndex
      get "/click" handleClick ]
```

Save the file and run the application:

```shell
 dotnet run
```

Open the address that `dotnet run` prints on the line that starts with `Now listening on`, and click the button. You should see the text "Hello, World, from the Server!" appear in the place of the button.

Jump to [Signal Reading and Server Side Events](#reading-signals-and-server-side-events).

## The Tao of Datastar

Datastar's authors describe [the way they mean it to be used](https://data-star.dev/guide/the_tao_of_datastar). This library tries to make that way the easy one.

| The Tao says | In this library |
| --- | --- |
| Most state lives on the backend, which is the source of truth. | Your handlers own the state. Nothing here keeps state for you. |
| Use signals sparingly: for user interactions, and for sending new state to the backend. | Every [signal has a kind](#signals-expressions-and-statements-in-f). `Signal.browser` never leaves the browser, and `Signal.server` is sent with requests. |
| The backend drives the frontend by patching elements and signals. | The `Response` functions patch elements and signals. |
| There is no real benefit to a content type other than `text/event-stream`. | The `Response` functions send Server Side Events. |
| Start with the defaults. | An option that you leave at `RequestOptions.Defaults` is not sent, so Datastar's own default applies. |
| In morph we trust: send big chunks, up to the whole page. | `Response.ofHtmlElements` patches by element `id`, with Datastar's default outer morph. |
| Compress the stream. | See [Compression](#compression). |
| Use CQRS: one long-lived request reads, short requests write. | See [CQRS](#cqrs). |
| Show a loading indicator, and confirm success only from the backend. | Use `Ds.indicator` with a `Signal.browser<bool>`. The library has no helper for optimistic updates. |
| Use anchors to navigate, and let the browser keep the history. | The library has no navigation or history helper. |

### CQRS

One long-lived request reads. The server writes to it whenever something changes. Short requests write, and the change comes back over the stream.

```fsharp
// The long-lived request. The server sends an update whenever the state changes.
Elem.body [ Ds.onInit (Stmt.get "/updates") ] [
    // A short request. The server answers 204 No Content, and the change arrives on the stream.
    Elem.button [ Ds.onClick (Stmt.post "/items/add") ] [ Text.raw "Add" ]
]
```

A 204 response ends without an error and is not retried. The [RocketComponents example](https://github.com/falcoframework/Falco.Datastar/tree/main/examples/RocketComponents) is a whole working app: one agent owns the state, and every visitor sees each change as it happens.

Two details of Datastar's behaviour matter here. The Tao advises showing a loading indicator by hand and hiding it when the backend's update arrives.
`Ds.indicator` turns off when the request ends, and a 204 ends the request before the update comes, so for a write that answers 204, set the indicator yourself and clear it when the update arrives.
Also, `@get` stops its request when the page is hidden and opens it again when the page is visible (`openWhenHidden` is false for `@get`), so the server should send the current state first on every new stream, as the example does.

### Compression

A stream of morphs compresses very well. ASP.NET Core does not compress `text/event-stream` unless you add it:

```fsharp
let builder = WebApplication.CreateBuilder()

builder.Services.AddResponseCompression(fun options ->
    options.EnableForHttps <- true
    options.Providers.Add<BrotliCompressionProvider>()
    options.MimeTypes <- Seq.append ResponseCompressionDefaults.MimeTypes [ "text/event-stream" ])
|> ignore

let wapp = builder.Build()
wapp.UseResponseCompression() |> ignore
```

Each event still reaches the browser as soon as it is sent. In the example the stream is sent with `content-encoding: br`, and a second window shows each change as it arrives.
`EnableForHttps` compresses HTTPS responses too. A response that holds both a secret, such as a CSRF token, and text that an attacker can choose can leak the secret (the BREACH attack), so keep such responses out of the stream, or leave `EnableForHttps` off.

## Signals and Expressions

Signals are reactive variables in the browser. When one changes, every binding and expression that reads it updates.
They can be created and changed with data attributes on the frontend, or by events sent from the backend.

Use them sparingly. The Tao of Datastar says that most state lives on the backend, and that signals are for user interactions, such as toggling an element,
and for sending new state to the backend, such as binding a form input. See [The Tao of Datastar](#the-tao-of-datastar).

[Datastar expressions](https://data-star.dev/guide/datastar_expressions) are strings that are evaluated by bindings, events, and triggers.
Updating a signal value in an expression will cause other bindings and expressions to update elsewhere.

Some important notes: Signals defined later in the DOM tree override those defined earlier.
`data-*` attributes are [evaluated in the order they appear in the DOM](https://data-star.dev/reference/attributes#attribute-evaluation-order); meaning that signals need to be specified before they can be used.

### Signals, expressions and statements in F#

Most functions above take JavaScript as a string, such as `Ds.text "$count"`. Nothing checks the string until the browser runs it.
This library also has typed versions. The compiler then catches a signal that does not exist, a condition that is not a boolean, and a value of the wrong type.
The string versions still work.

A `Signal<'T>` has a type and a kind. The kind decides whether Datastar sends the signal to the server:

- `Signal.browser<'T> "name"` stays in the browser. Datastar never sends it, because its name starts with an underscore, which the function adds for you. Use it for what the user does on the page.
- `Signal.server<'T> "name"` is sent with every request. Use it for new state that the backend needs, such as the value of a form input.
- `Signal.rocket<'T> "name"` belongs to one instance of a [Rocket component](#rocket-components). Rocket keeps these under a private `_rocket` path, so Datastar never sends them.

```fsharp
let count = Signal.browser<int> "count"
let name = Signal.server<string> "form.name"

Elem.div [ Ds.signal (count, 0); Ds.signal (name, "Ada") ] [
    Elem.button [ Ds.onClick (Stmt.set count (Expr.add (Expr.read count) (Expr.int 1))) ] [ Text.raw "+" ]
    Elem.span [ Ds.text (Expr.read count) ] []
    Elem.input [ Ds.bind name ]
    Elem.button [ Ds.onClick (Stmt.post "/save") ] [ Text.raw "Save" ]   // sends form.name only
]
```

In a browser, the save request carried only `{"form":{"name":"..."}}`. The browser signal `count` stayed on the page.

A name that Datastar cannot use is refused with a message that says what to write. Datastar reads the name of a signal from an attribute name, and HTML makes attribute names lower case,
so the name has to read the same there and in an expression. These names are refused:

- a name with a hyphen, because an expression reads a hyphen as minus. Write `myCount`, not `my-count`.
- a name that starts with an underscore. Datastar keeps such a signal in the browser, so a server signal cannot have one, and `Signal.browser` adds the underscore itself.
- a part that starts with a capital letter, such as `Menu` or `form.First`. Start each part with a lower case letter.
- a double underscore, which Datastar reads as the start of a modifier, and an underscore at the end of a part.
- anything that is not letters, digits, underscores and dots.

`Signal.tryCreate scope name` returns the reason as a `SignalNameError`, which has a `Message`. `Signal.browser`, `Signal.server` and `Signal.rocket` raise an `ArgumentException` with the same message.

```fsharp
match Signal.tryCreate<int> SignalScope.Server "Menu" with
| Ok signal -> Ds.signal (signal, 0)
| Error (SignalNameError.StartsWithCapital name) -> failwith $"rename {name}"   // the case tells you what is wrong
| Error error -> failwith error.Message
// error.Message: The signal name 'Menu' has a part that starts with a capital letter. Datastar reads the name from an attribute name,
// and HTML makes attribute names lower case, so the signal would be called 'menu' there but 'Menu' in an expression.
// Start each part with a lower case letter, for example 'menu'.
```

The cases are `Blank`, `HasHyphen`, `StartsWithUnderscore`, `StartsWithCapital`, `HasDoubleUnderscore`, `EndsWithUnderscore` and `NotAPath`. They are `RequireQualifiedAccess`.

An `Expr<'T>` is an expression with a value, and a `Stmt` is something that is done. Build them with these functions:

| For | Functions |
| --- | --- |
| Values | `Expr.int`, `Expr.float`, `Expr.bool`, `Expr.string`, `Expr.read signal` |
| Numbers | `Expr.add`, `Expr.subtract`, `Expr.multiply`, `Expr.divide`, `Expr.remainder` |
| Comparing | `Expr.equal`, `Expr.notEqual`, `Expr.greater`, `Expr.less`, `Expr.atLeast`, `Expr.atMost` |
| Booleans | `Expr.andAlso`, `Expr.orElse`, `Expr.negate` |
| Choosing | `Expr.ifElse condition whenTrue whenFalse` |
| Text | `Expr.concat`, `Expr.toText` |
| Not subscribing | `Expr.peek`, which reads a value without running the expression again when it changes |
| Doing | `Stmt.set signal value`, `Stmt.toggle signal`, `Stmt.all [ ... ]` |
| Many signals | `Stmt.setAll prefix value`, `Stmt.toggleAll prefix`, and `Stmt.setAllWhere` / `Stmt.toggleAllWhere` with a `SignalsFilter` |
| Backend actions | `Stmt.get`, `Stmt.post`, `Stmt.put`, `Stmt.patch`, `Stmt.delete`, `Stmt.query`, and the same with `With` and a `RequestOptions` |

`Ds.text`, `Ds.show`, `Ds.class'`, `Ds.attr'`, `Ds.style`, `Ds.computed`, `Ds.signal`, `Ds.bind`, `Ds.indicator`, `Ds.onEvent`, `Ds.onClick`, `Ds.onInit`, `Ds.effect`, `Ds.onInterval`,
`Ds.onIntersect` and `Ds.onSignalPatch` all have overloads that take them. The typed `Ds.show` takes a boolean expression, and the typed `Ds.onClick` takes a statement.

```fsharp
Expr.toString (Expr.divide (Expr.read count) (Expr.int 5))       // Math.trunc($_count / 5), because count is an int
Expr.toString (Expr.unsafeRaw<bool> "$a || $b")                  // ($a || $b), so Expr.negate gives (!($a || $b))
Expr.toString (Expr.string "it's")                               // 'it\'s'
Stmt.toString (Stmt.all [ Stmt.set count (Expr.int 0); Stmt.toggle menuOpen ])   // $_count = 0; $_menuOpen = !$_menuOpen
```

Some details follow. Operators are written with spaces, because Datastar reads `$a-1` as a signal called `a-1`. `Expr.add` and the other arithmetic functions take numbers, and text is joined with `Expr.concat`.
JavaScript has one kind of number, so `7 / 2` is 3.5 there. `Expr.divide` on whole numbers, such as `int`, cuts the result to a whole number, so an `int` signal never holds a fraction.
Text in `Expr.string`, and the URL in `Stmt.get` and the like, is escaped, so it stays text whatever it contains.
The text inside every `Expr` and `Stmt` is escaped for use in an attribute. `Expr.toString` and `Stmt.toString` return it, for the string helpers such as `Ds.text`. It is not meant for a script element, where the escapes would show.

For what the typed functions do not cover, `Expr.unsafeRaw` and `Stmt.unsafeRaw` pass JavaScript through. Nothing checks that it is valid JavaScript. The library escapes it for the attribute, and `Expr.unsafeRaw` puts it in parentheses when it is more than a name, so an operator around it applies to all of it. Never build it from text that a user can change.
`Stmt.all` needs at least one statement, because an attribute with an empty expression makes Datastar throw.
The same goes for the string helpers: pass user values to Datastar through signals, and keep the text of an expression fixed, as the [Datastar documentation](https://data-star.dev/reference/security) advises.

#### Sections:

- [Index](#attribute-index)
- [Signals, expressions and statements in F#](#signals-expressions-and-statements-in-f)
- [Creating Signals](#creating-signals)
- [Binding to Signals](#signal-binding)
- [Events and Triggers](#events-and-triggers)
- [Actions and Functions](#actions-and-functions)
- [Rocket Components](#rocket-components)
- [When to $](#when-to-)

## _Attribute Index_

- [data-attr](#dsattr--data-attr)
- [data-bind](#dsbind--data-bind)
- [data-class](#dsclass--data-class)
- [data-computed](#dscomputed--data-computed)
- [data-effect](#dseffect--data-effect)
- [data-ignore](#dsignore--dsignoreself--dsignoremorph--data-ignore)
- [data-indicator](#dsindicator--data-indicator)
- [data-json-signals](#dsjsonsignals--dsjsonsignalsoptions--data-json-signals)
- [data-init](#dsoninit--data-init)
- [data-nonce](#dsnonce--data-nonce)
- [data-on](#dsonevent--data-on)
- [data-on-intersect](#dsonintersect--data-on-intersect)
- [data-on-interval](#dsoninterval--data-on-interval)
- [data-on-signal-patch](#dsonsignalpatch--dsonsignalpatchfilter--data-on-signal-patch)
- [data-preserve-attr](#dspreserveattr--data-preserve-attr)
- [data-ref](#dsref--data-ref)
- [data-show](#dsshow--data-show)
- [data-signals](#dssignals--dssignal--data-signals)
- [data-style](#dsstyle--data-style)
- [data-text](#dstext--data-text)

## _Creating Signals_

Create signals, which are reactive variables that automatically propagate their value to all references of the signal.
Important: Never use hyphens when naming signals. A signal whose name starts with an underscore stays in the browser: Datastar does not send it to the server.

### [Ds.signals / Ds.signal : `data-signals`](https://data-star.dev/reference/attributes#data-signals)

Serializes the passed object with [`System.Text.Json.JsonSerializer`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer)
and will merge the signals with the existing signals.

```fsharp
type MySignals() =
    member val firstName = "Don" with get, set
    member val lastName = "Syme" with get, set

let signals = MySignals()

Elem.div [ Ds.signals signals ] []
```

As a convenience, you can create a single signal with the option to add it only if it is missing.

**Important note**: the HTML parser lowercases attribute names, so this library writes the name in kebab-case (`signal-path`), and Datastar reads it as `signalPath`. Use [`Ds.withCase`](#dswithcase--__case) to change that.

```fsharp
Elem.div [ Ds.signal (sp"signalPath", "signalValue", ifMissing = true) ] []
```

### [Ds.computed : `data-computed`](https://data-star.dev/reference/attributes#data-computed)

Creates a read-only signal that is computed based on a [Datastar expression](https://data-star.dev/guide/datastar_expressions). [`data-text`](#dstext--data-text) is used
here to bind and display the signal value. **Important:** Computed signal expressions must not be used for performing actions.
If you need to perform an action in response to a signal change, use the [`data-effect`](#dseffect--data-effect) attribute.

```fsharp
Elem.div [ Ds.computed (sp"foo", "$bar + $baz") ] []
Elem.div [ Ds.text "$foo" ] []
```

### [Ds.ref : `data-ref`](https://data-star.dev/reference/attributes#data-ref)

Creates a new signal that is a reference to the element on which the data attribute is placed. [`data-text`](#dstext--data-text)
is used here to bind and display the signal value.

```fsharp
Elem.div [ Ds.ref "foo" ] []
Elem.div [ Ds.text "$foo.tagName" ] []
```

### [Ds.indicator : `data-indicator`](https://data-star.dev/reference/attributes#data-indicator)

Creates a signal and sets its value to true while an SSE request is in flight, otherwise false.
As an example, the signal can be used to show a loading indicator.

```fsharp
Elem.button [
    Ds.onClick (Ds.get "/fetchBigData")  // make a request to the backend, making fetch happen
    Ds.indicator "_fetching"  // the signal we are creating. The underscore keeps it in the browser, so it is not sent with requests
    Ds.attr' ("disabled", "$_fetching")  // assigns the "disabled" attribute if the `_fetching` signal value is true
    ] [ Text.raw "Fetch!" ]

Elem.div
    [ Ds.show "$_fetching" ]  // show or hide this <div> if the `_fetching` signal value is true or false, respectively
    [ Text.raw "Fetching" ]
```

The previous example uses a couple functions we haven't covered yet. [`Ds.onClick`](#dsonevent--data-on) firing a [`Ds.get`](#get--post--put--patch--delete--query) action, which sends a GET request to the server.
[`Ds.attr'`](#dsattr--data-attr) and [`Ds.show`](#dsshow--data-show) are evaluating the Datastar expression `$_fetching` and are assigning `disabled` attribute and
show/hiding the div, respectively, based on the `_fetching` signal value's "true-ness".

### [Ds.withCase : `__case`](https://data-star.dev/reference/attributes#data-signals)

Datastar reads the name that some attributes create from the attribute's key, and the HTML parser lowercases attribute names.
Because of that, a signal gets a camelCase name, and a class name or an event name gets a kebab-case name.
`Ds.withCase` adds Datastar's `__case` modifier to change that. The styles are `CaseStyle.Camel`, `Kebab`, `Snake` and `Pascal`.
Use it when your server uses another style, such as `snake_case` JSON.

```fsharp
Elem.div [ Ds.signal (sp"myValue", 1) |> Ds.withCase CaseStyle.Snake ] []                   // creates $my_value
Elem.div [ Ds.onEvent ("my-event", "$seen = true") |> Ds.withCase CaseStyle.Camel ] []     // listens for myEvent
```

```html
<div data-signals:my-value__case.snake="1"></div>
<div data-on:my-event__case.camel="$seen = true"></div>
```

It works on `Ds.bind`, `Ds.class'`, `Ds.computed`, `Ds.indicator`, `Ds.onEvent` and `Ds.signal`.
It does nothing on `Ds.ref` and `Ds.signals`, which put the name or the object in the attribute's value. They do not use its key.
Datastar's default casing is the recommended one, so use `Ds.withCase` only when you have a reason, such as a server that uses `snake_case` JSON.

## _Signal Binding_

Binding to a signal means tying an attribute or value of an element to a value that can be modified by another effect.
Example: setting the innerText of a `<div>` to a value that is updated by a server; or, changing the `class` attribute on an element.

### [Ds.bind : `data-bind`](https://data-star.dev/reference/attributes#data-bind)

Creates a two-way binding from a signal to the "value" of an HTML "input" element. Can be placed on any HTML element on which data can be input or choices
selected (e.g. `input`, `textarea` and `select` elements, `input` with `type` of `checkbox` or `radio`, and web components. Although not necessary, you can find the `switch` statement in the
[source](https://github.com/starfederation/datastar/blob/v1.0.4/library/src/plugins/attributes/bind.ts) to see how signals are translated).
The signal will be created if it does not already exist. And the type of the signal is preserved during binding; if an element's value changes,
the signal value is automatically converted to match the original (see the [documentation](https://data-star.dev/reference/attributes#data-bind) for an example.)

```fsharp
Elem.input [ Attr.type' "text"; Ds.bind "firstName" ]
```

An `input` with `type="file"` is a special case: Datastar puts the chosen files into one signal as a list of `{ name; contents; mime }`, where `contents` is a base64 data URI. It does not create the `{name}`, `{name}Names` and `{name}Mimes` signals that older versions did.

For custom elements and web components, `Ds.bindProp` binds a signal to a named property of the element. You can also list the events that copy the property back into the signal.
`Ds.bindEvent` changes only the events, and needs at least one, because with none Datastar never syncs the signal.

Write the property name in kebab-case, because the HTML parser lowercases attribute names. Datastar only removes the hyphens from what it reads, so a name with a capital letter in it is not turned back into camelCase: `"isOn"` would be read as the property `ison`, and the component has no such property.

```fsharp
Elem.create "my-slider" [ Ds.bindProp (sp"volume", "value", [ "change" ]) ] []
Elem.create "my-toggle" [ Ds.bindProp (sp"isOn", "is-on") ] []
Elem.create "my-input" [ Ds.bindEvent (sp"query", "input", [ "change" ]) ] []
```

```html
<my-slider data-bind:volume__prop.value__event.change></my-slider>
<my-toggle data-bind:is-on__prop.is-on></my-toggle>
<my-input data-bind:query__event.input.change></my-input>
```

### [Ds.text : `data-text`](https://data-star.dev/reference/attributes#data-text)

Binds the `text` value of an element to a [Datastar expression](https://data-star.dev/guide/datastar_expressions). The value in `$foo` will be automatically set to the `divs` innerText.

```fsharp
Elem.div [ Ds.text "$foo" ] []
```

### [Ds.attr' : `data-attr`](https://data-star.dev/reference/attributes#data-attr)

Binds the value of an HTML attribute to an expression.

```fsharp
Elem.div [ Ds.attr' ("title", "$foo") ] []
```

### [Ds.show : `data-show`](https://data-star.dev/reference/attributes#data-show)

Shows or hides an element based on whether a [Datastar expression](https://data-star.dev/guide/datastar_expressions) evaluates to true or false.
For anything with custom requirements, use [`data-class`](#dsclass--data-class) instead.

```fsharp
Elem.div [ Ds.show "$foo" ] []
```

### [Ds.class' : `data-class`](https://data-star.dev/reference/attributes#data-class)

Adds or removes a class to or from an element based on the "true-ness" of a [Datastar expression](https://data-star.dev/guide/datastar_expressions).

```fsharp
Elem.div [ Ds.class' ("hidden", "$foo") ] [] // add the 'hidden' class when $foo evaluates to true
```

The class name goes into an attribute name (`data-class:hidden`), so it is checked. A name with a quote, a space, `=`, `/`, `<` or `>` is refused, because HTML would end the attribute name there.
A name with a double underscore, such as `card__title`, is refused too, because Datastar reads `__` as the start of a modifier and would toggle a class called `card`. To toggle such a class, write the object form yourself:
`Attr.create "data-class" "{'card__title': $isActive}"`. The same checks apply to the names in `Ds.attr'`, `Ds.style`, `Ds.onEvent` and `Rocket.prop*`, and to the events of `Ds.bindEvent`. HTML lowercases attribute names, so write these names in lower case.

```fsharp
Ds.class' ("is-active", "$a")            // data-class:is-active="$a"
Ds.class' ("card__title", "$a")          // ArgumentException: ... it contains '__'. Datastar reads '__' in an attribute name as the start of a modifier,
                                         //    so it would read 'card__title' as 'card' with the modifier 'title'. Choose a name without '__'. ...
Ds.class' ("x\" onmouseover=\"y", "$a")  // ArgumentException: ... it contains '"'. HTML ends an attribute name there,
                                         //    and what follows would become attributes of their own. ...
```

### [Ds.style : `data-style`](https://data-star.dev/reference/attributes#data-style)

Sets the value of inline CSS styles on an element based on an expression, and keeps them in sync. Write the property name in kebab-case, as CSS spells it (`background-color`), because the HTML parser lowercases attribute names and Datastar passes the name on as it reads it.

```fsharp
Elem.div [ Ds.style ("background-color", "$usingRed ? 'red' : 'blue'") ] [ Text.raw "Red or blue" ]

Elem.div [ Ds.style ("display", "$hiding && 'none'") ] [ Text.raw "Might be hiding" ]
```

## _Events and Triggers_

Events and triggers result in [Datastar expressions](https://data-star.dev/guide/datastar_expressions) being executed. This can result in signal changes and other expressions being run.
Example: clicking a button to send a request or an element scrolling into view.

### [Ds.onInit : `data-init`](https://data-star.dev/reference/attributes#data-init)

Runs a [Datastar expression](https://data-star.dev/guide/datastar_expressions) when the attribute is initialized. That happens on page load, when an element that has it is patched into the DOM,
and any time the attribute is modified.

```fsharp
Elem.div [ Ds.onInit (Ds.get "/moreAgents") ] []
```

Attributes are evaluated in the order they appear, so when the request should use a signal from `Ds.indicator`, put `Ds.indicator` before `Ds.onInit`.

### [Ds.onEvent : `data-on`](https://data-star.dev/reference/attributes#data-on)

Attaches an event listener to an element, executing a [Datastar expression](https://data-star.dev/guide/datastar_expressions) whenever the event is triggered.
An `evt` variable that represents the event object is available in the expression.

```fsharp
Elem.div [ Ds.onEvent("mouseup", "$selection = document.getSelection().toString()") ] [ Text.raw "Highlight some of me!" ]
Elem.div [ Ds.onEvent("mouseenter", "$show = !$show"); Ds.onEvent("mouseleave", "$show = !$show") ] []
```

```fsharp
Elem.button [ Ds.onClick "$show = !$show" ] [ Text.raw "Peek-a-boo!" ]
Elem.div [ Ds.onInit (Ds.get "/edit") ] []
```

#### `data-on` Modifiers

Modifiers allow you to alter the behavior when events are triggered. (Modifiers with a '*' can only be used with the [built-in events](https://developer.mozilla.org/en-US/docs/Web/Events)).

```fsharp
 type OnEventModifier =
    | Once     // * - can only be used with built-in events
    | Passive  // * - can only be used with built-in events
    | Capture  // * - can only be used with built-in events
    | Delay of TimeSpan
    | DelayMs of int  // identical to Delay, but using milliseconds instead
    | Debounce of Debounce  // timespan, leading, and notrailing
    | Throttle of Throttle  // timepan, noleading, and trailing
    | ViewTransition
    | Window    // listen on the window
    | Document  // listen on the document
    | Outside
    | Prevent
    | Stop
```

As an example:
```fsharp
Elem.div [
    Ds.onEvent ("click", "$foo = ''", [ Window; Debounce (Debounce.With(1000, leading = true)) ])
    ] []
```

Results in:
```html
<div data-on:click__debounce.1000ms.leading__window="$foo = ''"></div>
```

The modifiers are written in the reverse of the order you list them. Datastar does not care about their order.

### [Ds.effect : `data-effect`](https://data-star.dev/reference/attributes#data-effect)

Executes an expression on page load and whenever any signals in the expression change. This is useful for performing
side effects, such as updating other signals, making requests to the backend, or manipulating the DOM.

```fsharp
Elem.div [ Ds.effect @"$foo = $bar + $baz" ] []
Elem.div [ Ds.text "$foo" ] []
```

### [Ds.onIntersect : `data-on-intersect`](https://data-star.dev/reference/attributes#data-on-intersect)

Runs an expression when the element intersects with the viewport.

```fsharp
Elem.div [ Ds.onIntersect "$intersected = true" ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Full) ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Half, onlyOnce = true) ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Half, onlyOnce = true, debounce = Debounce.With(TimeSpan.FromSeconds(1.0))) ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Half, onlyOnce = true, throttle = Throttle.With(TimeSpan.FromSeconds(1.0))) ] []
```

### [Ds.onSignalPatch | Ds.onSignalPatchFilter : `data-on-signal-patch`](https://data-star.dev/reference/attributes#data-on-signal-patch)

Runs an expression whenever a signal is patched. Use it sparingly, because it runs on every patch.

```fsharp
Elem.div [ Ds.onSignalPatch "$show = !$show" ] []

Elem.div [ Ds.onSignalPatchFilter (SignalsFilter.Include "foo") ] []
```

A `SignalsFilter` holds regular expressions for the paths of the signals to include and exclude. Write the pattern without slashes around it: the library adds them, and escapes a slash inside the pattern.
`SignalsFilter.Prefix "form."` matches the signals that start with `form.`.

```fsharp
SignalsFilter.Serialize (SignalsFilter.Include "^form\\.")                          // { include: /^form\./ }
SignalsFilter.Serialize { SignalsFilter.Prefix "form." with ExcludePattern = ValueSome "\\.id$" }
                                                                                   // { include: /^form\./,exclude: /\.id$/ }
SignalsFilter.Serialize (SignalsFilter.Include "a/b")                              // { include: /a\/b/ }, the slash is escaped for you
Ds.onSignalPatchFilter (SignalsFilter.Include "a\"b")                              // data-on-signal-patch-filter="{ include: /a&quot;b/ }"
```

### [Ds.onInterval : `data-on-interval`](https://data-star.dev/reference/attributes#data-on-interval)

Runs a statement at a regular interval. Pass the interval in milliseconds, and `leading = true` to run it once straight away.
The Tao of Datastar prefers one long-lived stream from the server to polling, so use an interval for work that only the browser needs, such as a clock. Do not use it to ask the backend for changes.

```fsharp
let tick = Signal.browser<bool> "tick"

Elem.div [
    Ds.signal (tick, false)
    Ds.onInterval (Stmt.toggle tick, 1000)
    Ds.text (Expr.ifElse (Expr.read tick) (Expr.string "tick") (Expr.string "tock"))
] []

// The same with strings
Elem.div [
    Ds.signal (sp"_fiveSecond", false)
    Ds.onInterval ("$_fiveSecond = !$_fiveSecond", 5000, leading = true)
] []
```

## _Actions and Functions_

Datastar provides a number of actions and functions that can be used in [Datastar expressions](https://data-star.dev/guide/datastar_expressions)
for making server requests and manipulating signals.

### [@get | @post | @put | @patch | @delete | @query](https://data-star.dev/reference/actions#backend-actions)

These actions make requests to any backend service that supports Server Side Events (SSE).
Luckily an F#-friendly [SDK exists](https://data-star.dev/reference/sdks#csharp) and `Falco.Datastar` has several [helper methods](#reading-signals-and-server-side-events)

All signals, that do not have an underscore prefix, are sent in the request.
`@get` and `@delete` send the signal values in the `datastar` query parameter. The other actions send them in a JSON body.

```fsharp
Elem.div [ Ds.onInit (Ds.get "/get") ] []

Elem.button [ Ds.onClick (Ds.post "/post") ] [ Text.raw "Post" ]

Elem.button [ Ds.onClick (Ds.put "/put") ] [ Text.raw "Put" ]

Elem.button [ Ds.onClick (Ds.patch "/patch") ] [ Text.raw "Patch" ]

Elem.button [ Ds.onClick (Ds.delete "/delete") ] [ Text.raw "Delete" ]

Elem.button [ Ds.onClick (Ds.query "/query") ] [ Text.raw "Query" ]
```

`@query` sends an HTTP `QUERY` request. Like a `@get`, it does not change anything on the server, but it sends the signals in the request body instead of the query string.

The majority of the above examples are fired from a button click, but remember that these are
[Datastar expressions](https://data-star.dev/guide/datastar_expressions) and any [event or trigger](#events-and-triggers)
could activate them.

Each request action can also be provided a number of options, explained in depth [here](https://data-star.dev/reference/actions#options).
Start with the defaults. Datastar's authors recommend them for most apps, and an option that you leave at `RequestOptions.Defaults` is not sent, so Datastar's own default applies.

```fsharp
Elem.button [ Ds.onClick (Ds.get ("/endpoint",
                                  { RequestOptions.Defaults with
                                        Headers = [ ("X-Csrf-Token", "JImikTbsoCYQ9...") ]
                                        OpenWhenHidden = ValueSome true }
                                 )) ] [ Text.raw "Push the Button" ]
```

`OpenWhenHidden` controls what happens to a request while the page is hidden. When it is `false`, Datastar cancels the request as soon as the page is hidden and starts it again when the page is visible.
When it is `true`, the request keeps running, which suits something like a dashboard.
The option is not set unless you set it, so Datastar chooses: `false` for `@get`, and `true` for `@post`, `@put`, `@patch`, `@delete` and `@query`.
Set `OpenWhenHidden = ValueSome false` to make a `@post` behave like a `@get` here, or `ValueSome true` to keep a `@get` running.

Older versions of this library declared the option as `OpenWhenHidden: bool` with a default of `false`, but they never sent that default, so a `@post` used Datastar's `true` anyway.
The type is now `bool voption`. Change `OpenWhenHidden = true` in your code to `OpenWhenHidden = ValueSome true`.

Each option is written as a value in an object that follows the URL: `@post('/x',{"retry":"error"})`. In the attribute the quotes are escaped as `&quot;`. Only the options that differ from Datastar's defaults are written.
The examples below show the F# and what it writes, with the attribute's escaping undone.

**`FilterSignals`** sends only the signals that match. `SignalsFilter.Prefix "form."` matches the signals that start with `form.`. `SignalsFilter.Include` and `SignalsFilter.Exclude` take a regular expression.
A filter that has an exclude keeps Datastar's rule that signals whose names start with an underscore stay in the browser, which Datastar would otherwise drop when it is given an exclude of its own.

```fsharp
Ds.get ("/search", { RequestOptions.Defaults with FilterSignals = SignalsFilter.Prefix "form." })
// @get('/search',{"filterSignals":{"include":"^form\\."}})

Ds.post ("/save", { RequestOptions.Defaults with FilterSignals = SignalsFilter.Exclude "^secret" })
// @post('/save',{"filterSignals":{"exclude":"(^|\\.)_|(?:^secret)"}})
```

**`Headers`** sends HTTP headers. Each name can be given once, because a request sends each header name once. Put several values in one header, separated by commas. A name that appears twice raises an `ArgumentException` that says so.

```fsharp
Ds.post ("/save", { RequestOptions.Defaults with Headers = [ "X-Csrf-Token", "abc" ] })
// @post('/save',{"headers":{"X-Csrf-Token":"abc"}})
```

**`RequestCancellation`** decides what happens to an earlier request with the same method and URL. `Auto` cancels it, `Disabled` lets both run, and `Cleanup` also cancels the request when the element it is on is removed from the page.
`AbortController` lets you cancel the request from your own code. The name is a signal that holds an `AbortController`, for example one made with `data-signals:_controller="new AbortController()"`.
Datastar only cancels the request when it is given the controller itself, so the name is written as code and not as text. An empty name raises an `ArgumentException`.

```fsharp
Elem.div [ Attr.create "data-signals:_controller" "new AbortController()" ] [
    Elem.button [ Ds.onClick (Ds.get ("/slow", { RequestOptions.Defaults with RequestCancellation = AbortController "$_controller" })) ] [ Text.raw "Start" ]
    Elem.button [ Ds.onClick "$_controller.abort()" ] [ Text.raw "Cancel" ]
]
// @get('/slow',{"requestCancellation":$_controller})
```

**`ContentType`** is `Json` by default and sends the signals. `Form` sends the closest form instead, and `SelectedForm "#myForm"` sends the form that the selector finds. `CustomJson obj` sends the object as the request body, instead of the signals.

**`Retry`** is sent when it is not `OnAuto`. `OnNever` does not retry a response that is not 200, but Datastar 1.0.4 still retries after a network error, up to `RetryMaxCount` times.
**`RetryScaler`** has to be a finite number. Datastar multiplies the wait by it after every retry, so leave it at 2, or use a number such as 1.5.

```fsharp
Ds.get ("/x", { RequestOptions.Defaults with Retry = OnError; RetryInterval = TimeSpan.FromMilliseconds 250.0; RetryMaxCount = 4 })
// @get('/x',{"retry":"error","retryInterval":250,"retryMaxCount":4})
```

### [`@setAll`](https://data-star.dev/reference/actions#setall)

Sets every signal whose path starts with the prefix to the value in the second argument. Use it to set a whole group of signals at once.
Strings are quoted in the expression, and numbers and booleans are not.
The Datastar action itself takes the value first and a filter second, and `Ds.setAll` builds that filter from the prefix.

```fsharp
Elem.button [ Ds.onClick (Ds.setAll ("foo.", true)) ] [ Text.raw "Check all" ]
```

```html
<button data-on:click="@setAll(true, { include: /^foo\./ })">Check all</button>
```

`Ds.setAllFiltered` takes a `SignalsFilter` instead of a prefix, and the value comes first: `Ds.setAllFiltered (true, SignalsFilter.Prefix "foo.")`. Pass `SignalsFilter.None` to set every signal.

### [`@toggleAll`](https://data-star.dev/reference/actions#toggleall)

Toggles all the signals that start with the prefix. This is useful for toggling all the values of a signal namespace at once.
`Ds.toggleAllFiltered` takes a `SignalsFilter` instead of a prefix.

```fsharp
Elem.button [ Ds.onClick (Ds.toggleAll "foo.") ] [ Text.raw "Toggle all" ]
```

### [`@peek`](https://data-star.dev/reference/actions#peek)

Evaluates an expression without subscribing to the signals it reads. Use it in `Ds.effect` to read a signal without re-running the effect when that signal changes.

```fsharp
Elem.div [ Ds.effect $"""$last = {Ds.peek "$count"}""" ] []
```

### Ds.expression

Joins several expressions into one, separated by `; `. Datastar reads a semicolon as the end of a statement, so this is how a line of code becomes several statements.
The [typed layer](#signals-expressions-and-statements-in-f) does this for you with [`Stmt.all`](#signals-expressions-and-statements-in-f), which is preferred where it fits.

```fsharp
Ds.expression [ "$_menuOpen = !$_menuOpen"; "$_count = 0" ]   // $_menuOpen = !$_menuOpen ; $_count = 0
```

### [Ds.nonce : `data-nonce`](https://data-star.dev/reference/security#csp-mode)

Datastar runs the expressions in your `data-*` attributes with `Function`, which a Content Security Policy blocks unless the policy allows `unsafe-eval`.
If your policy uses a nonce instead, put `Ds.nonce` on the `<html>` element. This is Datastar's [CSP mode](https://data-star.dev/reference/security#csp-mode): it runs its expressions through script tags that carry the nonce.
Without it, a page under such a policy fails with `EvalError: Evaluating a string as JavaScript violates the following Content Security Policy directive`.

The nonce must match the one in your policy's `script-src`. It must not be empty, and it should be a new random value for every full-page response.
Datastar reads the attribute once and then removes it from the page.
The script tag that loads Datastar needs the nonce too, unless your policy already allows its source, for example with `'self'`.
Datastar also applies the nonce to scripts that arrive in element patches and JavaScript responses, so those responses do not need to carry it.

```fsharp
let nonce = "..." // a new random value for each response
ctx.Response.Headers["Content-Security-Policy"] <- $"script-src 'nonce-{nonce}'"

Elem.html [ Ds.nonce nonce ] [
    Elem.head [] [ Elem.script [ Attr.type' "module"; Attr.src Ds.cdnSrc; Attr.create "nonce" nonce ] [] ]
    Elem.body [] [ (* ... *) ]
]
```

CSP mode does not make Datastar expressions safe to use with untrusted content, because Datastar does not check or clean the expressions in your attributes.
Pass user values through signals. Do not put them in the text of an expression. Sanitize any HTML that users can provide.
Datastar also works with Trusted Types: it creates a policy named `datastar`, so a policy with `trusted-types datastar; require-trusted-types-for 'script'` allows it.
If you use an aliased Datastar script, the attribute carries the alias too, for example `data-star-nonce`. Setting `Constants.dataSlugPrefix <- "data-star"` makes `Ds.nonce` write that name, and it must match the alias the bundle was built with, because that is what Datastar reads.

### `Ds.safariStreamingFix`

Safari can show a stale copy of a streaming page when the user goes back to it, and the stream never starts again. Put this attribute on the `<body>` of a page that opens a stream, to reload the page when it is restored from the back-forward cache.
It listens for `pageshow` on the window. Datastar 1.0.4 reads modifiers after `__`, so this library writes `__window`.
[The problem](https://stackoverflow.com/questions/8788802/prevent-safari-loading-from-cache-when-back-button-is-clicked) is explained on Stack Overflow.

```fsharp
Elem.body [ Ds.safariStreamingFix; Ds.onInit (Stmt.get "/updates") ] [ (* ... *) ]
```

```html
<body data-on:pageshow__window="evt?.persisted && window.location.reload()" data-init="@get('/updates')"></body>
```

### [Ds.ignore | Ds.ignoreSelf | Ds.ignoreMorph : `data-ignore`](https://data-star.dev/reference/attributes#data-ignore)

Datastar walks the entire DOM and applies plugins to each element it encounters.
It’s possible to tell Datastar to ignore an element and its descendants by placing a data-ignore attribute on it.
This can be useful for preventing naming conflicts with third-party libraries.

`Ds.ignore` will force Datastar to ignore the element and all child elements.
`Ds.ignoreSelf` ignores only the element it is on. Its children are still processed.
`Ds.ignoreMorph` stops a server patch from changing the element. It applies when both the element on the page and the element the server sends have the attribute, or when the element on the page is inside an element that has it.

```fsharp
Elem.div [ Ds.ignore ] [
    Elem.div [ Ds.text "ignoredAsWell" ] []
]

Elem.div [ Ds.ignoreSelf ] [
    Elem.div [ Ds.text "thisIsNotIgnored" ] []
]

Elem.div [ Ds.ignoreMorph ] [
    Elem.div [ Ds.text "thisWillNotBeMorphed" ] []
]
```

### [Ds.preserveAttr : `data-preserve-attr`](https://data-star.dev/reference/attributes#data-preserve-attr)

Keeps the value of an attribute when a server patch morphs the element. List several attributes with a space between them.

```fsharp
Elem.details [ Attr.createBool "open"; Ds.preserveAttr "open" ] [ Elem.summary [] [ Text.raw "Title" ]; Text.raw "Content" ]
```

### [Ds.jsonSignals | Ds.jsonSignalsOptions : `data-json-signals`](https://data-star.dev/reference/attributes#data-json-signals)

Sets the text content of an element to a reactive JSON stringified version of signals. Useful when troubleshooting an
issue. Has options for restricting the signals displayed.

```fsharp
Elem.pre [ Ds.jsonSignals ] []

Elem.pre [ Ds.jsonSignalsOptions (SignalsFilter.Include "foo") ] []
```

## _Rocket Components_

[Rocket](https://github.com/starfederation/datastar/tree/v1.0.4/library/src/rocket) is Datastar's way of writing web components.
You write a component in JavaScript with `rocket(tag, { props, setup, render })`, and the server renders the component's tag, its props and its children.
Load Datastar with `Ds.rocketCdnScript` to use it. The bundle exports `rocket`, so a component definition starts with `import { rocket } from '...'` and the address in `Ds.rocketCdnSrc`, as the example does. The helpers below write only the values you pass, so Rocket's own defaults still apply.
Datastar's [Rocket reference](https://data-star.dev/reference/rocket) says that Rocket is in beta and that its API is subject to change, so these helpers may need to change with it.

Rocket components fit the Tao when they hold behaviour for the user interface. Keep business state on the backend, and let a component emit an event that the page turns into a request.
The [RocketComponents example](https://github.com/falcoframework/Falco.Datastar/tree/main/examples/RocketComponents) does this: props come from the server, one component keeps state that only the browser needs, and the events turn into commands.

### `Rocket.propString | propNumber | propBool | propDate | propJson | propBin`

Rocket reads each prop from the attribute with the prop's name, and decodes it with the [codec](https://github.com/starfederation/datastar/blob/v1.0.4/library/src/rocket/codecs.ts) the component chose.
Each helper writes the format its codec reads:

| Helper | Codec | What it writes |
| --- | --- | --- |
| `Rocket.propString` | `string` | the text, escaped for use in an attribute. A carriage return is written as `&#13;`, because a parser would change it into a line feed. HTML cannot keep a NUL character, so it is written as U+FFFD, as a browser would read it |
| `Rocket.propNumber` | `number` | any number type, with the invariant culture (`1.5`, never `1,5`) |
| `Rocket.propBool` | `bool` | `true` or `false`, always written, because a missing attribute means the prop's default, which might be true |
| `Rocket.propDate` | `date` | UTC ISO 8601 with milliseconds, like `Date.toISOString()` |
| `Rocket.propJson` | `json`, `array`, `object`, `tuple`, `oneOf`, and `js` | camelCase JSON, or JSON made with the options you pass |
| `Rocket.propBin` | `bin` | base64 |

The attribute name is the prop name converted the way Rocket [converts it](https://github.com/starfederation/datastar/blob/v1.0.4/library/src/utils/text.ts): `maxCount` becomes `max-count`, `innerHTML` becomes `inner-html`, and `pos3d` becomes `pos-3-d`.

```fsharp
Elem.create "my-counter"
    [ Attr.id "counter"
      Rocket.propString ("label", "Clicks")
      Rocket.propNumber ("step", 1)
      Rocket.propNumber ("count", 0) ]
    []
```

```html
<my-counter id="counter" label="Clicks" step="1" count="0"></my-counter>
```

To change a prop from the server, patch the element that has the same `id`, with new attribute values. Rocket reads the new values and renders the component again.

```fsharp
let handleChange : HttpHandler = fun ctx ->
    Response.ofHtmlElements (Elem.create "my-counter" [ Attr.id "counter"; Rocket.propNumber ("count", 7) ] []) ctx
```

### `Rocket.local | Rocket.call | Rocket.root`

The children of a light-DOM component (`mode: 'light'`), and the component's own render output, can use signals and actions that belong to one instance of the component.
In an open shadow-DOM component, `Rocket.local` and `Rocket.root` also work in the children that the server rendered.
Rocket rewrites `$$name` into a path that is unique to that instance, so two instances do not share it. It rewrites `@name(...)` into a call to the action registered with `action('name', fn)` in the component's `setup`, or, if there is none, into a call to the Datastar action with that name.

The typed way declares the signal in F#, so the state needs no JavaScript:

```fsharp
let isOn = Signal.rocket<bool> "on"

Elem.create "my-toggle" [ Attr.id "toggle" ] [
    Elem.div [ Ds.signal (isOn, false) ] []
    Elem.button [ Ds.onClick (Stmt.toggle isOn) ] [ Text.raw "Toggle" ]
    Elem.p [ Ds.show (Expr.read isOn) ] [ Text.raw "Now you see me." ]
]
```

Define the tag with `rocket('my-toggle', { mode: 'light' })`. It needs no props, setup or render function. Two instances keep separate state: toggling one leaves the other alone.
The string helpers do the same, with an action that you register in JavaScript:

```fsharp
Elem.create "my-toggle" [ Attr.id "toggle" ] [
    Elem.button [ Ds.onClick (Rocket.call "flip") ] [ Text.raw "Toggle" ]
    Elem.p [ Ds.show (Rocket.local "on") ] [ Text.raw "Now you see me." ]
]
```

Inside a component, Rocket ties the signals named by `Ds.bind`, `Ds.computed` and `Ds.indicator`, and the signals created by `Ds.signal`, to the instance.
`Rocket.root` makes a bind, computed or indicator use the page's signal instead. It does nothing to `Ds.signal`, because Rocket always ties those to the instance ([source](https://github.com/starfederation/datastar/blob/v1.0.4/library/src/rocket/template.ts#L467-L481)).
Datastar's Rocket reference says `__root` also applies to `data-ref`, but Datastar 1.0.4 turns every `data-ref` in a component into a component reference before it looks for `__root`, so it has no effect there.
Use `Rocket.root` sparingly. It suits a wrapper component whose children should stay connected to the page's signals.

```fsharp
Elem.input [ Rocket.root (Ds.bind "query") ]   // <input data-bind:query__root>: binds the page's $query
Elem.input [ Ds.bind "note" ]                   // binds this instance's $$note
```

### `Rocket.templateFor | templateIf | templateElseIf | templateElse`

Rocket adds directives that go on `<template>` elements. [`data-for`](https://github.com/starfederation/datastar/blob/v1.0.4/library/src/rocket/for.ts) repeats its content for each item in an expression.
[`data-if`, `data-else-if` and `data-else`](https://github.com/starfederation/datastar/blob/v1.0.4/library/src/rocket/conditional.ts) render one branch of a chain.
Rocket calls the item `item` and the index `i` unless you give other names. It always looks for the attributes `data-for`, `data-if`, `data-else-if` and `data-else`, even if you set a different attribute prefix.

Rocket runs these directives on the server-rendered children of a light-DOM component when the page loads.
In an open or closed shadow-DOM component it does not: the `<template>` elements stay inert until a later server patch sends new children for the component, and then they run.
For a shadow-DOM component, put the directives in its `render` function in JavaScript, or use `mode: 'light'`.

```fsharp
let items = Signal.rocket<string list> "items"
let count = Signal.rocket<int> "count"

Elem.ul [] [
    Rocket.forEach (Expr.read items, fun item index -> [ Elem.li [ Ds.text (Expr.concat [ Expr.toText index; Expr.string ": "; item ]) ] [] ])
]

Rocket.templateIf (Expr.greater (Expr.read count) (Expr.int 9), [ Text.raw "That is a lot." ])
Rocket.templateElse [ Text.raw "Keep going." ]
```

`Rocket.forEach` gives the function that builds a row the item and the index as typed expressions, so the row cannot refer to a name that the loop does not define. Pass `itemName` and `indexName` to choose other names.
`Rocket.templateFor` and the string versions of `templateIf` and `templateElseIf` take the list and the condition as text.

The Tao says to keep your HTML DRY with your backend templates. Render a list on the server when the server knows it, and use these directives for lists that only the browser knows.

### `Request.getRocketManifests`

A page can post the description of every Rocket component it defines to your server, for example to build documentation or a component registry. In JavaScript:

```js
import { publishRocketManifests } from '/path/to/datastar-rocket.js'
await publishRocketManifests({ endpoint: '/api/rocket/manifests' })
```

`Request.getRocketManifests` reads that request into a `RocketManifestDocument`. It holds the components, and for each one its props (with their codec, default value, allowed values and documentation), slots and events.
`RocketManifest.parse` does the same for a string.

```fsharp
let handleManifests : HttpHandler = fun ctx -> task {
    match! Request.getRocketManifests ctx with
    | Ok manifest ->
        for info in manifest.Components do
            printfn "%s has %d props" info.Tag info.Props.Length
        return! Response.ofEmpty ctx
    | Error error ->
        return! (Response.withStatusCode 400 >> Response.ofPlainText error.Message) ctx
}
```

The error is a `RocketManifestError`: `NotJson`, `TooLarge`, `Missing`, `WrongKind`, `UnsupportedVersion`, `NotAnObject`, `ConnectionFailed` or `Cancelled`. Match on it, or use its `Message`, which says what is wrong and what to do about it.
`Missing` and `WrongKind` say where the problem is, for example the prop "count" of my-card. `Request.getRocketManifests` refuses a body larger than 1 MiB without reading the rest of it.
```fsharp
match! Request.getRocketManifests ctx with
| Ok manifest -> store manifest
| Error RocketManifestError.Cancelled
| Error (RocketManifestError.ConnectionFailed _) -> ()      // the page went away, so there is nobody to answer
| Error (RocketManifestError.UnsupportedVersion (found, supported)) -> log $"version {found} is newer than the {supported} that this library reads"
| Error error -> log error.Message
```

`ConnectionFailed` and `Cancelled` mean that the connection failed or the request was cancelled before the whole body arrived, so there may be nobody left to answer. `Request.getRocketManifests` does not throw for any of these. A mistake in your own code, such as reading the body twice, still throws.
A codec name that this library does not know is kept as `RocketPropType.Other`, so a newer Rocket does not break it.
This reads the manifest only. Generating F# code from it is left to a separate tool.

## Testing

The unit tests check a rule that has to hold for every input, not only for the examples. There are four kinds, and each answers a different question.

**Named tests** pin the output of every helper, so a change to what it writes is a failing test rather than a surprise in a browser. The `Response` tests drive a real `HttpContext` and read back the bytes, because the Server Sent Events a browser receives are the only thing that matters about them.

**Property tests** state a rule and let [FsCheck](https://fscheck.github.io/) look for an input that breaks it. They cover the rules the library's own reasoning rests on: that text in a signal comes back as the same text, that a signal name the library accepts is the name Datastar reads, and that an expression keeps the parentheses that stop a neighbouring operator from reaching into it.

**Simulation tests** build attributes from hostile text and read the HTML back with a real HTML5 parser, fuzz the Rocket manifest reader, and feed `Request.getRocketManifests` a body that arrives in odd chunks, fails or is cancelled. Every random test takes its randomness from a seed, so a failure can be replayed exactly:

```shell
dotnet test test/Falco.Datastar.Tests -c Release                    # seeds 1 to 25, the same every time
DST_SEEDS=2000 dotnet test test/Falco.Datastar.Tests -c Release     # seeds 1 to 2000, to look for rare cases
DST_SEED=417 dotnet test test/Falco.Datastar.Tests -c Release       # only seed 417, to replay a failure
```

The hostile text is built from fragments grouped by what they attack — text that ends an attribute, text that closes the
element, text that ends a JavaScript string, the characters a parser rewrites — and each group is weighted, because an
unweighted mix produces mostly harmless letters and the dangerous cases are then almost never drawn. Tests measure which
groups a long run actually reaches, so a change to the weights shows up as a number rather than as a run that quietly
stopped covering anything.

The randomness comes from a generator written out in the test project rather than from `System.Random`, so a seed means
the same case on any runtime and after any upgrade, and the values a seed gives are pinned by a test.

**Mutation tests** make a small, deliberate mistake in the source and run the whole suite. A failing test proves the suite catches that mistake; a mistake that survives is a test to write. This is what keeps a green suite honest, because a suite can pass while the code it covers is wrong.

```shell
dotnet run --project test/mutation          # every mutant; a survivor exits non-zero and names the test to write
```

`test/Falco.Datastar.E2E` has browser tests that check a real Chromium, running Datastar 1.0.4, does what the text the library writes says it should. They load Datastar from a CDN, so they need internet access and they take longer than the unit tests; the **e2e** workflow in the Actions tab runs them. See the READMEs in [the unit tests](https://github.com/falcoframework/Falco.Datastar/tree/main/test/Falco.Datastar.Tests) and [the browser tests](https://github.com/falcoframework/Falco.Datastar/tree/main/test/Falco.Datastar.E2E).

## _When to `$`_

This section is about the string helpers. With the [typed functions](#signals-expressions-and-statements-in-f) you do not choose: `Expr.read signal` is the value, `Ds.bind signal` takes the signal, and the compiler tells you which one a function needs.

The sample code uses `$` in some places and not in others. `$` marks the value of a signal. Without it, you name the signal itself.

The `$` symbol is a shorthand to get the value of the signal (e.g. `$count` -> `count.value`), so when the `$` is elided, you are referring to the signal directly.
[`Ds.bind signalPath`](#dsbind--data-bind) is two-way binding to the signal, so it requires the signal path, no `$`.
[`Ds.text`](#dstext--data-text) is replacing the element's innerText, so it needs the value, via `$`.
[`Ds.computed (signalPath, expression)`](#dscomputed--data-computed) needs both a signal path AND an expression, e.g. `Ds.computed ("countPlusTen", "$count + 10")`.

If you want to be certain you are doing it correctly, then there is a helper method `SignalPath.sp`
that will throw an exception at startup, if a signal path contains any invalid symbols, such as `$`.

```fsharp
open Falco.Datastar.SignalPath
...
Elem.input [ Attr.typeCheckbox; Ds.bind (sp"checkBoxSignal") ]
```

## Reading Signals and Server Side Events

[Falco.Datastar](https://github.com/falcoframework/Falco.Datastar) has a number of Request and Response functions for reading the [Datastar signal](https://data-star.dev/guide/reactive_signals) values and responding
with [Datastar Server Side Events (SSEs)](https://data-star.dev/reference/sse_events).

Sections:
- [Reading Signal Values](#reading-signal-values)
- [Responding with Signals](#responding-with-signals)
- [Responding with HTML Elements](#responding-with-html-elements)
- [Streaming Server Side Events](#streaming-server-side-events)

## _Reading Signal Values_

Datastar sends the signals whose names do not start with an underscore, so you can keep a signal in the browser by starting its name with one.
`@get` and `@delete` put the signals in the `datastar` query parameter as JSON. The other actions send them as the JSON body.
Luckily, with [Falco.Datastar](https://github.com/falcoframework/Falco.Datastar), you don't have to worry about any of that.
The body is read as a stream, so call one of the `getSignals` functions only once for each request.

### `Request.getSignals<'T>`

Will use [`System.Text.Json.JsonSerializer`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.deserialize) to deserialize the signals into a `'T`.
If the request has no signals, it returns `ValueNone`.

```fsharp
[<CLIMutable>]
type MySignals =
    { firstName : string
      lastName : string
      email : string }
...
let httpHandler : HttpHandler = (fun ctx -> task {
    let! signals : MySignals voption = Request.getSignals<MySignals> (ctx)
    ...
    })
```

### `Request.getSignalsJson`

Will return a `System.Text.Json.JsonDocument` of the signals.

```fsharp
let httpHandler : HttpHandler = (fun ctx -> task {
    let! jsonDocument = Request.getSignalsJson ctx
    ...
    })
```

## _Responding with Signals_

### `Response.ofPatchSignals<'T>`

Serializes signals with [`System.Text.Json.JsonSerializer`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer) and sends to client where Datastar will merge them.

```fsharp
Response.ofPatchSignals (MySignals())
```

### `Response.ofPatchSignal<'T>`

Updates a single signal on the client.

```fsharp
Response.ofPatchSignal (sp"user.firstName") "Don"
```

### Raw JSON

`Response.ofPatchSignals` serializes the value you give it, so a string would be sent as a JSON string and not as an object.
To send JSON that you already have, call the SDK after starting the response:

```fsharp
open StarFederation.Datastar.FSharp

let handler : HttpHandler = fun ctx -> task {
    do! Response.sseStartResponse ctx
    do! ServerSentEventGenerator.PatchSignalsAsync (ctx.Response, """{ "firstName": "Don", "lastName": "Syme" }""")
}
```

### Removing signals

Datastar removes a signal when the server patches it to `null`.

```fsharp
Response.ofPatchSignals {| user = {| firstName = (null: string); lastName = (null: string) |} |}
```

## _Responding with HTML Elements_

HTML elements are sent to client and replace the current element (matching on the `id` attribute) with the one that is sent.
The following functions are `HttpHandler`s that will send down a single Server Sent Event.

### `Response.ofHtmlElements`

Will render an XMLNode and send it to the client. Client Datastar will replace the element with the matching `id` attribute (or optionally provided selector)

```fsharp
Response.ofHtmlElements ( Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hello, World from the Server!" ] )
```

### `Response.ofHtmlStringElements`

Will send HTML fragments to the client. Client Datastar will replace the element with the matching `id` attribute (or optionally provided selector)

```fsharp
Response.ofHtmlStringElements @"<h2 id='hello'>Hello, World from the Server!</h2>"
```

### `Response.ofRemoveElement`

Will send a command to client Datastar to remove fragments with the matching selector.

```fsharp
Response.ofRemoveElement (sel"#hello")   // needs: open Falco.Datastar.Selector
```

### `Response.ofExecuteScript`

Sends JavaScript for Datastar to run in the browser.

```fsharp
Response.ofExecuteScript "console.log('Hello from the server')"
```

The `Options` variants (`ofHtmlElementsOptions`, `ofHtmlStringElementsOptions`, `ofPatchSignalsOptions`, `ofPatchSignalOptions`, `ofRemoveElementOptions` and `ofExecuteScriptOptions`) take the SDK's option records.
`Request.getSignalsOptions` reads the signals with the `JsonSerializerOptions` that you pass.

### Patch options

`Response.ofHtmlElementsOptions` and `Response.ofHtmlStringElementsOptions` take a `PatchElementsOptions`, which sets where and how the elements are patched. Start from `PatchElementsOptions.Defaults`. The default mode, `Outer`, morphs the element that has the matching `id`. Datastar recommends it, so use another mode only when you have a reason.

| Field | What it does |
| --- | --- |
| `Selector` | The element to patch. Without it, each new element replaces the page element with the same `id`. |
| `PatchMode` | How to patch: `Outer` (the default), `Inner`, `Remove`, `Replace`, `Prepend`, `Append`, `Before` or `After`. |
| `UseViewTransition` | Runs the patch inside a view transition. |
| `ViewTransitionSelector` | With `UseViewTransition`, runs the transition on the first element that matches this selector instead of on the whole document. If nothing matches, the document is used. |
| `Namespace` | The namespace the new elements are created in: `Html` (the default), `Svg` or `MathMl`. |
| `EventId` | The id of the event. The browser sends the last one back in the `last-event-id` header when it reconnects. |
| `Retry` | How long the browser waits before it reconnects after the stream ends, as a `TimeSpan`. The default is one second. |

```fsharp
let appendRows =
    { PatchElementsOptions.Defaults with
        Selector = ValueSome "#rows"
        PatchMode = Append
        UseViewTransition = true
        ViewTransitionSelector = ValueSome "#table" }

Response.ofHtmlElementsOptions appendRows (Elem.tr [] [ Elem.td [] [ Text.raw "New row" ] ])
```

## _Streaming Server Side Events_

Within the `Response` module there are the `of` methods that are for sending single server side events and then closing the connection.
But the client can also keep a connection open, and the server streams updates over it as they happen. This replaces polling, and it is the read side of [CQRS](#cqrs).

The [progress bar example](https://data-star.dev/examples/progress_bar) shows it: no polling is needed.

All the functions in [Responding with Signals](#responding-with-signals) and [Responding with HTML Elements](#responding-with-html-elements)
are mirrored with a function with `sse` as their prefix instead of `of`.

```fsharp
let handleStream = (fun ctx -> task {
    do! Response.sseStartResponse ctx  // make sure this is called first; sends the appropriate headers

    let mutable counter = 0

    while true do  // all Datastar methods will throw on ctx.RequestAborted
        do! Response.ssePatchSignal ctx (sp"counter") counter
        do! Response.sseHtmlElements ctx ( Elem.pre [ Attr.id "counterId" ] [ Text.raw (string counter) ] )
        do! Task.Delay(TimeSpan.FromSeconds 1.0, ctx.RequestAborted)
        counter <- counter + 1
    })
```

See the [Streaming example](https://github.com/falcoframework/Falco.Datastar/tree/main/examples/Streaming) for more.
