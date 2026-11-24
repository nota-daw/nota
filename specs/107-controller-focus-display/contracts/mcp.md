# Contract: MCP tools (spec 107)

## `step_controller_device`

- Params: `direction` (int, +1 next / −1 previous), `portUid` (string?, omit = lead controller).
- Effect: as the "Next / Previous device" command — focus moves along the track's chain
  (MIDI effects → instrument → audio effects, racks as one device), stops at the ends.
- Returns: `ControllerFocusInfo?` (as `get_controller_focus`) after the step; unchanged at an end.

## `get_session_selection`

- Returns `{ kind: "none" | "slot" | "scene", track: int?, scene: int? }` — the Session view's
  selection (what the controller's launch button acts on). `none` when the Session isn't showing.
