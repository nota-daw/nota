---
description: "Tasks for loop record mode — Overdub / Replace"
---

# Tasks: Loop record mode — Overdub / Replace

**Input**: Design documents from `/specs/018-loop-record-replace/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. Constitution IV requires the smoke test to cover any engine behaviour change.

**Organization**: Grouped by user story. US1 (Replace) and US2 (Overdub unchanged) share the engine foundation. US3 is the switch, persistence and surfaces.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Create `tests/Nota.SmokeTest/LoopRecordModeTests.cs` with the two-line SPDX header, a `Run()` that yields the groups below, and the helpers copied from `LoopSeamTests.cs` (`Beats`, `Looped`, `Notes`). Register it as `-- loop record mode --` in `tests/Nota.SmokeTest/Program.cs`, both in the full run (after loop seam) and in the `--live-record` block.

---

## Phase 2: Foundational (engine + ABI)

**⚠️ Blocks US1–US3**

- [X] T002 Add `int32_t pass` to `RecordedNote` in `src/native/nota.engine/src/Engine.h`. Update every construction site in `src/native/nota.engine/src/Engine_Render.cpp` (`drainLiveMidi`, `captureRoutedNotes`, `onLoopWrap`'s split lambda) to pass `recordPass_`. In `onLoopWrap` the head gets the old pass and the continuation the new one.
- [X] T003 Add the mode and pass state to `src/native/nota.engine/src/Engine.h`: `std::atomic<bool> loopRecordReplace_{false}` with `setLoopRecordReplace(bool)` / `loopRecordReplace()`, audio-thread `int32_t recordPass_ = 0; bool passReplace_ = false; bool passTriggered_ = false;`, `std::atomic<int32_t> replaceUpTo_{0}`, and message-thread `struct TakeNote { int32_t pitch; double absStart; int32_t pass; }; std::vector<TakeNote> takeNotes_; int32_t appliedReplaceUpTo_ = 0;`.
- [X] T004 Reset the pass state on the audio thread when a take starts. In `drainLiveMidi` (`src/native/nota.engine/src/Engine_Render.cpp`), on `takeStarted` set `recordPass_ = 0`, `passTriggered_ = false`, `passReplace_ = loopRecordReplace_.load(relaxed)`, `replaceUpTo_.store(0, release)`. In `Engine::setRecording(true)` (`src/native/nota.engine/src/Engine_Recording.cpp`) clear `takeNotes_` and set `appliedReplaceUpTo_ = 0` before `recording_` goes true.
- [X] T005 In `Engine::onLoopWrap` (`src/native/nota.engine/src/Engine_Render.cpp`), for an Arrangement take only (recording, no `recordSlotPlayer_`), after the split: `++recordPass_`, `passTriggered_ = false`, `passReplace_ = loopRecordReplace_.load(relaxed)`.
- [X] T006 [P] Add the C ABI per `specs/018-loop-record-replace/contracts/c-abi.md`. Declare `nota_transport_set_loop_record_mode` and `nota_transport_loop_record_mode` in `src/native/nota.engine/include/nota/nota_engine.h` next to the count-in calls, and implement them in `src/native/nota.engine/src/nota_engine_core.cpp` ("mode outside {0, 1} → NOTA_ERR_INVALID_ARG").
- [X] T007 [P] Add the managed mirror. `SetLoopRecordMode(bool replace)` and `bool LoopRecordReplace { get; }` go in `src/managed/Nota.Application/Engine/IAudioEngine.cs`. P/Invokes go in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs`, and wrappers in `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs`. Implement it in any other `IAudioEngine` implementers or fakes (grep `SetCountIn(` to find them).

**Checkpoint**: Builds. Recording behaves exactly as before (no trigger yet).

---

## Phase 3: User Story 1 — Re-record a loop until it's right (P1) 🎯 MVP

**Goal**: In Replace, the first fresh note of a new pass removes this take's earlier passes.

**Independent test**: quickstart.md rows "Replace, same phrase on 5 passes", "silent pass", "existing clip notes", "held across seam", "routed".

