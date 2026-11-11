---
description: "Tasks: Settings reorganisation (#427)"
---

# Tasks: Settings reorganisation — every setting where you'd look for it

**Input**: `specs/088-settings-reorganisation/` — plan.md, spec.md, research.md, data-model.md,
contracts/settings-pages.md, quickstart.md

**Tests**: one smoke check (page ids ↔ palette commands), per plan; the rest is the build gate
and the visual walk-through in quickstart.md.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Confirm branch `feat/settings-reorganisation` builds before changes: `dotnet build src/managed/Nota.App` (baseline)

---

## Phase 2: Foundational — pages by id (blocks every story)

- [X] T002 In `src/managed/Nota.App/PreferencesWindow.cs` add `IndexOf(string id)` (unknown id → 0) and `Select(string id)`; make `ShowPage(id)` use it; `BuildPane(int)` switches on `Pages.ElementAt(index).Id` instead of positions; the Downloads branch in `Select(int)` and `UpdateDownloadsBadge` compare/use `IndexOf("Downloads")`
- [X] T003 Remove the positional constants: `RemoteIndex` in `PreferencesWindow.Remote.cs` (`ShowRemote` → `Select("Remote")`), `DownloadsIndex` in `PreferencesWindow.Downloads.cs`, `UpdatesIndex` in `PreferencesWindow.Updates.cs`; `OnAudioDeviceRestarted` rebuilds when the selected id is `Audio`, `Outputs` or `Inputs` (`PreferencesWindow.cs`)
- [X] T004 Rewrite the `Nav` table in `PreferencesWindow.cs` to the contract order — Devices: `Audio`, `Outputs`, `Inputs`, `MIDI`, `Gamepads` · Remote control: `Remote`, `MCP` · Plug-ins: `Plug-ins`, `Downloads` · General: `Appearance`, `Language`, `Editing`, `Library`, `Shortcuts`, `Updates` — with icons Speaker / Input / Action / Globe / Edit (R5) and the subtitle keys of contracts/settings-pages.md
- [X] T005 [P] Add the `Globe` path glyph to `src/managed/Nota.App/Controls/Glyph.cs` (24-unit `Paths` style, stroke like `Appearance`)
- [X] T006 [P] Add the new keys of contracts/settings-pages.md to `src/managed/Nota.Application/Localization/en.json`; remove `preferences.theme_scale_waveforms_and_ai` once nothing uses it

**Checkpoint**: window opens; every page id resolves (new panes may still be placeholders).

---

## Phase 3: User Story 1 — find a setting on the page its name suggests (P1) 🎯 MVP

**Goal**: MCP, Language and Editing get their own pages; Appearance holds only the look.
**Independent test**: quickstart steps 1, 5–8.

- [X] T007 [P] [US1] Create `src/managed/Nota.App/PreferencesWindow.Mcp.cs` (SPDX header) with `McpPane()` — the MCP block moved verbatim from `AppearancePane` (port box, status, Copy config, `ApplyMcp`, `Refresh`), one `Section(Loc.T("preferences.ai_control_mcp"), 12, enable, details)`
- [X] T008 [P] [US1] Create `src/managed/Nota.App/PreferencesWindow.Editing.cs` (SPDX header) with `EditingPane()` — sections Piano roll (label every note + caption), Arrangement (`preferences.arrangement`: fade audio clips when splitting + caption, still sets `_main.Engine.SplitFades`), Detail panel (open on select + caption)
- [X] T009 [P] [US1] Add `LanguagePane()` to `src/managed/Nota.App/PreferencesWindow.Language.cs`: one section with `LanguagePicker()` and its caption `preferences.the_language_of_menus_panels`
- [X] T010 [US1] Reduce `AppearancePane()` in `PreferencesWindow.cs` to colour theme (+ follow-system caption), accent, waveform, track headers, interface scale, text size, each with its caption; wire `MCP` / `Language` / `Editing` ids in `BuildPane`

**Checkpoint**: US1 independently testable.

---

## Phase 4: User Story 2 — inputs and outputs on their own pages (P1)

**Goal**: Audio = format, latency, CPU check; Outputs = output device (+ ASIO output pair) and test tone; Inputs = input device (+ ASIO input pair) and the spec-086 list.
**Independent test**: quickstart steps 2–4, 11.

