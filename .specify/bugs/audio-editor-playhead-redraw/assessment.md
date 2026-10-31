# Bug Assessment: playback stutters once the playhead reaches an audio clip

- **Slug**: audio-editor-playhead-redraw
- **Created**: 2026-10-30
- **Source**: https://github.com/nota-daw/nota/issues/142 (host `github.com`, allowlisted)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

> **Windows: FPS drops and the playhead stutters when it reaches a looped audio clip**
>
> An audio clip on a track with loop enabled on the clip. As soon as the playhead reaches the
> clip, the UI frame rate drops sharply and the playhead moves in jerks.
>
> 1. Arrangement, an audio track with an audio clip
> 2. Turn on Loop for the clip and extend it over several repeats
> 3. Play from before the clip — when the playhead enters it, the UI stutters
>
> Nota 0.55.1, Windows 11 x64. Suspect the looped waveform being redrawn (or re-computed)
> every frame during playback.

No comments on the issue.

## Symptom

During playback the UI frame rate drops, and the playhead moves in jerks, from the moment the
playhead enters an audio clip. Expected: playing through an audio clip costs the UI no more
than playing through empty bars.

## Reproduction

1. Arrangement, an audio track with an audio clip (a long one makes it worse).
2. Open the clip in the Clip panel: double-click it, or select it and press the Clip tab.
3. Play from before the clip. Once the playhead enters the clip, the UI stutters.

Arrangement audio clips have no per-clip Loop switch, in 0.55.1 or now. The only clip
"Loop" switch is a disabled placeholder in the MIDI clip inspector (`ClipPropsView`); the
clip menu's "Loop selection" sets the transport loop to the clip. Looking for the switch
opens the clip in the Clip panel, which is the condition that triggers the bug.
[NEEDS CLARIFICATION: whether the reporter's Clip panel was open — very likely, see above.]

## Suspected Code Paths

- `src/managed/Nota.App/MainWindow.axaml.cs` (`PlayheadUpdated` handler): every UI tick
  (~30 Hz) calls `_audioEditor.OnPlayhead(beats, playing)` while the editor is visible.
- `src/managed/Nota.App/AudioClipEditorView.cs` `OnPlayhead`: −1 outside the clip; inside it,
  a fraction that changes every tick.
- `AudioClipEditorView.WaveformView.SetPlayhead`: any change calls `InvalidateVisual()` on
  the whole waveform view.
- `AudioClipEditorView.WaveformView.Render`: repaints everything — background, ruler with
  formatted labels, snap grid, a `FillRectangle` per visible peak bucket (one per pixel
  column or more), warp markers and BPM chips, envelope, ADSR — just to move the 1px line
  drawn at the end.

Not the cause, but checked: the arrangement lanes don't repaint on a tick (the playhead lives
on `LaneOverlayControl`, #55) unless Follow scrolls the view, and that doesn't depend on
reaching a clip. The piano roll's `SetPlayhead` repaints only its grid and ruler.

## Root Cause Hypothesis

The audio clip editor draws its playhead inside the same `Render` as everything else. Outside
the clip `SetPlayhead(-1)` is a no-op, so nothing repaints. From the moment the playhead
enters the open clip, every transport tick invalidates and fully re-renders the editor. Its
`Render` emits thousands of draw operations and allocates `FormattedText` for the ruler.
That load, on the UI thread at 30 Hz, starves the rest of the window, so it starts exactly when
the playhead "reaches the clip". Confidence: high for the mechanism; medium that it is the
reporter's only trigger (not reproduced on Windows).

## Proposed Remediation

**Preferred**: move the playhead line onto its own hit-transparent layer stacked over the
waveform view (the arrangement's `LaneOverlayControl` pattern). `SetPlayhead` invalidates only
the layer. Every existing full repaint of the view (zoom, scroll, edits, region changes) also
invalidates the layer, so the line can never lag the content it sits on.

**Alternatives**:
- Cache the waveform into a bitmap and blit it each tick. Same effect, but needs dirty
  tracking for every input of `Render` and holds a bitmap per editor.
- Throttle the editor's playhead to a lower rate. Hides the cost and makes the cursor choppy.

**Files likely to change**:
- `src/managed/Nota.App/AudioClipEditorView.cs`
- `CHANGELOG.md`

**Tests to add or update**:
- No UI test harness exists (the smoke test drives the engine through the C ABI). Verify by
  hand: playback through an open audio clip before/after (UI-thread CPU), and the playhead
  line still tracks zoom, scroll, reverse and trim; ruler / playhead-line scrubbing still seeks.

## Risks & Considerations

- The layer must be repainted whenever the line's position inputs change (zoom, scroll,
  placement, region, reverse, size). Routing every existing invalidation of the view through
  one `Redraw()` that also invalidates the layer covers them; a resize re-renders both.
- UI-thread only; no engine or audio-thread change.

## Open Questions

- Was the Clip panel open in the report? The fix only changes behaviour when it is.
