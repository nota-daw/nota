---
description: "Task list for specs/111 — Follow Action when a recorded take lands (#481)"
---

# Tasks: Follow Action When a Recorded Take Lands

**Input**: Design documents from `specs/111-take-landed-follow/` — [spec.md](spec.md),
[plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/abi.md](contracts/abi.md), [contracts/mcp.md](contracts/mcp.md), [quickstart.md](quickstart.md)

**Tests**: Engine behaviour changes, so the smoke test is extended (constitution IV). Checks live
in a new `tests/Nota.SmokeTest/TakeLandedFollowTests.cs` (pattern: `SlotTakeLengthTests.cs` — a
static `Run()` yielding `(bool ok, string label)`), run from `tests/Nota.SmokeTest/Program.cs`
next to both `SlotTakeLengthTests.Run()` call sites.

**Organization**: by user story (spec.md). Branch `feat/take-landed-follow`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different files, no dependency on an unfinished task
- **[Story]**: US1–US5 from spec.md

---

## Phase 1: Setup

- [X] T001 Create `tests/Nota.SmokeTest/TakeLandedFollowTests.cs` with the two-line AGPL-3.0-only SPDX header, a comment header naming #481 / specs/111, the `Rolling` / `Beats` / `Armed` / `Resampling` helpers copied from `SlotTakeLengthTests.cs`, and an empty `public static IEnumerable<(bool, string)> Run()`; call it from both `SlotTakeLengthTests.Run()` sites in `tests/Nota.SmokeTest/Program.cs`

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the per-slot rule, its ABI and managed mirror.

- [X] T002 In `src/native/nota.engine/src/engine/Track.h` add `enum class LandingAction : int32_t { Off = 0, NextScene = 1, Scene = 2 };` and `struct LandingRule { LandingAction action = LandingAction::Off; int32_t scene = 0; double quantBeats = -1.0; bool operator==(const LandingRule&) const = default; };` and the field `LandingRule landing;` on `SessionSlot` after `nextTake` (comment: "what a fresh take landing here launches (#481); belongs to the slot, kept hidden under a clip")
- [X] T003 In `src/native/nota.engine/src/engine/Engine.h` declare `bool sessionSlotLandingRule(int32_t trackId, int32_t scene, LandingRule& out) const;` and `bool setSessionSlotLandingRule(int32_t trackId, int32_t scene, const LandingRule& v);`
- [X] T004 Implement both in `src/native/nota.engine/src/engine/Engine_Session.cpp` after the take-length block. Validation per data-model: "action not 0–2; Scene with target out of range; `quantBeats` not −1 and not in 0..32 (finite)" → false; Off/NextScene store `scene = 0`; any `quantBeats < 0` stores −1; unchanged → true without publishing; else `cloneTrack`, resize slots to `sceneCount`, assign, `republishWithTrack` (one undo step). `replaceSlotContent` also keeps `dst.landing`
- [X] T005 Add `NotaSlotLandingRule`, the `NOTA_SLOT_LANDING_*` defines and the two functions exactly as in [contracts/abi.md](contracts/abi.md) to `src/native/nota.engine/include/nota/nota_engine.h` after `nota_session_slot_take_beats`, and wrappers in `src/native/nota.engine/src/abi/nota_engine_session.cpp`
- [X] T006 [P] P/Invoke entries `SessionGetSlotLandingRule` / `SessionSetSlotLandingRule` in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Session.cs`
- [X] T007 [P] `public enum SlotLandingAction { Off, NextScene, Scene }` and blittable `NotaSlotLandingRule { int Action; int Scene; double QuantBeats; }` (ctor, `LandingAction` property) in `src/managed/Nota.Application/Engine/EngineContracts.cs`; `TryGetSessionSlotLandingRule` / `SetSessionSlotLandingRule` on `IAudioEngine` (`src/managed/Nota.Application/Engine/IAudioEngine.cs`), implemented in `src/managed/Nota.Infrastructure/Interop/NotaEngine.Session.cs` and any other `IAudioEngine` implementation (grep)
- [X] T008 Smoke checks (`Values()`): default Off; Scene 1 / quant 4 reads back; NextScene stores scene 0; refused and unchanged for action 7, target out of range, quant 33, quant NaN, unknown track, slot scene ≥ count; setting the same value adds no undo step

**Checkpoint**: rule stored, validated, reachable from C#; nothing fires yet.

---

## Phase 3: User Story 1 — Launch a scene once a kept take is ready (P1) 🎯 MVP

**Independent test**: 120 BPM, quant 1 bar, slot (t, 0) take length 4 bars, rule Scene 1 / Global; record → scene 1 launches on the first bar after landing, once.

- [X] T009 [US1] `Engine.h`: `bool fresh = false;` on `MidiTakeLane` and `CaptureLane`; members `std::vector<std::pair<int32_t, int32_t>> landedTakes_;`, `struct RuleWait { int32_t gen = 0, trackId = 0, scene = -1; } ruleWait_;`, `int32_t landingMuted_ = 0;`; private `void noteTakeLanded(int32_t trackId, int32_t scene);` and `void serviceLandingRules();`; `launchScene(int32_t scene, bool fromFollow = false, double quantOverride = -1.0)` and `launchSlotImpl(..., double quantOverride = -1.0)`
- [X] T010 [US1] Set `fresh` where lanes start, in `src/native/nota.engine/src/engine/Engine_Session.cpp` `recordSessionSlot` (MIDI lane 0: `!hasClip`; audio main lane: the slot was empty — set on `captureLanes_[0]` after `startAudioRecording`) and `src/native/nota.engine/src/engine/Engine_Recording.cpp` `addSessionMidiLane` / `addSessionAudioLane` (`!slot->hasClip`)
- [X] T011 [US1] Note landings: in `materialiseCaptureLane` Session branch after `s.hasClip = true` → `if (lane.fresh) noteTakeLanded(trackId, scene)`; in `finalizeSessionRecord` after `cutGrowingLane` for each lane with `scene >= 0`, `fresh`, `started` (or its player playing the slot) → note; in `finishSessionMidiLane` after `cutGrowingLane` the same. `noteTakeLanded` returns at once when `landingMuted_ > 0`, and skips a MIDI slot with no notes
- [X] T012 [US1] `launchScene` / `launchSlotImpl`: `quantOverride >= 0` replaces `s.quantBeats` in `requestLaunch` and `q` for `sceneFollowStart_`; default −1 keeps today's behaviour
- [X] T013 [US1] `serviceLandingRules()` called at the top of `serviceSessionFollow` (after `sceneLaunchWaiting()`): move out `landedTakes_`; for each, if the transport plays, `sessionFollow()` is on, the slot holds a clip and its rule is not Off, resolve the target (`NextScene` → `(scene + 1) % sceneCount`), `launchScene(target, false, rule.quantBeats)` and set `ruleWait_ = {sceneLaunchGen_, trackId, scene}`
- [X] T014 [US1] Smoke checks (`IssueExample()`, `OpenTake()`, `AudioTake()`, `NextScene()`): MIDI 4-bar take with notes, rule Scene 1: scene 1 not queued before landing, queued right after, launched on the next bar, never queued again over the following 8 bars; open take: no queue at the Stop request until it lands; audio (resampling) take fires the same; NextScene from the last scene launches scene 0

**Checkpoint**: the issue's example works.

---

## Phase 4: User Story 2 — Nothing fires for a take that did not land (P1)

- [X] T015 [US2] `landingMuted_` scope (RAII struct) around the landing work in `Engine::transportStop` (`src/native/nota.engine/src/engine/Engine.cpp`), `Engine::stopAllSession` and `Engine::removeScene` (`Engine_Session.cpp`)
- [X] T016 [US2] Watch the wait in `serviceLandingRules`: if `ruleWait_.gen != 0` and `waitSceneGen_ != ruleWait_.gen` → clear; else if the slot no longer holds a clip → `cancelSceneLaunch(waitScene_)` and clear
- [X] T017 [US2] Smoke checks (`NeverFires()`): count-in stopped; stopped while waiting for the boundary; transport stop; Stop all clips; global Follow off; empty MIDI take; overdub into a filled slot; retry → only the retried take fires once; Undo before the boundary cancels the queued launch; Undo after it launched does not re-fire

---

## Phase 5: User Story 3 — Each take fires once; nothing replays (P2)

- [X] T018 [US3] Scene identity: helper `remapLandingScenes(Graph&, map)` in `Engine_Session.cpp` used by `insertSceneRow`, `moveScene`, `removeScene` (deleted target → Off)
- [X] T019 [US3] Persistence: `SlotLandingRuleDto { Scene, Action, TargetScene, QuantBeats }` and `TrackDto.SessionLandingRules` in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`; write non-Off rules in `ProjectService.Capture` beside `SessionTakeLengths`; apply on load beside it with a warning on refusal (`src/managed/Nota.Infrastructure/Persistence/ProjectService.cs`)
- [X] T020 [US3] Smoke checks (`OncePerTake()`, `SceneEdits()`, `SaveAndReopen()`): second fresh take after deleting the clip fires once; Undo/Redo never queue; move/insert/delete rows remap or turn Off; save/reopen keeps rules and queues nothing; a project without rules opens all Off

