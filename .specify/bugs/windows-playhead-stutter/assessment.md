<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Windows playhead stutters even in an empty project

- **Slug**: windows-playhead-stutter (accepted by the user)
- **Created**: 2026-11-01
- **Source**: pasted text and follow-up clarification
- **Revision inspected**: 55c3c29f
- **Verdict**: likely valid, needs reproduction of the complete application's visible symptom; uneven Windows timer cadence reproduced in isolation
- **Severity**: medium — affects basic playback feedback even without project content; no evidence of data loss or an audio dropout in this investigation

## Report (verbatim or summarized)

> inspect the nota source code and identify the bug that causes stuttering playhead in windows system

Follow-up clarification: **Even in an empty project**.

## Symptom

The playback cursor advances in visible steps on Windows with an empty project. Expected: smooth movement synchronized to the engine transport, independent of clips or open editors.

## Reproduction

User-reported application scenario: an empty project on Windows during playback. Exact application version, audio backend, buffer size, display refresh rate and steps beyond this are [NEEDS CLARIFICATION].

An isolated reproduction of the timing defect is included under `diagnostics/`. It compiles the repository's actual `AvaloniaPeriodicTimerFactory` and `IPeriodicTimer` sources, initializes the Windows Avalonia dispatcher without creating a window, and records 120 intervals with a nearly empty callback. It uses the existing Release build's Avalonia 12.1.4 assemblies; it adds no application dependency.

From the repository root, after a Windows Release build exists at `src/managed/Nota.App/bin/Release/net10.0`:

```powershell
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/TimerProbe.csproj -- src/managed/Nota.App/bin/Release/net10.0/nota_engine.dll
```

Measured on this machine (`Microsoft Windows NT 10.0.26300.0`, .NET 10.0.14):

| Probe | Requested interval | Mean | Median | 95th percentile | Actual update rate |
|---|---:|---:|---:|---:|---:|
| Windows dispatcher, no engine DLL loaded | 33 ms | 45.19 ms | 47.68 ms | 48.54 ms | 22.13/s |
| Same, with native engine DLL loaded | 33 ms | 44.65 ms | 47.59 ms | 48.15 ms | 22.40/s |

Raw samples: `diagnostics/timer-results.json` and `diagnostics/timer-engine-loaded-results.json`. An earlier unrecorded successful run also averaged 44.64 ms / 22.40 updates per second. Loading the native DLL controls for its static initialization, including JUCE's Windows timer-period request. The probe does **not** start the audio backend, show a window, or measure presented frames: these figures establish the timer defect, not the precise frame rate of a running Nota window.

## Suspected Code Paths

- `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs:102` — creates the sole playhead polling clock with a fixed 33 ms interval.
- `src/managed/Nota.App/AvaloniaPeriodicTimer.cs:23` — implements that clock with the parameterless `DispatcherTimer` constructor, which defaults to Background priority.
- `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs:113` — each tick runs engine polling and transport readout updates before invoking all `PlayheadUpdated` subscribers with the raw `Engine.PositionBeats` value.
- `src/managed/Nota.App/MainWindow.axaml.cs:303` — the event directly calls `Timeline.SetPlayhead(beats)` alongside meters, live device controls and other UI work.
- `src/managed/Nota.App/ArrangementView.cs:693` — stores the supplied position and invalidates the overlay. There is no display-frame callback or interpolation between ticks.
- `src/native/nota.engine/src/engine/Transport.cpp:14` and `:35` — advances and publishes the engine position per render segment. `Engine.h:356` returns that atomic position through the ABI; this is a discrete render position rather than a continuous presentation clock.

## Root Cause Hypothesis

**High confidence in the mechanism; medium-to-high confidence it explains the reported empty-project stutter.** The moving cursor is driven by a Background-priority, 33 ms dispatcher timer instead of a display animation callback. On the inspected Windows dispatcher this timer delivers about 22 updates per second even with essentially no callback work. Each update assigns the latest block-based transport position directly to the cursor. Between updates the cursor stays stationary. Irregular timer gaps therefore become irregular visual jumps, and block-size quantization can add variation to the distance moved. Neither waveform rendering nor DSP overload is required for this mechanism.

Avalonia 12.1.4's [Windows dispatcher implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Windows/Avalonia.Win32/Win32DispatcherImpl.cs#L37) schedules dispatcher timers through Win32 `SetTimer`. Its [DispatcherTimer implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Base/Threading/DispatcherTimer.cs#L16) selects Background priority by default and, in `FireTick` / `Restart`, schedules the next interval after the callback returns. As a result, callback work further lengthens the period, and the timer provides no synchronization to presentation frames. These dependency sources match the package version in `Nota.App.csproj`.

