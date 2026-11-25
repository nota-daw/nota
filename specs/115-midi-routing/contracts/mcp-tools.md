# Contract: MCP tools (FR-023)

Every mutating tool is one undo step and reports errors as `ArgumentException` text, like
the existing tools. Port uids come from `list_midi_devices` (inputs) or `list_midi_outputs`.

## Changed

| Tool | Change |
|---|---|
| `set_track_midi_input(trackId, uid?, channel = 0, inputs?)` | New optional `inputs: [{ uid, channels: int[] }]` (channels 1–16, at least one). When given, `uid` and `channel` must be omitted. `inputs: []` = All inputs. Old calls behave as before |
| `get_track_midi_input(trackId)` | Adds `inputs: [{ uid, name, channels, availability }]` (empty = All inputs); the old fields stay |
| `list_midi_devices` | Adds `overLimit: bool` per input (#1011) |

## New

| Tool | Arguments | Result / rules |
|---|---|---|
| `list_midi_outputs` | — | `[{ uid, name }]` |
| `set_external_midi` | `trackId`, `outputUid?` \| `role?` (name or id), `channel` (1–16 or `"keep"`) | The track's instrument must be External MIDI (add one with `add_instrument_track` and the External MIDI kind from `list_instrument_kinds`). Exactly one of `outputUid` / `role` |
| `get_external_midi` | `trackId` | `{ outputUid?, outputName?, role?, channel \| "keep", availability, dropped }` |
| `list_midi_output_roles` | — | `[{ id, name, outputUid?, outputName?, availability, usedBy: [trackName] }]` |
| `create_midi_output_role` | `name`, `outputUid?` | `{ id }`; the name must be unique |
| `rename_midi_output_role` | `role`, `name` | — |
| `rebind_midi_output_role` | `role`, `outputUid`, `preview = false` | With `preview: true`, returns the affected track names and changes nothing. Otherwise it is refused while transport plays (FR-015) |
| `delete_midi_output_role` | `role` | Refused while any track uses it (the message lists them) |
| `get_midi_routes` | `source?`, `destination?` | Rows as [data-model](../data-model.md) `MidiRouteRow`; read-only (FR-021) |

`list_instrument_kinds` lists External MIDI, and `add_instrument_track` takes its kind like
any built-in.
