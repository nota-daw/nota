---
description: "Tasks for 087 — user skins (colours, typeface, corner radii) with MCP support"
---

# Tasks: User skins (colours, typeface, corner radii) with MCP support

**Input**: `specs/087-user-skins/` — plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Issue**: nota-daw/nota#336 (approved)

**Tests**: Included. The constitution's verification gate needs the smoke test to cover the
new rules (quickstart.md § 1). They are pure checks with no UI, in `tests/Nota.SmokeTest/SkinTests.cs`.

**Organization**: Grouped by user story. US1, US2 and US4 are P1; US3 is P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 wear a skin · US2 load problems · US3 start from a theme · US4 MCP

---

## Phase 1: Setup

- [X] T001 Create branch `feat/user-skins` off `develop` (spec dir `specs/087-user-skins/` comes along)
- [X] T002 Spike (research R6), throwaway and not committed: in the running app on macOS, host an `ArrangementView`/`MixerView` over a second, never-started `NotaEngine` in an offscreen top level (try `Avalonia.Controls.Embedding.EmbeddableControlRoot` first, then an unshown `Window`), `Measure`/`Arrange` at a fixed size, render it with `RenderTargetBitmap` and save the PNG. Inside the same UI-thread job, re-tint the palette (`NotaPalette.Apply` with another built-in theme) before rendering and restore it after. Confirm that (a) app styles and resources apply, (b) the live window never shows the other colours. Record the chosen host and any gotchas in specs/087-user-skins/research.md § R6

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the format, the shared validator and skin geometry. Everything else builds on these.

- [X] T003 [P] Create `RadiusStep` enum (`Card, Group, Field, Cell, ValueBar, Switch, Fine`) with design values 9/7/6/5/4/3/2, and `SkinGeometry` record (`FontFamily: string?`, `Roundness: double` "0–2, default 1", `StepOverrides: IReadOnlyDictionary<RadiusStep,double>` "exact pixels 0–24"). Add `RadiusOf(step)` = override else `Math.Round(design × Roundness)`, `MapRadius(double v)`: 0 → 0; a design value → its step's `RadiusOf`; other → `Math.Round(v × Roundness)`. Add `Default` and `IsDefault`. File: src/managed/Nota.Application/Themes/SkinGeometry.cs
- [X] T004 Create `ThemeFile` in src/managed/Nota.Application/Themes/ThemeFile.cs (no Avalonia):
  - `Validate(string json, IReadOnlyList<string> catalogue, IReadOnlySet<string> translucent, Func<string,bool>? fontInstalled = null) → ThemeValidation`. It collects **every** problem: F1′ `formatVersion` 1 or 2; F2 id `^[a-z0-9]+(-[a-z0-9]+)*$`, not `system`; F3 name 1–40 chars; F4 kind dark|light; F5 exactly the catalogue tokens; F6 `#RRGGBB`/`#AARRGGBB`, opaque unless translucent; V1 `font`/`radius` only in v2; V2 `font` = `{ "family": string 1–100 chars after trim }` and no other keys; V3 `radius` = `{ scale?: 0–2, steps?: {} }` and no other keys; V4 step keys `card, group, field, cell, valueBar, switch, fine`, values 0–24; JSON errors. Each problem gets `Rule`, `Field` and `Message` (data-model.md § ThemeValidation).
  - Warnings: T3.1–T3.8 readability, ported from tests/Nota.SmokeTest/DesignTokenCheck.Themes.cs `Pairings` (same ids, minimums, WCAG contrast, CIE76 ΔE, ink `#17181A`/`#FFFFFF`), only when the colours are valid; `FontMissing` when `fontInstalled` returns false.
  - `ParsedTheme` (id, name, kind, `uint` ARGB colours by token, `SkinGeometry`).
  - `Write(ParsedTheme, IReadOnlyList<string> catalogue) → string`: catalogue key order, 2-space indent, `formatVersion` 2 only when geometry is not default.
