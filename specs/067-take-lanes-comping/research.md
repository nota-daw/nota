# Research: Take lanes and comping

## R1 — Where takes live

**Decision**: On the engine `Track` (graph snapshot), next to `clips`.

**Rationale**: Undo/redo restore graph snapshots (AR-5), so a comp edit, a delete and a flatten
are one undo step each with no extra machinery, and the take buffers are shared across
snapshots by `shared_ptr`. Keeping takes in the managed app would need a second undo history in
lock-step with the engine's.

**Alternatives**: managed-side take store (rejected: two histories); a special "comp clip" type
the audio thread resolves per block (rejected: new render-path work, AR-6 risk, perf gate).

## R2 — What the comp is

**Decision**: Ordinary audio clips, each tagged with the id of the take it plays (`takeId`).

**Rationale**: Playback, editing, clipboard, export, freeze and the clip editor all work
unchanged. The tag only drives the lane highlight and flatten. A section edited away from its
take keeps playing; flatten simply leaves it alone.

## R3 — Capturing passes

**Decision**: Keep today's loop-region write cursor. When it wraps, the drain copies the finished
pass `[passFrom, loopFrames)` into a fresh zeroed loop-length buffer. On stop the open pass is
added. Every take buffer starts at the loop start, so a position inside a take is a position in
the loop.

**Rationale**: The drain already runs on the message thread and already allocates; the audio
thread is untouched. Aligned buffers make the default comp, comp edits and flatten pure frame
arithmetic.

## R4 — Default comp

**Decision**: Newest-first coverage: every frame of the loop comes from the latest pass that
recorded it. This is exactly what today's in-place buffer holds at stop, so SC-002 is
sample-exact. A recording with a single pass keeps today's code path and creates no takes.

## R5 — Persistence identity

**Decision**: Save the take id with each take and with each comp clip; on load restore takes
first, map old ids to the fresh ones, tag the clips, and re-point each tagged clip at its take's
decoded buffer (same frames and rate) so a reopened comp shares memory like a fresh one.

**Rationale**: `SampleBuffer` ids are not persisted and the engine has no decode cache, so
sample identity can't link clips to takes across a save.

## R6 — Crossfades at comp boundaries

**Decision**: None in this version (spec assumption); sections are cut on whole frames of the
same loop-aligned recording. #286 (automatic crossfades at splits) is the follow-up.
