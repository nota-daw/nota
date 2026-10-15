// SPDX-License-Identifier: AGPL-3.0-only
// A stand-in Nota for working on the phone page without the desktop app: it serves the built
// page (Nota.Remote/wwwroot) and speaks enough of the Remote protocol — pairing, a demo
// project, transport, mixer, XY, macros, Session and the theme message built from the real
// theme files — to show every screen. For screenshots and design review; Nota itself is the
// reference (Nota.Remote/RemoteHub*.cs). No dependencies: the WebSocket is hand-rolled.
//
//   npm run mock            then open http://127.0.0.1:7790/#p=1234 at phone width
//   ?theme=<id>             the theme the stand-in wears (any Theme/Themes/<id>.json)
//   ?pick=<dark>,<light>    its dark-mode and light-mode picks
//   ?legacy=1               behave like an older Nota: no theme message, only proj.dark
//   GET /mock?theme=<id>    switch the worn theme live for the connected phones

import { createServer } from 'node:http';
import { createHash } from 'node:crypto';
import { readFileSync, existsSync, statSync, readdirSync } from 'node:fs';
import { join, extname, normalize, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '../../../managed/Nota.Remote/wwwroot');
if (!existsSync(join(root, 'index.html'))) {
  console.error('nota-mock: Nota.Remote/wwwroot has no built page yet; run `npm run build` first.');
  process.exit(1);
}
const themesDir = join(here, '../../../managed/Nota.App/Theme/Themes');
const port = Number(process.env.PORT ?? 7790);

// ---- themes -------------------------------------------------------------------------

const themes = Object.fromEntries(readdirSync(themesDir).filter((f) => f.endsWith('.json')).map((f) => {
  const t = JSON.parse(readFileSync(join(themesDir, f), 'utf8'));
  // Avalonia writes #AARRGGBB; CSS reads #RRGGBBAA.
  const c = Object.fromEntries(Object.entries(t.colors).map(([k, v]) => [k, v.length === 9 ? '#' + v.slice(3) + v.slice(1, 3) : v]));
  return [t.id, { name: t.name, dark: t.kind === 'dark', c }];
}));
const look = { on: 'studio-dark', dark: 'studio-dark', light: 'studio-light', legacy: false };

function themeMessage() {
  const ids = [...new Set([look.on, look.dark, look.light])];
  const ink = [themes['studio-dark'].c.TextOnAccent, themes['studio-dark'].c.ShadeUp];
  return { t: 'theme', on: look.on, dark: look.dark, light: look.light, ink, th: Object.fromEntries(ids.map((id) => [id, themes[id]])) };
}

function readLook(url) {
  const q = new URL(url, 'http://x').searchParams;
  if (themes[q.get('theme')]) look.on = q.get('theme');
  const pick = (q.get('pick') ?? '').split(',');
  if (themes[pick[0]]?.dark) look.dark = pick[0];
  if (themes[pick[1]] && !themes[pick[1]].dark) look.light = pick[1];
  if (q.has('legacy')) look.legacy = q.get('legacy') === '1';
}

// ---- the demo project -----------------------------------------------------------------

const colour = (slot) => themes[look.on].c[`Track${slot}`];
const trackDefs = [
  { id: 1, name: 'Drums', slot: 1, kind: 'drum', dev: 'Nota Rhythm', grp: 0 },
  { id: 2, name: 'Bass', slot: 3, kind: 'inst', dev: 'Nota Volt', grp: 0, mpe: true },
  { id: 3, name: 'Keys', slot: 4, kind: 'inst', dev: 'Nota Aurora', grp: 0 },
  { id: 4, name: 'Vocals', slot: 8, kind: 'audio', dev: 'Audio', grp: 0 },
  { id: 5, name: 'Pads', slot: 5, kind: 'group', dev: 'Group', grp: 0 },
  { id: 6, name: 'Strings', slot: 6, kind: 'inst', dev: 'Nota Chamber', grp: 5 },
  { id: 7, name: 'Reverb', slot: 9, kind: 'return', dev: 'Return', grp: 0 },
];
const pads = [[36, 'Kick'], [37, 'Rim'], [38, 'Snare'], [39, 'Clap'], [42, 'Closed hat'], [46, 'Open hat'], [45, 'Low tom'], [49, 'Crash']];

const state = {
  tp: { play: true, rec: false, loop: true, met: false, pos: 22.5, bpm: 122, ls: 16, le: 32, can: true },
  mix: new Map(trackDefs.map((t, i) => [t.id, { vol: [0.9, 0.75, 0.6, 1.1, 0.8, 0.7, 0.5][i], pan: [0, -0.2, 0.3, 0, 0, -0.4, 0][i], mute: t.id === 3, solo: t.id === 2, arm: t.id === 4 }])),
  master: 1,
  you: 1,
  follow: false,
  started: Date.now(),
};

