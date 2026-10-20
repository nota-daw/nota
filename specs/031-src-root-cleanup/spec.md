# Feature Specification: `src/` Root Cleanup — Finish the 030 Modularization

**Feature Branch**: `031-src-root-cleanup`

**Created**: 2026-10-19

**Status**: Implemented

**Input**: User description: "Can the files in the src/ root be split up further?" — after spec 030 (completed: 25 effects, 18 instruments, 6 MIDI FX, 4 racks, engine models, thread pool, parallel render), 13 files remain directly in `src/native/nota.engine/src/`.

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`)
- [ ] UI (Avalonia)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR-6 audio-thread safety — no allocation/lock/block on the audio thread; AR-7 pure C ABI),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (AGPL-3.0-only SPDX headers on every new file),
[`BUILD.md`](../../BUILD.md).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Empty Root, Domain-Owned Sources (Priority: P1) 🎯 MVP

As a C++ audio developer working on Nota's native engine, I want no orphan sources left in `src/` root — engine translation units live in `src/engine/`, bridge headers in `pluginhost/bridge/`, and the last two flat DSP headers (`CompensationDelay.h`, `Oversampler.h`) migrated onto their `dsp/primitives/` twins — so that every file has an owning domain folder and the 030 layout holds without exceptions.

**Why this priority**: 030 acceptance scenario 1 requires sources "cleanly separated" into domain folders. 13 files in the root (9 already-split `Engine_*.cpp` TUs, a ~1800-line god header, 2 legacy DSP twins, 1 bridge header) are the remaining exceptions.

**Independent Test**: `ls src/native/nota.engine/src/*.h src/native/nota.engine/src/*.cpp` shows only `Engine.h` (the class definition itself, which cannot be split across headers); `./scripts/build.sh` green; `tests/Nota.SmokeTest` 3704 checks pass.

**Acceptance Scenarios**:

1. **Given** `src/native/nota.engine/src/engine/`, **When** listing it, **Then** it contains `Engine.cpp` and all `Engine_{Automation,Devices,Modulation,Rack,Recording,Render,Session,Tracks}.cpp` moved verbatim, wired in `CMakeLists.txt`, with the old root paths removed.
2. **Given** `src/native/nota.engine/src/engine/EngineTypes.h`, **When** included, **Then** it provides the free types extracted verbatim from `Engine.h` (SPSC queue aliases, `WarpBuildCursor`, standalone structs/enums), and `Engine.h` still declares the full `Engine` class compiling unchanged.
3. **Given** `Track::pdc`, **When** rendering through plugin-delay compensation, **Then** output is sample-identical to the legacy flat `CompensationDelay` (standalone null-test, bit-identical or documented ≤ −100 dB RMS with cause), and `src/CompensationDelay.h` is gone.
4. **Given** Vintage/Amp/Forge oversampling, **When** rendering saturation at 1x/2x/4x/8x, **Then** output matches the legacy flat `Oversampler` per the project −60 dB rule, and `src/Oversampler.h` is unified or formally deprecated with a documented reason.
5. **Given** `pluginhost/bridge/PluginHostBridge.h`, **When** building, **Then** all 6 `Engine_*.cpp` users resolve it via include path with no relative-path includes, and the root copy is gone.
6. **Given** the public C ABI header `include/nota/nota_engine.h`, **When** built, **Then** zero changes (this spec touches internal layout only).

### Edge Cases

- `Oversampler.h` audit may find the flat and the primitive define the same symbols (ODR violation today) — then unification is a bug fix, not a refactor, and takes precedence over move tasks.
- `Track::pdc` migration must preserve `ready_`/`delay_` atomic semantics the engine relies on (PDC skip logic reads them); if the primitive's `isIdle` disagrees with the flat's early-return conditions, keep the flat and document why.
- `Engine.h` class definition itself stays (C++ requires one class definition site); only free types move out.
- Message-thread-only setup code (`reserveScratch`, `ensureTrackScratch`) must keep running before the live-graph swap in `publishRaw` (AR-6); moves must not reorder it after the swap.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: No `.h`/`.cpp` remains in `src/` root except `Engine.h` (verified by listing).
- **FR-002**: Every moved file keeps byte-identical logic (mechanical move; `git diff` shows renames + include/CMake adjustments only), except the two twin migrations which each carry a passing equivalence harness.
- **FR-003**: Every new file (notably `EngineTypes.h`) carries the AGPL-3.0-only two-line SPDX header.
- **FR-004**: `CMakeLists.txt` source list and include directories updated in the same commit as each move (the build never references a deleted path).
- **FR-005**: `./scripts/build.sh` green + smoke 3704 PASS before each commit; tasks checked off in `specs/031-src-root-cleanup/tasks.md`.

### Key Entities

- `src/engine/Engine_*.cpp` (9 moved TUs), `src/engine/EngineTypes.h` (new), `dsp::CompensationDelay` / `dsp::Oversampler` (migration targets), `pluginhost/bridge/PluginHostBridge.h` (new home).

## Success Criteria *(mandatory)*

- [ ] SC-001: Root listing shows only `Engine.h`.
- [ ] SC-002: Equivalence harnesses for PDC + oversampler migrations recorded with numbers (RMS dB + maxdiff).
- [ ] SC-003: Full gate green; no new smoke failures; no C ABI diff.
