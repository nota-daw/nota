# Implementation Plan: Track inspector

**Branch**: `055-track-inspector` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

A `TrackInspectorView` hosted as the browser's ninth rail tab, beside the bespoke Map and
History tabs. `MainWindow.ShowDevices` — the one place that decides which track the Devices
panel shows — also points the inspector at that track. The view builds its sections from the
engine and writes through the calls the header and the track menu already use, so undo,
arming and automation rules are shared rather than copied. No engine, ABI or format change.

## Technical Context

- **Rail**: `BrowserView` — `TabCount` 9, `InspectorTab = 8` treated as a custom tab (no
  search, status line or preview), a hairline above it, `SetTrackInspector(Control)`.
- **View**: `TrackInspectorView.cs` (new). Sections reuse the Session inspector's pieces
  (`SectionHead`, `Row`, `Chip`, `Pick`, `Segmented`, widened from `private` to `internal`).
- **Edits**:
  - name → `SetTrackName`; mute / solo → `SetTrackMute` / `SetTrackSolo`; arm →
    `TrackArming.Set` (exclusive arm, ⌘-click adds);
  - record input → `RecordInputPick.ApplyTo`, monitor → `SetTrackMonitor`;
  - MIDI from → `SetTrackMidiSource`;
  - colour → `ArrangementView.SetTrackColours` (extracted from the menu's colour row: one
    undo group); height → `ArrangementView.SetTrackHeights`;
  - freeze → `MainWindow.TrackFreezeEntries`, the menu's freeze entries as data
    (`FreezeEntry`), rendered by the menu as items and by the inspector as buttons.
- **Picker lists**: `ArrangementView.RecordInputChoices` / `MidiSourceChoices` give the same
  sources, in the same order, as the menu.
- **Staying current**: a 250 ms timer while the view is attached; it skips when the tab is
  hidden (browser folded) or the name box has focus, compares a string snapshot of
  everything shown, and rebuilds on a difference. A rebuild is a few dozen controls.

## Constitution Check

- I Real-time safety: UI only; every engine call is an existing message-thread setter. ✓
- II Boundary: no ABI change. ✓
- III Licensing: one new source file with the SPDX header; no dependencies. ✓
- IV Verification: no engine behaviour change; the smoke test's design-token checks cover the
  new view's tokens and labels; `scripts/build-linux.sh` green. ✓
- V One change: the inspector only (the track menu is untouched apart from building its
  freeze items from the shared list). ✓
- VI Docs: CHANGELOG, FEATURES.md; the user manual (nota-docs) follows separately. ✓

## Project Structure (touched)

```
src/managed/Nota.App/TrackInspectorView.cs            (new)
src/managed/Nota.App/BrowserView.cs
src/managed/Nota.App/ArrangementView.Tracks.cs
src/managed/Nota.App/MainWindow.{axaml.cs,Detail.cs,Freeze.cs}
src/managed/Nota.App/SessionView.{Inspector.cs,Mixer.cs}
CHANGELOG.md, FEATURES.md
```
