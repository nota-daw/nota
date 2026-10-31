# Quickstart: validating the feature

## Prerequisites

- `scripts/build.sh` (Release engine in `build/`, see the engine-build-dirs note in BUILD.md).
- Run the app with an isolated data dir: `NOTA_DATA_DIR=/tmp/nota-043 <app>`.

## Engine (smoke)

```bash
dotnet run --project tests/Nota.SmokeTest -- --instrument-chain
```

Expected: every `InstrumentChainTests` check passes (see [contracts/c-abi.md](contracts/c-abi.md)
§ Smoke coverage). `scripts/build.sh` runs it with the full suite.

## In the app

1. **Select / Replace (US1)** — New project, add a Nota Volt track with a MIDI clip. Click the
   Volt card: selection ring. Right-click → Replace… → Nota Keys: the clip plays Keys. ⌘Z: Volt
   back with its settings. Replace… → Instrument Rack: an empty rack card; ⌘Z.
2. **Copy / Paste instrument** — Tweak Volt's cutoff, ⌘C; select another instrument track's card,
   ⌘V: it is a Volt with the same cutoff; ⌘Z restores that track's own instrument.
3. **Delete → track** — Select the instrument, press Delete: confirmation (track has a clip) →
   Delete: track gone. ⌘Z: track, clip and instrument back. On a track without clips: no prompt.
4. **Remove control (US2)** — Add EQ-8, a plug-in effect, an Audio Effect Rack; fold one. Each
   header (and the folded strip) shows ×; Tab to it, Enter removes; three ⌘Z restore all.
   Rack chain compact cards show the same ×. Check all four themes.
5. **Session drops (US3)** — Session view: drop Nota Volt right of the last column → new track;
   drop Nota Keys on its header → replaced; drop a drum kit preset on an audio column → new
   track after it; drop an instrument on a Drum Rack column → a new pad. ⌘Z each.
6. **Move (US4)** — Automate a reverb's Mix on track 1. Menu → Move to track ▸ Track 2: it lands
   at the end of track 2's effects with its lane. ⌘Z: back on track 1 with the lane. Drag the
   reverb's header out onto track 3's header in the Arrangement: moved. ⌥-drag onto track 4 in
   Session: copied, track 3 keeps its own. Drag Volt's card onto another instrument track's
   header: that track now plays Volt, the source track is gone after the prompt; one ⌘Z
   restores both.
7. **Refusals** — Drag a MIDI effect over an audio track header: no highlight. Paste an
   instrument with an effect selected: status says why.
8. **MCP** — `set_track_instrument` and `move_device_to_track` against a running app do the same
   as steps 1 and 6.
9. **Playback** — Repeat 1 and 6 while the project plays: no dropout on other tracks.
