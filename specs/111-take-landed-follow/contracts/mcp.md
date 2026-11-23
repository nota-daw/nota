# Contract: MCP Session tools (`src/managed/Nota.Mcp/Tools/SessionTools.cs`)

## New tool `set_session_slot_landing_rule`

| Param | Type | Notes |
|---|---|---|
| `trackId` | int | |
| `scene` | int | 0-based slot row |
| `action` | string | `"off"`, `"next_scene"`, `"scene"` |
| `targetScene` | int | `"scene"` only, 0-based |
| `quantBeats` | double | −1 = global launch quantization (default); 0 = at once; else 0.25–32 |

Description: "Set what happens once a take recorded into a Session slot is ready to play: launch
the next scene or a chosen scene, once, on the next launch boundary (global quantization or
quantBeats). 'off' removes it. Only fresh takes into an empty slot fire it; follows the global
Follow switch."

Errors: unknown track/scene; unknown action; target scene out of range; bad quantBeats.
Filled slots are accepted (the rule belongs to the slot).

## `SessionSnapshot` addition

`LandingRules: LandingRule[]` — every slot whose rule is not Off:
`LandingRule(int TrackId, int Scene, string Action, int TargetScene, double QuantBeats)`.
