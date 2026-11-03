# Tasks: Pick the Bend lane's range in the piano roll

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Stories 1 and 2 — pick a range, never clip (P1)

- [x] T001 `BendLaneRange` (steps, default, `Fits`, `Shown`) in `src/managed/Nota.Application/Editing/BendLaneRange.cs` (FR-002)
- [x] T002 `BendLaneRangeTests` in `tests/Nota.SmokeTest`, in the full run and alone as `--bend-range`
- [x] T003 The range label as a button — caret, hover ink, hand cursor, a point under it still grabbable — in `src/managed/Nota.App/PianoRollView.Expression.cs` (FR-001)
- [x] T004 The range menu: every step, the shown one checked, too-narrow steps disabled (FR-001, FR-003)

## Phase 2: User Story 3 — the pick stays with the clip (P2)

- [x] T005 `BendPick` per clip for the session, keyed like the view state; on the instance without a clip identity (FR-004)

## Phase 3: Polish

- [x] T006 Preferences → Shortcuts row, French in `fr.json` (FR-005)
- [x] T007 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T008 `scripts/build-linux.sh` steps pass (engine, managed, smoke test incl. localization checks)
