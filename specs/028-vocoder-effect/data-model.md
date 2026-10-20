# Data Model: Nota Vocoder

Phase 1 of [plan.md](plan.md). The persisted parameter table and the scope layout are the
contract in [contracts/device-params.md](contracts/device-params.md); this file describes
the entities and their rules.

## Vocoder device

A built-in audio effect on a track's chain, `builtinKind() == 26`, `name() == "Nota Vocoder"`.

| Field | Kind | Notes |
|---|---|---|
| params[15] | normalized 0..1 floats | Order = persisted layout, **append-only** (see contract). |
| bypassed | bool | Base `Device`; power switch. |
| sidechain source | track id or −1 | Base sidechain; the carrier track (Carrier = Track). |
| sidechain tap | 0 post / 1 pre | Base sidechain; default **1 (pre)** for a new Vocoder. |
| sidechain gain | dB | Base sidechain; default 0, trims the carrier track. |

**Validation**
- Low is clamped so that `High / Low ≥ 2` (one octave) when either moves.
- Bands, Carrier, Pitch, Hold are discrete: `round(v · (steps − 1))`.
- A source equal to the device's own track or one of its ancestor groups is refused by the
  picker (R-12); the engine accepts any id, as today.

**State transitions**
- *Carrier track*: None → Track (picker / MCP; picking sets Carrier = Track) → None (picker, MCP, or source track deleted — the
  existing sidechain fallback). Undo restores the previous source.
- *Carrier*: Saw ⇄ Noise ⇄ Track, 10 ms crossfade. Track with no source passes the track through.
- *Band layout*: (Bands, Low, High, Q, Formant) change → coefficients recomputed at control
  rate; a Bands change additionally ducks 5 ms out / reset / 5 ms in.
- *Hold*: off → on freezes envelopes; on → off resumes following from the frozen values.

## Band (runtime only, not persisted)

| Field | Notes |
|---|---|
| analysis centre | `Low · (High/Low)^((i+0.5)/N)` |
| synthesis centre | analysis centre · `2^(Formant/12)`; muted if `> 0.45 · fs` |
| width | from Q |
| envelope | follower output, 0..4 |
| carrier level | follower on the synthesis band input, for the graph only |

## Modulator source (existing)

The engine's device sidechain — route slot per source track (max 8 per project), pre-FX
pre-fader or post-FX post-fader tap, previous-block buffer. Persisted in
`ProjectModel` device entries (`SidechainSource` as a track index, `SidechainTapPre`,
`SidechainGain`). No change.

## Factory preset

`Fx("vocoder", 26, "<Name>", ("<Param name>", v), …)` in `FactoryPresetCatalog.cs`;
modulator not stored (presets are params only). Six presets — see research R-11.
