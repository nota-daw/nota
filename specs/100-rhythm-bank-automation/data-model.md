# Data Model: Automate the Nota Rhythm pattern bank

## Nota Rhythm params (appended; ids never change)

| Index | Id | Name (automation menu) | Values | Default |
|------:|----|------------------------|--------|---------|
| 93 | `bank` | Perform Bank | A, B, C, D — normalized `b / 3` (read back with `round(v · 3)`) | A (0) |
| 94 | `bank_switch` | Perform Bank Switch | Step (< 0.5), Pattern (≥ 0.5) | Step (0) |

`pluginParamText` returns `A`…`D` and `Step` / `Pattern` for them.

## State blob

`RTH4` (`0x34485452`): `[magic][95 × f32][bank u8, voice u8, 0, 0][on, vel, acc × 4 × 8 × 16][src × 8]`
then the tagged `RFX1` / `RMC1` blocks as before. The bank byte equals `round(bank · 3)`.

| Magic | Params | Bank comes from |
|-------|-------:|-----------------|
| RTH1 | 60 | the bank byte |
| RTH2 | 85 | the bank byte |
| RTH3 | 93 | the bank byte |
| RTH4 | 95 | the `bank` param |

## Runtime (audio thread)

- **Playing bank** — the bank the last step played; reported in scope `S_Bank`. Follows the
  `bank` param while stopped.
- **Lane steps** — `ParamLaneSteps`: first step index, count (≤ 32) and the lane's value at each
  step's grid beat for the current block; cleared after render.

## Nota Twin

No data change. `GSeqPattern` is read through its own `ParamLaneSteps` at step onsets while
the transport plays.
