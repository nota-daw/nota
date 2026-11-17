# Feature Specification: Modular device cards and a searchable cable-drop picker

**Feature Branch**: `feat/modular-device-cards`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "Issue nota-daw/nota#656 — Modular: real device cards with an XS size, and a searchable picker for cable drops. (1) Every Modular node shows the device's real card (the same one Detail draws), with ports and cables around it; every node also has an XS size which is today's compact node (header, ports, parameter rows); the size is switchable from the node and remembered per node; by default a device that has an S card size starts at S. Ports, CV inputs for modulated params, wiring target highlight and saved positions keep working at every size. (2) Replace the cable-drop parameter menus (a flat menu of all params; a submenu per device for cross-track drops) with a picker panel of sensible fixed max size opened at the drop point, with a search field focused on open (type to filter, Enter picks first match, Esc cancels) and a virtualized list so plug-ins with thousands of parameters open instantly; cross-track drops list the target track's devices grouped, searchable across all devices. Keyboard + mouse, all four themes, nota-design style."

Issue: [nota-daw/nota#656](https://github.com/nota-daw/nota/issues/656) (approved). Also covers
[nota-daw/nota#605](https://github.com/nota-daw/nota/issues/605) — "Connect CV by dropping directly onto a
parameter" — which is FR-001a (drop on a parameter links it; anywhere else on the device opens
the chooser).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The work is UI-only: the Modular view (`ModularView`) and the device cards Detail
already draws (`DeviceChainView*`). No engine or C ABI change is expected — the cards,
the parameter lists and CV link creation already exist behind the ABI.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md). Prior Modular specs: `specs/095-*` (browser tab,
header column).

## Clarifications

### Session 2026-11-17

- Q: Is the Modular node size tied to the same device's card size in Detail? → A: No — independent; Modular keeps its own size per node next to its position (FR-014).
- Q: When a cable is dropped right on a control of a card in Modular, link that parameter without the picker? → A: Yes — the control under the pointer highlights during the drag and the drop links it directly; a drop anywhere else on the node opens the picker (FR-001, FR-001a).
- Q: Should dragging a card node's header past a sibling still reorder the chain? → A: Yes — keep Modular's existing drag-to-reorder for card nodes too (US3/AC4, FR-017).
- Q: Do the knob right-click submenus "Modulate from a parameter" / "Modulate from another track" move to the new picker in this change? → A: Yes — the menu items stay, and each opens the same searchable picker to choose the source (FR-002a).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find a parameter fast when dropping a CV cable (Priority: P1)

A user drags a CV cable from an LFO onto a large plug-in (hundreds or thousands of
parameters). Instead of a screen-tall menu, a compact panel opens at the drop point with
the cursor already in a search field. They type "cut", the list narrows to the cutoff
parameters, they press Enter (or click one), and the link is made.

**Why this priority**: today the menu is unusable on big plug-ins (taller than the
screen, no search, slow to open); it blocks modulation of exactly the devices where it
matters most. It is also the smaller, self-contained half of the issue.

**Independent Test**: load a plug-in with ≥1,000 parameters, drop an LFO cable on its
node, type part of a parameter name, press Enter; a link to that parameter exists and
the panel opened without a visible delay.

**Acceptance Scenarios**:

1. **Given** a CV cable is dragged onto a device node, **When** it is released
   anywhere but on a parameter control, **Then** a picker panel opens at the drop point listing that device's parameters,
   with keyboard focus in its search field.
2. **Given** the picker is open, **When** the user types, **Then** the list shows only
   parameters whose name contains the typed text (case-insensitive), updating on every
   keystroke.
3. **Given** a filtered list, **When** the user presses Enter, **Then** the highlighted
   (first, unless moved with the arrow keys) parameter is linked and the panel closes.
4. **Given** the picker is open, **When** the user clicks a parameter, **Then** it is
   linked and the panel closes.
5. **Given** the picker is open, **When** the user presses Esc or clicks outside it,
   **Then** it closes and no link is made.
