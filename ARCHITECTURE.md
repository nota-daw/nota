<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Architecture

Nota is a cross-platform DAW split into two halves that meet at a single, narrow
boundary:

- a **portable C++20 audio engine** that owns all audio, MIDI, and DSP;
- a **.NET / Avalonia application** that owns the UI, project files, and everything
  a user interacts with.

They communicate over a **pure C ABI** — nothing else. That constraint is what keeps the
engine embeddable and the UI replaceable.

```
+--------------------- managed (.NET 10, C#) -------------------------+
|  Nota.App             Avalonia views, windows, input, device cards  |
|  Nota.Presentation    view-models                                   |
|  Nota.Mcp             MCP server (AI drives the app)                |
|  Nota.Infrastructure  P/Invoke interop, .nota persistence, services |
|  Nota.Application     ports (interfaces), use cases                 |
|  Nota.Domain          pure domain types                             |
+-------------------------------+-------------------------------------+
                                |  C ABI  (nota/nota_engine.h)
+-------------------------------+-------------------------------------+
|  nota.engine (C++20)                                                |
|    Engine, Transport, Graph, Track, devices, instruments            |
|    audio backends - MIDI backends - warp / stretch                  |
|    pluginhost (JUCE - VST3 / AU)  <- the ONLY JUCE-aware module     |
+---------------------------------------------------------------------+
```

## Repository layout

| Path | What lives there |
|---|---|
| `src/managed/Nota.Domain` | Pure domain types. No dependencies. |
| `src/managed/Nota.Application` | Ports (interfaces) and use cases. Depends only on Domain. |
| `src/managed/Nota.Infrastructure` | P/Invoke interop (`Interop/`), `.nota` project persistence, device services. |
| `src/managed/Nota.Presentation` | View-models. |
| `src/managed/Nota.App` | Avalonia views, windows, device cards, input handling. Composition root. |
| `src/managed/Nota.Mcp` | MCP server so an AI agent can drive the app. Off unless enabled in Preferences. |
| `src/native/nota.engine/include/nota` | The public C ABI header — the whole contract. |
| `src/native/nota.engine/src` | Engine core, DSP, devices, instruments, backends. JUCE-free. |
| `src/native/nota.engine/pluginhost` | JUCE-based VST3/AU hosting, built as a separate static library. |
| `src/native/nota.engine/vendor` | Vendored third-party sources (see `LICENSES/third-party.md`). |
| `tests/Nota.SmokeTest` | End-to-end smoke test driven through the C ABI. |
| `scripts` | Per-platform build and packaging scripts. |

Managed dependency direction is strictly inward:
`App -> Presentation -> Application -> Domain`, with `Infrastructure` implementing
Application's ports. Nothing outside `Nota.Infrastructure/Interop` may touch the engine.

## Architecture rules (AR-*)

Source comments cite these rule IDs. They are load-bearing — read them before changing
engine internals.

| Rule | Statement |
|---|---|
| **AR-4** | Message-to-audio-thread communication goes through a **lock-free SPSC ring buffer** (`CommandQueue.h`). Commands are plain PODs written into a pre-allocated ring and drained at the top of each audio block. |
| **AR-5** | The audio graph is an **immutable snapshot** (`Graph.h`). Structural edits happen on the message thread and *publish a new graph*; the audio thread reads the current snapshot once per block. This is also what makes undo/redo cheap. |
| **AR-6** | The audio thread is **real-time safe**: no allocation, no locks, no I/O, ever. This applies to every DSP class and every backend callback. |
| **AR-7** | The managed/native boundary is a **pure C ABI**: `extern "C"`, opaque handles as pointers, UTF-8 strings, plain structs, integer result codes. No C++ types and no exceptions cross the line. |
| **AR-8** | MIDI is resolved to **sample-accurate events per block**. Clips are immutable once scheduled. |
| **AR-9** | `Transport` is the **single source of musical time**. Nothing else derives tempo or position independently. |

## The C ABI boundary

`src/native/nota.engine/include/nota/nota_engine.h` is the entire contract. The managed
side reaches it only through `Nota.Infrastructure/Interop`. The ABI is sliced across
several translation units by concern: `nota_engine_core.cpp`, `_audio`, `_track`,
`_device`, `_rack`, `_session`, `_automation`, `_modulation`.

