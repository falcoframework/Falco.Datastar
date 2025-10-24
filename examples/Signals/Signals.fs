open Falco
open Falco.Markup
open Falco.Routing
open Falco.Datastar
open Microsoft.AspNetCore.Builder
open Falco.Datastar.SignalsFilter
open Falco.Datastar.SignalPath

let handleIndex : HttpHandler =
    let html =
        Elem.html [] [
            Elem.head [ Attr.title "Signals" ] [ Ds.cdnScript ]
            Elem.body [] [
                Text.h1 "Example: Signals"

                Elem.hr []

                Elem.div [ Ds.signal (sp"showSignal", false) ] [
                    Elem.h2 [] [ Text.raw "Ds.show" ]
                    Elem.button
                        [ Ds.onClick "$showSignal = !$showSignal" ]
                        [ Text.raw "Click Me" ]
                    Elem.span
                        [ Ds.show "$showSignal" ]
                        [ Text.raw "Peek-a-boo!" ]
                ]

                Elem.hr []
                Elem.h2 [] [ Text.raw "Ds.onInterval" ]

                Elem.div [
                    Ds.signal (sp"intervalSignalOneSecond", false)
                    Ds.onInterval ("$intervalSignalOneSecond = !$intervalSignalOneSecond", intervalMs = 1000)
                    Ds.text "'One Second Interval = ' + $intervalSignalOneSecond"
                ] []

                Elem.div [
                    Ds.signal (sp"intervalSignalFiveSecond", false)
                    Ds.onInterval ("$intervalSignalFiveSecond = !$intervalSignalFiveSecond", intervalMs = 5000, leading = true)
                    Ds.text "'Five Second Interval = ' + $intervalSignalFiveSecond"
                ] []

                Elem.hr []
                Elem.h2 [] [ Text.raw "Ds.effect" ]

                Elem.div [
                    Ds.effect "$effectText = (($intervalSignalFiveSecond ? 2 : 0) + ($intervalSignalOneSecond ? 1 : 0)).toString(2)"
                ] [
                    Text.raw "(($intervalSignalFiveSecond ? 2 : 0) + ($intervalSignalOneSecond ? 1 : 0)).toString(2) = "
                    Elem.span [ Ds.text "$effectText" ] []
                ]

                Elem.hr []
                Elem.h2 [] [ Text.raw "Ds.onEvent" ]

                Elem.div [
                    Ds.signal (sp"selectedText", "hi")
                    Ds.onEvent("mouseup", "$selectedText = document.getSelection().toString()")
                    Ds.onEvent("mouseenter", "$selectedText = 'oooo! The anticipation!'") ] [ Text.raw "Select some of this text" ]
                Elem.div [ Ds.text "$selectedText" ] [ Text.raw "hi" ]

                Elem.hr []

                Elem.div [ Ds.signal (sp"ranges.numberSignal", 50) ] [
                    Elem.h2 [] [ Text.raw "Ds.bind" ]
                    Elem.input [ Attr.id "typeCheckbox"; Attr.typeCheckbox; Ds.bind (sp"toggle.typeCheckbox") ]; Elem.label [Attr.for' "typeCheckbox"] [ Text.raw "Check" ]
                    Elem.br []
                    Elem.input [ Attr.typeRadio; Attr.id "A"; Attr.value "A"; Ds.bind (sp"toggle.typeRadio") ]; Elem.label [Attr.for' "A"] [Text.raw "A"]
                    Elem.input [ Attr.typeRadio; Attr.id "B"; Attr.value "B"; Ds.bind (sp"toggle.typeRadio") ]; Elem.label [Attr.for' "B"] [Text.raw "B"]
                    Elem.input [ Attr.typeRadio; Attr.id "C"; Attr.value "C"; Ds.bind (sp"toggle.typeRadio") ]; Elem.label [Attr.for' "C"] [Text.raw "C"]
                    Elem.br []
                    Elem.input [ Attr.typeRange; Attr.min "0"; Attr.max "100"; Ds.bind (sp"ranges.numberSignal") ]
                    Elem.br []
                    Elem.input [ Attr.typeText; Ds.bind (sp"ranges.numberSignal") ]
                ]

                Elem.hr []

                Elem.div [ Ds.computed (sp"ranges.rangedSignal", "$ranges.numberSignal * 10") ] [
                    Elem.h2 [] [ Text.raw "Ds.computed" ]
                    Elem.span [ Ds.text "$ranges.numberSignal + ' * 10 = ' + $ranges.rangedSignal" ] []
                ]

                Elem.hr []

                Elem.div [] [
                    Elem.h2 [] [ Text.raw "Ds.attr'" ]
                    Elem.input [ Attr.typeRange; Attr.disabled; Attr.readonly; Attr.min "0"; Attr.max "1000"; Ds.attr' ("value", "$ranges.rangedSignal") ]
                    Elem.br []
                    Elem.span [ Ds.text "$ranges.rangedSignal" ] []
                ]

                Elem.hr []

                Elem.div [ Ds.signal (sp"classSignal", @"off") ] [
                    Elem.style [] [ Text.raw ".red { color:red }" ]
                    Elem.h2 [ Ds.class' ("red", "$classSignal === 'on'") ] [ Text.raw "Ds.class'"; ]
                    Elem.button
                        // classSignal could be a bool, but we are demo'ing more complicated expressions
                        [ Ds.onClick @"$classSignal = $classSignal === 'off' ? 'on' : 'off'" ]
                        [ Text.raw "Click Me" ]
                ]

                Elem.hr []

                Elem.div [ Ds.signal (sp"checkBoxSignal", false) ] [
                    Elem.h2 [] [ Text.raw "Ds.ref"; ]
                    Elem.input [ Attr.id "checkbox"; Attr.typeCheckbox; Ds.bind (sp"checkBoxSignal") ]
                    Elem.label [ Attr.for' "checkbox" ] [ Text.raw "I have two favorite numbers" ]
                    Elem.br []

                    Elem.input [ Attr.id "r1"; Attr.typeRadio; Attr.name "radioGroup1" ]
                    Elem.label [ Attr.for' "r1" ] [ Text.raw "One" ]
                    Elem.input [ Attr.id "r2"; Attr.typeRadio; Attr.name "radioGroup1" ]
                    Elem.label [ Attr.for' "r2" ] [ Text.raw "Two" ]
                    Elem.input [ Attr.id "r3"; Attr.typeRadio; Attr.name "radioGroup1" ]
                    Elem.label [ Attr.for' "r3" ] [ Text.raw "Three" ]
                    Elem.br []
                    Elem.label [ Ds.show "$checkBoxSignal" ] [ Text.raw "Group2:" ]
                    Elem.input [ Attr.id "r4"; Attr.typeRadio; Ds.ref (sp"r4"); Attr.name "radioGroup1" ]
                    Elem.label [ Attr.for' "r4" ] [ Text.raw "Four" ]
                    Elem.input [ Attr.id "r5"; Attr.typeRadio; Ds.ref (sp"r5"); Attr.name "radioGroup1" ]
                    Elem.label [ Attr.for' "r5" ] [ Text.raw "Five" ]
                    Elem.input [ Attr.id "r6"; Attr.typeRadio; Ds.ref (sp"r6"); Attr.name "radioGroup1" ]
                    Elem.label [ Attr.for' "r6" ] [ Text.raw "Six" ]
                    Elem.br [
                        // note that this must follow AFTER the refs are created above
                        Ds.filterOnSignalPatch (sf"^checkBoxSignal$")
                        Ds.onSignalPatch "$r4.name = $r5.name = $r6.name = ($checkBoxSignal ? 'radioGroup2' : 'radioGroup1')"
                    ]
                ]

                Elem.hr []

                Elem.div [] [
                    Elem.h2 [] [ Text.raw "Signal Debugging" ]
                    Elem.pre [ Ds.jsonSignals ] []
                ]
            ]
        ]
    Response.ofHtml html

let handleClick : HttpHandler =
    let html = Elem.h2 [ Attr.id "hello" ] [ Text.raw "Hello, World from the Server!" ]
    Response.ofHtmlElements html

let wapp = WebApplication.Create()
wapp.UseStaticFiles() |> ignore

let endpoints =
    [
        get "/" handleIndex
        get "/click" handleClick
    ]

wapp.UseRouting()
    .UseFalco(endpoints)
    .Run()
