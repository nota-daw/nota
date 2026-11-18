# Data Model: Export one Strata layer on its own

## Strata state blob (existing, version 1 — read only)

Little-endian, as written by `Strata::getState`:

| Field | Type | Notes |
|---|---|---|
| version | int32 | 1 |
| sampleRate | int32 | rate the buffers were recorded at |
| loopLen | int32 | frames in one cycle; 0 while the first pass records |
| loopBars | int32 | |
| layerCount | int32 | 0…8 |
| per layer × layerCount | float32 gainDb, int32 muted, int32 state | state: 0 Play, 1 Mute, 2 Rec |
| per layer × layerCount | float32 × loopLen × 2 | interleaved stereo PCM, index 0 = loop top |

No format change.

## Layer export result

| Outcome | When | UI |
|---|---|---|
| Written | layer exists, not recording, loop closed | file written |
| NoLoop | `loopLen <= 0` (first pass or empty/cleared) | entry disabled / status message |
| Recording | layer `state == 2` | entry disabled / status message |
| NoLayer | index ≥ layerCount (undone/cleared) or device gone | status message, nothing written |
| Invalid | blob shorter than its header says | status message, nothing written |
