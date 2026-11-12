# Implementation Plan: Arrangement group trays

**Branch**: `feat/arrangement-group-trays` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/094-arrangement-group-trays/spec.md`
(issue nota-daw/nota#584, milestone 0.60.1)

## Summary

Redraw groups in the Arrangement's track headers per look. In the **new look** a group is a
tray: its header row on a 16 % wash, the rest of the tray on an 8 % wash with a 3 px edge in
the group colour, members as plain cards carrying only their own colour bar, nested trays
stepped in 6 px per level, and a 4 px closing strip carved out of the last row's height.
In the **classic look** the headers return to 0.59.2's staircase (own 3 px spine stepped in
10 px per depth, raised / card surfaces, the 0.59.2 slim group row with its meter on its own
line). In both looks header meters fit inside their card (the root cause is the vertical
`MeterBar`'s `MinHeight = 40`), the slim group row's meter moves to the right-hand column
(new look), controls keep their columns at every depth, and a folded group shows how many
tracks it hides. The tray / spine geometry is a pure layout in `Nota.Presentation`, tested
by the smoke test; `ArrangementView` paints it and rebuilds on a look change. See
[research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) § Track header, § Track colours, and the
  `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: N/A — the look is already stored (spec 091); fold state and row heights are
already per project.

**Audio-Thread Impact**: None: UI-only change. Meters keep reading the existing level feed
on the UI tick.

**Testing**: `tests/Nota.SmokeTest` — new `GroupTrayTests` on the pure layout
(`Nota.Presentation`); `DesignTokenCheck` keeps the source free of raw radii / colours;
visual check with the headless-Avalonia screenshot harness (quickstart).

**Target Platform**: macOS, Windows, Linux desktop (Avalonia).

**Project Type**: Desktop app.

**Performance Goals**: Header rebuild on a look switch within one frame (SC-005); no new
per-tick work (tray layers are static; meters unchanged).

**Constraints**: Header rows never change height (FR-009); controls' right content edge is
the same for every row (FR-007); colours only from `NotaPalette` / track tints, radii from
`NotaRadius`.

**Scale/Scope**: Typical projects 10–100 rows, nesting rarely deeper than 3; the layout is
O(rows × depth).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|---|---|
| 0. Approved issue first | ✅ nota-daw/nota#584 carries `approved`, milestone 0.60.1 |
| I. Real-time audio safety | ✅ No engine or audio-thread change |
| II. Boundary discipline | ✅ No C ABI change, no JUCE |
| III. Licensing hygiene | ✅ New files get the two-line SPDX header; no dependency |
| IV. Verification gate | ✅ `scripts/build.sh` + smoke test; new `GroupTrayTests` |
| V. One logical change per PR | ✅ One PR: Arrangement group headers in both looks + meter fit |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]`, DESIGN.md § Track header, nota-docs (arrangement / groups page screenshots), FEATURES.md via nota-features if the folded count counts as a capability |
| VII. Spec-first | ✅ spec → clarify → this plan |
| VIII. Rules not weakened | ✅ N/A |

Post-design re-check (after Phase 1): unchanged — the design adds one pure class in
`Nota.Presentation`, one paint layer and builder branches in `Nota.App`; nothing crosses
into the engine.

## Project Structure

### Documentation (this feature)

```text
specs/094-arrangement-group-trays/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1: the layout's inputs and per-row slices
├── quickstart.md        # Phase 1: how to see and verify it
├── contracts/
│   └── header-geometry.md   # the visual contract: numbers per look
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Presentation/
│   └── GroupTrayLayout.cs          # NEW pure layout: rows → per-row slices (both looks)
└── Nota.App/
    ├── ArrangementView.cs          # HeaderBars → slice-based; BuildGroupBar / BuildTrackCard
    │                               #   per look; meters MinHeight 0; folded count; HeaderLook
    ├── ArrangementView.Groups.cs   # feed the layout from _tracks (Depth, GroupId, fold)
    ├── ArrangementView.LaneControl.cs  # group lane wash: new look only
    ├── Controls/GroupTrayLayer.cs  # NEW: draws one row's tray slice (washes, edges, corners,
    │                               #   closing strips) under the card content
    └── MainWindow.axaml.cs         # set Timeline.HeaderLook at start-up and on a look change

tests/Nota.SmokeTest/
├── GroupTrayTests.cs               # NEW: layout rules (SC-001, SC-003, closings, counts, classic)
└── Program.cs                      # register GroupTrayTests

DESIGN.md                            # § Track header: trays (new), staircase (classic), meter
CHANGELOG.md                         # [Unreleased] Changed / Fixed
```

**Structure Decision**: the geometry is data (`Nota.Presentation`) so it is testable without
a window; painting and control layout stay in `Nota.App` beside the existing header
builders. No new project.

## Design outline

1. **Layout** (`GroupTrayLayout.Compute(rows, look)`): see [data-model.md](data-model.md).
   New look: chain, heads, closings, content left `k * 6`; classic: spine x `depth * 10`,
   content left `depth * 10 + 3`. Folded count passed through from `GroupSlotCount`.
2. **Painting**: `GroupTrayLayer` (custom `Render`) draws, for one row, each tray in the
   chain outer → inner: the 8 % wash (16 % on the tray's own header row) over the full
   width from the tray's edge, its 3 px edge, the rounded top-right on a head row and the
   rounded bottom-right on the closing strip. The member card body is a `Brush.Panel`
   rectangle from `contentLeft` to `HeaderW − TrayInsetR`, above the closing strips.
   Replaces `HeaderBars` + the 6 % / 16 % `Border` tints in the new look.
3. **Builders**: `BuildTrackCard` / `BuildGroupBar` take the slice: content margin from
   `contentLeft`, bottom margin from closings, a constant right content edge. Slim group
   row (new): chevron, name (+ folded count), chips, vertical meter right; the "Group" tag
   goes (the tray says it). Classic: the 0.59.2 slim row and `SpineGrid`-style columns.
4. **Meters**: header `MeterBar`s get `MinHeight = 0`; margins symmetric; checked on
   Compact / Standard / Large.
5. **Look switch**: `ArrangementView.HeaderLook` → `RebuildHeaders()` + lanes invalidate;
   `MainWindow` sets it from `NotaThemeService.Look` at start-up and on `Changed` when it
   differs.
6. **Lanes**: group lane colour wash only when `HeaderLook == New`.
7. **Docs**: DESIGN.md § Track header (replace the "indented 14 … 4 px bar" bullet),
   CHANGELOG, nota-docs screenshots of grouped tracks in both looks.

## Complexity Tracking

No constitution violations.
