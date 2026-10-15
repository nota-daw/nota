# Implementation Plan: Resizable track height and track-list width

**Branch**: `015-track-list-sizing` | **Date**: 2026-10-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/015-track-list-sizing/spec.md`

## Summary

Make the arrangement's track rows resizable per track (edge drag with a light snap to
30 / 64 / 128 px, range 30–256; Alt-drag or a context-menu preset for all tracks;
multi-selection resizes together) and the header column resizable (seam drag,
160–480 px). Headers pick a compact / standard / large layout from their height. All
of this is view-only: per-track heights replace the `RowHeight` constant in the
existing row-geometry table that the lanes already use, the `HeaderW` constant becomes a
property pushed to the header-column parts, and both values persist in a new bundle
sidecar, `track-view.json`, keyed by track index. No engine or C ABI changes.
Details and alternatives: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill — seams stay neutral, no new accent use.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.
- Engine rules ([`ARCHITECTURE.md`](../../ARCHITECTURE.md)) are untouched: no engine work.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing); `System.Text.Json` (existing, as other sidecars use). No new dependency.

**Storage**: `.nota` bundle sidecar `track-view.json` (see [contracts/track-view-sidecar.md](contracts/track-view-sidecar.md)); versioned automatically by `ProjectHistory` (all `*.json`).

**Audio-Thread Impact**: None — UI-only change; no engine calls are added.

**Testing**: `scripts/build.sh` (smoke test unchanged and must pass); UI verification by headless Avalonia / in-app screenshot recipe ([quickstart.md](quickstart.md)); manual gesture checks.

**Target Platform**: macOS, Linux, Windows (desktop app).

**Project Type**: desktop app (Avalonia).

**Performance Goals**: a resize drag follows the pointer at frame rate on a 40-track project (no full header rebuild per pointer move — research R4).

**Constraints**: existing arrangement gestures keep working (FR-014); no clipped/overlapping header controls at any size within limits (SC-003).

**Scale/Scope**: ~5 files in `src/managed/Nota.App` (`ArrangementView.cs`, `ArrangementView.Tracks.cs`, `ArrangementView.LaneControl.cs`, `MainWindow.Files.cs`, one new `ArrangementView.Sizing.cs`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Real-time audio safety | ✅ Pass | No engine/audio code touched. |
| II. Boundary discipline | ✅ Pass | No C ABI change; no JUCE. |
| III. Licensing hygiene | ✅ Pass | New `ArrangementView.Sizing.cs` gets the two-line SPDX header; no new dependency. |
| IV. Verification gate | ✅ Pass | No engine behaviour change → smoke test unchanged, still run via `scripts/build.sh`. |
| V. One logical change per PR | ✅ Pass | One feature: arrangement sizing. Visibility toggles deferred (Clarifications). |
| VI. Docs in sync | ✅ Planned | `CHANGELOG.md` [Unreleased] entry; `nota-features`, `nota-docs` (arrangement page EN+RU, screenshots), `nota-shortcuts` (new mouse gestures: edge drag, Alt-drag, double-click reset, seam drag). |
| VII. Spec-first | ✅ Pass | spec → clarify → this plan. |
| VIII. Rules not weakened | ✅ Pass | — |

**Post-design re-check (after Phase 1)**: still all pass — the design adds a sidecar and
view code only.

## Project Structure

### Documentation (this feature)

```text
specs/015-track-list-sizing/
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── track-view-sidecar.md   # persisted JSON format
│   └── ui-gestures.md          # user-facing gesture/menu contract
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/managed/Nota.App/
├── ArrangementView.cs            # RowHeight const → per-track lookup; HeaderW const → property;
│                                 # BuildTrackCard split by tier (compact / standard / large);
│                                 # header-column parts registered for width updates; seam grips
├── ArrangementView.Sizing.cs     # NEW: sizes state, snap/clamp, drag controllers (row + column),
│                                 # live relayout, presets, sidecar Save/LoadTrackView
├── ArrangementView.Tracks.cs     # duplicate/copy/paste carry heights; "Track height" submenu
├── ArrangementView.LaneControl.cs# seam band hit-test + cursor + start of a row resize in lanes
└── MainWindow.Files.cs           # call Save/LoadTrackView beside Save/LoadSections; reset on New

CHANGELOG.md, FEATURES.md         # docs (via skills)
../nota-docs                      # user manual EN + RU (via nota-docs)
Preferences → Shortcuts list      # via nota-shortcuts
```

**Structure Decision**: all code in `Nota.App` beside the existing `ArrangementView`
partials; the new `ArrangementView.Sizing.cs` partial keeps the sizing logic in one
place, in the same way `ArrangementView.Sections.cs` and `ArrangementView.Groups.cs` do.

## Implementation Outline (for /speckit-tasks)

1. **Geometry**: `DefaultRowHeight = 64`, `MinRowHeight = 30`, `MaxRowHeight = 256`,
   presets; `_trackHeights` dictionary; `TrackRowHeight(TrackVM)`; `RebuildRowGeometry`,
   `RowHeightAt` and every `RowHeight` use (`_lanes.Height`, `_headers.Height`, card
   `Height`) read it.
2. **Width**: `HeaderW` property backed by `_headerW`; register parts; `SetHeaderWidth`.
3. **Header tiers**: `HeaderTier` enum plus `Tier(h)`; `BuildTrackCard` dispatches to
   compact / standard (today's) / large builders that share the control factories
   (chips, fader, pan, meter, combos) so the wiring (undo gestures, automation write,
   `_volFaders`/`_meters` registries) is written once.
4. **Row drag**: header seam grips plus the lane seam band → `BeginRowResize(trackId, mods)`,
   `UpdateRowResize(y)` (snap, clamp, target set: Alt → all, in multi-selection →
   selection, else the one track), `EndRowResize()`; double-click → reset; live relayout
   per R4.
5. **Column drag**: a seam grip on the outer grid; live `SetHeaderWidth`; double-click resets.
6. **Menu**: "Track height" submenu (selection plus all tracks).
7. **Carry-over**: duplicate/paste heights (R9); new tracks use the default.
8. **Persistence**: `SaveTrackView(dir)` / `LoadTrackView(dir)` sidecar; call sites in
   `MainWindow.Files.cs` (open, save); reset on New (beside `ClearSections`).
9. **Verify and docs**: quickstart scenarios; CHANGELOG; nota-features / nota-docs /
   nota-shortcuts.

## Complexity Tracking

No constitution violations — nothing to justify.
