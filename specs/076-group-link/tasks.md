---

description: "Tasks for Linked editing on group tracks (specs/076, #184)"
---

# Tasks: Linked editing on group tracks

**Input**: Design documents from `specs/076-group-link/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The constitution (IV) requires the smoke test to cover the new C ABI; the pure
rule class and linked arming are covered there too (`tests/Nota.SmokeTest/GroupLinkTests.cs`,
run from `Program.cs` like `ArrangementMovesTests`). UI behaviour is checked with the
quickstart scenarios.

**Organization**: by user story. US2 (selection) is implemented before US1 (edits) because
the linked edits ride on the linked selection (plan § 3); both are P1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 edits · US2 selection · US3 arming · US4 toggle / visibility / persistence

---

## Phase 1: Setup

- [X] T001 Confirm the branch `feat/folder-track-groups` is based on `develop` and the engine build dir `build/` is Release (memory: engine build dirs); no code change

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the Link flag end to end and the shared rule class.

- [X] T002 Add `bool editLink = false;` beside `pianoFold` in `src/native/nota.engine/src/engine/Track.h` (comment: specs/076, authoring only, never read on the audio thread) and make sure `cloneTrack` copies it (check the clone helper in `src/native/nota.engine/src/engine/Engine_Tracks.cpp`)
- [X] T003 Declare `bool setTrackEditLink(int32_t trackId, bool on); int32_t trackEditLink(int32_t trackId) const;` in `src/native/nota.engine/src/engine/Engine.h` and implement them in `src/native/nota.engine/src/engine/Engine_Tracks.cpp` after `trackPianoFold`: refuse non-group tracks (return false), return true without republishing when unchanged, else clone + `republishWithTrack` (an undo step); getter returns 1 / 0 / -1 for unknown
- [X] T004 Add `nota_track_set_edit_link` / `nota_track_get_edit_link` with the doc comment from [contracts/c-abi.md](contracts/c-abi.md) after `nota_track_get_piano_fold` in `src/native/nota.engine/include/nota/nota_engine.h`, and their wrappers in `src/native/nota.engine/src/abi/nota_engine_track.cpp` (null engine → `NOTA_ERR_INVALID_ARG` / -1)
- [X] T005 [P] Add the P/Invoke pair `TrackSetEditLink` / `TrackGetEditLink` in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`
- [X] T006 Add `bool SetTrackEditLink(int trackId, bool on); bool GetTrackEditLink(int trackId);` with the doc comment from the contract to `src/managed/Nota.Application/Engine/IAudioEngine.cs` and implement them in `src/managed/Nota.Infrastructure/Interop/NotaEngine.Tracks.cs` (set returns `result == NOTA_OK`, get returns `== 1`)
- [X] T007 Persist: add `public bool? EditLink { get; set; }   // group Link (specs/076); written only when on` to `TrackDto` in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`; in `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` write `EditLink = engine.GetTrackEditLink(ti.Id) ? true : null` and on load call `engine.SetTrackEditLink(id, true)` for `EditLink == true` after group parents are restored, without leaving an undo step (load clears history — verify)
- [X] T008 Create `src/managed/Nota.Application/Editing/GroupLink.cs` (SPDX header): `readonly record struct LinkTrack(int Id, int GroupId, bool IsGroup, bool IsReturn, bool EditLink)`; `GroupLink.Build(IEnumerable<LinkTrack>)`; `int RootOf(int trackId)` (outermost linked ancestor group, -1 none); `IReadOnlyList<int> Members(int trackId)` (the other non-group, non-return tracks with the same root; empty when unlinked); `IReadOnlyList<int> ExpandTracks(IEnumerable<int>)` (closure under membership, input order kept, then members); `static bool Matches(double startA, double lenA, double startB, double lenB)` with `public const double MatchTolerance = 1.0 / 256` applied to start and end ("within 1/256 of a beat", FR-006); `bool IsLinked(int trackId)`; `bool AnyLinked`
- [X] T009 Create `tests/Nota.SmokeTest/GroupLinkTests.cs` (SPDX header) with `Run()` and register it in `tests/Nota.SmokeTest/Program.cs` next to `ArrangementMovesTests` (both places it is listed). First checks — the ABI table of [contracts/c-abi.md](contracts/c-abi.md) against the real engine: set/get on a group, refused on an audio track, idempotent set adds no undo step, undo/redo, duplicate group keeps it, unknown id → false; save → load round trip keeps it and an old project (no field) loads unlinked
- [X] T010 [P] Add `GroupLink` rule checks to `tests/Nota.SmokeTest/GroupLinkTests.cs`: unlinked group → no members; linked group with A, B → members of A = [B]; nested: outer linked + inner unlinked → inner's tracks in the outer set; inner linked alone → own set; group and return tracks never members; `Matches` true at exact and 1/512-off edges, false at 1/128 off or different length; `ExpandTracks([A, X])` = [A, X, B]

**Checkpoint**: `scripts/build.sh` green with the flag and rules in place; no UI change yet.

---

## Phase 3: User Story 2 — Select once, see the whole group selected (P1)

**Goal**: selecting a clip or a range on a linked track selects the matching material.

**Independent Test**: quickstart rows 2, 7 and 11.

- [X] T011 [US2] Add `public bool EditLink;` to the track VM in `src/managed/Nota.App/ArrangementView.Vms.cs` and fill it in `Refresh` from `_engine.GetTrackEditLink` for group tracks (`src/managed/Nota.App/ArrangementView.cs`); add a cached `GroupLink _link` rebuilt at the end of `Refresh` from `_tracks` (`ArrangementView.Groups.cs`)
- [X] T012 [US2] In `src/managed/Nota.App/ArrangementView.cs`: add `private readonly HashSet<(int track, int clip)> _selLinked = new();` and a helper `IEnumerable<(int,int)> MatchesOf(int trackId, int clipIndex)` that looks up the clip VM and returns the matching clips on `_link.Members(trackId)` (`GroupLink.Matches` on `StartBeat` / `LengthBeats`); every place that clears `_sel` clears `_selLinked`
- [X] T013 [US2] Make `Select(trackId, clipIndex, keepTrackSet, linked = true)` add `MatchesOf` to `_sel` and `_selLinked` when `linked` (`src/managed/Nota.App/ArrangementView.cs`); `ToggleSelect` adds the clip with its matches, and removing a clip removes its matches that are in `_selLinked`
- [X] T014 [US2] ⌥-click bypass (FR-012): in `src/managed/Nota.App/ArrangementView.LaneControl.cs` remember the press modifiers for the pending click and, on release of a pending click with Alt held, call `_o.Select(track, clip, linked: false)`; ⌥-drag behaviour unchanged
- [X] T015 [US2] `SetSelection` / `ReselectPlaced`: after replacing `_sel`, recompute `_selLinked` — for `ReselectPlaced` mark as linked the placed clips that came from `_selLinked` entries (map by request order of the move/duplicate), for marquee / ⌘A leave `_selLinked` empty (`src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.KeyMoves.cs`)
- [X] T016 [US2] Time ranges (FR-008): `TimeSelectionTrackIds()` returns `_link.ExpandTracks(...)` of the row span (non-return rows); `SetTimeSelection` widens `_timeSelRowLo/Hi` to cover the visible rows of every touched set so the range draws over all members (`src/managed/Nota.App/ArrangementView.cs`)

**Checkpoint**: selection and ranges link; edits on the selection (delete, duplicate, copy, cut, nudge, block drag) already follow.

---

## Phase 4: User Story 1 — Edit two takes as one (P1) 🎯 MVP (with US2)

**Goal**: split, move, trim, fades, delete, duplicate, nudge, cut / copy act on matches as one undo step.

**Independent Test**: quickstart rows 3, 4, 5, 6 and 12.

- [X] T017 [US1] Split at playhead (FR-010): in `SplitSelectedAtPlayhead` (`src/managed/Nota.App/ArrangementView.cs`) add, for each linked track of the selection, every clip of its members spanning the playhead; run the split loop inside `_engine.BeginUndoGroup()` / `EndUndoGroup()` (try/finally)
- [X] T018 [US1] Clip menu **Split here** (`src/managed/Nota.App/ArrangementView.LaneControl.cs`, the `MenuKit.Item(Loc.T("Split here")…)`): when the track is linked, split the clip and every member clip spanning `at` in one undo group (factor a `SplitLinkedAt(trackId, clipIndex, at)` helper in `ArrangementView.cs` shared with T017)
- [X] T019 [US1] `DeleteSelectedClips` (`src/managed/Nota.App/ArrangementView.cs`): wrap the delete loop in one undo group so a linked delete is one step
- [X] T020 [US1] Trim / resize: in `OnPointerReleased` (`src/managed/Nota.App/ArrangementView.LaneControl.cs`) collect the grabbed clip's matches that are in `_selLinked` **before** committing (matched on the pre-drag `_origStart` / `_origLen`), then apply the same `TrimClip` / `ResizeAudioClip` (by each clip's kind) to all of them inside one undo group; raise `RaiseClipGeometryChanged` for each
- [X] T021 [US1] Clip ADSR / fades: `CommitAdsr` (`src/managed/Nota.App/ArrangementView.LaneControl.cs`) sets the same `ClipAdsr` on the grabbed clip's audio matches in `_selLinked`, in one undo group
- [X] T022 [US1] Vertical drag move (research R5): in the group-move commit, entries whose `(track, clip)` is in `_selLinked` keep `destTrack = m.track` while taking the time shift; in the drag preview (`UpdateActiveDrag` / the `_groupMove` row drawing in `ArrangementView.LaneControl.cs`) those entries stay on their own rows; the `rowDelta` validity check ignores them
- [X] T023 [US1] ⌥↑ / ⌥↓ (`MoveSelectedClipsToTrack` in `src/managed/Nota.App/ArrangementView.KeyMoves.cs`): compute the row shift from entries not in `_selLinked` and leave the linked entries out of the move
- [X] T024 [US1] Verify (read the code paths, no change expected) that nudge, duplicate, copy, cut, paste and block drag act on `_sel` including the matches and are single undo steps; note any exception in this file

**Checkpoint**: Stories 1 + 2 = the MVP; quickstart rows 2–7, 11, 12 pass.

---

## Phase 5: User Story 3 — Arm the group with one click (P2)

**Goal**: arming one member arms the set; ⌥-click arms one track.

**Independent Test**: quickstart rows 8 and 9.

- [X] T025 [US3] In `src/managed/Nota.Presentation/TrackArm.cs`: build a `GroupLink` from `TryGetTrackInfo` + `GetTrackEditLink`; `Set(engine, trackId, on, additive, linked = true)` applies `on` to the track and (when `linked`) its members; the exclusive pass skips the set; return true when any track other than `trackId` changed. `FollowSelection` treats the set as one track (an armed set counts as one, arming arms the set)
- [X] T026 [US3] In `src/managed/Nota.App/TrackArming.cs`: `Set(engine, trackId, on, additive, linked = true)` forwards; add `IsBypass(KeyModifiers)` (Alt held); update `Tip` to mention "⌥-click to arm one track of a linked group" via `MenuKit.Keys`
- [X] T027 [US3] Pass `linked: !TrackArming.IsBypass(mods)` at every arm button: `src/managed/Nota.App/ArrangementView.cs` (arm chip), `MixerView.cs`, `MixerView.Compact.cs`, `SessionView.Mixer.cs`, `TrackInspectorView.cs` (each already has the click modifiers)
- [X] T028 [US3] Add linked-arm checks to `tests/Nota.SmokeTest/GroupLinkTests.cs` with the real engine: linked A, B + C outside: plain arm A → A, B on, C off; additive → C stays; disarm B → A off; `linked: false` → only that track; Link off → today's behaviour

**Checkpoint**: arming works from every view.

---

## Phase 6: User Story 4 — Turn the link on, see it, keep it (P2)

**Goal**: the Link key, member marks, menu item, MCP.

**Independent Test**: quickstart rows 1 and 10; ⌘Z after toggling restores it.

- [X] T029 [US4] Link key on the group header (slim bar and full row) in `src/managed/Nota.App/ArrangementView.cs` / `ArrangementView.Groups.cs`, built like the M / S keys with `GlyphKind.ChainLink`, accent when on, tooltip from [contracts/ui.md](contracts/ui.md); click → `SetTrackEditLink(id, !on)`, status "The group's tracks are linked" when turned on, `Refresh()`; raise the event the Mixer listens to for track-state changes
- [X] T030 [US4] Member mark: a 10 px `ChainLink` glyph before the name in a linked member's header, secondary text colour, tooltip "Linked with {0}" (`src/managed/Nota.App/ArrangementView.cs` header builder)
- [X] T031 [US4] Group header context menu: checkable **Link tracks** item (`src/managed/Nota.App/ArrangementView.Groups.cs` or wherever the group header menu is built)
- [X] T032 [P] [US4] Mixer: the Link key on group strips in `src/managed/Nota.App/MixerView.cs` and `MixerView.Compact.cs`, and the Session mixer group column in `src/managed/Nota.App/SessionView.Mixer.cs`; repaint on undo / external change like M / S
- [X] T033 [P] [US4] MCP `set_group_link` / `get_group_link` in `src/managed/Nota.Mcp/Tools/TrackTools.cs` with the descriptions from [contracts/mcp.md](contracts/mcp.md)
- [X] T034 [P] [US4] Localise the new strings (French entries; English is the key) in `src/managed/Nota.App/Languages/fr.json` per contracts/ui.md, and satisfy the DesignTokenCheck allow-lists if they flag the new labels

**Checkpoint**: all stories done.

---

## Phase 7: Polish & Cross-Cutting

- [X] T035 Run the quickstart scenarios in the app (headless harness per memory recipe) and capture a before/after shot of a linked group header for the PR
- [X] T036 [P] Preferences → Shortcuts: add the two ⌥-click lines via the `nota-shortcuts` skill
- [X] T037 [P] FEATURES.md via `nota-features` (Tracks and mixer → Group; Arrangement editing), user docs EN + RU via `nota-docs`

### Nota closing tasks

- [X] T038 Run `scripts/build.sh` until native + managed + smoke test are green — green except the render-pool `overloaded` burst check, which flakes on `develop` too (alternating base/branch rounds 2026-11-04: base failed 1 of 4, branch 1 of 4 under load; unrelated to this change, which adds no audio-thread code)
- [X] T039 AGPL-3.0-only SPDX two-line header on `GroupLink.cs` and `GroupLinkTests.cs`
- [X] T040 New dependency: N/A (none added)
- [X] T041 `CHANGELOG.md` `[Unreleased]` → Added: group Link (#184)
- [X] T042 Engine behaviour change → smoke test covers the ABI (T009) — confirm green

---

## Dependencies & Execution Order

- Phase 2 blocks everything (T002 → T003 → T004 → T005/T006 → T007; T008 independent of the engine tasks; T009 after T006/T007; T010 after T008).
- US2 (Phase 3) before US1 (Phase 4): US1's trim / ADSR / vertical-move code reads `_selLinked`.
- US3 (Phase 5) needs only Phase 2 — can run in parallel with Phases 3–4 (different files: `TrackArm.cs`, `TrackArming.cs`, arm call sites).
- US4 (Phase 6) needs Phase 2; T029–T031 touch `ArrangementView*.cs`, so run them after Phase 4 to avoid conflicts; T032–T034 are parallel.
- Polish after all stories.

### Parallel examples

```text
After Phase 2:  Phase 3 (ArrangementView)  ‖  Phase 5 (TrackArm / TrackArming)  ‖  T033 (MCP)
In Phase 6:     T032 (Mixer)  ‖  T033 (MCP)  ‖  T034 (strings)
```

## Implementation Strategy

1. Phase 2 → build green (flag + rules + smoke).
2. Phases 3 + 4 → MVP: linked selection and edits (Link can be toggled through MCP until T029).
3. Phase 5 → arming. 4. Phase 6 → UI entry points. 5. Polish, docs, build.
