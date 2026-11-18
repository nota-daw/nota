# Contract: Nota Remote messages and MCP tools

## Nota Remote (phone → hub, JSON over the existing WebSocket)

| Message | Fields | Effect |
|---------|--------|--------|
| `cancel` | `id` track or group id, `i` scene | cancel that slot's queued launch; a group id cancels each member's queued launch on row `i` |
| `cancelScene` | `i` scene | cancel the queued scene launch of row `i` |

Unknown ids / out-of-range rows are ignored, like `launch`. The Session message (`ses`) gains
`q` (rows with a launch waiting, including stops-only rows) and, per track, `w` (the slot whose
launch waits, also a restart of the slot playing); the phone falls back to slot state 2 when an
older Nota sends neither. The Session message the hub already
sends carries slot state 2 (queued); the phone needs no new state.

Page gesture: press and hold a queued clip, group slot or scene button for 500 ms → `cancel` /
`cancelScene`; the release after a hold sends nothing. A tap stays a launch (a queued clip tapped
again stays queued).

## MCP (`SessionTools`)

| Tool | Arguments | Result |
|------|-----------|--------|
| `cancel_slot_launch` | `trackId`, `scene` | true if a queued launch was there to cancel |
| `cancel_scene_launch` | `scene` | true if anything of that row was queued |

`get_session` already reports each filled slot's state, `"queued"` included — no new field.