6. **Given** a device with thousands of parameters, **When** the picker opens,
   **Then** it appears at its normal size (not taller than its fixed maximum) and
   scrolls smoothly through the whole list.

---

### User Story 2 - Pick a parameter on another track from one searchable list (Priority: P1)

A user drops a CV cable on a track in the header column beside the canvas. The same
picker opens, listing every modulatable parameter of that track, grouped under each
device's name (MIDI FX, instrument, effects — in chain order). Search covers all of them
at once; a match shows which device it belongs to.

**Why this priority**: same problem as Story 1, worse — a submenu per device means
hunting through several long menus. Shares the picker with Story 1.

**Independent Test**: on a track with an instrument plug-in and two effects, drop a
cable on its header, type a name that exists on the second effect, press Enter; the
cross-track link targets that effect's parameter.

**Acceptance Scenarios**:

1. **Given** a CV cable is dropped on another track's header, **When** the picker
   opens, **Then** it lists that track's parameters grouped by device in chain order,
   each group headed by the device name.
2. **Given** a search term matches parameters on several devices, **When** the list
   filters, **Then** each match stays under its device's heading (headings with no
   matches are hidden).
3. **Given** the target track has no modulatable parameters, **When** the cable is
   dropped, **Then** the picker shows a short "no modulatable parameters" message and
   makes no link.
4. **Given** a parameter on a node, **When** the user right-clicks it and chooses
   "Modulate from a parameter" or "Modulate from another track", **Then** the same
   picker opens listing the possible sources (grouped by device or by track), and
   picking one creates the same link the old submenu did.

---

### User Story 3 - See and play the real device card inside Modular (Priority: P2)

A user opens Modular on a track with a compressor and a synth. Each node shows the same
card Detail shows (the compressor's meters and graph, the synth's panels), so they can
tweak the device where they patch it. Devices that have a small (S) card show that by
default to keep the graph readable; devices without one show their full (L) card.

**Why this priority**: the bigger change and the main visual upgrade, but patching
works without it; Stories 1–2 fix the blocker first.

**Independent Test**: open Modular on a track with an S-capable effect and an Audio
Effect Rack; the effect node shows its S card, the rack node its full card; turning a knob on the
effect's card changes the same value Detail shows.

**Acceptance Scenarios**:

1. **Given** a device whose card has an S size, **When** its node first appears in
   Modular, **Then** the node shows the device's S card.
2. **Given** a device with no S size (e.g. an Audio Effect Rack), **When** its node
   first appears, **Then** the node shows its L card.
3. **Given** a node showing a card, **When** the user changes a control on it,
   **Then** the device's value changes exactly as it would from Detail (and Detail shows
   the new value).
4. **Given** a node showing a card, **When** the user drags its header, **Then** the
   node moves on the canvas and cables follow it; dragged past a sibling it reorders
   the chain, as Modular nodes always have (the card's own Detail drag handle is not
   there).
5. **Given** a CV cable is dragged over a knob on a node's card, **When** it is
   released on the knob, **Then** that knob's parameter is linked directly, without the
   picker.
6. **Given** a parameter on a node's card is modulated, **When** the node is shown,
   **Then** that parameter still has its CV input port on the node and the cable
   connects to it.

---

### User Story 4 - Collapse a node to XS (Priority: P2)

A user with a crowded graph switches some nodes to XS — today's compact node (header,
ports, a short scrolling list of parameter rows) — and switches others back to their
card. The choice sticks when they reopen the project.

**Why this priority**: keeps big graphs manageable once nodes become cards; it is the
existing node, so little new behaviour.

**Independent Test**: switch a node to XS, save and reopen the project; the node is
still XS, at the same position, with its cables attached.

**Acceptance Scenarios**:

1. **Given** any device node, **When** the user picks XS from the node's size control,
   **Then** the node becomes the compact node that Modular draws today, in place, with
   all cables still attached.