function proj() {
  return {
    t: 'proj', host: 'Studio Mac', dark: themes[look.on].dark,
    tracks: trackDefs.map((t) => ({ id: t.id, name: t.name, color: colour(t.slot), kind: t.kind, dev: t.dev, grp: t.grp, ...(t.mpe ? { mpe: true } : {}), ...(t.kind === 'drum' ? { pads } : {}) })),
    key: { root: 9, minor: true, name: 'A minor' }, scenes: 4,
    sections: [['Intro', 0], ['Verse', 16], ['Chorus', 32], ['Bridge', 48], ['Outro', 64]],
    access: 'control', you: state.you, follow: state.follow,
  };
}

const level = (base, t, k) => Math.max(0, Math.min(0.95, base + 0.08 * Math.sin(t * (2.1 + k)) + 0.05 * Math.sin(t * 7.3 + k)));
function mix() {
  const t = (Date.now() - state.started) / 1000;
  return {
    t: 'mix',
    ch: trackDefs.map((d, i) => {
      const m = state.mix.get(d.id);
      const on = state.tp.play && !m.mute;
      return [d.id, m.vol, m.pan, m.mute ? 1 : 0, m.solo ? 1 : 0, m.arm ? 1 : 0, on ? level(0.62 + i * 0.03, t, i) : 0, on ? level(0.6 + i * 0.03, t, i + 0.5) : 0, d.id === 3 ? 'Anna' : '', d.id === 3 ? 99 : 0];
    }),
    m: [state.master, state.tp.play ? level(0.8, t, 9) : 0, state.tp.play ? level(0.79, t, 9.4) : 0, '', 0],
  };
}

const xy = () => ({ t: 'xy', id: state.you, ok: true, auto: false, x: { n: 'Cutoff', d: 'Auto Filter', v: 0.62, txt: '2.4 kHz', map: null, tilt: null }, y: { n: 'Resonance', d: 'Auto Filter', v: 0.35, txt: '35 %', map: null, tilt: null } });
const mac = () => ({
  t: 'mac', id: state.you, di: 0, devs: ['Nota Volt', 'Auto Filter', 'Reverb'],
  k: [
    { n: 'Cutoff', v: 0.64, txt: '3.1 kHz', a: true, map: null }, { n: 'Reso', v: 0.3, txt: '30 %', map: null },
    { n: 'Drive', v: 0.45, txt: '+4.5 dB', map: null }, { n: 'Env amount', v: 0.7, txt: '70 %', map: null },
    { n: 'Attack', v: 0.12, txt: '12 ms', map: null }, { n: 'Decay', v: 0.4, txt: '420 ms', map: null },
    { n: 'LFO rate', v: 0.55, txt: '2.20 Hz', map: null }, { map: null },
  ],
});
function ses() {
  const prog = (((Date.now() - state.started) / 1000) % 8) / 8;
  return {
    t: 'ses', n: 4,
    tr: [
      { id: 1, c: [[3, 16, prog], [1, 16, 0], [1, 8, 0], [0, 0, 0]] },
      { id: 2, c: [[1, 16, 0], [3, 16, prog], [2, 16, 0], [0, 0, 0]] },
      { id: 3, c: [[1, 32, 0], [0, 0, 0], [1, 16, 0], [1, 4, 0]] },
      { id: 4, c: [[0, 0, 0], [4, 16, 0], [0, 0, 0], [0, 0, 0]] },
      { id: 6, c: [[1, 16, 0], [1, 16, 0], [0, 0, 0], [3, 16, prog]] },
    ],
  };
}

// ---- WebSocket ----------------------------------------------------------------------

const clients = new Set();

function frame(text) {
  const data = Buffer.from(text);
  const n = data.length;
  const head = n < 126 ? Buffer.from([0x81, n]) : n < 65536 ? Buffer.from([0x81, 126, n >> 8, n & 255]) : Buffer.concat([Buffer.from([0x81, 127, 0, 0, 0, 0]), Buffer.from([(n >>> 24) & 255, (n >>> 16) & 255, (n >>> 8) & 255, n & 255])]);
  return Buffer.concat([head, data]);
}

function onSocket(sock) {
  const c = { sock, screen: 'Pads', buf: Buffer.alloc(0), paired: false };
  clients.add(c);
  const send = (m) => { if (!sock.destroyed) sock.write(frame(JSON.stringify(m))); };
  c.send = send;
  sock.on('data', (chunk) => {
    c.buf = Buffer.concat([c.buf, chunk]);
    while (c.buf.length >= 2) {
      const op = c.buf[0] & 15;
      let len = c.buf[1] & 127, off = 2;
      if (len === 126) { len = c.buf.readUInt16BE(2); off = 4; } else if (len === 127) { len = Number(c.buf.readBigUInt64BE(2)); off = 10; }
      if (c.buf.length < off + 4 + len) return;
      const mask = c.buf.subarray(off, off + 4);
      const body = Buffer.from(c.buf.subarray(off + 4, off + 4 + len));
      for (let i = 0; i < body.length; i++) body[i] ^= mask[i & 3];
      c.buf = c.buf.subarray(off + 4 + len);
      if (op === 8) { sock.end(); return; }
      if (op === 1) { try { onMessage(c, JSON.parse(body.toString())); } catch { /* ignore */ } }
    }
  });
  sock.on('close', () => clients.delete(c));
  sock.on('error', () => clients.delete(c));
}

