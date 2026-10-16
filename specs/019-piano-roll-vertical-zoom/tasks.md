---

description: "Task list for piano roll vertical zoom"
---

# Tasks: Piano roll vertical zoom

**Input**: Design documents from `/specs/019-piano-roll-vertical-zoom/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/ui-gestures.md](contracts/ui-gestures.md), [quickstart.md](quickstart.md)

**Tests**: Not requested in the spec. There is no managed unit-test project, and the engine is untouched, so the smoke test needs no extension. Verification is the headless capture plus the quickstart scenarios (Phase 7).

**Organization**: Tasks are grouped by user story so each one can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

Abbreviation: `PRV` = `src/managed/Nota.App/PianoRollView.cs`.

---

## Phase 1: Setup

**Purpose**: None needed. There are no new projects, packages or files (plan § Technical Context). Work starts at Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Turn the fixed row height into state that all zoom inputs and Fit share (research R1, R2, R8).

**⚠️ CRITICAL**: Every user story depends on this phase.

- [X] T001 Replace `private const double RowH = 12` with constants `MinRowH = 6`, `DefaultRowH = 12`, `MaxRowH = 36` and an instance field `_rowH = DefaultRowH` exposed as `internal double RowH => _rowH`. Make `RangeH` an instance property (`88 × RowH`). In `PRV`, make `NoteGrid.PitchToY` / `YToPitch` and the keys column's pitch-from-Y non-static and reading `_o.RowH`. Do the same for every `RowH` use in `KeysColumn.Render` / `DrawKeyLabel` (make `DrawKeyLabel` take the row height), `NoteGrid` rendering, hit-testing, marquee, expression curves (`v * RowH`), `CenterOffsetForNotes` (`12 * RowH` fallback), scroll-to-pitch (`EnsurePitchVisible`, ~line 836) and drag auto-scroll. Build and confirm the editor looks identical at 12 px.
- [X] T002 Add a `RowH` field to the `ViewState` record in `PRV` ("row height, px, clamped to 6–36; absent/first open → 12"). `SaveView` writes `_rowH`. `SetNotes` restores `Math.Clamp(vs.RowH, MinRowH, MaxRowH)` when saved state exists and sets `DefaultRowH` otherwise. `_keys.Height` / `_gridControl.Height = RangeH` must be applied **after** `_rowH` is set. Zooming one clip must never change another clip's entry (clarification Q1).
- [X] T003 Add `SetRowHeightAt(double h, double viewportY)` and `ZoomRowsAt(double factor, double viewportY) => SetRowHeightAt(_rowH * factor, viewportY)` to `PRV`, per research R2:
  - Clamp h to `[MinRowH, MaxRowH]`; return if unchanged.
  - Compute `c = _bodyScroll.Offset.Y + viewportY` and set the new offset to `c * h1 / h0 - viewportY`.
  - Set `_rowH`, then `_keys.Height` / `_gridControl.Height = RangeH`.
  - Queue the offset through `_pendingVOffset` and call `ApplyPendingVOffset()`. Its clamp against the extent pins the range to the top when 88 rows are shorter than the viewport.
  - Finish with `Invalidate()` and `SaveView()`.
  - Do nothing while a note gesture is active: expose `NoteGrid`'s `_drag != Mode.None` as `internal bool Dragging` and check it here (R6).
- [X] T004 Make `ApplyPendingVOffset` in `PRV` still run when the ScrollViewer extent hasn't caught up with the new `RangeH` yet. Retry on `LayoutUpdated` until `_bodyScroll.Extent.Height` ≥ `RangeH`, or clamp against `RangeH − viewport` directly. Otherwise zooming in near the bottom clamps against the old, smaller extent.

**Checkpoint**: Row height is per-clip state, and one method zooms it with an anchor. Nothing calls it yet.

---

## Phase 3: User Story 1 — Zoom rows with the wheel (Priority: P1) 🎯 MVP

**Goal**: ⌘⌥ + wheel (Ctrl + Alt on Windows/Linux) over the grid or the keys zooms rows, anchored at the pointer. All other wheel meanings stay the same.

**Independent Test**: Quickstart rows 1–5 and 15. At 6 / 12 / 36 px, edit notes and confirm the pitch under the pointer is the one edited.

- [X] T005 [US1] In `NoteGrid.OnPointerWheelChanged` (`PRV`, ~line 1507), add a first branch: when `(mods & (Control|Meta)) != 0 && (mods & Alt) != 0`, call `_o.ZoomRowsAt(WheelInput.ZoomFactor(e.Delta.Y, 1.2), e.GetPosition(_o._bodyScroll).Y)` and set `e.Handled = true`. The existing ⌘ (horizontal zoom), Shift and horizontal-swipe branches follow unchanged (FR-003).
- [X] T006 [US1] Add `OnPointerWheelChanged` to `KeysColumn` in `PRV` with the same ⌘⌥ branch, anchored at `e.GetPosition(_o._bodyScroll).Y`. Any other wheel input stays unhandled, so the ScrollViewer keeps scrolling.
- [X] T007 [US1] Update the header comments in `PRV` (file header, the horizontal zoom block, and the wheel handler comment) to describe vertical zoom and the ⌘⌥ gesture.

**Checkpoint**: The MVP is in place. Rows zoom with the wheel and nothing else changes.

---

## Phase 4: User Story 2 — Fit to notes (Priority: P2)

**Goal**: One action fits all of the clip's notes vertically. It's available as ⇧F in the grid, a header button and a palette command.

**Independent Test**: Quickstart rows 8–11 and 17.

- [X] T008 [US2] Add `public void FitToNotes()` to `PRV` (research R7):
  - Empty clip: `_rowH = DefaultRowH` and centre with the existing C5 fallback.
  - Otherwise, over **all** notes (ignore the selection): `span = maxP − minP + 1 + 2`, `h = clamp(viewportHeight / span, 6, 36)`.
  - Apply h, resize, then centre via `_pendingCenter = true; ApplyPendingVOffset()`.
  - Finish with `SaveView()` and `Invalidate()`. It is not undoable and doesn't mark the project modified (FR-010).
- [X] T009 [US2] In `NoteGrid.OnKeyDown` in `PRV`, handle `Key.F` when the modifiers are exactly Shift: call `_o.FitToNotes()` and set `e.Handled = true`. Handling it here means `MainWindow.OnKeyDown`'s note map never sees it, so no note F sounds. Put it before the selection-nudge block.
- [X] T010 [P] [US2] Add `C("roll.fitNotes", "Fit to notes", "Piano roll", "⇧F", "zoom vertical rows height")` after `roll.removeOutOfScale` in `src/managed/Nota.Application/Palette/CommandCatalog.cs`.
- [X] T011 [US2] Bind it in `src/managed/Nota.App/MainWindow.Commands.cs`: `B("roll.fitNotes", () => _editorRoll?.FitToNotes(), Roll);` next to the other `roll.*` bindings. Depends on T008 and T010.
- [X] T012 [P] [US2] Add a neutral "Fit" chip (header `chip` style, like "Tools") to the right header group in `ClipEditorView`, before `SnapControl(roll)` in `src/managed/Nota.App/ClipEditorView.cs`, then a `HeaderDivider()`. Give it the tooltip "Fit to notes (⇧F)" and make it call `roll.FitToNotes()`. Follow DESIGN.md / the `nota-design` skill: reuse the header's existing icon-button style (as `_toolsToggle` does) and no accent colour.

**Checkpoint**: US1 and US2 both work on their own.

---

## Phase 5: User Story 3 — Drag on the keys column (Priority: P3)

**Goal**: A vertical drag on the keys zooms rows. Dragging down makes them taller, and the pressed key stays where it was.

**Independent Test**: Quickstart row 6.

- [X] T013 [US3] In `KeysColumn` in `PRV`, add drag zoom (research R4):
  - `OnPointerPressed`, left button only: capture the pointer and store `_pressY = e.GetPosition(_o._bodyScroll).Y` and `_pressRowH = _o.RowH`. Mark it handled, and audition or create nothing.
  - `OnPointerMoved` while captured: `_o.SetRowHeightAt(_pressRowH * Math.Exp((y - _pressY) * 0.01), _pressY)`. Ignore X.
  - `OnPointerReleased` and `OnPointerCaptureLost`: release and clear the state.
  - Show the resize-vertical cursor while dragging. Keep the hover highlight working when not dragging.

**Checkpoint**: The mouse-only zoom works without the wheel.

---

## Phase 6: User Story 4 — Pinch on the trackpad (Priority: P3)

**Goal**: ⌥ + pinch on a trackpad zooms rows, anchored at the pointer.

**Independent Test**: Quickstart row 7, on a Mac trackpad.

- [X] T014 [US4] In the `NoteGrid` constructor in `PRV`, add `AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, OnMagnify)`. When `(e.KeyModifiers & Alt) != 0`, call `_o.ZoomRowsAt(1 + e.Delta.X, e.GetPosition(_o._bodyScroll).Y)` and set handled. Without ⌥, leave the event alone (research R5).

**Checkpoint**: All four stories work.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T015 [P] Add four rows to the "Piano roll" section of `src/managed/Nota.App/PreferencesWindow.cs`, using the exact text from [contracts/ui-gestures.md](contracts/ui-gestures.md): `⌘⌥ + wheel`, `Drag on the keys`, `⌥ + pinch`, `⇧F`. Run the `nota-shortcuts` skill to check them (FR-012).
- [X] T016 Verify headlessly following the [quickstart](quickstart.md) § Headless capture. Use a throwaway harness in the scratchpad with `NOTA_DATA_DIR` set. Seed clips A–D. Drive ⌘⌥-wheel, keys drag and ⇧F with synthetic events, then capture frames at 6 / 12 / 36 px and after Fit. Check the anchor pitch, that the range is pinned at 6 px, and that clips restore. Delete the harness afterwards.
- [X] T017 Walk through quickstart scenarios 1–17 and note any that couldn't be run headlessly (pinch, for example).

### Nota closing tasks *(the PR gates)*

- [X] T018 Run `scripts/build.sh` until native, managed and the smoke test are green.
- [X] T019 SPDX header on every new source file. N/A, no new files planned; re-check if any were added.
- [X] T020 New dependency: N/A (Avalonia already present).
- [X] T021 Add a `CHANGELOG.md` `[Unreleased]` → Added entry: piano roll vertical zoom (⌘⌥ + wheel, drag on the keys, ⌥ + pinch) and Fit to notes (⇧F), referencing #43.
- [X] T022 Update FEATURES.md via the `nota-features` skill and the user manual (EN + RU) via the `nota-docs` skill.
- [X] T023 Engine behaviour change → smoke test: N/A (UI only).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 2)**: T001 → T002 → T003 → T004 run in order, all in `PRV`. It blocks every story.
- **US1 (Phase 3)**: Depends on Phase 2.
- **US2 (Phase 4)**: Depends on Phase 2 only, not on US1. T011 needs T008 and T010.
- **US3 / US4 (Phases 5–6)**: Depend on Phase 2 only.
- **Polish (Phase 7)**: After the stories you intend to ship. T015 can start any time.

### Within a story

Most tasks edit `PRV`, so tasks touching it run one at a time. Only the tasks in other files (T010, T012, T015) carry [P].

### Parallel Opportunities

- T010 (`CommandCatalog.cs`), T012 (`ClipEditorView.cs`) and T015 (`PreferencesWindow.cs`) can run alongside the `PRV` work.

## Parallel Example: User Story 2

```bash
# While T008/T009 edit PianoRollView.cs:
Task: "Add roll.fitNotes to src/managed/Nota.Application/Palette/CommandCatalog.cs"   # T010
Task: "Add the Fit header button in src/managed/Nota.App/ClipEditorView.cs"          # T012
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 2 (T001–T004). Check that nothing looks different at 12 px.
2. Phase 3 (T005–T007). ⌘⌥ + wheel works. **Stop and validate** with quickstart rows 1–5.

### Incremental Delivery

1. Add US2 (Fit to notes). Validate with rows 8–11 and 17.
2. Add US3 (keys drag), then US4 (pinch).
3. Phase 7: shortcuts list, verification, docs and the build gate. The feature ships as a single PR (constitution V).

## Notes

- Spec traceability: FR-001 → T001–T003 · FR-002/003 → T005–T006 · FR-004 → T013 · FR-005 → T014 · FR-006/007 → T008–T012 · FR-008 → T001 · FR-009 → T002 · FR-010 → T003/T008 · FR-011 → T001 (research R9: only C rows are labelled, so no thinning is needed) · FR-012 → T015.
- Don't commit. The user reviews and commits the changes.
