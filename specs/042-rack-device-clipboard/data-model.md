# Data Model: Move, copy and paste devices in rack chains

Nothing new is saved: no rack blob or project format change. The entities below live for the
app session.

## Device clipboard (engine, existing)

| Field | Meaning |
|---|---|
| `clipDevice_` | a full clone of a copied audio effect (built-in, hosted plug-in or rack), or empty |
| `clipMidi_` | a full clone of a copied MIDI effect, or empty |

- **Invariant**: at most one of them is set (copying one clears the other).
- **Filled by**: track `copyDevice` / `copyMidiEffect` (existing), and rack `rackChainDeviceCopy` / `rackChainMidiCopy` (new).
- **Read by**: track `pasteDevice` / `pasteMidiEffect` (existing), and rack `rackChainDevicePaste` / `rackChainMidiPaste` (new).

  Every paste clones the clipboard again, so it can be pasted many times.
- **Kind** (`nota_device_clipboard_kind`, existing): 0 empty, 1 audio effect, 2 MIDI effect.
- **Sidechain** (`nota_device_clipboard_sidechain`, new): 1 when `clipDevice_` has a sidechain source track.

## Rack chain device address

| Field | Values |
|---|---|
| rack | −1 = the track's Instrument Rack (or Drum Rack); ≥0 = Audio Effect Rack at device index; −1000 − mi = MIDI Effect Rack at MIDI effect index mi; `NOTA_RACK_NESTED_BASE` − c = the Drum Rack nested in Instrument Rack chain c (specs/041) |
| chain | 0..chainCount−1 |
| section | audio effect, or MIDI effect |
| index | position within the section |

## Rack device selection (managed, new)

`RackDeviceSel(int Rack, int Chain, bool Midi, int Index)` is held by `DeviceChainView`.

- **Exclusive** with the track-chain selection (`_selChainKind` / `_selDeviceIndex`): setting one clears the other.
- **Clearing**: cleared on `Show(track)`, on a rebuild whose rack or chain no longer has that index, and when the chosen chain changes on that rack.
- **Keys**: drives the ⌘C / ⌘X / ⌘V / ⌘D / Delete routing and the card's selected look.

## State transitions (one rack fork each = one undo step)

| Action | Rack change | Mappings |
|---|---|---|
| Move within section (from → to) | reorder | the device's follow it; between from and to shift by one (existing) |
| Move to another chain (c1, i → c2, end) | remove from c1, append to c2 | the device's rewrite to (c2, new index); c1's after i shift down |
| Paste at `at` (c) | insert clone at `at` | c's at or after `at` shift up; the clone has none |
| Duplicate (c, i) | insert clone at i+1 | as Paste at i+1 |
| Cut (c, i) | copy to the clipboard, then remove | removed with it (undo restores them) |
| Delete (c, i) | remove | removed with it (undo restores them) |

## Paste result codes (engine → UI)

| Code | Meaning | Status line |
|---|---|---|
| ≥0 | index of the pasted device | "Pasted <name>" (+ " · sidechain dropped" when the clipboard was keyed) |
| −1 | nothing on the clipboard / bad address | (Paste disabled) |
| −2 | the wrong family for this section (MIDI effect into effects, or the other way round; an audio effect into a MIDI Effect Rack) | "A <section> takes <family> only." |
| −3 | a rack inside a rack | "A rack can't go inside a rack." |
| −5 | a MIDI effect past chain 16 of an Instrument Rack | "MIDI effects run in the first 16 chains." |
