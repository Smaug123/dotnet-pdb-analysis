{
  description = "PDB analysis for .NET";

  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  };

  outputs = {
    self,
    nixpkgs,
    flake-utils,
    ...
  }:
    flake-utils.lib.eachDefaultSystem (system: let
      pkgs = nixpkgs.legacyPackages.${system};
      pname = "PdbAnalysis";
      dotnet-sdk = pkgs.dotnet-sdk_8;
      dotnet-runtime = pkgs.dotnetCorePackages.runtime_8_0;
      version = "0.1";
      dotnetTool = dllOverride: toolName: toolVersion: hash:
        pkgs.stdenvNoCC.mkDerivation rec {
          name = toolName;
          version = toolVersion;
          nativeBuildInputs = [pkgs.makeWrapper];
          src = pkgs.fetchNuGet {
            pname = name;
            version = version;
            hash = hash;
            installPhase = ''mkdir -p $out/bin && cp -r tools/net6.0/any/* $out/bin'';
          };
          installPhase = let
            dll =
              if isNull dllOverride
              then name
              else dllOverride;
          in ''
            runHook preInstall
            mkdir -p "$out/lib"
            cp -r ./bin/* "$out/lib"
            makeWrapper "${dotnet-runtime}/bin/dotnet" "$out/bin/${name}" --add-flags "$out/lib/${dll}.dll"
            runHook postInstall
          '';
        };
    in {
      packages = {
        fantomas = dotnetTool null "fantomas" (builtins.fromJSON (builtins.readFile ./.config/dotnet-tools.json)).tools.fantomas.version (builtins.head (builtins.filter (elem: elem.pname == "fantomas") ((import ./nix/deps.nix) {fetchNuGet = x: x;}))).hash;
        default = pkgs.buildDotnetModule {
          pname = pname;
          name = "PdbAnalysis";
          version = version;
          src = ./.;
          projectFile = "./PdbAnalysis.App/PdbAnalysis.App.fsproj";
          testProjectFile = "./PdbAnalysis.Test/PdbAnalysis.Test.fsproj";
          nugetDeps = ./nix/deps.nix; # `nix build .#default.passthru.fetch-deps && ./result` and put the result here
          doCheck = true;
          dotnet-sdk = dotnet-sdk;
          dotnet-runtime = dotnet-runtime;
        };
      };
      apps = {
        default = {
          type = "app";
          program = "${self.packages.${system}.default}/bin/PdbAnalysis.App";
        };
      };
      devShells = {
        default = pkgs.mkShell {
          buildInputs = with pkgs; [
            (with dotnetCorePackages;
              combinePackages [
                dotnet-sdk_8
                dotnetPackages.Nuget
              ])
          ];
          packages = [
            pkgs.alejandra
            pkgs.nodePackages.markdown-link-check
            pkgs.shellcheck
          ];
        };
      };
    });
}
