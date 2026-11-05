// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// use:tap — a tap that works with several fingers at once. iOS Safari synthesises `click` only
// for a single-finger tap, so a button that acts in onclick loses every touch made while another
// finger is down (two clips launched together launched one, or none). This follows each pointer
// itself and fires on that pointer's release, if it stayed near where it landed, lifted over the
// button, and wasn't taken by the browser for a scroll (pointercancel). Firing on release keeps
// a scrollable grid safe to drag. Keyboard and assistive-technology activation still come as a
// plain click and always act; only the click a browser synthesises right after a pointer lifted
// on the button (detail > 0) is that same tap again, and is dropped.
import type { Action } from 'svelte/action';

const SLOP = 10;          // px a finger may wander and still count as a tap
const SAME_TAP_MS = 800;  // a pointer click this soon after a pointer lifted belongs to that pointer

export const tap: Action<HTMLElement, () => void> = (node, fn) => {
  let act = fn;
  const down = new Map<number, [number, number]>();   // pointer → where it landed
  let lifted = -Infinity;

  const far = (e: PointerEvent, p: [number, number]) => Math.hypot(e.clientX - p[0], e.clientY - p[1]) > SLOP;
  const onDown = (e: PointerEvent) => {
    if (e.button === 0) down.set(e.pointerId, [e.clientX, e.clientY]);
  };
  const onMove = (e: PointerEvent) => {
    const p = down.get(e.pointerId);
    if (p && far(e, p)) down.delete(e.pointerId);
  };
  const onUp = (e: PointerEvent) => {
    const p = down.get(e.pointerId);
    lifted = performance.now();
    if (!p) return;
    down.delete(e.pointerId);
    const r = node.getBoundingClientRect();
    const over = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
    if (!far(e, p) && over) act();
  };
  const onCancel = (e: PointerEvent) => {
    if (down.delete(e.pointerId)) lifted = performance.now();
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
  node.addEventListener('click', onClick);
  return {
    update(f) { act = f; },
    destroy() {
      node.removeEventListener('pointerdown', onDown);
      node.removeEventListener('pointermove', onMove);
      node.removeEventListener('pointerup', onUp);
      node.removeEventListener('pointercancel', onCancel);
      node.removeEventListener('click', onClick);
    },
  };
};
