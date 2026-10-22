---

description: "Task list for the clip editor batch (#277, #278, #275, #114, #248, #140)"
---

# Tasks: Clip editor batch — playhead, arrangement ruler, keyboard focus, warp tempo

**Input**: Design documents from `specs/036-clip-editor-batch/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/clip-editor-ui.md](contracts/clip-editor-ui.md),
[quickstart.md](quickstart.md)

**Tests**: The plan adds smoke-test checks for the four pure helpers (`--clipeditor`
section, linked as source like `ValueText`). UI flows are verified by the quickstart, not
by automated tests.

**Organization**: Grouped by user story; each story can be implemented and checked on its
own. Story → issue: US1 #277 + #278, US2 #275, US3 #114, US4 #248, US5 #140.

All paths are repo-relative. `App/` below means `src/managed/Nota.App/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 from spec.md

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Create folder `src/managed/Nota.App/ClipEditor/` for the pure helpers, and link
  its files into the smoke test: add `<Compile Include="..\..\src\managed\Nota.App\ClipEditor\*.cs" LinkBase="ClipEditor" />`
  next to the `ValueText.cs` link in `tests/Nota.SmokeTest/Nota.SmokeTest.csproj`. Helpers
  must not reference Avalonia or the engine.
- [X] T002 Add `tests/Nota.SmokeTest/ClipEditorTests.cs` (SPDX header, `static
  IEnumerable<(bool ok, string label)> Run()` like `ValueEntryTests`) and wire a
  `--clipeditor` section in `tests/Nota.SmokeTest/Program.cs` beside `--valueentry`
  ("-- clip editor --", "CLIP EDITOR PASSED/FAILED"), and add it to the full run.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The position mapping and ruler labels used by US1 and US2.

