# Implementation Plan: Docked Mixer

**Branch**: `054-docked-mixer` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/054-docked-mixer/spec.md`

## Summary

Let the full Mixer dock as its own bottom panel in the main window (under the main view,
above the Devices / Clip panel), keep the floating `MixerWindow`, switch between them from
the panel, the window, the View menu and the palette, and restore shown / placement /
height per user on restart. Strips adapt to the height they get. Technically: the single
existing `MixerView` instance is reparented between hosts (as `DetailWindow` already
does), `BodyGrid` rows are built for the visible set of bottom panels by one function,
and `MixerView` gains a height tier. UI-only; no engine or ABI change. Details:
[research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill: the
  docked panel copies `DetailPanel`'s docked-on-a-seam look and 32 px toolbar.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: per-user `settings.json` via `ISettingsService` (`Settings.cs`) — three new
fields, see [data-model.md](data-model.md). No project-file change.

**Audio-Thread Impact**: None — UI-only re-hosting and layout. Meter reads stay on the
UI tick, unchanged; the Mixer is never ticked twice.

**Testing**: `scripts/build.sh` (smoke test, unaffected); feature validated with the
scenarios in [quickstart.md](quickstart.md), driven through a headless Avalonia harness
with screenshots where the GUI can't be launched.

**Target Platform**: macOS, Windows, Linux desktop (Linux native frame: no custom drag
area — the dock button still sits in the title row).

**Project Type**: desktop app

**Performance Goals**: docked ↔ floating switch < 0.2 s with no strip rebuild (SC-002);
tier changes only toggle visibility/height, never rebuild strips; no extra meter cost.

**Constraints**: one `MixerView` instance; main view keeps ≥ 120 px; minimum heights
derived from measured content × UI scale.

**Scale/Scope**: ~6 files in `src/managed/Nota.App`, 2 in `Nota.Application`;
projects with 50+ tracks must scroll horizontally in the panel as in the window.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code touched |
| II. Boundary discipline | Pass — no C ABI / JUCE change |
| III. Licensing hygiene | Pass — no new dependency; any new `.cs` file gets the SPDX header |
| IV. Verification gate | Pass — `scripts/build.sh` before PR; no engine behaviour change, so no smoke-test extension |
| V. One logical change | Pass — one feature; the adaptive strips are part of it (clarified FR-014) |
| VI. Docs in sync | Planned — CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs` (Mixer page EN+RU + screenshots), `nota-shortcuts` check (no new binding) |
| VII. Spec-first | Pass — this run |
| VIII. Rules not weakened | Pass |

Post-design re-check: unchanged — the design adds no native code, no dependency, no
persistent project data.

## Project Structure

### Documentation (this feature)

```text
specs/054-docked-mixer/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/ui-commands.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                    # + MixerFloating, MixerShown, MixerDockHeight
│   └── Palette/CommandCatalog.cs      # + view.mixerFloat
└── Nota.App/
    ├── MainWindow.axaml               # MixerPanel + MixerSplitter in BodyGrid; View menu item
    ├── MainWindow.axaml.cs            # restore on Opened; splitter DragCompleted → settings
    ├── MainWindow.Mixer.cs            # placement controller (show/hide/toggle/float/dock)
    ├── MainWindow.Detail.cs           # RowDefinitions[2] → DetailRow; LayoutBottomPanels()
    ├── MainWindow.DetailWindow.cs     # same accessor change
    ├── MainWindow.Commands.cs         # bind view.mixerFloat
    ├── MainWindow.Palette.cs          # docked focus → PaletteOrigin.Mixer
    ├── MixerWindow.cs                 # dock button, DockRequested; MinHeight from MixerView
    └── MixerView.cs                   # height tiers, MinContentHeight, auto-hidden toggles

CHANGELOG.md, FEATURES.md              # entries
../nota-docs                           # Mixer page EN + RU (nota-docs skill)
```

**Structure Decision**: all work is in the managed app's presentation code
(`Nota.App`) plus two small `Nota.Application` additions; the existing partial-class
split of `MainWindow` is followed (`MainWindow.Mixer.cs` owns placement).

## Implementation Order

1. `MixerView` height tiers + `MinContentHeight` + auto-hidden toggles (works in the
   window alone — a shippable improvement on its own).
2. `BodyGrid` refactor: `LayoutBottomPanels()`, `DetailRow` accessor; Devices panel
   behaviour must be byte-for-byte the same with the Mixer hidden.
3. Docked panel + placement controller + settings + restore (US1, US2).
4. Pop-out / dock both ways + `view.mixerFloat` + menu (US3).
5. Palette origin, short-window clamping, edge cases; docs and changelog.

## Risks

- **Devices panel regressions** from the row refactor (#53 height drift, Tab cycling,
  pop-out). Mitigation: step 2 lands alone and is checked against the existing flows
  before the Mixer is added.
- **Reparenting across windows** (#164 stale layout entries): reuse `SetHost` /
  `FlushOtherWindows`, never set `Content` directly.
- **Shutdown recording "hidden"** when the app closes `MixerWindow`: guard with
  `_shuttingDown`.

## Complexity Tracking

None — no constitution violations.
