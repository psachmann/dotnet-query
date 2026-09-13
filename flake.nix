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
          in
          pkgs.mkShell {
            packages = with pkgs; [
              nixd
              dotnet-sdk_10
              omnisharp-roslyn
              netcoredbg
            ] ++ avaloniaNativeLibs;
            DOTNET_ROOT = "${pkgs.dotnet-sdk_10}/share/dotnet";
            LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath avaloniaNativeLibs;
          };
      }
    );
}
