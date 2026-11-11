// SPDX-License-Identifier: AGPL-3.0-only
// Numbers as the desktop prints them (DESIGN.md § Numbers, NotaNum): a true minus (U+2212),
// a thin space before the unit, fixed precision so a value's width never jumps (the page
// draws figures tabular). One place, so every screen writes −3.0 dB, 72 % and 122.00 alike.

export const MINUS = '\u2212';
export const THIN = '\u2009';

/** A number with a true minus sign. */
export const num = (v: number, digits = 0) => (v < 0 ? MINUS : '') + Math.abs(v).toFixed(digits);

/** Linear gain as dB, one decimal: "+1.5", "0.0", "−6.0", "−∞". A plus only where it matters. */
export function db(gain: number): string {
  if (gain < 1e-4) return MINUS + '∞';
  const d = 20 * Math.log10(gain);
  if (Math.abs(d) < 0.05) return '0.0';
  return (d > 0 ? '+' : MINUS) + Math.abs(d).toFixed(1);
}

/** 0..1 as a whole percent with its unit: "72 %". */
export const pct = (v: number) => `${Math.round(v * 100)}${THIN}%`;

/** Tempo, two decimals: "122.00". */
export const bpm = (v: number) => v.toFixed(2);

/** A value with its unit after a thin space: "12 ms". */
export const unit = (v: string | number, u: string) => `${v}${THIN}${u}`;

/** A length in beats as [number, unit]: whole bars where it divides, else beats. */
export function bars(beats: number): [number, string] {
  const b = beats / 4;
  return b === Math.round(b) ? [b, b === 1 ? 'bar' : 'bars'] : [Math.round(beats * 100) / 100, beats === 1 ? 'beat' : 'beats'];
}

/** A pan position: "C", "L20", "R50" (as Nota's mixer writes it). */
export const pan = (p: number) => (Math.abs(p) < 0.01 ? 'C' : (p < 0 ? 'L' : 'R') + Math.round(Math.abs(p) * 50));
