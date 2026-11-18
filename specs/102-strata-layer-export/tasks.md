# Tasks: Export one Strata layer on its own

**Input**: Design documents from `/specs/102-strata-layer-export/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Smoke-test checks in `tests/Nota.SmokeTest/Program.cs` (Strata section) — the
project's end-to-end gate.

**Organization**: US1 (export a layer) and US2 (after save/reopen) share one code path;
US3 (unavailable layers) adds the guards and menu states.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Foundational

- [x] T001 Create `src/managed/Nota.Infrastructure/Audio/StrataLayerExport.cs` (SPDX header): `StrataLayerExportResult` enum, `Check`, `TryReadLayer`, `WriteLayerWav` per [contract](contracts/strata-layer-export.md), parsing the v1 blob from [data-model](data-model.md); `WriteLayerWav` writes nothing unless the result is `Written`

## Phase 2: User Story 1 - Export the vocal overdub on its own (P1) 🎯 MVP

- [x] T002 [US1] In `StrataDeviceBody.cs`, add a right-click `MenuFlyout` on each layer row: caption "Layer N", item "Export layer N as WAV…" (`GlyphKind.Save`) with a tooltip that says what is exported (FR-001, FR-009)
- [x] T003 [US1] Export flow in `StrataDeviceBody.cs`: remember the Strata instance id, open the save picker (title + suggested "Nota Strata layer N.wav"), re-find the device by id, re-read `DeviceGetState`, run `WriteLayerWav` off the UI thread, report the outcome in the status bar (FR-003…FR-005, FR-008)
- [x] T004 [P] [US1] Strings in `src/managed/Nota.Application/Localization/en.json` (`device.strata.*`)
- [x] T005 [US1] Smoke test: record layer 1 (note) and overdub layer 2 (different note), export layer 2 → length == loopLen, sample-identical to blob layer 2, differs from layer 1; blob byte-identical before/after; Speed/Reverse/Feedback/level/mute changed → file unchanged (SC-001, SC-002)

## Phase 3: User Story 2 - Export after saving and reopening (P1)

- [x] T006 [US2] Smoke test: `DeviceSetState` the blob onto a fresh Strata and export layer 2 → byte-identical to the earlier export (SC-004)

## Phase 4: User Story 3 - A layer that cannot be exported says so (P2)

- [x] T007 [US3] Menu state in `StrataDeviceBody.cs`: item disabled with trailing "still recording" / "loop not closed yet" from the live scope (FR-007)
- [x] T008 [US3] Smoke test: layer being recorded → `Recording`; first pass → `NoLoop`; index ≥ layer count → `NoLayer`; truncated blob → `Invalid`; no file created in any of them (FR-008)

## Phase 5: Polish & cross-cutting

- [x] T009 [P] `CHANGELOG.md` `[Unreleased]` Added entry (#482)
- [x] T010 [P] `FEATURES.md` Strata line (nota-features)
- [x] T011 [P] User docs: nota-docs Strata page EN + RU (nota-docs skill)
- [x] T012 Headless shot of the layer menu (enabled + disabled) for the PR
- [x] T013 `scripts/build.sh` green

## Dependencies

T001 → T002/T003/T005–T008. T004 parallel. T009–T011 after the UI is settled. T013 last.

## Nota Compliance Checklist (re-check)

- [x] SPDX header on `StrataLayerExport.cs`
- [x] No new dependency
- [x] No audio-thread change
- [x] Only existing C ABI calls
- [x] Smoke test extended
- [x] CHANGELOG / FEATURES / docs
- [x] UI follows DESIGN.md (MenuKit)

## Converge (2026-11-18)

Assessed the code against spec.md FR-001…FR-010 and SC-001…SC-004: **Converged** — no
remaining work.

- FR-001/FR-009: `StrataDeviceBody` layer-row `ContextRequested` → "Export layer N as WAV…"
  (tooltip and dialog title state "as recorded, after Input gain").
- FR-002…FR-004, SC-001: `StrataLayerExport` copies the layer's PCM verbatim; smoke test
  compares the WAV data chunk byte for byte with the blob slice, with Speed 2×, Reverse,
  Feedback 40 %, −9 dB and mute set.
- FR-005, SC-002: blob byte-identical before/after (smoke); the export only calls
  `DeviceGetState`.
- FR-006, SC-004: `DeviceSetState` onto a fresh Strata → identical file (smoke).
- FR-007: menu item disabled with "still recording" / "loop not closed yet" (headless shot).
- FR-008: device re-found by instance id after the picker; `NoLayer` / `Recording` /
  `NoLoop` / `Invalid` write nothing and report in the status bar (smoke: no file created).
- FR-010: the rail's Export is unchanged.

## Review follow-up (PR #663, 2026-11-18)

- [x] T014 `Strata::setState` restores a layer saved mid-recording as finished (`LS_Rec` → `LS_Play`; recording is already stopped on load), so it exports after reopen; smoke check
- [x] T015 Take identity across the save dialog: the card keeps a small fingerprint of the chosen take (the layer's peak envelope via `DeviceLayerWave`, one layer only — no copy of the device state before the dialog) and compares it after the dialog; a re-recorded slot reports the layer gone; smoke check
