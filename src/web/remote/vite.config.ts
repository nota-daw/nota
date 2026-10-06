// SPDX-License-Identifier: AGPL-3.0-only
// Builds the phone app into Nota.Remote/wwwroot, which Nota embeds and serves. `npm run dev`
// proxies /ws to a running Nota (NOTA_REMOTE=http://192.168.1.5:7788 to pick another one).
import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

const nota = process.env.NOTA_REMOTE ?? 'http://127.0.0.1:7788';

export default defineConfig({
  plugins: [svelte()],
  base: './',
  build: {
    outDir: '../../managed/Nota.Remote/wwwroot',
    emptyOutDir: true,
    target: ['safari16', 'chrome100'],
    assetsInlineLimit: 0,
    reportCompressedSize: true,
  },
  server: {
    host: true,
    proxy: { '/ws': { target: nota, ws: true } },
  },
});