- [X] T005 Make `NotaThemes.Parse` a wrapper over `ThemeFile.Validate` (first problem as `error`, same messages style), keep built-in loading behaviour; add `Geometry` (SkinGeometry), `Source` (`BuiltIn | User(path) | TryOn | Preview`) and `Warnings` to `NotaColourTheme` in src/managed/Nota.App/Theme/NotaColourTheme.cs
- [X] T006 [P] Smoke checks in tests/Nota.SmokeTest/SkinTests.cs (`SkinTests.Run()`), wired in tests/Nota.SmokeTest/Program.cs next to `RunThemes`:
  - the four built-ins validate with no problems, and their T3 warnings equal `DesignTokenCheck` pairings;
  - the SC-002 malformed set (missing/unknown token, bad hex, translucent opaque token, bad id, `system`, bad kind, invalid JSON, v1+`font`, scale 3, step `huge`, empty family) each names its field;
  - a file with three problems reports all three;
  - `Write`→`Validate` round-trips;
  - `SkinGeometry` at scale 0/0.5/2 with overrides; `MapRadius(0)==0`; each design value maps to its step.

**Checkpoint**: `dotnet build` + smoke green; app behaviour unchanged.

---

## Phase 3: User Story 1 — Wear a skin I made (P1) 🎯 MVP

**Goal**: theme files in `<data>/Themes` show up in Appearance and can be worn, typeface and radii included. The choice survives a restart.

**Independent Test**: quickstart.md § 2 steps 2–4.

### Registry and wearing

- [X] T007 [US1] In src/managed/Nota.App/Theme/NotaColourTheme.cs add `ThemesFolder` (`Path.Combine(NotaPaths.DataDir, "Themes")`), `User`, `All` (built-ins, then user themes by name, culture-aware, case-insensitive), `UserLoadProblems: List<(string FileName, string Reason)>`, `ReloadUser()` and `event ThemesChanged`. `ReloadUser` reads `*.json` in ordinal file-name order, skips files > 256 KB with a reason, validates with `fontInstalled` = `FontManager.Current.SystemFonts` names, and rejects ids taken by a built-in or an earlier file ("duplicate id"). It never throws. `Find` and `OfKind` search `All`
- [X] T008 [US1] In src/managed/Nota.App/Theme/NotaAccent.cs: `Wear` must not swap a non-built-in theme for `Find(id)` when `theme.Source` is TryOn/Preview, and the cache must clear on `NotaThemes.ThemesChanged` (research R5 gotcha)
- [X] T009 [US1] In src/managed/Nota.App/Theme/NotaThemeService.cs: `KnownThemes` over `All`; on `ThemesChanged`, re-apply the active theme (the object for the same id may have changed, so compare by content, not by reference). Raise `GeometryChanged` when the worn theme's `SkinGeometry` differs from the previous one
- [X] T010 [US1] Load user themes before `Appearance.Resolve` at startup in src/managed/Nota.App/App.axaml.cs (`NotaThemes.ReloadUser()`), so a stored user id resolves. The stored setting must not be overwritten when it falls back (FR-006)

### Live radii and typeface (research R2, R3)

- [X] T011 [US1] Create src/managed/Nota.App/Theme/NotaSkin.cs:
  - `Geometry` (current `SkinGeometry`) and `ResolvedFamily: FontFamily?` (null = Geist or font missing);
  - `Install()`, called before XAML loads: metadata overrides with coerce callbacks on `Border.CornerRadiusProperty`, `TemplatedControl.CornerRadiusProperty` and `ContentPresenter.CornerRadiusProperty` (each corner through `Geometry.MapRadius`), and on `TextElement.FontFamilyProperty` for `TextBlock`, `TextPresenter`, `TemplatedControl` and `TopLevel` (a Geist family → `ResolvedFamily`, else unchanged);
  - `Set(SkinGeometry)`;
  - `Reapply()`: walk every top level of the desktop lifetime and `CoerceValue` those properties on all visual descendants, then `InvalidateVisual`. Follow the pattern of src/managed/Nota.App/Theme/NotaTextSize.cs
