# Feature Specification: Modular tab in the browser, arrangement track headers in Modular

**Feature Branch**: `095-modular-browser-headers`

**Created**: 2026-11-12

**Status**: Draft

**Issue**: [nota-daw/nota#592](https://github.com/nota-daw/nota/issues/592) (approved)

**Input**: User description: "Modular view: (1) a new Modular tab in the browser rail, with its own icon, listing every Modular-only node (LFO, Envelope Follower, MIDI → CV, ADSR, Macro, Math, Scope) with category and description, searchable; drag onto the Modular canvas adds the node unconnected at the drop point on the shown track, double-click/Enter adds it at the middle of the view; new node kinds appear automatically. (2) Replace Modular's 'Tracks' rail (name + colour stripe only, groups omitted, always left) with the same track headers the arrangement draws, with all their controls and gestures; clicking a header shows that track's graph; the column follows 'Track headers on the right' live; Global view keeps hiding the column. Out of scope: new modulator kinds, engine/DSP changes, Global view islands, lanes/clips in Modular. Open question: keep or remove the toolbar '+ Add'."

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

**Where things are today** (for the plan, not requirements):

- Modular nodes are reachable only from the Modular toolbar's **+ Add** dropdown and from the
  command palette opened in Modular. The node list is the single catalogue the palette also
  reads (seven nodes, each with a name, a category and a one-line description).
- The browser rail has nine tabs: Instruments, Audio effects, MIDI effects, Files, Presets,
  Projects, MIDI map, History, Track inspector.
- Modular's Track view has a fixed-width "Tracks" rail on the left: one row per track with
  the name and a colour stripe; group tracks are left out; clicking a row shows its graph and
  selects the track in the arrangement. It ignores **View → Track headers on the right**.
- The Modular canvas already accepts browser drops of instruments, effects, MIDI effects and
  samples onto the shown track.

## Clarifications

### Session 2026-11-12

- Q: Keep the toolbar's + Add next to the browser tab? → A: No — remove it (US4, FR-019).
- Q: What does clicking a group's header in Modular do? → A: Opens the group's effect chain as a graph (US3, FR-017–FR-018).
- Q: What does the Modular tab do when Modular isn't the shown view? → A: It isn't shown at all — the tab is on the rail only while Modular is the main view (FR-001, FR-007a).
- Q: Should the browser switch to the Modular tab when Modular opens? → A: The browser keeps a separate last-picked tab for Modular; the first time Modular opens it picks the Modular tab (FR-007b).
- Q (added by the user): Is there a way to jump to a track's graph from elsewhere? → A: Yes — a **Show in Modular** item in the track context menu (US5, FR-022–FR-024).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find and add Modular nodes from the browser (Priority: P1)

A producer patching modulation opens the browser's new **Modular** tab and sees every node
Modular offers — LFO, Envelope Follower, MIDI → CV, ADSR, Macro, Math, Scope — each with its
category and a short description. They type "env" in the search field to narrow the list,
drag **Envelope Follower** onto an empty spot on the canvas, and it appears exactly there,
unconnected, ready to patch. Next they double-click **LFO** and it appears in the middle of
the visible canvas.

**Why this priority**: This is the first half of the issue and the main discoverability gap:
today the nodes hide behind a toolbar menu with names only, and they can't be dragged.

**Independent Test**: With a project open in Modular (Track view on an instrument track),
open the Modular tab, check the list, search, drag one node and double-click another;
verify both land on the shown track at the expected places, unconnected, each as one undo
step.

**Acceptance Scenarios**:

1. **Given** Modular is the shown view and the browser is open, **When** the user looks at
   the rail, **Then** there is a Modular tab with its own icon, distinct from every other
   tab's, and a tooltip naming it.
1a. **Given** Arrangement or Session is the shown view, **When** the user looks at the rail,
   **Then** there is no Modular tab; switching to Modular brings it back.
1b. **Given** Modular has never been opened, **When** the user switches to Modular, **Then**
   the browser shows the Modular tab; **Given** the user then picks Audio effects in Modular,
   goes to the arrangement and comes back, **Then** the browser shows Audio effects.
2. **Given** the Modular tab is open, **When** the list shows, **Then** it lists every
   Modular node kind the app offers, each with its name, category and description, in the
   same order the palette uses.
3. **Given** the Modular tab is open, **When** the user types in the browser search field,
   **Then** the list narrows by name, category and the node's search aliases (for example
   "wobble" finds LFO), as the palette's search does.
4. **Given** Modular is in Track view on a track, **When** the user drags a node from the tab
   onto the canvas, **Then** the canvas shows the drop highlight while over it and, on drop,
   the node is added to the shown track, unconnected, at the drop point, as one undo step.
