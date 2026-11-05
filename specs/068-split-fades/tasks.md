# Tasks: Fade audio clips when they are split

**Input**: Design documents from `/specs/068-split-fades/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: `tests/Nota.SmokeTest/SplitFadeTests.cs` is required by FR-001 through FR-009 and SC-001 through SC-003.

## Phase 1: Engine fade

- [x] T001 Add `raiseSplitEdge` (4 ms, raise only, cap at half the piece and at remaining room) in `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- [x] T002 Call it from `Engine::splitClip` and `Engine::splitClipsAtRange` only when `splitFades_` is on, and only on edges the cut created, in the same publish as the split
- [x] T003 Add `Engine::fadeTouchingSplits` (abutting audio clips, either side selected, one undo, false when nothing changed) and the C ABI `nota_engine_set_split_fades`, `nota_engine_split_fades`, `nota_clips_fade_splits`

## Phase 2: App preference and command

- [x] T004 Add `SplitFades` (default true) on `IAudioEngine` and `Settings`, push it at startup and from the Appearance switch **Fade audio clips when splitting**
- [x] T005 Add `edit.fadeSplits` with an empty shortcut, bind it, and add **Fade at splits** to the audio clip menu. Do not bind Shift+X
- [x] T006 Add the French strings in `src/managed/Nota.App/Languages/fr.json`

## Phase 3: Proof and docs

- [x] T007 Add `SplitFadeTests` and the `--split-fades` hook; turn the flag off in `WarpSplitTests`; expect no room for a left release in the existing ADSR split fixture
- [x] T008 Update `CHANGELOG.md`, `FEATURES.md`, and the EN+RU docs for the split, the ADSR section, and Appearance

## Phase 4: Verification

- [x] T009 `CC=gcc CXX=g++ scripts/build-linux.sh` passes on Linux
