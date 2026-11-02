# Implementation Plan: Reset a note's velocity to the default

**Branch**: `069-velocity-reset-default` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/069-velocity-reset-default/spec.md`

## Summary

A double-click on a note's stem in the piano roll's velocity lane, or **Reset velocity** from the stem's right-click menu, puts the note (or the whole selection, when the stem belongs to it) back to the default velocity 0.8. The reset is one undo step that returns to the velocities from before the double-click. Managed UI only: the lane (`PianoRollView.VelocityLane`) gains the two gestures; the rule "which notes, to what" is a small pure helper in `Nota.Application/Editing` so the smoke test can cover it; the undo behaviour reuses the velocity-wheel burst (specs/063). No engine, ABI or file-format change.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (menu follows the existing piano-roll lane menus; no new visual element) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md); user-visible text goes through `Loc` with a French entry ([specs/068-ui-language](../068-ui-language/spec.md)).
- Engine rules (`ARCHITECTURE.md` AR table): not touched.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app); no C++ change.

**Primary Dependencies**: Avalonia 12 (already used); no new dependency.

**Storage**: N/A — a velocity is already a field of every note in the project file; nothing new is saved.

**Audio-Thread Impact**: None: UI-only change. A reset pushes notes through the existing live path (`SetClipNotesLive`, no undo checkpoint) and the existing undo seed, exactly as a velocity-wheel burst does; nothing new reaches the audio thread.

**Testing**: smoke test (`tests/Nota.SmokeTest`): the pure rule (`VelocityReset`) in a new `VelocityResetTests`, run by `scripts/build.sh`; localization checks (French key present, no raw literal). The gesture wiring in the lane is verified by hand (quickstart) and by a screenshot hook run, as the lane has no headless pointer harness.

**Target Platform**: macOS, Windows, Linux (desktop app).

**Project Type**: desktop-app.

**Performance Goals**: a reset on a 1,000-note selection is imperceptible (one pass over the selection, one live push).

**Constraints**: the reset must not add a second undo step on top of the first click of a double-click; locked and ghost notes are never touched; other lane tabs unchanged.

**Scale/Scope**: one lane, two gestures, one helper, ~3 new interface strings, two docs rows.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code; edits use the existing command path. |
| II. Boundary discipline | Pass — no ABI change. |
| III. Licensing hygiene | Pass — new files carry the SPDX header; no dependency. |
| IV. Verification gate | Pass — pure rule covered by the smoke test; `scripts/build.sh` runs it. |
| V. One logical change per PR | Pass — one feature: reset velocity. |
| VI. Documentation in sync | Planned — CHANGELOG, FEATURES.md, Shortcuts pane (`nota-shortcuts`), user manual EN + RU (`nota-docs`), French strings. |
| VII. Spec-first | Pass — spec, plan, tasks before code. |
| VIII. Safety rules not weakened | Pass. |

Re-checked after design: no change. No violation to justify.

## Project Structure

### Documentation (this feature)

```text
specs/069-velocity-reset-default/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/velocity-lane.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Application/Editing/VelocityReset.cs        # new: the default and the pick/apply rule (pure)
└── Nota.App/
    ├── PianoRollView.cs                             # VelocityLane: double-click, right-click menu,
    │                                                # burst reuse; s_lastVel and KeyVelocity read the shared default
    ├── PreferencesWindow.cs                         # Shortcuts → Piano roll: two rows
    └── Languages/fr.json                            # French for the new texts
tests/Nota.SmokeTest/
├── VelocityResetTests.cs                            # new
└── Program.cs                                       # run the new tests
CHANGELOG.md · FEATURES.md                           # entries
../nota-docs (separate repo)                         # piano-roll page EN + RU, coverage.yml
```

**Structure Decision**: the rule lives beside `VelocityWheel` and `NoteOverwrite` in `Nota.Application/Editing` (pure, testable); the lane keeps pointer handling, menu and undo timing, like the wheel does.

## Complexity Tracking

No violations.
