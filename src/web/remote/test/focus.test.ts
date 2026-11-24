// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// The controller's focus line and Nota's Session selection on the phone (src/lib/focus.ts,
// specs/107 contracts/remote-focus.md). Run with `npm test`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { focusLine, slotSelected, rowSelected } from '../src/lib/focus.ts';
import type { Focus, Session } from '../src/lib/types.ts';

const f = (o: Partial<Focus>): Focus => ({ on: true, ctl: 'KeyLab', mode: 'device', id: 6, tn: 'Bass', dev: 'Twin', md: -1, pg: 1, pn: 4, pgn: '', bank: [0, 0], ...o });

test('the focus line names track, device and page', () => {
  assert.equal(focusLine(f({})), 'Bass · Twin · 2 / 4');
  assert.equal(focusLine(f({ pn: 1, pg: 0 })), 'Bass · Twin');
  assert.equal(focusLine(f({ dev: null })), 'Bass · no device');
  assert.equal(focusLine(f({ id: 0, tn: '' })), 'No track');
  assert.equal(focusLine(null), '');
});

test('the focus line in a mixer mode names the bank', () => {
  assert.equal(focusLine(f({ mode: 'volume', bank: [9, 16] })), 'Volume · tracks 9–16');
  assert.equal(focusLine(f({ mode: 'send', send: 1, bank: [1, 8] })), 'Send B · tracks 1–8');
});

test('Nota’s selection outlines one slot or one row; an older Nota outlines nothing', () => {
  const base: Session = { n: 4, tr: [] };
  assert.ok(slotSelected({ ...base, sel: { id: 6, i: 2 } }, 6, 2));
  assert.ok(!slotSelected({ ...base, sel: { id: 6, i: 2 } }, 6, 1));
  assert.ok(!slotSelected({ ...base, sel: { row: 2 } }, 6, 2));
  assert.ok(rowSelected({ ...base, sel: { row: 2 } }, 2));
  assert.ok(!rowSelected({ ...base, sel: null }, 2));
  assert.ok(!slotSelected(base, 6, 2) && !rowSelected(base, 0));
});
