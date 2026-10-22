# Bug Assessment: Arrangement clipboard ignores time ranges and the target track

- **Slug**: arrangement-range-clipboard
- **Created**: 2026-10-22
- **Source**: pasted text (issue, reported in Russian)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

> When I Shift-select part of a track in the arrangement, I want to be able to copy, duplicate
> or cut it. Cmd+Shift+V works as "paste bounced", but plain copy doesn't work.
> Also: there's no way to copy a clip from one track to another except by dragging. E.g. I copy
> a clip on one track with Cmd+C, put the cursor on another track, press Cmd+V — the copy
> lands on the source track.

Two problems:

1. **Time range → clipboard.** A Shift+drag time-range selection has no Copy (⌘C) or Cut (⌘X).
   ⌘D (duplicate range) and Delete already work on the range; ⌘⇧V (Paste bounced audio) works
   because it remembers the range separately (`BounceSource`).
2. **Keyboard paste ignores the target track.** ⌘V always pastes onto the tracks the clips
   were copied from, wherever the user last clicked.

## Symptom

1. With a time range selected, ⌘C / ⌘X / Edit ▸ Copy / Cut do nothing (no status message, the
   clipboard is unchanged). Expected: the covered slice of every selected track goes to the
   clipboard, sliced at the range edges; Cut also removes it.
2. ⌘C on a clip on track A, click empty space on track B (moves the playhead), ⌘V: the copy
   lands on track A at the playhead. Expected: it lands on track B. The right-click lane menu's
   **Paste** already does this; only the keyboard and Edit-menu paste don't.

## Reproduction

Range copy:
1. Shift+drag over part of a clip on one track to make a time range.
2. Press ⌘C. Then click elsewhere and press ⌘V.
3. Nothing is pasted (or the clip copied earlier is pasted instead). ⌘X likewise does nothing.

Cross-track paste:
1. Click a clip on track A, press ⌘C.
2. Click empty lane space on track B at some beat (the playhead moves there).
3. Press ⌘V. The copy appears on track A at the playhead, not on track B.

## Suspected Code Paths

- `src/managed/Nota.App/MainWindow.Input.cs:272-287` — the ⌘C/X/V handler. Outside automation
  mode it only calls `Timeline.CopySelectedClip` / `CutSelectedClip` / `PasteClipboard`; there
  is no time-range branch. (⌘D at `MainWindow.Menu.cs:106` and Delete at `MainWindow.Input.cs:299`
  do check `DuplicateTimeSelection` / `DeleteTimeSelection` first.)
- `src/managed/Nota.App/MainWindow.Menu.cs:151-174` — `OnMenuCopy` / `OnMenuCut` /
  `OnMenuPaste` (Edit menu): same gap.
- `src/managed/Nota.App/ArrangementView.cs:1593-1601` — `SetTimeSelection` clears the clip
  selection (the two are mutually exclusive), so `SelectionBlock()` is empty and
  `CopySelectedClip` / `CutSelectedClip` return false: the keystroke is silently dropped.
- `src/managed/Nota.App/ArrangementView.cs:1290-1299` — `PasteClipboard()` (keyboard path)
  calls `_engine.PasteClipBlock(playhead, -1)`; `-1` = "paste onto the source tracks". Its doc
  comment states this as the intent. `PasteClipboardAt(trackId, beat)` (used by the lane menu)
  already remaps onto a destination track.
- `src/managed/Nota.App/ArrangementView.cs:1608-1611` + `ArrangementView.LaneControl.cs:587` —
  `FocusTrackId`: the track the user last clicked (clip, header or empty lane space). Paste
  bounced (`MainWindow.PasteBounced.cs:38`) already targets it; plain paste doesn't.
- `src/native/nota.engine/src/engine/Engine_Tracks.cpp:2740` (`copyClipBlock`), `:2749`
  (`cutClipBlock`), `:1773` (`duplicateRange`) — the engine has a block clipboard of whole
  clips, and range slicing (`midiSlice` / `audioSlice`) used by `duplicateRange`, but no
  operation that captures a *range* into the block clipboard.

## Root Cause Hypothesis

Not a regression; the feature was never built. The time-range selection (Phase 2) received
Delete, Duplicate, Split, Consolidate, Duplicate Time, Insert Silence and Paste Bounced, but
not Copy/Cut. The keyboard paste was designed to restore clips onto their source tracks at
the playhead, so the lane menu's track-aware paste never reached ⌘V. Confidence: high. Both
problems follow directly from the code above.

## Proposed Remediation

**Preferred**:

