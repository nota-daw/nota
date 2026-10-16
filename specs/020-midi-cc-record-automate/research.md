# Research: Record and automate MIDI CC

Findings from the code as of `develop` @ d724fb5, and the decisions they lead to.

## What the code does today

- **Live decode** (`MpeInput.h`): on a non-member (ordinary or master) channel the wheel,
  channel pressure and CC74 become `mpeExpression(-1, dim)`. CC64 becomes
  `mpeExpression(-1, ExprSustain, 0|1)`, **thresholded at 64**. Every other CC is
  dropped, **CC1 included**. Poly aftertouch (0xA0) becomes per-note pressure on any channel.
- **MIDI Learn** (`Engine.cpp` ~L104): every CC is copied to `midiControl_` **in addition
  to** the MPE decode, so a learned CC already reaches both. The clarification "Learn CC is
  also recorded and passed on" needs no change to MIDI Learn.
- **Instruments**:
  - `Instrument::noteExpression(pitch, dim, value)`. Built-ins handle dims 0–2, and Keys
    and Physical handle dim 3 (sustain).
  - `RackInstrument` (and therefore `DrumRack`) forwards to every chain.
  - `PluginHost.cpp` turns pitch −1 into pitch wheel, channel pressure, CC74 and CC64
    (`value >= 0.5 ? 127 : 0`), always on channel 1.
- **Mod wheel on built-ins** is a *parameter*, not a controller: `ModWheel` in BassSynth,
  OperatorSynth, Consort (also a mod-matrix source) and others, reachable only by MIDI
  Learn or automation.
- **Recorded MPE**: `captureLiveExpr` records per-note dims for notes being captured;
  instrument-wide entries are skipped. `RecordedExprQueue` goes to `pendingExpr_` and then
  to `attachRecordedExpr`, which thins with RDP (`thinExprCurve`). Playback goes through
  `noteExprEvents` into `ExprEv{off, pitch, dim, value}` every 64 samples while moving,
  merged with note events in `playEvents`.
- **Routing**: `computeBlockMidi` fills `blockExpr_` per track. A "MIDI In" destination
  merges its source's `blockExpr_` in `renderInstrumentRaw`, so anything in the `ExprEv`
  stream reaches routed instruments for free.
- **Clip model**: `MidiClip` has notes plus `velocityEnvelope` and `volumeEnvelope`
  (`AutomationLane`). It is copied by value in `cloneTrack`, which is how undo works.
- **Automation**: `AutomationTarget` 0–4. `applyAutomation` runs per block on the audio
  thread. Writing goes through `beginAutomationWrite` / `sampleAutomationWritesAt` on the
  message thread, latched for hardware controls.
- **Recording fold-in** happens in `Engine::poll()` (message thread):
  - Notes move the clip's left edge back for pickups.
  - Replace mode removes earlier-pass notes. A pass replace is triggered only by a fresh
    **note** (`triggerPassReplace`).
- **Persistence**: C# `ProjectService` at format v21. Clip envelopes go through
  `GetMidiClipEnvelope` and `SetMidiClipEnvelope`.

## Decisions

### R1 — One controller id space

**Decision**: `ctl` 0–127 = that CC; 128 = pitch wheel; 129 = channel pressure
(`NOTA_CTL_PITCH_WHEEL`, `NOTA_CTL_CHANNEL_PRESSURE`). Values are normalised:
- CC: 0..1 (data / 127).
- Wheel: −1..+1 (centre 0).
- Pressure: 0..1.

**Rationale**: one integer addresses every controller in the clip, the ABI, automation
(`paramIndex`) and the UI picker. The wheel and pressure are not CCs in MIDI, but users
treat them as controllers.

**Alternatives**: separate enums per kind (more ABI surface); raw 14-bit / 7-bit integers
(pushes MIDI encoding into the UI).

### R2 — `Instrument::controller(ctl, value)` as the single entry point

**Decision**: add `virtual void controller(int32_t ctl, float value)` to `Instrument`.
- **Default**: wheel → `noteExpression(-1, ExprBend, v)`, pressure → `(-1, ExprPressure)`,
  CC74 → `(-1, ExprSlide)`, CC64 → `(-1, ExprSustain, v)`. Built-ins and racks keep
  working unchanged.
- **RackInstrument**: forwards `controller` to its chains.
- **PluginHost**: overrides it and sends the exact message (14-bit wheel, 7-bit CC,
  channel pressure). CC64 keeps its real value.
- **Built-ins with a `modwheel` parameter**: map CC1 to it.
- **Sustain** becomes continuous 0..1 on the wire. Instruments already threshold at 0.5
  (`Keys.h` L191), so half-pedal values reach plug-ins intact and built-ins see on/off.

**Rationale**: FR-003 (plug-ins get all controllers, built-ins act on what they
understand) and the clarification that half-pedal values are kept. The issue explicitly
names CC1 for built-ins, and the existing *Mod Wheel* parameters are the natural target.

