# Quickstart: validating the Classic look

How to prove [spec.md](spec.md) end to end. Contracts:
[UI](contracts/ui-contract.md), [theme file v3](contracts/theme-file-v3.md),
[MCP](contracts/mcp-tools.md).

## Prerequisites

- macOS build: `scripts/build.sh` green (builds native + managed, runs the smoke test).
- A `v0.59.2` build or its screenshots for side-by-side comparison (the release DMG, or
  `git worktree add /tmp/nota-0592 v0.59.2` and build there).
- A scratch data folder: `export NOTA_DATA_DIR=/tmp/nota-classic` (keeps your settings).

## 1. Smoke test (automated)

`scripts/build.sh` must report, among the existing checks:

- `Looks.Resolve`: `""`, `"new"`, `"classic"`, garbage → New/New/Classic/New.
- `Looks.Compose`: the table in [data-model.md](data-model.md) § Composition, each row.
- Classic palettes: four files, full catalogue, 107 tokens equal the committed 0.59.2
  snapshot, readability warnings only on 0.59.2-only pairings.
- Theme file v3: valid/invalid cases for `font.values`; v1/v2 files unchanged.
- Type check: `Font.UI` + `Font.Measure` (default Geist), four Geist + four Geist Mono
  weights, OFL licence present, no family named by string in views.

## 2. Switch live (US1)

1. Start Nota with an empty `NOTA_DATA_DIR`. Preferences → Appearance shows **Look: New**.
2. Pick **Classic** during playback of a project with audio and a synth. Expect: every
   window changes at once; no dropout or xrun in the log; transport keeps running.
3. Compare against 0.59.2 on Studio Dark, then each other theme: main window, mixer, EQ-8
   card, piano roll, Preferences. Colours match; corners tight; tempo, position, dB, Hz
   in Geist Mono; names in Geist.
4. Quit and start again: classic from the splash on.
5. Pick **New**: today's look returns, identical to a build without this feature (compare
   MCP previews before/after, SC-004).

## 3. Clipping (FR-016)

Under Classic, at 100 % and the largest UI scale, render the full preview set over MCP
(`preview_theme` with `look: "classic"`) for Studio Dark and Studio Light, and walk every
image for clipped, overlapping or wrapped values. Zero defects.

## 4. Skins (US2)

Put three files in `$NOTA_DATA_DIR/Themes/`:

- colours only (format 1) → under Classic: own colours, classic corners and type;
- `font.family` + `radius.scale: 0` (format 2) → under Classic: skin typeface and square
  corners; values in Geist Mono;
- `font.values: "Menlo"` (format 3) → values in Menlo under both looks; with a missing
  family, the warning and fallback per contract L3.

Duplicate Studio Dark under Classic → the copy has the 0.59.2 colours.

## 5. MCP (US3)

`list_themes` shows `choice.look`; `set_look classic` switches and Preferences follows;
`get_theme studio-dark look=classic` returns the classic palette; `preview_theme` in both
looks differs only in colours, corners and typefaces, and the live window does not change.

## 6. Docs

CHANGELOG `[Unreleased]` entry; FEATURES.md and the user manual (EN + RU) via
`nota-features` / `nota-docs`; DESIGN.md § Classic look.
