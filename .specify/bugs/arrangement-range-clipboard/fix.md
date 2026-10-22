# Bug Fix: Arrangement clipboard ignores time ranges and the target track

- **Slug**: arrangement-range-clipboard
- **Fixed**: 2026-10-22
- **Assessment**: ./assessment.md
- **Issue**: nota-daw/nota#281
- **Status**: applied

## Summary

The engine can now copy a time range into the block clipboard. `copyRange` slices every
covered clip at the range edges with `midiSlice` / `audioSlice`, positions each slice from
the range start and carries its automation. `cutRange` adds the range delete in one undo
step. A range block pastes in overwrite mode, like ⌘D on a range. A cross-track paste anchors
on the range's top row, even when that row has no clips. In the app, ⌘C / ⌘X and Edit ▸ Copy
/ Cut act on a time range first. The range's right-click menus have Copy / Cut / Duplicate.
⌘V pastes at the playhead onto `FocusTrackId`, the track clicked last. A clip or range copy
moves that focus to the block's top track, so copy → ⌘V still lands on the source.

Open questions from the assessment, resolved with the recommended defaults:
- A pasted range overwrites what it lands on, matching ⌘D on a range.
- Duplicate was already available through ⌘D on a range. The range menus now list it too.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine.h` | modified | `copyRange` / `cutRange`, `clipboardBlockOverwrite_`, `clipboardBlockAnchor_`, `remapBlock(…, anchorTrackId)` |
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | range copy/cut; paste uses overwrite + anchor for range blocks; clip copy resets both |
| `src/native/nota.engine/src/engine/Engine_Session.cpp` | modified | block → session paste honours the anchor |
| `src/native/nota.engine/include/nota/nota_engine.h`, `src/abi/nota_engine_track.cpp` | modified | `nota_clips_copy_range` / `nota_clips_cut_range` |
| `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs`, `src/managed/Nota.Application/Engine/IAudioEngine.cs` | modified | `CopyRange` / `CutRange` |
| `src/managed/Nota.App/ArrangementView.cs` | modified | `CopyTimeSelection` / `CutTimeSelection`; `PasteClipboard` targets `FocusTrackId`; copy/cut focus the block's top track |
| `src/managed/Nota.App/ArrangementView.LaneControl.cs` | modified | lane + clip menus: Copy / Cut / Duplicate act on the range when right-clicked inside it |
| `src/managed/Nota.App/MainWindow.Input.cs`, `MainWindow.Menu.cs` | modified | ⌘C / ⌘X and Edit menu: range first |
| `src/managed/Nota.App/PreferencesWindow.cs` | modified | Shortcuts row for ⌘C ⌘X ⌘V |
| `tests/Nota.SmokeTest/RangeClipboardTests.cs`, `Program.cs` | added test | 21 checks; full run + `--range-clipboard` |
| `CHANGELOG.md`, `FEATURES.md` | modified | [Unreleased] → Fixed; Arrangement View entry |

## Verification

- `--range-clipboard` smoke: 21/21 pass. Covered: MIDI and audio slices, the leading gap,
  overwrite on paste, the clip-block paste still shifting, cut + undo, the empty-top-row
  anchor and an empty range.
- Headless harness (`/tmp/rangeharness`): the real MainWindow driven with Meta+C/X/V key
  presses. Checked: a clip pastes onto the focused track B; copy → ⌘V with no click pastes
  on the source; range ⌘C leaves the project unchanged and ⌘V puts the slice on B; range ⌘X
  cuts.
- `scripts/build.sh`: see the test record.
