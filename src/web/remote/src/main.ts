// SPDX-License-Identifier: AGPL-3.0-only
import { mount } from 'svelte';
import './app.css';
import App from './App.svelte';
import { app } from './lib/app.svelte';

// Browser gestures fight fast playing: no pinch zoom, no long-press menu, no double-tap zoom.
for (const ev of ['gesturestart', 'gesturechange', 'contextmenu', 'dblclick']) {
  document.addEventListener(ev, (e) => e.preventDefault(), { passive: false });
}
document.addEventListener('touchmove', (e) => { if ((e as TouchEvent).touches.length > 1) e.preventDefault(); }, { passive: false });
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') app.wake(); });
window.addEventListener('online', () => app.wake());

if ('serviceWorker' in navigator && window.isSecureContext) {
  navigator.serviceWorker.register('./sw.js').catch(() => {});
}

mount(App, { target: document.getElementById('app')! });
app.connect();
