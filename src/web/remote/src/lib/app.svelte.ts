// SPDX-License-Identifier: AGPL-3.0-only
// The phone's whole state and its link to Nota. One WebSocket; a hello with the device token
// (or a pairing code from the QR) opens it. The phone pings every second: the round trip is
// the link indicator, and Nota releases a silent phone's notes. On a drop it retries on its
// own — the only state that needs the person is a forgotten device or an expired code.

import type { Project, Transport, Mixer, Xy, Macros, Session, Learn, LinkState, Screen, Track } from './types';

const LS = 'nota-remote:';
const store = {
  get(k: string, d = ''): string { try { return localStorage.getItem(LS + k) ?? d; } catch { return d; } },
  set(k: string, v: string) { try { localStorage.setItem(LS + k, v); } catch { /* private mode */ } },
  del(k: string) { try { localStorage.removeItem(LS + k); } catch { /* */ } },
};

export interface Prefs {
  theme: 'nota' | 'phone';
  haptics: boolean;
  countIn: boolean;
  fullVel: boolean;
  keysMode: 'Keyboard' | 'Scale' | 'Chords';
  octave: number;
  /** Phone-side key override: tonic 0..11 + minor, or null to use the project's. */
  key: { root: number; minor: boolean } | null;
  xyHold: boolean;
  bank: number;
  groupsOpen: number[];
}

const defaultPrefs: Prefs = {
  theme: 'nota', haptics: true, countIn: true, fullVel: false, keysMode: 'Scale', octave: 3, key: null,
  xyHold: true, bank: 0, groupsOpen: [],
};

function guessName(): string {
  const ua = navigator.userAgent;
  if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return 'iPad';
  if (/iPhone/.test(ua)) return 'iPhone';
  if (/Android/.test(ua)) return /Mobile/.test(ua) ? 'Android phone' : 'Android tablet';
  return 'Phone';
}

export class App {
  // ---- from Nota ----
  proj = $state<Project | null>(null);
  tp = $state<Transport>({ play: false, rec: false, loop: false, met: false, pos: 0, bpm: 120, ls: 0, le: 16, can: false });
  lit = $state<number[]>([]);
  who = $state<Record<string, string[]>>({});
  mix = $state<Mixer | null>(null);
  xy = $state<Xy | null>(null);
  mac = $state<Macros | null>(null);
  ses = $state<Session | null>(null);
  learn = $state<Learn>({ on: false, pend: null });
  learned = $state<string | null>(null);

  // ---- link ----
  link = $state<LinkState>('connecting');
  rtt = $state(-1);
  /** The link the phone came in over — a cable says so in the header ("USB · 2 ms"). */
  via = $state<'usb' | 'wifi'>('wifi');
  /** This phone's connection id (Nota marks faders another phone holds with theirs). */
  conn = $state(0);
  host = $state(store.get('host', 'Nota'));
  deny = $state<string | null>(null);
  everConnected = false;

  // ---- phone ----
  screen = $state<Screen>((store.get('screen', '') as Screen) || 'Pads');
  bigTransport = $state(false);
  sheet = $state<'tracks' | 'menu' | 'key' | 'devices' | null>(null);
  toast = $state<string | null>(null);
  countIn = $state<number | null>(null);
  name = $state(store.get('name', guessName()));
  prefs = $state<Prefs>({ ...defaultPrefs, ...JSON.parse(store.get('prefs', '{}') || '{}') });
  /** Position estimate between transport messages, for a smooth readout. */
  posAt = 0;
  posBase = 0;

  private ws: WebSocket | null = null;
  private pingTimer = 0;
  private retryTimer = 0;
  private lostSince = 0;
  private lastPong = 0;
  private rtts: number[] = [];
  private toastTimer = 0;
  private learnedTimer = 0;
  private pairCode: string | null = null;
  private screenChosen = !!store.get('screen', '');
  /** The notes this phone told Nota it holds. Sent with every ping: a pointer-up a browser
   * swallowed (fast multi-touch, a pad replaced by a bank switch) must not ring forever —
   * Nota releases anything the phone stops claiming. */
  private held = new Set<number>();

