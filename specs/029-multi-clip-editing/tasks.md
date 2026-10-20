---

description: "Tasks for 029 multi-clip editing"
---

# Tasks: Multi-Clip Editing in the Clip Editor

**Input**: Design documents from `specs/029-multi-clip-editing/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included — the constitution's verification gate (IV) and plan.md call for
`tests/Nota.SmokeTest/MultiClipTests.cs` (`--multiclip`) for the pure rules plus one
engine-driven grouped-undo check; `--notes` (spec 007) must stay green unchanged.

**Organization**: Grouped by user story. Paths are real Nota paths (the UI lives in
`src/managed/Nota.App/`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 … US4 from spec.md

---

## Phase 1: Setup

**Purpose**: Test harness entry point for the new suite.

- [X] T001 Create `tests/Nota.SmokeTest/MultiClipTests.cs` (SPDX header, `internal static class MultiClipTests` with `public static IEnumerable<(bool Ok, string Label)> Run()` returning no checks yet) and register it in `tests/Nota.SmokeTest/Program.cs` both as a standalone `--multiclip` mode (mirror the `--notes` block near line 284) and in the full run (next to `NoteEditingTests.Run()` near line 14939)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The pure `ClipSet` model, owner-aware overwrite, and the roll's owner
plumbing with single-clip parity. Every story builds on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests (write first, see them fail)

- [X] T002 [P] Add `ClipSet` checks to `tests/Nota.SmokeTest/MultiClipTests.cs`: Origin = min StartBeat, SpanBeats = max(Start+Length) − Origin; ToEditor/ToLocal round-trip; clips ordered by StartBeat then TrackId; Split→Merge round-trip preserves every note and owner; a one-clip set has Origin = clip start so editor beats equal clip-local beats; validation per data-model.md ("At least one clip; Active is a valid slot and not a reference"; "Setting a clip to reference is refused for Active"; "making a reference clip Active clears its reference flag first"); removing a clip remaps slots and moves Active to the next clip
- [X] T003 [P] Add owner-aware overwrite checks to `tests/Nota.SmokeTest/MultiClipTests.cs`: two same-pitch overlapping notes with different owners are both kept by `NoteOverwrite.Settle(..., owners)`; same owners behave exactly as spec 007; `Fate(..., owners)` agrees with `Settle`; `owners == null` gives results identical to the existing overloads

### Implementation

- [X] T004 [P] Create `src/managed/Nota.Application/Editing/ClipSet.cs` (SPDX header, namespace `Nota.Application.Editing`): `OpenClip` (TrackId, ClipIndex, StartBeat, LengthBeats, IsReference), `ClipNotes(int TrackId, int ClipIndex, IReadOnlyList<NotaNote> Notes)`, and `ClipSet` with `Clips`, `Active`, `Origin`, `SpanBeats`, `IsMulti`, `ToEditor`, `ToLocal`, `Split(notes, owners)`, `Merge(perClip)`, `SetActive`, `SetReference`, `Remove(trackId, clipIndex)`, `UpdateGeometry(trackId, clipIndex, start, length)` per [data-model.md](data-model.md); make T002 pass
- [X] T005 [P] Add optional `IReadOnlyList<int>? owners` (and `List<int>? outOwners` for Settle) parameters to `Settle` and `Fate` in `src/managed/Nota.Application/Editing/NoteOverwrite.cs`; same-pitch overlap counts only when owners match; `owners == null` must stay byte-identical to today; make T003 pass
- [X] T006 Add the parallel owner list to `src/managed/Nota.App/PianoRollView.cs`: field `List<int> _owner`, private helpers `AddNote(NotaNote, int owner)`, `RemoveNoteAt(int)`, `ReplaceAll(IEnumerable<NotaNote>, IEnumerable<int>)`, and route every structural `_notes` mutation through them (SetNotes, DuplicateSelection, DeleteSelection, RemoveOutOfScaleNotes, ShowPreview, CancelPreview, PasteClipboard, SettlePlacement, `_previewBackup` save/restore, NoteGrid double-click add/remove near line 1585); add a debug assert that `_notes.Count == _owner.Count` after each helper
- [X] T007 Add `SetClips(ClipSet set, IReadOnlyList<ClipNotes> notes)`, `Clips`, `ActiveClip`, `Origin` to `src/managed/Nota.App/PianoRollView.cs` per [contracts/piano-roll-api.md](contracts/piano-roll-api.md): merge notes into editor beats with owners, `_lengthBeats = set.SpanBeats`, view-state key = active clip; make existing `SetNotes(notes, length)` build a one-clip set internally so single-clip and Session-slot callers are unchanged (R3)
- [X] T008 Add `CommitClips` / `CommitClipsLive` (`Action<IReadOnlyList<ClipNotes>>`) to `src/managed/Nota.App/PianoRollView.cs`; `CommitNotes` and `PushLive` split via `ClipSet.Split` and call them when set (only clips whose notes changed since the last commit; the drag-seed commit passes every editable clip), falling back to `Commit`/`CommitLive` otherwise; keep a per-clip last-committed snapshot for the change check
- [X] T009 Pass `_owner` to `NoteOverwrite.Settle`/`Fate` everywhere the roll calls them (SettlePlacement, DuplicateSelection, the overwrite marks in NoteGrid render) in `src/managed/Nota.App/PianoRollView.cs`, carrying owners through to the settled list
- [X] T010 Run `dotnet run --project tests/Nota.SmokeTest -- --notes` and `-- --multiclip`; both green (single-clip parity checkpoint)

**Checkpoint**: Roll can hold notes of several clips with owners; one-clip behaviour unchanged.

---

## Phase 3: User Story 1 - Open several clips together (Priority: P1) 🎯 MVP

**Goal**: Double-click a clip within a multi-selection opens all selected MIDI clips on one
arrangement-time axis, clicked clip active, notes in track colours.

**Independent Test**: Select two MIDI clips at different positions, double-click one; both
clips' notes appear at their arrangement positions, the ruler shows arrangement bars, the
double-clicked clip is active; with one clip selected nothing changed.

### Tests

- [X] T011 [P] [US1] Add an engine-driven check to `tests/Nota.SmokeTest/MultiClipTests.cs`: create two MIDI clips, `BeginUndoGroup`, `SetClipNotes` on both, `EndUndoGroup`, one `Undo` restores both clips' previous notes (FR-011 basis; follow the engine setup used in `tests/Nota.SmokeTest/HistoryTests.cs`)

### Implementation

- [X] T012 [P] [US1] Add `IReadOnlyList<(int TrackId, int ClipIndex)> SelectedMidiClips()` to `src/managed/Nota.App/ArrangementView.cs` (from `_sel`, MIDI only, ordered by StartBeat then track order)
- [X] T013 [US1] Generalise `BuildClipEditor` in `src/managed/Nota.App/MainWindow.Detail.cs` to take a list of `(trackId, clipIndex)` plus the active one: build `ClipSet` from `Engine.TryGetClipInfo`, load each clip's notes, `roll.SetClips`, store the set (replace `_editorTrackId/_editorClipIndex` usages with the active clip where a single id is still needed); wire `CommitClips` (validate each clip `TryGetClipInfo(...).IsMidi` else drop it from the set; `Engine.BeginUndoGroup()`; `SetClipNotes` per entry; `EndUndoGroup()` in `finally`; `Timeline.Refresh()`) and `CommitClipsLive` (`SetClipNotesLive`, `Timeline.Refresh(rebuildHeaders: false)`)
- [X] T014 [US1] In `OpenClipEditor` (`src/managed/Nota.App/MainWindow.Detail.cs`) open every `Timeline.SelectedMidiClips()` when the double-clicked clip is among them and there are ≥2, active = double-clicked; otherwise one clip as today (FR-001, FR-002); audio clips in the selection are ignored
- [X] T015 [US1] Draw notes of all clips in `src/managed/Nota.App/PianoRollView.cs` NoteGrid render with their own track brush (host passes per-clip brushes via `ClipSet`/a `Func<int, ISolidColorBrush>`), active clip full colour, other clips subdued (opacity per `nota-design`), active clip's notes drawn last (on top); replace `DrawOutsideClip` with a wash over every editor-beat range not covered by any open clip
- [X] T016 [US1] Ruler/playhead/seek in arrangement time: `RulerLane` bar numbers offset by `Origin` (`src/managed/Nota.App/PianoRollView.cs`), `Seek` callback gets `Origin + editorBeat` and the playhead in `src/managed/Nota.App/MainWindow.axaml.cs` (near line 306) uses `beats − Origin`; on open, scroll so the active clip is in view
- [X] T017 [US1] Header in `src/managed/Nota.App/ClipEditorView.cs`: active clip name and track colour cell, plus "· +N clips" when the set is multi (Story 1 scenario 6)
- [X] T018 [US1] Fixed set (FR-001a): in `OnTrackSelected` and `OnDetailClip` (`src/managed/Nota.App/MainWindow.Detail.cs`) skip `ResetClipEditor` / re-pointing while the open set is multi; single-clip behaviour unchanged
- [X] T019 [US1] Add the "Edit selected clips" command: `B("roll.editSelected", …)` in `src/managed/Nota.App/MainWindow.Commands.cs` (enabled only with ≥2 selected MIDI clips; active = first in time) and an item in the arrangement clip context menu in `src/managed/Nota.App/ArrangementView.LaneControl.cs` (near line 1158)

**Checkpoint**: Multi-clip view works; edits already land in the right clips through the owner plumbing.

---

## Phase 4: User Story 2 - Edit notes across all open clips (Priority: P1)

**Goal**: Selection and every edit span clips; draw goes to the right clip; notes cross seams
into neighbouring open clips; paste routes copies to their source clip; one Undo per edit.

**Independent Test**: Open two clips, rubber-band across both, press ↑ → both change, one
⌘Z restores both; drag a note of A to start in B → it moves to B.

### Tests

- [X] T020 [P] [US2] Add reach/owner checks to `tests/Nota.SmokeTest/MultiClipTests.cs`: A(0–16)+B(16–32) same track → note moved to start 17 gets owner B; gap between A(0–16) and B(20–36) → move clamped at A's edge (start ≤ 16 − min length); B reference → wall; clip on another track never becomes owner; overlapping same-track clips → keep current if it contains the start, else active, else later-starting; resize never changes owner
- [X] T021 [P] [US2] Add paste-routing checks to `tests/Nota.SmokeTest/MultiClipTests.cs` (FR-012): copies from an open clip land on their originals in that clip; copies from a clip not in the set land in the active clip at their clip-relative beats

### Implementation

- [X] T022 [US2] Add `Reach(slot)` and `OwnerAfterMove(slot, editorStart)` to `src/managed/Nota.Application/Editing/ClipSet.cs` per research R4 (contiguous = next start ≤ previous end + 1e-9; only open, non-reference clips of the same track); make T020 pass
- [X] T023 [US2] Use reach in moves in `src/managed/Nota.App/PianoRollView.cs`: `MoveSelection` and NoteGrid `DragUpdate` Mode.Move clamp the shared delta so every moved note's start stays in its reach (shape kept; replaces the `_lengthBeats` cap), then set `_owner[i] = OwnerAfterMove(...)`; ResizeSelection / resize drags clamp against the owner clip's end and never change owner
- [X] T024 [US2] Draw target (FR-007) in NoteGrid double-click (`src/managed/Nota.App/PianoRollView.cs`, near line 1588): owner = the active clip if it contains the beat, else another open editable clip of the active clip's track containing it (which then becomes active), else do nothing; `NewNoteLen` caps at the owner clip's end
- [X] T025 [US2] Selection across clips in `src/managed/Nota.App/PianoRollView.cs`: marquee, Select All (`SelectPitch` and the select-all path), hit-test and keyboard edits skip notes whose owner is a reference clip; clicking or selecting a single note of a non-active clip calls `SetActiveClip` (settles a pending placement first)
- [X] T026 [US2] Clipboard with source (R11) in `src/managed/Nota.App/PianoRollView.cs`: `s_clipboard` entries keep source `(TrackId, ClipIndex)` and the source clip's arrangement start; `PasteClipboard` routes per T021 and trims/skips against the target clip's span; `DuplicateSelection` keeps each copy in its original's clip (cap at that clip's end); make T021 pass
- [X] T027 [US2] Whole-clip commands across editable clips in `src/managed/Nota.App/PianoRollView.cs`: `Quantize`, `TransposeBy`, `RemoveOutOfScaleNotes` skip reference-clip notes; quantize snaps on the editor grid, which with several clips open is the arrangement's bar grid (ClipSet puts editor beat 0 on a bar line), so every clip lands on the same grid
- [X] T028 [US2] Audition plays on the note's own track: replace the single `AuditionOn/AuditionOff` target with a track resolved from `_owner` of the placed/clicked note (`src/managed/Nota.App/PianoRollView.cs` + wiring in `src/managed/Nota.App/MainWindow.Detail.cs`)

**Checkpoint**: Stories 1+2 deliver the core request (the bass-line fix).

---

## Phase 5: User Story 3 - Reference clips (Priority: P2)

**Goal**: Clip strip under the ruler; click a bar to make it active; lock toggles reference
(dimmed, read-only, still plays).

**Independent Test**: Open a melody clip and a chord clip, lock the chord clip, rubber-band
everything → only melody notes selected; chord notes dimmed; playback unchanged.

- [X] T029 [US3] Design the strip and note looks with the `nota-design` skill (bar height ~18px, track-colour fill, clip name, chosen marker on the active bar, lock glyph, subdued and reference note opacities, stacking rows when clips of different tracks overlap); record the chosen tokens in a comment header of the new file
- [X] T030 [US3] Create `src/managed/Nota.App/PianoRollView.ClipStrip.cs` (SPDX header): `ClipStrip : Control` on the roll's beat axis (BeatToX), visible only when `Clips.IsMulti`, hosted between `RulerLane` and the note grid in the `PianoRollView` layout; click bar → `SetActiveClip`; click lock (non-active bars only) → `SetReference`; repaint with `Invalidate()`
- [X] T031 [US3] `SetActiveClip(int)` / `SetReference(int, bool)` + `ActiveClipChanged` in `src/managed/Nota.App/PianoRollView.cs`: settle pending placement first, refuse reference for the active slot, drop reference notes from the selection, redraw reference notes dimmed with no hover/edge cursors
- [X] T032 [US3] Velocity lane in `src/managed/Nota.App/PianoRollView.cs` (`VelocityLane`): bars in each note's track colour, reference-clip notes not drawn and not grabbable

**Checkpoint**: Composing against a locked chord clip works.

---

## Phase 6: User Story 4 - Inspector, envelopes and tools (Priority: P3)

**Goal**: Panels follow the active clip; clip tools act per clip.

**Independent Test**: With two clips open, click a note of the other clip → inspector Start/
Length and Envelopes switch to it; Arpeggiate on notes in both clips → each clip gets its
own result, Apply is one undo step.

- [X] T033 [P] [US4] `src/managed/Nota.App/ClipPropsView.cs`: Start/Length/Loop read the active clip (subscribe to `ActiveClipChanged`); Start edits move the active clip as today
- [X] T034 [P] [US4] `src/managed/Nota.App/ClipEditorView.cs`: on `ActiveClipChanged` rebind `EnvOverlay` to the active clip's track/clip/length and draw it offset by that clip's editor start; refresh the header (T017)
- [X] T035 [US4] Per-clip tool runs (R6): add `SourceClipNotes`, `SelectionByClip` and `ShowPreview(IReadOnlyList<ClipNotes>)` to `src/managed/Nota.App/PianoRollView.cs`; in `src/managed/Nota.App/MidiToolsView.cs` `Resync`/`Preview` run `MidiToolRunner.Apply` once per clip that has selected notes (no selection → active clip only) with `MidiToolContext.LengthBeats` = that clip's length, merge results with owners; `ApplyPreview` commits all through `CommitClips` (one undo group); the scope text reads "N selected" / "clip · N"
- [X] T036 [US4] Expression lanes in `src/managed/Nota.App/PianoRollView.Expression.cs` work on selected notes whatever their clip (editor-beat positions); reference notes excluded

**Checkpoint**: All stories functional.

---

## Phase 7: Host robustness (cross-cutting, FR-013)

- [X] T037 `OnClipGeometryChanged` in `src/managed/Nota.App/MainWindow.Detail.cs`: if the clip is in the open set, `ClipSet.UpdateGeometry` and reload the set (`SetClips`); `ReloadEditorNotes` reloads every clip of the set (undo/redo, recording); record-take preview shown when the take clip is in the set, with `clipStart = Origin`
- [X] T038 Dead clips in `src/managed/Nota.App/MainWindow.Detail.cs`: on commit/reload, clips failing `TryGetClipInfo(...).IsMidi` are removed from the set (Active → next); empty set → `InvalidateClipEditor()`; `BuildClipEditor` and save/export keep calling `SettlePlacement()` before replacing the set

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T039 Performance check (SC-003): 8 clips / 2,000 notes in a headless or manual session; drag stays smooth; live pushes only changed clips (T008)
- [X] T040 Headless Avalonia render of a three-clip editor (strip, subdued and reference notes, header) per the nota-ui-verify recipe; compare against the `nota-design` intent
- [X] T041 Run the manual scenarios in [quickstart.md](quickstart.md)

### Nota closing tasks *(PR gates)*

- [X] T042 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T043 AGPL-3.0-only SPDX two-line header on `ClipSet.cs`, `PianoRollView.ClipStrip.cs`, `MultiClipTests.cs`
- [X] T044 New dependency: N/A (none)
- [X] T045 `CHANGELOG.md` `[Unreleased]` → Added: edit several MIDI clips at once in the Clip editor (#48)
- [X] T046 FEATURES.md via `nota-features`; clip-editor docs page (EN + RU) via `nota-docs`; Preferences → Shortcuts (clip-strip clicks, "Edit selected clips") via `nota-shortcuts`
- [X] T047 Engine behaviour change: N/A (engine untouched; grouped undo covered by T011)

---

## Phase 9: Review fixes (bug06.nota, 2026-10-20)

- [X] T048 [US1] Follow the arrangement selection while the Clip editor is on screen (FR-001a revised): `ClipSelectionChanged` in `src/managed/Nota.App/ArrangementView.cs` (posted, deduplicated, from `UpdateHeaderSelection`), `FollowClipSelection` in `src/managed/Nota.App/MainWindow.Detail.cs`; drop the fixed-set guards in `OnTrackSelected` / `OnDetailClip`
- [X] T049 [US2] ⌘D places each copy by the move rule (FR-005a): `ClipSet.ShiftTarget` in `src/managed/Nota.Application/Editing/ClipSet.cs`, used by `DuplicateSelection` in `src/managed/Nota.App/PianoRollView.cs`; copies past the reach are dropped instead of clamped; checks in `tests/Nota.SmokeTest/MultiClipTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (T001) → Foundational (T002–T010) → user stories.
- US1 (Phase 3) depends on Foundational only.
- US2 (Phase 4) depends on US1 (it needs a multi-clip editor to open: T013/T014).
- US3 (Phase 5) depends on US1; independent of US2 except T025's reference skip, which reads the flag T031 sets (either order works; the skip is a no-op without the strip).
- US4 (Phase 6) depends on US1; T035 uses `CommitClips` (T008/T013).
- Host robustness (Phase 7) after US1; Polish last.

