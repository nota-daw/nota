<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Scenes: the Session grid in the project's colours — scenes down and tracks across (as on the
     desktop), or turned, scenes across and tracks down (⋯ → Session grid; specs/102). Tap a clip
     to launch it, a scene button to launch the scene, Stop to stop a track. The scene buttons,
     the track names, every Stop and Stop All are pinned to the edges, so they stay on screen
     wherever the clips are scrolled (issue 662). Groups show as on the desktop: tap the name to fold
     or unfold its tracks (one choice with the Mixer); a group's slot counts its tracks' clips in
     the scene, a bar per track, and launches them all; its Stop stops them all. A filled cell
     reads like the desktop's Session slot (issue 557): the clip's name and length on top, what
     is in it below — its notes or its waveform, in the clip's colour. A queued clip blinks until
     the launch point; a playing one fills with its colour and shows a play mark and a progress
     line; a recording one is red and shows its take growing. Every button takes its own finger
     (use:tap), so several clips launch at once. Press and hold a queued clip, group slot or scene
     to cancel its launch before the launch point (issue 480); a tap never cancels. The slot or
     scene selected in Nota (by the mouse or a hardware controller) is outlined in the text
     colour, as on the desktop (specs/107). -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { look } from '../lib/theme.svelte';
  import { tap } from '../lib/tap';
  import { bars } from '../lib/fmt';
  import { lanes as laneList, groupSlot, effectiveAcross, type Lane } from '../lib/grid';
  import ClipPreview from '../components/ClipPreview.svelte';
  import { slotSelected, rowSelected } from '../lib/focus';

  let { form }: { form: Form } = $props();
  const s = $derived(app.ses);
  const tracks = $derived(new Map((app.proj?.tracks ?? []).map((t) => [t.id, t])));
  const open = $derived(new Set(app.prefs.groupsOpen));
  const lanes = $derived(s ? laneList(app.proj?.tracks ?? [], s, open) : []);
  const across = $derived(effectiveAcross(app.prefs.sessionGrid, s));   // specs/108: own choice or Nota's
  const scenes = $derived(Array.from({ length: s?.n ?? 0 }, (_, i) => i));
  const colMin = $derived(form === 'phone' ? 104 : form === 'phoneLand' ? 112 : 140);
  const rowH = $derived(form === 'phone' ? 62 : form === 'phoneLand' ? 54 : 84);
  // The pinned rail: scene buttons (scenes down) or track names with their Stop (scenes across).
  const railW = $derived(across ? (form === 'phone' ? 112 : 132) : form === 'phone' ? 92 : 110);
  const cols = $derived(across
    ? `${railW}px repeat(${scenes.length}, minmax(${colMin}px, 1fr))`
    : `${railW}px repeat(${lanes.length}, minmax(${colMin}px, 1fr))`);
  const rows = $derived(across
    ? `48px repeat(${lanes.length}, ${rowH}px)`
    : `40px repeat(${scenes.length}, ${rowH}px) 48px`);
  // Where lane j's slot in scene i sits: past the pinned header strip and rail.
  const at = (j: number, i: number) => (across ? `${j + 2} / ${i + 2}` : `${i + 2} / ${j + 2}`);
  const STATE = ['', 'Stopped', 'Queued', 'Playing', 'Recording', 'Recording'];

  function guard(): boolean {
    if (!app.canControl) { app.showToast('Nota lets this phone play notes only.'); return false; }
    return true;
  }
  function fold(l: Lane) {
    const set = new Set(app.prefs.groupsOpen);
    if (set.has(l.id)) set.delete(l.id); else set.add(l.id);
    app.prefs.groupsOpen = [...set];
  }
  // Issue 480: a queued clip, group slot or scene is cancelled by pressing and holding it. A tap keeps
  // launching (a queued one stays queued); the hold is the deliberate gesture, and only acts while
  // something waits for its launch point.
  // Nota says what waits (q, w); an older Nota that doesn't falls back to slot state 2.
  const queued = (id: number, i: number) => {
    const r = s?.tr.find((x) => x.id === id);
    return r?.w === i || r?.c[i]?.[0] === 2;
  };
  const sceneQueued = (i: number) => (s?.q ? s.q.includes(i) : !!s?.tr.some((r) => r.c[i]?.[0] === 2));
  const holdCancel = (id: number, i: number, members?: number[]) => () => {
    if (!(members ?? [id]).some((m) => queued(m, i)) || !guard()) return false;
    app.send({ t: 'cancel', id, i });
    return true;
  };
  const holdCancelScene = (i: number) => () => {
    if (!sceneQueued(i) || !guard()) return false;
    app.send({ t: 'cancelScene', i });
    return true;
  };
  const colourOf = (id: number) => tracks.get(id)?.color;
  const barInk = (st: number, id: number) => (st === 3 ? 'var(--success)' : st === 4 || st === 5 ? 'var(--record)' : st ? colourOf(id) : undefined);
</script>

