# Quickstart

Run `scripts/build.sh` and open Nota Color on a bass instrument or an audio
track. Pick a scale or click the piano keys to use a custom mask. Start with
Amount and Mix at 100%, Morph zero and Gate off. Raise Resonance and compare
the ringdown at different Decay values. Mix at zero plays the aligned original.
Use Mute low and Mute high to silence either sideband at full Mix. Switch
between S and L in the header. Save and reopen the project to check the card
size, control values and mask recall.

Run `dotnet run --project tests/Nota.SmokeTest -c Release -- --color` to run
the focused Nota Color smoke tests through the C ABI.

## Listening comparisons

Use the same FM, growl, chord or attack source at 48 kHz for both renders.
For the HQ baseline use C minor, Low 150 Hz, High 8 kHz, Amount and Mix at 100%,
Resonance at 25%, Decay at 120 ms, Output at unity, Morph at zero and Gate off.
Align renders using their reported latency, accounting for any compensation
already applied. Match stereo RMS over the same window for listening copies and
keep the raw levels for measuring spectral balance, attacks and ringdown.

## MIDI note source

Put Color on an audio track with a sustained C-sharp tone. Add C and D
notes to an instrument track's Arrangement clip, then choose that track
from Color's **MIDI** menu. The corresponding keys light up and the
tuned signal follows the notes. Leave a gap after the last note to hear
the correction fade out. Select **Manual** to restore the manual scale.
