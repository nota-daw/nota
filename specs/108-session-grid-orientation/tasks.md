---
description: "Task list for spec 108 — Session view: turn the grid"
---

# Tasks: Session view — turn the grid, scenes across and tracks down

**Input**: Design documents from `specs/108-session-grid-orientation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: the constitution's verification gate applies — the smoke test is extended for the
Remote `ax` field and the `GridNav` step mapping; the phone page gets vitest cases.

**Organization**: grouped by user story (spec.md). Paths are relative to the worktree
`~/repos/nota/nota-968` unless they start with `../nota-nx/`.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Add `SessionGrid` to `src/managed/Nota.Application/Settings.cs`: `public string SessionGrid { get; set; } = "down";` with a doc comment ("down" | "across"; unknown reads as "down"; app setting, never in the project — specs/108) and a computed `[JsonIgnore] public bool SessionAcross => SessionGrid == "across";`
- [X] T002 [P] Add English strings to `src/managed/Nota.Application/Localization/en.json`: `preferences.session` ("Session"), `preferences.session_grid` ("Session grid"), `preferences.scenes_down` ("Scenes down"), `preferences.scenes_across` ("Scenes across"), `preferences.session_grid_hint` ("Scenes down: a scene per row, a track per column. Scenes across: a scene per column, a track per row."), `session.toolbar.turn_to_across` ("Turn the grid: scenes across"), `session.toolbar.turn_to_down` ("Turn the grid: scenes down"), `session.grid.all` ("All"), `session.mixer.io` ("I/O"), `session.mixer.sends` ("Sends"), `session.mixer.mixer` ("Mixer"); rename `commands.session_select_up/down/left/right` to "Select the previous scene" / "Select the next scene" / "Select the previous track" / "Select the next track"; rename the controller-setup labels `controllers.setup.slot_up/down/left/right` to "Previous scene" / "Next scene" / "Previous track" / "Next track"

---

## Phase 2: Foundational (blocking)

- [X] T003 Create `src/managed/Nota.App/SessionView.Axis.cs` (SPDX header + why-comment): `internal enum SessionStep { PrevScene, NextScene, PrevTrack, NextTrack }`; `internal static class GridNav { public static SessionStep? FromArrow(Key key, bool across) }` mapping per data-model.md (down: ↑ PrevScene, ↓ NextScene, ← PrevTrack, → NextTrack; across: ← PrevScene, → NextScene, ↑ PrevTrack, ↓ NextTrack); a `private bool _across` field on `SessionView` set from settings at `Refresh()`
- [X] T004 Rewrite `Move(Key, List<Column>)` in `src/managed/Nota.App/SessionView.cs` as `Move(SessionStep, List<Column>)` with today's rules by meaning (scene selection: Prev/NextScene walk scenes, NextTrack → first track; bus: Prev/NextTrack walk Return/Master, PrevTrack off the first bus → last track, else the scene; slot: NextTrack past the last track → first bus, PrevTrack off the first track → the scene); `HandleKey` maps arrows through `GridNav.FromArrow(e.Key, _across)`; `MoveSelection(Key)` becomes `MoveSelection(SessionStep)`
- [X] T005 Wire `session.selectUp/Down/Left/Right` in `src/managed/Nota.App/MainWindow.Commands.cs` to `MoveSelection(SessionStep.PrevScene/NextScene/PrevTrack/NextTrack)` (ids unchanged); check every other caller of `MoveSelection` (`git grep MoveSelection`) compiles and keeps its meaning
- [X] T006 [P] Smoke check in `tests/Nota.SmokeTest` (next to the other Session checks in `Program.cs` or a new `SessionGridTests.cs` included the same way): `GridNav.FromArrow` returns the data-model.md table for both orientations (make `GridNav` reachable — `InternalsVisibleTo` exists for the smoke test, or mirror how other `Nota.App` internals are checked; if `Nota.App` isn't referenced, move `SessionStep`/`GridNav` to `src/managed/Nota.Application/Editing/SessionGridNav.cs` instead and reference it from the view)

**Checkpoint**: commands and controllers step by scene / track; no visual change yet.

---

## Phase 3: User Story 1 — Turn the desktop Session grid (P1) 🎯 MVP

**Goal**: Scenes across draws a scene per column and a track per row with every slot behaviour intact; the choice is live and remembered.

**Independent test**: quickstart.md § 2 steps 1–4 and 6.

- [X] T007 [US1] In `src/managed/Nota.App/SessionView.cs` make the strips axis-aware: `_headerRow`, `_cellsRow`, `_footerRow` get `Orientation` and margins from `_across` at `Refresh()` (down: horizontal, margins as today; across: vertical, margins turned); rebuild the `inner` grid as `Auto,*,Auto` rows (down) or columns (across) and swap the two `ScrollViewer`s' scroll axes (inner scrolls cells vertically in down / horizontally in across; outer the other way) — keep references to both viewers and `inner` as fields so `Refresh()` can re-layout
- [X] T008 [US1] Lane geometry in `src/managed/Nota.App/SessionView.Axis.cs`: `double LaneLength(Column c)` (down: `c.Width`; across: lane height = `CellH`, raised by US3), `double SlotMain` / `SlotCross` helpers so `AddColumn` sizes each lane's stack (down: `Width = c.Width`, vertical cells; across: `Height = lane height`, horizontal cells each `TrackColW` wide)
- [X] T009 [US1] Turn `AddSceneRail()` in `src/managed/Nota.App/SessionView.Grid.cs`: across → corner (`SceneRailW × HeaderH`, "Scenes N") into `_headerRow` (now the left strip), the scene cells as a horizontal stack of `SceneCell`s each `TrackColW × HeaderH` followed by the "+ Scene" ghost button the same size, added first to `_cellsRow`; the rail foot (Stop All + mixer labels) is built by US3 (T016) — in across for now add Stop All alone at the top of `_footerRow`
- [X] T010 [US1] Turn `AddColumn(Column c)` in `src/managed/Nota.App/SessionView.Grid.cs`: across → the header (`ColumnHeader`) `SceneRailW` wide × lane height into the left strip; the cells as a horizontal `StackPanel` (Spacing = Gap) of `SlotCell` / `GroupCell` / `BlankCell` each `TrackColW` wide × lane height; the foot (Stop button, mixer) into `_footerRow` as a horizontal stack lane-tall (Stop `StopColW` = 40 wide; mixer as today until US3)
- [X] T011 [US1] Make the cell controls take their size from the view instead of constants: `SlotCell` (Height = CellH), `GroupCell`, `BlankCell`, `SceneCell` (Width = SceneRailW, Height = CellH; `LaunchBox` centred on the cell's height), `ColumnHeader` (Width = c.Width, Height = HeaderH) in `src/managed/Nota.App/SessionView.Grid.cs` — pass width/height from the builders; `LaunchBox` and any painting that used `CellH`/`SceneRailW` read `Bounds` instead
- [X] T012 [US1] Scene drag in across: `SceneAt(Point)` and the `SceneCell` drag threshold use X in across (Y in down), cursor `SizeWestEast`; the drag-target marker painting (`_sceneDragTarget`) draws a vertical bar before the target scene in across — `src/managed/Nota.App/SessionView.Grid.cs`
- [X] T013 [US1] Check every other screen-axis assumption in `src/managed/Nota.App/SessionView*.cs` (`git grep -n "\.Y\b\|\.X\b\|Height\|Width" SessionView*.cs`): clip drag (`SlotAt` is geometry-agnostic — confirm), group fold chevron glyphs (down: ChevronRight/Down; across: ChevronDown/Right meaning "members below"), `ScrollIntoView` of the selection, the grid-area right-click / Browser drop on empty space ("right of the last column" → "below the last row")
- [X] T014 [US1] Live apply: in `src/managed/Nota.App/SessionView.cs` subscribe to `ISettingsService.Changed` on attach (unsubscribe on detach) and `Refresh()` when `Settings.SessionAcross != _across`; keep `_sel` (already preserved by Refresh)
- [X] T015 [US1] Preference UI: in `src/managed/Nota.App/PreferencesWindow.Editing.cs` add a "Session" section (`preferences.session`) with a segmented "Session grid" choice (`preferences.scenes_down` / `preferences.scenes_across`, hint `preferences.session_grid_hint`) using the same segmented control the page already uses; on change set `SessionGrid`, `Save()`
- [X] T016 [US1] Toolbar toggle in `src/managed/Nota.App/SessionView.Toolbar.cs`: a ghost icon button next to the I/O / Sends / Mixer toggles (pick an existing `GlyphKind` that reads as "turn"/"swap axes"; add one to the glyph set only if none fits), tooltip `session.toolbar.turn_to_across` / `turn_to_down`, flips `SessionGrid` and saves; `SyncToolbar()` updates its tooltip

**Checkpoint**: US1 complete — the turned grid works with the mixer as a plain stack (refined in US3).

---

## Phase 4: User Story 2 — Keys follow the screen, controllers follow the grid (P1)

**Goal / independent test**: quickstart.md § 2 steps 4 and 7.

- [X] T017 [US2] Verify `HandleKey` arrows in both orientations and the command path through `MainWindow.Commands.cs` by hand or the headless harness (synthetic `KeyDownEvent` on the view; `RunCommand("session.selectDown")`); the selected slot / scene matches the data-model.md rules — fix anything off in `src/managed/Nota.App/SessionView.cs`
- [X] T018 [P] [US2] Confirm `src/managed/Nota.App/Controllers/ControllerSetupWindow.cs` and the Shortcuts page show the new names (strings only, T002); run the `nota-shortcuts` skill to keep Preferences → Shortcuts in sync

---

## Phase 5: User Story 3 — Track mixer in the turned grid (P2)

**Goal / independent test**: quickstart.md § 2 step 5.

- [X] T019 [US3] In `src/managed/Nota.App/SessionView.Mixer.cs` `BuildMixer`: across → the shown sections side by side (a horizontal `StackPanel`, Spacing = Gap), each section a vertical stack `TrackColW` wide with `SectionTop` replaced by a left margin; `MixerParts` gains `double Height` (the tallest section, 0 if none shown); controls get tooltips with their row names (Input, Monitor, Output, Send A…, Pan, Volume)
- [X] T020 [US3] Lane height: `LaneLength` in `src/managed/Nota.App/SessionView.Axis.cs` returns `max(CellH, mixer height)` in across — build the mixer before the lane's cells in `AddColumn` so the height is known; every lane uses the same height (the max over lanes) so scene columns stay straight
- [X] T021 [US3] `MixerRailLabels()` in across: section titles ("I/O", `common.sends`, `common.mixer` — the toolbar's own labels) each `TrackColW` wide, side by side, `HeaderH` tall, in the top-right corner after Stop All ("All" + stop glyph, `StopColW` wide) — `src/managed/Nota.App/SessionView.Mixer.cs` + the rail foot in `SessionView.Grid.cs`
- [X] T022 [US3] Return / Master lanes in across: blank where Stop is, mixer block as their columns have; cells blank — `src/managed/Nota.App/SessionView.Grid.cs`

---

## Phase 6: User Story 4 — Nota Remote and Nota NX (P2)

**Goal / independent test**: quickstart.md § 3 and § 4.

- [X] T023 [US4] `src/managed/Nota.Remote/RemoteContracts.cs`: `bool SessionAcross => false;` default member on `IRemoteHost` with a doc comment (specs/108); `src/managed/Nota.App/MainWindow.Remote.cs` implements it from settings
- [X] T024 [US4] `src/managed/Nota.Remote/RemoteHub.State.cs` `BuildSession`: `if (host.SessionAcross) w.WriteString("ax", "across");` right after `n` (contracts/remote-ses-axis.md)
- [X] T025 [P] [US4] `tests/Nota.SmokeTest/RemoteTests.cs`: the fake host's `SessionAcross` settable; flip it and `Wait("ses", …)` for `ax == "across"`, flip back and wait for `ax` absent
- [X] T026 [P] [US4] Phone page: `src/web/remote/src/lib/types.ts` `Session.ax?: 'across'`; `src/web/remote/src/lib/app.svelte.ts` `Prefs.sessionGrid: 'nota' | 'down' | 'across'` (default `'nota'`), drop `sessionAxis` with the migration "old `sessionAxis: 'across'` → `'across'`; else `'nota'`" in an exported pure `migratePrefs(raw)`, and an exported pure `effectiveAcross(grid, ses)`; `src/web/remote/src/screens/Scenes.svelte` uses it; `src/web/remote/src/components/Sheets.svelte` three buttons "As in Nota" / "Scenes down" / "Scenes across" with a caption naming the effective layout
- [X] T027 [P] [US4] `src/web/remote/test/grid.test.ts`: cases for `migratePrefs` (across kept, down → nota, missing → nota) and `effectiveAcross` (nota + ax, nota without ax, fixed choices); run `npm test` and `npm run build` in `src/web/remote` (node may not be on PATH — see memory "Nota docs")
- [X] T028 [US4] Nota NX state: `../nota-nx/src/state.hpp` `Prefs::sessionGrid = 0` (0 As in Nota, 1 down, 2 across) and `State::notaAcross`; `../nota-nx/src/state.cpp` load / save it in `prefs` like `uiScale`; `../nota-nx/src/link.cpp` set `S.notaAcross` from `ses.ax` (absent → false)
- [X] T029 [US4] Nota NX settings: `../nota-nx/src/overlays.cpp` a "Session grid" `segGroup` (As in Nota / Scenes down / Scenes across) in the settings sheet, `cfgSave()` on change
- [X] T030 [US4] Nota NX drawing: `../nota-nx/src/screens.cpp` `scenes::draw` — across variant: scene launch buttons pinned at the top (one per column, `Scene N` + triangle), track name + Stop pinned at the left (one per row), Stop All in the top-left corner, clips `colW × rowH` with scenes on X and tracks on Y; the `Pan` scroll and the scroll bars follow the swapped extents; selection outline (`S.selTrack`/`selScene`/`selRow`) on the turned cells
- [X] T031 [US4] Nota NX verification: `make -f Makefile.desktop && make -f Makefile.desktop test` in `../nota-nx`; NX_SCRIPT shots of Scenes in both orientations against the mock / real app; Switch build `.nro` with nacp + romfs in ASET

---

## Phase 7: Polish & cross-cutting

- [X] T032 Desktop screenshots of both orientations (headless harness `/private/tmp/n968harness`, NOTA_DATA_DIR isolated) at Studio Dark and Studio Light; check DESIGN.md typography / spacing of the turned headers and mixer block (nota-design skill)
- [X] T033 [P] `CHANGELOG.md` `[Unreleased]` → Added: "Session view: turn the grid — scenes across and tracks down — in Preferences → Editing or from the Session toolbar; Nota Remote and Nota NX follow it ("As in Nota") or keep their own (#968)"; Changed: the Session selection commands are named previous / next scene and track
- [X] T034 [P] Run the `nota-features` skill (FEATURES.md) and the `nota-docs` skill (Session view page EN + RU, Preferences page, Remote page; shots in both orientations) in `../nota-docs` (worktree `../nota-docs-968`)
- [X] T035 [P] Russian strings for the new / renamed keys via the translations repo flow (`../nota-translations`, as for earlier features)
- [X] T036 `scripts/build.sh` green in the worktree
- [X] T037 `/speckit-converge` reports Converged

---

## Dependencies & execution order

- Phase 1 → Phase 2 → US1 (Phase 3) → US3 (Phase 5, builds on the turned `AddColumn`).
- US2 (Phase 4) needs Phase 2 and, for the across half, US1.
- US4 desktop side (T023–T025) needs only T001; phone (T026–T027) and NX (T028–T031) need T024's contract and are independent of each other and of US1/US3.
- Polish after all stories.

## Parallel opportunities

- T002 ‖ T001; T006 after T003.
- T025, T026–T027 and T028–T031 can run alongside Phases 3–5 (different files / repos).
- T033, T034, T035 in parallel at the end.

## Implementation strategy

MVP = Phases 1–3 (desktop turn, with a plain mixer stack) + Phase 4 checks. Then US3 (mixer block),
then US4 (remotes), then polish. The NX part is committed in `nota-nx` separately; the desktop and
phone parts form one PR in `nota-daw/nota` (`Closes #968`).

## Phase 8: Convergence

- [X] T038 Show the full scene name (SceneCell) and track name (ColumnHeader) as a hover tooltip when it is cut with an ellipsis, in src/managed/Nota.App/SessionView.Grid.cs, per Edge Cases "Text that no longer fits" (partial)
- [X] T039 Time a Scenes down ↔ across switch on a 32 tracks × 32 scenes project in the headless harness (/private/tmp/n968harness) and keep it under 0.5 s, per SC-001 (partial)
