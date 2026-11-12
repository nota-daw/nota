---

description: "Tasks for 093 — Menu button and a hideable menu bar"
---

# Tasks: Menu button and a hideable menu bar

**Input**: Design documents from `specs/093-menu-button-title-bar/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui.md, quickstart.md

**Tests**: SC-002 asks for an automated comparison of the two menus → one smoke-test suite
(`MenuMirrorTests`). The rest is verified per [quickstart.md](quickstart.md).

**Organization**: grouped by user story (spec.md priorities).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: US1…US5 from spec.md

---

## Phase 1: Setup

- [X] T001 Add `GlyphKind.Menu` (three strokes `M4 6.5 H20 M4 12 H20 M4 17.5 H20`, stroke only) to the enum and the `Paths` table in `src/managed/Nota.App/Controls/Glyph.cs`
- [X] T002 [P] Add English localisation keys `main.menu` ("Menu"), `main.show_menu_bar` ("Show menu bar"), `commands.show_menu_bar` ("Show menu bar"), `main.commands.menu_bar_shown` / `main.commands.menu_bar_hidden` (status line), `preferences.menu_bar` ("Menu bar"), `preferences.menu_bar_hidden` ("Hidden"), `preferences.menu_bar_shown` ("Shown"), `preferences.menu_bar_caption` (one sentence: the whole menu is also under the ☰ key at the start of the transport bar; Alt or F10 opens it), `preferences.open_the_menu` ("Open the menu") in `src/managed/Nota.Application/Localization/en.json` (other languages follow the usual translation flow; add the keys where the catalogue check requires them)

---

## Phase 2: Foundational (blocks all stories)

- [X] T003 Add `public bool ShowMenuBar { get; set; }` — default `false`, doc comment "Windows / Linux: the in-window menu row is shown (#518). Off = the ☰ Menu key at the start of the transport bar gives the menu. Ignored on macOS." — next to `ArrangementHeadersRight` in `src/managed/Nota.Application/Settings.cs`
- [X] T004 Create `src/managed/Nota.App/MenuMirror.cs` (SPDX header; Avalonia base only): `static IReadOnlyList<Control> Build(NativeMenu source)` making a fresh `MenuItem` / `Separator` tree per research R1 / data-model mirror table (Header, InputGesture ← Gesture, ToggleType / IsChecked, IsEnabled, ToolTip, submenus; click → `((INativeMenuItemExporterEventsImplBridge)src).RaiseClicked()`), plus `static MenuShape Describe(NativeMenu)` / `Describe(IEnumerable<Control>)` producing a comparable plain record tree for tests
- [X] T005 Link `MenuMirror.cs` into `tests/Nota.SmokeTest/Nota.SmokeTest.csproj` and add `tests/Nota.SmokeTest/MenuMirrorTests.cs` (SPDX header): build a `NativeMenu` with a nested submenu, a separator, checked and unchecked checkbox items, a gesture, a disabled item, a tooltip and a header with `__`; assert `Describe(source) == Describe(Build(source))`; flip a check mark and add an item, rebuild, assert again (freshness); register the suite where the smoke runner lists its suites

**Checkpoint**: mirror proven equal to its source.

---

## Phase 3: User Story 1 — the whole menu from a Menu button, row hidden (P1) 🎯 MVP

**Goal**: ☰ leads the transport bar on Windows / Linux and opens the full menu; the row is hidden by default.

**Independent test**: quickstart steps 1–6.

- [X] T006 [US1] In `src/managed/Nota.App/MainWindow.axaml`: name the menu-row border `MenuBarRow` (and the bar `MenuBarHost` = the `NativeMenuBar`); add, first in transport column 0's `StackPanel`, a `Border x:Name="MenuKeyWell" Classes="segmented view"` holding `ToggleButton x:Name="MenuBtn" Classes="seg view"` with `<app:Glyph Kind="Menu" Size="16"/>`, `ToolTip.Tip` / `AutomationProperties.Name` = `{app:T 'main.menu'}`, `IsVisible="False"` (code shows it); update the transport comment to mention the Menu key
- [X] T007 [US1] Create `src/managed/Nota.App/MainWindow.MenuButton.cs` (SPDX header): `InitMenuButton()` — on macOS leave `MenuKeyWell` hidden and return; else give `MenuBtn` a `ContextMenu` (placement bottom, anchored to the key) whose `Opening` clears and refills `Items` from `MenuMirror.Build(NativeMenu.GetMenu(this))`, keep `MenuBtn.IsChecked` true while open, save / restore keyboard focus around it (FR-015); `OpenMenuFromKeyboard()` opens it with the first item selected; `ApplyMenuBar(bool shown)` — Windows / Linux: `MenuBarRow.Child = shown ? MenuBarHost : null`, `MenuBarRow.IsVisible = shown`, `MenuKeyWell.IsVisible = !shown` (FR-011), tick `MenuItemFor("view.menuBar")`, `RequestTransportFit()`
- [X] T008 [US1] Wire it in `src/managed/Nota.App/MainWindow.axaml.cs`: call `InitMenuButton()` after `InitRecentMenu()` and apply `ApplyMenuBar(settings.Current.ShowMenuBar)` from `App.Services`' `ISettingsService` in the constructor (first frame right, SC-005); in the `vm.Settings.Changed` handler call `ApplyMenuBar(vm.Settings.Current.ShowMenuBar)`
- [X] T009 [US1] Confirm `MenuKeyWell` is not in `TransportShedOrder` and add a one-line note to the header comment of `src/managed/Nota.App/MainWindow.TransportFit.cs` that the Menu key, like the view switch, never goes (FR-006)

**Checkpoint**: US1 works with the row permanently hidden.

---

## Phase 4: User Story 2 — bring the menu row back (P2)

**Goal**: "Show menu bar" in View and Settings → Appearance, persisted.

**Independent test**: quickstart steps 7–8.

- [X] T010 [P] [US2] Add `C("view.menuBar", Loc.T("commands.show_menu_bar"), Loc.T("commands.view"), "", "menu bar menubar hamburger file edit row classic")` after `view.fullscreen` in `src/managed/Nota.Application/Palette/CommandCatalog.cs`
- [X] T011 [US2] Add `<NativeMenuItem Header="{app:T 'main.show_menu_bar'}" app:Commands.Id="view.menuBar" ToggleType="CheckBox"/>` after the Toggle full screen item in the View menu of `src/managed/Nota.App/MainWindow.axaml`
- [X] T012 [US2] In `src/managed/Nota.App/MainWindow.Commands.cs` bind `B("view.menuBar", ToggleMenuBar)`; `ToggleMenuBar()` (macOS: return) flips `_vm.Settings.Current.ShowMenuBar`, saves, sets the status line (`main.commands.menu_bar_shown` / `_hidden`); `Settings.Changed` applies it via T008
- [X] T013 [US2] In `src/managed/Nota.App/MainWindow.MenuKeys.cs` remove `view.menuBar` on macOS next to `file.exit` / `prefs.open` (FR-020)
- [X] T014 [US2] In `src/managed/Nota.App/PreferencesWindow.cs` add `MenuBarPicker()` (segmented Hidden | Shown, built like `HeaderSidePicker`, repainting on `Settings.Changed`) and, on Windows / Linux only, the rows `Row(Loc.T("preferences.menu_bar"), MenuBarPicker())` + caption after the Track headers rows of `AppearancePane()`

**Checkpoint**: US1 + US2 complete.

---

## Phase 5: User Story 3 — open the menu from the keyboard (P2)

**Goal**: F10 and a lone Alt tap open the Menu key's menu while the row is hidden.

**Independent test**: quickstart step 9.

- [X] T015 [US3] In `src/managed/Nota.App/MainWindow.MenuButton.cs` add the Alt-tap / F10 detector per research R4: tunnel + handledEventsToo handlers for `KeyDown`, `KeyUp`, `PointerPressed`, `PointerWheelChanged` registered in `InitMenuButton()` (Windows / Linux only), active only while the row is hidden and no popup / dialog is open; F10 (no modifiers) and Alt released after an un-interrupted Alt press call `OpenMenuFromKeyboard()` and mark the event handled; any other key, click or wheel disarms (FR-013, FR-014)
- [X] T016 [US3] Make sure the main key handlers in `src/managed/Nota.App/MainWindow.Input.cs` do not consume a bare Alt / F10 before the detector (adjust only if they do), and that F10 doesn't reach a text field's editing
- [X] T017 [US3] Add an "Alt   F10 → Open the menu" row (`preferences.open_the_menu`) to the general group of `ShortcutGroups` in `src/managed/Nota.App/PreferencesWindow.cs`, shown on Windows / Linux only (FR-017; use the existing platform filtering, or filter when the list is built)

---

## Phase 6: User Story 4 — project name in the Windows title bar (P3)

- [X] T018 [P] [US4] In `UpdateWindowTitle()` of `src/managed/Nota.App/MainWindow.Files.cs` set `DocTitleText.Text = title` on Windows, keep `ProjectDisplayName()` on macOS; update the comment (FR-018)

---

## Phase 7: User Story 5 — macOS unchanged (P1)

- [X] T019 [US5] Review every change above for its macOS guard: Menu key hidden, no detector, no View item, no Appearance row, no Shortcuts row, caption unchanged, menu row untouched (FR-020); run the app on macOS and compare with `develop`

---

## Phase 8: Polish & cross-cutting

- [X] T020 [P] `CHANGELOG.md` `[Unreleased]` → Changed entry (Windows / Linux: menu under ☰, menu bar hidden by default, Show menu bar setting, Alt / F10; Windows caption "Nota — name") with `(#518)`
- [X] T021 [P] Update `FEATURES.md` via the `nota-features` skill
- [X] T022 [P] Update the user docs (`../nota-docs`, EN + RU) via the `nota-docs` skill: main-window chrome, Settings → Appearance, Shortcuts
- [ ] T023 Russian (and other shipped languages) strings for the new keys in `src/managed/Nota.Application/Localization/*.json`, as the localisation check requires
- [X] T024 Visual check on the headless / harness app per quickstart (Windows / Linux layout of the transport with ☰, the open menu, Settings → Appearance) and before / after screenshots for the PR
- [ ] T025 `scripts/build.sh` passes (smoke test incl. MenuMirror and palette drift)

---

## Dependencies & execution order

- Phase 1 → Phase 2 → US1 (T006–T009) → US2 (T010–T014) → US3 (T015–T017).
- US4 (T018) depends on nothing but Phase 1; can be done any time.
- US5 (T019) after all behaviour is in.
- Polish after the stories; T025 last.

## Parallel opportunities

- T001 ‖ T002; T010 ‖ T018; T020 ‖ T021 ‖ T022.

## Implementation strategy

MVP = Phases 1–3 (the Menu key, row hidden). Then US2 (the way back), US3 (keyboard), US4
(caption), the macOS review and the docs.

## Nota compliance re-check

- [X] New files (`MenuMirror.cs`, `MainWindow.MenuButton.cs`, `MenuMirrorTests.cs`) carry the SPDX header
- [X] No new dependency
- [X] No audio-thread / engine change
- [X] CHANGELOG, FEATURES, docs, Shortcuts screen updated
- [X] UI follows DESIGN.md

## Implementation notes (2026-11-12)

- T005: the suite also runs alone with `Nota.SmokeTest --menu-mirror`.
- T007: `ContextMenu.Open()` does not raise `Opening` (only a right-click does), so
  `OpenMenuKeyMenu` fills the menu itself (`FillMenuKeyMenu`); found by the headless harness.
- T015/T019: the platform rule lives in one place, `MainWindow.HasMenuKey` (settable so a
  headless harness on a Mac can draw the Windows / Linux chrome).
- T016: nothing in `MainWindow.Input.cs` handles a bare Alt or F10; the detector runs in the
  tunnel phase with handledEventsToo, ahead of every view.
- T017: the Alt / F10 row is a plain literal row in `ShortcutGroups` (the docs' shortcuts
  generator parses literals only) and is filtered out on macOS at render time.
- T023: open — the repo holds only `en.json`; Russian / French come through the
  nota-translations reference sync.
- T024: headless harness `/tmp/menukeyharness` (12 checks: default hidden row, mirror of the
  real menu = 73 entries, F10 / Alt tap / Alt+S / Alt+click / AltGr, View toggle, Settings
  toggle, narrow window). Real Windows / Linux hardware not tried (no machine here); the
  built-in Avalonia access-key behaviour with the row shown is untested there.
- T025: open — `scripts/build.sh` on 2026-11-12 built everything and the smoke run passed all
  but 3 render-pool timing guards (VST3 device deadlines, "overloaded" late blocks), the same 3
  again with `--perf` alone. The machine was loaded (Rider ~270 % CPU, Nota.app running,
  load average ~9); this change touches no engine / render code. Re-run with them closed.
  Re-run with Rider closed (load ~5, Nota.app still running): the same guards failed. A/B of
  `--perf`, twice each, base `eec6a4ff` vs this branch on the same engine build: base 52 / 303
  late blocks of 4125, branch 233 / 311; the VST3 deadline guard flips on both. The failures
  are the machine's, not this change's; a fully idle run is still to do.
- Review on #585 (2026-11-12): Alt during a drag no longer arms the tap (pointer button state
  tracked; a move with a button down disarms), and Alt / F10 do nothing while the command
  palette is open. Harness: 15 checks, all pass.
