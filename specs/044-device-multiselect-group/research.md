# Research: Multi-select devices and group them into a rack

Phase 0 of [plan.md](plan.md). Each entry: decision, rationale, alternatives.

## What exists (read before the decisions)

- **Single wrap / unwrap (#257)** — `Engine::wrapDeviceInRack` / `unwrapRack`
  (`src/native/nota.engine/src/engine/Engine_Rack.cpp`) move one audio effect *instance* into a
  new `RackDevice` chain without re-preparing it (a Reverb keeps its tail) and publish once.
  Both refuse when `deviceIsReferenced` finds an automation lane, an Arp CC lane or a CV link
  on the device, or when the device has a sidechain key: nothing outside a track's own chain
  can address a device inside a rack chain.
- **Addressing today** — `AutomationLane` (`Automation.h`) is `{target, deviceIndex,
  paramIndex, paramId}` on the track's chain only; `CvLink` (`Modulation.h`) the same for
  source and target; an Arp's CC destination is `ccDestDevice/ccDestParam` on the
  `MidiDevice` itself (outside the undo snapshot). Rack macro mappings are the only thing that
  reach chain children (`RackCore::MacroMapping {chainIndex, deviceIndex, paramIndex}`,
  `deviceIndex` −1 = chain instrument, `kMidiDeviceBase − i` = chain MIDI effect).
- **Sidechain** — `Engine_Render.cpp` hands each *track-level* keyed device (and the
  instrument) its previous-block route bus; route claims (`Engine_Devices.cpp` ~1370) look at
  track-level devices only. Rack chain children are never fed a key.
- **Rack state** — `RackCore` keeps its chains in its own immutable `RackState` snapshot
  (`live()` is read by the audio thread; edits `commit` a new one), chains have a stable
  `uid`; children take `setParam` from the audio thread already (macros do it).
- **Undo** — every structural engine edit republishes a graph (one undo step);
  `nota_engine_undo_group_begin/end` folds several into one.
- **Clipboard** — `clipDevice_` / `clipMidi_` / `clipInstrument_`: one device (specs/043).
- **UI** — `DeviceChainView.Clipboard.cs` holds one selection (`_selChainKind`,
  `_selDeviceIndex`, `_instrumentSelected`), ⌘G / ⌘⇧G call the single wrap, the card's
  per-device UI state (`CardExtra`: A/B snapshots, preset label) moves with the device, and a
  rack keeps the one wrapped device's extra in `CardExtra.Wrapped`.

## R1 — A nested parameter address (the foundation for FR-016)

**Decision**: Introduce one engine value type, `ParamPath`, used wherever something points at
a device parameter:

```text
ParamPath { int32 host;      // track-level device: >= 0 audio effect, -1 instrument,
                             // MIDI effect as today's MIDI addressing
            uint64 chainUid; // 0 = the host itself; else a chain of the rack at `host`
            int32 slot; }    // in that chain: >= 0 audio device, -1 the chain instrument,
                             // RackCore::kMidiDeviceBase − i the chain's i-th MIDI effect
```

`AutomationLane`, `CvLink` (source and target) and the Arp CC route each gain
`chainUid` + `slot` beside their existing `deviceIndex` (which becomes `host`). A shared
resolver `Engine::resolveParamOwner(Track&, host, chainUid, slot)` returns the `Device*`,
`MidiDevice*` or `Instrument*`; with `chainUid` 0 it is today's lookup. Nesting goes one rack
level deep, which is all Group can create (racks don't nest in rack chains; specs/041).

**Rationale**: The user chose that automation, CV, sidechain and Arp CC travel into the rack
(spec Clarifications). Rack macros can't stand in (only 8 per rack; an automated chain easily
has more targets, and a macro-bridge would show macros the user never made). A path keyed by
chain **uid** survives chain reorders, duplicates and deletes inside the rack without
re-indexing the track's lanes; a lane whose chain is gone resolves to nothing and is pruned by
the chain-delete path (R6).

**Alternatives**: (a) macro bridging — rejected (8-macro limit, visible side effect); (b) a
per-device uid across the engine — the cleanest long-term address but a project-wide refactor
of every index-based API, out of proportion for this feature; (c) keep refusing (#257) —
rejected by the clarification.

## R2 — Resolving on the audio thread

**Decision**: `resolveParamOwner` walks `RackCore::live()->chains` (a vector scan by uid, no
allocation, no lock) and is called from `applyAutomation`, `applyMidiCcRouting`, the CV apply
pass and the sidechain feed. Results are not cached.

**Rationale**: AR-6 — the walk only reads the published rack snapshot, exactly as the rack's
own render does; chains are few (≤ 16 MIDI / tens of audio), so the scan is cheaper than the
param set itself. A cache would have to be invalidated on every rack edit.

**Alternatives**: resolve once per graph publish into raw pointers — rejected: rack edits
commit their own snapshot without republishing the graph, so the cache would go stale.

## R3 — Group and Ungroup as single-publish engine operations

**Decision**: Replace `wrapDeviceInRack` / `unwrapRack` with general operations (the old ABI
calls become thin wrappers so nothing breaks):

- `groupDevices(trackId, midiFirst, midiCount, withInstrument, fxFirst, fxCount)` builds the
  rack off-graph, **moves** the instances in (no re-prepare, as #257 does), rewrites every
  `ParamPath` that pointed at a moved device to its nested path, shifts the indices of
  devices after the group (`remapAutomationAfterDeviceChange`, `remapCvLinksAfterDeviceChange`
  generalised to paths), and publishes once.
  - only audio effects → `RackDevice` at `fxFirst`;
  - only MIDI effects → `MidiRack` at `midiFirst`;
  - with the instrument → a `RackInstrument` whose chain 0 holds the MIDI effects, the
    instrument (a new `RackCore::setChainInstrumentInstance` moves the instance; held notes keep
    sounding) and the audio effects; it replaces the track's instrument through
    `replaceTrackInstrument` (specs/043) inside the same publish.
- `ungroupRack(trackId, family, index)` is the inverse for a one-chain rack of any of the three
  types (Drum Rack excluded), rewriting nested paths back to track-level ones.
- Validation happens before anything is built; failures return a reason code (contracts/c-abi.md).

**Rationale**: FR-020 / FR-022 / SC-006: one publish means the device is never on the track
and in the rack at once, nor missing; one publish is one undo step with no undo-group needed.

**Alternatives**: compose from add-rack + per-device moves inside an undo group — rejected:
intermediate graphs would be audible (devices missing for a block) and re-prepare would reset
tails.

## R4 — The Arp CC route moves into the snapshot

**Decision**: Move the Arp's CC destination from the `MidiDevice` (`ccDestDevice/ccDestParam`)
into the track snapshot as a route keyed by the MIDI effect's position path, with the target as
a `ParamPath`. `applyMidiCcRouting` walks track MIDI effects **and** MIDI effects inside rack
chains (a grouped Arp keeps driving its target). The existing set/get ABI keeps its signature
and writes the snapshot; projects load the old per-Arp field and migrate it.

**Rationale**: `unwrapRack` already refuses because a CC destination outside the snapshot can't
be undone; Group and Ungroup must rewrite it and undo must restore it.

**Alternatives**: clone the Arp on every rewrite — rejected: resets its running pattern and
breaks "the track sounds the same" (FR-013).

## R5 — Sidechain keys inside rack chains

**Decision**: Rack chain children keep their own `sidechainSourceTrackId` (it already lives on
the device). The render's sidechain feed and the route-bus claims walk rack chains (audio effect
racks' devices, Instrument Rack chains' audio effects and instrument) via the R2 resolver walk.
The rack device itself gains no key.

**Rationale**: the key is previous-block route-bus data, so nesting adds no ordering
constraint; feeding per child keeps two grouped devices with different keys working.

**Alternatives**: one key per rack forwarded to all children — rejected: breaks a group of
two devices keyed from different tracks.

## R6 — Chain edits and dangling paths

**Decision**: Deleting a chain, deleting / moving a device inside a chain (specs/042), or
deleting / replacing the rack prunes or rewrites the track's lanes, CV links and CC routes that
point into it, in the same publish (one helper, `remapPathsAfterRackEdit`). Undo restores them
with the snapshot.

**Rationale**: once lanes can point into a rack, every rack edit that removes or moves a child
must keep them consistent, or a lane would silently drive the wrong parameter.

## R7 — Persistence

**Decision**: Lane and CV DTOs in `ProjectService` gain optional `Chain` (chain **index** at
save time) and `Slot`; absent means track-level, so old projects load unchanged. On load, after
the devices are rebuilt, the index is turned into the chain's uid. Bump the project format
version. The Arp CC route DTO follows R4.

**Rationale**: chain uids are runtime ids (regenerated on load); the index is stable within a
saved file.

## R8 — The multi-device clipboard and batch edits

**Decision**: The engine clipboard becomes an ordered list of entries (family + clone), filled
by `copyDevices(track, set)` and inserted by `pasteDevices(track, midiAt, fxAt)` in one
publish: MIDI effects at `midiAt`, the instrument replacing the target's (specs/043), audio
effects at `fxAt`; entries the target can't take are skipped and counted. Single-device copy /
paste ABI stays as a one-entry wrapper. Rack-chain paste (specs/042) takes the entries that fit
the chain's sections, instrument skipped.
Delete, Cut, Duplicate, drag-move within a section and Move to track ▸ for a selection run the
existing per-device engine calls inside one `undo_group` (highest index first so indices stay
valid); the instrument in a Move goes last (it removes the source track).

**Rationale**: paste must be one publish for FR-005 (the instrument swap and effect inserts
together); the other batch edits only remove or reorder, and every intermediate graph is a
valid chain, so an undo group gives one step without new engine surface.

## R9 — Selection model in the UI

**Decision**: A small `DeviceSelection` class (Nota.App) replaces `_selChainKind` /
`_selDeviceIndex` / `_instrumentSelected`: an ordered set of `(ChainKind, index)` plus an
anchor, with `Click`, `ShiftClick`, `ToggleClick`, `Clear`, chain-order ranges
(MIDI → instrument → audio) and a `GroupShape()` query that returns the rack type or the
refusal reason (non-contiguous, MIDI + audio without instrument). Indices are remapped after
each edit from the engine's return values. The rack-chain selection (`_rackSel`, specs/042)
stays single and still clears the track selection.

**Rationale**: keeps the contiguity / rack-type rules in one testable place (the smoke test
can drive it headless) and out of the view code.

## R10 — Card UI state across Group / Ungroup

**Decision**: `CardExtra.Wrapped` becomes a list: on Group the rack's extra keeps the grouped
devices' extras in order; on Ungroup (or Undo) they come back to the restored cards when their
signature matches, as the single-device case does today.

**Rationale**: FR-013 (A/B snapshots and preset labels survive a round trip).

## R11 — ⌘G routing and menus

**Decision**: Keep the existing focus rule: ⌘G / ⌘⇧G in the Devices panel act on devices,
elsewhere on tracks. The device menu's **Group into rack** / **Ungroup rack** items move into
every card's menu (MIDI effects, instrument, audio effects) and are enabled from
`GroupShape()` / the rack's chain count.

## As built

R4 changed: the Arp's CC target stays on the Arp (nested part added) and is rewritten on a
clone, as the device-removal path already did — see tasks.md § Implementation notes. R9: the
selection model is in `Nota.Presentation`. Everything else as above.

## Open risks

- R4 touches Arp persistence; the migration needs a fixture project with an Arp CC lane.
- R1 widens three structs that are copied with every track snapshot; their size stays small
  (two extra fields each).
- Writing new automation from a chain device's knob (arming a lane from inside a rack) stays
  out of scope: lanes that came in through Group play, show and edit in the Arrangement, but
  the rack card's knobs don't start new lanes.
