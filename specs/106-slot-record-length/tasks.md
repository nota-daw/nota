---
description: "Task list for specs/106 — next-take length for an empty Session slot (#503)"
---

# Tasks: Next-Take Length for an Empty Session Slot

**Input**: Design documents from `specs/106-slot-record-length/` — [spec.md](spec.md),
[plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/abi.md](contracts/abi.md), [contracts/mcp.md](contracts/mcp.md), [quickstart.md](quickstart.md)

**Tests**: Engine behaviour changes, so the smoke test is extended (constitution IV). Smoke
checks live in a new `tests/Nota.SmokeTest/SlotTakeLengthTests.cs` (pattern:
`RetryTakeTests.cs` — a static `Run()` yielding `(bool ok, string label)`), run from
`tests/Nota.SmokeTest/Program.cs` next to `RetryTakeTests.Run()` (both call sites, ~1046 and ~16096).

**Organization**: by user story (spec.md). Worktree: `~/repos/nota/nota-503`, branch
`feat/slot-record-length`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different files, no dependency on an unfinished task
- **[Story]**: US1–US4 from spec.md

---

## Phase 1: Setup

- [X] T001 Create `tests/Nota.SmokeTest/SlotTakeLengthTests.cs` with the two-line AGPL-3.0-only SPDX header, a comment header naming #503 / specs/106, and an empty `public static IEnumerable<(bool, string)> Run()`; call it from both `RetryTakeTests.Run()` sites in `tests/Nota.SmokeTest/Program.cs`

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the per-slot value, its ABI and managed mirror, and the take-length plumbing.

