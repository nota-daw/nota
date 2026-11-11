# Bug Assessment: Monitoring an instrument track through an audio track crackles

- **Slug**: 563-monitor-crackle
- **Created**: 2026-11-11
- **Source**: https://github.com/nota-daw/nota/issues/563 (host: github.com — allowlisted)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

> **Monitoring an instrument track through an audio track crackles** (#563, label `bug`, `approved`)
>
> An audio track can take another track as its input. When that input is an instrument track and
> the audio track's monitoring is on, what you hear through the audio track crackles (clicks,
> distortion). Recording the same input works: the recorded take is clean. So the problem is in
> the monitoring path, not in how the input is captured.
>
> Steps: instrument track + audio track; audio track input = the instrument track; monitoring on;
> play notes → crackle. Arm and record → the clip plays back clean. Nota 0.59.2 (develop), macOS
> Apple Silicon.

## Symptom

An audio track monitoring another track (Input = a track, Monitor "In") plays the source with
clicks / distortion, while recording the same source into a take is clean. Expected: the monitored
signal is the source's post-fader output, delayed by a constant amount, with no discontinuities.

## Reproduction

1. Add an instrument track (any built-in synth) and an audio track.
2. Set the audio track's input to the instrument track; turn its monitoring on.
3. Play notes on the instrument → the monitored signal crackles.
4. Arm and record → the take is clean.

Condition found in the code (see below): the crackle needs render segments of **varying length**.
The reporter's machine outputs to a Bluetooth device (`outputDeviceUid` is a MAC address in
`audio.json`); Nota does not set a Bluetooth device's nominal rate
(`CoreAudioBackend.mm:167`), so the AUHAL converts the 44.1 kHz client stream to the device rate
and asks for a varying `inNumberFrames` per callback. The same happens on any device running at
another rate, and inside a callback at every loop seam / count-in end (`processBlock` splits the
segment there) — so it also clicks once per loop pass on a fixed-size device while looping.
[NEEDS CLARIFICATION: whether it also crackles on a wired device at the project rate with the
transport stopped — the code says it should not.]

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine_Render.cpp:2433` `Engine::renderMonitorInput()` — for a
  track source it copies `frames` frames out of `routeBus_[slot]`, the source's post-fader tap from
  the *previous* segment.
- `Engine_Render.cpp:3348` (end of `mixGraph`) — `routeBus_[s].swap(routeBusNext_[s])`: the tap is
  double-buffered per segment, with no record of how many frames the previous segment wrote.
- `Engine_Render.cpp:2655` — `routeBusNext_` is cleared for `frames` only, so the frames past the
  previous segment's length hold whatever an older segment left there.
- `Engine_Render.cpp:2269` `processBlock()` — splits a callback at loop seams / count-in end into
  segments of different lengths.
- `src/audio_io/macos/CoreAudioBackend.mm:59` / `OutputRouting.h:137` — the device callback length
  passes straight through as the segment length (chunked at 4096).
- Recording path for comparison: `Engine_Render.cpp` Pass 1 pushes the source's post-fader samples
  of the *current* segment into `internalQueue_` (`w.tapRec`) — a continuous stream, hence clean.

## Root Cause Hypothesis

The monitor reads "the previous segment's tap, `frames` long". That is a clean one-block delay only
while every segment has the same length. When the length changes (AUHAL rate conversion gives e.g.
470/471/470… frames; a loop seam cuts one segment short), a longer segment reads past the end of
what the previous one wrote — stale samples from an older segment — and a shorter one drops the
tail of the previous segment. Each such seam is a discontinuity: with a varying callback size it
happens on most callbacks, which is heard as crackle/distortion. The recording path is a FIFO of
the current segment's samples, so it never loses or repeats a sample. Confidence: **high** for the
mechanism (it follows from the code), **medium** that it is the reporter's exact trigger (Bluetooth
output fits, not reproduced on their device).

## Proposed Remediation

**Preferred**: give the monitor a continuous stream instead of a per-segment snapshot. Keep, per
route slot, a small ring of the source's post-fader tap: after Pass 1 (where the swap happens now)
append this segment's `frames` to the slot's ring; `renderMonitorInput` reads `frames` frames at a
fixed lag behind the write position. The lag is the largest segment length seen so far (one block
on a fixed-size device, as today; on a varying device the largest callback), so a read never
crosses unwritten samples and no sample is dropped or repeated. Capacity 2 × `kMaxBlock` frames
per slot (allocated up front, RT-safe). Reset ring + lag where the route table is re-derived or
the engine (re)starts. Only monitoring reads the ring; sidechain keys keep `routeBus_`.

**Alternatives**:
- Render a monitoring track after its source in the same segment and read `routeBusNext_` (zero
  latency, matches recording timing). Needs ordering across parallel/serial paths, groups and
  monitor→monitor chains; much larger change for a bug fix.
- Fix the general route-bus contract (record the previous length, zero-fill the rest): removes
  stale data but still drops/zero-pads samples at every length change — still clicks.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine.h` (ring + lag state)
- `src/native/nota.engine/src/engine/Engine_Render.cpp` (`renderMonitorInput`, tap publish)
- wherever the route table is re-derived (reset)
- `tests/Nota.SmokeTest/` — new test

**Tests to add or update**:
- Smoke test: instrument track playing a steady tone + audio track monitoring it; render offline in
  chunks of varying length (e.g. 470/471/333/512…) and assert the monitoring track's output is the
  source's output shifted by a constant lag (no sample lost or repeated) — fails before the fix.
- Same with constant chunks: unchanged one-block lag (regression guard).

## Risks & Considerations

- Real-time safety: ring buffers sized at construction, no allocation on the audio thread.
- Latency on a varying device becomes the largest callback seen (a few samples more than today's
  average); on a fixed-size device it is unchanged.
- Memory: 16 slots × 2 × 16384 × 2 floats ≈ 4 MB; could be sized to the slots actually used by
  monitors, or capped to the OutputStage chunk (4096) for live use.
- The same per-segment contract affects sidechain keys and the armed-track input meter; out of scope
  for #563 (detectors smooth it), worth a separate issue.

## Open Questions

- [NEEDS CLARIFICATION: does the reporter's case go away on a wired output at the project rate?
  Not needed for the fix — the test pins the mechanism.]
