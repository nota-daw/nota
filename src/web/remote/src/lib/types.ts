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

export interface Project {
  host: string;
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
