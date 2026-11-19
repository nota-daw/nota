# Contract: UI and MCP

## Clip inspector — Follow action section head

`[Follow action] ……… [state text] [switch]`

| Global Follow | Clip bypass | Switch | State text | Section |
|---|---|---|---|---|
| on | off | on (accent) | — | normal |
| on | on | off | "Bypassed" | inactive, editable |
| off | off | on, dim | "Off globally" | inactive, editable |
| off | on | off | "Bypassed" | inactive, editable |

Click on the switch toggles the clip's bypass (one undo step). Tooltip: "Follow actions for this
clip: off holds the clip without changing its settings".

## Session grid slot

A filled slot with `FollowBypass` and (A or B ≠ None) draws the Bypass glyph (≈9 px, secondary
ink) left of its length readout. Nothing else changes.

## Slot context menu (filled slot)

Item after Rename: "Bypass follow actions" (bypass off) / "Restore follow actions" (bypass on),
Follow glyph.

## MCP

- `get_session` → each clip gains `"followBypass": bool`.
- `set_session_clip(..., followBypass: bool? = null)` → sets only the bypass when given.