5. **Given** Modular is in Track view on a track, **When** the user double-clicks a node (or
   selects it and presses Enter), **Then** the node is added to the shown track, unconnected,
   in the middle of the visible canvas, as one undo step.
6. **Given** a new node kind is added to the app's Modular node catalogue, **When** the app
   is built and run, **Then** the node appears in the Modular tab with no other change.

---

### User Story 2 - Arrangement track headers in Modular (Priority: P1)

The producer works in Modular and wants the same track column they know from the
arrangement: a group's tray with its children, each track's mute / solo / arm, volume, pan,
meter and context menu. They mute a track right from Modular, rename another, drag a third
into a group, and click a header to switch the graph to that track. Their preference puts
track headers on the right, and Modular's column sits on the right too.

**Why this priority**: The second half of the issue. Without it Modular is the only view where
the track column behaves differently, and mixing while patching means leaving the view.

**Independent Test**: In a project with instrument, audio, group and return tracks, open
Modular Track view: compare the column with the arrangement's (same cards, same controls,
same look); use mute, solo, arm, rename, recolour, drag-reorder and the context menu from
Modular and check the arrangement reflects each; toggle **Track headers on the right** and
see the column move in both views at once.

**Acceptance Scenarios**:

1. **Given** Modular Track view is shown, **When** the user looks at the track column,
   **Then** it shows the same track header cards as the arrangement, for the same tracks, in
   the same order, including group rows with their trays and nesting, in the current look
   (new or classic).
2. **Given** a header card in Modular, **When** the user uses any of its controls or gestures
   (rename, colour, mute, solo, arm, monitor, input, volume, pan, sends, group link, fold /
   unfold, drag to reorder or into / out of a group, the context menu, multi-select), **Then**
   it behaves exactly as the same action on the arrangement's card, and the arrangement,
   mixer and session views reflect it.
3. **Given** the header cards in Modular, **When** audio plays, **Then** their meters move
   as they do in the arrangement.
4. **Given** Modular Track view, **When** the user clicks a patchable track's header (an
   instrument, audio or return track), **Then** Modular shows that track's graph and the
   track becomes the selected track app-wide, as the old rail row did.
5. **Given** **Track headers on the right** is on, **When** Modular Track view is shown,
   **Then** the column is on the right of the canvas; **When** the user toggles the setting
   while Modular is shown, **Then** the column moves immediately, without reopening the view.
6. **Given** Modular switches to Global view, **When** it shows the islands, **Then** the
   header column is hidden, as the rail is today; switching back to Track view brings it
   back.
7. **Given** a track is added, removed, renamed, recoloured or moved from anywhere, **When**
   Modular is shown, **Then** its column updates to match.

---

### User Story 3 - Patch a group's effect chain (Priority: P2)

With every track's header in Modular, groups are there too. The producer clicks a group's
header and Modular shows the group's own effect chain as a graph — the audio coming in from
its tracks, its effects as nodes, its output — so they can drop an LFO on it and modulate the
group's filter the same way they would on any track.

**Why this priority**: Follows from US2 (group headers are now in Modular); without it a group
header in Modular would be the only header that doesn't open a graph.

**Independent Test**: In a project with a group that has an effect on it, click the group's
header in Modular: its chain shows as a graph; add an LFO from the browser tab, patch it to an
effect parameter, play, and hear / see the parameter move; save, reopen, and the patch and node
positions are still there.

**Acceptance Scenarios**:

1. **Given** Modular Track view, **When** the user clicks a group's header, **Then** Modular
   shows the group's effect chain as a graph (input, effects, output), and the group becomes
   the selected track app-wide.
2. **Given** a group's graph is shown, **When** the user adds a node from the browser tab and
   patches it to an effect parameter, **Then** the modulation works during playback as on any
   audio track, and is saved with the project with its node positions.
3. **Given** a group's graph is shown, **When** the user expands, bypasses, duplicates, deletes
   or reorders an effect node, **Then** it behaves as on an audio track's graph.
4. **Given** a group's graph is shown, **When** the user folds or unfolds the group from its
   header, **Then** the graph stays on the group.

---

### User Story 4 - The browser tab replaces the toolbar's + Add (Priority: P3)

With the browser tab in place, the Modular toolbar's **+ Add** dropdown goes away: the browser
tab and the command palette are the ways to add a node, and the toolbar keeps only the view
switch and the zoom controls.

**Why this priority**: Toolbar clean-up; depends on US1 being in.

**Independent Test**: Open Modular and check the toolbar has no **+ Add**; add a node from the
browser tab and from the palette.

