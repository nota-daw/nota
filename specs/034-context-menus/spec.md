# Feature Specification: Studio context menus

**Feature Branch**: `034-context-menus`

**Created**: 2026-10-21

**Status**: Draft

**Input**: User description: "Redesign the track context menu (right-click on a track header in Arrangement) to match the Studio design language. Layout, top to bottom: (1) a header row with the track's colour chip, the track name (600 weight) and a muted mono badge with the track type and number ("Audio · 3"); (2) an inline colour row of the track colours, the chosen one ringed — replaces the "Color" submenu; (3) an inline "Height" row with segmented keys, the chosen one accent — replaces the "Track height" submenu; rule; (4) Rename… and Record input (submenu, current input shown muted on the right, e.g. "In 1–2"); rule; (5) Freeze track and Live freeze (with an 18×10 switch on the right, accent while on); rule; (6) Add track (submenu, then a "Recent" group of recently added instruments) and Group tracks ⌘G; rule; (7) Copy ⌘C, Cut ⌘X, Paste ⌘V (disabled when the clipboard is empty), Duplicate ⌘D; rule; (8) Delete track ⌫ in Danger ink. All icons neutral muted ink via Glyph (no blue freeze icons), small submenu carets inset from the edge, shortcuts in Geist Mono written the macOS way (⌘G, not ⌘+G). Menu 248 wide, items 24 tall radius 2, raised surface, seam ring, rules between groups, hover = full accent fill with derived ink. Update DESIGN.md § Menus for the new in-menu controls (swatch row, segments, switch) and the Danger destructive item." — extended by the user mid-specification: "Additionally: update all context menus in the program."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The menus live in `src/managed/Nota.App`: about 72 `MenuFlyout` / `ContextMenu` sites in
44 files (Arrangement track / clip / lane / section / automation menus, Session grid and
toolbar, Modular, Browser, History, device cards and racks, macro panel, graph views,
preview player, snap chip, the main window). Shared pieces: `MenuKit.cs` (items, icons,
gestures) and the menu styles in `Theme/NotaTheme.axaml`. No engine change is expected.
A design mockup of the track menu was made in the conversation that started this spec.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-21

- Q: Live freeze is an action today (renders a frozen copy and links it to the source). Should it become an on/off switch? → A: Yes — a switch: on = the track is in a live-freeze link; switching it off unfreezes (wakes the source); switching it on runs Live freeze.
- Q: Should "Add track" list recently added instruments under a "Recent" heading, with a new persisted list? → A: Yes — add a "Recent" group backed by a persisted list.
- Q: Does the track menu's colour row show all 27 palette colours (9 bases × 3 shades) as a 9 × 3 grid, or only the 9 bases? → A: A 9 × 3 grid of all 27 colours plus "Auto", the current colour ringed.
- Q: Does the object header row appear in every menu for a named object (clip, device, rack chain, scene, Session slot, return) or only in track menus? → A: In every menu for a named object, device menus included.
- Q: Is "Recent" only recent instruments in "Add track", or also recent effects in add-device menus? → A: Instruments in "Add track" only; effects get no recent list.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every context menu looks and reads the same (Priority: P1)

A producer right-clicks around the app — a track header, a clip, a device card header,
a Session slot, a browser item, a node in Modular. Every menu has the same frame, the
same item height and spacing, the same neutral icons in one column, shortcuts set the
same way on the right (`⌘G`, `⇧⌘G` on macOS; `Ctrl+G` elsewhere), small submenu carets
set in from the edge, rules between groups, and destructive commands (Delete, Remove,
Clear…) in red ink at the end of the menu.

**Why this priority**: This is the user's main complaint ("too simple, doesn't match
the program's design") and it touches every menu, so it gives the most value and lays
the foundation the other stories build on.

**Independent Test**: Open each context menu listed in the inventory (see FR-014) and
compare it against the menu rules in `DESIGN.md`; every menu passes the same checks
without any of the inline controls from Story 2.

**Acceptance Scenarios**:

1. **Given** any context menu, **When** it opens, **Then** its items are 24 px tall, the icon column, label column, shortcut column and submenu caret line up across all items, and every icon is drawn in the muted neutral ink (no coloured icons at rest).
2. **Given** a menu item with a shortcut on macOS, **When** the menu opens, **Then** the shortcut is written with the macOS symbols without separators (`⌘G`, `⇧⌘V`, `⌫`) in the monospaced face; **and** on Windows / Linux the same item reads `Ctrl+G`, `Ctrl+Shift+V`, `Del`.
3. **Given** a menu item that opens a submenu, **When** the menu opens, **Then** the caret is a small glyph set in from the menu's right edge, the same size as the shortcut text, never touching the edge.
4. **Given** a menu that contains a destructive command, **When** it opens, **Then** that command sits in the last group, its label and icon are drawn in the danger ink at rest, and on hover it takes the full accent fill with derived ink like every other item.
5. **Given** a disabled item (e.g. Paste with an empty clipboard), **When** the menu opens, **Then** the label, icon and shortcut all take the disabled ink step, and hovering it shows no fill.
6. **Given** any theme (Studio Dark, Studio Light, Ember Graphite, Ember Paper), **When** a menu opens, **Then** it follows the theme, and switching theme while the app runs restyles the next menu opened.

