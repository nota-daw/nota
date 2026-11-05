---

description: "Tasks: choose the accent colour (specs/084, issue #311)"
---

# Tasks: Choose the accent colour

**Input**: Design documents from `specs/084-accent-colour/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, mockup/

**Tests**: The spec requires smoke-test enforcement (FR-002, SC-002), so test tasks are included.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 presets · US2 custom colour · US3 Nota Remote

## Phase 1: Setup

- [X] T001 Confirm the worktree builds on `feat/accent-colour` (`scripts/build.sh`) before any change, recording the smoke-test pass count as the baseline

---

## Phase 2: Foundational (blocking)

- [X] T002 Create `src/managed/Nota.Application/Accent.cs` (SPDX header): `AccentChoice(string Preset, uint? Custom)` with `IsDefault`; `AccentPreset(Id, NameKey, Chroma, Hue)` and `Accent.Presets` in display order orange, lime (0.15, 125°), aqua (0.12, 185°), sky (0.13, 224°), violet (0.15, 295°), magenta (0.16, 330°), pink (0.15, 355°) — research R4
- [X] T003 In `src/managed/Nota.Application/Accent.cs` add the pure derivation `AccentColours`: OKLab/OKLCH ↔ sRGB on `0xRRGGBB`, gamut mapping by ×0.95 chroma steps, `SolidTokens` (`Accent`, `AccentBright`, `AccentHover`, `AccentDeep`, `AccentPale`, `SystemAccent`, `SystemAccentLight1–3`, `SystemAccentDark1–3`) move by ΔL, `BlendTokens` (`AccentDim`, `AccentEdge`, `AccentMute`, `AccentSubtle`, `BorderBrass`) keep lightness; `TextOnAccent` = `InkOn(Accent)` (`#17181A` or `#FFFFFF`, higher WCAG contrast); Orange returns the theme's own values — research R2
- [X] T004 In `src/managed/Nota.Application/Accent.cs` add the rules and the fit: `Failures(theme, tokens, includeTracks)` for T3.4 (`AccentBright` on `BgApp`/`Panel`/`BgSunken` ≥ 2.3), T3.5 (ink on `Accent` ≥ 4.5), T3.8 + A1 (ΔE76 ≥ 15 vs `Record`/`Mute`/`Solo`/`Success`), A2 (Δa\*b\* ≥ 15 vs the same), A3 presets-only (ΔE76 ≥ 15 vs `Track1`–`Track9`); `Wear(theme, choice) → AccentResult { Tokens, Adjusted, Refusal }` searching lightness in 0.005 steps (+n then −n), presets ±0.40, custom ±0.12; custom refused when chroma < 0.05 (`Grey`), when the chosen colour itself clashes (`Clash`, `ClashWith` = token), or when nothing passes (`Unreadable`) — research R3
- [X] T005 [P] In `src/managed/Nota.Application/Settings.cs` add `Accent` (string, default `""`) and `CustomAccent` (string, default `""`, `#RRGGBB`) with doc comments; in `Accent.cs` add `Accent.Resolve(Settings) → AccentChoice` (`""`, unknown id, `custom` with missing/malformed hex → Orange; never throws, never writes) and `Accent.Store(Settings, AccentChoice)` — data-model.md § Settings
- [X] T006 Create `src/managed/Nota.App/Theme/NotaAccent.cs` (SPDX): `Wear(NotaColourTheme, AccentChoice) → NotaColourTheme` overlaying `AccentColours.Wear` tokens, same Id/Name/Kind, cached per (Id, choice), Orange → the same theme object; a preset refusal (never expected) falls back to the theme itself
- [X] T007 In `src/managed/Nota.App/Theme/NotaThemeService.cs` keep `Accent` next to `Choice`; `Set(choice, accent)` / `SetAccent(accent)`; `ApplyActive` wears `NotaAccent.Wear(theme, Accent)` so slots, Brush.* and the Fluent `SystemAccentColor*` ramp follow; `Worn(theme)` for previews and Remote; raise a `Changed` event after an apply
- [X] T008 In `src/managed/Nota.App/App.axaml.cs` resolve the accent with the appearance and pass it to `NotaThemeService.Set` before the first window paints (FR-009)
- [X] T009 Create `tests/Nota.SmokeTest/DesignTokenCheck.Accent.cs` with `RunAccent()` and call it from `tests/Nota.SmokeTest/Program.cs`: loads the four theme files, asserts Orange identity for every theme (all tokens equal the file), every preset × theme has no `Failures` incl. A3, every theme as shipped passes A1/A2, resolution fallbacks (`""`, `"nope"`, `custom` + `"zz"`), store round-trip, custom cases `#00B5D8` as is, `#4C6FFF` adjusted, `#3FA7FF` clash solo in Studio Light only, `#8C8C8C` grey

**Checkpoint**: smoke test green, the app wears Orange as before.

---

## Phase 3: User Story 1 — Pick a preset accent (P1) 🎯 MVP

**Goal**: an Accent row of seven swatches applies live, saves, survives restart and theme switches.

**Independent Test**: Settings → Appearance → click each preset, switch themes, restart.

