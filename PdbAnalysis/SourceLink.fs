namespace NugetMetadata

open System
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization

type SourceLinkJsonDto =
    {
        [<JsonPropertyName "documents">]
        Documents : Map<string, string>
    }

type SourceLinkEntry =
    | Wild of key : string * valueBeforeWild : string * valueAfterWild : string
    | Verbatim of key : string * value : string

type SourceLink = | SourceLink of SourceLinkEntry list

type SourceLinkValidation =
    {
        Linked : (Document * string) list
        NotLinked : Document list
        /// This is here for information only, because it's what NuGetPackageExplorer does.
        /// I think a better check is `LinkedToNonExistent`.
        LinkedButObj : (Document * string) list
        LinkedToNonExistent : (Document * string * string) list
    }


[<RequireQualifiedAccess>]
module SourceLink =
    let private sourceLinkGuid = Guid "CC110556-A091-4D38-9FEC-25AB9A351A6A"

    let pullMetadata (portablePdbReader : System.Reflection.Metadata.MetadataReader) =
        portablePdbReader.GetCustomDebugInformation System.Reflection.Metadata.EntityHandle.ModuleDefinition
        |> Seq.choose (fun handle ->
            let debugInfo = portablePdbReader.GetCustomDebugInformation handle

            if portablePdbReader.GetGuid debugInfo.Kind = sourceLinkGuid then
                Some debugInfo.Value
            else
                None
        )
        |> Seq.exactlyOne
        |> portablePdbReader.GetBlobBytes
        |> System.Text.Encoding.UTF8.GetString
        |> JsonSerializer.Deserialize<SourceLinkJsonDto>

    let parse (dto : SourceLinkJsonDto) : SourceLink =
        dto.Documents
        |> Map.toSeq
        |> Seq.map (fun (key, value) ->
            let asterisk = key.IndexOf '*'

            let key, isWild =
                if asterisk >= 0 then
                    if asterisk <> key.Length - 1 then
                        failwith $"failed validation on key %s{key} : requires exactly one * at the end"

                    key.Substring (0, key.Length - 1), true
                else
                    key, false

            let value =
                let asterisk = value.IndexOf '*'

                if isWild then
                    if asterisk < 0 then
                        failwith
                            $"failed validation on value %s{value} for key %s{key} : wildcard was present in key so must be present in value"

                    SourceLinkEntry.Wild (key, value.Substring (0, asterisk), value.Substring (asterisk + 1))
                else
                    if asterisk >= 0 then
                        failwith
                            $"failed validation on value %s{value} for key %s{key} : no wildcard present in key so no wildcard allowed in value"

                    SourceLinkEntry.Verbatim (key, value)

            value
        )
        |> Seq.toList
        |> SourceLink

    let remap (path : string) (SourceLink sourceLink) : string option =
        if path.Contains '*' then
            failwith $"cannot map path %s{path} which contains a wildcard - can't think why but there you go"

        sourceLink
        |> List.tryPick (fun entry ->
            match entry with
            | SourceLinkEntry.Wild (key, valueBeforeWild, valueAfterWild) ->
                if path.StartsWith (key, StringComparison.OrdinalIgnoreCase) then
                    // TODO there's some escaping required here apparently
                    valueBeforeWild + path.[key.Length ..].Replace ("\\", "/") + valueAfterWild
                    |> Some
                else
                    None
            | SourceLinkEntry.Verbatim (key, value) ->
                if String.Equals (key, path, StringComparison.OrdinalIgnoreCase) then
                    Some value
                else
                    None
        )

    let validate (sourceLink : SourceLink) (allDocuments : Document list) =
        let sourceLinked, notSourceLinked =
            allDocuments
            // where's my List.partitionChoice :(
            |> List.map (fun doc ->
                match remap doc.Name sourceLink with
                | Some value -> Choice1Of2 (doc, value)
                | None -> Choice2Of2 doc
            )
            |> List.partition (fun c ->
                match c with
                | Choice1Of2 _ -> true
                | Choice2Of2 _ -> false
            )

        let sourceLinked =
            sourceLinked
            |> List.map (fun c ->
                match c with
                | Choice1Of2 c -> c
                | _ -> failwith "logic error"
            )

        let notSourceLinked =
            notSourceLinked
            |> List.map (fun c ->
                match c with
                | Choice2Of2 c -> c
                | _ -> failwith "logic error"
            )

        // `/obj/` dirs are specifically usually wrong
        let sourceLinkedObj =
            sourceLinked
            |> List.filter (fun (doc, _) ->
                let normalised = doc.Name.Replace ('\\', '/')
                // If the source file is embedded, we can ignore the fact that SourceLink won't work
                not doc.IsEmbedded
                && normalised.Contains ("/obj/", StringComparison.OrdinalIgnoreCase)
            )

        use client = new HttpClient ()

        let fetchErrors =
            sourceLinked
            // If the source file is embedded, we can ignore the fact that SourceLink won't work
            // because we won't need to SourceLink to get it!
            |> Seq.filter (fun (doc, _) -> not doc.IsEmbedded)
            |> Seq.map (fun (doc, target) ->
                async {
                    let! ct = Async.CancellationToken
                    use message = new HttpRequestMessage (Method = HttpMethod.Get)
                    message.RequestUri <- Uri target
                    let! response = client.SendAsync (message, ct) |> Async.AwaitTask

                    if response.IsSuccessStatusCode then
                        return None
                    else
                        let! response = response.Content.ReadAsStringAsync (ct) |> Async.AwaitTask
                        return Some (doc, target, response)
                }
            )
            |> Async.Parallel
            |> Async.RunSynchronously
            |> Seq.choose id
            |> Seq.toList

        {
            LinkedButObj = sourceLinkedObj
            Linked = sourceLinked
            NotLinked = notSourceLinked
            LinkedToNonExistent = fetchErrors
        }
