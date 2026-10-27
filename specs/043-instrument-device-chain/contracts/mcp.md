# MCP contract

## `set_track_instrument` (InstrumentTools.cs) — new

- Args: `trackId: int`, `kind: int` (see `list_instrument_kinds`; 3 = Instrument Rack, 4 = Drum
  Rack, both empty).
- Replaces the track's instrument; clips, MIDI effects, audio effects and mixer stay. One undo
  step. Returns true, or an error for a non-instrument track / unknown kind.
- Description notes: plug-ins use `set_track_instrument_plugin`; there is no "delete
  instrument" — an instrument track always has one, `remove_track` deletes the track.

## `move_device_to_track` (DeviceTools.cs) — new

- Args: `trackId: int`, `chain: "audio" | "midi" | "instrument"`, `index: int` (ignored for
  "instrument"), `targetTrackId: int`, `atIndex: int = -1`, `copy: bool = false`.
- Calls `nota_track_transfer_device`. Returns `{ index, sidechainCleared, linksDropped,
  sourceTrackRemoved }`, or an error naming the refusal (−1 / −2 / −3 of the C contract).
- Description notes: moving the instrument replaces the target's instrument and removes the
  source track; automation moves along, a copy has none.
