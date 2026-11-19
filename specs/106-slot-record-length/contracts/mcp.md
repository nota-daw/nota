# Contract: MCP Session tools (`src/managed/Nota.Mcp/Tools/SessionTools.cs`)

## New tool `set_session_slot_take_length`

| Param | Type | Notes |
|---|---|---|
| `trackId` | int | |
| `scene` | int | 0-based |
| `mode` | string | `"global"`, `"open"`, `"fixed"` |
| `bars` | int | fixed only, 0–64 |
| `beats` | int | fixed only, 0 – beats-per-bar − 1 |

Description: "Prepare how long the next take recorded into an EMPTY Session slot runs:
'global' follows the session record length, 'open' records until stopped, 'fixed' records
bars + beats of the current meter (1 beat to 64 bars). Does not change clips, tempo or the
global length."

Errors (tool error text): slot holds a clip → "slot is not empty"; unknown track/scene;
unknown mode; out of range → names the limits.

## `SessionSnapshot` addition

`PreparedSlots: PreparedSlot[]` — every **empty** slot whose mode is not Global:
`PreparedSlot(int TrackId, int Scene, string Mode, int Bars, int Beats, double Beats Resolved)`.
`RecordLengthBeats` (global) unchanged.
