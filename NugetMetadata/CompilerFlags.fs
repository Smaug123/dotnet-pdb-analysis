namespace NugetMetadata

open System
open System.Reflection.Metadata

type CompilerFlags =
    /// For example, ("version", "2") ; ("language", "C#") ; ("language-version", "7.3")
    | CompilerFlags of (string * string) list

[<RequireQualifiedAccess>]
module CompilerFlags =
    let private compilerFlagsGuid = Guid "B5FEEC05-8CD0-4A83-96DA-466284BB4BD8"

    let read (portablePdbReader : MetadataReader) : CompilerFlags list =
        portablePdbReader.GetCustomDebugInformation EntityHandle.ModuleDefinition
        |> Seq.choose (fun handle ->
            let debugInfo = portablePdbReader.GetCustomDebugInformation handle
            // magic "compiler flags" GUID
            if portablePdbReader.GetGuid debugInfo.Kind = compilerFlagsGuid then
                Some (portablePdbReader.GetBlobReader debugInfo.Value)
            else
                None
        )
        |> Seq.map (fun blob ->
            let flags =
                seq {
                    let mutable nullIndex = blob.IndexOf 0uy

                    while nullIndex >= 0 do
                        let key = blob.ReadUTF8 nullIndex
                        blob.ReadByte () |> ignore
                        let value = blob.ReadUTF8 (blob.IndexOf 0uy)
                        blob.ReadByte () |> ignore
                        yield (key, value)
                        nullIndex <- blob.IndexOf 0uy
                }
                |> Seq.toList

            CompilerFlags flags
        )
        |> Seq.toList
