namespace NugetMetadata

open System

type Hash =
    | MD5
    | SHA1
    | SHA256

[<RequireQualifiedAccess>]
module Hash =
    let private mapping =
        [
            "406ea660-64cf-4c82-b6f0-42d48172a799", Hash.MD5
            "ff1816ec-aa5e-4d10-87f7-6f4963833460", Hash.SHA1
            "8829d00f-11b8-4213-878b-770e8597ac16", Hash.SHA256
        ]
        |> Map.ofList

    let getAlgo (hashGuid : Guid) : Hash option =
        mapping |> Map.tryFind (hashGuid.ToString ())

type Language =
    | VB
    | CSharp
    | FSharp

[<RequireQualifiedAccess>]
module Language =
    let private mapping =
        [
            "3f5162f8-07c6-11d3-9053-00c04fa302a1", Language.CSharp
            "3a12d0b8-c26c-11d0-b442-00a0244a1dd2", Language.VB
            "ab4f38c9-b6e6-43ba-be3b-58080b2ccce3", Language.FSharp
        ]
        |> Map.ofList

    let get (langGuid : Guid) : Language option =
        mapping |> Map.tryFind (langGuid.ToString ())

type Document =
    {
        HashAlgo : Hash
        Name : string
        Language : Language
        Hash : byte[]
        IsEmbedded : bool
    }
