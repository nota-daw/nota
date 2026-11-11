<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Scenes: the Session grid, tracks across and scenes down, in the project's colours. Tap a clip
     to launch it, a scene name to launch the row, Stop to stop a track. A filled cell reads like
     the desktop's Session slot (issue 557): the clip's name and length on top, what is in it below —
     its notes or its waveform, in the clip's colour. A queued clip blinks until the launch
     point; a playing one fills with its colour and shows a play mark and a progress line; a
     recording one is red and shows its take growing. Every button takes its own finger
     (use:tap), so several clips launch at once. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { look } from '../lib/theme.svelte';
  import { tap } from '../lib/tap';
  import { bars } from '../lib/fmt';
  import ClipPreview from '../components/ClipPreview.svelte';

  let { form }: { form: Form } = $props();
  const s = $derived(app.ses);
  const tracks = $derived(new Map((app.proj?.tracks ?? []).map((t) => [t.id, t])));
  const cols = $derived(s?.tr ?? []);
  const colMin = $derived(form === 'phone' ? 104 : form === 'phoneLand' ? 112 : 140);
  const rowH = $derived(form === 'phone' ? 62 : form === 'phoneLand' ? 54 : 84);
  const STATE = ['', 'Stopped', 'Queued', 'Playing', 'Recording', 'Recording'];

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
          {@const pv = app.clips[`${c.id}:${si}`]}
          {@const col = pv?.col ?? t?.color}
          {@const rec = st === 4 || st === 5}
          {@const [bn, bu] = bars(pv?.len ?? len)}
          {#if st === 0}
            <div class="clip empty" style:height="{rowH}px"></div>
          {:else}
            <button class="clip" class:playing={st === 3} class:queued={st === 2} class:rec style:height="{rowH}px"
              style:--tc={col} style:--tc-ink={col ? look.inkOn(col) : undefined}
              use:tap={() => guard() && app.send({ t: 'launch', id: c.id, i: si })}
              aria-label="{pv?.nm ?? t?.name} · scene {si + 1}: {STATE[st].toLowerCase()}">
              <span class="top">
                {#if st === 3}<span class="ptri"></span>{:else if rec}<span class="rdot"></span>{/if}
                <span class="cn">{pv?.nm ?? STATE[st]}</span>
                <span class="len">{rec ? 'Rec' : `${bn} ${bu}`}</span>
              </span>
              <span class="pv"><ClipPreview preview={pv} ink={rec ? 'var(--ink-on-record)' : st === 2 || st === 3 ? 'var(--tc-ink)' : 'var(--tc)'} /></span>
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
  .th span { width: 3px; height: 14px; border-radius: min(var(--r-fine), 1px); flex: none; }
  .th b { font-size: 12px; font-weight: 600; color: var(--text-strong); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .scene { display: flex; align-items: center; gap: 8px; padding: 0 10px; border-radius: min(var(--r-cell), 14px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); font-size: 12px; font-weight: 600; color: var(--text-primary); }
  .tri { width: 0; height: 0; border-left: 9px solid var(--text-secondary); border-top: 6px solid transparent; border-bottom: 6px solid transparent; }
  /* A clip slot, as the desktop's: raised when filled — its name, the preview in its colour
     below; playing and queued fill with the clip's colour, recording with red — ink derived. */
  .clip { position: relative; display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding: 6px 8px 7px; border-radius: min(var(--r-cell), 14px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); overflow: hidden; min-width: 0; text-align: left; }
  .clip.empty { background: var(--bg-sunken); border-color: transparent; box-shadow: none; }
  .top { display: flex; align-items: center; gap: 6px; min-width: 0; height: 16px; flex: none; }
  /* The name in ink 1 (read at arm's length in every theme); the clip's colour is in its preview and fill. */
  .cn { flex: 1; min-width: 0; font-size: 12px; font-weight: 600; color: var(--text-strong); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .len { flex: none; font-size: 10px; color: var(--text-tertiary); }
  .pv { flex: 1; min-height: 0; display: block; }
  .clip.playing, .clip.queued { background: var(--tc); border-color: var(--tc); box-shadow: none; }
  .clip.playing .cn, .clip.queued .cn, .clip.playing .len, .clip.queued .len { color: var(--tc-ink); }
  /* Queued waits for the launch point: it blinks until then (a time state, not a colour one). */
  .clip.queued { animation: blink 800ms steps(1) infinite; }
  .clip.rec { background: var(--record); border-color: var(--record); box-shadow: none; }
  .clip.rec .cn, .clip.rec .len { color: var(--ink-on-record); }
  @keyframes blink { 50% { opacity: .35; } }
  .ptri { flex: none; width: 0; height: 0; border-left: 8px solid var(--tc-ink); border-top: 5px solid transparent; border-bottom: 5px solid transparent; }
  .rdot { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--ink-on-record); }
  .prog { position: absolute; left: 0; bottom: 0; height: 3px; background: var(--tc-ink); }
  .stop, .stopall { height: 44px; border-radius: min(var(--r-cell), 14px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); display: flex; align-items: center; justify-content: center; gap: 6px; font-size: 12px; font-weight: 500; color: var(--text-secondary); }
  .sq { width: 9px; height: 9px; border-radius: calc(var(--r-fine) / 2); background: currentColor; }
  .empty { padding: 20px; font-size: 12px; color: var(--text-tertiary); }
</style>
