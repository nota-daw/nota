# Implementation Plan: Audio dropouts in the status bar

**Branch**: `068-status-bar-dropouts` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

## Summary

Put a ghost button counting dropouts left of the status bar's sample-rate readout. The
existing `MainWindowViewModel.ReportDropouts` poll feeds a small `DropoutCounter` that holds
the reset point, so the button shows the dropouts since the device started or the user last
clicked it. It appears in the danger ink from the first dropout and is hidden at 0.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: None. The count is the engine's existing atomic xrun counter
(`nota_engine_xrun_count`), already read on the UI thread by the ~30 Hz playhead clock; nothing
is added to the audio thread or the C ABI.

**Testing**: `tests/Nota.SmokeTest/DropoutCounterTests.cs` (text, counting, change detection,
reset, never negative); the design checks (labels, tokens) scan the new XAML; manual check in
the running app.

## Design

- `Nota.Presentation/ViewModels/DropoutCounter.cs` — `Observe(engineCount)` returns whether
  the count since the reset point changed; `Reset(engineCount)` moves the reset point;
  `Text(count)` reads "0 dropouts" / "1 dropout" / "N dropouts".
- `MainWindowViewModel` — `DropoutText` and `HasDropouts`, updated by `ReportDropouts` only
  when the count changes (every dropout is still logged); `ResetDropouts()` for the click and
  after every successful `ApplyAudioSettings` (device, rate or buffer change, ASIO reset).
- `MainWindow.axaml` — `DropoutButton` (ghost, caption mono text, tooltip), visible while
  `HasDropouts`, in a new status bar column before `AudioFormatButton`; `MainWindow.axaml.cs`
  wires its click to `ResetDropouts`.
- `Theme/NotaTheme.axaml` — `TextBlock.Caption.alert` takes `Brush.DangerBright`, the overload
  readout ink.

## Constitution Check

UI-only, no engine or ABI change, no dependency, sentence-case labels, danger ink only while
something is wrong (DESIGN.md § semantics by purpose).
