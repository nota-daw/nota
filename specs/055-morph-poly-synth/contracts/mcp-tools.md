# Contract: MCP surface (specs/055)

FR-054: at least what other built-ins have. Generic tools already cover Twin once kind 18 is in
`InstrumentTools`' kind list: `list_instrument_kinds`, `add_instrument_track` /
`set_track_instrument` (kind 18), `get_instrument_params`, `set_instrument_param`,
`set_instrument_param_by_id` (ids per [data-model.md](../data-model.md)), preset load.

Twin-specific tools (shaped like `read_pendulum` / `set_pendulum`, `get_rhythm` /
`set_rhythm_pattern`):

| Tool | Input | Output / effect |
|------|-------|-----------------|
| `read_twin` | track id, part (`lo` default / `up`) | layout (Single / Layer / Split), split key, voice share, the part the sequencer plays; for the part read: morph, voice mode, edited-side-independent summary of A and B for the main controls, used matrix routes `(slot, src, dst, amtA, amtB)` by name |
| `set_twin_morph_side` | track, part, side, `{id: value}` | sets params on one side; one undo step |
| `copy_twin_side` | track, part, `a_to_b` / `b_to_a` / `swap` | one undo step |
| `copy_twin_part` | track, `lower_to_upper` / `upper_to_lower` / `swap` | the whole part (both sides, matrix, FX, voice settings); one undo step |
| `set_twin_route` | track, part, slot (or `next_free`), src name, dst name, amtA, amtB | errors "matrix full" when no slot is free |
| `get_twin_pattern` | track, pattern | steps as text rows (`C3 E3 G3:100:50%`, `.` rest, `~` tie) + motion lanes |
| `set_twin_pattern` | track, pattern, rows | replaces the pattern; one undo step |
| `twin_pattern_to_clip` | track, pattern, start bar | creates the MIDI clip (+ automation for motion lanes), returns clip id |

One MCP call = one undo step (specs/016 rule).
