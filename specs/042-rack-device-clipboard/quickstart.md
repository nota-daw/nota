# Quickstart: validating spec 042

## Build and smoke

```bash
scripts/build.sh
dotnet run --project tests/Nota.SmokeTest -c Release -- --racks
```

Expect the `racks: chain device clipboard` section to pass:
- move within a chain and to another chain (mappings follow, one undo each);
- track → rack and rack → track paste with equal params and state;
- every refusal code, sidechain stripping, and Duplicate on both chains.

## In the app

1. **Q1 — reorder (US1)**: an Audio Effect Rack, chain 1 with EQ-3 · Compressor · Reverb. Drag Reverb's header in front of EQ-3. A drop bar shows while dragging; the order becomes Reverb · EQ-3 · Compressor. ⌘Z restores it.
2. **Q2 — move to a chain (FR-003a)**: map Macro 1 to the Compressor's Threshold, then drag the Compressor onto chain 2's row. It now sits in chain 2 and chain 2 is chosen. Macro 1 still drives it, as the M1 tag on its card shows. ⌘Z puts it back in chain 1, still mapped.
3. **Q3 — clipboard inside a rack (US2)**: click the Reverb, ⌘C, choose chain 2, ⌘V. Chain 2 has a Reverb with the same values. ⌘D adds another right after it. Delete removes it. Each step undoes on its own.
4. **Q4 — across (US3)**: tune a Delay on the track chain, ⌘C. Click a device in an Instrument Rack chain and press ⌘V: the Delay lands after it with the same values. Copy it back from the rack, click a track device, ⌘V. Repeat with an Arp from the track's MIDI effects into an Instrument Rack chain and into a MIDI Effect Rack chain.
5. **Q5 — refusals**: copy an Arp and paste into an Audio Effect Rack chain. Copy an Audio Effect Rack and paste into another rack's chain. Paste a MIDI effect into chain 17 of an Instrument Rack. Each time nothing changes and the status line gives the reason.
6. **Q6 — sidechain**: key a Compressor from another track, copy it, paste into a rack chain. The status line ends with "sidechain dropped" and the pasted compressor runs on the chain's signal.
7. **Q7 — themes**: repeat Q1–Q3 in Studio Light, Ember Graphite and Ember Paper. The selection, drop bar and row drop outline must be readable.
