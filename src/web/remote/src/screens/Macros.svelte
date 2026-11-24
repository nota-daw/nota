<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Macros: a controller surface for one device of the track's chain (specs/114). Track ◀ ▶ and
     Device ◀ ▶ step as a controller's buttons do (and move the controller with them when the
     phone follows Nota); the LED display shows the device page Nota renders; the page buttons are
     the controller's pages for that device; eight knobs turn that page's parameters. A vertical
     swipe turns a knob, as in Nota; values carry the track's colour, the knob being turned a quiet
     tint of it; a dot marks an automated parameter. A knob mapped with MIDI Learn shows what it
     drives instead. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { pct } from '../lib/fmt';
  import Led from '../components/Led.svelte';

  let { form }: { form: Form } = $props();
  const land = $derived(form === 'phoneLand');
  const size = $derived(form === 'tablet' ? 96 : land ? 40 : 52);
  // The display alone on the whole screen (specs/114).
  let full = $state(false);
  const m = $derived(app.mac?.id === app.proj?.you ? app.mac : null);
  const devName = $derived(m?.devs[m.di] ?? 'No devices');
  const pages = $derived(m?.pgs ?? []);
  const pg = $derived(m?.pg ?? 0);
  // Values carry the playing track's colour (spec 090), neutral ink without a track.
  const tc = $derived(app.track?.color ?? 'var(--viz-ink)');
  const ledColor = $derived(app.track?.color ?? '');
  const dimmed = $derived(app.link !== 'ok' && app.link !== 'weak');
  const led = $derived(app.led && app.led.id === app.proj?.you ? app.led : null);
  // The tracks Track ◀ ▶ walk on the phone: what can hold a device.
  const order = $derived(app.proj?.tracks.filter((t) => t.kind !== 'group' && t.kind !== 'return').map((t) => t.id) ?? []);
  const at = $derived(order.indexOf(app.proj?.you ?? 0));

  let local = $state<Record<number, number>>({});
  let mappedPos = $state<Record<number, number>>({});
  let drag: { i: number; y: number; v: number } | null = null;
  let active = $state(-1);
  let lastSend = 0;
  let pageRow = $state<HTMLDivElement>();

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
  // Keep the current page button in view when the row scrolls (a plug-in's many pages).
  $effect(() => {
    void pg;
    const b = pageRow?.querySelector<HTMLElement>('button.on');
    // Scroll the row only: scrollIntoView would also scroll the page itself (specs/114 review).
    const row = pageRow;
    if (!b || !row) return;
    const bl = b.offsetLeft, br = bl + b.offsetWidth, bt = b.offsetTop, bb = bt + b.offsetHeight;
    if (bl < row.scrollLeft) row.scrollLeft = bl;
    else if (br > row.scrollLeft + row.clientWidth) row.scrollLeft = br - row.clientWidth;
    if (bt < row.scrollTop) row.scrollTop = bt;
    else if (bb > row.scrollTop + row.clientHeight) row.scrollTop = bb - row.clientHeight;
  });
  const ARC = 98.9, CIRC = 131.9;
</script>

