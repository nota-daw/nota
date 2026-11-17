// SPDX-License-Identifier: AGPL-3.0-only
// Note names (Nota's convention: middle C, MIDI 60, is C4), keys and chords.

export const NN = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
const FLAT = ['C', 'Db', 'D', 'Eb', 'E', 'F', 'F#', 'G', 'Ab', 'A', 'Bb', 'B'];
const MAJOR = [0, 2, 4, 5, 7, 9, 11];
const MINOR = [0, 2, 3, 5, 7, 8, 10];

export const noteName = (m: number) => NN[((m % 12) + 12) % 12] + (Math.floor(m / 12) - 1);

export interface Key { root: number; minor: boolean }

export const keyName = (k: Key) => (k.minor ? NN : FLAT)[k.root] + (k.minor ? ' minor' : ' major');
export const scaleOf = (k: Key) => (k.minor ? MINOR : MAJOR);

/** The MIDI note of scale degree <paramref name="deg"/> (0 = the tonic at <paramref name="base"/>'s octave). */
export function degreeNote(k: Key, base: number, deg: number): number {
  const iv = scaleOf(k);
  const o = Math.floor(deg / 7), d = ((deg % 7) + 7) % 7;
  return base + k.root + iv[d] + 12 * o;
}

/** A finger's sideways slide as a pitch bend in semitones: <paramref name="unit"/> pixels is
 * <paramref name="semis"/> semitones (a key or pad width is its pitch step). A fifth of a semitone
 * either side stays in tune, so a wobbling finger doesn't detune the note, and a full unit still
 * lands on the step. At most two octaves either way. */
export function slideBend(dx: number, unit: number, semis: number): number {
  const DEAD = 0.2;
  const s = (dx / unit) * semis;
  const m = Math.max(0, Math.abs(s) - DEAD) * (semis / (semis - DEAD));
  return Math.max(-24, Math.min(24, Math.sign(s) * m));
}

const ROMAN = ['i', 'ii', 'iii', 'iv', 'v', 'vi', 'vii'];

export interface Chord { roman: string; name: string; notes: number[] }

/** The seven diatonic triads of a key, voiced from <paramref name="base"/>. */
export function chords(k: Key, base: number): Chord[] {
  const iv = scaleOf(k);
  return iv.map((_, d) => {
    const a = iv[d], b = iv[(d + 2) % 7] + (d + 2 >= 7 ? 12 : 0), c = iv[(d + 4) % 7] + (d + 4 >= 7 ? 12 : 0);
    const third = b - a, fifth = c - a;
    const q = third === 4 ? '' : fifth === 6 ? '°' : 'm';
    const root = base + k.root + a;
    return {
      roman: (q === '' ? ROMAN[d].toUpperCase() : ROMAN[d]) + (q === '°' ? '°' : ''),
      name: (k.minor ? NN : FLAT)[(k.root + a) % 12] + q,
      notes: [root, base + k.root + b, base + k.root + c],
    };
  });
}
