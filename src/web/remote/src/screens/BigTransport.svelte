<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- The big transport, opened from the header's position: buttons sized for a hand on a
     guitar neck, the position large, sections that jump on tap and loop on hold, tap tempo. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { bpm } from '../lib/fmt';

  let { form }: { form: Form } = $props();
  let pos = $state('1.1.1');
  $effect(() => {
    let raf = 0;
    const loop = () => {
      const b = Math.max(0, app.position());
      pos = `${Math.floor(b / 4) + 1}.${Math.floor(b % 4) + 1}.${Math.floor((b * 4) % 4) + 1}`;
      raf = requestAnimationFrame(loop);
    };
    loop();
    return () => cancelAnimationFrame(raf);
  });

  const sections = $derived(app.proj?.sections ?? []);
  const beat = $derived(app.position());
  const current = $derived(sections.reduce((a, s, i) => (beat >= s[1] ? i : a), -1));
  const loopSec = $derived(app.tp.loop ? sections.findIndex((s, i) => Math.abs(s[1] - app.tp.ls) < 0.01 && Math.abs((sections[i + 1]?.[1] ?? s[1] + 16) - app.tp.le) < 0.01) : -1);

  function ctl(m: object) {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    app.send(m);
  }

  let taps: number[] = [];
  function tap() {
    const now = performance.now();
    taps = [...taps.filter((t) => now - t < 2500), now];
    if (taps.length >= 3) {
      const iv = (taps[taps.length - 1] - taps[0]) / (taps.length - 1);
      const bpm = Math.round(Math.min(220, Math.max(50, 60000 / iv)) * 100) / 100;
      ctl({ t: 'bpm', v: bpm });
    }
  }

  let holdTimer = 0;
  let held = false;
  function secDown(i: number) {
    held = false;
    holdTimer = window.setTimeout(() => {
      held = true;
      const s = sections[i], end = sections[i + 1]?.[1] ?? s[1] + 16;
      if (loopSec === i) ctl({ t: 'loop', on: false });
      else ctl({ t: 'loopRange', v: s[1], v2: end, on: true });
    }, 450);
  }
  function secUp(i: number) {
    clearTimeout(holdTimer);
    if (!held) ctl({ t: 'seek', v: sections[i][1] });
  }
  const bpmText = $derived(bpm(app.tp.bpm));
</script>

