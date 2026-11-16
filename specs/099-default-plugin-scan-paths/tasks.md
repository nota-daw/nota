---
description: "Task list template for feature implementation"
---

# Tasks: Default plugin scan paths

**Input**: Design documents from `/specs/099-default-plugin-scan-paths/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Yes — the spec requires smoke-test coverage for fresh / backfill / remove / restore (Assumptions, SC-001–SC-003).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Engine (C++)**: `src/native/nota.engine/` (C ABI surface: `include/nota/nota_engine.h`; JUCE only in `pluginhost/`)
- **Managed app (C#)**: `src/managed/<Project>/`
- **UI (Avalonia)**: `src/managed/Nota.App/` + `src/managed/Nota.Presentation/`
- **Tests**: `tests/Nota.SmokeTest/` — the end-to-end gate every PR must pass

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the starting point before any behavior changes

- [X] T001 Confirm the scanner's format-name strings (`"AudioUnit"`, `"VST3"` in `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp`) so the per-format provider keys on the same names the scan loop uses

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Per-format default provider + C ABI + managed plumbing; no UI yet

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 [P] Add the per-format default provider (`defaultScanPaths(format)` as a runtime JUCE `getDefaultLocationsToSearch()` query returning absolute paths) in `src/native/nota.engine/pluginhost/scanner/PluginCatalog.h` and `src/native/nota.engine/pluginhost/scanner/PluginCatalog.cpp`
- [X] T003 [P] Declare `nota_pluginhost_default_scan_path_count`, `nota_pluginhost_default_scan_path`, `nota_pluginhost_restore_default_scan_paths` in `src/native/nota.engine/include/nota/nota_engine.h` per `specs/099-default-plugin-scan-paths/contracts/c-abi.md`
- [X] T004 Implement the three ABI functions in `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp` (thread-local string buffers, unknown format → 0/NULL) per `specs/099-default-plugin-scan-paths/contracts/c-abi.md`
- [X] T005 [P] Add P/Invoke entries in `src/managed/Nota.Infrastructure/Interop/NativeMethods.PluginHost.cs` (UTF-8 marshalling, same `EntryPoint` names as the C ABI)
- [X] T006 [P] Add checked wrappers in `src/managed/Nota.Infrastructure/Interop/NotaEngine.PluginHost.cs` following the existing `Marshal.PtrToStringUTF8` pattern
- [X] T007 Extend `IPluginCatalog` in `src/managed/Nota.Application/Ports/IPluginCatalog.cs` and its implementation in `src/managed/Nota.Infrastructure/Interop/PluginCatalog.cs` (`DefaultScanPathCount(format)`, `DefaultScanPath(format, index)`, `RestoreDefaultScanPaths(format)`)
- [X] T008 Backfill missing OS defaults inside `PluginCatalog::loadScanPathsOnce` in `src/native/nota.engine/pluginhost/scanner/PluginCatalog.cpp` (append at end, save only when something was added, keep existing order)
- [X] T009 Update the smoke-test `IPluginCatalog` fakes so they compile in `tests/Nota.SmokeTest/BrowserTestSupport.cs` and `tests/Nota.SmokeTest/PaletteTests.cs`
- [X] T010 [P] Add ABI contract smoke tests in `tests/Nota.SmokeTest/DefaultScanPathsTests.cs` (AGPL SPDX header): `VST3` count ≥ 1 with absolute unique paths; unknown format (`CLAP`) → count 0 / NULL / restore 0

**Checkpoint**: Foundation ready — defaults are seeded/backfilled and queryable, UI work can now begin

---

## Phase 3: User Story 1 - Fresh install shows where VST3s come from (Priority: P1) 🎯 MVP

**Goal**: Settings → Plug-ins lists OS VST3 defaults + store folder, badged, including missing folders

**Independent Test**: Fresh scratch profile (`NOTA_DATA_DIR` under /tmp) shows every OS default plus the store folder with `Default` badges and no user action

### Tests for User Story 1

- [X] T011 [P] [US1] Fresh-profile smoke test in `tests/Nota.SmokeTest/DefaultScanPathsTests.cs`: scratch data dir lists OS defaults + store folder, each flagged as default

### Implementation for User Story 1

- [X] T012 [US1] Tag defaults via the new API in `src/managed/Nota.App/PreferencesWindow.cs` (`IsDefault` recognizes OS defaults + store; list shows missing folders as-is, no existence filter)
- [X] T013 [US1] Reword the scan-folders caption (`extra_folders_are_searched_for`) in `src/managed/Nota.Application/Localization/en.json` plus its use in `src/managed/Nota.App/PreferencesWindow.cs`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Remove a default, then restore it (Priority: P1)

**Goal**: Defaults are removable; "Restore defaults" re-adds missing ones (OS + store) without touching user folders

**Independent Test**: Remove one default, add one custom folder, restore → default back, custom kept, no duplicates; second restore → no change

### Tests for User Story 2

- [X] T014 [P] [US2] Remove→restore roundtrip smoke test in `tests/Nota.SmokeTest/DefaultScanPathsTests.cs` (user paths preserved, no duplicates, double restore returns 0 / no-op)

### Implementation for User Story 2

- [X] T015 [US2] Add restore orchestration (native OS restore + existing store ensure) in `src/managed/Nota.App/PluginScan.cs`
- [X] T016 [US2] Add the "Restore defaults" button (including its `en.json` label) and keep Remove enabled for defaults in `src/managed/Nota.App/PreferencesWindow.cs` (re-render the folder well after restore)

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - Existing install gains the defaults on upgrade (Priority: P2)

**Goal**: Pre-feature folder lists are backfilled exactly once, customs and order preserved

**Independent Test**: Seed a pre-feature `scanpaths.txt` (customs + store, no OS defaults), launch, relaunch → defaults appended once, customs untouched, second launch unchanged

### Tests for User Story 3

- [X] T017 [P] [US3] Upgrade-backfill smoke test in `tests/Nota.SmokeTest/DefaultScanPathsTests.cs` (seeded old list → exactly-once append → relaunch stable; rescan-before/after yields identical catalog counts as the automated FR-008 pin, reusing the existing scan fixture/worker pattern)

**Checkpoint**: All user stories should now be independently functional

---

## Phase 7: PR review fixes (#623: P1 + 3×P2)

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Docs, gates, and final verification

- [X] T018 Add the `CHANGELOG.md` `[Unreleased]` entry for the visible settings change in `CHANGELOG.md`
- [X] T019 Document the per-format default provider seam in `ARCHITECTURE.md` (§ Plugin hosting, one paragraph)
- [X] T020 Run `specs/099-default-plugin-scan-paths/quickstart.md` validation (fresh / backfill / roundtrip / scan-unchanged), including confirming non-English `Loc` fallback for the reworded caption and new label
- [X] T021 Run `scripts/build.sh` (or `scripts/build-linux.sh` / `scripts/build-win.ps1` per OS) until native + managed + smoke test are green

### Nota closing tasks *(always include — these are the PR gates)*

- [X] T022 AGPL-3.0-only SPDX two-line header on every new source file (see CONTRIBUTING.md) — applies to `tests/Nota.SmokeTest/DefaultScanPathsTests.cs`; no new native files expected
- [X] T023 New dependency, if any: license checked + row added to `LICENSES/third-party.md`; none expected → verify and mark N/A
- [X] T024 User-visible capability change → run the `nota-features` skill (FEATURES.md) and `nota-docs` skill (settings manual) or record why N/A; no shortcut changes → `nota-shortcuts` N/A

---

## Phase 7: PR review fixes (#623: P1 + 3×P2)

**Purpose**: Removal persistence, CI coverage, translation-key rule, honest scan identity

- [X] T025 Native removed-set in `src/native/nota.engine/pluginhost/scanner/PluginCatalog.h` and `src/native/nota.engine/pluginhost/scanner/PluginCatalog.cpp` (`scanpaths-removed.txt` load/save; record on `removeScanPath`; backfill skips listed; `restoreDefaultScanPaths` and `addScanPath` clear)
- [X] T026 Query ABI `nota_pluginhost_is_scan_path_removed` in `src/native/nota.engine/include/nota/nota_engine.h` and `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp` (1/0, NULL → 0, never negative)
- [X] T027 Managed removal-awareness: P/Invoke + `NotaEngine.IsScanPathRemoved` (`NativeMethods.PluginHost.cs`, `NotaEngine.PluginHost.cs`), `IPluginCatalog` DIM + impl (`IPluginCatalog.cs`, `PluginCatalog.cs`); `PluginScan.EnsureStoreScanPath` skips a removed store folder while `PluginScan.RestoreDefaultScanPaths` re-adds it via explicit `AddScanPath` in `src/managed/Nota.App/PluginScan.cs`
- [X] T028 Translation-key rule: revert `preferences.extra_folders_are_searched_for` text, add `preferences.folders_are_searched_for` with the new caption in `src/managed/Nota.Application/Localization/en.json`, point the PluginsPane caption at it in `src/managed/Nota.App/PreferencesWindow.cs`
- [X] T029 CI coverage: run both entries with fresh scratch dirs in `scripts/build.sh`, `scripts/build-linux.sh` and `scripts/build-win.ps1`, mirroring the `--scan-scale` lines
- [X] T030 Blocklist-skip test in `tests/Nota.SmokeTest/DefaultScanPathsTests.cs` (`RunUpgrade` seeds `scanpaths-removed.txt`: a listed default is NOT backfilled, an unlisted missing one is)
- [X] T031 Child-process scan identity in `tests/Nota.SmokeTest/DefaultScanPathsTests.cs` and `tests/Nota.SmokeTest/Program.cs`: new `--default-scan-paths-identity <worker>` entry spawns three fresh child scans (`--default-scan-paths-scan <idsFile>`, plugins.xml deleted first, 10-min kill like `HostedRenderPoolTests`) across remove-all/restore and compares the id sets; drop the old in-process rescan asserts from `RunUpgrade`
- [X] T032 Docs: `specs/099-default-plugin-scan-paths/quickstart.md` restart-persistence manual check; extend the `CHANGELOG.md` `[Unreleased]` entry with the removal-persistence clause

**Checkpoint**: All four review findings resolved; new tests green; full suite shows no new failures

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Stories (Phases 3–5)**: All depend on Foundational completion
  - Then proceed in parallel (if staffed) or sequentially P1 → P1 → P2
  - US3's implementation lives in Foundational (T008); its phase adds the upgrade-scenario proof
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: After Foundational — no dependencies on other stories
- **User Story 2 (P1)**: After Foundational — builds on the US1 listing visually, independently testable via the API-level roundtrip
- **User Story 3 (P2)**: After Foundational — backfill code ships in T008; this phase proves the upgrade path

### Within Each User Story

- Tests written alongside implementation; contract tests (T010) prove the ABI before UI consumes it
- Provider/ABI before managed wrappers before UI
- Story complete before moving to next priority (MVP = US1)

### Parallel Opportunities

- T002 + T003 + T005 + T006: different files, contract-fixed signatures — parallel
- T010 + T011 + T014 + T017: all append to `tests/Nota.SmokeTest/DefaultScanPathsTests.cs` — same file, run sequentially to avoid edit conflicts (only T010 is marked [P] against Phase 2 files)
- US1 UI (T012) vs US2 orchestration (T015): different files — parallel once Foundation is done

---

## Parallel Example: Foundational phase

```bash
# Same file pairs stay sequential; cross-file work goes parallel:
Task: "Add the per-format default provider in PluginCatalog.h/.cpp (T002)"
Task: "Declare ABI functions in nota_engine.h (T003)"
Task: "Add P/Invoke entries in NativeMethods.PluginHost.cs (T005)"
Task: "Add checked wrappers in NotaEngine.PluginHost.cs (T006)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T010)
3. Complete Phase 3: User Story 1 (T011–T013)
4. **STOP and VALIDATE**: Fresh scratch profile lists defaults; contract tests green
5. Demo the settings pane if ready

### Incremental Delivery

1. Setup + Foundational → defaults seedable and queryable
2. Add US1 → listing visible → test → demo (MVP!)
3. Add US2 → restore works → test → demo
4. Add US3 → upgrade proven → test
5. Polish → changelog, arch paragraph, full build green → PR (`Closes #622`)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate the story independently
- `scanpaths.txt` format is unchanged — old builds stay readable, no migration code
- Unknown formats (`CLAP` today) are exercised only as negative contract cases; no scanning code
