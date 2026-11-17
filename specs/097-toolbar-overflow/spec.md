# Feature Specification: Toolbar overflow

**Feature Branch**: `feat/toolbar-overflow`

**Created**: 2026-11-13

**Status**: Draft

**Input**: Issue [#520](https://github.com/nota-daw/nota/issues/520) (approved) "Add Expandable
Toolbar Buttons and Overflow Menu": toolbar controls that don't fit must stay reachable
through an overflow (⋯) button that expands the toolbar, and come back when there is room;
the overflow area also hosts a few less-frequent quick functions (the issue's example:
switching the interface scale between 100 % and 125 %). User-customised overflow content
and expandable individual buttons (metronome options, #404) are out of scope.

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

**Where things stand today**: on a narrow window, or at a large interface scale, the
transport bar sheds its least-needed controls in a fixed order (#303): the CPU bar (the DSP
number stays), the Remote label, Tap, Key, Remote, Follow, Automation, Mixer (#516), the time signature,
MIDI learn, Grid, Snap, loop-record mode, loop, the master strip, and last the DSP meter. A
shed control is simply hidden. Some have a menu entry or a shortcut, but Tap's beat dots,
the project-key picker, the time-signature fields and the master level have no other place
in the main window at all. Stop / play / record, the position, the tempo, the metronome, the
view switch and the Menu key (#518) never shed. The interface scale (75 / 100 / 125 / 150 /
200 %) is changed only in Settings → Appearance.

## Clarifications

### Session 2026-11-13

- Q: How does the ⋯ button show what doesn't fit — a second toolbar row that pushes the workspace down, a row floating over it, or a drop-down menu? → A: An ordinary drop-down menu, like the rest of Nota's menus.
- Q: When the overflow is open and the user clicks in the workspace, what does that click do? → A: As with any Nota menu: the menu closes on the click outside it, on Escape and when an item is chosen; the click outside only closes the menu.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nothing on the toolbar becomes unreachable (Priority: P1)

A musician works on a laptop screen at 125 % interface scale. The transport bar no longer
has room for every control. Instead of Grid, Snap, Key and the master strip silently
vanishing, an overflow (⋯) button sits at the end of the bar. Clicking it opens a menu with
an entry for each control that didn't fit — Snap as a check item, Grid and Key as
submenus, the master level adjustable in place — each doing what the control does in the
bar. When the window is widened, the controls go back to the bar and their entries leave
the menu.

**Why this priority**: It is the core of #520 — today some functions are reachable only by
shortcut, or not at all, on a small screen.

**Independent Test**: Narrow the main window step by step; at every width open the overflow
menu and use the entry of each shed control; widen again and check the entries go as the
controls return.

**Acceptance Scenarios**:

1. **Given** a window too narrow for Snap and Grid, **When** the user opens the overflow
   menu, **Then** it lists Snap and Grid, in the bar's left-to-right order, and nothing
   that is still in the bar.
2. **Given** Snap is shed, **When** the user chooses Snap in the menu, **Then** snapping
   toggles exactly as from the bar and the item shows the new state next time.
3. **Given** Grid or Key is shed, **When** the user opens its submenu, **Then** it offers
   the same choices as the control's own menu in the bar, with the current one marked.
4. **Given** the master strip is shed, **When** the user opens the menu, **Then** the master
   level can be read and adjusted from it.
5. **Given** the time signature is shed, **When** the user opens its submenu, **Then** the
   common signatures are offered with the current one marked.
6. **Given** Tap is shed, **When** the user taps its entry repeatedly, **Then** the menu
   stays open and each tap counts toward the tempo as on the bar's Tap button.
7. **Given** the DSP meter is shed, **When** the user opens the menu, **Then** it shows the
   current DSP load (read-only), updating while the menu is open.
8. **Given** the bar is wide enough for everything, **When** the user looks at the bar,
   **Then** it looks as it does today apart from the overflow button.

---

### User Story 2 - The overflow menu behaves like every Nota menu (Priority: P1)

The overflow is a temporary menu: the user opens it, picks something, and it goes.

**Why this priority**: A predictable way in and out keeps it from getting in the way.

**Independent Test**: Open the menu, close it with each route, check the workspace below is
unaffected by the closing click.

**Acceptance Scenarios**:

1. **Given** the menu is closed, **When** the user clicks the overflow button, **Then** the
   menu opens under it.
2. **Given** the menu is open, **When** the user chooses an item, presses Escape, clicks
   the overflow button again or clicks outside the menu, **Then** the menu closes; a click
   outside does nothing else.
3. **Given** keyboard navigation, **When** the user tabs to the overflow button and presses
   Space / Return, **Then** the menu opens with keyboard focus on its first item.

---

### User Story 3 - Quick functions in the overflow menu (Priority: P2)

The overflow menu also carries a small, fixed group of quick functions that would otherwise
need a trip to Settings — first the interface scale, with its presets one submenu away. A
user who flips between 100 % and 125 % often does it from the toolbar.

**Why this priority**: The issue's second purpose; useful, but not needed to fix the
disappearing controls.

**Independent Test**: With a wide window, open the menu, change the interface scale there,
check Settings → Appearance shows the same value and it is kept after a restart.

**Acceptance Scenarios**:

1. **Given** any window width, **When** the user opens the menu, **Then** the quick
   functions are listed after the shed controls' entries, separated from them.
2. **Given** the scale is 100 %, **When** the user picks 125 % in the menu, **Then** the
   interface rescales at once, Settings → Appearance shows 125 % and the value persists.

---

### Edge Cases

- The window is resized while the menu is open: the menu closes (or keeps showing what was
  shed when it opened); the next open reflects the new fit. It never lists a control that
  is in the bar.
- Purely informational parts shed early (the CPU bar beside the DSP number, the Remote
  label beside its glyph and count) leave their information visible in the bar and get no
  menu entry.
- When the whole Remote panel is shed, the menu offers MIDI learn (checked when on) and
  Connect Phone (with the number of connected phones).
- The Re-enable automation chip appears while Automation is shed: Automation's entry in the
  menu offers the re-enable action too.
- The interface scale changes so much that even the never-shed controls barely fit: the
  overflow button still fits and is never shed.
- Menu row hidden on Windows / Linux (#518): the Menu key and the overflow button are both
  present and never shed.
- Language change makes labels wider or narrower: the fit is redone at once.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every control the transport bar sheds for lack of room MUST remain usable
  through the overflow menu; no control may be unreachable at any window width the app
  allows and at any interface-scale preset.
- **FR-002**: The overflow menu MUST list an entry for each shed control, in bar order, and
  none for controls still in the bar. Each entry MUST do what the control does:
  - toggles (Follow, Automation, Mixer, Snap, loop, loop-record mode, MIDI learn) as check items
    showing their state;
  - Grid, Key and the time signature as submenus offering the control's choices with the
    current one marked (time signature: the common signatures);
  - Tap as an item that stays open on click so it can be tapped repeatedly;
  - Remote as Connect Phone, with the phone count;
  - the master level readable and adjustable in the menu;
  - the DSP meter as a read-only line with the current load.
  Informational parts whose information stays in the bar (CPU bar, Remote label) need no
  entry.
- **FR-003**: The shed order and the never-shed set MUST stay as today (#303, #518); the
  overflow button itself MUST never shed.
- **FR-004**: When room returns, controls MUST move back into the bar in reverse shed order
  and their entries MUST leave the menu.
- **FR-005**: The overflow menu MUST behave like Nota's other menus: it closes when an item
  is chosen (except Tap), on Escape, on a second click of the button and on a click outside,
  and a click outside only closes it.
- **FR-006**: The menu MUST hold a fixed quick-functions group after the shed controls'
  entries, containing an interface-scale submenu with all presets and the current one
  marked; a change there MUST be the same setting Settings → Appearance shows, and MUST
  persist.
- **FR-007**: Because of the quick functions the overflow button MUST be shown at every
  width; it MUST indicate when controls are currently shed into the menu.
- **FR-008**: The overflow button and the menu MUST be keyboard-reachable, with tooltips,
  accessible names and item labels in every supported language; an entry's shortcut, when
  the control has one, is shown beside it as in other menus.
- **FR-009**: The button and menu MUST follow the Studio design language in all four themes
  and the classic look, and scale with the interface scale.
- **FR-010**: Opening the menu, using it and re-fitting the bar MUST NOT interrupt playback
  or recording.

### Key Entities

- **Toolbar control**: a part of the transport bar with a place in the shed order (or marked
  never-shed); while shed, it is represented by an entry in the overflow menu.
- **Quick function**: an entry always present in the overflow menu, independent of width; the
  interface scale is the first.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At every window width from the app's minimum up and at every interface-scale
  preset, 100 % of the transport bar's controls are usable in at most three clicks.
- **SC-002**: Switching the interface scale from the toolbar takes three clicks (button,
  Interface scale, preset), against four or more through Settings today.
- **SC-003**: Checked at every shed step, the menu lists exactly the shed controls — none
  missing, none duplicated with the bar.
- **SC-004**: The menu opens within a single frame of the click, with no audio dropout
  during playback.

## Assumptions

- The overflow button sits at the right end of the bar, after the master strip, and uses a
  three-dot glyph.
- The quick-functions group is fixed; user customisation and expandable individual buttons
  (#404) are out of scope.
- The phrase-lines toggle (#517) is not built yet; when it joins the bar it takes part in
  the shed order and so in the overflow menu, as the Mixer key (#516) already does.
- Only the main window's transport bar is covered; the Session toolbar and the editors'
  toolbars keep their current behaviour.
- Time signatures outside the common list stay settable from the bar's own fields at a
  wider window or from the project; the submenu does not need a free-entry field.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI-only change
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
