# Tasks: Nota Color prototype
## Phase 1: Setup
- [x] T001 Record accepted scope, design, contracts and checklist in specs/100-color-bass-prototype/.
## Phase 2: Foundation
- [x] T002 Add native DSP validation and WAV generator in tests/native/ColorTest.cpp and scripts/test-color.sh.
## Phase 3: US1 — Color a bass
- [x] T003 [US1] Implement stereo spectral retuning, bounded resonance and aligned dry path in src/native/nota.engine/src/devices/effects/Color.h and Color.cpp.
- [x] T004 [US1] Register kind 27 in src/native/nota.engine/src/engine/Engine_Devices.cpp, rack factory paths and CMakeLists.txt.
## Phase 4: US2 — Shape and recall
- [x] T005 [US2] Add smoke coverage in tests/Nota.SmokeTest/ColorTests.cs for generic persistence, clone and rendering.
- [x] T006 [US2] Add card, palette and preset integration under src/managed/Nota.App/DeviceCards/ and the existing application/infrastructure catalogs.
## Phase 5: US3 — Evaluate
- [x] T007 [US3] Generate reproducible level-matched WAVs and CPU results in build/color-prototype/ with scripts/test-color.sh.
- [x] T008 [US3] Build and verify through scripts/build.sh; inspect rendered card and record evidence in specs/100-color-bass-prototype/verification.md.
## Phase 6: Polish
- [x] T009 Update CHANGELOG.md, FEATURES.md and EN/RU documentation with experimental status.
- [x] T010 Run convergence against specs/100-color-bass-prototype/{spec,plan,tasks}.md and resolve implementation gaps.
## Dependencies and execution
T001 → T002 → T003 → T004 → T005 → T006 → T007/T008 → T009 → T010. Independent reads and builds can run concurrently; implementation is sequential. US1 validates tuning; US2 validates controls/recall; US3 supplies audition evidence. No deployment.

- [x] T011 Separate Dry and Tuned spectrum telemetry, render soft spectral lines, and verify the local prototype.

## Phase 7: Naturalness
- [x] T012 Implement peak tracking and phase continuity with prepared storage.
- [x] T013 Add stereo-linked Morph and Gate, appended parameters and processed telemetry.
- [x] T014 Add controls with gestures, reset, MIDI Learn, live updates and 24-parameter persistence coverage.
- [x] T015 Validate native DSP, old-state loading, active UI, before/after WAVs and CPU.
- [x] T016 Update docs/features/changelog and run full build and convergence.

## Phase 8: Convergence
- [ ] T017 [SC-004] Obtain a passing full smoke run for the overloaded render-pool deadline guards on a stable verification host. Current run reproduces the two previously documented failures; no Color checks fail. This is the remaining global verification gap, outside the new Color DSP implementation.
