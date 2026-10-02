// SPDX-License-Identifier: AGPL-3.0-only
// Nota Remote service worker. Browsers only run it on a secure origin (HTTPS or localhost),
// so over plain LAN HTTP the page simply works without it. Where it does run, it keeps the
// app shell so a Home Screen launch still opens — and can say "Nota isn't responding" — while
// Nota is closed. Network first: the page always comes from the running Nota when it can.
const CACHE = 'nota-remote-v1';

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(CACHE).then((c) => c.addAll(['./', './index.html', './manifest.webmanifest'])).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET' || new URL(req.url).pathname.endsWith('/ws')) return;
  e.respondWith(
    fetch(req)
      .then((res) => {
        if (res.ok) { const copy = res.clone(); caches.open(CACHE).then((c) => c.put(req, copy)); }
        return res;
      })
      .catch(() => caches.match(req).then((r) => r || caches.match('./index.html')))
  );
});
