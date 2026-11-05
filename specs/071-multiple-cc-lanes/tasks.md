<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Tasks: multiple CC lanes and saved heights

## Setup
- [x] T001 Verify approved issue #422, clarify contribution scope and complete spec/plan/contracts.

## US1 — Independent lanes
- [x] T002 [US1] Port per-instance identity, picker callback and existing commit path in PianoRollView.Controllers.cs.
- [x] T003 [US1] Add plus/picker/minus and synchronized scrolling in PianoRollView.ControllerLanes.cs and PianoRollView.cs.

## US2 — Safe hiding and reload
- [x] T004 [US2] Cancel stale gestures/capture and synchronize caches/gutters in PianoRollView.Controllers.cs and ControllerLanes.cs.
- [x] T005 [US2] Exercise data preservation, commits, reloads and short-window overflow in validation/Program.cs.

## US3 — Saved independent sizes
- [x] T006 [US3] Port dividers, normalized preferences and settings injection in ControllerLanes.cs, MainWindow.Detail.cs, Settings.cs and MidiLaneHeight.cs.
- [x] T007 [US3] Add normalization/JSON smoke tests and actual pointer/persistence checks in tests/Nota.SmokeTest and validation/Program.cs.

## Polish and submission
- [x] T008 Update CHANGELOG [Unreleased], FEATURES, French strings, Shortcuts and EN/RU manual handoff.
- [x] T009 Capture comparable base/branch UI evidence into pr-assets, and inspect it.
- [x] T010 Run scripts/build-win.ps1, UI checks and convergence; record validation.md.
- [x] T011 Commit scoped source/spec changes under the configured identity, push the fork over HTTPS and open a PR against upstream develop referencing #422.

Dependencies: T001 -> T002-T004 -> T006 -> T005/T007 -> T008-T010 -> T011.

Submitted as [nota-daw/nota#425](https://github.com/nota-daw/nota/pull/425) against develop.