- [X] T003 [P] Implement `ClipTimeMap` in `src/managed/Nota.App/ClipEditor/ClipTimeMap.cs` per
  [data-model.md § ClipTimeMap](data-model.md#cliptimemap--editor-position--arrangement-position):
  fields `StartBeat, LengthBeats, Domain (WarpBeats|SourceFrames), ContentTotal, PlayStart,
  PlayEnd, Reversed, IsSlot`; `PlayFracAtContent(c)` = `(c·Total − PlayStart) / (PlayEnd −
  PlayStart)` then `1 − f` when reversed, **not clamped**; `ContentAtPlayFrac(f)` (inverse);
  `ArrangementBeatAtContent(c) = StartBeat + PlayFracAtContent(c) × LengthBeats`;
  `ContentAtArrangementBeat(b)` (inverse). Total and NaN-free for `ContentTotal > 0`;
  degenerate windows (PlayEnd ≤ PlayStart) return 0.
- [X] T004 [P] Implement `RulerLabels` + `RulerDisplay` in
  `src/managed/Nota.App/ClipEditor/RulerLabels.cs` per [data-model.md § RulerLabels](data-model.md#rulerlabels--shared-ruler-text):
  `Bars(beat, stepBeats, beatsPerBar)` → `N` / `N.b` / `N.b.s` (1-based, same output as
  `PianoRollView` / `AudioClipEditorView.BeatLabel` today); `Time(beat, stepSeconds, bpm)` →
  `m:ss` for steps ≥ 1 s, then `m:ss.f`, `m:ss.ff`, `m:ss.fff`; clamp negative positions to
  0; `TimeSteps` table (reuse the seconds steps from `AudioClipEditorView.DrawRuler`);
  `static class RulerDisplay { bool ShowTime; event Action? Changed; void Set(bool) }`
  (raises only on change). Use `NotaNum.Culture`-free formatting (pass a culture in) so it
  links without the app.
- [X] T005 Add `ClipTimeMap` and `RulerLabels` checks to `tests/Nota.SmokeTest/ClipEditorTests.cs`:
  warped untrimmed / trimmed Start / reversed / unwarped round trips (`ContentAtPlayFrac ∘
  PlayFracAtContent = id`), a clip at beat 32 reads 32 at its played start, seeking left of
  a trimmed Start gives a beat < StartBeat; labels for bars at 16th / beat / bar steps and
  time at 1 s / 0.1 s / 0.01 s / 0.001 s steps. (depends on T002–T004)
- [X] T006 Wire `RulerDisplay` to the transport: in `src/managed/Nota.App/MainWindow.axaml.cs`
  subscribe to `vm.Transport.PropertyChanged` for `ShowTimeDisplay` and call
  `RulerDisplay.Set(...)`; set the initial value on DataContext.

**Checkpoint**: `dotnet run --project tests/Nota.SmokeTest -c Release -- --clipeditor` green.

---

## Phase 3: User Story 1 — See and move the playhead from the clip editors (P1) 🎯 MVP

**Goal**: #277 + #278 — playhead visible while stopped; ruler click / drag and playhead-line
drag seek in both editors; Session slots show "not available".

**Independent Test**: quickstart § 2.

- [X] T007 [US1] In `src/managed/Nota.App/AudioClipEditorView.cs` build a `ClipTimeMap` in
  `Reload()` from `ci` / `ai` (warped: `WarpBeats`, trim `warp_play_start/_end` with
  `play_end = 0 → warp_beats`; unwarped: source frames, `source_offset_frames`,
  `length_frames`; `Reversed`; `IsSlot = SessionClip.SceneOf(ClipIndex) >= 0`) and pass it to
  the waveform canvas (`_wave.SetTimeMap(map)`); replace the inline playhead x formula in
  `Render` (the `_frac` block) with `ContentAtPlayFrac` — the drawing must not change.
- [X] T008 [US1] #277: in `AudioClipEditorView.OnPlayhead` drop the `playing &&` gate for
  arrangement clips (inclusive at start and end); keep the session-slot branch as is.
- [X] T009 [US1] Add `public Action<double>? Seek` (arrangement beat) to
  `AudioClipEditorView`, forwarded to the canvas; in `src/managed/Nota.App/MainWindow.Detail.cs`
  `BuildAudioClipEditor` set it only when `!slot`: `Engine.Seek(Math.Max(0, beat));
  Timeline.SetPlayhead(...)` — same body as the piano roll's `Seek`.
- [X] T010 [US1] Ruler seek in the waveform canvas (`AudioClipEditorView.cs`, nested canvas
  class): press in `y < RulerH` with the left button → capture, seek at x; move → scrub;
  release / capture lost → end. Position: x → content frac → `ArrangementBeatAtContent`;
  snap in the warp-beat domain with `SnapWarp` when warped (Alt = `_free`), unwarped seeks
  free; skip when the arrangement beat is unchanged; give immediate feedback via
  `SetPlayhead`. Hand cursor over the ruler when `Seek` is set.
- [X] T011 [US1] Playhead-line grab in the waveform canvas: hit zone ±4 px around the
  drawn playhead x in ruler, marker strip and wave; checked **first** in
  `OnPointerPressed` (before ADSR / envelope / marker / bracket gestures) only inside the
  zone; drag scrubs like T010; `SizeWestEast` cursor in the zone (update the hover cursor
  logic in `OnPointerMoved`).
- [X] T012 [US1] Piano roll playhead-line grab in `src/managed/Nota.App/PianoRollView.cs`:
  in `RulerLane` a press within ±4 px of `_playheadBeat` scrubs (same `SeekAt`); in
  `NoteGrid.OnPointerPressed` a press within ±3 px of the visible playhead becomes a
  *pending* scrub — once the pointer moves ≥ 3 px it scrubs via the ruler's snap / Alt
  logic, a release without movement runs the grid's normal click (add / select note);
  `SizeWestEast` cursor in the zone. Only when `Seek` is set and the line is on screen.
- [X] T013 [US1] Session-slot "not available" state in both editors: when the clip is a
  slot (`Seek is null` and slot), ruler and playhead-line zone show
  `StandardCursorType.No` and `ToolTip.Tip` = "A Session slot plays on its own loop clock —
  launch it to play from the start" (`AudioClipEditorView.cs` canvas ruler,
  `PianoRollView.cs` `RulerLane`; the slot roll is built in `MainWindow.Detail.cs` ~L772 —
  pass an `IsSessionSlot` flag there).
- [X] T014 [US1] Verify US1 by quickstart § 2 (headless harness or app): stopped playhead,
  ruler click / drag / Alt, line drag in both editors, reversed + trimmed clip, Session
  slot cursor + tooltip, and a click on the line in the note grid still adds a note.

**Checkpoint**: MVP — the playhead is fully usable from both editors.

---

## Phase 4: User Story 2 — The clip editor ruler reads like the arrangement (P2)

**Goal**: #275 — arrangement-position labels; bars / time follow the transport read-out on
all three rulers.

**Independent Test**: quickstart § 3.

- [X] T015 [P] [US2] Piano roll ruler in `src/managed/Nota.App/PianoRollView.cs`
  `RulerLane.Render`: label each tick with `RulerLabels.Bars(Origin + b, …)` for
  arrangement clips (single and multi; remove `RulerBarOffset` once equivalent), keep
  `Origin = 0` behaviour for Session slots; tick positions and grid lines unchanged
  (FR-011). In time mode place labelled ticks at `RulerLabels.TimeSteps` converted to
  beats (`sec × Bpm / 60`), labels via `RulerLabels.Time`. Subscribe to
  `RulerDisplay.Changed` → `InvalidateVisual` (unsubscribe on detach). BPM comes from the
  engine the roll already has or a `Func<double> Bpm` set by the host.
- [X] T016 [P] [US2] Audio editor ruler in `src/managed/Nota.App/AudioClipEditorView.cs`
  `DrawRuler`: bars mode — ticks at arrangement-beat steps mapped back with
  `ContentAtArrangementBeat`, labels `RulerLabels.Bars(arrBeat)`, for warped **and**
  unwarped clips (unwarped no longer shows source seconds); before the Start bracket the
  labels continue backwards (stop at arrangement beat 0). Time mode — the same with time
  steps. Session slots: clip-relative labels as today (warp beats from 0 / seconds from 0).
  Keep the existing label-thinning by width. Subscribe to `RulerDisplay.Changed`.
- [X] T017 [P] [US2] Arrangement ruler time mode in
  `src/managed/Nota.App/ArrangementView.Controls.cs` `RulerControl` render: when
  `RulerDisplay.ShowTime`, ticks at time steps (chosen by pixel width like the bars) and
  `RulerLabels.Time` labels; loop brace, markers and gestures unchanged; subscribe to
  `RulerDisplay.Changed`.
- [X] T018 [US2] Verify US2 by quickstart § 3: MIDI clip at 9.1 reads 9; warped clip with
  trimmed Start reads 9 at the bracket and 8 before it; unwarped clip in bars; flip the
  read-out → all three rulers in m:ss and back; label at clip start matches the read-out.

---

## Phase 5: User Story 3 — Keyboard shortcuts keep working after clicking in the clip editor (P2)

**Goal**: #114.

**Independent Test**: quickstart § 4.

- [X] T019 [US3] Reproduce #114 before changing code (quickstart § 4.1): audio clip with
  ADSR shown, then a MIDI clip; click props-rail buttons, the ADSR, the wave / grid; press
  Space and ⌘1–⌘5; log the focused element (`FocusManager.GetFocusedElement()`), whether the
  focused control re-activates on Space, and the grid target from `GridTarget()`. Record
  the observed cause(s) in `specs/036-clip-editor-batch/research.md` § R4 ("Reproduction").
- [X] T020 **N/A — T019: no control takes focus on click, Space is never re-activated (research R4).** [US3] Space KeyUp (R4 fix 1, if confirmed): in
  `src/managed/Nota.App/MainWindow.Input.cs` remember that the tunnel handler ran Space
  (`_spaceHandled`) and add a tunnel `KeyUpEvent` handler (registered next to the KeyDown
  one in `MainWindow.axaml.cs` and forwarded from the floating detail window like
  `HandleTransportKeyTunnel`) that marks the matching Space KeyUp handled, so a focused
  Button / ToggleButton is not clicked. Text fields stay exempt.
- [X] T021 **N/A — T019: clip editor buttons and segments already take no focus on click (research R4).** [P] [US3] Pointer-only props-rail controls not focusable (R4 fix 2, if
  confirmed): in `src/managed/Nota.App/ClipEditorKit.cs` set `Focusable = false` on the
  buttons, switches and dropdown cells the factories build (`Button`, `SwitchRow`,
  `Dropdown`, segment cells), and on any equivalent built directly in
  `AudioClipEditorView.cs` / `ClipEditorView.cs`; text fields keep focus.
- [X] T022 [US3] Inert clip grid fallback (R4 fix 3, if confirmed): in
  `src/managed/Nota.App/MainWindow.Commands.cs` `GridTarget()` return
  `EditorKind.Arrangement` when the clip editor target is the audio editor and its grid is
  inert (unwarped clip, or ADSR mode) — expose `bool GridInert` on `AudioClipEditorView`;
  the status line already names the target.
- [X] T023 **N/A — T019 found no other cause in the clip editor (research R4).** [US3] Any other cause found in T019: fix at its source and note it in research
  R4; if T019 refutes a fix in T020–T022, mark that task N/A with the reason instead of
  doing it.
- [X] T024 [US3] Verify US3 by quickstart § 4.2–4.4 (docked and floating): each region,
  Space twice + ⌘1, no button re-activates, typing in fields still types, Enter / Esc
  release focus.

---

## Phase 6: User Story 4 — Find and set the clip's warp tempo (P3)

**Goal**: #248.

**Independent Test**: quickstart § 5.

- [X] T025 [P] [US4] Implement `ClipTempo` in `src/managed/Nota.App/ClipEditor/ClipTempo.cs`
  per [data-model.md § ClipTempo](data-model.md#cliptempo--the-clips-warp-tempo): `SrcAt(beat)`
  piecewise linear through markers sorted by `src` (≥ 2), extrapolating with the end
  segments' slope; `Bpm = (PlayEnd − PlayStart) × SampleRate × 60 / (SrcAt(PlayEnd) −
  SrcAt(PlayStart))` with `PlayEnd = 0 → last marker beat`; `Scale(newBpm)` → markers with
  every beat × `k = newBpm / Bpm`, `PlayStart × k`, `PlayEnd × k` (`PlayEnd = 0` stays 0);
  "Valid range 20–999 BPM; `Half()` / `Double()` return null if the result leaves it".
- [X] T026 [US4] `ClipTempo` checks in `tests/Nota.SmokeTest/ClipEditorTests.cs`: one segment
  equals the segment-chip formula; scaling keeps segment beat ratios; trim scales; ×2 / ÷2
  range limits; `PlayEnd = 0` handling. (depends on T025)
- [X] T027 [US4] Tempo row in `src/managed/Nota.App/AudioClipEditorView.cs` Warp section,
  directly under the Warp switch row (per [contract § Warp section](contracts/clip-editor-ui.md#warp-section-audio-clip-editor)):
  label "Tempo", a `DragNumber` (20–999, two decimals, "BPM" unit, fine drag on the usual
  modifier, double-click / click → type, Enter / click-away commit, Esc cancel), and
  compact `÷2` / `×2` buttons (`ClipEditorKit.Button` style, non-focusable); tooltip on
  label and value: "The tempo the audio was recorded at. Nota stretches it to the project
  tempo. ×2 / ÷2 fix a half- or double-time detection." Follow DESIGN.md / nota-design;
  must fit the current single-column rail.
- [X] T028 [US4] Warped-clip edits: on commit compute `ClipTempo.Scale(t)` from the current
  markers / trim / `_srcSR` and apply inside `Engine.BeginUndoGroup()` … `EndUndoGroup()`
  via `WarpEdit`: `SetClipWarpMarkers` then `SetClipWarpTrim` (skip trim when unchanged);
  then `Reload()`. A drag scales the canvas markers locally (live redraw, no engine) and
  commits once on release; ÷2 / ×2 commit once each; disable them when `Half()` /
  `Double()` is null.
- [X] T029 [US4] Unwarped-clip edits: show `_detectedBpm` (or "—", dimmed but editable;
  ÷2 / ×2 disabled when "—"); a commit calls `Engine.AutoWarpClipAtBpm(TrackId, ClipIndex,
  t)` inside `WarpEdit` (one engine call = one undo step), updates `_detectedBpm` /
  `_warpEnabled`, then `Reload()`.
- [X] T030 [US4] Keep the value in sync: refresh it in `Reload()`, after Detect tempo / To
  transients, after a segment-chip edit and after a marker drag; segment chips keep working
  (FR-022).
- [X] T031 [US4] Verify US4 by quickstart § 5: 120 BPM loop reads 120.00; ÷2, ×2, drag,
  type — each one undo step; multi-marker ratios kept; unwarped → Warp on at the typed
  tempo, one undo back.

---

## Phase 7: User Story 5 — See the warp while dragging a marker (P3)

**Goal**: #140.

**Independent Test**: quickstart § 6.

- [X] T032 [P] [US5] Implement `WarpRemap` in `src/managed/Nota.App/ClipEditor/WarpRemap.cs`
  per [data-model.md § WarpRemap](data-model.md#warpremap--live-marker-drag-preview):
  `OldBeatAt(newBeat) = BeatAt(committed, SrcAt(live, newBeat))`, piecewise linear, using
  `ClipTempo.SrcAt`-style interpolation; identity when `live == committed`; no allocation
  per call (arrays passed in, binary search).
- [X] T033 [US5] `WarpRemap` checks in `tests/Nota.SmokeTest/ClipEditorTests.cs`: identity,
  monotone over a dragged middle marker, end markers fixed, last-marker drag. (depends on
  T032)
- [X] T034 [US5] Live preview in the waveform canvas (`AudioClipEditorView.cs`): on a
  marker press snapshot the committed markers into reusable arrays; while dragging, draw
  the overview and the zoomed detail peaks column by column at new beat `b` from the peak at
  `WarpRemap.OldBeatAt(b)` (both are beat-domain); BPM chips already follow `_mk`. Release
  → existing `Commit()` (one engine call, one undo step) → `Reload()` swaps in the engine
  render. No engine call during the drag (FR-026).
- [X] T035 [US5] Cancel: add `public bool CancelGesture()` on `AudioClipEditorView` (restores
  `_mk` from the snapshot, releases capture, redraws, returns true if a drag was active);
  call it first for `Key.Escape` in `src/managed/Nota.App/MainWindow.Input.cs`
  `HandleKeyDown` (before `Timeline.EscapePressed`), also reached from the floating detail
  window; capture lost → same restore.
- [X] T036 [US5] Verify US5 by quickstart § 6, including a 5-minute clip (SC-004: no lag, no
  dropouts while playing) and Esc leaving nothing in undo.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T037 Design pass with the `nota-design` skill on all new UI (Tempo row, cursors, slot
  tooltip, time labels) in Studio Dark / Studio Light / Ember Graphite / Ember Paper; the
  design checks in the smoke test stay green.
- [X] T038 [P] Preferences → Shortcuts via the `nota-shortcuts` skill: audio-editor ruler
  click / drag, Alt free seek, playhead-line drag (both editors), Esc cancels a warp-marker
  drag, Space no longer activates a focused control.
- [X] T039 [P] User docs via the `nota-docs` skill (EN + RU): clip editor page (playhead,
  ruler in arrangement position, bars / time), warping page (Tempo field, ÷2 / ×2, live
  marker drag), arrangement ruler time mode; re-shoot affected screenshots.
- [X] T040 [P] FEATURES.md via the `nota-features` skill.
- [X] T041 `CHANGELOG.md` `[Unreleased]`: Fixed — #277, #114; Added / Changed — #278, #275,
  #248, #140 (one line each, user-facing wording, issue links).
- [X] T042 Screenshots for the PR (quickstart § 7): before / after for #277, #275, #248 and a
  mid-drag #140 frame, via the headless harness.
- [X] T043 Run the full quickstart once more end to end.

### Nota closing tasks *(PR gates)*

- [X] T044 Run `scripts/build.sh` until native + managed + smoke test are green (includes
  `--clipeditor`).
- [X] T045 AGPL-3.0-only SPDX two-line header on every new source file
  (`ClipEditor/*.cs`, `tests/Nota.SmokeTest/ClipEditorTests.cs`).
- [X] T046 New dependency: N/A (none).
- [X] T047 CHANGELOG entry: see T041.
- [X] T048 FEATURES.md / docs / shortcuts: see T038–T040.
- [X] T049 Engine behaviour change: N/A (UI-only; existing ABI entry points).
- [ ] T050 Run `/speckit-converge` and reach **Converged** before the PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001–T002)** → **Foundational (T003–T006)** → user stories.
- **US1** needs T003 (map) and T006 is not needed. **US2** needs T003, T004, T006.
- **US3** needs nothing from Phase 2 (can start right after Setup).
- **US4** and **US5** need only their own pure helper (T025 / T032) and Setup.
- **Polish** after the stories it documents.

### User Story Dependencies

- US1 → US2 share `AudioClipEditorView`'s canvas: do US1's canvas tasks (T007, T010, T011)
  before T016 to avoid conflicting edits.
- US4 and US5 both edit the Warp code in `AudioClipEditorView.cs`: sequence T027–T030
  before T034–T035.
- US3 is independent of the others.

### Within Each User Story

- Pure helper → its smoke checks → UI wiring → verification task.

### Parallel Opportunities

- T003 ∥ T004 (different new files); T025 ∥ T032 ∥ T003 (pure helpers, different files).
- In US2: T015 (PianoRollView) ∥ T017 (ArrangementView.Controls) ∥ T016 (after US1 canvas work).
- In US3: T021 (ClipEditorKit) ∥ T020 (MainWindow.Input).
- Polish: T038 ∥ T039 ∥ T040.

---

## Parallel Example: Foundational + pure helpers

```bash
Task: "Implement ClipTimeMap in src/managed/Nota.App/ClipEditor/ClipTimeMap.cs"
Task: "Implement RulerLabels + RulerDisplay in src/managed/Nota.App/ClipEditor/RulerLabels.cs"
Task: "Implement ClipTempo in src/managed/Nota.App/ClipEditor/ClipTempo.cs"
Task: "Implement WarpRemap in src/managed/Nota.App/ClipEditor/WarpRemap.cs"
```

## Parallel Example: User Story 2

```bash
Task: "Piano roll ruler labels + time mode in src/managed/Nota.App/PianoRollView.cs"
Task: "Arrangement ruler time mode in src/managed/Nota.App/ArrangementView.Controls.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (T001–T006).
2. US1 (T007–T014) → validate by quickstart § 2. The two most-felt issues (#277, #278) are
   fixed here.

### Incremental Delivery

1. US1 → US2 (rulers build on the same map) → US3 (keyboard) → US4 (tempo) → US5 (live
   drag), each validated by its quickstart section.
2. Suggested commit split for the user's review (the user commits): one commit per issue,
   in the order #277, #278, #275, #114, #248, #140, plus one for docs.

---

## Notes

- [P] = different files, no dependency on unfinished tasks.
- The user makes the commits; leave changes uncommitted for review.
- If T019 shows #114 has a cause outside the clip editor, keep the fix scoped and record it.

## Phase 9: Convergence

- [ ] T051 Add fine drag to the audio clip editor's **Tempo** field — holding the usual fine-drag modifier (⇧ / ⌘, as on `Knob` / `SliderTrack`) drags it in smaller BPM steps — in `src/managed/Nota.App/Controls/DragNumber.cs` (opt-in, so the transport BPM field is unchanged unless chosen) and `src/managed/Nota.App/AudioClipEditorView.cs`; restore the fine-drag note in `contracts/clip-editor-ui.md` § Warp section per FR-018 (partial)
- [ ] T052 Review and justify (or narrow to marker drags) the live warp preview and Esc cancel that also cover the segment BPM-chip scrub and the Tempo-field drag in `src/managed/Nota.App/AudioClipEditorView.cs` (`Remapped`, `CancelGesture`), beyond the warp-marker drag FR-023 / FR-025 call for; if kept, record it in research.md R6 and the contract per FR-023 (unrequested)
