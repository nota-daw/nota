# MCP contract

## `group_devices` (DeviceTools.cs) — new

- Args: `trackId: int`, `midiFirst: int = -1`, `midiCount: int = 0`,
  `withInstrument: bool = false`, `fxFirst: int = -1`, `fxCount: int = 0`.
- Calls `nota_track_group_devices`. Returns `{ rackType: "audio" | "instrument" | "midi",
  index }` (index −1 for the instrument), or an error naming the refusal (−2…−7 of the C
  contract, in words).
- Description notes: the run must be contiguous; MIDI effects + audio effects need the
  instrument between them; automation and modulation follow the devices into the rack; inspect
  the result with `get_effect_rack` / `get_rack` / `get_midi_rack`.

## `ungroup_rack` (DeviceTools.cs) — new

- Args: `trackId: int`, `chain: "audio" | "midi" | "instrument"`, `index: int = 0` (ignored
  for "instrument").
- Calls `nota_track_ungroup_rack`. Returns `{ restored, macrosDropped, chainMixDropped }`, or
  an error for not a rack / two or more chains / a Drum Rack.

Both are one undo step (`undo` reverts them).
