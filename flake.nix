# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
{
  description = "Nota - Modern open-source Digital Audio Workstation";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
    devenv = {
      url = "github:cachix/devenv";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs = { self, nixpkgs, flake-utils, devenv, ... } @ inputs:
    flake-utils.lib.eachSystem [ "x86_64-linux" "aarch64-linux" ] (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
        dotnetSdk = pkgs.dotnetCorePackages.sdk_10_0;
        dotnetRuntime = pkgs.dotnetCorePackages.aspnetcore_10_0;

        runtimeLibs = with pkgs; [
          alsa-lib
          libGL
          libxkbcommon
          fontconfig
          freetype
          icu
          openssl
          libx11
          libxcursor
          libxext
          libxi
          libxinerama
          libxrandr
          libxrender
          libxfixes
          libice
          libsm
          xorgproto
          zlib
        ];

        notaPackage = pkgs.stdenv.mkDerivation {
          pname = "nota";
          version = "git";
          src = ./.;

          nativeBuildInputs = with pkgs; [
            cmake
            ninja
            pkg-config
            dotnetSdk
            nodejs
            curl
            makeWrapper
          ];

          buildInputs = runtimeLibs;

          dontUseCmakeConfigure = true;

          buildPhase = ''
            runHook preBuild

            export DOTNET_CLI_HOME="$TMPDIR/dotnet"
            export DOTNET_NOLOGO=1
            export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

            # 1. Build C++20 core engine
            cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build -DCMAKE_BUILD_TYPE=Release
            cmake --build src/native/nota.engine/build

            # 2. Publish Avalonia UI
            dotnet publish src/managed/Nota.App/Nota.App.csproj \
              -c Release \
              --no-restore \
              -o $out/lib/nota

            runHook postBuild
          '';

          installPhase = ''
            runHook preInstall

            mkdir -p $out/bin $out/lib/nota
            find src/native/nota.engine/build -name "*.so" -exec cp {} $out/lib/nota/ \;
            if [ -f src/native/nota.engine/build/nota-scanworker ]; then
              cp src/native/nota.engine/build/nota-scanworker $out/lib/nota/
            fi

            makeWrapper ${dotnetRuntime}/bin/dotnet $out/bin/nota \
              --add-flags "$out/lib/nota/Nota.App.dll" \
              --prefix LD_LIBRARY_PATH : "${pkgs.lib.makeLibraryPath runtimeLibs}:$out/lib/nota"

            runHook postInstall
          '';

          meta = with pkgs.lib; {
            description = "Open-source Digital Audio Workstation";
            homepage = "https://github.com/nota-daw/nota";
            license = licenses.agpl3Only;
            platforms = [ "x86_64-linux" "aarch64-linux" ];
            mainProgram = "nota";
          };
        };

        runScript = pkgs.writeShellScriptBin "nota-run" ''
          set -euo pipefail

          if [ -f "$PWD/src/native/nota.engine/CMakeLists.txt" ]; then
            WORK_DIR="$PWD"
          else
            CACHE_DIR="''${XDG_CACHE_HOME:-$HOME/.cache}/nota-nix"
            WORK_DIR="$CACHE_DIR/src"
            SRC_STAMP="$CACHE_DIR/.src-stamp"
            CURRENT_HASH="${self.narHash or "default"}"

            if [ ! -f "$SRC_STAMP" ] || [ "$(cat "$SRC_STAMP" 2>/dev/null)" != "$CURRENT_HASH" ]; then
              echo "==> Syncing Nota source to $WORK_DIR..."
              mkdir -p "$CACHE_DIR"
              rm -rf "$WORK_DIR"
              mkdir -p "$WORK_DIR"
              cp -r "${self}/." "$WORK_DIR/"
              chmod -R u+w "$WORK_DIR"
              echo "$CURRENT_HASH" > "$SRC_STAMP"
            fi
          fi

          NATIVE="$WORK_DIR/src/native/nota.engine/build"

          export PATH="${pkgs.cmake}/bin:${pkgs.ninja}/bin:${pkgs.gcc}/bin:${pkgs.pkg-config}/bin:${pkgs.nodejs}/bin:${pkgs.curl}/bin:$NATIVE:$PATH"
          export CC="${pkgs.gcc}/bin/gcc"
          export CXX="${pkgs.gcc}/bin/g++"
          export C_INCLUDE_PATH="${pkgs.lib.makeSearchPathOutput "dev" "include" (runtimeLibs ++ [ pkgs.curl ])}"
          export CPLUS_INCLUDE_PATH="$C_INCLUDE_PATH"
          export LIBRARY_PATH="${pkgs.lib.makeLibraryPath runtimeLibs}"
          export PKG_CONFIG_PATH="${pkgs.lib.makeSearchPathOutput "dev" "lib/pkgconfig" (runtimeLibs ++ [ pkgs.curl ])}"
          export LD_LIBRARY_PATH="$NATIVE:${pkgs.lib.makeLibraryPath runtimeLibs}:''${LD_LIBRARY_PATH:-}"

          if [ ! -f "$NATIVE/libnota_engine.so" ]; then
            echo "==> Building native engine..."
            cmake -G Ninja -S "$WORK_DIR/src/native/nota.engine" -B "$NATIVE" -DCMAKE_BUILD_TYPE=Release
            ninja -C "$NATIVE"
          fi

          echo "==> Launching Nota..."
          exec ${dotnetSdk}/bin/dotnet run --project "$WORK_DIR/src/managed/Nota.App" -c Release -- "$@"
        '';
      in
      {
        packages.default = notaPackage;

        apps.default = flake-utils.lib.mkApp {
          drv = runScript;
        };

        devShells.default = pkgs.mkShell {
          nativeBuildInputs = with pkgs; [
            cmake
            ninja
            pkg-config
            dotnetSdk
            nodejs
            curl
            gcc
          ];
          buildInputs = runtimeLibs;
          LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath runtimeLibs;
          CI = "1";

          shellHook = ''
            export CI=1
            echo "Nota development environment loaded."
            echo "Run 'scripts/build-linux.sh' to build and run smoke tests."
          '';
        };

        devShells.devenv = devenv.lib.mkShell {
          inherit inputs pkgs;
          modules = [
            {
              devenv.root = "${./.}";
            }
            ./devenv.nix
          ];
        };
      });
}
