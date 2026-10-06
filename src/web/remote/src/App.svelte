<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<script lang="ts">
  import { app } from './lib/app.svelte';
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

  $effect(() => {
    document.documentElement.dataset.theme = app.dark ? 'dark' : 'light';
    document.querySelector('meta[name=theme-color]')?.setAttribute('content', app.dark ? '#0B0A09' : '#DCD6C5');
  });
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
      <div class="seg">
        {#each SCREENS as s}
          <button class:on={app.screen === s && !app.bigTransport} onclick={() => app.setScreen(s)}>{s}</button>
        {/each}
      </div>
    </nav>
  {/if}
  <Sheets {form} />
  <Overlays {form} />
</div>

<style>
  .shell {
    position: absolute; inset: 0; display: flex; flex-direction: column; background: var(--app);
    padding: env(safe-area-inset-top) env(safe-area-inset-right) 0 env(safe-area-inset-left);
  }
  .body { flex: 1; min-height: 0; display: flex; }
  main { flex: 1; min-width: 0; position: relative; display: flex; flex-direction: column; padding: 12px; transition: opacity 200ms ease-out; }
  .phoneLand main { padding: 8px 10px max(10px, env(safe-area-inset-bottom)) 10px; }
  .tablet main { padding: 18px 18px max(18px, env(safe-area-inset-bottom)); }
  main.dim { opacity: .35; pointer-events: none; }
  .rail { width: 104px; flex: none; display: flex; flex-direction: column; gap: 4px; padding: 8px; border-right: 1px solid var(--hairline); }
  .tablet .rail { padding: 12px 10px; }
  .rail button { height: 44px; border-radius: 5px; font-size: 11px; font-weight: 600; color: var(--ink4); position: relative; transition: background-color 120ms ease-out, color 120ms ease-out; }
  .tablet .rail button { height: 56px; font-size: 13px; }
  .rail button.on { background: var(--raised); color: var(--ink1); }
  .rail button.on::before { content: ''; position: absolute; left: 0; top: 10px; bottom: 10px; width: 2px; border-radius: 1px; background: var(--accent); }
  .tabs { flex: none; padding: 8px 12px max(12px, env(safe-area-inset-bottom)); border-top: 1px solid var(--hairline); }
  .tabs .seg { border-radius: 8px; }
  .tabs .seg button { height: 44px; padding: 0; font-size: 11px; min-width: 0; border-radius: 5px; }
</style>
