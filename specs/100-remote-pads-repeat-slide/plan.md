# Implementation Plan: Nota Remote — Pads Repeat: slide the finger to change the rate and the pitch

**Branch**: `100-remote-pads-repeat-slide` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

## Summary

`Pads.svelte` keeps one roll per repeating note (velocity, rate, bend, when the last hit was due,
its timer) and one slide per finger (where it landed, the pad's size, the rate it started from).
A vertical slide steps the roll's rate and re-arms its timer from the last hit; on an MPE track a
sideways slide sets its bend, re-sent after each repeated note-on.

## Technical Context

**Language/Version**: TypeScript, Svelte 5 (the phone page, `src/web/remote`).

**Primary Dependencies**: none new.

**Audio-Thread Impact**: none.

**Testing**: `npm run check`; `npm test` — the roll timing (`src/lib/roll.ts`) under a hand-moved
clock in `test/roll.test.ts` (rate changes, an overdue hit, a held-back timer, release); the built page against `mock/nota-mock.mjs` (logging `on` / `off` /
`x`), driven by headless Chrome over CDP with emulated touch: a held pad sliding up, sideways and
down on an MPE track; two fingers on two pads of a drum track; Repeat off sliding off a pad.

## Design

- **Rates**: `RATES` runs 1/4 … 1/32T, each triplet after its straight value; `div` is hits per
  beat, the step `60000 / bpm / div` ms. The chip still cycles them and sets where a press starts.
- **Roll** (per note, `rolls`): `arm` sets the timer for `last + step(rate)`; `hit` sends off and
  on, then moves `last` to the due time — or to now if the timer ran more than half a step late,
  so a throttled timer doesn't burst. A rate change calls `arm`, which fires at once when the new
  due time has passed.
- **Slide** (per pointer, `slides`): steps `trunc((y0 − y) / (h / 2))` from the rate the roll had
  when the finger landed, clamped to the list; bend `slideBend(x − x0, padWidth, 1)` when the track
  takes MPE. With Repeat on the pad captures the pointer, so a slide off it keeps playing.
- **Bend after a note-on**: `strike` sends the note-on, then the roll's bend through
  `app.noteExpr(…, now = true)`, which sends at once instead of on the next frame (a note-on resets
  the note's expression).
- **Shared bend curve**: Keys' `bendOf` moves to `music.ts` as `slideBend(dx, unit, semis)` — the
  same dead zone and ±24 clamp — and both screens call it.
- **Display**: a repeating pad shows `shown[note]` (rate over bend) at its top right; a hint under
  the grid while Repeat is on.

## Constitution Check

The phone page only; no engine, ABI, protocol or format change. Theme tokens only.
