# Research: Discard and Retry a Take

All findings come from reading the engine and app as of `origin/develop` (31c2efda).

## R1 — How a take reaches the project and the undo history today

- **Audio (Arrangement)**: the capture lives in `audioCaptureBuf_` (plus `capturePasses_` for
  loop passes) on the message thread; nothing reaches the graph until `stopAudioRecording()`
  materialises it with `republishWithTrack` — one undo step
  (`Engine_Recording.cpp`).
- **MIDI (Arrangement)**: `setRecording(true)` creates the take's clip with
  `republishWithTrack` when no clip spans the playhead (an undo step); `poll()` folds the
  notes / controllers / expression the audio thread queued (`recorded_`, `recordedCtl_`,
  `recordedExpr_`) into the clip with `republishWithTrackRaw` — no undo step
  (`Engine_Render.cpp`).
- **Automation record**: the record button also turns automation record on;
  `beginAutomationWrite` pushes one undo checkpoint per write session and grows lanes raw
  (`Engine_Automation.cpp`).
- **Session slot**: audio is captured through the same buffer and materialised into the slot
  in `stopAudioRecording()`; a fresh MIDI slot is created with `addSessionMidiClip` (an undo
  step, its count kept in `sessionCountInUndo_`) and grown raw in `poll()`.

**Decision**: a take attempt remembers, at its start, the *pre-take* `Track` objects of every
track it can write (record targets; tracks that start an automation write during the take are
added at that moment) and the undo/redo stacks' state. Discard republishes those tracks raw
(no checkpoint), truncates the undo entries pushed since the take started and restores the
redo stack.

**Rationale**: graph snapshots are immutable and cheap to keep (`shared_ptr<Track>`, AR-5);
restoring per track keeps edits on other tracks (FR-011); truncating the stack gives FR-003
exactly (the take never was).

**Alternatives considered**:
- *Plain `undo()` after a normal stop* — leaves a redo step (violates FR-003), materialises
  audio first (wasted work, and a loop take adds several take lanes before vanishing).
- *Restore the whole pre-take graph* — throws away edits made on other tracks during the take.
- *Wrap every take in an undo group* — changes the undo shape of ordinary takes; not needed.

Review of #486: the take's own checkpoints (its clip, its first lane write) are marked
(`UndoEntry::take`, via `TakePushScope`). A discard drops only those; checkpoints the user
pushed meanwhile stay, with the take's tracks put back in their saved graphs, so undoing them
later doesn't bring the take back. A track gets into the attempt before its first automation
write, armed or not.

Undo-depth trimming: `pushUndo` erases the oldest entry past `kMaxUndoDepth`; the take's base
index is decremented with it (as `undoGroupBase_` already is).

## R2 — Late MIDI from the audio thread

The audio thread queues a note when it is *released* (`Engine_Render.cpp`, live and routed
paths, loop-wrap split). A note released after the discard but timed in the old take would
otherwise land in the retried one.

**Decision**: a take epoch (`std::atomic<uint32_t>`) bumped by the discard; the audio thread
stamps `RecordedNote` / `RecordedCtl` / `RecordedExpr` with the epoch it read for the block;
`poll()` drops entries from an older epoch. Keys still held at the retry are seeded into the
new take by the existing "held across Record" logic (#56), from the new take's first moment.

**Rationale**: lock-free, no allocation on the audio thread (AR-6), one relaxed load per block.

Audio capture frames get the same fence (review of #486): each producer (input thread,
internal-resampling tap) pushes a marker frame the first time it writes after the capture epoch
moved, and loads the epoch before the capture gate; a retried take drops everything drained
before that marker. A Session MIDI retry adopts the new take epoch on the audio thread only
when its slot has relaunched, so notes and controllers played while it waits stay out and keys
held across the relaunch start with it.

**Alternatives**: draining the queues at discard time only — races with the block in flight.

## R3 — Where an Arrangement retry restarts

`Transport::stop()` returns the playhead to the start anchor (where Play was launched or the
last seek). A take started from stop has `recordStartedTransport_` with `recordReturnBeat_` =
its start.

**Decision** (clarified 2026-11-04): stop the transport; if the take started it, seek to
`recordReturnBeat_`; otherwise the stop has already returned to the anchor — where playback
started. Then start recording again *from stop* with the attempt's count-in policy: the count-in
plays again when the original attempt counted in; a punched-in take restarts rolling with no
count-in (the run-up is the context). Implemented by a `forceNoCountIn` argument to the
internal start path, not by changing `setRecording`'s public behaviour.

## R4 — Session slot retry

- Arrangement and Session takes are mutually exclusive in the engine (one capture buffer, one
  `recording_` flag; `poll()` gives the session target priority). FR-014 was reworded to say so.
- Session MIDI takes start via `launchSlot` → the slot player launches on the next launch
  quantum; `sessionSlotState` already reports 4 (recording) the moment `recording_` is set,
  even while the player is still pending — the issue comment's "pending, not recording" gap.
- Session **audio** takes start capturing immediately (no quantum), from fresh-take or retry.

**Decision**:
- Discard restores the slot from the pre-take track (only that slot's `SessionSlot`, so other
  slots' edits survive), drops the capture without materialising, and leaves every other
  player and the transport alone.
- Restart: MIDI → the existing `recordSessionSlot` path (launches on the next quantum; immediate
  when quantize is off). Audio → capture starts now, and a *retry start beat* is set to the next
  launch-quantum boundary; the drain notes the capture frame where the
  playhead crossed it (at the tempo of that moment) and `stopAudioRecording()` trims what came
  before, so the take starts on the grid.
- New slot state **5 = record pending**: `recording_` for this slot but MIDI player not yet
  playing it, or audio playhead before the retry start beat. Callers that test `!= 0` / `>= 2`
  keep working (5 is "active, not empty").

**Alternatives**: gating the input tap on the audio thread at the boundary — more moving parts
for the same result; the trim is sample-exact against the take's own clock.

## R5 — Disk and dirty state (FR-004, "clean stays clean")

Recorded audio stays in memory (`SampleBuffer`) and is written by `ProjectService` on save
from the graph; autosave also serialises the graph. A discarded take never enters the graph,
so it never reaches disk. `HasUnsavedChanges()` compares a content fingerprint, so restoring
the pre-take tracks makes a clean project clean again.

## R6 — Command surface

- No user-remappable shortcuts exist; commands live in `CommandCatalog` with a default
  shortcut and are bound by id (`MainWindow.Commands.cs`). `⌘.` / `Ctrl+.` is free
  (`Shortcut.Cmd('.')`; `.` → `Key.OemPeriod` already mapped).
- MCP: `TransportTools.cs` holds `play`/`stop`/`seek`; add `retry_take` there.
- C ABI: one new call `nota_engine_retry_take` returning what it did; slot state doc gets 5.
- Shortcuts page, FEATURES.md, CHANGELOG, user docs (EN+RU) via the nota-shortcuts /
  nota-features / nota-docs skills.
