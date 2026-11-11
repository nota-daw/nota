# Research: User skins (087)

Findings from the code as of `develop` @ 4508a93d, and the decisions they lead to.

## R1 — Where the theme file is parsed and validated

**Today**: `NotaThemes.Parse` (`Nota.App/Theme/NotaColourTheme.cs`) checks F1–F6 and stops at
the first problem. The T3 readability pairings exist only in the smoke test
(`tests/Nota.SmokeTest/DesignTokenCheck.Themes.cs`). `Nota.Mcp` references
`Nota.Application` but not `Nota.App` (Avalonia).

**Decision**: A new pure validator `ThemeFile` in `Nota.Application/Themes/` — no Avalonia
types, colours as `uint` ARGB. It takes the token catalogue and the translucent-token set as
inputs (they stay defined by `NotaPalette` in the app), collects **every** problem, computes
the T3 pairings as warnings, and returns a `ThemeValidation` (data-model.md). `NotaThemes.Parse`
becomes a thin wrapper that converts a valid result into `NotaColourTheme`.

**Rationale**: One validation for the loader and MCP (FR-011); testable from the smoke test
without a UI; the MCP project needs no reference to `Nota.App`.

**Alternatives**: Keep `Parse` in the app and expose it over an interface only — rejected, the
smoke test then can't exercise the rules directly. Move `NotaPalette.TokenNames` to
`Nota.Application` — rejected for now, it is tied to the slot brushes; passing the catalogue in
is enough.

The smoke test's own T3 copy stays: it is an independent check of the built-ins. A new test
checks the two agree on the built-ins.

## R2 — Live corner radii without touching ~430 call sites

**Today**: radii are XAML `StaticResource Radius.*` (39 uses) and C# `NotaRadius.*`
constants (~430 uses in 132 files; ~113 `DrawRoundedRect`-style custom draws, mostly through
`NotaRadius.*Value`). Nothing can change at runtime.

**Decision**: Coercion, the way `NotaTextSize` already steps font sizes live:

- Override the metadata of `Border.CornerRadiusProperty`, `TemplatedControl.CornerRadiusProperty`
  and `ContentPresenter.CornerRadiusProperty` with a coerce callback that maps each corner
  through `NotaRadiusSkin.Map(double)`: a value equal to a scale step's design value → that
  step's skinned value; `0` stays `0`; any other value → value × roundness, rounded to whole
  pixels. The **base** value stays the Studio design value, so mapping is always from the
  design scale and never compounds, and switching back restores it exactly.
- `NotaRadius.*Value` constants become `static double` properties that return the skinned
  value (custom-drawn views read them at render time; the theme service already repaints
  every window on a change). `NotaRadius.Card` etc. stay design values — coercion maps them.