2. **Given** an XS node, **When** the user picks S (if the device has it) or L,
   **Then** the node shows that card.
3. **Given** a node's size was changed, **When** the project is saved and reopened,
   **Then** the node shows the same size.
4. **Given** a node changes size, **When** it grows, **Then** it keeps its top-left
   position, nodes the user has placed are not moved, and the auto-placed rest of the
   chain moves aside so no node overlaps it.

---

### Edge Cases

- A device is removed from the chain while its node is on screen, or while the picker
  for it is open → the node disappears; the open picker closes without linking.
- The picker is opened near a screen edge → it is placed fully on screen (flipped /
  shifted), never clipped.
- A search with no matches → the list shows "No matching parameters"; Enter does nothing.
- Parameters with empty or duplicate names (common in plug-ins) → shown with a fallback
  label (e.g. "Parameter 42") so every entry is distinguishable and pickable.
- A card control that is not a modulatable parameter (a tab, a graph handle, a button,
  a preset picker) → it does not highlight during a cable drag; dropping on it opens
  the picker as for any other spot on the node.
- A parameter that is already the target of this modulator → still listed; picking it
  again does not create a duplicate link (same as today).
- The graph is zoomed far out → cards scale with the canvas like today's nodes; text
  may become unreadable, which is the cue to zoom in or use XS.
- A device's card is XL in Detail (the panel above the chain) → in Modular it shows as L;
  Detail's XL state is unaffected.
- Modulator nodes (LFO, Env, Math, Scope…), In / Out nodes and Global-view islands have
  no Detail card → they keep their current look and have no size control.
- A device opens its own editor window (plug-in) from its card → works from the Modular
  card exactly as from Detail.
- Old projects with no saved Modular size → each node takes the default (S if
  available, else L).

## Requirements *(mandatory)*

### Functional Requirements

**Parameter picker**

- **FR-001**: Dropping a CV cable on a device node — anywhere except on a parameter
  control (FR-001a) — MUST open a picker panel listing that device's modulatable
  parameters, replacing the current menu.
- **FR-001a**: While a CV cable is dragged over a parameter control on a node (a knob or
  value on a card, or a parameter row of an XS node), that control MUST highlight;
  dropping on it MUST link that parameter directly, with no picker.
- **FR-002**: Dropping a CV cable on another track in the header column MUST open the
  same picker listing every modulatable parameter of that track, grouped by device in
  chain order under the device's name, replacing the current per-device submenus.
- **FR-002a**: The right-click items "Modulate from a parameter" and "Modulate from
  another track" on a parameter in Modular MUST open the same picker (search, keyboard,
  virtualized list) listing the possible sources — parameters on this track grouped by
  device, or modulators on other tracks grouped by track — instead of their submenus;
  the created link MUST match what the submenus create today.
- **FR-003**: The picker MUST have a fixed maximum size (it never grows taller than that
  maximum, regardless of parameter count) and MUST appear at the drop point, kept fully
  on screen.
- **FR-004**: The picker MUST contain a search field that has keyboard focus as soon as
  the panel opens; typing MUST filter the list by case-insensitive substring match on
  the parameter name (and, for cross-track, the device name), updating on every keystroke.
- **FR-005**: The first visible parameter MUST be highlighted; Up/Down MUST move the
  highlight (skipping group headings); Enter MUST link the highlighted parameter;
  Esc or a click outside MUST close without linking.
- **FR-006**: Clicking a parameter MUST link it and close the picker.
- **FR-007**: The list MUST only build the rows it shows (virtualized), so opening,
  filtering and scrolling stay immediate for devices with thousands of parameters.
- **FR-008**: Empty states MUST be shown in the panel: no parameters on the target, and
  no matches for the search.
- **FR-009**: The link created MUST be identical to what the current menus create
  (same target kind, device, parameter, same-track vs cross-track ownership).

**Device cards in Modular**

