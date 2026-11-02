<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Tasks: Logic Grey theme

## Phase 1: Setup
- [x] T001 Inspect theme architecture and complete spec/clarification/plan in specs/069-logic-grey-theme/.

## Phase 2: Foundation and US1 — selectable grey workspace
Independent test: preset loads and every required token/readability pairing passes.
- [x] T002 [US1] Add logic-grey.json, two compatible slots to old palettes and built-in registration in Theme/.
- [x] T003 [US1] Publish shared RaisedFill and wire raised backgrounds/playheads in NotaTheme.axaml and cursor views.

## Phase 3: US2 — reversible persistent selection
Independent test: real Appearance selection survives a new process and all original themes return.
- [x] T004 [US2] Verify existing picker, settings roundtrip and Follow system using validation/ThemeProbe; retain defaults.

## Phase 4: US3 — readable and scalable UI
Independent test: contrast/data guards and actual Arrangement/piano-roll/mixer captures at all four scales.
- [x] T005 [US3] Extend meaningful palette/readability/appearance guards in tests/Nota.SmokeTest/DesignTokenCheck.Themes.cs.
- [x] T006 [US3] Capture and inspect UI/scales; compare frame cadence and project-data invariance in validation/.

## Phase 5: Documentation and verification
- [x] T007 Update DESIGN.md/html, scripts/design-html.py, FEATURES.md, CHANGELOG.md and local manual handoff.
- [x] T008 Run scripts/build-win.ps1 and record all feature-modified files and results in validation.md.

## Dependencies and execution
T001 -> T002 -> T003 -> T004/T005 -> T006 -> T007 -> T008.
T005 and documentation drafting may run independently after palette names stabilize;
all shared-file edits remain sequential. MVP is US1; user delivery includes all stories.
The final speckit-converge audit follows this completed implementation checklist.
