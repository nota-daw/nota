# Research: Nota Remote in the new look, with clip previews in Scenes

## R1 — What the phone is told about a skin's geometry

- **Decision**: per theme in the `theme` message, add `font` (family name, only when the skin
  names one) and `r` (the seven resolved radii in px, keyed by the theme-file step keys
  `card group field cell valueBar switch fine`, only when the skin differs from the design
  scale). Nota resolves them with `SkinGeometry.RadiusOf`, the function the desktop uses.
  `RemoteTheme` gains `string? Font` and `IReadOnlyDictionary<string, double>? Radii`.
- **Rationale**: the phone needs no copy of the roundness rule — one source of truth.
  Omitting both for built-ins keeps the message unchanged for the common case.
- **Alternatives**: send `scale` + `steps` and recompute in TypeScript (drift risk).

## R2 — Wearing a typeface the phone may not have

- **Decision**: `--font-ui: "<family>", Geist, system-ui, sans-serif`; CSS falls back to
  Geist per family and per glyph. The family is sanitised (quotes, backslashes, `;{}`
  stripped) before it goes into CSS. Nota sends no font files.
- **Alternatives**: `document.fonts.check()` detection — unreliable for local fonts on iOS.

## R3 — Radii as custom properties

- **Decision**: `app.css` defines `--r-card 9px … --r-fine 2px`; the theme layer overrides
  them from `r` and resets them when `r` is absent. Every `border-radius` in the phone's
  sources is `0`, `50%` or built from `var(--r-*)`.
- **Phone part → step** (DESIGN.md § Corner scale, by what the part is):

| Phone part | Step |
|---|---|
| Header, tabs, rail, banner, Mixer strips, covers, Big transport ground | Docked (0) |
| Big framed areas: Macros grid frame, XY field, Empty card, cover checklist, Big transport keys | Card |
| Sheet / menu, toast, segment group, `.btn`, transport keys, track and device pickers, axes panel | Group |
| Field (device name, code cells), row highlight, pad, Scenes clip cell | Field |
| Segment cell, M / S / Arm, scale cell, chord, scene button, Stop, section button, tag chips | Cell |
| Fader cap, pan bar, slots, rate chip | ValueBar |
| Switch track | Switch |
| Switch knob, meter channel, colour bars, note bars | Fine |

## R4 — Orange in three strengths on the phone

- **live** (solid `--accent` + `--ink-on-accent`): Play running, Record (record colour),
  count-in number, a looping section, pad / key / cell / chord under a finger, the Tilt pill.
- **picked** (`--accent-soft` fill + `--picked-ink`; `--picked-ink` = `--accent-bright` in
  dark themes, `--accent-pale` in light ones): current tab / rail item, segment `on`,
  `.btn.on` toggles (Full velocity, Repeat, Loop, Click, Tilt off-state stays raised), chosen
  row in sheets, chosen tonic, the open position readout.
- **values**: track colour on Macros arcs and the XY crosshair / point (clarification);
  Mixer grey at rest, a held fader / pan borrows soft tint + accent line.
- Switches in the phone's menu are settings switches: solid accent track when on, as
  DESIGN.md § Switch.
- **Track-colour tint** without `color-mix()` (target Safari 16 / Chrome 100): a layer
  filled with `var(--tc)` at a fixed opacity under the content.

## R5 — Type: one face, tabular figures, phone sizes

- **Decision**: root `font-family: var(--font-ui)`, `font-variant-numeric: tabular-nums`;
  drop `GeistMono-Medium.woff2`, the `Geist Mono` face and `.mono`. Phone sizes stay (never
  smaller than today); weights follow the scale (readouts 500, names 600, labels 400 ink 3).
- `lib/fmt.ts`: `db()`, `pct()`, `bpm()`, `bars()` with U+2212, U+2009 and fixed precision.

## R6 — Clip previews: what is sent (#557)

