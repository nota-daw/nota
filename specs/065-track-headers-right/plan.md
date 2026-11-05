# Implementation Plan: Track headers on the right

**Branch**: `065-track-headers-right` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/065-track-headers-right/spec.md`

## Summary

A per-user setting moves the arrangement's header column to the right edge (issue #72).
`ArrangementView` already builds every horizontal band as a two-column grid — header part
in an `Auto` column, lane part in a `*` column (chrome strips, the scrolling tracks, the
pinned footer, the scrollbar row). A `HeadersOnRight` property swaps those columns, flips
the header/lane dividers to the lane-facing edge, re-anchors the column backdrop and the
column-resize seam, and rebuilds the cards with mirrored borders. All hit-testing is
already relative to the header canvases and the lane controls, so gestures carry over
without coordinate changes; only the seam drag reverses its sign.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing) — no new dependency

**Storage**: user `settings.json` (`Settings.ArrangementHeadersRight`); project bundles unchanged

**Audio-Thread Impact**: None: UI-only change.

**Testing**: smoke test (`tests/Nota.SmokeTest`, palette/menu consistency checks) + manual pass in the app

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: desktop app

**Performance Goals**: switching sides rebuilds the header cards once (same cost as a header-width change)

**Constraints**: default Left must be pixel-identical to today

**Scale/Scope**: `ArrangementView` (+ `.Sizing`, `.Overview`, `.Sections`), `Settings`, `MainWindow` (menu/commands), `PreferencesWindow`, `CommandCatalog`

## Constitution Check

- I Real-time safety: no engine/audio-thread code touched. Pass.
- II Boundary discipline: no C ABI change. Pass.
- III Licensing: new files carry the AGPL header; no dependencies. Pass.
- IV Verification: `scripts/build-linux.sh` (build + smoke test) before the PR. Pass.
- V One change: the side setting only. Pass.
- VI Docs: CHANGELOG `[Unreleased]` and FEATURES.md here; no shortcut changes (nota-shortcuts n/a); the user manual lives in `nota-daw/nota-docs` and gets its own follow-up PR. Pass.
- VII Spec-first: this spec. Pass.

## Project Structure

### Documentation (this feature)

```text
specs/065-track-headers-right/
├── spec.md
├── plan.md
├── tasks.md
└── checklists/requirements.md
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/Settings.cs                 # ArrangementHeadersRight (per user)
├── Nota.Application/Palette/CommandCatalog.cs   # view.headersRight
└── Nota.App/
    ├── ArrangementView.cs           # HeadersOnRight, side-aware rows, dividers, card borders
    ├── ArrangementView.Sizing.cs    # seam anchoring + drag sign
    ├── ArrangementView.Overview.cs  # register the chrome row
    ├── ArrangementView.Sections.cs  # register the chrome row
    ├── MainWindow.axaml             # View ▸ Track headers on the right (checkbox)
    ├── MainWindow.Commands.cs       # bind + toggle
    ├── MainWindow.axaml.cs          # apply on start and on Settings.Changed
    └── PreferencesWindow.cs         # Appearance → Track headers: Left / Right
```

**Structure Decision**: extend the existing arrangement partials; no new types beyond a
small row record.

## Design notes

- Each two-column band registers `(grid, header, lane parts)`; `ApplyHeaderSide` sets
  `ColumnDefinitions` to `Auto,*` or `*,Auto` and the parts' `Grid.Column`.
- Dividers: every header-column chrome border and each card border goes through one
  helper so the 1 px line is on the lane-facing side.
- Seam: `HorizontalAlignment.Left` + left margin, or `Right` + right margin; on the right,
  width = start − Δx.
- The side is view preference, applied by `MainWindow` from `Settings` at start and on
  `Settings.Changed`, the same path as the waveform style; the View menu check is synced there.
- The track scroller's overlay scrollbar stays on the view's right edge (over the
  header meters when on the right), as it auto-hides and does not take input from cards.
