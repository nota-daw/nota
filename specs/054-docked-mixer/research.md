# Research: Docked Mixer

Phase 0 for [plan.md](plan.md). Each entry: Decision / Rationale / Alternatives considered.
Findings are from the current code on `develop` (0.58.1).

## What exists today

- `MixerView` (`src/managed/Nota.App/MixerView.cs`) is created **once** in
  `MainWindow.axaml.cs` (`_mixer = new MixerView(...)`, `IsVisible = false`) and hosted on
  demand. `ToggleMixerWindow()` (`MainWindow.Mixer.cs`) builds a `MixerWindow`, reparents
  the view into `win.Host` through `SetHost` (`MainWindow.DetailWindow.cs`), sets
  `IsVisible = true` so the UI tick (`MainWindow.axaml.cs:304`) pushes meters, and on close
  sets it back to `false`.
- Strip layout (top → bottom): header 24 · I/O row 34 · sends row ~42 · pan 38 · fader
  row with a fixed **190 px** fader · dB read-out · M/S/Arm 28. Footer 44: `Show` Sends /
  I/O toggles (manual, session-only), Pan law group, crossfader placeholder. Full height ≈
  460 px — the window's default height.
- The bottom Devices / Pattern / Clip panel lives in `BodyGrid` row 2 (`RowDefinitions`
  `*` / `Auto` (splitter) / `0`), resized by `DetailSplitter`; its row is referenced as
  `BodyGrid.RowDefinitions[2]` in `MainWindow.Detail.cs`, `MainWindow.DetailWindow.cs` and
  `MainWindow.axaml.cs`. Its Clip-tab height is kept in memory (`_detailHeight`), not
  persisted. It pops out to `DetailWindow` and docks back on close (`DockRequested`).
- `Settings.MixerView` ("strips" / "map") is the only persisted mixer preference.
- The main window already routes transport keys in the tunnel phase and owns
  `LearnOverlay` (MIDI learn) and `PaletteLayer`; `SnapshotContext` maps a `MixerWindow`
  source to `PaletteOrigin.Mixer`.

## R1 — One `MixerView`, three hosts

**Decision**: Keep the single `MixerView` instance and move it between three states:
hidden (no parent), docked (`MixerHost` in the new main-window panel), floating
(`MixerWindow.Host`) with the existing `SetHost` + `FlushOtherWindows` helpers. A small
placement controller in `MainWindow.Mixer.cs` owns the transitions
(`ShowMixer`, `HideMixer`, `ToggleMixer`, `SetMixerFloating`).

**Rationale**: FR-008 (state carries over, no rebuild delay) comes for free: meters,
scroll offset, Stereo map, MIDI-learn bindings and the pan-law cache live in the
instance. The same pattern already works for `DetailWindow` (#164 layout-queue fix
included).

**Alternatives**: two `MixerView` instances (docked + window) — double meter cost, state
divergence, two refresh paths; rejected.

## R2 — Bottom-zone layout: one function owns `BodyGrid` rows

**Decision**: Replace the fixed three-row `BodyGrid` with rows built by one method,
`LayoutBottomPanels()`, from the visible set: `[main *] [mixer splitter, mixer]? [detail
splitter, detail]?`. Each splitter is a `GridSplitter` (`ResizeDirection=Rows`) on the
seam, as today. Code that touched `RowDefinitions[2]` goes through `DetailRow` /
`MixerRow` accessors that return the current row (or a detached dummy when hidden).
Heights are read from the rows' `ActualHeight` on `DragCompleted` (the #53 lesson: store
the row, not the panel).