- On a geometry change, walk every open top level and `CoerceValue` the three properties
  (the same pattern as `NotaTextSize`'s live re-coerce), then repaint.

Design values are distinct (9/7/6/5/4/3/2), so the step of a base value is unambiguous.
Values that are not on the scale (pill radii = height/2, the allow-listed literals in
`RadiusAllowList.txt`) scale with roundness, so roundness 0 squares everything (spec edge
case).

**Alternatives**: Replace every assignment with a `DynamicResource` binding — rejected
(~430 edits, easy to miss, no help for the custom-drawn layer). Rebuild windows on change —
rejected (loses state, FR-025 says live).

**Risk**: a `CornerRadius` that is not a radius-scale value but happens to equal one (e.g. a
literal `6`) is treated as that step. That is the intended reading of the style guide; the
`RadiusAllowList` ratchet keeps literals visible.

## R3 — Live typeface

**Today**: Geist is the app default via `FontManagerOptions.DefaultFamilyName` (fixed at
startup), XAML `StaticResource Font.UI` (14 uses), and `NotaFonts.UiFamily` / `Sans*`
typefaces (~230 uses, some cached in `static readonly` fields). **All** custom-drawn text is
built through `NotaFormattedText` (125 call sites, 0 raw `FormattedText`).

**Decision**:

- Controls: override `TextElement.FontFamilyProperty` metadata on `TextBlock`, `TextPresenter`,
  `TemplatedControl` and `TopLevel` with a coerce callback: a family that is Geist (the
  `Font.UI` URI or the default family) → the worn skin's family when one is set; anything else
  is left alone (explicit non-UI fonts keep working). Re-coerce on change as in R2.
- Custom-drawn: `NotaFormattedText`'s constructors swap a Geist typeface for the skin family
  with the same weight and style. One choke point covers every `static readonly Typeface`
  cache, because the swap happens when the text is built.
- Tabular figures stay requested (`+tnum`); a font without them just ignores the feature.
- Family resolution: `FontManager.Current.SystemFonts` names. Not found → Geist and a
  warning (FR-022). Checked at load and on wear (a font may be uninstalled meanwhile).

**Alternatives**: Restart to change the default family — rejected (FR-025). Change every
`NotaFonts.Sans` field to a property — unnecessary given the `NotaFormattedText` choke point.

## R4 — Format version 2

**Decision**: `formatVersion: 2` = v1 + optional `font` and `radius` objects (contracts/
theme-file-v2.md). v1 stays valid, and the four built-ins stay v1. `font`/`radius` in a v1
file are an error ("needs formatVersion 2"). Unknown top-level keys stay ignored, as today.
F8 is not run for user themes (it is a built-in Studio rule).

## R5 — Where user themes live and how they join the list

**Decision**: `NotaPaths.DataDir/Themes/*.json` (honours `NOTA_DATA_DIR`). `NotaThemes`
gains `User`, `All` (built-ins then user themes by name), `UserLoadProblems`, and
`ReloadUser()`; `Find`, `OfKind` and `NotaThemeService.KnownThemes` read `All`. User themes
load before `Appearance.Resolve` at startup, so a stored user id resolves. `Resolve` already
falls back to Studio Dark/Light for unknown ids without writing, so FR-006's "keep the stored
choice" holds as long as nothing calls `Store` with the fallback — the Appearance page only
stores on a user pick.

Reading is synchronous on the UI thread: small files, a 256 KB cap, a handful of files.

**Gotcha**: `NotaAccent.Wear` re-finds the theme by id (`NotaThemes.Find(theme.Id) ?? theme`)
and caches by id. A try-on or preview theme that reuses an existing id would be swapped for
the file version or hit a stale cache entry. Try-on/preview themes get a synthetic id
(`~tryon-<n>` / `~preview-<n>`, which no file can have under F2), and the accent cache is
invalidated on reload.

## R6 — Preview rendering inside the running app

**Today**: no in-app screenshot path. The docs harness (`../nota-docs/shots/app`) and the
headless recipes construct real views (`MixerView(engine)`, `DeviceChainView(engine, presets)`,
`PreferencesWindow`, menus via `Build*Menu`) and capture them. A `NotaEngine` can be created
and used without `Start()` (the smoke tests create several).

**Decision**:

- A `SkinPreviewRenderer` in `Nota.App` owns a private, never-started `NotaEngine` holding a
  fixed sample scene (built once on first use through the engine API: 6 coloured tracks of
  mixed kinds, MIDI and audio clips, an automation lane, built-in devices — an instrument, an
  audio effect, a MIDI effect — a return, a selection). It never touches the live engine.
- Each surface (contracts/preview-surfaces.md) is built from the real view classes over that
  engine, hosted in an offscreen top level, measured/arranged at a fixed size at scale 1×,
  and rendered with `RenderTargetBitmap` to PNG.
- The theme is applied for the duration of one UI-thread job: wear preview theme (palette
  re-tint + resources + radius/font skin) → build, lay out and render all requested surfaces
  → restore the live theme. The compositor commits only between dispatcher jobs, so the live
  window never presents the preview colours (FR-015). Live controls are not re-coerced during
  the job; only the offscreen tree is.
- Requests are serialised through `IEngineDispatch` (already one UI thread).

**Spike first (task)**: confirm (a) the offscreen top level (`EmbeddableControlRoot` or an
unshown `Window`) applies app styles and resources and renders via `RenderTargetBitmap` in
the live app on macOS, and (b) no frame with preview colours reaches the screen. Fallback for
(b): render under a cloned brush set by giving the offscreen root its own `Resources`
overriding every `Brush.*` key — needs custom-drawn views to read brushes from resources,
which they don't, so the job-scoped swap is the plan of record.

**Implementation finding** (T026): the top bar is ~240 lines of `MainWindow` markup bound
to `MainWindowViewModel`, and the browser needs the app's library view model (plug-in
catalogue, sample index, background scans). A second `MainWindow` or `BrowserViewModel` over
the sample engine would duplicate the app's services. So `overview`, `top-bar` and
`browser` render the **live** window's own controls inside the same scoped job: they are
re-coerced for the skin, drawn with `RenderTargetBitmap`, and re-coerced back after
`RenderScoped` restores the live skin. They show the user's project and are not
deterministic. The other seven surfaces are built from the sample scene. See
contracts/preview-surfaces.md.

**Live-app findings** (2026-11-10, real macOS app over MCP):
- A window shown off screen gets no client size from Width/Height set before `Show()`, and
  its position is clamped. Its size is set after `Show()`.
- The window's base font is Fluent's `fonts:Inter#Inter, $Default`. Nota ships no Inter,
  so this resolves to Geist. `NotaSkin` treats it as Geist, otherwise most text keeps Geist
  under a skin typeface.
- Measured: 10 images in about 1 s. A theme switch takes about 30 ms including the MCP round
  trip, on a near-empty project.

**Determinism**: fixed scene, fixed sizes, fixed scale, no meters or playhead (engine not
started), animations off in the offscreen root, text size step and UI scale forced to
defaults inside the job. Same theme → same bytes on the same machine (SC-004).

## R7 — MCP images

`ModelContextProtocol` 2.1.0 has `ImageContentBlock.FromBytes(bytes, "image/png")`. The
preview tool returns a `CallToolResult` whose content is, per surface, a `TextContentBlock`
("`mixer` — the mixer with six channel strips…") followed by the `ImageContentBlock`; the
structured problems/warnings come first as a JSON text block. No new dependency.

## R8 — MCP access to the app

New facade `IThemeAccess` in `Nota.Mcp/Abstractions.cs`, implemented in `Nota.App` (like
`IProjectVersionsAccess`), registered in `McpAppServices.ShareServices`. Every member runs on
the UI thread through `IEngineDispatch.InvokeAsync`. Tools in `Nota.Mcp/Tools/ThemeTools.cs`.

Writes are confined to the themes folder: the file name is derived from the validated id
(`<id>.json`), never from client input.

## R9 — Nota Remote

The phone page takes the worn theme's colours (`MainWindow.Remote.cs`). User themes and
try-ons flow through unchanged. Typeface and radii are not sent to the phone — out of scope
(the phone has its own type and geometry).

## R10 — Preferences UI

`PreferencesWindow.ThemePicker` builds cards with `ThemeCards(kind)` over `NotaThemes.OfKind`.
User themes join that list (after built-ins, by name) with a small "User" tag; a card
context menu offers "Duplicate". Below the cards: a row with "Open themes folder" and
"Reload", and — when there are problems — a compact list "file — reason". A card with
warnings shows a warning glyph + count with a tooltip. Follows `DESIGN.md` / `nota-design`;
strings are added to `Nota.Application/Localization/en.json` and its Russian twin (spec 077).
