// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// use:tap's pointer bookkeeping (src/lib/press.ts): a tap, a press that wandered off, and a hold —
// a hold its handler takes (issue 480: cancel a queued launch) is not also a tap on release, one it
// declines still is. Run with `npm test`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Presses, SLOP } from '../src/lib/press.ts';

test('a press lifted where it landed, over the button, is a tap', () => {
  const p = new Presses();
  p.press(1, 10, 10);
  assert.equal(p.release(1, 12, 11, true), true);
  assert.equal(p.release(1, 12, 11, true), false, 'once');
});

test('a press that wandered or lifted off the button is not a tap', () => {
  const p = new Presses();
  p.press(1, 0, 0);
  assert.equal(p.move(1, SLOP + 1, 0), false);
  assert.equal(p.release(1, 0, 0, true), false);
  p.press(2, 0, 0);
  assert.equal(p.release(2, 0, 0, false), false);
});

test('a hold the handler takes is spent: its release launches nothing', () => {
  const p = new Presses();
  let held = 0;
  p.press(1, 5, 5);
  assert.equal(p.held(1, () => { held++; return true; }), true);
  assert.equal(p.held(1, () => { held++; return true; }), false, 'a hold acts once per press');
  assert.equal(held, 1);
  assert.equal(p.release(1, 5, 5, true), false);
});

test('a hold the handler declines leaves the press a tap', () => {
  const p = new Presses();
  p.press(1, 5, 5);
  assert.equal(p.held(1, () => false), false);
  assert.equal(p.release(1, 5, 5, true), true);
});

test('no hold for a pointer already lifted, wandered or cancelled', () => {
  const p = new Presses();
  const yes = () => true;
  p.press(1, 0, 0); p.release(1, 0, 0, true);
  assert.equal(p.held(1, yes), false);
  p.press(2, 0, 0); p.move(2, 40, 0);
  assert.equal(p.held(2, yes), false);
  p.press(3, 0, 0);
  assert.equal(p.cancel(3), true);
  assert.equal(p.held(3, yes), false);
});

test('fingers are followed one by one', () => {
  const p = new Presses();
  p.press(1, 0, 0); p.press(2, 100, 0);
  p.held(1, () => true);
  assert.equal(p.release(2, 100, 0, true), true, 'the other finger still taps');
  assert.equal(p.release(1, 0, 0, true), false);
});