- [X] T002 Add `enum class SlotTakeMode : int32_t { Global = 0, Open = 1, Fixed = 2 }` and `struct NextTakeLength { SlotTakeMode mode = SlotTakeMode::Global; int32_t bars = 0; int32_t beats = 0; }` and a field `NextTakeLength nextTake;` on `SessionSlot` (after `stopButton`, comment: "the slot's next fresh take: belongs to the slot, kept hidden under a clip (#503)") in `src/native/nota.engine/src/engine/Track.h`
- [X] T003 Declare in `src/native/nota.engine/src/engine/Engine.h`: `bool sessionSlotTakeLength(int32_t trackId, int32_t scene, NextTakeLength& out) const;`, `bool setSessionSlotTakeLength(int32_t trackId, int32_t scene, const NextTakeLength& v);`, `double sessionSlotTakeBeats(int32_t trackId, int32_t scene) const;`, private `double resolveTakeBeats(const SessionSlot* s) const;`, private `static void replaceSlotContent(SessionSlot& dst, SessionSlot src);`, member `double recordSessionLen_ = 0.0; // resolved length of the running Session take, beats (0 = open)`, and `double lenBeats = 0.0;` on `MidiTakeLane`, `double lenBeats = 0.0, startBeat = 0.0;` on `CaptureLane`
- [X] T004 Implement in `src/native/nota.engine/src/engine/Engine_Session.cpp` (next to `setSessionSlotStopButton`): getter (false for unknown track/scene; a slot past `sessionSlots.size()` reads Global); setter validating per data-model — "`Fixed` needs `bars*bpb + beats >= 1` and `bars*bpb + beats <= 64*bpb` (64 bars max); `beats < bpb` for the current meter. `Global`/`Open` ignore and store 0/0" (bpb = `transport_.beatsPerBar()`), returning false on invalid input or unknown mode, `true` without publishing when unchanged, else `cloneTrack` + resize slots to `sceneCount` + assign + `republishWithTrack` (one undo step); `resolveTakeBeats`: null/Global → `sessionRecordLen_`, Open → 0, Fixed → `bars * transport_.beatsPerBar() + beats`; `sessionSlotTakeBeats` → `resolveTakeBeats(slotAt(...))`; `replaceSlotContent` keeps `dst.nextTake`
- [X] T005 Add `NotaSlotTakeLength`, the `NOTA_SLOT_TAKE_*` defines and the three functions exactly as in [contracts/abi.md](contracts/abi.md) to `src/native/nota.engine/include/nota/nota_engine.h` (after `nota_session_set_slot_stop_button`), and their wrappers in `src/native/nota.engine/src/abi/nota_engine_session.cpp` (null checks; set → `NOTA_OK` / `NOTA_ERR_INVALID_ARG`)
- [X] T006 [P] Add P/Invoke entries (`[StructLayout(Sequential)] NotaSlotTakeLength`, `SessionGetSlotTakeLength`, `SessionSetSlotTakeLength`, `SessionSlotTakeBeats`) in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Session.cs`
- [X] T007 Add `public enum SlotTakeMode { Global, Open, Fixed }` and the blittable `NotaSlotTakeLength` struct (as built: matches the ABI like `NotaSessionClipProps`, in `EngineContracts.cs`) and to `IAudioEngine`: `bool TryGetSessionSlotTakeLength(int trackId, int scene, out SlotTakeLength value)`, `bool SetSessionSlotTakeLength(int trackId, int scene, SlotTakeLength value)`, `double SessionSlotTakeBeats(int trackId, int scene)` in `src/managed/Nota.Application/Engine/IAudioEngine.cs`; implement them in `src/managed/Nota.Infrastructure/Interop/NotaEngine.Session.cs`; add no-op/fake implementations wherever else `IAudioEngine` is implemented (grep `: IAudioEngine` in src and tests)
- [X] T008 Smoke checks in `tests/Nota.SmokeTest/SlotTakeLengthTests.cs`: default Global; set Fixed 3/0 reads back; Open reads back with bars/beats 0; invalid inputs refused and leave the value (Fixed 0/0, 65/0, 64/1, beats = 4 in 4/4, mode 7, unknown track, scene ≥ count); `SessionSlotTakeBeats` = 12 for Fixed 3 bars in 4/4, = global `SessionRecordLength` for Global, = 0 for Open; in 7/8 Fixed 3 bars → 21

**Checkpoint**: value stored, validated, reachable from C#; nothing records with it yet.

---

## Phase 3: User Story 1 — Prepare a length for one empty slot and record it (P1) 🎯 MVP

**Goal**: a take into a prepared empty slot (MIDI or audio, alone or with other slots) runs exactly its prepared length; nothing else changes.

**Independent test**: global Fixed Off, slot A = 3 bars, record at 120 BPM 4/4, quant 1 bar → clip 12 beats, stopped by itself, global still 0 (quickstart steps 1–4).

- [X] T009 [US1] In `recordSessionSlot` (`src/native/nota.engine/src/engine/Engine_Session.cpp` ~370): for an empty slot compute `const double len = resolveTakeBeats(slotAt(*t, scene))`, set `recordSessionGrowing_ = len <= 0.0`, create the clip with `recordSessionGrowing_ ? kGrowingTakeBeats : len`, store `recordSessionLen_ = len`; for an overdub into a filled slot set `recordSessionLen_ = 0` (FR-017); set `midiLanes_[0].lenBeats = recordSessionLen_`. In the audio branch of the same function set `recordSessionLen_ = resolveTakeBeats(slot)` before `startAudioRecording`. Reset `recordSessionLen_ = 0` wherever `recordSessionGrowing_ = false` is reset for a finished/cleared take (Engine_Session.cpp ~478/~499, `Engine_TakeRetry.cpp` ~142)
- [X] T010 [US1] In `addSessionMidiLane` (`src/native/nota.engine/src/engine/Engine_Recording.cpp` ~768): replace `sessionRecordLen_` with `resolveTakeBeats` of the lane's slot for `growing` and the clip length; set `l.lenBeats` (0 for a filled slot)
- [X] T011 [US1] In `addSessionAudioLane` (`src/native/nota.engine/src/engine/Engine_Recording.cpp`): set the new lane's `lenBeats = resolveTakeBeats(slot)` and `startBeat = transport_.uiPositionBeats()`; for the main audio lane created by `startAudioRecording` in a Session take set `lenBeats = recordSessionLen_`
- [X] T012 [US1] In the Session-slot branch of audio materialise (`src/native/nota.engine/src/engine/Engine_Recording.cpp` ~1568) replace `sessionRecordLen_ > 0.0 ? sessionRecordLen_ : …` with the lane's `lenBeats > 0.0 ? lane.lenBeats : roundedTakeBeats(s.lengthBeats)`
- [X] T013 [US1] Rewrite the fixed-length block of `serviceSessionFollow` (`src/native/nota.engine/src/engine/Engine_Session.cpp` ~1188): drop the `sessionRecordLen_ > 0.0` gate; MIDI lanes (when `openMidiLaneCount() > 1`) stop when `l.lenBeats > 0 && playedBeats >= l.lenBeats - 1e-6`; audio lanes other than the main one (`captureLanes_.size() > 1`, `scene >= 0`, `trackId != recordSessionTrackId_`) stop via `stopSessionRecordSlot(trackId)` when `lenBeats > 0 && uiPositionBeats() - startBeat >= lenBeats - 1e-6`; the main take stops when `recordSessionLen_ > 0 && el >= recordSessionLen_ - 1e-6` (el from `sessionRecordTarget`, unchanged)
- [X] T014 [US1] Grep `src/native/nota.engine/src` for any remaining read of `sessionRecordLen_` outside `resolveTakeBeats`, the setter/getter and nothing else; route each through the resolved per-take value
- [X] T015 [P] [US1] Grid label: in the slot cell (`src/managed/Nota.App/SessionView.Grid.cs` ~342, where `_stopButton` is read) read `TryGetSessionSlotTakeLength`; an empty, non-recording slot whose mode is Fixed draws its length ("3 bars", "2.2" style via a shared formatter: whole bars → `Loc.F("session.take_length.bars", n)`, else `bars.beats`) and Open draws `Loc.T("session.take_length.open")`, small tertiary caption text per DESIGN.md (no new colours); Global draws nothing new; refresh with `RefreshSlot`
- [X] T016 [US1] Inspector row in `BuildEmptyInspector` (`src/managed/Nota.App/SessionView.Inspector.cs` ~436), after the Stop-button explanation: `Row(Loc.T("session.inspector.take_length"), Chip(label, mode != Global, a => Pick(a, items, …)))` with items Global (labelled with its resolved value, see T020), Open, 1, 2, 3, 4, 8, 16 bars; picking calls `SetSessionSlotTakeLength` then `RefreshSlot` + `RebuildInspector`
- [X] T017 [US1] Same inspector: when the mode is Fixed, a second row with two `DragNumber`s — bars `min 0 max 64 step 1`, beats `min 0 max bpb-1 step 1` (`Meter.BeatsPerBar`) — each gesture bracketed by the engine undo group (as other inspector `DragNumber`s do) so a drag is one undo step; a combination the engine refuses (total 0, over 64 bars) leaves the value and shows the caption in T018 in the warning style
- [X] T018 [US1] Same inspector: caption line (style of the Stop-button explanation, FontSize 10, TextTertiary, wrap): `Loc.T("session.inspector.take_length_hint")` = "Applies to the next take recorded into this slot: it starts on the launch boundary and stops after this length. 1 beat to 64 bars."
- [X] T019 [P] [US1] EN strings in `src/managed/Nota.Application/Localization/en.json`: `session.inspector.take_length` "Take length", `session.inspector.take_length_hint` (T018), `session.take_length.open` "Open", `session.take_length.global` "Global", `session.take_length.global_value` "Global ({0})", `session.take_length.bars` "{0} bars", `session.take_length.bar` "1 bar"; run the localization/label allow-list checks the smoke test runs (`DesignTokenCheck.Localization.cs`)
- [X] T020 [US1] Smoke checks in `tests/Nota.SmokeTest/SlotTakeLengthTests.cs` (120 BPM 4/4, quant 1 bar, global 0): instrument track armed, slot A = Fixed 3 bars, slot B (other track) = 8 bars; `RecordSessionSlot(A)` → `SessionSlotLength == 12`, run past 12 beats + boundary → not recording, state playing; B still Fixed 8, global still 0, an existing clip's length unchanged; with global 16 a Fixed 3 slot still makes 12; multi-slot (specs/086): two instrument slots 1 bar and 2 bars recording together → first lands after 4 beats while second still records, second lands after 8; audio slot via the test capture lane (`beginTestLane`, see `MultiInputSessionTests.cs`) Fixed 1 bar → kept clip length 4 and recording stopped

**Checkpoint**: MVP — per-slot lengths record correctly; the UI sets and shows them.

---

## Phase 4: User Story 2 — Inherit the global setting, or record open-ended (P1)

**Goal**: Global follows the toolbar exactly as today; Open overrides a global Fixed.

**Independent test**: old project → all Global, same behaviour; global 2 bars + Open slot → runs until stopped.

- [X] T021 [US2] Inspector Global item label: `Loc.F("session.take_length.global_value", resolved)` where resolved is the global `SessionRecordLength` formatted as bars or `Loc.T("session.take_length.open")` when 0, in `src/managed/Nota.App/SessionView.Inspector.cs`; rebuild the empty-slot inspector when the toolbar Fixed changes (`SessionView.Toolbar.cs` `SyncToolbar`/the Fixed segment's setter → `RebuildInspector()`)
- [X] T022 [US2] Smoke checks in `tests/Nota.SmokeTest/SlotTakeLengthTests.cs`: Global slot with global 8 beats → 8-beat take (existing behaviour); change global to 16 → `SessionSlotTakeBeats` 16; global 8 + Open slot → after 12 beats still recording, stop → rounded to whole bars (`roundedTakeBeats`); setting Fixed then Global → reads Global
- [X] T023 [US2] Confirm the existing Session Rec smoke block (`tests/Nota.SmokeTest/Program.cs` ~9346–9364, open-ended + global Fixed 8) passes unchanged — FR-014 / SC-003 regression guard

---

## Phase 5: User Story 3 — The prepared length across cancel, retry, keep, Undo and save (P2)

**Goal**: the value is a slot instruction: kept across cancel/retry/keep, back after a clip goes, Undo/Redo-able, saved.

**Independent test**: quickstart steps 6–10.

- [X] T024 [US3] Keep `nextTake` at every whole-slot overwrite (research R2) in `src/native/nota.engine/src/engine/Engine_Session.cpp`: `clearSessionSlot` (`SessionSlot{}` → keep `nextTake`), `copySessionSlot` (`= copy` → `replaceSlotContent`), `arrangementClipToSession` and `arrangementAudioClipToSession` (`= slotFrom…` → `replaceSlotContent`)
- [X] T025 [US3] Inspect the slot-building sites in `src/native/nota.engine/src/engine/Engine_Render.cpp` (~4291, ~4437) and any other `sessionSlots[...] =` assignment (grep); where one replaces an existing slot's content, use `replaceSlotContent`; leave `cancelSessionCountIn` and the retry restore (`Engine_TakeRetry.cpp` ~175) as they are (they restore the same slot's earlier state)
- [X] T026 [US3] Persistence: add `public List<SlotTakeLengthDto>? SessionTakeLengths { get; set; }` to `TrackDto` and `public sealed class SlotTakeLengthDto { public int Scene; public int Mode; public int Bars; public int Beats; }` (properties, matching the file's style) in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`; in `ProjectService.Capture` (~289, the Session-slots loop) write every non-Global slot, filled or empty ("Written for every non-Global slot, filled or empty"); in load (~963, after `SessionNoStop`) apply each with `SetSessionSlotTakeLength`, warning (not failing) on a refused entry, in `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs`
- [X] T027 [US3] Smoke checks in `tests/Nota.SmokeTest/SlotTakeLengthTests.cs`: (a) cancel before start — transport stopped with count-in on, record into A (Fixed 3), cancel the count-in → A empty, still Fixed 3, other clips' lengths unchanged; (b) keep → A holds a 12-beat clip and `TryGetSessionSlotTakeLength` still Fixed 3; Undo → A empty, Fixed 3; Redo → clip back; (c) `ClearSessionSlot(A)` → Fixed 3; (d) a clip copied onto prepared slot C (`CopySessionSlot`) → C keeps its own value, and moving it away (copy + clear) leaves C's value; Arrangement clip → Session into a prepared slot keeps it; (e) set value then Undo → previous value, Redo → set again; (f) `MoveScene` / `DuplicateScene` carry the value with the row, `RemoveScene` drops it, `InsertScene` row is Global; (g) save/load (`ProjectService.Capture` + load, pattern of the Stop-button round trip ~9370) keeps Fixed/Open on empty and filled slots; a document without `SessionTakeLengths` → all Global
- [X] T028 [US3] Retry check in `tests/Nota.SmokeTest/SlotTakeLengthTests.cs`: Fixed 1 bar slot, record, discard+retry mid-take → retried take records a full bar from its own start (FR-010). Until #678 is fixed it fails for the same reason as #678: guard it with `const bool Bug678Fixed = false;` and yield `(true, "retry keeps the slot's length — waiting for #678")` while false, plus an unconditional check that the slot's value is still Fixed 1 after the retry