- [X] T010 [US1] Create `src/managed/Nota.App/PreferencesWindow.Accent.cs` (SPDX, partial class): `AccentPicker()` — swatch per preset (22 × 22 hit, 2 px ring slot: transparent / `Rule` hover / `TextPrimary` chosen, 14 × 14 fill radius `NotaRadius.Clip` in the preset's accent as worn in the active theme, tooltip = name), 6 px gap, Custom button (chip + "Custom" + `GlyphKind.Edit`), readout (`TextMuted` 12; "Custom · #RRGGBB" hex in Geist Mono); a pick → `Accent.Store`, `settings.Save()`, `NotaThemeService.SetAccent`, refresh — contracts/ui-contract.md § Accent row
- [X] T011 [US1] In `src/managed/Nota.App/PreferencesWindow.cs` insert `TopRow(Loc.T("preferences.accent"), AccentPicker())` and its caption under the colour theme rows; theme cards (`ThemeCards`/`ThemePreview`) draw `NotaThemeService.Worn(t)` and repaint on accent change, keeping the `ColorIn(…, _theme)` contract RunThemes checks
- [X] T012 [P] [US1] Add keys to `src/managed/Nota.Application/Localization/en.json`: `preferences.accent`, `preferences.accent_caption`, `preferences.accent_custom`, `accent.orange|lime|aqua|sky|violet|magenta|pink`
- [X] T013 [US1] Verify in the headless app (memory recipe: NOTA_DATA_DIR, env-guarded hook + screencapture): Violet in all four themes, Orange identical to develop

**Checkpoint**: MVP — presets work end-to-end.

---

## Phase 4: User Story 2 — Use my own colour (P2)

**Goal**: Custom opens a picker; colours apply live, are tuned or refused, Esc reverts.

**Independent Test**: Custom → `#00B5D8`, `#4C6FFF`, `#3FA7FF`, `#8C8C8C`; Esc; restart.

- [X] T014 [US2] Create `src/managed/Nota.App/Controls/ColourPicker.cs` (SPDX): saturation/value area 216 × 132 and hue strip 216 × 10 as `WriteableBitmap`s (SV rebuilt only on hue change), `Rule` frames radius 3, handles in `HandleSel` with a `Seam` ring, left-button drag, `ColourChanged(uint rgb)` event, `Set(uint rgb)`; no gradient brushes, no colour literals (RunBans)
- [X] T015 [US2] In `src/managed/Nota.App/PreferencesWindow.Accent.cs` open a `Flyout` under the Custom button: `ColourPicker`, before/after chip, hex `TextBox.field` (Geist Mono 12; `#RGB`/`#RRGGBB`, `#` optional), "In themes" four chips (derived `Accent` per theme via `AccentColours.Wear`, 4 px `TextPrimary` dot when adjusted, `BgSunken` when refused, tooltip = theme name), note line (two lines reserved; `TextMuted`, `Danger` when refused); live apply of accepted colours without saving; close → store `custom` + `CustomAccent` and save; Esc → restore the previous accent and close — contracts/ui-contract.md § Colour picker
- [X] T016 [P] [US2] Add keys to `src/managed/Nota.Application/Localization/en.json`: `preferences.accent_in_themes`, `preferences.accent_note_as_is`, `preferences.accent_note_adjusted`, `preferences.accent_note_adjusted_all`, `preferences.accent_note_grey`, `preferences.accent_note_clash`, `preferences.accent_note_unreadable`, `preferences.accent_clash_record|mute|solo|meter`
- [X] T017 [US2] Verify the four custom examples and Esc in the headless app; screenshots for the PR

---

## Phase 5: User Story 3 — The phone follows (P3)

- [X] T018 [US3] In `src/managed/Nota.App/MainWindow.Remote.cs` build `RemoteTheme`s from `NotaThemeService.Worn(…)`, cache by the worn object (not the id), and include the accent in `_remoteLookFor` so a change re-sends the theme
- [X] T019 [US3] Extend `tests/Nota.SmokeTest/DesignTokenCheck.Remote.cs` (or RunAccent) with a source check that the Remote appearance is built from worn themes

---

## Phase 6: Polish & cross-cutting

- [X] T020 [P] `DESIGN.md`: accent section — the accent is a user choice derived per theme (link specs/084), rules A1/A2 next to the bar, the picker spectrum in Accepted exceptions; run `python3 scripts/design-html.py`
- [X] T021 [P] `CHANGELOG.md` `[Unreleased]` Added entry (#311)
- [X] T022 [P] FEATURES.md via the `nota-features` skill
- [X] T023 [P] User docs EN + RU via the `nota-docs` skill (Settings → Appearance page)
- [ ] T024 Run `scripts/build.sh` (smoke test incl. RunAccent, RunThemes, RunBans, RunLabels, RunLocalization) and fix anything red
- [X] T025 Re-run `specs/084-accent-colour/mockup/build.py`; confirm its preset values equal the app's (RunAccent prints them)

## Dependencies

- Phase 2 blocks all stories (T002 → T003 → T004; T005 ∥; T006 → T007 → T008; T009 after T004/T005).
- US1 (T010–T013) after Phase 2. US2 after T010 (same file). US3 after T007 only — parallel to US1/US2.
- Polish after the stories.

## Parallel opportunities

- T005 ∥ T003/T004 (different file); T012 ∥ T010; T016 ∥ T014; T018 ∥ US1/US2; T020–T023 together.

## Implementation strategy

MVP = Phase 2 + US1 (presets, guaranteed readable). Then US2 (custom), US3 (Remote), polish,
one PR closing #311.

## Phase 7: Convergence

- [X] T026 Open the Custom picker on the remembered custom colour (`Settings.CustomAccent`) when a preset is worn, falling back to the worn accent only when none was ever chosen, in `src/managed/Nota.App/PreferencesWindow.Accent.cs` per US2/AC5 (partial)
