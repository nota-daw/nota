# Research: Record a Session Performance into the Arrangement

Findings from the code on 2026-11-20 (`develop` at e0bdcd6c) and the decisions they lead to.

## R1 — Where a launch or stop really happens

**Found**: `SessionPlayer::maybeApply` (audio thread, `SessionPlayer.cpp`) consumes the queued
request when a quantum boundary falls in the block (or at once for quantize None, `kStopNow`, a
loop seam). It sets `playing` and resets `localBeats` to 0, or keeps it for Legato. It is called
once per track per block from the render loop in `Engine_Render.cpp` (~line 3214), which already
knows the block's `blockStart / spb` and the track. Follow actions and Repeat queue through the
same request (`queueIfIdle`), so they also pass through `maybeApply`. A cancelled launch
(specs/104) is withdrawn before it is consumed and never reaches it.

**Decision**: Report every applied request from that call site as a `LaunchEvent` on a new
SPSC ring (audio → message thread), the same way recorded notes travel (`recorded_`, drained in
`poll()`). Each event carries the track id, the new slot (−1 = stopped), the beat, the slot
position (`localBeats` after the apply: 0, or the Legato carry), the launch gain and the take
epoch. Events are pushed only while a pass runs (an audio-thread flag set by the take start
command), so the ring costs nothing otherwise. Pushing to a preallocated ring does not allocate
or lock (AR-6).

**The beat**: the request applies in the block that contains the boundary, so the block start
can sit up to one block before the grid beat. For a quantized request the event carries the
boundary beat (`ceil(blockStart / q) · q`, clamped inside the block), so written clips land
exactly on the grid (SC-002). For an unquantized one it carries the block start.

**Alternatives**: polling `playing` from the UI timer (≈30 Hz) — misses short plays and is
off by tens of ms; recording launch *requests* on the message thread — wrong for quantize,
follow actions, Repeat and cancels.

## R2 — What a played span turns into

**Found**: `sessionSlotToArrangement` (⌘⇧C) copies a slot as one arrangement clip, one pass of
its loop. Arrangement `MidiClip` has `startBeat` + `lengthBeats` and notes relative to the start;
`AudioClip` has `sourceOffsetFrames`, `lengthFrames`, warp markers and `warpPlayStart/End`.
**Arrangement clips do not loop** (`MidiClip::lengthBeats` comment: "for looping later; unused").
`midiSlice` and `audioSlice` (`Engine_Tracks.cpp`) cut any clip to a beat range, warp included.

**Decision**: A span `[a, b)` of slot `s` starting at slot position `p`, slot length `L`:

- **Instrument slot** → one `MidiClip` from `a` to `b`. Its notes, controller curves,
  articulations and per-note expression are the slot's, unrolled: repetition *k* adds the
  content shifted by `k·L − p`, cut to `[0, b − a)`. A note keeps its length across a repetition
  seam (the player lets it ring) and is cut at the span end; a note still sounding at the span
  start keeps its remaining part, its expression re-based. Each repetition's controller curve
  keeps its end value and steps back to its start value at the next repetition. The slot's
  velocity and volume envelopes are not copied: Session playback does not apply them (review).
- **Audio slot** → one `AudioClip` per repetition, back to back: the slot's clip placed at
  `a − p + k·L`, then `audioSlice`d to `[a, b)`. This reuses the tested warp/offset slicing and
  sounds exactly like the loop.
- Launch gain (velocity amount) is carried into the clip: `AudioClip::gain` multiplied; for MIDI,
  note velocities scaled the way the Session player scales them. [Checked in tasks — the session
  MIDI path applies `gain` to the output; if it scales velocities, the same factor is applied to
  written note velocities.]
- The slot content used is the slot as it is when the pass ends (spec assumption).

**Alternatives**: making arrangement clips loop — a far larger change to the arrangement model,
editing and export; out of scope. Unrolling audio into one rendered file — bounces, loses warp
editability.

## R3 — Replacing the Arrangement only where a clip played

**Found**: `splitClipsAtRange` and `deleteClipsInRange(trackIds, start, end, withAutomation)`
cut and remove clips over a beat range per track; paste with overwrite uses the same pair.

**Decision**: At the end of a pass, per written track and per span: cut the track's clips at
`a` and `b` and remove what lies between (clips only, `withAutomation = false`), then insert the
written clip(s). All done on one cloned track per track and published once (one snapshot per
track, AR-5).

## R4 — Armed instrument track that also plays a slot (clarification Q3)

**Found**: an Arrangement MIDI take overdubs into the arrangement clip spanning the playhead, or
opens one there (`startArrangementTake`); notes arrive through `recorded_` and are folded into
that clip in `poll()` (`foldMidiLane`); `takeNotes_` keeps the take's notes for loop replace.

