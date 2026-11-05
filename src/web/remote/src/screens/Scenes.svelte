<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Scenes: the Session grid, tracks across and scenes down, in the project's colours. Tap a clip
     to launch it, a scene name to launch the row, Stop to stop a track. A queued clip blinks
     until the launch point; a playing one shows a play mark and a progress line — readable at
     arm's length. Every button takes its own finger (use:tap), so several clips launch at once. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { look } from '../lib/theme.svelte';
  import { tap } from '../lib/tap';

  let { form }: { form: Form } = $props();
  const s = $derived(app.ses);
  const tracks = $derived(new Map((app.proj?.tracks ?? []).map((t) => [t.id, t])));
  const cols = $derived(s?.tr ?? []);
  const colMin = $derived(form === 'phone' ? 96 : form === 'phoneLand' ? 104 : 132);
  const rowH = $derived(form === 'phone' ? 58 : form === 'phoneLand' ? 50 : 84);
  // A clip's length as [number, unit]: whole bars where it divides, else beats.
  const bars = (beats: number): [number, string] => { const b = beats / 4; return b === Math.round(b) ? [b, b === 1 ? 'bar' : 'bars'] : [beats, 'beats']; };

  function guard(): boolean {
    if (!app.canControl) { app.showToast('Nota lets this phone play notes only.'); return false; }
    return true;
  }
</script>

<div class="scroll">
  {#if !s}
    <div class="empty">Loading…</div>
  {:else}
    <div class="grid" style:grid-template-columns="{form === 'phone' ? 92 : 110}px repeat({cols.length}, minmax({colMin}px, 1fr))">
      <div class="label corner">Scene</div>
      {#each cols as c (c.id)}
        {@const t = tracks.get(c.id)}
        <div class="th"><span style:background={t?.color}></span><b>{t?.name ?? ''}</b></div>
      {/each}
      {#each Array.from({ length: s.n }, (_, i) => i) as si (si)}
        <button class="scene" style:height="{rowH}px" use:tap={() => guard() && app.send({ t: 'scene', i: si })} aria-label="Launch scene {si + 1}">
          <span class="tri"></span>Scene {si + 1}
        </button>
        {#each cols as c (c.id)}
          {@const t = tracks.get(c.id)}
          {@const [st, len, prog] = c.c[si] ?? [0, 0, 0]}
          {#if st === 0}
            <div class="clip empty" style:height="{rowH}px"></div>
          {:else}
            <button class="clip" class:playing={st === 3} class:queued={st === 2} class:rec={st === 4} style:height="{rowH}px" style:--tc={t?.color} style:--tc-ink={t ? look.inkOn(t.color) : undefined}
              use:tap={() => guard() && app.send({ t: 'launch', id: c.id, i: si })}
              aria-label="{t?.name} scene {si + 1}: {st === 3 ? 'playing' : st === 2 ? 'queued' : st === 4 ? 'recording' : 'stopped'}">
              <span class="cn">{#if st === 3}<span class="ptri"></span>{/if}<span><span class="mono">{bars(len)[0]}</span> {bars(len)[1]}</span></span>
              <span class="cs">{st === 3 ? 'Playing' : st === 2 ? 'Queued' : st === 4 ? 'Recording' : ''}</span>
              {#if st === 3}<span class="prog" style:width="{prog * 100}%"></span>{/if}
            </button>
          {/if}
        {/each}
      {/each}
      <button class="stopall" use:tap={() => guard() && app.send({ t: 'stopAll' })}><span class="sq"></span>All</button>
      {#each cols as c (c.id)}
        <button class="stop" use:tap={() => guard() && app.send({ t: 'stopSlot', id: c.id })} aria-label="Stop {tracks.get(c.id)?.name}"><span class="sq"></span>Stop</button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .scroll { flex: 1; min-height: 0; overflow: auto; touch-action: pan-x pan-y; overscroll-behavior: contain; }
  .grid { display: grid; gap: 4px; min-width: min-content; }
  .corner { height: 36px; display: flex; align-items: center; padding-left: 4px; }
  .th { height: 36px; display: flex; align-items: center; gap: 6px; padding: 0 6px; min-width: 0; }
  .th span { width: 3px; height: 14px; border-radius: 1px; flex: none; }
  .th b { font-size: 12px; font-weight: 600; color: var(--text-strong); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .scene { display: flex; align-items: center; gap: 8px; padding: 0 10px; border-radius: 2px; background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); font-size: 12px; font-weight: 600; color: var(--text-primary); }
  .tri { width: 0; height: 0; border-left: 9px solid var(--text-secondary); border-top: 6px solid transparent; border-bottom: 6px solid transparent; }
  /* Clip slots: raised when filled; playing and queued in the track's colour, recording red — ink derived. */
  .clip { position: relative; display: flex; flex-direction: column; justify-content: center; align-items: flex-start; gap: 3px; padding: 0 10px; border-radius: 2px; background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); overflow: hidden; min-width: 0; text-align: left; }
  .clip.empty { background: var(--bg-sunken); border-color: transparent; box-shadow: none; }
  .cn { display: flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 600; color: var(--text-strong); white-space: nowrap; }
  .cs { font-size: 11px; color: var(--text-tertiary); }
  .clip.playing, .clip.queued { background: var(--tc); border-color: var(--tc); box-shadow: none; }
  .clip.playing *, .clip.queued * { color: var(--tc-ink); }
  /* Queued waits for the launch point: it blinks until then (a time state, not a colour one). */
  .clip.queued { animation: blink 800ms steps(1) infinite; }
  .clip.rec { background: var(--record); border-color: var(--record); box-shadow: none; }
  .clip.rec * { color: var(--ink-on-record); }
  @keyframes blink { 50% { opacity: .35; } }
  .ptri { width: 0; height: 0; border-left: 8px solid var(--tc-ink); border-top: 5px solid transparent; border-bottom: 5px solid transparent; }
  .prog { position: absolute; left: 0; bottom: 0; height: 3px; background: var(--tc-ink); }
  .stop, .stopall { height: 44px; border-radius: 2px; background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); display: flex; align-items: center; justify-content: center; gap: 6px; font-size: 12px; font-weight: 500; color: var(--text-secondary); }
  .sq { width: 9px; height: 9px; border-radius: 1px; background: currentColor; }
  .empty { padding: 20px; font-size: 12px; color: var(--text-tertiary); }
</style>
