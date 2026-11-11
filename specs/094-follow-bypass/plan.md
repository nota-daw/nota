# Implementation Plan: Bypass one Session clip's follow actions

**Branch**: `094-follow-bypass` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

## Summary

A per-slot `followBypass` flag. The follow check counts the slot's follow time from a moving
start (`SessionPlayer::followFrom`): a bypassed slot moves it on by the follow times that have
ended, instead of firing, so turning the bypass off fires at the end of the time then
running. The clip inspector gets a Bypass row; the flag is saved (format 33) and exposed over
MCP.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: `sessionPreApply` reads one more slot field and adds to an
audio-thread-only double on the player. No allocation, lock or I/O. `playedBeats` itself is not
touched while bypassed, so fixed-length capture, the record target and the articulation chase,
which read it, are unaffected.

**Testing**: `FollowBypassTests` in the smoke test (`-- --follow-bypass` alone, and in the full
run): the clip held while another follows, settings kept, a hand launch, no catch-up when the
bypass goes off, save → reopen, a v32 project.

## Design

- `SessionSlot::followBypass`; `NotaSessionClipProps::follow_bypass` (appended) read and written
  by `sessionClipProps` / `setSessionClipProps`; the managed `NotaSessionClipProps.FollowBypass`.
- `SessionPlayer::followFrom` — beats played where the follow time counts from; 0 at a launch
  (`maybeApply`) and when an action of None restarts the count.
- `Engine::sessionPreApply`: due when `playedBeats - followFrom` reaches the follow time; a
  bypassed slot then adds the whole follow times that have ended to `followFrom` and returns. A
  bypassed slot counts with the global Follow switch off too.
- `SessionSlotDto.FollowBypass`, captured and applied with the other clip properties;
  `CurrentFormatVersion` 33 with a no-op migration. `CueOutputTests` checks "32 or later" and the
  current format, like `AuroraTableTests` did for 31.
- `SessionView.Inspector`: a **Bypass** row (On / Off chip) under the Follow action head; the
  head reads **Bypassed** in the warning ink; the A/B, Jump and After rows sit in their own panel
  that `Inactive` dims while bypassed (the whole section still dims when Follow is off globally).
- MCP: `get_session` slots carry `FollowBypass`; `set_session_clip` takes `followBypass`.

## Constitution Check

Real-time rules kept (AR-6). The C ABI grows by one trailing field of a plain struct (AR-7), with
the managed layout updated. Texts through `Loc`.
