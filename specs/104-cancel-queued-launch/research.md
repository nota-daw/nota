# Research: Cancel a queued Session launch

## R1 — Where the cancel takes effect

- **Decision**: a command on the lock-free queue, drained at block start and applied in each
  track's Session step before `maybeApply`.
- **Rationale**: tracks apply their requests serially in one render pass, after `drainCommands`.
  A cancel made on the message thread track by track could land between two tracks of the same
  boundary block and split a scene (FR-005). Applied from the drained table, every track sees the
  cancel in the same block. A single compare-exchange per track also keeps a newer launch made in
  the meantime (Story 2.3): it no longer matches.
- **Alternatives**: message-thread compare-exchange per track (simple, but a scene can split at the
  boundary); a lock around launch application (forbidden on the audio thread, AR-6).

## R2 — Which requests a scene cancel withdraws

- **Decision**: per-track `pendingGen` — the generation of the scene launch that queued the
  request. A scene cancel withdraws a launch of a slot in that row (whoever queued it) and any
  request carrying the scene launch's generation (the stops of tracks whose slot is empty).
- **Rationale**: the issue's example only works if the drum's stop, queued by the bass scene, is
  withdrawn too. A scene index alone cannot tell a stop the scene queued from a Stop the performer
  pressed; a generation also tells two launches of the same scene apart.
- **Alternatives**: remembering a list of tracks per launch on the message thread (races with
  newer requests on those tracks; still needs per-track identity).

## R3 — Scene follow and tempo of a cancelled launch

- **Decision**: a scene launch waits for confirmation; `poll()` confirms it when any track's
  `appliedGen` equals its generation, treats it as withdrawn when no track still holds a request of
  that generation, and services neither tempo nor follow while it waits. Withdrawn → tempo change
  dropped, the follow running before it restored, unless that follow was already due (it launched
  this scene itself) — then follow ends.
- **Rationale**: tempo and follow live on the message thread and today start from the launch call,
  not from the boundary actually applying. Tying them to an applied request makes "a follow action
  belonging only to the cancelled launch does not fire" hold however the launch was withdrawn —
  scene cancel, the last slot cancelled by hand, or every track's request replaced.
- **Alternatives**: an acknowledgement value from the audio thread per cancel (does not cover the
  "replaced one by one" case); restoring the old follow unconditionally (a follow-launched scene,
  once cancelled, would relaunch on the next poll since its due time has passed).

## R4 — Second activation and Stop

- **Decision**: unchanged — a second press re-queues the same launch; Stop withdraws a queued
  launch and stops the track at the boundary; Stop All additionally drops a waiting scene launch's
  tempo change (it already ends scene follow).
- **Rationale**: user's choice (clarification 2026-11-18): an accidental double press must not
  undo a launch. Stop means "go silent" and needs no new meaning.

## R5 — Gestures

- **Desktop**: Alt+click on the launch button of a queued slot, group slot or scene; "Cancel
  launch" in the context menus. Alt+drag on the slot body copies — the launch zone is separate, so
  the two never meet. A rebindable key is not needed: it acts on the clicked item.
- **Nota Remote**: press and hold a queued clip or scene for 500 ms. A phone has no modifier key;
  a second tap must keep the launch (R4); hold is the deliberate gesture. The hold suppresses the
  release tap, otherwise the same press would re-queue what it cancelled.
- **MCP**: two tools mirroring `launch_slot` / `launch_scene`.