---

### User Story 2 - Change a track's colour and height in one click (Priority: P1)

A user right-clicks a track header to recolour it. The menu opens with the track's name
and type at the top, then a row of colour swatches — the current colour ringed — and a
row of height keys. One click on a swatch recolours the track; one click on a height key
resizes it. No submenus to travel through.

**Why this priority**: Colour and height are the most frequent track-menu edits, and
today each needs two levels of submenu (colour needs three). This is the centrepiece of
the requested mockup.

**Independent Test**: Right-click a track, click a swatch, confirm the track's colour
changes and the menu closes; reopen, click a height key, confirm the track's height
changes.

**Acceptance Scenarios**:

1. **Given** a track, **When** the user right-clicks its header, **Then** the menu opens with a header row showing the track's colour chip, its name and a muted badge with its type and position (e.g. "Audio · 3"); the header row is not clickable and never highlights.
2. **Given** the track menu is open, **When** the user looks at the colour row, **Then** every track colour of the palette (all bases and their light / dark shades) is shown as a swatch, plus an "Auto" choice, and the track's current colour is ringed.
3. **Given** the track menu is open, **When** the user clicks a swatch, **Then** the track takes that colour, the change is one undo step, and the menu closes.
4. **Given** the track menu is open, **When** the user looks at the height row, **Then** it shows the Compact, Standard and Large presets as segmented keys, the key matching the track's height in the accent; when the track has a custom (dragged) height no key is chosen.
5. **Given** the track menu is open, **When** the user clicks a height key, **Then** the track takes that height and the menu closes; **When** the user Option/Alt-clicks a height key, **Then** every track takes that height.
6. **Given** several tracks are selected, **When** the user right-clicks one of them, **Then** the header row reads the count ("3 tracks"), and a swatch or height key applies to all of them as one undo step.
7. **Given** the footer menu of a return or the master track, **When** it opens, **Then** it shows the same header row and colour row.

---

### User Story 3 - See the current state without opening submenus (Priority: P2)

A user wants to know where an audio track records from and whether it is frozen. The
track menu shows the current record input on the right of "Record input" (e.g.
"In 1–2", "Master", "Bass"), the current MIDI source on the right of "MIDI from" for an
instrument track, and the freeze state of the track.

**Why this priority**: Showing state in place removes a round trip into the submenu,
but the menu stays usable without it.

**Independent Test**: Set a track's record input to another track, open the menu, and
read that track's name on the right of "Record input" without opening the submenu.

**Acceptance Scenarios**:

1. **Given** an audio track recording from the hardware input, **When** its menu opens, **Then** "Record input" shows the input in muted ink on the right, before the caret.
2. **Given** an instrument track that takes MIDI from another track, **When** its menu opens, **Then** "MIDI from" shows that track's name on the right; with no source it shows "None".
3. **Given** a live-frozen track (either side of the link), **When** its menu opens, **Then** the "Live freeze" switch is on (accent), with "Edit source" and "Flatten (remove source)" below it; **When** the user clicks the switch, **Then** the link unfreezes and the source wakes.
4. **Given** a live track that can be live-frozen, **When** the user clicks the off "Live freeze" switch, **Then** the track is live-frozen exactly as the current command does.

---

### User Story 5 - Add a recently used instrument from the menu (Priority: P3)

A user who keeps adding Nota Volt tracks right-clicks a track, opens "Add track", and
picks Nota Volt from the "Recent" group; an instrument track with Volt lands right after
the clicked track.

**Why this priority**: A convenience on top of the redesign; the menu works without it.

**Independent Test**: Add two different instruments, restart the app, open "Add track"
and see both under "Recent", most recent first; click one and get a new track with it.

**Acceptance Scenarios**:

1. **Given** the user has added instruments before, **When** "Add track" opens, **Then** "Recent" lists up to 5 of them, most recent first, without duplicates.
2. **Given** a recent entry, **When** the user clicks it, **Then** a new instrument track with that instrument is added after the clicked track, as one undo step.
3. **Given** a fresh install, **When** "Add track" opens, **Then** no "Recent" heading is shown.
4. **Given** a recent plug-in that has since been uninstalled, **When** "Add track" opens, **Then** it is not listed.

---

### User Story 4 - Menus are ordered the same way everywhere (Priority: P2)

