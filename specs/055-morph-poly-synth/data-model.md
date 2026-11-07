# Data model: Nota Twin (specs/055)

All sound data are normalized plugin-params (0..1, stable string ids, append-only order). Only
the sequencer patterns live in the state blob. Display names read `<Part> <Side> · <Group> <Leaf>`
("Lower A · Ladder Cutoff", "Upper · Morph", "Patch Mode"): the automation menu groups params
on the part of the name before its last space, so each section becomes one submenu. See [research.md](research.md) R2, R9, R10.

## Patch (the instrument)

| Field | Param id | Values | Notes |
|-------|----------|--------|-------|
| Mode | `mode` | Single / Layer / Split | stepped; changing releases, never cuts |
| Split key | `splitkey` | MIDI 0..127 | Split only; first key of Upper |
| Voice share | `share` | Lower 2..10 (step 2) | Upper = 12 − Lower; Single → all 12 to Lower |
| Max voices | `voices` | 4..12 | CPU cap (FR-001) |
| Expression pad | `padx`, `pady`, `padz` | 0..1 | side- and part-less, automatable (FR-031) |
| Bend range | `bendrange` | ±2 / ±5 / ±12 / ±24 | MPE controllers' announced range wins per note |
| Edit side / part | — | view state | card only, not a param, not saved in presets |
| Parts | `lo.*`, `up.*` | Part | Upper is present but silent in Single |
| Sequencer settings | `seq.*` | see Pattern | global |
| Patterns | state blob | Pattern × 8 | structural, R10 |

## Part (`lo` / `up`)

Side-less per part: `morph`, `voicemode` (Poly / Mono / Legato / Unison), `glide`,
`glidelegato`, `octave` (Split transpose), `vol`, `pan`, `fxon` (whole section bypass), the 64
matrix slots' `mN.src` / `mN.dst`, 8 macro values `macroN` and macro target slots.

Per side (`<part>.a.*` and `<part>.b.*`) — the morphed sound:

| Group | Params |
|-------|--------|
| Osc 1 / Osc 2 | `oNwave`, `oNpw`, `oNmetal`, `oNcoarse`, `oNfine`, `oNlevel`, `oNbal` (filter balance) |
| Osc links | `sync` (stepped), `fm21` (Osc 2 → Osc 1), `fmfilt` (osc → multimode cutoff), `o2lo` (stepped, low-freq / kbd-off) |
| Noise | `noiselevel`, `noisecolor`, `noisebal` |
| Multimode | `mmcut`, `mmres`, `mmmode`, `mmkey`, `mmenv` |
| Ladder | `ldcut`, `ldres`, `ldslope` (stepped 12/24), `ldkey`, `ldenv` |
| Routing / drive | `route` (series ↔ parallel), `brute`, `drive` |
| Envelopes | `fenv{a,d,s,r,vel,curve}`, `aenv{a,d,s,r,vel,curve}`, `menv{dly,a,d,s,r,vel,loop}` |
| LFO 1–3 | `lfoN{shape,rate,sync,syncdiv,mode(voice/global),retrig,fade,amt}`; LFO 3 + `lfo3sym`, `lfo3curve` |
| Hard-wired | `vibamt` (LFO 1 → pitch, mod-wheel scaled); velocity → amp is the amp envelope's `aenvvel` |
| Drift / stereo | `drift`, `unidetune`, `spread`, `spreadmode` (stepped) |
| Matrix depth | `mNamt` for N = 1..64 |
| FX | `modfx{type,rate,depth,mix}`, `dly{time,sync,div,fb,pingpong,mix}`, `rev{size,damp,mix}`, `modfxon`, `dlyon`, `revon` |

**Morph rule** (R3): continuous → interpolated in the param's musical domain; stepped (marked
above) → A below 0.5, B from 0.5. Morph = 0 / 1 reproduce A / B exactly.

**Validation**: `share` snaps to even values; `splitkey` 0..127; matrix `dst` that is a stepped
param is rejected by the card (stepped params aren't destinations); a slot with `src = none`
or `dst = none` is empty.

## Modulation route (matrix slot)

`(src, dst, amtA, amtB)` per slot, 64 per part. Sources (append-only table): none, LFO 1–3,
filter env, amp env, mod env, velocity, release velocity, key, pressure (per note, falls back
to channel AT), slide, per-note bend, mod wheel, pitch wheel, pad X / Y / Z, random-per-note,
macro 1–8, sequencer gate / step value. Destinations (append-only): every continuous voice or
part param of the table above, plus `morph` (evaluated per voice when the source is per-note).

## Voice expression state (audio thread only)

Per voice: note, velocity, release velocity, pressure, slide, bend, random seed, the part it
belongs to, its own morph value (part morph + per-voice matrix contribution, clamped 0..1).

## Pattern (state blob, 8 per patch)

| Field | Range |
|-------|-------|
| length | 1..64 steps |
| step[i].notes | 0..6 MIDI notes (0 = rest) |
| step[i].velocity | 1..127 |
| step[i].gate | 1..100 % of step, or tie |
| motion[0..3].target | param index or none |
| motion[k].mode | stepped / smooth |
| motion[k].value[i] | 0..1 or "unset" (no override on that step) |

Global sequencer params: `seq.on`, `seq.kind` (Seq / Arp), `seq.pattern` (1..8, switches at
pattern end), `seq.rate`, `seq.swing`, `seq.latch`, `seq.transpose` (on/off), `seq.target`
(Lower / Upper / Both), `arp.mode`, `arp.oct`, `arp.gate`, `arp.hold`.

**State transitions** (sequencer): `Stopped` → (transport rolls ∧ `seq.on`) → `Running` →
(transport stops ∨ `seq.on` off) → `Stopped` (releases its notes). A transport jump while
`Running` → re-align at next step (releases sounding notes). Pattern switch request → `Pending`
→ applied at the current pattern's end.

## State blob layout

`[u32 magic "TWN1"] [u32 paramCount] [paramCount × f32 LE] [u32 patternBytes] [pattern data]`
— `setState` tolerates shorter / longer param arrays and a missing pattern block (FR-052).
The preset `StateBase64` carries only the pattern block (params travel as `NamedParams`, R11).

## Factory preset

`Inst("twin", 18, name, params…, TwinSeq(...)?)` in `FactoryPresetCatalog`, folder, macro
names; ≥ 40 (US6), ≥ 8 with sequencer / arp, ≥ 6 Split / Layer (SC-011).
