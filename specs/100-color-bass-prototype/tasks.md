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

## Phase 9: Clean color bass — reproducible foundation
- [x] T018 Extend specs/100-color-bass-prototype/{spec,plan,research,data-model,contracts/device}.md with FR-016–021 and the accepted fixed-parameter/fixed-latency design.
- [x] T019 Restore tests/native/ChromaTest.cpp and scripts/test-color.sh with source-override baseline renders, allocation guard, boundary/attack/release/stereo tests and repeatable WAV/CPU output (FR-021).

## Phase 10: US1 — stable and clear coloration
- [x] T020 [US1] Implement selected-note hysteresis, reliable startup estimates and predicted/energy-aware peak matching in src/native/nota.engine/src/devices/effects/Chroma.{h,cpp} (FR-016–017).
- [x] T021 [US1] Add frame-centred phase and two-hop placement transitions in src/native/nota.engine/src/devices/effects/Chroma.{h,cpp}; preserve exact zero-shift/stereo and test moving/overlapping partials (FR-017).
- [x] T022 [US1] Add local stereo-linked attack protection, honest Decay release, smooth obsolete-tail release and energy-based sleep in src/native/nota.engine/src/devices/effects/Chroma.{h,cpp} (FR-018–019).

## Phase 11: US2 — sound selection and recall
- [x] T023 [US2] Add three presets to the existing FactoryPresetCatalog and update tests/Nota.SmokeTest/ColorTests.cs for six presets, unchanged 24-param persistence and boundary behavior (FR-020).

## Phase 12: US3 — validation and listening
- [x] T024 [US3] Run scripts/test-color.sh before/after, generate level-matched WAVs and CPU/block metrics under build/color-prototype/clean, and record evidence in specs/100-color-bass-prototype/verification.md (FR-021).
- [x] T025 Update CHANGELOG.md, FEATURES.md and sibling nota-docs EN/RU Chroma pages, build/check docs, run scripts/build.sh, update quickstart and perform convergence.

Clean iteration dependencies: T018 → T019 → T020 → T021 → T022 → T023 → T024 → T025.
Independent baseline/measurement reads may overlap; DSP changes are sequential. Each DSP
stage reruns native regression checks; US2 independently verifies preset/old-state recall,
and US3 provides listening evidence without claiming human approval. T017 remains the
pre-existing global smoke gate and must be reassessed after the full build.

## Phase 13: Polyphonic scale correction
- [x] T026 Add native polyphonic acceptance fixtures to tests/native/ChromaTest.cpp.
- [x] T027 Implement bounded voice detection, assignment, synthesis and mode transitions in the native Chroma effect.
- [x] T028 Add Mode to the existing Chroma card, preserve project and preset defaults, and cover the public path in tests/Nota.SmokeTest/ColorTests.cs.
- [x] T029 Generate piano listening examples, run the focused and full builds and document verification.
- [x] T030 Update user documentation, FEATURES.md and CHANGELOG.md and check convergence against FR-022–025.

## HQ color-bass engine

- [x] T031 Replace the peak tracker and Polyphonic path with the fixed-latency HQ topology.
- [x] T032 Keep independent Nota controls, apply Morph/Gate in the correction frames,
  and preserve legacy parameters with Mode retired.
- [x] T033 Run native, C ABI and full build verification and record the results. The shared render-pool timing gate remains tracked by T017.
- [x] T034 Update the bilingual manual, capture the card and generate baseline/HQ comparisons.
- [ ] T035 Complete the external listening comparison with matched inputs and playback levels.

## Card sizes and sideband mute

- [x] T036 Add the S/L card, two automatable wet-sideband mutes and legacy parameter handling.
- [x] T037 Draw one-pixel spectral traces with a theme-derived Tuned color.
- [x] T038 Verify sideband audio, legacy state, both card sizes and live theme changes. Update the manual and design guide. The shared render-pool timing gate stays tracked by T017.

## Selectable processing FFT

- [x] T039 Prepare the three FFT configurations, activate at block entry, and preserve the two scope analyses.
- [x] T040 Enforce stopped-transport edits and update PDC for inserts and rack children. Exclude FFT from presets and continuous control.
- [x] T041 Add the shared S/L FFT selector and adjacent latency readout.
- [x] T042 Verify the native and C ABI paths, waveform equality, allocation and callback cost. Verify headless cards and update the bilingual manual and changelog.

## MIDI note source

- [x] T043 Extend the existing MIDI key feed with the pre-block held notes and verify Arrangement routing.
- [x] T044 Implement Chroma pitch classes, aligned transitions, scope and sleep coverage.
- [x] T045 Add the shared S/L MIDI source menu and live keyboard, with project and rack coverage.
- [x] T046 Verify realtime and offline audio, update user docs and feature lists, and record convergence.
