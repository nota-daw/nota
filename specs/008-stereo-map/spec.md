# Feature Specification: Stereo Map (MVP)

**Feature Branch**: `008-stereo-map`

**Created**: 2026-10-14

**Status**: Draft

## Clarifications

### Session 2026-10-14

- Q: Should a map drag be undoable, given that volume and pan are not undoable anywhere in
  Nota today? → A: Option C — make volume and pan edits undoable everywhere (Mixer, track
  headers, the map), one gesture = one step. That capability is its own prerequisite spec
  (volume/pan undo), done before this one, like the Stereo pan law (specs/006); this spec
  only requires that a map drag uses it as one step.
- Q: At what correlation should the map mark a track red (cloud outline, handle ring, master
  correlation bar)? → A: Below −0.05 — a small tolerance so unrelated stereo material, whose
  correlation hovers around 0, doesn't flicker red; real phase problems (well below 0) still
  show.

**Input**: User description: "Stereo Map — MVP (first of three specs: MVP, then V2 diagnostics, then V3 Remote/automation). A "Stereo Map" tab in the Mixer window (title-bar switch Strips | Stereo Map, per nota-design/Nota Mixer v2.html) that shows where every track really sits in the stereo field. Plane: X = pan L100…C…R100, Y = fader volume. Each track, return and the master gets a handle (the control) and a measured cloud (centre = measured balance, width = measured stereo width, opacity = measured level), measured post-fader and post-pan. Master: fixed handle, master correlation bar. Drag a handle = pan + volume, Shift locks the axis, double-click resets pan, ⌥+double-click resets volume; click selects; hover tooltip with the measurements. Requires the Stereo pan law (specs/006). Out of scope for MVP: Frequency axis, warnings, Conflicts, Hold, Mono, filters, Export PNG, Remote, automation write from the map, multi-select, MCP."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Ember) — plus a small engine part: stereo measurements per track
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Design reference**: [`Nota Mixer v2.html`](../../../nota-design/Nota%20Mixer%20v2.html)
(sibling repo `nota-design`). The mock shows the MVP **and** the later versions on one
screen; only the parts listed in this spec belong to the MVP (see Out of Scope).

**Current state (for reference)**:

- The Mixer is its own window (**View ▸ Mixer**, ⌘⇧M): a row of channel strips with a
  footer (Show Sends / I/O, PAN LAW from specs/006, a disabled crossfader placeholder).
  It has no tabs and no track selection of its own; the app's selected track lives in the
  arrangement.
- Every track, return and the master already has a live peak/RMS meter per channel. There
  is no measure of stereo balance, width or correlation anywhere.
- Volume and pan changes are **not undoable** anywhere in Nota today (Mixer knobs, faders,
  track headers): the engine's undo covers structural edits only. A separate prerequisite
  spec makes them undoable (clarification 2026-10-14); this spec depends on it.
- The Stereo pan law (specs/006) is implemented: under Stereo, a panned stereo track's
  image follows its pan, so a measured cloud lines up with its handle.

**Later specs (not this one)**: V2 diagnostics — Frequency Y-axis, wide-bass and phase
warnings, Conflicts lines, Hold, Mono, the All / Tracks / Returns / Selected filter.
V3 — Nota Remote mirror of the map, automation write from the map, multi-select
(⌘-click, marquee) with ⌥-drag spread, Export PNG, MCP access.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See where every track really sits (Priority: P1)

A producer's chorus sounds crowded. They open the Mixer, switch to **Stereo Map** and,
while the song plays, see every track as a dot on a plane — left/right by pan, up/down by
volume — with a coloured cloud showing where its sound actually is and how wide it is. Five
clouds pile up in the centre at the same height; the pad's cloud spans the whole width even
though its pan is centred; the lead's cloud sits right of its dot because the source itself
leans right.

**Why this priority**: Seeing the real stereo picture is the whole point of the feature;
without it nothing else on the map matters.

