<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Pads: a 4×4 grid (8×2 in landscape, 8×4 on a tablet) of 16-note banks from C2, the
     bottom-left pad lowest, as on a hardware pad controller. A drum track names its pads. Velocity
     comes from where the finger lands (higher is louder) unless Full velocity is on. A pad lights
     in the accent under the finger at once, and in the track colour when the track plays it.
     Repeat retriggers a held pad at the chosen rate; a finger sliding up or down then steps the
     rate faster or slower, and on a track that takes per-note expression sliding sideways bends
     the repeated note (a pad's width a semitone, as the pads run). Each finger plays its own. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { num, unit } from '../lib/fmt';
  import { noteName, slideBend } from '../lib/music';
  import { Rolls } from '../lib/roll';
  import { look } from '../lib/theme.svelte';

  let { form }: { form: Form } = $props();
  // Note values from the slowest to the fastest, each triplet after its straight value. `div`
  // is hits per beat.
  const RATES = [
    { n: '1/4', div: 1 }, { n: '1/4T', div: 1.5 }, { n: '1/8', div: 2 }, { n: '1/8T', div: 3 },
    { n: '1/16', div: 4 }, { n: '1/16T', div: 6 }, { n: '1/32', div: 8 }, { n: '1/32T', div: 12 },
  ];
  let rate = $state(4);
  let repeat = $state(false);
  let down = $state<Record<number, boolean>>({});
  const pointers = new Map<number, number>();   // pointer → note

  // What a held pad shows under the finger while it repeats.
  let shown = $state<Record<number, { rate: string; bend: number }>>({});
  // A finger on a repeating pad: where it landed and how far one step is, either way.
  interface Slide { note: number; x0: number; y0: number; w: number; h: number; from: number }
  const slides = new Map<number, Slide>();
  const bends = $derived(!!app.track?.mpe);

  const cols = $derived(form === 'phone' ? 4 : 8);
  const count = $derived(form === 'tablet' ? 32 : 16);
  // Banks step by an octave (C2, C3…), each covering the sixteen notes from its start.
  const base = $derived(36 + Math.min(app.prefs.bank, 3) * 12);
  const names = $derived(new Map((app.track?.pads ?? []).map(([n, name]) => [n, name])));
  const lit = $derived(new Set(app.lit));
  const color = $derived(app.track?.color ?? 'var(--accent)');
  const colorInk = $derived(app.track ? look.inkOn(app.track.color) : 'var(--ink-on-accent)');

  // Rows run bottom-up: the first note of the bank sits bottom-left.
  const pads = $derived.by(() => {
    const rows = count / cols, out: number[] = [];
    for (let r = rows - 1; r >= 0; r--) for (let c = 0; c < cols; c++) out.push(base + r * cols + c);
    return out;
  });

  const banks = $derived(Array.from({ length: 4 }, (_, i) => ({ i, name: noteName(36 + i * 12) })));

  const stepMs = (r: number) => 60000 / Math.max(20, app.tp.bpm) / RATES[r].div;
  // The repeating notes: one roll per note, however many fingers hold it.
  const rolls = new Rolls(stepMs, (note, r) => {
    app.noteOff(note);
    strike(note, r.vel);
  });

  // Sound the note again, its bend with it: a note-on resets the note's expression.
  function strike(note: number, vel: number) {
    app.noteOn(note, vel);
    const r = rolls.get(note);
    if (r && r.bend !== 0) app.noteExpr(note, 0, r.bend, true);
  }

  function press(e: PointerEvent, note: number) {
    e.preventDefault();
    const el = e.currentTarget as HTMLElement;
    const r = el.getBoundingClientRect();
    const vel = app.prefs.fullVel ? 1 : Math.max(0.2, Math.min(1, 1 - (e.clientY - r.top) / r.height + 0.15));
    // A second finger on a held pad retriggers it; the note stays counted once, so the
    // last finger to lift releases it.
    if ([...pointers.values()].includes(note)) app.noteOff(note);
    pointers.set(e.pointerId, note);
    down[note] = true;
    strike(note, vel);
    if (!repeat) return;
    // The pad keeps the finger while it slides off it, so the slide plays on.
    try { el.setPointerCapture(e.pointerId); } catch { /* the pointer is already gone */ }
    // One repeat chain per note: a second finger must not stack a second timer, or the
    // release clears only the last one and the first keeps the note repeating forever.
    if (!rolls.has(note)) shown[note] = { rate: RATES[rate].n, bend: 0 };
    const roll = rolls.start(note, vel, rate);
    // A slide steps from the rate the note repeats at now: half a pad's height per step (a
    // phone's pad takes two), a pad's width per semitone.
    slides.set(e.pointerId, {
      note, x0: e.clientX, y0: e.clientY, w: Math.max(8, r.width), h: Math.max(12, r.height / 2), from: roll.rate,
    });
  }

  function slide(e: PointerEvent) {
    const f = slides.get(e.pointerId);
    const r = f && rolls.get(f.note);
    if (!f || !r) return;
    // Up is faster. Steps round toward the landing point, so a small wobble keeps the rate.
    const steps = Math.trunc((f.y0 - e.clientY) / f.h);
    const next = Math.max(0, Math.min(RATES.length - 1, f.from + steps));
    const b = bends ? slideBend(e.clientX - f.x0, f.w, 1) : 0;
    if (next === r.rate && b === r.bend) return;
    rolls.setRate(f.note, next);
    if (b !== r.bend) {
      r.bend = b;
      app.noteExpr(f.note, 0, b);
    }
    shown[f.note] = { rate: RATES[r.rate].n, bend: r.bend };
  }

  function release(e: PointerEvent) {
    slides.delete(e.pointerId);
    const note = pointers.get(e.pointerId);
    if (note === undefined) return;
    pointers.delete(e.pointerId);
    if ([...pointers.values()].includes(note)) return;   // another finger still holds it
    rolls.stop(note);
    delete shown[note];
    down[note] = false;
    app.noteOff(note);
  }

  // A browser can swallow a pointer-up in fast multi-touch, and a bank switch replaces the
  // pads a finger is on. Watch at the window too, and let go of whatever is still mapped to
  // a pointer when the bank (or the screen) changes — the pads it pressed no longer exist.
  $effect(() => {
    const rel = (e: PointerEvent) => release(e);
    window.addEventListener('pointerup', rel, true);
    window.addEventListener('pointercancel', rel, true);
    return () => {
      window.removeEventListener('pointerup', rel, true);
      window.removeEventListener('pointercancel', rel, true);
    };
  });
  $effect(() => {
    void base;
    return () => {
      rolls.clear();
      slides.clear();
      shown = {};
      for (const note of new Set(pointers.values())) {
        down[note] = false;
        app.noteOff(note);
      }
      pointers.clear();
    };
  });
</script>

<div class="pads">
  <div class="bar">
    <div class="seg banks" role="group" aria-label="Bank">
      {#each banks as b}
        <button class:on={app.prefs.bank === b.i} onclick={() => (app.prefs.bank = b.i)}>{b.name}</button>
      {/each}
    </div>
    <button class="btn" class:on={app.prefs.fullVel} aria-pressed={app.prefs.fullVel} onclick={() => (app.prefs.fullVel = !app.prefs.fullVel)}>Full velocity</button>
    <button class="btn rep" class:on={repeat} aria-pressed={repeat} onclick={() => (repeat = !repeat)}>
      Repeat
      <span class="rate" role="button" tabindex="0" aria-label="Repeat rate"
        onclick={(e) => { e.stopPropagation(); rate = (rate + 1) % RATES.length; }}
        onkeydown={() => {}}>{RATES[rate].n}</span>
    </button>
  </div>
  <div class="grid" style:grid-template-columns="repeat({cols}, minmax(0, 1fr))">
    {#each pads as note (note)}
      {@const on = down[note]}
      {@const playing = !on && lit.has(note)}
      {@const roll = shown[note]}
      <button class="pad" class:on class:playing style:--tc={color} style:--tc-ink={colorInk}
        onpointerdown={(e) => press(e, note)} onpointermove={slide} onpointerup={release} onpointercancel={release} onpointerleave={release}
        aria-label={names.get(note) ?? noteName(note)}>
        <span class="top">
          <span class="num">{note - 35}</span>
          {#if roll}
            <span class="roll">{roll.rate}{#if roll.bend !== 0}<span class="bend">{(roll.bend > 0 ? '+' : '') + unit(num(roll.bend, 1), 'st')}</span>{/if}</span>
          {/if}
        </span>
        <span class="txt">
          <span class="name">{names.get(note) ?? noteName(note)}</span>
          <span class="note">{noteName(note)}</span>
        </span>
      </button>
    {/each}
  </div>
  {#if repeat}
    <span class="hint">{bends ? 'Hold a pad and slide up or down for the rate, sideways to bend.' : 'Hold a pad and slide up or down for the rate.'}</span>
  {/if}
</div>

<style>
  .pads { flex: 1; min-height: 0; display: flex; flex-direction: column; gap: 10px; }
  .bar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .banks button { min-width: 42px; flex: none; font-size: 12px; }
  .rep { padding: 0 6px 0 10px; gap: 8px; }
  .rate { height: 30px; display: flex; align-items: center; padding: 0 7px; border-radius: min(var(--r-value-bar), 10px); background: var(--bg-sunken); font-size: 11px; color: var(--text-secondary); }
  .rep.on .rate { color: var(--picked-ink); }
  .grid { flex: 1; min-height: 0; display: grid; grid-auto-rows: minmax(0, 1fr); gap: 6px; }
  /* Pads: raised at rest; under the finger live (solid accent); played by the track solid in its colour — ink derived either way. */
  .pad { border-radius: var(--r-field); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); display: flex; flex-direction: column; justify-content: space-between; align-items: stretch; padding: 8px; min-height: 0; min-width: 0; overflow: hidden; text-align: left; touch-action: none; }
  .top { display: flex; justify-content: space-between; align-items: flex-start; gap: 4px; min-width: 0; }
  .num, .note { font-size: 10px; color: var(--text-tertiary); }
  /* The rate over the bend, at the pad's right: two short lines fit a phone's pad. */
  .roll { display: flex; flex-direction: column; align-items: flex-end; gap: 1px; min-width: 0; font-size: 12px; font-weight: 600; color: var(--text-primary); white-space: nowrap; }
  .bend { font-size: 11px; font-weight: 400; }
  .hint { font-size: 11px; color: var(--text-tertiary); }
  .txt { display: flex; flex-direction: column; gap: 1px; min-width: 0; }
  .name { font-size: 12px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  :global(.tablet) .name { font-size: 14px; }
  .pad.playing { background: var(--tc); border-color: var(--tc); box-shadow: none; }
  .pad.playing * { color: var(--tc-ink); }
  .pad.on { background: var(--accent); border-color: var(--accent); box-shadow: none; }
  .pad.on * { color: var(--ink-on-accent); }
</style>
