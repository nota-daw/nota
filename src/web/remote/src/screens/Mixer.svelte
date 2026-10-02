<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Mixer: a channel per track, scrolling sideways, the master pinned right. Groups are folded
     until tapped, as in the arrangement. A fader moves relative to the finger (no jump to the
     touch), a double tap resets it to 0 dB; values follow Nota live, and a fader another phone
     is holding carries their name. Mute and Solo read as letters, not colour alone. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import type { Track, MixRow } from '../lib/types';

  let { form }: { form: Form } = $props();
  const MAX = 1.5;

  const rows = $derived(new Map((app.mix?.ch ?? []).map((r) => [r[0], r] as [number, MixRow])));
  const tracks = $derived(app.proj?.tracks ?? []);
  const open = $derived(new Set(app.prefs.groupsOpen));

  // A track shows unless one of its groups is folded.
  const visible = $derived.by(() => {
    const byId = new Map(tracks.map((t) => [t.id, t]));
    return tracks.filter((t) => {
      let g = t.grp, guard = 0;
      while (g > 0 && guard++ < 16) {
        if (!open.has(g)) return false;
        g = byId.get(g)?.grp ?? -1;
      }
      return true;
    });
  });
  const childCount = (id: number) => tracks.filter((t) => t.grp === id).length;

  // Local values while a finger holds a control, so Nota's echo doesn't fight the finger.
  let local = $state<Record<string, number>>({});
  let drag = $state<{ key: string; id: number; kind: "vol" | "pan" | "mvol"; start: number; y: number; x: number; size: number } | null>(null);
  const lastTap = new Map<string, number>();

  function begin(e: PointerEvent, key: string, id: number, kind: 'vol' | 'pan' | 'mvol', value: number) {
    if (!app.canControl) { app.showToast('Nota lets this phone play notes only.'); return; }
    const el = e.currentTarget as HTMLElement;
    el.setPointerCapture(e.pointerId);
    const now = performance.now();
    if (now - (lastTap.get(key) ?? 0) < 300) {
      lastTap.delete(key);
      const reset = kind === 'pan' ? 0 : 1;
      local[key] = reset;
      app.send({ t: kind, id, v: reset, ph: 2 });
      setTimeout(() => delete local[key], 400);
      drag = null;
      return;
    }
    lastTap.set(key, now);
    const r = el.getBoundingClientRect();
    drag = { key, id, kind, start: value, y: e.clientY, x: e.clientX, size: kind === 'pan' ? r.width : r.height };
    local[key] = value;
    app.send({ t: kind, id, v: value, ph: 1 });
  }
  let lastSend = 0;
  function moveDrag(e: PointerEvent) {
    if (!drag) return;
    const v = drag.kind === 'pan'
      ? Math.max(-1, Math.min(1, drag.start + ((e.clientX - drag.x) / drag.size) * 2))
      : Math.max(0, Math.min(MAX, drag.start + ((drag.y - e.clientY) / drag.size) * MAX));
    local[drag.key] = v;
    const now = performance.now();
    if (now - lastSend > 30) { lastSend = now; app.send({ t: drag.kind, id: drag.id, v, ph: 0 }); }
  }
  function end() {
    if (!drag) return;
    const d = drag;
    drag = null;
    app.send({ t: d.kind, id: d.id, v: local[d.key], ph: 2 });
    setTimeout(() => { if (drag?.key !== d.key) delete local[d.key]; }, 400);
  }

  function toggle(kind: 'mute' | 'solo' | 'arm', t: Track, on: boolean) {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    app.send({ t: kind, id: t.id, on });
  }
  function fold(t: Track) {
    const s = new Set(app.prefs.groupsOpen);
    if (s.has(t.id)) s.delete(t.id); else s.add(t.id);
    app.prefs.groupsOpen = [...s];
  }

  const db = (v: number) => {
    if (v < 1e-4) return '−∞';
    const d = 20 * Math.log10(v);
    return (d >= 0.05 ? '+' : d <= -0.05 ? '−' : '') + Math.abs(d).toFixed(1);
  };
  const panText = (p: number) => (Math.abs(p) < 0.01 ? 'C' : (p < 0 ? 'L' : 'R') + Math.round(Math.abs(p) * 50));
  const heldBy = (r: MixRow | undefined) => (r && r[8] && r[9] !== app.conn ? r[8] : '');
  const master = $derived(app.mix?.m);
</script>

