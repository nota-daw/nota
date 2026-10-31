# Implementation Plan: Preset name and "modified" state

**Branch**: `feat/preset-state` | **Spec**: [spec.md](spec.md) | **Issue**: nota-daw/nota#157

## Summary

Each engine device carries a **preset tag**: an index into an append-only engine table of
{id, name, loaded values}. Index 0 = no preset (Init). Because the tag is a small integer on the
device object, it rides the existing value-gesture undo (a new mix kind, `PresetTag`), and it is
copied wherever the engine clones a device (copy/paste, duplicate, move to track, rack grouping).
"Modified" is computed on demand: the device's current parameter values (the CV base where a
parameter is modulated) against the tag's loaded values, leaving view parameters out.

## Technical context

- Engine (C++20): `Device`, `MidiDevice` and `Instrument` get `presetTag`; `Engine_Presets.cpp`
  holds the table, `presetModified` and a self-test; `MixKind::PresetTag = 6` in `mixValue` /
  `applyMix`; clone paths copy the tag.
- C ABI: `nota_track_device_preset_tag`, `nota_track_device_set_preset_tag`,
  `nota_preset_tag_info`, `nota_track_device_preset_modified`, `nota_engine_preset_tag_selftest`
  (`NOTA_MIX_PRESET_TAG`). Surfaces: 0 audio effect, 1 instrument, 2 MIDI effect.
- Managed: `IAudioEngine` tag methods; `PresetEdit.Apply` wraps a preset apply plus the tag in
  one `MixEdit.Step` over every parameter and the tag; `PresetEdit.Tag` for presets landed from
  the Browser (a new device or track, already inside the insert's undo group).
- App: the device card header reads the tag (the card's own cache follows it) and shows
  "Name \*" with a tooltip while modified, checked a few times a second from the live tick.
  Save preset… starts from the tag's name and retags the device.
- Persistence: `PresetTagDto { Id, Name, Loaded }` on `InstrumentDto`, `DeviceDto`,
  `MidiDeviceDto`; format v26, no-op migration.
- MCP: `read_preset_state`; `apply_factory_preset_in_place` tags.

## Constitution check

- Audio thread: untouched. Tags are authoring state read and written on the control thread;
  the modified check runs on the UI thread through the ABI.
- C ABI only, no new dependencies, AGPL headers on new files.

## Out of scope / follow-ups

- US4: rack chain devices (rack cards apply presets through their own proxies).
- Grain / Mosaic sources loaded by a preset are not part of the undo step (as before).
