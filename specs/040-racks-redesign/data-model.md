# Data Model: Racks redesign and MIDI Effect Rack

**Spec**: [spec.md](spec.md) · **Research**: [research.md](research.md)

Engine-side data lives in `RackCore` (`src/native/nota.engine/src/devices/racks/`) and
is persisted as the rack blob; view-side data lives in the card and is never saved.
**New** marks what this feature adds.

## Rack (engine)

| Field | Type / range | Kinds | Notes |
|---|---|---|---|
| kind | Instrument 3 (instrument) · Audio Effect 5 (device) · **MIDI Effect 6 (MIDI effect, new)** | all | Drum Rack (instrument kind 4) unchanged |
| chains | ordered list of Chain | all | MIDI: max 16 (**new limit**) |
| macros[8] | value 0–1, name (empty = "Macro N") | all | |
| mappings | list of MacroMapping | all | |
| volume | 0–4 linear | Instrument (rack out), Audio (gain) | existing |
| glide | 0–1 (→ 0–500 ms) | Instrument | **now applied** in mono mode (research D4) |
| **mono** | bool, default off | Instrument | last-note priority, chains share one voice |
| mode | Instrument: n/a · Audio: 0 Parallel, 1 Serial, 2 Select · **MIDI: 0 Parallel, 2 Select** | Audio, MIDI | MIDI reuses the same field; Serial (1) is refused for MIDI |
| dryWet | 0–1 | Audio | existing |
| pdc | bool | Audio | existing |
| chainSelect | 0–1 (UI shows 0–127) | Audio, **MIDI** | macro-mappable (existing target for Audio; **new** for MIDI) |
| selFollow | bool | Audio | existing, kept, not shown in the new card |
| **merge** | bool, default on | MIDI | merge duplicate output notes |
| **thru** | bool, default on | MIDI | pass notes outside every zone |
| bypassed | bool | all | the card's power switch |

## Chain

| Field | Type / range | Kinds | Notes |
|---|---|---|---|
| name | string (empty = first device's name) | all | existing (v8) |
| instrument | Instrument or none | Instrument | existing |
| devices | ordered audio Devices | Instrument, Audio | existing |
| **midiDevices** | ordered MidiDevices (kinds 0–5) | MIDI, **Instrument (issue #329)** | new; Instrument chains run MIDI effects → instrument → audio effects |
| gain | 0–4 linear | Instrument, Audio | existing |
| **velScale** | 0–2, default 1 | MIDI | the MIDI chain's "Volume" (FR-023a); 0 = sends nothing |
| pan | −1…+1 | Instrument, Audio | existing |
| **transpose** | −48…+48 semitones, default 0 | MIDI | output pitch clamped 0–127 |
| mute, solo | bool | all | existing |
| keyLo, keyHi | 0–127, lo ≤ hi | Instrument, MIDI | existing |
| velLo, velHi | 0–127, lo ≤ hi | Instrument, MIDI = velocity zone · **Audio = select range** | Audio meaning unchanged (research D2) |
| **selLo, selHi** | 0–127, lo ≤ hi, default 0–127 | MIDI | select range |
| meter | 0–1 peak, read-only | Instrument, Audio | MIDI: activity level (note-ons in the last ~100 ms, decays) |
| colour | palette index = chain index (view) | all | derived, not stored |

Validation: lo ≤ hi enforced on set (an edge cannot cross the other); all integer ranges
clamped.

## MacroMapping

| Field | Type | Notes |
|---|---|---|
| macro | 0–7 | |
| chainIndex | ≥ 0 | |
| deviceIndex | −1 instrument · ≥ 0 audio device (`devices`) · −2 pad controls (Drum) · **−3 rack control (new)** · **≤ −1000: chain MIDI device j = −1000 − j (new, all rack kinds)** | the `nota_midirack_*` ABI and MCP tools take plain indices and translate |
| paramIndex | ≥ 0 | for −3: 0 = selector |
| rangeMin, rangeMax | target units; min > max inverts | |
| curve | 0 Lin · 1 Exp · 2 Log · 3 S | kept per mapping (research D5) |

Rules: a (chain, device, param) triple belongs to at most one macro — adding a mapping
for a triple already mapped elsewhere moves it (one undo step). Deleting a chain or device
removes its mappings and re-indexes the rest.

*Selector as macro target*: today the selector cannot be mapped to a macro (it is set by
hand or follows the input level). The plan adds a rack-control mapping target
(`deviceIndex = −3`, param 0 = selector) so Audio and MIDI racks map it the same way
(FR-018). `applyMacro` writes `chainSelect` for it. Old projects have no such mappings.

## Blob versions

- **v8** (current): unchanged on read.
- **v9** (new): appends per chain `selLo, selHi, transpose, velScale, midiDeviceCount,
  { kind, paramCount, params[], stateSize, state[], bypassed }`, and per rack `mono,
  merge, thru`. Readers of v ≤ 8 use the defaults above.

## Project file

- `MidiDeviceDto.State` (**new**, optional blob reference): written when the MIDI effect's
  state blob is non-empty (the MIDI rack always; other MIDI effects stay params-only).
- Project format version +1; files without `State` load unchanged.

## View state (not persisted)

Per rack card instance, kept across rebuilds in the card's state object:

| Field | Values |
|---|---|
| centreTab | Chains · Zones/Select · Macros |
| rightTab | Chain · Rack out (MIDI: Output) |
| selectedChain | index (clamped after delete) |
| selectedMacro | 0–7 |
| mappingMacro | none or 0–7 (mapping on) |
| probePitch | none or 0–127 (hover) |

State transitions for mapping: `off → on(m)` by Map; `on(m) → on(m')` by selecting
macro m'; `on → off` by Map, Esc, or switching the centre tab away from Chains.
