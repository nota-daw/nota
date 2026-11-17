// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Note repeat timing (src/lib/roll.ts) on a clock the test moves by hand: rate changes, an
// overdue hit, a timer the browser held back, and letting go. Run with `npm test`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Rolls, type Clock } from '../src/lib/roll.ts';

// A clock whose timers fire only when the test advances it, in due order.
class FakeClock implements Clock {
  t = 0;
  #id = 0;
  #timers = new Map<number, { at: number; fn: () => void }>();
  now() { return this.t; }
  set(fn: () => void, ms: number) { this.#timers.set(++this.#id, { at: this.t + ms, fn }); return this.#id; }
  clear(id: number) { this.#timers.delete(id); }
  get pending() { return this.#timers.size; }
  /** Move to `to`, firing each timer due by then at its own time. */
  advance(to: number) {
    for (;;) {
      let first: [number, { at: number; fn: () => void }] | undefined;
      for (const e of this.#timers) if (e[1].at <= to && (!first || e[1].at < first[1].at)) first = e;
      if (!first) break;
      this.#timers.delete(first[0]);
      this.t = Math.max(this.t, first[1].at);
      first[1].fn();
    }
    this.t = to;
  }
  /** Fire whatever is pending only at `to`, as a throttled browser does. */
  stall(to: number) {
    this.t = to;
    this.advance(to);
  }
}

// Rate i is i + 1 hits per 100 ms: rate 0 steps 100 ms, rate 1 50 ms, rate 3 25 ms.
const STEP = (r: number) => 100 / (r + 1);

function setup() {
  const clock = new FakeClock();
  const hits: number[] = [];
  const rolls = new Rolls(STEP, () => hits.push(clock.now()), clock);
  return { clock, hits, rolls };
}

test('repeats every step of the rate it starts at', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.advance(350);
  assert.deepEqual(hits, [100, 200, 300]);
});

test('a second start keeps the one roll', () => {
  const { clock, hits, rolls } = setup();
  const a = rolls.start(60, 1, 0);
  clock.advance(40);
  const b = rolls.start(60, 0.5, 3);
  assert.equal(a, b);
  assert.equal(clock.pending, 1);
  clock.advance(250);
  assert.deepEqual(hits, [100, 200]);
});

test('a faster rate takes over one new step after the last hit, without a gap', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);   // hits at 100, then due at 200
  clock.advance(120);
  rolls.setRate(60, 1);    // 50 ms: next is 100 + 50
  clock.advance(260);
  assert.deepEqual(hits, [100, 150, 200, 250]);
});

test('a slower rate pushes the next hit out from the last', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 1);   // 50 ms: hits at 50, 100
  clock.advance(110);
  rolls.setRate(60, 0);    // 100 ms: next is 100 + 100
  clock.advance(310);
  assert.deepEqual(hits, [50, 100, 200, 300]);
});

test('a rate change whose next hit has passed hits now, once, then keeps the grid', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);   // last hit due at 0
  clock.advance(80);
  rolls.setRate(60, 3);    // 25 ms: due at 25, long gone
  clock.advance(80);
  assert.deepEqual(hits, [80]);
  clock.advance(130);
  assert.deepEqual(hits, [80, 105, 130]);
});

test('several rate changes before an overdue hit fires make one hit', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.advance(80);
  rolls.setRate(60, 3);
  rolls.setRate(60, 2);
  rolls.setRate(60, 3);
  assert.equal(clock.pending, 1);
  clock.advance(80);
  assert.deepEqual(hits, [80]);
});

test('setting the rate it already has leaves the timer alone', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.advance(90);
  rolls.setRate(60, 0);
  clock.advance(100);
  assert.deepEqual(hits, [100]);
});

test('a timer held back by less than half a step stays on the grid', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.stall(140);        // due at 100, 40 ms late
  clock.advance(210);
  assert.deepEqual(hits, [140, 200]);
});

test('a timer held back far resumes from now instead of catching up', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.stall(1000);       // due at 100, nine steps late
  assert.deepEqual(hits, [1000]);
  clock.advance(1210);
  assert.deepEqual(hits, [1000, 1100, 1200]);
});

test('stop ends the roll and its timer', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.advance(150);
  rolls.stop(60);
  assert.equal(rolls.has(60), false);
  assert.equal(clock.pending, 0);
  clock.advance(1000);
  assert.deepEqual(hits, [100]);
});

test('stop right after a rate change leaves no timer behind', () => {
  const { clock, hits, rolls } = setup();
  rolls.start(60, 1, 0);
  clock.advance(80);
  rolls.setRate(60, 3);    // armed to fire at once
  rolls.stop(60);
  clock.advance(500);
  assert.deepEqual(hits, []);
  assert.equal(clock.pending, 0);
});

test('a setRate on a note that is not repeating does nothing', () => {
  const { clock, rolls } = setup();
  rolls.setRate(61, 2);
  assert.equal(rolls.has(61), false);
  assert.equal(clock.pending, 0);
});

test('notes repeat on their own, and clear stops them all', () => {
  const { clock } = setup();
  const by = new Map<number, number[]>();
  const own = new Rolls(STEP, (n) => by.set(n, [...(by.get(n) ?? []), clock.now()]), clock);
  own.start(60, 1, 0);
  clock.advance(30);
  own.start(62, 1, 1);
  clock.advance(130);
  assert.deepEqual(by.get(60), [100]);
  assert.deepEqual(by.get(62), [80, 130]);
  own.clear();
  assert.equal(clock.pending, 0);
  assert.equal(own.has(60), false);
});