---

## Phase 6: User Story 4 — Set a slot's next-take length from an assistant (P3)

**Goal**: MCP can read and set the value ([contracts/mcp.md](contracts/mcp.md)).

**Independent test**: set (track, scene) to 3 bars over MCP → snapshot reports it; filled slot refused.

- [X] T029 [US4] In `src/managed/Nota.Mcp/Tools/SessionTools.cs`: tool `set_session_slot_take_length(int trackId, int scene, string mode, int bars = 0, int beats = 0)` with the description in contracts/mcp.md; refuse a slot whose `SessionSlotState != 0` ("slot is not empty"), an unknown mode, or an engine refusal (message naming "1 beat to 64 bars"); add `PreparedSlot(int TrackId, int Scene, string Mode, int Bars, int Beats, double ResolvedBeats)` and `PreparedSlot[] PreparedSlots` to `SessionSnapshot` (every empty non-Global slot)
- [X] T030 [US4] MCP smoke check next to the existing `mcpSession.SetSessionSlotStopButton` block (`tests/Nota.SmokeTest/Program.cs` ~15101): set an empty slot to fixed 3 bars → snapshot lists it with ResolvedBeats 12 and `RecordLengthBeats` unchanged; a filled slot → the call throws/returns the "not empty" error; out-of-range → error

