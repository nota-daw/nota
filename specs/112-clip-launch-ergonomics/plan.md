# Implementation Plan: Clip launching ergonomics

**Branch**: `feat/clip-launch-ergonomics` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/112-clip-launch-ergonomics/spec.md`

## Summary

Make the Session playable from the keyboard with the arrows plus four keys, and make the slot
buttons hittable. Return already launches the selection; it gains the click's record / stop-take
behaviour on empty armed and recording slots. The commands `session.stopTrack`,
`session.launchScene` and `session.stopAll` already exist (palette, controllers) but have no
key: they get ⇧Return, ⌘Return and ⌘⇧Return in the Session scope. The record circle, stop square
and stop-button glyphs are drawn larger with larger click areas. No engine or ABI change.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia (managed app only)
**Primary Dependencies**: none new
**Storage**: none (no project-file change; key overrides use the existing Settings path)
**Testing**: `tests/Nota.SmokeTest` — pure decision helper + key catalog checks
**Target Platform**: macOS, Windows, Linux
**Project Type**: desktop app (engine untouched)
**Performance Goals**: n/a — UI-thread key handling and drawing only
**Constraints**: audio-thread rules untouched (no engine change)
**Scale/Scope**: ~6 managed files

## Constitution Check

- No new dependency; AGPL header on the new file. ✅
- No audio-thread code touched. ✅
- Pure rule kept in `Nota.Application` so the smoke test checks it without a view (as SessionGridNav, specs/108). ✅
- Rebindable keys via the specs/096 catalog; the default-collision test guards conflicts. ✅

## Design

1. **`Nota.Application/Editing/SessionLaunchKey.cs`** (new, pure): `Resolve(SlotLaunchState)` →
   `SlotKeyAction { Launch, Record, StopRecording, CancelQueued?, ... }`. Inputs: the selection is a
   track slot, the slot is filled, the track is armed, the slot is recording. Output for Return:
   recording → `StopRecording`; empty + armed → `Record`; otherwise `Launch` (today's path).
2. **`SessionView.LaunchSelection`** asks `SessionLaunchKey` for a track slot and calls
   `RecordSessionSlot` / `StopSessionRecordSlot` like the click (`SlotCell.OnPointerPressed`).
   `ToggleSelection` (controllers) goes through it, so FR-011 follows.
3. **Keys**: `CommandCatalog` gives `session.stopTrack` `Shortcut.Shift("Return")`,
   `session.launchScene` `Shortcut.Cmd("Return")`, `session.stopAll` `Shortcut.CmdShift("Return")`
   (all already `view: "Session"` → `KeyScope.Session`). `MainWindow.OnGlobalTransportKey`
   (tunnel) dispatches them next to `session.launchSelection` while the Session has focus — the
   tunnel so a focused Button cannot eat Return.
4. **Sizes** (`SessionView.Grid.cs`): `CentreZone` 16 → 24 px (clipped to the cell); record
   circle radius 3.25 → 5.75 (11.5 px); stop square 6 → 10 px; footer stop glyph and Stop All
   glyph 7 → 10.
5. **Docs**: CHANGELOG [Unreleased] Added/Changed, FEATURES.md (nota-features), Shortcuts screen
   lists the commands automatically (catalog); check nota-shortcuts for anything hand-written.

## Project Structure

### Documentation (this feature)

```text
specs/112-clip-launch-ergonomics/
├── spec.md
├── plan.md
├── tasks.md
└── checklists/requirements.md
```

### Source Code (repository root)

```text
src/managed/Nota.Application/Editing/SessionLaunchKey.cs     (new)
src/managed/Nota.Application/Palette/CommandCatalog.cs        (default keys)
src/managed/Nota.App/MainWindow.Input.cs                      (dispatch)
src/managed/Nota.App/SessionView.cs                           (LaunchSelection)
src/managed/Nota.App/SessionView.Grid.cs                      (sizes)
tests/Nota.SmokeTest/ClipLaunchKeyTests.cs                    (new) + Program.cs
```

## Complexity Tracking

None.
