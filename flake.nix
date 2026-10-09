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
            # Everything targets net10.0 only (Directory.Build.props at the repo root), so the
            # SDK's bundled runtimes are all `dotnet test` needs — no side-by-side runtime.
            dotnet = pkgs.dotnetCorePackages.sdk_10_0;
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