*Engine (C++ + C ABI).* Add `Engine::copyRange(trackIds, start, end)` and
`Engine::cutRange(...)`. `copyRange` builds `BlockClip`s from `midiSlice` / `audioSlice` of
every clip that overlaps `[start,end)` on each listed track. Each `relStart` is measured from
`start`, not from the first clip, so leading space in the range is kept. Each slice's
clip-relative automation goes in the same way `captureBlock` does it. It then sets
`clipboardBlock_` and `clipboardBlockLen_ = end - start`. `cutRange` = `copyRange` +
`deleteClipsInRange` (plus the range's automation) as one undo step. Expose
`nota_clips_range_copy` / `nota_clips_range_cut` and wrap them in `IAudioEngine` /
`NotaEngine.Tracks.cs`.

*App.*
- `ArrangementView`: add `CopyTimeSelection()` / `CutTimeSelection()` (use
  `TimeSelectionTrackIds()`, call `ClipTransfer.TookFromArrangement()`, and on cut refresh and
  clear the range).
- Put them ahead of the clip path in `MainWindow.Input.cs` (⌘C/⌘X) and in `OnMenuCopy` /
  `OnMenuCut`, mirroring how ⌘D and Delete already put the range first.
- Add Copy / Cut entries for the range to the lane context menu's `inRange` block
  (`ArrangementView.LaneControl.cs:1121`) and to the clip menu when right-clicking inside the
  range.
- Keyboard paste: `PasteClipboard()` uses `FocusTrackId` when it refers to a live track
  (`PasteClipBlock(playhead, FocusTrackId)`), falling back to `-1`. Because copying a clip
  also sets `FocusTrackId` to its track, "copy → ⌘V" without clicking elsewhere still pastes
  onto the source track. A multi-track block remaps by the top-track delta, the same as the
  lane menu today. Do the same for `PasteSessionSlot(…, -1)` so a slot copied in the Session
  view also lands on the focused track.

**Alternatives**:
- *Managed only:* `SplitClipsInRange` first, then select the covered clips and `CopyClipBlock`.
  No engine change, but **Copy then modifies the project** (it splits clips and adds an undo
  step), and the block length/offset is measured from the first clip, not the range start.
  Rejected.
- *Paste onto the selected track (`SelectedTrackId`) instead of `FocusTrackId`.* A click on
  empty lane space clears the selection, so the user's "put the cursor on another track"
  gesture would be lost. `FocusTrackId` exists for exactly this and is already used by Paste
  bounced.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine.h`, `Engine_Tracks.cpp`
- `src/native/nota.engine/include/nota/nota_engine.h`, `src/abi/nota_engine_track.cpp`
- `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs`
- `src/managed/Nota.Application/Engine/IAudioEngine.cs` (+ any test fakes implementing it)
- `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.LaneControl.cs`
- `src/managed/Nota.App/MainWindow.Input.cs`, `MainWindow.Menu.cs`
- Shortcuts pane (`nota-shortcuts`), `CHANGELOG.md`, `FEATURES.md`, docs (`nota-docs`)
- `tests/Nota.SmokeTest/…` (new checks)

**Tests to add or update** (smoke test, engine level + the `ArrangementView` methods):
- Range copy of part of a MIDI clip → paste elsewhere: notes match the slice and start at the
  same offset from the range start; the source is unchanged; undo count unchanged by the copy.
- Range copy of part of an audio clip → pasted clip has the sliced source offset/length.
- Range cut: covered content removed and on the clipboard, in one undo step; undo restores it.
- Multi-track range copy → paste with destination = second track: block shifts down by one;
  type-mismatched rows are skipped.
- Keyboard paste after focusing another track (`FocusTrackId` = B) lands on B; with focus
  still on the source it lands on the source (no behaviour change for the common case).

## Risks & Considerations

- **Behaviour change for ⌘V.** Users who copy, click on another track's empty space just to
  move the playhead, and expect a same-track paste will now get the cross-track paste. This
  matches the issue and other DAWs (Ableton pastes onto the selected track), but needs a
  CHANGELOG "Changed" entry.
- **Range paste and overlap.** `placeBlock` avoids overlap by shifting the whole block right.
  For a range paste, overwriting the target span (as `duplicateRange` does) may match user
  expectations better; decide in the fix (`placeBlock(..., overwrite)` already exists).
- **Automation.** Whether a range copy carries track automation for the span should match
  `duplicateRange` / Duplicate Time. Device/plugin automation is dropped on cross-track
  remaps, as today.
- Warped audio slices need `configureClipWarp` on paste. `placeBlock` already does this.
- Paste bounced (⌘⇧V) keeps working: it reads `BounceSource`, not the block clipboard.

## Open Questions

- [NEEDS CLARIFICATION: should a pasted range overwrite what it lands on (like ⌘D on a range)
  or shift right to avoid overlap (like clip paste)? Recommendation: overwrite, for parity
  with ⌘D.]
- [NEEDS CLARIFICATION: is "duplicate" in the report already covered by ⌘D on a range (it
  works today), or is something else expected?]