### Within phases

- Tests before their implementation (T002/T003 → T004/T005; T020 → T022; T021 → T026).
- `PianoRollView.cs` tasks are sequential (same file).

## Parallel Opportunities

- Phase 2: T002 ∥ T003 (tests, same file — write together) ; T004 ∥ T005 (different files).
- Phase 3: T011 ∥ T012 ∥ T017 (test file, ArrangementView, ClipEditorView).
- Phase 6: T033 ∥ T034 (ClipPropsView, ClipEditorView).
- Docs (T045, T046) in parallel once behaviour is final.

```text
# Phase 2 parallel start
T004 ClipSet.cs            ∥ T005 NoteOverwrite.cs
# Phase 3 parallel start
T011 grouped-undo test     ∥ T012 SelectedMidiClips() ∥ T017 header
```

## Implementation Strategy

- **MVP**: Phases 1–3 (US1) + Phase 4 (US2) — the issue's core: see and edit a split bass
  line together. US1 alone is a viewable increment but not shippable without US2.
- **Increment 2**: US3 (reference lock + strip).
- **Increment 3**: US4 (panels, tools), Phase 7 robustness, polish and gates.
- After all tasks: `/speckit-converge` must report Converged before the PR.

## Nota Compliance Re-check

- [x] SPDX headers planned for every new file (T043)
- [x] No new dependency (T044)
- [x] No audio-thread code touched
- [x] No C ABI change; existing undo group reused
- [x] Smoke test extended (T001–T003, T011, T020, T021)
- [x] CHANGELOG / FEATURES / docs / shortcuts planned (T045, T046)
- [x] UI follows DESIGN.md via `nota-design` (T029, T040)
