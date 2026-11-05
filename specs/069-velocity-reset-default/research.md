# Research: Reset a note's velocity to the default

## R1. Where the default comes from

**Decision**: one shared constant, `VelocityReset.Default = 0.8f`, in `Nota.Application/Editing`. `PianoRollView.s_lastVel` (the value before any note was touched) and `KeyVelocity` (the playable keys) initialise from it.

**Rationale**: today 0.8 is written in two places (`PianoRollView.cs` lines 241 and 1850). Naming it once keeps "the default a new note gets" and "what the reset restores" from drifting apart. The reset itself never reads `s_lastVel` (spec FR-004).

**Alternatives**: a user setting for the default — out of scope (spec Assumptions); reading `s_lastVel` — rejected, it equals the edited note's velocity right after an edit, so the reset would do nothing.

## R2. What 0.8 is in MIDI terms

**Decision**: store 0.8f as is; the engine converts to 1..127 (≈ 101.6, so 102). A note whose velocity is within half a MIDI step of the default (|v − 0.8| < 0.5/127) counts as already at the default (FR-005: no change, no undo step).

**Rationale**: velocities are floats in the project; comparing exactly would call 0.8000001 "changed". The half-step tolerance matches how the roll shows velocity (`vel N` of 127).

**Alternatives**: exact compare — rejected (spurious undo steps); snapping all velocities to integers — out of scope.

## R3. One undo step for a double-click

**Problem**: a double-click is two presses. Today every press-release in the lane applies the clicked height and calls `CommitNotes()`, one undo step each. A reset on the second press would add a second step, so undo would return to the first click's value, not to the value before the double-click (spec FR-003).

**Decision**: reuse the velocity-wheel burst (specs/063: `_velWheelBurst`, `_velWheelTimer`, `EndVelocityWheel`, `Remember`). A lane gesture opens the burst (arrangement clips: seed the undo step with the notes as they were, `CommitNotes(seed: true)`, then push live; a Session slot: hold and commit when the burst closes). A press closes an open burst first, except the second press of a double-click on the same stem, which continues it. The burst closes after the existing idle time (600 ms) or before any undo/redo/other edit, as the wheel's does, and the `Remember` call keeps "last touched" in step.

**Rationale**: the wheel already solved "several nearby edits are one undo step" including the Session-slot case, and `EndVelocityWheel` is already called before undo and redo.

**Alternatives**:
- Undo the first click from the lane — the roll has no access to the engine's undo, and a lane that undoes would also undo unrelated steps.
- Restore the old velocities live, then commit — leaves an extra no-op undo step.
- Ignore it (undo returns to the first click's value) — a first click lands within a few pixels of the stem's current height, so the drift is small, but it costs a second ⌘Z and breaks the spec; rejected.

**Cost**: a lane drag or click in a Session slot reaches the engine when the burst closes (≤ 600 ms later), as the wheel does there. Arrangement clips push live, so playback follows at once.

## R4. The right-click menu

**Decision**: a `MenuFlyout` with one item, **Reset velocity** (`MenuKit.Item`, `enabled:` false when every target is already at the default), shown on a right-press that lands on a stem (the same `Nearest` pick as a left press). The press also applies the selection rule: a stem outside the selection becomes the selection; a stem inside keeps it. No menu on empty lane space.

**Rationale**: `MenuKit.Item` supports a disabled state (`ClipEditorKit.ShowMenu` does not); the other lane tabs open their "Clear" menu from the right press the same way (`PianoRollView.Expression.cs`).

**Alternatives**: `ClipEditorKit.ShowMenu` — radio-toggle items, no disabled state; a keyboard shortcut — out of scope.

## R5. Testing without a pointer harness

**Decision**: the rule is a pure function, `VelocityReset.Targets(selection, hit, count, isLocked, velocities)` plus `IsDefault`, tested in `VelocityResetTests` (single, selection, outside selection, locked, already default, tolerance, bounds). The lane wiring, the undo count and the menu are checked by hand per the quickstart, and the English / French strings by the existing localization checks.

**Rationale**: the smoke test has no way to inject pointer events into `PianoRollView`; the repository tests such views through pure helpers (`VelocityWheel`, `NoteOverwrite`, `NotePlacementDrag`).

## R6. Docs and strings

**Decision**: new interface text: `Reset velocity` (menu), and two Shortcuts rows — `Double-click a velocity stem`, `Right-click a velocity stem`, each with a description. All through `Loc.T` with entries in `fr.json`. `nota-shortcuts` checks the Shortcuts list; `nota-features` and `nota-docs` cover FEATURES.md and the manual (EN + RU, piano-roll page, `coverage.yml`).

**Rationale**: spec FR-008 and the project's Documentation-in-sync principle.