The Native DLL's vendored JUCE source requests a finer Windows timer period (`juce_SystemStats_windows.cpp`, `HiResCounterHandler`). It would be incorrect to claim Nota simply forgot `timeBeginPeriod`, or to assume adding it fixes animation. The engine-loaded probe still exhibited the same uneven dispatcher cadence.

### Other paths investigated

- The older `.specify/bugs/audio-editor-playhead-redraw/` issue addressed full waveform repaints in the audio clip editor. That fix is already present: `WaveformView.SetPlayhead` invalidates only `PlayheadLayer`. It cannot explain the clarified empty-project case.
- Follow mode can repaint arrangement lanes and synchronously refresh detailed waveform peaks; this is a separate content-dependent performance concern, not a prerequisite for this report.
- The piano roll still invalidates its grid and ruler when the cursor moves. That can add UI work when open, but is not needed for empty-project stutter.
- No evidence obtained here establishes an audio-thread stall, a broken WASAPI sample rate, or a Windows-only transport arithmetic error.

## Proposed Remediation

**Preferred**: give the moving playhead a presentation path driven by the visible window's animation frames (for example `TopLevel.RequestAnimationFrame`) and a bounded visual interpolation/extrapolation of authoritative engine transport snapshots. Keep meters, engine polling, remote servicing, automation and recording materialization on their existing service cadence unless independently justified. Merely repainting the same stale engine snapshot more often will still show audio-block steps at larger buffer sizes.

The engine remains the sole source of musical time (AR-9). Use a coherent transport snapshot and monotonic timing to place the visual cursor between authoritative updates; promptly reconcile start, stop, pause, seek, loop wraps, tempo changes, count-in, device restarts and stalled audio callbacks. If accurate block timestamps require additional native snapshot fields, expose them through the pure C ABI and publish them without allocation or locking on the audio thread. Do not generate a separate musical clock for engine or editing operations, or accidentally use a speculative visual position for paste/split/seek semantics.

**Alternative**: reducing the timer interval or raising its priority may make the symptom less severe, but still leaves animation asynchronous to display frames and audio blocks. Raising all existing tick work to frame rate also increases UI and interop load unnecessarily.

**Files likely to change**:

- `src/managed/Nota.App/MainWindow.axaml.cs` — frame callback ownership and lifecycle; separate presentation from service work.
- `src/managed/Nota.App/ArrangementView.cs` — accept a visual position without changing edit-operation semantics.
- A small presentation timing helper and related application port, if required by the chosen design.
- `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs` — only as needed to separate snapshot delivery from existing polling.
- Native transport / ABI and managed interop only if coherent timing snapshots need extending.
- `CHANGELOG.md` for the eventual user-visible fix.

**Tests to add or update**:

- Deterministic tests of visual-position reconciliation for start/stop, seeks, loop wraps, tempo changes, long callback delays and stale transport snapshots. Use injected timestamps, not wall-clock timing thresholds in CI.
- Windows manual or instrumented validation of visible cursor frame intervals in an empty project at 60 Hz and higher refresh rates, with several WASAPI / ASIO buffer sizes. Compare against engine progression and dropout counts to distinguish visual jitter from audio stalls.
- Check minimization, detail-window visibility, window closing and export suspension so frame callbacks stop or detach correctly and never poll a disposed engine.
- Check editor cursors, Follow scrolling, split/paste position and recording alignment; add ABI smoke coverage if native snapshot data changes.

## Risks & Considerations

- The isolated timer measurement is not a full-application visual reproduction, and other processes, system load, windows being occluded and renderer configuration can affect observed cadence. Do not promise every Windows system runs at exactly 22 updates per second.
- A naive UI-only clock can drift from audible playback or overshoot stop/loop boundaries. Keep corrections authoritative and limit extrapolation when the audio backend stops advancing.
- A frame callback should update inexpensive moving layers; putting all current tick subscribers on it can increase cost and recreate stutter in larger projects.
- This assessment changes no production source. Diagnostic code and measurements are confined to this bug directory. No full build/smoke suite was run because no application behavior was changed.

## Open Questions

- [NEEDS CLARIFICATION: the affected Nota version, Windows version and display refresh rate.]
- [NEEDS CLARIFICATION: active WASAPI or ASIO backend, negotiated buffer size and whether audible playback also stutters or the dropout counter rises.]
- [NEEDS CLARIFICATION: visible frame timing in the complete application under the reporter's configuration; the timer timing has been reproduced independently.]

## Next Step

`/speckit-bug-fix slug=windows-playhead-stutter`
