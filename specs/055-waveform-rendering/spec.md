# Feature Specification: Default waveform rendering

**Feature Branch**: `055-waveform-rendering`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#267 — "A reviewer's first impression of the arrangement
was the waveform drawing: 'really nasty waveform rendering'. Review the default waveform style
in arrangement clips and the clip editor: anti-aliased min/max peaks with RMS body, correct
mapping at every zoom level (no stair-stepping or aliasing when zoomed out), crisp on HiDPI.
Pick the best-looking style as the default; keep the Waveform Style preference for the others.
Before/after screenshots at three zoom levels in the PR."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: audio clips on the lanes draw min/max peaks either as
**Bars** (a 1 px stroke every 3 px, mirrored about the centre — the default) or **Solid** (a
1 px column per logical pixel), Settings → Appearance → Waveform. The clip editor draws one
rectangle per peak bucket. The engine only returns min/max per bucket (a 512-frame min/max
block table, `SampleBuffer::peakTable`); zoomed views fetch range peaks at one bucket per
logical pixel (#299).

## Clarifications

### Session 2026-10-30

- Q: Where does the RMS body come from? → A: The engine — a mean-square table per 512-frame
  block next to the min/max table, so RMS over long spans costs the same as the peaks.
  Approximating it from min/max in the UI would not be RMS.
- Q: What is the new default called, and what happens to people on the old default? → A:
  **Smooth**. The setting stores 0 for the default; 0 now means Smooth, Solid stays 1 and
  Bars moves to 2. Everyone who never changed it (and anyone who picked Bars) sees Smooth once
  and can pick Bars again.
- Q: Does the clip editor follow the preference? → A: No — it never did; it now always draws
  Smooth. The preference keeps covering the lanes (and Solid / Bars in the browser preview).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Audio clips read cleanly on the lanes (Priority: P1)

A user drops audio onto the arrangement. At any zoom, the clip shows a smooth outline of its
peaks with a denser body for its loudness; transients stand out, quiet parts stay visible as a
line.

**Acceptance Scenarios**:

1. **Given** a drum loop on a lane, **When** zoomed far out, **Then** every transient shows
   (nothing drops between pixels) and the outline has no jagged steps.
2. **Given** the same clip zoomed in past one bucket per pixel, **Then** the outline slopes
   between points instead of stair-stepping.
3. **Given** a HiDPI display, **Then** the waveform is resolved per device pixel.

### User Story 2 - The clip editor matches (Priority: P1)

The audio clip editor draws the same style; zoomed to sample level it shows a thin
anti-aliased trace rather than filled blocks.

### User Story 3 - The other styles stay available (Priority: P2)

Settings → Appearance → Waveform offers Smooth (default), Solid and Bars; Solid and Bars draw
exactly as before.

### Edge Cases

- Silence draws as a line one device pixel thick.
- Import placeholders (no RMS known yet) draw the outline only.
- Reversed, warped, trimmed-while-dragging and ADSR-shaped clips keep their mapping; gain and
  the ADSR scale the body like the peaks.

## Requirements *(mandatory)*

- **FR-001**: The engine returns, with any clip peak query, each bucket's RMS of the stereo
  mid, over the same frames as its min/max.
- **FR-002**: One new C ABI call (`nota_clip_get_levels`) covers the five clip peak views; the
  existing calls are unchanged.
- **FR-003**: The lanes (Smooth) and the clip editor draw an anti-aliased filled peak envelope
  in a lighter ink of the clip's waveform colour and the RMS body in the full ink.
- **FR-004**: Peak buckets narrower than a device pixel aggregate into that pixel (min of mins,
  max of maxes, power-mean of RMS); wider buckets become one vertex at their centre.
- **FR-005**: Zoom-detail fetches request one bucket per device pixel.
- **FR-006**: The preference offers Smooth / Solid / Bars, Smooth by default.

## Success Criteria *(mandatory)*

- **SC-001**: Before/after screenshots at three zoom levels for the lanes and the clip editor.
- **SC-002**: Smoke test checks RMS against a brute-force scan (short and long spans) and that
  min/max are unchanged when RMS is requested.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: none — the table is built where the min/max table is (decode /
  load, message thread); queries run on the UI thread against the authoring graph
- [x] Managed/native crossings go through `nota_engine.h` only (AR-7)
- [x] Smoke test extended
- [x] `CHANGELOG.md` and `FEATURES.md` updated
- [x] Follows `DESIGN.md`: waveform ink from the clip colour, washes via `NotaPalette.Wash`