<div class="big {form}">
  <div class="top">
    <div class="pblock">
      <span class="label">Position</span>
      <span class="pos" class:rec={app.tp.rec}>{pos}</span>
    </div>
    <button class="tap" onclick={tap}>
      <span class="tv">{bpmText}</span>
      <span class="tl">Tap tempo</span>
    </button>
  </div>
  <div class="btns" style:grid-template-columns="repeat({form === 'phone' ? 3 : 6}, minmax(0, 1fr))">
    <button class="b play" class:live={app.tp.play} aria-pressed={app.tp.play} onclick={() => ctl({ t: app.tp.play ? 'stop' : 'play' })}><span class="tri"></span>Play</button>
    <button class="b" onclick={() => ctl({ t: 'stop' })}><span class="sq"></span>Stop</button>
    <button class="b recb" class:live={app.tp.rec} aria-pressed={app.tp.rec} onclick={() => app.canControl ? app.record() : ctl({})}><span class="dot"></span>Record</button>
    <button class="b" class:on={app.tp.loop} aria-pressed={app.tp.loop} onclick={() => ctl({ t: 'loop', on: !app.tp.loop })}>
      <svg width="30" height="26" viewBox="0 0 30 26"><path d="M7 9 H21 A5 5 0 0 1 21 19 H9 M9 19 L12 16 M9 19 L12 22 M7 9 A5 5 0 0 0 5 14" /></svg>Loop
    </button>
    <button class="b" class:on={app.tp.met} aria-pressed={app.tp.met} onclick={() => ctl({ t: 'met', on: !app.tp.met })}>
      <svg width="26" height="28" viewBox="0 0 26 28"><path d="M8 25 L11 3 H15 L18 25 Z M13 18 L21 8" /></svg>Click
    </button>
    <button class="b" class:dim={!app.tp.can} onclick={() => ctl({ t: 'undo' })}>
      <svg width="28" height="24" viewBox="0 0 28 24"><path d="M9 5 L4 10 L9 15 M4 10 H17 A6 6 0 0 1 17 22 H12" /></svg>Undo
    </button>
  </div>
  {#if sections.length > 0}
    <div class="secs">
      <span class="label">Sections · tap to jump, hold to loop</span>
      <div class="sgrid">
        {#each sections as s, i}
          <button class="sec" class:cur={current === i} class:looping={loopSec === i} onpointerdown={() => secDown(i)} onpointerup={() => secUp(i)} onpointercancel={() => clearTimeout(holdTimer)}>
            <span class="sn">{s[0]}</span>
            <span class="si">{#if loopSec === i}Looping{:else}Bar {Math.floor(s[1] / 4) + 1}{/if}</span>
          </button>
        {/each}
      </div>
    </div>
  {/if}
</div>

<style>
  .big { position: absolute; inset: 0; z-index: 4; background: var(--bg-app); display: flex; flex-direction: column; gap: 14px; padding: 12px; }
  .phoneLand.big { padding: 8px 10px; gap: 10px; }
  .tablet.big { padding: 18px; }
  .top { display: flex; align-items: flex-end; justify-content: space-between; gap: 12px; }
  .pblock { display: flex; flex-direction: column; gap: 4px; }
  .pos { font-size: 52px; font-weight: 500; line-height: 1; color: var(--text-primary); }
  .phoneLand .pos { font-size: 40px; }
  .tablet .pos { font-size: 88px; }
  .pos.rec { color: var(--record); }
  .tap { height: 56px; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2px; padding: 0 16px; border-radius: min(var(--r-group), 16px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); }
  .tap:active { background: var(--bg-sunken); box-shadow: none; }
  .tv { font-size: 18px; font-weight: 500; color: var(--text-primary); }
  .tl { font-size: 11px; color: var(--text-muted); }
  .btns { flex: 1; min-height: 0; display: grid; grid-auto-rows: minmax(0, 1fr); gap: 6px; }
  /* Keys sized for a hand on a guitar neck, on cards. Play running is live (solid accent),
     Record solid record; Loop and Click on are picked — the soft tint with orange icon and text. */
  .b { min-height: 0; border-radius: var(--r-card); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10px; font-size: 14px; font-weight: 600; color: var(--text-strong); transition: background-color var(--ease), color var(--ease); }
  .b:active { background: var(--bg-sunken); box-shadow: none; }
  .b svg path { fill: none; stroke: currentColor; stroke-width: 2; }
  .b.on { background: var(--accent-soft); border-color: var(--accent-soft); box-shadow: none; color: var(--picked-ink); }
  .b.live { background: var(--accent); border-color: var(--accent); box-shadow: none; color: var(--ink-on-accent); }
  .b.dim { color: var(--text-faint); }
  .tri { width: 0; height: 0; border-left: 26px solid currentColor; border-top: 16px solid transparent; border-bottom: 16px solid transparent; margin-left: 6px; }
  .sq { width: 26px; height: 26px; border-radius: var(--r-fine); background: currentColor; }
  .dot { width: 28px; height: 28px; border-radius: 50%; background: var(--record); }
  .recb.live { background: var(--record); border-color: var(--record); color: var(--ink-on-record); }
  .recb.live .dot { background: var(--ink-on-record); }
  .secs { display: flex; flex-direction: column; gap: 6px; }
  .sgrid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 4px; }
  /* Sections: the playhead's one is picked (soft tint, orange name); a looping one is live (solid). */
  .sec { height: 52px; position: relative; border-radius: min(var(--r-cell), 16px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2px; }
  .sec.cur { background: var(--accent-soft); border-color: var(--accent-soft); box-shadow: none; }
  .sec.cur .sn { color: var(--picked-ink); }
  .sec.looping { background: var(--accent); border-color: var(--accent); box-shadow: none; }
  .sn { font-size: 13px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 92%; }
  .si { font-size: 11px; color: var(--text-tertiary); }
  .sec.looping .sn, .sec.looping .si { color: var(--ink-on-accent); }
</style>
