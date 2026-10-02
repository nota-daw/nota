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

### Sample analysis (smart samples)

`SampleAnalysis.cpp` measures a decoded sample off the audio path: tempo (`TempoDetect`),
key (a chroma of spectral peaks correlated with the Krumhansl–Kessler profiles), the
envelope facts that tell a loop from a one-shot, and a 16-value timbre fingerprint.
`nota_sample_analyze_file` decodes only a file's head (`AudioImportJob` with a seconds
cap), so a long file in the library costs what it analyses, not its length. On the managed
side `ISampleIndex` (`Infrastructure/SampleStore/SampleLibraryIndex.cs`) scans the Samples
folder on a few below-normal threads and persists the raw measurements in
`<data>/sample-index/index.bin`, keyed by path and invalidated by size and date.
`SampleClassifier` (Application) turns those measurements and the file's name
(`SampleNameHints`: "Loop_124_Am", a "One Shots" folder) into what the browser shows. It
runs again on load, so classifier changes need no re-analysis. Names win over the
analysis, and a loop's tempo is refined from its length.

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

A plugin is found by its JUCE identifier, `<format>-<name>-<path hash>-<uid>`. When no
catalog entry matches exactly, the lookup falls back to format + name + uid, so a project
still finds a plugin that lives at another path on this machine.

**Downloads** (Settings → Downloads, formerly Get Plug-ins; `IPluginStore` →
`Infrastructure/PluginStore/`) installs open-source VST3 plugins listed in the
[plugin registry](https://github.com/nota-daw/nota-plugins-registry). The registry is one
static `index.json` on GitHub Pages, and the repo's README documents its manifest. A release asset downloads straight from the plugin's GitHub release and must
match the size and sha256 pinned in the index. `ArchiveUnpacker` then unpacks it:
zip / tar / dmg / pkg-payload / deb, and no installer ever runs. The listed bundles are
copied into `<data>/plugins/VST3/<id>/`, which is registered as a scan path. Set
`NOTA_PLUGIN_REGISTRY` to point Nota at another index (a URL or a local file). Set
`NOTA_DATA_DIR` to relocate the data dir, including the native catalog and scan paths, for
tests.

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

The binaries are **content-addressed** (`BundleContent`): `samples/<hash>.wav` and
`plugin-states/<hash>.bin`, where `<hash>` is the first 128 bits of a SHA-256 of the
content. Identical content shares one file, a save skips files already on disk (new ones
are written temp + rename), names stay stable across sessions, and after the manifest is
replaced the files it no longer references are deleted. Hashing a sample reads its whole
buffer, so hashes are cached per engine sample id and seeded from the file names on load.
The dirty check captures with session names instead (`contentNames: false`) and reads no
audio. Older bundles (`sample-N.wav`, `state-N.bin`) load as is and migrate on their next
save.

**Version history** (`ProjectHistory`, port `IProjectHistory`) lives in `.history/` inside
the bundle. A version is the bundle's top-level files — the manifest and its sidecars —
stored by content hash in `.history/objects/` (Brotli), plus the list of binaries its
manifest references. Binaries are not copied: being content-named, they stay in
`samples/` / `plugin-states/` and are shared by every version that uses them.
`.history/versions.json` holds the tree (parent links, head, labels, notes, stars).
Checking a version out writes its top-level files back (manifest last) and moves the head;
the next save on top of an older version branches. A save's prune keeps every binary some
version references, and prunes nothing if the history can't be read. Deleting a version
re-parents its children and collects objects and binaries nothing needs any more.
Each version also stores what it changed against its parent (`VersionDiff`, from the two
manifests: tracks added / removed / renamed, tempo, meter, and per track arrangement, sound
or mix), which the History tab words via `VersionSummary`.

`analysis/` holds a cache of imported audio — each file's waveform overview (min/max per
512 frames) and detected tempo, as `<content fingerprint>.npk` — so re-importing a file
is instant. It is disposable: delete it and entries are rebuilt on the next import. While
a project is unsaved the entries are staged in the per-user data folder and copied in on
the first save (`AudioAnalysisCache`).

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

## Updates

The welcome screen asks GitHub for the latest published release (`IAppUpdater` →
`Infrastructure/Update/AppUpdater.cs`) and picks the asset `release.yml` builds for this OS
and architecture. **Update** downloads it into `<data>/updates/` and checks its size and the
sha256 `digest` GitHub reports for the asset. On macOS the `.dmg` is mounted and its
`Nota.app` copied out and version-checked. Nothing touches the installed app while Nota runs.
On exit (`desktop.Exit` in `App`) `RunPendingInstall` starts a small detached script. It
waits for the process to end, then puts the new build in place: it swaps the `.app` bundle
on macOS (rolling back on failure) or renames over the AppImage on Linux. On Windows it runs
the Inno Setup installer with `/SILENT`, and `/relaunch=1` makes the installer start Nota
again as the original user. **Restart now** quits through the normal close path, with the
relaunch flag set.

Only a packaged install in a writable location updates in place: an `.app` that isn't
translocated or running off the disk image, an Inno install folder, or `$APPIMAGE`. Dev
builds and everything else get the old **Download** button, which opens the release page.
`NOTA_UPDATE_API` points the check at another release JSON (a URL or a local file).
