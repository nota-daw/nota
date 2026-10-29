# Implementation Plan: Mosaic and Drum Rack in Instrument Rack chains

**Branch**: `feat/rack-nested-instruments` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/041-rack-nested-instruments/spec.md`

## Summary

A rack chain's instrument is built by `RackCore::makeInstrument`, which stops at kind 16, so
every path that adds a chain instrument by kind (Browser drop, card menus, MCP, the rack
blob) refuses Mosaic (17) and the Drum Rack (4). The plan:

1. **Engine — chain kinds.** `makeInstrument` learns 17 (an empty Mosaic) and 4 (an empty
   Drum Rack). The rack blob already stores a built-in chain instrument as kind + its
   `getState()`, and both instruments' state is complete (Mosaic: params + program text;
   Drum Rack: its own rack blob, Sampler audio included), so persistence, presets and
   chain duplication follow. The one-level rule is enforced in the engine: a Drum Rack is
   accepted only in a chain of a track's plain Instrument Rack, refused in Drum Rack pads,
   nested racks and effect/MIDI racks, and not built when a blob would nest deeper.
2. **Engine — nested rack address.** A nested Drum Rack is addressed like any other rack
   by `(trackId, deviceIndex)` with a new range: `deviceIndex = kNestedRackBase − chain`
   (`kNestedRackBase = −100000`), "the rack in chain `chain` of the track's Instrument
   Rack". `rackCoreAt` resolves it, `rackForEdit` forks the outer rack and then the inner
   one (one graph republish = one undo step) — so every existing `rack*` engine call and
   every `nota_rackdev_*` ABI call works on a nested rack unchanged.
3. **ABI — the missing `rackdev` twins.** The Drum Rack card talks to its rack through
   `nota_rack_*` (device index −1 implied). The calls it needs that have no
   `nota_rackdev_*` twin get one (pad shaping, kit timing, chain name, Sampler pads, chain
   instrument telemetry/defaults/ids). Mosaic in a chain gets four calls addressed by
   `(track, device_index, chain)`: set program (live or undoable), program, status, zone
   sample id. `findSampleById` also looks inside chains (Mosaic zones, nested pads).
4. **Managed — proxies, not new cards.** The chain-instrument editor already hosts a
   built-in instrument's real editor through `RackChainEngineProxy` (a `DispatchProxy`
   over `IAudioEngine`). It gains the four Mosaic calls, so the Mosaic card runs on a
   chain as it does on a track. A new `NestedRackEngineProxy` redirects every
   `Rack*(track, …)` call to `RackDev*(track, nestedIndex, …)` by name (plus the plugin
   param / instrument-kind / automation calls), so the existing Drum Rack card is built
   unchanged inside a `DeviceWindow` for the nested rack. Both proxies move to
   `Nota.Application` so the MCP server can apply a Mosaic preset to a chain too.
5. **UI menus and drops.** Add Nota Mosaic and Nota Drum Rack to the Instrument Rack's
   add-chain and chain-instrument menus, Mosaic to the Drum Rack pad menu; drops already
   route by kind and start working once the engine accepts them; the nested card hides
   swing/humanize and MIDI Learn on its macros.
   Audio Effect and MIDI Effect Rack chains never hold an instrument: `rackAddChain` ignores
   the kind asked for there. MCP `add_effect_rack_chain` used to pass 4 by default, which meant
   "no instrument" while kind 4 built nothing; now that 4 builds a Drum Rack, the engine drops
   the kind for those racks and the tool's default is −1 (found by the full smoke run).
6. **Tests, docs.** Smoke-test sections for both chain kinds; CHANGELOG, FEATURES,
   nota-docs (EN + RU).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the rack edits keep the
  existing pattern — rack-internal snapshots published on the message thread
  (`SnapshotHistory`), fork + graph republish for undo (AR-5). Mosaic loads on its own
  loader threads and publishes through an atomic pointer (AR-6).
- **UI work** → [`DESIGN.md`](../../DESIGN.md): no new visual element; the two cards are
  reused as they are.
- **A built-in instrument** → `nota-instrument` skill: the kind registration checklist
  is already done for Mosaic (track instrument); only the rack factory missed it.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (app, MCP, smoke test)

**Primary Dependencies**: none new.

**Storage**: project files and presets — no format change: the rack blob (v9) already
carries "kind + instrument state" per chain. Older builds reading a blob with a kind-17
or kind-4 chain leave that chain without an instrument, the normal forward-compat story
for a new chain kind. No blob version bump.

**Audio-Thread Impact**: A nested Drum Rack renders inside its chain like any chain
instrument (its own `scratch_` buffer, its own snapshot `RenderScope`); a Mosaic in a
chain renders lock-free as on a track. Structural edits are message-thread snapshot
swaps; no new audio-thread allocation, lock or I/O.

**Testing**: `tests/Nota.SmokeTest` (end to end through the C ABI), `scripts/build.sh`.
UI checked with the headless Avalonia harness.

**Target Platform**: macOS / Linux / Windows desktop.

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**: no extra per-block cost for racks without these chains.

**Constraints**: one nesting level (spec FR-003).

**Scale/Scope**: ~4 engine files, 1 ABI file + header, P/Invoke + `IAudioEngine` +
`NotaEngine`, 3–4 UI files, MCP rack/mosaic tools, smoke test, docs.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time safety | Pass — reuses the rack snapshot + fork pattern; Mosaic loads off the audio thread. |
| II. Boundary discipline | Pass — new calls are plain C functions in `nota_engine.h`; no JUCE. |
| III. Licensing | Pass — no dependency; new files carry the SPDX header. |
| IV. Verification gate | Pass — smoke-test sections planned (FR-014). |
| V. One logical change | Pass — two chain instrument kinds and the nested-rack address they need. |
| VI. Docs in sync | Planned — CHANGELOG, FEATURES, nota-docs. |
| VII. Spec first | Pass — this spec/plan/tasks. |
| VIII. Rules not weakened | Pass. |

Re-check after design: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/041-rack-nested-instruments/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/c-abi.md
└── tasks.md
```

### Source Code

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # NOTA_RACK_NESTED_BASE, rackdev twins, chain Mosaic calls
├── nota_exports.txt / nota_exports.map   # export lists for the new symbols
└── src/
    ├── devices/racks/RackCore.{h,cpp}    # makeInstrument 4/17, nesting guard, replaceChainInstrument
    ├── engine/Engine.h                   # kNestedRackBase, new rack* methods
    ├── engine/Engine_Rack.cpp            # rackCoreAt / rackForEdit for nested racks, chain Mosaic
    ├── engine/Engine_Tracks.cpp          # findSampleById looks into chains
    └── abi/nota_engine_rack.cpp          # new ABI wrappers

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs          # RackDev* twins, RackChainMosaic*
├── Nota.Application/Engine/RackEngineProxies.cs      # chain-instrument + nested-rack proxies
├── Nota.Infrastructure/Interop/NativeMethods.Rack.cs, NotaEngine.Rack.cs
├── Nota.App/DeviceCards/Rack/RackCardView.cs         # menus, nested Drum Rack window, proxy use
├── Nota.App/DeviceCards/Rack/RackCard.Chains.cs      # chain instrument menu
├── Nota.App/DeviceCards/Rack/RackCardView.Drum.cs    # pad menu + nested mode (no swing / learn)
└── Nota.Mcp/Tools/{RackTools,MosaicTools}.cs

tests/Nota.SmokeTest/Program.cs
CHANGELOG.md, FEATURES.md, ../nota-docs (rack pages EN + RU)
```

**Structure Decision**: extend the existing rack code paths; no new projects.

## Complexity Tracking

None.
