# Feature Specification: Fade audio clips when they are split

**Feature Branch**: `cursor/split-crossfade-2773`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "Issue #286. Splitting an audio clip clicks when the cut is not at zero amplitude. Automatically apply a short fade at the split. The fade is visible and the user can drag it to change the length. Start from about 4 ms. A preference turns the automatic fade off; the default is on. A command in the palette and the clip menu applies the same fade to an existing join. No shortcut."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A split does not click (Priority: P1)

When someone splits an audio clip, the cut gets a short fade so the join does not click. The fade is the clip's existing corner handles, starting at about 4 ms, and they can drag it longer or shorter.

**Why this priority**: This is the request. A click at every split is the problem #286 describes.

**Independent Test**: Split an audio clip at a point that is not a zero crossing and read the two pieces' fades. The left piece's release and the right piece's attack are about 4 ms, and dragging a handle changes that length.

**Acceptance Scenarios**:

1. **Given** an audio clip and the preference on, **When** the clip is split, **Then** the left piece fades out and the right piece fades in over about 4 ms, and those fades are the corner handles.
2. **Given** a fade the user has dragged longer than 4 ms, **When** they look at the clip, **Then** the longer fade is what plays.
3. **Given** a split that already kept an attack on the left piece and a release on the right, **When** the clip is split, **Then** that attack stays on the left and that release stays on the right, and the new cut still gets the short fade where there is room.
4. **Given** the left piece's attack and decay already fill it, **When** the clip is split, **Then** the left piece's release stays empty because there is no room for a fade.
5. **Given** a split, **When** the user undoes once, **Then** the clip is whole again, fade included.

---

### User Story 2 - The automatic fade can be turned off (Priority: P2)

A preference turns the automatic fade off. It is on by default, including when an older settings file has no such key. Turning it off does not change fades already written.

**Why this priority**: The maintainer asked for the fade on every split, with a way to turn it off.

**Independent Test**: Turn the preference off, split an audio clip, and confirm both new edges are hard cuts. Turn it on and split again, and the short fade is back.

**Acceptance Scenarios**:

1. **Given** a fresh install, **When** settings are read, **Then** the preference is on.
2. **Given** the preference is off, **When** an audio clip is split or a time range is split, **Then** the new edges are hard cuts.
3. **Given** the preference is on, **When** a time range splits an audio clip into several pieces, **Then** each new cut is faded and the clip's original start and end are left as they were.

---

### User Story 3 - Fade a join that is already there (Priority: P3)

The command palette and an audio clip's menu offer **Fade at splits**, with no keyboard shortcut. It writes the same short fade on both sides of a join where two audio clips touch and at least one of them is selected. It does not shorten a fade that is already longer. It works even when the preference is off. MIDI clips are left alone. One undo covers the whole edit.

**Why this priority**: The issue also asked for a way to fade existing split points. The maintainer did not want a shortcut.

**Independent Test**: With the preference off, split a clip (a hard cut), run **Fade at splits** on one of the pieces, and confirm both sides of the join gained the short fade. Run it again and confirm nothing changes and no extra undo step appears.

**Acceptance Scenarios**:

1. **Given** two audio clips that touch and one of them selected, **When** **Fade at splits** runs, **Then** both sides of that join have at least the short fade, in one undo step.
2. **Given** a fade already longer than 4 ms, **When** the command runs, **Then** that fade is not shortened.
3. **Given** a gap, a MIDI clip, or nothing selected, **When** the command runs, **Then** nothing changes and the status line says to select audio clips that touch.
4. **Given** the command, **When** the user looks for a shortcut, **Then** there is none. Shift+X is not used.

---

### Edge Cases

- A piece shorter than 8 ms caps each fade at half the piece, and at the room the other stages leave.
- Delete, cut, duplicate, and insert silence stay hard cuts. The fade is only for an explicit split, and for the **Fade at splits** command.
- MIDI splits do not gain a fade.
- Two clips that do not touch are not a join.
- The fade length follows the project tempo so that 4 ms stays 4 ms when the tempo changes. At 120 BPM it is 0.008 beats.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Splitting an audio clip, at the playhead or at a time-range edge, MUST write a fade of about 4 ms at each new cut while the preference is on. The left side of a cut fades out; the right side fades in.
- **FR-002**: The fade MUST be the clip's attack or release, so the existing corner handles show it and dragging a handle changes its length.
- **FR-003**: A fade the carve kept, or one the user has dragged longer than the default, MUST NOT be overwritten or shortened by a later automatic split or by **Fade at splits**.
- **FR-004**: The preference MUST live in app settings, default on, including when the key is missing. It MUST be a switch on Settings → Appearance labelled **Fade audio clips when splitting**.
- **FR-005**: **Fade at splits** MUST appear in the command palette and on an audio clip's context menu, with no shortcut, and MUST apply the short fade even when the preference is off.
- **FR-006**: The command MUST fade both sides of a join when either touching audio clip is selected, across every selected track, in one undo step. When nothing changes it MUST NOT push an undo step.
- **FR-007**: MIDI clips MUST be unchanged by the automatic fade and by the command.
- **FR-008**: Delete, cut, duplicate, insert silence, and consolidate MUST NOT gain this fade.
- **FR-009**: The automatic fade MUST be part of the same undo step as the split.
- **FR-010**: User-visible strings MUST be translated in French. UI names in the docs stay English.

### Key Entities

- **Split fade**: A short attack or release on the edge of an audio clip that was created by a cut. About 4 ms to start. The user owns it after that.
- **Split-fade preference**: An app setting, on by default, that decides whether a split writes that fade.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At 120 BPM a fresh split of a long enough audio clip sets the new edges to 0.008 beats, and one undo restores the unsplit clip.
- **SC-002**: With the preference off, the same split leaves both new edges at zero. **Fade at splits** then raises them to 0.008 beats and does not shorten a 0.5 beat fade.
- **SC-003**: A warped split with the preference on matches the unsplit audio away from the cut and is quieter at the cut. With the preference off, a warped split stays sample-exact.
- **SC-004**: The command has an empty shortcut, is bound, and its strings exist in French.

## Assumptions

- Clips on one track cannot overlap, so the fade is a fade-out plus a fade-in, not an overlapping crossfade. A longer drag dips the level at the cut; the short default only stops the click.
- The existing few-millisecond safety fade on unwarped clips stays. This feature adds the visible, editable fade on top.
- "About 4 ms" means 0.004 seconds, converted to beats with the authoring tempo.
