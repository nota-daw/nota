// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// The Scenes grid's lanes (src/lib/grid.ts, specs/102): groups, nesting, folding, the group
// slot summary and an older Nota that sends no groups. Run with `npm test`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { lanes, groupSlot, migrateSessionGrid, effectiveAcross } from '../src/lib/grid.ts';
import type { Session, Track } from '../src/lib/types.ts';

const t = (id: number, kind: Track['kind'], grp = 0): Track => ({ id, name: `T${id}`, color: '', kind, dev: '', grp });
// 1 Drums · 2 Group A { 3 Bass, 4 Group B { 5 Keys } } · 6 Return
const tracks = [t(1, 'drum'), t(2, 'group'), t(3, 'inst', 2), t(4, 'group', 2), t(5, 'inst', 4), t(6, 'return')];
const ses: Session = {
  n: 2,
  tr: [
    { id: 1, c: [[3, 4, 0.5], [0, 0, 0]] },
    { id: 2, k: [3, 5], c: [] },
    { id: 3, c: [[1, 4, 0], [0, 0, 0]] },
    { id: 4, k: [5], c: [] },
    { id: 5, c: [[3, 4, 0.2], [0, 0, 0]] },
  ],
};
const ids = (open: number[]) => lanes(tracks, ses, new Set(open)).map((l) => l.id);

test('folded groups hide their members, nested ones too', () => {
  assert.deepEqual(ids([]), [1, 2]);
  assert.deepEqual(ids([2]), [1, 2, 3, 4]);
  assert.deepEqual(ids([2, 4]), [1, 2, 3, 4, 5]);
  assert.deepEqual(ids([4]), [1, 2], 'an open inner group stays hidden under a folded outer one');
});

test('lanes carry depth, parent, members and the open state', () => {
  const ls = lanes(tracks, ses, new Set([2, 4]));
  const by = new Map(ls.map((l) => [l.id, l]));
  assert.equal(by.get(1)!.depth, 0);
  assert.deepEqual([by.get(3)!.depth, by.get(3)!.parent], [1, 2]);
  assert.deepEqual([by.get(5)!.depth, by.get(5)!.parent], [2, 4]);
  assert.deepEqual(by.get(2)!.members, [3, 5]);
  assert.equal(by.get(2)!.open, true);
  assert.equal(by.get(3)!.group, false);
});

test('a group slot reads its members', () => {
  assert.deepEqual(groupSlot([3, 5], ses, 0), { has: 2, of: 2, playing: true, bars: [1, 3] });
  assert.deepEqual(groupSlot([3, 5], ses, 1), { has: 0, of: 2, playing: false, bars: [0, 0] });
  assert.deepEqual(groupSlot([], ses, 0), { has: 0, of: 0, playing: false, bars: [] });
});

test('an older Nota without groups: every track shows, none folds', () => {
  const old: Session = { n: 1, tr: [{ id: 1, c: [[1, 4, 0]] }, { id: 3, c: [[1, 4, 0]] }, { id: 5, c: [[0, 0, 0]] }] };
  const ls = lanes(tracks.filter((x) => x.kind !== 'group'), old, new Set());
  assert.deepEqual(ls.map((l) => [l.id, l.depth, l.group]), [[1, 0, false], [3, 0, false], [5, 0, false]]);
});

// specs/108: the phone follows Nota's Session grid ("As in Nota") or keeps its own.
test('session grid: old prefs migrate, an explicit across is kept', () => {
  assert.equal(migrateSessionGrid({ sessionAxis: 'across' }), 'across');
  assert.equal(migrateSessionGrid({ sessionAxis: 'down' }), 'nota');
  assert.equal(migrateSessionGrid({}), 'nota');
  assert.equal(migrateSessionGrid({ sessionGrid: 'down', sessionAxis: 'across' }), 'down');
  assert.equal(migrateSessionGrid({ sessionGrid: 'sideways' }), 'nota');
});

test('session grid: "As in Nota" follows ax, a fixed choice does not', () => {
  assert.equal(effectiveAcross('nota', { ax: 'across' }), true);
  assert.equal(effectiveAcross('nota', {}), false);
  assert.equal(effectiveAcross('nota', null), false);
  assert.equal(effectiveAcross('down', { ax: 'across' }), false);
  assert.equal(effectiveAcross('across', {}), true);
});

