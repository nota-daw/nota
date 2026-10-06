// SPDX-License-Identifier: AGPL-3.0-only
// What the phone itself offers: its form factor, keeping the screen awake, and tilt.

export type Form = 'phone' | 'phoneLand' | 'tablet';

class Device {
  w = $state(window.innerWidth);
  h = $state(window.innerHeight);
  /** Opened from the Home Screen: already full-screen, no browser bars to hide. */
  readonly standalone = matchMedia('(display-mode: standalone)').matches || (navigator as any).standalone === true;
  fullscreen = $state(!!fullscreenElement());

  constructor() {
    const upd = () => { this.w = window.innerWidth; this.h = window.innerHeight; };
    window.addEventListener('resize', upd);
    window.addEventListener('orientationchange', () => setTimeout(upd, 120));
    const fs = () => (this.fullscreen = !!fullscreenElement());
    document.addEventListener('fullscreenchange', fs);
    document.addEventListener('webkitfullscreenchange', fs);
  }

  get form(): Form {
    if (Math.min(this.w, this.h) >= 600) return 'tablet';
    return this.w > this.h ? 'phoneLand' : 'phone';
  }
}

export const device = new Device();

// ---- full screen --------------------------------------------------------------------
// A page opened in the browser can hide the browser's bars where the Fullscreen API exists
// (Android, iPad). Safari on iPhone has no page full screen: there "Add to Home Screen" is the way.

function fullscreenElement(): Element | null {
  const d = document as any;
  return d.fullscreenElement ?? d.webkitFullscreenElement ?? null;
}

export function canFullscreen(): boolean {
  const d = document as any;
  return !!(d.fullscreenEnabled || d.webkitFullscreenEnabled);
}

/** Enter or leave full screen. False when the browser can't (iPhone Safari). */
export async function toggleFullscreen(): Promise<boolean> {
  const d = document as any, el = document.documentElement as any;
  if (!canFullscreen()) return false;
  try {
    if (fullscreenElement()) await (d.exitFullscreen ?? d.webkitExitFullscreen).call(d);
    else await (el.requestFullscreen ?? el.webkitRequestFullscreen).call(el, { navigationUI: 'hide' });
    return true;
  } catch { return false; }
}

// ---- keep awake ---------------------------------------------------------------------
// The Wake Lock API needs a secure page; over plain LAN HTTP the classic fallback is a tiny
// silent looping video, which mobile browsers treat as playback and so keep the screen on.

let lock: { release(): Promise<void> } | null = null;
let video: HTMLVideoElement | null = null;

export async function keepAwake() {
  const wl = (navigator as any).wakeLock;
  if (wl && window.isSecureContext) {
    try {
      if (!lock) lock = await wl.request('screen');
      (lock as any)?.addEventListener?.('release', () => (lock = null));
      return;
    } catch { /* fall through to the video */ }
  }
  if (!video) {
    video = document.createElement('video');
    video.setAttribute('playsinline', '');
    video.setAttribute('muted', '');
    video.muted = true;
    video.loop = true;
    video.src = './keepawake.mp4';
    video.style.cssText = 'position:fixed;width:1px;height:1px;opacity:0;pointer-events:none;left:-10px;top:-10px';
    document.body.appendChild(video);
  }
  if (video.paused) video.play().catch(() => {});
}

// ---- tilt -----------------------------------------------------------------------------

export type TiltSupport = 'yes' | 'ask' | 'insecure' | 'no';

export function tiltSupport(): TiltSupport {
  if (typeof DeviceOrientationEvent === 'undefined') return 'no';
  if (!window.isSecureContext) return 'insecure';
  return typeof (DeviceOrientationEvent as any).requestPermission === 'function' ? 'ask' : 'yes';
}

/** Ask iOS for motion access (must run inside a tap). True when tilt can be used. */
export async function requestTilt(): Promise<boolean> {
  const s = tiltSupport();
  if (s === 'yes') return true;
  if (s !== 'ask') return false;
  try { return (await (DeviceOrientationEvent as any).requestPermission()) === 'granted'; } catch { return false; }
}

/** Follow the phone's tilt from where it is held now: ±30° left/right → x 0..1, ±25°
 * towards / away → y 0..1. Returns the stop function. */
export function followTilt(cb: (x: number, y: number) => void): () => void {
  let base: { b: number; g: number } | null = null;
  const clamp = (v: number) => Math.min(1, Math.max(0, v));
  const on = (e: DeviceOrientationEvent) => {
    if (e.beta == null || e.gamma == null) return;
    const land = Math.abs((screen.orientation?.angle ?? (window as any).orientation ?? 0) % 180) === 90;
    const angle = screen.orientation?.angle ?? (window as any).orientation ?? 0;
    // In landscape the axes swap: tilting "left/right" on screen is the device's beta.
    let lr = land ? (angle === 90 || angle === -270 ? e.beta : -e.beta) : e.gamma;
    let fb = land ? (angle === 90 || angle === -270 ? -e.gamma : e.gamma) : e.beta;
    if (!base) base = { b: fb, g: lr };
    cb(clamp(0.5 + (lr - base.g) / 60), clamp(0.5 - (fb - base.b) / 50));
  };
  window.addEventListener('deviceorientation', on);
  return () => window.removeEventListener('deviceorientation', on);
}
