# Contract: Nota Vocoder parameters and scope

Engine-facing contract for builtin kind **26**. Read through the existing C ABI
(`nota_device_param_*`, `nota_device_get_param` / `set_param`,
`nota_builtin_device_param_default`, `nota_device_scope`). No new ABI.

## Parameters

All values are normalized 0..1 at the ABI (`paramMin 0`, `paramMax 1`). Index order is the
persisted layout — **append only, never reorder**. Names are what presets (`Fx(...)`), the
automation menu and MCP show; they are sentence case.

| # | Name | Mapping (normalized → unit) | Default (unit / norm) |
|---|---|---|---|
| 0 | Bands | `8 + round(v · 24)` | 16 / 0.3333 |
| 1 | Low | exp 20 Hz – 1 kHz | 80 Hz / 0.3544 |
| 2 | High | exp 2 kHz – 20 kHz | 8 kHz / 0.6021 |
| 3 | Q | `1 + 9v` | 5.0 / 0.4444 |
| 4 | Formant | `round((v − 0.5) · 24)` st | 0 st / 0.5 |
| 5 | Attack | exp 0.5 – 100 ms | 2.0 ms / 0.2616 |
| 6 | Release | exp 5 – 1000 ms | 40 ms / 0.3925 |
| 7 | Mod gain | `(v − 0.5) · 48` dB | 0 dB / 0.5 |
| 8 | Carrier | `round(v · 2)`: 0 Saw · 1 Noise · 2 Track (the sidechain) | Saw / 0 |
| 9 | Tilt | `(v − 0.5) · 24` dB | +3 dB / 0.625 |
| 10 | Pitch | `round(v · 84)` semitones above C0 (C0–C7) | C3 / 0.4286 |
| 11 | Unvoiced | `v · 100` % | 35 % / 0.35 |
| 12 | Mix | `v · 100` %, equal-power | 100 % / 1.0 |
| 13 | Gain | `(v − 0.5) · 24` dB | 0 dB / 0.5 |
| 14 | Hold | `v ≥ 0.5` | off / 0 |

The input is the modulator (the voice). `acceptsSidechain() == true` — the sidechain is the
carrier track, used when Carrier = Track; a new instance sets `sidechainTapPre = 1`.

## Scope layout (`nota_device_scope`)

Written once per audio block; UI reads at 60 Hz, torn reads allowed. Returns 100 floats
(fewer if `max_samples` is smaller).

| Offset | Count | Meaning |
|---|---|---|
| 0 | 1 | Key (modulator after Mod gain) peak, linear |
| 1 | 2 | Output peak L, R, linear |
| 3 | 1 | Active band count N (8–32) |
| 4 | 32 | Band envelope 0..1 (display-scaled; first N valid) |
| 36 | 32 | Carrier band level 0..1 (first N valid) |
| 68 | 32 | Analysis band centre, Hz (first N valid) |

## Behaviour guarantees (tested in the smoke test)

- Param count 15; names as above; defaults as above (`nota_builtin_device_param_default`).
- Param round-trip and `cloneDevice` (duplicate track) preserve all 15 values and the
  sidechain source / tap.
- Defaults on a speech track → a robot voice (Saw), audible, finite, near the voice's level,
  following the syllables; Mod gain −20 dB → about −20 dB out.
- A silent track → silence.
- Carrier = Track with a chord track → audible; a voice 20 dB quieter vocodes within 3 dB;
  within 6 dB of the chord track alone; a muted chord track still feeds it (Pre).
- Carrier = Track with no track, or its track deleted → the output equals the bypassed track
  (after a 10 ms fade). A routed but silent carrier track → silent wet.
- Every factory preset on a speech track is audible and peaks below 0 dBFS.
- Two offline renders of the same project are sample-identical.
