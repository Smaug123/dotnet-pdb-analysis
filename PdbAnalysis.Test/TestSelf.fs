namespace PdbAnalysis.Test

open System
open System.IO
open System.Reflection.PortableExecutable
open NUnit.Framework
open NugetMetadata
open FsUnitTyped

[<TestFixture>]
module TestSelf =
    let pdbAnalysisDll = typeof<SourceLink>.Assembly.Location

    [<Test>]
    let ``Metadata for self`` () =
        let _messages, logger = Logger.makeTest ()
        let fileContents = File.ReadAllBytes pdbAnalysisDll
        use stream = new MemoryStream (fileContents)
        use reader = new PEReader (stream, PEStreamOptions.LeaveOpen)

        let reader =
            match MetadataReader.make reader with
            | None -> failwith $"Failed to find embedded PDBs in '%s{pdbAnalysisDll}'"
            | Some reader -> reader

        let sourceLink = SourceLink.pullMetadata reader |> SourceLink.parse

        // compiler flags are not supported by F# (https://github.com/dotnet/fsharp/issues/12002)
        let _compilerFlags = CompilerFlags.read reader

        let documents = MetadataReader.getDocuments logger reader

        let documentValidation = SourceLink.validate sourceLink documents

        documentValidation.NotLinked |> shouldBeEmpty

        documentValidation.Linked
        |> List.map (fun (doc, _) -> FileInfo(doc.Name).Name)
        |> List.filter (fun s ->
            not (s.EndsWith (".AssemblyAttributes.fs", StringComparison.Ordinal))
            && not (s.EndsWith (".AssemblyInfo.fs", StringComparison.Ordinal))
        )
        |> List.sort
        |> shouldEqual [ "CompilerFlags.fs" ; "Domain.fs" ; "MetadataReader.fs" ; "SourceLink.fs" ]
