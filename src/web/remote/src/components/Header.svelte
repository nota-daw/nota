<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- The header every screen sits under: the track picker (‹ › step, tap for the list), the
     mini transport, the link indicator and the ⋯ menu. Tapping the position opens the big
     transport. Portrait stacks it in two rows; landscape and tablet keep one. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
  import type { Form } from '../lib/device.svelte';

  let { form }: { form: Form } = $props();

  const t = $derived(app.track);
  const sub = $derived.by(() => {
    if (!t) return app.proj ? 'No track to play' : 'Connecting…';
    const others = (app.who[String(t.id)] ?? []).join(', ');
    return t.dev + (app.proj?.follow ? ' · following Nota' : '') + (others ? ` · with ${others}` : '');
  });

  // The readout animates between Nota's messages (bar.beat), off one rAF loop.
  let pos = $state('1.1');
  $effect(() => {
    let raf = 0;
    const loop = () => {
      const b = Math.max(0, app.position());
      pos = `${Math.floor(b / 4) + 1}.${Math.floor(b % 4) + 1}`;
      raf = requestAnimationFrame(loop);
    };
    loop();
    return () => cancelAnimationFrame(raf);
  });

  const link = $derived.by(() => {
    switch (app.link) {
      case 'ok': return { dot: 'var(--success-dim)', ink: 'var(--ink4)', label: app.rtt >= 0 ? `${app.rtt} ms` : 'Connected' };
      case 'weak': return { dot: 'var(--warning)', ink: 'var(--warning)', label: `Weak · ${app.rtt} ms` };
      case 'lost': case 'connecting': return { dot: 'var(--ink4)', ink: 'var(--ink3)', label: app.link === 'lost' ? 'Reconnecting' : 'Connecting', blink: true };
      case 'down': return { dot: 'var(--danger)', ink: 'var(--danger-bright)', label: 'No connection', blink: true };
      default: return { dot: 'var(--danger)', ink: 'var(--danger-bright)', label: 'Not paired' };
    }
  });

  function linkTip() {
    if (app.link === 'weak') app.showToast(`Latency is ${app.rtt} ms. Move closer to the router or put the phone on 5 GHz Wi-Fi.`);
    else if (app.link === 'ok') app.showToast(`Connected to ${app.host} · ${app.rtt} ms`);
    else if (app.link === 'lost') app.showToast('Touches are not sent until the link is back.');
  }

  function play() {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    app.send({ t: app.tp.play ? 'stop' : 'play' });
  }
  function stop() {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    app.send({ t: 'stop' });
  }
  function rec() {
    if (!app.canControl) return app.showToast('Nota lets this phone play notes only.');
    app.record();
  }
</script>

<header class={form}>
  <div class="picker">
    <button class="step" aria-label="Previous track" onclick={() => app.stepTrack(-1)}>
      <svg width="10" height="14" viewBox="0 0 10 14"><path d="M7 2 L2 7 L7 12" /></svg>
    </button>
    <button class="track" onclick={() => (app.sheet = 'tracks')} aria-label="Choose the track to play">
      <span class="bar" style:background={t?.color ?? 'var(--border-strong)'}></span>
      <span class="names">
        <span class="name">{t?.name ?? '—'}</span>
        <span class="sub mono">{sub}</span>
      </span>
      <svg width="10" height="10" viewBox="0 0 10 10"><path d="M2 3.5 L5 6.5 L8 3.5" /></svg>
    </button>
    <button class="step" aria-label="Next track" onclick={() => app.stepTrack(1)}>
      <svg width="10" height="14" viewBox="0 0 10 14"><path d="M3 2 L8 7 L3 12" /></svg>
    </button>
  </div>

  <div class="transport">
    <button class="tbtn" aria-label="Stop" onclick={stop}><span class="sq"></span></button>
    <button class="tbtn play" class:on={app.tp.play} aria-label={app.tp.play ? 'Stop playback' : 'Play'} aria-pressed={app.tp.play} onclick={play}><span class="tri"></span></button>
    <button class="tbtn rec" class:on={app.tp.rec} class:count={app.countIn !== null} aria-label="Record" aria-pressed={app.tp.rec} onclick={rec}><span class="dot"></span></button>
    <button class="pos" class:open={app.bigTransport} aria-label="Big transport" onclick={() => (app.bigTransport = !app.bigTransport)}>
      <span class="mono p">{pos}</span>
      <span class="mono bpm">{app.tp.bpm.toFixed(0)}</span>
      <svg width="10" height="10" viewBox="0 0 10 10"><path d="M2 3.5 L5 6.5 L8 3.5" /></svg>
    </button>
  </div>

  <div class="status">
    <button class="link" onclick={linkTip} aria-label="Connection: {link.label}">
      <span class="ldot" class:blink={link.blink} style:background={link.dot}></span>
      <span class="mono" style:color={link.ink}>{link.label}</span>
    </button>
    <button class="menu" class:open={app.sheet === 'menu'} aria-label="Menu" onclick={() => (app.sheet = 'menu')}>
      <span></span><span></span><span></span>
    </button>
  </div>
