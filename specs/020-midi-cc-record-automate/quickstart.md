# Quickstart: verifying MIDI controller recording, editing and automation

## Prerequisites

- macOS dev setup per [`BUILD.md`](../../BUILD.md).
- Isolated data dir for manual runs: `NOTA_DATA_DIR=/tmp/nota-cc`. `HOME` does not isolate JUCE.
- Optional: a MIDI keyboard with a sustain pedal and wheels. Without one, use the MCP
  tools or the smoke test.

## Gate (every PR)

```bash
scripts/build.sh
```

This builds the engine and the app and runs `tests/Nota.SmokeTest`, which includes
`MidiControllerTests` (checks 1–11 in [contracts/c-abi.md](contracts/c-abi.md)). Expected
result: all green, no new dropouts in `TimingBudget`.

## PR 1 — record and play back (US1)

1. **Keys and the pedal**: create a track with Nota Keys, arm it and record 4 bars while
   pressing and releasing the sustain pedal on chords.
   - Play the clip back. Notes are held and released at the same places as played.
   - Start playback in the middle of a held-pedal stretch. The pedal is already down.
2. **Plug-in and wheels**: on a plug-in synth, ride the mod wheel and bend.
   - Playback reproduces both.
   - Stop mid-bend: the pitch returns to centre and no note hangs.
3. **Any CC**: send CC11 swells to a plug-in.
   - Live: the plug-in follows.
   - Recorded: playback follows.
4. **Loop Overdub**: loop 2 bars and record notes on pass 1. On pass 2, move only the mod
   wheel over the second bar.
   - Pass-1 mod wheel data in bar 1 stays.
   - The notes stay.
5. **Loop Replace**: on pass 3, press only the pedal. The notes stay and the pedal is
   replaced where you pressed it.
6. **Save and reopen**: the controllers are still there.
7. **Old project**: open a pre-v22 project. It loads unchanged.

## PR 2 — piano-roll lanes (US2)

Open the take in the clip editor.

1. **Sustain**: the fifth lane tab reads *Sustain*, and the blocks match the pedal.
   - Drag a block's right edge earlier, then play: the release is earlier.
   - Undo restores it.
2. **Picker**: pick *Mod wheel* and draw a rise (⌥ for a straight line). Playback follows.
3. **Empty controller**: pick **Other CC ▸ CC 74**, draw a sweep in an empty clip on a
   plug-in, and play.
4. **Picker order**: the picker lists the controllers that have data first, each with a
   dot.

Visual proof: the headless Avalonia render, or the env-guarded in-app capture recipe
(see the project's UI verify notes). Capture the Sustain blocks and a CC curve in Studio
Dark and Studio Light.

## PR 3 — CC automation (US3)

1. **Draw a lane**: on an instrument track, open the automation target picker and choose
   **MIDI controllers ▸ CC 74**. Draw a lane across two clips and play: the plug-in's
   cutoff follows across clips.
2. **Record a lane**: add a *Mod wheel* lane, engage Record, play and move the hardware
   mod wheel. The lane is written, latched until stop.
3. **Precedence**: on a clip that also has recorded mod wheel data, the lane wins where
   it has points. The piano-roll lane shows "Track automation overrides this lane".

## MCP smoke (no hardware)

The calls below are illustrative; the exact names are settled in tasks:

```text
set_clip_controller   track=1 clip=0 ctl=64 points=[{beat:0,value:1},{beat:3.5,value:0}]
get_clip_controllers  track=1 clip=0
add_automation_lane   track=1 target=midi_cc ctl=1
```

Then play and confirm the effect by ear or through the plug-in's UI.
