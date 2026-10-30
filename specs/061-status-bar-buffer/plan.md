# Implementation Plan: Sample rate and buffer size in the status bar

**Branch**: `061-status-bar-buffer` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

Turn the status bar's sample-rate text into a ghost button reading "48 kHz · 256" and give
it a MenuKit flyout of buffer sizes. Picking one goes through a new
`MainWindowViewModel.SetAudioBufferFrames`, which stages the size, runs the existing
`ApplyAudioSettings` restart, resumes playback and raises `AudioDeviceRestarted` so an open
Settings → Audio pane rebuilds.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: None. The readout reads negotiated values on the UI thread; a
buffer change uses the same backend restart Settings already uses (polling suspended
around `ApplyAudio`).

**Testing**: `tests/Nota.SmokeTest/AudioFormatStatusTests.cs` (text, latency, size list,
buffer owner); the design checks (labels, menus) scan the new view code; manual check in
the running app.

## Design

- `Nota.Presentation/ViewModels/AudioFormatStatus.cs` — pure helpers: `Text(rate, frames)`,
  `Latency(rate, frames)`, the shared `BufferSizes` list (Settings uses it too) and
  `Owner(asioRunning, exclusiveRunning)` → `BufferOwner { Nota, Driver, Device }`.
- `MainWindowViewModel` — `EngineInfo` built by `UpdateEngineInfo()` at start and after each
  apply; `BufferOwner` from the active audio system and exclusive state;
  `SetAudioBufferFrames(int)`.
- `MainWindow.axaml` — `AudioFormatButton` (ghost, caption mono text bound to `EngineInfo`,
  tooltip), beside `VersionButton`.
- `MainWindow.AudioFormat.cs` — builds the flyout per click: caption, then sizes (radio,
  latency as trailing value, disabled while recording) or the ASIO / exclusive alternative,
  separator, Audio settings… (`ShowPreferencesPage("Audio")`).

## Constitution Check

UI-only, no engine change, no dependency, MenuKit conventions, sentence-case labels.
