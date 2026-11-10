# Implementation Plan: User skins (colours, typeface, corner radii) with MCP support

**Branch**: `feat/user-skins` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/087-user-skins/spec.md` (issue nota-daw/nota#336)

## Summary

Users drop theme files (spec 011's format, extended to version 2 with an optional system
typeface and corner-radius settings) into `<data>/Themes/`. Nota validates them with one
shared validator, lists them in Preferences → Appearance next to the built-ins, wears them
live (colours through the existing palette path; radii and typeface through property
coercion, the way text size already works), and reports what failed. The in-app MCP server
gets ten theme tools — list, read, catalogue, fonts, validate, preview, save, apply, try-on,
revert — where `preview_theme` renders the real views over a private sample-scene engine
into PNGs without touching the live window.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (Appearance page
  additions, warning badge, problems list).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- Prior specs: [011 colour themes](../011-colour-themes/spec.md) (format, T3 pairings),
  [038 style guide](../038-style-guide/spec.md) (radius scale, Geist), [084 accent
  colour](../084-accent-colour/spec.md) (accent worn over any theme), 077 key-based
  localization (EN + RU strings).

## Technical Context

**Language/Version**: C# / .NET 10 (managed only; no C++ change).

**Primary Dependencies**: Avalonia (existing), ModelContextProtocol 2.1.0 (existing;
`ImageContentBlock.FromBytes`). No new dependency.

**Storage**: `NotaPaths.DataDir/Themes/*.json` (honours `NOTA_DATA_DIR`); the theme choice in
the existing settings fields (`ColourTheme`, `SystemDarkTheme`, `SystemLightTheme`).

**Audio-Thread Impact**: None. Theme loading, validation, wearing and preview rendering are
UI-thread work. The preview engine is a second `NotaEngine` that is never started (no audio
thread, no device).

**Testing**: `tests/Nota.SmokeTest` — new `Skins` checks for the validator, geometry mapping
and the folder loader (no UI); the existing `DesignTokenCheck.RunThemes` stays. UI and MCP by
the headless/in-app recipes in [quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux (the app's platforms). System font enumeration is
Avalonia's, per platform.

**Project Type**: desktop app.

**Performance Goals**: theme switch (including radius/font re-coerce) ≤ 250 ms on a typical
project; full `preview_theme` (10 surfaces) < 10 s (SC-003), scene built once per run.

**Constraints**: live window must not show preview colours (R6); preview images
deterministic (SC-004); MCP writes only `<themes>/<validated-id>.json`.

**Scale/Scope**: ~100 colour tokens; a handful of user files; 10 preview surfaces.

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | ✅ #336 carries `approved`. Scope: skins + the screenshot API the issue describes; typeface/radius added at the user's request within "skins". |
| I Real-time audio safety | ✅ No audio-thread code. Preview engine never started. |
| II Boundary discipline | ✅ No C ABI change; uses existing engine API from managed code. |
| III Licensing | ✅ No new dependency; new files get the SPDX header. System fonts are used, not bundled. |
| IV Verification gate | ✅ Smoke test extended (validator, geometry, loader); `scripts/build.sh` before PR. |
| V One logical change | ⚠️ Large but one feature. Delivered as one PR with commits per phase; if review asks, MCP tools (Phase 5) can split into a follow-up PR without breaking Phases 1–4. |
| VI Docs in sync | ✅ Planned: CHANGELOG, FEATURES.md (`nota-features`), user manual EN+RU (`nota-docs`: Appearance page + new "Making a skin" page), DESIGN.md note on radius/typeface skinning. No shortcut change. |
| VII Spec-first | ✅ This spec/plan/tasks. |
| VIII Rules not weakened | ✅ |

**Post-design re-check**: unchanged — the design adds no native code, no dependency, no
audio-thread path.

## Design

### Phase 1 — Format and validation (`Nota.Application/Themes/`)

- `ThemeFile.Validate(json, catalogue, translucent, fontInstalled?) → ThemeValidation`
  (all problems, T3 warnings, v2 `font`/`radius`). `SkinGeometry`, `RadiusStep`.
  Pure, no Avalonia. ([data-model](data-model.md), [theme-file-v2](contracts/theme-file-v2.md))
- `ThemeFile.Write(parsed) → string` — canonical writer (catalogue order, 2-space indent).
- `NotaThemes.Parse` becomes a wrapper; built-in loading unchanged in behaviour.

### Phase 2 — User themes in the registry and Preferences

- `NotaThemes`: `User`, `All`, `UserLoadProblems`, `ThemesFolder`, `ReloadUser()`,
  `ThemesChanged`; `Find`/`OfKind`/`KnownThemes` over `All`; user themes load before
  `Appearance.Resolve` in startup.
- `NotaAccent`: synthetic ids for try-on/preview; cache cleared on reload (R5 gotcha).
- `NotaThemeService`: re-apply the worn theme after a reload if it changed or disappeared.
- `PreferencesWindow.Appearance`: U1–U5 ([ui-contract](contracts/ui-contract.md)).

### Phase 3 — Live radii and typeface

- `NotaSkin` (new, `Nota.App/Theme/`): the worn `SkinGeometry`, the resolved
  `FontFamily?`, `MapRadius(double)`, `MapFamily(FontFamily)`, `Install()` (metadata
  overrides with coerce callbacks — R2, R3), `Reapply()` (re-coerce every top level).
- `NotaRadius.*Value` → properties through `NotaSkin`. `NotaFormattedText` swaps the Geist
  typeface. `NotaThemeService.ApplyActive` sets `NotaSkin` from the worn theme and calls
  `Reapply()` when geometry changed.
- Smoke/geometry check (spec 012 `RunGeometry`) adapted: `*Value` are no longer `const`.

### Phase 4 — Try-on and preview rendering (`Nota.App`)

- `NotaThemeService.TryOn/RevertTryOn`, `RenderScoped(theme, action)`.
- `Skins/SampleScene.cs` — private engine + fixed content. `Skins/SkinPreviewRenderer.cs` —
  surfaces ([preview-surfaces](contracts/preview-surfaces.md)), offscreen host,
  `RenderTargetBitmap` → PNG bytes.
- **Spike first** (R6): offscreen host in the live app on macOS; no visible flash.

### Phase 5 — MCP

- `IThemeAccess` in `Nota.Mcp/Abstractions.cs`; `Nota.App/Mcp/ThemeAccess.cs` implements
  it; registered in `McpAppServices.ShareServices`.
- `Nota.Mcp/Tools/ThemeTools.cs` — the ten tools ([mcp-tools](contracts/mcp-tools.md)).

### Phase 6 — Docs and polish

CHANGELOG `[Unreleased]` Added; FEATURES.md; user docs EN+RU (Appearance + "Making a
skin", including the MCP loop); DESIGN.md: radius/typeface are skinnable, the design scale
is the base; localization EN+RU; PR evidence (Appearance before/after, one preview set).

## Project Structure

### Documentation (this feature)

```text
specs/087-user-skins/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── theme-file-v2.md
│   ├── mcp-tools.md
│   ├── preview-surfaces.md
│   └── ui-contract.md
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Application/
│   ├── Themes/ThemeFile.cs            # NEW validator + writer (v1/v2), T3 warnings
│   ├── Themes/SkinGeometry.cs         # NEW
│   ├── Appearance.cs                  # unchanged API; user ids pass through `known`
│   └── Localization/en.json (+ ru)    # new strings
├── Nota.App/
│   ├── Theme/NotaColourTheme.cs       # Parse → wrapper; User/All/Reload; Geometry, Source
│   ├── Theme/NotaThemeService.cs      # reload, try-on, RenderScoped, geometry apply
│   ├── Theme/NotaAccent.cs            # synthetic ids, cache invalidation
│   ├── Theme/NotaSkin.cs              # NEW coercion for radii + typeface
│   ├── Theme/NotaGeometry.cs          # *Value → skinned properties
│   ├── Theme/NotaFonts.cs             # NotaFormattedText family swap
│   ├── Skins/SampleScene.cs           # NEW
│   ├── Skins/SkinPreviewRenderer.cs   # NEW
│   ├── Mcp/ThemeAccess.cs             # NEW IThemeAccess impl
│   ├── Mcp/McpAppServices.cs          # register IThemeAccess
│   ├── App.axaml.cs                   # load user themes before Resolve; NotaSkin.Install
│   └── PreferencesWindow.cs (+ .Appearance partial)  # U1–U5
├── Nota.Mcp/
│   ├── Abstractions.cs                # IThemeAccess
│   └── Tools/ThemeTools.cs            # NEW ten tools
tests/Nota.SmokeTest/
└── SkinTests.cs                       # NEW validator / geometry / loader checks
```

**Structure Decision**: Pure rules in `Nota.Application` (shared by app, MCP and tests);
everything Avalonia in `Nota.App`; MCP tools in `Nota.Mcp` behind an app facade — the
existing layering.

## Risks

| Risk | Mitigation |
|---|---|
| Offscreen rendering in the live app shows a flash or doesn't apply styles | Spike task first; fallback is a hidden real `Window` positioned offscreen. |
| Coercion catches a non-radius value equal to a step | Accepted per style guide; `RadiusAllowList` ratchet keeps literals visible. |
| Some views cache brushes/typefaces/radii in fields beyond the choke points | Audit task: grep `static readonly` `CornerRadius`/`Typeface`/`IBrush` in views; preview `roundness 0` + odd font makes misses visible. |
| A system font with very different metrics breaks fixed device-card layouts | Spec accepts truncation; device cards keep fixed size. Warning text in docs. |
| Views need services from DI bound to the live engine | Build surfaces only from constructors that take the engine; `overview` composed, not a second `MainWindow`. |

## Complexity Tracking

Not needed: no constitution violations. (Principle V noted above, with a split point.)
