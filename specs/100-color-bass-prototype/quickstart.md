# Quickstart

Run `scripts/build.sh` and open Nota Chroma on a bass instrument or an audio
track. Pick a scale or click the piano keys to use a custom mask. Start with
Amount and Mix at 100%, Morph zero and Gate off. Raise Resonance and compare
the ringdown at different Decay values. Mix at zero plays the aligned original.
Use Mute low and Mute high to silence either sideband at full Mix. Switch
between S and L in the header. Save and reopen the project to check the card
size, control values and mask recall.

Run `scripts/test-color.sh --tests-only` for focused native tests or run
`scripts/test-color.sh` to also capture five-second stereo WAVs, paired auditions
and CPU metrics in `build/color-prototype/hq/current`. The C ABI smoke command
is `dotnet run --project tests/Nota.SmokeTest -c Release -- --color`.

The `--source` option renders a previous Nota Chroma from a directory containing
`devices/effects/Chroma.cpp` and `Chroma.h`. Use `--listen-only` for the older
DSP so it runs the same listening scenes with its own reported latency.

## Listening comparisons

Use the generated FM, growl, chord and attack `*-source.wav` files at 48 kHz.
For the HQ baseline use C minor, Low 150 Hz, High 8 kHz, Amount and Mix at 100%,
Resonance at 25%, Decay at 120 ms, Output at unity, Morph at zero and Gate off.
Align renders using their reported latency, accounting for any compensation
already applied. Match stereo RMS over the same window for listening copies and
keep the raw levels for measuring spectral balance, attacks and ringdown.

## MIDI note source

Put Chroma on an audio track with a sustained C-sharp tone. Add C and D
notes to an instrument track's Arrangement clip, then choose that track
from Chroma's **MIDI** menu. The corresponding keys light up and the
tuned signal follows the notes. Leave a gap after the last note to hear
the correction fade out. Select **Manual** to restore the manual scale.