Whatever the user right-clicks, commands come in the same order of groups: object
header (where the object has a name) → inline properties → naming and settings →
object-specific actions → create / group → clipboard (Copy, Cut, Paste, Duplicate) →
destructive. Labels are short because the header names the object ("Copy", not
"Copy track").

**Why this priority**: Consistent order builds muscle memory across views, but each
menu is usable before the reorder.

**Independent Test**: Open the clip, device, Session slot and track menus and confirm
the clipboard group and the destructive group sit in the same relative places with the
same labels and shortcuts.

**Acceptance Scenarios**:

1. **Given** any menu with clipboard commands, **When** it opens, **Then** they appear in the order Copy, Cut, Paste, Duplicate, with the same shortcuts in every menu.
2. **Given** a menu for a named object (track, clip, device, scene, chain, return), **When** it opens, **Then** it starts with the header row naming the object, and its item labels do not repeat the object's noun.
3. **Given** a menu for something without a name (empty arrangement area, a graph background, a snap chip), **When** it opens, **Then** it has no header row.

---

### Edge Cases

- A track with a very long name: the header row truncates the name with an ellipsis and keeps the type badge whole.
- A track with a colour index outside the palette (older project) or "Auto": no swatch is ringed except "Auto" when the colour is automatic.
- A group track, a return or the master: rows that do not apply (record input, freeze, height for the master footer) are left out rather than shown disabled.
- A menu opened near the screen edge: the menu and its submenus stay fully on screen; the inline rows never get clipped.
- Keyboard use: arrow keys move through the items; the colour and height rows can be reached and operated with the keyboard (arrows within the row, Enter to apply), and Escape closes the menu.
- A destructive item that is disabled (e.g. Delete on the master): it takes the disabled ink, not the danger ink.
- A menu on a device card inside a narrow layout still uses the shared menu width rules; long submenu lists (record inputs with many tracks) scroll rather than run off-screen.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every context menu in the app MUST use the shared menu frame: raised surface, 1 px seam ring, radius 3, 4 px padding, no blurred shadow, and a minimum width of 200 px (248 px for the track menu with inline rows).
- **FR-002**: Every menu item MUST be 24 px tall with radius 2, a fixed icon column (icon drawn with the shared glyph set in the muted ink at rest), a label column, a right-aligned shortcut or value column, and a caret column for submenus; columns MUST line up across all items of one menu whether or not an item has an icon.
- **FR-003**: Hover and keyboard highlight MUST be a full accent fill with derived ink on label, icon, shortcut, value and caret, with the standard 120 ms colour transition; disabled items MUST take the disabled ink and never highlight.
- **FR-004**: Shortcuts in menus MUST be set in the monospaced face and spelled per platform: macOS symbols without separators (`⌘G`, `⇧⌘G`, `⌫`); `Ctrl+G` style on Windows / Linux. A menu MUST only show a shortcut that actually performs the same command.
- **FR-005**: Submenu carets MUST be a small glyph from the shared icon set, inset from the right edge, in the muted ink (derived ink while highlighted).
- **FR-006**: Destructive commands (delete, remove, clear, discard, flatten that removes a source) MUST sit in the last group of their menu and be drawn in the danger ink at rest.
- **FR-007**: Menus for a named object (track, multi-track selection, clip, multi-clip selection, device — including the device card header menu, rack chain, macro, scene, Session slot with a clip, return / master, browser item, history version) MUST start with a non-interactive header row: the object's colour chip where it has one, its name in the heading weight, and a muted monospaced badge with its type and position or count.
- **FR-008**: The track menu, the multi-track menu and the return / master footer menu MUST replace the "Color" submenu with an inline colour grid of 9 columns (bases) × 3 rows (base, light, dark shade) — all 27 palette colours — plus an "Auto" choice beside it, the current colour ringed; clicking applies the colour to every target track as one undo step and closes the menu.
- **FR-009**: The track and multi-track menus MUST replace the "Track height" submenu with an inline row of segmented keys for the Compact, Standard and Large presets; the chosen key in the accent; click applies to the target tracks, Option/Alt-click to all tracks; the "All tracks: …" items go away.
- **FR-009a**: "Live freeze" MUST be an item with an 18 × 10 switch on the right, shown on both sides of a link (source and frozen copy): on (accent) while the track is in a live-freeze link, off otherwise. Clicking it while off runs Live freeze (same render and progress as today); clicking it while on unfreezes the link (wakes the source, same as today's "Unfreeze (wake source)"). While linked, "Edit source" (or "Done — re-freeze" / "Discard edits" mid-edit) and "Flatten (remove source)" stay as items under the switch; Flatten counts as destructive (FR-006) only in its ink, not its position. On a track that cannot be live-frozen (return, group, master, a track frozen in place) the switch row is left out.
- **FR-010**: Items that open a choice submenu with a current value (Record input, MIDI from, and similar elsewhere) MUST show the current value in muted ink to the left of the caret.
- **FR-011**: Every context-menu icon MUST be neutral at rest; the coloured freeze / live-freeze icons and any other tinted menu icons go away (state colour is shown by the inline controls or the header row, not by icons).
- **FR-012**: The "Add track" submenu MUST list the track kinds the app can add (instrument, audio, return) with short labels ("Instrument", "Audio", "Return"), followed by a "Recent" heading listing up to 5 recently added instruments (built-in and plug-in), most recent first; clicking one adds an instrument track with that instrument at the same place a plain "Instrument" would land. With no history the "Recent" group is left out.
- **FR-012a**: The recent-instruments list MUST be persisted in the app's settings (not in the project), survive restarts, be updated whenever an instrument is added to a track by any route (menu, browser drag, double-click, MCP tool), hold no duplicates (re-adding moves an entry to the top), and drop entries whose plug-in is no longer installed.
- **FR-013**: Within each menu the groups MUST follow the order: header → inline properties → naming and settings → object-specific actions → create / group → clipboard (Copy, Cut, Paste, Duplicate) → destructive; labels MUST NOT repeat the object noun already named by the header, and MUST stay sentence case.
- **FR-014**: The feature MUST cover every context menu in the app; the plan MUST include an inventory of all of them (file, trigger, purpose) and each one MUST be checked off against FR-001 – FR-013.
- **FR-015**: New in-menu controls (header row, colour row, segmented row, value column, danger item) MUST be shared building blocks, not built per menu, and MUST follow the theme live (no colour snapshots).
- **FR-016**: All existing commands, their behaviour, undo grouping and enable/disable rules MUST stay as they are; this feature changes presentation, order and labels only — plus the Option/Alt-click on height keys, the Live freeze switch (FR-009a) and the recent-instruments list (FR-012, FR-012a).
- **FR-017**: The inline rows MUST be operable with the keyboard: arrow keys move between swatches / keys, Enter applies, Escape closes the menu.
- **FR-018**: `DESIGN.md` § Menus MUST describe the header row, the inline colour and segmented rows, the value column, the danger item and the group order, and `DESIGN.html` MUST show a specimen menu.

### Key Entities

- **Context menu**: a floating list opened by right-click on an object or area; has an optional header, groups of items divided by rules.
- **Menu item**: icon, label, optional shortcut or current value, optional submenu caret; states rest / highlight / disabled; optional destructive role.
- **Header row**: the object's colour chip, name and type / position badge; not interactive.
- **Inline property row**: a colour row (swatches + Auto) or a segmented row (presets) that applies a value with one click.
- **Recent instruments**: an ordered, de-duplicated list of up to 5 instrument identities (built-in kind or plug-in id and name), stored in app settings, most recent first.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of the app's context menus (the full inventory) pass the menu checklist in `DESIGN.md` § Menus in all four themes.
- **SC-002**: Changing a track's colour takes one right-click and one click (down from three levels of menu travel today); changing its height takes one right-click and one click (down from two levels).
- **SC-003**: No context-menu icon is coloured at rest, and no shortcut in any menu is written with a "+" separator on macOS.
- **SC-004**: The clipboard group (Copy, Cut, Paste, Duplicate) appears in the same order with the same shortcuts in every menu that offers it.
- **SC-005**: All existing smoke tests, including the design-token, label and icon guards, pass; no command that was reachable from a menu before becomes unreachable.

## Assumptions

- Height presets: the app has three presets (Compact, Standard, Large) and continuous drag; the mockup's "S · M · L · XL" maps to these three — no fourth size is added.
- "Add track" kinds follow the app (Instrument, Audio, Return); the mockup's "MIDI" label is read as the instrument track.
- "Added an instrument" means the user chose one: the browser (drop, double-click, palette), "Add track ▸ Instrument / Recent", an instrument preset, an instrument swap, and the MCP add / set-instrument tools. Tracks Nota creates as a side effect — Convert to MIDI, MIDI-file import, the AI assistant's helper tracks, the demo project — do not enter the recent list.
- Recent devices are tracked for instruments only; audio and MIDI effects have no recent list and add-device menus keep their current content (restyled only).
- The Windows / Linux shortcut spelling stays `Ctrl+…`; only macOS spelling changes.
- The native macOS menu bar and the Windows / Linux menu bar are out of scope; only context menus and drop-down menus drawn by the app are covered.
- Drop-down pickers that are not menus (combo boxes, the preset picker) are out of scope unless they are built as menus.
- The work happens on a feature branch off `develop`; the current branch (`fix/pr212-review`) is unrelated.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only, no engine change
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (no engine change; design guards may gain a menu check)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
