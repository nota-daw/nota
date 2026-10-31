# UI Contract: Mixer placement

The user-facing surface this feature exposes. No C ABI or MCP change.

## Commands (`src/managed/Nota.Application/Palette/CommandCatalog.cs`)

| Id | Title | Group | Shortcut | Behaviour |
|---|---|---|---|---|
| `view.mixer` *(exists)* | Show Mixer | View | ⌘⇧M / Ctrl+Shift+M | Toggle the Mixer in the remembered placement |
| `view.mixerFloat` *(new)* | Mixer in its own window | View | — | Toggle placement; moves a shown Mixer, otherwise only flips the preference |

## Menu (`MainWindow.axaml`, View)

- `Mixer` → `view.mixer` (unchanged).
- `Mixer in its own window` → `view.mixerFloat`, checkable, checked when floating.

## Docked panel (main window, under the main view, above Devices / Clip)

Toolbar, left → right: `Mixer` caption · `Strips | Stereo map` segments · spacer ·
pop-out button (tooltip *Open the mixer in a window*) · close button (tooltip *Hide the
mixer*). Body: the `MixerView` (strips or map + footer). Top edge: resize splitter.

## Floating window (`MixerWindow`)

Unchanged, plus a dock button (new `DockIn` glyph — PopOut's arrow turned inward) at the right of the title bar (tooltip *Dock the mixer in
the main window*). Close / Escape = hide.

## Footer section toggles (`MixerView`)

`Show  Sends  I/O` keep their on/off meaning. New state **auto-hidden**: muted look,
tooltip *Hidden: not enough height · click to show anyway*. Click → pinned visible with
vertical scrolling; click again → off.
