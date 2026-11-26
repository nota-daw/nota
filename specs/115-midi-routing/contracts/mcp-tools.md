# Contract: MCP tools (FR-023)

Every mutating tool is one undo step and reports errors as `ArgumentException` text, like the
existing tools. Port uids come from `list_midi_devices` (inputs) or `list_midi_outputs`.

## Changed

| Tool | Change |
|---|---|
| `set_track_midi_input(trackId, uid?, channel = 0, inputs?)` | New optional `inputs: [{ uid, channels: int[] }]` (channels 1–16, at least one). When given, `uid` and `channel` must be left out. `inputs: []` = All inputs. Old calls behave as before |
| `get_track_midi_input(trackId)` | Adds `inputs: [{ uid, name, channels, availability }]` (empty = All inputs); `availability` is available, missing, over-limit or ambiguous. The old fields stay |
| `list_midi_devices` | Adds `overLimit: bool` per input (#1011) |
| `list_instrument_kinds`, `add_instrument_track` | List and take kind 19, External MIDI |

## New

| Tool | Arguments | Result / rules |
|---|---|---|
| `list_midi_outputs` | — | `[{ uid, name }]` |
| `set_external_midi` | `trackId`, `outputUid`, `channel` (`"1"`–`"16"` or `"keep"`, default `"1"`) | The track's instrument must be External MIDI. A port that isn't connected is kept and receives nothing; picking a port leaves any role |
| `set_external_midi_role` | `trackId`, `role` (id or name) | Points the track at a role; its channel stays |
| `get_external_midi` | `trackId` | `{ outputUid?, outputName?, role? (name), channel ("1"–"16" or "keep"), availability (available, missing, none), dropped }` |
| `list_midi_output_roles` | — | `[{ id, name, outputUid?, outputName?, availability, usedBy: [trackName] }]` |
| `create_midi_output_role` | `name`, `outputUid?` | The new id; the name must be unique (case-insensitive), 1–40 characters |
| `rename_midi_output_role` | `role`, `name` | — |
| `rebind_midi_output_role` | `role`, `outputUid`, `preview = false` | Returns the names of the tracks that move. With `preview: true` changes nothing. Otherwise refused while the transport plays (FR-015) |
| `delete_midi_output_role` | `role` | Refused while any track follows it (the message names them) |
| `get_midi_routes` | `source?` (port uid or `track:<id>`), `destination?` (output uid or `role:<id>`) | Rows as data-model.md `MidiRouteRow`; read-only (FR-021). A port destination also matches the tracks following a role bound to it |
