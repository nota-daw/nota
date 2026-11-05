# Implementation Plan: Mixer Groups and Compact Strips

**Branch**: `070-mixer-groups-compact-strips` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/070-mixer-groups-compact-strips/spec.md`

## Summary

Make groups readable and foldable in the Mixer, and add an optional compact strip layout
(GitHub issue nota-daw/nota#386). The Mixer lists strips in the Arrangement's hierarchical
order (group strip, then members), draws a coloured, named bar plus a faint tint over each
group's strips (one bar row per nesting level), and folds a group from a triangle on its
strip — one fold state shared with the Arrangement, now saved in the project's
`track-view.json` sidecar. The Mixer follows the selected track. A footer `Compact` toggle
switches every strip to a 72 px layout: a wide meter with a triangular volume handle on it,
the existing `PanBar` and bar-style sends, full-width M / S / Arm, as agreed in the
[mockup](mockups/). UI-only; no engine or C ABI change.
Details: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill — the
  bands and the compact strip follow the agreed [mockup](mockups/) (R9).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: per-user `settings.json` — one new field `MixerCompact`; per-project
`track-view.json` sidecar — one new field `collapsedGroups`. See
[data-model.md](data-model.md).

**Audio-Thread Impact**: None. Folding and layout are view state; meters are read on the
UI tick as today, and hidden strips read none (fewer reads, not more).

**Testing**: `scripts/build.sh` (smoke test, unaffected); the scenarios in
[quickstart.md](quickstart.md) through a headless Avalonia harness with screenshots;
SC-005 by an offline render folded vs unfolded, compared byte for byte.

**Target Platform**: macOS, Windows, Linux desktop.

**Project Type**: desktop app

**Performance Goals**: fold / unfold < 100 ms at 100 tracks, without rebuilding strips
(SC-002); band layer redraws only on layout changes; no per-tick allocations added.

**Constraints**: one `MixerView` instance, shared by the docked panel and the pop-out
window (specs/054); compact strip ≤ 77 px wide (FR-018); regular layout stays the default
and unchanged apart from order, bands, fold control and selection outline.

**Scale/Scope**: ~8 files changed (DESIGN.md included) and 4 new in `src/managed/Nota.App`, 1 in
`Nota.Application`; `en.json` / `fr.json` strings.

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue first | Pass — nota-daw/nota#386 carries `approved` (2026-11-02) |
| I. Real-time audio safety | Pass — no audio-thread code touched |
| II. Boundary discipline | Pass — no C ABI / JUCE change |
| III. Licensing hygiene | Pass — no new dependency; new `.cs` files get the SPDX header |
| IV. Verification gate | Pass — `scripts/build.sh` before PR; no engine behaviour change, no smoke-test extension |
| V. One logical change | Watch — two stories in one spec. Groups (US1+US2) and compact strips (US3) are independent; tasks keep them in separate phases so they can ship as two PRs if review prefers |
| VI. Docs in sync | Planned — CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs` (Mixer page EN + RU, screenshots), `nota-shortcuts` check (no new binding) |
| VII. Spec-first | Pass — this run |
| VIII. Rules not weakened | Pass |

Post-design re-check: unchanged — the design adds no native code, no dependency; the only
new project data is an additive, cosmetic sidecar field.

## Project Structure

### Documentation (this feature)

```text
specs/070-mixer-groups-compact-strips/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/ui-mixer.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   └── Settings.cs                      # + MixerCompact
└── Nota.App/
    ├── GroupFold.cs                     # NEW: shared fold state + Changed event (R2)
    ├── ArrangementView.Groups.cs        # _collapsed → GroupFold; + HierarchyOrder (R1)
    ├── ArrangementView.Sizing.cs        # track-view.json: + collapsedGroups (R2)
    ├── ArrangementView.cs               # triangle → GroupFold.Toggle (2 call sites)
    ├── MixerView.cs                     # order, fold, bands host, selection, compact switch
    ├── MixerView.Compact.cs             # NEW: compact strip builders + StripMetrics (R6)
    ├── MainWindow.Mixer.cs              # GroupFold wiring, selection follow (R5)
    ├── MainWindow.axaml.cs              # create GroupFold, hand to both views
    ├── Controls/GroupBandsLayer.cs      # NEW: bars + tint behind the strip row (R3)
    ├── Controls/MeterFader.cs           # NEW: wide meter + triangular handle (R7)
    └── Languages/en.json, fr.json       # new strings
DESIGN.md                                # Accepted exceptions: the compact meter's 18 px channels
```

**Structure Decision**: all work stays in `Nota.App` (presentation) plus one settings field.
`MainWindow.Detail.cs` is deliberately not touched: selection following is wired in
`MainWindow.Mixer.cs` (R5).

## Complexity Tracking

No violations to justify.
