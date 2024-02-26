namespace NugetMetadata.App

open System
open System.IO
open System.IO.Compression
open System.Reflection.PortableExecutable
open NugetMetadata
open Microsoft.Extensions.Logging

module Program =
    let getDllsFromNupkg (file : FileInfo) =
        use stream = file.Open (FileMode.Open, FileAccess.Read)
        use archive = new ZipArchive (stream, ZipArchiveMode.Read)

        archive.Entries
        |> Seq.choose (fun entry ->
            if not (entry.Name.EndsWith (".dll", StringComparison.OrdinalIgnoreCase)) then
                None
            else

            use dllContents = entry.Open ()
            use target = new MemoryStream ()
            dllContents.CopyTo target
            (entry.FullName, target.ToArray ()) |> Some
        )
        |> Seq.toList

    let console : ILogger =
        let isEnabled (logLevel : LogLevel) = logLevel > LogLevel.Debug

        { new ILogger with
            member _.BeginScope _ =
                { new IDisposable with
                    member _.Dispose () = ()
                }

            member _.IsEnabled l = isEnabled l

            member _.Log (l, _, state, exc, f) =
                if isEnabled l then
                    f.Invoke (state, exc) |> Console.WriteLine
        }

    [<EntryPoint>]
    let main argv =
        let files =
            match argv with
            | [||] -> failwith "Usage: any number of args which are paths to the dlls/nupkgs to analyse"
            | filenames ->
                filenames
                |> Seq.collect (fun fileName ->
                    if fileName.EndsWith (".nupkg", StringComparison.OrdinalIgnoreCase) then
                        getDllsFromNupkg (FileInfo fileName)
                    else
                        [ fileName, File.ReadAllBytes fileName ]
                )
                |> Seq.toList

        let mutable exitCode = 0

        for fileName, fileContents in files do
            console.LogInformation ("==== Examining file {CurrentlyExamining} ====", fileName)
            use stream = new MemoryStream (fileContents)
            use reader = new PEReader (stream, PEStreamOptions.LeaveOpen)

            match MetadataReader.make reader with
            | None ->
                console.LogInformation ("Skipping {SkippedDll} because it has no embedded portable PDBs", fileName)
            | Some portablePdbReader ->

            let sourceLink = SourceLink.pullMetadata portablePdbReader |> SourceLink.parse

            let compilerFlags = CompilerFlags.read portablePdbReader

            let documents = MetadataReader.getDocuments console portablePdbReader

            let documentValidation = SourceLink.validate sourceLink documents

            for doc, target in documentValidation.Linked do
                console.LogDebug ("Source linked: {SourceDoc} to {SourceLinkTarget}", doc.Name, target)

            for doc in documentValidation.NotLinked do
                exitCode <- 1
                console.LogError ("Not source linked: {SourceDoc}", doc.Name)

            for doc, target in documentValidation.LinkedButObj do
                exitCode <- 2

                console.LogWarning (
                    "Source linked obj dir (dubious check but it's what NuGetPackageExplorer does): {SourceDoc} to {SourceLinkTarget}",
                    doc.Name,
                    target
                )

            for doc, target, error in documentValidation.LinkedToNonExistent do
                exitCode <- 3

                console.LogError (
                    "Source linked to non-existent target: {SourceDoc} to {SourceLinkTarget}, error response {FetchError}",
                    doc.Name,
                    target,
                    error
                )

            if List.isEmpty compilerFlags then
                exitCode <- 4
                console.LogError "No compiler flags found"

            match documents |> List.tryFind (fun x -> not (x.Name.StartsWith "/_")) with
            | None -> ()
            | Some doc ->
                exitCode <- 5
                console.LogError ("Nondeterministic document found: {NondeterministicDocument}", doc.Name)

        exitCode
