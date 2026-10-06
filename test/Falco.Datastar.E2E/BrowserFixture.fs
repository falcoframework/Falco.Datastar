namespace Falco.Datastar.E2E

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Playwright
open Xunit

/// One browser and one server for all the tests. Set E2E_CHROMIUM_PATH to use a Chromium that is already installed
/// instead of the one that `playwright install chromium` downloads.
type BrowserFixture() =
    let mutable app : WebApplication = null
    let mutable playwright : IPlaywright = null
    let mutable browser : IBrowser = null
    let mutable baseUrl = ""

    /// The address of the server that serves the pages in Site.fs
    member _.BaseUrl = baseUrl

    /// A page in a new browser context, so cookies and storage are not shared between tests
    member _.NewPageAsync () : Task<IPage> = task {
        let! context = browser.NewContextAsync()
        return! context.NewPageAsync()
    }

    interface IAsyncLifetime with
        member _.InitializeAsync () = task {
            let builder = WebApplication.CreateBuilder()
            builder.WebHost.UseUrls "http://127.0.0.1:0" |> ignore
            builder.Logging.ClearProviders() |> ignore
            app <- builder.Build()
            app.UseRouting().UseFalco(Site.endpoints) |> ignore
            do! app.StartAsync()
            baseUrl <- Seq.head app.Urls

            let! created = Playwright.CreateAsync()
            playwright <- created
            let options = BrowserTypeLaunchOptions(Headless = true)
            match Environment.GetEnvironmentVariable "E2E_CHROMIUM_PATH" with
            | null | "" -> ()
            | path -> options.ExecutablePath <- path
            let! launched = playwright.Chromium.LaunchAsync options
            browser <- launched
        }
        member _.DisposeAsync () = task {
            if not (isNull browser) then do! browser.CloseAsync()
            if not (isNull playwright) then playwright.Dispose()
            if not (isNull app) then do! app.DisposeAsync().AsTask()
        }

[<CollectionDefinition("browser")>]
type BrowserCollection() =
    interface ICollectionFixture<BrowserFixture>

module internal ExampleHost =
    let rec findRoot (directory:DirectoryInfo) =
        match directory.GetFiles "Falco.Datastar.sln" with
        | [||] when not (isNull directory.Parent) -> findRoot directory.Parent
        | [||] -> failwith "Could not find Falco.Datastar.sln above the test assembly, so the example cannot be started"
        | _ -> directory

    let freePort () =
        use listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        listener.Start()
        (listener.LocalEndpoint :?> System.Net.IPEndPoint).Port

/// The RocketComponents example, started as its own process on a free port. It is the app that the README points to.
type ExampleFixture() =
    let mutable started : Process = null
    let mutable baseUrl = ""
    // The last lines that the example wrote, for the message when it does not start
    let output = System.Collections.Generic.Queue<string>()
    let remember (line:string) =
        if not (isNull line) then
            lock output (fun () ->
                output.Enqueue line
                if output.Count > 40 then output.Dequeue() |> ignore)
    let lastOutput () = lock output (fun () -> String.Join("\n", output))

    member _.BaseUrl = baseUrl

    interface IAsyncLifetime with
        member _.InitializeAsync () = task {
            let root = ExampleHost.findRoot (DirectoryInfo AppContext.BaseDirectory)
            let port = ExampleHost.freePort ()
            baseUrl <- $"http://127.0.0.1:{port}"
            // The example's appsettings.json sets Urls, and the example does not read the command line. An environment variable named Urls comes after appsettings.json, so it wins.
            let info = ProcessStartInfo("dotnet", $"run --project examples/RocketComponents -c Release --no-launch-profile", WorkingDirectory = root.FullName)
            info.Environment["Urls"] <- baseUrl
            info.RedirectStandardOutput <- true
            info.RedirectStandardError <- true
            started <- Process.Start info
            // A full pipe would stop the app, so the output is always read. The last lines go into the message when the app does not start.
            started.OutputDataReceived.Add(fun data -> remember data.Data)
            started.ErrorDataReceived.Add(fun data -> remember data.Data)
            started.BeginOutputReadLine()
            started.BeginErrorReadLine()

            use client = new HttpClient()
            let deadline = DateTime.UtcNow.AddMinutes 3.0
            let mutable ready = false
            while not ready do
                if started.HasExited then failwith $"The RocketComponents example stopped with exit code {started.ExitCode} before it answered. What it wrote last:\n{lastOutput ()}"
                if DateTime.UtcNow > deadline then failwith $"The RocketComponents example did not answer within 3 minutes. What it wrote last:\n{lastOutput ()}"
                try
                    let! response = client.GetAsync baseUrl
                    ready <- response.IsSuccessStatusCode
                with _ -> ()
                if not ready then do! Task.Delay 500
        }
        member _.DisposeAsync () = task {
            if not (isNull started) && not started.HasExited then
                started.Kill true
                do! started.WaitForExitAsync()
        }
