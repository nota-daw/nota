# Feature Specification: Mixer Groups and Compact Strips

**Feature Branch**: `070-mixer-groups-compact-strips`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "Mixer: fold groups and compact strips (GitHub issue nota-daw/nota#386). Today the Mixer (docked panel and pop-out window, specs/054) shows one 154 px strip per track with a classic vertical fader beside a separate stereo meter, and M/S/Arm at the bottom; group tracks can be collapsed in the Arrangement (persisted per project) but not in the Mixer, and which strips belong to a group is hard to see (only the group's own "→ parent" I/O text hints at it). Wanted: (1) Groups in the Mixer — a group strip can be collapsed/expanded to hide/show its member strips (nested groups too), and group membership is visible at a glance through subtle cues (shading/frame/label spanning the member strips, nesting shown). (2) Compact strips, inspired by Avril's mixer: a wide, easy-to-read level meter that doubles as the fader with a compact triangular handle, full-width prominent Rec/Solo/Mute buttons, and narrow strips so far more tracks fit on screen, without losing readability or existing features (pan, sends, I/O, dB entry, MIDI learn, height adaptation, Stereo map). Remote web mixer (src/web/remote) is out of scope unless trivially shared."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: the Mixer opens docked in the main window or as a
pop-out window (specs/054) and lists strips in the Arrangement's track order, groups
included (#389). Each strip is 154 px wide: header, I/O rows, A/B sends, pan knob, a
vertical fader beside a stereo meter, a typed dB value, and M / S / Arm buttons. Strips adapt
to the panel height — I/O hides first, then sends, then the fader shortens (specs/054
FR-014). Group tracks can be collapsed in the Arrangement; that state is view state saved
with the project. In the Mixer, a group strip looks like any other strip apart from its
"Group in → parent" I/O text and a BAL badge on its pan.

## Clarifications

### Session 2026-11-02

- Q: Is the Mixer's fold the same state as the Arrangement's group collapse, or independent per view? → A: The same state — folding a group in either view folds it in both (FR-006, FR-010).
- Q: Are compact strips an option beside the regular strips, or a replacement? → A: An option; the regular strips stay the default (FR-013).
- Q: When a track hidden inside a folded group is selected elsewhere, what does the Mixer do? → A: The Mixer follows the selection: it highlights the selected strip and scrolls to it; a hidden track highlights its folded group strip instead, and the group stays folded (FR-011).
- Q: How are sends shown on a narrow compact strip? → A: The same per-return list, each send a thin full-width horizontal bar labelled with the return's letter, dragged or typed into as today (FR-016).
- Q: What does pan look like on a compact strip? → A: A thin full-width horizontal bar filling from the centre left or right; drag, typed value, double-click resets to centre; groups keep their BAL tag beside it (FR-016).
- Q: Where is the regular / compact switch? → A: A "Compact" toggle in the Mixer footer, beside the Sends and I/O toggles, in both the docked panel and the pop-out window (FR-013).
- Q: What does a group band look like? → A: A thin bar in the group's colour with the group's name above the group's strips, plus a faint tint of the same colour behind them; each nesting level adds one more bar on top (FR-002, FR-003).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See which strips belong to which group (Priority: P1)

A producer has a 30-track project with drums, vocals and synths each grouped. In the Mixer
they want to find the drum strips without reading every name. The member strips of each
group sit together under a band in the group's colour that carries the group's name, and
the group's own strip closes the band, so the drum kit reads as one block. A group inside a
group shows as a band inside the outer band. The group's own strip comes first, followed
by its members, as the group row heads its tracks in the Arrangement.

**Why this priority**: The cheapest change with the biggest readability gain, and it is
what makes folding (US2) understandable — a folded group needs a visible boundary to fold.

**Independent Test**: Open a project with two top-level groups, one of them holding a
nested group, plus ungrouped tracks; show the Mixer and check that every member strip is
inside its group's band, nested bands are distinguishable, ungrouped strips carry no band,
and the band follows changes (moving a track into or out of a group, renaming or recolouring
the group, undo).

**Acceptance Scenarios**:

1. **Given** a project with a group "Drums" holding Kick, Snare and Hats, **When** the Mixer
   is shown, **Then** the Drums, Kick, Snare and Hats strips are adjacent in that order and a band
   labelled "Drums" in the group's colour spans exactly those strips.
2. **Given** a group "Bus" holding the group "Drums" and a track "Bass", **When** the Mixer is
   shown, **Then** the "Drums" band is visibly nested inside the "Bus" band and "Bass" is in
   "Bus" only.
3. **Given** the Mixer is shown, **When** the user moves a track into a group in the
   Arrangement (or undoes it), **Then** the Mixer's bands update without the user doing
   anything else.
4. **Given** an ungrouped track, **When** the Mixer is shown, **Then** its strip has no group
   band.
5. **Given** the Mixer is in Stereo map view, **When** the strip column is shown beside the
   map, **Then** the same group bands appear on that column.

---

### User Story 2 - Fold a group in the Mixer (Priority: P1)

The same producer has balanced the drums and now works on vocals. They fold the "Drums"
group in the Mixer: its member strips disappear and only the group strip stays, so the
vocals and the rest fit on screen. Later they unfold it to tweak the snare.

**Why this priority**: This is the issue's explicit missing capability — large projects
become a horizontal scroll otherwise.

**Independent Test**: In a project with a group of several tracks, fold the group from its
strip in the Mixer; check that the member strips (and any nested groups' members) are hidden,
the group strip keeps working (fader, pan, mute, solo, meter) and shows it is folded with how
many strips it hides, and unfolding restores them in place.

**Acceptance Scenarios**:

1. **Given** an unfolded group in the Mixer, **When** the user clicks the group strip's fold
   control, **Then** every strip inside that group, nested groups included, is hidden, and
   the group strip stays with a folded marker and the number of hidden strips.
2. **Given** a folded group, **When** the user clicks the fold control again, **Then** the
   hidden strips reappear in their previous order and a nested group that was folded before
   stays folded.
3. **Given** a folded group whose member is soloed or armed, **When** the group is folded,
   **Then** the group strip shows that a hidden member is soloed / armed, and the audio is
   unchanged — folding never mutes, solos or bypasses anything.
4. **Given** a folded group, **When** the user saves and reopens the project, **Then** the
   group is still folded in the Mixer.
5. **Given** a folded group, **When** the user selects a hidden member elsewhere (Arrangement
   track header, device panel), **Then** the Mixer highlights the folded group strip as
   holding the selected track and scrolls to it; the group stays folded.
6. **Given** a group collapsed in the Arrangement, **When** the Mixer is shown, **Then** the
   group is folded in the Mixer too; unfolding it in the Mixer expands it in the Arrangement,
   and the other way round.

---

### User Story 3 - Fit more strips with compact strips (Priority: P2)

A producer on a laptop screen wants to see all 24 tracks of a song at once. They switch the
Mixer to compact strips: each strip becomes narrow; the level meter is wide and fills the
strip, and the volume is set by dragging a small triangular handle that rides on the meter's
side; Mute, Solo and Arm are wide buttons across the full strip width. Pan, sends, I/O, the
dB value and MIDI learn still work as before.

**Why this priority**: It is the larger design change from the issue. It delivers value on
its own but needs design work and touches every strip control, so it follows the group
work.

**Independent Test**: Switch a 24-track project to compact strips in a docked Mixer at a
1440 px wide main window; check that at least twice as many strips are visible as in the
regular layout, that the meter and the triangular handle set and show the same volume as the
regular fader (including typed dB and MIDI learn), that M / S / Arm are full-width and
toggle as before, and that switching back restores the regular layout.

**Acceptance Scenarios**:

1. **Given** the Mixer in the regular layout, **When** the user switches to compact strips,
   **Then** every strip narrows to the compact width, group bands and folding keep working,
   and the choice is remembered per user.
2. **Given** compact strips, **When** the user drags the triangular handle, **Then** the track
   volume changes exactly as with the regular fader (same range, undo, automation write,
   fine-adjust modifier, double-click to reset), and the meter keeps showing the level
   behind the handle.
3. **Given** compact strips, **When** the user types an exact dB value, **Then** the handle
   moves to it, as in the regular layout.
4. **Given** compact strips, **When** the user MIDI-learns a strip's volume, mute or solo,
   **Then** the learn works and the mapping is the same one the regular layout uses.
5. **Given** compact strips, **When** the user clicks Mute, Solo or Arm, **Then** the button
   spans the strip width, its on state is unmistakable at a glance, and the track toggles.
6. **Given** compact strips in a short docked panel, **When** the panel height shrinks,
   **Then** the strips adapt as the regular ones do (lower-priority sections hide first,
   meter and M / S / Arm always stay).
7. **Given** compact strips, **When** a track name is longer than the strip, **Then** the name
   is shortened readably and the full name is available on hover.
8. **Given** a fresh install, **When** the user first opens the Mixer, **Then** it shows the
   regular strips; compact strips appear only after the user switches to them.

---

### Edge Cases

- A group with no members: the band covers only the group strip; folding it hides nothing
  and the fold control still shows the empty state honestly (0 strips).
- Deeply nested groups (4+ levels): each level adds one bar row above the strips; the bar rows
  always stay and count toward the Mixer's minimum height, so membership stays readable.
- A group's member list changes while it is folded (track added to it, track moved out,
  track deleted): hidden count and visible strips update; a track moved out appears.
- Return tracks and Master are never inside a group band and cannot be folded.
- Group and member colours clash or a group has no custom colour: the band uses the group's
  track colour as everywhere else, at low emphasis so the strips' own colours stay readable.
- Meters of hidden member strips: hidden strips cost nothing to draw; the group's meter shows
  the summed group signal as today.
- The pop-out Mixer window and the docked Mixer show the same fold state and layout.
- Compact strips with many returns: the send bars stack, so the sends section grows taller;
  it hides for lack of height before the meter shortens (FR-017) and can be pinned on from
  the footer as today, so no send becomes unreachable.
- Very quiet and clipping signals on the wide meter: the peak hold and clip indication stay
  visible; the triangular handle never hides the clip indicator.
- Stereo map view with compact strips: the strip column beside the map uses the compact
  strips and fits more of them.
- Keyboard / accessibility: the fold control and the compact handle are reachable by
  keyboard and have accessible names, as other mixer controls do.

## Requirements *(mandatory)*

### Functional Requirements

**Group bands (US1)**

- **FR-001**: The Mixer MUST list strips in the Arrangement's hierarchical order: each
  group's own strip immediately followed by its member strips (nested groups the same way),
  exactly as the Arrangement lists the rows.
- **FR-002**: The Mixer MUST draw a band for each group: a thin bar in the group's colour
  carrying the group's name above the group's member strips and the group strip, and a
  faint tint of the same colour behind those strips that leaves the strips' own colours
  readable.
- **FR-003**: Nested groups MUST show as stacked bars — one bar row per nesting level, the
  outermost on top — so each strip's full group path can be read from the bars above it.
- **FR-004**: Bands MUST follow every change to group membership, group name, group colour
  and track order, including undo / redo, without reopening the Mixer.
- **FR-005**: Return, Master and ungrouped strips MUST carry no band.

**Folding (US2)**

- **FR-006**: Every group strip MUST have a fold control; folding hides all strips inside the
  group (nested groups included) and leaves the group strip in place. The fold state is the
  Arrangement's group collapse: one state per group, shared by both views.
- **FR-007**: A folded group strip MUST show that it is folded, how many strips it hides, and
  whether a hidden member is soloed, armed or clipping.
- **FR-008**: Folding MUST be view-only: it never changes audio, mute, solo, arm, routing or
  automation.
- **FR-009**: Unfolding MUST restore hidden strips in their order, keeping the fold state of
  nested groups.
- **FR-010**: Fold state MUST be saved with the project and restored on open. (Today the
  Arrangement's collapse is kept for the session only; this feature adds saving it.)
- **FR-011**: The Mixer MUST follow the selected track: the selected track's strip is
  highlighted and scrolled into view. When that track is hidden by folding, the outermost
  folded group strip that holds it is highlighted and scrolled into view instead; selection
  never unfolds a group.
- **FR-012**: The docked and pop-out Mixer MUST share the same fold state.

**Compact strips (US3)**

- **FR-013**: Users MUST be able to switch the Mixer between the regular and the compact
  strip layout with a "Compact" toggle in the Mixer footer, beside the Sends and I/O
  toggles, in both the docked panel and the pop-out window; the regular layout is the
  default and the choice is remembered per user.
- **FR-014**: In compact strips, the level meter MUST be the strip's main element and the
  volume MUST be set by a compact triangular handle on the meter, with the same range,
  resolution, undo, automation, fine-adjust, reset and MIDI-learn behaviour as the regular
  fader.
- **FR-015**: In compact strips, Mute, Solo and Arm (where the track has one) MUST be
  full-width buttons with an on state readable at a glance.
- **FR-016**: Compact strips MUST keep pan, sends, I/O and typed dB entry reachable; sections
  MAY be condensed but none may be removed. Each send is a thin full-width horizontal bar
  labelled with its return's letter, one per return as in the regular strip, set by drag or
  typed value. Pan is a thin full-width horizontal bar filling from the centre, set by
  drag or typed value, double-click resets to centre; a group's BAL tag stays beside it.
- **FR-017**: Compact strips MUST adapt to the panel height by the same priority as the regular
  strips (I/O, then sends, then the meter shortens); meter and M / S / Arm always stay.
- **FR-018**: A compact strip MUST be at most half the width of a regular strip.
- **FR-019**: Group bands and folding MUST work identically in both layouts and in the
  Stereo map's strip column.

**General**

- **FR-020**: The new controls MUST follow the Studio design language (`DESIGN.md`) and work
  in all colour themes.
- **FR-021**: All new user-facing text MUST be localisable like the rest of the Mixer.
- **FR-022**: Session view's own Mixer sections and the Remote web mixer are out of scope.

### Key Entities

- **Group band**: the visual span of one group over its member strips and its own strip;
  has a name, a colour, a nesting depth.
- **Fold state**: per group, folded or not; the Arrangement's group collapse, shared by the
  Mixer; saved with the project's view state.
- **Strip layout**: regular (default) or compact; a per-user preference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a 30-track project with 4 groups, a user can name the group of any strip
  in under 2 seconds without hovering or reading I/O text.
- **SC-002**: Folding or unfolding a group takes one click and the Mixer updates in under
  100 ms for a 100-track project.
- **SC-003**: With compact strips, a docked Mixer in a 1440 px wide window shows at least twice
  as many strips without scrolling as with regular strips.
- **SC-004**: Every action available on a regular strip (volume, typed dB, pan, sends, I/O,
  M / S / Arm, MIDI learn) is available on a compact strip — 0 missing actions.
- **SC-005**: Folding never changes the rendered audio: a project rendered with any groups
  folded is bit-identical to the same project rendered unfolded.

## Assumptions

- Tracks follow the Arrangement's hierarchical order in the Mixer: the group strip first,
  then its members. Today the Mixer lists tracks in flat engine order (#389 only moved the
  returns to the end), so a group's members are not always next to it — this feature fixes that.
- The fold control lives on the group strip's header; there is no separate group-list UI.
- The triangular handle sits on one side of the meter (not overlapping the level bars), in
  the spirit of the Avril reference in the issue; the exact visual is settled with a
  `nota-design` mockup during planning.
- The regular strip's feature set is the reference for compact strips; nothing new is added
  to a strip beyond the layout, the fold control and the folded-group indicators.
- No engine or C ABI change is expected: grouping, solo / arm state and meters already exist.
- The Remote web mixer may reuse the band and fold idea later; not in this feature.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
