---
description: "Tasks for the Classic look (spec 091, issue #572)"
---

# Tasks: Classic look

**Input**: Design documents from `specs/091-classic-look/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The project gate is `tests/Nota.SmokeTest`; the plan (Constitution IV, R9)
requires smoke checks for the pure logic, palettes, file format and type rules, so they
are included as tasks. No separate unit-test project.

**Organization**: Grouped by user story; each story is testable on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (switch, P1), US2 (skins, P2), US3 (MCP, P3)

---

## Phase 1: Setup

**Purpose**: Bring back the font files and the data the rest builds on.

- [X] T001 Restore `assets/fonts/GeistMono-Regular.ttf`, `GeistMono-Medium.ttf`, `GeistMono-SemiBold.ttf`, `GeistMono-Bold.ttf` unmodified from `v0.59.2` (`git show v0.59.2:assets/fonts/<file> > assets/fonts/<file>`), and make sure `src/managed/Nota.App/Nota.App.csproj` packs them as `Assets/Fonts` like the Geist files
- [X] T002 [P] Update the Geist row and note 5 in `LICENSES/third-party.md`: Geist Mono back in use for values and readouts under the classic look, same OFL 1.1 licence file `LICENSES/OFL-1.1-Geist.txt`, unmodified files from the same package
- [X] T003 [P] Write `scripts/classic-role-sites.py` (SPDX header): from the #296 commit `cbd77012` list every removed line containing `NotaFonts.Mono`, `MonoFamily`, `MonoBold`, `MonoMedium` or `Font.Mono` with its file, and for each file that still exists on HEAD print the likely current line (the added line of the same hunk); output `file:line  old → new`, used by T019–T023
- [X] T004 [P] Commit the 0.59.2 palettes as a test fixture `tests/Nota.SmokeTest/Fixtures/classic-0.59.2.json`: `{ "<theme id>": { "<token>": "#RRGGBB[AA]" } }` for the four built-ins, values read from `git show v0.59.2:src/managed/Nota.App/Theme/Themes/<id>.json` (107 tokens each)

---

## Phase 2: Foundational (blocking)

**Purpose**: The look as data and the composition rule every story uses.

- [X] T005 Add `public string Look { get; set; } = "";` with a doc comment (`""`/`"new"` → New, `"classic"` → Classic; unknown reads as New, never rewritten on read) to `src/managed/Nota.Application/Settings.cs`, next to `Accent`
- [X] T006 Create `src/managed/Nota.Application/Looks.cs` (SPDX header, why-comment): `public enum Look { New, Classic }`; `Looks.Resolve(Settings)`; `Looks.Store(Settings, Look)` (writes `""` for New, `"classic"` for Classic); `Looks.Id(Look)` → `"new"`/`"classic"` and `Looks.Parse(string)` → `Look?` for MCP
- [X] T007 Extend `src/managed/Nota.Application/Themes/SkinGeometry.cs`: add `string? ValuesFamily` and `bool HasRadius` (true when the file carried a `radius` object, even an empty one); include both in `Equals`/`GetHashCode`; `Default` has `ValuesFamily = null`, `HasRadius = false`; `IsDefault` also requires `ValuesFamily is null && !HasRadius`; update every constructor call (grep `new SkinGeometry(`) including `ThemeFile.cs` reading format 2 (`HasRadius = root has "radius"`)
- [X] T008 In `src/managed/Nota.Application/Looks.cs` add `ClassicGeometry` = roundness `0.4`, steps `Card 3, Group 3, Field 3, Cell 2, ValueBar 2, Switch 2, Fine 2`, and `Compose(SkinGeometry skin, Look look) → SkinGeometry`: typeface = `skin.FontFamily`; radii = `skin.HasRadius` → skin's roundness+steps, else Classic → `ClassicGeometry`'s, else design (roundness 1, no steps); `ValuesFamily` = `skin.ValuesFamily` (installed-ness is checked in the app layer, T015)
- [X] T009 Create `tests/Nota.SmokeTest/LooksCheck.cs` (`RunLooks()`): `Resolve` of `""`, `"new"`, `"classic"`, `"Classic"`, `"x"` → New, New, Classic, New, New; `Store` round-trips; `Compose` rows of data-model § Composition — default skin × New = design scale; default × Classic = classic steps 3/3/3/2/2/2/2 and `MapRadius(9)=3`, `MapRadius(6)=3`, `MapRadius(0)=0`, off-scale `MapRadius(10)=4`; skin with `HasRadius` (scale 0) × Classic = all 0; skin font kept in both looks; register in `tests/Nota.SmokeTest/Program.cs` next to `RunSkins`

**Checkpoint**: `scripts/build.sh` green with the new checks.

---

## Phase 3: User Story 1 — Switch to the classic look (P1) 🎯 MVP

**Goal**: Preferences → Appearance → Look: New | Classic, live and stored; classic
colours, corners and Geist Mono for values on the four built-in themes.

**Independent Test**: quickstart § 2 and § 3 — flip during playback, compare with 0.59.2,
restart, flip back; New previews identical to before.

### Colours

- [X] T010 [P] [US1] Create the four classic palettes `src/managed/Nota.App/Theme/Themes/Classic/{studio-dark,studio-light,ember-graphite,ember-paper}.json` (format 1, same `id`/`name`/`kind` as the built-in, keys in catalogue order): copy the current built-in file, set `Panel`, `SurfaceRaised`, `TextDisabled`, `VizInk` (studio-dark) and `Mute` (studio-light) back to their `v0.59.2` values, and give the 15 new tokens (`Well`, `WellTrack`, `KnobTrack`, `Groove`, `DeviceSection`, `DeviceSectionEdge`, `DeviceHeader`, `Bevel`, `AccentSoft`, `KnobCap`, `MeterSafe`, `MeterHot`, `MeterOver`, `TrackHeaderSelected`, `LaneSelected`) values that read like 0.59.2, taken from the colours the `v0.59.2` styles used for those parts (`git show v0.59.2:src/managed/Nota.App/Theme/NotaTheme.axaml` and the views) per research R7
- [X] T011 [US1] Embed the classic palettes in `src/managed/Nota.App/Nota.App.csproj` with `LogicalName="Nota.Themes.Classic.%(Filename)%(Extension)"` (the existing `Theme\Themes\*.json` glob must not pick them up as built-ins), and load them in `src/managed/Nota.App/Theme/NotaColourTheme.cs` (`NotaThemes`) into a `Classic(string id) → NotaColourTheme?` lookup that is never listed in `All`/`BuiltIn`
- [X] T012 [P] [US1] Add `RunClassicPalettes()` to `tests/Nota.SmokeTest/DesignTokenCheck.Themes.cs` (registered in `Program.cs`): each built-in has a classic file; each passes F1–F6 with the full catalogue; every token present in `Fixtures/classic-0.59.2.json` equals the fixture (SC-002, no exceptions); T3 readability warnings are allowed only when every token of the pairing is in the fixture and equals it — a pairing that involves one of the 15 new tokens must pass (research R8)

### Corners and composition

- [X] T013 [US1] In `src/managed/Nota.App/Theme/NotaThemeService.cs` add `Look` state, `public static Look Look`, `SetLook(Look)` (live; saving is the caller's, as `SetAccent`), and change `Set` to take the look at startup; in `ApplyActive` compose per research R3: theme → under Classic a built-in id is replaced by `NotaThemes.Classic(id)` → accent (`Worn`) → `Looks.Compose(theme.Geometry, look)` → `NotaSkin.Set` + `NotaFonts.SetMeasure` (T016) → re-coerce, repaint, `Changed`; compare composed state so a look change with the same theme still re-applies (today's `ReferenceEquals(NotaPalette.Theme, theme)` short-circuit must not swallow it)
- [X] T014 [US1] Keep `NotaThemeService.Worn(theme)` look-free for Nota Remote (`src/managed/Nota.App/MainWindow.Remote.cs` stays on the new palette, spec Edge Cases) and add `WornForCard(theme)` that applies the look's colours (built-ins only) for theme cards
- [X] T015 [US1] Teach `src/managed/Nota.App/Theme/NotaSkin.cs` the composed geometry: `Set` keeps working from a `SkinGeometry` (now composed), and `IsDefault` short-cuts stay exact no-ops under New with the default skin (SC-004)

### Values and readouts role

- [X] T016 [US1] In `src/managed/Nota.App/Theme/NotaFonts.cs` add `public const string MeasureMonoUri = "avares://Nota.App/Assets/Fonts#Geist Mono";`, a current `MeasureFamily` (default `UiFamily`), `SetMeasure(FontFamily)` that also writes `Application.Current.Resources["Font.Measure"]`, and properties `Measure`, `MeasureMedium`, `MeasureBold` built from the current family (read at call time, never cached in static readonly fields); update the file's header comment (two roles; New is one typeface)
- [X] T017 [US1] In `src/managed/Nota.App/Theme/NotaTheme.axaml` add `<FontFamily x:Key="Font.Measure">avares://Nota.App/Assets/Fonts#Geist</FontFamily>` beside `Font.UI`; make `TextBlock.Mono`, `.Readout`, `.Value`, `.Eyebrow` set `FontFamily="{DynamicResource Font.Measure}"` (sizes/weights unchanged); add `TextBlock.Number` (Geist via `Font.UI`, same size/colour as `.Value`) for numbers added after 0.59.2; restore `Font.Measure` at each 0.59.2 `Font.Mono` use still present (T003 output)
- [X] T018 [US1] In `NotaThemeService.ApplyActive` (T013) set the measure family: skin `ValuesFamily` if installed (`NotaThemes.IsFontInstalled`), else New → `NotaFonts.UiFamily`, Classic → `new FontFamily(NotaFonts.MeasureMonoUri)`; under New with no skin values font the resource must be the very Geist URI so `NotaSkin.IsGeist` still maps it to a skin's `font.family`
- [X] T019 [US1] Run `scripts/classic-role-sites.py` and save its output as the inventory checklist `specs/091-classic-look/role-sites.md` (file, old line, current line, decision: role / gone / redrawn-same-value)
- [X] T020 [P] [US1] Restore the role in the shell views per `role-sites.md` — `NotaFonts.Sans*` → `NotaFonts.Measure*` (or `Font.UI` → `{DynamicResource Font.Measure}`) only at 0.59.2 mono sites: `src/managed/Nota.App/{PreferencesWindow*.cs, PreviewPlayer.cs, SessionView*.cs, ArrangementView*.cs, MixerView.cs, CommandPaletteView.cs, WelcomeWindow.cs, StereoMapView.cs, ModularView.cs}`, `src/managed/Nota.App/Remote/ConnectPhoneView.cs`
- [X] T021 [P] [US1] Same for `src/managed/Nota.App/Controls/*.cs` (e.g. `ConsortViews.cs`, `LengthGateViz.cs`) and the piano roll / audio editor views listed in `role-sites.md`
- [X] T022 [P] [US1] Same for `src/managed/Nota.App/DeviceCards/**` (instrument cards, bodies, rack `MacroPanel.cs`, `PluginCard.cs`); where a card was redrawn, put the role on the same value (e.g. a band's Freq readout) per research R5
- [X] T023 [US1] Move uses of `TextBlock.Mono/.Readout/.Value/.Eyebrow` and `NotaFonts.Sans*` numbers that were **added after 0.59.2** (in `role-sites.md`'s "new since" list: `git log -S'Classes.Add("Value")' v0.59.2..HEAD` and the XAML class uses) to `TextBlock.Number` / `NotaFonts.Sans*` so they stay Geist under Classic (clarification, FR-009)
- [X] T024 [US1] Replace every `static readonly` cache of a measure typeface introduced in T020–T022 with a call-time read (`NotaFonts.Measure`), so a live switch reaches custom-drawn text (research R11)

### Preferences and startup

- [X] T025 [US1] Add the Look row to the Appearance pane in `src/managed/Nota.App/PreferencesWindow.cs` (`AppearancePane`, above the mode row) per contracts/ui-contract.md L1: a two-cell segment control **New | Classic** (shared segment control from `Controls/`), caption under it, applies on pick via `NotaThemeService.SetLook`, stores `Looks.Store(settings, look)` and saves settings the way the accent does (`PreferencesWindow.Accent.cs`), Tab/arrow reachable; refresh on `NotaThemeService.Changed`
- [X] T026 [US1] Theme cards in `src/managed/Nota.App/PreferencesWindow.cs` use `NotaThemeService.WornForCard` so built-in cards show classic colours and classic corners under Classic (contract L2)
- [X] T027 [US1] At startup in `src/managed/Nota.App/App.axaml.cs` (where `NotaThemeService.Set(Appearance.Resolve(...))` runs) resolve `Looks.Resolve(startSettings)` and pass it in, before the splash/first window
- [X] T028 [P] [US1] Add the English strings to `src/managed/Nota.Application/Localization/en.json`: `preferences.look`, `preferences.look_new` ("New"), `preferences.look_classic` ("Classic"), `preferences.look_new_caption` ("The current Nota look."), `preferences.look_classic_caption` ("Colours, corners and type as in Nota 0.59.") — Russian follows via the reference sync (FR-018)

### Design checks

- [X] T029 [US1] Update `RunType()` in `tests/Nota.SmokeTest/DesignTokenCheck.cs` per research R9: families in the theme are exactly `Font.UI` and `Font.Measure`; `Font.Measure`'s default equals `Font.UI`'s URI; `NotaFonts` has `UiUri` and `MeasureMonoUri` and nothing else Mono; `assets/fonts` holds the four `Geist-*.ttf` and four `GeistMono-*.ttf` weights and nothing else; the OFL file exists; views still never name a family by string or `FontFamily.Default`; Geist Mono is named only in `Theme/`
- [X] T030 [US1] Run `scripts/build.sh`; then quickstart § 2–3: switch live during playback (no xrun in log), compare against `v0.59.2` on all four themes, restart, switch back; render the MCP preview set under New before (from `develop`) and after and confirm identical images (SC-004); fix clipping found under Classic at 100 % and the largest UI scale without changing layout sizes (FR-016)

**Checkpoint**: US1 is shippable on its own.

---

## Phase 4: User Story 2 — Classic with a user skin (P2)

**Goal**: Skin wins where it sets something; format 3 adds `font.values`; Duplicate copies
what is worn.

**Independent Test**: quickstart § 4 with the three skin files.

- [X] T031 [US2] Format 3 in `src/managed/Nota.Application/Themes/ThemeFile.cs` per contracts/theme-file-v3.md: F1″ accepts 1–3; V1′ `font.values` only in 3; V2′ `font` has optional `family` and (v3) optional `values`, "each a string of 1–100 characters after trimming; at least one present; no other keys", problems name `font.family`/`font.values`; W1′ warns for a missing `values` family; reader fills `SkinGeometry.ValuesFamily`; writer emits `formatVersion` 3 only when `ValuesFamily` is set, else 2 when font/radius set, else 1
- [X] T032 [P] [US2] Add problem/warning strings for the new rules to `src/managed/Nota.Application/Localization/en.json` (`themes.problem.needs_v3`, `themes.problem.font_values`, the missing-values-font warning)
- [X] T033 [P] [US2] Format-3 cases in `tests/Nota.SmokeTest/LooksCheck.cs` (`Format3()`; `DesignTokenCheck.Skins.cs` now refuses format 4 instead of 3): v3 valid (`family`+`values`, `values` only); invalid (`values` in v2, empty, 101 chars, extra key, `font: {}`); v1/v2 fixtures unchanged after load+save; `Compose` with a v3 skin keeps `ValuesFamily` under both looks
- [X] T034 [US2] Duplicate in `src/managed/Nota.App/PreferencesWindow.Skins.cs`: a built-in duplicated while Look is Classic writes the classic palette's colours (not the geometry) (FR-013, contract L2)
- [X] T035 [US2] Skins UI in `src/managed/Nota.App/PreferencesWindow.Skins.cs` / theme card tooltip: show the values typeface when a skin sets one; missing-family warning text names the fallback of the current look ("using Geist Mono" under Classic, "using Geist" under New) (contract L3; the warning names the missing values font and the look's own face is used — there is no separate skins editor)

**Checkpoint**: US1 + US2 work together; skins from #336 unchanged under New (SC-006).

---

## Phase 5: User Story 3 — Look from MCP clients (P3)

**Goal**: Read/set the look and preview either look over MCP.

**Independent Test**: quickstart § 5.

- [X] T036 [US3] Extend `IThemeAccess` and records in `src/managed/Nota.Mcp/Abstractions.cs`: `ThemeChoiceInfo` gains `string Look`; `string SetLook(string look)` (returns worn id; throws `ArgumentException` naming "new, classic" on an unknown value); `GetFile(string id, string? look)`; `PreviewAsync(..., string? look)`
- [X] T037 [US3] Implement them in `src/managed/Nota.App/Mcp/ThemeAccess.cs` on the UI thread: `SetLook` wears, stores (`Looks.Store` + settings save), refreshes Preferences if open; `GetFile(id, "classic")` for a built-in returns the classic palette file text; previews go through `NotaThemeService.RenderScoped(theme, themeAccent, look, render)` (add the `look` parameter in `src/managed/Nota.App/Theme/NotaThemeService.cs`, composing as `ApplyActive` does and restoring the live look's measure family afterwards)
- [X] T038 [US3] In `src/managed/Nota.Mcp/Tools/ThemeTools.cs` add `set_look` (input `look`: `"new"` | `"classic"`, returns `{ look, worn }`), optional `look` on `get_theme` and `preview_theme` (default: worn look for previews, original file for `get_theme`), and `choice.look` in `list_themes`; descriptions per contracts/mcp-tools.md; format 3 mentioned in `validate_theme`/`save_theme` descriptions
- [X] T039 [US3] Verify over MCP per quickstart § 5 (list, set, get classic, preview both looks, live window untouched)

**Checkpoint**: all stories done.

---

## Phase 7: User Story 4 — Solid or tinted clips (P2, scope widened)

**Goal**: Preferences → Appearance → Clips: Solid | Tinted; default follows the look.

**Independent Test**: spec User Story 4; harness renders Arrangement under each combination.

- [X] T046 [US4] Add `public string ClipStyle { get; set; } = "";` (`""` follows the look, `"solid"`, `"tinted"`) to `src/managed/Nota.Application/Settings.cs`, and `enum ClipStyle { Tinted, Solid }` with `Looks.ClipStyleOf(Settings)` / `Looks.StoreClipStyle(Settings, ClipStyle)` in `src/managed/Nota.Application/Looks.cs`
- [X] T047 [P] [US4] Smoke checks in `tests/Nota.SmokeTest/LooksCheck.cs`: `""` × New → Tinted, `""` × Classic → Solid, `"tinted"` × Classic → Tinted, `"solid"` × New → Solid, `"x"` follows the look
- [X] T048 [US4] `ArrangementView.ClipStyle` property (invalidates the lanes) in `src/managed/Nota.App/ArrangementView.cs`, set from settings in `src/managed/Nota.App/MainWindow.axaml.cs` at start and on `Settings.Changed`
- [X] T049 [US4] Solid branch in `DrawClipBody` (`src/managed/Nota.App/ArrangementView.LaneControl.cs`) per research R12, with `ClipBand` / `ClipInk` shades restored in `src/managed/Nota.App/ArrangementView.cs`; Tinted path unchanged
- [X] T050 [US4] Clips row in `src/managed/Nota.App/PreferencesWindow.cs` (contract L5) + strings `preferences.clips`, `preferences.clips_solid`, `preferences.clips_tinted`, `preferences.clips_caption` in `src/managed/Nota.Application/Localization/en.json`; refresh on `NotaThemeService.Changed` so a look switch moves the shown default
- [X] T051 [US4] Verify headless: Arrangement under New (identical to develop), Classic (solid), New+Solid, Classic+Tinted; update CHANGELOG, FEATURES, DESIGN.md (§ Clip: the Solid style)

## Phase 6: Polish & cross-cutting

- [X] T040 [P] Add a "Classic look" section to `DESIGN.md` (the switch, classic tokens table with where each of the 15 values came from, corner table with the three deviations, the two type roles and when to use `TextBlock.Number`) and regenerate `DESIGN.html` with its script
- [X] T041 [P] `CHANGELOG.md` `[Unreleased]` → Added: the Look switch (classic colours, corners, Geist Mono values), skins `font.values`, MCP `set_look` (#572)
- [X] T042 [P] Run the `nota-features` skill: FEATURES.md entries for the Look switch, format 3, MCP
- [X] T043 Run the `nota-docs` skill: Preferences → Appearance page EN + RU in `../nota-docs` (Look row; screenshots stay in the new look), skins page (`font.values`)
- [X] T044 Re-check the Nota Compliance Checklist in `specs/091-classic-look/spec.md` and tick what holds
- [X] T045 Full quickstart run (`specs/091-classic-look/quickstart.md` §§ 1–6) and `scripts/build.sh` green

---

## Dependencies & Execution Order

- **Setup (T001–T004)** → **Foundational (T005–T009)** → stories.
- **US1**: T010 → T011 → T012; T013 → T014, T015; T016 → T017 → T018; T019 → T020–T022 [P] → T023 → T024; T025–T027 after T013; T029 after T016–T017; T030 last.
- **US2** depends on Foundational (T007) and T013/T018 for wearing; T031 → T033; T034–T035 after T011.
- **US3** depends on T013 and T031 (format 3 in validate/save); T036 → T037 → T038 → T039.
- **Polish** after the stories it documents.

## Parallel Examples

- Setup: T002, T003, T004 together after T001.
- US1: T010 (palettes) beside T016–T017 (fonts); T020, T021, T022 (role sites in disjoint folders) together; T028 any time.
- US2: T032 and T033 beside T034–T035.
- Polish: T040, T041, T042 together.

## Implementation Strategy

1. **MVP = Phases 1–3 (US1)**: the switch over built-in themes — what the users asked
   for. Ship-ready alone (quickstart § 2–3).
2. **+US2**: skins behave predictably under Classic; format 3.
3. **+US3**: MCP parity for the skin design loop.
4. Polish, docs, full quickstart, `/speckit-converge`.
