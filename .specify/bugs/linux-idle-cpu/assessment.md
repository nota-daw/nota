# Bug Assessment: Linux — Nota's process CPU is well above other DAWs, also when idle

- **Slug**: linux-idle-cpu
- **Created**: 2026-10-15
- **Source**: https://github.com/nota-daw/nota/issues/55 (host: github.com — allowlisted)
- **Verdict**: likely valid, needs reproduction
- **Severity**: medium

## Report (summarized)

Issue #55, "Linux: Nota uses 20–30 % more CPU than other DAWs" (filed by the maintainer on behalf
of a user). On 64-bit Linux the system monitor shows Nota at 35–45 % CPU on average during a
session; two other DAWs on the same machine sit at 15–25 %. The user called this the main thing left
to fix. Expected: comparable CPU, and close to idle when nothing is playing. The issue already names
leads: the Linux-only 10 ms JUCE pump timer and UI polling/repaint timers that never stop. The
in-app CPU meter reading lower than the system monitor is tracked separately in #54.

## Symptom

Process CPU on Linux sits at roughly 35–45 % throughout a session, about 20 percentage points above
comparable DAWs. Expected: similar to other DAWs while playing, and near idle when stopped or
minimized.

## Reproduction

1. Linux x86_64, open a project that uses only built-in instruments.
2. Measure process CPU (`top -p <pid>` / `pidstat -t -p <pid> 1`) in three states: stopped, playing,
   and with the window minimized.
3. Compare with the same project on macOS / Windows.

[NEEDS CLARIFICATION: the user's project size (tracks/devices), distro, X11 vs Wayland, GPU/driver,
and whether the number was taken while playing or stopped.]

## Suspected Code Paths

- `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs:90` — `_clock` fires `OnTick`
  every 33 ms for the app's whole life. It never pauses when the transport is stopped, nothing
  changed, or the window is minimized/hidden.
- `src/managed/Nota.App/MainWindow.axaml.cs:269` — the `PlayheadUpdated` handler does a lot of work
  each tick: master meter, `Timeline.SetPlayhead`, `Timeline.UpdateMeters`, mixer meters,
  `_deviceChain.RefreshSynthLive`, editor playheads, the CPU indicator (sets `Width`/`Background`/`Text`
  every tick), MIDI learn, plus the extra subscribers in `MainWindow.Remote.cs:35` and
  `MainWindow.Gamepad.cs:49`.
- `src/managed/Nota.App/ArrangementView.cs:553` `SetPlayhead` — calls `InvalidateVisual()` on the
  ruler, sections lane, overlay and footer lanes **every** tick, even when `beats` is unchanged
  (stopped). That is a steady ~30 fps redraw of the arrangement chrome while idle.
- `src/managed/Nota.App/Controls/MeterBar.cs:39` `Push` — calls `InvalidateVisual()` unconditionally,
  even when the level and hold are unchanged (e.g. 0 → 0 in silence). `ArrangementView.UpdateMeters`
  (`ArrangementView.cs:664`) pushes to the header **and** footer meter of every track, plus the master.
  So N tracks give 2N+2 meter redraws per tick, forever.
- `src/managed/Nota.App/DeviceChainView.Instruments.cs:111` `RefreshSynthLive` — re-reads every
  device param over P/Invoke and runs the instrument's graph refresher (`_instLiveViz`) each tick
  while the device chain is visible. Whether the graphs redraw only on change was not verified.
- `src/managed/Nota.App/App.axaml.cs:114` — the Linux-only 10 ms `DispatcherTimer` that calls
  `NotaEngine.PumpPluginHost()`. Its native side (`src/native/nota.engine/pluginhost/PluginHost.cpp:90`)
  returns at once when no plugin GUI exists, so the cost is ~100 dispatcher wake-ups plus P/Invokes
  per second. That is real but small; not the main suspect for built-in-only projects.
- `src/native/nota.engine/src/Engine_Render.cpp:327` `Engine::render` → `processBlock` — the whole
  graph (every instrument, effect, rack) is processed every block, even when stopped. Nothing skips
  silent tracks or devices. Same on every OS, so it does not explain a Linux-only gap by itself, but
  it adds to the idle baseline.

## Root Cause Hypothesis