**Alternatives**: more `ExprDim` values (bloats `VoiceExpr` and per-note arrays); CC1 as a
new voice-level modulation in every synth (out of scope, see spec Assumptions).

### R3 — Live decode emits controllers

**Decision**:
- `MpeInput::Sink` gains `mpeController(ctl, value)`. On non-member channels the wheel,
  pressure and **every** CC (except RPN/NRPN/data-entry 6/38/98–101 and channel-mode
  120–127) go there.
- CC64 goes there on any channel, as today.
- Per-note member-channel behaviour is unchanged.
- The engine carries live controllers as `MidiEvent{expr = kCtlDimBase + ctl, pitch = -1}`
  through the existing coalescing in `drainLiveMidi`.
- `nota_engine_note_expression(-1, dim)` (Remote, gamepad) maps to the same controllers.

**Rationale**: FR-004 (all CCs live to plug-ins). Recording and live use one path, and
coalescing per (track, ctl) keeps a streaming wheel from backing up the queue.

### R4 — Clip storage: shared immutable curves

**Decision**: `MidiClip::controllers` is a small vector of
`shared_ptr<const ControllerCurve>`; each curve is `{ ctl, std::vector<AutomationPoint> }`.
- Times are clip-local beats. They may be negative for pickups, like notes (spec 017).
- **Switch controllers** (CC64–69) hold their value until the next point (step). Every
  other controller is linear between points, and `curve` shaping is honoured as in
  automation.
- **Before the first point** the curve has no value: playback sends nothing, and the
  controller keeps what it had. A pedal pressed on beat 1 is not down at the clip start.
- **Playback placement**: recorded points sit on block starts, because capture is
  block-timed. Each point is placed on a whole sample with 1e-6 sample of slack, and the
  clip-entry check has half a sample of slack. Floating-point noise could otherwise drop
  a point between two blocks (found in PR 1 by the Session slot test).

**Rationale**: `cloneTrack` runs on every recording poll and every edit. Sharing curves
makes a clone O(curves), not O(points). `AutomationPoint` is already on the ABI
(`NotaAutomationPoint`), so get/set needs no new struct. Step semantics give exact sustain
blocks.

**Alternatives**: `AutomationLane` by value (copied per poll, which is too heavy for
recorded streams); per-note storage (wrong for instrument-wide data).

### R5 — Recording: capture queue, incremental thinning, touched-range replace

**Decision**:
- **Audio thread**: when `rec && timed`, each coalesced instrument-wide controller for the
  record track pushes `RecordedCtl{ctl, absBeat, value, pass}`. Session takes use
  loop-local beats, as notes do.
- **Take start**: controllers whose current live value is off rest (pedal down, wheel off
  centre, any CC already touched) are seeded at the take's first moment, so a held pedal
  is recorded.
- **Routed capture**: `renderInstrumentRaw` with `captureRouted` also records the
  source's controller `ExprEv`s.
- **Message thread (`poll`)**:
  - Per ctl, the pending raw points are folded into the take clip. Switch controllers are
    de-duplicated. Linear ones are RDP-thinned with the tolerances below, anchored on the
    last committed point, so the take stays editable while it grows.
  - The clip's left-edge move for pickups shifts controller points along with notes.
- **Replacement rule (clarifications 1–2)**:
  - In Overdub, and in a pass without notes in either mode, the first point of `ctl` in
    pass *p* opens a replace range at its beat.
  - Each fold removes existing points of `ctl` in `[rangeStart, latestBeat]` and inserts
    the new ones.
  - At pass end (loop wrap) the range closes at the loop end. On record stop (no loop) it
    closes at the stop beat with a junction point that restores the old curve's value
    there.
  - Controllers never call `triggerPassReplace`, so a pedal-only pass in Replace mode
    keeps the notes.
  - In Replace mode, once notes trigger a pass replace, the earlier passes' controller
    points for this take are removed too: the whole pass is replaced.
- **Thinning tolerances** (settled in PR 1): 0.008 for CC and pressure (about one 7-bit
  step), 0.012 for the wheel (about 1% of its travel). Capture is block-timed, so finer
  tolerances only kept the block grid's jitter: a 41-step ramp kept 25 points at
  0.002 and keeps 2 now.
- **Junctions**: a replaced range meets the old curve in a step. The old value holds to
  `kCtlJunction` (1/1024 beat) before the range. At a close with old points after it, the
  range's last value holds to `kCtlJunction` before the close point.
- **Held-back tail**: a poll's run is thinned with the range's last committed point as
  the anchor. For a moving (linear) controller, the last thinned segment stays pending
  until the next poll, so thinning spans polls. The tail is committed in full at a range's
  end, at record stop and when a take ends otherwise (`flushCtlTake`: Session slot
  finalise, disarm). It is also capped at 512 points. Without this, every ~30 Hz poll kept
  a point: a 2-minute wobble was 7,502 points, and is 1,690 with it.