**Rationale**: A GridSplitter resizes its neighbouring rows; with a fixed row set a
hidden Mixer would leave a 0-height row next to the Devices splitter and break its drag.
Rebuilding rows for the visible set keeps both splitters correct in every combination
(FR-004): Mixer splitter trades space with the main view; the Devices splitter trades
with whatever sits above it (the Mixer when shown — the natural "boundary between two
panels" behaviour).

**Revised in review (#388)**: between two pixel rows a GridSplitter trades height between
them, so with both panels shown the Detail splitter resized the Mixer. While the Mixer is
docked the Detail panel is resized from its own edge (`DetailEdge`), which changes only the
Detail row and takes room from the main view; the GridSplitter stays for the undocked case.
The Detail row also follows the panel's visibility, and keeps a wanted height that a short
window can shrink and a taller one restores.

**Alternatives**: (a) a fixed five-row grid — breaks the Devices splitter when the Mixer
is hidden; (b) a custom drag handle that resizes only its own panel — re-implements the
splitter and the #53 handling; (c) nesting the Mixer inside `DetailPanel` — that is the
rejected option B from clarification.

## R3 — Fitting into short windows

**Decision**: On `BodyGrid` size changes and after each `LayoutBottomPanels()`, clamp in
order: the main view keeps `MinHeight` 120; the Mixer shrinks to its minimum first, then
the Devices panel to its minimum (FR-005, edge case). Remembered heights are not
overwritten by a clamp — only a user drag writes them. (Changed during implementation: the
Devices tab has a fixed card height and can't be resized, so shrinking it clips cards, while
the Mixer's strips adapt to any height down to their core.)

**Rationale**: pixel rows do not shrink by themselves; without a clamp a short window
pushes the main view to zero.

## R4 — Height-adaptive strips (FR-014)

**Decision**: `MixerView` picks a layout tier from the height its strip area actually
gets (`_main` `SizeChanged` / arrange), not from who hosts it, so docked and floating
behave the same:

1. everything (I/O, sends, 190 px fader);
2. hide I/O rows;
3. hide sends rows;
4. shrink the fader from 190 px down to a minimum (≈ 72 px), meters with it.

The core (header, pan, fader at minimum, dB, M/S/Arm) plus footer defines
`MixerView.MinContentHeight`; the docked panel's row minimum and `MixerWindow.MinHeight`
are derived from it (scaled by `NotaUiScale`), never hard-coded. The footer's existing
`Show` Sends / I/O toggles keep their meaning ("show this when it fits"). When a section
is auto-hidden its toggle shows a muted state with the tooltip *Hidden: not enough
height · click to show anyway*; clicking pins it visible and turns on vertical scrolling
of the strip row (`VerticalScrollBarVisibility.Auto`), which is how a collapsed section
stays reachable. Clicking again turns it off. Tier changes only toggle `IsVisible` /
`Height` on existing controls — no `Refresh()` rebuild.

**Rationale**: matches the clarification (adaptive, core always visible, collapsed
sections reachable) and reuses the controls that already hide these sections. Sends stay
editable elsewhere too (Devices panel Sends card, Session mixer sections).

**Alternatives**: per-strip expanders — 2× controls per strip and noisy at 30+ tracks;
a strip context-menu entry — less discoverable than the existing footer toggles.

**Stereo map**: `StereoMapView` already stretches; check it at the minimum height and give
it a `MinHeight` that fits inside `MinContentHeight` if it clips (task, not a design
change).

## R5 — Persistence (FR-009)

**Decision**: Three new `Settings` properties next to `MixerView`:
`MixerFloating` (bool, default `false` → docked), `MixerShown` (bool, default `false`),
`MixerDockHeight` (double, default `0` → natural height = full strips, clamped). Written
with `settings.Save()` on every user show/hide, placement switch and finished splitter
drag. App shutdown closes `MixerWindow` through the normal path, so the close handler
must skip writing `MixerShown = false` when `_shuttingDown` is set. Restore once, in the
main window's `Opened` (after the welcome-screen flow), because a floating window needs a
shown owner.

**Rationale**: per-user, not per-project (clarification); JSON settings already
round-trip new properties with defaults.

## R6 — Keys, palette, MIDI learn in the docked panel

**Decision**:
- Global keys need nothing new: the docked Mixer is inside `MainWindow`, whose tunnel
  handler already routes transport keys and whose `OnKeyDown` handles the rest.
- Escape: the docked Mixer adds no Escape handler (FR edge case); `MixerWindow` keeps
  Escape-to-close.
- Palette: `SnapshotContext` gets a branch "focus within `MixerPanel`" →
  `PaletteOrigin.Mixer`, mirroring the `MixerWindow` branch.
- MIDI learn: the main window's `LearnOverlay` covers its whole visual tree; the docked
  strips are learnable without wiring. Verify in quickstart.

## R7 — Commands and UI entry points

**Decision**:
- `view.mixer` (View → Mixer, ⌘⇧M / Ctrl+Shift+M) → `ToggleMixer()` in the remembered
  placement. Title "Show Mixer" unchanged.
- New command `view.mixerFloat` — "Mixer in its own window" (View menu, checkable;
  palette keywords "dock undock float pop out mixer window"), no shortcut. Switching
  while hidden only changes the preference; while shown it moves the Mixer.
- Docked panel toolbar (32 px, like `DetailPanel`'s): caption *Mixer* · Strips | Stereo
  map segments · spacer · pop-out (`Glyph Kind=PopOut`) · close (`Kind=Close`).
- `MixerWindow` title bar: add a dock button (same glyph family as the Devices window's
  dock affordance) on the right; it sets placement to docked and closes the window
  without hiding the Mixer.

**Rationale**: no new shortcut means no `nota-shortcuts` change beyond confirming the
list; the palette and menu cover keyboard users.
