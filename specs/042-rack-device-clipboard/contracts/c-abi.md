# Contract: C ABI additions (`nota_engine.h`)

All calls run on the message thread.

**Rack address.** `device_index` addresses the rack the same way as the other `nota_rackdev_*` calls:
- −1 = the track's Instrument (or Drum) Rack;
- ≥0 = an Audio Effect Rack at that device index;
- −1000 − mi = the MIDI Effect Rack at MIDI effect index mi;
- `NOTA_RACK_NESTED_BASE` − c = the Drum Rack nested in Instrument Rack chain c.

**Undo.** Every editing call is one undo step: one rack fork.

**Clipboard.** It is the engine's existing device clipboard, the one `nota_track_copy_device`, `nota_track_paste_device`, `nota_track_copy_midi_effect` and `nota_track_paste_midi_effect` use.

```c
/* Copy chain `chain`'s audio effect `index` (or MIDI effect) to the device clipboard.
   1 = copied, 0 = no such device. */
NOTA_API int32_t nota_rackdev_chain_device_copy(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t index);
NOTA_API int32_t nota_rackdev_chain_midi_copy(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t index);

/* Paste a fresh clone of the clipboard into chain `chain`'s audio-effect (or MIDI-effect)
   section at `at_index` (−1 or past the end = at the end). A pasted audio effect loses its
   sidechain key. Returns the new index, or:
     −1 nothing to paste / bad address
     −2 the clipboard holds the other family (or this rack has no such section)
     −3 a rack inside a rack
     −5 a MIDI effect past chain 16 of an Instrument Rack */
NOTA_API int32_t nota_rackdev_chain_device_paste(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t at_index);
NOTA_API int32_t nota_rackdev_chain_midi_paste(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t at_index);

/* Duplicate a chain device right after itself (the clipboard is untouched). New index or < 0. */
NOTA_API int32_t nota_rackdev_chain_device_duplicate(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t index);
NOTA_API int32_t nota_rackdev_chain_midi_duplicate(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t index);

/* Move a chain device to the end of another chain's matching section of the same rack; its
   macro mappings follow it. New index in `to_chain`, or −1 bad address / same chain, −5 as above. */
NOTA_API int32_t nota_rackdev_chain_device_move_to(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t index, int32_t to_chain);
NOTA_API int32_t nota_rackdev_chain_midi_move_to(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t chain, int32_t index, int32_t to_chain);

/* 1 when the clipboard holds an audio effect keyed from another track (its key won't survive
   a paste into a rack chain). */
NOTA_API int32_t nota_device_clipboard_sidechain(const NotaEngine* e);
```

**Managed mirror** (`IAudioEngine`):
- `RackDevChainDeviceCopy` / `RackDevChainMidiCopy` → bool;
- `RackDevChainDevicePaste` / `RackDevChainMidiPaste` → int;
- `RackDevChainDeviceDuplicate` / `RackDevChainMidiDuplicate` → int;
- `RackDevChainDeviceMoveTo` / `RackDevChainMidiMoveTo` → int;
- `DeviceClipboardSidechain` → bool.

The Instrument Rack's `IRackAccess` passes −1, the Audio Effect Rack passes its device index, and the MIDI Effect Rack passes −1000 − mi.

**Existing calls used as they are**:
- `nota_rack_move_chain_device`, `nota_rackdev_move_chain_device`, `nota_rack_chain_midi_move`, `nota_midirack_chain_device_move` (move within a section);
- `nota_track_duplicate_device`, `nota_track_duplicate_midi_effect` (track-chain Duplicate);
- `nota_device_clipboard_kind`.
