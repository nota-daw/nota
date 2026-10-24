# Tasks: Tempo Track, MIDI Tempo and Piano Roll Event Editing

## Slice 1 — Transport continuity

- [x] T001 Update the transport's tempo-change operation to preserve musical beat position and the Stop anchor.
- [x] T002 Add a smoke regression that changes tempo mid-playback and verifies position continuity and anchor stability.
- [ ] T003 Verify loop-boundary calculations still use the same musical beats after a tempo change.

## Slice 2 — MIDI import/export data model

- [x] T004 Extend MIDI parsing to retain tempo meta-events and supported controller/pitch-wheel/channel-pressure events.
- [x] T005 Keep MIDI format 0 channel splitting and MIDI format 1 track grouping deterministic while associating controller events with the correct part.
- [x] T006 Add a MIDI writer for notes, controller curves and tempo meta-events, with deterministic same-tick ordering.
- [x] T007 Add parser/writer round-trip and malformed-file tests.

## Slice 3 — Tempo track and transport integration

- [ ] T008 Add a backward-compatible project tempo-point model and capture/apply logic.
- [ ] T009 Add queued C ABI commands for tempo-map updates and safe immutable map exchange on the audio thread.
- [ ] T010 Make clip rendering, loop splitting, live MIDI, metronome and plug-in playhead data use one tempo-to-time mapping.
- [ ] T011 Add a tempo-track surface above regular arrangement tracks, including visibility toggle, box selection, point editing and interpolation selection.
- [ ] T012 Add Ctrl/Cmd+T, transport action and tempo-control context-menu integration.
- [ ] T013 Add project round-trip tests for constant and changing tempo maps.

## Slice 4 — Piano Roll Event Line Editor

- [x] T014 Keep existing clip-global ruler seek and playhead drag/scrub behavior; implement independent Piano Roll Follow Playhead without adding duplicate seeking handlers.
- [x] T015 Anchor a short keyboard range to the event-lane boundary at minimum zoom and Fit.
- [x] T016 Add Shift+wheel velocity editing with selected-note/hovered-note targeting.
- [x] T017 Add the fifth controller-lane tab and picker, controller curve/block render/edit gestures, clear action and undo integration.
- [x] T018 Add independent event-lane height/collapse controls and parameter snap state.
- [ ] T019 Add controller picker/gesture tests for every supported controller class.

## Slice 5 — Performance and verification

- [x] T020 Add viewport-aware drawing with a small overscan for notes, grid and event lanes.
- [x] T021 Reduce box-selection work using bounded candidate ranges while retaining correct chord/overlap behavior.
- [ ] T022 Add rendering/interaction regression checks where supported by the smoke-test architecture.
- [x] T023 Update `CHANGELOG.md` and relevant documentation.
- [ ] T024 Run all available smoke/build workflows on Windows, macOS and Linux; resolve warnings/failures without changing build scripts.
- [x] T025 Add persisted Shift+wheel velocity direction and 0×–2× multiplier controls in Settings.
