# Contract: C ABI additions (`nota_engine.h`)

The functions below are pure C, use no C++ types, and are message-thread only unless
noted. Points use the existing `NotaAutomationPoint {beat, value, curve}`. Beats are
clip-local, or loop-local for slots, and values are normalised per [data-model.md](../data-model.md).

```c
/* ---- MIDI controllers (specs/020) ----------------------------------------
 * ctl: 0..127 = CC number, NOTA_CTL_PITCH_WHEEL, NOTA_CTL_CHANNEL_PRESSURE.
 * Values: CC 0..1 (data/127; CC64-69 on at >= 0.5), wheel -1..+1, pressure 0..1. */
#define NOTA_CTL_PITCH_WHEEL       128
#define NOTA_CTL_CHANNEL_PRESSURE  129
#define NOTA_CTL_COUNT             130

/* Arrangement MIDI clip. list fills the ctls that hold data (sorted) and returns the total
 * count (out may be NULL). get(out=NULL) returns the point count. set replaces the curve
 * (count 0 removes it); points sorted or not, at most one per beat; reserved CCs
 * (6, 38, 96-101, 120-127) are refused. One undo step; plays on the next block. */
NOTA_API int32_t    nota_midi_clip_ctl_list(const NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t* out_ctls, int32_t cap);
NOTA_API int32_t    nota_midi_clip_ctl_get (const NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t ctl, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_midi_clip_ctl_set (NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t ctl, const NotaAutomationPoint* pts, int32_t count);

/* Session slot clip: same semantics, beats in [0, slot length). */
NOTA_API int32_t    nota_session_ctl_list(const NotaEngine* engine, int32_t track_id, int32_t scene, int32_t* out_ctls, int32_t cap);
NOTA_API int32_t    nota_session_ctl_get (const NotaEngine* engine, int32_t track_id, int32_t scene, int32_t ctl, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_session_ctl_set (NotaEngine* engine, int32_t track_id, int32_t scene, int32_t ctl, const NotaAutomationPoint* pts, int32_t count);

/* Live controller input (any thread, lock-free producer like note_on): reaches armed /
 * audition tracks (engine_) or one track (track_), plays now, and is recorded when the
 * take records. nota_engine_note_expression(pitch -1, dim 0..3) keeps working and maps to
 * wheel / pressure / CC74 / CC64. */
NOTA_API NotaResult nota_engine_controller(NotaEngine* engine, int32_t ctl, float value);
NOTA_API NotaResult nota_track_controller(NotaEngine* engine, int32_t track_id, int32_t ctl, float value);

/* The last value delivered to the track's instrument for ctl (NaN = none): tests, meters. */
NOTA_API float nota_track_delivered_controller(const NotaEngine* engine, int32_t track_id, int32_t ctl);

/* Device-free self-test: curve value-at (step + linear), split/trim carry, chase value,
 * end-of-clip release, automation precedence, overdub touched-range fold. 0 = pass, else
 * the number of the first failed check. */
NOTA_API int32_t nota_engine_midi_ctl_selftest(NotaEngine* engine);
```

## Changes to existing functions

- **`nota_track_add_automation_lane(engine, track, target, device_index, param_index)`**
  accepts **target 5** (MIDI controller) with `device_index -1` and `param_index = ctl`.
  It refuses target 5 on non-instrument tracks and for reserved CCs. An existing lane on
  the same ctl is reused.
- **`nota_track_automation_lane_info`** reports target 5 with `param_index = ctl`.
- **`nota_track_begin_automation_write` / `end_…`** accept target 5. Hardware writes are
  started by the engine itself (research R7); the UI calls these only for mouse gestures
  on a drawn lane.
- **`nota_engine_note_expression` / `nota_track_note_expression` with pitch −1, dim 3
  (sustain)** now pass the value through unthresholded. Built-ins threshold at 0.5;
  plug-ins get `round(value*127)`.
- **Clip operations** (`nota_clip_split`, trim, join/consolidate, clipboard copy/paste,
  cross-track move, `nota_session_slot_to_arrangement`, arrangement → slot) carry
  controllers per [data-model.md](../data-model.md). Their signatures are unchanged.

## Smoke coverage (`tests/Nota.SmokeTest/MidiControllerTests.cs`)

1. **Selftest**: `nota_engine_midi_ctl_selftest() == 0`.
2. **Set/get round trip**: list / get / set, including removal and the refusal of reserved CCs; undo restores.
3. **Record**: arm, record, send CC64 down/up, a wheel ramp and CC1 through `nota_track_controller`, stop. The take holds the curves at the expected beats within one block, with thinned point counts.
4. **Playback**: `nota_track_delivered_controller` reads what reached the instrument. Clip playback delivers the values at the right beats.
5. **Chase**: start playback mid-clip; the first block delivers the value at that beat.
6. **End release**: a clip with the pedal down at its end, then transport stop. CC64 = 0 and wheel = 0 are delivered.
7. **Overdub fold**: a loop take, pass 2 moves CC1 over half the loop. Pass-1 points outside the touched range stay.
8. **Pedal-only pass in Replace mode**: notes stay, and CC64 is replaced over the touched range.
9. **Precedence**: a target-5 lane on CC1 overrides the clip's CC1.
10. **Persistence**: save → load keeps the curves and target-5 lanes; a v21 file loads with none.
11. **Poly aftertouch** (R11): 0xA0 on a non-MPE take lands in the note's pressure curve.
