# Feature Specification: Clip Editor Track Mode

**Feature Branch**: `cursor/clip-editor-track-mode-6414`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: User description: "Clip editor: track mode showing every clip on the track (GitHub issue nota-daw/nota#49). See and edit the whole track's MIDI in the clip editor, not one clip at a time. A track mode for the clip editor next to the current clip mode: the piano roll spans the arrangement timeline and shows every MIDI clip on the selected track, with clip borders marked, so you can scroll through the part and edit any note without re-selecting clips. Today the clip editor only has a clip mode: it opens the one selected clip. Related: #48 (multi-clip editing)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The engine is untouched. Track mode is the open clip set of spec 029
([`../029-multi-clip-editing/spec.md`](../029-multi-clip-editing/spec.md)) filled with every
MIDI clip of one track: editing, moving notes across clip seams, reference locks, the clip strip
and one-step undo all come from there.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Current state (for reference)**:

- The Clip editor opens the selected clip (clip mode), or — spec 029 — several selected MIDI
  clips together on a shared timeline, with a clip strip under the ruler.
- There is no way to see every clip of a track without selecting them all by hand.

## Clarifications

The issue leaves these open; each was answered with the default below (no blocking product
question remained). Reviewers can overturn any of them without reworking the design.

- Q: Where is the switch? → A: A **Clip | Track** segmented control at the top of the
  inspector's Clip section (the header is full at the default 1200 px window), plus a palette command "Toggle clip editor track mode". No default
  shortcut.
- Q: Does the mode stick? → A: Yes, it is an app setting (saved like note audition): once on,
  every arrangement MIDI clip opens in track mode until switched back.
- Q: What does the timeline cover? → A: The arrangement's own axis from bar 1 to the end of the
  track's last MIDI clip; bar numbers match the arrangement. The editor opens scrolled to the
  clicked clip.
- Q: What happens when drawing in a gap between clips? → A: Nothing, as in spec 029 (FR-007).
  Making clips stays an arrangement edit.
- Q: Which track? → A: The track of the clip that was double-clicked / selected. Selecting a
  clip of the same track makes it active and scrolls to it; a clip on another track switches the
  editor to that track.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See every clip of a track in the Clip editor (Priority: P1)

A user has a bass line spread over six clips. They switch the Clip editor to Track: the piano
roll now shows all six clips' notes at their arrangement positions, each clip's span marked in
the clip strip, and they scroll through the whole part.

**Why this priority**: The core of the request.

**Independent Test**: Make two MIDI clips on one track at bars 3 and 9, open one, press Track;
the roll's ruler starts at bar 1, both clips' notes show at their bars, the strip marks both.

**Acceptance Scenarios**:

1. **Given** a MIDI clip open in clip mode, **When** the user picks Track, **Then** the editor
   shows every MIDI clip of that track on the arrangement timeline, the clip that was open is
   active, and the strip marks each clip's borders.
2. **Given** track mode, **When** the user double-clicks any MIDI clip, **Then** its whole track
   opens with that clip active and in view.
3. **Given** a track with a single MIDI clip, **When** it opens in track mode, **Then** the clip
   sits at its arrangement position and its borders are marked.
4. **Given** track mode, **When** the user picks Clip, **Then** the editor shows the active clip
   alone, as before this feature.
5. **Given** the app is restarted, **Then** the editor opens in the mode last chosen.

---

### User Story 2 - Edit any note without re-selecting clips (Priority: P1)

In track mode the user clicks and edits notes in any clip, rubber-bands across clips and
transposes, drags a note across a seam into the next clip — all without touching the
arrangement.

**Why this priority**: The issue's "edit any note without re-selecting clips".

**Independent Test**: In track mode, rubber-band notes in two clips and press ↑; both move and
one Undo reverts both.

**Acceptance Scenarios**:

1. **Given** track mode, **Then** every editing rule of spec 029 applies to the track's clips
   (selection, moves, seams, overwrite within a clip, paste, one undo step, reference locks).
2. **Given** track mode, **When** the user clicks a note of another clip, **Then** that clip
   becomes active (header, inspector, envelopes follow).

---

### User Story 3 - The editor follows the track (Priority: P2)

With track mode on and the Clip editor on screen, the user clicks clips in the arrangement: a
clip of the same track becomes active and scrolls into view; a clip on another track opens that
track. A clip drawn, pasted or recorded on the track joins the editor.

**Acceptance Scenarios**:

1. **Given** track mode on track A, **When** the user selects another clip of A, **Then** it
   becomes active and in view, the editor is not rebuilt (reference locks and zoom stay).
2. **Given** track mode on track A, **When** the user selects a MIDI clip on track B, **Then**
   the editor shows track B's clips.
3. **Given** track mode, **When** a new MIDI clip appears on the track (drawn, pasted,
   duplicated, recorded), **Then** it joins the editor once the arrangement refreshes or the
   clip is selected.
4. **Given** track mode, **When** the user runs "Edit selected clips together", **Then** the
   editor leaves track mode and shows the selection (spec 029).

### Edge Cases

- Audio clips on the track are not shown; track mode is MIDI only.
- Session slots have no track mode (the switch is not shown there).
- A track whose last MIDI clip is deleted closes the editor, as in spec 029.
- Clips far apart: the timeline covers bar 1 to the last clip's end; the editor opens at the
  active clip.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The arrangement Clip editor MUST offer a Clip | Track switch at the top of its inspector and a
  palette command toggling it. Session slot editors MUST NOT show it.
- **FR-002**: In track mode the editor MUST open every MIDI clip of the edited track (the track
  of the double-clicked / selected clip), with that clip active.
- **FR-003**: In track mode the horizontal axis MUST be the arrangement's: editor beat 0 is bar 1,
  and it runs to the end of the track's last MIDI clip. The clip strip MUST mark each clip, even
  when the track has only one.
- **FR-004**: All spec 029 editing behaviour (FR-003 to FR-013 there) MUST apply to the open
  track clips.
- **FR-005**: The mode MUST persist as an app setting and apply to every arrangement MIDI clip
  opened afterwards.
- **FR-006**: While the editor is on screen in track mode, selecting a MIDI clip of the same track
  MUST make it active and scroll it into view without rebuilding the editor; selecting a MIDI clip
  of another track MUST show that track.
- **FR-007**: MIDI clips that appear on the track while it is open MUST join the editor.
- **FR-008**: "Edit selected clips together" MUST switch to clip mode before opening the selection.
- **FR-009**: CHANGELOG and FEATURES.md MUST describe track mode.

### Key Entities

- **Track clip set**: the open clip set of spec 029 holding every MIDI clip of one track, on the
  arrangement axis; marked as track mode.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Editing notes in N clips of one track takes one open of the editor, versus N opens
  (or a hand-made selection) today.
- **SC-002**: The ruler's bar numbers in track mode equal the arrangement's in 100% of cases.
- **SC-003**: Users who never pick Track see no change in behaviour.

## Assumptions

- The open clip set, clip strip and editing rules of spec 029 are reused unchanged.
- Notes cannot be drawn in gaps between clips; creating or extending clips from the editor is
  out of scope.
- No new keyboard shortcut; the Shortcuts pane is unchanged (the palette command is bindable
  through the shortcut builder like any other).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md) — no new source files
- [x] No new dependency
- [x] Audio-thread impact considered: none — note edits use the existing per-clip commit path
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no new calls
- [x] Smoke-test impact assessed: `ClipSet` track-mode rules added to `tests/Nota.SmokeTest/MultiClipTests.cs`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (Studio design language): the existing segmented control, no new colours
