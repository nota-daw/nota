# Contract: C ABI and instrument channels (specs/055)

The boundary stays the pure C ABI in `include/nota/nota_engine.h` (AR-7). Twin adds **one**
function; everything else rides existing, instrument-generic entry points (plus the three
generic additions below).

## New

```c
/* Adds an instrument track with the built-in Nota Twin (two-part morphing analog poly,
 * kind 18). id (>0). */
NOTA_API int32_t nota_engine_add_twin_track(NotaEngine* engine);
```

## Added during implementation (beyond the plan)

Not in the original plan; each one closes a requirement the planned surface could not reach.
All are append-only; existing callers are unchanged.

```c
/* A live note-off with its release velocity (0..1), as a MIDI note-off message carries it —
 * engine-wide, and on one track. FR-030: Twin's "Release velocity" matrix source. */
NOTA_API NotaResult nota_engine_note_off_velocity(NotaEngine* engine, int32_t pitch, float release_velocity);
NOTA_API NotaResult nota_track_note_off_velocity(NotaEngine* engine, int32_t track_id, int32_t pitch, float release_velocity);

/* Preset audition: the instrument's state blob after its params (a Twin preset's patterns),
 * so a sequenced preset previews its own pattern (SC-011 presets would otherwise be silent). */
NOTA_API int32_t nota_audition_instrument_state(NotaAudition* rig, const uint8_t* data, int32_t size);
```

Inside the engine (no ABI):

- `Instrument::noteOffVelocity(pitch, velocity)`: a note-off that carries its release velocity.
  It defaults to `noteOff`, so other instruments are unaffected. MIDI input sends it for a
  0x80 message, and an Instrument Rack passes it on to its plain chains. A note-on at velocity
  0 and clip notes (which store no release velocity) still send a plain note-off.
- `Delay::A_Reset` and `Reverb::A_Reset` (`deviceAction` id 1 on each): the device starts over
  as a new one. This clears the tail, the modulation / wow phase and the time / size glide.
  Twin's built-in effects use it on a silent start, so a second render, a freeze and a render
  at another rate match the first one exactly (SC-008, edge cases). `A_KillTail` /
  `A_ClearLoop` keep their old, narrower meaning.

## Reused (no signature change)

| Need | Entry point |
|------|-------------|
| Params (both sides, both parts, matrix, sequencer settings) | the plugin-param family used by every built-in (count / id / name / get / set / text / default), `deviceIndex = -1` |
| Automation, MIDI learn, macros, CV links | existing param targets — Twin's params are ordinary targets |
| Pattern editing | `nota_track_instrument_action(engine, track, id, iarg, farg)` (and the rack-chain twin `nota_rack_chain_instrument_action`) — action ids below |
| Pattern read-back | the instrument state read (`nota_device_get_state`, device −1; chain variant for racks) |
| Live telemetry | the instrument scope read (`scopeRead`) — layout below |
| Voice meter, held notes | `activeVoiceCount`, `heldNotes` |
| Replace / clone / freeze / rack | engine factory by kind 18 |

## `action()` ids (append-only)

| Id | Name | iarg | farg |
|----|------|------|------|
| 0 | `SelectPattern` (edit target) | pattern 0..7 | — |
| 1 | `SetLength` | steps 1..64 | — |
| 2 | `ClearStep` | step | — |
| 3 | `AddStepNote` | step << 8 \| note | velocity 0..1 |
| 4 | `RemoveStepNote` | step << 8 \| note | — |
| 5 | `SetStepVelocity` | step | 0..1 |
| 6 | `SetStepGate` | step | 0..1 (> 1 = tie) |
| 7 | `StepRecordArm` | 0 / 1 | — |
| 8 | `StepRecordAdvance` | — | — (rest step) |
| 9 | `ClearPattern` | pattern | — |
| 10 | `CopyPattern` | from << 8 \| to | — |
| 11 | `MotionTarget` | lane << 16 \| param index (0xFFFF none) | — |
| 12 | `MotionMode` | lane | 0 stepped / 1 smooth |
| 13 | `MotionSet` | lane << 8 \| step | value 0..1, < 0 = unset |
| 14 | `MotionRecordArm` | lane (−1 off) | — |
| 15 | `ClearMotion` | lane | — |
| 16 | `StepRecordPoll` | — | — (the card's tick: notes played while armed become steps) |

Every edit bumps the pattern revision and publishes a new immutable pattern snapshot (AR-5).
An edit that changes the patterns is one undo step: the engine records the instrument's edit
state (`Instrument::editState`, a new `MixKind::InstrumentEdit`) around `instrumentAction`, and
undo hands the old bytes back without republishing the instrument, so sounding notes go on.
Side / part copies (A → B, swap, copy part) are **not** actions: the app writes the params in
one mix gesture (research R14), so they are ordinary undoable param edits.

## `scopeRead` layout (append-only)

| Index | Value |
|-------|-------|
| 0 | playing step (−1 stopped) |
| 1 | playing pattern |
| 2 | pending pattern (−1 none) |
| 3 | pattern revision |
| 4 | sounding voices Lower |
| 5 | sounding voices Upper |
| 6 | Lower live morph (part, before per-voice) |
| 7 | Upper live morph |
| 8–19 | per-voice morph (12 voices; −1 idle) |
| 20–22 | LFO 1–3 phase output (Lower), for the card's LFO graphs |
| 23 | output peak (dBFS) |
| 24 | step-record cursor (−1 = not recording) |
| 25 | last key played, MIDI 0..127 (−1 = none) — the card's split-point learn |

## Behavioural contract checked by the smoke test

1. `nota_engine_add_twin_track` returns a track that renders a non-silent Init note.
2. Morph = 0 / 1 render equals a patch whose B / A side was copied from A / B (SC-002).
3. Per-note pressure on one note of a chord leaves the others' render unchanged (SC-005).
4. A 16-step pattern started by the transport fires on the grid at 90 and 174 BPM (SC-009).
5. `get_state` → new instance `set_state` → identical render (SC-008); a truncated param
   array and a missing pattern block still load (FR-052).
