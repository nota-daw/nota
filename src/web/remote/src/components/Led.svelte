<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- The Macros screen's LED display (specs/113): Nota renders the picture and sends the dots;
     this only lights them — round dots in the track's colour on a dark bezel, unlit dots faintly
     visible, a soft glow over the lit ones. While the link is down the last frame stays, dimmed.
     `fit`: the display takes the largest size its box allows at its own aspect (landscape, full
     screen); otherwise it fills the width. `onexpand` puts a full-screen button in its corner. -->
<script lang="ts">
  import type { Led } from '../lib/types';

  let { led, color, dim = false, fit = false, onexpand, onclose }: {
    led: Led | null; color: string; dim?: boolean; fit?: boolean; onexpand?: () => void; onclose?: () => void;
  } = $props();

  let box: HTMLDivElement;
  let canvas: HTMLCanvasElement;
  let bw = $state(0), bh = $state(0);
  const W = 128, H = 48;
  const off = document.createElement('canvas');

  const w = $derived(led?.w ?? W);
  const h = $derived(led?.h ?? H);
  // The display's width in CSS px: the box's width, or as large as both sides allow.
  const width = $derived(fit && bh > 0 ? Math.max(0, Math.min(bw, (bh * w) / h)) : bw);

  // The track's colour, else the theme's bright accent (no track yet).
  function rgb(c: string): [number, number, number] {
    const src = c.trim() || (box ? getComputedStyle(box).getPropertyValue('--accent-bright').trim() : '');
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(src);
    return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : [200, 200, 200];
  }

  function paint() {
    if (!canvas || width <= 0) return;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const pitch = width / w;
    const cw = Math.round(width * dpr), ch = Math.round(pitch * h * dpr);
    for (const c of [canvas, off]) { if (c.width !== cw) c.width = cw; if (c.height !== ch) c.height = ch; }
    canvas.style.width = `${width}px`;
    canvas.style.height = `${pitch * h}px`;
    const o = off.getContext('2d')!, ctx = canvas.getContext('2d')!;
    const [r, g, b] = rgb(color);
    const p = pitch * dpr, rad = p * 0.38;
    o.clearRect(0, 0, cw, ch);
    const paths = Array.from({ length: 16 }, () => new Path2D());
    const dots = led?.dots;
    for (let y = 0; y < h; y++)
      for (let x = 0; x < w; x++) {
        const l = dots ? dots[y * w + x] : 0, cx = (x + 0.5) * p, cy = (y + 0.5) * p;
        paths[l].moveTo(cx + rad, cy);
        paths[l].arc(cx, cy, rad, 0, Math.PI * 2);
      }
    const k = dim ? 0.45 : 1;
    o.fillStyle = `rgba(${r},${g},${b},0.07)`;
    o.fill(paths[0]);
    for (let l = 1; l < 16; l++) {
      o.fillStyle = `rgba(${r},${g},${b},${((0.16 + 0.84 * l / 15) * k).toFixed(3)})`;
      o.fill(paths[l]);
    }
    ctx.clearRect(0, 0, cw, ch);
    ctx.drawImage(off, 0, 0);
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    ctx.globalAlpha = 0.4 * k;
    ctx.filter = `blur(${(p * 0.9).toFixed(1)}px)`;
    ctx.drawImage(off, 0, 0);
    ctx.restore();
  }

  $effect(() => {
    // Repaint on a new frame, a new colour, the link dimming or a resize.
    void led; void color; void dim; void width;
    paint();
  });

  $effect(() => {
    const ro = new ResizeObserver(() => { bw = box.clientWidth; bh = box.clientHeight; });
    ro.observe(box);
    bw = box.clientWidth; bh = box.clientHeight;
    return () => ro.disconnect();
  });
</script>

<div class="lcd" class:fit role="img" aria-label="Device display">
  <div class="screen" bind:this={box}><canvas bind:this={canvas}></canvas></div>
  {#if onexpand}
    <button class="corner" aria-label="Show the display full screen" onclick={onexpand}>
      <svg viewBox="0 0 12 12"><path d="M1.5 4.5 V1.5 H4.5 M7.5 1.5 H10.5 V4.5 M10.5 7.5 V10.5 H7.5 M4.5 10.5 H1.5 V7.5" /></svg>
    </button>
  {/if}
  {#if onclose}
    <button class="corner" aria-label="Close the full-screen display" onclick={onclose}>
      <svg viewBox="0 0 12 12"><path d="M2.5 2.5 L9.5 9.5 M9.5 2.5 L2.5 9.5" /></svg>
    </button>
  {/if}
</div>

<style>
  /* The bezel stays dark in every theme: an LED display is black glass on any synth. */
  .lcd { --lcd: color-mix(in srgb, var(--bg-sunken) 25%, black); position: relative; flex: none; padding: 8px 8px 7px; background: var(--lcd); border: 1px solid var(--seam); border-radius: min(var(--r-card), 12px); box-shadow: inset 0 2px 10px var(--shadow-modal); }
  .lcd.fit { flex: 1; min-height: 0; display: flex; }
  .screen { width: 100%; }
  .fit .screen { flex: 1; min-height: 0; display: flex; align-items: center; justify-content: center; }
  canvas { display: block; }
  /* A quiet corner button over the glass; the dots stay the point. */
  .corner { position: absolute; top: 4px; right: 4px; width: 32px; height: 32px; display: grid; place-items: center; color: var(--text-muted); opacity: .55; border-radius: min(var(--r-cell), 10px); }
  .corner:active { opacity: 1; background: color-mix(in srgb, var(--text-muted) 18%, transparent); }
  .corner svg { width: 12px; height: 12px; }
  .corner path { fill: none; stroke: currentColor; stroke-width: 1.4; }
</style>