**Independent Test**: Play a project with a centred mono track, a hard-panned mono track
and a wide stereo pad; open the Stereo Map and compare each cloud's position and width
with the known material.

**Acceptance Scenarios**:

1. **Given** a mono track panned L50 and playing, **When** the Stereo Map is open,
   **Then** its cloud is narrow and centred near L50, at the height of its fader.
2. **Given** a stereo track whose channels carry unrelated material, panned C,
   **When** playing, **Then** its cloud spans nearly the full width and the tooltip
   shows correlation near 0.
3. **Given** a track that is silent (below −60 dBFS) or muted, **When** the map is open,
   **Then** its dot stays and its cloud is hidden; a muted track's dot is dimmed.
4. **Given** one track soloed, **When** the map is open, **Then** the other tracks'
   dots and clouds are dimmed.
5. **Given** the song plays, **When** the user looks under the map, **Then** a master
   correlation bar (−1 … +1) moves with the mix, red when below −0.05.

---

### User Story 2 - Place tracks by dragging them (Priority: P1)

The producer drags the guitar's dot to the left and down: its pan moves to L40 and its
volume drops by 3 dB, and the channel strip, the track header and the arrangement show the
same values. Holding Shift keeps the drag horizontal so only the pan changes.
Double-clicking a dot centres its pan; ⌥+double-click returns its volume to 0 dB.

**Why this priority**: The map becomes a faster way to make a rough mix, not just a
display — the second half of the MVP's value.

**Independent Test**: Drag a dot with and without Shift and check the pan and volume in
the channel strip; double-click and ⌥+double-click it.

**Acceptance Scenarios**:

1. **Given** a track at C / 0 dB, **When** the user drags its dot left and down,
   **Then** its pan and volume change together, live, and the Mixer strip, track header
   and any other view show the same values.
2. **Given** a drag in progress, **When** Shift is held, **Then** only the axis with the
   larger movement changes.
3. **Given** a dot, **When** double-clicked, **Then** pan returns to C; **When**
   ⌥+double-clicked, **Then** volume returns to 0 dB.
4. **Given** pan or volume automation in read mode on that track, **When** the user drags
   its dot, **Then** the behaviour matches dragging the Mixer knob/fader of that track
   (same automation rules).
5. **Given** the master dot, **When** the user drags it, **Then** nothing moves (the
   master has no pan; its volume stays on its fader).
6. **Given** a drag has finished, **When** the user presses ⌘Z, **Then** the track's pan
   and volume both return to where the drag started — one undo step for the whole drag,
   the same as one gesture on its Mixer knob or fader; ⌘⇧Z redoes it.
7. **Given** a double-click or ⌥+double-click reset, **When** the user presses ⌘Z,
   **Then** the previous pan / volume returns (one step).

---

### User Story 3 - Inspect a track (Priority: P2)

The producer hovers the lead's dot and reads its pan, volume, measured balance, width,
correlation and level, plus a note: "Image is off the pan — the pan is at C, but the signal
measures R25. The source itself is unbalanced." Clicking the dot selects the lead
everywhere in Nota.

**Why this priority**: Turns the picture into numbers and connects the map to the rest
of the app, but the map is useful without it.

**Independent Test**: Hover dots of known material and compare the tooltip values; click
a dot and check the arrangement selection.

**Acceptance Scenarios**:

1. **Given** a dot, **When** hovered (and not dragging), **Then** a tooltip shows name,
   PAN, VOLUME, BALANCE, WIDTH, CORRELATION and LEVEL ("below −60 dBFS" when silent).
2. **Given** a track whose measured balance differs from its pan by more than 0.15,
   **When** hovered, **Then** the tooltip adds the "Image is off the pan" note.
3. **Given** a muted or silent track, **When** hovered, **Then** the tooltip says why
   there is no cloud ("Muted" / "Silent").
