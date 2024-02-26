namespace NugetMetadata

open System
open System.Reflection.PortableExecutable
open Microsoft.Extensions.Logging

[<RequireQualifiedAccess>]
module MetadataReader =
    /// Call `new PEReader(stream{, PEStreamOptions.LeaveOpen})` to get the input.
    /// It needs to stay alive for as long as you use the MetadataReader.
    let make (peReader : PEReader) : System.Reflection.Metadata.MetadataReader option =
        let debugDirs =
            peReader.ReadDebugDirectory ()
            |> Seq.filter (fun de -> de.Type = DebugDirectoryEntryType.EmbeddedPortablePdb)
            |> Seq.toList

        match debugDirs with
        | [] -> None
        | [ dir ] ->
            peReader.ReadEmbeddedPortablePdbDebugDirectoryData(dir).GetMetadataReader ()
            |> Some
        | _ -> failwith "Multiple embedded PDBs found and I don't know what to do about it"

    let private embeddedSourceGuid = Guid "0E8A571B-6926-466E-B4AD-8AB04611F5FE"

    let getDocuments (log : ILogger) (portablePdbReader : System.Reflection.Metadata.MetadataReader) : Document list =
        portablePdbReader.Documents
        |> Seq.choose (fun document ->
            if document.IsNil then
                failwith "nil document"
                None
            else
                let doc = portablePdbReader.GetDocument document

                if doc.Name.IsNil then
                    failwith "got a nil name for a document"

                let name = portablePdbReader.GetString doc.Name

                if doc.Language.IsNil || doc.HashAlgorithm.IsNil || doc.Hash.IsNil then
                    log.LogInformation ("A property was nil in document {DocumentName}", doc.Name)
                    None
                else
                    let isEmbedded =
                        portablePdbReader.GetCustomDebugInformation document
                        |> Seq.exists (fun handle ->
                            let debugInfo = portablePdbReader.GetCustomDebugInformation handle
                            portablePdbReader.GetGuid debugInfo.Kind = embeddedSourceGuid
                        )

                    let hashAlgo =
                        portablePdbReader.GetGuid doc.HashAlgorithm
                        |> Hash.getAlgo
                        |> Option.defaultWith (fun () -> failwith "unrecognised hash algo!")

                    let language =
                        portablePdbReader.GetGuid doc.Language
                        |> Language.get
                        |> Option.defaultWith (fun () -> failwith "unrecognised language!")

                    let hash = portablePdbReader.GetBlobBytes doc.Hash

                    {
                        Name = name
                        Language = language
                        HashAlgo = hashAlgo
                        Hash = hash
                        IsEmbedded = isEmbedded
                    }
                    |> Some
        )
        |> Seq.toList
