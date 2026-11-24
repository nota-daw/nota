# Research: Follow Action When a Recorded Take Lands

## R1 — Where the rule lives

- **Decision**: a POD `LandingRule { action, scene, quantBeats }` on `SessionSlot`, next to
  `nextTake`; `replaceSlotContent` keeps the destination's rule like it keeps `nextTake`.
- **Rationale**: the graph snapshot already carries per-slot properties through Undo/Redo, clone,
  scene edits, count-in cancel and take discard (specs/106 proved the path). A slot property also
  makes "belongs to the slot, not the clip" (FR-012) automatic for copy/paste/Arrangement drops.
- **Alternatives**: a property of the clip (would travel with copies, vanish under an empty slot —
  wrong for a slot waiting to record); a side table keyed by (track, scene) (needs its own undo
  and scene remapping).

## R2 — The landing moment

- **Decision**: "landed" is noted at the exact points where a fresh take becomes a kept clip:
  - audio: `materialiseCaptureLane`, Session branch, after `hasClip` is set (covers
    `stopAudioRecording` for every lane and `finishCaptureLane` for one lane);
  - MIDI: `finalizeSessionRecord` after `cutGrowingLane` for each lane, and
    `finishSessionMidiLane` (one slot of a several-slot take, also its fixed-length end).
  Each lane remembers at its start whether its slot was empty (`fresh`). A MIDI lane counts only
  if it started (`started` or seen playing) and the slot holds at least one note.
- **Rationale**: these are the only places a Session take is kept; everything earlier (Stop
  request, end of capture) or that drops the take (count-in cancel, stopped-before-start
  `discardTake`, retry, empty buffer) never reaches them. The audio sample is in memory at that
  point; saving is separate — matching "playable, not necessarily saved".
- **Alternatives**: managed side watching slot states at 30 Hz (would miss quick sequences,
  confuse overdubs and undo, and duplicate logic in UI and MCP).

## R3 — Firing: deferred to `poll()`

- **Decision**: landing sites only push `{trackId, scene}` into `landedTakes_` (message thread).
  `serviceSessionFollow` drains it: for each entry whose slot still holds a clip, whose rule is
  not Off, with the global Follow switch on and the transport playing, it calls
  `launchScene(target, false, quant)`.
- **Rationale**: an audio take relaunches its own slot right after materialising; deferring makes
  the scene launch the later request, so it wins on that track the way any later launch does
  (#480 semantics), and keeps the landing sites free of launch logic.
- **Alternatives**: firing inline (order-dependent with the relaunch; re-entrancy into the
  recording code).

## R4 — The boundary

- **Decision**: `launchScene` gains an optional quantum override passed to every slot's
  `requestLaunch` and used for the scene follow start and tempo boundary. Rule quantum `< 0` =
  Global → no override: the launch behaves exactly like a manual scene launch (clips keep their
  own quanta). A rule quantum `>= 0` overrides every slot of that launch (0 = immediately).
  Options match the toolbar's launch quantization list.
- **Rationale**: the launch is queued at once, so the UI shows the scene waiting, it can be
  cancelled or replaced (#480), and the first boundary at or after landing is exactly what
  `SessionPlayer::maybeApply` already computes.
- **Alternatives**: a held "due beat" served in `poll()` like scene follow (launches late by up to
  a quantum when the rule's quantum is finer than the global one, and is invisible until then).

## R5 — Not firing (FR-005)

- **Decision**: a `landingMuted_` counter around `transportStop`, `stopAllSession` and
  `removeScene` drops notes made while it is set (global stop, row delete). The global Follow
  switch and a stopped transport are checked when serving. Overdubs / audio over a clip are not
  `fresh`. Empty MIDI takes are skipped by note count.
- **Rationale**: the stop paths are the only callers that land takes on behalf of "stop
  everything"; one counter keeps each landing site simple.

## R6 — Undo before the boundary (FR-006)

- **Decision**: after firing, remember `ruleWait_ {gen, trackId, scene}` (gen = the scene launch
  generation). Each `serviceSessionFollow`: if that launch is no longer the waiting one
  (`waitSceneGen_ != gen`) forget it; else if the slot no longer holds a clip (take undone, clip
  deleted), `cancelSceneLaunch` and forget it.
- **Rationale**: Undo and delete are ordinary graph edits; polling the one remembered wait is
  cheaper and safer than hooking every edit. After the launch started nothing is reversed.
- Note: discard/retry (specs/078) only exist while recording, before landing — they never fire.

## R7 — Scene identity

- **Decision**: `insertSceneRow`, `moveScene` and `removeScene` remap every slot's
  `landing.scene` with the same index map they apply to players; a deleted target turns the rule
  Off (`action = Off`, the UI says "its scene was deleted" only transiently — the rule just reads
  Off afterwards). Duplicating a row copies its slots' rules as-is.
- **Rationale**: FR-012; the existing `jumpScene` does not do this, which is a known gap left alone.

## R8 — Next scene

- **Decision**: `(slotScene + 1) % sceneCount`, matching scene follow's wrap.

## R9 — Persistence and MCP

- **Decision**: `TrackDto.SessionLandingRules: List<SlotLandingRuleDto>?` (Scene, Action,
  TargetScene, QuantBeats), written for every slot whose rule is not Off (filled slots too);
  applied after scenes exist. MCP: `set_session_slot_landing_rule(trackId, scene, action,
  targetScene?, quantBeats?)` + `LandingRules` in the Session snapshot.
- **Rationale**: mirrors `SessionTakeLengths` / `set_session_slot_take_length`, no version bump
  since absence means Off.
