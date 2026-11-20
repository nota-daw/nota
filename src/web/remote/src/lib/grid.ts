// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// The Scenes grid's lanes (issue 662, specs/102): the tracks and groups Nota sends in `ses`, in
// project order, with a group's members hidden while it is folded (the Mixer's rule and its
// remembered open groups), and a group's slot summarised from its members as the desktop's
// Session grid draws it. Pure, so `npm test` checks it without a page.
import type { Session, SessionTrack, Track } from './types';

export type Slot = [number, number, number];

export interface Lane {
  id: number;
  group: boolean;
  /** Groups above it that are in the grid. */
  depth: number;
  /** The innermost group it sits in (0 when none) — its colour marks the lane. */
  parent: number;
  /** A group: whether its members show. */
  open: boolean;
  /** A group: every track below it that holds clips. */
  members: number[];
  slots: Slot[];
}

/** The lanes to draw, in project order. A group an older Nota doesn't send never folds anything. */
export function lanes(tracks: Track[], ses: Session, open: ReadonlySet<number>): Lane[] {
  const parentOf = new Map(tracks.map((t) => [t.id, t.grp]));
  const groups = new Set(ses.tr.filter((r) => isGroup(r, tracks)).map((r) => r.id));
  const out: Lane[] = [];
  for (const r of ses.tr) {
    let depth = 0, parent = 0, hidden = false, guard = 0;
    for (let g = parentOf.get(r.id) ?? 0; g > 0 && guard++ < 32; g = parentOf.get(g) ?? 0) {
      if (!groups.has(g)) continue;
      if (!parent) parent = g;
      depth++;
      if (!open.has(g)) hidden = true;
    }
    if (hidden) continue;
    const group = groups.has(r.id);
    out.push({ id: r.id, group, depth, parent, open: group && open.has(r.id), members: group ? r.k ?? [] : [], slots: r.c });
  }
  return out;
}

function isGroup(r: SessionTrack, tracks: Track[]): boolean {
  return r.k !== undefined || tracks.some((t) => t.id === r.id && t.kind === 'group');
}

export interface GroupSlot {
  /** Members with a clip in this scene, of all members. */
  has: number;
  of: number;
  playing: boolean;
  /** Per member, its slot state (0 empty … 4/5 recording). */
  bars: number[];
}

/** A group's slot in one scene, read from its members' rows. */
export function groupSlot(members: number[], ses: Session, scene: number): GroupSlot {
  const rows = new Map(ses.tr.map((r) => [r.id, r]));
  const bars = members.map((id) => rows.get(id)?.c[scene]?.[0] ?? 0);
  return { has: bars.filter((s) => s !== 0).length, of: bars.length, playing: bars.includes(3), bars };
}

// ---- orientation (specs/108) ---------------------------------------------------------------

/** The phone's Scenes grid choice: follow Nota's Session grid, or keep its own. */
export type SessionGrid = 'nota' | 'down' | 'across';

/** The stored choice, from saved prefs. Before specs/108 the phone kept `sessionAxis` ('down' |
 *  'across') and saved it even when never picked, so only an explicit 'across' is kept; every
 *  other phone starts on "As in Nota", which looks the same until Nota's grid is turned. */
export function migrateSessionGrid(saved: { sessionGrid?: unknown; sessionAxis?: unknown }): SessionGrid {
  const g = saved.sessionGrid;
  if (g === 'nota' || g === 'down' || g === 'across') return g;
  return saved.sessionAxis === 'across' ? 'across' : 'nota';
}

/** Whether the Scenes grid runs scenes across: the phone's own choice, or Nota's (`ses.ax`,
 *  absent from an older Nota or a grid running scenes down). */
export function effectiveAcross(grid: SessionGrid, ses: Pick<Session, 'ax'> | null | undefined): boolean {
  return grid === 'across' || (grid === 'nota' && ses?.ax === 'across');
}
