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

        runtimeLibs = import ./nix/runtime-libs.nix pkgs;

        cpmCmake = pkgs.fetchurl {
          url = "https://github.com/cpm-cmake/CPM.cmake/releases/download/v0.43.2/CPM.cmake";
          sha256 = "49a3bef91ceb65bb66d57255e12d1ffd22abc2f6408fa9fe4534c544a2f232aa";
        };

        cmakeDeps = {
          nlohmann_json = pkgs.fetchzip {
            url = "https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz";
            hash = "sha256-tOInM4YYi/cbHCWP2R0jlFzlaTN0ZVgHFMWagz9Wnvk=";
          };
          dr_libs = pkgs.fetchzip {
            url = "https://github.com/mackron/dr_libs/archive/cd99e2cca5ccb48f8d000f1cd844424dcdbbaf59.tar.gz";
            hash = "sha256-MbeC6gGz68bto0MK0xFb9iQ65w8uN7Vm7ZxYZj7IT+8=";
          };
          miniaudio = pkgs.fetchzip {
            url = "https://github.com/mackron/miniaudio/archive/refs/tags/0.11.25.tar.gz";
            hash = "sha256-2k346Z/ueINPbaY20P2cbBvRfFXXH0ugdv4d7WaYt2w=";
          };
          signalsmith_linear = pkgs.fetchzip {
            url = "https://github.com/Signalsmith-Audio/linear/archive/7f53cdd1ccd52b409dacf2af24e7ff838c5580cd.tar.gz";
            hash = "sha256-3IwQgT4LlCKryKyxr1w8CzdpNSdzU8DIMEsfqAZ8ZGI=";
          };
          signalsmith_stretch = pkgs.fetchzip {
            url = "https://github.com/Signalsmith-Audio/signalsmith-stretch/archive/57b93f4e9206a089a45387eaa39bdc9f310d3308.tar.gz";
            hash = "sha256-UFZsof6eclOlLqYzq4oK40waMGBEFmZAzMm67tL9pxs=";
          };
          hiir = pkgs.fetchzip {
            url = "https://ldesoras.fr/src/hiir-1.50.zip";
            stripRoot = false;
            hash = "sha256-ML5xnmwmJlp7K5nxH3iiB8E17kt+SIXukFTKAwdqxfc=";
          };
          flac = pkgs.fetchzip {
            url = "https://github.com/xiph/flac/archive/refs/tags/1.5.0.tar.gz";
            hash = "sha256-B6XRai5UOAtY/7JXNbI3YuBgazi1Xd2ZOs6vvLq9LIs=";
          };
          rtmidi = pkgs.fetchzip {
            url = "https://github.com/thestk/rtmidi/archive/518f8f1168afe745ea9660e11eeee044e74eeaa1.tar.gz";
            hash = "sha256-4AXjOvSGSeM7m/Pc62a0Bsr2To4ljAekCZ5R398cAKg=";
          };
          lame = pkgs.fetchzip {
            url = "https://sourceforge.net/projects/lame/files/lame/3.100/lame-3.100.tar.gz/download";
            extension = "tar.gz";
            hash = "sha256-bBJ8Ox4pbXujRQhS40sDhmtbk09H419PDDN1VFHKhoM=";
          };
          JUCE = pkgs.fetchzip {
            url = "https://github.com/juce-framework/JUCE/archive/refs/tags/8.0.14.tar.gz";
            hash = "sha256-oXFYfFqySW2XtM1e5/ifxAp9qSIVLzY1WAUKE3EscoQ=";
          };
        };

        cmakeFetchContentFlags = pkgs.lib.escapeShellArgs (
          [ "-DFETCHCONTENT_FULLY_DISCONNECTED=ON" ]
          ++ pkgs.lib.mapAttrsToList (name: dep: "-DCPM_${name}_SOURCE=${dep}") cmakeDeps
        );

        remoteNpmDeps = pkgs.fetchNpmDeps {
          name = "nota-remote-npm-deps";
          src = ./src/web/remote;
          hash = "sha256-7409zkTqYl+fPWxq68KMIxnTy7YFwUpcmeo13mTqSyY=";
        };

        nugetDeps = pkgs.mkNugetDeps {
          name = "nota-nuget-deps";
          sourceFile = ./nix/nuget-deps.json;
          installable = true;
        };

        notaPackage = pkgs.stdenv.mkDerivation {
          pname = "nota";
          version = "git";
          src = ./.;

          npmDeps = remoteNpmDeps;
          npmRoot = "src/web/remote";

          nativeBuildInputs = with pkgs; [
            cmake
            ninja
            pkg-config
            dotnetSdk
            nodejs
            npmHooks.npmConfigHook
            curl
            makeWrapper
          ];

          buildInputs = runtimeLibs;

          dontUseCmakeConfigure = true;

          buildPhase = ''
            runHook preBuild

            export HOME="$TMPDIR"
            export DOTNET_CLI_HOME="$TMPDIR/dotnet"
            export NUGET_PACKAGES="$TMPDIR/nuget-packages"
            export DOTNET_NOLOGO=1
            export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

            # 1. Build C++20 core engine with pre-fetched CPM.cmake and dependencies
            mkdir -p src/native/nota.engine/build/_cpm
            cp ${cpmCmake} src/native/nota.engine/build/_cpm/CPM.cmake
            cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build \
              -DCMAKE_BUILD_TYPE=Release \
              ${cmakeFetchContentFlags}
            cmake --build src/native/nota.engine/build

            # 2. Restore and publish Avalonia UI offline
            dotnet restore src/managed/Nota.App/Nota.App.csproj \
              --source ${nugetDeps}/share/nuget/source
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

          echo "==> Building native engine..."
          if [ ! -f "$NATIVE/build.ninja" ]; then
            cmake -G Ninja -S "$WORK_DIR/src/native/nota.engine" -B "$NATIVE" -DCMAKE_BUILD_TYPE=Release
          fi
          cmake --build "$NATIVE"

          echo "==> Launching Nota..."
          exec ${dotnetSdk}/bin/dotnet run --project "$WORK_DIR/src/managed/Nota.App" -c Release -- "$@"
        '';
      in
      {
        packages.default = notaPackage;

        apps.default = flake-utils.lib.mkApp {
          drv = runScript;
        };

        devShells.default = devenv.lib.mkShell {
          inherit inputs pkgs;
          modules = [ ./devenv.nix ];
        };
      });
}
