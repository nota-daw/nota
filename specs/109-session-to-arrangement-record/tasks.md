---

description: "Tasks: Record a Session Performance into the Arrangement (#971)"
---

# Tasks: Record a Session Performance into the Arrangement

**Input**: Design documents from `specs/109-session-to-arrangement-record/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/c-abi.md, quickstart.md

**Tests**: Included — the constitution (IV) requires engine behaviour changes to extend
`tests/Nota.SmokeTest`. All tests go in `tests/Nota.SmokeTest/SessionToArrangementTests.cs`
(device-free: `RenderOffline` + `Poll` at 48 kHz, 120 BPM, 4/4, launch quantize 1 bar).

**Organization**: by user story (spec.md). Engine paths are relative to
`src/native/nota.engine/src/engine/` unless written in full.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Create `src/native/nota.engine/src/engine/SessionPass.h` and `SessionPass.cpp` with the SPDX header and a file comment (why it exists: spans of a Session performance → Arrangement clips), and add `src/engine/SessionPass.cpp` next to `SessionPlayer.cpp` in `src/native/nota.engine/CMakeLists.txt`
- [X] T002 [P] Create `tests/Nota.SmokeTest/SessionToArrangementTests.cs` (SPDX header, `internal static class SessionToArrangementTests` with `Run()`), plus helpers: `Engine(out tracks)` builds a project with two instrument tracks + one audio track and slots, `Bars(e, n)` renders n bars in 256-frame chunks calling `Poll()`. Register it in `tests/Nota.SmokeTest/Program.cs` in the full run and as a `--session-to-arrangement` fast-iteration switch, like `SlotTakeLengthTests`

---

## Phase 2: Foundational (blocking all stories)

- [X] T003 Define in `SessionPass.h`: `struct LaunchEvent { int32_t trackId; int32_t slot /* -1 = stopped */; double beat; double slotPos; float gain; uint32_t epoch; }`, `struct PlayedSpan { int32_t trackId, slot; double start, end /* NaN = open */; double slotPos; float gain; }`, and `class SessionPass` holding `active`, `performanceOnly`, `std::vector<PlayedSpan> spans`, `tempoBefore`, `std::vector<std::pair<double,double>> tempoSteps`, `int32_t dropped` (data-model.md)
- [X] T004 Implement `SessionPass::onEvent(const LaunchEvent&)` in `SessionPass.cpp`: it closes the track's open span at `beat` and, if `slot >= 0`, opens one with `slotPos`/`gain`. Also implement `closeAll(beat)`, `seam(loopEnd, loopStart)` (close every open span at loopEnd, reopen the same slots at loopStart with the position carried on), and `dropTrack(id)`. Drop spans with `end - start <= 0`
- [X] T005 Add to `Engine.h`: `SpscRingBuffer<LaunchEvent, 1024> launchEvents_`, `SessionPass sessionPass_` (message thread), `bool rtPassActive_` and `bool rtPassOpened_` (audio thread), and a counter for dropped pushes. The audio-thread flag is set/cleared through the existing `CommandType::TakeRestart` path or a new `CommandType::SessionPass` command (AR-4)
- [X] T006 In `Engine_Render.cpp` (session apply site, ~line 3214): when `sp->maybeApply(...)` returns true and `rtPassActive_`, push a `LaunchEvent` (track id, `sp->playing`, beat, `sp->localBeats`, `sp->gain`, `rtTakeEpoch_`). For a quantized request use the grid beat `ceil(blockStart/q)·q` clamped inside the block; for quantize None, `kStopNow` or a loop seam use the block start (research R1). Treat a monitored audio track (`t.monitor()`) as stopped. No allocation; a full ring increments the dropped counter
- [X] T007 In `Engine_Render.cpp`: on the first block after `rtPassActive_` rises (`!rtPassOpened_`), push one opening event per track whose slot is playing (beat = block start, `slotPos = localBeats`), then set `rtPassOpened_` (research R8)
- [X] T008 In `Engine::poll()` (`Engine_Render.cpp:475`): drain `launchEvents_` and drop events whose epoch ≠ `takeEpoch_`. Feed `sessionPass_.onEvent`. When the transport loop wrapped during a pass, call `sessionPass_.seam(loopEnd, loopStart)`
- [X] T009 Start/stop plumbing in `Engine_Recording.cpp`: `startArrangementTake` sets `sessionPass_ = {active: true, tempoBefore: authoringBpm_}` and raises the audio flag. `setRecording(false)` calls `sessionPass_.closeAll(stop beat)` then `writeSessionPass()` (US1) and clears the flag. A transport stop during a pass ends it the same way
- [X] T010 Engine self-test `sessionPassSelfTest()` (pure, no audio) in `SessionPass.cpp`: a sequence of events → expected spans (launch, switch, stop, legato offset, zero-length drop, seam). Expose it through the existing self-test entry used by the smoke test

**Checkpoint**: launches during a take become spans; nothing is written yet.

---

## Phase 3: User Story 1 — Play clips in Session, get them on the timeline (P1) 🎯 MVP

**Goal**: every played span becomes Arrangement clip(s) at its real beats, sounding as the slot did.

**Independent Test**: quickstart case 1 + render compare (SC-001, SC-002).

### Tests for User Story 1

- [X] T011 [P] [US1] `TwoTracks()`: Record from bar 1, launch a 2-bar slot at once and a 1-bar slot during bar 4, stop it during bar 8, stop at bar 13. Check: one MIDI clip 0–48 beats holding 6 repetitions of the notes; a clip 16–32 holding 4 repetitions; edges exactly on beats
- [X] T012 [P] [US1] `RenderMatches()`: render the live pass, then Back to Arrangement, seek 0, render the same length. Peak/RMS per bar equal within the smoke test's deterministic-render tolerance (use audio slots or a deterministic instrument, per memory smoke-level-tests)
- [X] T013 [P] [US1] `LegatoAndMidPlay()`: a slot playing before Record → the clip starts at the Record beat with the content offset. A Legato launch → the clip starts inside the slot at the carried position
- [X] T014 [P] [US1] `FollowAndScenes()`: a scene launch writes clips on all its tracks at one beat. A follow action A→B writes two clips back to back. A cancelled queued launch (specs/104 API) writes nothing
- [X] T015 [P] [US1] `AudioSlot()`: a 1-bar audio slot played 3 bars → 3 audio clips back to back, same sample, gain kept. A warped slot keeps its warp

### Implementation for User Story 1

- [X] T016 [US1] `SessionPass.cpp`: `MidiClip unrollMidi(const SessionSlot&, const PlayedSpan&)`. It returns one clip `[start,end)` named after the slot. Notes, controller curves, articulation timeline and note expression are repeated every `L = slot.lengthBeats`, shifted by `k·L − slotPos`, and cut to `[0, end−start)`. A note crossing a seam or the end is cut there. `gain`: apply the same velocity/level factor the Session MIDI path uses (check `Engine_Render.cpp` session instrument path; if gain is applied at output level, scale note velocities by it). Reuse `midiSlice` for the cutting where it fits
- [X] T017 [US1] `SessionPass.cpp` / `Engine_Tracks.cpp`: `std::vector<AudioClip> repeatAudio(const SessionSlot&, const PlayedSpan&, ...)`. It returns one `AudioClip` per repetition, placed at `start − slotPos + k·L` and cut with `audioSlice(c, max(a,start), min(b,end), spb, devSR, tempoMap())`, with `gain *= span.gain`. Expose `audioSlice`/`midiSlice` to `SessionPass.cpp` (static helpers today → declare in a shared header or keep the call on the Engine)
- [X] T018 [US1] `Engine::writeSessionPass()` in `Engine_Recording.cpp`: group the spans by track. For each track with spans: `noteTakeTrack(id)`, clone the track once, apply the replace for each span (T021 lands the replace; in this task insert the clips), insert the written clips, and publish one snapshot per track (`republishWithTrack`). Skip tracks that have been deleted. Clear `sessionPass_`
- [X] T019 [US1] Slots that are edited during the pass: read slot content from `authoring_` at write time (spec assumption), so the written clips hold the slot as it is when the pass ends
- [X] T020 [US1] Make `TransportViewModel` refresh after a pass: `TracksChanged` already fires on Record off. Check that it also fires when the transport stops a pass (`src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`, Stop paths)

**Checkpoint**: MVP — Session performance lands on the timeline (may still overlap old clips).

---

## Phase 4: User Story 2 — Only what was played is replaced (P1)

**Independent Test**: quickstart "Replace" (US2, SC-005).

- [X] T021 [US2] In `writeSessionPass()`, before inserting the clips of a span `[a,b)` on a track, cut the track's own clips at a and b and remove what lies between. Do it on the cloned track: refactor the per-track body of `splitClipsAtRange` / `deleteClipsInRange` (`Engine_Tracks.cpp:2068`, `Engine.h:653`) into a helper that works on a `Track&` without pushing undo or publishing. Clips only (no automation, FR-005/US2 s3)
- [X] T022 [P] [US2] Test `ReplaceOnlyPlayed()`: an 8-bar Arrangement clip from bar 1, slot played bars 3–5 → pieces 0–8, written 8–16, kept 16–32 beats. A second track with Arrangement content and no launch is identical (note count, positions, lengths) after the pass. Automation lanes on the played track are unchanged

---

## Phase 5: User Story 3 — The pass is one take: input, automation, undo, retry (P2)

**Independent Test**: quickstart Undo/Redo, ⌘. retry, armed instrument, monitored audio, Session slot take.

### Tests

- [X] T023 [P] [US3] `ArmedMerge()`: an armed instrument track plays a slot. Notes played through the live note path during the span → in the written clip at their beats. Notes played before the launch → stay in the take clip. Notes that existed before the pass inside the span → gone
- [X] T024 [P] [US3] `MonitoredAudio()`: an armed audio track with monitoring on and a playing slot → no written clip, the input take is recorded (internal resampling input) *(N/A as built: an armed audio track's take wins and its slot writes nothing — spec Edge Cases)*
- [X] T025 [P] [US3] `UndoRedo()`: a pass with an armed track, two launches and an automation move → `UndoDepth` grows by exactly 1. Undo restores clips, lanes and the Tempo lane to the pre-pass state. Redo brings them back (SC-004)
- [X] T026 [P] [US3] `Retry()`: ⌘. (`RetryTake`) mid-pass → result Arrangement, no written clips, the replaced clip is whole again, the second attempt writes its own launches only (FR-011)
- [ ] T027 [P] [US3] `SlotTakeDuringPass()`: Session rec into an empty slot during the pass → the slot keeps its take, and the Arrangement holds it from the recording start, including the loop after it *(open: Session rec during a pass — the code path is covered by launch capture, no dedicated test yet)*

### Implementation

- [X] T028 [US3] Undo: `beginUndoGroup()` in `startArrangementTake` when the pass starts; `endUndoGroup()` at the end of `writeSessionPass()`. Make sure every exit path closes it (start failure, discardTake, retry restart, transport stop) so `undoGroupDepth_` is balanced. Keep `take` flags of collapsed entries (specs/078)
- [X] T029 [US3] Retry: in `discardTake()` (`Engine_TakeRetry.cpp:112`) drain `launchEvents_` and reset `sessionPass_` (keep `active`, which the restart re-opens, and clear the spans). The tracks come back through `baseTracks` because `writeSessionPass` calls `noteTakeTrack` before writing. For a retry that happens before the write, nothing has been written
- [X] T030 [US3] Armed merge (research R4): in `writeSessionPass()`, for an armed instrument track that recorded MIDI this take, move the take's notes, controller points and expression inside each span from the take clip into that span's written clip (timeline beats → clip-relative) before the replace cuts the take clip. Identify take notes through `takeNotes_` (or the lane's recorded batch) so that notes from before the pass are not treated as take notes
- [X] T031 [US3] Make sure the materialisation order is right: `stopAudioRecording()` / MIDI fold finish before `writeSessionPass()` reads the tracks, so a monitored-off audio slot is written first and the audio take lands over it as takes replace material today. Document the order in a comment in `setRecording`
- [X] T032 [US3] Session slot take during a pass: the take's slot launch goes through `maybeApply` and is captured like any launch. Check that the slot content read at write time is the landed take (`finishSessionMidiLane` / `finishCaptureLane` ran first)

---

## Phase 6: Performance-only pass (Clarification Q2; FR-015)

- [X] T033 In `startArrangementTake` (`Engine_Recording.cpp:978`): when nothing is armed and any track's `sessionPlayer` has `playing >= 0` or `pendingSlot() >= 0`, start a performance-only pass. Open the take attempt, set the count-in / roll the transport exactly as a take does, set `sessionPass_.performanceOnly`, `recording_ = true`, and `recordStartStatus_ = 3`, then return true. Otherwise keep status 1. Make sure `isRecording()` and Record-off handle a pass with no lanes (no audio/MIDI materialisation)
- [X] T034 [P] Doc comment of `nota_engine_record_start_status` in `src/native/nota.engine/include/nota/nota_engine.h`: add "3 ok — a performance-only pass (nothing armed; records the Session performance)" (contracts/c-abi.md). Mirror in `src/managed/Nota.Application/Engine/IAudioEngine.cs` (doc on `RecordStartStatus`)
- [X] T035 `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs` `OnRecordOnChanged`: when `IsRecording` and `RecordStartStatus == 3`, show `Loc.T("transport.recording_session_performance")` ("● Recording the Session performance") instead of the usual message. Add the key to `src/managed/Nota.Application/Localization/en.json`
- [X] T036 [P] `src/managed/Nota.Mcp/Tools/TransportTools.cs`: the tool that starts recording reports "performance-only pass (records the Session performance)" for status 3 *(N/A: the MCP server has no recording tool; nothing reports the status)*
- [X] T037 [P] Test `NothingArmed()`: no armed track and a slot playing → `SetRecording(true)` records, status 3, and after Stop the slot is written. No armed track and Session silent → status 1 (the auto-arm path is unchanged)

---

## Phase 7: User Story 4 — Listen back with one click (P2)

- [X] T038 [US4] Test `BackToArrangementAfter()`: slots playing at Stop → after the pass `sessionPlayingSlot` is unchanged (Session still holds the tracks). `BackToArrangement()` then seek 0 and render → the Arrangement clips play (non-silent where written)

---

## Phase 8: User Story 5 — Scene tempo → Tempo lane (P3)

- [X] T039 [US5] `applySceneTempo()` (`Engine_Session.cpp:1414`): when `sessionPass_.active`, a tempo > 0 and `!tempoMap()`, append `(sceneTempoAt_, sceneTempo_)` to `sessionPass_.tempoSteps`
- [X] T040 [US5] In `writeSessionPass()`: if `tempoSteps` is non-empty, call `noteTakeTrack(kMasterTrackId)`, `addAutomationLane(kMasterTrackId, Tempo)`, and set points: `tempoBefore` at beat 0, then for each step two points on its beat (previous bpm, new bpm), an instant change (specs/099 point convention). This happens inside the undo group
- [X] T041 [P] [US5] Test `SceneTempo()`: at 120 BPM with no lane, launch a scene with tempo 100 at bar 5 → the Tempo lane holds 120 → 100 at beat 16. A project with an existing Tempo lane → its points are unchanged. A scene with a signature → no lane written for it

---

## Phase 9: Edge cases (spec Edge Cases)

- [X] T042 [P] Test `LoopPass()`: loop bars 1–5 on, two loop passes with different slots → the loop range holds the second pass's clips only (FR-012)
- [ ] T043 [P] Test `CountIn()`: Record from stop with a 1-bar count-in and a launch queued during the count-in → the clip starts at the pass start beat *(open)*
- [X] T044 [P] Test `ShortPlay()` and `MutedTrack()`: a launch stopped after half a beat (quantize None) → a clip half a beat long. A muted track's slot is written

---

## Phase 10: Polish & Cross-Cutting

- [X] T045 [P] `CHANGELOG.md` `[Unreleased]` → Added: "Recording: the Session performance is recorded into the Arrangement …" (one entry, user wording)
- [X] T046 [P] FEATURES.md via the `nota-features` skill (Session + Recording sections, MCP status note)
- [X] T047 [P] User docs via the `nota-docs` skill in `../nota-docs` (Session page: recording into the Arrangement; Recording page: performance-only pass) — EN + RU pages *(../nota-docs-971, branch docs/session-to-arrangement-971)*
- [X] T048 [P] RU string for `transport.recording_session_performance` in the translations repo (as for earlier features) *(../nota-translations-971, stacked on ru/session-grid-orientation-968)*
- [ ] T049 Perf: run the perf instruction gate locally if the render-loop change is visible (memory: nota-perf-gate-local). The event push must stay behind the `rtPassActive_` branch *(open)*

### Review follow-up (#972)

- [X] T056 Show the pass while it records (FR-014): `nota_engine_session_pass_spans` (open spans end at the playhead) → `IAudioEngine.SessionPassSpans`; `ArrangementView.LaneControl.DrawPassSpans` draws each span as the clip it will write (slot name, colour, notes unrolled) on a lane-coloured base. Smoke: the mid-take readout in `TwoTracks`; headless shot in /private/tmp/n971harness

- [X] T057 Audio spans while recording show the slot's waveform, one piece per repetition (`PassAudioPieces`, slot peaks via `GetSessionSlotPeaks`); smoke `WarpedAudioSlot` covers a warped slot written and rendered like the performance
- [X] T058 Review fixes (PR #972): one-shot end reported, held notes kept, controller ramps and take controllers, envelopes not copied, disarm keeps the take, undo anchor with a full history and the pre-pass checkpoint, transport Stop ends the pass, launch position saved at apply; live view reads every span, builds only visible repetitions, refreshes slot data, skips armed audio spans, draws warped slots warped. Smoke: `OneShotEnds`, `HeldNoteAtRecord`, `DisarmKeepsTake`, `TakeControllers`, `TransportStopWrites`, `FullUndoHistory`; self-test checks 13–18

### Nota closing tasks

- [X] T050 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T051 SPDX two-line header on `SessionPass.h`, `SessionPass.cpp`, `SessionToArrangementTests.cs`
- [X] T052 New dependency: N/A
- [X] T053 CHANGELOG entry (T045)
- [X] T054 FEATURES.md / docs / shortcuts (T046, T047; shortcuts N/A — no new key)
- [X] T055 Smoke test extended (Phases 3–9)

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → US1 (Phase 3) → US2 (Phase 4) → US3 (Phase 5).
- Phase 6 (performance-only), US4 (Phase 7) and US5 (Phase 8) depend on Phase 3 only.
- Phase 9 needs Phases 3–5. Phase 10 comes last.
- Within a phase, tests [P] can be written together. Implementation tasks touching `Engine_Recording.cpp` run in order.

## Parallel Example: User Story 1

```text
T011, T012, T013, T014, T015 (tests in one file, independent functions)
T016 (MIDI unroll) ∥ T017 (audio repeat) — different functions in SessionPass.cpp
```

## Implementation Strategy

MVP = Phases 1–4 (US1 + US2): the performance lands on the timeline and replaces only what was
played. Then US3 (one take: undo, retry, armed merge), the performance-only pass, US4/US5, the
edge cases, then docs.
