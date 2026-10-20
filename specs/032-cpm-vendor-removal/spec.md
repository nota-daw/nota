# Feature Specification: Replace `vendor/` Snapshots with Pinned CPM Dependencies

**Feature Branch**: `032-cpm-vendor-removal`

**Created**: 2026-10-19

**Status**: Implemented

**Input**: User description: "We want to remove the entire vendor directory and replace it with CPM dependencies".

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`)
- [ ] UI (Avalonia)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR-7 pure C ABI),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (dependency policy — read before adding any source),
[`BUILD.md`](../../BUILD.md) (offline-build story must be updated),
`LICENSES/third-party.md` (one row per dependency — mandatory for every swap).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - No Vendored Sources, Reproducible Fetches (Priority: P1) 🎯 MVP

As a Nota maintainer, I want `src/native/nota.engine/vendor/` gone (9 snapshots, ~104 MB, JUCE alone 92 MB), with every dependency fetched by CPM from a pinned URL/tag + hash into the build dir (like `nlohmann_json` 3.12.0 today), so that upstream updates are version bumps, not vendored directory merges, and license texts live in `LICENSES/` instead of scattered snapshots.

**Why this priority**: Vendored snapshots rot silently (JUCE 8.0.14, LAME 3.100,ateurs unknown-pinned miniaudio/rtmidi/dr_libs), duplicate maintenance (two `Oversampler`-style twins were already found in 031), and bloat every clone. CPM infra already exists (`cmake/dependencies.cmake`, `CPM_VERSION v0.43.2` with hash pin).

**Independent Test**: Fresh clone on a machine with empty CPM cache: `./scripts/build.sh` green; `tests/Nota.SmokeTest` 3704 checks pass; `git status` shows no `vendor/`; license audit (`LICENSES/third-party.md` rows + texts) complete.

**Acceptance Scenarios**:

1. **Given** a fresh clone with network, **When** running `./scripts/build.sh`, **Then** CPM fetches all 9 dependencies pinned (URL+HASH or GIT_TAG+hash) and the full build + smoke pass with zero `vendor/` references in CMake or sources.
2. **Given** `dr_libs`/`miniaudio`/`hiir`/`signalsmith` header-only deps, **When** building, **Then** only include dirs are wired (no built artifacts), matching today's usage.
3. **Given** `flac`/`rtmidi` (upstream CMake) and `lame` (no upstream CMake), **When** building, **Then** flac/rtmidi build via their own CMake with tests/examples off (as today), and lame builds via our thin wrapper moved to `cmake/` (same sources, same static-into-shared linkage, LGPL note 7 unchanged).
4. **Given** JUCE 8.0.14, **When** building both the default target and `-DNOTA_BUILD_PENTAD_VST3=ON`, **Then** `juce_add_plugin` flows work unchanged (shallow fetch to bound the 92 MB cost) and hosted-plugin smoke tests pass.
5. **Given** an offline machine with a warm `CPM_SOURCE_CACHE`, **When** building, **Then** no network is touched (documented in `BUILD.md`); with a cold cache and no network, configure fails fast with a clear message (current nlohmann behavior, kept).

### Edge Cases

- LAME has no upstream CMake and upstream tarballs live on SourceForge (reliability): keep our wrapper `CMakeLists`, fetch the 3.100 tarball by URL+HASH like nlohmann_json; LGPL-2.0-or-later duties unchanged (static lib inside the SHARED engine dylib, note 7 stays accurate — re-verify the note text).
- JUCE full-clone cost: `GIT_SHALLOW TRUE` (or tarball if upstream provides versioned archives); never fetch submodules we don't build.
- `signalsmith-linear` vs `signalsmith-stretch` upstream layout unknown (one repo or two?) — P0 verifies before wiring.
- miniaudio/rtmidi/dr_libs/hiir pinned versions must be read off the CURRENT snapshots (version headers/changelogs) so the swap is version-neutral first, upgrades later.
- Any `scripts/` or docs references to `vendor/` paths must move with the code (grep-gated).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `vendor/` deleted; no `vendor/` string remains in CMake, sources, scripts, or docs (grep-verified).
- **FR-002**: Every dependency pinned (URL+HASH preferred; GIT_TAG + commit hash where no tarball exists) in `cmake/dependencies.cmake`; no floating `main`/`master`.
- **FR-003**: `LICENSES/third-party.md` gains/updates one row per dependency (upstream URL, pinned version, license); license texts preserved in `LICENSES/` (hiir WTFPL, lame LGPL note, JUCE AGPL terms as today).
- **FR-004**: Audio behavior unchanged: oversampler null harness (031, 1x–8x bit-identical) re-run against the fetched hiir; MP3/FLAC export smoke tests green (spec 001 surface); plugin-scan smoke green (JUCE).
- **FR-005**: `BUILD.md` documents the CPM cache story (warm/cold, `CPM_SOURCE_CACHE`, first-configure network requirement).

### Key Entities

- `cmake/dependencies.cmake` (all pins), `cmake/lame.cmake` or equivalent wrapper (moved thin build), `LICENSES/third-party.md` + texts, per-dep smoke coverage.

## Success Criteria *(mandatory)*

- [ ] SC-001: Fresh-clone full gate green with no `vendor/` on disk (except CPM cache outside the repo).
- [ ] SC-002: License audit complete (9 deps mapped, texts present).
- [ ] SC-003: No C ABI diff; smoke 3704 PASS.