**Acceptance Scenarios**:

1. **Given** Modular is shown, **When** the user looks at the toolbar, **Then** there is no
   **+ Add** dropdown.
2. **Given** Modular is shown, **When** the user opens the command palette, **Then** Modular
   nodes are still listed there and added as today.

---

### User Story 5 - Show a track in Modular from its context menu (Priority: P2)

Working in the arrangement, the producer right-clicks a track's header and picks **Show in
Modular**: the main view switches to Modular, in Track view, on that track's graph, and the
track is selected.

**Why this priority**: The quick way into a specific track's graph; useful but the view switch
plus a header click already gets there.

**Independent Test**: From the arrangement, the mixer and the session view, use **Show in
Modular** on an instrument, an audio, a return and a group track; each time Modular opens on
that track.

**Acceptance Scenarios**:

1. **Given** a patchable track (instrument, audio, return, group) in the arrangement, mixer or
   session view, **When** the user picks **Show in Modular** from its context menu, **Then**
   Modular becomes the shown view, in Track view, showing that track's graph, and the track is
   selected.
2. **Given** Modular is in Global view, **When** the user picks **Show in Modular** on a track,
   **Then** Modular switches to Track view on that track.
3. **Given** the master track, **When** the user opens its context menu, **Then** there is no
   **Show in Modular** item.
4. **Given** the track already shown in Modular's Track view, **When** the user opens its
   header's menu in Modular, **Then** **Show in Modular** is absent (or disabled) there.

---

### Edge Cases

- **Leaving Modular with the Modular tab open**: the tab leaves the rail and the browser shows
  the tab that was open before it.
- **Coming back to Modular**: the browser shows the tab last picked while in Modular (the
  Modular tab the first time).
- **Global view or no track shown**: a node can't be added — no track's graph is shown; the
  drop is refused and double-click / Enter explains why ("Open a track in Modular").
- **Dropping a node elsewhere** (the arrangement, session, device panel, the header column
  itself): refused, no change.
- **Dropping a device on a header in Modular**: behaves as dropping it on the same header in
  the arrangement (the arrangement already accepts browser drops on its headers).
- **Group header clicked**: opens the group's effect chain as a graph (US3). A group with no
  effects shows its input wired straight to its output, ready for nodes and drops.
- **Group deleted or ungrouped while its graph is shown**: as when the shown track is deleted —
  the canvas empties.
- **Global view**: groups still get no island (Global view is out of scope).
- **Master**: the arrangement's footer rows (returns, master) appear in Modular's column the
  same way; clicking the master row selects it but does not change the graph (Modular doesn't
  patch the master).
- **The shown track is deleted**: as today, the canvas empties and the column no longer marks
  any track as shown.
- **Very many tracks**: the column scrolls vertically on its own; scrolling it doesn't move the
  canvas.
- **Narrow window**: the column keeps the arrangement's header width setting; the canvas takes
  the rest.

## Requirements *(mandatory)*

### Functional Requirements

**Browser tab**

- **FR-001**: While Modular is the shown view, the browser rail MUST have a Modular tab with
  its own icon (not reused from any other tab) and a localised tooltip.
- **FR-007a**: While Arrangement or Session is the shown view, the rail MUST NOT show the
  Modular tab; leaving Modular with it open MUST return the browser to the tab open before it.
- **FR-007b**: The browser MUST remember a separate last-picked tab for Modular and show it
  whenever Modular opens; the first time Modular opens (no tab picked there yet) it MUST show
  the Modular tab. Whether that memory survives an app restart follows how the browser keeps
  its last tab today.
- **FR-002**: The Modular tab MUST list every node in the app's Modular node catalogue — the
  same list the palette uses — showing each node's name, category and description.
- **FR-003**: A node kind added to that catalogue MUST appear in the tab with no tab-specific
  change.
- **FR-004**: The browser's search field MUST filter the Modular tab by name, category and
  search aliases, with the same matching rules as the other browser tabs.
- **FR-005**: Dragging a node from the tab onto the Modular canvas in Track view MUST add it to
  the shown track, unconnected, at the drop point, as one undo step.
- **FR-006**: Double-click or Enter on a node MUST add it to the shown track, unconnected, at
  the middle of the visible canvas, as one undo step.
- **FR-007**: Where a node can't be added (Global view, no track shown, any drop target other
  than the Modular canvas), the drag MUST be refused and double-click / Enter MUST leave the project
  unchanged and show the reason.
- **FR-008**: The tab MUST keep the browser's behaviour shared by the list tabs: the collapsed
  / expanded browser, keyboard navigation in the list, and remembering the last open tab.

**Track headers in Modular**

