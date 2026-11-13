# Research: Toolbar overflow

## R1 — How the menu is presented

- **Decision**: a `MenuFlyout` anchored under the overflow button, built with `MenuKit` at
  open time (Clarifications, 2026-11-13).
- **Rationale**: every Nota menu is a `MenuKit` menu with the Nota MenuItem theme (spec 034);
  light dismiss, Escape and keyboard navigation come for free and match the other menus;
  building at open time means the entries always match the current fit and state, so no
  live syncing of hidden menu items is needed.
- **Alternatives**: a second toolbar row or a floating panel of live controls — rejected by
  the maintainer in favour of an ordinary menu.

## R2 — Which controls get entries

- **Decision**: one model, `ToolbarOverflow`, holds the parts in shed order (today's array in
  `TransportFit`, moved over) and in bar order; for "N steps shed" it returns the entries in
  bar order. `CpuBar` and `RemoteLabel` are info-only (their information stays in the bar) and
  give no entry. The `RemotePanel` step (MIDI learn + the already-shed Remote button) gives the
  MIDI learn entry; Remote's own step gives Connect Phone.
- **Rationale**: keeps the order in one place; the App's fit keeps a `Control[][]` mapped
  from the model's parts, so the shed behaviour itself does not change.
- **Alternatives**: reading `IsVisible` of each control at open time — works, but is not
  testable without the app and ties the menu to layout state.

## R3 — Entries that are not a simple toggle

- **Grid**: `SnapChip.OpenMenu` already builds the grid menu; it is split into
  `BuildItems()` (returns the items) used by both the chip and a "Grid" submenu.
- **Key**: the key picker is a custom flyout (`KeyPicker.Show`), not menu items. The entry
  "Key…" (with the current key as trailing text) opens that same picker anchored to the
  overflow button after the menu closes.
- **Time signature**: a submenu of common signatures (2/4, 3/4, 4/4, 5/4, 6/8, 7/8, 9/8,
  12/8) as radio items, the current one checked; an uncommon current signature is shown as an
  extra checked item at the top. Sets `Transport.TimeSigNumerator/Denominator` like the
  fields.
- **Tap**: a `MenuItem` with `StaysOpenOnClick = true` calling `TapTempoBeat()`; its trailing
  text shows the tapped tempo, refreshed on each tap.
- **Master**: a menu item whose header hosts a second `MiniFader` made by the same factory as
  the bar's (shared `MixGesture` undo, double-click to type, MIDI-learnable is not needed in
  the menu). `StaysOpenOnClick` so dragging does not close the menu.
- **DSP meter**: a disabled item "DSP 23 %" refreshed by the existing CPU tick while the menu
  is open.
- **Automation**: check item; when the Re-enable chip is shown, a "Re-enable automation" item
  follows it.
- **Remote**: "Connect Phone…" with the count text the chip shows.

## R4 — Interface scale from the menu

- **Decision**: a radio submenu of `InterfaceScale.Presets`; picking one sets
  `settings.Current.InterfaceScale`, saves and calls `NotaUiScale.Set` — the same three steps
  as Settings → Appearance. The Settings scale picker subscribes to `NotaUiScale.Changed` so an
  open Settings window follows.
- **Rationale**: one setting, two faces; no new state.

## R5 — Button look and "something is shed" cue

- **Decision**: a `tp-icon` key with a new `GlyphKind.More` (three dots), in the bar's last
  column after the master strip; when at least one step is shed it carries a small accent dot
  (the same 5 px dot language as the MIDI / Remote dots).
- **Rationale**: Studio design language — tool keys draw what they do; the dot is the
  existing "activity" cue.

## R6 — Fit interaction

- The button is not in the shed order, so it is measured with the never-shed controls; the
  fit loop is unchanged otherwise. `FitTransport` stores `_shedSteps` and updates the cue.
- A resize while the menu is open leaves the open menu as built; the next open reflects the
  new fit (spec edge case).
