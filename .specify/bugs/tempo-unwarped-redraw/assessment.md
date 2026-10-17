# Bug Assessment: un-warped audio clips keep their width when the tempo is changed in the transport bar

- **Slug**: tempo-unwarped-redraw
- **Created**: 2026-10-17
- **Source**: pasted text (user observation, Nota 0.55.1 on Ubuntu 26.04)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

> when I change the bpm, these no-warped tracks are not appearing to change size at all?
> e.g. when I used to un-warp tracks in ableton, then change the bpm, I would see the tracks
> expanding or contracting visually, and could easily line them up with the end of a bar for
> a given bpm

The user tried both ruler modes (bars and min:sec), zoomed and scrolled; the clips never
changed width.

## Symptom

An un-warped audio clip has a fixed duration in seconds, so its length in beats changes with
the tempo, and the engine reports that correctly. But when the tempo is changed in the
transport bar's BPM field, the arrangement keeps drawing every audio clip at the width it had
before. Expected: un-warped clips grow or shrink against the bar grid as the tempo changes
(as in Ableton), so a clip can be lined up with a bar line by ear and eye.

## Reproduction

1. Put an un-warped audio clip on an audio track (any one-shot or loop with warp off).
2. Drag or type a different tempo into the transport bar's BPM field.
3. The clip keeps its width. The engine's clip info has changed, though: `get_overview` over
   MCP reports a clip of 11.18 beats at 110 BPM as 9.14 beats at 90 BPM.
4. Change the tempo through MCP instead (`set_tempo`): the clip *does* resize at once.
   Confirmed by the reporter on 0.55.1.

## Suspected Code Paths

- `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs:68`: `OnBpmChanged` only calls
  `_engine.SetBpm`. Nothing tells the arrangement that clip geometry changed.
- `src/managed/Nota.App/MainWindow.axaml.cs:253`: the window's `Transport.PropertyChanged`
  handler updates the BPM field's readout on a `Bpm` change, but doesn't refresh the timeline.
- `src/managed/Nota.App/ArrangementView.cs:790` (`Refresh`): `ClipVM.LengthBeats` is a snapshot
  of `TryGetClipInfo(...).LengthBeats`, taken on refresh. Until the next refresh, the lane draws
  the stale length.
- `src/managed/Nota.App/MainWindow.axaml.cs:510` (MCP `OnRefresh`) and
  `src/managed/Nota.Mcp/EngineTools.cs:26` (`Mutate`): the MCP path calls `Timeline.Refresh()`
  after every edit. That's why `set_tempo` over MCP redraws correctly and the UI path doesn't.

## Root Cause Hypothesis

The arrangement caches each clip's beat length in its view-model and only re-reads it on
`Refresh`. Every arrangement edit refreshes, and so does every MCP mutation. A tempo change made
from the transport bar is neither: it goes from the view-model straight to the engine. So the
cached beat lengths of un-warped audio clips go stale until something unrelated triggers a
refresh. Confidence: high. The MCP path differs only by the refresh, and the reporter confirmed
that it resizes the clips.

## Proposed Remediation

**Preferred**: in `MainWindow`'s `Transport.PropertyChanged` handler, refresh the arrangement on a
`Bpm` change with `Timeline.Refresh(rebuildHeaders: false)`, the lightweight refresh already
used for the live recording tick. It keeps header controls and open menus intact. Dragging the
BPM field raises many changes per second, so coalesce them: post one refresh to the dispatcher
and skip further posts until it has run. Then a fast drag costs about one refresh per UI frame.

**Alternatives**:
- Raise a `TempoChanged` event from `TransportViewModel` and subscribe from the arrangement.
  That's cleaner in principle, but it adds an event for one subscriber. The window already owns
  the view-model → timeline wiring for every other transport property.
- Have `ArrangementView` read `LengthBeats` from the engine at draw time. That would put engine
  calls in the render path for every clip, and go against the snapshot design of `Refresh`.

**Files likely to change**:
- `src/managed/Nota.App/MainWindow.axaml.cs`
- `CHANGELOG.md` (user-visible fix)

**Tests to add or update**:
- The fix is in the Avalonia window wiring, which no automated test covers (the smoke test drives
  the engine through the C ABI; there is no UI test harness). Verify by hand on Linux: an
  un-warped clip resizes live while the BPM field is dragged, in both ruler modes. Also check
  that warped clips keep their beat length, and that the MCP path is unchanged.
- The smoke test must still pass.

## Risks & Considerations

- Performance: a live refresh refetches peaks (2048 buckets) for each audio clip whose beat
  length changed, which during a tempo drag means every un-warped clip. Coalescing caps it at
  about one refresh per frame. Reusing peaks across tempo-only changes would make it cheaper
  still, but that is a separate optimisation.
- No audio-thread impact: the change is UI-thread only. The engine call (`SetBpm`) is unchanged.

## Related (out of scope)

The reverse direction is also broken: `set_tempo` over MCP changes the engine but not
`TransportViewModel.Bpm`, so the BPM field shows a stale tempo and saving writes the stale value.
Tracked separately as `mcp-tempo-ui-sync`.

## Open Questions

- None.
