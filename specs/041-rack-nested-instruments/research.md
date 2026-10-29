# Research: Mosaic and Drum Rack in Instrument Rack chains

## R1 — How a nested rack is addressed

- **Decision**: a new device-index range, `kNestedRackBase − chain` (`kNestedRackBase =
  −100000`), meaning "the rack instrument in chain `chain` of the track's Instrument Rack".
- **Rationale**: every engine `rack*` method and every `nota_rackdev_*` ABI call already
  takes `(trackId, deviceIndex)` and resolves it in one place (`rackCoreAt`) and forks in
  one place (`rackForEdit`). A new range there makes the whole existing surface work on a
  nested rack. −100000 keeps clear of the MIDI Effect Rack range (−1000 − mi) and of
  macro-target constants (−2, −3, −1000 − j).
- **Alternatives**: a handle/ID per nested rack (state to keep valid across forks and
  undo — rejected); encoding the chain into the track id (hidden coupling — rejected);
  a full `nota_nested_*` ABI family (duplicates 70 calls — rejected).

## R2 — Undo for nested edits

- **Decision**: `rackForEdit(t, nested(c))` = `rackForEdit(t, −1)` (fork the outer rack,
  republish the track: one undo checkpoint), then fork the inner Drum Rack (chain children
  shared, mono/pedal state carried as for a track Drum Rack) and swap it into the forked
  outer's chain `c` with a rack-internal commit.
- **Rationale**: the graph snapshot on the undo stack still holds the old outer rack,
  which still holds the old inner rack — undo restores both. Voices survive because pad
  instruments are shared.
- **Alternatives**: forking only the inner rack in place (the shared outer rack would
  point at it from the undo snapshot too — no undo; rejected).

## R3 — Nesting depth

- **Decision**: one level. `rackSetChainInstrument` / `rackAddChain` accept kind 4 only
  when the address is −1 and the track instrument is a plain `RackInstrument` (not a Drum
  Rack); kind 3 is never a chain instrument. `RackCore::deserialize` keeps a thread-local
  depth and builds no rack instrument below depth 1 (a hand-made blob can't nest deeper).
- **Rationale**: covers drums + bass/keys splits; deeper nesting adds UI addressing
  complexity with no musical gain.

## R4 — Mosaic persistence inside a rack

- **Decision**: nothing new — `Mosaic::getState()` holds params + program text and the
  rack blob stores built-in chain instrument state. `deserialize` → `setState` →
  `setProgram` starts the async load.
- **Undoable program edits** in a chain clone the chain's Mosaic (the clone keeps decoded
  buffers, so a zone edit is ready at once), set the program, and swap it into a forked
  rack via a new `RackCore::replaceChainInstrument(chain, inst)` (keeps the chain's
  devices, MIDI effects, controls and mappings). Live (non-undoable) edits set the
  program in place, as on a track.
- **Samples by id** (`ReadSample` for the editor waveforms): `findSampleById` walks rack
  chains — Sampler and Mosaic chain instruments and, for a nested Drum Rack, its pads.

## R5 — The editors in the UI

- **Mosaic**: `RackChainEngineProxy` gains `MosaicSetProgram / MosaicProgram /
  TryGetMosaicStatus / MosaicZoneSampleId` → chain calls; `MosaicInstrumentCard` then runs
  unchanged in the chain device window (it already reads only via `engine.*(track)`).
- **Drum Rack**: `NestedRackEngineProxy` maps by name — `RackFoo(t, args…)` →
  `RackDevFoo(t, nested, args…)` when such a method exists (cached reflection), and
  `RackDevFoo(t, −1, …)` → `RackDevFoo(t, nested, …)`; `PluginParamGet/Set/Count/Id(t,
  −1, i)` → the nested rack's macros; `TrackInstrumentKind` → 4; automation writes →
  no-ops. A few Rack* calls whose RackDev twin has another shape (zones) are mapped
  explicitly. The Drum Rack card is built with a `DeviceCardContext` over that proxy in a
  `DeviceWindow`, so pads, pad editor, pad Sampler editor and macros all address the
  nested rack.
- **Why proxies**: two existing cards (≈2000 lines) stay single-source; a nested card
  can't drift from the track card.
- **Placement**: both proxies move to `Nota.Application` (public), so `Nota.Mcp` applies a
  Mosaic preset to a chain with the same `IFactoryPresets.ApplyInPlace(engine, …)` path.

## R6 — Kit timing and MIDI Learn in the nested card

- Swing/humanize are applied to the track's event list in `computeInstrumentMidi`, which
  only sees the track instrument; applying a nested rack's value would shift every chain.
  The nested card hides them (`E is NestedRackEngineProxy`), the engine ignores them.
- `MidiLearn.Bind(PluginParam(track, −1, m))` would learn the outer macro; the nested card
  skips MIDI Learn on its macros. Outer macros map to the nested macros (chain instrument
  params) instead.
