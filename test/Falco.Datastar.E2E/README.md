# Browser tests

These tests open pages that use Falco.Datastar in a real Chromium and check what Datastar and Rocket did with them.
The unit tests in `test/Falco.Datastar.Tests` check the text the library writes. These tests check that a browser, running Datastar 1.0.4, does what that text says.

They cover:

- `Ds.withCase`, and the retry options, measured on the server (`RetryMaxWait` keeps each wait under its cap)
- `Ds.nonce` on a page whose policy does not allow `unsafe-eval`, and the same page without it, where the browser refuses the JavaScript
- request options: `FilterSignals` (including that signals starting with an underscore are never sent), `Headers`, and an `AbortController` that cancels a request
- `DELETE` requests carrying signals, read with `Request.getSignals`
- the typed layer (`Signal`, `Expr`, `Stmt`), including text with quotes and tags in it, and exactly which signals a request sends
- the string helpers: text in quotes stays text, a template literal is evaluated, and an action name in a string is not run
- Rocket: `publishRocketManifests` read by `Request.getRocketManifests`, every prop helper read back by its codec, shadow-DOM template directives, the `templateIf` / `templateElseIf` / `templateElse` chain, and two instances of a component
- the `examples/RocketComponents` app, started as its own process: a click in one window reaches another through the stream, the write answers 204, and the stream is compressed with Brotli

The pages are in `Site.fs`. The tests are in `BrowserTests.fs`.

## Running them

They are not part of the solution, and `dotnet test test/Falco.Datastar.Tests` does not run them. CI does not run them on a push or a pull request.
To run them on GitHub, use the **e2e** workflow in the Actions tab (Run workflow).

To run them on your machine:

```sh
dotnet build test/Falco.Datastar.E2E -c Release
pwsh test/Falco.Datastar.E2E/bin/Release/net10.0/playwright.ps1 install chromium
dotnet test test/Falco.Datastar.E2E -c Release --no-build
```

If Chromium is already installed, set `E2E_CHROMIUM_PATH` to it and skip the install step.
The install step needs `pwsh`; without it, `npx playwright install chromium` puts the browser in the same place.

The tests need internet access, because the pages load Datastar from the jsDelivr CDN, as the library's own `Ds.cdnScript` does.
The example is built the first time the tests start it, which takes a while.
