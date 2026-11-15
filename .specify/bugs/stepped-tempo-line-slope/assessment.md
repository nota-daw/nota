# Bug Assessment: Stepped tempo line is slightly diagonal

- **Slug**: stepped-tempo-line-slope
- **Created**: 2026-11-11
- **Source**: pasted text
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

When tempo changes instantaneously at a stepped automation point, the connecting line is
slightly diagonal instead of perfectly vertical. The hold must end at the event's exact X
coordinate and the vertical transition must use that same X at both endpoints at every zoom,
DPI scale and scroll position. Linear and smooth modes must remain unchanged.

## Symptom

The stepped curve visually ramps across a few pixels around a tempo point even though playback
changes instantaneously. It should draw a horizontal hold followed by a zero-width vertical edge
aligned with the tempo point and grid.

## Reproduction

1. Add two tempo points with different BPM values.
2. Set the first point's outgoing shape to Step.
3. Zoom or scroll until the second point falls between the renderer's three-pixel samples.
4. Observe a short diagonal connector instead of a vertical edge at the second point.

## Suspected Code Paths

- `src/managed/Nota.App/ArrangementView.Tempo.cs:376` — `TempoLane.Render` samples the
  authoritative curve every three pixels and connects every pair with a straight line.
- `tests/Nota.TempoUiTest/Program.cs` — headless interaction coverage can pin the rendered
  stepped edge to the exact point coordinate.

## Root Cause Hypothesis

High confidence: the renderer does not special-case discontinuities. If a stepped event lies
between two three-pixel samples, `DrawLine` connects the old BPM sample to the new BPM sample,
creating a diagonal whose horizontal width is the sampling gap. Coordinate transforms and engine
timing are correct; only the polyline construction is wrong.

## Proposed Remediation

**Preferred**: while retaining native sampling for linear and smooth curves, detect each stepped
boundary crossed between adjacent samples. Draw from the prior sample to the exact transformed X
of the tempo point at the old BPM, draw the vertical edge using that X for both endpoints, then
continue from the new BPM endpoint. Use `BeatToX` for the boundary so scroll, zoom and DPI use the
same coordinate transform as the marker and musical grid.

**Files likely to change**:

- `src/managed/Nota.App/ArrangementView.Tempo.cs`
- `tests/Nota.TempoUiTest/Program.cs`

**Tests to add or update**:

- Render a stepped map at a boundary deliberately falling between sample positions and verify the
  transition column aligns with the point after zooming and scrolling.
- Rerun all Tempo UI interactions and the managed build.

## Risks & Considerations

- Do not change tempo-map evaluation or playback timing.
- Do not alter the sampled rendering path for linear and smooth segments.
- Handle multiple offscreen or tightly spaced step boundaries without introducing offsets.

## Open Questions

None.
