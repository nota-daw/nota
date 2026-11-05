---

description: "Tasks for #308 — open the Detail panel on double-click, not on every selection"
---

# Tasks: Open the Detail panel on double-click, not on every selection

**Input**: Design documents from `/specs/071-detail-panel-double-click/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/gestures.md, quickstart.md

**Tests**: no unit-test tasks (none requested; the managed app has no unit-test project).
Verification is the headless harness in [quickstart.md](quickstart.md) plus `scripts/build.sh`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)

---

## Phase 1: Setup

- [X] T001 Add `public bool OpenDetailOnSelect { get; set; }` (default `false`, "Missing in old settings files → `false`") with a `<summary>` naming #308 next to `ArmOnSelect` in `src/managed/Nota.Application/Settings.cs`

---

## Phase 2: Foundational (blocks US1, US2, US5)

- [X] T002 In `src/managed/Nota.App/MainWindow.Detail.cs` add the follow-only scope: an `int _followOnly` depth counter, a `bool OpenDetailOnSelect` helper reading `_vm?.Settings.Current.OpenDetailOnSelect`, and a guard at the top of `ShowDetail(...)` — when `_followOnly > 0 && !DetailPanel.IsVisible && !OpenDetailOnSelect` return before touching layout (research R1/R2)

**Checkpoint**: the gate exists but nothing enters the scope yet — behaviour unchanged.

---

## Phase 3: User Story 1 — Select without the panel popping up (P1) 🎯 MVP

**Goal**: a single click / keyboard / side-effect selection never opens a closed panel.

**Independent Test**: quickstart scenarios 1–4 and 14.

- [X] T003 [US1] Wrap the body of `OnTrackSelected` in `_followOnly++ … finally _followOnly--` in `src/managed/Nota.App/MainWindow.Detail.cs`
- [X] T004 [US1] Wrap the body of `OnSessionSlotSelected` the same way in `src/managed/Nota.App/MainWindow.Detail.cs` (nested `Timeline.Select` calls stay inside the scope)
- [X] T005 [US1] Wrap the body of `FollowClipSelection` the same way in `src/managed/Nota.App/MainWindow.Detail.cs`

**Checkpoint**: US1 testable — a closed panel stays closed on every selection.

---

## Phase 4: User Story 2 — An open panel keeps following (P1)

**Goal**: with the panel open (docked, maximised, popped out) behaviour is exactly as before.

**Independent Test**: quickstart scenarios 9–11.

- [X] T006 [US2] Review `ShowDevices`, `ShowClipPanel`, `ShowAudioClipPanel`, `ShowPatternPanel` in `src/managed/Nota.App/MainWindow.Detail.cs`: the gate only fires when hidden, and every `Show*Panel` still updates hosts and tab buttons before `ShowDetail`, so content follows while hidden; fix any branch that skips content when hidden
- [X] T007 [US2] In `src/managed/Nota.App/MainWindow.Menu.cs` `OnMenuToggleClip`: when it opens the panel, re-show the checked tab (Devices → `ShowDevices(selected track)`, Clip → the current clip/audio editor panel, Pattern → `ShowPatternPanel`) so height and tab match; closing stays a plain hide (research R7)

---

## Phase 5: User Story 3 — Double-click opens the panel (P1)

**Goal**: double-click a track header → Devices; double-click a clip / filled slot → Clip.

**Independent Test**: quickstart scenarios 5–8.

- [X] T008 [US3] In `src/managed/Nota.App/ArrangementView.cs`: add `public event Action<int>? TrackDevicesRequested;`; in `AttachHeaderGestures` replace the instrument-only `OpenPluginEditor(t.Id, -1)` double-click with: any track, left button, `ClickCount == 2` → `Select(t.Id, -1)` if not selected, then `TrackDevicesRequested?.Invoke(t.Id)`; update the comment
- [X] T009 [US3] In `src/managed/Nota.App/MainWindow.axaml.cs` subscribe `Timeline.TrackDevicesRequested += id => ShowDevices(id);` next to `Timeline.TrackSelected`
- [X] T010 [US3] Confirm clip double-click (`OpenClipEditor` / `OpenAudioClipEditor`) and Session slot double-click (`OpenSessionClipEditor`) run outside the follow-only scope and open a closed panel; fix if a nested selection path swallows the open (src/managed/Nota.App/MainWindow.Detail.cs)

---

## Phase 6: User Story 4 — Existing double-clicks survive (P2)

**Independent Test**: quickstart scenarios 14–15; contracts/gestures.md rows marked "unchanged".

- [X] T011 [US4] Check `src/managed/Nota.App/ArrangementView.LaneControl.cs` and `src/managed/Nota.App/SessionView.Grid.cs`: empty-lane new clip, ADSR reset, row-seam reset, Session header rename and empty-slot create are untouched by T002–T010 (no code change expected)

---

## Phase 7: User Story 5 — Preference for the old behaviour (P3)

**Independent Test**: quickstart scenarios 13 and 16.

- [X] T012 [US5] In `src/managed/Nota.App/PreferencesWindow.cs` (Appearance page) add a `SwitchRow(Loc.T("Open the Detail panel when you select a track or clip"), …)` bound to `OpenDetailOnSelect` with `settings.Save()`, in a new `Section(Loc.T("Detail panel"), 10, row, Caption(...))` explaining that off means double-click opens it

---

## Phase 8: Polish & Cross-Cutting

- [X] T013 [P] Preferences → Shortcuts Mouse group in `src/managed/Nota.App/PreferencesWindow.cs`: add "Double-click a track header → Open its devices in the Detail panel"; extend "Double-click clip" to say it opens the Detail panel (nota-shortcuts)
- [X] T014 [P] Add every new `Loc.T` string to `src/managed/Nota.App/Languages/fr.json` (glossary: specs/068-ui-language/glossary.md)
- [X] T015 [P] `CHANGELOG.md` `[Unreleased]` → `### Changed` entry describing the new habit, the double-clicks, the plug-in window button, and the Preferences switch
- [X] T016 [P] `FEATURES.md` via the nota-features skill
- [X] T017 User docs EN + RU in `../nota-docs` via the nota-docs skill (pages describing how the Detail panel opens)
- [X] T018 Build `src/managed/Nota.App` and run the headless harness scenarios 1–16 from quickstart.md; capture before/after shots
- [X] T019 `scripts/build.sh` — builds; smoke test green except the two render-pool "overloaded" timing checks, which fail the same way on untouched develop (4497c866) on this machine

---

## Dependencies & Execution Order

- T001 → T002 → (T003–T005) → T006–T007; T008–T010 depend only on T002 being in place for testing.
- US4 (T011) after US1 + US3. US5 (T012) after T001.
- Polish T013–T016 are independent files ([P]); T017–T019 last.

### Parallel Example

```text
After T012: T013 (PreferencesWindow shortcuts) · T014 (fr.json) · T015 (CHANGELOG) · T016 (FEATURES.md)
```

## Implementation Strategy

MVP = Phases 1–3 (selection stops opening the panel). Then US3 (double-click in), US2
check, US4 audit, US5 switch, docs. Single PR (`Closes #308`).

## Nota Compliance (re-check)

- [x] No new source files → no new SPDX headers
- [x] No new dependency
- [x] No audio-thread / C ABI change
- [x] Smoke test unaffected (no engine behaviour change); `scripts/build.sh` still the gate
- [x] CHANGELOG / FEATURES / docs / shortcuts — T013–T017
- [x] DESIGN.md — the switch row reuses existing Preferences helpers (T012)