---

## Phase 7: Polish & Cross-Cutting

- [X] T031 [P] `CHANGELOG.md` `[Unreleased]` → Added: "Session: an empty slot can have its own next-take length (bars and beats, or Open), set in its inspector and shown on the grid, instead of the global Fixed length (#503)."
- [X] T032 [P] FEATURES.md via the `nota-features` skill (Session recording entry)
- [X] T033 (text in ../nota-docs-503, branch docs/slot-take-length-503; no new screenshot — the shots harness has no Session-inspector scene) User docs via the `nota-docs` skill: Session page, Fixed-length recording section — the per-slot Take length, Global/Open, limits, what happens after a take (EN + RU) in `../nota-docs`
- [X] T034 (in nota-translations languages/ru.json; validate.py flags them until the reference sync, like other unreleased keys) RU strings for the T019 keys in the translations repo (as for earlier features)
- [X] T035 (headless harness /private/tmp/n503harness, dark; light skipped — NotaThemeService.Set(AppTheme) no longer exists) Visual check of the inspector row and the grid label (headless app harness, dark + light), per the nota-design skill; screenshots for the PR
- [X] T036 (inspector, grid label and drag-edit driven in the headless harness) Run [quickstart.md](quickstart.md) by-hand steps that the smoke test does not cover (UI picks, captions, grid label refresh)

