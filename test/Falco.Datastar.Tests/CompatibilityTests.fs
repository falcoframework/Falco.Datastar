namespace Falco.Datastar.Tests

open Falco.Datastar
open Falco.Markup

// These cases only have to compile. They pin the promises that the changelog makes about upgrading from 1.3.0,
// so that a change to the overloads or to a type cannot quietly break the advice that the changelog gives.
//
// The CHANGELOG says that passing an argument whose type is not yet known fails with FS0041 and that a type annotation fixes it,
// and that a string or a typed value needs no annotation. A build failure in this file means one of those is no longer true.

module CompatibilityTests =
    // The string and typed overloads both still work with no annotation
    let private stringOverloadWorks = Elem.button [ Ds.onClick "$count = 1" ] []
    let private typedOverloadWorks = Elem.button [ Ds.onClick (Stmt.get "/count") ] []
    let private showStringWorks = Elem.div [ Ds.show "$flag" ] []
    let private showTypedWorks = Elem.div [ Ds.show (Expr.bool true) ] []

    // A type annotation picks the overload, which is what the changelog tells people to do
    let private click (handler: string) = Elem.button [ Ds.onClick handler ] []
    let private clickWithStatement (statement: Stmt) = Elem.button [ Ds.onClick statement ] []

    // Signals, expressions and statements keep the shapes the documentation shows
    let private count = Signal.browser<int> "count"
    let private name = Signal.server<string> "form.name"
    let private on = Signal.rocket<bool> "on"

    let private typedUsage =
        Elem.div
            [ Ds.signal (count, 0)
              Ds.signal (name, "Ada")
              Ds.bind name
              Ds.indicator (Signal.browser<bool> "loading")
              Ds.computed (Signal.server<int> "total", Expr.multiply (Expr.read count) (Expr.int 2))
              Ds.onClick (Stmt.set count (Expr.add (Expr.read count) (Expr.int 1)))
              Ds.onInit (Stmt.post "/save")
              Ds.text (Expr.read count)
              Ds.show (Expr.greater (Expr.read count) (Expr.int 0)) ]
            []

    let private rocketUsage =
        Elem.create "my-toggle"
            [ Attr.id "toggle"
              Rocket.propString ("label", "Clicks")
              Rocket.propNumber ("step", 1)
              Rocket.propBool ("open", false) ]
            [ Rocket.templateIf (Expr.read on, [ Text.raw "on" ])
              Rocket.forEach (Expr.read (Signal.rocket<string list> "items"), fun item _ -> [ Elem.li [ Ds.text item ] [] ]) ]

    // A Rocket signal in an attribute name. Rocket's rewriteDataAttributes scopes a data-signals:NAME key to the
    // component's own path whether or not the setup declared NAME, so this is a supported use and not an error.
    let private rocketSignalInAttribute =
        Elem.div
            [ Ds.signal (on, false)
              Ds.bind on
              Ds.computed (Signal.rocket<bool> "shown", Expr.read on)
              Ds.indicator (Signal.rocket<bool> "busy") ]
            []

    // The numeric operators are constrained to the types JavaScript's number type stands for. IFormattable was not
    // enough: string, DateTime and Guid all satisfy it, and $a + $b on those is meaningless JavaScript. These are the
    // types that have to keep working. This file can show that; it cannot show that the others are refused, because
    // FsUnit cannot assert that code failed to compile.
    let private numericOperatorsOnNumbers =
        [ Expr.toString (Expr.add (Expr.read (Signal.browser<int> "i")) (Expr.int 5))
          Expr.toString (Expr.subtract (Expr.read (Signal.browser<double> "d")) (Expr.unsafeRaw "5"))
          Expr.toString (Expr.multiply (Expr.read (Signal.browser<decimal> "m")) (Expr.unsafeRaw "5"))
          Expr.toString (Expr.divide (Expr.read (Signal.browser<int64> "l")) (Expr.unsafeRaw "5"))
          Expr.toString (Expr.remainder (Expr.read (Signal.browser<float32> "f")) (Expr.unsafeRaw "5"))
          Expr.toString (Expr.greater (Expr.read (Signal.browser<float> "s")) (Expr.unsafeRaw "5"))
          Expr.toString (Expr.less (Expr.read (Signal.browser<uint32> "u")) (Expr.unsafeRaw "5")) ]

    let private numericProps =
        [ Rocket.propNumber ("n", 1)
          Rocket.propNumber ("d", 1.5m)
          Rocket.propNumber ("f", 2.5f) ]

    /// The names of these are not used. They exist so that the compiler has to check them.
    let internal checkEverything () =
        [ box stringOverloadWorks
          box typedOverloadWorks
          box showStringWorks
          box showTypedWorks
          box (click "$x = 1")
          box (clickWithStatement (Stmt.get "/x"))
          box typedUsage
          box rocketUsage
          box rocketSignalInAttribute
          box numericOperatorsOnNumbers
          box numericProps ]