Most of the excess is most likely UI work, not DSP. A single 33 ms clock drives unconditional
`InvalidateVisual()` calls on the arrangement chrome and 2N+2 meters, plus per-tick property sets
(CPU indicator) and P/Invoke polling. So the app redraws at ~30 fps even when stopped and nothing has
changed, and it keeps doing so when minimized. On Linux, Avalonia (`UsePlatformDetect`, no explicit
`X11PlatformOptions`) often ends up on software Skia rendering or a weaker GL path. That would make
the same redraw rate far more expensive than on macOS/Windows and explain why only Linux stands out.
The 10 ms JUCE pump and the always-on graph processing add smaller constant costs. Confidence:
**medium**. The code clearly does idle redraws, but nobody has measured how the reported 35–45 %
splits between UI, audio and pump.

## Proposed Remediation

**Preferred**: make the UI tick do nothing when nothing changed, and back off when the window can't be seen.
1. `MeterBar.Push`: store the last drawn values and call `InvalidateVisual()` only when a level or
   hold actually changed (with a small epsilon). Do the same in other `Push`-style meter controls.
2. `ArrangementView.SetPlayhead`: return early (skip all invalidations) when `beats`, the loop state
   and the scroll are unchanged since the last tick.
3. Main-window CPU indicator: update `Width`/`Background`/`Text` only when the rounded percentage
   changes.
4. `MainWindowViewModel._clock`: when the window is minimized/hidden, either stop the visual part of
   the tick or drop it to a slow interval (~250 ms). Keep `Engine.Poll()`, gamepad, MIDI learn and
   Remote serviced. Add `IsVisible`/`WindowState` checks in the `PlayheadUpdated` handler, or split
   the handler into a "service" part and a "paint" part.
5. Linux JUCE pump: start the 10 ms timer only after the first plugin GUI/instance exists (or make
   it lazy: slow it to e.g. 50 ms while `nota_pluginhost_pump` keeps returning 0, then go back to
   10 ms on activity).

Before changing anything, measure on Linux (stopped / playing / minimized; `perf top -p`,
`dotnet-counters`, per-thread `top -H`) to confirm how the cost splits. Also check which Avalonia
render backend is active, e.g. by logging `AvaloniaLocator`/`IPlatformGraphics` or forcing
`X11PlatformOptions { RenderingMode = [Glx, Egl, Software] }`.

**Alternatives**:
- Engine-side idle skip: skip `process` for tracks whose instrument has no active voices, no
  input/MIDI and whose effect tails have decayed below a threshold. Bigger win on large projects and
  on every OS, but it touches the audio thread and every device's tail logic. Treat it as a separate
  feature (spec-driven), not part of this fix.
- Slowing the global clock to 60–100 ms while stopped. Simpler, but MIDI-learn and gamepad latency
  get worse unless those move to their own timer.

**Files likely to change**:
- `src/managed/Nota.App/Controls/MeterBar.cs` (and sibling meter controls in `Controls/`)
- `src/managed/Nota.App/ArrangementView.cs`
- `src/managed/Nota.App/MainWindow.axaml.cs`
- `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs`
- `src/managed/Nota.App/App.axaml.cs`

**Tests to add or update**:
- Headless Avalonia test: with the transport stopped and silent meters, N ticks of the UI clock
  cause no further render passes on the arrangement/meters after the first (count `Render` calls or
  invalidations).
- `MeterBar` unit test: pushing the same reading twice invalidates once.
- Smoke test (`tests/Nota.SmokeTest`): `PumpPluginHost` still returns 0 safely with no plugin. That
  check already exists at `Program.cs:14646`; keep it passing if the pump becomes lazy.
- Manual Linux measurement before/after (stopped, playing, minimized), recorded in the fix notes.

## Risks & Considerations

- Skipping invalidations can leave stale visuals if some state change doesn't go through the
  compared fields (theme switch, resize, zoom). Those paths must still invalidate explicitly.
- Meter peak-hold decay: if any meter animates a fall-off over time, "no change" detection must
  account for it.
- Lazy JUCE pump: hosted plugin editors/timers on Linux must still start promptly. The black/
  unclosable window bug (`.specify/bugs/linux-plugin-black-window`) is the regression to watch.
- MIDI learn / gamepad / Nota Remote ride the same tick; throttling while minimized must not stall
  them (a pad note-off must still arrive).
- Related but separate: #54 (in-app CPU meter only measures DSP time, so it reads lower than the
  system monitor).

## Open Questions

- [NEEDS CLARIFICATION: Was the 35–45 % measured while playing or while stopped? Window minimized?]
- [NEEDS CLARIFICATION: Project size and devices used; X11 or Wayland; GPU/driver (software
  rendering suspected).]
- [NEEDS CLARIFICATION: Was any third-party plugin loaded (turns the JUCE pump into real work)?]
- Is "% CPU" in the user's monitor per-core (top-style) or of the total machine? This changes how big
  the gap really is.