- [X] T008 [US1] Trigger on the audio thread (`src/native/nota.engine/src/Engine_Render.cpp`). In `drainLiveMidi`, where a recorded live note-on sets `pendingActive_`, and in `captureRoutedNotes` on a routed note-on, when `!recordSlotPlayer_` and `passReplace_ && !passTriggered_ && recordPass_ > 0`: `replaceUpTo_.store(recordPass_, release); passTriggered_ = true;`. The seam continuation in `onLoopWrap` must not trigger.
- [X] T009 [US1] Apply on the message thread in `collectRecorded` (`src/native/nota.engine/src/Engine_Render.cpp`). Load `replaceUpTo_` with acquire **before** draining `recorded_`. In the Arrangement branch, skip batch notes with `pass < replaceUpTo` and push `{pitch, absStart, pass}` to `takeNotes_` for each appended note. Then, if `replaceUpTo > appliedReplaceUpTo_`, erase from `clip.notes` every note matching a `takeNotes_` entry with `pass < replaceUpTo` (same pitch, `|clip.startBeat + n.startBeat − absStart| < 1e-6`), drop those entries, and set `appliedReplaceUpTo_`. Also handle a batch that is empty but has a pending replace (don't early-return before applying), and publish via `republishWithTrackRaw` (same undo step). Session-slot branch: unchanged.
- [X] T010 [US1] Smoke tests in `tests/Nota.SmokeTest/LoopRecordModeTests.cs` (mode set via the new ABI, 1-bar loop at 120 BPM):
  - (a) Replace, the same 4-note phrase on 5 passes with ±0.03-beat jitter → exactly 4 notes, all from the last pass.
  - (b) Replace, play on pass 1, nothing on pass 2, stop → pass-1 notes kept.
  - (c) Replace into an existing clip that holds a note at beat 2.0 → that note survives.
  - (d) Replace, hold a key 3.5 → seam → 0.5, then a fresh note at 1.0 on the same pass → the head `3.5+0.5` is gone, while `0.0+0.5` and the fresh note stay. A further fresh note on the next pass removes them.
  - (e) Replace with routed MIDI (`SetTrackMidiSource` + `TrackNoteOn` on the source) → same result as (a) with 3 passes.

**Checkpoint**: US1 works headless.

---

## Phase 4: User Story 2 — Keep overdubbing (P1)

**Goal**: Overdub is unchanged and is the default.

- [X] T011 [US2] Smoke tests in `tests/Nota.SmokeTest/LoopRecordModeTests.cs`:
  - (f) default mode on a fresh engine reads overdub (`LoopRecordReplace == false`).
  - (g) Overdub, a different pitch on each of 3 passes → all 3 pitches present.
  - (h) the ABI round-trip works, and an invalid mode (2) returns an error (call the `NativeMethods` entry directly, or check that the wrapper throws).

---

## Phase 5: User Story 3 — Switch quickly, even while recording (P2)

**Goal**: One click or ⌘⇧R toggles the mode. The mode is visible, persisted and reachable from MCP. A mid-take switch applies from the next pass.

- [X] T012 [US3] Smoke test (i) in `tests/Nota.SmokeTest/LoopRecordModeTests.cs`: start in Overdub, play on pass 1, switch to Replace during pass 2 after playing a note in it, play on pass 3. Expected: pass-1 and pass-2 notes are removed only at pass 3's first note, and the pass-2 note did not trigger a replace when played.
- [X] T013 [P] [US3] Add `public bool LoopRecordReplace { get; set; }` (default `false`) to `src/managed/Nota.Application/Settings.cs`, next to `CountInBars`, with a doc comment.
- [X] T014 [US3] Add `[ObservableProperty] private bool _loopRecordReplace;` to `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`, with `partial void OnLoopRecordReplaceChanged(bool value) => _engine.SetLoopRecordMode(value);` and a `ToggleLoopRecordMode` relay command. Also add a `LoopRecordModeText` ("Overdub"/"Replace") if the cell needs a bound label.
- [X] T015 [US3] Restore and persist in `src/managed/Nota.App/MainWindow.axaml.cs`: set `vm.Transport.LoopRecordReplace = vm.Settings.Current.LoopRecordReplace` where `CountInBars` is restored (~line 347), and write it back plus save on change (subscribe to `PropertyChanged`, the same way other transport preferences are saved; find how `Settings` is saved).
- [X] T016 [US3] Add the transport console cell to `src/managed/Nota.App/MainWindow.axaml` right after the Loop cell, per `specs/018-loop-record-replace/contracts/ui.md`: a `ToggleButton Classes="cell"` bound to `Transport.LoopRecordReplace`, labelled Overdub/Replace, with the tooltip from the contract. Follow DESIGN.md / the nota-design skill. Add a separator if the Loop group uses one.
- [X] T017 [US3] Add the command `C("transport.loopRecordMode", "Loop record: overdub / replace", "Transport", "⌘⇧R", "overdub replace take pass")` to `src/managed/Nota.Application/Palette/CommandCatalog.cs`. Bind it in `src/managed/Nota.App/MainWindow.Commands.cs` (`B("transport.loopRecordMode", ...)` toggling `Transport.LoopRecordReplace`). Check that ⌘⇧R is not handled elsewhere in `src/managed/Nota.App/MainWindow.Input.cs`.
- [X] T018 [US3] Add the menu item `<NativeMenuItem Header="Loop Record: Replace" app:Commands.Id="transport.loopRecordMode"/>` under Transport in `src/managed/Nota.App/MainWindow.axaml`, next to Loop and Metronome, with a check state if other toggle items show one.
- [X] T019 [P] [US3] MCP in `src/managed/Nota.Mcp/Tools/TransportTools.cs`: add `bool LoopRecordReplace` to `TransportState` / `get_transport`, and add a `set_loop_record_mode(bool replace)` tool. Route it through the same state the UI uses (view-model or engine plus settings) so the toggle reflects it. Update any MCP tool listing or test that enumerates tools (grep `set_metronome` in `tests/`).

**Checkpoint**: The UI toggle, shortcut, menu, MCP and persistence all work.

---

## Phase 6: Polish & cross-cutting

- [X] T020 Add a `CHANGELOG.md` `[Unreleased] → Added` entry: "Loop recording: Overdub or Replace (nota-daw/nota#29)".
- [X] T021 Run the nota-shortcuts skill (Preferences → Shortcuts list gains ⌘⇧R), the nota-features skill (FEATURES.md), and the nota-docs skill (recording page, EN + RU).
- [X] T022 Run `scripts/build.sh`. It must pass, including the new `-- loop record mode --` group.
- [X] T023 Verify the UI headless or in the app per the "Nota UI verify recipe" memory: the transport cell renders in both states in Studio Dark and Light, and ⌘⇧R toggles it.
- [X] T024 Re-check the Nota Compliance Checklist in `specs/018-loop-record-replace/spec.md` and run `/speckit-converge`.
- [X] T025 Commit on `fix/loop-seam-note-cut` as `Egor Khindikaynen <khek@ambertape.ru>`, with no Co-Authored-By, separately from the seam-fix commit.

## Dependencies

- T001 → T010–T012 (test file).
- T002–T005 (engine state) → T008–T009 → T010.
- T006 → T007 → T010–T019 (tests and the app use the ABI).
- US1 (T008–T010) and US2 (T011) are independent once Phase 2 is done.
- US3 UI (T013–T018) depends only on T007. T012 depends on T008–T009.
- Polish depends on everything.

## Parallel opportunities

- T006 ∥ T002–T005 (different files). T007 after T006.
- T013 ∥ T019 ∥ T017 (different files) once T007 is done.

## Implementation strategy

MVP = Phase 2 + US1 (Replace works headless and is tested). Then US2 (guard rails), then US3 (surfaces), then polish. One commit at the end, per the user's workflow.

## Implementation notes (2026-10-16)

- T011 (h): the managed wrapper takes a `bool`, so a test cannot pass an out-of-range mode. The C ABI rejects modes outside {0, 1}, as the code shows. The smoke test covers the default and the set/get round-trip.
- T016 / T018: the toggle label sizes to the longer word ("Overdub") so the transport bar doesn't shift between states. The menu item reads **Loop record: overdub / replace**, a plain command, because the design-token check requires sentence case. It is not a check item. The cell also joins the transport's narrow-width shed order (`MainWindow.TransportFit.cs`), and is shed before Loop.
- T019: MCP sets the engine directly, as `set_metronome` does. `TransportViewModel.Tick` follows the engine's mode, so the toggle and the saved preference update either way.
- T023: verified headless (`/tmp/loopmodeharness`). Clicking the toggle, ⌘⇧R, and the MCP/engine path all update the engine, the VM, the label and the saved preference. Plain ⌘R still records. The cell is 58 px in both states. Screenshots were taken in Studio Dark and Light, and the setting survived a restart. The Shortcuts pane lists ⌘⇧R.

## Phase 7: Convergence

- [X] T026 When `collectRecorded` (src/native/nota.engine/src/Engine_Render.cpp) removes earlier-pass notes in Replace mode, release their voices: queue an audio-thread command (AR-4, allocation-free) carrying the record track id and the removed pitches. In the next block, before rendering, inject note-offs for those pitches into that track's MIDI (through its MIDI FX, like the seam offs), skipping any pitch the player is holding live on that track right now. Do not route it through `trackNoteOff`, which would end a note being recorded. Per SC-005 / FR-003 (contradicts).
- [X] T027 Add a smoke test to tests/Nota.SmokeTest/LoopRecordModeTests.cs: Replace, a long note on pass 0 (0.1 → 3.6), polled so it plays back on pass 1, then a fresh note on pass 1 while it sounds → after release, a pass with no notes in the take is silent (rms < 1e-4). Run the same scenario in Overdub as a control: it still sounds. Per SC-005 / Constitution IV (missing).