</header>

<style>
  header { flex: none; display: flex; flex-wrap: wrap; align-items: center; gap: 8px 10px; padding: 6px 10px 10px; border-bottom: 1px solid var(--hairline); background: var(--app); position: relative; z-index: 6; }
  header.phoneLand { flex-wrap: nowrap; padding: 6px 10px; }
  header.tablet { flex-wrap: nowrap; padding: 10px 16px; }
  svg path { fill: none; stroke: var(--ink4); stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }

  .picker { order: 1; flex: 1; min-width: 0; display: flex; align-items: center; gap: 4px; }
  .step { width: 36px; height: 44px; flex: none; display: flex; align-items: center; justify-content: center; border-radius: 5px; }
  .track { flex: 1; min-width: 0; height: 44px; display: flex; align-items: center; gap: 10px; padding: 0 10px; background: var(--panel); border: 1px solid var(--border); border-radius: 6px; text-align: left; }
  .bar { width: 4px; height: 24px; border-radius: 2px; flex: none; }
  .names { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
  .name { font-size: 14px; font-weight: 600; color: var(--ink1); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sub { font-size: 10px; color: var(--ink5); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

  .transport { order: 3; flex-basis: 100%; display: flex; align-items: center; gap: 6px; }
  .phoneLand .transport { order: 2; flex: 0 0 330px; }
  .tablet .transport { order: 2; flex: 0 0 460px; }
  .tbtn { width: 44px; height: 40px; border-radius: 5px; background: var(--raised); border: 1px solid var(--border); display: flex; align-items: center; justify-content: center; flex: none; transition: background-color 120ms ease-out, border-color 120ms ease-out; }
  .tbtn.play { width: 54px; }
  .sq { width: 11px; height: 11px; border-radius: 1px; background: var(--ink2); }
  .tri { width: 0; height: 0; border-left: 12px solid var(--ink2); border-top: 7px solid transparent; border-bottom: 7px solid transparent; margin-left: 3px; }
  .play.on { background: var(--accent); border-color: var(--accent); }
  .play.on .tri { border-left-color: var(--on-accent); }
  .dot { width: 12px; height: 12px; border-radius: 50%; background: var(--ink4); }
  .rec.on, .rec.count { background: var(--record-wash); border-color: var(--record); }
  .rec.on .dot, .rec.count .dot { background: var(--record); }
  .pos { flex: 1; min-width: 0; height: 40px; display: flex; align-items: center; gap: 10px; padding: 0 10px; border-radius: 5px; background: var(--well); border: 1px solid var(--border); box-shadow: inset 0 1px 0 rgba(0,0,0,.5); }
  .pos.open { border-color: var(--border-brass); }
  .pos.open svg { transform: rotate(180deg); }
  .pos svg { margin-left: auto; }
  .p { font-size: 16px; font-weight: 500; color: var(--ink1); }
  .bpm { font-size: 11px; color: var(--ink4); }

  .status { order: 2; display: flex; align-items: center; gap: 4px; }
  .phoneLand .status, .tablet .status { order: 3; }
  .link { height: 44px; display: flex; align-items: center; gap: 6px; padding: 0 8px; border-radius: 5px; }
  .link .mono { font-size: 10px; white-space: nowrap; }
  .ldot { width: 7px; height: 7px; border-radius: 50%; }
  .blink { animation: blink 800ms steps(1) infinite; }
  @keyframes blink { 50% { opacity: .35; } }
  .menu { width: 44px; height: 44px; display: flex; align-items: center; justify-content: center; gap: 3px; border-radius: 5px; }
  .menu.open { background: var(--raised); }
  .menu span { width: 4px; height: 4px; border-radius: 50%; background: var(--ink3); }
</style>
