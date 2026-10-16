---

description: "Task list for live MIDI notes while recording (#56)"
---

# Tasks: Live MIDI notes while recording

**Input**: Design documents from `/specs/017-live-midi-record-notes/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (IV) and FR-011 require smoke coverage for the engine changes and the new C ABI read. Visual checks use the headless capture recipe in quickstart.md.

**Organization**: Tasks are grouped by user story so each one can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

## Path Conventions

- Engine: `src/native/nota.engine/src/`, C ABI `src/native/nota.engine/include/nota/nota_engine.h`
- Managed: `src/managed/Nota.Application/`, `src/managed/Nota.Infrastructure/`, `src/managed/Nota.App/`
- Smoke tests: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

**Purpose**: Test scaffolding the stories add to

- [X] T001 Create `tests/Nota.SmokeTest/LiveRecordTests.cs` with the two-line AGPL SPDX header, a `static class LiveRecordTests` exposing `IEnumerable<(bool, string)> Run()` and a `Render(NotaEngine, int blocks)` helper modelled on `MpeTests.Recording` (`tests/Nota.SmokeTest/MpeTests.cs:184`). Register it in `tests/Nota.SmokeTest/Program.cs` the same way `MpeTests` is registered

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The held-note snapshot, its C ABI read and the UI plumbing that every story draws from

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 In `src/native/nota.engine/src/Engine.h`:
  - add the snapshot state: `std::atomic<uint32_t> heldSeq_`, `std::atomic<int32_t> heldTrack_`, `heldScene_` and `heldCount_`, `std::atomic<double> heldNow_`, and 256 slots of relaxed atomics (pitch, source, startBeat, velocity);
  - add a private `publishHeldSnapshot(double nowBeat)` and a public `int32_t recordHeldNotes(NotaRecordHeldNote* out, int32_t maxN, int32_t* trackId, int32_t* scene, double* nowBeat) const`.

  Rules: "count 0..256", "live (128) + routed (128)", "seq odd while writing; readers retry on odd or changed" (data-model.md § Held-note snapshot).
- [X] T003 Implement `publishHeldSnapshot` in `src/native/nota.engine/src/Engine_Render.cpp`:
  - bump the seq to odd, write the compacted entries from `pendingActive_`/`pendingStart_`/`pendingVel_` (source 0) and `recRoutedActive_`/`recRoutedStart_`/`recRoutedVel_` (source 1), then the track id (`recordTrackRt_`, or `recordSessionTrackId_` for slot takes), the scene (`recordSessionScene_`, else −1), the count and `nowBeat`, then bump the seq to even;
  - with no MIDI take, write count 0 and track 0;
  - call it once at the end of `processBlock`, after capture.

  No allocation or locks (AR-6).
- [X] T004 Implement `Engine::recordHeldNotes` in `src/native/nota.engine/src/Engine_Recording.cpp` (next to `liveHeldNotes`, `:196`):
  - read with up to 4 retries on an odd or changed seq, else return 0;
  - `out == NULL` or `maxN <= 0` returns the count without copying (contracts/c-abi.md § Guarantees).
- [X] T005 Add `NotaRecordHeldNote` and `nota_engine_record_held_notes` to `src/native/nota.engine/include/nota/nota_engine.h` after `nota_engine_audio_record_peaks`, exactly as in contracts/c-abi.md. Add the shim to `src/native/nota.engine/src/nota_engine_track.cpp` next to `nota_engine_live_held_notes` (`:654`)
- [X] T006 [P] Add the managed mapping:
  - the `[StructLayout(LayoutKind.Sequential)] NotaRecordHeldNote` struct (int pitch, int source, double startBeat, float velocity) and the P/Invoke `RecordHeldNotes` in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs`;
  - the wrapper in `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs`;
  - `int RecordHeldNotes(NotaRecordHeldNote[] buffer, out int trackId, out int sessionScene, out double nowBeat)` in `src/managed/Nota.Application/Engine/IAudioEngine.cs`, implemented by every `IAudioEngine` implementation and test stub (search for `AudioRecordPeaks` implementations).
