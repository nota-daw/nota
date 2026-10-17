# Tasks: DSP Silence Detection & Dynamic Sleep Bypass

**Input**: Design artifacts from `specs/025-dsp-silence-bypass/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`)

---

## Phase 1: Setup & Foundational Primitives

**Purpose**: Primitives and base interface methods required by silence detection across tracks and devices

- [x] T001 Implement `isBufferSilent(const float* interleaved, int32_t frames, float threshold)` in `src/native/nota.engine/src/AudioBufferUtils.h`
- [x] T002 Add virtual lifecycle methods (`isIdle()`, `hasTail()`, `wake()`) and state primitives to `Device` in `src/native/nota.engine/src/Device.h`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Test infrastructure and baseline verification for silence detection

- [x] T003 Add silence detection unit tests verifying threshold behavior, early exit, and denormal flushing in `tests/Nota.SmokeTest/`

**Checkpoint**: Core silence detection primitive and Device lifecycle base ready.

---

## Phase 3: User Story 1 - Track-Level Silence Bypass (Priority: P1) 🎯 MVP

**Goal**: Skip redundant insert device chains, PDC, and send bus additions on idle audio/instrument tracks

**Independent Test**: Play an arrangement where only 2 of 16 tracks sound; verify that silent tracks skip device processing and meters read zero with identical audio output.

### Implementation for User Story 1

- [x] T004 [US1] Update `Engine::mixGraph()` in `src/native/nota.engine/src/Engine_Render.cpp` to evaluate track generator output and `isTrackChainIdle(t)`, bypassing insert chain execution, PDC, and send bus addition on silent tracks
- [x] T005 [US1] Update Master track and Return track rendering in `src/native/nota.engine/src/Engine_Render.cpp` to skip device processing when incoming bus signals are silent and all devices are idle

**Checkpoint**: Track-level silence bypass functional for all tracks with idle device chains.

---

## Phase 4: User Story 2 - Device-Level Auto-Sleep with Tail Hangover (Priority: P1)

**Goal**: Enable time-based native effects (`Delay`, `Chamber`, `Reverb`, `Compressor`, `Ceiling`) to sleep after their tails decay below -100 dBFS

**Independent Test**: Feed an audio burst into `Chamber` or `Delay`; verify that the tail decays naturally to silence, enters sleep, and wakes on the next audio transient without clipping or click.

### Implementation for User Story 2

- [x] T006 [US2] Implement tail countdown (50 ms hangover window) and auto-sleep in `src/native/nota.engine/src/Delay.h`
- [x] T007 [US2] Implement tail countdown (100 ms hangover window) and auto-sleep in `src/native/nota.engine/src/Chamber.h` and `src/native/nota.engine/src/Reverb.h`
- [x] T008 [US2] Implement release envelope countdown and auto-sleep in dynamics devices (`src/native/nota.engine/src/Compressor.h`, `src/native/nota.engine/src/Ceiling.h`)

**Checkpoint**: High-CPU time-based devices automatically sleep when silent.

---

## Phase 5: User Story 3 - Memoryless / Instant Silence Passthrough (Priority: P2)

**Goal**: Instantly bypass memoryless native devices on silent blocks without wasting CPU on zero-math

**Independent Test**: Pass a block of zeroes through `Eq`, `AutoPan`, and `Utility`; verify that `process()` is bypassed and output is unchanged.

### Implementation for User Story 3

- [x] T009 [US3] Implement biquad state settling check and instant silence bypass in `src/native/nota.engine/src/Eq.h`
- [x] T010 [US3] Implement instant silence bypass in `src/native/nota.engine/src/AutoPan.h`, `src/native/nota.engine/src/Vintage.h`, and `src/native/nota.engine/src/Utility.h`

**Checkpoint**: Memoryless devices bypass processing on silent blocks.

---

## Phase 6: User Story 4 - Verification & Quality Gates (Priority: P1)

**Goal**: Verify zero audio dropouts, zero truncated tails, and full adherence to Real-Time Audio Rule AR-6

**Independent Test**: Full test suite pass via `scripts/build.sh`.

### Implementation for User Story 4

- [x] T011 [US4] Add comprehensive automated smoke tests in `tests/Nota.SmokeTest/DspSilenceBypassTests.cs` verifying tail decay preservation, instant transient wakeup, and CPU sleep behavior
- [x] T012 [US4] Verify Real-Time Audio Rule AR-6 across all touched files (zero heap allocations, zero locks, zero blocking calls in audio callbacks)
- [x] T013 [US4] Execute full clean build and smoke test via `scripts/build.sh`

---

## Phase 7: Polish & Closing Tasks

**Purpose**: Repository standards, licensing, and documentation

- [x] T014 [P] Ensure AGPL-3.0-only SPDX two-line headers on any newly created source files
- [x] T015 [P] Add entry to `CHANGELOG.md` under `[Unreleased]` documenting DSP silence detection and auto-sleep CPU savings
- [x] T016 Run `/speckit-converge` to verify all spec requirements are converged

---

## Phase 8: Hybrid Silence Architecture & Single-Scan Chain Pipeline (Refinement)

**Purpose**: Implement stateful dual-threshold hysteresis (`SilenceDetector`), hold-time counter, and propagate `inputSilent` metadata down the DSP graph so devices never analyze silence twice.

- [x] T017 Implement `SilenceDetector` with dual-threshold hysteresis (`attackThreshold`, `releaseThreshold`), sample-accurate `holdSamples`, and `SignalState` in `src/native/nota.engine/src/AudioBufferUtils.h`
- [x] T018 Add `virtual void process(float* buf, int32_t frames, bool inputSilent)` overload to `Device` in `src/native/nota.engine/src/Device.h`
- [x] T019 Wire `SilenceDetector` into `Track.h`, `Engine.cpp` snapshot clone/SR lifecycle, and implement `currentSilent` metadata propagation in `Engine_Render.cpp` for tracks, returns, and master
- [x] T020 Update native devices (`AutoPan.h`, `Eq.h`, `Utility.h`, `Vintage.h`, `Delay.h`, `Chamber.h`, `Reverb.h`, `Compressor.h`, `Ceiling.h`) to accept `inputSilent` and eliminate redundant buffer scans
- [x] T021 Add automated smoke tests in `tests/Nota.SmokeTest/DspSilenceBypassTests.cs` for hysteresis deadband, hold time, and chain metadata propagation
- [x] T022 Execute `./scripts/build.sh` to ensure all tests pass cleanly

---

## Dependencies & Execution Order

- **Setup & Foundational (Phases 1 & 2, T001–T003)**: Core silence primitives must be implemented first.
- **Track-Level Bypass (Phase 3, T004–T005)**: Depends on Phase 1. Delivers initial MVP benefits for silent tracks.
- **Device-Level Auto-Sleep (Phase 4, T006–T008)**: Depends on Phase 1 & 2. Enables time-based devices to sleep.
- **Memoryless Passthrough (Phase 5, T009–T010)**: Depends on Phase 1 & 2. Can proceed in parallel with Phase 4.
- **Verification & Gates (Phase 6 & 7, T011–T016)**: Validates complete feature and ensures PR readiness.
- **Hybrid Refinement (Phase 8, T017–T022)**: Enhances architecture with hysteresis, hold-time, and zero duplicate buffer scans.

