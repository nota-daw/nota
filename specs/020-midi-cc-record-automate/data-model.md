# Data Model: Record and automate MIDI CC

## Controller id (`ctl`)

| ctl | Controller | Value range | Rest value | Interpolation |
|---|---|---|---|---|
| 0–127 | CC *n* | 0..1 (data / 127) | 0 (CC7 / CC11: none, no reset) | linear |
| 64–69 | Sustain, Portamento, Sostenuto, Soft, Legato, Hold 2 | 0..1, on at ≥ 0.5 | 0 | **step** (hold) |
| 128 | Pitch wheel | −1..+1 | 0 | linear |
| 129 | Channel pressure | 0..1 | 0 | linear |

The reserved CCs are never recorded or offered: 6, 38 and 96–101 (data entry, RPN/NRPN)
and 120–127 (channel mode). Their meaning is set by context, so storing them alone would
be wrong.

## ControllerCurve *(engine, immutable once published)*

| Field | Type | Notes |
|---|---|---|
| ctl | int32 | see table |
| points | vector<AutomationPoint> | sorted by beat, clip-local beats; may be < 0 (pickup before the clip start); `curve` honoured for linear ctls; at most one point per beat |

- **Value at a beat**: held after the last point. Step ctls take the value of the last
  point ≤ beat. Linear ctls interpolate with automation shaping.
- **Before the first point**: the curve has no value. Playback sends nothing there and the
  controller keeps whatever it had, so a pedal pressed on beat 1 is not down at the clip start.
- **Replace-range junctions**: a recorded range meets the old curve in a step. The old value
  holds to 1/1024 beat before the range (`kCtlJunction`), and the range's last value holds to
  1/1024 beat before the old curve takes over again.
- **Empty curve**: never stored. Setting 0 points removes the curve.

## MidiClip (extended)

| Field | Change |
|---|---|
| controllers | **new** `vector<shared_ptr<const ControllerCurve>>`, at most one per ctl, sorted by ctl |

How clip operations treat controllers:
- **Move, copy, duplicate, cross-track move, clipboard**: travel with the clip by value of
  the vector, and the curves are shared.
- **Split at b**: each curve is cut. The left clip keeps points < b plus a point at b with
  the value there. The right clip gets a point at 0 with the value at b, then the rest
  shifted by −b.
- **Left trim / left-edge extend**: points shift with the clip start, like notes.
- **Join / consolidate**: curves are stitched per ctl. A gap inside the result gets the rest
  value when the source clip ended there.
- **Quantize**: no effect.
- **Session slot** clip (`SessionSlot::midi`): same field, loop-local beats in [0, L).

## Track (extended)

| Field | Change |
|---|---|
| automation lanes | target `MidiController = 5`, `deviceIndex = -1`, `paramIndex = ctl` |
| `Instrument::deliveredCtl[130]` *(audio-thread state, relaxed atomics, not persisted)* | last controller value delivered to the instrument (live, clip, later automation); NaN = none. It lives on the instrument rather than the `Track` because the instrument outlives graph snapshots. Used for the stop/seek release and read by `nota_track_delivered_controller` |

## Recording state *(engine)*

| Item | Thread | Shape |
|---|---|---|
| `RecordedCtl` queue | audio → message, SPSC `1 << 14` | `{ ctl, beat (absolute, or loop-local for slots), value, pass }` |
| live controller latest | MIDI/UI → message | `atomic<float> liveCtl_[130]`, `atomic<uint32_t> liveCtlTouch_[130]` |
| take ctl fold state | message | per ctl: pending raw points, last committed point, current replace range `[start, latest]`, pass of the range |

**Replace-range lifecycle** (per ctl, per take):
`idle` → first point of pass *p* → `open(start = beat)` → each fold removes old points in
[start, latest] and inserts the new ones → loop wrap closes the range at the loop end, and
record stop closes it at the stop beat with a junction point → `idle`.

## Persistence (format v22)

```text
MidiClipDto      { …, Controllers?: ControllerCurveDto[] }
SessionSlotDto   { …, Controllers?: ControllerCurveDto[] }   // slot's MIDI clip
ControllerCurveDto { Ctl: int, Beats: double[], Values: float[], Curves?: float[] }   // parallel arrays; Curves null when all linear; saved rounded to 1e-6 beat / 1e-4 value
AutomationLaneDto  { Target: 5, DeviceIndex: -1, ParamIndex: ctl, Points }   // existing DTO
```

- Loading v ≤ 21 means no controllers and no change.
- Unknown `Ctl` values (> 129) and reserved CCs are dropped with a load warning.

## Managed contracts

- `MidiController` static helper (Application): `PitchWheel = 128`, `ChannelPressure = 129`.
  It also provides `Name(ctl)` (standard CC names), `ShortName(ctl)`, `IsSwitch(ctl)`,
  `Range(ctl)` and `IsReserved(ctl)`.
- `AutomationTarget.MidiController = 5`.
- `IAudioEngine`: `GetMidiClipControllers(track, clip) → int[]` and
  `GetMidiClipController(track, clip, ctl) → NotaAutomationPoint[]`.
  `SetMidiClipController(…, points)` does the set, with 0 points meaning remove. The
  same calls exist for session slots, plus `SendController(track?, ctl, value)`.
