<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- What is in a Session clip, drawn as the desktop slot draws it (issue 557, SessionView.Grid.cs):
     a MIDI clip's notes as 3 px bars over the clip's own pitch range, an audio clip's waveform
     as centred bars scaled to its loudest part. One canvas per cell, redrawn only when the
     preview, the ink, the size or the theme changes. The ink is any CSS colour (a custom
     property included): the canvas reads it back as its own computed colour. -->
<script lang="ts">
  import type { ClipPreview } from '../lib/types';
  import { look } from '../lib/theme.svelte';

  let { preview, ink }: { preview: ClipPreview | undefined; ink: string } = $props();
  let canvas: HTMLCanvasElement | undefined = $state();
  let w = $state(0), h = $state(0);

  $effect(() => {
    const c = canvas;
    if (!c) return;
    const ro = new ResizeObserver(([e]) => { w = e.contentRect.width; h = e.contentRect.height; });
    ro.observe(c);
    return () => ro.disconnect();
  });

  $effect(() => {
    const c = canvas, p = preview;
    void look.msg; void look.phoneDark; void ink;   // a theme change re-resolves the ink
    if (!c || w <= 0 || h <= 0) return;
    const dpr = window.devicePixelRatio || 1;
    c.width = Math.round(w * dpr); c.height = Math.round(h * dpr);
    const g = c.getContext('2d');
    if (!g) return;
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);
    if (!p) return;
    g.fillStyle = getComputedStyle(c).color;
    const radius = parseFloat(getComputedStyle(c).getPropertyValue('--r-fine')) / 2 || 0;
    const bar = (x: number, y: number, bw: number, bh: number) => {
      g.beginPath();
      if (radius > 0 && 'roundRect' in g) g.roundRect(x, y, bw, bh, Math.min(radius, bw / 2, bh / 2));
      else g.rect(x, y, bw, bh);
      g.fill();
    };
    if (p.k === 'n' && p.n?.length) {
      let lo = 127, hi = 0;
      for (const [, , pitch] of p.n) { lo = Math.min(lo, pitch); hi = Math.max(hi, pitch); }
      const range = Math.max(1, hi - lo), nh = Math.min(3, h);
      for (const [start, len, pitch] of p.n) {
        if (start >= 64) continue;
        const x = (start / 64) * w;
        const bw = Math.max(1.5, (Math.min(len, 64 - start) / 64) * w);
        const y = ((hi - pitch) / range) * (h - nh);
        bar(x, y, bw, nh);
      }
    } else if (p.k === 'p' && p.p?.length) {
      const n = p.p.length, step = w / n, bw = Math.max(1, step * 0.62);
      for (let i = 0; i < n; i++) {
        const bh = Math.max(1, (p.p[i] / 99) * h);
        bar(i * step, (h - bh) / 2, bw, bh);
      }
    }
  });
</script>

<canvas bind:this={canvas} style:color={ink} aria-hidden="true"></canvas>

<style>
  canvas { display: block; width: 100%; height: 100%; }
</style>
