namespace Falco.Datastar.E2E

open System
open System.Threading.Tasks
open Falco.Datastar
open FsUnit.Xunit
open Microsoft.Playwright
open Xunit

/// Each test opens a page from Site.fs in a real Chromium, with the Datastar script from the CDN, and looks at what Datastar did with it.
[<Collection("browser")>]
type BrowserTests(browser:BrowserFixture) =

    let expect (locator:ILocator) = Assertions.Expect locator

    let openPage (path:string) = task {
        let! page = browser.NewPageAsync()
        let errors = ResizeArray<string>()
        page.PageError.Add errors.Add
        let! _ = page.GotoAsync (browser.BaseUrl + path)
        return page, errors
    }

    [<Fact>]
    member _.``A retry that has a RetryMaxWait never waits longer than it`` () = task {
        Site.retryAttempts.Clear()
        let! page, _ = openPage "/retry"
        do! page.ClickAsync "#go"
        // The first request and 4 retries
        let deadline = DateTime.UtcNow.AddSeconds 10.0
        while Site.retryAttempts.Count < 5 && DateTime.UtcNow < deadline do
            do! Task.Delay 50
        let times = Site.retryAttempts.ToArray()
        times.Length |> should equal 5
        let gaps = times |> Array.pairwise |> Array.map (fun (before, after) -> (after - before).TotalMilliseconds)
        // The wait starts at 100 ms and grows by 10 times each time. Without RetryMaxWait the second wait would be 1000 ms.
        // With RetryMaxWait of 300 ms, the waits after the first are 300 ms, and a request takes a little time on top of that.
        gaps.[0] |> should be (greaterThan 80.0)
        gaps.[0] |> should be (lessThan 250.0)
        for gap in gaps |> Array.skip 1 do
            gap |> should be (greaterThan 250.0)
            gap |> should be (lessThan 450.0)
    }

    [<Fact>]
    member _.``Ds.withCase names the signal in snake_case and the event listener in camelCase`` () = task {
        let! page, _ = openPage "/case"
        do! expect(page.Locator "#snake").ToHaveTextAsync "41"
        // Datastar's own casing is camelCase, so $myValue is not the signal that Ds.withCase created
        do! expect(page.Locator "#camel").ToHaveTextAsync ""
        do! expect(page.Locator "#seen-out").ToHaveTextAsync "false"
        let! _ = page.EvaluateAsync "document.getElementById('listener').dispatchEvent(new CustomEvent('myEvent'))"
        do! expect(page.Locator "#seen-out").ToHaveTextAsync "true"
    }

    [<Fact>]
    member _.``Request.getRocketManifests reads the manifest that publishRocketManifests posts`` () = task {
        while not Site.manifests.IsEmpty do Site.manifests.TryDequeue() |> ignore
        let! page, _ = openPage "/manifest"
        do! Assertions.Expect(page).ToHaveTitleAsync "published 204"
        match Site.manifests.ToArray() |> Array.tryLast with
        | Some (Ok document) ->
            document.Version |> should equal 1
            document.Components |> List.map (fun c -> c.Tag) |> should equal [ "demo-card"; "demo-plain" ]
            let card = document.Components |> List.find (fun c -> c.Tag = "demo-card")
            card.Props |> List.map (fun p -> p.Name) |> should equal [ "title"; "maxCount"; "open"; "theme" ]
            let maxCount = card.Props |> List.find (fun p -> p.Name = "maxCount")
            maxCount.Attribute |> should equal "max-count"
            maxCount.Type |> should equal RocketPropType.Number
            maxCount.Default.GetInt32() |> should equal 3
            let theme = card.Props |> List.find (fun p -> p.Name = "theme")
            theme.Type |> should equal RocketPropType.OneOf
            theme.Values |> ValueOption.map List.length |> should equal (ValueSome 3)
            card.Slots |> List.map (fun s -> s.Name) |> should equal [ "default" ]
            card.Events |> List.map (fun e -> e.Kind) |> should equal [ RocketEventKind.CustomEvent ]
        | Some (Error error) -> failwith error.Message
        | None -> failwith "The page published, but the server did not receive a manifest"
    }

    [<Fact>]
    member _.``Template directives in a shadow-DOM component stay inert until a server patch`` () = task {
        let! page, _ = openPage "/shadow"
        // Rocket.local works on load
        do! expect(page.Locator "#n").ToHaveTextAsync "5"
        do! expect(page.Locator "#if-branch").ToHaveCountAsync 0
        do! expect(page.Locator "#else-branch").ToHaveCountAsync 0
        do! page.ClickAsync "#patch"
        do! expect(page.Locator "#stamp").ToHaveTextAsync "patched"
        do! expect(page.Locator "#if-branch").ToHaveCountAsync 1
        do! expect(page.Locator "#else-branch").ToHaveCountAsync 0
    }

    [<Fact>]
    member _.``Typed expressions and statements do what they say`` () = task {
        let! page, errors = openPage "/typed"
        do! expect(page.Locator "#count").ToHaveTextAsync "0"
        do! expect(page.Locator "#big").ToBeHiddenAsync()
        for _ in 1 .. 3 do do! page.ClickAsync "#inc"
        do! expect(page.Locator "#count").ToHaveTextAsync "3"
        do! expect(page.Locator "#double").ToHaveTextAsync "6"
        do! expect(page.Locator "#big").ToBeVisibleAsync()
        do! page.ClickAsync "#dec"
        do! expect(page.Locator "#count").ToHaveTextAsync "2"
        do! expect(page.Locator "#big").ToBeHiddenAsync()

        do! expect(page.Locator "#panel").ToHaveAttributeAsync("aria-expanded", "false")
        do! page.ClickAsync "#menu"
        do! expect(page.Locator "#panel").ToHaveAttributeAsync("aria-expanded", "true")
        do! expect(page.Locator "#panel").ToHaveClassAsync "open"

        do! expect(page.Locator "#greeting").ToHaveTextAsync "Hello Ada!"
        do! page.FillAsync("#name-input", "Grace")
        do! expect(page.Locator "#greeting").ToHaveTextAsync "Hello Grace!"
        errors |> Seq.toList |> should be Empty
    }

    [<Fact>]
    member _.``Text in Expr.string stays text, whatever it contains`` () = task {
        let! page, errors = openPage "/typed"
        do! expect(page.Locator "#hostile").ToHaveTextAsync Site.hostileText
        do! expect(page.Locator "#hostile b").ToHaveCountAsync 0
        errors |> Seq.toList |> should be Empty
    }

    [<Fact>]
    member _.``A request from a typed page sends the server signal and keeps the browser signals in the browser`` () = task {
        let! page, _ = openPage "/typed"
        do! page.ClickAsync "#inc"
        do! page.ClickAsync "#send"
        // Only the server signal is sent. The signals that start with an underscore (count, menuOpen, loading and double) stay in the browser.
        // The apostrophe and the ampersand in the URL arrived as they were written.
        do! expect(page.Locator "#saved").ToHaveTextAsync """signals={"form":{"name":"Ada"}} note=it's b=2"""
    }

    [<Fact>]
    member _.``Two instances of a Rocket component keep separate signals`` () = task {
        let! page, _ = openPage "/typed"
        do! expect(page.Locator "#t1-state").ToHaveTextAsync "off"
        do! expect(page.Locator "#t2-state").ToHaveTextAsync "off"
        do! page.ClickAsync "#t1-toggle"
        do! expect(page.Locator "#t1-state").ToHaveTextAsync "on"
        do! expect(page.Locator "#t2-state").ToHaveTextAsync "off"
        do! expect(page.Locator "#t1-rows li").ToHaveTextAsync [| "0:a"; "1:b" |]
        do! expect(page.Locator "#t2-rows li").ToHaveTextAsync [| "0:a"; "1:b" |]
    }

    [<Fact>]
    member _.``A DELETE request sends the signals, so the server reads them`` () = task {
        let! page, _ = openPage "/item"
        do! page.ClickAsync "#delete"
        do! expect(page.Locator "#result").ToHaveTextAsync "DELETE got n=7"
        do! page.ClickAsync "#post"
        do! expect(page.Locator "#result").ToHaveTextAsync "POST got n=7"
    }

    [<Fact>]
    member _.``Ds.nonce lets Datastar run on a page whose policy does not allow unsafe-eval`` () = task {
        let! page, _ = openPage "/csp"
        do! expect(page.Locator "#n").ToHaveTextAsync "7"
    }

    [<Fact>]
    member _.``The same page without Ds.nonce is stopped by the policy, which is why the nonce is needed`` () = task {
        let! page = browser.NewPageAsync()
        let messages = ResizeArray<string>()
        page.Console.Add(fun message -> lock messages (fun () -> messages.Add message.Text))
        let! _ = page.GotoAsync (browser.BaseUrl + "/csp-without-nonce")
        // Datastar loaded, and the browser refused the string of JavaScript that it tried to run
        let deadline = DateTime.UtcNow.AddSeconds 10.0
        let refused () = lock messages (fun () -> messages |> Seq.exists (fun message -> message.Contains "unsafe-eval"))
        while not (refused ()) && DateTime.UtcNow < deadline do
            do! Task.Delay 100
        refused () |> should equal true
        do! expect(page.Locator "#n").ToHaveTextAsync ""
    }

    [<Fact>]
    member _.``A rocket template chain shows one branch, and changes when the signal does`` () = task {
        let! page, _ = openPage "/typed"
        do! expect(page.Locator "#t1-step").ToHaveTextAsync "step two"
        do! page.ClickAsync "#t1-first"
        do! expect(page.Locator "#t1-step").ToHaveTextAsync "step one"
        do! expect(page.Locator "#t2-step").ToHaveTextAsync "step two"
    }

    [<Fact>]
    member _.``FilterSignals sends the signals that match, and never the ones that stay in the browser`` () = task {
        let! page, _ = openPage "/filter"
        do! page.ClickAsync "#only-form"
        do! expect(page.Locator "#saved").ToHaveTextAsync """signals={"form":{"name":"Ada"}} header="""
        // An exclude of its own would replace Datastar's rule for underscore signals, so the library adds the rule to it
        do! page.ClickAsync "#not-secret"
        do! expect(page.Locator "#saved").ToHaveTextAsync """signals={"form":{"name":"Ada"},"other":{"note":"x"}} header="""
    }

    [<Fact>]
    member _.``Headers in the request options reach the server as they were written`` () = task {
        let! page, _ = openPage "/filter"
        do! page.ClickAsync "#with-header"
        do! expect(page.Locator "#saved").ToContainTextAsync "header=it's <b>hello</b>"
    }

    [<Fact>]
    member _.``An AbortController in the request options cancels the request when it is aborted`` () = task {
        Site.slowRequests.Clear()
        let! page, _ = openPage "/abort"
        do! page.ClickAsync "#slow"
        let waitFor (what:string) = task {
            let deadline = DateTime.UtcNow.AddSeconds 8.0
            while not (Site.slowRequests.ToArray() |> Array.contains what) && DateTime.UtcNow < deadline do
                do! Task.Delay 50
        }
        do! waitFor "started"
        do! page.ClickAsync "#abort"
        do! waitFor "aborted"
        Site.slowRequests.ToArray() |> should equal [| "started"; "aborted" |]
    }

    [<Fact>]
    member _.``Text in a string helper stays text, a template literal is evaluated, and an action name in a string is not run`` () = task {
        let! page, _ = openPage "/strings"
        do! expect(page.Locator "#quoted").ToHaveTextAsync "Hello $name"
        do! expect(page.Locator "#concatenated").ToHaveTextAsync "Hello Ada"
        do! expect(page.Locator "#template").ToHaveTextAsync "Hello Ada"
        do! expect(page.Locator "#action").ToHaveTextAsync "call @get(x) and $$y"
    }

    [<Fact>]
    member _.``Each Rocket prop helper writes what the codec of the same type reads`` () = task {
        let! page, errors = openPage "/props"
        do! expect(page.Locator "#read").ToHaveTextAsync """{"label":"it's <b>x</b> & \"q\"","maxCount":2.5,"open":true,"when":"2026-09-21T12:30:00.000Z","settings":{"firstName":"Ada","tags":["a","b"]},"payload":[1,2,255]}"""
        errors |> Seq.toList |> should be Empty
    }

