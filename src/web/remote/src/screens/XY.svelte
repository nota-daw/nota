<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- XY: a full field whose axes drive two parameters — Nota Flux's vector, else Cutoff ×
     Resonance of the track's first Auto Filter, or whatever MIDI Learn mapped them to. On
     release the point stays (Hold) or glides back (Return). Tilt hands the point to the phone's
     gyroscope. With Record on, moves write latch automation in Nota. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import { tiltSupport, requestTilt, followTilt, type Form } from '../lib/device.svelte';

  let { form }: { form: Form } = $props();
  const portrait = $derived(form === 'phone');

  let x = $state(0.5), y = $state(0.5);
  let dragging = $state(false);
  let tilt = $state(false);
  let gliding = false;
  let home = { x: 0.5, y: 0.5 };
  let startPt = { x: 0, y: 0 };
  let axisLock = 0;              // learn mode: 1 = X only, 2 = Y only
  let lastSend = 0;
  let stopTilt: (() => void) | null = null;
  const support = tiltSupport();

  // Follow Nota's values while the finger is off (automation, the mouse, another phone).
  $effect(() => {
    const s = app.xy;
    if (!s || dragging || tilt || gliding) return;
    if (s.x.v !== undefined && !s.x.map) x = s.x.v;
    if (s.y.v !== undefined && !s.y.map) y = s.y.v;
  });

  function send(ph: number, src = 'xy') {
    const now = performance.now();
    if (ph === 0 && now - lastSend < 30) return;
    lastSend = now;
    app.send({ t: 'xy', v: x, v2: y, ph, s: src, i: app.learn.on ? axisLock : 0 });
  }

  function at(e: PointerEvent, el: HTMLElement) {
    const r = el.getBoundingClientRect();
    x = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
    y = Math.min(1, Math.max(0, 1 - (e.clientY - r.top) / r.height));
  }

  function down(e: PointerEvent) {
    if (tilt || !app.canControl) { if (!app.canControl) app.showToast('Nota lets this phone play notes only.'); return; }
    const el = e.currentTarget as HTMLElement;
    el.setPointerCapture(e.pointerId);
    gliding = false;
    home = { x, y };
    startPt = { x: e.clientX, y: e.clientY };
    axisLock = 0;
    dragging = true;
    at(e, el);
    if (!app.learn.on) send(1);
  }
  function move(e: PointerEvent) {
    if (!dragging) return;
    if (app.learn.on && axisLock === 0) {
      const dx = Math.abs(e.clientX - startPt.x), dy = Math.abs(e.clientY - startPt.y);
      if (Math.max(dx, dy) < 14) return;
      axisLock = dx >= dy ? 1 : 2;
    }
    at(e, e.currentTarget as HTMLElement);
    send(0);
  }
  function up() {
    if (!dragging) return;
    dragging = false;
    send(2);
    if (!app.prefs.xyHold) glideHome();
  }

  function glideHome() {
    gliding = true;
    const step = () => {
      if (!gliding || dragging) return;
      x += (home.x - x) * 0.2;
      y += (home.y - y) * 0.2;
      const done = Math.abs(home.x - x) + Math.abs(home.y - y) < 0.003;
      if (done) { x = home.x; y = home.y; gliding = false; send(2); return; }
      send(0);
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  async function toggleTilt() {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    if (tilt) { tilt = false; stopTilt?.(); stopTilt = null; send(2, 'tilt'); return; }
    if (!(await requestTilt())) {
      app.showToast('Motion access was refused. Allow it in Settings → Safari → Motion & Orientation Access, then reload.');
      return;
    }
    tilt = true;
    stopTilt = followTilt((tx, ty) => { x = tx; y = ty; send(0, 'tilt'); });
  }
  $effect(() => () => stopTilt?.());

  const label = (a: 'x' | 'y') => {
    const s = app.xy?.[a];
    if (!s) return a === 'x' ? 'X' : 'Y';
    if (tilt && s.tilt) return s.tilt;
    return s.map ?? s.n ?? (a === 'x' ? 'X' : 'Y');
  };
  const sub = (a: 'x' | 'y') => {
    const s = app.xy?.[a];
    if (!s) return '';
    if ((tilt && s.tilt) || s.map) return 'Mapped in Nota';
    return s.d ?? 'Not assigned';
  };
  const value = (a: 'x' | 'y') => {
    const s = app.xy?.[a];
    const v = a === 'x' ? x : y;
    if (s && !s.map && !(tilt && s.tilt) && s.txt && !dragging && !tilt) return s.txt;
    return Math.round(v * 100) + ' %';
  };
  const note = $derived(
    tilt ? 'Tilt the phone to move the point. Tap Tilt again to use your finger.'
    : !app.xy?.ok && !app.xy?.x.map && !app.xy?.y.map ? 'This track has no Auto Filter or Nota Flux. Add one in Nota, or map the axes with MIDI Learn.'
    : app.prefs.xyHold ? 'The point stays where you lift your finger.' : 'On release the point glides back to where it started.');
</script>

<div class="xy" class:col={portrait}>
  <div class="field" class:learnable={app.learn.on} role="slider" tabindex="0" aria-label="XY pad" aria-valuenow={Math.round(x * 100)}
    onpointerdown={down} onpointermove={move} onpointerup={up} onpointercancel={up}>
    <div class="gridlines"></div>
    <div class="fill" style:width="{x * 100}%" style:height="{y * 100}%" style:background={app.track?.color ?? 'var(--accent)'}></div>
    <div class="vl" style:left="{x * 100}%"></div>
    <div class="hl" style:top="{(1 - y) * 100}%"></div>
    <div class="pt" style:left="{x * 100}%" style:top="{(1 - y) * 100}%"><span></span></div>
    <span class="ax ax-x mono">{label('x')} →</span>
    <span class="ax ax-y mono">↑ {label('y')}</span>
    {#if tilt}<span class="pill">Tilt is moving the point</span>{/if}
  </div>
  <div class="panel">
    <div class="axes">
      {#each ['x', 'y'] as const as a}
        <div class="axis" class:learnable={app.learn.on}>
          <span class="mono an">{a.toUpperCase()}</span>
          <span class="names"><span class="pn">{label(a)}</span><span class="pd">{sub(a)}</span></span>
          <span class="mono av">{value(a)}</span>
        </div>
      {/each}
    </div>
    <div class="row">
      <div class="seg">
        <button class:on={app.prefs.xyHold} onclick={() => (app.prefs.xyHold = true)}>Hold</button>
        <button class:on={!app.prefs.xyHold} onclick={() => (app.prefs.xyHold = false)}>Return</button>
      </div>
      {#if support === 'yes' || support === 'ask'}
        <button class="btn tilt" class:on={tilt} class:learnable={app.learn.on} aria-pressed={tilt} onclick={toggleTilt}>
          <span class="ph"></span>Tilt
        </button>
      {/if}
    </div>
    <span class="note">{note}</span>
    {#if support === 'insecure'}
      <span class="note dim">Tilt needs a secure (HTTPS) page, which a local Nota can’t offer yet.</span>
    {/if}
    {#if app.xy?.auto}
      <div class="auto"><span class="rd"></span>Writing automation · latch</div>
    {/if}
  </div>
</div>

<style>
  .xy { flex: 1; min-height: 0; display: flex; gap: 12px; }
  .xy.col { flex-direction: column; }
  .field { flex: 1; min-height: 0; min-width: 0; position: relative; border-radius: 6px; background: var(--well); border: 1px solid var(--hairline); box-shadow: inset 0 1px 0 rgba(0,0,0,.5); overflow: hidden; touch-action: none; }
  .gridlines { position: absolute; inset: 0; background-image: linear-gradient(var(--grid) 1px, transparent 1px), linear-gradient(90deg, var(--grid) 1px, transparent 1px); background-size: 25% 25%; background-position: -1px -1px; }
  .fill { position: absolute; left: 0; bottom: 0; opacity: .08; }
  .vl { position: absolute; top: 0; bottom: 0; width: 1px; background: var(--accent-edge); }
  .hl { position: absolute; left: 0; right: 0; height: 1px; background: var(--accent-edge); }
  .pt { position: absolute; width: 40px; height: 40px; margin: -20px 0 0 -20px; border-radius: 50%; border: 2px solid var(--accent); background: var(--accent-wash); display: flex; align-items: center; justify-content: center; }
  .pt span { width: 10px; height: 10px; border-radius: 50%; background: var(--accent-bright); }
  .ax { position: absolute; font-size: 10px; color: var(--ink5); pointer-events: none; }
  .ax-x { right: 10px; bottom: 8px; }
  .ax-y { left: 10px; top: 8px; }
  .pill { position: absolute; left: 50%; top: 12px; transform: translateX(-50%); height: 24px; display: flex; align-items: center; padding: 0 10px; border-radius: 12px; background: var(--accent-wash); border: 1px solid var(--border-brass); font-size: 11px; font-weight: 600; color: var(--accent-bright); white-space: nowrap; }
  .panel { width: 290px; flex: none; display: flex; flex-direction: column; gap: 10px; }
  :global(.tablet) .panel { width: 340px; }
  .col .panel { width: auto; }
  .axes { border: 1px solid var(--border); border-radius: 6px; overflow: hidden; }
  .axis { height: 50px; display: flex; align-items: center; gap: 12px; padding: 0 12px; background: var(--panel); outline-offset: -3px; }
  .axis + .axis { border-top: 1px solid var(--hairline); }
  .an { font-size: 13px; color: var(--accent-dim); width: 12px; }
  .names { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
  .pn { font-size: 12px; font-weight: 600; color: var(--ink1); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .pd { font-size: 10px; color: var(--ink5); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .av { font-size: 13px; color: var(--ink1); white-space: nowrap; }
  .row { display: flex; gap: 8px; }
  .row .seg { flex: 1; }
  .row .seg button { height: 38px; }
  .tilt { height: 46px; padding: 0 14px; }
  .ph { width: 10px; height: 14px; border-radius: 2px; border: 1.5px solid currentColor; transform: rotate(-18deg); }
  .note { font-size: 11px; line-height: 1.5; color: var(--ink4); }
  .note.dim { color: var(--ink5); }
  .auto { height: 30px; align-self: flex-start; display: flex; align-items: center; gap: 8px; padding: 0 10px; border-radius: 4px; background: var(--raised); border: 1px solid var(--border); font-size: 11px; font-weight: 600; color: var(--ink1); }
  .rd { width: 7px; height: 7px; border-radius: 50%; background: var(--record); }
</style>
