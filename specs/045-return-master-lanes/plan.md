# Implementation Plan: Automation and resizable height for return and master rows

**Branch**: `045-return-master-lanes` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/045-return-master-lanes/spec.md`

## Summary

Return rows get the automation lane editing regular tracks have (target pill + menu,
points, curves, range select / clipboard), and return and master rows become resizable
(seam drag, Compact / Standard / Large in the context menu), Compact by default. The
pinned footer is capped at about half the arrangement height and scrolls past it. The
engine and the project file already handle automation lanes on return tracks, so the
change is in `Nota.App`: the arrangement's automation editor is pulled out of
`LaneControl` into a row-agnostic editor that both the track lanes and the footer lanes
use; the footer gets per-row geometry, seam grips and a vertical scroller; the
`track-view.json` sidecar gains return and master heights. See [research.md](research.md).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only). No C++ change expected; see R1.

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: `.nota` bundle. Return automation lanes are already saved in the project
document (`ProjectService` writes `t.Automation` for every track, returns included, and
reloads them after `AddReturnTrack`). Return and master heights go into the existing
`track-view.json` sidecar ([contract](contracts/track-view-sidecar.md)).

**Audio-Thread Impact**: None new. Return lanes use the existing automation ABI
(`AddAutomationLane` / `SetAutomationPoints[Live]` → new graph snapshot, AR-5), and the
block-rate `applyAutomation` pass already visits return tracks. Heights are view state.

**Testing**: `tests/Nota.SmokeTest` gets a return-automation round trip (add lane on a
return, set points, render, check level follows; save/load keeps the lane). UI behaviour is
checked with the [quickstart](quickstart.md) scenarios and headless screenshots (agent
memory `nota-ui-verify`).

**Target Platform**: macOS, Windows, Linux desktop (Avalonia).

**Project Type**: desktop app.

**Performance Goals**: footer seam drag follows the pointer with no visible lag; at most
5 footer rows (4 returns + master, `kMaxReturns = 4`), so rebuilding footer headers per
frame is acceptable.

**Constraints**: track-row automation, resizing and gestures must not regress (the
editor refactor touches `LaneControl`).

**Scale/Scope**: ~5 files in `src/managed/Nota.App`, one smoke test, one sidecar extension.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|-----------|--------|------|
| I. Real-time audio safety | Pass | No audio-thread code; existing automation path reused |
| II. Boundary discipline | Pass | Existing C ABI calls only; no JUCE outside `pluginhost/` |
| III. Licensing hygiene | Pass | No dependency; the new `ArrangementView.AutoEditor.cs` carries the SPDX header |
| IV. Verification gate | Pass | `scripts/build.sh` + new smoke check for return automation |
| V. One logical change | Pass | One PR: return/master rows in the arrangement footer |
| VI. Docs in sync | Planned | CHANGELOG `[Unreleased]`, nota-features, nota-docs (arrangement / automation / mixing pages), nota-shortcuts wording check |
| VII. Spec first | Pass | spec → plan → tasks |
| VIII. Rules not weakened | Pass | — |

Post-design re-check: unchanged, all pass.

## Project Structure

### Documentation (this feature)

```text
specs/045-return-master-lanes/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── track-view-sidecar.md   # delta on specs/015's sidecar
│   └── ui-gestures.md          # footer gestures and menu
└── tasks.md                    # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/Nota.App/
├── ArrangementView.AutoEditor.cs   # NEW: row-agnostic automation editor (press/move/release,
│                                   #      hover, bend, range select, draw) extracted from LaneControl
├── ArrangementView.LaneControl.cs  # track lanes delegate automation mode to the editor
├── ArrangementView.Controls.cs     # FooterLaneControl: per-row geometry, return rows via the editor,
│                                   #      master-volume row kept, lane-side seam band
├── ArrangementView.Sizing.cs       # per-kind default height, footer heights, master height,
│                                   #      footer resize, sidecar returns + masterHeight
├── ArrangementView.cs              # footer layout: ScrollViewer + cap, row tops, BuildSlimRow tiers,
│                                   #      ShowFooterMenu "Height" row, drop-row hit test by geometry
└── ArrangementView.Tracks.cs       # TrackHeightRow variant without the all-tracks modifier

tests/Nota.SmokeTest/
└── ReturnAutomationTests.cs        # NEW: return lane render + save/load round trip
```

**Structure Decision**: UI-only change in `Nota.App` (the arrangement lives there, not in
`Nota.Presentation`), plus one smoke test. The automation editor is extracted into its own
partial file so the footer can reuse it instead of duplicating ~400 lines (R2).

## Complexity Tracking

No constitution violations.
