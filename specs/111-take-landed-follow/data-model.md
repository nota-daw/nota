# Data Model: Follow Action When a Recorded Take Lands

## LandingRule (engine, per `SessionSlot`)

| Field | Type | Values | Notes |
|---|---|---|---|
| `action` | `LandingAction` (int32) | 0 Off, 1 NextScene, 2 Scene | default Off |
| `scene` | int32 | 0 .. sceneCount−1 | Scene only; remapped on row insert/move/delete |
| `quantBeats` | double | −1 (Global), 0, 0.25, 0.5, 1, 2, 4, 8, 16, 32 | −1 = like a manual scene launch |

Validation (setter refuses → `NOTA_ERR_INVALID_ARG`): unknown track/scene of the slot; action not
0–2; Scene with target out of range; `quantBeats` not −1 and not in 0..32 (finite). Off and
NextScene store `scene = 0`. Setting an equal value is a no-op (no undo step).

Lifecycle: set by the user (one undo step) → kept while the slot holds a clip (hidden) → used each
time a fresh take into the slot lands → follows its row on scene edits → Off if its target is
deleted, dropped with its row/track.

## Landing (engine, message thread, transient)

`landedTakes_: std::vector<{trackId, scene}>` — pushed at landing sites, drained in the next
`serviceSessionFollow`. Not persisted, not undoable.

`ruleWait_: {gen, trackId, scene}` — the one rule launch still waiting; cleared when it starts,
is replaced/cancelled, or is cancelled because its take was undone.

`landingMuted_: int` — >0 while a global stop or a row delete lands takes.

Lane flags: `MidiTakeLane::fresh`, `CaptureLane::fresh` — the slot was empty when the lane started.

## Project file (`TrackDto`)

`SessionLandingRules: List<SlotLandingRuleDto>?` — absent = every rule Off.

`SlotLandingRuleDto { Scene, Action, TargetScene, QuantBeats }`.

## Managed contract

`NotaSlotLandingRule { int Action; int Scene; double QuantBeats; }` (sequential layout, matches
the C struct) + `enum SlotLandingAction { Off, NextScene, Scene }`.
