# Falco.Datastar

[![NuGet Version](https://img.shields.io/nuget/v/Falco.Datastar.svg)](https://www.nuget.org/packages/Falco.Datastar)
[![build](https://github.com/falcoframework/Falco.Datastar/actions/workflows/build.yml/badge.svg)](https://github.com/falcoframework/Falco.Datastar/actions/workflows/build.yml)

```fsharp
open Falco.Markup
open Falco.Datastar

let demo =
    Elem.button
        [ Ds.onClick (Ds.get "/click-me") ]
        [ Text.raw "Reset" ]
```

[Falco.Datastar](https://github.com/SpiralOSS/Falco.Datastar) brings type-safe [Datastar](https://data-star.dev) support to [Falco](https://github.com/pimbrouwers/Falco).
It provides a complete mapping of all [attribute plugins](https://data-star.dev/reference/attribute_plugins) and [action plugins](https://data-star.dev/reference/action_plugins).
As well as helpers for retrieving the signals and responding with Datastar Server Side Events.

## Key Features
- Idiomatic mapping of `data-*` attributes (e.g. `data-text`, `data-bind`, `data-signals`, etc.).
- Helper functions for reading signals and responding with Datastar Server Side Events.

## Design Goals
- Create a self-documenting way to integrate Datastar into Falco applications.
- Provide type safety without over-abstracting.

## Getting Started

First off, for any questions or criticisms of this library or [Datastar](http://data-star.dev) in general,
please join our [Discord](https://discord.com/channels/1296224603642925098/1334541716497109042), where we are definitely not a cult.

This guide assumes you have a [Falco](https://github.com/pimbrouwers/Falco) project setup. If you don't, you can create a new Falco project using the following commands.
The full code for this guide can be found in the [Hello World example](examples/HelloWorld).

```shell
> dotnet new web -lang F# -o HelloWorld
> cd HelloWorld
```

Install the nuget package:
```shell
> dotnew add package Falco
> dotnew add package Falco.Datastar
```

Remove any `*.fs` files created automatically, crate a new file name `Program.fs` and set the contents to the following:

```fsharp
open Falco
open Falco.Datastar
open Falco.Markup
open Falco.Routing
open Microsoft.AspNetCore.Builder

let bldr = WebApplication.CreateBuilder()
let wapp = bldr.Build()

let endpoints = [ ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
```

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
                    [ Text.raw "Click Me" ] ] ]

    Response.ofHtml html
```

Next, we'll define a handler for the click event that will return an HTML fragment from the server to replace the HTML of the button; note the `#hello`.

```fsharp
let handleClick : HttpHandler =
    let html = Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hello, World, from the Server!" ]
    Response.ofHtmlFragments html
```

And lastly, we'll make Falco aware of these routes by adding them to the `endpoints` list.

```fsharp
let endpoints =
    [
        get "/" handleIndex
        get "/click" handleClick
    ]
```

Save the file and run the application:

```shell
> dotnet run
```

Navigate to `https://localhost:5001` in your browser and click the button. You should see the text "Hello, World from the Server!" appear in the place of the button.

## Signals and Expressions

Datastar uses signals to manage state. Signals are reactive variables that automatically track and
propagate changes in [Datastar expressions](https://data-star.dev/guide/datastar_expressions).
They can be created and modified using data attributes on the frontend, or events sent from the backend.

[Datastar expressions](https://data-star.dev/guide/datastar_expressions) are strings that are evaluated by bindings, events, and triggers.
Updating a signal value in an expression will cause other bindings and expressions to update elsewhere.

Some important notes: Signals defined later in the DOM tree override those defined earlier.
`data-*` attributes are [evaluated in the order they appear in the DOM](https://data-star.dev/examples/plugin_order); meaning that signals need to be specified before they can be used.

#### Sections:

- [Creating Signals](#creating-signals)
- [Binding to Signals](#signal-binding)
- [Events and Triggers](#events-and-triggers)
- [Actions and Functions](#actions-and-functions)
- [Miscellaneous Actions](#miscellaneous-actions)
- [When to $](#when-to-)

## _Creating Signals_

Create signals, which are reactive variables that automatically propagate their value to all references of the signal.

### [Ds.signals / Ds.signal : `data-signals`](https://data-star.dev/reference/attribute_plugins#data-signals)

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

**Important note**: if you use kebab-case, it will be returned in pascal-case.

```fsharp
Elem.div [ Ds.signal (sp"signalPath", "signalValue", ifMissing = true) ] []
```

### [Ds.computed : `data-computed`](https://data-star.dev/reference/attribute_plugins#data-computed)

Creates a read-only signal that is computed based on a [Datastar expression](https://data-star.dev/guide/datastar_expressions). [`data-text`](#dstext--data-text) is used here to bind and display the signal value.

```fsharp
Elem.div [ Ds.computed ("foo", "$bar + $baz") ] []
Elem.div [ Ds.text "$foo" ] []
```

### [Ds.ref : `data-ref`](https://data-star.dev/reference/attribute_plugins#data-ref)

Creates a new signal that is a reference to the element on which the data attribute is placed. [`data-text`](#dstext--data-text)
is used here to bind and display the signal value.

```fsharp
Elem.div [ Ds.ref "foo" ] []
Elem.div [ Ds.text "$foo.tagName" ] []
```

### [Ds.indicator : `data-indicator`](https://data-star.dev/reference/attribute_plugins#data-indicator)

Creates a signal and sets its value to true while an SSE request is in flight, otherwise false.
As an example, the signal can be used to show a loading indicator.

```fsharp
Elem.button [
    Ds.onClick (Ds.get "/fetchBigData")  // make a request to the backend
    Ds.indicator "fetching"  // the signal we are creating
    Ds.attr' ("disabled", "$fetching")  // assigns the "disabled" attribute if the `fetching` signal value is true
    ] [ Text.raw "Fetch!" ]

Elem.div
    [ Ds.show "$fetching" ]  // show or hide this <div> if the `fetching` signal value is true or false, respectively
    [ Text.raw "Fetching" ]
```

The previous example uses a couple functions we haven't covered yet. [`Ds.onClick`](#dsonevent--data-on) firing a [`Ds.get action`](), which sends a GET request to the server.
[`Ds.attr'`](#dsattr--data-attr) and [`Ds.show`](#dsshow--data-show) are evaluating the Datastar expression `$fetching` and are assigning `disabled` attribute and
show/hiding the div, respectively, based on the `fetching` signal value's "true-ness".

## _Signal Binding_

Binding to a signal means tying an attribute or value of an element to a value that can be modified by another effect.
Example: setting the innerText of a `<div>` to a value that is updated by a server; or, toggling an HTML `class` on an element.

### [Ds.bind : `data-bind`](https://data-star.dev/reference/attribute_plugins#data-bind)

Creates a two-way binding from a signal to the "value" of an HTML element. Can be placed on any HTML element on which data can be input or choices
selected (e.g. `input`, `textarea`, `select`, `checkbox` and `radio` elements, as well as web components. You can see in the
[source](https://github.com/starfederation/datastar/blob/main/library/src/plugins/official/dom/attributes/bind.ts) how signals are translated).
The signal will be created if it does not already exist.

```fsharp
Elem.input [ Attr.type' "text"; Ds.bind "firstName" ]
```

### [Ds.text : `data-text`](https://data-star.dev/reference/attribute_plugins#data-text)

Binds the `text` value of an element to a [Datastar expression](https://data-star.dev/guide/datastar_expressions).

```fsharp
Elem.div [ Ds.text "$foo" ] []
```

### [Ds.attr' : `data-attr`](https://data-star.dev/reference/attribute_plugins#data-attr)

Binds the value of an HTML attribute to an expression.

```fsharp
Elem.div [ Ds.attr' ("title", "$foo") ] []
```

### [Ds.show : `data-show`](https://data-star.dev/reference/attribute_plugins#data-show)

Show or hides an element based on whether a [Datastar expression](https://data-star.dev/guide/datastar_expressions) evaluates to true or false.
For anything with custom requirements, use [`data-class`](#dsclass--data-class) instead.

```fsharp
Elem.div [ Ds.show "$foo" ] []
```

### [Ds.class' : `data-class`](https://data-star.dev/reference/attribute_plugins#data-class)

Adds or removes a class to or from an element based on the "true-ness" of a [Datastar expression](https://data-star.dev/guide/datastar_expressions).

```fsharp
Elem.div [ Ds.class' "hidden" "$foo" ]  // add the 'hidden' class when $foo evaluates to true
```

### [Ds.viewTransition : `data-view-transition`](https://data-star.dev/reference/attribute_plugins#data-view-transition)

Sets the [`view-transition-name`](https://developer.mozilla.org/en-US/docs/Web/CSS/view-transition-name) style attribute explicitly.

```fsharp
Elem.div [ Ds.viewTransition "$foo" ]
```

### [Ds.customValidity : `data-custom-validity`](https://data-star.dev/reference/attribute_plugins#data-custom-validity)

Allows adding custom validity to an element using an expression. The expression must evaluate to a string,
which will be set as the custom validity message. If the string is empty, the input is considered valid.

```fsharp
Elem.form [] [
    Elem.input [ Ds.bind "foo"; Attr.name "foo" ]
    Elem.input [ Ds.bind "bar"; Attr.name "bar"
                 Ds.customValidity "$foo === $bar ? '' : 'Field values must be the same.'" ]
    Elem.button [] [ Text.raw "Submit form" ]
]
```

## _Events and Triggers_

Events and triggers result in [Datastar expressions](https://data-star.dev/guide/datastar_expressions) being executed. This can possibly result in signal changes and other expressions being run.
Example: clicking a button to send a request or update the visibility on an element via [Ds.show](#dsshow--data-show).

### [Ds.onEvent : `data-on`](https://data-star.dev/reference/attribute_plugins#data-on)

Attaches an event listener to an element, executing a [Datastar expression](https://data-star.dev/guide/datastar_expressions) whenever the event is triggered.
An `evt` variable that represents the event object is available in the expression.

```fsharp
Elem.div [ Ds.onEvent("mouseup", "$selection = document.getSelection().toString()") ] [ Text.raw "Highlight some of me!" ]
Elem.div [ Ds.onEvent("mouseenter", "$show = !$show"); Ds.onEvent("mouseexit", "$show = !$show") ] []
```

There are helper methods for `Ds.onEvent("click", ...)` and `Ds.onEvent("load", ...)`:

```fsharp
Elem.button [ Ds.onClick "$show = !$show" ] [ Text.raw "Peek-a-boo!" ]
Elem.div [ Ds.onLoad (Ds.get "/edit") ] []
```

#### [`data-on` Modifiers](https://data-star.dev/reference/attribute_plugins#modifiers-5)

Modifiers allow you to alter the behavior when events are triggered. (Modifiers with a '*' can only be used with the [built-in events](https://developer.mozilla.org/en-US/docs/Web/Events)).

```fsharp
 type OnEventModifier =
    | Once     // * - can only be used with built-in events
    | Passive  // * - can only be used with built-in events
    | Capture  // * - can only be used with built-in events
    | Debounce of Debounce  // timespan, leading, and notrail
    | Throttle of Throttle  // timepan, noleading, and trail
    | ViewTransition
    | Window
    | Outside
    | Prevent
    | Stop
```

As an example:
```fsharp
Elem.div [
    Ds.onEvent ("click", "$foo = ''", [ Window; Debounce.With(TimeSpan.FromSeconds(1.0), leading = true) ])
    ] []
```

Results in:
```html
<div data-on-click__window__debounce.1000ms.leading="$foo = ''"></div>
```

### [Ds.onIntersect : `data-on-intersect`](https://data-star.dev/reference/attribute_plugins#data-on-intersect)

Runs an expression when the element intersects with the viewport.

```fsharp
Elem.div [ Ds.onIntersect "$intersected = true" ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Full) ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Half, onlyOnce = true) ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Half, onlyOnce = true, debounce = Debounce.With(TimeSpan.FromSeconds(1.0))) ] []

Elem.div [ Ds.onIntersect ("$intersected = true", visibility = Half, onlyOnce = true, throttle = Throttle.With(TimeSpan.FromSeconds(1.0))) ] []
```

### [Ds.onSignalChange | Ds.onAnySignalChange : `data-on-signal-change`](https://data-star.dev/reference/attribute_plugins#data-on-signal-change)

Runs an expression when another signal changes. This should be used sparingly, as it is cost intensive.

```fsharp
Elem.div [ Ds.onAnySignalChange "$show = !$show" ] []

Elem.div [ Ds.onSignalChange (sp"foo", "$show = !$show") ] []
```

### [Ds.onRequestAnimationFrame : `data-on-raf`](https://data-star.dev/reference/attribute_plugins#data-on-raf)

Runs an expression on every request animation frame event.

```fsharp
Elem.div [ Ds.onRequestAnimationFrame "$frame++" ] []

Elem.div [ Ds.onRequestAnimationFrame ("$frame++", debounce = Debounce.With(TimeSpan.FromSeconds(1.0))) ] []

Elem.div [ Ds.onRequestAnimationFrame ("$frame++", throttle = Throttle.With(TimeSpan.FromSeconds(1.0))) ] []
```

### [Ds.onInterval : `data-on-interval`](https://data-star.dev/reference/attribute_plugins#data-on-interval)

Runs an expression at a regular interval. The interval duration defaults to 1 second and can be modified by passing a `TimeSpan`

```fsharp
Elem.div [
    Ds.signal (sp"intervalSignalOneSecond", false)
    Ds.onInterval "$intervalSignalOneSecond = !$intervalSignalOneSecond"
    Ds.text "'One Second Interval = ' + $intervalSignalOneSecond"
] []

Elem.div [
    Ds.signal (sp"intervalSignalFiveSecond", false)
    Ds.onInterval ("$intervalSignalFiveSecond = !$intervalSignalFiveSecond", TimeSpan.FromSeconds(5.0), leading = true)
    Ds.text "'Five Second Interval = ' + $intervalSignalFiveSecond"
] []
```

## _Actions and Functions_

Datastar provides a number of actions and functions that can be used in [Datastar expressions](https://data-star.dev/guide/datastar_expressions)
for making server requests and manipulating signals.

### [@get | @post | @put | @patch | @delete](https://data-star.dev/reference/action_plugins#backend-plugins)

These actions make requests to any backend service that supports Server Side Events (SSE).
Luckily an F#-friendly [SDK exists](https://data-star.dev/reference/sdks#dotnet) and `Falco.Datastar` has several [helper methods](#reading-signals-and-server-side-events)

All signals, that do not have an underscore prefix, are sent in the request.
`@get` will send the signal values as query parameters. All others are sent within a JSON body.

```fsharp
Elem.div [ Ds.onLoad (Ds.get "/get") ] []

Elem.button [ Ds.onClick (Ds.post "/post") ] [ Text.raw "Post" ]

Elem.button [ Ds.onClick (Ds.put "/put") ] [ Text.raw "Put" ]

Elem.button [ Ds.onClick (Ds.patch "/patch") ] [ Text.raw "Patch" ]

Elem.button [ Ds.onClick (Ds.delete "/delete") ] [ Text.raw "Delete" ]
```

The majority of the above examples are fired from a button click, but remember that these are
[Datastar expressions](https://data-star.dev/guide/datastar_expressions) and any [event or trigger](#_events-and-triggers_)
could activate them.

Each request action can also be provided a number of options, explained in depth [here](https://data-star.dev/reference/action_plugins#options):

```fsharp
Elem.button [ Ds.onClick (Ds.get ("/endpoint",
                                  { RequestOptions.Defaults with
                                        IncludeLocal = true;
                                        Headers = [ ("X-Csrf-Token", "JImikTbsoCYQ9...") ]
                                        OpenWhenHidden = true }
                                 )) ] [ Text.raw "Push the Button" ]
```

### [`@setAll`](https://data-star.dev/reference/action_plugins#setall)

Sets all the signals that start with the prefix to the expression provided in the second argument.
This is useful for setting all the values of a signal namespace at once.

```fsharp
Elem.div [ Ds.onEvent (OnEvent.SignalsChanged, (Ds.setAll "foo." true)) ] []
```

### [`@toggleAll`](https://data-star.dev/reference/action_plugins#toggleall)

Toggles all the signals that start with the prefix. This is useful for toggling all the values of a signal namespace at once.

```fsharp
Elem.div [ Ds.onEvent (OnEvent.SignalsChanged, (Ds.toggleAll "foo.")) ] []
```

### [Ds.persistSignals, Ds.persistAllSignals : `data-persist`](https://data-star.dev/reference/attribute_plugins#data-persist)

Persists signals in local or session storage. Useful for storing values between page loads.

```fsharp
// persist all signals in local storage
Elem.div [ Ds.persistAllSignals ] []

// persist the signals `foo` and `bar` in local storage
Elem.div [ Ds.persistSignals [ sp"foo"; sp"bar" ] ] []

// persist all signals in session storage
Elem.div [ Ds.persistAllSignals (inSession = true) ] []
```

### [Ds.scrollIntoView : `data-scroll-into-view`](https://data-star.dev/reference/attribute_plugins#data-scroll-into-view)

Scrolls the element into view. Useful when updating the DOM from the backend, and you want to scroll to the new content.

```fsharp
Elem.div [ Ds.scrollIntoView (Smooth, Center, Center) ] []

Elem.div [ Ds.scrollIntoView (Auto, Left, Bottom, focus = true) ] []
```

## _Miscellaneous Actions_

Helper actions that can be used in [Datastar expressions](https://data-star.dev/guide/datastar_expressions) and performing browser operations.

### [Ds.replaceUrl : `data-replace-url`](https://data-star.dev/reference/attribute_plugins#data-replace-url)

Replace the URL in the browser without reloading the page. Can be a relative of absolute URL, and is an evaluated
expression.

```fsharp
Elem.div [ Ds.replaceUrl "/page${page}" ] []
```

### [`@clipboard`](https://data-star.dev/reference/action_plugins#clipboard)

Copies the provided evaluated expression to the clipboard.

```fsharp
Elem.button [ Ds.onClick (Ds.clipboard "'Hello, ' + $name") ] [ Text.raw "Copy Name" ]
```

### [`@fit`](https://data-star.dev/reference/action_plugins#fit)

Make a value linear interpolate from an original range to new one.

```fsharp
Elem.button [
    Ds.onClick (
        Ds.clipboard (
            Ds.fit (5, oldRange = (0, 10), newRange = (0, 100))
        )
    )
    ] [ Text.raw "Copy 50 to clipboard" ]
```

### [Ds.ignore, Ds.ignoreThis : `data-star-ignore`](https://data-star.dev/reference/attribute_plugins#data-star-ignore)

Datastar walks the entire DOM and applies plugins to each element it encounters.
It’s possible to tell Datastar to ignore an element and its descendants by placing a data-star-ignore attribute on it.
This can be useful for preventing naming conflicts with third-party libraries.

`Ds.ignore` will force Datastar to ignore the element and all child elements.
`Ds.ignoreThis` only affects the attribute it is attached to.

```fsharp
Elem.div [ Ds.ignore ] [
    Elem.div [ Ds.text "ignoredAsWell" ] []
]

Elem.div [ Ds.ignoreThis ] [
    Elem.div [ Ds.text "thisIsNotIgnored" ] []
]
```

## _When to `$`_

You may have noticed in the sample code that the `$` is used in some places, but not others. At first, it might be
confusing when a `$` is required, but it really isn't all that complicated when you think of the arguments as
being a signal path or an expression.

The `$` symbol is used as a shorthand to access the value of a signal in an expression (i.e. `$count` -> `count.value`),
so when the `$` is elided, you are referring to the signal directly.
[`Ds.bind signalPath`](#dsbind--data-bind) is two-way binding to the signal, so requires the signal path, no `$`.
[`Ds.text`](#dstext--data-text) is replacing the element's innerText, so it only needs the value, via `$`.
[`Ds.computed (signalPath, expression)`](#dscomputed--data-computed) needs both a signal path AND an expression, e.g. `Ds.computed ("countPlusTen", "$count + 10")`.

If you want to be certain you are doing it correctly, then there is a helper method `SignalPath.sp`
that will throw an exception at startup, if a signal path contains any invalid symbols, such as `$`.

```fsharp
open StarFederation.Datastar.SignalPath
...
Elem.input [ Attr.typeCheckbox; Ds.bind (sp"checkBoxSignal") ]
```

## Reading Signals and Server Side Events

[Falco.Datastar](https://github.com/SpiralOSS/Falco.Datastar) has a number of Request and Response functions for reading the [Datastar signal](https://data-star.dev/guide/going_deeper#2-signals) values and responding
with [Datastar Server Side Events (SSEs)](https://data-star.dev/reference/sse_events).

Sections:
- [Reading Signal Values](#reading-signal-values)
- [Responding with Signals](#responding-with-signals)
- [Responding with HTML Fragments](#responding-with-html-fragments)
- [Streaming Server Side Events](#streaming-server-side-events)

## _Reading Signal Values_

All requests are sent with a `{datastar: *}` object containing the current signals (you can keep signals local to the client
by prefixing the name with an underscore). When using a `GET` request, the signals are sent as a query parameter; otherwise,
they are sent as a JSON body. Luckily, with [Falco.Datastar](https://github.com/SpiralOSS/Falco.Datastar), you don't have to
worry about any of that.

### `Request.getSignals<'T>`

Will use [`System.Text.Json.JsonSerializer`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.deserialize) to deserialize the signals into a `'T`.
If there are no signals, then the default values will be returned.

```fsharp
type MySignals() =
    member val firstName = "John" with get, set
    member val lastName = "Doe" with get, set
    member val email = "john@doe.com" with get, set
...
let httpHandler : HttpHandler = (fun ctx -> task {
    let! signals = Request.getSignals<MySignals> (ctx)
    ...
    })
```

### `Request.getSignal<'T>`

Given a signal path, it will deserialize the item at the end of the path into a `'T`.

```fsharp
let httpHandler : HttpHandler = (fun ctx -> task {
    let! lastAgentShown = Request.getSignal<int> (ctx, "user.firstName")
    ...
    })
```

### `Request.getSignals`

Will simply return the signal values as a JSON string

```fsharp
let httpHandler : HttpHandler = (fun ctx -> task {
    let! rawSignals = Request.getSignals (ctx)
    ...
    })
```

## _Responding with Signals_

### `Response.ofMergeSignals<'T>`

Serializes signals with [`System.Text.Json.JsonSerializer`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer) and sends to client where Datastar will merge them.

```fsharp
Response.ofMergeSignals (MySignals())
```

### `Response.ofMergeSignal<'T>`

Updates a single signal on the client.

```fsharp
Response.ofMergeSignal (sp"user.firstName", "Don")
```

### `Response.ofMergeSignals`

Takes the signals as a JSON string and sends them to the client where Datastar will merge them.

```fsharp
Response.ofMergeSignals @" { ""firstName"": ""Don"", ""lastName"": ""Syme"" } "
```

### `Response.ofRemoveSignals`

Given a `seq` of signal paths, will remove that signals from the client.

```fsharp
Response.ofRemoveSignals [ sp"user.firstName"; sp"user.lastName" ]
```

## _Responding with HTML Fragments_

HTML fragments are sent to client and replace the current element (matching on the `id` attribute) with the one that is sent.
The following functions are `HttpHandler`s that will send down a single Server Sent Event.

### `Response.ofHtmlFragments`

Will render an XMLNode and send it to the client. Client Datastar will replace the element with the matching `id` attribute (or optionally provided selector)

```fsharp
Response.ofHtmlFragments (Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hello, World from the Server!" ])
```

### `Response.ofHtmlStringFragments`

Will send HTML fragments to the client. Client Datastar will replace the element with the matching `id` attribute (or optionally provided selector)

```fsharp
Response.ofHtmlStringFragments @"<h2 id='hello'>Hello, World from the Server!"
```

### `Response.ofRemoveFragments`

Will send a command to client Datastar to remove fragments with the matching selector.

```fsharp
Response.ofRemoveFragments [ sel"hello" ]
```

## _Streaming Server Side Events_

Within the `Response` module there are the `of` methods that are for sending single server side events and then closing the connection.
But, [Datastar's](https://data-star.dev) true power is unlocked when the client keep a connection open to the server
and updates are sent to all clients as they are received by the server. This is a much more efficient alternative
to having all the clients poll the server every few moments and provides much greater control over back-pressure.

The [progress bar example](https://data-star.dev/examples/progress_bar) is a great and simple demonstration of what can
be achieved with [Datastar](https://data-star.dev); no polling necessary.

All the functions in [Responding with Signals](#responding-with-signals) and [Responding with HTML Fragments](#responding-with-html-fragments)
are mirrored with a function with `sse` as their prefix instead of `of`.

The `sse*` functions require an `sseHandler` that is obtained through `Response.startServerSentEventStream`.

```fsharp
let handleStream = (fun ctx -> task {
    let sseHandler = Response.startServerSentEventStream ctx

    let mutable counter = 0

    while not <| ctx.RequestAborted.IsCancellationRequested do
        do! Response.sseMergeSignal (sseHandler, sp"counter", counter)
        do! Response.sseHtmlFragments (sseHandler, Elem.pre [ Attr.id "counterId" ] [ Text.raw counter.ToString() ] )
        do! Task.Delay(TimeSpan.FromSeconds 1L, ctx.RequestAborted)
        counter <- counter + 1
```

See the [Streaming example](examples/Streaming) for more.
