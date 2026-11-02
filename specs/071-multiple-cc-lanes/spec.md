<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Feature specification: multiple MIDI CC lanes and saved heights

Approved issue: [nota-daw/nota#422](https://github.com/nota-daw/nota/issues/422).
Date: 2026-11-02. Branch: feat/multiple-midi-cc-lanes.

## User scenarios and acceptance

### US1 — Edit several controllers together (P1)
Use + below the piano roll to add three lanes. Each has its own controller picker,
continuous/switch editor, shared timeline and existing undo boundary. The primary
Velocity/MPE/controller lane remains available. Changes target only the chosen CC.

### US2 — Hide a lane without deleting music (P1)
Use a lane's minus control to hide it; re-add its controller and see identical MIDI
points. Undo/reload or changing the active clip cancels stale gestures and refreshes
all visible lanes. Many lanes scroll while leaving notes usable in a short window.

### US3 — Resize lanes independently (P1)
Drag the upper boundary of Velocity or an individual CC lane. Other lane heights,
notes, controller values and undo history remain unchanged. Reopen the editor or
restart the app, re-add the same lane slots/controllers, and recover saved heights.

## Functional requirements

- FR-001: Add native plus/minus controls and independent controller selection.
- FR-002: Reuse the current CC editor and its existing commit/undo callbacks.
- FR-003: All lanes share active-clip mapping, cache, horizontal zoom and scroll.
- FR-004: Hiding/reselecting/reloading cancels stale gestures without deleting MIDI.
- FR-005: Bound overflow with scrolling, keeping notes and the primary lane usable.
- FR-006: Independently resize primary/extra lanes within readable bounds; persist
  sizes in application preferences, tolerating old or invalid settings.
- FR-007: Follow existing Studio controls, all official themes and localization;
  update the Shortcuts reference for the divider gesture.
- FR-008: Preserve engine/ABI, transport/playheads, zoom limits, note geometry,
  workspace activation and official app identity. Add no dependency.
- FR-009: Pass Windows build/smoke and real-window regression checks.

## Success criteria

- SC-001: Three distinct controller lanes are visible and independently editable.
- SC-002: Removing/re-adding lanes leaves MIDI byte-identical; each edit uses one
  existing commit callback and preserves unrelated controllers/notes.
- SC-003: Many lanes scroll in a short window while notes stay usable; cache reloads
  and gutter alignment follow the active editor.
- SC-004: Velocity and two CC lanes retain independent sizes after reopening and
  reading saved settings in another process; malformed sizes remain readable.
- SC-005: Theme/localization/shortcut guards, real UI probes and Windows smoke pass.

## Clarification and scope

This PR implements the multiple-lane and independent-resizing portion of approved
issue #422. Reordering, a separate collapse control, and persisting which extra lanes
are visible are deferred; use Refs #422 rather than closing the whole issue.
Extra lane slots are editor-local; sizes are preferences, not MIDI/project edits.
Reserved controllers retain the current picker's restrictions. A hidden lane may be
re-added; no data is cleared. Default primary/extra heights remain 72/112 logical
pixels, with saved sizes normalized to 48–480. The primary row additionally keeps a
72-pixel display minimum so all five tabs remain reachable, including in short editors
and when restoring older saved sizes. Extra lanes retain a 48-pixel minimum.
No new key binding is introduced.
The personal workspace/zoom changes and named portable build stay outside this PR.
No additional user clarification is needed for this bounded contribution.
