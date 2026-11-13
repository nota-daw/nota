<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<script lang="ts">
  import { app } from './lib/app.svelte';
  import { look } from './lib/theme.svelte';
  import { device, keepAwake } from './lib/device.svelte';
  import { SCREENS } from './lib/types';
  import Header from './components/Header.svelte';
  import Banner from './components/Banner.svelte';
  import Sheets from './components/Sheets.svelte';
  import Overlays from './components/Overlays.svelte';
  import BigTransport from './screens/BigTransport.svelte';
  import Pads from './screens/Pads.svelte';
  import Keys from './screens/Keys.svelte';
  import XY from './screens/XY.svelte';
  import Mixer from './screens/Mixer.svelte';
  import Macros from './screens/Macros.svelte';
  import Scenes from './screens/Scenes.svelte';
  import Empty from './screens/Empty.svelte';

  const form = $derived(device.form);
  const rail = $derived(form !== 'phone');
  const t = $derived(app.track);
  const noInstrument = $derived(t?.kind === 'audio' && (app.screen === 'Pads' || app.screen === 'Keys'));
  const dim = $derived(app.link === 'lost' || app.link === 'connecting');

  // Wear the theme: Nota's, or under Follow phone the pick for the phone's scheme (spec 014).
  $effect(() => look.apply(app.prefs.theme));
</script>

<!-- The first touch anywhere starts keep-awake (browsers want a gesture for it). -->
<div class="shell {form}" onpointerdowncapture={keepAwake}>
  <Header {form} />
  <Banner />
  <div class="body">
    {#if rail}
      <nav class="rail" aria-label="Screens">
        {#each SCREENS as s}
          <button class:on={app.screen === s && !app.bigTransport} onclick={() => app.setScreen(s)}>{s}</button>
        {/each}
      </nav>
    {/if}
    <main class:dim aria-busy={dim}>
      {#if noInstrument}
        <Empty />
      {:else if app.screen === 'Pads'}
        <Pads {form} />
      {:else if app.screen === 'Keys'}
        <Keys {form} />
      {:else if app.screen === 'XY'}
        <XY {form} />
      {:else if app.screen === 'Mixer'}
        <Mixer {form} />
      {:else if app.screen === 'Macros'}
        <Macros {form} />
      {:else}
        <Scenes {form} />
      {/if}
      {#if app.bigTransport}
        <BigTransport {form} />
      {/if}
    </main>
  </div>
  {#if !rail}
    <nav class="tabs" aria-label="Screens">
      {#each SCREENS as s}
        <button class:on={app.screen === s && !app.bigTransport} aria-current={app.screen === s && !app.bigTransport ? 'page' : undefined} onclick={() => app.setScreen(s)}>{s}</button>
      {/each}
    </nav>
  {/if}
  <Sheets {form} />
  <Overlays {form} />
</div>

<style>
  .shell {
    position: absolute; inset: 0; display: flex; flex-direction: column; background: var(--bg-app);
    padding: env(safe-area-inset-top) env(safe-area-inset-right) 0 env(safe-area-inset-left);
  }
  .body { flex: 1; min-height: 0; display: flex; }
  main { flex: 1; min-width: 0; position: relative; display: flex; flex-direction: column; padding: 12px; transition: opacity 200ms ease-out; }
  .phoneLand main { padding: 8px 10px max(10px, env(safe-area-inset-bottom)) 10px; }
  .tablet main { padding: 18px 18px max(18px, env(safe-area-inset-bottom)); }
  /* The link is down: touches are not sent, so the surface steps back until it returns. */
  main.dim { opacity: .35; pointer-events: none; }

  /* Rail (landscape, tablet): a docked panel on a seam; the chosen screen is picked — the soft
     tint, orange text and a 2 × 14 orange tick, as the desktop's left rail. */
  .rail { width: 104px; flex: none; display: flex; flex-direction: column; gap: 2px; padding: 8px 6px; background: var(--panel); border-right: 1px solid var(--seam); }
  .tablet .rail { padding: 12px 8px; }
  .rail button { height: 44px; border-radius: min(var(--r-field), 14px); font-size: 12px; font-weight: 500; color: var(--text-muted); position: relative; transition: background-color var(--ease), color var(--ease); }
  .tablet .rail button { height: 56px; font-size: 13px; }
  .rail button.on { background: var(--accent-soft); color: var(--picked-ink); }
  .rail button.on::before { content: ''; position: absolute; left: -6px; top: 50%; height: 14px; margin-top: -7px; width: 2px; border-radius: 0 var(--r-fine) var(--r-fine) 0; background: var(--accent); }
  .tablet .rail button.on::before { left: -8px; }

  /* Tabs (portrait): a docked bar; the chosen screen is picked — the soft tint and orange text,
     with an orange line on the edge facing the screen. */
  .tabs { flex: none; display: flex; padding: 4px 6px max(8px, env(safe-area-inset-bottom)); background: var(--panel); border-top: 1px solid var(--seam); }
  .tabs button { flex: 1; min-width: 0; height: 48px; border-radius: min(var(--r-field), 14px); font-size: 12px; font-weight: 500; color: var(--text-muted); position: relative; transition: background-color var(--ease), color var(--ease); }
  .tabs button.on { background: var(--accent-soft); color: var(--picked-ink); }
</style>
