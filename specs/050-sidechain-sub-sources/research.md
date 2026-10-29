# Research: Sidechain from individual pads, voices and chains

Findings from the code as of `develop` @ 89bc635, and the decisions they lead to.

## R1. How a sidechain key flows today

- A consumer stores its source track id (`Device::sidechainSourceTrackId()`, ten built-in overrides
  each with their own `scTrackId_` atomic; `Instrument::sidechainSourceTrackId()` for Nota Flux).
- `Engine::recomputeRouting()` (message thread, `Engine_Devices.cpp`) clears `routeSlotTrackId_[8]`
  and claims one slot per distinct source track referenced by a track device, a track instrument, a
  keyed rack child (specs/044) or a monitoring audio track. A source not in the graph gets no slot.
- Render (`Engine_Render.cpp`): every slot has four buffers (`routeBus_`/`routeBusNext_` post,
  `routeBusPre_`/`routeBusPreNext_` pre). A source track adds its pre-FX signal into
  `routeBusPreNext_` (step 1b) and its post-fader signal into `routeBusNext_`; consumers read the
  previous block's `routeBus_`/`routeBusPre_` in `feedDeviceKey` / the instrument path; buffers swap
  at block end. One block of key latency, order-independent, parallel-safe (a slot is written by
  exactly one source track).
- A muted / solo-gated source track keeps rendering while it owns a slot (`keying`), with its
  post-fader tap silent while muted.

**Decision**: keep this design. A route slot's identity becomes *(track id, part)* instead of a
track id; part slots are filled from inside the instrument's render, using the same Next buffers
and the same swap.

## R2. Where the per-part signal exists

- **Drum Rack** (`DrumRack::render`): per pad `scratch_` ← instrument; inserts process in place; then
  one loop applies decay VCA (`env`) and `g * rv * pan` while summing into `out`.
  - Pre tap = `scratch_` right after `instrument->render`.
  - Post tap = per sample `l * env * gPan` (pad gain/pan, **not** rack volume `rv`) — computed in the
    same loop, so no extra pass.
- **Instrument Rack** (`RackInstrument::render`): same shape without the decay VCA; MIDI chains render
  `scratch_` in pieces, complete before the inserts run. Pre / Post as above.
- **Nota Rhythm** (`RhythmMachine::render`): 256-frame chunks; each active voice adds
  `voiceStereo * panGains` into `vbuf_[v]` (voices with FX) or straight into `mix_`; voice FX run per
  chunk; then Glue + Volume. Rhythm voices have no mute/solo.
  - Pre tap = the voice's panned output (`sl*gl, sr*gr`), written at `base + j`.
  - Post tap = `vbuf_[v]` after its FX; for a voice without FX, Post = Pre.
- Mute / solo-gated rack chains `continue` before rendering (DrumRack also drops `retrig`).

**Decision**: a tapped chain that is muted / solo-gated renders anyway (inserts included, decay VCA
kicked as usual) and feeds its taps, but skips the add into `out` and keeps its meter at 0
(FR-007a). Untapped chains keep the early `continue`.

## R3. Handing taps to the instrument without allocation or locks

The engine already hands per-block pointers to instruments right before render
(`setSidechain(ptr, frames)`, `setTransportInfo`). **Decision**: same pattern —

```text
struct PartTap { uint64_t part; float* pre; float* post; };   // part = stable id (R4)
virtual void setPartTaps(const PartTap* taps, int32_t n) {}    // Instrument; audio thread
```

`renderLeafDry` builds a stack array (≤ kMaxRoutes) by scanning the route slots owned by this track
with a part ≠ 0, passes it before `renderInstrumentRaw`, and clears it after. The instrument looks up
each chain's tap by a linear scan of ≤ 16 entries (only when `n > 0`), so a project without part keys
pays one branch per render (FR-011, SC-005). Pointers target `routeBus*Next_[slot]` — engine-owned,
preallocated, written only by this track's render ⇒ safe under the parallel leaf render.

Rejected: a persistent per-instrument tap table published from the message thread (needs its own
snapshot/RCU lifetime; the per-block hand-off has none).

## R4. Stable part identity

- Rack chains carry `RackChain::uid` (global counter, kept by rack forks for undo, used by lanes /
  CV links / CC routes via `chainByUid` — lock-free, audio-thread safe). It is **not** saved in the
  rack blob; saved cross-references into a rack use the chain index at save time (specs/044).
- Rhythm voices are fixed slots 0..7.

**Decision**: a part id is a `uint64_t`, 0 = whole track.
- Rack chain → `RackChain::uid` (base uid, *without* `instGen`: swapping a pad's sample instrument
  keeps the key, like lanes do).
- Rhythm voice v → `v + 1`.
Resolution (uid ↔ current index, name) is a virtual on `Instrument`, implemented by `RackCore`
(first level only) and `RhythmMachine`. The C ABI speaks **part indices** (current position), like
every other rack call; the engine converts to/from the stable id. Saving writes the index at save
time; loading converts it back to the freshly loaded chain's uid (R7).

