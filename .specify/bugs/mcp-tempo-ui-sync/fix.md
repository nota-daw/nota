# Bug Fix: a tempo set over MCP never reaches the transport bar, and the stale value is saved

- **Slug**: mcp-tempo-ui-sync
- **Fixed**: 2026-10-17
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

`TransportViewModel.Tick()` now follows the engine's tempo, like it already follows the loop
record mode. A tempo set outside the UI (MCP `set_tempo`, or a controller driving it) then
reaches the BPM field and is what saving, autosave and export read. The field's own changes
are guarded while they are in flight, so a drag never snaps back.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs` | modified | `FollowEngineBpm()` called from `Tick()`; `OnBpmChanged` records the in-flight tempo; `_followingBpm` suppresses the echo |
| `tests/Nota.SmokeTest/TempoFollowTests.cs` | added test | 9 checks, `RenderOffline` standing in for the audio thread |
| `tests/Nota.SmokeTest/Program.cs` | modified | runs `TempoFollowTests` in the full smoke run, after loop record mode |
| `CHANGELOG.md` | modified | `Fixed` entry under `[Unreleased]` |

```csharp
partial void OnBpmChanged(decimal value)
{
    if (_followingBpm) return;   // the engine already has it (unrounded): don't echo it back
    _sentBpm = (double)value;
    _sentBpmAt = Environment.TickCount64;
    _engine.SetBpm((double)value);
}

private void FollowEngineBpm()
{
    double engineBpm = _engine.Bpm;
    if (_sentBpm is double sent)
    {
        bool landed = Math.Abs(engineBpm - sent) < 1e-6;
        if (!landed && Environment.TickCount64 - _sentBpmAt < SentBpmTimeoutMs) return;
        _sentBpm = null;
    }
    decimal bpm = Math.Round((decimal)engineBpm, 2);   // the field's resolution
    if (bpm == Bpm) return;
    _followingBpm = true;
    Bpm = bpm;
    _followingBpm = false;
}
```

## Tests Added or Updated

- `TempoFollowTests`: an outside tempo reaches the field; it shows rounded to hundredths
  without the rounded value being written back to the engine; consecutive outside changes
  are each followed; a tick before the engine applies the field's own tempo keeps the
  field's value; a drag that runs ahead of the engine keeps its latest value and lands on
  it; outside changes are followed again once the field's own change has landed.

## Local Verification

- Commands run, Ubuntu 26.04 x64, .NET SDK 10.0.112:
  - `dotnet build Nota.sln -c Release -nodeReuse:false -m:1` → 0 errors, no new warnings.
    (The parallel build crashes on this machine with `Internal CLR error (0x80131506)`
    before compiling anything, on `develop` as well. It's an SDK problem on this host and
    unrelated to the change.)
  - `dotnet run --project tests/Nota.SmokeTest -c Release` → `SMOKE TEST PASSED`; all
    9 `tempo follow` checks pass.
- The tests fail without the fix. Against `develop`'s `TransportViewModel.cs`, the 5 "follow
  an outside tempo" checks fail. Against the assessment's naive remediation (follow the
  engine in `Tick` with no in-flight guard), 4 checks fail: the field snaps back mid-drag,
  and the rounded value is echoed to the engine.
- Manual checks: pending (`/speckit-bug-test`). These are: MCP `set_tempo` updates the BPM
  field in the running app, a save and reopen keeps it, and a drag of the field is smooth.

## Deviations from Assessment

- The assessment said the `OnBpmChanged` echo would be harmless, because the engine would
  already match the field by the next `Tick`. That's wrong: `Engine::setBpm` only queues a
  `SetBpm` command for the audio thread, and `Engine::bpm()` reads the transport's value,
  which updates when the audio thread drains the queue (`Engine.cpp:438`). A naive follow
  in `Tick` reads the old tempo after a field edit and snaps the field back mid-drag.
  The fix adds two things for this:
  - an in-flight guard: wait until the engine reports the sent tempo, with a 1 s timeout for
    when no audio thread is draining commands.
  - an echo guard (`_followingBpm`, like the existing `_syncingLoop`): following the engine
    never writes back to it.
- Following doesn't write the rounded value back to the engine (an outside 110.333 shows as
  110.33 and keeps playing at 110.333). The assessment assumed the echo would normalise
  the engine to the field. Not echoing keeps follow read-only, so it can't race a second
  outside change.

## Follow-ups

- Time signature, metronome and master volume set over MCP probably diverge from the
  view-model in the same way. That needs engine getters (see the assessment, *Related*).
  The loop could use the existing `SyncLoop()`.
- `Engine::bpm()` is read from the UI thread while the audio thread writes `bpm_` (a plain
  `double`). This predates the fix, since MCP `get_transport` already reads it, but it's
  worth making atomic.
