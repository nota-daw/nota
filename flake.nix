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

        nixDeps = builtins.fromJSON (builtins.readFile ./nix/nix-deps.json);

        cpmCmake = pkgs.fetchurl {
          url = "https://github.com/cpm-cmake/CPM.cmake/releases/download/${nixDeps.cpm.version}/CPM.cmake";
          sha256 = nixDeps.cpm.sha256;
        };

        cmakeDeps = builtins.mapAttrs (_: spec: pkgs.fetchzip spec) nixDeps.cmakeDeps;

        cmakeFetchContentFlags = pkgs.lib.escapeShellArgs (
          [ "-DFETCHCONTENT_FULLY_DISCONNECTED=ON" ]
          ++ pkgs.lib.mapAttrsToList (name: dep: "-DCPM_${name}_SOURCE=${dep}") cmakeDeps
        );

        remoteNpmDeps = pkgs.fetchNpmDeps {
          name = "nota-remote-npm-deps";
          src = ./src/web/remote;
          hash = nixDeps.npmHash;
        };

        nugetDeps = pkgs.mkNugetDeps {
          name = "nota-nuget-deps";
          sourceFile = ./nix/nuget-deps.json;
          installable = true;
        };

        notaPackage = pkgs.stdenv.mkDerivation {
          pname = "nota";
          version = pkgs.lib.trim (builtins.readFile ./VERSION);
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
            cmake --build src/native/nota.engine/build --parallel "$NIX_BUILD_CORES"

            # 2. Restore and publish Avalonia UI offline (into the build tree; installPhase
            #    copies it to $out). The publish copies libnota_engine.so and nota-scanworker
            #    from the engine build itself (Nota.App.csproj, NotaNativeDir).
            dotnet restore src/managed/Nota.App/Nota.App.csproj \
              --source ${nugetDeps}/share/nuget/source
            dotnet publish src/managed/Nota.App/Nota.App.csproj \
              -c Release \
              --no-restore \
              -o "$TMPDIR/publish"

            runHook postBuild
          '';

          installPhase = ''
            runHook preInstall

            mkdir -p $out/bin $out/lib/nota
            cp -r "$TMPDIR/publish/." $out/lib/nota/

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

        # `nix run github:nota-daw/nota`: the package above, built once and cached (#596).
        apps.default = flake-utils.lib.mkApp {
          drv = notaPackage;
        };
        # `nix run .#dev` in a checkout: builds the working tree and runs it (developers).
        apps.dev = flake-utils.lib.mkApp {
          drv = runScript;
        };

        # The same shell as `devenv shell` (devenv.nix). devenv keeps its state under the
        # project root: the working directory with `nix develop --impure`, else a temp dir,
        # so the flake still evaluates purely (`nix flake show` / `check`).
        devShells.default = devenv.lib.mkShell {
          inherit inputs pkgs;
          modules = [
            ./devenv.nix
            {
              devenv.root =
                let pwd = builtins.getEnv "PWD";
                in if pwd != "" then pwd else "/tmp/nota-devenv";
            }
          ];
        };
      });
}
