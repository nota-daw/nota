# Bug Assessment: Discard and retry ignores count-in

- **Slug**: retry-take-count-in
- **Created**: 2026-11-10
- **Source**: https://github.com/nota-daw/nota/issues/521 (host: github.com — allowlisted)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

> **Discard and retry ignores count-in** (#521, Nota 0.59.1, Arch Linux x86_64)
>
> When using Discard and retry take, the recording starts immediately instead of respecting the
> configured count-in. The count-in appears to be respected visually by the playhead, but the
> actual recording starts immediately. This causes the playhead to lag behind the recording by
> the duration of the count-in.

Related: PR #486 (Discard and retry the take being recorded, specs/078).

## Symptom

An Arrangement take started from stop with a count-in, then retried (⌘. / Ctrl+.), counts in
again on the transport, but the audio input is captured from the moment of the retry: the
retried audio clip begins with the count-in bar(s), so everything in it sits late by the
count-in length. Expected: nothing is captured until the count-in runs out, as on a fresh take.

## Reproduction

1. Set a count-in (1 bar or more); arm an audio track on a hardware input.
2. From stop, press Record: the count-in clicks, then the take records (correct).
3. While recording, Discard and retry take.
4. The count-in clicks again, but the new clip holds the count-in audio at its start.

MIDI takes are not affected (see below). The reporter did not say which track type;
the "recording ahead of playhead" description matches an audio take.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine_TakeRetry.cpp:retryTake()` — for a counted-in
  Arrangement take it queues `TransportStop`, `Seek`, `TakeRestart` (gate stays shut), then
  `startArrangementTake(..., retryCountIn=true)` → `startCountIn()`.
- `src/native/nota.engine/src/engine/Engine.cpp:startCountIn()` — shuts `captureGate_` on the
  **message thread** at once, then queues `TransportPlay(bars)`.
- `src/native/nota.engine/src/engine/Engine_Render.cpp` `case CommandType::TransportStop` →
  `endCountIn()` — **re-opens** `captureGate_` on the audio thread when the queued Stop lands,
  i.e. after `startCountIn()` already shut it.
- `Engine_Render.cpp` `case CommandType::TransportPlay` (count-in branch) — starts the count-in
  but leaves the gate as it found it: open.
- `src/native/nota.engine/src/engine/Engine_Recording.cpp:onInputBlock()` — the hardware input
  thread captures whenever `captureGate_` is open; it has no notion of `countInActive_`.

## Root Cause Hypothesis

Ordering race between threads, confidence **high**. On a fresh take nothing precedes the
count-in, so the gate `startCountIn()` shut stays shut until the count-in's end opens it. On a
retry, the stop queued just before is applied by the audio thread *after* the message thread
shut the gate, and `endCountIn()` opens it again; the following `TransportPlay(bars)` starts
the count-in without shutting it. The hardware input thread therefore pushes the count-in into
the take, which starts at the take's start beat — so the take's audio is late by the count-in.
MIDI capture and the internal (resampling) tap gate on `countInActive_` on the audio thread as
well, which is why they stay correct.

## Proposed Remediation

**Preferred**: make the audio thread's count-in start shut the capture gate itself — in
`case CommandType::TransportPlay`, when it starts a count-in, `captureGate_.store(false)`. The
gate is then shut in the same order as the commands that open it (Stop/`endCountIn`, the
count-in end), whatever was queued before. `startCountIn()` keeps its immediate store for the
window before the command lands.

**Alternatives**:
- Don't queue `TransportStop` in the retry when the count-in follows — would need a separate
  "rewind and count in" command; more surface for the same effect.
- Make `onInputBlock` also check a published `countInActive_` — a second flag for the same
  state.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine_Render.cpp`
- a test hook to drive the hardware input path between offline renders
  (`include/nota/nota_engine.h`, `src/abi/…`, `Nota.Infrastructure` interop)
- `tests/Nota.SmokeTest/RetryTakeTests.cs`

**Tests to add or update**:
- Retry of a counted-in audio take on a (test) hardware input, with input frames pushed through
  the real input-thread path every render block: the retried clip starts at the take start and
  is about as long as the time recorded after the count-in, not count-in + that.

## Risks & Considerations

- The Session count-in also uses `TransportPlay(bars)`; it already shuts the gate in
  `startCountIn()`, so the extra audio-thread store changes nothing there.
- A retry with `countInBars() == 0` takes the `!countedIn` path and is untouched.

## Open Questions

- None blocking.
