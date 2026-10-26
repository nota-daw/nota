# Research: Racks redesign and MIDI Effect Rack

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-10-26

What the code does today, and the decisions this plan takes. Each decision lists what it
was weighed against.

## Current state (findings)

- **Engine racks** (`src/native/nota.engine/src/devices/racks/`): `RackCore` holds an
  immutable `RackState` snapshot (chains + mappings) swapped through `SnapshotHistory`,
  8 macros, per-mapping range and curve, chain controls as relaxed atomics
  (`ChainControls`: gain, pan, mute, solo, keyLo/keyHi, velLo/velHi, meter, drum-pad
  fields), rack-out values (volume, glide, mode, dry/wet, pdc, chainSelect, selFollow) and
  a shared blob format at **v8**. `RackInstrument` (kind 3) and `RackDevice` (audio kind
  5) add the Instrument / Device interfaces; `DrumRack` derives from `RackInstrument`.
- **Undo for rack edits**: `Engine::rackForEdit` forks the rack (`RackCore::forkFrom`,
  children shared) and republishes the track, so the undo snapshot keeps the old rack (#236).
- **Audio Effect Rack Select mode reuses `velLo/velHi` as the chain's select range**
  (`RackDevice.cpp:73`). The MIDI rack needs both a velocity zone and a select range, so
  that reuse cannot carry over.
- **Glide is stored but does nothing** (`glide_` "stored/reserved"); the current card
  already shows a Glide slider. There is no mono mode.
- **Per-target curves** exist in the engine (`MacroMapping::curve`) and in the ABI
  (`set_mapping_curve`).
- **MIDI effects** are `MidiDevice`s (`MidiDevice.h`): `process(in, nIn, out, nOut,
  maxOut, frames, beatStart, spb, playing)`, `reset`, `timeJump`, generic float params,
  `getState/setState`, `clone`, bypass, fold. Kinds 0–5 (Arp, Chord, Scale, Length,
  Velocity, Random) built by `makeMidiDevice` (`DeviceFactory.cpp`). The track runs them in
  `Engine_Render.cpp` on a 1024-event stack scratch, then re-sorts.
- `Engine::cloneMidiDevice` already copies `getState()` blobs, so a stateful MIDI device
  survives track duplication, copy/paste and undo.
- **The ABI has no MIDI-effect state blob calls**; the project saves MIDI effects as
  kind + params (`MidiDeviceDto`), so a MIDI rack cannot be saved today.
- **Rack ABI** is two families: `nota_rack_*` (Instrument/Drum rack = the track's
  instrument) and `nota_rackdev_*` (Audio Effect Rack at an audio device index), plus
  structural calls; both resolve through `Engine::rackCoreAt(track, deviceIndex)`.
- **Managed card**: `DeviceCards/Rack/RackCardView.cs` (1806 lines) renders both racks
  through `IRackAccess` (`InstrumentRackAccess`, an effect-rack access), with a separate
  macro-map view (`InstrumentRackMacroMap`, Done button), Instrument-only and
  Audio-only layouts (`Ir*` / `Ae*` builders). `RackCardView.Drum.cs` and `MacroPanel.cs`
  serve the Drum Rack. Colours come from `NotaPalette` slots and `Wash` (no hex), but the
  labels are caps literals (`IrCap("RACK OUT")`, "GLIDE", "SPLIT").
- **MIDI effect cards**: `DeviceChainView.Midi.cs` wraps a body from
  `MidiDeviceCardFactory` in the shared `BuildCardShell` (`ChainKind.Midi`).
- **Catalog / Browser**: `BuiltinDeviceCatalog.cs` (MIDI kinds 0–5) and
  `DeviceCategories.cs` (MIDI tab: Generate, Shape).
- **MCP**: `RackTools.cs`, `EffectRackTools.cs`, `MidiFxTools.cs`.
- **Chain count**: the engine has no chain limit for Instrument / Audio racks.
- **Selector**: the Audio Effect Rack selector is set by hand or follows the input level
  (`selFollow`); it is not a macro target today. FR-018 needs a new rack-control mapping
  target (data-model: `deviceIndex = −3`).

## Decisions

### D1 — MIDI Effect Rack = `MidiDevice` + `RackCore`

- **Decision**: New `MidiRack : public MidiDevice, public RackCore` with `midiKind() == 6`,
  display name "Nota MIDI Effect Rack". `RackChain` gains
  `std::vector<std::shared_ptr<MidiDevice>> midiDevices` — used by MIDI rack chains (no
  instrument, no audio devices) and by Instrument Rack chains (issue #329, D15). Mappings
  address a chain MIDI device as `deviceIndex = kMidiDeviceBase − j` (`kMidiDeviceBase =
  −1000`) in every rack kind, so audio-device indices keep their meaning.
- **Rationale**: Reuses the snapshot swap, macros, mappings (with curves), names, blob
  format, `forkFrom` undo and the Drum/Instrument conventions; only routing is new.
- **Alternatives**: A standalone class duplicating `RackCore` (rejected: two copies of the
  macro/mapping/persistence machinery to keep in step). Templating `RackCore` on the child
  type (rejected: large churn in a shared, tested class for one new kind).

### D2 — Separate select range fields

- **Decision**: `ChainControls` gains `selLo/selHi` (0–127). The MIDI rack uses them for
  Select mode; the Audio Effect Rack keeps reading `velLo/velHi` as its select range so v8
  blobs and the existing ABI keep their meaning. The managed access layer exposes one
  "select range" call per rack kind and hides which fields back it.
- **Rationale**: Zero migration risk for existing Audio Effect Racks (FR-029).
- **Alternatives**: Migrate the Audio Effect Rack to `selLo/selHi` on load (rejected: a
  blob migration plus ABI semantics change for no user-visible gain).

### D3 — MIDI rack routing on the audio thread

- **Decision**: Per block, `MidiRack::process`:
  1. Splits the sorted input: note-ons go to each chain that passes mute/solo, key zone,
     velocity zone and (Select mode) select range; the chain set is recorded per input
     pitch (`routeMask[128]`, one bit per chain) so the matching note-off goes to the same
     chains even if zones change in between (no stuck notes, FR-026).
  2. Runs each chain's `midiDevices` in order on per-chain ping-pong buffers preallocated
     as members (`kMaxMidiChains = 16` chains × 2 × 1024 events) — no allocation (AR-6).
  3. Applies chain transpose (−48…+48, clamped to 0–127) and velocity scale (0–2; 0 drops
     the chain's note-ons), tracking each chain's sounding output pitches in a 128-bit set.
  4. Merges chain outputs: with *Merge duplicate notes* a per-pitch counter emits note-on
     only on 0→1 and note-off on 1→0; without it every event passes.
  5. *Pass through outside zones*: input notes that reach no chain are forwarded unchanged
     (their pitch is recorded so the note-off follows).
  6. Sorts the merged output (`midiEvLess`) and writes at most `maxOut`.
- **Snapshot change** (chain removed / reordered, mode or zone edit): on the first block
  that sees a new `RackState*`, the rack emits note-offs for output pitches held by chains
  that no longer exist and rebuilds `routeMask` indices by chain identity (the chain's
  `ctl` pointer), so surviving chains keep sounding.
- `reset()` / `timeJump()` fan out to every chain device and clear the rack's tables.
- **Rationale**: Mirrors how the engine already runs a MIDI chain; bounded memory; note
  bookkeeping is per pitch, independent of chain internals (an Arp's generated notes are
  tracked on its output side).
- **Alternatives**: Cutting all held notes on any edit (rejected: an edit during playback
  would chop sustained notes); unbounded chains (rejected: needs allocation on the audio
  thread or unbounded member memory).

### D4 — Mono (last note) and real glide for the Instrument Rack

- **Decision**: `RackInstrument` gains `mono_` (blob v9). With mono on, the rack keeps a
  note stack (fixed 128 array); a new note-on releases the sounding pitch in every chain
  and starts the new one; releasing the top note returns to the previous held note.
  Glide (0–1 → 0–500 ms, the current UI scale) applies in mono mode: the new note starts
  with a per-note `ExprBend` equal to *previous − new* semitones and the rack ramps it to 0
  over the glide time at block rate through `noteExpression` (the instruments smooth it per
  sample, see `NoteExpression.h`). Chains whose instrument does not support MPE
  (`supportsMpe() == false`) retrigger without the glide. Glide has no effect in poly mode;
  the Glide row says so in its tooltip. Mono runs on the notes entering the rack, before
  any chain MIDI effects (FR-032); glide applies only to chains without MIDI effects, since
  a chord or arp in front of the instrument makes the previous pitch undefined.
- **Rationale**: Makes the existing Glide control do what it says without per-instrument
  plumbing; almost every built-in synth (Synth, Volt, Aurora, Operator, Physical, Pentad,
  Keys, Mosaic) takes per-note bend.
- **Alternatives**: Dropping Glide from Rack out (rejected: it is in the design and in
  saved projects); driving each instrument's own glide parameter (rejected: kind-specific
  coupling, and several instruments have none).

### D5 — Per-target curves, macro shows "Mixed"

- **Decision** (Clarification 2026-10-26): curves stay per mapping. The UI derives the
  macro's curve: all targets equal → that curve; otherwise **Mixed**. Picking a curve calls
  `set_mapping_curve` for every target of that macro (one undo step). A target added to a
  Mixed macro gets Lin; to a uniform macro it gets that curve.
- **Alternatives**: Per-macro curve in the engine (rejected by the user: changes old projects).

### D6 — Selected chain and UI-only state stay in the view

- **Decision**: Selected chain, selected macro, active tab, right-panel tab, mapping on/off
  and probe position are view state (per rack, kept in the card's state object across
  rebuilds), not engine or project data — as today's `_splitAxis` / `_macroMapMode`.
- **Rationale**: No ABI or file-format change for pure presentation.

### D7 — One card for three racks

- **Decision**: Replace the `Ir*` / `Ae*` / macro-map builders with one `RackCard` built
  from small parts (macro column, centre tabs: Chains / Zones-or-Select / Macros; right
  panel: Chain / Rack out; status line), parameterised by a `RackKindProfile` (labels,
  which zone rows exist, routing modes, rack-out rows, which device kinds may be dropped).
  `IRackAccess` gains the calls the new parts need (select range, mode, rack-out values,
  MIDI transpose / velocity scale, mapping curve) and a third implementation,
  `MidiRackAccess`. The Drum Rack card (`RackCardView.Drum.cs`, `MacroPanel.cs`) is not
  touched beyond compile fixes.
- **Shell**: the card body sits in `BuildCardShell` (name · preset · type badge · power).
  The mockup's Bypass / Fold / A/B are not rebuilt: power switch = bypass; fold and A/B
  stay in the shell's existing places. The status line is the bottom 18 px of the body.
- **Rationale**: FR-001 (shared layout), FR-002, DESIGN.md "never build a header of your own".

### D8 — Compact device cards: curated key parameters

- **Decision** (Clarification 2026-10-26): a static table `RackKeyParams` maps
  (device family, kind) → up to three parameter indices, covering every instrument,
  audio effect and MIDI effect that can live in a rack; a kind missing from the table falls
  back to its first three parameters. While mapping, the compact card shows a scrollable
  list of all parameters. A smoke test asserts every rack-eligible kind has an entry
  whose indices are in range.
- **Alternatives**: first-three only (rejected by the user); user-chosen (rejected:
  needs new persisted state).

### D9 — Theme safety

- **Decision**: All colours from `NotaPalette` slots, `Wash`, `InkOn`, `Derived`; chain
  colours from a palette-index (`ArrangementView.TrackBrush(index)`-style lookup, read at
  render time) so they re-tint per theme; mapping marks use `NotaPalette.Teal` (a graph
  chroma allowed for modulation/mapping); selection = accent; Mute / Solo semantic fills
  with `InkOn`; resting slider fills `VizInk`; no `Opacity` (inactive = `Inactive.Set`);
  sentence-case labels via `NotaText.Label`; icons via `Glyph` (new `GlyphKind`s only if
  needed: e.g. drag handle, chevrons already exist — check before adding).
  Live theme switch is covered by `DynamicResource` / slot brushes; no brush is created
  from a `.Color` outside `Render`.
- **Verification**: `DesignTokenCheck` (`RunStudio`, `RunThemes`, `RunLabels`, `RunIcons`)
  with no new allow-list lines; headless renders of the three cards in all four themes
  (quickstart Q7).
- **Note**: `RackCardView.cs` currently passes the guards with caps literals routed through
  `IrCap`; the new card must not carry that helper over.

### D10 — Persistence, presets, MCP

- **Decision**:
  - ABI: `nota_midi_effect_get_state` / `nota_midi_effect_set_state` (generic, any MIDI
    effect) + a `nota_midirack_*` family addressed by (track, MIDI effect index), mirroring
    `nota_rackdev_*`; structural calls (add/remove/move chain, add/remove/move chain
    device, mappings) also in that family. Details: [contracts/c-abi.md](contracts/c-abi.md).
  - Project: `MidiDeviceDto.State` (blob ref, optional) — written when the state is
    non-empty; older files without it load as before. Project format version bump.
  - Presets: a MIDI rack user preset stores its state blob (same path the Audio Effect
    Rack uses for kind 5).
  - Blob **v9**: per chain `selLo, selHi, transpose, velScale` and its `midiDevices`
    (kind, params, state, bypass); rack `mono`, `merge`, `thru`. v8 and older read with
    defaults (`selLo 0, selHi 127, transpose 0, velScale 1`, mono off, merge on, thru on).
  - MCP: `midi_rack_*` tools at parity with `RackTools` / `EffectRackTools`
    ([contracts/mcp-tools.md](contracts/mcp-tools.md)); `add_midi_fx` accepts kind 6.

### D11 — Device kinds allowed per rack

- Instrument Rack chain: built-in MIDI effects (kinds 0–5, issue #329) + one instrument
  (built-in or plug-in, as today) + audio effects. A dropped MIDI effect always lands in the
  chain's MIDI section.
- Audio Effect Rack chain: audio effects (built-in or plug-in, as today).
- MIDI Effect Rack chain: built-in MIDI effects kinds 0–5 only; no nested racks (kind 6
  refused), no plug-ins. Racks are not nested in any rack kind (unchanged).
- A refused drop shows a short tooltip on the drop target.

### D12 — Chain limit

- Instrument / Audio Effect Rack: unchanged (no engine limit).
- MIDI Effect Rack: 16 chains (D3 preallocation); "+ Chain", Duplicate and drops are
  disabled with a tooltip at the limit.

### D13 — Status line

- Left: the one-line summary (FR-008). Right: empty (Clarification 2026-10-26).

### D14 — Keyboard

- Esc ends mapping while the rack card has focus or mapping is on (registered with the
  app's shortcut layer so it doesn't swallow Esc elsewhere); added to Preferences →
  Shortcuts via the `nota-shortcuts` skill.

### D15 — MIDI effects inside Instrument Rack chains (issue #329)

- **Problem**: the engine feeds an instrument one note at a time between render segments
  (`playEvents` in `Engine_Render.cpp`, used by the arrangement, Session and live paths),
  but a `MidiDevice` needs the whole block's events with offsets and the block clock.
- **As built** (supersedes the hook below): `Instrument::takesBlockMidi()` / `blockMidi(evs, n,
  frames)` / `midiTimeJump(delta)`. `playEvents` hands the block's sorted events to the rack once,
  before its usual event-by-event loop; the rack routes them to its MIDI chains (zones, a route
  per held key shared across forks), runs each chain's MIDI effects with the clock from
  `setTransport`, and its `render` then plays each MIDI chain's output into that chain's
  instrument in sub-segments at the exact samples. Expression, live input and bypass stay on the
  existing path; `allNotesOff` flushes chain MIDI effects; the loop seam calls `midiTimeJump`.
  Mono and the sustain pedal act on the chains without MIDI effects. Verified bit-exact against
  the same Arp on a track across loop seams.
- **Decision (planned)**: a new optional `Instrument` hook,
  `virtual bool rendersBlockEvents() const` + `virtual void renderBlock(float* out, int32_t
  frames, const MidiEv* evs, int n, const ExprEv* xev, int xn, double beatStart, double spb,
  bool playing, LiveHook)`. `playEvents` calls it instead of its own segment loop when the
  instrument returns true. `RackInstrument` returns true when any chain has MIDI effects
  (checked on the live snapshot). In `renderBlock` the rack, per chain: filters the block's
  events by zone (mono first, D4), runs the chain's MIDI effects with the block clock (the
  same values `applyMidiEffects` gets), then renders the chain instrument with the same
  segment loop as `playEvents` (shared helper, no copy), runs its audio effects and sums.
  Chains without MIDI effects use the same path with an empty MIDI section. Expression
  events and the live-expression hook are delivered per chain as `playEvents` does.
- `reset()` / `timeJump()` from the engine's stop / seek / loop-wrap / slot-switch calls
  fan out to chain MIDI effects through new `Instrument::midiReset()` /
  `midiTimeJump(delta)` virtuals (no-ops by default), called next to the track's own MIDI
  chain calls.
- Buffers: per-chain event scratch is a rack member (2 × 1024 events, reused chain by
  chain) — no allocation (AR-6).
- Latency: none added; offsets stay sample-accurate.
- **Alternatives**: buffering notes inside the rack and running chain MIDI effects one
  block late (rejected: adds a block of latency and breaks Arp timing); moving chain MIDI
  effects into the track's pre-pass (rejected: the zones and mono would have to run in the
  engine, outside the rack).
- Drum Rack (derived from `RackInstrument`) keeps returning false and does not show MIDI
  sections (out of scope).

## Open risks

- `RackCardView.cs` is large; the rewrite lands the new card behind the same entry points
  (`BuildInstrumentRackBody`, `BuildEffectRackBody`) to keep `DeviceChainView` stable.
- Mono/glide changes how an Instrument Rack with mono on sounds; mono defaults off, so
  existing projects are unaffected.
- The MIDI rack's routing table assumes ≤ 1024 events per block per chain, the same cap
  the track's MIDI chain already uses.
- D15 touches `playEvents`, the hot path for every instrument: the hook is one virtual call
  per block, and the perf gate (`scripts/perf-gate.sh`, spec 034) must show no regression.
- Instrument Rack chains that run MIDI effects share the per-chain scratch sequentially;
  parallel per-chain rendering stays out of scope.
