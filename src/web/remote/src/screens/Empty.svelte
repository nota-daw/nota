<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<script lang="ts">
  import { app } from '../lib/app.svelte';
</script>

<div class="empty">
  <span class="t">{app.track?.name ?? 'This track'} is an audio track</span>
  <span class="s">There is no instrument to play. Pick an instrument track, or use the transport to record.</span>
  <button class="btn" onclick={() => (app.sheet = 'tracks')}>Choose another track</button>
</div>

<style>
  .empty { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px; text-align: center; padding: 24px; border-radius: var(--r-card); background: var(--panel); box-shadow: 0 0 0 1px var(--seam); }
  .t { font-size: 14px; font-weight: 600; color: var(--text-primary); }
  .s { font-size: 12px; line-height: 1.6; color: var(--text-tertiary); max-width: 280px; }
  .btn { height: 44px; padding: 0 18px; font-size: 13px; }
</style>
