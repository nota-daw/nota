# Contract: Strata layer export

## Managed helper (`Nota.Infrastructure.StrataLayerExport`)

```text
enum StrataLayerExportResult { Written, NoLoop, Recording, NoLayer, Invalid }

StrataLayerExportResult Check(byte[] blob, int layer)
    // What an export of `layer` (0-based) would do with this blob; writes nothing.

StrataLayerExportResult TryReadLayer(byte[] blob, int layer,
                                     out int sampleRate, out float[] interleaved, out int frames)
    // Written → `interleaved` is a copy of the layer's stereo PCM, `frames` == loopLen.

StrataLayerExportResult WriteLayerWav(byte[] blob, int layer, string path)
    // Written → path holds a 32-bit float stereo WAV, one loop cycle at sampleRate.
    // Any other result → no file is created.
```

Never reads or writes the device; never mixes layers.

## UI (Strata L card, layer list)

- Right-click on a layer row opens a menu:
  - caption: "Layer N"
  - item "Export layer N as WAV…" (Save glyph). Tooltip / dialog title say:
    "as recorded, after Input gain — Speed, Reverse, Feedback and level not applied".
  - disabled with "still recording" / "loop not closed yet" in the item's trailing text
    when unavailable.
- Save dialog: title "Export Strata layer N (as recorded) to WAV", suggested name
  "Nota Strata layer N.wav".
- After the pick: on any non-Written outcome the status bar shows why; nothing is written.