4. **Given** a dot, **When** clicked, **Then** that track becomes the selected track in
   the arrangement, and the map shows it with a selection ring; selecting a track
   elsewhere moves the ring.

### Edge Cases

- **Many tracks in one spot**: the selected dot is drawn on top, then louder over
  quieter; a label that would overlap another is hidden (the name stays in the tooltip).
- **Groups**: group tracks are not drawn; their child tracks are. The group's own pan and
  volume still affect the children's measured clouds (measurements are of what reaches
  the master).
- **Returns**: drawn like tracks, with their own fader and pan; their cloud is what the
  return sends to the master.
- **Pan law Balance (older project)**: the map still works; clouds then show the balance
  law's behaviour (a hard-panned stereo track's cloud collapses to one side with its far
  channel gone).
- **Transport stopped**: clouds fade out (silent) unless something still sounds (tails,
  monitoring, preview into a track); dots stay and stay draggable.
- **Track added, removed, renamed or recoloured while the map is open**: the map follows
  within one refresh.
- **Window too small**: the map keeps its aspect; labels drop first, dots never.
- **Automation driving pan/volume**: dots move with the automated values during playback.

## Requirements *(mandatory)*

### Functional Requirements

**View**

- **FR-001**: The Mixer window MUST offer two views, **Strips** and **Stereo Map**,
  switched by a segmented control centred in its title bar (design reference); the
  choice is remembered between openings of the window. First opening shows Strips.
- **FR-002**: In the Stereo Map view the map takes the main area and the channel strips
  stay visible beside it in a narrower, horizontally scrolling column (design reference);
  the footer (Show, PAN LAW, crossfader) stays.
- **FR-003**: The map MUST be a plane with X = pan (L100 … C … R100, grid lines and labels
  at L100, L50, C, R50, R100) and Y = fader volume on the same scale as the Mixer fader
  (grid lines and labels at its top, 0 dB, −6, −12, −24), C and 0 dB lines emphasised.
- **FR-004**: The map header MUST read **STEREO MAP**. A hint line under the map MUST read
  "Drag: pan + volume · Shift locks axis · double-click centres pan · ⌥ double-click 0 dB".

**Measurement**

- **FR-005**: For every track, return and the master, Nota MUST measure, from the signal
  that reaches the master (after fader and pan), over a window of about 300 ms with
  smoothing: **balance** (−1 … +1, energy left vs right), **width** (0 = mono … 1 =
  fully side), **correlation** (−1 … +1) and **level** (RMS, dBFS).
- **FR-006**: A signal below −60 dBFS counts as silent.
- **FR-007**: Measuring MUST NOT add audible load: no dropouts, and the audio cost stays
  within the existing meters' budget (see SC-004).

**Drawing**

- **FR-008**: Every non-group track and every return MUST have a **handle** (dot in the
  track colour) at (its pan, its fader volume), and the master a fixed handle at
  (C, master volume).
- **FR-009**: Each handle with sound MUST have a **cloud** at the handle's height: a
  horizontal ellipse centred on the measured balance, half as wide as the measured width
  spans the map (a minimum visible width for mono), in the track colour, more opaque the
  louder the measured level; a cloud whose correlation is below −0.05 (negative beyond a small
  tolerance) gets a red outline, and so does its handle's ring.
- **FR-010**: Silent tracks MUST show no cloud; muted tracks MUST show a dimmed handle and
  no cloud; with any solo active, non-soloed tracks (not returns or master) MUST be dimmed.
- **FR-011**: Each handle MUST carry its track name as a label (truncated), unless the
  label would overlap another; drawing order is selected first, then by level.
- **FR-012**: A **MASTER CORR** bar (−1 … +1, centre mark, value as a number) under the map
  MUST show the master correlation, green at or above −0.05 and red below it.
- **FR-013**: The map MUST update at the display rate and follow track list changes,
  renames, recolours, mute/solo, automation and edits made elsewhere.

