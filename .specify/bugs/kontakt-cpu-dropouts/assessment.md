# Bug Assessment: high CPU and dropouts with four Kontakt instances

- **Slug**: kontakt-cpu-dropouts
- **Created**: 2026-11-02
- **Source**: https://github.com/nota-daw/nota/issues/436 (host: github.com, allowlisted; issue written in this session from the user's report) + reproducing project `~/Documents/Nota Projects/kontakt_problem.nota`
- **Verdict**: valid
- **Severity**: high (hosting several heavy sampler plug-ins — a core DAW use case — drops out on a top-end machine; affects every hosted plug-in instrument, not only Kontakt)

## Report (summarized)

> **High CPU load and audio dropouts with four Kontakt instances** — With four instances of
> Kontakt loaded (one per instrument track), CPU load climbs very high as soon as playback
> starts and the audio drops out. Expected: four Kontakt instances play without dropouts, as
> in other DAWs.

Follow-up from the reporter: the dropouts do not start at once but after some time of playback.

Environment: Nota 0.58.1 (develop), macOS 26.6.2, Apple M4 Max. `audio.json`: built-in
speakers out, built-in microphone in, **64-frame buffer** at 44.1 kHz (period ≈ 1.45 ms).

The project: four instrument tracks, each `AudioUnit-Kontakt 8`, each with one 16-beat clip of
114 short notes (1/64 beat and up) on four pitches; two clips start at beat 0 and two at beat 1,
so the tracks' events fall on different samples. Loop on, 120 BPM, all four tracks
record-armed. Today's log (`~/Library/Application Support/Nota/logs/nota-20261102.log`) shows
~100 `Audio dropout` warnings in 16:29–16:32, sparse at first, then several per second.

## Symptom

Playing the project, the audio callback overruns more and more often until there are several
dropouts a second. Expected: four Kontakt instances at 64 frames on an M4 Max play cleanly.

## Reproduction

1. Set the audio buffer to 64 frames (Preferences → Audio).
2. Open `kontakt_problem.nota` (four Kontakt 8 AU instances, dense short-note loops).
3. Play with loop on; watch the CPU meter / dropout counter for a minute.
4. Dropouts begin after a while and become frequent.

[NEEDS CLARIFICATION: does it also happen at 256/512 frames, and with the tracks disarmed?]

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine_Render.cpp:170` `playEvents()` — renders an
  instrument in stretches between MIDI events: `renderTo(off)` → `inst.render(&dst[cursor*2],
  to - cursor)` before every note-on/off and expression event. Right for built-in synths
  (events land between sub-renders), but for a hosted plug-in each stretch is a whole
  `processBlock` call.
- `src/native/nota.engine/pluginhost/hosting/PluginInstrument.cpp:63` `render()` — one
  `plugin_->processBlock(block, pending_)` per call; `noteOn/noteOff/controller` (`:37–60`)
  add every event at sample 0 of the *next* call. So a 64-frame block with k events becomes up
  to k+1 `processBlock` calls with buffers as small as 1 frame, each with the full per-call
  overhead of the plug-in (Kontakt: voice/stream bookkeeping, internal threading, AU render
  setup). The loops here put 2–4 events into many blocks.
- `src/native/nota.engine/pluginhost/hosting/PluginInstrument.cpp:169` `prepare()` →
  `enableEveryBus()` (`PluginCommon.cpp:32`) — every output bus of the plug-in is enabled, and
  JUCE's AU wrapper (`juce_AudioUnitPluginFormatImpl.h:1329–1435`) copies the whole buffer and
  runs one `AudioUnitRender` per enabled bus on *every* `processBlock`. For a multi-out
  instrument (Kontakt exposes up to 16 stereo outs) each of the many sub-block calls above
  renders all of them, though Nota only mixes the main bus.
- `src/native/nota.engine/src/engine/Engine_Render.cpp:2341–2347` — leaves hosting a plug-in
  (instrument or any un-bypassed plug-in device) are never `eligible` for the render pool
  ("hosted plugin instances are not thread-safe across threads"). All four Kontakt instances
  render serially on the CoreAudio callback thread, whatever the load, so their summed cost
  must fit one 1.45 ms period. The render pool (`AudioThreadPool`, P-cores, adaptive
  `rtParallel_` join) exists but does nothing for this project.
- `src/native/nota.engine/src/engine/AudioThreadPool.cpp:49` — workers are plain
  `std::thread`s: no `THREAD_TIME_CONSTRAINT` policy and no join to the device's
  `os_workgroup` (`kAudioDevicePropertyIOThreadOSWorkgroup`); there's no use of either
  anywhere in the engine. Relevant once plug-ins render on the pool: macOS may run
  non-workgroup threads at low clocks / on E-cores, so pool work can miss a 1.45 ms deadline.

Ruled out by reading the code (MIDI path traced end to end): no lost note-offs at the loop
seam (`seamOffs_` emits offset-0 offs for notes crossing the seam, chase re-triggers sort
after them), none for sub-block notes, no duplicated note-ons; nothing on the plug-in path
allocates or grows per block while the tracks are only armed. Loop wraps send no
`allNotesOff` by design (#29), so release tails ring across the seam — normal, but it feeds
Kontakt's voice count. Recording (not just arming) would add a growing `clip.notes` scan per
block; the report doesn't involve recording.

## Root Cause Hypothesis

Two compounding costs on a single real-time thread. (1) Every hosted plug-in instrument is
called once per MIDI-event stretch instead of once per block, multiplying Kontakt's per-call
overhead by the event density and feeding it sub-block buffers; (2) all plug-in tracks are
excluded from the render pool, so four instances add up serially inside one 64-frame period.
"After some time": Kontakt's per-call cost grows with the number of sounding voices (release
tails of the dense loop accumulate, disk-streaming pre-load grows), so the serial sum starts
under the period and creeps over it. Confidence: **high** for (1) and (2) being real and
costly, **medium** that together they fully explain the report until measured.

## Proposed Remediation

**Preferred** — two independent changes, measured separately:

1. *One `processBlock` per block for hosted plug-in instruments.* Add a block-MIDI path for
   instruments that take sample-accurate events themselves (e.g.
   `Instrument::takesTimedEvents()`; `PluginInstrument` returns true). In `playEvents`, for
   such instruments send note-on/off, controllers and expression with their sample offset
   (new offset-aware entry points, or the existing `MidiEv`/`ExprEv` arrays) into the
   plug-in's `MidiBuffer`, keep the sustain-pedal bookkeeping (`sendNoteOn/Off`) intact, then
   call `render(dst, frames)` once. `PluginInstrument` stores the offset in `addEvent(msg,
   off)`; live/GUI events stay at 0. Bypass fade-out and the `live()` ordering (offset-0 notes
   before live expression) keep their semantics. Built-in instruments are untouched.
2. *Let plug-in tracks use the render pool.* A plug-in instance must not be processed by two
   threads at once, but it may be processed by different threads in different blocks — every
   major host does this (AU/VST3 make no same-thread guarantee for render). Make leaves whose
   plug-ins are confined to that leaf eligible again; keep excluded the cases with real
   cross-thread sharing (shared record queues, `midiFrom` routing, plug-in editor-driven
   state if any is touched off the leaf). Give pool workers real-time scheduling: join the
   output device's `os_workgroup` (macOS 11+) on each worker for the duration of a dispatch,
   or at least set `THREAD_TIME_CONSTRAINT_POLICY` matching the period. Revisit the
   `rtParallel_` join heuristic: plug-in work is far more variable than built-in DSP, so the
   1/16-smoothed single-thread estimate reacts late (dozens of late blocks) — consider
   joining on the first late block when plug-in leaves are present.

3. *Render only the buses Nota uses.* If the bus check (Open Questions) shows Kontakt with
   many outputs enabled, enable the main output only, and further buses only when something
   routes them. Cheap, and multiplies with (1).

**Alternatives**:
- Only (1): smallest, safest change; likely removes most of the overhead for dense MIDI, but
  four heavy instances at 64 frames stay single-threaded — the CPU ceiling remains one core.
- Larger internal block for plug-ins (render plug-ins at a fixed ≥128-frame block with added
  latency): what some hosts do for "anticipative" processing, but adds latency and a big design
  change; out of scope for this bug.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine_Render.cpp` (`playEvents`, leaf eligibility)
