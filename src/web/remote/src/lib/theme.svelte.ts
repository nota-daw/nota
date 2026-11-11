// SPDX-License-Identifier: AGPL-3.0-only
// What the phone wears (spec 014). Nota sends the theme it wears and the user's dark-mode and
// light-mode picks with all their colours, and a skin's typeface and corner radii (spec 090); Follow Nota wears the first, Follow phone one of
// the picks by the phone's own scheme. The colours go onto the root as custom properties
// named after the tokens (palette.ts), so a theme change is one pass over ~110 properties and
// nothing re-renders. The last one worn is stored for the head script in index.html, which
// paints a returning phone in it before any of this loads; a new phone starts on the
// built-in fallback (theme-plugin.ts).

import { cssVars, geometryVars, inkOn, type Colors, type Ink } from './palette';
import type { RadiusStep, ThemeMessage } from './types';

const MSG = 'nota-remote:theme', VARS = 'nota-remote:vars';
const scheme = matchMedia('(prefers-color-scheme: dark)');

function stored(): ThemeMessage | null {
  try {
    const m = JSON.parse(localStorage.getItem(MSG) ?? 'null');
    return m && m.t === 'theme' && m.th?.[m.on] ? m : null;
  } catch { return null; }
}

export type Pref = 'nota' | 'phone';
interface Worn { id: string; name: string; dark: boolean; c: Colors; font?: string; r?: Partial<Record<RadiusStep, number>> }

class Look {
  /** The last theme Nota sent (or the stored one, until Nota answers). */
  msg = $state<ThemeMessage | null>(stored());
  /** The phone's own colour scheme, live. */
  phoneDark = $state(scheme.matches);
  /** An older Nota says only dark or light: the fallback's two sides. */
  legacyDark = $state<boolean | null>(null);

  constructor() {
    scheme.addEventListener('change', (e) => (this.phoneDark = e.matches));
  }

  receive(m: ThemeMessage) {
    this.msg = m;
    try { localStorage.setItem(MSG, JSON.stringify(m)); } catch { /* private mode */ }
  }

  /** The theme a preference wears now, or null for the fallback. */
  worn(pref: Pref): Worn | null {
    const m = this.msg;
    if (!m) return null;
    const id = pref === 'phone' ? (this.phoneDark ? m.dark : m.light) : m.on;
    const t = m.th[id] ?? m.th[m.on];
    return t ? { id, ...t } : null;
  }

  /** The ink pair: Nota's, else the fallback's (theme-plugin.ts writes it as --ink-dark / --ink-light). */
  get ink(): Ink {
    if (this.msg) return this.msg.ink;
    const s = getComputedStyle(document.documentElement);
    return [s.getPropertyValue('--ink-dark').trim(), s.getPropertyValue('--ink-light').trim()];
  }

  /** Readable ink on a fill the page computes — a track's colour. */
  inkOn(fill: string): string { return inkOn(fill, this.ink); }

  /** Put the preference's theme on the page. */
  apply(pref: Pref) {
    const root = document.documentElement;
    const t = this.worn(pref);
    const dark = t ? t.dark : this.legacyDark !== false;
    root.dataset.theme = dark ? 'dark' : 'light';
    if (t && this.msg) {
      const vars = { ...cssVars(t.c, this.msg.ink), ...geometryVars(t.font, t.r) };
      for (const [k, v] of Object.entries(vars)) root.style.setProperty(k, v);
      try { localStorage.setItem(VARS, JSON.stringify({ dark, v: vars })); } catch { /* private mode */ }
    }
    const ground = getComputedStyle(root).getPropertyValue('--bg-app').trim();
    if (ground) document.querySelector('meta[name=theme-color]')?.setAttribute('content', ground);
  }
}

export const look = new Look();