- [X] T012 [US1] Call `NotaSkin.Install()` at startup next to `NotaFonts.InstallTabular()` / `NotaTextSize` install, in src/managed/Nota.App/App.axaml.cs (or Program.cs, wherever those are called)
- [X] T013 [US1] Make `NotaRadius.*Value` `static double` properties returning `NotaSkin.Geometry.RadiusOf(step)` (`DockedValue` stays 0) in src/managed/Nota.App/Theme/NotaGeometry.cs. Fix any compile errors where a `*Value` was used as a constant (attribute args, `const` locals, `switch` cases). Adapt `DesignTokenCheck.RunGeometry` in tests/Nota.SmokeTest/DesignTokenCheck.cs if it reads them as constants
- [X] T014 [US1] In src/managed/Nota.App/Theme/NotaFonts.cs `NotaFormattedText` constructors: when the typeface's family is Geist (`NotaFonts.UiFamily`, by name) and `NotaSkin.ResolvedFamily` is set, use `new Typeface(ResolvedFamily, style, weight)`; tabular figures stay
- [X] T015 [US1] In `NotaThemeService.ApplyActive` (src/managed/Nota.App/Theme/NotaThemeService.cs): `NotaSkin.Set(theme.Geometry)` and, when it changed, `NotaSkin.Reapply()` before `RepaintEverything()`. Resolve the family on every wear (a font may have been uninstalled)
- [X] T016 [US1] Audit views for cached radii and typefaces that bypass the choke points: `grep -rn "static readonly CornerRadius\|static readonly double.*Radius\|RadiusX = [0-9]"` in src/managed/Nota.App. Route radii through `NotaRadius`, or accept them via `MapRadius` (record intentional exceptions in tests/Nota.SmokeTest/RadiusAllowList.txt as that file's format requires)

### Preferences

- [X] T017 [US1] Appearance theme cards (contracts/ui-contract.md U1) in src/managed/Nota.App/PreferencesWindow.cs `ThemePicker`/`ThemeCards`:
  - user themes follow the built-ins and carry a "User" tag;
  - the swatch uses the theme's Card radius and typeface;
  - selecting a user card is the same as selecting a built-in;
  - `NotaThemes.ReloadUser()` runs whenever the page is built;
  - a "Reload" button (contracts/ui-contract.md U3) re-reads the folder and rebuilds the page.

  Follow DESIGN.md / nota-design.
- [ ] T018 [P] [US1] (EN done; RU goes to ../nota-translations after the reference has the keys — see Notes) Strings `preferences.themes.user_tag`, `preferences.themes.reload` (and the keys the later tasks add) in src/managed/Nota.Application/Localization/en.json and the Russian twin in the same folder

**Checkpoint**: quickstart § 2 steps 2–4 pass by hand (or headless harness). Built-in themes look exactly as before (Studio Dark screenshot diff empty).

---

## Phase 4: User Story 2 — Know why my skin did not load (P1)

**Goal**: problems and warnings are visible in Appearance, and the fallback is graceful.

**Independent Test**: quickstart.md § 2 steps 5–6.

- [X] T019 [US2] Problems list (U4) in src/managed/Nota.App/PreferencesWindow.cs: show it only when `NotaThemes.UserLoadProblems` is non-empty, one row per file `file — reason` (first three problems, then "+N more"), never a modal
- [X] T020 [US2] Warning badge on cards (U1) in src/managed/Nota.App/PreferencesWindow.cs: a glyph plus the count; the tooltip lists up to 5 (pairing with "measured of minimum", or "Font 'X' is not installed — using Geist"), then "+N more"
- [X] T021 [US2] Fallback note (U5) in src/managed/Nota.App/PreferencesWindow.cs: when the stored id (`Settings.ColourTheme` / the per-mode ids) is not in `KnownThemes`, show "'<id>' could not be loaded — showing <fallback>" under the mode row; do not call `Appearance.Store` until the user picks
- [ ] T022 [P] [US2] (EN done; RU follow-up as T018) Strings for U4/U5 and the warning tooltip in src/managed/Nota.Application/Localization/en.json + the Russian twin
- [X] T023 [P] [US2] Loader smoke checks in tests/Nota.SmokeTest/SkinTests.cs, using `ThemeFile` plus a temp folder. Either extract the folder scan into a pure `ThemeFolder.Scan(dir, …)` in src/managed/Nota.Application/Themes/ThemeFolder.cs that `NotaThemes.ReloadUser` calls, or test through it. Cover: duplicate id across two files (first wins, ordinal order), a built-in id rejected, a 300 KB file skipped, non-json ignored

**Checkpoint**: broken files are reported in the page, none of them crash the app, and fixing a file brings its theme back.

---

## Phase 5: User Story 4 — An AI designs a skin over MCP (P1)

**Goal**: the ten tools in contracts/mcp-tools.md, with `preview_theme` rendering the surfaces of contracts/preview-surfaces.md.

**Independent Test**: quickstart.md § 3.

### Try-on and scoped wearing

- [X] T024 [US4] In src/managed/Nota.App/Theme/NotaThemeService.cs add:
  - `TryOn(NotaColourTheme)`: Source TryOn, synthetic id `~tryon-<n>`; worn, not stored;
  - `RevertTryOn()`. `Set(choice)` also ends a try-on;
  - `TryOnTheme`;
  - `RenderScoped(NotaColourTheme theme, bool themeAccent, Action render)`: wear the theme (palette, resources, `NotaSkin`, user accent or the theme's own) without `RepaintEverything` or `Changed`, run `render`, restore the previous worn theme and skin in `finally`. Everything happens in one UI-thread job.

### Preview rendering

- [X] T025 [US4] Create src/managed/Nota.App/Skins/SampleScene.cs. It holds a private `NotaEngine` (never `Start()`ed, disposed with the app), built once on first use:
  - 6 tracks of mixed kinds with fixed names and colours, MIDI clips with notes, an audio clip from a bundled sample, an automation lane;
  - built-in instrument + audio effect + MIDI effect on one track, a return, a selection.

  Use only the public `IAudioEngine` API.
- [X] T026 [US4] Create src/managed/Nota.App/Skins/SkinPreviewRenderer.cs:
  - the surface table of contracts/preview-surfaces.md (stable names, descriptions, sizes), each built from the real view class over `SampleScene`. `top-bar` is extracted from MainWindow markup if the T002 spike requires it; `overview` is composed in a grid;
  - offscreen host per T002;
  - inside the job: animations off, text-size step 0, UI scale 1;
  - `Render(NotaColourTheme theme, bool themeAccent, IReadOnlyList<string>? names) → (rendered list of (name, description, png bytes), unknown names)`, all inside `NotaThemeService.RenderScoped`.
- [X] T027 [US4] Determinism check: render the same theme twice and compare the bytes. Add a debug-only `--skin-previews <theme.json> <outdir>` path in the smoke test only if it can run headless; otherwise document the manual check in quickstart.md § 3 step 4

### MCP

- [X] T028 [US4] Add `IThemeAccess` to src/managed/Nota.Mcp/Abstractions.cs. Its members, all run on the UI thread: `List()`, `GetFile(id)`, `Catalogue()`, `SystemFonts()`, `Validate(json)`, `Preview(json, surfaces, themeAccent)`, `Save(json, overwrite)`, `Apply(id)`, `TryOn(json)`, `RevertTryOn()`. Their DTO records follow contracts/mcp-tools.md
- [X] T029 [US4] Implement `ThemeAccess` in src/managed/Nota.App/Mcp/ThemeAccess.cs over `NotaThemes`, `NotaThemeService`, `ThemeFile` and `SkinPreviewRenderer`:
  - `Save` writes only `Path.Combine(ThemesFolder, id + ".json")`, using the validated id. It refuses an invalid theme, a built-in id, and an existing user id without `overwrite`, then calls `ReloadUser()`;
  - `Apply` = `Appearance.WithPick` + `Store` + `Set`, as the Preferences pick does;
  - the token catalogue carries a group and a one-line role per token, from `NotaPalette` (add a role table there if none exists).

  Register it in src/managed/Nota.App/Mcp/McpAppServices.cs `ShareServices`.
- [X] T030 [US4] Create src/managed/Nota.Mcp/Tools/ThemeTools.cs with the ten tools `list_themes`, `get_theme`, `get_theme_catalogue`, `list_system_fonts`, `validate_theme`, `preview_theme`, `save_theme`, `apply_theme`, `try_on_theme`, `revert_try_on`:
  - names, arguments and results as in contracts/mcp-tools.md, `theme` as a JSON string;
  - `preview_theme` returns a `CallToolResult`: one JSON text block (validation + `rendered`/`unknown`), then per surface a text block `"<name> — <description>"` and `ImageContentBlock.FromBytes(png, "image/png")`;
  - descriptions as in contracts/mcp-tools.md § Tool descriptions. A malformed JSON argument is a validation result, never an exception
- [X] T031 [US4] Keep Preferences current after MCP changes (FR-020): an open PreferencesWindow rebuilds its Appearance page on `NotaThemes.ThemesChanged` and `NotaThemeService.Changed`, in src/managed/Nota.App/PreferencesWindow.cs

**Checkpoint**: quickstart § 3 passes from a real MCP client against the running app.

---

## Phase 6: User Story 3 — Start from an existing theme (P2)

**Goal**: "Open themes folder" and "Duplicate".

**Independent Test**: quickstart.md § 2 step 1.

- [X] T032 [US3] "Open themes folder" (U3) in src/managed/Nota.App/PreferencesWindow.cs: `Directory.CreateDirectory(ThemesFolder)`, then open it in the system file manager the way the app already opens folders (find the existing helper, e.g. the one used for the samples or logs folder)
- [X] T033 [US3] Card context menu (U2) in src/managed/Nota.App/PreferencesWindow.cs:
  - "Duplicate" on every card: id = slug of "<name> copy", numbered `-2`, `-3`… on a clash; name "<name> copy" trimmed to 40 chars; written with `ThemeFile.Write`; then `ReloadUser()` and select it;
  - "Show in folder" on user cards.

  Build it with MenuKit like the other context menus.
- [ ] T034 [P] [US3] (EN done; RU follow-up as T018) Strings for U2/U3 in src/managed/Nota.Application/Localization/en.json + the Russian twin

---

## Phase 7: Polish & cross-cutting

- [X] T035 (live app: 10 previews ≈ 1 s; switch ≈ 30 ms incl. MCP, near-empty project — a large project not measured) Live performance: time a theme switch with a custom font and roundness on a large project (≤ 250 ms target, plan.md). Time a full `preview_theme` (< 10 s, SC-003). Record the numbers in specs/087-user-skins/research.md
- [ ] T036 Visual check with the headless harness recipe (memory: nota-ui-verify) for the four built-ins: before and after this change are pixel-identical (default geometry is a no-op)
- [X] T037 [P] DESIGN.md: a short section saying that radii and typeface are skinnable, the style guide's scale is the base and coercion maps it; new code keeps using `NotaRadius`/`NotaFonts`, never literals or other families
- [X] T038 (§ 1 smoke; § 3 over MCP in the live app incl. restart persistence; § 2 by hand-free equivalents — Preferences seen through preview images, no screen capture available) Run quickstart.md end to end (§§ 1–3)

### Nota closing tasks

- [ ] T039 (2026-11-10: native + managed build green; smoke 5600+ pass, every skins check passes; the only failures are the two render-pool "overloaded" timing checks, which flake under machine load and touch nothing here — re-run on an idle machine before the PR) Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T040 AGPL-3.0-only SPDX two-line header on every new source file (SkinGeometry.cs, ThemeFile.cs, ThemeFolder.cs, NotaSkin.cs, SampleScene.cs, SkinPreviewRenderer.cs, ThemeAccess.cs, ThemeTools.cs, SkinTests.cs)
- [X] T041 New dependency: N/A (none added) — confirm
- [X] T042 `CHANGELOG.md` `[Unreleased]` → Added: user skins (colours, typeface, corner radii) in Preferences → Appearance; MCP theme tools with preview screenshots
- [X] T043 (FEATURES.md done; nota-docs EN+RU: Appearance ▸ Your own themes, reference/theme-files, ai/mcp ▸ Design a theme — nota-daw/nota-docs branch docs/user-skins) FEATURES.md via the `nota-features` skill; user manual EN + RU via `nota-docs` (Appearance page, new "Making a skin" page with the file format and the MCP loop); shortcuts: N/A
- [X] T044 Engine behaviour change: N/A (managed only); the smoke test is extended by T006/T023 for the new rules

---

## Dependencies & Execution Order

- **Phase 1** → **Phase 2** → stories.
- T002 (spike) blocks T025–T027 only. Phases 2–4 can go ahead while the spike is open.
- **US1** (Phase 3) is the base for US2, US3 and US4, which need the registry (T007) and wearing (T009, T015).
- **US2** depends on T007 and T017 (the page it extends).
- **US4** depends on T005, T007, T009 and T015, and on T002 for previews. `validate_theme`, `get_theme`, `list_*`, `save`, `apply` (T028–T030 without preview) work before T025–T026.
- **US3** depends on T004 (`Write`), T007 and T017.
- Within US1: T011 → T012, T013, T014 → T015 → T016.
- Same-file serialisation: PreferencesWindow.cs tasks T017 → T019 → T020 → T021 → T031 → T032 → T033; NotaThemeService.cs tasks T009 → T015 → T024.

## Parallel opportunities

- T003 ∥ T006 (test scaffolding) once T004's API is sketched.
- T018, T022, T034 (strings) ∥ any code task.
- After US1: US2 (Preferences) ∥ US4 MCP non-preview parts (T028–T030), since they touch different files except T031.
- T037 (DESIGN.md) ∥ anything.

```text
# Example: after Phase 3
Task: "T019–T021 Appearance problems/warnings/fallback in PreferencesWindow.cs"
Task: "T028 IThemeAccess in Nota.Mcp/Abstractions.cs"
Task: "T030 ThemeTools.cs (non-preview tools first)"
```

## Implementation strategy

1. **MVP** = Phases 1–3 (US1): user skins load, wear, persist, with typeface and radii. Validate with quickstart § 2 steps 2–4 and the built-in pixel check (T036).
2. Add **US2**, which is what makes hand-written files usable.
3. Add **US4** in two steps: non-preview tools first, then preview (after the spike).
4. Add **US3** (convenience).
5. Polish and closing tasks, then `/speckit-converge` and the PR (`nota-pr`, evidence: Appearance before/after plus one preview set).

If review asks to split (constitution V), the cut is after Phase 4: US1+US2+US3 in one PR, US4 (MCP) in a follow-up.

## Notes

- Implementation notes (2026-11-10):
  - Smoke checks live in `tests/Nota.SmokeTest/DesignTokenCheck.Skins.cs` (`RunSkins`),
    not `SkinTests.cs`.
  - Radii and the typeface are laid over the base value at Animation priority, not coerced.
    Avalonia refuses metadata overrides on Border and Rectangle, and a coerced inherited
    font did not come back reliably.
  - Previews are hosted in off-screen transparent windows (T002). They are verified
    headless by the `/tmp/skinharness` recipe:
    - the live window stays pixel-identical across a preview;
    - the live window is pixel-identical again after apply, try-on, revert and back;
    - the sample surfaces are byte-identical between runs.
  - RU strings are ready in `/tmp/nota-skins-tools/keys-ru.json` (the T018/T022/T034
    follow-up). They go to `../nota-translations`, whose check accepts only keys in its
    published reference, i.e. after this PR merges.

- Commit style `(feat) …` per repo history. The user makes commits themselves: leave changes for review.
- The live UI must never show preview colours. If T002 can't guarantee this, stop and revisit research R6 before T025.
