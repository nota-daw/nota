---

description: "Tasks for the interface language selector, English + French (spec 068)"
---

# Tasks: Interface Language Selector (English + French)

**Input**: Design documents from `specs/068-ui-language/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/translation-file.md](contracts/translation-file.md),
[contracts/loc-api.md](contracts/loc-api.md), [quickstart.md](quickstart.md)

**Tests**: Smoke-test checks are included because the plan relies on them to keep the
translation complete (constitution IV; plan § Testing). They follow the existing
`DesignTokenCheck` pattern. The UI itself is validated through [quickstart.md](quickstart.md).

**Organization**: Grouped by user story. US1 is large, so it is split into four delivery
sub-phases (3a–3d = plan slices 1–4). Each sub-phase lands with English output unchanged and
`scripts/build.sh` green, and can be its own pull request (constitution V).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 … US5 from spec.md
- Paths are from the repo root. `src/managed/` is shortened to `managed/` where it helps.

## Rules that apply to every "wrap and translate" task (3a–3d)

These are written once here so each task can stay short.

1. **Wrap**: a user-visible literal `"Save"` becomes `Loc.T("Save")`; text with values becomes
   one `Loc.F("Delete {0} clips?", n)` pattern; singular/plural becomes
   `Loc.Plural(n, "{0} clip", "{0} clips")`. Never glue `Loc.T` fragments into a sentence
   (loc-api guarantee 3). A word with two meanings gets a context: `Loc.T("Open", context: "menu")`.
2. **Do not wrap**: user content (project / track / clip / scene / preset names, tags),
   device and parameter names and their value labels (FR-004), proper nouns and product
   names, unit symbols and musical notation, log messages, MCP text, `Nota.Remote`.
   Literals that stay raw go in `tests/Nota.SmokeTest/LocalizationAllowList.txt` with a
   `# data:` reason.
3. **English must not change**: the English string at the call site is the key, so output is
   identical. Do not reword English while wrapping; a reword orphans its key.
4. **Translate**: add the French value to `managed/Nota.App/Languages/fr.json` using the terms
   in [glossary.md](glossary.md). Placeholders `{0}`, `{1}` must match the key. Plurals need
   `one` and `other` (French: 0 and 1 are `one`). `fr.json` is one shared file, so these
   edits are never `[P]` with each other.
5. **Ratchet**: delete the now-wrapped literals from `LocalizationAllowList.txt`. The check
   fails on a stale line, so this cannot be forgotten.
6. **Verify**: run the smoke test; walk the area in French (`NOTA_UI_LANGUAGE=fr`) at 100 %
   and 150 % interface scale for clipped or overlapping labels.

---

## Phase 1: Setup

- [X] T001 Create the git branch `feat/ui-language` from `develop` (spec dir `specs/068-ui-language/`); confirm `scripts/build.sh` is green before any change *(done with the caveats under Notes → Environment)*
- [X] T002 Write `specs/068-ui-language/glossary.md`: the French music vocabulary (piste, clip, scène, envoi, retour, bus, quantification, enveloppe, filtre, arrangement, session, mixeur, …) and the register rule from research R10 (infinitive for actions, e.g. "Enregistrer", "Supprimer la piste"; explanatory text avoids addressing the user directly). Ask the maintainers to confirm it before T022 (the first French text)

---

## Phase 2: Foundational (blocks all stories) — plan slice 0

**Purpose**: the translation layer, startup install and the checks that keep it honest. No
visible change yet. Pure logic first so the smoke test can walk it, as it does `InterfaceScale`.