**Rationale**: FR-002, FR-009, FR-021 and SC-005.

**Alternatives**: thin only at record stop (the live take would hold raw block-rate
points, and every poll's clone and republish would grow); merge passes (rejected in
clarify).

### R6 — Playback: controllers ride `ExprEv`

**Decision**:
- **Events**: in `gatherInstrumentNotes` (arrangement) and `renderSessionSlotRaw`, each
  active clip's curves emit `ExprEv{off, -1, kCtlDimBase + ctl, value}`:
  - at each point inside the block;
  - every 64 samples while a linear curve moves;
  - at offset 0 on `chaseNotes_` (play start, jump, loop wrap) and when the playhead
    enters the clip, with the value at that beat.
- **Dispatch**: `playEvents` sends dims ≥ `kCtlDimBase` to `inst.controller()`.
- **End of clip (FR-006)**: when a clip that moved CC64 or the wheel ends, or the transport
  stops, the engine sends CC64 = 0 and wheel = 0. On stop it sends them to every
  instrument whose last sent value is off rest. The reset sorts ahead of a following
  clip's chase at the same offset.
- **Precedence (clarification)**: a ctl that has a track automation lane with points is
  skipped in clip emission.
- **Routed instruments, racks and Drum Racks**: get controllers through the existing
  `blockExpr_` merge and `RackInstrument::controller`.

**Rationale**: no new buffers or merge logic; timing matches recorded MPE.

### R7 — Track automation target `MidiController = 5`

**Decision**:
- **Lane shape**: `AutomationTarget::MidiController` with `paramIndex = ctl`. Values use
  the controller's own range (wheel −1..1, others 0..1).
- **Playback**: `applyAutomation` sends `instrument->controller(ctl, v)` only when it
  differs from a per-track `lastCtl_[130]`, which is preallocated, NaN-initialised and
  reset on track clone.
- **Recording**:
  - The MIDI thread stores each live controller's latest value and a touch counter in
    fixed atomics.
  - `poll()` starts a latched `beginAutomationWrite(track, 5, -1, ctl)` when automation
    record is on, the transport rolls, and the armed record track has a lane for that ctl.
  - `currentTargetValue` reads the latest live value.
  - While a lane exists for a ctl, the take does not capture that ctl into the clip:
    automation wins.
- **Scope**: instrument tracks only.

**Rationale**: FR-014–017; reuses M9-C writing and the existing lane editing and undo.

### R8 — C ABI surface

See [contracts/c-abi.md](contracts/c-abi.md). It covers:
- clip and slot controller list, get and set (set with 0 points removes the curve; one
  undo step);
- live `nota_engine_controller` / `nota_track_controller`;
- target 5 on the existing automation functions;
- `nota_engine_midi_ctl_selftest`.

### R9 — Persistence v22

**Decision**:
- `MidiClipDto.Controllers` and the Session slot clip DTO each get
  `ControllerCurveDto[]?`. A `ControllerCurveDto` is `{ int Ctl, double[] Beats, float[] Values,
  float[]? Curves }`: parallel arrays, with `Curves` null when every segment is linear.
  Values are rounded on save to 1e-6 beat and 1e-4 value. An object per point (`AutomationPointDto`)
  cost about 150 bytes each: a 2-minute wobble grew the project by 244 KB that way, and by
  84.5 KB as arrays (SC-005).
- Automation lanes with target 5 save through the existing `AutomationLaneDto`
  (`ParamIndex = ctl`).
- No migration is needed: absent means none. The format constant goes to 22. The DTOs carry `[JsonExtensionData]`, so an older Nota keeps the field when it re-saves.

### R10 — UI

**Piano roll** (see [contracts/ui-lanes.md](contracts/ui-lanes.md)):
- A fifth lane tab shows the chosen controller's short name (default "Sustain"). Clicking
  it opens a picker:
  - Sustain, Mod wheel, Pitch wheel, Channel pressure, Expression (CC11);
  - "Other CC" with all 0–127 by number and the standard name;
  - controllers with data marked with a dot and listed first, the same affordance as the
    automation picker.
- The lane is clip-wide, drawn over the whole clip, not per note:
  - switch controllers as blocks;
  - others as a curve with points.
- Edits use the same gestures as the MPE lanes and reuse the arrangement automation
  drawing helpers. Each gesture is one undo step through `nota_midi_clip_ctl_set`.

**Automation picker**: a "MIDI controllers" submenu on instrument tracks, with the same
list and dots.

Studio rules apply: sentence-case labels, neutral ink, accent only for the hot point or
selection.

### R11 — Already satisfied: poly aftertouch

`MpeInput` already turns 0xA0 into per-note pressure on any channel, and `captureLiveExpr`
already records per-note dims for captured notes. So poly aftertouch from a non-MPE
keyboard is already stored as each note's pressure. The plan adds a smoke check, and a
FEATURES/docs wording fix if needed, instead of new code.