<div class="mixer {form}" onpointermove={moveDrag} onpointerup={end} onpointercancel={end} role="group" aria-label="Mixer">
  <div class="strips">
    {#each visible as t (t.id)}
      {@const r = rows.get(t.id)}
      {@const vol = local['v' + t.id] ?? r?.[1] ?? 1}
      {@const pan = local['p' + t.id] ?? r?.[2] ?? 0}
      {@const held = heldBy(r)}
      {@const isGroup = t.kind === 'group'}
      <div class="strip" class:group={isGroup} class:active={drag?.key === 'v' + t.id}>
        <button class="head" onclick={() => isGroup && fold(t)} aria-label={isGroup ? `${t.name}: ${open.has(t.id) ? 'fold' : 'unfold'}` : t.name}>
          <span class="cbar" style:background={t.color}></span>
          <span class="nm">{t.name}</span>
          {#if isGroup}
            <span class="tag mono">
              <svg width="8" height="8" viewBox="0 0 8 8" style:transform={open.has(t.id) ? 'rotate(90deg)' : 'none'}><path d="M2.5 1.5 L5.5 4 L2.5 6.5" /></svg>{childCount(t.id)}
            </span>
          {/if}
        </button>
        <div class="pan" onpointerdown={(e) => begin(e, 'p' + t.id, t.id, 'pan', pan)} role="slider" tabindex="0" aria-label="{t.name} pan" aria-valuetext={panText(pan)} aria-valuenow={pan}>
          <div class="pbar"><span style:left="{50 + pan * 50}%"></span></div>
          <span class="mono pv">{panText(pan)}</span>
        </div>
        <div class="fz">
          {#if held}<span class="held">{held} has it</span>{/if}
          <div class="meters"><div><span style:height="{(r?.[6] ?? 0) * 100}%"></span></div><div><span style:height="{(r?.[7] ?? 0) * 100}%"></span></div></div>
          <div class="fader" onpointerdown={(e) => begin(e, 'v' + t.id, t.id, 'vol', vol)} role="slider" tabindex="0" aria-label="{t.name} volume" aria-valuetext="{db(vol)} dB" aria-valuenow={vol}>
            <div class="slot"></div>
            <div class="travel"><span class="cap" class:held={!!held} style:bottom="{(vol / MAX) * 100}%"><i></i></span></div>
          </div>
        </div>
        <span class="mono dbv" class:act={drag?.key === 'v' + t.id}>{db(vol)}</span>
        <div class="btns">
          <button class="ms" class:mute={r?.[3]} aria-pressed={!!r?.[3]} aria-label="Mute {t.name}" onclick={() => toggle('mute', t, !r?.[3])}>M</button>
          <button class="ms" class:solo={r?.[4]} aria-pressed={!!r?.[4]} aria-label="Solo {t.name}" onclick={() => toggle('solo', t, !r?.[4])}>S</button>
          {#if t.kind !== 'group' && t.kind !== 'return'}
            <button class="arm" class:on={r?.[5]} aria-pressed={!!r?.[5]} aria-label="Arm {t.name}" onclick={() => toggle('arm', t, !r?.[5])}><span></span>ARM</button>
          {/if}
        </div>
      </div>
    {/each}
  </div>
  <div class="rule"></div>
  <div class="strip masterstrip">
    <div class="head mh"><span class="caps">Master</span></div>
    <div class="fz">
      {#if heldBy(master as any)}<span class="held">{master?.[3]} has it</span>{/if}
      <div class="meters wide"><div><span style:height="{(master?.[1] ?? 0) * 100}%"></span></div><div><span style:height="{(master?.[2] ?? 0) * 100}%"></span></div></div>
      <div class="fader" onpointerdown={(e) => begin(e, 'm', 0, 'mvol', local['m'] ?? master?.[0] ?? 1)} role="slider" tabindex="0" aria-label="Master volume" aria-valuenow={local['m'] ?? master?.[0] ?? 1}>
        <div class="slot"></div>
        <div class="travel"><span class="cap" style:bottom="{((local['m'] ?? master?.[0] ?? 1) / MAX) * 100}%"><i></i></span></div>
      </div>
    </div>
    <span class="mono dbv">{db(local['m'] ?? master?.[0] ?? 1)}</span>
  </div>
</div>

<style>
  .mixer { flex: 1; min-height: 0; display: flex; gap: 8px; touch-action: none; }
  .strips { flex: 1; min-width: 0; display: flex; gap: 6px; overflow-x: auto; overflow-y: hidden; touch-action: pan-x; overscroll-behavior: contain; }
  .strip { flex: 0 0 88px; min-width: 0; display: flex; flex-direction: column; gap: 6px; padding: 8px 6px; border-radius: 6px; background: var(--panel); border: 1px solid var(--border); }
  .phoneLand .strip { flex-basis: 84px; }
  .tablet .strip { flex: 1 1 0; min-width: 84px; }
  .strip.group { background: var(--well); border-color: var(--hairline); }
  .strip.active { border-color: var(--border-brass); }
  .head { display: flex; align-items: center; gap: 6px; min-width: 0; height: 28px; text-align: left; }
  .cbar { width: 3px; height: 16px; border-radius: 2px; flex: none; }
  .nm { flex: 1; min-width: 0; font-size: 12px; font-weight: 600; color: var(--ink1); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .tag { display: flex; align-items: center; gap: 2px; font-size: 9px; color: var(--ink5); }
  .tag path { fill: none; stroke: var(--ink5); stroke-width: 1.4; stroke-linecap: round; }
  .pan { height: 22px; display: flex; align-items: center; gap: 4px; touch-action: none; }
  .pbar { flex: 1; height: 4px; border-radius: 2px; background: var(--well); position: relative; }
  .pbar span { position: absolute; top: -3px; width: 3px; height: 10px; margin-left: -1.5px; border-radius: 1px; background: var(--ink2); }
  .pv { font-size: 9px; color: var(--ink4); width: 22px; text-align: right; }
  .fz { flex: 1; min-height: 0; display: flex; gap: 6px; justify-content: center; position: relative; }
  .held { position: absolute; left: 50%; top: 0; transform: translateX(-50%); z-index: 2; height: 18px; display: flex; align-items: center; padding: 0 6px; border-radius: 9px; background: var(--track-off); border: 1px solid var(--border-strong); font-size: 9px; font-weight: 600; color: var(--ink1); white-space: nowrap; }
  .meters { display: flex; gap: 2px; padding: 4px 0; }
  .meters div { width: 4px; height: 100%; border-radius: 2px; background: var(--well); position: relative; overflow: hidden; }
  .meters.wide div { width: 5px; }
  .meters span { position: absolute; left: 0; right: 0; bottom: 0; background: var(--success); }
  .fader { width: 40px; position: relative; display: flex; justify-content: center; padding: 10px 0; touch-action: none; }
  .slot { width: 4px; height: 100%; border-radius: 2px; background: var(--well); border: 1px solid var(--hairline); }
  .travel { position: absolute; left: 0; right: 0; top: 10px; bottom: 10px; }
  .cap { position: absolute; left: 4px; right: 4px; height: 20px; margin-bottom: -10px; border-radius: 4px; background: var(--raised); border: 1px solid var(--border-strong); display: flex; align-items: center; justify-content: center; }
  .cap.held { border-color: var(--steel); }
  .cap i { width: 18px; height: 2px; border-radius: 1px; background: var(--ink3); }
  .active .cap i { background: var(--accent-bright); }
  .dbv { font-size: 11px; color: var(--ink2); text-align: center; }
  .dbv.act { color: var(--accent-bright); }
  .btns { display: grid; grid-template-columns: 1fr 1fr; gap: 4px; }
  .ms { height: 34px; border-radius: 4px; background: var(--raised); border: 1px solid var(--border); font-size: 12px; font-weight: 700; color: var(--ink4); }
  .ms.mute { background: var(--track-off); border-color: var(--ink5); color: var(--ink0); }
  .ms.solo { background: var(--accent); border-color: var(--accent); color: var(--on-accent); }
  .arm { grid-column: span 2; height: 30px; border-radius: 4px; background: var(--raised); border: 1px solid var(--border); display: flex; align-items: center; justify-content: center; gap: 5px; font-size: 10px; font-weight: 700; letter-spacing: .08em; color: var(--ink4); }
  .arm span { width: 8px; height: 8px; border-radius: 50%; background: var(--ink6); }
  .arm.on { background: var(--record-wash); border-color: var(--record); color: var(--record-ink); }
  .arm.on span { background: var(--record); }
  .rule { width: 1px; background: var(--hairline); }
  .masterstrip { flex: 0 0 84px; }
  .tablet .masterstrip { flex-basis: 110px; }
  .mh { justify-content: center; }
</style>
