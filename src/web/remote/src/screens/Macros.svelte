<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Macros: eight big knobs for one device of the track's chain — a rack's macros, a built-in
     instrument's or effect's parameters, a plug-in's first eight (paged). A vertical swipe turns
     a knob, as in Nota; values carry the track's colour, the knob being turned a quiet tint of it; a dot marks an automated parameter. A knob mapped with MIDI Learn
     shows what it drives instead. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { pct } from '../lib/fmt';

  let { form }: { form: Form } = $props();
  const cols = $derived(form === 'phone' ? 2 : 4);
  const size = $derived(form === 'phone' ? 64 : form === 'phoneLand' ? 52 : 112);
  const m = $derived(app.mac?.id === app.proj?.you ? app.mac : null);
  const devName = $derived(m?.devs[m.di] ?? 'No devices');
  // Values carry the playing track's colour (spec 090), neutral ink without a track.
  const tc = $derived(app.track?.color ?? 'var(--viz-ink)');

  let local = $state<Record<number, number>>({});
  let mappedPos = $state<Record<number, number>>({});
  let drag: { i: number; y: number; v: number } | null = null;
  let active = $state(-1);
  let lastSend = 0;

  function down(e: PointerEvent, i: number, v: number) {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    if (!m) return;
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    drag = { i, y: e.clientY, v };
    active = i;
    local[i] = v;
    app.send({ t: 'mac', i, v, ph: 1 });
  }
  function move(e: PointerEvent) {
    if (!drag) return;
    const v = Math.min(1, Math.max(0, drag.v + (drag.y - e.clientY) / 200));
    local[drag.i] = v;
    if (m?.k[drag.i]?.map) mappedPos[drag.i] = v;
    const now = performance.now();
    if (now - lastSend > 30) { lastSend = now; app.send({ t: 'mac', i: drag.i, v, ph: 0 }); }
  }
  function up() {
    if (!drag) return;
    const i = drag.i;
    app.send({ t: 'mac', i, v: local[i], ph: 2 });
    drag = null;
    active = -1;
    setTimeout(() => { if (active !== i) delete local[i]; }, 500);
  }
  const ARC = 98.9, CIRC = 131.9;
</script>

<div class="macros" onpointermove={move} onpointerup={up} onpointercancel={up} role="group" aria-label="Macros">
  <button class="dev" onclick={() => m && m.devs.length > 1 && (app.sheet = 'devices')}>
    <span class="k">Device</span>
    <span class="dn">{devName}</span>
    {#if m && m.devs.length > 1}<span class="pos">{m.di + 1}/{m.devs.length}</span>{/if}
    <svg width="10" height="10" viewBox="0 0 10 10"><path d="M2 3.5 L5 6.5 L8 3.5" /></svg>
  </button>
  {#if !m || m.devs.length === 0}
    <div class="empty">{m ? 'This track has no instrument or effects to control.' : 'Loading…'}</div>
  {:else}
    <div class="grid" style:--tc={tc} style:grid-template-columns="repeat({cols}, minmax(0, 1fr))">
      {#each m.k as k, i (i)}
        {@const v = local[i] ?? (k.map ? mappedPos[i] ?? 0.5 : k.v ?? 0)}
        {@const on = active === i}
        {@const empty = k.n === undefined && !k.map}
        <div class="knob" class:on class:empty class:learnable={app.learn.on && !empty}
          onpointerdown={(e) => !empty && down(e, i, v)} role="slider" tabindex="0"
          aria-label={k.map ?? k.n ?? `Macro ${i + 1}`} aria-valuenow={Math.round(v * 100)} aria-valuetext={k.map ? pct(v) : k.txt}>
          <span class="tint"></span>
          {#if k.a && !k.map}<span class="auto"></span>{/if}
          <svg width={size} height={size} viewBox="0 0 52 52">
            <circle cx="26" cy="26" r="21" class="trk" stroke-dasharray="{ARC} {CIRC}" transform="rotate(135 26 26)" />
            {#if !empty}<circle cx="26" cy="26" r="21" class="arc" stroke-dasharray="{v * ARC} {CIRC}" transform="rotate(135 26 26)" />{/if}
            <circle cx="26" cy="26" r="14" class="capc" />
            {#if !empty}<line x1="26" y1="26" x2="26" y2="14" class="ptr" transform="rotate({-135 + 270 * v} 26 26)" />{/if}
          </svg>
          <span class="kn">{k.map ?? k.n ?? `Macro ${i + 1}`}</span>
          <span class="kv">{empty ? '—' : k.map || on ? pct(v) : k.txt}</span>
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .macros { flex: 1; min-height: 0; display: flex; flex-direction: column; gap: 10px; touch-action: none; }
  /* The device picker reads like a dropdown: raised, a group corner. */
  .dev { height: 44px; display: flex; align-items: center; gap: 10px; padding: 0 12px; background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); border-radius: min(var(--r-group), 14px); text-align: left; }
  .k { font-size: 11px; color: var(--text-muted); }
  .dn { flex: 1; min-width: 0; font-size: 13px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .pos { font-size: 11px; color: var(--text-muted); }
  .dev path { fill: none; stroke: var(--text-muted); stroke-width: 1.5; }
  /* Eight knobs on one card, cells on seams. */
  .grid { flex: 1; min-height: 0; display: grid; grid-auto-rows: minmax(0, 1fr); gap: 1px; background: var(--seam); border: 1px solid var(--seam); border-radius: var(--r-card); overflow: hidden; }
  .knob { min-height: 0; position: relative; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; background: var(--panel); touch-action: none; }
  .knob > svg, .kn, .kv { position: relative; }
  /* The knob being turned: a quiet tint of the track colour behind it. */
  .knob > .tint { position: absolute; inset: 0; background: var(--tc); opacity: 0; transition: opacity var(--ease); }
  .knob.on > .tint { opacity: .14; }
  .auto { position: absolute; top: 8px; right: 8px; width: 6px; height: 6px; border-radius: 50%; background: var(--text-muted); }
  /* Nota's knob: a grey groove, the value arc in the track colour, a raised face with a seam edge. */
  .trk { fill: none; stroke: var(--bg-sunken); stroke-width: 5; stroke-linecap: round; }
  .arc { fill: none; stroke: var(--tc); stroke-width: 5; stroke-linecap: round; }
  .capc { fill: var(--surface-raised); stroke: var(--seam); }
  .ptr { stroke: var(--text-primary); stroke-width: 2.4; stroke-linecap: round; }
  .kn { font-size: 11px; color: var(--text-secondary); max-width: 92%; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .kv { font-size: 12px; font-weight: 500; color: var(--text-primary); }
  .on .kn { color: var(--text-primary); }
  .knob.empty .kn, .knob.empty .kv { color: var(--text-faint); }
  .empty { flex: 1; display: flex; align-items: center; justify-content: center; font-size: 12px; color: var(--text-tertiary); }
  .knob.empty { flex: initial; display: flex; }
</style>