Reorder / insert / delete-other keep the uid ⇒ FR-003. Deleting the chain leaves a dangling uid ⇒
`recomputeRouting` finds no such part and claims no slot ⇒ silent key; an undo restores the rack
snapshot with the same uid ⇒ the key resumes (FR-004, mirrors deleted-track behaviour).

## R5. Where the key's part lives on the consumer

Ten `Device` subclasses and `FluxSynth` each own `scTrackId_`. **Decision**: don't touch them; add a
non-virtual `std::atomic<uint64_t> scPart_{0}` with `sidechainPart()/setSidechainPart()` on the
`Device` and `Instrument` base classes. Hosted plug-ins get it for free. Every place that copies a
key (`Engine_Devices.cpp` device clone ~L698, `Engine_Rack.cpp` grouping ~L215, the device clipboard,
move-to-track's self-key clear ~L893/L1093) copies / clears the part with the track id.

## R6. Route budget (FR-007b/c)

`kMaxRoutes` 8 → 16. Memory: 16 slots × 4 buffers × `kMaxBlock` (16384) × 2 ch × 4 B ≈ 8 MiB
(today ≈ 4 MiB), allocated once in the `Engine` constructor. `routeSlotForTrack` becomes
`routeSlotFor(track, part)`. Slots fill from 0 in order, so a `routeSlotCount_` bounds every
scan to the used slots: lookups run several times per track per block, and a first benchmark
showed a scan over all 16 slots costing late blocks at 64-sample buffers with 192 tracks. With
the bound, a project without sidechains pays one load per lookup.

Slot identity is two atomics (`routeSlotTrackId_`, `routeSlotPart_`). `recomputeRouting` writes the
part before the track (release) and the audio thread reads track then part (acquire); a reroute can
at worst mis-key one block, the same tolerance today's non-atomic clear-and-reclaim already has.

Set calls return `-2` when the source needs a new slot and none is free; the source is then not set.
The picker greys out unused sources from `nota_sidechain_routes_free == 0` and
`nota_sidechain_route_in_use(track, part)`. Project load counts `-2` results and shows one notice.

## R7. Persistence

- Format **v27 → v28** (`ProjectService.CurrentFormatVersion`), no-op migration: absent fields read as
  -1 = whole track (FR-013).
- `DeviceDto.SidechainPart`, `InstrumentDto.SidechainPart` (React), `NestedSidechainDto.Part` — the
  part **index** at save time; -1 whole track. A dormant key (part gone) saves as no source.
- Load already defers key wiring until every track exists; by then racks are loaded, so the index
  resolves to the new chain's uid.
- Keyed rack children must carry the part too: `group_devices` moves a keyed Compressor into a rack
  with its key (specs/044), so `nota_rack_chain_sidechain`/`_set_sidechain` gain a part variant.

Risk: the ASIO branch (`specs/049`) may also bump the format; whoever merges second renumbers.

## R8. Undo

`nota_engine_undo` covers structural snapshots and value gestures; the sidechain source is a mutable
member of a device whose pointer survives snapshots, and no managed caller wraps it in an undo group.
**Decision**: picking a part is no more undoable than picking a track today (FR-015). Rack chain
edits remain undoable and carry the uid, which is what FR-004 relies on.

## R9. UI

Nine device bodies (Compressor, Auto Filter, Auto Gain, Auto Shift, Ceiling, Dynamic EQ, Prism,
Shutter, Vocoder), `DeviceParamControls.SidechainSelector` (plug-ins and generic cards) and
`FluxInstrumentCard` each build their own `srcIds` / `srcNames` ComboBox from the track list.

**Decision**: one shared builder, `SidechainSourcePicker` (Nota.App/DeviceCards), returning the
ComboBox plus `SourceName()` for readouts. Items: "Internal / None", then per track one row (= whole
track) and, when the track's instrument has parts, indented part rows ("Kick", "Snare"…) under it.
This is the tree of FR-009 in a single flat dropdown — selecting a part is open → pick (2 clicks,
SC-002) and it keeps every body's current layout. Items rebuild on `DropDownOpened` (part names /
order change while the card is open) and follow external changes from the existing readout tick.
Disabled rows carry the "No free routes" tooltip; a frozen track's parts are disabled. Display name
"Track · Part" + " pre"/" post" for Listen. Styling per `DESIGN.md` (the combo already uses it).
Rejected: a cascading submenu (two hovers, and Avalonia's ComboBox has no native tree).

Rack cards keep refusing sidechain for chain children (out of scope, Clarifications Q3).

## R10. Verification

The smoke test drives the engine offline through the C ABI and can read
`DeviceGainReduction(track, device)`. Test file `tests/Nota.SmokeTest/SidechainPartTests.cs`
(deterministic Drum Rack with two Sampler pads fed by a fixture WAV, per the
"compare levels with a deterministic clip" rule — synth notes drift between renders).
