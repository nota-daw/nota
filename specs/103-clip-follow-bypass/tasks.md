# Tasks: Bypass one Session clip's Follow configuration (spec 103, issue #499)

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[contracts/c-abi.md](contracts/c-abi.md), [contracts/ui-and-mcp.md](contracts/ui-and-mcp.md)

## Phase 1: Foundation — the flag end to end (blocks every story)

- [x] T001 Engine: `SessionSlot::followBypass` — `src/native/nota.engine/src/engine/Track.h`
- [x] T002 C ABI: `NotaSessionClipProps.follow_bypass` (appended, doc comment) — `src/native/nota.engine/include/nota/nota_engine.h`
- [x] T003 Engine: get/set `follow_bypass` in `sessionClipProps` / `setSessionClipProps` — `src/native/nota.engine/src/engine/Engine_Session.cpp`
- [x] T004 [P] C#: `NotaSessionClipProps.FollowBypass` (appended) — `src/managed/Nota.Application/Engine/EngineContracts.cs`

## Phase 2: User Story 1 + 2 — hold the clip, resume without catch-up (P1)

- [x] T005 [US1] `SessionPlayer`: audio-thread-only `followHeld`, `followBase`; reset in `maybeApply` on launch — `src/native/nota.engine/src/engine/SessionPlayer.h`, `SessionPlayer.cpp`
- [x] T006 [US1][US2] `sessionPreApply`: bypassed → set `followHeld`, skip; un-bypassed with `followHeld` → `followBase = floor(played/due)*due`; fire when `played - followBase + block/2 >= due`; None outcome resets base/held — `Engine_Session.cpp`
- [x] T007 [US1][US2] Smoke test: bypass holds a clip ≥ 4× its follow time while another track's clip follows; global switch unchanged; manual launch works; resume past the deadline fires at the next multiple (not at once); resume before the deadline fires on time; props round-trip; undo restores; copy-slot carries it — `tests/Nota.SmokeTest/Program.cs`

## Phase 3: User Story 3 — see and toggle it (P2)

- [x] T008 [US3] Inspector Follow head: `SwitchTrack` (on = follows, dim when global off), "Bypassed" state text, section inactive-but-editable while bypassed — `src/managed/Nota.App/SessionView.Inspector.cs`
- [x] T009 [US3] Grid slot: Bypass glyph left of the length when bypassed and A or B ≠ None — `src/managed/Nota.App/SessionView.Grid.cs`
- [x] T010 [US3] Slot context menu: "Bypass follow actions" / "Restore follow actions" — `SessionView.Grid.cs`
- [x] T011 [P] [US3] Strings — `src/managed/Nota.Application/Localization/en.json`

## Phase 4: User Story 4 — save / reopen (P2)

- [x] T012 [US4] `SessionSlotDto.FollowBypass`; `WithClipProps` / `ApplyClipProps`; format v35 + no-op migration — `ProjectModel.cs`, `ProjectService.cs`, `ProjectMigrations.cs`
- [x] T013 [US4] Smoke test: bypass survives save/load; a clip without it loads unbypassed — `tests/Nota.SmokeTest/Program.cs`

## Phase 5: User Story 5 — MCP (P3)

- [x] T014 [US5] `get_session` clip `FollowBypass`; `set_session_clip(followBypass)` — `src/managed/Nota.Mcp/Tools/SessionTools.cs`

## Phase 6: Polish & verification

- [x] T015 Visual check in the headless app: inspector (follow on, bypassed, off globally, both) and the slot mark; screenshots for the PR
- [x] T016 CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs Session page EN + RU (nota-docs)
- [x] T017 `scripts/build.sh` green (smoke test included)

## Dependencies

T001–T004 → everything. T005 → T006 → T007. T008–T011 after T004. T012 after T004. T014 after T004.

## Nota Compliance

- [x] No new source files in `src/` (SPDX n/a); no new dependency
- [x] Audio thread: only POD reads + audio-thread-only scalars; no alloc / lock / I/O
- [x] C ABI: `nota_engine.h` updated, struct size unchanged, smoke test covers it
- [x] UI follows DESIGN.md (existing controls only)
