# Implementation Plan: Piano roll vertical zoom

**Branch**: `019-piano-roll-vertical-zoom` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/019-piano-roll-vertical-zoom/spec.md`

## Summary

Make the piano roll's row height variable (6–36 px, default 12) and drive it with
⌘⌥ + wheel, a vertical drag on the keys column and ⌥ + trackpad pinch, all anchored on
the pitch under the pointer, plus a **Fit to notes** command (⇧F, header button, command
palette). The fixed `RowH` constant in `PianoRollView` becomes instance state saved in the
existing per-clip view state. UI-only: no engine, ABI or project-format change.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill — the
  Fit button is a neutral header control, not accent.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- Engine / instrument / build docs: not touched.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12.0.3 (existing) — uses
`InputElement.PointerTouchPadGestureMagnifyEvent`; no new dependency

**Storage**: N/A — per-clip view state stays in memory for the app session

**Audio-Thread Impact**: None: UI-only change; no engine calls added

**Testing**: Smoke test unchanged (no engine change); feature verified with the headless
Avalonia harness and manual scenarios in [quickstart.md](quickstart.md)

**Target Platform**: macOS, Windows, Linux desktop (pinch: wherever Avalonia raises the
touchpad magnify event — macOS)

**Project Type**: desktop app

**Performance Goals**: Zoom follows the wheel/drag at display rate; the grid already
redraws all 88 rows per frame, unchanged in cost

**Constraints**: Existing wheel meanings unchanged; ⇧F must not sound a note while the
grid has focus; zoom ignored mid note-gesture

**Scale/Scope**: ~4 files edited, no new source files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|-----------|--------|------|
| I. Real-time audio safety | ✅ Pass | No audio-thread code touched |
| II. Boundary discipline | ✅ Pass | No ABI change, no JUCE |
| III. Licensing hygiene | ✅ Pass | No new files expected; any new file gets the SPDX header; no dependency |
| IV. Verification gate | ✅ Pass | `scripts/build.sh` must stay green; no engine behaviour change → no smoke-test extension |
| V. One logical change per PR | ✅ Pass | One feature, one PR |
| VI. Docs in sync | ✅ Planned | CHANGELOG `[Unreleased]` Added entry; `nota-features`, `nota-docs`, `nota-shortcuts` skills |
| VII. Spec-first | ✅ Pass | spec → clarify → plan done |
| VIII. Safety rules not weakened | ✅ Pass | — |

**Post-design re-check**: still all pass — the design (research R1–R10) stays inside
`Nota.App` and `Nota.Application/Palette`.

## Design

See [research.md](research.md) for decisions and alternatives. In short:

1. **Row height as state** (R1): `RowH` → instance property; `MinRowH/DefaultRowH/MaxRowH`
   = 6/12/36; `RangeH`, `PitchToY`, `YToPitch` become instance members; nested
   `KeysColumn` / `NoteGrid` read `_o.RowH`.
2. **Anchored zoom** (R2): `ZoomRowsAt(factor, viewportY)` and `SetRowHeightAt(h,
   viewportY)` resize `_keys` / `_gridControl` and queue the anchored offset via the
   existing `_pendingVOffset` / `ApplyPendingVOffset` path; `SaveView`.
3. **Inputs** (R3–R6): ⌘⌥ branch first in `NoteGrid.OnPointerWheelChanged` + new
   `KeysColumn.OnPointerWheelChanged`; keys-column press/drag/release with capture
   (exponential 0.01/px); ⌥ + `PointerTouchPadGestureMagnify` on the grid; all ignored
   while `_drag != Mode.None`.
4. **Fit to notes** (R7): `FitToNotes()` on the view; ⇧F in `NoteGrid.OnKeyDown`
   (handled, so the window's note map never sees it); `roll.fitNotes` in
   `CommandCatalog` + `MainWindow.Commands`; a Fit icon button in `ClipEditorView`'s header.
5. **Persistence** (R8): `ViewState` gains `RowH`; restored and clamped in `SetNotes`;
   default 12 for new clips.
6. **Shortcuts list**: four rows in `PreferencesWindow`'s "Piano roll" section
   ([contract](contracts/ui-gestures.md)).

## Project Structure

### Documentation (this feature)

```text
specs/019-piano-roll-vertical-zoom/
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── ui-gestures.md   # Phase 1: gestures, command, header control, shortcuts rows
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/managed/
├── Nota.App/
│   ├── PianoRollView.cs         # RowH state, zoom/fit, wheel/drag/pinch/⇧F, view state
│   ├── ClipEditorView.cs        # header Fit button
│   ├── MainWindow.Commands.cs   # bind roll.fitNotes
│   └── PreferencesWindow.cs     # Shortcuts rows
└── Nota.Application/
    └── Palette/CommandCatalog.cs  # roll.fitNotes entry

CHANGELOG.md, FEATURES.md        # docs; ../nota-docs via the nota-docs skill
```

**Structure Decision**: Presentation-only change inside the existing `Nota.App` piano
roll and the palette catalog in `Nota.Application`; no new projects or files.

## Complexity Tracking

No constitution violations — nothing to justify.
