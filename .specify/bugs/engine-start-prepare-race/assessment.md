# Bug Assessment: Engine::start() re-prepares live nodes while the audio thread renders

- **Slug**: engine-start-prepare-race
- **Created**: 2026-10-14
- **Source**: pasted text (found while assessing `.specify/bugs/plugin-inactive-bus-crash`)
- **Verdict**: valid
- **Severity**: high

## Report (summarized)

`Engine::start()` calls `backend_->start(render)` first and only then
`propagateSampleRate()`. That re-prepares every live node, including hosted plugins
(`PluginInstrument/PluginEffect::prepare()` → `prepareToPlay`, `storage_.setSize`,
`spare_.assign`) on the calling thread, while the audio thread may already be inside
`processBlock` on the same objects. Triggers: after export (`AudioExporter.cs` `Restore`
→ `engine.Start()`), after freeze/bounce (`TrackFreezer.cs` `Restore` → `engine.Start()`),
and Preferences → Audio → Apply (`Engine::applyAudioConfig` → `stop()` + `start()`).
It does not explain the Vienna Synchron crash (a plain insert does not call `start()`),
but produces the same class of audio-thread access violation.

## Symptom

Restarting the backend with a non-empty project (export, freeze, audio-settings Apply)
lets the device callback render the graph while the control thread re-prepares the same
nodes. Expected: nodes are prepared before the first device callback touches them.
Observed (possible): access violation / heap corruption inside a plugin's `process()`
or in `storage_` (JUCE `AudioBuffer` reallocated under the reader), or glitches from
built-in DSP whose buffers are resized mid-block.

## Reproduction

1. Open a project with a hosted VST3/AU instrument or effect on a track.
2. Export (or freeze a track, or press Apply in Preferences → Audio).
3. On completion the engine calls `start()`; the backend callback begins immediately,
   racing `propagateSampleRate()`.

Timing-dependent — the window is the first few device callbacks after start.
[NEEDS CLARIFICATION: no observed crash report attributed to this path yet.]

## Suspected Code Paths

- `src/native/nota.engine/src/Engine.cpp:54` `Engine::start()` — starts the backend before
  `propagateSampleRate` / `reconfigureAllWarpStreams` / `recomputePdc`.
- `src/native/nota.engine/src/Engine.cpp:80` `Engine::propagateSampleRate()` — calls
  `setSampleRate` on every live instrument/device/MIDI effect (shared with the live graph).
- `src/native/nota.engine/pluginhost/PluginHost.cpp:769-773, 820-824` — `prepareToPlay`,
  `storage_.setSize`, `spare_.assign` (reallocations the audio thread reads).
- `src/native/nota.engine/src/Engine_Render.cpp:327` `Engine::render()` — the device
  callback; has no gate.
- `src/native/nota.engine/src/Engine_Render.cpp:347` `Engine::renderOffline()` — also
  calls `propagateSampleRate` + `processBlock` on the calling thread; safe only if the
  backend is stopped (callers do stop it today, nothing enforces it).
- `src/native/nota.engine/src/Engine.cpp:134` `Engine::applyAudioConfig()` — stop + start.

## Root Cause Hypothesis

Ordering bug: the render callback is installed and the device started before nodes are
re-prepared at the negotiated rate, and the device rate is only known after the backend
opens, so the prepare cannot simply move before `backend_->start()`. Nothing tells the
audio thread to stay out of the graph during that window. Confidence: high (direct
reading of the code).

## Proposed Remediation

**Preferred**: add `std::atomic<bool> renderSuspended_` to `Engine`. In `start()`, store
`true` before `backend_->start(...)`; `Engine::render()` checks it with an acquire load
and, while set, writes silence (`numFrames * 2` zeros) and returns without touching the
graph. After `propagateSampleRate`, `reconfigureAllWarpStreams` and `recomputePdc`
finish, clear it with a release store (also clear it on the failure path). No locks or
allocation on the audio thread. Remaining exposure: a callback that loaded `false`
before the store — impossible here because the flag is set before the backend starts.

Guard `renderOffline()`: if `backend_->isRunning()`, zero the output and return without
preparing or rendering, so an offline render can never race the device thread.

**Alternatives**:
- Open the device without starting it, prepare, then start — needs a backend API change
  in CoreAudio/miniaudio backends; larger.
- A spin/mutex handshake in render — rejected (no locks on the audio thread).

**Files likely to change**:
- `src/native/nota.engine/src/Engine.h`
- `src/native/nota.engine/src/Engine.cpp`
- `src/native/nota.engine/src/Engine_Render.cpp`
- `CHANGELOG.md`

**Tests to add or update**:
- The smoke test runs without an audio device, so the live race cannot be exercised in
  CI. `scripts/build.sh` must stay green (offline render path unchanged when the backend
  is stopped).

## Risks & Considerations

- A few device callbacks of silence right after start — inaudible (transport is stopped
  after export/freeze/Apply).
- Commands queued while suspended stay in the queue and are drained on the next real
  block — no loss.
- `renderOffline` guard changes behaviour for a caller that renders offline with the
  device live: it now gets silence instead of a racy render. No current caller does
  this (exporter and freezer stop first; smoke tests never start the backend).

## Open Questions

- None blocking.