/// The example is started once for the tests in this class, and the browser is shared with the other tests
[<Collection("browser")>]
type ExampleTests(example:ExampleFixture, browser:BrowserFixture) =
    interface IClassFixture<ExampleFixture>

    [<Fact>]
    member _.``RocketComponents: a click in one window reaches every window through the stream, and the write answers 204`` () = task {
        let! first = browser.NewPageAsync()
        let! second = browser.NewPageAsync()
        let streamResponse = first.WaitForResponseAsync(fun response -> response.Url.Contains "/counter/stream")
        let! _ = first.GotoAsync example.BaseUrl
        let! _ = second.GotoAsync example.BaseUrl
        let! stream = streamResponse
        // The stream is compressed with Brotli
        stream.Headers["content-encoding"] |> should equal "br"

        let output (page:IPage) = page.Locator "my-counter output"
        let! before = (output first).InnerTextAsync()
        let expected = string (int before + 1)

        let commandResponse = first.WaitForResponseAsync(fun response -> response.Url.EndsWith "/counter/increment")
        do! first.Locator("my-counter button", PageLocatorOptions(HasText = "+")).ClickAsync()
        let! command = commandResponse
        command.Status |> should equal 204

        do! Assertions.Expect(output first).ToHaveTextAsync expected
        do! Assertions.Expect(output second).ToHaveTextAsync expected
    }

    [<Fact>]
    member _.``RocketComponents: a section keeps its open state to itself`` () = task {
        let! page = browser.NewPageAsync()
        let! _ = page.GotoAsync example.BaseUrl
        do! Assertions.Expect(page.Locator "#first-body").ToBeHiddenAsync()
        do! page.ClickAsync "#first-button"
        do! Assertions.Expect(page.Locator "#first-body").ToBeVisibleAsync()
        do! Assertions.Expect(page.Locator "#first-button").ToHaveAttributeAsync("aria-expanded", "true")
        do! Assertions.Expect(page.Locator "#second-body").ToBeHiddenAsync()
    }
