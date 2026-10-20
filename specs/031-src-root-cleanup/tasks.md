---
description: "Root src/ cleanup: move Engine_* TUs, split Engine.h types, migrate legacy DSP twins, rehome PluginHostBridge"
---

# Tasks: `src/` Root Cleanup (spec 031)

**Input**: Design documents from `/specs/031-src-root-cleanup/` (`spec.md`, `plan.md`, `research.md`)

**Organization**: Tasks are ordered so each leaves `./scripts/build.sh` and `tests/Nota.SmokeTest` (3704 checks) 100% green. P0 is read-only investigation; P1/P2/P5 are mechanical moves; P3/P4 carry equivalence tests.

---

## P0. Audit (read-only, no code changes)

**Purpose**: Resolve the two open questions from `research.md` with measurements, not guesses.

- [x] T001 Audit `Oversampler.h` resolution: for each includer (`Amp.h`, `Vintage.h`, `Forge.h`, `dsp/primitives/Oversampler.cpp`) determine WHICH header the quoted include resolves to (same-dir-first vs `-I` order), and check the link for duplicate/ODR-violating symbols between `src/Oversampler.h` and `dsp/primitives/Oversampler.h`. Record the verdict (unify on the primitive vs keep both with a documented split) in `research.md`.
- [x] T002 Compare `src/CompensationDelay.h` vs `dsp/primitives/CompensationDelay.h`: list every behavioral difference (read-index semantics, `isIdle`, latency reporting, `ready_` handling). Verdict: drop-in migration for `Track::pdc` or adapter needed.

**Checkpoint**: `research.md` holds both verdicts; no source files touched.

---

## P1. Mechanical moves (no logic changes)

**Purpose**: Empty the root of already-split translation units.

- [x] T003 Move `Engine.cpp` + `Engine_{Automation,Devices,Modulation,Rack,Recording,Render,Session,Tracks}.cpp` (9 TUs, verbatim, no logic changes) into `src/native/nota.engine/src/engine/`, update `CMakeLists.txt` source paths. Old paths removed via `git rm`.
- [x] T004 Move `PluginHostBridge.h` (verbatim) into `pluginhost/bridge/`, add `pluginhost/` to the `nota_engine` include dirs if needed so bare `#include "PluginHostBridge.h"` keeps resolving for the 6 `Engine_*.cpp` users. No logic changes.

**Checkpoint**: `cmake --build` green; smoke green.

---

## P2. `Engine.h` type split (no logic changes)

**Purpose**: Shrink the ~1800-line god header by extracting standalone types.

- [x] T005 Extract free types from `Engine.h` into `src/native/nota.engine/src/engine/EngineTypes.h` (SPSC queue aliases, `WarpBuildCursor`, other structs/enums not depending on the `Engine` class). `Engine.h` keeps the `Engine` class + `LeafWork` (member coupling). Verbatim move; no logic changes.

**Checkpoint**: `cmake --build` green; smoke green.

---

## P3–P4. Legacy twin migrations (equivalence-tested)

**Purpose**: Remove the last two flat DSP headers; each migration proves sample-level equivalence first.

- [x] T006 Migrate `Track::pdc` to `dsp::CompensationDelay`, delete `src/CompensationDelay.h`. Standalone null-test old-vs-new (impulse + tail through the PDC line at several delay settings, both `ready_` states): bit-identical or documented ≤ −100 dB RMS with cause. Per T041 lesson, verify read-index semantics explicitly — do NOT assume `read(d)` equivalence.
- [x] T007 Unify oversampling on `dsp::primitives::Oversampler` (per T001 verdict), delete or formally deprecate `src/Oversampler.h`. Null-test old-vs-new through Vintage/Amp/Forge saturation at 1x/2x/4x/8x: follow the project −60 dB rule (FastMath downstream only; coefficient solving stays `std::`-exact); revert any substitution that costs more.

**Checkpoint**: `ls src/*.h src/*.cpp` shows only `Engine.h` (+ audit the remainder); smoke green.

---

## Dependencies & Execution Order

- P0 first (answers gate P3/P4 scope).
- P1, P2, P5(T004) independent of each other after P0 — parallelizable.
- P3, P4 depend on P0 verdicts; each needs its equivalence harness before the swap.
- Final: `scripts/build.sh` green + tasks checked off (this file is the checklist; the spec folder itself stays untracked until the branch merges, per 030 practice).
