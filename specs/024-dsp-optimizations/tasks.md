# Tasks: DSP Core Performance & Optimization

**Input**: Design artifacts from `specs/024-dsp-optimizations/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`)

---

## Phase 1: Setup & Foundational Tasks

**Purpose**: Core infrastructure and mathematical primitives required by all DSP optimizations

- [x] T001 Implement `FastMath.h` header in `src/native/nota.engine/src/FastMath.h` with unit-tested implementations of `exp2f`, `log2f`, `fastTanh`, `tanhRatio`, `dbToLin`, `linToDb`, `fastSin`, and `tanPrewarp`

**Checkpoint**: `FastMath.h` is standalone, compiles without warnings, and passes basic unit checks.

---

## Phase 2: User Story 1 - Lower CPU Load on Complex Sessions (Priority: P1) 🎯 MVP

**Goal**: Optimize CPU-intensive inner loops across Delay, Compressor, Eq, Chamber, and Pentad

**Independent Test**: Build and render audio tracks with active Delay, Compressor, Eq, and Chamber, measuring reduced CPU execution time with clean audio output.

### Implementation for User Story 1

- [x] T002 [US1] Optimize `Delay.h` in `src/native/nota.engine/src/Delay.h`: hoist `1.0f / std::tanh(drive)` invariant outside the sample loop with safe fallback for $\text{drive} \le 0.0f$, and use `fast_math::fastSin` for the wow/flutter LFO
- [x] T003 [US1] Optimize `Compressor.h` in `src/native/nota.engine/src/Compressor.h`: replace per-sample `std::log10` with `fast_math::linToDb` in the envelope detector, and replace `dbToLin` with `fast_math::dbToLin` in gain reduction
- [x] T004 [US1] Optimize `Eq.h` in `src/native/nota.engine/src/Eq.h`: refactor `Eq::process()` into chunked block-oriented biquad filtering, moving channel mode routing (`ChLeft`, `ChRight`, `ChMid`, `ChSide`, `ChStereo`) outside the sample loop
- [x] T005 [US1] Optimize `Chamber.h` in `src/native/nota.engine/src/Chamber.h`: unroll the 8-point Fast Walsh-Hadamard Transform `hadamard8` into straight-line additions and subtractions with constant scaling
- [x] T006 [US1] Consolidate math in `Pentad.h` in `src/native/nota.engine/src/Pentad.h`: include `FastMath.h` and use centralized `fast_math` functions

**Checkpoint**: Core DSP components run with streamlined math and reorganized loops; audible output is clear and glitch-free.

---

## Phase 3: User Story 2 - Audio Transparency & Accuracy Verification (Priority: P1)

**Goal**: Verify that fast approximations preserve professional audio fidelity within $< 0.05$ dB relative error

**Independent Test**: Smoke test verifying DSP processing stability, frequency response, and dynamic envelope response.

### Implementation for User Story 2

- [x] T007 [US2] Add DSP optimization accuracy and stability tests in `tests/Nota.SmokeTest/` verifying fast math bounds and filter stability across full-scale and near-zero signals

**Checkpoint**: Approximations confirmed perceptually transparent and numerically bounded.

---

## Phase 4: User Story 3 - Regression Testing & Quality Gates (Priority: P2)

**Goal**: Verify zero violations of Real-Time Audio Safety (AR-6) and ensure all builds and tests pass

**Independent Test**: Full test pass via `scripts/build.sh`.

### Implementation for User Story 3

- [x] T008 [US3] Verify Real-Time Audio Rule AR-6 across all touched files: ensure zero heap allocations (`malloc`, `new`, `std::vector` resize), zero mutex locks, and no blocking calls in audio processing callbacks
- [x] T009 [US3] Execute full clean build and smoke test via `scripts/build.sh`

---

## Phase 5: Polish & Closing Tasks

**Purpose**: Repository standards, licensing, and documentation

