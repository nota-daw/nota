# Quickstart: validating Mosaic and Drum Rack chains

## Automated

```bash
scripts/build.sh
```

The smoke test's "Rack chains: Mosaic" and "Rack chains: nested Drum Rack" sections must
print PASS:

- Mosaic chain: add by kind 17, set a program over generated WAVs, wait for status ready,
  render a note inside the chain zone (audible) and outside (silent), save/reload the rack
  blob (program text equal), an undoable program set + undo restores the old text.
- Nested Drum Rack: add by kind 4 on an Instrument Rack (accepted) and on a Drum Rack pad
  (refused); add two Sampler pads through the nested address; pad note inside the zone
  audible, other note silent; choke/tune/decay round-trip; undo of a nested pad add;
  blob round-trip keeps pads and audio; an outer macro mapped to the nested macro moves a
  nested pad's decay.

## Manual / headless UI

1. New Instrument Rack track → card → add chain → Nota Mosaic; open its GUI → load a
   factory preset → zones fill in; play.
2. Drop "Nota Drum Rack" from the Browser on the add-chain area → chain appears; open its
   GUI → Drum Rack card in a window; drop a sample on a pad; play C1.
3. ⌘Z after each edit; save, reopen, check both chains.
