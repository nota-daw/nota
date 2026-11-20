# Tasks: Remap one controller function without redoing the wizard

**Input**: `specs/110-controller-remap-function/` — plan.md, spec.md, research.md, data-model.md,
contracts/mapping-editor.md, quickstart.md

**Tests**: included — the pure edit model is covered in the smoke test (plan § Testing).

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Add the editor's strings (`controllers.edit.*`: title, origin lines, back to shipped + confirm, learn again, unassigned only, all, other, listening prompts, conflict line, move here, find-by-touch status, + Command…, search placeholder, revert / clear / remove tips, nothing-changed) and `preferences.controllers.edit_mapping` (+ tip) to `src/managed/Nota.Application/Localization/en.json`

---

## Phase 2: Foundational (blocks all stories)

- [X] T002 [P] Create `FunctionKey` (`Of(ControlRole)`, `LeftOf(ControlRole)`), physical-control comparison (same channel and: same message + number; CC n vs CC14 n / n+32; both pitch bend; NRPN by number) and `MappingEntry(ControlDecl Decl, JsonObject? Json)` in `src/managed/Nota.Application/Controllers/ProfileMapping.cs`
- [X] T003 Implement `ProfileMapping` (load from `ControllerProfile` + original JSON text, pairing each decl with its JSON object by order of valid entries; `ControlOf`, `IsChanged`, `HasChanges`, `Try` → `Conflict`, `Assign(key, ClassifiedControl, leftKey?)`, `AssignHalf`, `Clear`, `Revert` → `Conflict?`, `ReplaceAll`, `Find(status, ch, d1, d2)`) in `src/managed/Nota.Application/Controllers/ProfileMapping.cs`
- [X] T004 `ProfileMapping.ToJson(originalJson)`: original document with only `controls` rebuilt — unchanged entries verbatim, new entries via a shared entry writer extracted from `ProfileWriter` (`ProfileWriter.WriteControl(JsonObject, LearnedControl)`), inheriting `feedback` / `motorised` from the original entry of the same physical control; command rows with no control dropped — in `src/managed/Nota.Application/Controllers/ProfileMapping.cs` and `ProfileWriter.cs`
- [X] T005 [P] Extract the wizard's function list into `SetupFunctions` (group enum, single / pair / slot, name, prompt, picture, icon, word, right / left `FunctionKey`, command; `ForCommand(id)` for + Command… rows; `GroupName` / `GroupTitle`) in `src/managed/Nota.App/Controllers/SetupFunctions.cs`
- [X] T006 [P] Extract the learn heuristics (event buffer, still windows 350 / 600 ms, `PressedAndReleased`, `LooksLikeEncoder`, `Raw`, `Describe`, `Short`, classify → `Result`: none / single / ambiguous / pair-button) into `ControlCatcher` in `src/managed/Nota.App/Controllers/ControlCatcher.cs`
- [X] T007 Refactor `ControllerSetupWindow` to build its steps from `SetupFunctions` and use `ControlCatcher` helpers; behaviour unchanged, in `src/managed/Nota.App/Controllers/ControllerSetupWindow.cs`
- [X] T008 [P] `ControllerService`: `ProfileJson(ControllerProfile)` (embedded resource or user file text), `HasShipped(string id)`, `ShippedFileName(id)`, `RemoveUserProfile(string fileName)` (delete + reload) in `src/managed/Nota.App/Controllers/ControllerService.cs`
- [X] T009 [P] Smoke tests for the model: single assign changes one entry; nothing changed → JSON unchanged; init / release / feedback / `source` survive; feedback follows the physical control — in `tests/Nota.SmokeTest/ControllerRemapTests.cs` (+ register in `tests/Nota.SmokeTest/Program.cs` if tests are listed there)

**Checkpoint**: model + catalog ready; the wizard still works as before.

---

## Phase 3: User Story 1 — Reassign one function (P1) 🎯 MVP

**Independent test**: wizard-made profile → editor → Undo → press a button → Finish → only Undo's entry changed; restart keeps it.

