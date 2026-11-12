# Feature Specification: Arrangement group trays

**Feature Branch**: `feat/arrangement-group-trays`

**Created**: 2026-11-12

**Status**: Draft

**Issue**: [nota-daw/nota#584](https://github.com/nota-daw/nota/issues/584) (milestone 0.60.1)

**Input**: User description: "Issue #584 (approved, milestone 0.60.1): Arrangement groups — trays in the new look, the 0.59.2 staircase in the classic look, meters that fit. New look: a group is a tray (group-colour wash behind its header row and members, 3 px left edge in the group colour, rounded right corners, bottom padding closing the tray); members are normal cards with only their own 3 px colour bar (no ancestor bars, no 6% member tint); a nested group is a tray inside the tray inset ~4 px per level; the group header row has chevron, name, M/S/(Cue)/Link and a vertical meter in the same right-hand column as tracks; a folded group shows its member count after the name; nesting moves only the left side, the name trims before controls are pushed or clipped; header rows stay aligned with their lanes (tray padding comes out of existing row heights); the group lane tint follows the tray if needed. Classic look: as in v0.59.2 — each row only its own 3 px spine stepped in 10 px per depth, no ancestor bars or tints, group row on SurfaceRaised and tracks on SurfaceCard, selected spine in accent; the 0.59.2 slim group row (chevron, 11 px name, M/S/Link, Group tag, horizontal meter docked below the title row); switching look rebuilds headers live. Both looks: track and group meters sit fully inside the header card on every row height and theme. Out of scope: Mixer group bands, sticky group headers, child-clip summary in group lanes."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
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

**Related specs**: [038 style guide](../038-style-guide/spec.md) (the new look and its
§ Groups rules this replaces for the Arrangement headers),
[091 classic look](../091-classic-look/spec.md) (the New / Classic switch this follows),
[015 track list sizing](../015-track-list-sizing/) (Compact / Standard / Large rows),
[070 mixer groups](../070-mixer-groups-compact-strips/) (out of scope here),
[076 group Link](../076-group-link/) (the Link chip on the group row).

**Background.** In the new look (#296) every group a track sits in draws its own 4 px bar
14 px further in, and the track then adds its own 3 px bar: a track two levels deep shows
three stripes, often two of the same colour, and loses about 50 px of the header column
before its name starts. The group colour is also washed over the group row (16 %) and
every member (6 %), so nested groups stack tints and turn muddy. Nothing marks where a
group ends — the first top-level track after a group reads as part of it. On the slim group
row the level meter is a thin horizontal rail along the bottom of the whole row, running
under the name and the chips; on a track row the vertical meter pokes slightly past the
card's edge. Version 0.59.2 drew nesting as a simple "staircase" of each row's own colour
spine; the classic look (#572) brings 0.59.2's appearance back and should bring this back
too.

## Clarifications

### Session 2026-11-12

- Q: In the new look, should the Arrangement lanes (with the clips) carry the group colour
  too, so a group reads as one block across the grid? → A: Only the group's own lane keeps
  its wash, as today; member lanes stay unwashed.
- Q: What does a folded group's count show when it holds nested groups? → A: Every track it
  hides, including tracks inside nested groups; the nested groups themselves are not
  counted (the Mixer's group bar counts nested groups too; the Arrangement does not).
- Q: Does the classic look show the folded count too, though 0.59.2 had none? → A: Yes, in
  both looks — it is information, not styling.
- Q: How strong is the tray's wash in the new look? → A: The group's header row keeps
  today's 16 %; the rest of the tray (around the member cards) is lighter, about 8 %.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read nested groups at a glance in the new look (Priority: P1)

A producer with drums, instruments and vocals grouped — and a sub-group inside a group —
scans the Arrangement's track headers and immediately sees which tracks belong to which
group, where each group starts and ends, and which track is which, without a stack of
same-coloured stripes or muddy tints.

**Why this priority**: This is the complaint that opened the issue; the new look is the
default, so every user with groups sees it.

**Independent Test**: Open a project with a group holding a nested group of two tracks
and a second (folded) nested group, followed by a top-level track; in the new look,
check the headers against the acceptance scenarios below.

**Acceptance Scenarios**:

1. **Given** the new look and a group with members, **When** the headers are shown,
   **Then** the group reads as one tray: a wash of the group's colour behind the group's
   row and all its members, a 3 px edge in the group's colour down the tray's left side,
   rounded right corners, and a small bottom padding that closes the tray under its last
   member.
2. **Given** a track inside one or more groups, **When** its header is shown, **Then** it
   carries only its own 3 px colour bar — no bar per enclosing group — and its own card
   background is not tinted by any group.
3. **Given** a group nested in another group, **When** the headers are shown, **Then**
   the inner group is a tray inside the outer tray, inset about 4 px per nesting level,
   and the outer tray visibly continues around it.
4. **Given** a top-level track right after a group, **When** the headers are shown,
   **Then** it is clearly outside the group: the group's tray has closed above it.
5. **Given** a folded group, **When** the headers are shown, **Then** its tray shows only
   its header row, with the number of tracks it holds after its name (e.g.
   "Instruments · 4"); the same count shows on a folded group in the classic look.

---

### User Story 2 - Group controls and meters that don't collide (Priority: P1)

A user mixing from the Arrangement watches the level of each group and track in its
header, and clicks M / S / Link on a group row without a meter drawn under the buttons or
a meter spilling out of the card.

**Why this priority**: The overlap and overflow are visible defects on every project with
a group, in both looks.

**Independent Test**: With audio playing through a group and its members, view the
headers at Compact, Standard and Large row heights in each of the four colour themes and
both looks; check meters and controls.

**Acceptance Scenarios**:

1. **Given** the new look and a group row in its slim (unselected) form, **When** audio
   plays through the group, **Then** its level shows on a vertical meter in the same
   right-hand column as the tracks' meters, and nothing is drawn under the name or the
   chips.
2. **Given** any track or group header at any row height, theme and look, **When** its
   meter is drawn, **Then** the meter lies fully inside the header card with the same
   inner padding on every side it touches.
3. **Given** a track or group several levels deep and a narrow header column, **When** the
   header is laid out, **Then** the name trims (with an ellipsis) before any chip, fader,
   pan, input picker or meter is pushed out of place or clipped, and those controls sit in
   the same columns as on a top-level row.

---

### User Story 3 - The 0.59.2 staircase in the classic look (Priority: P2)

A user who switched to the classic look gets the group headers they knew in 0.59.2: each
row's own colour spine, stepped in a little per nesting level, and the slim group row
with its meter below the title.

**Why this priority**: The classic look exists to bring 0.59.2's appearance back; groups
are part of that, but fewer users wear the classic look than the default.

**Independent Test**: Switch to the classic look with the grouped project open and compare
the track headers with 0.59.2 on the same project.

**Acceptance Scenarios**:

1. **Given** the classic look, **When** the headers are shown, **Then** each row carries
   only its own 3 px colour spine, stepped in 10 px per nesting level, with no bars for
   enclosing groups and no group-colour tints.
2. **Given** the classic look, **When** a group row and a track row are shown, **Then** the
   group row sits on the raised surface and tracks on the card surface, as in 0.59.2, and
   the selected row's spine is drawn in the accent.
3. **Given** the classic look and an unselected group, **When** its row is shown, **Then**
   it is 0.59.2's slim group row: disclosure chevron, the name in the smaller title size,
   M / S / Link, the "Group" tag, and the level as a horizontal meter on its own line under
   the title — not under the chips.
4. **Given** the Arrangement is open, **When** the user switches between New and Classic in
   Preferences → Appearance (or an MCP tool sets the look), **Then** the track headers
   change to the other look's group drawing at once, without reopening the project.

---

### Edge Cases

- **Selected group**: a selected group shows its full header (fader, pan, meter) as today;
  in the new look it still sits in its tray as the tray's header row.
- **Deep nesting** (4 or more levels): each level insets its tray by the same step; the
  name trims first; controls keep their columns. Nesting deeper than the column can show
  still renders without overlapping controls.
- **Empty group** (no members): the tray is just the header row, closed below it.
- **Group whose last member is itself a group**: both trays close, inner first, then outer,
  within the existing row heights.
- **Group colour equal to a member's colour**: the member's own bar still reads against the
  tray wash (the wash is soft, the bar is the full colour).
- **Light themes** (Studio Light, Ember Paper): the tray wash stays a soft tint that keeps
  text and chips readable.
- **Row height changes** (dragging a row seam, Compact / Standard / Large) inside a tray:
  the tray follows; header rows stay aligned with their lanes.
- **Drag-reordering tracks into, out of or between groups**: the drop line and the trays
  redraw for the new hierarchy.
- **Takes rows / footer rows (returns, master)**: not part of a group; unaffected.
- **A user skin worn**: the tray uses the skin's track colours and surfaces; the look
  (New / Classic) decides tray vs staircase.

## Requirements *(mandatory)*

### Functional Requirements

**New look — trays**

- **FR-001**: In the new look, a group's header row and all of its visible members MUST be
  drawn on one tray: a wash of the group's colour (16 % on the group's header row, about
  8 % on the rest of the tray around the member cards), a 3 px edge in the group's colour
  down the tray's left side, rounded right corners, and a small bottom padding under the
  last visible member that closes the tray.
- **FR-002**: In the new look, a member track's header MUST carry only its own 3 px colour
  bar; it MUST NOT draw a bar for any enclosing group, and its card MUST NOT be tinted by
  any group's colour.
- **FR-003**: In the new look, a nested group MUST be drawn as a tray inside its parent's
  tray, inset by one fixed step (about 4 px) per nesting level, replacing today's 14 px
  indent plus 4 px bar per level.
- **FR-004**: Group tints MUST NOT stack: a member card sits on the tray, not under one or
  more washes of its own.
- **FR-005**: In the new look, the group header row MUST show the disclosure chevron, the
  name, M / S, the Cue key when Cue is shown, Link, and a vertical level meter in the same
  right-hand column as the tracks' meters.
- **FR-006**: In both looks, a folded group's row MUST show after its name the number of
  tracks it hides — every track inside it, including tracks in nested groups; nested groups
  themselves are not counted. In the new look its tray shows only that row.

**Both looks — layout and meters**

- **FR-007**: Nesting MUST move only the left side of a header (spine or tray edge, colour
  bar, name). Chips, fader, pan, input picker and meter MUST keep the same columns at
  every depth; when space runs out, the name trims first.
- **FR-008**: Track and group meters MUST lie fully inside their header card, with the same
  inner padding, on every row height (Compact, Standard, Large), every built-in theme and
  both looks.
- **FR-009**: Header rows MUST stay aligned with their lanes: tray insets and paddings MUST
  come out of the existing row heights, never add to them.

**Classic look — 0.59.2 staircase**

- **FR-010**: In the classic look, each row MUST carry only its own 3 px colour spine,
  stepped in 10 px per nesting level, with no bars for enclosing groups and no group-colour
  tints on headers.
- **FR-011**: In the classic look, group rows MUST sit on the raised surface and track rows
  on the card surface, and the selected row's spine MUST be drawn in the accent, as in
  0.59.2.
- **FR-012**: In the classic look, the slim group row MUST be 0.59.2's: chevron, name in the
  smaller title size, M / S / Link (and Cue when shown), the "Group" tag, and a horizontal
  level meter on its own line beneath the title row, never beneath the chips.

**Switching and lanes**

- **FR-013**: Changing the look (Preferences → Appearance or the MCP look tool) MUST redraw
  the track headers in the other look's group drawing immediately, with the project open.
- **FR-014**: In the new look, a group's own lane in the Arrangement MUST keep its wash of
  the group colour (16 %, as today), and member lanes MUST carry no group wash; in the classic
  look, the group's lane MUST carry no group-colour wash, as in 0.59.2. The preview of
  members' clips on a group's lane is unchanged in both looks.
- **FR-015**: The style guide (DESIGN.md § Groups and the track-header section) MUST
  describe the tray rules for the new look and the staircase for the classic look.

### Key Entities

- **Group tray** (new look): the visual container of one group in the header column — its
  header row plus its visible members, with its colour, nesting level and folded state.
- **Spine** (classic look): a row's own 3 px colour stripe and its step-in for the row's
  nesting depth.
- **Header meter**: the level display on a track or group header; its placement differs by
  look and row form (slim group row vs full card).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the new look, a track two levels deep shows exactly one colour stripe
  (its own), and its name starts at most 12 px further in than a top-level track's
  (today: about 50 px).
- **SC-002**: In both looks, at every row height and in all four built-in themes, no meter
  pixel falls outside its header card and no meter is drawn under a name or a chip.
- **SC-003**: At every nesting depth from 0 to 4, the M chip of every track row of the same
  kind starts at the same horizontal position.
- **SC-004**: In the classic look, the headers of a grouped project match 0.59.2's on the
  same project, row for row (spine positions, surfaces, slim group row), judged from side
  by side screenshots.
- **SC-005**: Switching the look with a grouped project open redraws the headers within one
  frame of the switch, with no reopen and no lost row heights, fold states or selection.
- **SC-006**: In a hallway check, a user shown the new-look headers of a project with a
  nested group correctly names every track's group and where each group ends on the first
  try.

## Assumptions

- The selected-group full card (fader, pan, meter) stays as today in both looks; only its
  surroundings (tray or spine) follow the look.
- The group lane's preview of members' clips (stacked mini-clips folded, a colour strip
  unfolded) is an existing feature and is not changed; "child-clip summary" in the issue's
  out-of-scope list means no new summary beyond it.
- Clip drawing (Solid / Tinted, #572) is independent of this feature.
- The Mixer's group bands (specs/070), sticky group headers while scrolling, and the Nota
  Remote view are out of scope.
- No engine or C ABI change: this is presentation only.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only, meters read the existing level feed
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no new crossings
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