- [X] T007 Create `src/managed/Nota.App/RecordPreview.cs` (SPDX header): one instance owned by `MainWindow`.
  - `Update(IAudioEngine)` reads the snapshot once into a reused 256-entry buffer and exposes `TrackId`, `SessionScene`, `NowBeat` and `ReadOnlySpan<NotaRecordHeldNote> Held`;
  - `Clear()` empties it.

  No per-tick allocation.
- [X] T008 Wire `RecordPreview` in `src/managed/Nota.App/MainWindow.axaml.cs:318`:
  - call `_recordPreview.Update(Engine)` at the start of the `RecordingTick` handler, before `Timeline.Refresh(rebuildHeaders: false)`;
  - clear it when recording stops (the first tick where `Engine.IsRecording` is false, from `PlayheadUpdated`) and refresh once;
  - pass the instance to `ArrangementView`, `SessionView` and the piano-roll creation sites (`MainWindow.Detail.cs:430`, `:615`).
- [X] T009 Smoke tests in `tests/Nota.SmokeTest/LiveRecordTests.cs`, using a 120 BPM, armed built-in instrument track, recording:
  - after `TrackNoteOn(t, 60, 0.8f)` and rendering, the read returns pitch 60, source 0 and the take's track id;
  - `start_beat` equals the note-on beat;
  - `now_beat` increases across renders;
  - `out == null` returns the count;
  - with no take, the read returns 0 and track 0.

**Checkpoint**: The engine publishes held notes and the UI receives them every recording tick

---

## Phase 3: User Story 1 — See held notes grow in the arrangement (Priority: P1) 🎯 MVP

**Goal**: Held notes appear on key-down and grow with the playhead in the arrangement take. On release, the stored note replaces them without a jump. Notes from the count-in and keys held across Record are kept at their real start, and the clip grows left to include them (FR-001–FR-003, FR-007, FR-012, FR-013).

**Independent Test**: Record in the arrangement, hold a key for several beats, release, and compare with the stored note. Repeat with a pickup on the count-in, and with a take at 1.1 and a key pressed one beat early (quickstart.md §1–2).

### Tests for User Story 1

- [X] T010 [P] [US1] Smoke tests in `tests/Nota.SmokeTest/LiveRecordTests.cs`:
  - (a) note-off removes the pitch from the next read, and after `Poll` the clip has the note with the same start, length and pitch (contracts/c-abi.md: never seen twice or missing);
  - (b) `SetRecording(false)` with a key held empties the snapshot and stores nothing for that key.
- [X] T011 [P] [US1] Smoke tests in `tests/Nota.SmokeTest/LiveRecordTests.cs`, using `SetCountIn(1)` with Record from stop at bar 5 (beat 16):
  - (a) a note on during the count-in's last beat and off before beat 16 is stored at beat 15, the take's clip starts at 15, and an offline render has energy at beat 15;
  - (b) from 1.1, a key pressed 1 beat before 1.1 and held to beat 2 gives clip start 0 and the note's `startBeat ≈ −1`, length ≈ 3, and the render sounds the note from 0;
  - (c) with the transport rolling and a key down at beat 3, Record at beat 4 and release at beat 6 store the note from beat 3.

### Implementation for User Story 1

- [X] T012 [US1] Add a capture clock in `src/native/nota.engine/src/Engine_Render.cpp` / `Engine.h`. `captureBeat(blockStartBeat)` returns:
  - the block start beat while rolling;
  - `takeStartBeat − (countInTotal_ − countInPos_)` while `countInActive_`, where `takeStartBeat` is the transport playhead beat (it does not move during the count-in).

  Source of truth: research R2 and data-model.md § Capture clock. In `drainLiveMidi` (`:413`), capture when `rec && (playing || countInActive_)` instead of `rec && playing` (`:459`), and use the clock for `nowBeat` when no Session slot is active.
- [X] T013 [US1] Seed held keys at the record edge in `src/native/nota.engine/src/Engine_Render.cpp` / `Engine.h` (research R3):
  - add audio-thread arrays `liveDown_[128]`, `livePressBeat_[128]`, `livePressVel_[128]` and `livePressTrack_[128]`, updated on every live note-on/off in `drainLiveMidi`, whether or not recording;
  - set `livePressBeat_` from the capture clock when rolling or in a count-in, else mark it "pressed while stopped";
  - on the `rec != prevRecording_` rising edge (`:417`), seed `pendingActive_`/`pendingStart_`/`pendingVel_` from keys still down whose track matches (`livePressTrack_ < 0 || == recordTrackRt_`);
  - start beat: the press beat if pressed while rolling; the count-in's first beat (`takeStartBeat − countInTotal_`) if pressed while stopped and a count-in starts; else the take start beat;
  - on the falling edge, clear as today.
