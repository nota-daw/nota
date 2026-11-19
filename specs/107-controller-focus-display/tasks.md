---
description: "Tasks for spec 107 — controller focus you can see, in Nota and in Remote"
---

# Tasks: Controller focus you can see — in Nota and in Remote

**Input**: `specs/107-controller-focus-display/` (spec, plan, research, data-model, contracts, quickstart)

**Tests**: smoke tests for the pure helpers and the Remote messages (plan § Testing); web vitest
for the Scenes outline / focus line; headless UI harness for the readout and card mark.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Confirm branch `feat/controller-focus-display` is on current `origin/develop` and `scripts/build.sh` builds before changes (baseline)

---

## Phase 2: Foundational

**Purpose**: the focus summary every story reads.

- [X] T002 Add `ControllerFocusSummary` record (fields per data-model.md: Controller, Mode, TrackId, TrackName, Device?, Address?, Page, PageCount, PageName, BankFirst, BankLast) in src/managed/Nota.Application/Controllers/ControllerFocusSummary.cs
- [X] T003 Add `ControllerService.FocusSummary()` (lead controller = first active with knobs, as SyncControllerIndicator) and a `FocusSummaryChanged` signal raised on FocusChanged / ControllersChanged / BankChanged / project refresh, in src/managed/Nota.App/Controllers/ControllerService.cs

**Checkpoint**: summary available.

---

## Phase 3: User Story 1 — See what the controller is on (P1) 🎯 MVP

**Goal**: title-bar readout + card mark. **Independent test**: quickstart Manual 1–2.

- [X] T004 [US1] Render the summary in the controller chip (`ctl · track · device · p / n`, or `ctl · mode · first–last`), MaxWidth + ellipsis, full text in tooltip, refresh on FocusSummaryChanged and track renames, in src/managed/Nota.App/MainWindow.Controllers.cs
- [X] T005 [US1] Chip click menu: "Show device" (opens Devices on the focused track, selects the focused device) above the knob modes, in src/managed/Nota.App/MainWindow.Controllers.cs
- [X] T006 [P] [US1] Add strings `controllers.readout_device`, `controllers.readout_bank`, `controllers.readout_no_device`, `controllers.show_device` in src/managed/Nota.Application/Localization/en.json
- [X] T007 [US1] `CardAddress` attached property (DeviceAddress) on ControllerFocusOverlay; set it on every top-level card (MIDI effects, instrument, audio effects; rack = one card) in src/managed/Nota.App/DeviceChainView.cs and src/managed/Nota.App/DeviceChainView.Midi.cs
- [X] T008 [US1] Draw the focused card's mark (orange line on the top edge + small controller tag, orange-lines strength per DESIGN.md) in src/managed/Nota.App/Controllers/ControllerFocusOverlay.cs; a rack-inner focus marks its rack card
- [X] T009 [US1] Headless harness shots: readout in Arrangement / Session (Clip editor shown) / Volume mode; card mark on Twin then Valve (/private/tmp/n107harness)

**Checkpoint**: US1 works alone.

---

## Phase 4: User Story 2 — Track buttons select the track (P1)

**Goal**: Track ◀ / ▶ select tracks in every view; bank follows. **Independent test**: quickstart Manual 3, 2.

- [X] T010 [P] [US2] `ControllerTrackStep.Order(engine)` (HierarchyOrder minus returns minus members of collapsed groups) and `Step(order, selected, delta)` (neighbour; null at the ends; selected absent → first) in src/managed/Nota.Application/Controllers/ControllerTrackStep.cs — HierarchyOrder lives in Nota.App's ArrangementView: pass the order in, or move the ordering helper if it is engine-only
- [X] T011 [P] [US2] `ControllerState.BankFollow(index, trackCount)` — least move to contain index, clamped as BankStep — in src/managed/Nota.Application/Controllers/ControllerState.cs
- [X] T012 [US2] RoleRouter: TrackNext / TrackPrev → new `ControllerActionKind.TrackStep` (±1) in `Route` and the encoder-pair map, in src/managed/Nota.Application/Controllers/RoleRouter.cs
- [X] T013 [US2] ControllerService: `SelectTrack` hook; Execute(TrackStep) → step, SelectTrack, BankFollow + BankChanged; drop the Session special case from BankStep; same for ProtocolSurface track buttons if they route there, in src/managed/Nota.App/Controllers/ControllerService.cs and src/managed/Nota.App/Controllers/ProtocolSurface.cs
- [X] T014 [US2] Wire `SelectTrack` in MainWindow: Session effectively visible → `SessionView.SelectSlot(id, current scene)`; else `Timeline.SelectTrackFromOutside(id)` + scroll into view, in src/managed/Nota.App/MainWindow.Controllers.cs (+ a `CurrentScene` accessor in src/managed/Nota.App/SessionView.cs)
- [X] T015 [US2] Smoke: track order skips returns / collapsed members, Step ends, BankFollow cases (8→9 moves bank to contain 9; inside → no move), RoleRouter emits TrackStep, in tests/Nota.SmokeTest/ControllerTests.cs

---

## Phase 5: User Story 3 — Step between devices (P2)

**Goal**: Next / Previous device. **Independent test**: quickstart Manual 4.

