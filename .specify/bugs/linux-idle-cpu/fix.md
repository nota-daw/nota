# Bug Fix: Linux — Nota's process CPU is well above other DAWs, also when idle

- **Slug**: linux-idle-cpu
- **Fixed**: 2026-10-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The UI no longer redraws when nothing changed. Meters repaint only when a level or hold moved,
the arrangement playhead chrome repaints only when the playhead, the loop or the follow-scroll
moved, and the CPU read-out updates only when its percentage changes. While the main window is
minimized (and no floating Devices/Clip window is shown), the tick keeps servicing MIDI learn and
scene tempo but skips all visual work. On Linux, the JUCE message pump backs off from 10 ms to
100 ms once its queue has stayed empty for ~0.25 s, and snaps back on the first handled message.

The assessment's verdict was "likely valid, needs reproduction" with open questions. The user
chose to go ahead with the fix without a Linux measurement first.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/Controls/MeterBar.cs` | modified | `Push` returns early when level and hold are unchanged; covers track headers, footer, mixer and master meters |
| `src/managed/Nota.App/ArrangementView.cs` | modified | `SetPlayhead` invalidates ruler/sections/overlay/footer/overview only when the playhead moved, the loop changed or follow-scroll moved the view; the auto-baseline check still runs every tick |
| `src/managed/Nota.App/MainWindow.axaml.cs` | modified | the `PlayheadUpdated` handler returns after MIDI learn + scene tempo while the window is minimized; the CPU read-out updates only on a changed percentage; `SyncSceneTempo` moved above the early return |
| `src/managed/Nota.Application/Engine/PumpPacer.cs` | added | pure pacing logic for the Linux pump (Fast 10 ms / Slow 100 ms after 25 empty pumps) |
| `src/managed/Nota.App/App.axaml.cs` | modified | the Linux pump timer re-times itself from `PumpPacer.Next(handled)` |
| `tests/Nota.SmokeTest/PumpPacerTests.cs` | added test | pacing rules |
| `tests/Nota.SmokeTest/Program.cs` | modified | runs `PumpPacerTests` next to the existing pump check |
| `CHANGELOG.md` | modified | `[Unreleased] → Fixed` entry |

## Diff Highlights

```csharp
// MeterBar.Push
if (l == _levelL && r == _levelR && hl == _holdL && hr == _holdR) return;

// ArrangementView.SetPlayhead
if (!moved && !scrolled && !loopChanged) return;

// MainWindow PlayheadUpdated
if (WindowState == WindowState.Minimized
    && _detailWindow is not { IsVisible: true, WindowState: not WindowState.Minimized }) return;
```

## Tests Added or Updated

- `tests/Nota.SmokeTest/PumpPacerTests.cs` — the pacer starts fast, stays fast through a short
  idle run, backs off after `IdlePumpsBeforeSlow` empty pumps, stays slow, and snaps back to fast
  on one handled message (with the back-off counting from zero again).
- The existing "plugin host pump: exported, non-blocking no-op" check still passes.

## Local Verification

- Commands run: `dotnet build Nota.sln` → succeeded; `scripts/build.sh` (macOS arm64: native
  + managed + full smoke test) → `SMOKE TEST PASSED`, exit 0, all new pacer checks PASS.
- Manual checks: none yet. The app was not launched, and CPU was not measured on Linux.

## Deviations from Assessment

- **No headless render-count test.** The smoke test has no Avalonia/`Nota.App` reference, and
  adding Avalonia.Headless is a new test dependency the assessment didn't call for. The
  meter/playhead early returns are covered by the build only; the pump pacing logic was extracted
  into `Nota.Application` so it could be tested instead.
- **The `_clock` in `MainWindowViewModel` was left untouched.** Minimized throttling lives in the
  view's tick handler instead. That keeps `Engine.Poll`, transport, gamepad and Remote at full rate
  (a pad note-off or a phone fader must not wait), and skips only the visual work.
- **Pump slow rate is 100 ms**, not the 50 ms the assessment sketched. Any handled message
  (X11 input over a plugin window, a plugin timer) brings it back to 10 ms immediately.
- `RefreshSynthLive`, mixer and session updates were not given their own change detection.
  Their redraws go through controls that mostly already compare values (faders check `> 1e-3`);
  they are skipped while minimized.

## Follow-ups

- Measure on Linux (stopped / playing / minimized; `top -H`, `perf top -p`) before and after,
  and record which Avalonia render backend is active. If it's software Skia, consider explicit
  `X11PlatformOptions { RenderingMode = … }`.
- Manually check in the app: meters still fall to zero on stop, peak-hold click clears, loop
  toggles from the transport bar show in the ruler, theme switch/resize repaint the ruler, plugin
  editors on Linux still open and respond at once (`linux-plugin-black-window` regression).
- Engine-side skipping of silent tracks/devices when stopped: a separate spec-driven feature.
- Ask the reporter for the open questions in the assessment (playing vs. stopped, project size,
  X11/Wayland, GPU).
