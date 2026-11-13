---

description: "Tasks for 096 custom keyboard shortcuts"
---

# Tasks: Custom keyboard shortcuts

**Input**: Design documents from `specs/096-custom-shortcuts/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/actions.md,
contracts/ui-contract.md, quickstart.md

**Tests**: Smoke-test checks are part of the Nota PR gate (constitution IV), so the binding
logic gets `KeyBindingTests` in `tests/Nota.SmokeTest/`.

**Organization**: grouped by user story. Paths are repo-relative.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4 from spec.md

---

## Phase 1: Setup

- [X] T001 Add `KeyBindings` (`Dictionary<string, KeyBindingOverride>`) and the `KeyBindingOverride { string? Primary; string? Alternative; }` class ("null = default, \"\" = unassigned, else glyph text") to `src/managed/Nota.Application/Settings.cs`

---

## Phase 2: Foundational (blocking)

- [X] T002 [P] Create `KeyScope` enum (Global, Arrangement, Notes, Session, PianoRoll, AudioEditor, Devices, Browser, Modular) with `IsWindowLayer` (Global | Arrangement | Notes) in `src/managed/Nota.Application/Input/KeyScope.cs`
- [X] T003 [P] Create `KeyAction` (Id, Title, Group, Scope, DefaultPrimary, DefaultAlternative, IsMenuCommand, NoMenuKey, Fixed, Aliases) in `src/managed/Nota.Application/Input/KeyAction.cs`
- [X] T004 Create `KeyActionCatalog.Create()` in `src/managed/Nota.Application/Input/KeyActionCatalog.cs`: import every `CommandCatalog` command (gesture → default, scope from `View`/id per contracts/actions.md, roll.* → PianoRoll, session.insertScene → Session), give the shared actions their defaults (edit.copy ⌘C, edit.cut ⌘X, edit.paste ⌘V, edit.delete ⌫ + alt ⌦, NoMenuKey on copy/cut/paste/delete), add `edit.cancel` (Esc) and every *(new)* action of contracts/actions.md with per-platform defaults; `file.exit` Fixed on macOS
- [X] T005 [P] Create per-platform `ReservedKeys` (research R5) with `Reason(Shortcut, platform)` in `src/managed/Nota.Application/Input/ReservedKeys.cs`
- [X] T006 Create `KeyBindings` service in `src/managed/Nota.Application/Input/KeyBindings.cs`: load overrides from settings (skip unknown ids / unparsable glyphs with a log line), effective map, `Current`, `IsEdited`, `Matches`, `Resolve(scope, chord)`, `FindConflicts` (rule R3 incl. default-default exemption), `FindShadows`, `Validate` (reserved / bare modifier), atomic `Apply` (set + Replace), `Reset`, `ResetAll`, `NeedsDecision`, `Changed`, persist via `ISettingsService`
- [X] T007 [P] Create `KeyTokens` (Avalonia `Key` ⇄ token, keypad digits → digit, `FromKeyEvent(KeyEventArgs) → Shortcut?`, platform modifier mapping) in `src/managed/Nota.App/Input/KeyTokens.cs`; route `ToGesture` through it in `src/managed/Nota.App/Input/ShortcutAvaloniaExtensions.cs`
- [X] T008 Create the app facade `Keys` (`Is(e, id)`, `Resolve(e, scope)`, `Capturing`, `Bindings`) in `src/managed/Nota.App/Input/Keys.cs`; register and load `KeyBindings` at startup (DI in `src/managed/Nota.App/App.axaml.cs` or where `ISettingsService` is created)
- [X] T009 [P] Add `KeyBindingTests` in `tests/Nota.SmokeTest/KeyBindingTests.cs` (catalog ids unique, every catalog command present, defaults conflict-free under R3 except default pairs, round trip of overrides, unknown id / junk glyph skipped, reserved keys refused, Replace atomic, Reset rechecks, ResetAll, new-default collision → NeedsDecision) and register it in `tests/Nota.SmokeTest/Program.cs`

**Checkpoint**: catalog and service exist, nothing in the UI uses them yet.

---

## Phase 3: User Story 1 — Rebind a shortcut (P1) 🎯 MVP

**Goal**: every key handler asks the bindings; the Shortcuts page can capture, confirm, clear.

**Independent test**: rebind Split to ⌘T and piano-roll Transpose up to ⌘K, restart, both work; old keys don't.

- [X] T010 [US1] Switch `src/managed/Nota.App/MainWindow.Input.cs` to `Keys.Is`: palette toggle and Space / ⇧Space / Return in the tunnel handler (Session Return → `session.launchSelection`), Tab → `view.cycleDetailTab`, the Esc chain → `edit.cancel`, track keys → shared actions, ⌘⇧A, ⌘A, ⌘R, ⌘., ⌘⇧R, ⌘M, ⌘⇧M, ⌘G/⌘⇧G, ⌘⇧V, ⌘⇧C, ⌘⇧D/⌘⇧I, clipboard, nudge/move (`arr.*`), delete, 0, P, Z X C V (`notes.*`), `GridCommandFor`, the `KeyToPitch` map → `notes.*` actions (KeyUp still releases by physical key); keep every gate and the order
- [X] T011 [P] [US1] Switch `src/managed/Nota.App/PianoRollView.cs` `OnKeyDown` and `src/managed/Nota.App/PianoRollView.Controllers.cs` to `Keys.Is` (roll.* actions, shared edit.*)
- [X] T012 [P] [US1] Switch `src/managed/Nota.App/SessionView.cs` `HandleKey` (session.rename, session.insertScene, edit.copyToOtherView, shared edit.*; selection arrows stay fixed)
- [X] T013 [P] [US1] Switch `src/managed/Nota.App/ModularView.cs` key handler (modular.fit, modular.bypass, edit.duplicate, edit.delete, edit.cancel; Space pan stays fixed)
- [X] T014 [P] [US1] Switch `src/managed/Nota.App/DeviceChainView.Clipboard.cs` (devices.group/ungroup, shared edit.*, edit.cancel) and the Esc paths of rack cards / audio clip editor reached from MainWindow
- [X] T015 [P] [US1] Switch `src/managed/Nota.App/BrowserView.Files.cs` (browser.back/forward/up/editPath incl. off-macOS alternatives; Return stays fixed)
- [X] T016 [US1] Menu: `src/managed/Nota.App/MainWindow.MenuKeys.cs` takes gestures from `KeyBindings` (skip NoMenuKey), re-applies them and rebuilds `_menuShortcuts` on `Changed`; `HandleFloatingKeyDown` undo/redo/grid via `Keys.Is`
- [X] T017 [US1] Replace `ShortcutGroups` with a `ShortcutLayout` (groups of `Action(id)` and `Fixed(key, text)` rows; every catalog action exactly once, mouse/gamepad/navigation rows fixed) in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`; render action rows with key-cap buttons (primary + alternative slot) in `src/managed/Nota.App/PreferencesWindow.cs`
- [X] T018 [US1] Capture mode in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`: one row at a time, tunnel KeyDown takes every key, `Keys.Capturing` set (MainWindow ignores keys; macOS menu gestures cleared and restored), proposed caps shown, Confirm / Cancel / Clear / Assign Esc, Esc cancels, window deactivation cancels
- [X] T019 [US1] Add EN strings for new action titles and editor UI to `src/managed/Nota.Application/Localization/en.json`
- [X] T020 [US1] Update `tests/Nota.SmokeTest/PaletteTests.cs` `RegistryDrift`: every catalog action appears in the Shortcuts layout (instead of the glyph scan)

**Checkpoint**: US1 works end to end.

---

## Phase 4: User Story 2 — Conflicts (P1)

**Independent test**: ⌘D for Transpose up → conflict names Duplicate → Replace; ⇧F for Nudge left → accepted with a note; ⌘Q refused.

- [X] T021 [US2] Conflict line in capture (`FindConflicts`: "⌘D is used by Duplicate (Global)", Replace / Cancel, Confirm hidden) and the shadow note (`FindShadows`) in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`
- [X] T022 [US2] Reserved / incomplete feedback (`Validate`) in the capture field in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`

---

## Phase 5: User Story 3 — Edited mark, filter, reset (P2)

**Independent test**: edit three, Edited only shows three; reset one; Reset all → none.

- [X] T023 [US3] Edited mark (accent dot), "default ⌘E" caps, ↺ Reset per row (conflict check first) in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`
- [X] T024 [US3] Header: "Edited only" toggle, "Reset all to defaults…" with confirmation dialog, "Needs a decision" group; filter matches titles, aliases, group and current keys in glyph and spelled forms ("cmd t", "ctrl+t") in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`

---

## Phase 6: User Story 4 — Note map (P3)

**Independent test**: notes.c → Q, Q plays C4; A doesn't.

- [X] T025 [US4] Lay out the 13 note rows + octave / velocity rows in their own group; confirm Notes-scope conflicts against window-layer keys in `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs`

---

## Phase 7: Polish & cross-cutting

- [X] T026 [P] Tooltips (the snap chip's ⌘1–⌘5 line stays static text — follow-up with #60): `KeyTips.Set(control, text, id)` swaps the default glyph for the current key and re-applies on `Changed` in `src/managed/Nota.App/Input/Keys.cs`; use it in `src/managed/Nota.App/MainWindow.MenuKeys.cs` (record, loop record, metronome, maximize, automation) and the snap chip / loop tips
- [X] T027 [P] Palette hints: current key from `KeyBindings` in `src/managed/Nota.Presentation/PaletteProviders.cs`; refresh the index on `Changed`
- [X] T028 [P] `CHANGELOG.md` `[Unreleased]` entry and `FEATURES.md` (nota-features skill)
- [X] T029 User docs (nota-daw/nota-docs#99): shortcuts page EN + RU in `../nota-docs` and its `scripts/shortcuts.mjs` reading the new layout (nota-docs skill; separate nota-docs PR)
- [ ] T030 `scripts/build.sh` green; walk quickstart.md in the app (macOS) with `NOTA_DATA_DIR=/tmp/n096data`, screenshots of the editor

---

## Dependencies & order

- T001 → T002–T009 → US1 (T010–T020) → US2 / US3 / US4 (editor, any order) → Polish.
- T010–T015 touch different files and can run in parallel after T008.
- US2–US4 extend the same editor file, so run them in sequence.

## Implementation strategy

MVP = Phases 1–3 (rebind works, defaults unchanged). Conflicts (US2) must land in the same
PR before release — without them US1 can break keys. Then US3, US4, polish.
