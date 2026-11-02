# Bug Fix: high CPU and dropouts with four Kontakt instances

- **Slug**: kontakt-cpu-dropouts
- **Fixed**: 2026-11-02
- **Assessment**: ./assessment.md
- **Status**: partial (remediation step 1 applied; steps 2–3 are follow-ups by agreement)

## Summary

A hosted plug-in instrument is now rendered once per block, its notes, controllers and
expression stamped with their sample offsets in the plug-in's `MidiBuffer`, instead of one
`processBlock` per stretch between MIDI events. On the reporter's project this cuts the
callback's tail (p99 −19 %, late 64-frame blocks 135 → 10 over three 40 s rounds). Also fixed,
on the user's request in the same branch: replaced engine graphs were never freed.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/devices/Instrument.h` | modified | `takesTimedEvents()` (default false) + `eventOffset`, the offset of the event being delivered |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | `playEvents`: for a timed instrument, set `eventOffset` per event and render the block once; events on the block's end go after the render at offset 0 (as the segmented path); live expression still after the offset-0 notes. `timedEventsSelfTest()` |
| `src/native/nota.engine/pluginhost/hosting/PluginInstrument.{h,cpp}` | modified | `takesTimedEvents() = true`; note/CC/pressure/wheel/aftertouch events go in at `eventOffset` |
| `src/native/nota.engine/src/engine/Engine.{h,cpp}` | modified | leak: `retired_` + `liveGraph_` → `SnapshotHistory<Graph> graphHist_` (a replaced graph is freed two renders later, on the message thread); `graphRetireSelfTest()` |
| `src/native/nota.engine/src/engine/Engine_Session.cpp` + `Engine.h` | modified | `recordSlotHold_` keeps the recording slot's `SessionPlayer` alive (the audio thread dereferences `recordSlotPlayer_`; it was only safe while graphs were never freed) |
| `src/native/nota.engine/src/abi/nota_engine_{automation,core}.cpp`, `include/nota/nota_engine.h` | added | `nota_engine_timed_events_selftest`, `nota_engine_graph_retire_selftest` |
| `src/managed/Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}.cs` | added | bindings |
| `tests/Nota.SmokeTest/Program.cs` | added test | both self-tests in the xrun section |
| `CHANGELOG.md` | modified | two `Fixed` entries |

## Diff Highlights

```cpp
if (timed) {
    if (at >= frames) break; // after the render, at the next block's start (as segmented)
    if (at > 0) liveAt0();   // live input sits after the offset-0 notes
    inst.eventOffset = at;
} else {
    renderTo(at);
}
```

## Tests Added or Updated

- `nota_engine_timed_events_selftest` — a probe instrument: timed → exactly one 64-frame render,
  events at offsets 0/10/10/20 in segmented order (off before on at a tie, live expression
  after the offset-0 notes), the end-of-block note-off after the render at 0, `eventOffset`
  back to 0; untimed → renders of 10/10/44 with every event at 0 (unchanged behaviour).
- `nota_engine_graph_retire_selftest` — a published graph survives its replacement until two
  renders have passed, then is freed at the next publish.

## Local Verification

- `scripts/build.sh` → engine + managed build; smoke test: 4778 PASS, 2 FAIL — both
  `RenderPoolTests` "overloaded" checks (late blocks with render threads). They fail the same
  way on an untouched `origin/develop` engine on this machine (75–111 late blocks), so they are
  pre-existing / machine load, not this change. "adding and removing 48 tracks while blocks
  render: no crash" passes with graphs now freed.
- Bench (`/private/tmp/nota-bench-436/bench.cpp`): the four Kontakt 8 AU tracks with their saved
  state and clips from `kontakt_problem.nota`, played through `nota_engine_render_device_block`
  at 64 frames / 44.1 kHz (period 1.451 ms), paced like a device, 40 s per run, alternating:

  | round | build | mean ms | p99 ms | p99.9 ms | late blocks |
  |---|---|---|---|---|---|
  | 1 | develop | 0.767 | 1.088 | 1.463 | 32 |
  | 1 | fix | 0.697 | 0.881 | 1.029 | 0 |
  | 2 | develop | 0.768 | 1.095 | 1.419 | 20 |
  | 2 | fix | 0.710 | 0.896 | 1.201 | 10 |
  | 3 | develop | 0.729 | 1.100 | 1.566 | 83 |
  | 3 | fix | 0.705 | 0.870 | 0.957 | 0 |

  One earlier 60 s pair run fix-first read 77 (fix) vs 67 (develop) late blocks — noise is real
  at this load; the p99 drop held in every run. The mean also shows the "after a while" shape
  from the report: ~0.59 ms in the first 10 s, ~0.77 ms after (Kontakt's voices / streaming
  warming up), not a growth in Nota.
- Not verified in the app with a live device; not measured with the leak fix in a long session.

## Deviations from Assessment

- Scope expanded on the user's request: the `Engine::retired_` leak (listed as out of scope in
  the assessment) is fixed in the same branch, under its own approved issue #437.
- The assessment guessed Kontakt's per-call overhead dominates; measured, the mean barely moves
  (−3…−9 %) — the gain is in the tail (blocks with many events). Four instances still take
  ~50 % of a 64-frame period on one thread, so remediation step 2 (plug-in tracks on the render
  pool) is what removes the single-core ceiling.

## Follow-ups

- Step 2: let plug-in tracks render on the pool (one thread per instance per block), with the
  pool workers joined to the device's `os_workgroup` / RT policy. Measure with the same bench.
- Step 3: check `NOTA_DEBUG_PLUGIN_BUSES=1` for Kontakt's enabled outputs; enable only the main
  bus unless something routes another.
- Racks: a `PluginInstrument` inside an Instrument Rack chain is still driven by the rack in
  segments — the rack could pass timed events through too.
- Pre-existing flaky `RenderPoolTests` overload checks on this Mac.
