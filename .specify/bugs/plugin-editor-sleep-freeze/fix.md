<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Keep VST3 / AU plugins actively processing while their editor is open

- **Slug**: `plugin-editor-sleep-freeze`
- **Fixed**: 2026-11-25T17:58:00+01:00
- **Assessment**: `./assessment.md`
- **Status**: applied

## Summary

Added an audio-thread-safe atomic open-editor state (`EditorHost::open_`) that holds `dsp::SilenceGate::holdAwake_` active without overwriting `PluginSleepMode`, propagated `requiresActiveProcessing()` / open-editor awareness across `Device`, `Instrument`, `RackCore` (`RackInstrument`, `DrumRack`, `RackDevice`), `RenderGraphExecutor`, `Engine_Render`, and `HybridBufferController`, and added VST3/AU selftest regression coverage in `HostedRenderPoolTests`.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/dsp/core/SilenceGate.h` | modified | Added `holdAwake_` orthogonal to persistent `PluginSleepMode` (`Auto` / `Never`) |
| `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.h` | modified | Added `std::atomic<bool> open_` (`AR-4` safe `isOpen()`), `onOpenChanged`, and `setSimulatedOpenForTest` |
| `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp` | modified | Unified `applyOpenState` across `open()`, `close()`, `onHide`, and destructor |
| `src/native/nota.engine/pluginhost/hosting/PluginInstrument.h/.cpp` | modified | Wired `editor_.onOpenChanged` to `gate_.setHoldAwake` |
| `src/native/nota.engine/pluginhost/hosting/PluginEffect.h/.cpp` | modified | Wired `editor_.onOpenChanged` to `gate_.setHoldAwake` |
| `src/native/nota.engine/src/devices/Device.h`, `Instrument.h` | modified | Added `requiresActiveProcessing()` (`sleepMode() == Never \|\| editorOpen()`) |
| `src/native/nota.engine/src/devices/racks/RackCore.h`, `RackInstrument.h/.cpp`, `RackDevice.h`, `DrumRack.h/.cpp` | modified | Recursive chain active-processing / open-editor propagation across rack containers and muted chains |
| `src/native/nota.engine/src/engine/RenderGraphExecutor.cpp`, `Engine_Render.cpp` | modified | Prevented sleep/skip for Audio/Instrument/Group/Return/Muted tracks when a hosted plugin editor is open |
| `src/native/nota.engine/src/engine/HybridBufferController.cpp`, `Engine_Devices.cpp`, `Engine_Rack.cpp` | modified | Excluded open-editor tracks from 85 ms anticipative lookahead and invalidated on editor open/close |
| `src/native/nota.engine/include/nota/nota_engine.h`, `src/abi/nota_engine_device.cpp`, `src/managed/Nota.Infrastructure/Interop/*` | modified | Added `nota_engine_plugin_editor_sleep_selftest` ABI and P/Invoke wrapper |
| `tests/Nota.SmokeTest/HostedRenderPoolTests.cs` | modified | Added VST3 (`Nota Render Probe`) and macOS AU (`AUBandpass`) open-editor keep-awake tests |

## Diff Highlights

- Orthogonal `holdAwake_` in `SilenceGate.h` preserves user `PluginSleepMode` across editor open/close and excludes window state from `clone()`.
- Stopped Audio tracks (`tailQuiet < 0`), idle buses, muted tracks (`toMix = false`, zeroed meters), and Rack chains now honor active editors while keeping audio output silenced when muted.

## Tests Added or Updated

- `HostedRenderPoolTests.cs`: `VST3 open editor keeps PluginInstrument, Audio/Muted/Group PluginEffect, Hybrid Auto, and Racks awake (code=0)`
- `HostedRenderPoolTests.cs`: `macOS real AudioUnit open editor keeps Audio/Muted/Group/Rack chains active past hangover (code=0)`

## Local Verification

- `./scripts/build.sh` -> exit 0 (`SMOKE TEST PASSED`, 0 failures)
- `dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --perf` -> exit 0 (`PERF PASSED`)
- `nota_idle_cpu 5 5` -> `0.0%` (`PASS`)

## Deviations from Assessment

- Reused `Nota Render Probe` and Apple `AUBandpass` (`0 ms` tail) with `setSimulatedOpenForTest()` inside `nota_engine_plugin_editor_sleep_selftest` to exercise the exact `EditorHost` -> `SilenceGate` -> engine/rack transition deterministically across headless Linux CI and macOS without popping focus-stealing OS windows.

## Follow-ups

- Manual UI smoke check with third-party GUI plugins containing custom internal keyboards/analyzers when running the full interactive desktop app.
