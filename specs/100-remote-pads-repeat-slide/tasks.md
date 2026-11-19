# Tasks: Nota Remote — Pads Repeat: slide the finger to change the rate and the pitch

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Story 1 — rate (P1)

- [x] T001 `RATES` 1/4 … 1/32T, 1/16 by default in `src/web/remote/src/screens/Pads.svelte` (FR-001)
- [x] T002 Per-note rolls timed from the last hit, in `src/web/remote/src/lib/roll.ts` (FR-003)
- [x] T003 Per-finger slides stepping the rate; pointer capture while Repeat is on (FR-002, FR-006)

## Phase 2: User Story 2 — bend (P2)

- [x] T004 `slideBend` in `src/web/remote/src/lib/music.ts`, used by Keys and Pads
- [x] T005 `app.noteExpr(…, now)` in `src/web/remote/src/lib/app.svelte.ts`; the bend re-sent after each repeated note-on (FR-004)

## Phase 3: User Story 3 — display (P2)

- [x] T006 The rate and bend on a repeating pad; the hint while Repeat is on (FR-005)

## Phase 4: Polish

- [x] T007 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T008 `npm run check` and `npm run build` pass; verified against the mock in headless Chrome (touch)
- [x] T009 Roll timing tests on a controlled clock in `src/web/remote/test/roll.test.ts` (`npm test`)