Symbol export is deliberately tight. On macOS and Linux only the `nota_*` C ABI is
exported (via `nota_exports.map` / `nota_exports.txt`). This is not cosmetic: with JUCE's
weak symbols visible, the dynamic loader can coalesce our JUCE against a hosted plugin's
own JUCE, so the plugin allocates with its JUCE and frees with ours — a heap corruption
that crashes when a plugin builds its editor.

## Audio engine

`Engine` owns the transport, an immutable graph of audio and instrument tracks, the
mixer, sends and return buses, the metronome, file playback, recording, and
sample-accurate MIDI. Its implementation is split by concern across
`Engine_Tracks.cpp`, `Engine_Render.cpp`, `Engine_Session.cpp`, `Engine_Rack.cpp`,
`Engine_Devices.cpp`, `Engine_Automation.cpp`, `Engine_Modulation.cpp`,
`Engine_Recording.cpp`.

Device I/O sits behind `AudioBackend` so the platform layer stays swappable:

| Platform | Audio | MIDI |
|---|---|---|
| macOS | CoreAudio (`CoreAudioBackend.mm`) | CoreMIDI |
| Windows | WASAPI via miniaudio | WinMM via RtMidi |
| Linux | PulseAudio / ALSA via miniaudio | ALSA via RtMidi |

miniaudio runtime-links PulseAudio/ALSA with `dlopen`, so those libraries are not needed
at link time.

### Warping

Warped clips are **not** stretched on the audio thread. When a clip is warped, its played
window is rendered **once, offline** into a device-rate buffer stretched to the target
musical length (Signalsmith Stretch); the audio thread then just copies from that cache.
Changing tempo rebuilds the caches. See `Warp.h`, `WarpStream.cpp`, `Engine_Tracks.cpp`.

Resize semantics differ by clip type: a warped clip **trims** its played window, while an
unwarped clip's source region moves — which is what lets a short one-shot be dragged out
to a whole bar.

## Devices and instruments

Built-in instruments, audio effects, and MIDI effects are registered by an integer
**kind**. Adding one means touching the C++ class, the kind registration, the ABI, the
interop layer, and the device-card UI — the chain is deliberately explicit.

Racks (`RackCore.h`, `RackInstrument.h`, `RackDevice.h`, `DrumRack.h`) are container
devices holding N parallel chains plus 8 macros with parameter mappings. A Drum Rack is
an Instrument Rack whose chains are pads gated by a trigger note.

## Plugin hosting

JUCE is confined to `pluginhost/`, built as a separate static library and reached through
`PluginHostBridge.h`. **No JUCE type crosses that line** — the core engine has no JUCE
dependency at all. VST3 is hosted everywhere; AU is macOS-only. Plugin scanning runs
out-of-process in `nota-scanworker` so a crashing plugin cannot take the app down.

This module is also the reason Nota's open build is AGPL — see
[`LICENSES/README.md`](LICENSES/README.md#why-agpl-and-not-gpl).

## Automation

A lane binds one target to a breakpoint envelope. Automation is **structural data**: it
lives in the immutable graph (on a `Track`, or per clip) and is copied by `cloneTrack`,
so undo/redo comes for free. Plugin-parameter automation is applied block-rate on the
audio thread by `Engine::applyAutomation`.

## Project format

A `.nota` project is a **bundle directory**, and **C# owns serialization** — the engine
never touches the file. Saving queries live engine state into a `ProjectDocument`, copies
referenced samples into `samples/` and plugin state blobs into `plugin-states/`, then
writes `project.json` atomically (temp file + rename) with an auto-backup. Loading
replays the document through the same structural-edit operations the UI uses.

See `Nota.Infrastructure/Persistence/ProjectService.cs`.

## UI

Avalonia, with a view-model layer in `Nota.Presentation`. The main window hosts the
arrangement, session view, piano roll, clip editors, device chain, browser, and mixer.
UI state is driven from engine polls on a UI clock rather than engine callbacks, which
keeps the audio thread free of UI concerns.

## Build

The native engine is built by CMake + Ninja, separately from the managed app; the app's
project copies the resulting `libnota_engine.{dylib,so}` / `nota_engine.dll` plus
`nota-scanworker` next to the managed binary for P/Invoke. See [`README.md`](README.md)
for commands and [`BUILD.md`](BUILD.md) for details.
