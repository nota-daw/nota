# Research: Studio context menus

## R1 — Where the "⌘+G" spelling and the loose layout come from

**Finding**: menu items use Avalonia.Themes.Fluent's `MenuItem` template. Its
`PART_InputGestureText` formats `MenuItem.InputGesture` with Avalonia's own
`KeyGesture.ToString("p")`, which joins modifiers with "+" on macOS. Column widths, the
caret (a large `Path` chevron flush to the edge) and the icon slot size are fixed in that
template; `NotaTheme.axaml` only restyles colours and padding through
`/template/` selectors.

**Decision**: add a Nota `ControlTheme` for `MenuItem` (`TargetType="MenuItem"`, set as the
default theme in `NotaTheme.axaml`). Grid columns: icon 16 · label * · value/shortcut auto
· caret 12, padding 8,0, height 24. The shortcut cell shows
`Shortcut.Spell(InputGesture)` through a value converter (macOS: `⌘G`, `⇧⌘G`, `⌫`;
elsewhere `Ctrl+G`), Geist Mono 11, muted ink. The value cell shows the attached
`MenuKit.Value` string, muted ink. The caret is `Glyph(ChevronRight, 8)`. Keeps the
template parts the `MenuItem` code-behind expects (`PART_Popup`, header presenter).

**Rationale**: one place controls every menu; call sites keep using plain `MenuItem`.

**Alternatives considered**: (a) keep Fluent and override with more `/template/`
selectors — cannot change how the gesture string is built or replace the caret path;
(b) a custom `Popup` menu control — loses keyboard navigation, submenus and
accessibility that `MenuFlyout` gives for free. (c) Copying Fluent's template — avoided;
written fresh (licensing hygiene, and only the parts we need).

## R2 — Non-clickable rows (header, colour grid, segments) inside a MenuFlyout

**Finding**: `MenuFlyout` wraps any item that is not a `MenuItem` or `Separator` in a
`MenuItem` container, which then highlights on hover and closes on click.

**Decision**: the building blocks return a `MenuItem` with class `inline` (header, colour
grid, segmented row) whose `Header` is the row control. Theme styles for
`MenuItem.inline` drop the hover fill, hide icon/caret columns and let the row span the
width. `inline` rows set `Focusable` so keyboard focus can land; arrow keys inside the row
are handled by the row control (FR-017), Enter applies, the row then closes the flyout via
its owning `MenuFlyout.Hide()`. The header row additionally sets `IsHitTestVisible=false`
and `Focusable=false`.

**Alternatives considered**: a `Separator`-derived host (no keyboard focus); putting rows
in the flyout presenter's template (would apply to every menu).

## R3 — Colours, danger ink and theme following

**Decision**: swatches use `ArrangementView.TrackBrush(index)` (theme-following); ring =
1 px `TextPrimary` outline with 1 px gap; "Auto" is a 14 px key with an `Auto` caption.
Danger items use class `danger` → label and icon `Brush.Danger`; on highlight the normal
accent fill with `TextOnAccent` (DESIGN: hover is always the accent). Disabled `danger`
falls back to `TextDisabled`. All through `DynamicResource`, so a theme switch restyles.

## R4 — Live freeze as a switch

**Finding**: `MainWindow.Freeze.cs` `BuildTrackFreezeMenu` already knows the link state
(`LinkForSource` / `LinkForFrozen`). Live freeze = `LiveFreezeAsync`; unfreeze =
`UnfreezeLink(src)`.

**Decision**: `MenuKit.SwitchItem(label, icon, isOn, toggle)` draws the existing
`DeviceCardKit.Switch` (18 × 10) in the value column. Off → `LiveFreezeAsync(trackId)`;
on → `UnfreezeLink(src)`. While linked, Edit source (or Done / Discard mid-edit) and
Flatten follow as items; Flatten takes `danger` ink. The accent `GlyphIcon(…, accent:true)`
and the tinted `SnowflakeIcon` / `ChainLinkIcon` in menus become `MenuKit.Icon` (neutral).
The freeze button's own menus (`EnsureFreezeMenu`, `EnsureLiveFreezeMenu`) get the same
switch.

## R5 — Recent instruments: where to record, where to store

**Finding**: UI adds go through `Nota.Presentation/DeviceInsertService`
(`CreateInstrumentTrack`, `Insert`, `TryReplaceInstrument`, `RouteToTrack`) with a
`BrowserItem`; the plain "Instrument" track adds a Volt (`Engine.AddVoltSynthTrack` in
`MainWindow.AddTrack`); MCP adds go through `Nota.Mcp/Tools/InstrumentTools.cs` and
`PluginTools.cs`. `Settings` already keeps a similar list (`RecentProjects`).

**Decision**: `Settings.RecentInstruments : List<RecentInstrument>`; a small pure helper
`RecentInstruments.Record(list, entry)` (move-to-top, de-dupe by identity, cap 5) and
`Visible(list, isInstalled)` (drops missing plug-ins). Record from `DeviceInsertService`
(every instrument add / replace), `MainWindow.AddTrack(Instrument)` (Nota Volt), and the
MCP tools; save through the existing `ISettingsService`. "Add track ▸ Recent ▸ X" calls
`DeviceInsertService.CreateInstrumentTrack` with a `BrowserItem` rebuilt from the entry,
placed after the anchor track as "Add track" does today.

**Alternatives considered**: store in the project (rejected by clarification — the list is
per-user); record in the engine (no C ABI change wanted, and the engine does not know
browser identities).

## R6 — Guard test

**Decision**: `DesignTokenCheck.RunMenus` (source scan like `RunIcons`):
- no `Header = "Color"` / `"Track height"` submenus left;
- every `MenuItem.Icon =` is `MenuKit.Icon(...)` (no tinted icon controls in menus);
- no `InputGesture` rendered through anything but the theme (no hand-written "⌘+");
- destructive headers (`Delete`, `Remove`, `Clear`, `Discard`, `Flatten`) built via
  `MenuKit.Danger` or carrying the `danger` class.
An allow list (`MenuAllowList.txt`, shrink-only) covers justified exceptions.

## R7 — Labels

Header row names the object, so items drop the noun: "Copy", "Cut", "Paste", "Duplicate",
"Delete" in object menus; "Delete track" stays in menus without a header. All through
`NotaText.Label`; RunLabels already enforces sentence case.