- [X] T014 [US1] Move the clip's left edge in `Engine::poll` in `src/native/nota.engine/src/Engine_Render.cpp` (`:276–292`), arrangement branch (research R4):
  - before appending each `RecordedNote`, if `r.startBeat < clip.startBeat`, set `newStart = max(0.0, r.startBeat)` and `delta = clip.startBeat − newStart`;
  - shift every existing note's `startBeat += delta`, then `clip.lengthBeats += delta` and `clip.startBeat = newStart`;
  - then compute `n.startBeat = r.startBeat − clip.startBeat`, which may stay negative only when `r.startBeat < 0`.

  Keep `republishWithTrackRaw`, with no new undo step.
- [X] T015 [US1] Negative-start render rule: a note with `startBeat < 0` and `startBeat + lengthBeats > 0` sounds from the clip start with length `startBeat + lengthBeats`. Notes with `startBeat ≥ lengthBeats` stay skipped (data-model.md § Stored early note). Apply it at:
  - `src/native/nota.engine/src/Engine_Render.cpp:524` (computeBlockMidi);
  - `:749` and `:765` (the session/expression paths; read each site and keep its semantics otherwise);
  - `src/native/nota.engine/src/Engine_Tracks.cpp:1807`.

  The expression lookup for such a note starts at beat offset `−startBeat`.
- [X] T016 [US1] Draw held notes in the arrangement take in `src/managed/Nota.App/ArrangementView.LaneControl.cs`, after the clip loop and near the audio take block (`:1349`):
  - for the row whose track id equals `RecordPreview.TrackId` with `SessionScene < 0`, and only if visible, draw each held note from `BeatToX(startBeat)` to `BeatToX(max(startBeat, NowBeat))`, at least 2 px wide;
  - use the pitch mapping of the take's clip (`DrawClipBody`'s pitch range), extended to include held pitches, and the track's note colour;
  - when the earliest held start is before the clip start, draw a faint take extension box from it to the clip start first (contracts/ui-preview.md § Arrangement take).

  Allocate no new geometry when `Held` is empty.

**Checkpoint**: US1 works: held notes grow, early notes are kept, and the smoke tests pass

---

## Phase 4: User Story 2 — Short appear animation (Priority: P2)

**Goal**: Each new note fades in from the accent colour once, over 150 ms (FR-008, SC-006)

**Independent Test**: Record staccato notes and a held note; each animates once and none re-animates on growth or release (quickstart.md §2.3)

- [X] T017 [US2] Add the appear tracker to `src/managed/Nota.App/RecordPreview.cs` (research R7, data-model.md § Appear-animation entry):
  - a `Dictionary<(int pitch, long startKey), long firstSeenTicks>` keyed by pitch and `Math.Round(startBeat * 1e6)` (absolute beat for the arrangement, slot-local for Session);
  - `Update` adds held notes not yet present, and adds notes newly present in the take's stored notes (compare against the previous tick's key set; the caller passes the take clip's notes in absolute beats) that were never seen held;
  - drop entries older than 150 ms;
  - `double Progress(int pitch, double startBeat)` returns 1 when the note is absent.

  Reuse collections; no per-tick allocation in steady state.
- [X] T018 [US2] Apply the animation when drawing held and stored take notes in `src/managed/Nota.App/ArrangementView.LaneControl.cs`. For notes with `Progress < 1`:
  - draw separately from the batched `StreamGeometry` with opacity `0.35 + 0.65·e(p)`;
  - fill = blend(accent, note colour, e(p)), with ease-out `e(p) = 1 − (1 − p)²`;
  - take the accent from the theme tokens via the `nota-design` skill and never hard-code it.

  Notes at `Progress == 1` stay in the batched geometry.
- [X] T019 [US2] Visual check with the headless harness (quickstart.md §2): a stub `IAudioEngine.RecordHeldNotes`, the take shot at progress 0, 0.5 and 1 in Studio Dark and Studio Light. Keep the harness in /tmp, not in the repo. If 30 Hz looks stepped in the app, add the animation-only 60 Hz timer in `src/managed/Nota.App/MainWindow.axaml.cs`; it runs only while an entry is younger than 150 ms (contracts/ui-preview.md § Repaint rules)

