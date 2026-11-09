# Research: Device cards XL panel

## R1 — Where the XL choice is stored

- **Decision**: the device's existing `View` param takes a third value: `0 = L`, `1 = S`,
  `2 = XL`. For EQ-8 (`Eq.cpp`) and Dynamic EQ-8 (`DynamicEq.cpp`), `paramMax(View)`
  goes from 1 to 2. `DeviceView` (Nota.Application) gets a `CardSize` enum and reads the
  value by rounding, so `IsMini` stays true only for `1`.
- **Rationale**: the size already lives there (#531). It is saved with the project and
  follows a duplicated device. Presets skip it by name (`Engine_Presets.cpp`), A/B skips
  it (`ApplyParams`), and it is not offered for automation. XL gets all of that for free.
  `setParam` clamps to `paramMax`, so without the widening a stored 2 would read back as
  1 (S).
- **Alternatives considered**: a managed-only store (a project sidecar keyed by device),
  rejected because it needs new persistence, remapping on move, delete and duplicate,
  and undo of structural edits. Twin already uses a 0..2 `view` param, so a three-state
  View is not new to the engine.
- **Spec impact**: the spec says "no engine or param changes". This is the one exception,
  and the spec's Assumptions already allow it: it widens editor state, with no new
  param, no DSP change and nothing on the audio thread beyond the existing atomic store.
- **Old versions**: a project saved with XL and opened in an older Nota clamps 2 to 1 and
  opens that device in S. Acceptable.

## R2 — Where the panel lives and how tall the Devices area gets

- **Decision**: the panel is a row inside `DeviceChainView`, above the chain's scroller.
  Without XL the view is exactly as today. With XL the rows are `*` (the panel) and
  `Auto` (the chain: 6 + 260 + 6). `DeviceChainView` exposes `PreferredHeight`
  (`2·6 + 260` without XL, plus `XlHeight + 6` with it) and raises
  `PreferredHeightChanged` when it changes. `MainWindow.ShowDevices` sizes the docked
  Detail panel from `PreferredHeight` instead of the hard-coded card height, and
  re-sizes on the event while Devices is the shown tab.
- **XlHeight**: 460 desired (420 gave the EQ-8 graph only 1.8× the L card's height, under SC-001), minimum 180. In a
  short window `FitBottomPanels` caps the Detail row, the `*` row takes what is left, and
  the chain keeps its 272.
- **Rationale**: the Detail panel already fits its content for Devices
  (`ShowDetail(..., resizable: false)`). Keeping the panel inside the chain view means
  floating Detail gets it too with no extra host.
- **Alternatives considered**: a separate MainWindow row above the Detail panel, rejected
  because it splits the device UI across two hosts and two selection/refresh paths.

## R3 — Building the panel's content

- **Decision**: the panel calls the same `IDeviceBody.Build(ctx, index)` as the card,
  wrapped in the shared `BuildCardShell`. Width is NaN (stretch), height is the panel's,
  and the full header is shown (name, preset picker, type badge, S/L/XL, power). The L
  layouts of EQ-8 and Dynamic EQ-8 are a graph that fills a `DockPanel`, plus a 186 right
  column, so in a wider and taller host the graph stretches and the controls keep their
  size. This meets FR-004 with no XL-specific layout in the bodies.
- **The card in the chain**: `BuildCardShell` at 260 wide (compact header with the same
  toggle), whose body is a placeholder that says the device is open in XL above. Only
  the panel's body is built, so live refreshers (`ctx.AddDeviceRefresher`) run once
  (FR-013).
- **Precedent**: `LensWindow` (specs/050) builds a device body a second time in a larger
  host, with the graph taking the extra room.
- **Shell changes**: `ShellSpec` gets `Height` (default `CardH`) and `Xl` (the panel:
  no drag-to-reorder, no fold on header double-click, text size not fixed).

## R4 — One XL device per track

- **Decision**: picking XL on a device sets every other XL device of the track to L
  first. On rebuild, if several devices still say XL (duplicate, a move from another
  track, an older edit), the panel goes to the one that held it last on this track (a
  flag on the per-device `CardExtra`, which already follows moves and removals), or to
  the first in the chain. The others are set to L.
- **Rationale**: it matches FR-007 and the edge cases (a duplicate opens in L, and a
  device moved onto a track that has an XL device returns to L), using state that
  already follows devices through moves.

## R5 — Where XL is offered

- **Decision**: `IDeviceBody.SupportsXl` (default false; true for `EqDeviceBody` and
  `DynamicEqDeviceBody`) and a `CanXl` flag on `DeviceCardContext` (true only for the
  top-level chain). `CardLayout.SizeToggle` shows `S / L / XL` when both are true, and
  `S / L` otherwise. Racks (`RackCardView`) and `LensWindow` build their contexts
  without it.
- **Rationale**: XL is not offered inside racks (spec), and the mechanism stays generic:
  a later device opts in with one property.

## R6 — Leaving XL

- **Decision**: S or L from either toggle sets that size. The panel's close button and
  Esc (when the key reaches the panel, not taken by a text box) set L. Selecting another
  track hides the panel without touching sizes (FR-015). Removing the device removes the
  panel on the next rebuild (FR-016).
- **The close key** sits in the panel header's accessory slot, next to the toggle. It uses
  the `Shrink` glyph (`DeviceCardKit.IconKey`), not ×: the header's own × beside it removes
  the device, and two × keys side by side invite the wrong click.

## R7 — Found while building

- **Init reset the card size.** `PresetService.ResetInPlace` set every effect param to its
  default, View included (MIDI effects already skipped it). Init now leaves View alone, as
  FR-011 asks. This fixes S too.
- **"XL" read as "Xl".** `Segments` runs labels through `NotaText.Label` (sentence case), so
  `XL` joins the acronyms in `NotaText.Fixed`.
- **The placeholder header had no room for the name** with the three-way toggle and the
  preset picker. `ShellSpec.Placeholder` drops the picker, because the presets are on the
  panel.
- **Text size**: the panel keeps `NotaTextSize.SetFixed` like every card, because the
  body's controls are laid out at fixed sizes.
