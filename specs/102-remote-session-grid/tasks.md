# Tasks: Nota Remote — Session grid (spec 102, issue #662)

**Input**: [spec.md](spec.md), [plan.md](plan.md), [contracts/session-message.md](contracts/session-message.md)

## Phase 1: Foundation (protocol + shared helpers)

- [x] T001 [P] Hub: `Members(groupId)` helper; `BuildSession` keeps group tracks (not returns) with `k` and an empty `c` — `src/managed/Nota.Remote/RemoteHub.State.cs`
- [x] T002 [P] Hub: `launch` / `stopSlot` on a group id launch / stop each member — `src/managed/Nota.Remote/RemoteHub.Apply.cs`
- [x] T003 [P] Page types: `SessionTrack.k`; `Prefs.sessionAxis` ('down' | 'across', default 'down') — `src/web/remote/src/lib/types.ts`, `src/web/remote/src/lib/app.svelte.ts`
- [x] T004 [P] Pure helper `lanes()` (visible tracks and groups, depth, fold by `groupsOpen`) and `groupCell()` (has / of / playing / bars) — `src/web/remote/src/lib/grid.ts`
- [x] T005 Unit tests for `grid.ts` (nesting, folding, summary, older Nota without `k`) — `src/web/remote/test/grid.test.ts`; `npm test` runs both test files
- [x] T006 Mock: a second member in the Pads group, 12 scenes, the group in `ses` with `k`, log `launch` / `stopSlot` / `scene` / `stopAll` — `src/web/remote/mock/nota-mock.mjs`

## Phase 2: User Story 1 + 2 — pinned controls and groups (P1)

- [x] T007 [US1] Rebuild `Scenes.svelte` on lanes: sticky header row, sticky scene rail, sticky Stop row, sticky corners (scenes down)
- [x] T008 [US2] Group header (fold / unfold, member count), member headers indented under the group colour, group slot as the desktop's (launch triangle, "n of m", member bars, playing wash), group Stop
- [x] T009 [US2] Smoke test: group in `ses` with `k`, group `launch` queues the members, group `stopSlot` stops them — `tests/Nota.SmokeTest/RemoteTests.cs`

## Phase 3: User Story 3 — orientation (P2)

- [x] T010 [US3] Scenes across layout: scene buttons as a sticky top row, Stop All sticky top-left, a lane per row with its header + Stop sticky left — `Scenes.svelte`
- [x] T011 [US3] ⋯ menu "Session grid" segmented control — `src/web/remote/src/components/Sheets.svelte`

## Phase 4: Polish & verification

- [x] T012 `npm run check`, `npm test`, `npm run build` (refreshes `Nota.Remote/wwwroot`)
- [x] T013 Visual check against the mock in headless Chrome: both orientations, phone and phone landscape, scrolled to the far corner, a group open and folded; dark and light
- [x] T014 CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs Remote page EN + RU (nota-docs)
- [x] T015 `scripts/build.sh` green (smoke test included)

## Nota Compliance

- [x] SPDX headers on `grid.ts` and `grid.test.ts`
- [x] No new dependency
- [x] No audio-thread or ABI change
- [x] Theme tokens only (DesignTokenCheck.Remote passes)
