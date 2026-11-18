# Quickstart
Build with `scripts/build.sh`. Launch `dotnet run --project src/managed/Nota.App -c Release`. Search for **Nota Chroma**, then drop it after a bass instrument or on an audio track. Start with C minor and full Mix. Use the note buttons for Custom. Set Mix to zero for a latency-aligned dry comparison. Save or duplicate the track to check recall.
Run `scripts/test-color.sh` for native checks and listening files in `build/color-prototype/clean/current/`. Managed checks run with `dotnet run --project tests/Nota.SmokeTest -c Release -- --color`.
Subjective listening acceptance remains pending the user's audition; no claim of exact Chroma matching.

Try Morph at 35% and Gate at −60 dBFS. Morph at 100% may attenuate shifted
frequencies absent from the input spectrum. With Gate off, try a long Decay and let
the source fall silent to hear the resonant tail. **Tight bass**, **Clear growl** and
**Spacious resonance** demonstrate distinct settings.

For A/B, use the same runner on the baseline source and current source. A baseline only
needs the matching `devices/effects/Chroma.cpp` and `Chroma.h` under its native source
directory. Use `--source <native-src> --out <directory> --listen-only` for that run.
The `*-wet.wav` listening copies are delay-trimmed and RMS-matched to their `*-dry.wav`
copies. The `*-raw-wet.wav` files retain the processing delay and output level.
Pass `--input <audio>` (requires ffmpeg) to include the first 4.5 seconds of a recording.
Use the same recordings in the same order for both runs.

## Tune a chord to a scale
Put Nota Chroma on a track with a sustained piano chord. Pick **Polyphonic**,
root **C**, and **Minor**. At full Amount and Mix, C4–E4–G4 becomes C4–Eb4–G4
on the reference recording. Turn Mix to zero for an aligned comparison. For
close or dense voicings, expect uncertain parts of the original to remain.

To regenerate the native tests and delay-aligned A/B recordings:

```sh
scripts/test-color.sh --polyphonic --out build/color-prototype/polyphonic/current \
  --input piano-c-major.ogg --input piano-inversions.ogg
```

See the verification report for the two source recordings. In each A/B WAV,
the input plays first, then the corrected audio after a half-second gap.