  constructor() {
    // A QR link carries the pairing code in the fragment (never sent over the network).
    const m = /[#&]p=(\d{4})/.exec(location.hash);
    if (m) {
      this.pairCode = m[1];
      history.replaceState(null, '', location.pathname + location.search);
    }
    $effect.root(() => {
      $effect(() => { store.set('prefs', JSON.stringify(this.prefs)); });
      $effect(() => { store.set('screen', this.screen); });
    });
  }

  get token() { return store.get('token'); }

  get track(): Track | null {
    const p = this.proj;
    if (!p) return null;
    return p.tracks.find((t) => t.id === p.you) ?? null;
  }

  get playable(): Track[] {
    return this.proj?.tracks.filter((t) => t.kind === 'drum' || t.kind === 'inst' || t.kind === 'audio') ?? [];
  }

  get canControl() { return this.proj?.access !== 'play'; }

  get dark(): boolean {
    if (this.prefs.theme === 'phone') return matchMedia('(prefers-color-scheme: dark)').matches;
    return this.proj?.dark ?? true;
  }

  // ---- connection ---------------------------------------------------------------

  connect() {
    clearTimeout(this.retryTimer);
    if (this.ws) { this.ws.onclose = null; this.ws.close(); }
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    let ws: WebSocket;
    try { ws = new WebSocket(`${proto}//${location.host}/ws`); } catch { this.retry(); return; }
    this.ws = ws;
    const opened = Date.now();
    ws.onopen = () => {
      const base = { v: 1, name: this.name, model: guessName(), track: Number(store.get('track', '0')), screen: this.screen, follow: store.get('follow') === '1' };
      if (this.pairCode) this.raw({ t: 'pair', code: this.pairCode, ...base });
      else if (this.token) this.raw({ t: 'hello', token: this.token, ...base });
      else { this.link = 'expired'; this.deny = 'new'; }
    };
    ws.onmessage = (e) => { try { this.onMessage(JSON.parse(e.data)); } catch { /* ignore */ } };
    ws.onclose = () => {
      this.ws = null;
      clearInterval(this.pingTimer);
      if (this.link === 'left' || this.link === 'expired') return;
      if (this.link === 'ok' || this.link === 'weak') { this.link = 'lost'; this.lostSince = Date.now(); }
      else if (this.link === 'connecting' && Date.now() - opened > 0 && !this.everConnected && Date.now() - this.firstTry > 4000) this.link = 'down';
      else if (this.link === 'lost' && Date.now() - this.lostSince > 10000) this.link = 'down';
      this.retry();
    };
  }

  private firstTry = Date.now();

  private retry() {
    clearTimeout(this.retryTimer);
    const wait = this.link === 'down' ? 3000 : this.link === 'lost' ? 700 : 1200;
    this.retryTimer = window.setTimeout(() => this.connect(), wait);
  }

  /** The phone came back to the foreground (unlock, app switch): reconnect at once. */
  wake() {
    if (this.link === 'left') return;
    if (!this.ws || this.ws.readyState > 1 || Date.now() - this.lastPong > 3000) {
      if (this.link === 'ok' || this.link === 'weak') { this.link = 'lost'; this.lostSince = Date.now(); }
      this.connect();
    }
  }

  private startPing() {
    clearInterval(this.pingTimer);
    this.lastPong = Date.now();
    const tick = () => {
      if (Date.now() - this.lastPong > 3500 && (this.link === 'ok' || this.link === 'weak')) {
        // Silent link (Wi-Fi dropped without a close): treat as lost and redial.
        this.link = 'lost';
        this.lostSince = Date.now();
        this.connect();
        return;
      }
      this.raw({ t: 'ping', c: performance.now(), rtt: this.rtt, h: [...this.held] });
    };
    tick();
    this.pingTimer = window.setInterval(tick, 1000);
  }

  pairWith(code: string) {
    this.pairCode = code;
    this.deny = null;
    this.link = 'connecting';
    this.connect();
  }

  leave() {
    this.releaseHeld();
    this.raw({ t: 'bye' });
    this.link = 'left';
    this.sheet = null;
    clearInterval(this.pingTimer);
    this.ws?.close();
  }

  rejoin() {
    this.link = 'connecting';
    this.firstTry = Date.now();
    this.connect();
  }

  // ---- messages ----------------------------------------------------------------------

  private onMessage(m: any) {
    switch (m.t) {
      case 'welcome':
        if (m.token) store.set('token', m.token);
        this.pairCode = null;
        this.deny = null;
        this.host = m.host; store.set('host', m.host);
        this.conn = m.conn;
        this.via = m.via === 'usb' ? 'usb' : 'wifi';
        this.name = m.name; store.set('name', m.name);
        this.link = 'ok';
        this.everConnected = true;
        this.held.clear();   // a fresh socket released everything; the fingers re-claim as they lift
        this.startPing();
        break;
      case 'denied':
        this.deny = m.why;
        if (m.why === 'forgotten') store.del('token');
        this.pairCode = null;
        this.link = 'expired';
        clearInterval(this.pingTimer);
        break;
      case 'kicked':
        this.link = 'left';
        this.showToast('Disconnected in Nota');
        break;
      case 'pong': {
        this.lastPong = Date.now();
        const ms = Math.max(0, Math.round(performance.now() - m.c));
        this.rtts.push(ms);
        if (this.rtts.length > 5) this.rtts.shift();
        const avg = Math.round(this.rtts.reduce((a, b) => a + b, 0) / this.rtts.length);
        this.rtt = avg;
        if (this.link === 'ok' || this.link === 'weak') this.link = avg > 50 ? 'weak' : 'ok';
        break;
      }
      case 'proj': {
        const first = !this.proj;
        const prevTrack = this.proj?.you;
        delete m.t;
        this.proj = m;
        store.set('track', String(m.you));
        store.set('follow', m.follow ? '1' : '0');
        if (first || prevTrack !== m.you) this.onTrackChanged(first);
        break;
      }
      case 'tp':
        delete m.t;
        this.tp = m;
        this.posBase = m.pos;
        this.posAt = performance.now();
        break;
      case 'lit': this.lit = m.n; break;
      case 'who': this.who = m.p; break;
      case 'mix': delete m.t; this.mix = m; break;
      case 'xy': delete m.t; this.xy = m; break;
      case 'mac': delete m.t; this.mac = m; break;
      case 'ses': delete m.t; this.ses = m; break;
      case 'learn':
        this.learn = { on: m.on, pend: m.pend };
        if (!m.on) this.learned = null;
        break;
      case 'learned':
        this.learned = `${m.src} → ${m.dst}`;
        clearTimeout(this.learnedTimer);
        this.learnedTimer = window.setTimeout(() => (this.learned = null), 6000);
        break;
      case 'toast': this.showToast(m.msg); break;
    }
  }

  /** A new track: open the screen that suits it (once, unless the person picked one). */
  private onTrackChanged(first: boolean) {
    const t = this.track;
    if (!t) return;
    if (first && this.screenChosen) {
      this.sub();
      return;
    }
    if (t.kind === 'audio') this.bigTransport = true;
    if (!first || !this.screenChosen) {
      if (t.kind === 'drum' && (this.screen === 'Keys' || this.screen === 'Pads')) this.screen = 'Pads';
      else if (t.kind === 'inst' && (this.screen === 'Pads' || this.screen === 'Keys')) this.screen = 'Keys';
    }
    this.sub();
  }

  setScreen(s: Screen) {
    this.screen = s;
    this.screenChosen = true;
    this.bigTransport = false;
    this.sub();
  }

  sub() {
    this.raw({ t: 'sub', s: this.screen, i: this.macroDevice });
  }

  macroDevice = 0;

  selectTrack(id: number) {
    this.sheet = null;
    if (this.proj && this.proj.you !== id) {
      this.proj = { ...this.proj, you: id, follow: false };
      this.onTrackChanged(false);
    }
    this.send({ t: 'track', id });
  }

  stepTrack(dir: number) {
    const list = this.playable;
    if (list.length === 0) return;
    const i = list.findIndex((t) => t.id === this.proj?.you);
    const n = list[(i + dir + list.length) % list.length];
    this.selectTrack(n.id);
  }

  // ---- sending -------------------------------------------------------------------------

  private raw(m: object): boolean {
    if (this.ws?.readyState !== WebSocket.OPEN) return false;
    this.ws.send(JSON.stringify(m));
    return true;
  }

  /** Send when the link is up; while it is down nothing is queued (a late note is worse than none). */
  send(m: object): boolean {
    if (this.link === 'ok' || this.link === 'weak') return this.raw(m);
    return false;
  }

  get live() { return this.link === 'ok' || this.link === 'weak'; }

  noteOn(p: number, v: number) {
    if (p < 0 || p > 127) return;
    if (this.send({ t: 'on', p, v: Math.round(v * 1000) / 1000 })) this.held.add(p);
    if (this.prefs.haptics && navigator.vibrate) { try { navigator.vibrate(8); } catch { /* */ } }
  }

  noteOff(p: number) {
    if (p < 0 || p > 127) return;
    if (this.send({ t: 'off', p })) this.held.delete(p);
  }

  /** Let go of everything (the page hid, the phone left): no note outlives the fingers. */
  releaseHeld() {
    for (const p of [...this.held]) this.noteOff(p);
  }

  /** The transport position now, interpolated between Nota's messages while playing. */
  position(now = performance.now()): number {
    if (!this.tp.play) return this.tp.pos;
    return this.posBase + ((now - this.posAt) / 60000) * this.tp.bpm;
  }

  record() {
    if (this.tp.rec) { this.send({ t: 'rec', on: false }); return; }
    if (!this.tp.play && this.prefs.countIn) this.runCountIn();
    else this.send({ t: 'rec', on: true });
  }

  private countTimer = 0;
  private runCountIn() {
    if (this.countIn !== null) { clearInterval(this.countTimer); this.countIn = null; return; }
    const beat = 60000 / Math.max(20, this.tp.bpm);
    this.countIn = 4;
    this.countTimer = window.setInterval(() => {
      if (this.countIn === null) return;
      if (this.countIn <= 1) {
        clearInterval(this.countTimer);
        this.countIn = null;
        this.send({ t: 'rec', on: true });
      } else this.countIn = this.countIn - 1;
    }, beat);
  }

  rename(name: string) {
    name = name.trim();
    if (!name || name === this.name) return;
    this.name = name;
    store.set('name', name);
    this.send({ t: 'name', s: name });
  }

  setFollow(on: boolean) {
    this.send({ t: 'follow', on });
    if (this.proj) this.proj = { ...this.proj, follow: on };
  }

  showToast(msg: string) {
    this.toast = msg;
    clearTimeout(this.toastTimer);
    this.toastTimer = window.setTimeout(() => (this.toast = null), 2600);
  }
}

export const app = new App();
