# Feature Specification: A Mixer button in the toolbar

**Feature Branch**: `092-toolbar-mixer-toggle`

**Created**: 2026-11-11

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#516: "Show or hide the Mixer with one click while
working. Today it is only reachable from View → Mixer or ⌘⇧M / Ctrl+Shift+M, and neither is
obvious to someone who doesn't know they exist. […] A Mixer toggle button in the toolbar's icon
group, right after the Automation toggle (Metronome · Follow · Snap · Automation · Mixer). It
runs the same `view.mixer` command as View → Mixer and ⌘⇧M […]. It reads as on while the Mixer
is visible, and stays in sync when the Mixer is opened or closed any other way (menu, shortcut,
the window's close button). A new glyph drawn to the style guide (24-unit grid, 1.6 line), with
a tooltip naming the shortcut. Preferences → Shortcuts and the user docs mention the button."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/*`) — the main window's toolbar and the Mixer placement
- [x] UI (Avalonia) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: the toolbar's tool group holds Metronome, Follow, Snap
and Automation as `tp-icon` toggle buttons with a `Glyph` each; on is the soft tint. The Mixer
(specs/054) is one `MixerView` that `MainWindow.Mixer.cs` docks as a bottom panel or moves into
its own window. `view.mixer` (View → Mixer, ⌘⇧M) runs `ToggleMixer`; the panel's close button,
the window's close button and its dock button, and View → Mixer in its own window also show,
hide or move it. On a narrow window the transport bar sheds controls in a fixed order
(`MainWindow.TransportFit.cs`).

## User Scenarios & Testing

### User Story 1 - One click to the Mixer (Priority: P1)

A user who doesn't know the shortcut sees a faders key after Automation, clicks it, and the
Mixer opens where it was last (docked or in its window). Clicking it again hides it.

**Acceptance Scenarios**:

1. **Given** the Mixer hidden, **When** the user clicks the Mixer button, **Then** the Mixer
   shows in its remembered placement and the button reads as on.
2. **Given** the Mixer shown, **When** the user clicks it, **Then** the Mixer hides and the
   button reads as off.
3. **When** the pointer rests on it, **Then** the tooltip names the shortcut (⌘⇧M on macOS,
   Ctrl+Shift+M elsewhere).

### User Story 2 - Always in step (Priority: P1)

**Acceptance Scenarios**:

1. **When** the Mixer is shown or hidden from View → Mixer, ⌘⇧M, the docked panel's close
   button or the Mixer window's close button, **Then** the button follows.
2. **When** the Mixer moves between docked and its own window (pop-out, dock, View → Mixer in
   its own window), **Then** the button stays on.
3. **When** Nota starts with the Mixer remembered as shown, **Then** the button starts on.

### Edge Cases

- **Narrow window**: the button folds away with the other tools, right after Automation; the
  menu and the shortcut stay.
- **No Mixer view yet**: a click changes nothing and the button stays off.

## Requirements

### Functional Requirements

- **FR-001**: The toolbar MUST show a Mixer toggle after Automation, with its own glyph on the
  24-unit grid with the shared 1.6 line.
- **FR-002**: The button MUST run `view.mixer`, the command behind View → Mixer and ⌘⇧M.
- **FR-003**: The button MUST read as on exactly while the Mixer is shown, docked or in its
  window, whatever opened, closed or moved it.
- **FR-004**: The tooltip MUST name the shortcut, spelled for the platform.
- **FR-005**: Preferences → Shortcuts MUST mention the button beside ⌘⇧M.
- **FR-006**: On a narrow window the button MUST fold away right after Automation.

## Success Criteria

- **SC-001**: The Mixer is reachable from the main window in one visible click.

## Assumptions

- The faders glyph is new (the browser rail's Sliders icon stands for audio effects): three
  fader slots with solid caps at different levels.
- The user docs (nota-docs) are a separate repository; FEATURES.md, the Shortcuts pane and
  DESIGN.md's tool list are updated here.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header (no new source files)
- [x] No new dependency
- [x] Audio-thread impact: none — UI only
- [x] No new managed/native crossings
- [x] Smoke-test impact: none new; the localization, icon and design checks cover the new
  texts, the glyph and the toolbar markup
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (tool icon button, soft tint when on)
