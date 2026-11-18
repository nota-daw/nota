# Implementation Plan: Cancel a queued Session launch before its quantized start

**Branch**: `feat/cancel-queued-launch` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/104-cancel-queued-launch/spec.md` (issue nota-daw/nota#480)

## Summary

A queued slot or scene launch can be withdrawn before its boundary — on the desktop grid
(Alt+click on the queued launch button, "Cancel launch" in the slot / scene menus), in Nota Remote
(press and hold a queued clip or scene) and over MCP. The engine withdraws the request on the audio
thread, at the start of a block and before any track applies its request, so a cancel and the
boundary never split a scene. A scene launch gets a generation number that each track's request
carries; the message thread confirms a scene launch only once a request of that generation applied,
so the scene's follow action and tempo / signature change happen only for a launch that really
started — and a withdrawn one puts the previous scene follow back. Second activation stays as
today (re-queues); Stop withdraws and stops as today; Stop All now also drops a queued scene's
tempo change.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4 (the cancel is a command on
  the lock-free queue), AR-6 (fixed-size table, no allocation), AR-7 (two new C functions).
- **UI work** → [`DESIGN.md`](../../DESIGN.md), the `nota-design` skill: no new visuals — the
  queued blink already exists; the cancel is a gesture, a menu item and a tooltip line.
- **Shortcuts** → the `nota-shortcuts` skill: Alt+click on a queued launch button is a new mouse
  gesture and belongs in `ShortcutLayout` (Session group).
- **Docs** → `nota-docs` (Session page EN + RU), `nota-features` (FEATURES.md), CHANGELOG.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (app, MCP, Remote hub), Svelte 5 / TypeScript
(Nota Remote page).

**Primary Dependencies**: none new.

**Storage**: N/A — queued state is runtime only; no project-format change.

**Audio-Thread Impact**: two new `CommandType`s drained at block start into a fixed table
(≤ 8 slot cancels + 1 scene cancel per block); in the per-track Session step each table entry is a
compare-exchange on that track's `SessionPlayer::pending`. Nothing when no cancel is queued beyond
one integer compare per track. No allocation, lock or I/O (AR-6).

**Testing**: `tests/Nota.SmokeTest` (engine through the C ABI, offline render), the Remote page's
`node --test` unit tests under `src/web/remote/test` (the hold logic), and the real app checked
by hand / screenshots (desktop harness, phone page against a running hub).

**Target Platform**: macOS / Windows / Linux desktop; Nota Remote in a phone browser.

**Project Type**: desktop app + engine library + phone web page.

**Performance Goals**: no measurable extra work per block when nothing is cancelled (perf gate).

**Constraints**: a cancel must be all-or-nothing against the boundary (FR-005); queued state
reaches the UI within one refresh (FR-006).

**Scale/Scope**: one engine feature, three surfaces (desktop grid, Remote, MCP).

## Constitution Check

| Principle | Status |
|-----------|--------|
| 0. Approved issue | ✅ #480 carries `approved`, milestone 0.61.1 |
| I. Real-time safety | ✅ command queue in, fixed table, CAS only; no alloc / lock / I/O |
| II. Boundary discipline | ✅ `nota_session_cancel_slot_launch`, `nota_session_cancel_scene_launch` in `nota_engine.h`; no JUCE |
| III. Licensing | ✅ no new files without SPDX; no dependency |
| IV. Verification gate | ✅ new smoke tests: slot cancel, scene cancel (drum keeps playing, stops withdrawn), follow / tempo dropped, boundary race, Stop All tempo, re-launch works |
| V. One logical change | ✅ one feature across the surfaces the issue names |
| VI. Docs in sync | ✅ CHANGELOG, FEATURES.md, nota-docs Session page EN + RU, shortcuts screen |
| VII. Spec first | ✅ this spec / plan / tasks |
| VIII. No weakened rules | ✅ |

Post-design re-check: unchanged — the design adds no shared mutable state on the audio thread
beyond the drained table, written and read by the audio thread only.

## Design

### Engine (`src/native/nota.engine`)

- **`SessionPlayer`**: the queued request is one word `request` (slot, scene-launch generation —
  0 for a direct launch, stop, follow action or repeat — and a per-track serial); `appliedGen` is set by
  `maybeApply` to an applied scene request's generation. `requestLaunch` / `requestStop` take the
  generation (default 0). Helpers: `withdraw(word)` (audio thread), `queueIfIdle` (follow /
  Repeat), `viewGen` (seqlocked read for the UI). `kStopNow` is never a cancel target.
- **Cancel = a batch of exact targets**: the message thread reads each matching track's request
  word and pushes one `CancelSessionLaunch` per target (track id + that word) as a single batch
  (`CommandQueue::pushBatch`: all or none visible to the consumer; false when it doesn't fit, and
  the cancel reports false). `applyLaunchCancel` withdraws a target, as `drainCommands(g)` pops it,
  only if the request is still that very request — same generation and serial (a row insert /
  move that renumbered its slot doesn't change that) — no table, no per-block limit, before any track
  reaches its boundary. The live request is never written by a cancel, so a launch being applied
  is never disturbed. A launch made after the cancel is a new word (a per-track serial) and stays.
  A whole-scene cancel spares a scene launch that has started on any track (clips with their own
  quanta); a slot cancel may still withdraw that scene's clip that is waiting on its own slot.
- **One request word** (review of #677): `SessionPlayer::request` packs the queued slot (32 bits),
  its scene-launch generation (24 bits) and a per-track serial (8 bits) in one 64-bit atomic; only
  scene requests record `appliedGen`, so a clip follow applying later can't erase that a scene
  started, so they are replaced and consumed together;
  `maybeApply` records `appliedGen` inside an `applySeq` seqlock that `viewGen` reads, so `poll()`
  never takes a started scene for a cancelled one.
- **Scene launch record** (message thread, `Engine`; `launchScene` first resolves the launch before
  it, and remembers whether a scene follow made it — a cancelled follow-made launch ends follow, any
  other brings the previous follow back, due or not): `sceneLaunchGen_` counter; `launchScene`
  passes `++sceneLaunchGen_` to each track's request; if it queued no request at all it is
  confirmed at once (today's behaviour). Otherwise `waitSceneGen_ = gen` and, unless an earlier
  scene launch is still waiting, the previous `sceneFollowScene_ / sceneFollowStart_` are saved.
  `serviceSessionFollow` resolves a waiting launch first: confirmed when any track's `appliedGen`
  equals it; withdrawn when no track still has `the request's generation == gen` with a request queued. While it
  waits, neither the scene tempo nor scene follow is serviced. Confirmed → as today. Withdrawn →
  tempo change dropped, the saved follow restored unless its due time had already come (the
  cancelled launch was that follow's own: follow ends).
- **Scene cancel** (`cancelSceneLaunch(scene)`, message thread): pushes `CancelSessionScene` with
  the waiting generation of that scene when there is one (else 0 — only launches of the row are
  withdrawn). **Slot cancel** (`cancelSlotLaunch(track, scene)`): pushes `CancelSessionSlot` when
  the slot is queued. Both return whether something was queued to cancel.
- **Stop All**: also drops a waiting scene launch's tempo change (`sceneTempoPending_ = false`,
  `waitSceneGen_ = 0`).
