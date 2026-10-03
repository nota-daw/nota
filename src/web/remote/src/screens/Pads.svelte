<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Pads: a 4×4 grid (8×2 in landscape, 8×4 on a tablet) of 16-note banks from C2, the
     bottom-left pad lowest, as on a hardware pad controller. A drum track names its pads. Velocity
     comes from where the finger lands (higher is louder) unless Full vel. is on. A pad lights
     brass under the finger at once, and in the track colour when the track plays it. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { noteName } from '../lib/music';

  let { form }: { form: Form } = $props();
  const RATES = [{ n: '1/8', div: 2 }, { n: '1/16', div: 4 }, { n: '1/32', div: 8 }];
  let rate = $state(1);
  let repeat = $state(false);
  let down = $state<Record<number, boolean>>({});
  const timers = new Map<number, number>();
  const pointers = new Map<number, number>();   // pointer → note

  const cols = $derived(form === 'phone' ? 4 : 8);
  const count = $derived(form === 'tablet' ? 32 : 16);
  // Banks step by an octave (C2, C3…), each covering the sixteen notes from its start.
  const base = $derived(36 + Math.min(app.prefs.bank, 3) * 12);
  const names = $derived(new Map((app.track?.pads ?? []).map(([n, name]) => [n, name])));
  const lit = $derived(new Set(app.lit));
  const color = $derived(app.track?.color ?? 'var(--accent)');

  // Rows run bottom-up: the first note of the bank sits bottom-left.
  const pads = $derived.by(() => {
    const rows = count / cols, out: number[] = [];
    for (let r = rows - 1; r >= 0; r--) for (let c = 0; c < cols; c++) out.push(base + r * cols + c);
    return out;
  });

  const banks = $derived(Array.from({ length: 4 }, (_, i) => ({ i, name: noteName(36 + i * 12) })));

  function press(e: PointerEvent, note: number) {
    e.preventDefault();
    const el = e.currentTarget as HTMLElement;
    const r = el.getBoundingClientRect();
    const vel = app.prefs.fullVel ? 1 : Math.max(0.2, Math.min(1, 1 - (e.clientY - r.top) / r.height + 0.15));
    pointers.set(e.pointerId, note);
    down[note] = true;
    app.noteOn(note, vel);
    if (repeat) {
      const step = () => 60000 / Math.max(20, app.tp.bpm) / RATES[rate].div;
      const tick = () => {
        app.noteOff(note);
        app.noteOn(note, vel);
        timers.set(note, window.setTimeout(tick, step()));
      };
      timers.set(note, window.setTimeout(tick, step()));
    }
  }

  function release(e: PointerEvent) {
    const note = pointers.get(e.pointerId);
    if (note === undefined) return;
    pointers.delete(e.pointerId);
    if ([...pointers.values()].includes(note)) return;   // another finger still holds it
    clearTimeout(timers.get(note));
    timers.delete(note);
    down[note] = false;
    app.noteOff(note);
  }

  $effect(() => () => { for (const t of timers.values()) clearTimeout(t); });
</script>

<div class="pads">
  <div class="bar">
    <div class="seg banks" role="group" aria-label="Bank">
      {#each banks as b}
        <button class="mono" class:on={app.prefs.bank === b.i} onclick={() => (app.prefs.bank = b.i)}>{b.name}</button>
      {/each}
    </div>
    <button class="btn" class:on={app.prefs.fullVel} aria-pressed={app.prefs.fullVel} onclick={() => (app.prefs.fullVel = !app.prefs.fullVel)}>Full vel.</button>
    <button class="btn rep" class:on={repeat} aria-pressed={repeat} onclick={() => (repeat = !repeat)}>
      Repeat
      <span class="rate mono" role="button" tabindex="0" aria-label="Repeat rate"
        onclick={(e) => { e.stopPropagation(); rate = (rate + 1) % RATES.length; }}
        onkeydown={() => {}}>{RATES[rate].n}</span>
    </button>
  </div>
  <div class="grid" style:grid-template-columns="repeat({cols}, minmax(0, 1fr))">
    {#each pads as note (note)}
      {@const on = down[note]}
      {@const playing = !on && lit.has(note)}
      <button class="pad" class:on class:playing style:--tc={color}
        onpointerdown={(e) => press(e, note)} onpointerup={release} onpointercancel={release} onpointerleave={release}
        aria-label={names.get(note) ?? noteName(note)}>
        <span class="num mono">{note - 35}</span>
        <span class="label">
          <span class="name">{names.get(note) ?? noteName(note)}</span>
          <span class="note mono">{noteName(note)}</span>
        </span>
      </button>
    {/each}
  </div>
</div>

<style>
  .pads { flex: 1; min-height: 0; display: flex; flex-direction: column; gap: 10px; }
  .bar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .banks button { min-width: 42px; flex: none; font-size: 11px; font-weight: 500; }
  .rep { padding: 0 6px 0 10px; gap: 8px; }
  .rate { height: 30px; display: flex; align-items: center; padding: 0 7px; border-radius: 3px; background: var(--well); font-size: 11px; color: var(--ink3); }
  .rep.on .rate { color: var(--accent-bright); }
  .grid { flex: 1; min-height: 0; display: grid; grid-auto-rows: minmax(0, 1fr); gap: 8px; }
  .pad { border-radius: 6px; background: var(--raised); border: 1px solid var(--border); box-shadow: inset 0 1px 0 rgba(255,255,255,.03); display: flex; flex-direction: column; justify-content: space-between; align-items: stretch; padding: 8px; min-height: 0; min-width: 0; overflow: hidden; text-align: left; touch-action: none; }
  .num, .note { font-size: 10px; color: var(--ink5); }
  .note { font-size: 9px; }
  .label { display: flex; flex-direction: column; gap: 1px; min-width: 0; }
  .name { font-size: 12px; font-weight: 600; color: var(--ink1); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  :global(.tablet) .name { font-size: 14px; }
  .pad.playing { background: var(--tc); border-color: var(--tc); }
  .pad.on { background: var(--accent); border-color: var(--accent); }
  .pad.on *, .pad.playing * { color: var(--on-accent); }
  .pad.playing * { color: #171613; }
</style>
