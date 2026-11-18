// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// The pointer bookkeeping behind use:tap, without the DOM, so it can be tested on its own. Each
// pointer is followed from where it landed: it stays a tap while it keeps within SLOP of that
// point. A press held HOLD_MS that a hold handler takes (issue 480: a queued clip or scene, whose
// launch it cancels) is spent — its release is not a tap too, or the same press would launch
// again what it just cancelled. A hold the handler declines leaves the press a tap as before.

export const SLOP = 10;      // px a finger may wander and still count as a tap
export const HOLD_MS = 500;  // how long a press must stay down to count as a hold

export class Presses {
  private down = new Map<number, { x: number; y: number; spent: boolean }>();

  press(id: number, x: number, y: number) {
    this.down.set(id, { x, y, spent: false });
  }

  /** The pointer moved; false once it has wandered too far to be a tap or a hold. */
  move(id: number, x: number, y: number): boolean {
    const p = this.down.get(id);
    if (!p) return false;
    if (Math.hypot(x - p.x, y - p.y) > SLOP) { this.down.delete(id); return false; }
    return true;
  }

  /** HOLD_MS after the press: run `hold` if the pointer is still down and near where it landed.
   *  When `hold` acts (returns true) the press is spent. */
  held(id: number, hold: () => boolean): boolean {
    const p = this.down.get(id);
    if (!p || p.spent) return false;
    if (hold()) { p.spent = true; return true; }
    return false;
  }

  /** The pointer lifted at (x, y), `over` the button or not: true when that is a tap. */
  release(id: number, x: number, y: number, over: boolean): boolean {
    const p = this.down.get(id);
    if (!p) return false;
    this.down.delete(id);
    return !p.spent && over && Math.hypot(x - p.x, y - p.y) <= SLOP;
  }

  /** The browser took the pointer (a scroll): neither a tap nor a hold. True if it was down. */
  cancel(id: number): boolean {
    return this.down.delete(id);
  }

  isDown(id: number): boolean {
    return this.down.has(id);
  }
}
