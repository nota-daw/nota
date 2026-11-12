# Tasks: Bypass one Session clip's follow actions

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Stories 1 and 2 — hold a clip, no catch-up (P1)

- [x] T001 `SessionSlot::followBypass`, `NotaSessionClipProps::follow_bypass`, managed `FollowBypass` (FR-001)
- [x] T002 `SessionPlayer::followFrom`; the bypass in `Engine::sessionPreApply` (FR-002, FR-003)
- [x] T003 Bypass row, **Bypassed** head and dimmed rules in `SessionView.Inspector` (FR-004)
- [x] T004 `FollowBypassTests` in `tests/Nota.SmokeTest`: held clip, other clip follows, settings kept, hand launch, no catch-up; in the full run and alone as `--follow-bypass`

## Phase 2: User Story 3 — saved with the project (P2)

- [x] T005 `SessionSlotDto.FollowBypass`, capture and apply; format 33 with a no-op migration (FR-005)
- [x] T006 Smoke test: save → reopen, a v32 project; `CueOutputTests` follow the current format
- [x] T007 MCP `get_session` / `set_session_clip` (FR-006)

## Phase 3: Polish

- [x] T008 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T009 Native engine and `Nota.sln` build on Linux; the smoke test's `--follow-bypass`, `--cue`, `--session-input` and `--localization` runs pass
