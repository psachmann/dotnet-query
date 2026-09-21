{
  description = "DotNet dependencies for the project";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs?ref=nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs =
    { nixpkgs, flake-utils, ... }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = import nixpkgs {
          inherit system;
        };
      in
      {
        devShells.default =
          let
            avaloniaNativeLibs = with pkgs; [
              fontconfig
              freetype
              icu
              libGL
              libGLU
              libX11
              libICE
              libSM
              libXi
              libXcursor
              libXrandr
              libXext
              libXrender
              libXtst
              libXfixes
            ];
            # Every src/ package multi-targets net9.0 and net10.0 (Directory.Build.props at the
            # repo root), and the tests do too (tests/Directory.Build.props), so `dotnet test`
            # needs the net9.0 runtime side-by-side with the net10.0 SDK — a bare dotnet-sdk_10
            # can build the net9.0 test binaries but not launch them.
            dotnet = pkgs.dotnetCorePackages.combinePackages [
              pkgs.dotnetCorePackages.sdk_10_0
              pkgs.dotnetCorePackages.runtime_9_0
              pkgs.dotnetCorePackages.aspnetcore_9_0
            ];
          in
          pkgs.mkShell {
            packages = with pkgs; [
              nixd
              dotnet
              omnisharp-roslyn
              netcoredbg
            ] ++ avaloniaNativeLibs;
            DOTNET_ROOT = "${dotnet}/share/dotnet";
            LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath avaloniaNativeLibs;
          };
      }
    );
}