- **FR-010**: Every Modular node that stands for a device in the chain (MIDI effect,
  instrument, audio effect, including plug-ins) MUST be able to show that device's
  Detail card at S (when the device has an S size) and L.
- **FR-011**: Every such node MUST also offer an XS size that is today's compact
  Modular node (header, port block, scrolling parameter rows), unchanged in behaviour.
- **FR-012**: A size control on the node MUST switch between the sizes the device
  offers (XS, S where available, L). XL is not offered in Modular.
- **FR-013**: A node with no saved size MUST start at S when the device has an S size,
  otherwise at L.
- **FR-014**: A node's Modular size MUST be saved with the project's Modular layout and
  restored on reopen, per node. It MUST NOT change the device's card size in Detail,
  and Detail's size MUST NOT change the Modular size.
- **FR-015**: Controls on a card in Modular MUST read and write the device exactly as
  the same card does in Detail (values, automation follow, undo, MIDI learn, the
  plug-in editor window). Preset browsing and saving stay in Detail's card header and
  are not repeated on the Modular node.
- **FR-016**: At every size a node MUST keep its audio / MIDI ports, one CV input port
  per modulated parameter, its CV output (where it has one), the brass outline while a
  cable is dragged over a valid target, and the ability to drop a cable on it.
- **FR-017**: Dragging a card node's header MUST move the node on the canvas (and save
  its position) as today; card gestures that would reorder or move the device in Detail
  (chain drag, fold) MUST NOT act in Modular.
- **FR-018**: Changing a node's size MUST keep its top-left position and keep every
  cable attached; nodes the user has placed MUST NOT move, and auto-placed chain nodes
  re-flow so none overlaps the resized node.
- **FR-019**: Modulator nodes, In / Out nodes and Global-view islands MUST keep their
  current appearance and have no size control.

**Look and feel**

- **FR-020**: The picker and the node size control MUST follow the Nota style guide
  (DESIGN.md / nota-design) and read correctly in all four colour themes.
- **FR-021**: All new user-visible strings MUST be localisable (English and Russian).

### Key Entities

- **Node size (Modular)**: per device node — XS, S or L; stored in the project's
  Modular layout data next to the node's position; independent of the device's Detail
  card size.
- **Picker entry**: a parameter (device, index, display name) or a group heading
  (device name) for cross-track lists; filtered by the search text.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Dropping a cable on a device with 5,000 parameters shows the picker in
  under 150 ms, and each keystroke updates the list in under 50 ms.
- **SC-002**: The picker never exceeds its fixed maximum height (around 420 logical
  pixels) for any device, and is never clipped by the screen edge.
- **SC-003**: A user can link a named parameter on a 1,000-parameter plug-in with the
  keyboard only (type + Enter) in under 5 seconds after the drop.
- **SC-004**: With the default sizes, a track of 6 devices opens in Modular without a
  visible stall (under 300 ms to first draw) and its knobs follow automation at the
  same rate as Detail.
- **SC-005**: Changing a node's size never detaches or misroutes a cable, verified on a
  graph with audio, MIDI and CV links (0 broken links).
- **SC-006**: A saved Modular node size survives save / reopen in 100 % of nodes.

## Assumptions

- The Detail card UI can be hosted inside a Modular node without the chain-specific
  behaviour (reordering, folding, XL); the plan decides how.
- "S size" means the device's Detail card offers S: built-ins with a `View` card-size
  parameter and hosted plug-ins (their compact card); racks and generic-parameter cards
  have L only (see research.md R2).
- Modular size lives in the existing Modular layout sidecar (UI-only data), so Detail
  and the engine are untouched and older projects open with defaults.
- Only Modular's own right-click modulation items move to the picker (FR-002a); other
  context menus (automation, Detail's own card menus) stay as they are.
- No new dependency; the virtualized list uses what the UI framework already offers.
- Nota Remote and the Cardputer / NX remotes do not show Modular and are unaffected.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only; parameter writes and CV links use the existing engine calls
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no ABI change expected
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change, no smoke-test change
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