**Checkpoint**: US1 + US2: notes grow and animate in

---

## Phase 5: User Story 3 — Held notes in the piano roll (Priority: P2)

**Goal**: The piano roll showing the take's clip draws held notes growing (FR-006)

**Independent Test**: Open the record clip in the piano roll, record, hold a key; the note grows on the grid. With another clip open, nothing is drawn (spec US3)

- [X] T020 [US3] In `src/managed/Nota.App/PianoRollView.cs`, add a held-note overlay:
  - a `RecordPreview` reference plus the take's clip start beat and a flag "this roll shows the record clip", set from `MainWindow.Detail.cs` when `_editorTrackId == RecordPreview.TrackId` and `_editorClipIndex` is the take clip;
  - draw on the grid control after the notes: x from `startBeat − clipStart` to `NowBeat − clipStart`, at the note's pitch row;
  - it is not hit-testable, not selectable and not part of `SetNotes`;
  - apply the `Progress` animation as in T018;
  - notes left of beat 0 are clipped at the left edge unless the roll shows negative beats.
- [X] T021 [US3] In `src/managed/Nota.App/MainWindow.Detail.cs` (`ReloadEditorNotes`, `:500`), find the take clip with `Engine.TryGetClipInfo` (its start may move left during recording, T014), pass the current clip start to the roll each recording tick, and invalidate the overlay. Clear the flag when recording stops

**Checkpoint**: The piano roll mirrors the arrangement preview

---

## Phase 6: User Story 4 — Routed MIDI and Session slots (Priority: P3)

**Goal**: The same preview for "MIDI In" capture and Session slot takes, including loop wrap (FR-004, FR-005)

**Independent Test**: (a) Record a track fed by routed MIDI and hold a note on the source. (b) Record into a Session slot and hold a note across the loop end (quickstart.md §1 table, §2.6)

### Tests for User Story 4

- [X] T022 [P] [US4] Smoke tests in `tests/Nota.SmokeTest/LiveRecordTests.cs`:
  - (a) a destination armed with `midiFromTrackId` set to a source track; a note held on the source shows in the read with `source = 1`, and on release is stored once;
  - (b) Session slot recording (`nota_session_record_slot`) with a key held: the read gives `session_scene` equal to the scene and a slot-local `start_beat` (0 ≤ start < slot length);
  - (c) N/A: a Session slot take launches without a count-in (`launchSlot`), so no pickup can precede it (research R5).

### Implementation for User Story 4

- [X] T023 [US4] In `src/native/nota.engine/src/Engine_Render.cpp`, `captureRoutedNotes` (`:495`) and its gate (`:682`), capture when `playing || countInActive_`, using the capture clock (T012) instead of `(blockStart + off) / spb` during the count-in. Routed notes already populate `recRoutedActive_`, so they enter the snapshot through T003
- [X] T024 [US4] Draw held notes in the recording Session slot in `src/managed/Nota.App/SessionView.Grid.cs`, near the take drawing at `:569`:
  - when `RecordPreview.SessionScene` equals the slot's scene and `TrackId` its track, draw notes at `x = startBeat / slotLen`;
  - draw a note whose `NowBeat < startBeat` (wrapped) as two pieces: start → slot end, and 0 → `NowBeat`;
  - use the slot's mini-note pitch mapping and the `Progress` animation (contracts/ui-preview.md § Session slot).

**Checkpoint**: All stories work

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T025 Performance check (quickstart.md §3):
  - stopped and idle, the repaint rate and CPU are unchanged versus `develop`;
  - over a 2-minute continuous MIDI take, `XrunCount` stays at 0 and UI CPU is within 10% of an audio take of equal length.

  Record the numbers in the PR description.
