# Research: Remote Macros LED display (spec 114)

## R1 — Where the frame is drawn

**Decision**: in Nota, in `Nota.Application` (no Avalonia), sent as a dot matrix.
**Why**: one implementation serves the web Remote, Nota NX, a later MCP tool and controller
screens; the desktop card views live in Avalonia controls and cannot be reused off the UI layer.
**Rejected**: drawing on each client from parameter values (every view written once per client);
rendering in the engine (C++), which would add non-audio work and a C ABI surface for pictures.

## R2 — Pages

**Decision**: the controller's pages (`FocusedDevice.Build`, edited → catalog → plug-in default).
**Why**: the phone and the hardware show the same eight knobs; user page edits apply to both;
one source of truth (clarified 2026-11-24).

## R3 — Units for drawing

**Decision**: `DisplayParams` reads built-in effect / MIDI-effect params as raw values with their
min / max, and instrument params (plug-in style, normalized) through the engine's value text
("1.20 kHz", "250 ms", "−6.0 dB", "45 %"), parsed into Hz / s / dB / fraction; when text has no
unit the normalized value maps onto a sensible range (frequency 20 Hz–20 kHz log, time 1 ms–10 s
log). **Why**: one helper covers 49 devices without mirroring every card's value mapping.

## R4 — Live data

| Source | Used by |
|--------|---------|
| `InstrumentOutput` → 64-band spectrum | every instrument's filter / tone page |
| `DeviceScope` Reverb layout (S_Rt60, history after `kScope = 11`: 1536 input dB, 1536 tail dB) | Reverb tail + measured tail |
| `DeviceGainReduction` | Compressor, Ceiling, Level, Prism, Shutter, Dynamic EQ |
| `TryGetTrackMeter` peak | moving dot on transfer curves, meters, Lens, effects without their own telemetry |
| `MidiEffectLastIn` / `LastOut` | Scale, Chord, Arp keyboards |

Other per-device scope layouts (EQ spectra, Chamber history …) are later refinements; their
displays draw from parameters plus the track level.

## R5 — Encoding

**Decision**: 4-bit RLE (run 1–16 per byte), base64 in JSON. A typical graph page is 300–900
bytes; 30 fps of a busy page ≈ 30–50 KB/s. Unchanged frames are not sent.

## R6 — Rate

The hub ticks at ~30 Hz on the UI clock; the frame renders on every tick for watched pages (≤ 2 ms
for 6 144 dots of simple primitives) and is sent only when its bytes change.