- **FR-009**: Modular's Track view MUST show the arrangement's track header cards in place of
  the current "Tracks" rail: the same tracks (groups included), order, nesting, group trays,
  return and master rows, look (new and classic) and header width.
- **FR-010**: Every control, gesture and menu on those cards MUST work in Modular as in the
  arrangement and change the same project state; changes made in Modular MUST show in every
  other view, and changes made elsewhere MUST show in Modular.
- **FR-011**: Meters on the cards MUST animate during playback in Modular.
- **FR-012**: Clicking a patchable track's header (instrument, audio, return, group) MUST show
  that track's graph and make it the app-wide selected track; the shown track MUST be marked on its
  card as the selected track is in the arrangement.
- **FR-013**: The column MUST be on the left or right of the canvas according to **Track
  headers on the right**, and MUST move at once when that setting changes.
- **FR-014**: Global view MUST hide the column; Track view MUST show it.
- **FR-015**: Row heights in Modular's column MUST be the arrangement's row heights (a row
  resized in either view is that height in both).
- **FR-016**: The column MUST scroll vertically on its own and keep its scroll position while
  the user works on the canvas.

**Group graphs**

- **FR-017**: A group's graph MUST show its effect chain (input → effects → output) with the
  same node actions as an audio track's graph (expand, bypass, duplicate, delete, reorder,
  browser drops of effects).
- **FR-018**: Modular nodes MUST be addable to a group's graph and patchable to its effects'
  parameters; the modulation MUST act during playback and be saved and restored with the
  project, node positions included.

**Toolbar**

- **FR-019**: The Modular toolbar MUST NOT have the **+ Add** dropdown; Modular nodes remain
  available in the command palette opened from Modular.

**Show in Modular**

- **FR-022**: Every track context menu that the arrangement, mixer and session views show for
  a patchable track (instrument, audio, return, group) MUST have a **Show in Modular** item.
- **FR-023**: **Show in Modular** MUST make Modular the shown view, in Track view, on that
  track's graph, and select the track; it is one action, not an undo step.
- **FR-024**: The item MUST NOT appear for the master track, and MUST NOT be enabled in
  Modular for the track Modular already shows.

**General**

- **FR-020**: All new user-visible text MUST be localised (English and Russian).
- **FR-021**: The change MUST NOT alter the engine, the project file format or Global view
  (issue #592 scope). Group tracks are engine tracks and already run CV modulation like any
  track; the plan confirms this, and if it turns out false, group graphs (US3) are split out
  into their own issue rather than widening this one.

### Key Entities

- **Modular node**: a node only Modular offers (a modulator, converter, control, CV utility or
  monitor). Has a name, a category, a description and search aliases; added to a track's
  graph unconnected.
- **Track header card**: the arrangement's per-track control card (name, colour, mute / solo /
  arm, monitor, input, volume, pan, meter, sends, context menu, group tray), now shown in two
  views over the same track state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add any Modular node to the canvas from the browser in one drag or
  one double-click, without opening a menu.
- **SC-002**: 100 % of the Modular node kinds the app offers are listed in the Modular tab, and
  searching any node's name, category or alias finds it.
- **SC-003**: Every action available on an arrangement track header (100 % of the controls,
  gestures and context-menu items) is available and gives the same result from Modular's
  column.
- **SC-004**: Toggling **Track headers on the right** moves Modular's column within the same
  frame update as the arrangement's, with no restart or view switch.
- **SC-005**: Showing Modular with 64 tracks feels as responsive as the arrangement with the
  same project: no visible stall when switching to Modular or clicking a header.

## Assumptions

- The tab is placed on the rail right after MIDI effects (devices together); its icon is a new
  glyph in Nota's icon set (a patch cable / modulation mark) drawn to DESIGN.md.
- Nodes in the tab are not presets or files: no audition player and no favourites beyond what
  every list tab already offers.
- "Add at the drop point" and "add at the middle of the view" reuse what the palette already
  does when opened from Modular, so placement, naming and undo behave the same.
- Row heights, header width, fold state, multi-selection and the column's vertical scroll are
  shared with the arrangement (it is the same column; see research R1).
- The arrangement's lane side (clips, automation and take lanes) doesn't come to Modular — only
  the header column (issue out of scope). Automation and take sub-rows that belong to the header
  column show as they do in the arrangement.
- The mixer has no track context menu today, so Show in Modular lands in the arrangement's and
  the session's menus (research R5).
- Shortcuts, FEATURES.md, CHANGELOG and the user docs (Browser and Modular pages, EN + RU) are
  updated with the change.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only; node adds use the existing engine calls
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no new crossings
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
