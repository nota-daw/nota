<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Sheets over the screen: the track list, the ⋯ menu, the key picker and the device list. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';
  import { NN } from '../lib/music';
  import { look } from '../lib/theme.svelte';

  let { form }: { form: Form } = $props();
  const portrait = $derived(form === 'phone');
  let nameInput = $state('');
  $effect(() => { if (app.sheet === 'menu') nameInput = app.name; });

  function close() {
    if (app.sheet === 'menu') app.rename(nameInput);
    app.sheet = null;
  }
  const projectKey = $derived(app.proj?.key ?? null);
  // What the Theme choice wears now, and where it comes from (spec 014).
  const wearing = $derived.by(() => {
    const t = look.worn(app.prefs.theme);
    if (!t) return app.live ? 'Nota’s colours' : 'Until Nota answers: Studio Dark';
    if (app.prefs.theme === 'nota') return `${t.name} · as Nota wears it`;
    return `${t.name} · Nota’s ${look.phoneDark ? 'dark' : 'light'}-mode theme`;
  });
</script>

{#if app.sheet}
  <button class="scrim" aria-label="Close" onclick={close}></button>
{/if}

{#if app.sheet === 'tracks'}
  <div class="sheet" class:bottom={portrait} role="dialog" aria-label="Play on track">
    <div class="label head">Play on track</div>
    <div class="list">
      {#each app.playable as t (t.id)}
        {@const others = app.who[String(t.id)] ?? []}
        <button class="row" class:on={t.id === app.proj?.you} onclick={() => app.selectTrack(t.id)}>
          <span class="bar" style:background={t.color}></span>
          <span class="names"><span class="n">{t.name}</span><span class="d">{t.dev}</span></span>
          {#if t.id === app.proj?.you}<span class="who">You</span>{/if}
          {#each others as o}<span class="who">{o}</span>{/each}
        </button>
      {/each}
      {#if app.playable.length === 0}<div class="empty">The project has no tracks yet.</div>{/if}
    </div>
    <button class="toggle" onclick={() => app.setFollow(!app.proj?.follow)}>
      <span class="switch" class:on={app.proj?.follow}></span>
      <span><span class="n2">Follow Nota’s selection</span><span class="d2">Off: this phone keeps its own track</span></span>
    </button>
  </div>
{:else if app.sheet === 'menu'}
  <div class="sheet menu" role="dialog" aria-label="Menu">
    <div class="block">
      <label class="label" for="devname">Device name</label>
      <input id="devname" class="field" bind:value={nameInput} maxlength="40" enterkeyhint="done"
        onkeydown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
        onblur={() => app.rename(nameInput)} />
    </div>
    <div class="block">
      <span class="label">Theme</span>
      <div class="seg">
        <button class:on={app.prefs.theme === 'nota'} onclick={() => (app.prefs.theme = 'nota')}>Follow Nota</button>
        <button class:on={app.prefs.theme === 'phone'} onclick={() => (app.prefs.theme = 'phone')}>Follow phone</button>
      </div>
      <span class="d2">{wearing}</span>
    </div>
    <button class="toggle" onclick={() => (app.prefs.haptics = !app.prefs.haptics)}>
      <span class="switch" class:on={app.prefs.haptics}></span><span class="n2">Haptics on pads</span>
    </button>
    <button class="toggle" onclick={() => (app.prefs.countIn = !app.prefs.countIn)}>
      <span class="switch" class:on={app.prefs.countIn}></span><span class="n2">Count-in before recording</span>
    </button>
    <button class="disconnect" onclick={() => app.leave()}>Disconnect</button>
  </div>
{:else if app.sheet === 'key'}
  <div class="sheet" class:bottom={portrait} role="dialog" aria-label="Key">
    <div class="label head">Key on this phone</div>
    <div class="block">
      <button class="row slim" class:on={!app.prefs.key} onclick={() => { app.prefs.key = null; app.sheet = null; }}>
        <span class="names"><span class="n">Project key</span><span class="d">{projectKey ? projectKey.name : 'Not set in Nota — C major'}</span></span>
      </button>
      <div class="seg">
        <button class:on={!(app.prefs.key?.minor ?? projectKey?.minor ?? false)} onclick={() => (app.prefs.key = { root: app.prefs.key?.root ?? projectKey?.root ?? 0, minor: false })}>Major</button>
        <button class:on={app.prefs.key?.minor ?? projectKey?.minor ?? false} onclick={() => (app.prefs.key = { root: app.prefs.key?.root ?? projectKey?.root ?? 0, minor: true })}>Minor</button>
      </div>
      <div class="tonics">
        {#each NN as n, i}
          <button class="btn" class:on={app.prefs.key?.root === i} onclick={() => (app.prefs.key = { root: i, minor: app.prefs.key?.minor ?? projectKey?.minor ?? false })}>{n}</button>
        {/each}
      </div>
    </div>
  </div>
{:else if app.sheet === 'devices'}
  <div class="sheet" class:bottom={portrait} role="dialog" aria-label="Device">
    <div class="label head">Device on {app.track?.name ?? 'this track'}</div>
    <div class="list">
      {#each app.mac?.devs ?? [] as d, i}
        <button class="row" class:on={i === (app.mac?.di ?? 0)} onclick={() => { app.macroDevice = i; app.sub(); app.sheet = null; }}>
          <span class="names"><span class="n">{d}</span></span>
        </button>
      {/each}
    </div>
  </div>
{/if}

<style>
  .scrim { position: absolute; inset: 0; z-index: 20; background: var(--scrim); }
  /* A floating sheet drawn in the page: a menu-sized card with the popover ring and drop. */
  .sheet { position: absolute; z-index: 21; left: 12px; top: calc(env(safe-area-inset-top) + 64px); width: 360px; max-width: calc(100% - 24px); max-height: calc(100% - 88px); background: var(--panel); border-radius: min(var(--r-group), 12px); box-shadow: var(--popover); display: flex; flex-direction: column; overflow: hidden; }
  .sheet.bottom { top: auto; right: 12px; width: auto; bottom: max(24px, env(safe-area-inset-bottom)); max-height: 75%; }
  .sheet.menu { left: auto; right: 12px; width: 300px; }
  .head { padding: 14px 16px 8px; }
  .list { overflow-y: auto; touch-action: pan-y; overscroll-behavior: contain; }
  /* Rows on rules, full width; the chosen one is picked: the soft tint, orange name and a 2 × 14 orange tick. */
  .row { width: 100%; height: 52px; display: flex; align-items: center; gap: 12px; padding: 0 16px; border-top: 1px solid var(--rule); position: relative; text-align: left; transition: background-color var(--ease); }
  .row.slim { height: 44px; border: none; padding: 0 10px; border-radius: min(var(--r-field), 14px); }
  .row:active { background: var(--bg-sunken); }
  .row.on { background: var(--accent-soft); }
  .row.on .n { color: var(--picked-ink); }
  .row.on .d { color: var(--text-secondary); }
  .row.on::before { content: ''; position: absolute; left: 0; top: 50%; height: 14px; margin-top: -7px; width: 2px; border-radius: 0 var(--r-fine) var(--r-fine) 0; background: var(--accent); }
  .bar { width: 3px; height: 22px; border-radius: min(var(--r-fine), 1px); flex: none; }
  .names { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
  .n { font-size: 13px; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .d { font-size: 11px; color: var(--text-tertiary); }
  .who { height: 20px; display: flex; align-items: center; padding: 0 7px; border-radius: min(var(--r-cell), 6px); background: var(--bg-sunken); font-size: 10px; font-weight: 500; color: var(--text-secondary); }
  .empty { padding: 16px; font-size: 12px; color: var(--text-tertiary); }
  .toggle { width: 100%; min-height: 52px; display: flex; align-items: center; gap: 12px; padding: 0 16px; border-top: 1px solid var(--rule); text-align: left; }
  .n2 { display: block; font-size: 12px; color: var(--text-primary); }
  .d2 { display: block; font-size: 11px; color: var(--text-tertiary); }
  .block { padding: 14px 16px; display: flex; flex-direction: column; gap: 8px; }
  .block + .block { border-top: 1px solid var(--rule); }
  /* A field: the well; keyboard focus gets a 1 px orange outline. */
  .field { height: 40px; padding: 0 12px; background: var(--bg-sunken); border: none; box-shadow: var(--sunken); border-radius: min(var(--r-field), 13px); font-size: 13px; outline: none; }
  .field:focus { box-shadow: inset 0 0 0 1px var(--accent); }
  .tonics { display: grid; grid-template-columns: repeat(6, 1fr); gap: 6px; }
  .tonics .btn { padding: 0; height: 44px; border-radius: min(var(--r-cell), 14px); }
  .disconnect { height: 52px; padding: 0 16px; border-top: 1px solid var(--rule); font-size: 13px; font-weight: 500; color: var(--text-primary); text-align: left; }
  .disconnect:active { background: var(--bg-sunken); }
</style>
