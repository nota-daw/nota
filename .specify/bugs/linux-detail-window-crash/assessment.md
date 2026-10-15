# Bug Assessment: Linux — app aborts when the piano roll is popped out into its own window

- **Slug**: linux-detail-window-crash
- **Created**: 2026-10-15
- **Source**: pasted text (screenshot of a terminal stack trace + a video description from a tester on Fedora)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

A tester on Fedora Linux runs the Nota AppImage (`/tmp/.mount_Nota-…/usr/bin/Nota.App`), tries to open the
piano roll in a separate window, and the app crashes. The terminal shows:

```
#0  __pthread_kill_implementation (libc.so.6)
#1  raise (libc.so.6)
#2  abort (libc.so.6)
#3–#8   libcoreclr.so
#9–#18  n/a (n/a + 0x0)          ← JIT-compiled managed frames
#19–#24 libcoreclr.so
#25–#29 libhostpolicy.so / libhostfxr.so
#30–#34 Nota.App / __libc_start_main
```

The tester's log (`~/.config/nota/logs/nota-20261015.log`) then gave the managed exception:

```
2026-10-15T07:46:34.692Z ERROR Unhandled exception
System.ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager.
   at Avalonia.Layout.LayoutManager.ExecuteArrangePass()
   at Avalonia.Layout.LayoutManager.ExecuteLayoutPass()
   at Avalonia.Media.MediaContext.FireInvokeOnRenderCallbacks()
   at Avalonia.Media.MediaContext.RenderCore()
   ...
   at Avalonia.X11.X11PlatformThreading.RunLoop(CancellationToken cancellationToken)
   ...
   at Nota.App.Program.Main(String[] args)
```

## Symptom

Opening the detached Devices / Clip window (piano roll on top, device chain below) kills the process on
Linux. Expected: the window opens with the current clip editor and device chain, as on macOS/Windows.

## Reproduction

1. Launch the Nota AppImage on Fedora [NEEDS CLARIFICATION: Nota version, Fedora version, X11 or Wayland/XWayland, desktop (GNOME/KDE)].
2. Open or create a project with a MIDI clip [NEEDS CLARIFICATION: was a clip selected / the piano roll already open docked?].
3. Pop the detail panel out (View → pop out Devices/Clip, command `view.popOutDetail`) [NEEDS CLARIFICATION: exact action used — menu, shortcut or button].
4. Process aborts.

## Suspected Code Paths

- `src/managed/Nota.App/MainWindow.DetailWindow.cs:SetHost()` / `OnToggleDetailWindow()` — moves the live
  `_deviceChain` and clip editor out of the main window's `DetailBody` into the new `DetailWindow` hosts,
  with layout work for those controls still queued in the **main window's** `LayoutManager`.
- `src/managed/Nota.App/MainWindow.Mixer.cs:45` — the mixer pop-out does the same (and sets
  `_mixer.IsVisible = true` *while still in the main tree*, which queues it in the main window's layout
  manager right before moving it). Same crash expected on Linux.
- `src/managed/Nota.App/DetailWindow.cs:OnClosing` and `MixerWindow.cs:OnClosing` — the reverse move
  (re-dock); lower risk because the closing window's layout manager is disposed, but same pattern.
- Avalonia 12.0.3 `src/Avalonia.Base/Layout/LayoutManager.cs` — `ExecuteArrangePass()` → `Arrange()` walks up to
  the control's (new) root, finds an ancestor whose measure is invalid (`AncestorMeasureInvalid`), adds the
  control to `_toArrangeAfterMeasure`, then calls `InvalidateArrange(i)`, which throws because
  `control.GetLayoutRoot() != _owner`.

## Root Cause Hypothesis

Every `TopLevel` has its own `LayoutManager` with its own measure/arrange queues, and Avalonia never removes a
control from a queue when it is reparented. The detail panel's controls are invalidated constantly (meters,
playhead, tick loop), so at pop-out time some of them sit in the main window's queues. `OnToggleDetailWindow`
then moves them into `DetailWindow`. On the next render the main window's arrange pass dequeues such a
control, walks up into the *new* window, finds that window not yet measured, and re-queues the control via
`InvalidateArrange` on the main window's manager. Avalonia rejects that and throws `ArgumentException`. Nothing
catches it on the dispatcher, so the process aborts.

Why Linux: it depends on timing. On X11 mapping a window is asynchronous, and the WM's
`ConfigureNotify` (final size / centring / scaling) invalidates the new root's measure after `Show()`. So when
the main window's pass runs, the new window's ancestors are measure-invalid. On macOS/Windows the new window
is usually laid out synchronously before that, so `Arrange()` succeeds. The bug is cross-platform in
principle. **Confidence: high** for the mechanism (it matches the Avalonia source line for line), medium for
the exact reason it shows up only on X11.

## Proposed Remediation

**Preferred**: Flush the source window's layout queue before moving a control to another top-level. In
`SetHost`, when `content`'s current `TopLevel` differs from `host`'s, call `sourceTopLevel.UpdateLayout()`
before detaching (this runs `ExecuteLayoutPass` and empties the queues while the control is still in its old
tree). Any invalidation after the move goes to the new window's manager. That covers both the detail and the
mixer pop-out through one helper. In `MainWindow.Mixer.cs`, move `_mixer.IsVisible = true` after `SetHost`
(or rely on the flush). In `DetailWindow.OnClosing` / `MixerWindow.OnClosing`, call `UpdateLayout()` before
`Content = null`, for symmetry on re-dock.

**Alternatives**:
- Build the window and reparent inside `win.Opened` (after the first layout). Narrows the window but doesn't
  remove the race: the main queue can still hold the moved controls.
- `Dispatcher.UIThread.UnhandledException` → log + `Handled = true`. Stops the abort but leaves layout in a
  bad state. Fine as a separate safety net, not as the fix.

**Files likely to change**:
- `src/managed/Nota.App/MainWindow.DetailWindow.cs` (`SetHost`)
- `src/managed/Nota.App/MainWindow.Mixer.cs`
- `src/managed/Nota.App/DetailWindow.cs`, `src/managed/Nota.App/MixerWindow.cs` (`OnClosing`)
- a headless UI test under `tests/`

**Tests to add or update**:
- Avalonia.Headless: host a control in window A, `InvalidateArrange` it (and an ancestor's measure), move it
  into a new, not-yet-laid-out window B with `SetHost`, then run the render/layout pass. Without the fix it
  should throw the same `ArgumentException`; with the fix it should not.
- Pop out / re-dock detail and mixer during playback (meters invalidating) without a crash; manual check on
  Linux X11 (Fedora tester) is the real confirmation.

## Risks & Considerations

- Headless tests run on macOS won't exercise the X11/AT-SPI back end; a real Linux run is needed to confirm.
- Re-docking after a partial failure must not leave a control with two parents (see `SetHost`).
- Crash recovery should already offer the autosave on relaunch; confirm the tester didn't lose work.

## Open Questions

- [NEEDS CLARIFICATION: does the mixer pop-out crash on the tester's machine too? (expected: yes)]
- [NEEDS CLARIFICATION: X11 or XWayland session — the trace shows the X11 backend either way]
