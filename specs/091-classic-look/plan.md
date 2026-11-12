# Implementation Plan: Classic look

**Branch**: `feat/classic-look` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/091-classic-look/spec.md`
(issue [nota-daw/nota#572](https://github.com/nota-daw/nota/issues/572))

## Summary

A **Look: New | Classic** switch in Preferences → Appearance brings back the 0.59.2
colours, corners and Geist + Geist Mono pairing, live and stored, over any theme; the new
look stays the default and renders unchanged. Approach (research): the look is a
user-wide setting like the accent; the theme service composes theme × look × skin at the
one point every theme change already goes through. Classic colours are four embedded
palette files (only 5 old values differ from today, plus 15 new tokens); classic corners
are a `SkinGeometry` over today's steps; the values-and-readouts role comes back as a
dynamic `Font.Measure` resource that is Geist under New (no visual change) and Geist Mono
under Classic, placed exactly where 0.59.2 used mono. Theme files gain format 3
(`font.values`), MCP gains `set_look` and a `look` argument on previews.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill; DESIGN.md
  gains a "Classic look" section (tokens, corner table, the two type roles).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): Geist Mono returns under the
  existing OFL row (it shipped until #296).
- Related specs: [011](../011-colour-themes/) themes and token catalogue,
  [038](../038-style-guide/) the new look, [084] accent, [087](../087-user-skins/) skins,
  format 2, MCP theme tools.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only); no C++ change

**Primary Dependencies**: Avalonia (existing); Geist Mono 1.x font files (SIL OFL 1.1,
previously bundled — license row and `LICENSES/OFL-1.1-Geist.txt` cover it; row text updated)

**Storage**: `settings.json` gains `Look`; theme files in `<data>/Themes/` gain format 3;
four embedded classic palette JSONs

**Audio-Thread Impact**: None — UI-only. The switch runs on the UI thread through the
existing theme-switch path; the engine is not called.

**Testing**: `tests/Nota.SmokeTest` (pure resolve/compose logic, palette and format
checks, design token/type checks); MCP preview images for visual checks; manual
side-by-side with 0.59.2 (quickstart)

**Target Platform**: macOS, Windows, Linux desktop (Avalonia)

**Project Type**: desktop app

**Performance Goals**: look switch shows in < 1 s (same cost as a theme switch); zero
render cost difference under New

**Constraints**: no layout size changes (FR-005); New previews pixel-identical before/after
(SC-004); no xrun on switch (SC-007)

**Scale/Scope**: 4 palettes × 15 new tokens; ~180 role call sites in 132 files at 0.59.2
(fewer survive on develop); 1 Preferences row; 1 new MCP tool, 3 changed

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #572, `approved`; scope = colours, corners, type; layouts out |
| I. Real-time audio safety | ✅ N/A — no audio-thread code touched |
| II. Boundary discipline | ✅ No ABI change, no JUCE |
| III. Licensing hygiene | ✅ Geist Mono is OFL 1.1, AGPL-compatible, previously listed; the row in `LICENSES/third-party.md` is updated (no new dependency); new files carry SPDX headers |
| IV. Verification gate | ✅ Smoke test extended (Looks, palettes, format 3, type check) |
| V. One logical change | ✅ One feature; may split into 2 PRs (P1 switch+colours+corners+type, then P2/P3 skins+MCP) if review size demands |
| VI. Docs in sync | ✅ CHANGELOG, FEATURES (nota-features), user manual EN+RU (nota-docs), DESIGN.md |
| VII. Spec-first | ✅ This spec/plan/tasks |
| VIII. No weakening of I–III | ✅ |

Design-check change (R9): the "one typeface" guard becomes "two roles, New is one
typeface" — it keeps guarding New exactly (FR-017), not a loosening of I–III.

**Post-design re-check**: unchanged, all pass.

## Project Structure

### Documentation (this feature)

```text
specs/091-classic-look/
├── plan.md              # this file
├── research.md          # R1–R11
├── data-model.md        # Look, palettes, geometry, composition, text roles
├── quickstart.md        # validation guide
├── contracts/
│   ├── ui-contract.md   # Preferences → Appearance, Look row
│   ├── theme-file-v3.md # font.values
│   └── mcp-tools.md     # set_look, look arguments
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
assets/fonts/                          # + GeistMono-{Regular,Medium,SemiBold,Bold}.ttf (from v0.59.2)
LICENSES/third-party.md                # Geist row: Geist Mono back in use
src/managed/
├── Nota.Application/
│   ├── Looks.cs                       # NEW: Look enum, Resolve, ClassicGeometry, Compose
│   ├── Settings.cs                    # + Look
│   └── Themes/
│       ├── SkinGeometry.cs            # + ValuesFamily, HasRadius
│       └── ThemeFile.cs               # format 3: font.values (read, validate, write)
├── Nota.App/
│   ├── App.axaml.cs                   # resolve Look at startup, before the first window
│   ├── Nota.App.csproj                # embed Theme/Themes/Classic/*.json, Geist Mono
│   ├── PreferencesWindow*.cs          # Look row (Appearance), duplicate under Classic
│   ├── Theme/
│   │   ├── NotaTheme.axaml            # Font.Measure; role classes use it; TextBlock.Number
│   │   ├── NotaFonts.cs               # MeasureMonoUri, Measure* properties, SetMeasure
│   │   ├── NotaThemeService.cs        # SetLook, compose in ApplyActive, RenderScoped(look)
│   │   ├── NotaThemes (loader)        # classic palette lookup
│   │   ├── NotaSkin.cs                # wear composed geometry
│   │   └── Themes/Classic/*.json      # NEW: four classic palettes
│   └── … ~130 view files              # NotaFonts.Measure* / Font.Measure at 0.59.2 sites
├── Nota.Mcp/
│   ├── Abstractions.cs                # IThemeAccess: look on list/get/preview, SetLook
│   └── Tools/ThemeTools.cs            # set_look; look args
tests/Nota.SmokeTest/
├── DesignTokenCheck.cs                # RunType: two roles
├── DesignTokenCheck.Themes.cs         # classic palettes vs snapshot, readability rule
├── DesignTokenCheck.Skins.cs          # format 3 cases
├── LooksCheck.cs                      # NEW: Resolve, Compose
└── Fixtures/classic-0.59.2.json       # NEW: committed snapshot of the 0.59.2 palettes
scripts/classic-role-sites.sh          # NEW (dev aid): lists the #296 Mono→Sans hunks still on develop
DESIGN.md, CHANGELOG.md, FEATURES.md   # docs
```

**Structure Decision**: Managed only. Pure logic (`Looks`, format 3) in
`Nota.Application` so the smoke test covers it without UI; wearing in `Nota.App/Theme`;
MCP surface in `Nota.Mcp`. No new project.

## Phasing

1. **Foundation** — `Looks`, `Settings.Look`, format-3 geometry fields, smoke checks.
2. **US1 (P1, MVP)** — classic palettes, classic geometry, `Font.Measure` + Geist Mono,
   role restored at the 0.59.2 sites, Preferences Look row, startup, design checks.
3. **US2 (P2)** — skin composition rules, `font.values` in format 3, duplicate under
   Classic, skins editor display.
4. **US3 (P3)** — MCP `set_look`, `look` on `get_theme` / `preview_theme`, `list_themes`.
5. **US4 (P2, widened scope)** — Clips: Solid | Tinted, default from the look (research R12).
6. **Polish** — clipping pass (FR-016), DESIGN.md, CHANGELOG, FEATURES, user docs, RU.

## Complexity Tracking

No constitution violations.
