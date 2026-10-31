# Quickstart: validating the racks redesign

Runnable checks that prove the feature end to end. Contracts:
[c-abi.md](contracts/c-abi.md) · [mcp-tools.md](contracts/mcp-tools.md) ·
[ui-rack-card.md](contracts/ui-rack-card.md); entities: [data-model.md](data-model.md).

## Prerequisites

- macOS dev setup per `BUILD.md`; the engine in `build/` is a Release build.
- Scratch data dir for anything that starts App services: `NOTA_DATA_DIR=/tmp/nota-racks`.

## Q1 — Full gate

```bash
scripts/build.sh
```

Expect green: native + managed build, the smoke test (incl. the new MIDI-rack, mono/glide,
mapping and design checks below) passes.

## Q2 — Smoke test only (faster loop)

```bash
dotnet run --project tests/Nota.SmokeTest -c Release --nologo
```

New sections it must print as passing:

| Section | Proves |
|---|---|
| `racks: midi rack routing` | zones, velocity zones, Parallel vs Select (selector), mute/solo, transpose, velocity scale (100 % / 50 % / 0 %), merge on/off, thru on/off — notes in → notes out compared to an expected list (US4, FR-021–023a) |
| `racks: midi rack no stuck notes` | 1 000 random note-on/off interleaved with zone / mode / mute / transpose edits and chain deletes; at the end the instrument holds no notes (SC-005, FR-026) |
| `racks: midi rack persistence` | state blob → new rack → identical `get_midi_rack`; save/reopen project; undo/redo of each structural edit; preset save/load (SC-006) |
| `racks: instrument mono/glide` | mono on: one voice at a time, last-note priority; glide: per-note bend ramps from Δ to 0 over the glide time on an MPE synth; poly mode unchanged |
| `racks: mapping rules` | adding a mapping of an already-mapped param moves it; curve "Mixed" detection; set curve on a macro updates every target; selector target (−3) drives `chainSelect` |
| `racks: v8 compatibility` | stored v8 blobs of an Instrument Rack and an Audio Effect Rack (fixtures) render bit-identical before/after (SC-004) |
| `racks: chain MIDI effects` (#329) | an Arp in an Instrument Rack chain produces the same note-ons (pitch, sample offset) as the same Arp on a track over 8 bars, incl. a loop wrap and a stop/start; zones and mono run before chain MIDI effects; the other chain is unaffected; save/reopen, rack preset, track duplicate, device copy/paste, undo/redo and `add_rack_chain_midi_fx` round-trip |
| `racks: key params table` | every rack-eligible instrument / audio effect / MIDI effect kind has ≤ 3 valid key-param indices |
| `design: studio / themes / labels / icons` | the new card passes with no new allow-list lines (SC-003) |

## Q3 — MCP walkthrough (US4 via the AI surface)

With the app running (or the MCP smoke harness, `McpTestSupport`):

1. `add_instrument_track` → `add_midi_fx(kind=6)` → `get_midi_rack` shows one chain.
2. `add_midi_rack_chain` ×2; `add_midi_rack_chain_device(0, 0)` (Arp), `(1, 1)` (Chord),
   `(2, 5)` (Random).
3. `set_midi_rack_chain_zone(0, keyLo="C3")`, `(1, keyHi="B2")`, `(2, velLo=110)`.
4. Play notes C2, C4 @100, E4 @120 → chain 1 only, chain 0 only, chains 0 + 2.
5. `set_midi_rack_mode("select")`, `set_midi_rack_chain_zone(0, selLo=0, selHi=63)`,
   `set_midi_rack_selector(100)` → chain 0 receives nothing.
6. `save_version` / reopen → `get_midi_rack` unchanged.

## Q4 — UI flows (headless harness)

Use the headless Avalonia harness pattern (see the `nota-design` skill and the project's
UI-verify notes): a throwaway console project referencing `Nota.App`, a `DeviceChainView`
in a `Window`, synthetic pointer events, `CaptureRenderedFrame()`.

For each rack (Instrument with 3 chains, Audio Effect with 2, MIDI Effect with 3):

1. Chains tab: click chain rows → chosen chain changes, devices update, Chain panel follows.
2. Select Macro 2 → Map → click a param in a compact card → badge count +1, param shows
   "M2"; click it again → removed; press Esc → mapping ends.
3. Macros tab: drag a target's max below its min → readout inverts; pick Exp → preview and
   all targets update; on a project fixture with mixed curves the segmented shows "Mixed".
4. Zones (Select) tab: drag a key edge → label updates in note names; hover → probe line +
   footer names the chains.
5. Chain panel: Duplicate → new chain after the chosen one; Delete → mappings of that chain
   are gone; with one chain Delete is disabled.
6. Rack out: Instrument mono switch; Audio Parallel/Serial/Select explanation text; MIDI
   merge / thru switches.

## Q4b — Instrument Rack chain MIDI effects (US6, #329)

1. Two-chain Instrument Rack (Volt, Pentad). Drag Nota Arp from the Browser onto chain 1's
   strip → it lands in the MIDI section before Volt; drag Nota Chord onto the instrument
   slot → it also lands in the MIDI section.
2. Hold a C-major chord: chain 1 arpeggiates, chain 2 sustains.
3. Map Arp Rate to Macro 1 (Map → click the Arp's Rate in its compact card) → turning
   Macro 1 changes the rate.
4. Save as rack preset, load it on a new track → MIDI effects come back.

## Q4c — Perf gate (the `playEvents` hook)

```bash
scripts/perf-gate.sh develop
```

Expect no regression on the existing scenarios (CI runs the same check on the PR).

## Q5 — Themes (US5)

Capture every rack × centre tab × right tab in Studio Dark, Studio Light, Ember Graphite
and Ember Paper (switch with the reflected `NotaThemeService.Set(new AppearanceChoice(…))`),
then switch theme **while** the card is open and capture again without rebuilding.
Expect: no fixed dark surfaces in light themes, legible text (DESIGN.md readability bar),
accent only on the chosen chain / macro / tab, active switches and touched controls.
Attach the 4 × 3 sheet to the PR (before/after, `nota-pr` skill).

## Q6 — Compatibility (FR-029)

Open a project saved on `develop` before this change that has an Instrument Rack (with
zones, macros, per-target curves that differ within a macro) and an Audio Effect Rack in
Select mode. Expect identical sound (Q2 v8 section) and the Macros tab showing "Mixed" for
the mixed macro.