- `remapPlayers` (scene insert / remove) already remaps `pending`; `the request's generation` needs no remap.

### C ABI (`nota_engine.h`, `abi/nota_engine_session.cpp`)

See [contracts/c-abi.md](contracts/c-abi.md): `nota_session_cancel_slot_launch(engine, track_id,
scene)`, `nota_session_cancel_scene_launch(engine, scene)` → `NOTA_OK` when a request was queued
to cancel, `NOTA_ERR_UNKNOWN` (nothing queued — the precedent of `nota_undo`-style "nothing to do") — the result is what the message thread saw; the
audio thread may still find the boundary already passed (then nothing is withdrawn).

### Managed

- `NativeMethods.Session.cs`, `NotaEngine.Session.cs`, `IAudioEngine`:
  `bool CancelSlotLaunch(int trackId, int scene)`, `bool CancelSceneLaunch(int scene)`.
- **Desktop grid** (`SessionView.Grid.cs`): clip slot — Alt+click on the launch zone of a queued
  slot cancels it (any other Alt+click on a launch zone launches as today); group slot — the same,
  for each member queued on that row (a group slot has no context menu); scene cell — Alt+click on
  a queued scene's launch box. Slot and scene context menus show "Cancel launch" while queued. The launch button tooltip of a
  queued slot / scene reads "Queued — Alt+click to cancel". Status line ("Launch cancelled").
- **Shortcuts**: `ShortcutLayout` Session row "Cancel a queued launch — Alt+click its launch
  button" (a fixed gesture row, not a rebindable key).
- **MCP** (`SessionTools.cs`): `cancel_slot_launch(trackId, scene)`, `cancel_scene_launch(scene)`;
  `get_session` already reports a slot's state as `"queued"` — no new field.
- **Nota Remote**: hub messages `cancel` {id, i} (a group id cancels its members) and
  `cancelScene` {i}; page — press and hold (500 ms) a queued clip or a queued scene button cancels
  it; the hold never also fires the tap. `use:tap` gains an optional hold handler.

### Docs

CHANGELOG `[Unreleased]` Added; FEATURES.md Session line; nota-docs Session page (EN + RU):
cancelling, the second press, Stop / Stop All while a launch waits; Remote page hold gesture; the
Russian strings for the new labels.

## Project Structure

### Documentation (this feature)

```text
specs/104-cancel-queued-launch/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── remote-and-mcp.md
└── tasks.md
```

### Source Code

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                 # two new functions
└── src/
    ├── abi/nota_engine_session.cpp            # wrappers
    └── engine/
        ├── SessionPlayer.h / .cpp             # the request's generation, appliedGen, cancelSlot, cancelGen
        ├── CommandQueue.h                     # CancelSessionSlot, CancelSessionScene
        ├── Engine.h                           # cancel table, scene launch record
        ├── Engine_Session.cpp                 # launchScene gen, cancel*, serviceSessionFollow, stopAll
        └── Engine_Render.cpp                  # drain + per-track apply
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs
├── Nota.Infrastructure/Interop/NativeMethods.Session.cs, NotaEngine.Session.cs
├── Nota.App/SessionView.Grid.cs               # Alt+click, menus, tooltip
├── Nota.App/ShortcutLayout*.cs                # gesture row
├── Nota.App/Strings (en / ru)                 # labels
├── Nota.Mcp/Tools/SessionTools.cs
└── Nota.Remote/RemoteHub.Apply.cs
src/web/remote/src/
├── lib/tap.ts                                 # optional hold
└── screens/Scenes.svelte                      # hold on queued clip / scene
tests/Nota.SmokeTest/CancelLaunchTests.cs      # new
```

**Structure Decision**: the existing engine / managed / web layout; one new smoke-test file.

## Complexity Tracking

None.