{#snippet name(l: Lane)}
  {@const t = tracks.get(l.id)}
  {#if l.group}
    <button class="name grp" onclick={() => fold(l)} aria-expanded={l.open} aria-label="{t?.name}: {l.open ? 'fold' : 'unfold'} its {l.members.length} tracks">
      <span class="chev" class:open={l.open}></span><span class="bar" style:background={t?.color}></span><b>{t?.name ?? ''}</b><i>{l.members.length}</i>
    </button>
  {:else}
    <span class="name"><span class="bar" style:background={t?.color}></span><b>{t?.name ?? ''}</b></span>
  {/if}
{/snippet}

{#snippet stop(l: Lane)}
  <button class="stop" use:tap={() => guard() && app.send({ t: 'stopSlot', id: l.id })} aria-label="Stop {tracks.get(l.id)?.name}"><span class="sq"></span>Stop</button>
{/snippet}

{#snippet stopAll()}
  <button class="stop all" use:tap={() => guard() && app.send({ t: 'stopAll' })} aria-label="Stop all clips"><span class="sq"></span>All</button>
{/snippet}

{#snippet sceneButton(i: number)}
  <button class="scene" class:queued={sceneQueued(i)} class:sel={rowSelected(s, i)}
    use:tap={{ tap: () => guard() && app.send({ t: 'scene', i }), hold: holdCancelScene(i) }}
    aria-label="Launch scene {i + 1}{sceneQueued(i) ? ' — queued, hold to cancel' : ''}">
    <span class="tri"></span>Scene {i + 1}
  </button>
{/snippet}

<div class="scroll">
  {#if !s}
    <div class="loading">Loading…</div>
  {:else}
    <div class="grid" class:across style:grid-template-columns={cols} style:grid-template-rows={rows}>
      <!-- The corner where the two pinned strips meet. -->
      <div class="c pin pin-t pin-l corner" style:grid-area="1 / 1">
        {#if across}{@render stopAll()}{:else}<span class="label">Scene</span>{/if}
      </div>

      {#each scenes as i (i)}
        <div class="c pin {across ? 'pin-t' : 'pin-l'}" style:grid-area={across ? `1 / ${i + 2}` : `${i + 2} / 1`}>{@render sceneButton(i)}</div>
      {/each}

      {#each lanes as l, j (l.id)}
        {@const member = l.parent ? colourOf(l.parent) : undefined}
        <div class="c pin {across ? 'pin-l' : 'pin-t'} head" class:member={!!member} style:--gc={member}
          style:--depth={l.depth} style:grid-area={across ? `${j + 2} / 1` : `1 / ${j + 2}`}>
          {@render name(l)}
          {#if across}{@render stop(l)}{/if}
        </div>
        {#if !across}
          <div class="c pin pin-b" style:grid-area="{scenes.length + 2} / {j + 2}">{@render stop(l)}</div>
        {/if}

        {#each scenes as i (i)}
          {#if l.group}
            {@const g = groupSlot(l.members, s, i)}
            <div class="c" class:sel={slotSelected(s, l.id, i)} style:grid-area={at(j, i)}>
              {#if g.has === 0}
                <div class="clip empty"></div>
              {:else}
                <button class="clip group" class:playing={g.playing} style:--gc={colourOf(l.id)}
                  use:tap={{ tap: () => guard() && app.send({ t: 'launch', id: l.id, i }), hold: holdCancel(l.id, i, l.members) }}
                  aria-label="{tracks.get(l.id)?.name} · scene {i + 1}: {g.has} of {g.of} tracks{g.playing ? ', playing' : ''}">
                  <span class="top"><span class="tri" class:on={g.playing}></span><span class="len">{g.has} of {g.of}</span></span>
                  <span class="mbars">
                    {#each g.bars as st, k (k)}<span style:background={barInk(st, l.members[k])}></span>{/each}
                  </span>
                </button>
              {/if}
            </div>
          {:else}
            {@const t = tracks.get(l.id)}
            {@const [st, len, prog] = l.slots[i] ?? [0, 0, 0]}
            {@const pv = app.clips[`${l.id}:${i}`]}
            {@const col = pv?.col ?? t?.color}
            {@const rec = st === 4 || st === 5}
            {@const [bn, bu] = bars(pv?.len ?? len)}
            <div class="c" class:sel={slotSelected(s, l.id, i)} style:grid-area={at(j, i)}>
              {#if st === 0}
                <div class="clip empty"></div>
              {:else}
                <button class="clip" class:playing={st === 3} class:queued={st === 2} class:rec
                  style:--tc={col} style:--tc-ink={col ? look.inkOn(col) : undefined}
                  use:tap={{ tap: () => guard() && app.send({ t: 'launch', id: l.id, i }), hold: holdCancel(l.id, i) }}
                  aria-label="{pv?.nm ?? t?.name} · scene {i + 1}: {STATE[st].toLowerCase()}{st === 2 ? ', hold to cancel' : ''}">
                  <span class="top">
                    {#if st === 3}<span class="ptri"></span>{:else if rec}<span class="rdot"></span>{/if}
                    <span class="cn">{pv?.nm ?? STATE[st]}</span>
                    <span class="len">{rec ? 'Rec' : `${bn} ${bu}`}</span>
                  </span>
                  <span class="pv"><ClipPreview preview={pv} ink={rec ? 'var(--ink-on-record)' : st === 2 || st === 3 ? 'var(--tc-ink)' : 'var(--tc)'} /></span>
                  {#if st === 3}<span class="prog" style:width="{prog * 100}%"></span>{/if}
                </button>
              {/if}
            </div>
          {/if}
        {/each}
      {/each}

      {#if !across}
        <div class="c pin pin-b pin-l corner" style:grid-area="{scenes.length + 2} / 1">{@render stopAll()}</div>
      {/if}
    </div>
  {/if}
</div>

<style>
  .scroll { flex: 1; min-height: 0; overflow: auto; touch-action: pan-x pan-y; overscroll-behavior: contain; }
  .grid { display: grid; min-width: min-content; }
  /* Every cell carries its half of the 4 px gap as padding, so a pinned cell's ground covers
     the gap too and the clips slide under it unseen. */
  .c { padding: 2px; display: flex; flex-direction: column; min-width: 0; min-height: 0; }
  .c > :global(*) { flex: 1; min-height: 0; }
  /* Nota's selection: an outline in the text colour around the slot (the desktop's SelPen). */
  .c.sel > :global(*) { outline: 1.5px solid var(--text-primary); outline-offset: -1.5px; }
  .scene.sel { outline: 1.5px solid var(--text-primary); outline-offset: -1.5px; }
  .pin { position: sticky; z-index: 2; background: var(--bg-app); }
  .pin-t { top: 0; }
  .pin-l { left: 0; }
  .pin-b { bottom: 0; }
  .pin.corner { z-index: 3; }
  .label { display: flex; align-items: center; padding-left: 4px; }

  /* Track names: a colour bar and the name; a group's name folds and unfolds its tracks. A
     track inside a group carries the group's colour — a line over its name, or indented under
     it when tracks run down. */
  .head { gap: 4px; }
  .name { flex: 1; display: flex; align-items: center; gap: 6px; padding: 0 6px; min-width: 0; text-align: left; border-radius: min(var(--r-cell), 14px); }
  .across .name { flex: none; height: 20px; padding-left: calc(4px + var(--depth, 0) * 8px); }
  .head.member.pin-t .name { box-shadow: inset 0 2px 0 var(--gc); border-radius: 0; }
  .head.member.pin-l .name { box-shadow: inset 2px 0 0 var(--gc); border-radius: 0; }
  .name .bar { width: 3px; height: 14px; border-radius: min(var(--r-fine), 1px); flex: none; }
  .name b { font-size: 12px; font-weight: 600; color: var(--text-strong); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .name i { font-style: normal; font-size: 10px; color: var(--text-tertiary); flex: none; }
  .name.grp:active { background: var(--bg-sunken); }
  .chev { flex: none; width: 0; height: 0; border-left: 5px solid var(--text-secondary); border-top: 4px solid transparent; border-bottom: 4px solid transparent; transition: transform var(--ease); }
  .chev.open { transform: rotate(90deg); }

  .scene { display: flex; align-items: center; gap: 8px; padding: 0 10px; border-radius: min(var(--r-cell), 14px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); font-size: 12px; font-weight: 600; color: var(--text-primary); white-space: nowrap; }
  .tri { flex: none; width: 0; height: 0; border-left: 9px solid var(--text-secondary); border-top: 6px solid transparent; border-bottom: 6px solid transparent; }
  .tri.on { border-left-color: var(--success); }
  /* A queued scene blinks Caution until its launch point, as on the desktop (issue 480: hold to cancel). */
  .scene.queued .tri { border-left-color: var(--warning); animation: blink 800ms steps(1) infinite; }
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
  /* A group's slot, as the desktop's: washed with the group's colour (stronger, with a green
     edge, while one of its tracks plays), "n of m" and a bar per track — its colour where it
     has a clip, green where it plays, red where it records, a rule where it is empty. */
  .clip.group { background: color-mix(in srgb, var(--gc) 9%, var(--surface-raised)); }
  .clip.group.playing { background: color-mix(in srgb, var(--gc) 18%, var(--surface-raised)); border-color: color-mix(in srgb, var(--success) 50%, transparent); }
  .clip.group .len { color: var(--text-secondary); }
  .mbars { flex: 1; min-height: 0; display: flex; flex-direction: column; justify-content: flex-end; gap: 3px; overflow: hidden; }
  .mbars span { flex: none; height: 3px; border-radius: min(var(--r-fine), 1px); background: var(--rule); }

  .stop { display: flex; align-items: center; justify-content: center; gap: 6px; border-radius: min(var(--r-cell), 14px); background: var(--surface-raised); border: 1px solid var(--seam); box-shadow: var(--bevel); font-size: 12px; font-weight: 500; color: var(--text-secondary); }
  .across .head .stop { flex: 1; min-height: 0; }
  .sq { width: 9px; height: 9px; border-radius: calc(var(--r-fine) / 2); background: currentColor; }
  .loading { padding: 20px; font-size: 12px; color: var(--text-tertiary); }
</style>
