---
description: "Task list for Feature 032: replace vendor/ snapshots with pinned CPM dependencies"
---

# Tasks: Replace `vendor/` Snapshots with Pinned CPM Dependencies

**Input**: Design documents from `/specs/032-cpm-vendor-removal/` (`spec.md`, `plan.md`, `research.md`)

**Organization**: P0 investigates (read-only); P1 wires header-only deps; P2 wires built deps; P3 wires JUCE; P4 deletes + docs. Each task leaves `./scripts/build.sh` green (fresh-cache AND warm-cache runs where noted).

---

## P0. Audit (read-only, no code changes)

**Purpose**: Answer `research.md` RQ-001–RQ-004 with measurements.

- [x] T001 Record signalsmith upstream layout + per-dep pinned versions read off snapshots (miniaudio/rtmidi/dr_libs/hiir/flac/lame/JUCE). No upgrades — version-neutral pins only.
- [x] T002 Verify lame tarball URL + wrapper source parity; verify JUCE tag + shallow-fetch mechanics; grep stray `vendor/` references (scripts/docs/CI/IDE).

**Checkpoint**: `research.md` verdicts filled; no source files touched.

---

## P1. Header-only deps (include dirs only)

**Purpose**: Swap the four header-only snapshots with zero build-system risk.

- [x] T003 CPM blocks for dr_libs + miniaudio + hiir + signalsmith (per T001 URLs/pins) in `cmake/dependencies.cmake`; repoint include dirs; keep `vendor/` until P4. License rows/texts land FIRST (FR-003 ordering).
- [x] T004 Re-run the 031 oversampler null harness (1x–8x) against fetched hiir; audio-file IO smoke (dr_libs/miniaudio read paths) green.

**Checkpoint**: build + smoke green; both header sets resolve to fetched copies (guard: temporarily rename check or `-I` order proof).

---

## P2. Built deps (flac/rtmidi/lame)

**Purpose**: Swap compiled snapshots keeping linkage and options identical.

- [x] T005 CPM blocks for flac + rtmidi (upstream CMake, tests/examples/docs OFF as today); move thin lame wrapper to `cmake/lame.cmake` against the tarball URL+HASH; rewire `target_link_libraries` identically (static lame into SHARED engine, LGPL note 7 re-verified).
- [x] T006 MP3/FLAC export + import smoke (spec 001 surface) green; MIDI smoke green; fresh-cache full gate green.

**Checkpoint**: build + smoke green; no `add_subdirectory(vendor/...)` remains.

---

## P3. JUCE (shallow, last)

**Purpose**: Swap the 92 MB snapshot without breaking hosting, scanning, or the Pentad VST3.

- [x] T007 CPM `juce-framework/JUCE` tag `8.0.14` shallow; `juce_add_plugin` flows (Pentad VST3 target + pluginhost) build; plugin-scan smoke + hosted-plugin smoke green. License posture unchanged (AGPL terms as today).
- [x] T008 Fresh-cache full gate green (measures the cold-fetch cost; records it in `BUILD.md`).

**Checkpoint**: build + smoke green incl. VST3 bundle existing at the documented path.

---

## P4. Delete + docs

**Purpose**: Land FR-001/FR-005 and close the spec.

- [x] T009 Delete `vendor/`; grep-gate the tree for `vendor/` (CMake/sources/scripts/docs, minus history + `CPM_SOURCE_CACHE` docs); update `BUILD.md` (cache story: warm/cold, pre-warm step, fail-fast message); final fresh-cache gate green.
- [x] T010 Final license audit (9/9 rows + texts in `LICENSES/`); tasks checked off (this file; spec folder untracked until merge per practice).

**Checkpoint**: `ls vendor/` fails; gate green; no C ABI diff.

---

## Dependencies & Execution Order

- P0 → everything (pins gate all wiring).
- P1 ∥ P2 after P0 (disjoint files; shared gate at the end).
- P3 after P1+P2 green (JUCE fetch dominates configure time; isolate it).
- P4 last (delete only when every consumer resolves fetched).
