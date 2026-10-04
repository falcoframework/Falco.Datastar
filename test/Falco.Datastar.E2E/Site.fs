/// The pages that the browser tests open. Each page uses one part of the library, and a test drives it in a real browser.
module Falco.Datastar.E2E.Site

open System
open System.Collections.Concurrent
open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Falco.Datastar.SignalPath
open Microsoft.AspNetCore.Http

let private page (title:string) (body:XmlNode list) (extraHead:XmlNode list) : HttpHandler =
    Response.ofHtml (Elem.html [] [ Elem.head [] ([ Elem.title [] [ Text.raw title ]; Ds.rocketCdnScript ] @ extraHead); Elem.body [] body ])

let private moduleScript (source:string) =
    Elem.script [ Attr.type' "module" ] [ Text.raw ("import { rocket, publishRocketManifests } from '" + Ds.rocketCdnSrc + "'\n" + source) ]

// The time of each request that reached /retry-target, so a test can measure how long Datastar waited between attempts.
let retryAttempts = ConcurrentQueue<DateTime>()

let private retryOptions =
    { RequestOptions.Defaults with
        Retry = OnError
        RetryInterval = TimeSpan.FromMilliseconds 100.0
        RetryScaler = 10.0
        RetryMaxWait = TimeSpan.FromMilliseconds 300.0
        RetryMaxCount = 4 }

let private retryPage =
    page "retry" [ Elem.button [ Attr.id "go"; Ds.onClick (Ds.get ("/retry-target", retryOptions)) ] [ Text.raw "go" ] ] []

let private retryTarget : HttpHandler = fun ctx ->
    retryAttempts.Enqueue DateTime.UtcNow
    ctx.Response.StatusCode <- 500
    Response.ofPlainText "fail" ctx

// Ds.withCase: the signal is named in snake_case, and the event listener in camelCase
let private casePage =
    page "case"
        [ Elem.div [ Ds.signal (sp "myValue", 41) |> Ds.withCase CaseStyle.Snake ] []
          Elem.div [ Ds.signal (sp "seen", false) ] []
          Elem.span [ Attr.id "snake"; Ds.text "$my_value" ] []
          Elem.span [ Attr.id "camel"; Ds.text "$myValue" ] []
          Elem.div [ Attr.id "listener"; Ds.onEvent ("my-event", "$seen = true") |> Ds.withCase CaseStyle.Camel ] []
          Elem.span [ Attr.id "seen-out"; Ds.text "$seen" ] [] ] []

// The manifest that a page publishes, and what Request.getRocketManifests made of it
let manifests = ConcurrentQueue<Result<RocketManifestDocument, RocketManifestError>>()

let private manifestPage =
    page "manifest" [ Elem.create "demo-card" [] []; Elem.create "demo-plain" [] [] ] [
        moduleScript """
rocket('demo-card', {
  props: ({ string, number, bool, oneOf }) => ({
    title: string.trim.default('Card').docs({ description: 'The card title.' }),
    maxCount: number.min(0).default(3),
    open: bool,
    theme: oneOf('light', 'dark', 'system').default('system'),
  }),
  manifest: {
    slots: [ { name: 'default', description: 'Body.' } ],
    events: [ { name: 'close', kind: 'custom-event', bubbles: true, composed: true } ],
  },
  render: ({ html, props }) => html`<p>${props.title}</p>`,
})
rocket('demo-plain', { render: ({ html }) => html`<b>plain</b>` })
await new Promise(resolve => setTimeout(resolve, 300))
const response = await publishRocketManifests({ endpoint: '/manifests' })
document.title = 'published ' + response.status
""" ]

let private manifestSink : HttpHandler = fun ctx -> task {
    let! result = Request.getRocketManifests ctx
    manifests.Enqueue result
    ctx.Response.StatusCode <- 204
}

// Directives on the server-rendered children of a shadow-DOM component. Rocket.local works on load, and the directives run after a server patch.
let private shadowBox (stamp:string) =
    Elem.create "shadow-box" [ Attr.id "box" ] [
        Elem.p [ Attr.id "stamp" ] [ Text.raw stamp ]
        Elem.span [ Attr.id "n"; Ds.text (Rocket.local "n") ] []
        Rocket.templateIf (Rocket.local "on", [ Elem.p [ Attr.id "if-branch" ] [ Text.raw "if branch" ] ])
        Rocket.templateElse [ Elem.p [ Attr.id "else-branch" ] [ Text.raw "else branch" ] ]
    ]

let private shadowPage =
    page "shadow"
        [ shadowBox "first"
          Elem.button [ Attr.id "patch"; Ds.onClick (Ds.post "/shadow-patch") ] [ Text.raw "patch" ] ]
        [ moduleScript """
rocket('shadow-box', {
  mode: 'open',
  setup({ $$ }) { $$('n', 5); $$('on', true) },
  render: ({ html }) => html`<div><slot></slot></div>`,
})
""" ]

let private shadowPatch : HttpHandler = Response.ofHtmlElements (shadowBox "patched")

// The typed layer: the signals, expressions and statements of Expr.fs
let private count = Signal.browser<int> "count"
let private double' = Signal.browser<int> "double"
let private menuOpen = Signal.browser<bool> "menuOpen"
let private loading = Signal.browser<bool> "loading"
let private formName = Signal.server<string> "form.name"
let private isOn = Signal.rocket<bool> "on"
let private items = Signal.rocket<string list> "items"
let private step = Signal.rocket<int> "step"

let private typedBox (id':string) =
    Elem.create "typed-box" [ Attr.id id' ] [
        Elem.div [ Ds.signal (SignalPath.sp "on", false); Ds.signal (SignalPath.sp "items", [ "a"; "b" ]); Ds.signal (SignalPath.sp "step", 2) ] []
        Elem.button [ Attr.id $"{id'}-toggle"; Ds.onClick (Stmt.toggle isOn) ] [ Text.raw "toggle" ]
        Elem.button [ Attr.id $"{id'}-first"; Ds.onClick (Stmt.set step (Expr.int 1)) ] [ Text.raw "first" ]
        Rocket.templateIf (Expr.equal (Expr.read step) (Expr.int 1), [ Elem.p [ Attr.id $"{id'}-step" ] [ Text.raw "step one" ] ])
        Rocket.templateElseIf (Expr.equal (Expr.read step) (Expr.int 2), [ Elem.p [ Attr.id $"{id'}-step" ] [ Text.raw "step two" ] ])
        Rocket.templateElse [ Elem.p [ Attr.id $"{id'}-step" ] [ Text.raw "another step" ] ]
        Elem.span [ Attr.id $"{id'}-state"; Ds.text (Expr.ifElse (Expr.read isOn) (Expr.string "on") (Expr.string "off")) ] []
        Elem.ul [ Attr.id $"{id'}-rows" ] [
            Rocket.forEach (Expr.read items, fun item index -> [ Elem.li [ Ds.text (Expr.concat [ Expr.toText index; Expr.string ":"; item ]) ] [] ]) ]
    ]

/// The text that the /typed page shows through Expr.string. It has every character that could end a string, an attribute or a tag.
let hostileText = "it's \"q\" <b>x</b> ') alert(1)"

let private typedPage =
    page "typed"
        [ Elem.div [ Ds.signal (count, 0); Ds.signal (menuOpen, false); Ds.signal (formName, "Ada"); Ds.signal (loading, false)
                     Ds.computed (double', Expr.multiply (Expr.read count) (Expr.int 2)) ] [
              Elem.button [ Attr.id "inc"; Ds.onClick (Stmt.set count (Expr.add (Expr.read count) (Expr.int 1))) ] [ Text.raw "+" ]
              Elem.button [ Attr.id "dec"; Ds.onClick (Stmt.set count (Expr.subtract (Expr.read count) (Expr.int 1))) ] [ Text.raw "-" ]
              Elem.span [ Attr.id "count"; Ds.text (Expr.read count) ] []
              Elem.span [ Attr.id "double"; Ds.text (Expr.read double') ] []
              Elem.span [ Attr.id "big"; Ds.show (Expr.greater (Expr.read count) (Expr.int 2)) ] [ Text.raw "big" ]
              Elem.button [ Attr.id "menu"; Ds.onClick (Stmt.toggle menuOpen) ] [ Text.raw "menu" ]
              Elem.div [ Attr.id "panel"; Ds.class' ("open", Expr.read menuOpen)
                         Ds.attr' ("aria-expanded", Expr.ifElse (Expr.read menuOpen) (Expr.string "true") (Expr.string "false")) ] [ Text.raw "panel" ]
              Elem.input [ Attr.id "name-input"; Ds.bind formName ]
              Elem.span [ Attr.id "greeting"; Ds.text (Expr.concat [ Expr.string "Hello "; Expr.read formName; Expr.string "!" ]) ] []
              Elem.span [ Attr.id "hostile"; Ds.text (Expr.string hostileText) ] []
              Elem.button [ Attr.id "send"; Ds.indicator loading; Ds.onClick (Stmt.post "/typed-save?note=it's&b=2") ] [ Text.raw "send" ]
              Elem.pre [ Attr.id "saved" ] [ Text.raw "not saved" ] ]
          typedBox "t1"
          typedBox "t2" ]
        [ moduleScript "rocket('typed-box', { mode: 'light' })" ]

let private typedSave : HttpHandler = fun ctx -> task {
    use! signals = Request.getSignalsJson ctx
    let note = string ctx.Request.Query["note"]
    let b = string ctx.Request.Query["b"]
    let text = $"signals={signals.RootElement.GetRawText()} note={note} b={b}"
    return! Response.ofHtmlElements (Elem.pre [ Attr.id "saved" ] [ Text.raw text ]) ctx
}

// GET and DELETE send the signals in the query, and POST in the body. Request.getSignals reads them all the same way.
type Item = { N: int }

let private itemPage =
    page "item"
        [ Elem.div [ Ds.signal (sp "n", 7) ] [
              Elem.button [ Attr.id "delete"; Ds.onClick (Ds.delete "/item") ] [ Text.raw "delete" ]
              Elem.button [ Attr.id "post"; Ds.onClick (Ds.post "/item") ] [ Text.raw "post" ]
              Elem.span [ Attr.id "result" ] [ Text.raw "nothing yet" ] ] ] []

let private itemAnswer (verb:string) : HttpHandler = fun ctx -> task {
    let! signals = Request.getSignals<Item> ctx
    let text =
        match signals with
        | ValueSome item -> $"{verb} got n={item.N}"
        | ValueNone -> $"{verb} got no signals"
    return! Response.ofHtmlElements (Elem.span [ Attr.id "result" ] [ Text.raw text ]) ctx
}

// A page under a policy that does not allow unsafe-eval. It only works when it gives Datastar the nonce.
let private cspPage (withNonce:bool) : HttpHandler = fun ctx ->
    let nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes 16)
    ctx.Response.Headers["Content-Security-Policy"] <- $"script-src 'nonce-{nonce}'"
    let htmlAttributes = match withNonce with | true -> [ Ds.nonce nonce ] | false -> []
    let document =
        Elem.html htmlAttributes [
            Elem.head [] [ Elem.script [ Attr.type' "module"; Attr.src Ds.cdnSrc; Attr.create "nonce" nonce ] [] ]
            Elem.body [ Ds.signal (sp "n", 7) ] [ Elem.span [ Attr.id "n"; Ds.text "$n" ] [] ] ]
    Response.ofHtml document ctx

// What Datastar sends with a request: the signals it filters, and the headers
let private filterPage =
    let postWith (options:RequestOptions) = Ds.onClick (Stmt.postWith "/filter-save" options)
    page "filter"
        [ Elem.div [ Ds.signal (Signal.server<string> "form.name", "Ada"); Ds.signal (Signal.server<string> "other.note", "x")
                     Ds.signal (Signal.server<string> "secret.key", "k"); Ds.signal (Signal.browser<string> "draft", "d") ] [
              Elem.button [ Attr.id "only-form"; postWith { RequestOptions.Defaults with FilterSignals = SignalsFilter.Prefix "form." } ] [ Text.raw "only form" ]
              Elem.button [ Attr.id "not-secret"; postWith { RequestOptions.Defaults with FilterSignals = SignalsFilter.Exclude "^secret" } ] [ Text.raw "not secret" ]
              Elem.button [ Attr.id "with-header"; postWith { RequestOptions.Defaults with Headers = [ "X-Test", "it's <b>hello</b>" ] } ] [ Text.raw "with header" ]
              Elem.pre [ Attr.id "saved" ] [ Text.raw "not saved" ] ] ] []

let private filterSave : HttpHandler = fun ctx -> task {
    use! signals = Request.getSignalsJson ctx
    let header = string ctx.Request.Headers["X-Test"]
    let text = $"signals={signals.RootElement.GetRawText()} header={header}"
    // The header has markup in it on purpose, so it is written as text
    return! Response.ofHtmlElements (Elem.pre [ Attr.id "saved" ] [ Text.enc text ]) ctx
}

// requestCancellation: an AbortController in a signal. Datastar only cancels the request when it is given the controller itself.
let slowRequests = ConcurrentQueue<string>()

let private abortPage =
    page "abort"
        [ Elem.div [ Attr.create "data-signals:_controller" "new AbortController()" ] [
              Elem.button [ Attr.id "slow"; Ds.onClick (Stmt.getWith "/slow" { RequestOptions.Defaults with RequestCancellation = AbortController "$_controller" }) ] [ Text.raw "slow" ]
              Elem.button [ Attr.id "abort"; Ds.onClick (Stmt.unsafeRaw "$_controller.abort()") ] [ Text.raw "abort" ] ] ] []

let private slow : HttpHandler = fun ctx -> task {
    slowRequests.Enqueue "started"
    try
        do! System.Threading.Tasks.Task.Delay(10000, ctx.RequestAborted)
        slowRequests.Enqueue "finished"
    with :? OperationCanceledException -> slowRequests.Enqueue "aborted"
}

// Strings in a string helper: text stays text, a template literal is evaluated, and an action name inside a string is not run
let private stringsPage =
    page "strings"
        [ Elem.div [ Ds.signal (sp "name", "Ada") ] [
              Elem.span [ Attr.id "quoted"; Ds.text "'Hello $name'" ] []
              Elem.span [ Attr.id "concatenated"; Ds.text "'Hello ' + $name" ] []
              Elem.span [ Attr.id "template"; Ds.text "`Hello ${$name}`" ] []
              Elem.span [ Attr.id "action"; Ds.text "'call @get(x) and $$y'" ] [] ] ] []

// Rocket props: each helper writes what the codec of the same type reads
let private propBoxScript = """
rocket('prop-box', {
  mode: 'light',
  props: ({ string, number, bool, date, json, bin }) => ({
    label: string.default('none'),
    maxCount: number.default(0),
    open: bool,
    when: date,
    settings: json.default({}),
    payload: bin,
  }),
  render: ({ html, props }) => html`<output id="read">${JSON.stringify({
    label: props.label, maxCount: props.maxCount, open: props.open,
    when: props.when.toISOString(), settings: props.settings, payload: Array.from(props.payload) })}</output>`,
})
"""

let private propsPage =
    page "props"
        [ Elem.create "prop-box" [
              Rocket.propString ("label", "it's <b>x</b> & \"q\"")
              Rocket.propNumber ("maxCount", 2.5)
              Rocket.propBool ("open", true)
              Rocket.propDate ("when", DateTimeOffset(2026, 9, 21, 12, 30, 0, TimeSpan.Zero))
              Rocket.propJson ("settings", {| firstName = "Ada"; tags = [ "a"; "b" ] |})
              Rocket.propBin ("payload", [| 1uy; 2uy; 255uy |]) ] [] ]
        [ moduleScript propBoxScript ]

let endpoints =
    [ get "/retry" retryPage
      get "/retry-target" retryTarget
      get "/case" casePage
      get "/manifest" manifestPage
      post "/manifests" manifestSink
      get "/shadow" shadowPage
      post "/shadow-patch" shadowPatch
      get "/typed" typedPage
      post "/typed-save" typedSave
      get "/item" itemPage
      delete "/item" (itemAnswer "DELETE")
      post "/item" (itemAnswer "POST")
      get "/csp" (cspPage true)
      get "/csp-without-nonce" (cspPage false)
      get "/filter" filterPage
      post "/filter-save" filterSave
      get "/abort" abortPage
      get "/slow" slow
      get "/strings" stringsPage
      get "/props" propsPage ]