- [x] T010 [P] Ensure AGPL-3.0-only SPDX two-line headers on `src/native/nota.engine/src/FastMath.h` and any new files
- [x] T011 [P] Add entry to `CHANGELOG.md` under `[Unreleased]` documenting core DSP performance improvements and CPU efficiency gains
- [x] T012 Run `/speckit-converge` to verify all spec requirements are fulfilled and converged

---

## Phase 6: User Story 4 - High-Load Native DSP Hotspots Optimization (Priority: P1)

**Goal**: Eliminate transcendental bottlenecks, hoist invariants, and eliminate per-sample branches in Chamber, Vintage, AutoFilter, AutoPan, BassSynth, DynamicEq, and Ceiling.

**Independent Test**: Smoke tests in `Nota.SmokeTest` executing each device with full parameter modulation and audio rendering to verify numerical bounds, absence of NaNs, and stability.

### Implementation for User Story 4

- [x] T013 [US4] Optimize `Chamber.h` in `src/native/nota.engine/src/Chamber.h`: use `fast_math::fastTanh` in `loopLimit` and `softClip`, hoist invariant branches (`p.cubic`, `p.vintage`, `shim` feedback injection) outside the sample loop, and statically unroll the 4 diffusion stages (`s = 0..3`)
- [x] T014 [US4] Optimize `Vintage.h` in `src/native/nota.engine/src/Vintage.h`: replace `std::tanh` in `Curve::eval()` and `saturate()` with `fast_math::fastTanh`, precompute `std::tanh(0.6f)` in `Curve`, and replace `std::sin`/`std::cos` in wow/flutter LFO with `fast_math::fastSin`
- [x] T015 [US4] Optimize `AutoFilter.h` in `src/native/nota.engine/src/AutoFilter.h`: replace `std::tanh` in drive saturation and analog mode with `fast_math::fastTanh`, and use `fast_math::fastSin` in `lfoValue()`
- [x] T016 [US4] Optimize `AutoPan.h` in `src/native/nota.engine/src/AutoPan.h`: precompute `1.0 / std::tanh(kk)` invariant outside the sample loop when `shape > 1.0e-3f`, and replace `std::sin` in `lfoAt()` with `fast_math::fastSin`
- [x] T017 [US4] Optimize `BassSynth.h` in `src/native/nota.engine/src/BassSynth.h`: hoist `1.0 / std::tanh(preDrive)` outside the audio voice and sample loops
- [x] T018 [US4] Optimize `DynamicEq.h` in `src/native/nota.engine/src/DynamicEq.h`: replace `20.0 * std::log10(detEnv + 1e-9)` with `fast_math::linToDb` in the 4-band envelope detector, and replace `std::exp2` with `fast_math::exp2f` in modulation
- [x] T019 [US4] Optimize `Ceiling.h` in `src/native/nota.engine/src/Ceiling.h`: hoist `1.0f / std::max(1e-6f, ceil)` in limiter processing and use `fast_math::fastTanh` in `softClip()`
- [x] T020 [US4] Extend `tests/Nota.SmokeTest/DspOptimizationTests.cs` with test coverage for `Vintage`, `AutoFilter`, `AutoPan`, `BassSynth`, `DynamicEq`, and `Ceiling`
- [x] T021 [US4] Verify Real-Time Audio Rule AR-6 across all touched files (zero heap allocations, zero locks, zero blocking calls)
- [x] T022 [US4] Execute full clean build and smoke test via `scripts/build.sh`

---

## Dependencies & Execution Order

- **Setup (Phase 1, T001)**: Pre-requisite for all DSP modifications.
- **Implementation (Phase 2, T002–T006)**: Depends on T001. Completed.
- **Verification (Phase 3, T007)**: Depends on Phase 2. Completed.
- **Quality Gates (Phase 4, T008–T009)**: Depends on Phase 2 & 3. Completed.
- **Polish (Phase 5, T010–T012)**: Initial merge readiness. Completed.
- **Hotspots (Phase 6, T013–T019)**: Depends on T001 (`FastMath.h`). T013 through T019 can be implemented incrementally.
- **Verification & Gates (Phase 6, T020–T022)**: Validates extended suite.

