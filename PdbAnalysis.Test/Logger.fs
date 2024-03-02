namespace PdbAnalysis.Test

open System
open Microsoft.Extensions.Logging

[<RequireQualifiedAccess>]
module Logger =
    let makeTest () : (unit -> string list) * ILogger =
        let results = ResizeArray ()

        let logger =
            { new ILogger with
                member _.Log (_, _, c, exc, f) =
                    let toLog = f.Invoke (c, exc)
                    lock results (fun () -> results.Add toLog)

                member _.IsEnabled _ = true

                member _.BeginScope _ =
                    { new IDisposable with
                        member _.Dispose () = ()
                    }
            }

        let freeze () =
            lock results (fun () -> Seq.toList results)

        freeze, logger