### Nota closing tasks

- [X] T037 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T038 AGPL-3.0-only SPDX two-line header on every new source file (`SlotTakeLengthTests.cs`)
- [X] T039 New dependency: N/A (none)
- [X] T040 CHANGELOG entry present (T031)
- [X] T041 FEATURES.md (T032) and docs (T033) done; shortcuts: N/A (no new key)
- [X] T042 Engine behaviour change covered by `SlotTakeLengthTests` + MCP check (T008, T020, T022, T027, T028, T030)

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2** → all stories.
- **US1** (T009–T020) needs Phase 2. T009→T010→T011→T012→T013→T014 are sequential (same engine files); T015, T019 parallel with the engine work; T016→T017→T018 sequential (same file).
- **US2** needs US1's T009/T013 (resolution in the record path) and T016 (inspector row).
- **US3** T024/T025/T026 need only Phase 2; T027 needs US1's record path; T028 depends on #678 for its full check.
- **US4** needs Phase 2 only (T029); T030 after T029.
- **Polish** after the stories; T037 last.

## Parallel Example: User Story 1

```text
Engine sizing + stop (T009–T014, one engine author, sequential)
  ∥ T015 grid label (SessionView.Grid.cs)
  ∥ T019 EN strings (en.json)
```

## Implementation Strategy