function sendScreen(c) {
  const s = { Mixer: mix, XY: xy, Macros: mac, Scenes: ses }[c.screen];
  if (s) c.send(s());
}

function welcome(c, m) {
  c.paired = true;
  c.screen = m.screen ?? c.screen;
  if (trackDefs.some((t) => t.id === m.track && t.kind !== 'group' && t.kind !== 'return')) state.you = m.track;
  c.send({ t: 'welcome', token: 'mock-token-0123456789abcdef', host: 'Studio Mac', conn: 1, via: 'wifi', name: m.name ?? 'iPhone' });
  if (!look.legacy) c.send(themeMessage());
  c.send(proj());
  c.send({ t: 'tp', ...state.tp });
  c.send({ t: 'who', p: { 3: ['Anna'] } });
  c.send({ t: 'learn', on: false, pend: null });
  sendScreen(c);
}

function onMessage(c, m) {
  const tp = state.tp, ch = state.mix.get(m.id);
  switch (m.t) {
    case 'pair': case 'hello': welcome(c, m); break;
    case 'ping': c.send({ t: 'pong', c: m.c }); break;
    case 'sub': c.screen = m.s; sendScreen(c); break;
    case 'track': state.you = m.id; broadcast(proj()); break;
    case 'follow': state.follow = m.on; broadcast(proj()); break;
    case 'play': tp.play = true; broadcast({ t: 'tp', ...tp }); break;
    case 'stop': tp.play = false; tp.rec = false; broadcast({ t: 'tp', ...tp }); break;
    case 'rec': tp.rec = m.on; if (m.on) tp.play = true; broadcast({ t: 'tp', ...tp }); break;
    case 'loop': tp.loop = m.on; broadcast({ t: 'tp', ...tp }); break;
    case 'met': tp.met = m.on; broadcast({ t: 'tp', ...tp }); break;
    case 'bpm': tp.bpm = m.v; broadcast({ t: 'tp', ...tp }); break;
    case 'mute': case 'solo': case 'arm': if (ch) ch[m.t] = m.on; break;
    case 'vol': if (ch) ch.vol = m.v; break;
    case 'pan': if (ch) ch.pan = m.v; break;
    case 'mvol': state.master = m.v; break;
    case 'on': c.send({ t: 'lit', n: [m.p] }); break;
    case 'off': c.send({ t: 'lit', n: [] }); break;
  }
}

function broadcast(m) { for (const c of clients) if (c.paired) c.send(m); }

// Transport and screen state tick on, so meters move and clips progress.
setInterval(() => {
  if (state.tp.play) {
    state.tp.pos += (state.tp.bpm / 60) * 0.25;
    if (state.tp.loop && state.tp.pos >= state.tp.le) state.tp.pos = state.tp.ls;
    broadcast({ t: 'tp', ...state.tp });
  }
  for (const c of clients) if (c.paired && (c.screen === 'Mixer' || c.screen === 'Scenes')) sendScreen(c);
}, 250);

// ---- HTTP -----------------------------------------------------------------------------

const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.webmanifest': 'application/manifest+json', '.png': 'image/png', '.woff2': 'font/woff2', '.mp4': 'video/mp4', '.svg': 'image/svg+xml' };

const server = createServer((req, res) => {
  const url = new URL(req.url, 'http://x');
  if (url.pathname === '/mock') {
    readLook(req.url);
    if (!look.legacy) broadcast(themeMessage());
    broadcast(proj());
    res.end(JSON.stringify(look));
    return;
  }
  if (url.pathname === '/' || url.pathname === '/index.html') readLook(req.url);
  let file = normalize(join(root, decodeURIComponent(url.pathname)));
  if (!file.startsWith(root) || !existsSync(file) || statSync(file).isDirectory()) file = join(root, 'index.html');
  res.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
  res.end(readFileSync(file));
});

server.on('upgrade', (req, sock) => {
  const key = req.headers['sec-websocket-key'];
  if (!key) { sock.destroy(); return; }
  const accept = createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  sock.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
  onSocket(sock);
});

server.listen(port, '127.0.0.1', () => console.log(`Stand-in Nota on http://127.0.0.1:${port}/#p=1234`));
