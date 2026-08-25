# Building Nota (dev)

See [`README.md`](README.md) for the short version and the packaging commands. This file
covers the details — per-platform prerequisites, manual step-by-step builds, and the
edition flags.

## Requirements (macOS)

- macOS 26+, Xcode 26+ (clang, CoreAudio SDK).
- CMake 3.24+ and Ninja — `brew install cmake ninja`.
- .NET SDK 10.
- Avalonia templates, only if you are scaffolding new projects —
  `dotnet new install Avalonia.Templates`.

## Quick start

```bash
scripts/build.sh          # free edition
scripts/build.sh pro      # pro edition
```

The script builds the native engine (universal arm64 + x86_64), builds the managed side,
and runs the smoke test.

## Running the app

```bash
dotnet run --project src/managed/Nota.App
```

## Layout

```
src/native/nota.engine/        # C++ engine + C ABI (libnota_engine.dylib)
  include/nota/nota_engine.h   # the public boundary (C ABI)
  src/                         # Engine, audio/MIDI backends, SPSC queue, DSP
  pluginhost/                  # JUCE-based VST3/AU hosting (the only JUCE-aware module)
src/managed/Nota.Domain/       # pure domain types
src/managed/Nota.Application/  # ports (interfaces) and use cases
src/managed/Nota.Infrastructure/  # P/Invoke interop, .nota persistence, services
src/managed/Nota.Presentation/ # view-models
src/managed/Nota.App/          # Avalonia application
src/managed/Nota.Mcp/          # MCP server
tests/Nota.SmokeTest/          # end-to-end check: C# -> C ABI -> engine -> audio device
```

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for how these fit together and the rules that
govern the boundary between them.

## Editions (dual-license, AR-10)

- Native: `-DNOTA_EDITION=free|pro` (CMake).
- Managed: `-p:NotaEdition=free|pro` → defines the `PRO` constant
  (`Directory.Build.props`).
- `nota_engine_edition()` returns the edition string, used as an end-to-end check.

Note that the **pro** edition cannot be distributed until a JUCE commercial license is
purchased — see [`LICENSES/README.md`](LICENSES/README.md).

## Building the parts by hand

```bash
# native
cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build \
  -DCMAKE_BUILD_TYPE=Release -DNOTA_EDITION=free
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

## Building on Windows (x64)

### Requirements

- Windows 10/11 x64 (the arm64 installer cross-builds from an x64 host).
- Visual Studio 2022 (Desktop C++ workload: MSVC v143 + Windows SDK); for ARM64 also the
  **"MSVC v143 - ARM64 build tools"** component.
- CMake 3.24+ and Ninja (both ship with the VS 2022 "C++ CMake tools", or
  `winget install Ninja-build.Ninja`).
- .NET SDK 10.
- For the installer, Inno Setup **6.3+** (`winget install JRSoftware.InnoSetup`).

### Quick start

Run from a **"Developer PowerShell for VS 2022"** so `cl` and `ninja` are on PATH:

```powershell
pwsh scripts/build-win.ps1                 # native (x64/WASAPI) + managed + smoke
pwsh scripts/package-win.ps1 free x64      # installer dist/Nota-Setup-<ver>-x64.exe
pwsh scripts/package-win.ps1 free arm64    # installer dist/Nota-Setup-<ver>-arm64.exe (cross-built)
```

`package-win.ps1` uses the VS generator (`-A x64|ARM64`), so both architectures build from
a single x64 host. An ARM64 build cannot be run on x64 — the smoke test is x64 only.

### Platform details

- Audio is **WASAPI** through vendored miniaudio (`vendor/miniaudio`); ASIO is not
  supported yet.
- MIDI input is **WinMM** through vendored RtMidi (`vendor/rtmidi`).
- Plugin hosting is **VST3** (AU is macOS-only).
- Settings, logs and projects are written to `%APPDATA%\Nota` (the counterpart of
  `~/Library/Application Support/Nota`).
- The engine builds as `nota_engine.dll`; the C ABI is exported via
  `__declspec(dllexport)`.

## Building on Linux

### Requirements

- `build-essential`, CMake 3.24+, Ninja, .NET SDK 10.
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
  macOS or Windows, run the build inside a Linux container — see the Docker commands in
  [`README.md`](README.md).
