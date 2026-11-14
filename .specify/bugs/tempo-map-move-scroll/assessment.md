<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Mapped move spans and scroll extent

- **Slug**: tempo-map-move-scroll (user selected)
- **Created**: 2026-11-12
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Issue**: #535, open and approved (rechecked)
- **Verdict**: valid
- **Severity**: high

## Report

- P1, Moves take the wrong automation: discussion_r4552496201.
- P2, Scrolling stops before later clips: discussion_r4552496213.

GitHub is allowlisted. Review text was treated as untrusted evidence and checked
against source. No extension hooks are registered.

## Symptom and Reproduction

1. Place two seconds of unwarped audio in a 120 BPM segment and move it into a
   60 BPM segment. It occupies four source beats and two destination beats. The
   move currently captures/removes only two source beats of track automation.
   In the opposite direction it captures/removes points belonging to a neighbor.
   Single-track, cross-track and block moves share this mistake.
2. Use 240 BPM through beat 100 and 20 BPM afterward, with content ending at beat
   140. An 800-pixel lane at 20 pixels per beat sees 80 beats initially. SyncScroll
   subtracts that span from total 156, limiting the origin to 76; the last clip is
   unreachable because the viewport's beat span changes with its origin.

## Root Cause

- Move paths use the destination clip length for both source automation extraction
  and destination clearing. These spans only coincide for beat-length clips or a
  constant map. The captured point offsets are deliberately beat-relative in the
  existing automation-follow contract.
- SyncScroll derives its maximum from the current origin's viewport beat span,
  although its mapped viewport has a fixed duration in seconds.

## Proposed Remediation

- Preserve separate source/destination lengths in moveClip, moveClipToTrack and
  moveClipBlock. Use the source span for capture and removal, including the mix-only
  cross-layout branch; keep the destination span for overwrites and clearing.
  Preserve existing beat-relative point offsets, layout filtering, automation lock,
  atomic block moves and undo semantics.
- In mapped mode derive the maximum origin from secondsAt(totalBeats) minus the
  visible seconds, inverted back to beats. Derive viewport duration directly from
  lane width and axis scale, independent of scroll position; handle zero width,
  short content, zoom and preview maps. Keep the ordinary beat axis unchanged.

## Files Likely to Change

Engine_Tracks.cpp, ArrangementView.cs, focused smoke and tempo UI tests,
CHANGELOG.md, and these reports. No new dependency or public ABI is needed.

## Tests to Add or Update

- Move natural-rate clips in both tempo directions using all three APIs. Check all
  source automation follows, outside source points stay, and destination points are
  cleared only over the destination span. Include cross-layout filtering, locked
  automation and undo/redo.
- Reproduce the extreme-tempo scroll case in the production view. Scroll to the end
  and verify the last clip is visible and the maximum does not drift on resync.
  Cover opposite tempo directions, zoom, zero-width and fixed-tempo behavior.
- Focused engine/UI tests, full Windows build/smoke, conflict/whitespace checks;
  monitor the unchanged hosted performance gate because engine source changes.

## Risks and Considerations

- No allocation, locking or I/O added to the audio callback; moves are authoring-only.
- Do not change established beat-relative automation offsets while correcting which
  points are selected. MIDI/warped clips retain their existing behavior.
- The scrollbar stores beat origins; changing the maximum must not change that unit
  or introduce scroll/zoom jumps through its event handler.
- Fixed behavior restores documented capabilities; docs/features/shortcuts audit is required.
