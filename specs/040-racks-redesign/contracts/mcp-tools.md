# Contract: MCP tools (`src/managed/Nota.Mcp/Tools`)

New `MidiRackTools.cs`, at parity with `RackTools` / `EffectRackTools`. All tools take
`trackId` and `midiIndex` (the MIDI effect index on the track) unless noted, run on the
engine dispatch, refresh the UI, and return the snapshot or a short result like the
existing rack tools. Writes are one undo step each.

| Tool | Arguments | Result |
|---|---|---|
| `get_midi_rack` | — | `{ mode, modeName, selector (0–127), merge, thru, chains: [{ index, name, mute, solo, keyLo, keyHi, velLo, velHi, selLo, selHi, transpose, velScale, devices: [{ index, kind, name, bypassed }] }] }` |
| `add_midi_rack_chain` | `name?` | new chain index |
| `remove_midi_rack_chain` | `chain` | ok |
| `duplicate_midi_rack_chain` | `chain` | new chain index |
| `set_midi_rack_chain_name` | `chain, name` | ok |
| `add_midi_rack_chain_device` | `chain, kind (0–5)` | device index |
| `remove_midi_rack_chain_device` | `chain, device` | ok |
| `move_midi_rack_chain_device` | `chain, from, to` | ok |
| `get_midi_rack_chain_params` | `chain, device` | `[{ index, name, min, max, value }]` |
| `set_midi_rack_chain_param` | `chain, device, param, value` | ok |
| `set_midi_rack_chain_bypass` | `chain, device, bypassed` | ok |
| `set_midi_rack_chain_mix` | `chain, mute?, solo?, transpose?, velScale?` | ok |
| `set_midi_rack_chain_zone` | `chain, keyLo?, keyHi?, velLo?, velHi?, selLo?, selHi?` | ok (accepts note names, e.g. "C3") |
| `set_midi_rack_mode` | `mode: "parallel" \| "select"` | ok |
| `set_midi_rack_selector` | `value 0–127` | ok |
| `set_midi_rack_output` | `merge?, thru?` | ok |
| `get_midi_rack_macros` | — | `[{ index, name, value, curve: "lin"\|"exp"\|"log"\|"s"\|"mixed", targets }]` |
| `set_midi_rack_macro` | `macro, value?, name?` | ok |
| `list_midi_rack_mappings` | — | `[{ index, macro, chain, device, param, min, max, curve }]` |
| `add_midi_rack_mapping` | `macro, chain, device (−3 = selector), param, min, max` | mapping index (moves an existing mapping of the same param) |
| `remove_midi_rack_mapping` | `index` | ok |
| `set_midi_rack_mapping` | `index, min?, max?, curve?` | ok |

Changes to existing tools:

- `list_midi_fx_kinds` lists kind 6 "Nota MIDI Effect Rack"; `add_midi_fx` accepts it.
- `set_rack_glide` description: audible only with mono on; new `set_rack_mono(trackId, on)`.
- `get_rack` / `get_effect_rack` gain `mono` (Instrument) and per-macro `curve` incl.
  `"mixed"`; new `set_macro_curve(trackId, deviceIndex, macro, curve)` and
  `set_mapping_range(…)` for Instrument/Audio racks so the Macros tab is fully scriptable.
- `add_macro_mapping` / `add_effect_rack_mapping` accept `deviceIndex = −3` (selector,
  Audio Effect Rack) and move an existing mapping of the same param.

Instrument Rack chain MIDI effects (issue #329), in `RackTools`:

| Tool | Arguments | Result |
|---|---|---|
| `add_rack_chain_midi_fx` | `trackId, chain, kind (0–5)` | index in the chain's MIDI section |
| `remove_rack_chain_midi_fx` | `trackId, chain, index` | ok |
| `move_rack_chain_midi_fx` | `trackId, chain, from, to` | ok |
| `get_rack_chain_midi_fx_params` | `trackId, chain, index` | `[{ index, name, min, max, value }]` |
| `set_rack_chain_midi_fx_param` | `trackId, chain, index, param, value` | ok |
| `set_rack_chain_midi_fx_bypass` | `trackId, chain, index, bypassed` | ok |

`get_rack` lists each chain's `midiEffects: [{ index, kind, name, bypassed }]`;
`add_macro_mapping` takes `midiFx: index` as an alternative to `deviceIndex`.

AI golden tests (`tests/Nota.SmokeTest/AiGoldenTests.cs`) gain a MIDI-rack scenario.
