# Implementation Plan: A plug-in that hangs while loading doesn't freeze Nota

**Branch**: `fix/979-plugin-load-freeze` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Every hosted plug-in is created through two bridge factories (`createPluginEffect`,
`createPluginInstrument`) and `clone()`. Instead of creating the JUCE instance there, the
factories now return the adapter (`PluginEffect` / `PluginInstrument`) **unloaded**: a
placeholder that passes audio through (effect) or is silent (instrument), keeps the
plug-in's identity and any state handed to it, and owns a *load ticket*. A process-wide
`PluginLoader` loads each ticket's binary on one background thread (FR-001, FR-010); the
app's UI timer calls `nota_engine_plugin_loads_service`, which on the message thread
creates the instance from the already-loaded binary and attaches it to the adapter
(FR-002, FR-004). Engine indices, the C ABI shape of every insert call and all managed
callers stay as they are; only the moment the plug-in starts sounding moves.

## Nota: Read Before Planning

- `ARCHITECTURE.md` AR-4/5/6: attach publishes through an atomic `live_` flag (release on
  the message thread, acquire on the audio thread); no lock, allocation or I/O is added to
  the audio thread — an unloaded adapter returns early.
- AR-7: new engine calls are plain C in `nota_engine.h`; JUCE stays in `pluginhost/`.

## Technical Context

- **Binary load per format** (`PluginLoader::preloadBinary`, loader thread):
  - VST3 — the module the format would load: macOS `CFBundleCreate` +
    `CFBundleLoadExecutableAndReturnError` on the bundle (no `bundleEntry`); Windows
    `LoadLibraryW` on the `.vst3` (or `Contents/<arch>-win/<name>.vst3` in a bundle); Linux
    `dlopen` of `Contents/<arch>-linux/<name>.so`. The handle is kept for the process
    lifetime, so JUCE's own load on the message thread finds the image already mapped and
    initialised (refcount bump, no initialisers re-run).
  - AU (macOS) — `AudioComponentInstanceNew` + `AudioComponentInstanceDispose` on the loader
    thread: the component's bundle (and its copy protection) loads there.
  - Anything else — no preload (the main-thread step does it all, under the freeze guard).
- **States** (`pluginLoadState()`): 0 loaded / not a hosted plug-in, 1 waiting in line,
  2 loading, 3 not responding (past the deadline), 4 couldn't load; `pluginLoadReason()`
  gives the text. Actions (`pluginLoadAction`): 1 keep waiting, 2 give up, 3 try again.
- **Deadline**: 30 s, `NOTA_PLUGIN_LOAD_TIMEOUT_MS` overrides it (tests).
- **Freeze guard** (FR-008/009): `plugin-loading.txt` in the data dir holds the identifier
  and name while the main-thread step runs; at loader start a leftover file moves the
  identifier to `plugins-froze-nota.txt` and leaves a one-time notice
  (`nota_plugin_frozen_notice`). Inside `nota_plugin_autoload_scope(1)` (project open) a
  marked plug-in is created as "couldn't load" without loading; a successful load unmarks.
- **Automation**: a plug-in-param lane made while its device was unloaded has
  `paramIndex = -1`; after an attach the engine re-resolves every plug-in lane with an id
  and no index (track and nested lanes) and republishes.
- **PDC**: `recomputePdc()` after any attach (latency appears with the instance).
- **Parallel render**: `parallelRender_` becomes atomic and turns on at attach.
- **Sync callers** (tests, tools): `nota_engine_plugin_loads_wait(ms)` services until no
  load is pending or the time is up.
- **Test plug-in**: the render probe sleeps `NOTA_RENDER_PROBE_LOAD_DELAY_MS` in a static
  initialiser — a stand-in for a copy-protection wrapper that runs at binary load.

## Constitution Check

- I (real-time): audio-thread paths only gain an atomic load and an early return. ✔
- II (boundary): new ABI is C; JUCE only in `pluginhost/`. ✔
- III (licensing): no dependency. ✔
- IV (verification): smoke test covers async insert, timeout states, give-up, wait, frozen
  mark, state round-trip; existing hosted tests wait for loads. ✔

## Project Structure

### Documentation (this feature)

`specs/113-plugin-load-freeze/` — spec.md, plan.md, tasks.md, checklists/.

### Source Code (repository root)

- `src/native/nota.engine/pluginhost/hosting/PluginLoader.{h,cpp}` — new: tickets, loader
  thread, binary preload, service, freeze guard.
- `pluginhost/hosting/PluginEffect.*`, `PluginInstrument.*` — unloaded constructor,
  `attach`, pending state, `live_` gate, load-state virtuals.
- `pluginhost/hosting/PluginHost.cpp`, `bridge/PluginHostBridge.h` — factories return
  unloaded adapters; service/wait/scope/notice bridges.
- `src/devices/Device.h`, `src/devices/Instrument.h` — JUCE-free load-state virtuals.
- `src/engine/Engine*.{h,cpp}`, `src/abi/*`, `include/nota/nota_engine.h` — service, wait,
  per-device state/action, lane re-resolution.
- `pluginhost/tests/RenderProbe.cpp` — load delay.
- Managed: interop + `IAudioEngine`; `App.axaml.cs` service timer; `PluginCard` load-state
  body; `ProjectService` autoload scope; `MainWindow` notice; `en.json`.
- `tests/Nota.SmokeTest` — new isolated `--plugin-load` module; hosted tests wait.

## Complexity Tracking

- macOS-only code paths (CFBundle / AU preload) can't be built on the Windows dev machine;
  they're kept small and compiled by CI's macOS job.
- Out-of-process hosting and the CoreCLR Mach-exception-port experiment stay out of scope
  (spec Assumptions).
