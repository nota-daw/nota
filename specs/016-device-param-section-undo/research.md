# Research: Undo for Device Parameters and Arrangement Sections

## R1 — Device parameters ride on the specs/009 value steps

**Decision**: extend the existing mix gesture (`MixKind` / `MixTarget` / `MixChange` in
`Engine.h`) with three parameter kinds — audio-effect parameter, MIDI-effect parameter,
normalized plug-in / instrument parameter — addressed by `(track, device index, parameter
index)`. Undo/redo, the group merge, the automation-recording merge, the "skip a step
whose target is gone" rule and the depth limit all apply unchanged.

**Why**: device parameters, like volume and pan, live outside the graph snapshot — a
cloned track *shares* its `Device` objects (`Engine::cloneTrack`), so restoring an older
graph never restores a parameter. A value step is the only model that fits, and specs/009
already built it with the exact gesture semantics the spec asks for.

**Alternatives**: a second, parameter-only undo stack (rejected: two histories can't keep
order, which is bug #30 itself); snapshotting the device's whole state per edit (rejected:
heavy, and hosted plug-ins' state blobs are large).

**Device index stability**: a step addresses its device by chain position. Every structural
edit that moves or removes a device is itself a step above the parameter step, so by the
time a parameter step is undone the chain is back in the layout it was recorded in (the
same argument specs/009 makes for track ids). A target that is out of range is skipped.

## R2 — Gestures come from the existing automation "touch"

**Decision**: `Engine::beginAutomationWrite` / `endAutomationWrite` — which every on-screen
parameter control already calls on press and release (88 call sites: knobs, sliders, curve
handles, graphs, and the one-shot `Begin; set; End` helpers of switches and menus) — also
open / close a value gesture for that parameter. Latched writes (hardware controls: MIDI
Learn, Nota Remote) do not; those sources use the managed `MixMovement` / per-touch
gestures of specs/009 instead. Volume / pan touches are skipped (specs/009 wires those).

**Why**: one change in the engine covers every card, including those added later, with no
per-card wiring. The touch already means "the user is moving this control".

**Alternatives**: wire a gesture next to each of the 88 call sites (rejected: churn, and
every future card must remember it); record a step on every setter call without a gesture
(rejected: preset and project loads and internal sets go through the same setters and
would flood the history — FR-006).

**Gaps found and closed**: `GenericMidiBody` knobs and `ArpGrid` steps set MIDI-effect
parameters without a touch; the device shell's A/B switch restores every parameter with
plain sets; `ValueEntry` solved a typed value by trial sets *before* opening its gesture,
which would have recorded a solver value as the "before". Each gets a gesture.

## R3 — Sections become graph-level project data

**Decision**: `Graph::sections` (start beat, length in beats, name), copied by
`inheritGraphLevel` like scenes and the pan law. `setSections(list, checkpoint)` publishes a
new graph — with a checkpoint for a user edit, without one for a load — so a section edit
is an ordinary structural snapshot step. The C ABI reads the list and replaces it whole.

**Why**: sections are a few dozen bytes; one source of truth; they are saved with the
project, visible to MCP and Nota Remote through the engine; undo needs no new machinery.

**Alternatives**: an opaque app-state marker on the engine's undo stack (rejected by the
user: two models to keep in step; sections stay invisible to MCP / Remote).

**View**: the Sections lane keeps a working copy for drawing and drags; it commits the
whole list to the engine once per edit (drag release, rename, add, duplicate, delete,
delete all) and reloads from the engine after undo / redo / load / new.

## R4 — Persistence and migration

**Decision**: `project.json` gains `sections: [{ start, length, name }]` (always written,
`[]` when empty). On load, a document without the member (an older build's) reads the
bundle's `sections.json` sidecar once; saving removes the sidecar. No format-version bump:
the member is optional and older builds keep unknown members on re-save
(`[JsonExtensionData]`).

## R5 — MCP: one call, one step

**Decision**: every MCP mutation runs inside an engine undo group, so a tool that sets
several parameters (or adds a device and sets its parameters) is one step; the single-value
parameter tools use `MixEdit.Step`. New tools `list_sections` / `set_sections`.