- [X] T011 [US2] Split `AudioPane()` in `PreferencesWindow.cs`: drop the output/input combos and `InputsHost`; keep audio system, ASIO driver row (+ Driver settings…), sample rate, buffer, exclusive (Windows), latency; Test section keeps only `CpuCheckRow`. Split `AsioRows` so the driver/rate/buffer rows stay and the output / input pair rows become helpers used by the new panes
- [X] T012 [US2] Add `OutputsPane()` to `src/managed/Nota.App/PreferencesWindow.AudioIo.cs`: non-ASIO → `Row(common.output, outputCombo)` (same `FillDeviceCombo` / `SetAudioOutputDevice` / `Apply`); ASIO → driver note row (`preferences.asio_device_on_audio` with the driver name + `preferences.change_on_audio_page` button → `Select("Audio")`) and, when running, the output pair combo; then a Test section with `TestToneRow()`
- [X] T013 [US2] Add `InputsPane()` to `PreferencesWindow.AudioIo.cs`: non-ASIO → input combo (`SetAudioInputDevice` / `Apply`); ASIO → the driver note row and, when running, the input pair combo or `preferences.no_inputs_on_this_driver`; then `InputsHost(engine)`; header comment updated (Inputs now its own page)
- [X] T014 [US2] Make `_latencyValue`, `_latencyDetail` and `_inputsHost` reset to null in `Select(int)` so `Apply()`/`UpdateLatency()`/`RebuildInputs()` touch only controls of the page shown (`PreferencesWindow.cs`, `PreferencesWindow.AudioIo.cs`); `_main is null` on Outputs/Inputs shows the same "unavailable" caption as Audio

**Checkpoint**: US2 independently testable.

---

## Phase 5: User Story 3 — entry points land on the right page (P2)

**Goal**: every caller opens the page that now holds its content.
**Independent test**: quickstart steps 9–10.

- [X] T015 [P] [US3] Add `prefs.outputs`, `prefs.inputs`, `prefs.mcp`, `prefs.language`, `prefs.editing` to `src/managed/Nota.Application/Palette/CommandCatalog.cs` with the R7 keywords; move "language langue francais english" off `prefs.appearance`
- [X] T016 [US3] Bind the five commands to their page ids in `src/managed/Nota.App/MainWindow.Commands.cs`
- [X] T017 [US3] Record-input menu "Audio I/O setup" → Inputs: add `ShowInputPreferences()` (`ShowPreferencesPage("Inputs")`) in `src/managed/Nota.App/MainWindow.AudioFormat.cs`, call it from `src/managed/Nota.App/ArrangementView.cs`; fix the `ShowAudioPreferences` doc comment (format menu → Audio)
- [X] T018 [US3] No-output status text names Outputs: new keys in `en.json`, used in `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs`; drop the old keys
- [X] T019 [US3] Smoke check in `tests/Nota.SmokeTest/PaletteTests.cs`: every `prefs.*` page id bound in `MainWindow.Commands.cs` appears as a `new Page("<id>"` in `PreferencesWindow.cs`, and every page id has a `prefs.*` command

---

## Phase 6: Polish & cross-cutting

- [X] T020 Grep for leftover references: `ShowPage("`, `Select(` with literals, `preferences.theme_scale_waveforms_and_ai`, `RemoteIndex|DownloadsIndex|UpdatesIndex`; `grep` en.json for "Settings → Appearance"/"→ Audio" texts that now mean another page
- [X] T021 Build gate: `scripts/build.sh` passes (smoke test incl. T019 and palette checks)
- [X] T022 Visual check with the headless-app harness: every page EN, Studio Dark; Russian once RU strings exist; nothing clipped in the sidebar
- [X] T023 [P] `CHANGELOG.md` `[Unreleased]` → Changed entry (Settings reorganised, #427)
- [X] T024 [P] FEATURES.md via the `nota-features` skill
- [ ] T025 [P] Russian strings for the new keys in `../nota-translations/languages/ru.json` (separate repo/PR; remove the dropped keys there too)
- [X] T026 nota-docs EN + RU via the `nota-docs` skill: split `preferences/audio.mdx` into audio / outputs / inputs, `preferences/appearance.mdx` into appearance / language / editing, new `preferences/mcp.mdx`; fix links in `ai/mcp`, `views/detail-panel`, `editing/audio-clips`, `editing/time-ranges`, `views/arrangement`, `library/preview-player`, `control/nota-remote`; reshoot Settings screenshots (separate repo/PR)

---

## Dependencies & execution order

- Phase 2 blocks everything (T002–T004 sequential in one file; T005, T006 parallel).
- US1 (T007–T010) and US2 (T011–T014) are independent of each other; T010 and T011 both edit `PreferencesWindow.cs` — do them one after the other.
- US3 depends on the ids from Phase 2 only; T015 parallel with US1/US2.
- Polish after all stories; T023–T025 parallel.

## Parallel examples

- US1: T007, T008, T009 (three different files) together, then T010.
- US3: T015 alongside any US1/US2 task.

## Implementation strategy

MVP = Phase 2 + US1 (the issue's original ask). Then US2 (Audio split), US3 (entry points),
polish, docs/translations in their repos.
