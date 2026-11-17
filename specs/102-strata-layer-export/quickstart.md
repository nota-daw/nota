# Quickstart: validate the Strata layer export

## Automated

```bash
scripts/build.sh
```

The smoke test's "Nota Strata" section records two layers with different material and
checks (see [contract](contracts/strata-layer-export.md)):

- the layer-2 WAV is one loop long, sample-identical to layer 2 in the blob, and differs
  from layer 1;
- the device state blob is byte-identical before and after the export;
- after `DeviceSetState` on a fresh Strata (save/reopen), the export is identical;
- a layer that is recording → `Recording`; first pass → `NoLoop`; out-of-range → `NoLayer`;
  none of them creates a file.

## Manual / headless

1. Put a Strata on a track, record a pass, overdub a second.
2. Right-click layer 2 → "Export layer 2 as WAV…", save.
3. Open the WAV: one loop long, only layer 2's material, starting at the loop's top.
4. Both layers still play with their levels and mutes.
5. Start an overdub on layer 3; right-click layer 3 → entry disabled, "still recording".