**Interaction**

- **FR-014**: Dragging a non-master handle MUST change that track's pan (horizontal) and
  volume (vertical) together, live; Shift locks to the dominant axis.
- **FR-015**: Double-click on a handle MUST centre its pan; ⌥+double-click MUST set its
  volume to 0 dB. The master handle ignores both.
- **FR-016**: Map edits MUST behave exactly like the same edits on the track's Mixer knob /
  fader, including automation recording/read rules; Nota Remote and every other view show
  the new values.
- **FR-017**: Each map gesture (one drag, one reset) MUST be exactly one undo step covering
  everything it changed, through Nota's volume/pan undo (prerequisite spec), interleaved
  correctly with structural undo steps.
- **FR-018**: Clicking a handle MUST select that track app-wide (the arrangement's selected
  track); the selected track shows a brass ring on the map and stays drawn on top.
- **FR-019**: Hovering a handle MUST show the tooltip of US3 (rows PAN, VOLUME, BALANCE,
  WIDTH, CORRELATION, LEVEL; notes "Image is off the pan" when |balance − pan| > 0.15,
  "Muted", "Silent").

**Quality**

- **FR-020**: Ember design language, both themes, matching the design reference.
- **FR-021**: The automated smoke test MUST cover the measurements: a mono source panned
  L50 measures balance ≈ the Stereo law's balance at L50 and width ≈ 0; a stereo source
  with unrelated channels at C measures width ≈ 1 and correlation ≈ 0; an inverted-polarity
  pair measures correlation ≈ −1; silence measures below −60 dBFS.
- **FR-022**: The change MUST be listed in the changelog, FEATURES.md and the user docs
  (EN + RU, Mixer page: a Stereo Map section with screenshots in both themes).

### Key Entities

- **Stereo measurement** (per track / return / master, live): balance, width, correlation,
  level; silent flag.
- **Map handle**: a track's pan and fader volume as a point; colour, name, selected /
  muted / dimmed state.
- **Mixer view choice**: Strips or Stereo Map; remembered per user (not per project).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For reference material (mono at L50, unrelated stereo at C, inverted pair),
  the measured balance is within ±0.05, width within ±0.05 and correlation within ±0.05 of
  the expected values.
- **SC-002**: A user can place a track at a target pan and volume (±5 % pan, ±1 dB) with
  one drag on the map.
- **SC-003**: The map stays smooth (60 frames per second) with 128 tracks while playing.
- **SC-004**: With 64 tracks playing, measuring adds no more than 1 % of the audio block
  time, and no dropouts occur.
- **SC-005**: Clouds react to a change in the signal within 0.5 s.

## Assumptions

- The map shows what reaches the master (after fader and pan), for returns as well —
  decided in the feature discussion; pre-fader views are not offered.
- The Y axis uses the Mixer fader's scale and range (its top is the fader's maximum,
  about +3.5 dB), not the mock's +6 dB, so a handle and its fader always agree.
- Depends on two prerequisites: the Stereo pan law (specs/006, done) and volume/pan undo
  (its own spec, to be done first).
- Pan is the Stereo pan law's pan (specs/006); under Balance the map still works and
  simply shows that law's result.
- The Strips / Stereo Map choice is a user preference, like the Show Sends / I/O toggles,
  not saved in the project.
- Clouds use measured level only for opacity; their height is the handle's (fader) height,
  as in the design reference.
- Multi-selection, marquee and ⌥-drag spread are V3; the MVP selects one track.

## Out of Scope (MVP)

Frequency Y-axis; wide-bass and phase warnings; Conflicts lines; Hold (ghost clouds); Mono
button; All / Tracks / Returns / Selected filter; Export PNG; the REMOTE indicator and the
Nota Remote mirror; writing automation from the map; multi-select, marquee and ⌥-spread;
MCP tools; the strip redesign shown in the mock.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Ember design language)
