// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// use:tap — a tap that works with several fingers at once. iOS Safari synthesises `click` only
// for a single-finger tap, so a button that acts in onclick loses every touch made while another
// finger is down (two clips launched together launched one, or none). This follows each pointer
// itself and fires on that pointer's release, if it stayed near where it landed, lifted over the
// button, and wasn't taken by the browser for a scroll (pointercancel). Firing on release keeps
// a scrollable grid safe to drag. Keyboard and assistive-technology activation still come as a
// plain click and always act; only the click a browser synthesises right after a pointer lifted
// on the button (detail > 0) is that same tap again, and is dropped. An optional hold handler
// runs when a press stays put for HOLD_MS; a hold it takes is not also a tap (lib/press.ts).
import type { Action } from 'svelte/action';
import { Presses, HOLD_MS } from './press';

const SAME_TAP_MS = 800;  // a pointer click this soon after a pointer lifted belongs to that pointer

/** A tap handler, or a tap and a hold: `hold` runs when a press stays down HOLD_MS and returns
 *  true when it acted — then that press's release is not a tap as well (issue 480). */
export type TapParam = (() => void) | { tap: () => void; hold?: () => boolean };

export const tap: Action<HTMLElement, TapParam> = (node, param) => {
  let act: () => void, hold: (() => boolean) | undefined;
  const set = (p: TapParam) => {
    if (typeof p === 'function') { act = p; hold = undefined; } else { act = p.tap; hold = p.hold; }
  };
  set(param);
  const presses = new Presses();
  const timers = new Map<number, ReturnType<typeof setTimeout>>();
  let lifted = -Infinity;

  const clearTimer = (id: number) => {
    const t = timers.get(id);
    if (t !== undefined) { clearTimeout(t); timers.delete(id); }
  };
  const onDown = (e: PointerEvent) => {
    if (e.button !== 0) return;
    presses.press(e.pointerId, e.clientX, e.clientY);
    clearTimer(e.pointerId);
    if (hold)
      timers.set(e.pointerId, setTimeout(() => {
        timers.delete(e.pointerId);
        presses.held(e.pointerId, () => hold?.() ?? false);
      }, HOLD_MS));
  };
  const onMove = (e: PointerEvent) => {
    if (!presses.move(e.pointerId, e.clientX, e.clientY)) clearTimer(e.pointerId);
  };
  const onUp = (e: PointerEvent) => {
    clearTimer(e.pointerId);
    lifted = performance.now();
    const r = node.getBoundingClientRect();
    const over = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
    if (presses.release(e.pointerId, e.clientX, e.clientY, over)) act();
  };
  const onCancel = (e: PointerEvent) => {
    clearTimer(e.pointerId);
    if (presses.cancel(e.pointerId)) lifted = performance.now();
  };
  // A pointer that leaves the button abandons its press: no hold fires after the user let go out
  // there (a mouse release outside never reaches this node), and the press is no tap either.
  const onLeave = (e: PointerEvent) => {
    clearTimer(e.pointerId);
    presses.cancel(e.pointerId);
  };
  const onClick = (e: MouseEvent) => {
    // A keyboard click (Enter / Space) has detail 0 and is never a pointer tap's echo.
    if (e.detail > 0 && performance.now() - lifted <= SAME_TAP_MS) return;
    act();
  };

  node.addEventListener('pointerdown', onDown);
  node.addEventListener('pointermove', onMove);
  node.addEventListener('pointerup', onUp);
  node.addEventListener('pointercancel', onCancel);
  node.addEventListener('pointerleave', onLeave);
  node.addEventListener('click', onClick);
  return {
    update(p) { set(p); },
    destroy() {
      for (const t of timers.values()) clearTimeout(t);
      node.removeEventListener('pointerdown', onDown);
      node.removeEventListener('pointermove', onMove);
      node.removeEventListener('pointerup', onUp);
      node.removeEventListener('pointercancel', onCancel);
      node.removeEventListener('pointerleave', onLeave);
      node.removeEventListener('click', onClick);
    },
  };
};
