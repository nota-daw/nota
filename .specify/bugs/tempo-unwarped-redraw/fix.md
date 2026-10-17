# Bug Fix: un-warped audio clips keep their width when the tempo is changed in the transport bar

- **Slug**: tempo-unwarped-redraw
- **Fixed**: 2026-10-17
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

A `Bpm` change on the transport view-model now queues one live arrangement refresh. The
timeline re-reads each clip's beat length, so un-warped audio clips resize as the tempo
changes. A drag of the BPM field is coalesced to one refresh per dispatcher batch.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/MainWindow.axaml.cs` | modified | `Transport.PropertyChanged` → `RequestTempoRefresh()` on `Bpm`; new `_tempoRefreshPosted` flag + helper |
| `CHANGELOG.md` | modified | `Fixed` entry under `[Unreleased]` |

```csharp
private void RequestTempoRefresh()
{
    if (_tempoRefreshPosted) return;
    _tempoRefreshPosted = true;
    Dispatcher.UIThread.Post(() => { _tempoRefreshPosted = false; Timeline.Refresh(rebuildHeaders: false); }, DispatcherPriority.Background);
}
```

The coalescing copies `RequestTransportFit()` (`MainWindow.TransportFit.cs`), which uses a
posted flag and a single dispatcher post. `Refresh(rebuildHeaders: false)` is the live
refresh the recording tick uses: it rebuilds clip data and lanes but leaves header
controls and open menus alone, and it ends in `Redraw()`, so the ruler redraws too.

## Tests Added or Updated

- None automated. The change is in Avalonia window wiring, and the smoke test has no UI
  harness (as the assessment said). Verified by hand instead (`/speckit-bug-test`).

## Local Verification

- Commands run, Ubuntu 26.04 x64, .NET SDK 10.0.112:
  - `dotnet build Nota.sln -c Release -nodeReuse:false -m:1` → 0 errors, no new warnings.
  - `dotnet run --project tests/Nota.SmokeTest -c Release` → `SMOKE TEST PASSED`.
- Before the fix (reporter, 0.55.1): MCP `set_tempo` resized clips live (it refreshes the
  timeline after every mutation), but the BPM field didn't. That isolates the missing
  refresh as the cause.
- Manual checks: pending (`/speckit-bug-test`).

## Deviations from Assessment

- None.

## Follow-ups

- Peak reuse: during a tempo drag every un-warped clip refetches its 2048-bucket peaks,
  because `Refresh` only reuses peaks when start and length are unchanged. An un-warped
  clip's peaks don't depend on the tempo, so they could be reused across tempo-only changes.
