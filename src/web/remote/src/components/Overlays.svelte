<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- States that cover the screen: Nota can't be reached, the phone needs a new code, the phone
     left, the count-in before a take — and the toast. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';

  let { form }: { form: Form } = $props();
  let digits = $state('');
  let input: HTMLInputElement | undefined = $state();

  $effect(() => {
    if (app.link === 'expired') { digits = ''; setTimeout(() => input?.focus(), 50); }
  });
  $effect(() => {
    if (digits.length === 4 && app.link === 'expired') app.pairWith(digits);
  });

  const why = $derived(
    app.deny === 'code' ? 'That code didn’t match. Codes change every two minutes — check the one Nota shows now.'
    : app.deny === 'locked' ? 'Too many wrong codes. Wait half a minute, then try the code Nota shows.'
    : app.deny === 'version' ? 'This page is older than Nota. Reload it.'
    : app.deny === 'new' ? 'In Nota, click Remote in the top bar and scan the QR code with the camera, or type its four digits here.'
    : 'This phone was removed from trusted devices, or the code has expired. In Nota, click Remote to show a fresh QR, or type its four digits here.');
</script>

{#if app.link === 'down'}
  <div class="cover" role="alert">
    <h1>Nota isn’t responding</h1>
    <ol>
      <li><span class="mono">1</span>Nota is open on {app.host}</li>
      <li><span class="mono">2</span>Remote is on: the Remote button in Nota’s top bar is lit</li>
      <li><span class="mono">3</span>The phone is on the same Wi-Fi. Guest networks often keep devices apart; share the phone’s hotspot with the computer instead.</li>
    </ol>
    <div class="retry"><span class="dot blink"></span>Retrying every few seconds</div>
  </div>
{:else if app.link === 'expired'}
  <div class="cover" role="alert">
    <h1>{app.deny === 'new' ? 'Pair with Nota' : 'Scan a new code in Nota'}</h1>
    <p>{why}</p>
    <label class="code" aria-label="Four-digit code">
      <input bind:this={input} bind:value={digits} inputmode="numeric" pattern="[0-9]*" maxlength="4" autocomplete="one-time-code"
        oninput={() => (digits = digits.replace(/\D/g, '').slice(0, 4))} />
      {#each [0, 1, 2, 3] as i}
        <span class="cell" class:cur={digits.length === i}>
          {#if digits[i]}<span class="mono">{digits[i]}</span>{:else if digits.length === i}<span class="caret"></span>{/if}
        </span>
      {/each}
    </label>
  </div>
{:else if app.link === 'left'}
  <div class="cover" role="alert">
    <h1>Disconnected</h1>
    <p>This phone has left Nota. It is still trusted, so connecting again needs no code.</p>
    <button class="btn on big" onclick={() => app.rejoin()}>Connect again</button>
  </div>
{/if}

{#if app.countIn !== null}
  <button class="count" onclick={() => app.record()} aria-label="Cancel count-in">
    <span class="mono num {form}">{app.countIn}</span>
    <span class="caps rec">Count-in · recording starts on 1</span>
  </button>
{/if}

{#if app.toast}
  <div class="toast" class:low={form === 'phone'} role="status">{app.toast}</div>
{/if}

<style>
  .cover { position: absolute; inset: 0; z-index: 40; background: var(--app); display: flex; flex-direction: column; justify-content: center; gap: 18px; padding: 28px; padding-top: calc(env(safe-area-inset-top) + 28px); }
  h1 { font-size: 22px; font-weight: 600; color: var(--ink0); }
  p { font-size: 13px; line-height: 1.6; color: var(--ink3); max-width: 420px; }
  ol { list-style: none; border: 1px solid var(--border); border-radius: 6px; overflow: hidden; max-width: 420px; }
  li { padding: 12px 14px; background: var(--panel); display: flex; gap: 12px; font-size: 12px; line-height: 1.5; color: var(--ink2); }
  li + li { border-top: 1px solid var(--hairline); }
  li .mono { font-size: 11px; color: var(--accent-dim); }
  .retry { display: flex; align-items: center; gap: 10px; font-size: 12px; color: var(--ink4); }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--ink5); }
  .blink { animation: blink 800ms steps(1) infinite; }
  @keyframes blink { 50% { opacity: .35; } }
  .code { position: relative; display: grid; grid-template-columns: repeat(4, 64px); gap: 10px; }
  .code input { position: absolute; inset: 0; opacity: 0; font-size: 16px; }
  .cell { height: 68px; border-radius: 6px; background: var(--well); border: 1px solid var(--hairline); box-shadow: inset 0 1px 0 rgba(0,0,0,.5); display: flex; align-items: center; justify-content: center; pointer-events: none; }
  .cell.cur { border-color: var(--border-brass); }
  .cell .mono { font-size: 28px; color: var(--ink0); }
  .caret { width: 2px; height: 28px; background: var(--accent); animation: blink 1s steps(1) infinite; }
  .big { height: 48px; align-self: flex-start; padding: 0 20px; font-size: 13px; }
  .count { position: absolute; inset: 0; z-index: 35; background: var(--app); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px; }
  .num { font-size: 200px; font-weight: 500; line-height: 1; color: var(--accent-bright); }
  .num.phoneLand { font-size: 150px; }
  .num.tablet { font-size: 260px; }
  .rec { color: var(--record); font-size: 12px; }
  .toast { position: absolute; left: 50%; bottom: 24px; transform: translateX(-50%); z-index: 50; max-width: 86%; padding: 10px 14px; border-radius: 6px; background: var(--raised); border: 1px solid var(--border-strong); font-size: 12px; line-height: 1.45; color: var(--ink1); text-align: center; }
  .toast.low { bottom: calc(env(safe-area-inset-bottom) + 84px); }
</style>
