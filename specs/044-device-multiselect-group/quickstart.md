# Quickstart: validating the feature

## Prerequisites

- `scripts/build.sh` (Release engine in `build/`, see the engine-build-dirs note in BUILD.md).
- Run the app with an isolated data dir: `NOTA_DATA_DIR=/tmp/nota-044 <app>`.

## Engine (smoke)

```bash
dotnet run --project tests/Nota.SmokeTest -- --device-group
```

Expected: every `DeviceGroupTests` and `DeviceSelectionTests` check passes (see
[contracts/c-abi.md](contracts/c-abi.md) § Smoke coverage and
[data-model.md](data-model.md) § Group shape). `scripts/build.sh` runs them with the full suite,
plus the engine perf gate (nested resolve adds no measurable block time).

## In the app

1. **Group audio effects (US1)** — Nota Volt track with a MIDI clip; add EQ-8, Compressor,
   Saturator, Reverb. Click Compressor, Shift-click Reverb: three borders. ⌘G: EQ-8 → Audio
   Effect Rack; its chain lists Compressor, Saturator, Reverb with the same values. Play: same
   sound (export a bar before and after; levels within 0.1 dB). ⌘Z: four cards back, the three
   selected. ⌘G again, then ⌘⇧G on the rack: same result as undo.
2. **Links travel (FR-016)** — Draw a lane on the Reverb's Mix, add an LFO CV link to the
   Saturator's Drive, key the Compressor from another track, and set an Arp's CC lane on the
   EQ-8 gain. Select EQ-8..Reverb, ⌘G. Play: the Mix lane still moves the Reverb (the lane
   title reads "Audio Effect Rack / Reverb · Mix"), the LFO still wobbles Drive, the Compressor
   still ducks, the Arp still drives EQ-8. Save, reopen: all still act. ⌘⇧G: all back on the
   track devices; ⌘Z twice: back to the start.
3. **Multi-select (US2)** — ⌘-click EQ-8 and Reverb (not adjacent): ⌘G is refused with
   "Select devices next to each other…", the menu item is disabled. ⌘C, select another
   instrument track's last effect, ⌘V: both land after it in order. Shift-click a range of
   three effects, Delete: gone in one step; ⌘Z restores. Drag the selected range's header two
   slots left: the range moves together; ⌘Z.
4. **Instrument in the selection** — Shift-click from an Arp to the Chorus after Volt, press
   Delete: the "Delete track …?" dialog appears even on a track without clips; Cancel leaves
   everything. ⌘C on that selection, select another instrument track, ⌘V: its instrument is now
   Volt, Arp and Chorus land in their sections, one ⌘Z undoes all.
5. **Instrument Rack (US3)** — Select Arp → Volt → Chorus, ⌘G: the instrument is an Instrument
   Rack whose chain 0 holds Arp, Volt, Chorus; the clip plays the same notes. Add a chain,
   save the rack as a preset, load it on a new track: same chains. ⌘⇧G on a one-chain rack:
   Arp after the track's MIDI effects, Volt as the instrument, Chorus before the track's audio
   effects. Select an Instrument Rack instrument and ⌘G: refused with a reason.
6. **MIDI Effect Rack (US4)** — Select Chord → Scale → Arp, ⌘G: one MIDI Effect Rack; same notes;
   ⌘⇧G restores.
7. **Refusals** — An Audio Effect Rack inside the selection: refused, naming it. A rack with two
   chains: Ungroup disabled. MIDI effect + audio effect without the instrument: refused.
8. **During playback** — Repeat 1 and 5 with the transport running and a Reverb tail sounding:
   no gap or click; the tail continues through the edit.
9. **MCP** — `group_devices` (trackId, fxFirst 1, fxCount 3) then `get_effect_rack`, then
   `ungroup_rack` and `undo`; each answer matches the UI's.
10. **Themes** — Multi-selection borders, the "N devices" menu header and the disabled Group
    item in all four themes.
