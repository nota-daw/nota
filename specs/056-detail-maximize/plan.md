# Implementation Plan: Clip editor fills the window; double-click a panel edge to reset it

**Branch**: `cursor/detail-maximize-splitter-reset-bc46` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/056-detail-maximize/spec.md`

## Summary

Add a "fill the window" state to the docked Detail panel (#304) and a double-click reset on
the panel edges (#305). Technically: one `_detailMaximized` flag in `MainWindow.Detail.cs`
switches the `BodyGrid` rows (main row collapsed to 0 with its minimum lifted, Detail row
star, docked-Mixer rows left out by `LayoutBottomPanels`) and hides `BrowserGrid`; leaving it
restores the row from `_detailWanted` and re-lays the bottom panels through the existing
specs/054 path. Double-clicks are caught in the tunnel phase on each splitter / edge and reset
the remembered size. UI-only; no engine or ABI change.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill: the
  button is a ghost icon button in the existing Detail header row, before pop-out.
- **Shortcut** → `nota-shortcuts` skill: catalog entry, menu binding, Settings → Shortcuts
  row, docs shortcuts page.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: none — the filled state is not saved; the reset clears the existing per-user
Mixer dock height (`MixerDockHeight`) and the in-memory Clip-tab height.

**Audio-Thread Impact**: None. Every change runs on the UI thread (grid rows, visibility,
pointer handlers, a menu/catalog command). No engine source, C ABI, graph snapshot, JUCE
or Transport code is touched; nothing new allocates, locks, logs or does I/O on the audio
thread.

**Testing**: `scripts/build-linux.sh` (smoke test, including the palette check that every
catalog shortcut is bound and listed); the spec's scenarios driven headlessly against the
real `MainWindow` with the nota-docs shots harness, with before/after screenshots.

**Target Platform**: macOS, Windows, Linux desktop (built and tested here on Linux; CI
builds all three).

**Project Type**: desktop app

**Performance Goals**: toggling only changes row definitions and visibility — no view is
rebuilt or re-parented.

**Constraints**: main view keeps its 120 px minimum whenever it is shown; Devices (fixed
height) cannot fill the window; F11 full screen is independent.

**Scale/Scope**: ~9 files in `src/managed/Nota.App`, 1 in `Nota.Application`.

## Design

| Concern | Where | Approach |
|---|---|---|
| Fill / back | `MainWindow.Detail.cs` `SetDetailMaximized` | Rows + `BrowserGrid` + splitter visibility; glyph, tooltip and menu tick follow |
| Availability | `CanMaximizeDetail`, `MainWindow.Commands.cs` | Docked, visible, Clip / audio / Pattern content; otherwise status-line reason |
| Leaving it | `ShowDetail`, panel hide, `SetView`, Toggle browser, `DockMixer` | Each calls `SetDetailMaximized(false)` (another clip keeps it) |
| Layout | `LayoutBottomPanels`, `FitBottomPanels` | Mixer rows omitted while filled; fitting skipped while filled |
| Double-click | `OnDoubleClick` (tunnel, `ClickCount == 2`, handled) | Detail splitter + `DetailEdge` → tab default; Mixer splitter → first-dock height; browser splitter → 344 |
| Command | `CommandCatalog` `view.maximizeDetail`, ⌘⇧F | View menu `CheckBox` item via `app:Commands.Id` |
| Icons | `Glyph` | `Expand` / `Shrink` path kinds |

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code touched |
| II. Boundary discipline | Pass — no C ABI / JUCE / Transport change |
| III. Licensing hygiene | Pass — no new dependency, no new source file |
| IV. Verification gate | Pass — `scripts/build-linux.sh` before PR; no engine behaviour change, so no smoke-test extension |
| V. One logical change | Pass — #304 and #305 are one change to the Detail separator (its size states), bundled at the maintainer's request |
| VI. Docs in sync | Done — CHANGELOG `[Unreleased]`, FEATURES.md, Settings → Shortcuts, nota-docs detail-panel + shortcuts pages EN + RU (nota-docs#51) |
| VII. Spec-first | Pass — spec, plan, tasks, converge |
| VIII. Rules not weakened | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/056-detail-maximize/
├── spec.md
├── plan.md
└── tasks.md
```

### Source Code

```text
src/managed/
├── Nota.App/
│   ├── MainWindow.axaml            # header button, View menu item
│   ├── MainWindow.Detail.cs        # filled state, double-click reset
│   ├── MainWindow.Mixer.cs         # DockMixer leaves it; Mixer edge reset
│   ├── MainWindow.Browser.cs       # browser default width, reset
│   ├── MainWindow.axaml.cs         # SetView leaves it
│   ├── MainWindow.Menu.cs          # Toggle browser leaves it
│   ├── MainWindow.MenuKeys.cs      # tooltip key spelling
│   ├── MainWindow.Commands.cs      # command binding
│   ├── PreferencesWindow.cs        # Settings → Shortcuts rows
│   └── Controls/Glyph.cs           # Expand / Shrink
└── Nota.Application/Palette/CommandCatalog.cs
```

**Structure Decision**: existing managed app only; no new project, file or folder outside
this spec.
