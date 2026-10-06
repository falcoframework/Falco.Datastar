open System
open System.Threading.Channels
open System.Threading.Tasks
open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Falco.Datastar.SignalPath
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.ResponseCompression
open Microsoft.Extensions.DependencyInjection

// This example follows the Tao of Datastar (https://data-star.dev/guide/the_tao_of_datastar).
//
// - State in the right place: the server owns the count, and every visitor sees the same one. The browser only keeps state that one
//   section needs for itself, whether it is open, and that state never leaves the browser.
// - CQRS: the page opens one long-lived request that the server writes updates to (a read), and a click sends a short request (a write).
//   The write answers 204 No Content. The new count reaches the page over the stream, and never before the server has confirmed it.
// - Fat morph: the server sends the whole component again, and Datastar morphs only what changed.
// - Start with the defaults: no request option is set.
// - Compression: the stream is compressed with Brotli.

// Rocket components are written in JavaScript, so this is the only JavaScript in the example.
// `my-counter` shows a count. It does not know what a click means. It emits an event, and the page decides what to do about it.
// `my-toggle` has no props, setup or render code. The server renders its children, and they keep their state in the component.
let components =
    "import { rocket } from '" + Ds.rocketCdnSrc + "'\n" + """
rocket('my-counter', {
  props: ({ number, string }) => ({ count: number.default(0), label: string.default('Count') }),
  setup({ action, emit }) {
    action('decrement', () => emit('decrement'))
    action('increment', () => emit('increment'))
  },
  render: ({ html, props }) => html`
    <strong>${props.label}</strong>
    <button data-on:click="@decrement()">-</button>
    <output>${props.count}</output>
    <button data-on:click="@increment()">+</button>
  `,
})
rocket('my-toggle', { mode: 'light' })
"""

// The counter lives in one agent, so no other code can change the count or the list of viewers.
type CounterMessage =
    | Read of AsyncReplyChannel<int>
    | Change of by:int
    | Watch of Channel<int> * AsyncReplyChannel<int>
    | Unwatch of Channel<int>

let counter =
    MailboxProcessor.Start(fun inbox ->
        let rec loop count (watchers:Channel<int> list) = async {
            match! inbox.Receive() with
            | Read reply ->
                reply.Reply count
                return! loop count watchers
            | Change by ->
                let next = max 0 (count + by)
                watchers |> List.iter (fun watcher -> watcher.Writer.TryWrite next |> ignore)
                return! loop next watchers
            | Watch (watcher, reply) ->
                reply.Reply count
                return! loop count (watcher :: watchers)
            | Unwatch watcher ->
                return! loop count (watchers |> List.filter (fun other -> not (obj.ReferenceEquals(other, watcher))))
        }
        loop 0 [])

let counterElement count =
    Elem.create "my-counter"
        [ Attr.id "counter"
          Rocket.propString ("label", "Clicks")
          Rocket.propNumber ("count", count)
          Ds.onEvent ("decrement", Stmt.post "/counter/decrement")
          Ds.onEvent ("increment", Stmt.post "/counter/increment") ]
        []

// Each section keeps its own `open` state. It is a Rocket component signal, so two sections do not share it.
// The component declares it with $$('open', false) in its setup, so the name goes into the attribute as a plain
// signal path; Signal.rocket is for reading it in an expression, which writes $$open.
let section id' title =
    let isOpen = Signal.rocket<bool> "open"
    Elem.create "my-toggle" [ Attr.id id' ] [
        Elem.div [ Ds.signal (SignalPath.sp "open", false) ] []
        Elem.button
            [ Attr.id $"{id'}-button"
              Ds.onClick (Stmt.toggle isOpen)
              Ds.attr' ("aria-expanded", Expr.ifElse (Expr.read isOpen) (Expr.string "true") (Expr.string "false")) ]
            [ Text.raw title ]
        Elem.p [ Attr.id $"{id'}-body"; Ds.show (Expr.read isOpen) ] [ Text.raw "Only this section knows that it is open." ]
    ]

let handleIndex : HttpHandler = fun ctx -> task {
    let! count = counter.PostAndAsyncReply Read |> Async.StartAsTask
    let html =
        Elem.html [] [
            Elem.head [] [
                Elem.title [] [ Text.raw "Rocket Components" ]
                Ds.rocketCdnScript
                Elem.script [ Attr.type' "module" ] [ Text.raw components ]
            ]
            // The long-lived request: the server writes to it whenever the count changes
            Elem.body [ Ds.onInit (Stmt.get "/counter/stream") ] [
                Text.h1 "Example: Rocket Components"
                Elem.h2 [] [ Text.raw "my-counter: the server owns the count" ]
                Elem.p [] [ Text.raw "Every visitor sees the same count. A click sends a command, and the change comes back over the stream that this page opened." ]
                counterElement count
                Elem.h2 [] [ Text.raw "my-toggle: state that only the browser needs" ]
                section "first" "First section"
                section "second" "Second section"
            ]
        ]
    return! Response.ofHtml html ctx
}

let handleStream : HttpHandler = fun ctx -> task {
    let watcher = Channel.CreateUnbounded<int>()
    let! current = counter.PostAndAsyncReply(fun reply -> Watch (watcher, reply)) |> Async.StartAsTask
    try
        do! Response.sseStartResponse ctx
        do! Response.sseHtmlElements ctx (counterElement current)
        // All Datastar methods and the read below throw when the visitor leaves, which ends the loop
        while true do
            let! next = watcher.Reader.ReadAsync ctx.RequestAborted
            do! Response.sseHtmlElements ctx (counterElement next)
    finally
        counter.Post (Unwatch watcher)
}

let command by : HttpHandler = fun ctx ->
    counter.Post (Change by)
    ctx.Response.StatusCode <- 204
    Task.CompletedTask

let endpoints =
    [ get "/" handleIndex
      get "/counter/stream" handleStream
      post "/counter/increment" (command 1)
      post "/counter/decrement" (command -1) ]

let builder = WebApplication.CreateBuilder()

// Compression: a stream of morphs compresses very well. Response compression skips text/event-stream unless you add it.
builder.Services.AddResponseCompression(fun options ->
    options.EnableForHttps <- true
    options.Providers.Add<BrotliCompressionProvider>()
    options.MimeTypes <- Seq.append ResponseCompressionDefaults.MimeTypes [ "text/event-stream" ])
|> ignore

let wapp = builder.Build()

wapp.UseResponseCompression() |> ignore
wapp.UseRouting().UseFalco(endpoints) |> ignore
wapp.Run()
