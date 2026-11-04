# Implementation Plan: Linked editing on group tracks

**Branch**: `feat/folder-track-groups` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/076-group-link/spec.md` (issue
[nota-daw/nota#184](https://github.com/nota-daw/nota/issues/184))

## Summary

A Group track gets a **Link** flag. While it is on, the non-group tracks below it form a
*link set*: arming one arms the set, selecting a clip also selects its *matching* clips
(same start and end) on the other members, a time range touching a member spans every
member, and Arrangement clip edits act on the matches as one undo step.

The approach keeps the change small by making **selection the carrier of the link**: the
Arrangement's existing multi-selection (`_sel`) and the block edits built on it (move
block, nudge, duplicate, copy, cut, delete) already act on every selected clip, so adding
the matches to the selection links those edits for free. Only the edits that act on one
grabbed clip (trim, clip ADSR / fades), split at the playhead, time ranges, vertical moves
and arming need their own linking code. The flag itself is engine-side track metadata —
the same pattern as `pianoFold` / `colorIndex` — so it is undoable, copied with the group
and saved with the project, and the audio thread never reads it.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the change is one authoring
  flag on `Track` published through the usual clone-and-republish path (AR-5); nothing on
  the audio thread (AR-6); one new ABI pair in `nota_engine.h` (AR-7).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: the Link key
  is a small header key like M / S; orange accent only while on.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine: one flag + ABI), C# / .NET 10 (managed app: the bulk).

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: project JSON — `TrackDto.EditLink` (`bool?`, written only when on, like
`PianoFold`); no format-version bump (an absent field reads as off; older builds ignore it).

**Audio-Thread Impact**: None. `Track::editLink` is authoring metadata copied on clone; the
render path never reads it. Setting it goes through `republishWithTrack` on the message
thread (AR-5), like the track colour.

**Testing**: `tests/Nota.SmokeTest` (engine via C ABI: set / get / undo / duplicate group /
save-load round-trip); pure-logic tests for link-set and matching rules
(`Nota.Presentation` / `Nota.Application.Editing` classes, run from the smoke test like
`ArrangementMoves`); in-app verification with the headless harness (memory: UI verify recipe).

**Target Platform**: macOS, Windows, Linux (desktop app).

**Project Type**: desktop app (engine + managed UI).

**Performance Goals**: link-set and match computation per click/edit is O(tracks × clips of
the set) on the UI thread — trivially below a frame for realistic projects (hundreds of clips).

**Constraints**: no change in behaviour while every group's Link is off (FR-014); each
linked edit one undo step (SC-003).

**Scale/Scope**: ~1 engine flag + ABI pair, ~8 managed files touched, 1 new Application
class, MCP tool pair, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|-----------|--------|
| 0. Approved issue | ✅ #184 carries `approved` (2026-11-04). |
| I. Real-time safety | ✅ Flag never read on the audio thread; set via clone + republish (AR-5). |
| II. Boundary discipline | ✅ One `nota_track_set_edit_link` / `get` pair in `nota_engine.h`; no JUCE. |
| III. Licensing | ✅ No dependency; new files carry the SPDX header. |
| IV. Verification gate | ✅ Smoke test extended for the new ABI (set/get/undo/dup/save-load). |
| V. One logical change | ✅ One feature; the shared rule lives in one new class. |
| VI. Docs in sync | ✅ CHANGELOG, FEATURES.md (nota-features), user docs (nota-docs), Shortcuts (nota-shortcuts: ⌥-click bypass). |
| VII. Spec-first | ✅ This run. |
| VIII. No weakening | ✅ Nothing bent. |

Post-design re-check (after Phase 1): unchanged — the design adds no audio-thread code and
one ABI pair. **PASS.**

## Design overview

### 1. The flag (engine → ABI → managed)

- `Track.h`: `bool editLink = false;` beside `pianoFold`; copied by `cloneTrack` (so
  duplicating / copy-pasting a group keeps it).
- `Engine::setTrackEditLink(id, on)` — group tracks only (false otherwise), clone +
  `republishWithTrack` (**an undo step**, unlike `pianoFold`'s raw republish);
  `trackEditLink(id)` → 1 / 0 / -1.
- ABI: `nota_track_set_edit_link` / `nota_track_get_edit_link` (see
  [contracts/c-abi.md](contracts/c-abi.md)).
- Managed: `NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs`,
  `IAudioEngine.SetTrackEditLink / GetTrackEditLink` (`NotaEngine` is the only
  implementation; the smoke test drives the real engine).
- Persistence: `TrackDto.EditLink` written as `true` or omitted; applied after the track
  hierarchy is rebuilt on load.

### 2. The rule (one class, pure)

`Nota.Application.Editing.GroupLink` (new), fed with plain track and clip records so it is
testable without the engine:

- `LinkSets(tracks)` → for every non-group, non-return track: the id of its outermost
  linked ancestor group (or none). Built from `GroupId` / `IsGroup` / `EditLink`.
- `Members(trackId)` → the other tracks of its set (empty when unlinked).
- `Matches(source clip, member clips)` → clips with |Δstart| and |Δend| ≤ 1/256 beat
  (FR-006).
- `ExpandTracks(trackIds)` → the set closed under membership (for ranges and split).

`ArrangementView` builds it from its `_tracks` (which already carry `GroupId`, `IsGroup`;
`EditLink` is added to the track VM on `Refresh`), and `TrackArm` builds it from
`IAudioEngine.TryGetTrackInfo` + `GetTrackEditLink`.

### 3. Selection carries the link

- `Select(track, clip)` (plain click, drag start on an unselected clip) adds the matches;
  they go into `_sel` and into a parallel `_selLinked` set marking them as *added by the
  link* (not chosen by the user).
- `ToggleSelect` (⌘-click) adds or removes the clip with its matches.
- `SetSelection` (marquee, ⌘A, reselect after paste/move): unchanged — it already contains
  what it covers; `ReselectPlaced` after a linked move re-derives `_selLinked` by matching.
- Bypass (FR-012): a ⌥-click that releases without dragging selects the clip alone (no
  matches). The lane already tracks the modifiers of the press (`_lastMods`).
- Visual: linked matches draw selected like any selected clip; nothing new.

With that, **move block, nudge, duplicate, copy, cut, delete** need no change.

### 4. Edits that need their own linking

| Edit | Where | Change |
|------|-------|--------|
| Trim / resize | `LaneControl.OnPointerReleased` (single `_dragClip`) | Apply the same new start/length to the grabbed clip's matches **that are in the selection**, in one undo group. |
| Clip ADSR / fades | `LaneControl.CommitAdsr` | Same: matches in the selection get the same `ClipAdsr` (audio clips only), one undo group. |
| Split at playhead (⌘E) | `SplitSelectedAtPlayhead` | Add, for each link set touched by the selection, the clips of the members spanning the playhead (FR-010); wrap the loop in one undo group. |
| Split here (clip menu) | LaneControl context menu | Route through the same linked split at the menu's beat. |
| Delete (multi) | `DeleteSelectedClips` | Wrap in an undo group (today: one step per clip) so a linked delete is one step. |
| Time range | `TimeSelectionTrackIds` | Return `GroupLink.ExpandTracks(rows lo..hi)`; the drawn range spans the member rows (`_timeSelRowLo/Hi` widened to the set's row extent, a set's rows being contiguous in hierarchy order). |
| Vertical move (drag) | group move commit | Entries in `_selLinked` keep their own track (`destTrack = m.track`) but take the time shift; the drag preview keeps them on their rows. |
| Vertical move (⌥↑/⌥↓) | `MoveSelectedClipsToTrack` | Row shift computed from the non-linked entries only; `_selLinked` entries are left out of the move (their time shift is 0). |
| Arm | `TrackArm.Set` / `FollowSelection` | Arm/disarm the track's whole set; exclusive arm disarms tracks outside the set; ⌥-click arms the one track (bypass). Arm-on-select treats a set as one track. |

Session-View arming goes through `TrackArming.Set` too, so it is linked (spec edge case);
Session clips are not.

### 5. UI

- **Link key** on the group's Arrangement header — slim bar (next to M / S) and full row —
  using the existing `ChainLink` glyph; accent when on; tooltip "Link the group's tracks for
  editing and recording · ⌥-click a clip to edit one track". Click toggles via
  `SetTrackEditLink` (one undo step) and refreshes.
- **Mixer strip** of a group (`MixerView` full + compact): the same key in the group's
  M / S row. Session-View mixer group columns get it as well (same strip builder).
- **Member mark**: a linked member's header shows a small `ChainLink` glyph before its name
  in the group's hue (no accent: it states membership, not a live state).
- Context menu of the group header: **Link tracks** checkable item.

### 6. MCP

`set_group_link(trackId, on)` / `get_group_link(trackId)` in `TrackTools` (see
[contracts/mcp.md](contracts/mcp.md)); MCP edit tools stay per-track (they name the clips
they act on), documented in the tool description.

## Project Structure

### Documentation (this feature)

```text
specs/076-group-link/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   ├── mcp.md
│   └── ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # + nota_track_set_edit_link / get
├── src/engine/Track.h                  # + bool editLink
├── src/engine/Engine.h                 # + setTrackEditLink / trackEditLink
├── src/engine/Engine_Tracks.cpp        # implementation (undoable republish)
└── src/abi/nota_engine_track.cpp       # ABI wrappers
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs          # + SetTrackEditLink / GetTrackEditLink
├── Nota.Application/Editing/GroupLink.cs            # NEW: link sets + matching
├── Nota.Infrastructure/Interop/NativeMethods.Tracks.cs
├── Nota.Infrastructure/Interop/NotaEngine.Tracks.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs  # + TrackDto.EditLink
├── Nota.Infrastructure/Persistence/ProjectService.cs
├── Nota.Presentation/TrackArm.cs                    # linked arm
├── Nota.App/ArrangementView.cs                      # selection, split, delete, range
├── Nota.App/ArrangementView.Groups.cs               # Link key, member mark, menu
├── Nota.App/ArrangementView.LaneControl.cs          # trim, ADSR, ⌥-click, drag preview/commit
├── Nota.App/ArrangementView.KeyMoves.cs             # ⌥↑/⌥↓
├── Nota.App/ArrangementView.Vms.cs                  # TrackVM.EditLink
├── Nota.App/MixerView.cs, MixerView.Compact.cs      # group strip Link key
├── Nota.App/TrackArming.cs                          # ⌥ bypass helper, tooltip
├── Nota.App/PreferencesWindow (Shortcuts list)      # ⌥-click entry (nota-shortcuts)
└── Nota.Mcp/Tools/TrackTools.cs                     # set/get_group_link
tests/Nota.SmokeTest/
├── GroupLinkTests.cs                   # NEW: ABI + GroupLink rules + linked TrackArm
└── Program.cs                          # runs GroupLinkTests.Run()
CHANGELOG.md, FEATURES.md, ../nota-docs (EN + RU)
```

**Structure Decision**: Engine change is limited to metadata + ABI; all behaviour lives in
the managed app, centred on one pure rule class shared by the Arrangement and the arm logic.

## Complexity Tracking

No constitution violations.
