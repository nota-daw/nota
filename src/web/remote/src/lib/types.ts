// SPDX-License-Identifier: AGPL-3.0-only
// The messages Nota sends (see Nota.Remote/RemoteHub.State.cs for the writer side).

export type TrackKind = 'drum' | 'inst' | 'audio' | 'group' | 'return';

export interface Track {
  id: number;
  name: string;
  color: string;
  kind: TrackKind;
  dev: string;
  grp: number;
  /** The instrument takes per-note expression (MPE): the Keys screen offers Expressive. */
  mpe?: boolean;
  /** Drum tracks: [note, name] per pad. */
  pads?: [number, string][];
}

/** What Nota tells the phone about colour (specs/014 contracts/theme-message.md). */
export interface ThemeMessage {
  t: 'theme';
  /** The theme Nota wears (Follow Nota). */
  on: string;
  /** The user's dark-mode and light-mode picks (Follow phone). */
  dark: string;
  light: string;
  /** The dark and light ink text on a solid fill is chosen from. */
  ink: [string, string];
  /** Each theme named above, once: token → CSS colour, and a skin's typeface and corner
   * radii (spec 087; specs/090 contracts/theme-message.md) — absent means Geist and the design scale. */
  th: Record<string, ThemeEntry>;
}

export interface ThemeEntry {
  name: string;
  dark: boolean;
  c: Record<string, string>;
  /** The skin's typeface family; the phone wears it when it has it, Geist otherwise. */
  font?: string;
  /** Every corner step in px as the desktop draws it under this skin. */
  r?: Partial<Record<RadiusStep, number>>;
}

export type RadiusStep = 'card' | 'group' | 'field' | 'cell' | 'valueBar' | 'switch' | 'fine';

export interface Project {
  host: string;
  /** Whether Nota's theme is dark — only read when Nota sends no theme (an older Nota). */
  dark: boolean;
  tracks: Track[];
  key: { root: number; minor: boolean; name: string } | null;
  scenes: number;
  sections: [string, number][];
  access: 'control' | 'play';
  /** The track Nota has this phone on. */
  you: number;
  follow: boolean;
}

export interface Transport {
  play: boolean;
  rec: boolean;
  loop: boolean;
  met: boolean;
  pos: number;
  bpm: number;
  ls: number;
  le: number;
  can: boolean;
}

/** [id, volume (0..1.5), pan, mute, solo, arm, meterL, meterR, held-by name, held-by connection]. */
export type MixRow = [number, number, number, number, number, number, number, number, string, number];

export interface Mixer {
  ch: MixRow[];
  /** [volume, meterL, meterR, held-by, held-by connection]. */
  m: [number, number, number, string, number];
}

export interface XyAxis {
  n?: string;
  d?: string;
  v?: number;
  txt?: string;
  map: string | null;
  tilt: string | null;
}

export interface Xy {
  id: number;
  ok: boolean;
  auto: boolean;
  x: XyAxis;
  y: XyAxis;
}

export interface MacroKnob {
  n?: string;
  v?: number;
  txt?: string;
  a?: boolean;
  map: string | null;
}

export interface Macros {
  id: number;
  di: number;
  devs: string[];
  k: MacroKnob[];
}

/** Per scene: [state (0 empty, 1 filled, 2 queued, 3 playing, 4 recording), length beats, progress 0..1]. */
export interface SessionTrack {
  id: number;
  c: [number, number, number][];
}

export interface Session {
  n: number;
  tr: SessionTrack[];
}

export interface Learn {
  on: boolean;
  pend: string | null;
}

export type Screen = 'Pads' | 'Keys' | 'XY' | 'Mixer' | 'Macros' | 'Scenes';
export const SCREENS: Screen[] = ['Pads', 'Keys', 'XY', 'Mixer', 'Macros', 'Scenes'];

/** ok · weak (> 50 ms) · lost (was connected, retrying) · down (Nota unreachable) · expired (needs a code). */
export type LinkState = 'connecting' | 'ok' | 'weak' | 'lost' | 'down' | 'expired' | 'left';

/** One Session slot's content as the desktop slot draws it (issue 557; specs/090
 * contracts/clips-message.md): notes in 1/64 of the clip's length, or 48 peaks 0–99. */
export interface ClipPreview {
  /** Track id and scene index. */
  id: number;
  i: number;
  /** 'n' notes, 'p' peaks, 'x' the slot is empty now. */
  k: 'n' | 'p' | 'x';
  nm?: string;
  col?: string;
  len?: number;
  n?: [number, number, number][];
  p?: number[];
  rec?: boolean;
}

export interface ClipsMessage {
  t: 'clips';
  /** The first batch of a full send: forget previews not in it. */
  all: boolean;
  s: ClipPreview[];
}