1. Phase 1–2, then US1 → **MVP**: `scripts/build.sh` green with T008 + T020.
2. US2 (Global/Open UX + regression guard).
3. US3 (slot-overwrite rule, persistence, undo/cancel/retry checks).
4. US4 (MCP).
5. Polish: changelog, features, docs, RU, screenshots, final build.

## Notes

- Keep #678 out of this PR (research R5); flip `Bug678Fixed` when it lands.
- Expect conflicts with #481 (`feat/recorded-take-follow-action`) and #480 around `serviceSessionFollow` / `finalizeSessionRecord`.
- The user commits; leave changes uncommitted for review.

## Phase 8: Convergence

- [X] T043 Stopping a fresh Session take while it still waits for its launch boundary (transport running, slot state 5 "record pending") must drop the take's empty clip and leave the slot empty with its value — today `finalizeSessionRecord` keeps the clip at the prepared length and the slot then plays it (probe: Fixed 3 / global 8 / Open all end in state 3). Undo the take's own clip as `cancelSessionCountIn` does (`src/native/nota.engine/src/engine/Engine_Session.cpp`), for the main slot and any joined lane still pending; add the check to `tests/Nota.SmokeTest/SlotTakeLengthTests.cs` (`CancelledCountIn` covers only the count-in). Coordinate with #480 (cancel a queued launch) per FR-009, US3/AC1 (partial)
- [X] T044 A fixed-length take stopped by hand before its length must be kept "like any take stopped early (cut to whole launch quanta)" — today the MIDI clip keeps the full prepared length (created at it, `cutGrowingLane` skips non-growing lanes) and an audio slot gets `lane.lenBeats` as its loop length over a shorter capture. Cut both to whole launch quanta of what was recorded (`Engine_Session.cpp` finalize / `Engine_Recording.cpp` materialise), decide whether Global takes change too (SC-003 says Global behaves as before), and add MIDI + audio checks per US3/AC4, spec Assumptions (contradicts)
- [X] T045 Offer the quick picks in the empty slot's right-click menu as a **Take length** submenu (Global / Open / 1–16 bars, current one checked) in `ShowSlotMenu` (`src/managed/Nota.App/SessionView.Grid.cs`), so preparing a slot takes right-click + pick per SC-002 (partial — the inspector path is select + chip + item)
- [X] T046 Check the inspector row and grid label in the light theme(s) — the harness skipped light because `NotaThemeService.Set(AppTheme)` is gone; switch via `AppearanceChoice` as the menu harness does — per Constitution VI / DESIGN.md (partial)
- [X] T047 Wrap the new engine lines over the `.clang-format` ColumnLimit 120 (`resolveTakeBeats(...)` calls and `const SessionSlot* sl = …` in `Engine_Session.cpp` / `Engine_Recording.cpp`) per Constitution V, match the surrounding style (partial)