<div class="macros" class:land onpointermove={move} onpointerup={up} onpointercancel={up} role="group" aria-label="Macros">
  <div class="nav">
    <div class="step">
      <button aria-label="Previous track" disabled={at <= 0} onclick={() => app.nav('track', -1)}><svg viewBox="0 0 10 10"><path d="M6.5 2 L3.5 5 L6.5 8" /></svg></button>
      <div class="mid"><span class="k">Track</span><span class="v"><i class="sw" style:background={tc}></i>{app.track?.name ?? '—'}</span></div>
      <button aria-label="Next track" disabled={at < 0 || at >= order.length - 1} onclick={() => app.nav('track', 1)}><svg viewBox="0 0 10 10"><path d="M3.5 2 L6.5 5 L3.5 8" /></svg></button>
    </div>
    <div class="step">
      <button aria-label="Previous device" disabled={!m || m.di <= 0} onclick={() => app.nav('device', -1)}><svg viewBox="0 0 10 10"><path d="M6.5 2 L3.5 5 L6.5 8" /></svg></button>
      <button class="mid pick" onclick={() => m && m.devs.length > 1 && (app.sheet = 'devices')}>
        <span class="k">{m && m.devs.length > 1 ? `Device ${m.di + 1}/${m.devs.length}` : 'Device'}</span>
        <span class="v">{devName}</span>
      </button>
      <button aria-label="Next device" disabled={!m || m.di >= m.devs.length - 1} onclick={() => app.nav('device', 1)}><svg viewBox="0 0 10 10"><path d="M3.5 2 L6.5 5 L3.5 8" /></svg></button>
    </div>
  </div>

  <div class="screen">
    <Led {led} color={ledColor} dim={dimmed} fit={land} />

    <div class="bar">
      <div class="keys">
        <!-- Macros follows the hardware controller (its track, device, page), or stays put. -->
        <button class="key follow" class:on={app.prefs.macFollow} aria-pressed={app.prefs.macFollow}
          aria-label="Follow the controller" onclick={() => app.setMacFollow(!app.prefs.macFollow)}>
          <svg viewBox="0 0 14 14"><path d="M5.5 8.5 L8.5 5.5 M6 3.8 L7.3 2.5 A2.5 2.5 0 0 1 11.5 6.7 L10.2 8 M8 10.2 L6.7 11.5 A2.5 2.5 0 0 1 2.5 7.3 L3.8 6" /></svg>
          <span>Follow</span>
        </button>
        <!-- Off the glass, so it never covers the display's header. -->
        <button class="key icon" aria-label="Show the display full screen" onclick={() => (full = true)}>
          <svg viewBox="0 0 14 14"><path d="M2 5.5 V2 H5.5 M8.5 2 H12 V5.5 M12 8.5 V12 H8.5 M5.5 12 H2 V8.5" /></svg>
        </button>
      </div>
      {#if pages.length > 1}
        <div class="pages" bind:this={pageRow} role="tablist" aria-label="Pages">
          {#each pages as name, i (i)}
            <button role="tab" aria-selected={i === pg} class:on={i === pg} onclick={() => app.nav('pageAt', i)}>{name}</button>
          {/each}
        </div>
      {/if}
    </div>
  </div>

  {#if !m || m.devs.length === 0}
    <div class="none">{m ? 'This track has no instrument or effects to control.' : 'Loading…'}</div>
  {:else}
    <div class="grid" style:--tc={tc}>
      {#each m.k as k, i (i)}
        {@const v = local[i] ?? (k.map ? mappedPos[i] ?? 0.5 : k.v ?? 0)}
        {@const on = active === i}
        {@const empty = k.n === undefined && !k.map}
        <div class="knob" class:on class:empty class:learnable={app.learn.on && !empty}
          onpointerdown={(e) => !empty && down(e, i, v)} role="slider" tabindex="0"
          aria-label={k.map ?? k.n ?? `Knob ${i + 1}`} aria-valuenow={Math.round(v * 100)} aria-valuetext={k.map ? pct(v) : k.txt}>
          <span class="tint"></span>
          {#if k.a && !k.map}<span class="auto"></span>{/if}
          <svg width={size} height={size} viewBox="0 0 52 52">
            <circle cx="26" cy="26" r="21" class="trk" stroke-dasharray="{ARC} {CIRC}" transform="rotate(135 26 26)" />
            {#if !empty}<circle cx="26" cy="26" r="21" class="arc" stroke-dasharray="{v * ARC} {CIRC}" transform="rotate(135 26 26)" />{/if}
            <circle cx="26" cy="26" r="14" class="capc" />
            {#if !empty}<line x1="26" y1="26" x2="26" y2="14" class="ptr" transform="rotate({-135 + 270 * v} 26 26)" />{/if}
          </svg>
          <span class="kn">{k.map ?? k.n ?? '—'}</span>
          <span class="kv">{empty ? '' : k.map || on ? pct(v) : k.txt}</span>
        </div>
      {/each}
    </div>
  {/if}

  {#if full}
    <!-- The display alone, as large as the screen allows; the pages stay reachable under it. -->
    <div class="full" role="dialog" aria-label="Device display">
      <Led {led} color={ledColor} dim={dimmed} fit />
      <div class="fullnav">
        <button class="btn" aria-label="Previous page" disabled={pg <= 0} onclick={() => app.nav('page', -1)}><svg viewBox="0 0 10 10"><path d="M6.5 2 L3.5 5 L6.5 8" /></svg></button>
        <span class="fulltitle">{devName}{pages.length > 1 ? ` · ${pages[pg] ?? ''}` : ''}</span>
        <button class="btn" aria-label="Next page" disabled={pg >= pages.length - 1} onclick={() => app.nav('page', 1)}><svg viewBox="0 0 10 10"><path d="M3.5 2 L6.5 5 L3.5 8" /></svg></button>
        <button class="btn" aria-label="Close the full-screen display" onclick={() => (full = false)}><svg viewBox="0 0 10 10"><path d="M2 2 L8 8 M8 2 L2 8" /></svg></button>
      </div>
    </div>
  {/if}
</div>

<style>
  .macros { flex: 1; min-height: 0; display: flex; flex-direction: column; gap: 8px; touch-action: none; }
  /* A phone on its side (specs/114): the display as large as the height allows on the left, the
     steppers, Follow and the pages beside it, the eight knobs in one row underneath. */
  .macros.land { display: grid; grid-template-columns: minmax(0, 1fr) minmax(150px, 28%); grid-template-rows: auto minmax(0, 1fr) auto;
    grid-template-areas: 'led nav' 'led bar' 'knobs knobs'; gap: 8px; }
  .macros.land .screen { display: contents; }
  .macros.land .screen > :global(.lcd) { grid-area: led; }
  .macros.land .nav { grid-area: nav; grid-template-columns: minmax(0, 1fr); gap: 6px; }
  .macros.land .step { height: 40px; }
  /* Beside the display the pages are a two-column grid of even keys from the top, as tall as
     they need (it scrolls when a plug-in has many), never stretched over the free height. */
  .macros.land .keys .follow { flex: 1; }
  .macros.land .bar { grid-area: bar; flex-direction: column; align-items: stretch; justify-content: flex-start; min-height: 0; }
  .macros.land .pages { flex: 0 1 auto; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); grid-auto-rows: 34px;
    align-content: start; min-height: 0; overflow-x: hidden; overflow-y: auto; touch-action: pan-y; }
  .macros.land .pages > button { height: auto; min-width: 0; padding: 0 6px; overflow: hidden; text-overflow: ellipsis; }
  .macros.land .grid { grid-area: knobs; flex: none; grid-template-columns: repeat(8, minmax(0, 1fr)); grid-auto-rows: auto; }
  .macros.land .knob { padding: 5px 1px 4px; gap: 1px; }
  .macros.land .kn { font-size: 9.5px; }
  .macros.land .kv { font-size: 10.5px; }
  .macros.land .none { grid-area: knobs; }

  /* Follow and the page row under the display. */
  .bar { display: flex; gap: 6px; align-items: center; }
  .keys { flex: none; display: flex; gap: 6px; }
  .key { flex: none; height: 40px; padding: 0 10px; display: inline-flex; align-items: center; justify-content: center; gap: 6px;
    border-radius: min(var(--r-group), 14px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel);
    color: var(--text-secondary); font-size: 12px; font-weight: 500; transition: background-color var(--ease), color var(--ease); }
  .key.on { background: var(--accent-soft); border-color: var(--accent-soft); color: var(--picked-ink); box-shadow: none; }
  .key.icon { width: 40px; padding: 0; color: var(--text-muted); }
  .key:active { background: var(--bg-sunken); box-shadow: none; }
  .key svg { width: 14px; height: 14px; }
  .key path { fill: none; stroke: currentColor; stroke-width: 1.4; }
  .bar .pages { flex: 1; min-width: 0; }

  /* The display alone on the whole screen, the page steps under it. */
  .full { position: fixed; inset: 0; z-index: 25; display: flex; flex-direction: column; gap: 10px; background: var(--bg-app);
    padding: max(12px, env(safe-area-inset-top)) max(12px, env(safe-area-inset-right)) max(12px, env(safe-area-inset-bottom)) max(12px, env(safe-area-inset-left)); }
  .fullnav { flex: none; display: flex; align-items: center; gap: 10px; }
  .fullnav .btn { width: 56px; }
  .fullnav svg { width: 10px; height: 10px; }
  .fullnav path { fill: none; stroke: currentColor; stroke-width: 1.6; }
  .fullnav .btn:disabled { opacity: .4; }
  .fulltitle { flex: 1; min-width: 0; text-align: center; font-size: 13px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

  .nav { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 8px; }
  /* A stepper reads like a raised key with arrows at its ends. */
  .step { height: 44px; display: flex; align-items: stretch; background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); border-radius: min(var(--r-group), 14px); overflow: hidden; }
  .step > button:not(.mid) { width: 36px; flex: none; display: grid; place-items: center; color: var(--text-muted); }
  .step > button:not(.mid):active { background: var(--bg-sunken); }
  .step > button:disabled { color: var(--text-faint); opacity: .45; }
  .step svg { width: 10px; height: 10px; }
  .step path { fill: none; stroke: currentColor; stroke-width: 1.6; }
  .mid { flex: 1; min-width: 0; display: flex; flex-direction: column; justify-content: center; align-items: center; border-inline: 1px solid var(--seam); line-height: 1.15; padding: 0 4px; }
  .mid.pick:active { background: var(--bg-sunken); }
  .k { font-size: 10px; color: var(--text-muted); }
  .v { max-width: 100%; display: flex; align-items: center; gap: 6px; font-size: 12.5px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sw { width: 8px; height: 8px; flex: none; border-radius: var(--r-fine); }

  .screen { display: flex; flex-direction: column; gap: 8px; min-height: 0; }
  /* The pages as a segment row; it scrolls sideways when there are many. */
  .pages { position: relative; display: flex; gap: 2px; padding: 2px; background: var(--bg-sunken); box-shadow: var(--sunken); border-radius: min(var(--r-group), 14px); overflow-x: auto; scrollbar-width: none; touch-action: pan-x; }
  .pages::-webkit-scrollbar { display: none; }
  .pages > button { flex: 1 0 auto; min-width: 56px; height: 36px; padding: 0 10px; border-radius: min(var(--r-cell), 12px); color: var(--text-secondary); font-size: 12px; font-weight: 500; white-space: nowrap; transition: background-color var(--ease), color var(--ease); }
  .pages > button.on { background: var(--accent-soft); color: var(--picked-ink); }

  /* Eight knobs on one card, cells on seams: four across, two rows. */
  .grid { flex: 1; min-height: 0; display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); grid-auto-rows: minmax(0, 1fr); gap: 1px; background: var(--seam); border: 1px solid var(--seam); border-radius: var(--r-card); overflow: hidden; }
  .knob { min-height: 0; position: relative; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; background: var(--panel); touch-action: none; padding: 4px 2px; }
  .knob > svg, .kn, .kv { position: relative; }
  .knob > .tint { position: absolute; inset: 0; background: var(--tc); opacity: 0; transition: opacity var(--ease); }
  .knob.on > .tint { opacity: .14; }
  .auto { position: absolute; top: 8px; right: 8px; width: 6px; height: 6px; border-radius: 50%; background: var(--text-muted); }
  /* Nota's knob: a grey groove, the value arc in the track colour, a raised face with a seam edge. */
  .trk { fill: none; stroke: var(--bg-sunken); stroke-width: 5; stroke-linecap: round; }
  .arc { fill: none; stroke: var(--tc); stroke-width: 5; stroke-linecap: round; }
  .capc { fill: var(--surface-raised); stroke: var(--seam); }
  .ptr { stroke: var(--text-primary); stroke-width: 2.4; stroke-linecap: round; }
  .kn { font-size: 10.5px; color: var(--text-secondary); max-width: 94%; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .kv { font-size: 11.5px; font-weight: 500; color: var(--text-primary); white-space: nowrap; min-height: 1.2em; }
  .on .kn { color: var(--text-primary); }
  .knob.empty .kn { color: var(--text-faint); }
  .none { flex: 1; display: flex; align-items: center; justify-content: center; font-size: 12px; color: var(--text-tertiary); }
  @media (prefers-reduced-motion: reduce) { .knob > .tint, .pages > button, .key { transition: none; } }
</style>