- [X] T016 [P] [US3] `ChainOrder.Of(engine, track)` = [Midi 0..n-1, Instrument if any, Audio 0..m-1] and `Step(order, current, delta)` (rack-inner → its rack; current missing → first; null at ends) in src/managed/Nota.Application/Controllers/ChainOrder.cs
- [X] T017 [US3] `DeviceChainView.SelectAddress(DeviceAddress)` (top-level sections: SelectDevices + Rebuild → ControllerFocusChanged) in src/managed/Nota.App/DeviceChainView.Clipboard.cs
- [X] T018 [US3] Commands `device.focusNext` / `device.focusPrevious` (group Devices, no default key) in src/managed/Nota.Application/Palette/CommandCatalog.cs + strings in en.json; bindings in src/managed/Nota.App/MainWindow.Commands.cs (focus → step → if panel isn't on the track `ShowDevices(track, takeOverPanel:false)` → SelectAddress)
- [X] T019 [US3] Wizard extra-buttons page: "Previous device / Next device" pair as command roles in src/managed/Nota.App/Controllers/ControllerSetupWindow.cs
- [ ] T020 [US3] KeyLab DAW: capture Shift + track ◀ / ▶ and Shift + encoder on the real device (raw monitor /private/tmp/n100mon/mon); if distinct, bind to the device commands in src/managed/Nota.App/Controllers/Profiles/arturia.keylab-essential-61-mk3-daw.json (research R4) — needs the user's hardware
- [X] T021 [US3] Smoke: ChainOrder on Arp → Twin → Valve → EQ8 (order, ends, rack as one, deleted current), in tests/Nota.SmokeTest/ControllerTests.cs
- [X] T022 [US3] Shortcuts screen lists the two commands (nota-shortcuts skill; no default key)

---

## Phase 6: User Story 4 — Remote mirrors it (P2)

**Goal**: `ses.sel`, `foc`, Macros follow (contracts/remote-focus.md). **Independent test**: quickstart Manual 5–6.

- [X] T023 [US4] `RemoteSessionSelection` / `RemoteControllerFocus` records and `IRemoteHost.SessionSelection` / `ControllerFocus` in src/managed/Nota.Remote/RemoteContracts.cs
- [X] T024 [US4] Implement both in src/managed/Nota.App/MainWindow.Remote.cs (SessionView selection accessor when effectively visible; Bus → none; summary → focus with `md` mapped: Instrument → −1, Audio i → i, Midi → null)
- [X] T025 [US4] `BuildSession` writes `sel`; Snapshot gets a lazy `Focus` piece; Push sends `foc` on change (`SentFocus`); macro follow on focus-key change for Follow phones on the focus track (`SeenFocusKey`), in src/managed/Nota.Remote/RemoteHub.State.cs and src/managed/Nota.Remote/RemoteClient.cs
- [X] T026 [P] [US4] Web: `foc` state + `ses.sel` type in src/web/remote/src/lib/app.svelte.ts; focus line in src/web/remote/src/components/Header.svelte; outline in src/web/remote/src/screens/Scenes.svelte (desktop selection style)
- [X] T027 [P] [US4] Mock sends `sel` (walks with a timer or follows the phone's last tap) and `foc`, in src/web/remote/mock/nota-mock.mjs
- [X] T028 [P] [US4] vitest: focus-line formatting + outline cell selection in src/web/remote/test/focus.test.ts
- [X] T029 [US4] Smoke: `ses.sel` (slot / row / null), `foc` on change only, macro follow (and a manual pick survives until the next change), in tests/Nota.SmokeTest/RemoteTests.cs
- [X] T030 [US4] Browser-pane shots of the Scenes outline and header line at phone and tablet sizes (against the mock with `sel` / `foc`; the real hub is covered by RemoteTests and the n107 harness)

---

## Phase 7: MCP (FR-018)

- [X] T031 `IControllerAccess.StepDevice` / `SessionSelection` in src/managed/Nota.Mcp/Abstractions.cs, tools `step_controller_device` / `get_session_selection` in src/managed/Nota.Mcp/Tools/ControllerTools.cs, App adapter implementation (contracts/mcp.md)

---

## Phase 8: Polish & Nota closing tasks

- [X] T032 [P] CHANGELOG.md `[Unreleased]` entries (Added: readout, device stepping, Remote selection + focus; Changed: Track ◀ / ▶ select the track)
- [X] T033 [P] FEATURES.md via nota-features
- [X] T034 [P] User docs via nota-docs (control/controllers, remote pages, EN + RU) in ../nota-docs-929 (branch docs/controller-focus-929 on top of nota-docs#114)
- [X] T035 Remote protocol note for Nota NX (link contracts/remote-focus.md from the Remote docs / protocol section)
- [X] T036 RU strings for the new keys (nota-translations, ../nota-translations-929 on top of nota-translations#9)
- [X] T037 AGPL SPDX header on the new files (ControllerFocusSummary.cs, ControllerTrackStep.cs, ChainOrder.cs, focus.test.ts)
- [X] T038 New dependency: N/A
- [ ] T039 Real KeyLab check: track steps, readout, device steps, Remote on a tablet (user's hardware)
- [X] T040 `scripts/build.sh` green (smoke test included) — 2026-11-19: all green except the known render-pool timing check under load (3/100 deadlines); it passes alone (`--hosted-render-pool`, 0/100)

---

## Dependencies & Execution Order

- Phase 2 (T002–T003) blocks US1 and US4 (both read the summary).
- US2 and US3 depend only on Phase 1; they change what the summary shows but not its shape.
- US4's macro follow (T025) uses US3's device stepping only for testing, not to build.
- MCP (T031) after US3 (StepDevice reuses the command path) and US4 (selection accessor).
- Polish last; T039 needs the user.

## Parallel Opportunities

- T010, T011, T016 (pure helpers, separate files) together.
- T026, T027, T028 (web) together once T025's message shape is fixed by the contract.

## Implementation Strategy

MVP = Phase 2 + US1 (the readout answers "which device"), then US2 (makes the track buttons
visible), US3, US4, MCP, polish. Each phase is checked with its quickstart step before the next.