**Decision**: The take records as today. At the end of the pass, before the span replace, the
notes and controller points the take recorded inside a span are lifted out of the take clip
and merged into that span's written clip at the same timeline beats (a moved controller follows
the take from its first move on). A track counts as recording if it was armed when the pass
began or is armed at its end, so disarming mid-pass keeps its notes. Notes outside the spans
stay in the take clip. Notes that existed before the pass are not take notes and are replaced
with the rest of the span (FR-004).

**Armed audio track** (as built): its slot writes nothing and its input take stands. With
monitoring on the slot is silent (`sessionActive` is false while `t.monitor()`); with monitoring
off the take is materialised before the pass writes, so writing the slot would cover the take —
the take wins, as an audio take replaces material today.

## R5 — One undo step, and discard / retry

**Found**: a take opens a `TakeAttempt` (`openTakeAttempt`, `Engine_TakeRetry.cpp`) with
`undoBase` and `baseTracks` — the pre-take state of every track the take may write
(`noteTakeTrack`, first write wins). `discardTake` restores those tracks and drops the take's
undo entries without a step. `beginUndoGroup / endUndoGroup` collapse the entries pushed
between them into the first.

**Decision**:

- The pass writes its clips inside the take attempt: `noteTakeTrack(id)` for every track before
  it is written (and the master for the Tempo lane), so ⌘. restores them (FR-011) with no change
  to `discardTake`.
- Undo: the pass opens an undo group when it starts (`beginUndoGroup` at take start) and closes
  it after the clips are written at the end, so the input takes, the automation written in the
  pass and the written clips are one entry (FR-010). Automation writes already push through the
  undo stack, so they fall inside the group. Check in tasks: a group spanning `poll()` cycles —
  if any path calls `clearUndoHistory` or opens its own group mid-pass, nesting keeps the depth
  right (`undoGroupDepth_`).
- Loop on (FR-012): a seam closes all open spans at the loop end and reopens the playing slots
  at the loop start; at the end, later passes over the same range replace earlier ones because
  spans are applied in time order and each one replaces what is under it.

## R6 — Scene tempo

**Found**: a scene tempo is applied on the message thread by `applySceneTempo()` when the
transport reaches `sceneTempoAt_` (`serviceSessionFollow`). `setBpm` refuses while a tempo map
exists ("the master Tempo lane leads", specs/099). The time signature is one project value.

**Decision**: When a pass is running and `applySceneTempo` applies a tempo while no Tempo lane
exists, record `(sceneTempoAt_, bpm)` plus the tempo before the pass. At the end of the pass,
if any were recorded, create the master Tempo lane (`addAutomationLane(kMasterTrackId,
Tempo)`) with the pass-start tempo from beat 0 and an instant step at every recorded beat. With
a Tempo lane already present nothing is recorded (the scene did not change the tempo). The lane
is written at the end, not live: writing it mid-pass would switch the transport to the tempo map
while recording.

## R7 — Record with nothing armed (clarification Q2)

**Found**: `startArrangementTake` returns false with `recordStartStatus_ = 1` when nothing is
armed; `TransportViewModel.OnRecordOnChanged` then auto-arms `RecordArmTarget` and retries, or
pops the button up with "arm a track". `setRecording(true)` has already turned automation
recording on.

**Decision**: In `startArrangementTake`, when nothing is armed and any track's Session player is
playing or has a queued launch, start a **performance-only pass**: open the take attempt, roll
the transport as a take would (count-in included), set the pass flag, and return true with
`recordStartStatus_ = 3` (new: performance only). The view model shows "Recording the Session
performance" for status 3. With the Session silent the old path (status 1 → auto-arm) is
unchanged.

## R8 — Record while a Session-only clock runs

**Found**: launching from stop runs the clock with `arrangementActive_ = false`;
`startArrangementTake` only activates the Arrangement when the transport was stopped.

**Decision**: unchanged — Record does not change what plays (spec edge case). Slots already
playing when the pass starts produce an opening `LaunchEvent` at the pass start beat with their
current position, emitted by the audio thread when it sees the pass flag rise.

## R9 — UI and other surfaces

- No new control: the existing Record button drives it (clarification Q1). The Arrangement
  refreshes on `TracksChanged` after Record stops, as for takes.
- New strings: the performance-only status message (EN in the app catalog; RU through the
  translations repo as with earlier features).
- MCP, Nota Remote, controllers: no change — they launch and record through the same engine
  calls (FR-017). MCP `start_recording` reports the new status like the app.
- Docs: the Session and Recording pages of the manual (nota-docs), FEATURES.md, CHANGELOG.