---

## Phase 6: User Story 4 — Configure and see the rule (P2)

- [X] T021 [P] [US4] Strings `session.inspector.landing`, `session.inspector.landing_on`, `session.landing.off|next|scene|global|hint|hint_off_global` in `src/managed/Nota.Application/Localization/en.json` (and every other catalog the build checks)
- [X] T022 [US4] Inspector rows in `src/managed/Nota.App/SessionView.Inspector.cs` below Take length for an empty slot: "When the take lands" chip (Off / Launch next scene / Launch scene · each scene by name) and "On" chip (Global (value) / launch quantization values), a caption explaining it fires once when a take recorded here is ready to play, or that Follow is off globally; context-menu submenu like `TakeLengthSubmenu`; each change via `SetSessionSlotLandingRule` then `RefreshSlot`
- [X] T023 [US4] Grid mark in `src/managed/Nota.App/SessionView.Grid.cs`: an empty slot with a rule shows a small "→ <scene>" caption (beside the prepared take length), tertiary text per DESIGN.md

---

## Phase 7: User Story 5 — Set the rule from an assistant (P3)

- [X] T024 [US5] `set_session_slot_landing_rule` tool and `LandingRules` snapshot list in `src/managed/Nota.Mcp/Tools/SessionTools.cs` per [contracts/mcp.md](contracts/mcp.md)
- [X] T025 [US5] MCP smoke check in `tests/Nota.SmokeTest/Program.cs` beside the take-length check: set → snapshot shows it; unknown action / target out of range refused

---

## Phase 8: Polish

- [X] T026 CHANGELOG `[Unreleased]` Added entry (#481); FEATURES.md via the nota-features skill; user docs via nota-docs if the docs repo is available
- [ ] T027 Run `scripts/build-win.ps1`; all smoke checks green

## Dependencies

Phase 1 → Phase 2 → US1 → (US2, US3, US4, US5 in any order) → Polish. US2 and US3 extend the
engine files touched by US1; US4 and US5 are managed-only and can run in parallel with US2/US3.

## Implementation Strategy

MVP = Phases 1–3 (the issue's example). Then US2 (safety), US3 (persistence), US4 (UI), US5 (MCP),
then Polish and the build.
