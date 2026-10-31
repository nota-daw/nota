# Data Model: The instrument as a first-class device in the chain

No persisted data changes. The entities below are runtime state (engine) and UI state (managed).

## Device clipboard (engine, `Engine`)

| Field | Type | Notes |
|-------|------|-------|
| `clipDevice_` | `shared_ptr<Device>` | existing (#238): an audio effect clone |
| `clipMidi_` | `shared_ptr<MidiDevice>` | existing: a MIDI effect clone |
| `clipInstrument_` | `shared_ptr<Instrument>` | **new**: an instrument clone (`cloneInstrument`), bypass copied |

- Exactly one is set; a copy of any family resets the other two.
- `deviceClipboardKind()`: 0 empty, 1 audio effect, 2 MIDI effect, **3 instrument**.
- Paste always inserts a fresh clone, so the clipboard can be pasted again and survives a cut
  (the source track's removal) and later edits of the source.
- Not persisted; not part of undo.

## Track instrument (engine, `Track::instrument`)

- Never null on an `Instrument` track (clarification Q1). Replace goes through
  `replaceTrackInstrument` (one snapshot). Deleting it is deleting the track.
- Allowed new instruments: built-in kinds incl. **3 Instrument Rack, 4 Drum Rack** (fresh,
  empty), hosted plug-ins, a clipboard / transfer clone.

## Device transfer (engine, one call → one snapshot)

| Input | Meaning |
|-------|---------|
| `srcTrack`, `family` (0 audio effect, 1 MIDI effect, 2 instrument), `index` | the device (index ignored for the instrument) |
| `dstTrack`, `atIndex` | target; −1 = end of the matching section; ignored for the instrument |
| `keepSource` | 0 move, 1 copy (⌥-drag) |

| Output (return / flags) | Meaning |
|-------------------------|---------|
| ≥ 0 | the device's index on the target (0 for the instrument) |
| −1 | bad address / same track |
| −2 | the target can't take this family (audio / return / group / master for an instrument or MIDI effect) |
| −3 | the clone failed (a plug-in that won't load a second instance) |
| flag `sidechainCleared` | the key pointed at the target and was cleared |
| flag `linksDropped` | CV links / rack macro mappings to the device stayed behind |
| flag `sourceTrackRemoved` | the instrument moved and its track was removed |

State transitions in the published graph (move):

```text
source.devices[index]      → removed   (lanes on it → handed over; later lanes shift down)
target.devices[atIndex]    ← clone     (target lanes at/after atIndex shift up; handed-over lanes rewritten to atIndex)
instrument: target.instrument ← clone; source track removed
```

Copy (`keepSource`): the target side only; no lanes handed over.

## UI selection (managed, `DeviceChainView`)

- `_selChainKind` ∈ {Effect, Midi, **Instrument**}; `_selDeviceIndex` = −1 for the instrument
  when selected (a separate `_instrumentSelected` flag keeps "nothing selected" distinct).
- Mutually exclusive with `_rackSel` (specs/042), as today.

## Out-of-panel drag payload (managed, new `DeviceDrag`)

| Field | Type | Notes |
|-------|------|-------|
| `Current` | `(int TrackId, ChainKind Kind, int Index)?` | set while a device header drag is outside the panel |
| `IsActive` | `bool` | `Current is not null` |

- Set just before `DragDrop.DoDragDropAsync`, cleared in `finally` (as `BrowserView.CurrentDrag`).
- A target accepts when `CanTake(targetTrack, Kind)` and `targetTrack != TrackId`.