- `src/native/nota.engine/src/devices/Instrument.h` (timed-event capability)
- `src/native/nota.engine/pluginhost/hosting/PluginInstrument.{h,cpp}` (offset-aware events)
- `src/native/nota.engine/src/engine/AudioThreadPool.{h,cpp}`, `audio_io/macos/CoreAudioBackend.mm`
  (workgroup / RT policy for workers)
- `tests/Nota.SmokeTest/*` (new checks)

**Tests to add or update**:
- Engine test with a stub plug-in-like instrument that counts `render` calls and records
  event offsets: a block with several notes → exactly one render call, offsets preserved,
  note-off after note-on at the same offset, pedal holds still work.
- Render-pool test: two plug-in-hosting leaves render on different workers and the mix is
  bit-identical to the serial render.
- Before/after benchmark (nota-pr evidence): N instances of a heavy AU/VST3 at 64 frames with
  a dense loop, late blocks per minute and callback CPU.

## Risks & Considerations

- Plug-ins with thread-affinity assumptions (rare, but some older AU/VST2-era code) could
  misbehave when rendered from pool threads; keep a per-plug-in opt-out (or a global
  "render plug-ins on the audio thread" preference) as a safety valve.
- Timed events change what a plug-in hears inside a block only by moving events from
  "start of a sub-block" to their exact offset — audibly identical or better; but the
  expression step (`kExprStep` 64-frame curves) now reaches the plug-in as CC/aftertouch
  events with offsets rather than per-stretch values — check MPE/expression plug-ins.
- `os_workgroup` join needs the device's workgroup re-fetched on device change; workers
  must leave it when parked.
- The 64-frame buffer is aggressive; even fixed, the reporter's setup may need 128 frames
  for four Kontakt instances with heavy libraries — the benchmark will show where it lands.

## Open Questions

- [NEEDS CLARIFICATION: reproduce at 128/256 frames — does it disappear?]
- [NEEDS CLARIFICATION: does disarming the tracks change anything (rules out the
  record/monitor path)?]
- [NEEDS CLARIFICATION: Kontakt's own "Multiprocessor support" setting in these instances.]
- [NEEDS CLARIFICATION: how many output buses these Kontakt 8 instances expose — run with
  `NOTA_DEBUG_PLUGIN_BUSES=1`. If many, enable only the main bus unless a track actually
  routes another one (a third, cheap fix).]
- Out of scope, seen in passing: `Engine::publishRaw` appends every published graph to
  `Engine::retired_` (`Engine.cpp:702`) and nothing erases it — memory grows with every edit
  and every recording poll. Separate issue.
