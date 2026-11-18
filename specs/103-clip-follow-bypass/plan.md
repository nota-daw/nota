# Implementation Plan: Bypass one Session clip's Follow configuration

**Branch**: `feat/clip-follow-bypass` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/103-clip-follow-bypass/spec.md` (issue nota-daw/nota#499)

## Summary

Add a per-clip `followBypass` flag to the Session slot. The audio thread skips the clip's Follow
check while it is set, and when it clears, re-bases the follow count on the last multiple of the
follow time already played, so the next action lands where it would have without the bypass —
never as a catch-up (research R1–R2). The flag rides the existing clip-props path (C ABI struct,
one undo step, slot copies), is saved in the project (v35), shown in the inspector's Follow head
as a switch with a "Bypassed" state, marked on the grid slot when the clip has a Follow action,
toggled from the slot context menu, and exposed over MCP.

## Nota: Read Before Planning

- Engine: `ARCHITECTURE.md` AR-5 (the flag is a POD field of the published slot snapshot), AR-6
  (two new audio-thread-only scalars in `SessionPlayer`, no alloc/lock/I/O), AR-7 (C ABI struct
  grows one field; size unchanged).
- UI: `DESIGN.md` / nota-design — reuse `SwitchTrack`, `Inactive`, `Glyph.Draw`, `MenuKit`.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (app, infrastructure, MCP, smoke test)

**Primary Dependencies**: none new

**Storage**: project file `project.json`, `SessionSlotDto.FollowBypass`, format v34 → v35 (no-op migration)

**Audio-Thread Impact**: `sessionPreApply` reads one more bool from the slot snapshot and keeps two
audio-thread-only scalars (`followHeld`, `followBase`) in the per-track `SessionPlayer`; reset in
`maybeApply`. No allocation, lock or I/O. Cost: a branch and a `floor` once per resume.

**Testing**: `tests/Nota.SmokeTest` (offline render through the C ABI), `scripts/build.sh`; UI by the
headless-app harness and a screenshot.

**Target Platform**: macOS / Linux / Windows desktop

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: no measurable change to the render path

**Constraints**: the follow action must land within one audio block of the FR-006 boundary

**Scale/Scope**: ~10 files; one field end to end

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | #499 carries `approved` (milestone 0.61.1) |
| I Real-time safety | pass — POD in snapshot, audio-thread-only scalars, no new commands |
| II Boundary | pass — only `NotaSessionClipProps` grows, in `nota_engine.h` |
| III Licensing | pass — no new files besides specs; no dependency |
| IV Verification | smoke test extended (bypass hold, resume rule, persistence, undo, copy) |
| V One change | one feature, one PR |
| VI Docs | CHANGELOG `[Unreleased]`, FEATURES.md, nota-docs Session page (EN + RU) |
| VII Spec-first | this spec / plan / tasks |
| VIII | no rule bent |

Re-check after design: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/103-clip-follow-bypass/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/c-abi.md
├── contracts/ui-and-mcp.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # NotaSessionClipProps.follow_bypass
└── src/engine/
    ├── Track.h                           # SessionSlot::followBypass
    ├── SessionPlayer.h / .cpp            # followHeld, followBase; reset on launch
    └── Engine_Session.cpp                # props get/set; sessionPreApply hold + re-base
src/managed/
├── Nota.Application/Engine/EngineContracts.cs        # NotaSessionClipProps.FollowBypass
├── Nota.Application/Localization/en.json             # new strings
├── Nota.Infrastructure/Persistence/ProjectModel.cs   # SessionSlotDto.FollowBypass
├── Nota.Infrastructure/Persistence/ProjectService.cs # v35, WithClipProps / ApplyClipProps
├── Nota.Infrastructure/Persistence/ProjectMigrations.cs # v34 → v35 no-op
├── Nota.App/SessionView.Inspector.cs                 # Follow head switch + Bypassed state
├── Nota.App/SessionView.Grid.cs                      # slot mark + context-menu item
└── Nota.Mcp/Tools/SessionTools.cs                    # snapshot field + set arg
tests/Nota.SmokeTest/Program.cs                       # Session block
CHANGELOG.md, FEATURES.md                             # + ../nota-docs Session page
```

**Structure Decision**: the existing Session clip-props path end to end; no new files in `src/`.

## Complexity Tracking

None.