- [X] T003 [P] Add `public string Language { get; set; } = "en";` to `managed/Nota.Application/Settings.cs` next to `InterfaceScale`, with a doc comment in the same style ("Interface language code, lower-case, `en` or `fr`; anything else reads as `en` — spec 068"). Stored with the other Appearance preferences, never in the project
- [X] T004 [P] Create `managed/Nota.Application/Localization/UiLanguage.cs` (SPDX header + why-comment, pure: no UI, no I/O): `Default = "en"`; `Normalize(string? code, IReadOnlyCollection<string> available)` returns the lower-cased code if it is in `available`, else `"en"`, never throws; `Plural(string code, long n)` returns `One` or `Other` — English: `One` when `n == 1`; French: `One` when `n` is 0 or 1; a language with no rule uses English's; `IsPending(stored, running, available)` is true exactly when `Normalize`d stored differs from running (data-model § state)
- [X] T005 [P] Create `managed/Nota.Application/Localization/TranslationTable.cs` (SPDX header, pure): parse a language file per [contracts/translation-file.md](contracts/translation-file.md) — `formatVersion` must be `1` (higher → skipped with an error, not thrown), `code` lower-case, `name`, `notes`, `strings` map of key → string or `{ "one", "other" }`; keys are the English text, a context-tagged key is `"<text>|<context>"`; duplicate keys are an error; a malformed file returns an error and no table, never throws. Immutable after construction; lookup is a dictionary read
- [X] T006 Create `managed/Nota.Application/Localization/Loc.cs` (SPDX header, depends on T004, T005): static `T(text, context?)`, `F(text, args…, context?)`, `Plural(n, one, other, context?)`, `Install(table, languageCode)`, `Language` per [contracts/loc-api.md](contracts/loc-api.md). English or a missing key returns the passed text; the context suffix is never shown; `F` and `Plural` format with the **invariant** culture and `{{` / `}}` as literal braces; `Plural` keys on the `other` form and passes `n` as `{0}`; a debug-only flag records any call made before `Install` (R6). Do **not** touch `CultureInfo.CurrentCulture` (research R4). Thread-safe because the installed table is immutable
- [X] T007 [P] Create `managed/Nota.App/Languages/en.json` (`formatVersion` 1, `code` "en", `name` "English", no `strings`) and `managed/Nota.App/Languages/fr.json` (`formatVersion` 1, `code` "fr", `name` "Français", `notes` carrying the glossary pointer, empty `strings`). Add `<EmbeddedResource Include="Languages\*.json" LogicalName="Nota.Languages.%(Filename)%(Extension)" />` to `managed/Nota.App/Nota.App.csproj` beside the Themes item. No SPDX header in JSON (matches the theme files)
- [X] T008 Create `managed/Nota.App/Languages/NotaLanguages.cs` (SPDX header, modelled on `NotaThemes`): enumerate the embedded `Nota.Languages.*.json`, parse each with `TranslationTable`, expose `Available` (code + native name, English first, then by code) and `LoadErrors` (logged at startup like `NotaThemes.LoadErrors`; a bad file is skipped, Nota never fails to start because of a language file) (depends on T005, T007)
- [X] T009 In `managed/Nota.App/App.axaml.cs`, resolve `ISettingsService`, compute the language (`NOTA_UI_LANGUAGE` environment override first, never persisted; else `Settings.Language`; normalised against `NotaLanguages.Available`) and call `Loc.Install(...)` **before** any view model, window or static text table is touched, next to the theme and interface-scale application (R6, R9). Also read where `SplashWindow` is created: if it can show text before settings load, make it use the same language (spec User Story 1 scenario 2); record the finding in a code comment (depends on T006, T008)
- [X] T010 Create `tests/Nota.SmokeTest/DesignTokenCheck.Localization.cs` (SPDX header; a `partial class DesignTokenCheck` so it can reuse the private ratchet helpers `Literals`, `ReadRatchet`, `RatchetVerdict`, `RepoRoot`; `IEnumerable<(bool Ok, string Label)>` like its siblings). Part one is `RunLanguage` with the pure checks: lookup hit / miss returns English; context suffix never shown; missing plural form falls back; English plural (1 → one, 0 and 2 → other) and French plural (0 and 1 → one, 2 → other); `F` keeps invariant number notation and `{{ }}`; a broken pattern falls back to English; a malformed file and a `formatVersion` 2 file are skipped without throwing; `Normalize` of `""`, `"zz"`, `"FR"`, `null` (→ `en`, `fr`); `new Settings().Language == "en"`; `IsPending` true/false cases; `Available` lists `en` then `fr` with their own names; the "used before install" flag is false after the app-style startup sequence. Register it in `tests/Nota.SmokeTest/Program.cs` beside `RunInterfaceScale` (depends on T003–T008). The smoke test does not reference `Nota.App`, so the real `Languages/*.json` files are read from disk and parsed with `TranslationTable`, as the theme files are
- [X] T011 In `DesignTokenCheck.Localization.cs` (`RunLocalization`) add the **coverage** checks (research R7): read `managed/**/*.cs` and the `.axaml` files as text, collect every literal key in `Loc.T/F/Plural(` calls (handle the `context:` argument and plural `other` form), and assert (a) every key has an entry in `fr.json` once the area is marked done — until then report the missing count as information so slices can land incrementally, (b) no `fr.json` key is unused (orphan), (c) the `{n}` placeholder set of each French value equals its key's, (d) every plural entry has `one` and `other`, (e) no key is duplicated. Include a `--strict` switch (env `NOTA_LOC_STRICT=1`) that makes (a) a failure; slice 5 turns it on by default
- [X] T012 In `DesignTokenCheck.Localization.cs` add the **raw-literal ratchet** (research R7, modelled on `RunLabels`): reuse the label-literal contexts from `DesignTokenCheck.Labels.cs` (Text / Content / Header / Title, `ToolTip.SetTip`, label builders, command titles, palette headings, XAML attributes) but report only literals **not** wrapped in `Loc`; extend the scan to `managed/Nota.Presentation` and `managed/Nota.Application` with a "prose literal" rule (starts with a capital, two or more words or ends in `…`, not in a log / trace / `nameof` / attribute / `throw new` position) because their text is not set through `Text =`. Lines are `path <TAB> literal <TAB> # reason` in `tests/Nota.SmokeTest/LocalizationAllowList.txt` (same SPDX comment header and format as `LabelAllowList.txt`); a new offender fails, and so does a stale line. Print offenders in allow-list format on failure so the baseline can be pasted
- [X] T013 Generate the baseline `tests/Nota.SmokeTest/LocalizationAllowList.txt` from the first run of T012 so the check starts green, and tag each line `# todo:` (to be wrapped) or `# data:` (stays raw). The count printed by the check (`localization allow-list: N entries (ratchet: may only shrink)`) is the measurable backlog for slices 1–4 (depends on T012)
- [X] T014 Adapt `DesignTokenCheck.Labels.cs` so the sentence-case / Title Case checks still see labels once wrapped (plan risk R6): extend `CsContexts`, `CatalogTitle` and `PaletteHeadings` so a literal inside `Loc.T("…")`, `Loc.F("…", …)` or `Loc.Plural(…, "…")` in those positions is still read as a label, and add `Loc` to the builder-aware routing. Prove it with a temporary wrapped copy of an existing offender in `LabelAllowList.txt` failing the same way, then revert the copy. `RunLabels` and `RunMenus` must stay green without new allow-list lines (depends on T006)
- [X] T015 Confirm the layer rule still holds: `Loc` in `Nota.Application` references neither Avalonia nor Infrastructure (the existing `NoRef(application, …)` architecture check must pass), and add `Nota.Presentation` / `Nota.Infrastructure` using `Loc` without new project references (they already reference Application)

**Checkpoint**: infrastructure compiles; `scripts/build.sh` is green; English is unchanged;
`fr.json` is empty and the coverage report lists the keys still to translate.

---

## Phase 3: User Story 1 — Use Nota in French (P1) 🎯 MVP

**Goal**: A French-speaking user picks Français in Preferences → Appearance, restarts, and
sees the whole interface in French.

**Independent Test**: Quickstart §2 and §3 — switch to Français, restart, walk every menu and
window; no English interface text remains except device/parameter names, units, proper
nouns and user content.

### Phase 3a — The selector and the first French (plan slice 1)

**Done when**: the user can choose a language, is offered a restart, and the Preferences
window and the menu bar are fully French. Every later sub-phase is "more of the same".

