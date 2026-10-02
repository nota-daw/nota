<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Keys: a keyboard (one octave upright, 1.5 sideways, two on a tablet), a scale grid where
     every cell is a note of the key (rows a fourth apart, tonic cells raised — no wrong notes),
     and Chords (one triad per degree). The key is the project's KEY unless overridden here.
     Fingers can slide across keys and cells: every pointer is tracked over the whole surface. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { NN, noteName, keyName, degreeNote, chords as chordsOf, type Key } from '../lib/music';

  let { form }: { form: Form } = $props();
  const MODES = ['Keyboard', 'Scale', 'Chords'] as const;
  const WP = [0, 2, 4, 5, 7, 9, 11];

  const key = $derived<Key>(app.prefs.key ?? (app.proj?.key ? { root: app.proj.key.root, minor: app.proj.key.minor } : { root: 0, minor: false }));
  const keySrc = $derived(app.prefs.key ? 'PHONE' : app.proj?.key ? 'PROJECT' : 'DEFAULT');
  const base = $derived(12 * (app.prefs.octave + 1));
  const lit = $derived(new Set(app.lit));
  const color = $derived(app.track?.color ?? 'var(--accent)');
  const mode = $derived(app.prefs.keysMode);

  // ---- keyboard ----
  const whiteCount = $derived(form === 'phone' ? 8 : form === 'phoneLand' ? 11 : 15);
  const whites = $derived(Array.from({ length: whiteCount }, (_, k) => base + WP[k % 7] + 12 * Math.floor(k / 7)));
  const blacks = $derived.by(() => {
    const out: { note: number; left: string; w: string }[] = [];
    for (let k = 0; k < whiteCount - 1; k++)
      if ([0, 1, 3, 4, 5].includes(k % 7))
        out.push({
          note: base + WP[k % 7] + 12 * Math.floor(k / 7) + 1,
          left: `calc(${((k + 1) / whiteCount) * 100}% - ${(0.62 / whiteCount) * 50}%)`,
          w: `${(0.62 / whiteCount) * 100}%`,
        });
    return out;
  });

  // ---- scale grid ----
  const gridCols = $derived(form === 'phone' ? 5 : 8);
  const gridRows = $derived(form === 'phoneLand' ? 4 : 6);
  const cells = $derived.by(() => {
    const out: { note: number; tonic: boolean }[] = [];
    for (let r = gridRows - 1; r >= 0; r--)
      for (let c = 0; c < gridCols; c++) {
        const deg = r * 3 + c;
        out.push({ note: degreeNote(key, base, deg), tonic: deg % 7 === 0 });
      }
    return out;
  });

  // ---- chords ----
  const chordList = $derived(chordsOf(key, base));

  // ---- pointers: each finger plays what is under it, sliding included ----
  const fingers = new Map<number, string>();   // pointer → target id ("n60" / "c3")
  let held = $state<Record<string, boolean>>({});

  function targetAt(x: number, y: number): string | null {
    const el = document.elementFromPoint(x, y)?.closest('[data-t]') as HTMLElement | null;
    return el?.dataset.t ?? null;
  }
  function notesOf(t: string): number[] {
    if (t[0] === 'n') return [Number(t.slice(1))];
    return chordList[Number(t.slice(1))]?.notes ?? [];
  }
  // The notes each held target sounded, so its release ends exactly those (the key may change mid-hold).
  const sounding = new Map<string, number[]>();
  function start(t: string, vel = 0.85) {
    if (sounding.has(t)) return;   // a second finger on the same key
    held[t] = true;
    const notes = notesOf(t);
    sounding.set(t, notes);
    for (const n of notes) app.noteOn(n, vel);
  }
  function stop(t: string) {
    if ([...fingers.values()].includes(t)) return;
    held[t] = false;
    for (const n of sounding.get(t) ?? []) app.noteOff(n);
    sounding.delete(t);
  }
  const active = new Set<number>();
  function down(e: PointerEvent) {
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    active.add(e.pointerId);
    const t = targetAt(e.clientX, e.clientY);
    if (!t) return;
    fingers.set(e.pointerId, t);
    start(t);
  }
  function move(e: PointerEvent) {
    if (!active.has(e.pointerId)) return;
    const prev = fingers.get(e.pointerId);
    const t = targetAt(e.clientX, e.clientY);
    if (t === prev) return;
    if (prev !== undefined) { fingers.delete(e.pointerId); stop(prev); }
    if (t) { fingers.set(e.pointerId, t); start(t); }
  }
  function up(e: PointerEvent) {
    active.delete(e.pointerId);
    const prev = fingers.get(e.pointerId);
    fingers.delete(e.pointerId);
    if (prev !== undefined) stop(prev);
  }

  // A key changed under held notes (octave, mode): release them.
  $effect(() => {
    void base; void mode; void key;
    return () => {
      for (const notes of sounding.values()) for (const n of notes) app.noteOff(n);
      sounding.clear();
      fingers.clear();
      active.clear();
      held = {};
    };
  });
</script>

