# Data model: Mosaic and Drum Rack in Instrument Rack chains

## Chain instrument kinds (`RackCore::makeInstrument`)

| Kind | Instrument | Where allowed as a chain instrument |
|---|---|---|
| 0, 1, 2, 5–16 | built-in synths, Sampler | every rack with instrument chains (unchanged) |
| 17 | Nota Mosaic (empty program) | Instrument Rack chains, Drum Rack pads (track or nested) |
| 4 | Nota Drum Rack (empty kit) | chains of a track's plain Instrument Rack only |
| 3 | Instrument Rack | never |
| −1 | hosted plug-in | unchanged |

## Rack blob (v9, unchanged layout)

A chain stores `instKind` then `u32 len + getState()` of its instrument:

- kind 17 → Mosaic state: parameters + program text (sample paths as written by the
  program; the audio is never in the blob).
- kind 4 → the nested Drum Rack's own complete rack blob (`NRK1`, v9), pads' Sampler
  audio included.

Deserialization depth: the outermost blob is depth 0; a rack instrument (kind 3/4) is
built only at depth 0 (so its chains are depth 1 and take no rack).

## Rack address `(trackId, deviceIndex)`

| deviceIndex | Rack |
|---|---|
| −1 | the track's instrument rack (Instrument / Drum Rack) |
| ≥ 0 | the Audio Effect Rack at that device position |
| −1000 − mi | the MIDI Effect Rack at MIDI effect index mi |
| −100000 − c (new) | the Drum Rack held by chain c of the track's Instrument Rack |

Validity: resolves to null (calls no-op / return defaults) when chain c is gone or holds
no rack — an open window on a removed nested rack goes inert.

## Undo

- Nested structural edits: outer fork + inner fork, one `pushUndo`.
- Chain Mosaic program (undoable): rack fork + chain instrument replaced with a clone
  holding the new program, one `pushUndo`.