- [X] T010 [US1] `ControllerMappingWindow` skeleton: header (controller name, profile origin line), group strip with counts + "All", live readout, scrolling grouped list of `SetupFunctions` rows (icon, name, control chip(s), changed dot), footer (Cancel, status, Finish primary) per contracts/mapping-editor.md in `src/managed/Nota.App/Controllers/ControllerMappingWindow.cs`
- [X] T011 [US1] Listening: click / Enter starts it on a row (solid accent bar + soft tint, prompt), capture + `ControlCatcher` poll, learned → `ProfileMapping.Assign`, ambiguous encoder → inline encoding combo + Use, pair rows (encoder both halves; button → first then second button), Esc / click again stops; after a learn stop and keep the row selected (FR-006a) in `ControllerMappingWindow.cs`
- [X] T012 [US1] Finish: no changes → close; else `ToJson` → `SaveUserProfile(file name)` (user profile's own file) and keep the controller's choice; Cancel / close with changes asks first (FR-011) in `ControllerMappingWindow.cs`
- [X] T013 [US1] Preferences: Set up opens the editor for a generic profile ("Edit mapping…"), the wizard for none in `src/managed/Nota.App/PreferencesWindow.Controllers.cs`
- [X] T014 [US1] Keyboard: ↑ / ↓ selection, Enter listen, Esc stop / cancel, Cmd/Ctrl+Enter Finish (FR-017) in `ControllerMappingWindow.cs`

---

## Phase 4: User Story 2 — Move a control already in use (P1)

**Independent test**: Stop ← Play's button → prompt → Move → Play "—", Stop has it.

- [X] T015 [P] [US2] Tests: conflict reported before change; Move removes the other entry; Move from a two-way encoder clears both halves; Cancel leaves both — in `tests/Nota.SmokeTest/ControllerRemapTests.cs`
- [X] T016 [US2] Inline conflict line under the row ("<control> drives <function>." + Move here / Cancel, Enter = Move, Esc = Cancel) in `ControllerMappingWindow.cs`

---

## Phase 5: User Story 3 — Edit a shipped profile safely (P1)

**Independent test**: shipped KeyLab DAW → assign Metronome → Finish → same-id file in user folder, init / LED kept, overrides after reload; Back to shipped removes it.

- [X] T017 [P] [US3] Tests: shipped edit saved under the shipped file name with same id overrides the shipped profile after `ReloadProfiles`-equivalent load; init + feedback kept — in `tests/Nota.SmokeTest/ControllerRemapTests.cs`
- [X] T018 [US3] Save shipped edits to `ShippedFileName(id)` in the user folder; header origin lines (shipped / your copy / yours); **Back to shipped profile** with confirmation → `RemoveUserProfile`, then reload the window's mapping in `ControllerMappingWindow.cs`

---

## Phase 6: User Story 4 — Clear a function (P2)

- [X] T019 [P] [US4] Tests: Clear removes the entry (two-way keeps the other half); Revert restores; Revert refuses when the control moved elsewhere — in `tests/Nota.SmokeTest/ControllerRemapTests.cs`
- [X] T020 [US4] Row actions: clear ×, revert ↺ (hover / selected), Delete / Backspace clears in `ControllerMappingWindow.cs`

---

## Phase 7: User Story 5 — Find what a control does (P2)

- [X] T021 [US5] Idle capture → `ProfileMapping.Find` → select + scroll + flash the row; unmapped → status line (FR-010) in `ControllerMappingWindow.cs`
- [X] T022 [US5] "Unassigned only" filter; with it on, a learn moves listening to the next unassigned row (FR-006a, FR-018) in `ControllerMappingWindow.cs`

---

## Phase 8: User Story 6 — Any command on a spare button (P2)

- [X] T023 [P] [US6] Tests: a command row assigned → entry `role: command, command: <id>` round-trips through `ProfileLoader`; a command row without a control isn't written — in `tests/Nota.SmokeTest/ControllerRemapTests.cs`
- [X] T024 [US6] "+ Command…" row at the end of Extra: flyout with search box + list of `CommandCatalog` commands (title — category), excluding ids already shown and `view.palette`; pick → new row, listening; existing command entries not in the catalog list shown as rows; remove action on added rows, in `ControllerMappingWindow.cs`

---

## Phase 9: User Story 7 — Learn everything again (P3)

- [X] T025 [US7] Seeded wizard: optional `ProfileMapping` seed pre-fills `_learned`; a seeded step shows the current control with Next = keep (Enter); a control seeded on a later step is taken, not "already learned"; seeded Finish returns the learned set instead of saving, in `src/managed/Nota.App/Controllers/ControllerSetupWindow.cs`
- [X] T026 [US7] Editor "Learn everything again" → seeded wizard dialog → `ReplaceAll` → save → refresh rows; wizard Cancel → editor unchanged, in `ControllerMappingWindow.cs`
- [X] T027 [P] [US7] Test: `ReplaceAll` with the seed's own controls → JSON unchanged; keeps Other + command rows — in `tests/Nota.SmokeTest/ControllerRemapTests.cs`

---

## Phase 10: Polish & Cross-Cutting

- [X] T028 Edge cases: controller disconnected while open (status, listening stops); MCU / HUI never reach the editor; "Other" group read-only at the end, in `ControllerMappingWindow.cs`
- [X] T029 Visual check in all four themes via a headless harness (screenshots: idle, listening, conflict, + Command flyout, shipped header) — `/private/tmp/n973harness`
- [ ] T030 Hand check on the KeyLab Essential 61 mk3 (quickstart.md steps 1–9)

### Nota closing tasks

- [X] T031 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T032 AGPL-3.0-only SPDX two-line header on every new source file
- [X] T033 New dependency: N/A
- [X] T034 `CHANGELOG.md` `[Unreleased]` entry (Added: mapping editor; Changed: Set up opens it)
- [X] T035 FEATURES.md via `nota-features`; Controllers page via `nota-docs` (EN + RU); shortcuts N/A (window-local keys)
- [X] T036 Engine behaviour change: N/A
- [X] T037 RU strings for `controllers.edit.*` in nota-translations

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 (T002 → T003 → T004; T005, T006, T008 parallel; T007 after T005 + T006).
- US1 (Phase 3) needs Phase 2. US2–US7 build on the US1 window (same file) — sequential.
- Tests (T009, T015, T017, T019, T023, T027) need only T002–T004 and can be written early.

## Parallel Example

```text
T005 SetupFunctions.cs  |  T006 ControlCatcher.cs  |  T008 ControllerService.cs  |  T009 tests
```

## Implementation Strategy

MVP = Phases 1–5 (reassign, conflicts, shipped copy — the issue's acceptance). Then clear, find by
touch, commands, seeded wizard, polish.