- [ ] T026 Run quickstart.md §2 end-to-end in the app or the UI verify harness, including the 1.1 head-reveal (move the clip right one bar, extend its left edge, and hear the pickup)
- [X] T027 [P] `CHANGELOG.md` `[Unreleased]`, Added: held notes show and grow while recording MIDI, with an appear animation. Changed: notes played on the count-in and keys held when recording starts are now recorded at their real position (#56)
- [X] T028 [P] Update FEATURES.md via the `nota-features` skill (live MIDI recording preview; count-in pickups recorded)
- [X] T029 [P] Update the user docs via the `nota-docs` skill: the recording page (EN + RU) gets live notes and count-in pickups, with a re-shot screenshot of a take with growing notes
- [X] T030 Shortcuts via `nota-shortcuts`: N/A, no bindings change

### Nota closing tasks *(PR gates)*

- [X] T031 Run `scripts/build.sh` until native, managed and the smoke test (including `LiveRecordTests`) are green
- [X] T032 AGPL-3.0-only two-line SPDX header on the new files (`tests/Nota.SmokeTest/LiveRecordTests.cs`, `src/managed/Nota.App/RecordPreview.cs`)
- [X] T033 New dependency: N/A (none added)
- [X] T034 Engine behaviour change is covered by `tests/Nota.SmokeTest/LiveRecordTests.cs` (T009, T010, T011, T022)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)** → **Foundational (T002–T009)** → user stories → **Polish**.
- In Foundational, T002 → T003 → T004 → T005 → T006, then T007 → T008. T009 needs T005 and T006.

### User Story Dependencies

- **US1 (P1)**: after Foundational. T012 → T013 (the seeding uses the clock); T014 and T015 can follow in either order; T016 needs T007 and T008.
- **US2 (P2)**: needs US1's arrangement drawing (T016) to animate it. T017 → T018 → T019.
- **US3 (P2)**: after Foundational, independent of US1's engine work for live input. T020 → T021. It shares the `Progress` API from T017; without US2 it draws unanimated.
- **US4 (P3)**: T023 needs T012 (the capture clock). T024 needs T007.

### Parallel Opportunities

- T006 (managed interop) runs alongside T007 once T005's signature is fixed.
- The test tasks T010, T011 and T022 can be written together because they are independent sections of one new file. Merge them carefully.
- After Foundational: engine work (T012–T015, T023) and UI work (T016, T020, T024) touch different files and can run in parallel.
- T027, T028 and T029 are independent.

---

## Parallel Example: User Story 1

```bash
# Engine and UI halves of US1 side by side:
Task: "T012–T015 capture clock, seeding, clip left move, negative-start render in src/native/nota.engine/src/"
Task: "T016 draw held notes in src/managed/Nota.App/ArrangementView.LaneControl.cs"
# Tests together:
Task: "T010 + T011 in tests/Nota.SmokeTest/LiveRecordTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001, then T002–T009.
2. T010–T016. Then run `scripts/build.sh` and quickstart §2 steps 1–4 without the animation.
3. Stop and validate: held notes grow and early notes are kept.

### Incremental Delivery

1. Add US2 (animation), then US3 (piano roll), then US4 (routed and Session), validating each at its checkpoint.
2. Finish with Polish and the closing gates.

---

## Notes

- Keep every audio-thread change allocation-free and lock-free (AR-6). The snapshot writer never waits.
- Don't commit. The user reviews and commits (project memory).

## Phase 8: Convergence

- [X] T035 Return no held notes (track 0) from `Engine::recordHeldNotes` in `src/native/nota.engine/src/Engine_Recording.cpp` while an audio take records into a Session slot (`recordSessionAudio_`), and publish nothing for it in `publishHeldSnapshot` — today `recordTrackId_` from an earlier MIDI take is never reset, so live keys played during a Session audio take preview on that stale arrangement track; cover it in `tests/Nota.SmokeTest/LiveRecordTests.cs` per FR-007 (contradicts)
- [X] T036 Seed routed-MIDI notes still held on the source when recording starts (`recRoutedActive_` from the source's held notes, start at their press beat or the take's first moment) in `src/native/nota.engine/src/Engine_Render.cpp`, as research R3 plans for routed input, with a smoke check in `tests/Nota.SmokeTest/LiveRecordTests.cs` per FR-012 / plan: research R3 (partial)
- [X] T037 Review the Session recording cell drawing the slot's already-stored notes in `src/managed/Nota.App/SessionView.Grid.cs` (`DrawRecordingNotes`) — contracts/ui-preview.md only asks for the held notes there; keep it (and note it in the contract) or draw held notes only per FR-006 (unrequested)
