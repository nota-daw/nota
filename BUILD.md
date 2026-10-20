# Building Nota (dev)

Everything for building Nota from source: per-platform prerequisites, the build scripts,
manual step-by-step builds, and packaging the installers and AppImages. Just want to use
Nota? Grab a ready build from [Releases](https://github.com/nota-daw/nota/releases/latest).

## Requirements (macOS)

- macOS 26+, Xcode 26+ (clang, CoreAudio SDK).
- CMake 3.24+ and Ninja — `brew install cmake ninja`.
- .NET SDK 10.
- Node.js 22 (or 20.19+) — `brew install node`. The build uses it to build the Nota Remote
  phone page (`src/web/remote`) into `src/managed/Nota.Remote/wwwroot`, which is build output
  and not committed. To build without Node, pass `-p:NotaSkipWebBuild=true`; the phone then
  gets a "not built" page.
- Avalonia templates, only if you are scaffolding new projects —
  `dotnet new install Avalonia.Templates`.

## Source dependencies (CPM)

All native third-party code (JUCE included) is fetched by CPM into the build
dir — nothing is vendored or submoduled, so a plain `git clone` is enough.
See "CMake dependencies (CPM)" below for the cache story.

## CMake dependencies (CPM)

Small header-only libs are fetched by [CPM](https://github.com/cpm-cmake/CPM.cmake) into the build dir (`_deps`)
from pinned URLs — never vendored. A fresh configure needs network. To reuse downloads
across builds, export `CPM_SOURCE_CACHE=~/.cache/CPM`; for an air-gapped build, override
a package with `-DFETCHCONTENT_SOURCE_DIR_<NAME>=<dir>`.

## Quick start

```bash
scripts/build.sh
```

The script builds the native engine (universal arm64 + x86_64), builds the managed side,
and runs the smoke test.

## Running the app

```bash
dotnet run --project src/managed/Nota.App
```

## Stress test

`scripts/stress-test.py` measures how much DSP headroom the engine has as a project
grows: it ramps from 1 to 80 tracks — each with the Volt instrument plus EQ-8,
Compressor and Auto Filter — recording DSP load and process CPU at every step while
the transport plays. Results accumulate in `scripts/stress-test-results.csv`.

It drives the app through MCP, so Nota must be running first with the MCP server
enabled (Preferences → Appearance → AI control; `127.0.0.1:3900`):

```bash
dotnet run --project src/managed/Nota.App
python3 scripts/stress-test.py
```

The script uses only the Python standard library. Each step settles for a few seconds
before measuring, so a full run takes several minutes.

## Layout

```
src/native/nota.engine/        # C++ engine + C ABI (libnota_engine.dylib)
  include/nota/nota_engine.h   # the public boundary (C ABI)
  src/                         # Engine core (Engine*.cpp), abi/, media/, audio_io/
    devices/                   # base interfaces + effects/ instruments/ midi_fx/ racks/
    engine/                    # Graph/Track/Transport/… + AudioThreadPool (.h/.cpp pairs)
    dsp/core,math,primitives,poly/  # shared DSP building blocks
  pluginhost/                  # JUCE-based VST3/AU hosting (the only JUCE-aware module)
    hosting/                   # play-head, editors, plugin devices (in nota_pluginhost)
    scanner/                   # catalog, out-of-process scan (nota_pluginscanner INTERFACE)
    scanner/worker/            # nota-scanworker sources
    bridge/                    # the two bridge headers
src/managed/Nota.Domain/       # pure domain types
src/managed/Nota.Application/  # ports (interfaces) and use cases
src/managed/Nota.Infrastructure/  # P/Invoke interop, .nota persistence, services
src/managed/Nota.Presentation/ # view-models
src/managed/Nota.App/          # Avalonia application
src/managed/Nota.Mcp/          # MCP server
tests/Nota.SmokeTest/          # end-to-end check: C# -> C ABI -> engine -> audio device
```

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for how these fit together and the rules that
govern the boundary between them. Native convention: every device, engine model
class, and DSP block is a `.h`/`.cpp` pair (declaration header + implementation
TU, AGPL SPDX header on each), so editing one `.cpp` recompiles one TU.
`nota_pluginscanner` is an INTERFACE target whose translation units compile
inside `nota_pluginhost` — a second STATIC JUCE library would duplicate ~11k
symbols at link time.

## Building the parts by hand

```bash
# native
cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build \
  -DCMAKE_BUILD_TYPE=Release
cmake --build src/native/nota.engine/build

# managed
dotnet build Nota.sln -c Release
```

The managed projects copy the right native artifact for the platform out of the CMake
build directory and place it next to the binary: `libnota_engine.dylib` +
`nota-scanworker` (macOS), `nota_engine.dll` + `nota-scanworker.exe` (Windows), or
`libnota_engine.so` + `nota-scanworker` (Linux). Override the paths with
`-p:NotaEngineLib=...` / `-p:NotaScanWorker=...`.

On memory-constrained machines and containers, cap parallelism so the large JUCE
translation units are not OOM-killed: `export CMAKE_BUILD_PARALLEL_LEVEL=2`.

### Nota Pentad as a standalone VST3 (optional)

For checking the Pentad synth in another host, the same DSP (`src/devices/instruments/Pentad.h`/`.cpp`) also builds
as a VST3 with JUCE's generic editor. It is off by default and not part of the app:

```bash
cmake -G Ninja -S src/native/nota.engine -B build-pentad -DCMAKE_BUILD_TYPE=Release \
  -DNOTA_BUILD_PENTAD_VST3=ON
cmake --build build-pentad --target NotaPentad_VST3
# → build-pentad/NotaPentad_artefacts/Release/VST3/Nota Pentad.vst3
```

## Building on Windows (x64)

### Requirements

- Windows 10/11 x64 (the arm64 installer cross-builds from an x64 host).
- Visual Studio 2022 (Desktop C++ workload: MSVC v143 + Windows SDK); for ARM64 also the
  **"MSVC v143 - ARM64 build tools"** component.
- CMake 3.24+ and Ninja (both ship with the VS 2022 "C++ CMake tools", or
  `winget install Ninja-build.Ninja`).
- .NET SDK 10.
- Node.js 22 (or 20.19+), for the Nota Remote phone page — see the macOS requirements.
- For the installer, Inno Setup **6.3+** (`winget install JRSoftware.InnoSetup`).
- PowerShell 7.3+ for running build scripts.

### Quick start

Run from a **"Developer PowerShell for VS 2022"** so `cl` and `ninja` are on PATH:

```powershell
pwsh scripts/build-win.ps1            # native (x64/WASAPI) + managed + smoke
pwsh scripts/package-win.ps1 x64      # installer dist/Nota-Setup-<ver>-x64.exe
pwsh scripts/package-win.ps1 arm64    # installer dist/Nota-Setup-<ver>-arm64.exe (cross-built)
```

`package-win.ps1` uses the VS generator (`-A x64|ARM64`), so both architectures build from
a single x64 host. An ARM64 build cannot be run on x64 — the smoke test is x64 only.

### Platform details

- Audio is **WASAPI** through miniaudio (fetched by CPM); ASIO is not
  supported yet.
- MIDI input is **WinMM** through RtMidi (fetched by CPM).
- Plugin hosting is **VST3** (AU is macOS-only).
- Settings, logs and projects are written to `%APPDATA%\Nota` (the counterpart of
  `~/Library/Application Support/Nota`).
- The engine builds as `nota_engine.dll`; the C ABI is exported via
  `__declspec(dllexport)`.

## Building on Linux

### Requirements

- `build-essential`, CMake 3.24+, Ninja, .NET SDK 10.
- Node.js 22 (or 20.19+), for the Nota Remote phone page — see the macOS requirements.
- Native dev packages:

```bash
sudo apt install build-essential cmake ninja-build libasound2-dev libx11-dev \
  libxext-dev libxrandr-dev libxinerama-dev libxcursor-dev libfreetype6-dev \
  libfontconfig1-dev
```

### Quick start

```bash
scripts/build-linux.sh    # native (PulseAudio/ALSA + RtMidi/ALSA) + managed + smoke
scripts/package-linux.sh  # portable AppImage in dist/
```

### Platform details

- Audio is **PulseAudio/ALSA** through miniaudio, which `dlopen`s them at runtime — so
  `libpulse` and `libasound` are not needed at link time, only `libasound2-dev` for
  RtMidi.
- JUCE (plugin hosting) is what pulls in the X11, freetype and fontconfig libraries.
- The AppImage is built for the host architecture. To produce a Linux build from
  macOS or Windows, run the build inside a Linux container — see
  [Building a Linux AppImage from macOS/Windows](#building-a-linux-appimage-from-macoswindows).

## Packaging (installers and AppImages)

All scripts write to `dist/`.

**macOS** — `.app` and one `.dmg` per arch (an Apple Silicon host builds both):
```bash
scripts/bundle-mac.sh              # -> /Applications/Nota.app (ad-hoc signed)
scripts/package-dmg.sh arm64       # -> dist/Nota-<version>-arm64.dmg
scripts/package-dmg.sh x86_64      # -> dist/Nota-<version>-x86_64.dmg
```
The installer window's background comes from `assets/macos/dmg-background.png`
(660×400) and its `@2x` (1320×800); both are required.

**Windows** — Inno Setup installer (x64 / arm64; one x64 host cross-builds both):
```powershell
pwsh scripts/package-win.ps1 x64     # -> dist/Nota-Setup-<version>-x64.exe
pwsh scripts/package-win.ps1 arm64   # -> dist/Nota-Setup-<version>-arm64.exe
```

**Linux** — portable AppImage (built for the host arch; `appimagetool` auto-downloaded):
```bash
scripts/package-linux.sh       # -> dist/Nota-<version>-<arch>.AppImage
```

### Building a Linux AppImage from macOS/Windows

The native `.so` can't be cross-compiled off Linux, so build it inside a Linux
container. The AppImage is built for the container's architecture: on Apple Silicon,
Docker runs an **arm64** container by default, so `--platform linux/amd64` is what
forces an **x86_64** build (via QEMU emulation — correct but noticeably slower).

Keep each command on one line when copying — line-continuation backslashes get
dropped by some terminals and break the `apt-get` package list.

**x86_64 (linux-x64)** → `dist/Nota-<version>-x86_64.AppImage`:

```bash
docker run --rm --platform linux/amd64 -e CMAKE_BUILD_PARALLEL_LEVEL=2 -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -c 'apt-get update -qq && apt-get install -y --no-install-recommends curl ca-certificates gnupg && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && apt-get install -y --no-install-recommends nodejs cmake ninja-build build-essential curl ca-certificates file squashfs-tools libasound2-dev libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxcursor-dev libfreetype6-dev libfontconfig1-dev && scripts/package-linux.sh'
```

**arm64 (linux-arm64)** → `dist/Nota-<version>-aarch64.AppImage` (drop `--platform`; native on Apple Silicon, fast):

```bash
docker run --rm -e CMAKE_BUILD_PARALLEL_LEVEL=2 -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -c 'apt-get update -qq && apt-get install -y --no-install-recommends curl ca-certificates gnupg && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && apt-get install -y --no-install-recommends nodejs cmake ninja-build build-essential curl ca-certificates file squashfs-tools libasound2-dev libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxcursor-dev libfreetype6-dev libfontconfig1-dev && scripts/package-linux.sh'
```
