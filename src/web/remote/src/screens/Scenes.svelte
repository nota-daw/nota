<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Scenes: the Session grid, tracks across and scenes down, in the project's colours. Tap a clip
     to launch it, a scene name to launch the row, Stop to stop a track. A queued clip blinks
     until the launch point; a playing one shows a play mark and a progress line — readable at
     arm's length. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';

  let { form }: { form: Form } = $props();
  const s = $derived(app.ses);
  const tracks = $derived(new Map((app.proj?.tracks ?? []).map((t) => [t.id, t])));
  const cols = $derived(s?.tr ?? []);
  const colMin = $derived(form === 'phone' ? 96 : form === 'phoneLand' ? 104 : 132);
  const rowH = $derived(form === 'phone' ? 58 : form === 'phoneLand' ? 50 : 84);
  const bars = (beats: number) => { const b = beats / 4; return b === Math.round(b) ? `${b} bar${b === 1 ? '' : 's'}` : `${beats} beats`; };

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
      <div class="caps corner">Scene</div>
      {#each cols as c (c.id)}
        {@const t = tracks.get(c.id)}
        <div class="th"><span style:background={t?.color}></span><b>{t?.name ?? ''}</b></div>
      {/each}
      {#each Array.from({ length: s.n }, (_, i) => i) as si (si)}
        <button class="scene" style:height="{rowH}px" onclick={() => guard() && app.send({ t: 'scene', i: si })} aria-label="Launch scene {si + 1}">
          <span class="tri"></span>Scene {si + 1}
        </button>
        {#each cols as c (c.id)}
          {@const t = tracks.get(c.id)}
          {@const [st, len, prog] = c.c[si] ?? [0, 0, 0]}
          {#if st === 0}
            <div class="clip empty" style:height="{rowH}px"></div>
          {:else}
            <button class="clip" class:playing={st === 3} class:queued={st === 2} class:rec={st === 4} style:height="{rowH}px" style:--tc={t?.color}
              onclick={() => guard() && app.send({ t: 'launch', id: c.id, i: si })}
              aria-label="{t?.name} scene {si + 1}: {st === 3 ? 'playing' : st === 2 ? 'queued' : st === 4 ? 'recording' : 'stopped'}">
              <span class="cn">{#if st === 3}<span class="ptri"></span>{/if}{bars(len)}</span>
              <span class="mono cs">{st === 3 ? 'PLAYING' : st === 2 ? 'QUEUED' : st === 4 ? 'RECORDING' : ''}</span>
              {#if st === 3}<span class="prog" style:width="{prog * 100}%"></span>{/if}
            </button>
          {/if}
        {/each}
      {/each}
      <button class="stopall" onclick={() => guard() && app.send({ t: 'stopAll' })}><span class="sq"></span>All</button>
      {#each cols as c (c.id)}
        <button class="stop" onclick={() => guard() && app.send({ t: 'stopSlot', id: c.id })} aria-label="Stop {tracks.get(c.id)?.name}"><span class="sq"></span>Stop</button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .scroll { flex: 1; min-height: 0; overflow: auto; touch-action: pan-x pan-y; overscroll-behavior: contain; }
  .grid { display: grid; gap: 6px; min-width: min-content; }
  .corner { height: 36px; display: flex; align-items: center; padding-left: 4px; font-size: 9px; }
  .th { height: 36px; display: flex; align-items: center; gap: 6px; padding: 0 6px; min-width: 0; }
  .th span { width: 3px; height: 14px; border-radius: 2px; flex: none; }
  .th b { font-size: 12px; font-weight: 600; color: var(--ink2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .scene { display: flex; align-items: center; gap: 8px; padding: 0 10px; border-radius: 5px; background: var(--raised); border: 1px solid var(--border); font-size: 12px; font-weight: 600; color: var(--ink1); }
  .tri { width: 0; height: 0; border-left: 9px solid var(--ink3); border-top: 6px solid transparent; border-bottom: 6px solid transparent; }
  .clip { position: relative; display: flex; flex-direction: column; justify-content: center; align-items: flex-start; gap: 3px; padding: 0 10px; border-radius: 5px; background: var(--raised); border: 1px solid var(--border); overflow: hidden; min-width: 0; text-align: left; }
  .clip.empty { background: var(--well); border: 1px dashed var(--hairline); }
  .cn { display: flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 600; color: var(--ink2); white-space: nowrap; }
  .cs { font-size: 9px; opacity: .8; }
  .clip.playing, .clip.queued { background: var(--tc); border-color: var(--tc); }
  .clip.playing { border-color: var(--ink0); }
  .clip.playing *, .clip.queued * { color: #171613; }
  .clip.queued { animation: blink 800ms steps(1) infinite; }
  .clip.rec { border-color: var(--record); }
  .clip.rec .cs { color: var(--record-ink); }
  @keyframes blink { 50% { opacity: .35; } }
  .ptri { width: 0; height: 0; border-left: 8px solid #171613; border-top: 5px solid transparent; border-bottom: 5px solid transparent; }
  .prog { position: absolute; left: 0; bottom: 0; height: 4px; background: #F2EDE1; opacity: .75; }
  .stop, .stopall { height: 44px; border-radius: 5px; background: var(--raised); border: 1px solid var(--border); display: flex; align-items: center; justify-content: center; gap: 6px; font-size: 11px; font-weight: 600; color: var(--ink3); }
  .sq { width: 9px; height: 9px; border-radius: 1px; background: var(--ink3); }
  .empty { padding: 20px; font-size: 12px; color: var(--ink5); }
</style>
