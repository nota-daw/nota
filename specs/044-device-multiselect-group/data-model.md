# Data Model: Multi-select devices and group them into a rack

See [research.md](research.md) for the reasoning behind each shape.

## ParamPath (engine, R1)

The address of a parameter's owner, one rack level deep.

| Field | Type | Meaning |
|-------|------|---------|
| `host` | int32 | Track-level device: ≥ 0 audio effect index, −1 the instrument, MIDI effects in the track's existing MIDI addressing |
| `chainUid` | uint64 | 0 = `host` itself; otherwise the uid of a chain of the rack at `host` |
| `slot` | int32 | Only when `chainUid` ≠ 0: ≥ 0 the chain's audio device, −1 the chain instrument, `RackCore::kMidiDeviceBase − i` the chain's i-th MIDI effect |

Rules:
- `chainUid` ≠ 0 requires `host` to be a rack (Audio Effect, Instrument or MIDI Effect Rack).
- A path that resolves to nothing (chain or slot gone) is a no-op on the audio thread and is
  pruned by the edit that removed it (R6).
- Saved as `Chain` (chain index at save time) + `Slot`; turned back into a uid on load (R7).

### Where it is used

| Owner | Old fields | New fields |
|-------|-----------|------------|
| `AutomationLane` | `deviceIndex` (= host), `paramIndex`, `paramId` | `chainUid`, `slot` |
| `CvLink` source (Param kind) | `sourceDevice` | `sourceChainUid`, `sourceSlot` |
| `CvLink` target | `targetDevice` | `targetChainUid`, `targetSlot` |
| Arp CC route (moves into `Track`, R4) | on `MidiDevice`: `ccDestDevice`, `ccDestParam` | `Track::ccRoutes[]`: `{ arp: ParamPath-like position of the MIDI effect, target: ParamPath, param }` |
| Sidechain | `Device::sidechainSourceTrackId` (unchanged, now honoured inside rack chains) | — |

## Group shape (UI rule + engine validation)

Input: the selection's members in chain order. Output: one of

| Shape | Condition | Result |
|-------|-----------|--------|
| `AudioRack` | only audio effects, indices consecutive | `RackDevice` at the first index |
| `MidiRack` | only MIDI effects, indices consecutive | `MidiRack` at the first index |
| `InstrumentRack` | the instrument; MIDI effects (if any) consecutive and ending at the last MIDI effect; audio effects (if any) consecutive and starting at index 0 | `RackInstrument` as the track's instrument, chain 0 = MIDI → instrument → audio |
| `Refused(reason)` | gaps; MIDI + audio without the instrument; a rack among the effects; a plug-in into a MIDI Effect Rack; an instrument a chain can't hold (an Instrument Rack) | nothing changes |

Ungroup validity: a rack (any of the three types, not a Drum Rack as the track instrument) with
exactly one chain.

## Device selection (UI, R9)

| Field | Type | Meaning |
|-------|------|---------|
| `members` | ordered set of `(ChainKind, index)` | `ChainKind` ∈ Midi, Instrument (index −1), Effect |
| `anchor` | `(ChainKind, index)?` | last plain- or ⌘-clicked card |

Transitions: `Click(x)` → {x}, anchor x · `ShiftClick(x)` → range anchor..x in chain order ·
`ToggleClick(x)` → members ⊕ x, anchor x · `Clear()` (Esc, empty space, track switch, rack
selection) → {} · after an engine edit → members remapped from the returned indices.

Chain order: MIDI effects 0..m−1, then the instrument, then audio effects 0..n−1.

## Device clipboard (engine, R8)

An ordered list of entries; each entry is `{family: AudioEffect | MidiEffect | Instrument,
clone}` with full parameters and state (plug-in state, a rack's chains and macros). Copy
replaces the whole list. `clipboard_kind` keeps answering for a one-entry list as before and
returns 4 (mixed / several) otherwise; `clipboard_count` gives the length.

## Card extra (UI, R10)

`CardExtra.Wrapped`: from one `CardExtra?` to an ordered `List<CardExtra>` — the grouped
devices' A/B snapshots and preset labels, restored on Ungroup / Undo when each card's
signature matches.
