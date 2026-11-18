# Implementation Plan: Modular tab in the browser, arrangement track headers in Modular

**Branch**: `feat/modular-browser-headers` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/095-modular-browser-headers/spec.md`
(issue nota-daw/nota#592)

## Summary

Modular view gets the arrangement's track header column and a browser tab of its own.
The header column is **the arrangement's own `ArrangementView` instance in a headers-only
mode**, shown beside the Modular canvas on the side **Track headers on the right** picks —
so every card, control, gesture, menu, meter, fold, row height and look is the arrangement's
by construction, and a header click already routes to the Modular graph. ModularView drops its
"Tracks" rail; the rail's cross-track CV drop moves onto the header column through a
"track under this point" query. Group tracks open their effect chain as a graph (the engine
already runs CV modulation on groups — no engine change). The browser gets a **Modular** tab
(index 9, on the rail after MIDI effects, a new patch-cable glyph) listing `ModularCatalog`
nodes; it shows only while Modular is the view, with its own remembered tab. Nodes drag onto
the canvas (at the drop point) or double-click in (at the centre). The toolbar's **+ Add**
goes. A **Show in Modular** item joins the arrangement's and session's track menus. See
[research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (rail tabs, track header, menus) and the
  `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: N/A — no project-format change. Node positions keep using the existing
`modular-layout.json` sidecar (group graphs use the same per-track keys). Browser tab memory
is runtime only, as the last tab is today.

**Audio-Thread Impact**: None: UI-only change. Adding a node and patching a group's effect
use the existing modulator / CV-link calls (immutable snapshot republish, AR-5).

**Testing**: `tests/Nota.SmokeTest` — new pure tests `ModularBrowserTests`
(`ModularBrowserItems` mapping + search, `BrowserTabMemory`); `DesignTokenCheck` keeps the new
code on tokens; the localisation check covers new keys. Visual / interaction check with the
headless screenshot harness and by hand (quickstart).

**Target Platform**: macOS, Windows, Linux desktop (Avalonia).

**Project Type**: Desktop app.

**Performance Goals**: No new per-tick work (meters already tick for the arrangement). Entering
Modular does one header rebuild at most (SC-005).

**Constraints**: The arrangement must look and behave exactly as before when it is the shown
view; headers-only mode is a pure visibility / width switch. Colours from `NotaPalette`, radii
from `NotaRadius`, strings through `Loc`.

**Scale/Scope**: Projects up to ~100 rows; 7 catalogue nodes today.

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #592 carries `approved`; Show in Modular was added to its text with the user's go-ahead. |
| I. Real-time safety | ✅ No engine / audio-thread code touched. |
| II. Boundary discipline | ✅ No new C ABI; existing calls only. |
| III. Licensing | ✅ New files get the SPDX header; no dependency. |
| IV. Verification gate | ✅ `scripts/build.sh` + new smoke tests on the pure pieces. |
| V. One logical change | ✅ One PR for #592 (tab, headers, groups, menu item — one feature). |
| VI. Docs in sync | ✅ CHANGELOG, FEATURES.md, nota-docs (Browser, Modular pages EN + RU), RU strings. |
| VII. Spec first | ✅ spec → clarify → plan → tasks. |
| VIII. Rules not bent | ✅ Nothing to bend. |

Post-design re-check: unchanged — the design keeps everything in `Nota.App` /
`Nota.Presentation`.

## Project Structure

### Documentation (this feature)

```text
specs/095-modular-browser-headers/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/ui-contract.md
└── tasks.md            # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Presentation/
│   ├── ViewModels/BrowserViewModel.cs        # BrowserItemKind.ModularNode, BrowserItem.Keywords
│   ├── ModularBrowserItems.cs                # NEW: catalogue → rows, search match
│   └── BrowserTabMemory.cs                   # NEW: per-context last tab
├── Nota.App/
│   ├── ArrangementView.HeadersOnly.cs        # NEW: HeadersOnly mode, TrackAtHeaderPoint, column hint
│   ├── ArrangementView.cs                    # keep refs to the bands headers-only hides; Show in Modular item
│   ├── BrowserView.cs                        # ModularTab, SetModularContext, filter, refuse node drops elsewhere
│   ├── ModularView.cs                        # rail removed, + Add removed, groups, master guard,
│   │                                         # node drop, AddModulatorAt(kind, point), cross-track via host
│   ├── SessionView.Grid.cs                   # Show in Modular item
│   ├── MainWindow.axaml.cs                   # SetView: headers-only Timeline + inset Modular; ArrangementShown
│   ├── MainWindow.Browser.cs / .Dnd.cs       # node activation, tab context
│   ├── MainWindow.Menu.cs / .Input.cs        # Timeline.IsVisible → ArrangementShown
│   ├── Controls/Glyph.cs                     # GlyphKind.PatchCable
│   └── Localization (en / ru)                # new strings
tests/Nota.SmokeTest/
├── ModularBrowserTests.cs                    # NEW
└── Program.cs                                # register
```

**Structure Decision**: UI-only; the pure, testable pieces (catalogue rows, search, tab
memory) live in `Nota.Presentation`; the views in `Nota.App`.

## Delivery order

1. Headers-only mode + MainWindow layout + cross-track CV via the header column + rail removal
   (US2) — the biggest risk first.
2. Group graphs + master guard (US3).
3. Browser tab, drops, activation, tab memory (US1).
4. + Add removal (US4).
5. Show in Modular (US5).
6. Strings (EN + RU), CHANGELOG, FEATURES.md, docs, shortcuts check, build + smoke.

## Complexity Tracking

None.
