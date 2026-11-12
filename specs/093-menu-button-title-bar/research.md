# Research: Menu button and a hideable menu bar

## R1 — One menu definition for the row and the Menu key

- **Decision**: Keep `NativeMenu` in `MainWindow.axaml` as the single definition. The Menu key's
  `ContextMenu` is rebuilt on every `Opening` by `MenuMirror.Build(nativeMenu)`, which walks the
  tree and makes one `MenuItem` per `NativeMenuItem` (Header, `InputGesture` = Gesture,
  ToggleType / IsChecked, IsEnabled, ToolTip, submenus) and a `Separator` per
  `NativeMenuItemSeparator`. Clicking a mirrored item calls the source item's
  `INativeMenuItemExporterEventsImplBridge.RaiseClicked()` — the very path `TryMenuShortcut`
  already uses — so it runs the same `Click` handlers (catalogue command, Open recent entry).
- **Rationale**: every piece of state the app keeps for the menu already lives on the
  `NativeMenuItem`s (`MenuItemFor(id).IsChecked = …`, the recent list rebuilt in place, labels
  from `{app:T}`), so mirroring at open time can never be stale (FR-005) and adds no second list
  (FR-003). Building ~150 items per open is negligible.
- **Alternatives considered**: (a) a second XAML `Menu` tree — rejected, a second hand-kept
  list; (b) one long-lived mirror kept in sync by property-change subscriptions — rejected,
  more code and more ways to drift for no gain; (c) generating both from the command catalogue
  alone — rejected, the catalogue has no order / grouping / separators; the XAML holds those.

## R2 — Enabled state

- **Finding**: menu commands are never disabled today: `CommandRegistry.Run` runs menu /
  keyboard commands whatever their availability, and the handler explains in the status line.
  Only the "No recent projects" placeholder is `IsEnabled = false`.
- **Decision**: mirror `IsEnabled` as is; no new greying. Spec scenario US1-4 updated to match.

## R3 — Hiding the row

- **Decision**: name the row's border (`MenuBarRow`) and, when hidden, set
  `MenuBarRow.Child = null` (and `IsVisible = false`); when shown, put the same `NativeMenuBar`
  back. On macOS the row is left as today (it renders nothing there).
- **Rationale**: Avalonia's `Menu` registers itself as the window's main menu for the access-key
  handler while it is in the visual tree; an invisible but attached menu bar would still take
  Alt / F10 and open off-screen. Detaching it removes that, so the window's own Alt / F10
  handling (R4) is the only one active while the row is hidden; attaching restores the stock
  behaviour (FR-016).
- **Alternatives**: `IsVisible = false` only — rejected for the reason above.

## R4 — Alt tap and F10

- **Decision**: a window-level detector in `MainWindow.MenuButton.cs`, active only on
  Windows / Linux with the row hidden:
  - `KeyDown` (tunnel, handledEventsToo): `F10` with no modifiers → open the Menu key's menu,
    mark handled. `LeftAlt` / `RightAlt` with no other key down → arm; any other key → disarm.
  - `PointerPressed` and `PointerWheelChanged` (tunnel, handledEventsToo) → disarm.
  - `KeyUp` of Alt while armed → open the menu, mark handled.
  - Not while a popup / ContextMenu / modal is already open (the window is not active or a
    popup has focus → skip).
  The menu opens with its first item selected, so arrows / Enter / Escape work at once; on close
  focus returns to the element focused before (saved on open).
- **Rationale**: matches Windows / GNOME / KDE convention; Alt+drag (free drag), Alt+wheel and
  ⌥-chords keep working because any intervening input disarms.
- **Alternatives**: rely on Avalonia's `AccessKeyHandler` with the Menu key as main menu —
  rejected: it expects a `Menu`/`IMainMenu`, not a ContextMenu, and its Alt handling shows access
  key underlines across the window.

## R5 — The setting and the View item

- **Decision**: `Settings.ShowMenuBar` (bool, default false — an older settings file without the
  key reads false, which is the new default, FR-008). Catalogue command `view.menuBar`
  ("Show menu bar", View category, no shortcut). The View item is
  `ToggleType="CheckBox"` with `app:Commands.Id="view.menuBar"`. `ToggleMenuBar()` flips the
  setting and saves; `Settings.Changed` → `ApplyMenuBar(on)` shows / hides the row, shows / hides
  the Menu key (FR-011), ticks the View item, and re-fits the transport. Settings → Appearance gets
  a "Menu bar: Hidden / Shown" segmented picker built like `HeaderSidePicker` (repaints on
  `Settings.Changed`). On macOS: the View item is removed at start-up (like `file.exit`), the
  Appearance row is not added, the command is not bound to any behaviour beyond a no-op status
  and is hidden from the palette there.
- **Applied at start-up** from `App.Services`' `ISettingsService` in the window constructor, so
  the first frame already has the right row (SC-005).

## R6 — The Menu key

- **Decision**: a `ToggleButton` (`Classes="seg view"`) in its own `Border Classes="segmented view"`,
  first in transport column 0's `StackPanel`, before the view switch; `GlyphKind.Menu` — three
  horizontal strokes on the 24 grid (`M4 6.5 H20 M4 12 H20 M4 17.5 H20`), drawn with the shared
  pen; `ToolTip.Tip` and `AutomationProperties.Name` = "Menu". It is checked while its menu is
  open (feedback like the other seg keys). It is not in `TransportShedOrder`, so it never sheds
  (FR-006). Hidden on macOS and while the row is shown.

## R7 — The Windows caption

- **Decision**: `UpdateWindowTitle()` sets `DocTitleText.Text = title` (the "Nota — {0}" string)
  on Windows; macOS keeps `ProjectDisplayName()`; Linux hides the bar (native frame shows
  `Title`).

## R8 — Settings → Shortcuts

- **Decision**: on Windows / Linux add a row "Alt   F10 → Open the menu" to the general group of
  `ShortcutGroups` (filtered out on macOS). It is not a catalogue command, so the palette drift
  check is unaffected.

## R9 — Testing approach

- **Decision**: `MenuMirrorTests` (smoke suite) builds a small `NativeMenu` in code (nested
  submenu, separator, checkbox checked / unchecked, a gesture, a disabled item, a header with
  `_`), mirrors it, and compares the two trees field by field; then flips a check mark / adds a
  recent item and mirrors again to prove freshness. If `MenuItem` can't be constructed without an
  Avalonia app in the console runner, the comparison runs on `MenuMirror.Describe(...)` — a plain
  record tree both sides produce — and the `MenuItem` construction is checked in the headless
  harness instead.
- Visual / keyboard checks on the real app follow [quickstart.md](quickstart.md).
