<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: VST3 / AU plugin becomes frozen / unresponsive while its editor is open

- **Slug**: `plugin-editor-sleep-freeze`
- **Created**: 2026-11-25T17:32:00+01:00
- **Source**: User report during `#1050` engine rewrite & optimization (pasted text)
- **Verdict**: valid
- **Severity**: high — breaks interactive UI metering, analyzers, internal GUI keyboards/sequencers, and plugin UI↔DSP responsiveness whenever an audio/MIDI gap exceeds silence hangover or transport stops

## Report

When a VST3 or AudioUnit plugin's native editor window is open, the plugin intermittently stops reacting or freezes during silence or stopped playback. Keeping the plugin processing whenever its editor window is open resolves the freeze. (Introduced on the unmerged `#1050` engine performance branch, fixed directly on the branch.)

## Symptom

When a VST3 or AudioUnit plugin's native editor window is open, the plugin intermittently stops reacting or freezes visually (meters/analyzers stop updating, GUI triggers/controls stop responding) during silent passages or when transport is stopped. Expected: as long as a plugin's editor window is open on screen, the engine keeps calling its audio processing path every block without overwriting the user's `PluginSleepMode` (`Auto` / `Never`), and resumes auto-sleeping cleanly once the editor closes.

## Reproduction

1. Insert a VST3 or AudioUnit instrument (`PluginInstrument`) or effect (`PluginEffect`) on a track, group/return bus, or inside a Rack chain (`InstrumentRack` / `DrumRack` / `RackDevice`), leaving `PluginSleepMode` at its default `Auto`.
2. Open its native editor (`OpenPluginEditor` / `nota_plugin_open_editor`).
3. Leave transport stopped or play past a silent gap longer than the plugin's `SilenceGate` hangover (`0.25 s`–`10 s`, or until `Track::tailQuiet() < 0` settles on an audio track / muted track / group bus).
4. Observe that `plugin_->processBlock(...)` stops being invoked, leaving the open editor unable to pump audio-thread telemetry or react to GUI-internal events until audio/MIDI resumes or playback restarts.

## Suspected Code Paths