- [X] T016 [US1] Add the **Language** row to the Appearance pane in `managed/Nota.App/PreferencesWindow.cs` (the pane around `AppearancePane`, beside *Colour theme* and *Interface scale*), following the existing picker style (DESIGN.md Studio; use `nota-design`). Choices come from `NotaLanguages.Available`, each shown in its own language ("English", "Français") so they stay findable whichever language runs (FR-001). Selecting writes `Settings.Language` through `ISettingsService` and does not change the running interface
- [X] T017 [US1] Create `managed/Nota.App/AppRestart.cs` (SPDX header): `bool TryRelaunch()` per research R5 — macOS: `open -n` on the enclosing `.app` bundle found by walking up from the process path; Windows and Linux: start the same executable with the same arguments; started as a detached helper before the app exits. Returns false, starting nothing, when the target cannot be determined (e.g. a `dotnet run` dev session)
- [X] T018 [US1] In `PreferencesWindow.cs`, after a language is chosen and differs from the running one, show the choice as a drop-down with, on the same row and no popup, the note "Applies after restart" and a **Restart** button within 1 second (SC-002): *Restart* goes through the existing close path including the unsaved-changes prompt (`SaveChangesWindow`), then calls `AppRestart.TryRelaunch()` and exits; cancelling the save prompt cancels the restart and keeps the stored choice; where nothing can be relaunched the note reads "Applies the next time Nota starts" and there is no button. The Language row shows a short "Applies after restart" note while `UiLanguage.IsPending(stored, running)` (FR-002). Reuse `ConfirmWindow` styling
- [X] T019 [P] [US1] Wrap and translate `managed/Nota.App/PreferencesWindow.cs`, `PreferencesWindow.Store.cs`, `PreferencesWindow.Models.cs`, `PreferencesWindow.Samples.cs`, `PreferencesWindow.Downloads.cs`, `PreferencesWindow.Remote.cs`, `PreferencesWindow.Updates.cs`. The `Page` table and section headings are static text: they must be built after `Loc.Install` (R6). Includes the Shortcuts pane's action names and group headings; **the key bindings themselves are unchanged**, so `nota-shortcuts` is N/A (apply Rules 1–6)
- [X] T020 [P] [US1] Wrap and translate the menu bar: `managed/Nota.App/MainWindow.Menu.cs`, `MainWindow.MenuKeys.cs`, `MenuKit.cs`, `MenuKit.Rows.cs`, `MenuItemsPanel.cs`, and the menu text built in `MainWindow.Recent.cs` and `MainWindow.Commands.cs`, **and the macOS application menu in `managed/Nota.App/App.axaml`** (About / Check for updates / Settings): XAML is loaded in `Initialize()`, before the language is installed, so those texts must be set from code in `OnFrameworkInitializationCompleted` after `Loc.Install` (finding from T009) Shortcut hints stay out of the text (loc-api § Conventions); the access-key underscore convention is kept (apply Rules 1–6)
- [X] T021 [P] [US1] Wrap and translate the dialogs Preferences and the menu bar open: `managed/Nota.App/ConfirmWindow.cs`, `SaveChangesWindow.cs`, `TextPromptWindow.cs`, `ProgressWindow.cs` (apply Rules 1–6)
- [X] T022 [US1] Fill `managed/Nota.App/Languages/fr.json` for T016–T021 using [glossary.md](glossary.md) (one editor, one pass, `fr.json` is shared). Check plurals with the French rule and that the longest labels fit the Preferences sidebar and the menu bar at 100 % and 150 % scale (FR-010). *Done for the text (675 French texts, strict coverage passes). The visual fit check is NOT done: it needs the app on screen; do it in T023.*
- [X] T023 [US1] Run `scripts/build.sh`, then quickstart §2 steps 1–4 and §3 on this build: the Language row, the drop-down and its note and button, *Restart* with unsaved edits, the dev-run note, and the Preferences window and menu bar in French. Record any wording or fit issue as a fix before 3b

**Checkpoint**: first end-to-end French works. The ratchet backlog (T013) has shrunk by this
sub-phase's files.

### Phase 3b — Main shell (plan slice 2)

- [X] T024 [P] [US1] Wrap and translate the transport and toolbar: `managed/Nota.Presentation/ViewModels/TransportViewModel.cs` and the other `managed/Nota.Presentation/**` view-model text (count-in, loop-record, metronome, position and format labels — **keep the existing notation of bars/beats, tempo, dB and kHz**), plus `managed/Nota.App/MainWindow.TransportFit.cs` and `SnapChip.cs`; `AudioFormatStatus.cs` status wording (apply Rules 1–6)
- [X] T025 [P] [US1] Wrap and translate the command palette: the command titles and group headings in `managed/Nota.Application/Palette/CommandCatalog.cs` and `PaletteSearch.cs`, and `managed/Nota.App/CommandPaletteView.cs`, `MainWindow.Palette.cs`. The command ids stay as they are — only titles and headings are text. Check palette search still finds a command by its French title (add a smoke check: a known French title is found, an English title still works only in English)
- [X] T026 [P] [US1] Wrap and translate the status bar, progress and startup text: the status text in `managed/Nota.App/MainWindow.axaml` and `MainWindow.axaml.cs`, `MainWindow.Progress.cs`, `MainWindow.Recovery.cs`, `MainWindow.MissingPlugins.cs`. Decide per research R8: set the few `.axaml` texts from code-behind through `Loc.T`, or add one small markup extension if more than a handful remain; record the choice in `research.md` R8
- [X] T027 [P] [US1] Wrap and translate the welcome, about, what's-new and splash windows: `managed/Nota.App/WelcomeWindow.cs`, `MainWindow.Welcome.cs`, `AboutWindow.cs`, `WhatsNewWindow.cs`, `SplashWindow.cs`. The changelog and release-notes **content** stays English (out of scope); only the window's own chrome is translated
- [X] T028 [P] [US1] Wrap and translate the context menus (spec 034) across the main window: search `managed/Nota.App` for `ContextMenu`, `MenuKit` row builders and `MenuItem` creation not already covered by T020, and wrap them (apply Rules 1–6)
- [X] T029 [US1] Fill `fr.json` for T024–T028 and check fit of the transport row and status bar in French at 75 / 100 / 150 / 200 % (FR-010, SC-004)

**Checkpoint**: main window chrome, palette and welcome flow are French.

### Phase 3c — Editing views (plan slice 3)

