# Tasks: Track inspector

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1 — Shared pieces

- [x] T001 Freeze entries as data: `TrackFreezeEntries` + `FreezeEntry`; the track menu builds its items from them (MainWindow.Freeze.cs)
- [x] T002 `SetTrackColours`, `RefreshAfterTrackEdit`, `TrackRowHeight`, `RecordInputChoices`, `MidiSourceChoices` (ArrangementView.Tracks.cs)
- [x] T003 Widen the Session inspector's `SectionHead` / `Row` / `Chip` / `Pick` / `Segmented` to `internal` (SessionView.Inspector.cs, SessionView.Mixer.cs)

## Phase 2 — Inspector (US1)

- [x] T004 `TrackInspectorView`: title, name, Mute / Solo / Arm, input (record input + monitor, or MIDI from), colour, height, freeze; return and master variants; empty state (TrackInspectorView.cs)
- [x] T005 Ninth rail tab "Track inspector" with a hairline above it; `SetTrackInspector` (BrowserView.cs)
- [x] T006 Create the view and hand it to the browser; `ShowDevices` points it at its track (MainWindow.axaml.cs, MainWindow.Detail.cs)

## Phase 3 — Staying current (US2)

- [x] T007 Snapshot poll while attached and visible; skipped while the name is being typed (TrackInspectorView.cs)

## Phase 4 — Verification + docs

- [x] T008 CHANGELOG `[Unreleased]`, FEATURES.md
- [x] T009 `scripts/build-linux.sh` green (smoke test incl. design-token checks)
- [x] T010 Run the app: walk through [quickstart.md](quickstart.md); screenshots for the PR
