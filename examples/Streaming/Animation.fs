module Animation

open System
open System.IO
open System.Text
open System.Threading.Tasks

// the laziest zstd parser
let readAnimationFile zstPath =
    let zstdReader (stream:Stream) = seq {
        use streamReader = new StreamReader(stream, encoding=Encoding.ASCII)
        streamReader.ReadLine() |> ignore  // header metadata
        while not <| streamReader.EndOfStream do
            yield streamReader.ReadLine()
        }
    let scanLines =
        seq {
            use zstStream = File.OpenRead(zstPath)
            use decompressionStream = new ZstdSharp.DecompressionStream(zstStream)
            yield! zstdReader decompressionStream
        }

    scanLines
    |> Seq.scan (fun (_, sb:StringBuilder) line ->
        if line = "?"
        then (ValueSome (sb.Replace("?","",0,1).ToString()), sb.Clear())
        else (ValueNone, sb.AppendLine(line))
        ) (ValueNone, StringBuilder() )
    |> Seq.filter (fst >> ValueOption.isSome)
    |> Seq.map (fst >> ValueOption.get)

// the laziest broadcast block with the latest frame of BadApple, plays continuously
let readBadAppleFrames =
    let zstPath = Path.Combine("assets", "badapple.zst")
    readAnimationFile zstPath |> Seq.toArray

let badAppleFrames = readBadAppleFrames
let mutable currentBadAppleFrame = 0
let totalBadAppleFrames = badAppleFrames |> Array.length
backgroundTask {
    while true do
        currentBadAppleFrame <- (currentBadAppleFrame + 1) % totalBadAppleFrames
        do! Task.Delay(TimeSpan.FromMilliseconds(50))
} |> ignore

let getCurrentBadAppleFrame () = badAppleFrames[currentBadAppleFrame]