- **Decision**: a new message `clips` (Nota → phone) carrying, per changed slot, the clip's
  name, colour, length in beats and either `n` (note bars) or `p` (peaks). See
  [contracts/clips-message.md](contracts/clips-message.md).
  - **Notes**: `[start, len, pitch]` relative to the clip, start/len quantised to 1/64 of
    the clip length (integers 0–64) — the phone draws them at cell resolution anyway. More
    than 128 notes: merge per pitch row overlapping bars after quantisation; if still over,
    keep the first 128 by start (the shape stays readable; FR-021).
  - **Peaks**: 48 buckets of `max(|min|, |max|)` normalised 0–99 to the clip's loudest
    bucket (the desktop draws exactly this normalised magnitude; sign is not drawn).
  - Clip colour: `host.ClipColor(trackId, scene)` — the clip's own colour, else its
    track's, as `SessionView.ClipBrush` does. Name: `GetSessionClipName`, else
    "<track> <scene+1>", as `SessionView.ClipName`.
- **Rationale**: integers keep JSON small: a 64-note clip ≈ 0.8 KB, peaks ≈ 0.2 KB; a
  32 × 32 grid of mixed clips stays well under 64 KB for the first send (SC-009).
- **Alternatives**: binary frames (the hub speaks JSON text only; not worth a second
  framing); sending raw notes (unbounded).

## R7 — Clip previews: when it is sent (change detection)

- **Decision**: no engine revision counter exists for Session slots, so the hub keeps per
  slot a 64-bit signature (FNV-1a over the compact preview + name + colour + length) and
  re-reads slots **round-robin**: at most 24 slots per UI tick, only while at least one
  phone is on Scenes, so a 32 × 32 grid is fully re-checked about every 0.7 s at 60 Hz.
  A slot whose `SessionSlotState` / length changed, and every recording slot (state 4 / 5,
  re-read 4× a second), jumps the queue. A changed signature marks the slot dirty; each
  phone on Scenes gets the dirty slots it has not seen (per-phone sent-signature map);
  opening Scenes or a new connection sends all non-empty slots once, batched up to 64
  slots per message.
- **Audio thread**: `GetSessionNotes`, `GetSessionSlotPeaks`, `GetSessionClipName`,
  `AudioRecordPeaksOf` are the control-side reads the desktop Session view already makes on
  the UI thread (`Engine_Session.cpp`: `sessionSlotPeaks` → `getClipPeaks`, const, no
  audio-thread work). No new locks; nothing new crosses the C ABI.
- **Recording MIDI**: stored notes via `GetSessionNotes` while recording, the length shown
  as whole bars recorded so far (as `DrawRecordingNotes`); the desktop's held-key overlay
  (`RecordPreview`) is app-side and not available to the hub — held keys appear when they
  are stored (an accepted simplification; the take still visibly grows).
- **Recording audio**: `AudioRecordPeaksOf(trackId, …)` for the slot recording.
- **Alternatives**: an engine-side revision counter (C ABI change, out of proportion for a
  look feature); re-reading everything each tick (FR-022 forbids).

## R8 — Drawing the preview on the phone

- **Decision**: one `<canvas>` per filled cell (device-pixel-ratio aware), redrawn only
  when its preview, colour, size or state changes; notes 3 px bars with `--r-fine`-scaled
  corners, peaks centred bars 62 % of a step wide, as `DrawPeaks`. Ink: the clip colour on
  a stopped cell, `--tc-ink` on a playing / queued (filled) cell, `--record` / its ink when
  recording. The canvas reads colours from computed custom properties, so a theme change
  repaints.
- **Cell layout**: top row name (ellipsis) + length; preview area below; play mark and
  progress line on top.
- **Alternatives**: inline SVG per cell (hundreds of nodes per clip on big grids).

## R9 — Checks

- `tests/Nota.SmokeTest/DesignTokenCheck.Remote.cs`: radii `0`, `50%` or `var(--r-…)`; no
  `Geist Mono` / `monospace` / `class="mono"`; `--picked-ink` defined for both kinds.
- `tests/Nota.SmokeTest/RemoteTests.cs`: theme payload carries `font` / `r` for a skin and
  neither for a built-in; `clips` sent only to a phone on Scenes, once per change, capped,
  and not re-sent while nothing changes.
- `npm run check` clean; `npm run build` builds; `scripts/build.sh` green.
