# Implementation Plan: MIDI Learn for Session clip and scene launch buttons

**Branch**: `feat/session-launch-midi-learn` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/108-session-launch-midi-learn/spec.md` (issue #601)

## Summary

Two MIDI Learn target kinds, `SessionSlotLaunch` (track + row) and `SessionSceneLaunch` (row),
appended to `MidiTargetKind`. The grid's `SlotCell` and `SceneCell` expose their launch buttons as
learn regions, so the existing learn overlay highlights and binds them with no other UI code. A
press lands in `ControlApplier.Trigger`, a release in a new `ControlApplier.Release`; both call
`Nota.Application.Controllers.SessionLaunch`, which does what a click on the button does through
`IAudioEngine` alone — so it runs with the Session view hidden and the smoke test can drive it.

## Technical Context

**Language/Version**: C# / .NET 10 (Application, App, smoke test)

**Primary Dependencies**: none new

**Storage**: `midimap.json` (existing): a slot is saved by track position with its row in
`ParamIndex`; a scene is a global mapping with its row in `ParamIndex`

**Audio-Thread Impact**: none. The MIDI thread's learn copy (`MidiSurface::route`) also pushes
note-offs into the existing SPSC `midiControl_` queue (no allocation, no lock); launches are
UI-thread calls to existing engine functions (`LaunchSlotVelocity`, `ReleaseSlot`, `LaunchScene`,
`RecordSessionSlot`, `StopSessionRecordSlot`, `StopSlot`)

**Testing**: `tests/Nota.SmokeTest/SessionLaunchTests.cs` (`-- --session-launch` alone, and in the
full run); `scripts/build-linux.sh`

## Design

- **Targets** (`MidiLearn.cs`): `MidiTarget.SessionSlotLaunch(track, scene)` and
  `MidiTarget.SessionSceneLaunch(scene)`; both are buttons (`IsButton`). Appended to the enum, whose
  integers are persisted.
- **Learn regions** (`SessionView.Grid.cs`): `SlotCell : IMidiLearnRegions` returns the button a
  click would press — the launch triangle on a clip or a take, the centre stop / record button on
  an empty slot (learnable before anything is recorded). `SceneCell` returns its launch box. Regions
  rather than a whole-cell binding: the overlay lights the buttons the issue names, not the grid.
- **Note-offs reach MIDI Learn** (`MidiSurface::route`): the learn copy took CC and note-on only,
  so a note had no way down — and a note-mapped button, whose latch only a release resets, fired
  once. It now also passes a note-off (or a note-on at velocity 0) as kind 1, value 0. On a port a
  profile owns whole, `ControllerService` hands 0x80 to `DispatchFromController` as value 0 too.
  `MidiLearnService.Tick` doesn't bind on a note-off nor record it as activity; a continuous target
  ignores it (it keeps the last velocity, as before).
- **Press / release** (`MidiLearnService.ApplyNormalized`): the button latch already fires once on
  the way up; its way down now calls `ControlApplier.Release` (a no-op for every other kind). A note
  passes its velocity; other sources (and an inverted mapping) pass 1.
- **`SessionLaunch`** (Application): `Press` — a missing row or track does nothing; a recording or
  record-pending slot (state 4 / 5, or the record target) stops its take; a filled slot launches with
  the velocity; an empty slot records when its track is armed, else stops the track when it has its
  stop button. `Release` → `ReleaseSlot` (the engine stops only a Gate / Repeat clip). `Scene` →
  `LaunchScene`. Guarding the row matters: the engine reads a direct launch of a missing row as
  "stop the track".
- **Persistence** (`MidiLearnService`): `SessionSlotLaunch` joins `TrackScoped`, so its track is
  saved by position and a deleted track drops it on load; the row rides in `ParamIndex`, which the
  sidecar already saves.
- **Names**: the binding is named `Launch · <track> · <scene>` / `Launch · <scene>` when learned
  (what MCP's `get_midi_mappings` reports); the MIDI map tab re-reads the names from the grid when
  it draws (`MidiLearnService.SessionLabel`), so a rename shows.

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #601 carries `approved` |
| I. Real-time safety | ✅ no audio-thread change; the MIDI thread pushes one more message kind into the lock-free learn queue |
| II. Boundary discipline | ✅ no ABI change (the learn queue's events keep their shape); existing `nota_session_*` functions |
| III. Licensing | ✅ new files carry the SPDX header; no dependency |
| IV. Verification gate | ✅ smoke test extended (`SessionLaunchTests`) |
| V. One logical change | ✅ Session launch buttons in MIDI Learn only |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]`, FEATURES.md; the manual's MIDI Learn page in nota-docs |
| VII. Spec-first | ✅ this spec / plan / tasks |
| VIII. Rules not weakened | ✅ |
