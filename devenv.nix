# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
{ pkgs, lib, ... }:

let
  runtimeLibs = import ./nix/runtime-libs.nix pkgs;
in
{
  # Packages available in the dev shell
  packages = with pkgs; [
    cmake
    ninja
    pkg-config
    gcc
    nodejs
    curl
  ] ++ runtimeLibs;

  # Node.js for Nota Remote web UI (src/web/remote)
  languages.javascript = {
    enable = true;
    package = pkgs.nodejs;
    npm.enable = true;
  };

  # .NET SDK 10
  languages.dotnet = {
    enable = true;
    package = pkgs.dotnetCorePackages.sdk_10_0;
  };

  languages.cplusplus = {
    enable = true;
  };

  # Set library search path for native engine and Avalonia dependencies
  env.LD_LIBRARY_PATH = lib.makeLibraryPath runtimeLibs;

  # Convenient development scripts
  scripts = {
    build-native.exec = ''
      if [ ! -f "src/native/nota.engine/build/build.ninja" ]; then
        cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build -DCMAKE_BUILD_TYPE=Release
      fi
      cmake --build src/native/nota.engine/build
    '';
    build-managed.exec = ''
      dotnet build Nota.sln -c Release --nologo
    '';
    build-all.exec = ''
      scripts/build-linux.sh
    '';
    run-nota.exec = ''
      build-native
      dotnet run --project src/managed/Nota.App -c Release
    '';
  };

  enterShell = ''
    echo "==========================================="
    echo "  Welcome to the Nota development shell!   "
    echo "==========================================="
    echo "Available helper commands:"
    echo "  build-native   - build the C++20 engine"
    echo "  build-managed  - build the .NET solution"
    echo "  build-all      - run full Linux build + smoke test"
    echo "  run-nota       - build engine & launch Nota"
    echo ""
  '';
}
