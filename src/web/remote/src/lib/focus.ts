// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The controller's focus and Nota's Session selection as the phone shows them (specs/107):
// one line for the header, and whether a Scenes cell or row is the one selected in Nota.

import type { Focus, Session } from './types';

/** "Bass · Twin · 2 / 4", "Volume · tracks 9–16", "Bass · no device". */
export function focusLine(f: Focus | null): string {
  if (!f) return '';
  if (f.mode !== 'device') {
    const mode = f.mode === 'send' ? `Send ${String.fromCharCode(65 + (f.send ?? 0))}` : f.mode === 'pan' ? 'Pan' : 'Volume';
    return `${mode} · tracks ${f.bank[0]}–${f.bank[1]}`;
  }
  if (f.id <= 0) return 'No track';
  if (!f.dev) return `${f.tn} · no device`;
  return f.pn > 1 ? `${f.tn} · ${f.dev} · ${f.pg + 1} / ${f.pn}` : `${f.tn} · ${f.dev}`;
}

/** Nota's selection is this slot. */
export const slotSelected = (s: Session | null, id: number, i: number): boolean =>
  !!s?.sel && 'id' in s.sel && s.sel.id === id && s.sel.i === i;

/** Nota's selection is this scene row. */
export const rowSelected = (s: Session | null, i: number): boolean =>
  !!s?.sel && 'row' in s.sel && s.sel.row === i;