<div class="keys">
  <div class="bar">
    <div class="seg modes" role="group" aria-label="Mode">
      {#each MODES as m}
        <button class:on={mode === m} onclick={() => (app.prefs.keysMode = m)}>{m}</button>
      {/each}
    </div>
    <button class="btn keybtn" onclick={() => (app.sheet = 'key')} aria-label="Key: {keyName(key)}">
      <span class="caps k">Key</span>
      <span class="kn">{keyName(key)}</span>
      <span class="mono src">{keySrc}</span>
    </button>
    <div class="oct">
      <button class="btn sq" aria-label="Octave down" onclick={() => (app.prefs.octave = Math.max(0, app.prefs.octave - 1))}>
        <svg width="12" height="12" viewBox="0 0 12 12"><path d="M2 6 H10" /></svg>
      </button>
      <span class="mono">Oct {app.prefs.octave}</span>
      <button class="btn sq" aria-label="Octave up" onclick={() => (app.prefs.octave = Math.min(7, app.prefs.octave + 1))}>
        <svg width="12" height="12" viewBox="0 0 12 12"><path d="M2 6 H10 M6 2 V10" /></svg>
      </button>
    </div>
  </div>

  <div class="surface" style:--tc={color}
    onpointerdown={down} onpointermove={move} onpointerup={up} onpointercancel={up} role="application" aria-label="Keys">
    {#if mode === 'Keyboard'}
      <div class="kb">
        {#each whites as n (n)}
          <div class="white" class:on={held['n' + n]} data-t={'n' + n}>
            {#if n % 12 === 0}<span class="mono lbl">{noteName(n)}</span>{/if}
            {#if lit.has(n)}<span class="band"></span>{/if}
          </div>
        {/each}
        {#each blacks as b (b.note)}
          <div class="black" class:on={held['n' + b.note]} data-t={'n' + b.note} style:left={b.left} style:width={b.w}>
            {#if lit.has(b.note)}<span class="band"></span>{/if}
          </div>
        {/each}
      </div>
    {:else if mode === 'Scale'}
      <div class="grid" style:grid-template-columns="repeat({gridCols}, minmax(0, 1fr))">
        {#each cells as c, i (i)}
          <div class="cell" class:tonic={c.tonic} class:on={held['n' + c.note]} class:lit={lit.has(c.note)} data-t={'n' + c.note}>
            <span class="mono">{NN[c.note % 12]}{c.tonic ? Math.floor(c.note / 12) - 1 : ''}</span>
          </div>
        {/each}
      </div>
    {:else}
      <div class="grid chords" style:grid-template-columns="repeat({form === 'phone' ? 2 : 7}, minmax(0, 1fr))">
        {#each chordList as ch, i (i)}
          <div class="chord" class:on={held['c' + i]} class:lit={ch.notes.every((n) => lit.has(n))} data-t={'c' + i}>
            <span class="mono roman">{ch.roman}</span>
            <span class="cn">{ch.name}</span>
          </div>
        {/each}
      </div>
    {/if}
  </div>
  {#if form === 'phone' && mode === 'Keyboard'}
    <span class="hint">Turn the phone sideways for 1.5 octaves.</span>
  {/if}
</div>

<style>
  .keys { flex: 1; min-height: 0; display: flex; flex-direction: column; gap: 10px; }
  .bar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .modes button { flex: none; }
  .keybtn { gap: 7px; padding: 0 10px; }
  .k { font-size: 9px; letter-spacing: .1em; }
  .kn { font-size: 12px; font-weight: 600; color: var(--ink1); }
  .src { font-size: 9px; color: var(--accent-dim); }
  .oct { display: flex; align-items: center; gap: 2px; margin-left: auto; }
  .oct .mono { width: 46px; text-align: center; font-size: 12px; color: var(--ink1); }
  .sq { width: 42px; padding: 0; }
  .sq path { stroke: var(--ink2); stroke-width: 1.6; stroke-linecap: round; fill: none; }
  .surface { flex: 1; min-height: 0; display: flex; touch-action: none; }
  .kb { flex: 1; min-width: 0; position: relative; display: flex; gap: 3px; }
  .white { flex: 1; min-width: 0; border-radius: 0 0 6px 6px; background: var(--white-key); position: relative; overflow: hidden; display: flex; flex-direction: column; justify-content: flex-end; align-items: center; padding-bottom: 12px; }
  .white.on { background: var(--accent); }
  .lbl { font-size: 10px; color: var(--white-key-ink); }
  .white.on .lbl { color: var(--on-accent); }
  .black { position: absolute; top: 0; height: 58%; border-radius: 0 0 5px 5px; background: var(--raised); border: 1px solid var(--border-strong); border-top: none; overflow: hidden; z-index: 1; }
  .black.on { background: var(--accent); border-color: var(--accent); }
  .band { position: absolute; left: 0; right: 0; bottom: 0; height: 5px; background: var(--tc); }
  .grid { flex: 1; min-width: 0; display: grid; grid-auto-rows: minmax(0, 1fr); gap: 6px; }
  .cell { border-radius: 5px; background: var(--raised); border: 1px solid var(--border); display: flex; align-items: center; justify-content: center; min-height: 0; }
  .cell .mono { font-size: 12px; color: var(--ink3); pointer-events: none; }
  .cell.tonic { background: var(--track-off); border-color: var(--border-strong); }
  .cell.tonic .mono { color: var(--ink0); font-weight: 600; }
  .cell.lit { border-color: var(--tc); }
  .cell.on { background: var(--accent); border-color: var(--accent); }
  .cell.on .mono { color: var(--on-accent); }
  .chords { gap: 8px; }
  .chord { border-radius: 6px; background: var(--raised); border: 1px solid var(--border); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; min-height: 0; }
  .chord * { pointer-events: none; }
  .roman { font-size: 12px; color: var(--accent-dim); }
  .cn { font-size: 20px; font-weight: 600; color: var(--ink1); }
  .chord.lit { border-color: var(--tc); }
  .chord.on { background: var(--accent); border-color: var(--accent); }
  .chord.on .cn, .chord.on .roman { color: var(--on-accent); }
  .hint { font-size: 11px; color: var(--ink5); }
  .white *, .black * { pointer-events: none; }
</style>