- `src/native/nota.engine/pluginhost/hosting/PluginInstrument.cpp:125-150` — `SilenceGate::beginBlock` skips `renderPiece` / `plugin_->processBlock` regardless of `editor_.isOpen()`.
- `src/native/nota.engine/pluginhost/hosting/PluginEffect.cpp:88-106` — `SilenceGate::beginBlock` skips `processBlock` when main and sidechain inputs are silent, ignoring `editor_.isOpen()`.
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.h:83-95` & `.cpp:178-195` — `EditorHost::isOpen()` queries JUCE `window->isVisible()` directly (unsafe on audio threads per `AR-4`) and never notifies `SilenceGate` on open, close, or OS close-button hide.
- `src/native/nota.engine/src/dsp/core/SilenceGate.h:73-141` — lacks an independent non-destructive hold-awake state (`holdAwake_`) separate from the persistent `PluginSleepMode` (`Never`).
- `src/native/nota.engine/src/engine/RenderGraphExecutor.cpp:47-79, 288-300, 483-545, 898-908` — `hasNeverSleepDevice`, `instrumentLeafCanSkip`, `canSkipInactiveBus`, `settleInactiveBus`, muted-track skip (`483`), and Audio-track stopped/tail settle (`537`, `900`) ignore open plugin editors (and line `537` skipped audio tracks with `tailQuiet() < 0` even with `NeverSleep` / open editor).
- `src/native/nota.engine/src/engine/Engine_Render.cpp:188-194` — `playEvents()` skips instrument `render()` solely on `isIdle() && !needsIdleRender()`.
- `src/native/nota.engine/src/devices/racks/RackCore.h:532-542`, `RackInstrument.cpp:520,552`, `RackDevice.h:50`, `DrumRack.h:44`, `DrumRack.cpp:75` — nested chains/racks did not propagate child `editorOpen()` / active processing requirements upward.
- `src/native/nota.engine/src/engine/HybridBufferController.cpp:198-229` — anticipative `4096`-frame lookahead remained active even while a track's plugin editor was open on an unarmed track.

## Root Cause Hypothesis

**High confidence.** Auto-sleep (`specs/115-instrument-plugin-sleep-gate`, commit `7bdc3472`) introduced `SilenceGate` auto-sleep to `PluginInstrument` and `PluginEffect`, and graph skipping (`specs/115`, `specs/118`) omits idle tracks, muted/solo-gated tracks, inactive audio tails, buses, and rack chains when `isIdle()` or `tailQuiet() < 0`. `EditorHost` did not track an audio-thread-safe atomic open state or hold `SilenceGate` awake while visible, and the render graph / rack propagation only checked `PluginSleepMode::Never` (not even reliably for stopped audio tracks with settled `tailQuiet < 0`), starving `plugin_->processBlock()` whenever the editor remained open during silence.

## Proposed Remediation

**Preferred**:
1. Add an audio-thread-safe `std::atomic<bool> open_{false}` and `onOpenChanged` callback to `EditorHost` in `PluginEditorWindow.h/.cpp` updated cleanly via one shared `applyOpenState(bool)` helper across `open()`, `close()`, `onHide` (`closeButtonPressed()`), and destructor.
2. Add an orthogonal `holdAwake_` flag to `dsp::SilenceGate` (`setHoldAwake(bool)`) so `PluginInstrument` and `PluginEffect` wake immediately when `editorOpen()` becomes true and stay out of sleep (`isSleeping() == false`) while the window is open, without mutating `PluginSleepMode` (`Auto` resumes automatically after close; `clone()` does not inherit window state).
3. Add `virtual bool requiresActiveProcessing() const` to `Device` and `Instrument` (`sleepMode() == Never || editorOpen()`), propagated recursively across `RackCore` (`RackInstrument`, `DrumRack`, `RackDevice`) and muted chains. Update `RenderGraphExecutor` (`hasPinnedDevice`, `instrumentLeafCanSkip`, `canSkipInactiveBus`, `settleInactiveBus`, audio `LeafPath::Tail` check even when `!playing` / `tailQuiet() < 0`, and open-editor muted-track handling with `toMix = false`) plus `Engine_Render::playEvents()` and `HybridBufferController::isTrackAnticipativeEligible()` (keep tracks with open editors at live latency).
4. Wake hybrid/track state when opening editors in `Engine_Devices.cpp` / `Engine_Rack.cpp`, and add `nota_engine_plugin_editor_sleep_selftest` covering `PluginInstrument`, `PluginEffect` (Audio track, muted track, Group bus), `InstrumentRack`/`RackDevice`, and `HybridBufferMode::Auto` on real VST3 and macOS AU fixtures (`Nota Render Probe`).

**Files likely to change**:
- `src/native/nota.engine/src/dsp/core/SilenceGate.h`
- `src/native/nota.engine/src/devices/Device.h`, `Instrument.h`
- `src/native/nota.engine/src/devices/racks/RackCore.h`, `RackInstrument.h`, `RackInstrument.cpp`, `RackDevice.h`, `DrumRack.h`, `DrumRack.cpp`
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.h`, `PluginEditorWindow.cpp`, `PluginInstrument.h`, `PluginInstrument.cpp`, `PluginEffect.h`, `PluginEffect.cpp`
- `src/native/nota.engine/src/engine/RenderGraphExecutor.cpp`, `Engine_Render.cpp`, `Engine_Devices.cpp`, `Engine_Rack.cpp`, `Engine.h`, `HybridBufferController.cpp`
- `src/native/nota.engine/include/nota/nota_engine.h`, `src/native/nota.engine/src/abi/nota_engine_device.cpp`
- `src/managed/Nota.Infrastructure/Interop/NativeMethods.Devices.cs`, `NotaEngine.Devices.cs`
- `tests/Nota.SmokeTest/HostedRenderPoolTests.cs` (and AU test suite)

**Tests to add or update**:
- Real `Nota Render Probe` VST3 and macOS AU verification via `nota_engine_plugin_editor_sleep_selftest` invoked from `Nota.SmokeTest`.

## Risks & Considerations

- **Performance**: Keeping open-editor plugins awake adds CPU only for the plugins whose windows are currently visible on screen; closed plugins remain in `PluginSleepMode::Auto` with zero CPU regression.
- **Headless CI**: Linux CI runs without an X11 display; test helpers driving `EditorHost`'s state transition cleanly exercise `SilenceGate` + engine graph logic without requiring an OS window server or stealing macOS focus.
- **Muted / Solo-gated tracks**: Rendering open-editor muted tracks must keep `w.toMix = false`, `w.route = nullptr`, zero sends, and zeroed meters so no audio leaks into Master or sidechains.
