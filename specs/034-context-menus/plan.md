# Implementation Plan: Studio context menus

**Branch**: `034-context-menus` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/034-context-menus/spec.md`

## Summary

Every context menu in the app gets one Studio look and one order, and the track menu
gains inline rows. The look moves out of each call site into two shared places: a
**Nota `ControlTheme` for `MenuItem`** in `Theme/NotaTheme.axaml` (fixed icon · label ·
value-or-shortcut · caret columns, shortcut spelled `⌘G` in Geist Mono, a glyph caret,
neutral icons, `danger` and `inline` classes) and **new `MenuKit` building blocks**
(header row, colour grid, segmented row, switch item, value item, danger item, standard
clipboard group). Then each of the ~70 menu sites is moved onto those blocks and
re-ordered. Live freeze becomes a switch item; "Add track" gains a "Recent" group backed
by a new `Settings.RecentInstruments` list recorded wherever an instrument is added.
`DESIGN.md` § Menus and `DESIGN.html` describe the result; a new `RunMenus` guard keeps it.
Details and alternatives: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill — accent only for hover / chosen / switch on; danger ink is a semantic fill colour, not a second accent.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.
- Engine rules ([`ARCHITECTURE.md`](../../ARCHITECTURE.md)) are untouched: no engine work.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12.1.4 + Avalonia.Themes.Fluent (existing). No new dependency.

**Storage**: app settings JSON (`Settings` → `SettingsService`), new `RecentInstruments` list (see [data-model.md](data-model.md)). Nothing in the project file.

**Audio-Thread Impact**: None — UI-only change; existing engine calls are reused, none added.

**Testing**: `scripts/build.sh` (smoke test must pass); new `DesignTokenCheck.RunMenus` guard and a `RecentInstrumentsTests` unit test in `tests/Nota.SmokeTest`; headless Avalonia shots of the menus' content in all four themes ([quickstart.md](quickstart.md)).

**Target Platform**: macOS, Linux, Windows (desktop app). Shortcut spelling differs per platform (`⌘G` / `Ctrl+G`).

**Project Type**: desktop app (Avalonia).

**Performance Goals**: a menu opens without a visible delay (< 50 ms to build, as today); the colour grid is 27 small borders, no measurable cost.

**Constraints**: no colour snapshots (theme switch live); no path geometry outside `Glyph.cs`; labels sentence case through `NotaText`; Avalonia popups cannot carry a blurred shadow.

**Scale/Scope**: ~70 menu sites in 44 files of `src/managed/Nota.App` (inventory below); `MenuKit.cs`, `NotaTheme.axaml`, `Settings.cs`, `DeviceInsertService.cs`, MCP instrument tools; `DESIGN.md`, `DESIGN.html`, `CHANGELOG.md`, `FEATURES.md`, user docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code touched. |
| II. Boundary discipline | Pass — no C ABI change, no JUCE. |
| III. Licensing hygiene | Pass — new files carry the SPDX header; no dependency. The `MenuItem` theme is written fresh, not copied from Fluent's template. |
| IV. Verification gate | Pass — smoke test runs; a new `RunMenus` design guard and a recent-list unit test are added. |
| V. One logical change per PR | **Watch** — touching ~70 sites is large. Plan splits it into reviewable commits (foundation → track menus → Arrangement → Session/Modular → devices → rest) inside one PR, since a half-migrated app would look inconsistent; see Complexity Tracking. |
| VI. Docs in sync | Pass — CHANGELOG, DESIGN.md/.html, FEATURES.md (nota-features), user docs (nota-docs) planned; nota-shortcuts runs for the new Option/Alt-click on height keys (the Shortcuts pane lists mouse gestures). |
| VII. Spec-first | Pass — spec + clarifications done. |
| VIII. Safety rules not weakened | Pass. |

Post-design re-check: unchanged — Pass, with V tracked below.

## Project Structure

### Documentation (this feature)

```text
specs/034-context-menus/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── menu-kit.md      # MenuKit building blocks + MenuItem theme contract
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                      # + RecentInstruments list
│   └── RecentInstruments.cs             # NEW: record / prune / read (pure, testable)
├── Nota.Presentation/
│   └── DeviceInsertService.cs           # record a recent instrument on every instrument add
├── Nota.Mcp/Tools/
│   ├── InstrumentTools.cs               # record recent on add_instrument_track / set instrument
│   └── PluginTools.cs                   # record recent on add_plugin_instrument
├── Nota.Application/Input/Shortcut.cs   # + Spell(KeyGesture) → "⌘G" / "Ctrl+G"
└── Nota.App/
    ├── Theme/NotaTheme.axaml            # MenuItem ControlTheme, .danger, .inline, menu widths
    ├── MenuKit.cs                       # header, colour grid, segments, switch, value, danger, clipboard group
    ├── MenuKit.Rows.cs                  # NEW: the inline row controls (colour grid, segments)
    ├── Controls/Glyph.cs                # + any missing GlyphKind for menu icons (one-line comments)
    ├── ArrangementView.cs / .Tracks.cs / .Sizing.cs / .LaneControl.cs / .Sections.cs / .Automation.cs / .Controls.cs
    ├── MainWindow.Freeze.cs / .LiveFreeze.cs / .Updates.cs / .axaml.cs / .axaml
    ├── SessionView.Grid.cs / .Toolbar.cs / .Inspector.cs
    ├── ModularView.cs, BrowserView.cs, HistoryView.cs, PreviewPlayer.cs, SnapChip.cs, ClipEditorKit.cs
    ├── DeviceChainView.Clipboard.cs / .Shell.cs / .Fold.cs, DeviceCardKit.cs
    ├── DeviceCards/** (Rack, MacroPanel, Bodies, Instruments, Midi), Controls/Eq8Views.cs, Controls/DynamicEqCurve.cs
tests/Nota.SmokeTest/
├── DesignTokenCheck.Menus.cs            # NEW: RunMenus guard
└── RecentInstrumentsTests.cs            # NEW
DESIGN.md, scripts/design-html.py (→ DESIGN.html), CHANGELOG.md, FEATURES.md
```

**Structure Decision**: presentation lives in `Nota.App` (where every menu is built
today); the recent-instruments rule is plain logic in `Nota.Application` so both the UI
(`Nota.Presentation`) and the MCP server can record into it and the smoke test can unit
test it.

### Menu inventory (FR-014)

Each row is checked off in tasks.md against FR-001 – FR-013. "H" = gets a header row (FR-007).

| # | Site | Menu | H |
|---|---|---|---|
| 1 | `ArrangementView.cs` `ShowTrackMenu` | Track header | H |
| 2 | `ArrangementView.Tracks.cs` `ShowTracksMenu` | Multi-track selection | H |
| 3 | `ArrangementView.cs` `ShowFooterMenu` | Return / master footer | H |
| 4 | `ArrangementView.cs` `ShowEmptyAreaMenu` | Empty area below tracks | – |
| 5 | `ArrangementView.LaneControl.cs` `ShowClipMenu` | Clip (and multi-clip) | H |
| 6 | `ArrangementView.LaneControl.cs` `ShowLaneMenu` | Lane background / time selection | – |
| 7 | `ArrangementView.LaneControl.cs` `ShowAutoMenu` | Automation lane | H (parameter) |
| 8 | `ArrangementView.Automation.cs` `ShowAutoTargetMenu` | Automation target picker | – |
| 9 | `ArrangementView.Sections.cs` `ShowMenu` | Section marker | H |
| 10 | `ArrangementView.Controls.cs` `ShowMenu` | Arrangement header controls | – |
| 11 | `SessionView.Grid.cs` `ShowSlotMenu` | Session slot | H (with clip) |
| 12 | `SessionView.Grid.cs` `ShowHeaderMenu` | Session track header | H |
| 13 | `SessionView.Grid.cs` `ShowSceneMenu` | Scene | H |
| 14 | `SessionView.Grid.cs` `ShowAddTrackMenu` | Session add track | – |
| 15 | `SessionView.Toolbar.cs` (2) | Toolbar overflow / options | – |
| 16 | `SessionView.Inspector.cs` `Pick<T>` | Inspector pickers | – |
| 17 | `ModularView.cs` `ShowModulateMenu` | Knob modulate | H (parameter) |
| 18 | `ModularView.cs` `ShowConnectParamMenu`, `ShowCrossTrackParamMenu` | Modulator targets | – |
| 19 | `ModularView.cs` `Toolbar` | Modular toolbar | – |
| 20 | `DeviceChainView.Clipboard.cs` `ShowDeviceContextMenu` (+ `.Shell.cs` `HeaderMenu`, `.Fold.cs` `Menu`) | Device card / folded strip | H |
| 21 | `DeviceCards/Rack/RackCardView.cs` (5) | Rack add device / add chain / macro / map | H (chain, macro) |
| 22 | `DeviceCards/Rack/RackCardView.Drum.cs` (2) | Drum pad / add pad | H (pad) |
| 23 | `DeviceCards/Rack/MacroPanel.cs` (4) + `FillMapMenu` in Rhythm / Drum | Macro / map | H (macro) |
| 24 | `DeviceCards/Instruments/*` (Rhythm 3, Consort 3, Mosaic, Pendulum, Flux) | In-card menus | per menu |
| 25 | `DeviceCards/Bodies/*` (Ceiling, Vocoder, AutoShift 2, Chamber, DynamicEq, BeatRepeat, Valve, AutoGain, Prism, Shutter, Vintage), `Midi/ScaleMidiBody` | In-card option menus | – |
| 26 | `Controls/Eq8Views.cs`, `Controls/DynamicEqCurve.cs` `ShowBandMenu` | EQ band | H (band) |
| 27 | `DeviceCardKit.cs` `PresetPicker` | Preset list | – |
| 28 | `ClipEditorKit.cs` `ShowMenu` (+ ClipEditorView, AudioClipEditorView, PianoRollView.Expression callers) | Clip editor pickers | – |
| 29 | `BrowserView.cs` `BuildContextMenu`, `ShowOptionsMenu`, `ShowOverflowChips` | Browser item / options / chips | H (item) |
| 30 | `HistoryView.cs` `BuildMenu`, `BuildOptionsMenu` | History version / options | H (version) |
| 31 | `PreviewPlayer.cs` `ShowSourceMenu` | Preview source | – |
| 32 | `SnapChip.cs` `OpenMenu` | Snap grid | – |
| 33 | `MainWindow.Freeze.cs`, `.LiveFreeze.cs` (button menus), `BuildTrackFreezeMenu` | Freeze button / track freeze group | – |
| 34 | `MainWindow.Updates.cs` `UpdateMenu` | Update chip | – |
| 35 | `MainWindow.axaml.cs` `OnMetronomeContext`, `MainWindow.axaml` | Metronome / transport | – |

The tasks phase re-greps (`new MenuFlyout|new ContextMenu|<ContextMenu|<MenuFlyout|FillMapMenu`) so a site missed here is still caught; the `RunMenus` guard fails on any menu item built outside `MenuKit` conventions.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Large PR (~45 files) against principle V | The user asked for every context menu to change together; the shared theme changes all menus at once anyway, so a split leaves menus half-migrated (new frame, old order/labels) between PRs | Splitting per view would ship an inconsistent app between merges; instead the PR is a sequence of self-contained commits (foundation first, then one view per commit) that review one at a time |
