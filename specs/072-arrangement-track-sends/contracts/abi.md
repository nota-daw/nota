# C ABI contract changes (`nota_engine.h`)

No new functions. Two new values on existing calls.

## Value undo

```c
#define NOTA_MIX_TRACK_SEND 8   /* a track's send level: device_index = return bus (0..3) */
```

`nota_mix_gesture_begin` accepts kind 8. The value read at begin/end is
`nota_track_get_send(track_id, device_index)`. A target with a missing track or a bus outside
0..3 is invalid (ignored; begin returns 0 if no target is valid).

## Automation

Target `5` = send level of bus `device_index` (`param_index` −1), values 0..1:

- `nota_track_add_automation_lane(e, track, 5, bus, -1)` → lane index (reused per bus).
- `nota_track_automation_lane_info` reports target 5 and `device_index` = bus.
- `nota_track_begin_automation_write / end_automation_write(e, track, 5, bus, -1, NULL, latch)`
  record / override like volume.
- Playback writes the send level read back by `nota_track_get_send`.
- `nota_engine_delete_track(s)` of a return: send lanes to that bus are removed and later
  buses' lanes renumbered, in the same undo step as the send-level remap.

## Managed mirror (`EngineContracts.cs`)

`AutomationTarget.Send = 5`, `MixKind.TrackSend = 8`, `MixTarget.Send(trackId, bus)`.

## MCP

`set_send` becomes one undo step. Automation tools accept / report target `"send"`
(bus in `device_index`).