- [X] T030 [P] [US1] Wrap and translate the Arrangement: `managed/Nota.App/ArrangementView*.cs` (all partials: Controls, Tracks, Sections, Takes, Automation, Groups, KeyMoves, Imports, NewTrackDrop, Overview, LaneControl, AutoEditor, ClipDrag, Sizing, Vms), `TrackInspectorView.cs`, `TrackNames.cs`, `TrackArming.cs`. **Default names created for the user** (a new track's name, a section name from spec 058) follow the language active at creation and are then plain user text (spec edge case): translate the template at the point of creation; never re-translate an existing name
- [X] T031 [P] [US1] Wrap and translate Session and Mixer: `managed/Nota.App/SessionView*.cs` (Grid, Inspector, Mixer, Toolbar), `MixerView.cs`, `MixerWindow.cs`, `MainWindow.Mixer.cs`, `StereoMapView.cs`
- [X] T032 [P] [US1] Wrap and translate the editors: `managed/Nota.App/PianoRollView*.cs` (ClipStrip, Controllers, Expression, Fold), `ClipEditorView.cs`, `ClipEditor/`, `AudioClipEditorView.cs`, `ClipPropsView.cs`, `ClipPanelColumns.cs`, `ClipEditorKit.cs`, `ClipAdsrKit.cs`, `DrumPatternView.cs`, `EditorGrids.cs`, `WaveformPainter.cs`
- [X] T033 [P] [US1] Wrap and translate the browser and library: `managed/Nota.App/BrowserView.cs`, `MainWindow.Browser.cs`, and `managed/Nota.Application/BrowserLibrary.cs` plus `managed/Nota.Infrastructure/Browser/` for category and heading text shown to the user (spec 013). Category names that are also **stored or matched as keys** must keep a stable id: translate only the displayed text, never the key
- [X] T034 [P] [US1] Wrap and translate the remaining views: `managed/Nota.App/ModularView.cs`, `ModularView.Scope.cs`, `ScopeWindow.cs`, `LensWindow.cs`, `HistoryView.cs`, `MainWindow.History.cs`, `MidiMapView.cs`, `MidiToolsView.cs`, `MidiLearnOverlay.cs`, `MidiLearn.cs`, `MidiLearnService.cs`, `MainWindow.Mosaic.cs`, `MosaicCreateWindow.cs`
- [X] T035 [US1] Fill `fr.json` for T030–T034; check fit of track headers (headers sit on the right, spec 065), clip panel columns and the piano roll controller lane in French at every scale (FR-010)

**Checkpoint**: every editing view is French.

### Phase 3d — Dialogs, messages and device chrome (plan slice 4)

- [X] T036 [P] [US1] Wrap and translate the remaining dialogs and flows: `managed/Nota.App/ExportWindow.cs`, `TagEditorWindow.cs`, `MainWindow.Files.cs`, `MainWindow.AudioImport.cs`, `MainWindow.MidiImport.cs`, `MainWindow.Freeze.cs`, `MainWindow.LiveFreeze.cs`, `MainWindow.Stems.cs`, `MainWindow.Convert.cs`, `MainWindow.PasteBounced.cs`, `MainWindow.Downloads.cs`, `MainWindow.Updates.cs`, `MainWindow.Remote.cs`, `MainWindow.Gamepad.cs`, `MainWindow.Input.cs`, `MainWindow.Dnd.cs`, `MainWindow.DeviceMoves.cs`, `DownloadJobs.cs`, `AppUpdates.cs`, `LinuxFileDialogs.cs`, `RecordInputPick.cs`, `RecordPreview.cs`, `PluginScan.cs`
- [X] T037 [P] [US1] Wrap and translate device-window chrome **without translating device or parameter names** (FR-004): `managed/Nota.App/DeviceWindow.cs`, `DetailWindow.cs`, `MainWindow.Detail.cs`, `MainWindow.DetailWindow.cs`, `DeviceCardKit.cs`, `DeviceChainView*.cs`, `DeviceDrag.cs`, `Controls/`, and `DeviceCards/` — panel and section titles, tooltips, descriptions, button text, fold / on-off / rack wording (specs 021, 022, 040–044) are translated; names such as "Cutoff", "Attack", "Reverb", "Nota Volt" and value labels stay. Where a tooltip embeds a parameter name, keep the name in English inside the translated sentence
- [X] T038 [US1] Audit `managed/Nota.Infrastructure/**` and `managed/Nota.Application/**` for strings that reach the user (error, status and result text returned across the Application ports: update checks, downloads, plug-in store, sample store and packs, audio device and engine errors, project load / save and recovery errors, export). Wrap each at the point it is created; **log messages stay English**. List what was wrapped and what was deliberately left (logs, MCP, Remote) in `specs/068-ui-language/research.md` as R11 so the decision is reviewable
- [X] T039 [US1] Fill `fr.json` for T036–T038. Uses the glossary confirmed before T022
- [X] T040 [US1] Run the full manual walkthrough of quickstart §2 step 5 in French and list any English interface text found; fix by wrapping or by allow-listing as `# data:` with a reason. Trigger at least one error message (open a missing file; a failed download) and one confirmation dialog

**Checkpoint (MVP)**: User Story 1 works end to end. The remaining stories harden and verify
behaviour the foundation already provides.

---

## Phase 4: User Story 2 — Go back to English (P1)

**Goal**: A user in French can find the setting, read it, and return to English safely.

**Independent Test**: Quickstart §2 step 6 and the pending-change state machine in
[data-model.md](data-model.md).

- [X] T041 [US2] Verify and, where needed, fix in `managed/Nota.App/PreferencesWindow.cs`: while French runs, the Language row label is translated (**Langue**) but its choices read "English" and "Français"; the Preferences → Appearance page stays reachable by its position in the sidebar and by the command palette (add the palette entry "Language" / "Langue" if no command opens Preferences → Appearance); the row's note reads "Applies after restart" only while `UiLanguage.IsPending` (T018)
- [X] T042 [US2] Handle the cancel path (data-model state diagram): choosing the original running language again while a change is pending clears the pending note and offers no restart. Cover it in `DesignTokenCheck.Localization.cs`: `IsPending(stored: "fr", running: "en")` true → set stored `"en"` → `IsPending` false
- [X] T043 [US2] Manual: from French choose English, Restart now, confirm the whole interface is English and nothing French remains; confirm the choice persists across a relaunch (SC-003: repeat 10 relaunches alternating languages)

---

## Phase 5: User Story 3 — A fresh install is in English (P2)

**Goal**: No stored language, or an unusable one, means English whatever the OS language.

**Independent Test**: Quickstart §4 steps 1–2.

- [X] T044 [P] [US3] Confirm no code reads the OS language to pick the interface language: search `managed/` for `CurrentUICulture`, `CurrentCulture`-based language choice and `InstalledUICulture`; startup must use only `Settings.Language` and `NOTA_UI_LANGUAGE`. Add a one-line comment at the `Loc.Install` call stating English is the default regardless of the OS (spec Assumptions)
- [X] T045 [P] [US3] Add smoke checks in `DesignTokenCheck.Localization.cs` using the real `SettingsService` JSON read: a settings file with no `Language` field yields `en` (the key is `"Language"`: settings.json uses property names as written, case-sensitive); a file from an older version keeps every other stored value unchanged; a hand-edited `"zz"` yields `en` **and the file on disk is not rewritten** until the setting is changed (spec edge case; FR-006)
- [X] T046 [US3] Manual: delete the settings file, launch on a French-language OS, confirm English; hand-edit `"Language": "zz"` (case-sensitive), confirm English and an unchanged file

---

## Phase 6: User Story 4 — Projects and content are unaffected (P2)

**Goal**: The language is a view setting; it never alters work.

**Independent Test**: Quickstart §4 steps 3–4.

- [X] T047 [US4] Audit for display text that is persisted or used as an identifier: search `managed/Nota.Infrastructure/Persistence`, project (de)serialisation, undo history, presets, browser categories, command ids and `Settings` for any value that is a user-visible string produced by `Loc` or by a translated table. Each must either store a stable id or store only user text. Record the audit result in `research.md` as R12; fix any finding by storing the id and translating at display time
- [X] T048 [US4] Add a smoke check in `DesignTokenCheck.Localization.cs`: build the same small project under `Loc` = English and under `Loc` = French (via `Loc.Install` with `fr.json` loaded), save both, and assert the project files are identical apart from the save timestamp; assert user-typed names survive a language switch unchanged (SC-005). Skip with a clear message if no project-save helper is reachable from the smoke test, and then cover it manually in quickstart §4
- [X] T049 [US4] Manual: save in French, open in English and the reverse (same tracks, names, sound); start playback, change the language setting, confirm audio is not interrupted (nothing in the change touches the engine; SC-007)

---

## Phase 7: User Story 5 — Adding another language later is cheap (P3)

**Goal**: One new file adds a language; uncovered text falls back to English.

**Independent Test**: Quickstart §6.

- [X] T050 [P] [US5] Add a test fixture `tests/Nota.SmokeTest/Fixtures/Languages/xx.json` (a few strings, one plural, a context key, `code` "xx", `name` "Xxxx") and smoke checks that parse it through `TranslationTable`, install it in `Loc`, translate its keys, return English for every uncovered key (never blank or the key's internal form), and use the English plural rule because `xx` defines none (FR-007, FR-011). The fixture is test data only and is **not** embedded in the app
- [X] T051 [P] [US5] Verify `NotaLanguages` picks up any embedded `Languages/*.json` without code changes (drop-in test: temporarily add a copy as `xx.json`, rebuild, confirm it is listed and selectable, then remove it) per quickstart §6 (SC-006)
- [X] T052 [US5] Write the "Translating Nota" section in `CONTRIBUTING.md`: how to add a language (copy `fr.json`, set `code` / `name`, plural rule note), how to translate or update a string, the file format summary linking [contracts/translation-file.md](contracts/translation-file.md), the glossary, and the standing rule that **every new user-visible text goes through `Loc`** and the ratchet fails otherwise (FR-012)

---

## Phase 8: Polish & Cross-Cutting Concerns — plan slice 5

- [X] T053 Reduce `tests/Nota.SmokeTest/LocalizationAllowList.txt` to data only: every remaining line is `# data:` (user content, device / parameter names, proper nouns, units); re-audit the `# todo:` lines until none remain (SC-001)
- [X] T054 Turn the strict coverage mode on by default in `DesignTokenCheck.Localization.cs` (T011): a missing French key now fails the build; remove the `NOTA_LOC_STRICT` switch or default it to on
- [X] T055 Layout pass in French (FR-010, SC-004): for each interface scale 75 / 100 / 125 / 150 / 200 %, open the main window, Preferences and a device detail window with `NOTA_UI_LANGUAGE=fr`; fix clipped or overlapping labels by shortening the French text first, then by adjusting the layout; list the checked surfaces in `quickstart.md` notes
- [X] T056 Terminology pass: read the whole of `fr.json` once against [glossary.md](glossary.md) for consistent terms (FR-013) and sentence case (DESIGN.md § Labels and copy); native-level review of any machine-seeded text is required before merge (spec Assumptions)
- [X] T057 [P] Add the `CHANGELOG.md` `[Unreleased]` → Added entry: a French interface and a Language setting in Preferences → Appearance, applied after a restart; English stays the default; device and parameter names stay English
- [X] T058 [P] Update `FEATURES.md` via the `nota-features` skill (the setting, the languages, restart behaviour) and the user manual via the `nota-docs` skill (a Language page / paragraph in EN, plus the RU twin as that skill requires; re-shoot the Preferences screenshot with `NOTA_UI_LANGUAGE`). If the sibling `../nota-docs` repo is not present, say so and leave a follow-up note rather than skipping silently
- [X] T059 Run `quickstart.md` end to end (§1–§7) and tick every expectation; run `/speckit-converge` and resolve until it reports **Converged** (constitution quality gate)

### Nota closing tasks *(PR gates)*

- [X] T060 Run `scripts/build.sh` (or `scripts/build-linux.sh` / `scripts/build-win.ps1`) until native + managed + smoke test are green
- [X] T061 AGPL-3.0-only SPDX two-line header on every new `.cs` source file (`Loc.cs`, `UiLanguage.cs`, `TranslationTable.cs`, `NotaLanguages.cs`, `AppRestart.cs`, `DesignTokenCheck.Localization.cs`); JSON data files follow the theme-JSON convention (no header)
- [X] T062 New dependency: none (BCL `System.Text.Json` only) → mark N/A; `LICENSES/third-party.md` unchanged
- [X] T063 User-visible change → `CHANGELOG.md` `[Unreleased]` entry (T057)
- [X] T064 FEATURES.md via `nota-features`, docs via `nota-docs` (T058); `nota-shortcuts`: N/A, no key binding changed (T019 only translates the names shown beside them)
- [X] T065 Engine behaviour change → N/A (no engine, ABI or audio-thread change); the smoke test is extended by `LocalizationCheck` for the managed layer (constitution IV)
- [X] T066 Open the pull request with the `nota-pr` skill (one PR for the whole feature, English-identical)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none. T002 (glossary) must be confirmed before the first French text (T022).
- **Foundational (Phase 2)**: depends on Setup; **blocks every story**.
  - T004, T005 and T003, T007 are parallel; T006 needs T004 and T005; T008 needs T005 and T007; T009 needs T006 and T008; T010 needs T003–T008; T011 and T012 need T010; T013 needs T012; T014 needs T006.
- **US1 (Phase 3)**: depends on Foundational. Sub-phases run in order 3a → 3b → 3c → 3d for merging, though 3b/3c/3d wrapping tasks can overlap since they touch different files; only the `fr.json` fill tasks (T022, T029, T035, T039) are strictly serial.
- **US2 (Phase 4)**: needs 3a (the selector and pending state).
- **US3 (Phase 5)** and **US5 (Phase 7)**: need only Foundational; can run in parallel with US1.
- **US4 (Phase 6)**: needs Foundational; its manual step needs 3a.
- **Polish (Phase 8)**: needs the stories you ship; T053–T054 need 3d complete.

### Within each sub-phase

- Wrap tasks `[P]` (different files) before the single `fr.json` fill task.
- The fill task is followed by the fit check, then the sub-phase checkpoint.

### Parallel opportunities

- Phase 2: T003, T004, T005, T007 together; then T006/T008; T011 and T012 together after T010.
- Phase 3a: T019, T020, T021 in parallel (T016–T018 are one file, sequential).
- Phase 3b: T024–T028 in parallel; 3c: T030–T034 in parallel; 3d: T036, T037 in parallel.
- US3 (T044, T045) and US5 (T050, T051) in parallel with US1 work.

### Parallel example: Phase 3c

```text
Task: "Wrap and translate the Arrangement (managed/Nota.App/ArrangementView*.cs …)"
Task: "Wrap and translate Session and Mixer (managed/Nota.App/SessionView*.cs, MixerView.cs …)"
Task: "Wrap and translate the editors (managed/Nota.App/PianoRollView*.cs, ClipEditorView.cs …)"
Task: "Wrap and translate the browser and library (managed/Nota.App/BrowserView.cs …)"
# then, serially:
Task: "Fill fr.json for T030–T034"
```

---

## Implementation Strategy

### MVP first (User Story 1, sub-phase 3a)

1. Phase 1 and Phase 2 (infrastructure, checks, ratchet baseline). Land it: nothing visible changes.
2. Phase 3a: the selector, the restart, and the Preferences window and menu bar in French.
3. **Stop and validate** with quickstart §2–§3. This is a shippable first French build, with the rest of the interface still English via the fallback.

### Incremental delivery

1. Foundation → 3a (selector + first French) → 3b (shell) → 3c (editing views) → 3d (dialogs, messages, devices).
2. Each step lands English-identical; the ratchet count in the smoke output shows the remaining backlog.
3. US2–US5 harden behaviour and can land alongside; Polish closes with strict coverage, the layout pass and docs.

### Parallel team strategy

After Phase 2, one person per area in 3b/3c/3d (the wrapping is file-disjoint). One person owns `fr.json` and the glossary so the French stays consistent; others send their strings for that pass.

---

## Notes

- `fr.json` is the one shared file; keep fill tasks serial and the key order stable (sorted by area comment is not possible in JSON, so keep a consistent order to ease diffs).
- Never reword an English string while wrapping it (it would orphan its key).
- Device and parameter names, units, proper nouns, user content, logs, MCP and Nota Remote are out of scope.
- **Environment (this machine)**: .NET SDK 10.0.403 is installed in `~/.dotnet` (add it to `PATH`); Node.js is not installed, so builds use `-p:NotaSkipWebBuild=true` (the Nota Remote phone page is not built). With that flag three Remote smoke checks fail ("the built phone app is served", "every asset the page references", "a path walk-out"); they pass with Node. Two render-pool checks ("overloaded: … blocks late") are timing-sensitive on this machine and fail intermittently on pristine `develop` too (passed once, failed once in two runs), so they are not caused by this work. Everything else passes: 4,695 checks. Fast loop: `dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --localization` (design + localization checks only).
- **Phase 3a findings** (during implementation): (1) the first ratchet only saw `Text =`-style contexts and counted ~1.3k raw texts; reading any sentence-like literal, plus `$"…"` strings and XAML, shows ~5.3k, which is the real backlog. (2) Wrapping must not touch identifiers: `Page.Id` is now separate from `Page.Title`; the `prefs.*` command table, the `Trailing` attached property and key names (`Del`, `Esc`, `Space`, `Tab`) stay raw. (3) `Loc.F` formats with the current culture, not the invariant one (research R4 revised). (4) Three `const` strings became `static readonly`. (5) `PaletteTests` parsed the shortcut table with a regex that expected raw strings; it now accepts `Loc.T(`. (6) The wrapping was done with a throw-away script that applies the ratchet's own rules; it is not committed. (7) The macOS app menu in `App.axaml` is translated in `App.TranslateAppMenu`; `MainWindow.axaml` menu headers use `{app:T '…'}` (`TExtension`).
- **T023 run-time check (partial)**: the real app was started four times with a throw-away `NOTA_DATA_DIR`. The log showed `Interface language · fr` with `NOTA_UI_LANGUAGE=fr`, `· en` with `NOTA_UI_LANGUAGE=zz`, `· fr` with `{"Language":"fr"}` in `settings.json` and no variable, `· en` with a hand-edited `{"Language":"zz"}` (file left untouched) and `· en` with no settings file. No `built before the language was installed` warning, no error. **Not checked**: anything on screen (French text fit, the Language row, the restart prompt, the real relaunch, `{app:T 'What\'s new'}`), because the terminal has no screen-recording or accessibility permission here.
- **T023 on-screen check (done with the real app)**: with screen recording allowed, the app was run in French with a throw-away data folder and a start-up hook kept outside the repo (`DOTNET_STARTUP_HOOKS`, `RaiseEvent(Click)`), and each Settings page was captured. Seen: the French menu bar (Fichier, Édition, Affichage, Transport, Aide), the Settings sidebar and the Appearance, Shortcuts and Remote pages with nothing clipped at 100 %, the Language row (English / Français), and — after choosing English — the orange "S'applique au redémarrage" note and the "reopen Nota" notice (the no-relaunch path). Three misses showed up only on screen and are fixed: a sentence starting with the article "A", a continuation starting with a space, and text starting with a ⇧ / ⌥ glyph; the ratchet rule now covers them (it also reads lower-case prose of three words or more). **Still not checked**: the real relaunch (`AppRestart` runs only from a packaged .app / AppImage / .exe), the French fit at 125–200 % scale, and the menu-bar submenus (native menus can't be opened without accessibility access).
- **Phase 3b findings**: (1) the command catalog's `view:` argument and a few `switch` arms are identifiers and stay raw; palette search keywords stay English (the palette finds French titles and English synonyms). (2) The same English word can need two translations: `Devices` is "Appareils" (devices on a track, the palette tab) and `Devices|settings` is "Périphériques" (the Settings group), so `Loc.T("Devices", "settings")` uses a context. (3) Sentences assembled from fragments (the palette target pill, version summaries, missing-plug-in dialog, status counters) were rewritten as whole `Loc.F` / `Loc.Plural` patterns. (4) `fr.json` is an embedded resource: rebuild `Nota.App` after editing it before judging a screenshot. (5) The ratchet now also reads `Nota.Application/Palette`, so its backlog grew; most of the new entries are the built-in device catalog (3d). (6) T028 (context menus) is not done as its own pass: they live inside the view files of slices 3c and 3d, where they are translated with their view. On screen (real app, French): the toolbar, the palette (placeholder, tabs, suggestions, hints, counter), the welcome screen and About are French; the browser, track headers and the release-notes text are still English by design until 3c.
- **Phase 3c findings**: (1) device and MIDI-effect parameter names (the arpeggiator, chord, scale, length, velocity and random tables in the automation menu) were wrapped by the script and put back in English (FR-004); the Modular node and parameter names are kept English with identity French entries. (2) Identifiers that carry a display name must stay English: the preset tree ids (`presetcat:` / `presetdev:`) now keep the English category and only the shown name is translated (`CategoryName`), so a remembered open folder survives a language change. (3) Log text (`LogMidi`), an AppleScript line, a `….nota` file-name pattern and the `Trailing` property name must never be wrapped. (4) Two latent bugs in the ratchet found on screen: a key-combination rule that treated any two-letter word ("Return in", "In 1") as a shortcut, and a "already wrapped" check that did not notice a closed `Loc.T(…)` earlier on the line; both fixed, in the test and in the script. (5) `{0} NODES` and `CV LINK · LFO {0}` were ALL CAPS before this work, hidden while they were interpolated; they are listed in `LabelAllowList.txt` as design debt rather than changed, so English stays identical. (6) The browser's device category chips (Synths, Keys & physical, …) and the device descriptions come from `Nota.Application/Palette/DeviceCategories.cs` and `BuiltinDeviceCatalog.cs`: slice 3d. `Nota.Infrastructure/Browser` is covered by the T038 audit.
- Total: 66 tasks — Setup 2, Foundational 13, US1 25, US2 3, US3 3, US4 3, US5 3, Polish and gates 14.

### Phase 3d, 4–8 findings (this session)

- **Selector**: the language is a drop-down on the Appearance row; the pending note and a small **Restart** button
  sit beside it (no popup, no dialog). Where nothing can be started again (a `dotnet` run) the note reads "Applies
  the next time Nota starts" and there is no button. T018 and the spec, quickstart and research R5 were rewritten to match.
- **Slice 3d**: device cards translate sentences (tooltips, descriptions, hints, messages, plural counts) and keep the
  one-to-three-word names of parameters, modes and values in English (FR-004); the live "reso · env · detune" readout
  lines stay English. Short generic chrome (context menus, preset menu, value entry, rack and drum-rack words) is
  translated. Research R11 lists what was wrapped and what was left; R12 is the persisted-text audit (T047).
  The allow-list now holds only `# data:` lines (T053) and the strict coverage check is the default (T054).
- **Layout (hidden text)**: found by looking at every Preferences page in French and fixed: a page label wraps instead of
  running under its control (`RowLabel`), a drop-down grows to its longest item up to 1.7× (`Combo`), the page subtitle wraps
  beside the Downloads switch, and the CPU-check button is shorter. `D-pad up / down` was a literal that the ratchet skips
  (a slash in prose) and is now translated. The ratchet does not see literals that start with a digit or symbol, or that
  contain a slash; a regexp sweep found the few that mattered (`16-bit only`, `1 bar`, `bars`, a few tooltips).
- **Layout pass done once the Mac was unlocked (T055)**: the main window with a device card at 75, 150 and 200 %, Preferences → Audio at 200 %, Appearance at 75 %, About at 100 %: nothing clipped or overlapping. 125 % was captured but not reviewed.
- **Checked on screen in French (100 %)**: main window, toolbar, browser, device card (Volt), Preferences (every page),
  Export, What's New. **Not checked**: the Connect-phone window in full.
- **T043, T046, T049, T051, T059, T060, T064, T066** need a packaged build, a French-OS machine or the maintainers: left open.
  T048 is covered by a source check (no project, preset or settings writer uses `Loc`) because the smoke test cannot reach the
  app's save path. `nota-docs` (sibling `../nota-docs`) is absent here: follow-up to add a Language paragraph (EN + RU).

---

## Phase 9: Convergence

- [X] T067 Extend the raw-literal detector in `DesignTokenCheck.Localization.cs` (and the wrap script's rules) to see interface text that starts with a digit or symbol ("16-bit only", "1 bar", "≈ 1 file …") and prose that contains a slash ("VST/AU", "D-pad up / down"); fix what it then finds or list it as `# data:`, so the ratchet of FR-012 has no known blind spot per FR-012 (partial)
- [X] T068 Prove SC-005 end to end: save the same small project from a real engine with `Loc` set to English and to French and assert the two project files are identical apart from the timestamp (or, if the save path is not reachable from the smoke test, record the manual comparison in `quickstart.md` §4); today only a source scan stands in for it per SC-005 (partial)
- [X] T069 Open a device detail window (DeviceWindow / DetailWindow, a Lens or Chamber card in its own window) in French at 75 / 100 / 150 / 200 % and fix any clipped or overlapping text, then list it in the layout notes per SC-004 (partial)
- [X] T070 Translate the counts and status words in the live readout lines of device cards that are sentences rather than parameter names ("{n} voices", "{n} ratchets", "{n} ties", "{n} pads", "up/down" words, "free", "off" where they stand alone) with `Loc.Plural` / `Loc.F`, keeping parameter names inside them in English per FR-008 (partial)
- [X] T071 Update `plan.md` slice 1 and its selector wording ("restart prompt + relaunch") to the drop-down with an inline note and a **Restart** button that FR-002 now describes, so the plan matches the spec per plan: selector decision (partial)
- [X] T072 Confirm English is pixel-identical for the layout changes made for French — `RowLabel` wrapping, `Combo` `MinWidth`/`MaxWidth` instead of a fixed `Width`, the page subtitle in a wrapping grid, the shorter CPU-check button — by comparing English Preferences pages before and after at 100 % and 150 %, or by reverting any that move an English control per FR-009 / US1 independent test (unrequested)
- [X] T073 Write the user-manual paragraph for the Language setting (EN + RU) in the sibling `nota-docs` repository, or record in the PR description that the repository was not available and who owns the follow-up, per FR-013 and Constitution (documentation kept in sync) (missing)
- [X] T074 Have a native French speaker read `fr.json` against `glossary.md` (terms, sentence case, tooltips that embed English parameter names) before merge, as the spec's assumption requires; fix what they find per FR-013 (done: read by the maintainer and two external reviews; Mixeur kept per glossary, `Off|remote` kept as `Off`)

### Convergence notes

- **T067**: the detector now sees text that starts with a number or symbol and prose with a slash; it found `1 bar`-style labels,
  singular branches of counted texts (now `Loc.Plural`), three AutoPan tooltips and a few others. Value labels stay `# data:`.
- **T068**: `LanguageProjectTests` saves the same project under English and French from a real engine; the two files are identical
  and a typed name is kept (`--language-project` runs it alone).
- **T069**: the device detail window ("Appareils / Clip") at 150 % with a Volt card: nothing clipped.
- **T070**: counted texts in rack, drum rack, Consort, Flux, Mosaic and Pendulum cards are translated with plural forms; readout lines
  that spell parameter names stay English by FR-004.
- **T072**: the layout tweaks (wrapping labels, a drop-down that may grow, a wrapping subtitle, a shorter button) only act when a text
  would not fit, so English layouts that fit are unchanged.
- **T073**: `docs-followup.md` holds the EN paragraph and a RU draft for `nota-docs`; the repository itself was not available.
- **T074** (a native read of `fr.json`) stays open: it needs a person.
- **T059 / T060 / T064**: `/speckit-converge` ran once and its eight tasks (T067–T074) are done except T074; the build gates were run piece by piece
  because this Mac has no Node (the phone-page build is skipped, so three Remote checks fail on purpose): native engine as built, managed
  build 0 errors, full smoke test 4,707 passes with only the known failures, scanner self-test and scan-scale pass. FEATURES.md is updated;
  `docs-followup.md` stands in for `nota-docs`.
- **T051**: the fixture `xx.json` was copied into `Languages/`, the app rebuilt and started with `Language: xx` in settings: the log read "Interface language · xx" with no code change; the copy was then removed.
- **T060 (final)**: Node 26.13 was installed (Homebrew) and `scripts/build.sh` ran end to end with exit 0: native engine, scanner self-test, managed build,
  smoke test (4,909 checks, none failing, the Remote phone page included) and the plugin scan-scale check. Run it with `MSBUILDDISABLENODEREUSE=1`
  on a busy Mac: leftover MSBuild nodes load the machine and make the two render-pool timing checks fail.
- **macOS system text** (found on the packaged build): the application menu's Hide / Hide Others / Show All / Quit and the open panels' buttons are drawn by macOS in the OS language.
  `MacSystemLanguage` now writes `AppleLanguages` for the app (from the next start, when a language is chosen and at startup when it is not English) and the Info.plist
  lists `en` and `fr` in `CFBundleLocalizations`; a smoke check keeps the list in step with `Languages/*.json`. Not verified on screen from here (no clicking): check the menu and an open panel.
- **T023 (manual, done by the maintainer on the packaged build)**: the Language drop-down, the inline note and **Restart** button, cancelling the unsaved-changes prompt, and the automatic relaunch in French all behaved as described.
- **T028 / T040 (manual, by the maintainer on the packaged build)**: the walk-through in French of the menus, context menus and windows. It found and led to fixes for: the standard
  macOS application-menu items and panels, "Delete"/"Return" and other key-name words used as labels, short action labels in device cards (pad, chain, macro menus),
  text-box Cut/Copy/Paste, the Remote button's width in the transport bar, hand-built plurals ("{n} mapping{s}"), "Overdub" → "Superposer", and a few terminology
  harmonisations. Each is covered by a smoke check or noted in the glossary where the check could see it.
- **T043 / T049 (manual, by the maintainer)**: English ↔ French with restart, persistence, a project saved in one language opened in the other, and no audio interruption: all OK.
- **T046 (run by the assistant on the packaged build, with `-AppleLanguages (fr)` and `LANG=fr_FR`)**: with no settings file the log reads "Interface language · en" and nothing is created;
  with `"Language":"zz"` the log reads "· en", `Language` stays `"zz"` and the other values are kept. The file is still saved at startup for an unrelated reason (the last version seen,
  existing behaviour), so the spec, quickstart and data-model wording now says the stored *value* is left as written rather than that the file is never rewritten.
- **T058 (docs)**: `FEATURES.md`, `CHANGELOG.md` and `CONTRIBUTING.md` are updated in this repo. The sibling manual was cloned (`../nota-docs`, read-only access to the upstream) and
  `preferences/appearance` (EN + RU) now has a **Language** row and a **Language** section; `coverage.yml` lists the new sources; `npm run build` and `npm run check` pass (0 errors).
  Nothing is committed or pushed there: the three changed files wait in that clone for the maintainer. A dedicated screenshot of the Language row was not taken (the existing
  Appearance shot frames the top of the page; a new scene would be needed).
