// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Note repeat: a held note sounds again every step of its rate, one roll per note however
// many fingers hold it. Free of the DOM and the app, so the timing can be tested with a clock
// of its own (test/roll.test.ts).

/** The time and timers a roll runs on: the browser's, or a test's. */
export interface Clock {
  now(): number;
  set(fn: () => void, ms: number): number;
  clear(id: number): void;
}

export const browserClock: Clock = {
  now: () => performance.now(),
  set: (fn, ms) => window.setTimeout(fn, ms),
  clear: (id) => window.clearTimeout(id),
};

/** A repeating note. `last` is when its latest hit was due, so the next lands one step of the
 * current rate after it; `next` is when that is. */
export interface Roll { vel: number; rate: number; bend: number; last: number; next: number; timer: number }

export class Rolls {
  readonly #map = new Map<number, Roll>();
  readonly #stepMs: (rate: number) => number;
  readonly #hit: (note: number, roll: Roll) => void;
  readonly #clock: Clock;

  /** `stepMs` turns a rate into the time between hits; `hit` sounds the note again. */
  constructor(stepMs: (rate: number) => number, hit: (note: number, roll: Roll) => void, clock: Clock = browserClock) {
    this.#stepMs = stepMs;
    this.#hit = hit;
    this.#clock = clock;
  }

  get(note: number): Roll | undefined { return this.#map.get(note); }
  has(note: number): boolean { return this.#map.has(note); }

  /** Start a note repeating from now, its first repeat one step away. A note that already
   * repeats keeps its roll: a second finger must not stack a second chain. */
  start(note: number, vel: number, rate: number): Roll {
    let r = this.#map.get(note);
    if (r) return r;
    r = { vel, rate, bend: 0, last: this.#clock.now(), next: 0, timer: 0 };
    this.#map.set(note, r);
    this.#arm(note, r);
    return r;
  }

  /** Change a repeating note's rate. The next hit re-arms one step of the new rate after the
   * last, so the new rate takes over without a gap; if that moment has passed, it goes now —
   * once. */
  setRate(note: number, rate: number): void {
    const r = this.#map.get(note);
    if (!r || r.rate === rate) return;
    r.rate = rate;
    this.#arm(note, r);
  }

  stop(note: number): void {
    const r = this.#map.get(note);
    if (!r) return;
    this.#clock.clear(r.timer);
    this.#map.delete(note);
  }

  clear(): void {
    for (const r of this.#map.values()) this.#clock.clear(r.timer);
    this.#map.clear();
  }

  #arm(note: number, r: Roll) {
    this.#clock.clear(r.timer);
    r.next = r.last + this.#stepMs(r.rate);
    r.timer = this.#clock.set(() => this.#fire(note), Math.max(0, r.next - this.#clock.now()));
  }

  #fire(note: number) {
    const r = this.#map.get(note);
    if (!r) return;
    this.#hit(note, r);
    // Keep to the grid of due times; a timer the browser held back far (a background tab)
    // starts afresh from now rather than catching up with a burst.
    const now = this.#clock.now();
    r.last = now - r.next < this.#stepMs(r.rate) / 2 ? r.next : now;
    this.#arm(note, r);
  }
}
