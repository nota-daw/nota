<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- The strip under the header: the link is being rebuilt, or Nota's MIDI Learn is listening. -->
<script lang="ts">
  import { app } from '../lib/app.svelte';

  const b = $derived.by(() => {
    if (app.link === 'lost')
      return { kind: 'quiet', title: `Reconnecting to ${app.host}…`, sub: 'Touches are not sent until the link is back' };
    if (app.learn.on && app.live) {
      if (app.learned) return { kind: 'learn', title: `Mapped ${app.learned}`, sub: 'Click another control in Nota to keep mapping' };
      return {
        kind: 'learn',
        title: app.learn.pend ? `Nota is listening: ${app.learn.pend}` : 'MIDI Learn is on in Nota',
        sub: app.learn.pend
          ? 'Move a macro, an XY axis or tilt the phone. Pads, keys and the mixer can’t be mapped.'
          : 'Click a control in Nota, then move a macro, an XY axis or tilt the phone.',
      };
    }
    return null;
  });
</script>

{#if b}
  <div class="banner {b.kind}" role="status">
    <span class="dot" class:blink={b.kind === 'quiet'}></span>
    <div>
      <div class="t">{b.title}</div>
      <div class="s">{b.sub}</div>
    </div>
  </div>
{/if}

<style>
  /* A docked strip under the header (square). MIDI Learn listening is live, so its dot is the accent. */
  .banner { flex: none; display: flex; align-items: center; gap: 10px; padding: 9px 16px; background: var(--panel); border-bottom: 1px solid var(--seam); }
  .dot { width: 8px; height: 8px; border-radius: 50%; flex: none; background: var(--accent); }
  .quiet .dot { background: var(--text-tertiary); }
  .blink { animation: blink 800ms steps(1) infinite; }
  @keyframes blink { 50% { opacity: .35; } }
  .t { font-size: 12px; font-weight: 600; color: var(--text-primary); }
  .s { font-size: 11px; color: var(--text-secondary); }
  .learn { background: var(--accent-soft); }
  .learn .t { color: var(--picked-ink); }
</style>
