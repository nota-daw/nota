# Research: Custom keyboard shortcuts (096)

## R1 — Where keys are decided today

- `CommandCatalog` (Nota.Application/Palette) declares ~110 commands; ~45 carry a glyph
  gesture. `MainWindow.MenuKeys.cs` copies it onto the `NativeMenuItem.Gesture` once at start
  (macOS: AppKit fires it as a key equivalent; Windows / Linux: `TryMenuShortcut` matches it in
  `HandleKeyDown`). The palette copies `PaletteCommand.Gesture` into its items.
- Everything else compares `e.Key` / `e.KeyModifiers` by hand. Files with song-acting keys:
  `MainWindow.Input.cs` (transport tunnel, Esc chain, arrangement clipboard / nudge / delete /
  0 / P, track-header keys, Z X C V, the `KeyToPitch` note map, `GridCommandFor`),
  `PianoRollView.cs` (`OnKeyDown`: delete, ⇧F, ⌥⇧F, Esc, arrows ± ⇧/⌥, ⌘A D C X V),
  `PianoRollView.Controllers.cs` (Delete, Esc), `SessionView.cs` (`HandleKey`), `ModularView.cs`
  (F, B, ⌘D, Delete, Esc), `DeviceChainView.Clipboard.cs` (⌘G ⌘⇧G, ⌘C X V D, Delete, Esc),
  `BrowserView.Files.cs` (⌘L ⌘[ ⌘] ⌘↑, Alt+←→↑ off macOS), `AudioClipEditorView` (Esc via
  `CancelGesture`), rack cards (Esc via `RackCardState`).
- Keys in dialogs, menus, the palette list, text boxes, value entry (`ValueEntry`, `DragNumber`),
  rename flyouts, `MenuKit` navigation, Welcome / Export / SaveChanges windows are
  list-navigation or text-entry keys → fixed (FR-011).
- The Shortcuts page is `PreferencesWindow.ShortcutGroups`: `(title, (Key, Action)[])`, where
  `Key` is either glyph tokens or a localized gesture description. `nota-docs/scripts/shortcuts.mjs`
  parses this array to generate the manual's shortcuts page; `PaletteTests.RegistryDrift`
  checks every catalog gesture is listed there.

## R2 — One catalog, two layers

**Decision**: a new `KeyActionCatalog` (Nota.Application/Input) lists every keyboard action:
id, title, group, scope, default primary, default alternative, flags. Command-palette
commands are *imported* from `CommandCatalog` (same id, title, gesture as default) so there
is one id space; non-command actions (note map, nudges, transposes, …) are declared next to
them. A `KeyBindings` service (Application) holds the user's overrides on top of the defaults
and answers "which action does this chord mean in this scope" and "what is the current key
of action X". Views ask through a small App-side facade (`Keys.Is(e, "roll.transposeUp")`).

**Rationale**: smallest change to the many hand-written handlers — each `e.Key == Key.X && mods`
becomes `Keys.Is(e, id)`; the handlers keep their order, gates (focus, selection) and
fall-through, so default behaviour stays identical (FR-023). `CommandCatalog` keeps its role
for the palette and menu; its `Gesture` becomes the *default* only.

**Alternatives**: a central dispatcher that owns every key and calls into views — rejected:
rewrites focus routing in ~10 views at once, high regression risk for one release.
Moving gestures out of `CommandCatalog` — rejected: the palette tests and docs read it.

## R3 — Scopes and the conflict rule

The app already routes by focus: a focused piano-roll grid, Session grid, device chain,
Modular canvas or Files browser handles its keys before the main window; the main window's
`HandleKeyDown` then runs Global, Arrangement and note-map keys in a fixed order. On macOS a
chord carried by a menu item fires as a key equivalent *before* any view sees the key (the
code comments on ⌘D / ⌘⇧D say so), and the menu handlers route by focus themselves
(`OnMenuDuplicate`).

**Decision** — scopes:

| Scope | Layer | Live when |
|-------|-------|-----------|
| Global | window | the main or floating detail window has the keyboard |
| Arrangement | window | the Arrangement is shown |
| Notes | window | computer-keyboard notes (lowest priority) |
| Session | focus | the Session grid has focus |
| PianoRoll | focus | the piano-roll grid (or its controller lanes) has focus |
| AudioEditor | focus | the audio clip editor is visible |
| Devices | focus | the device chain / rack card has focus |
| Browser | focus | the Files tab of the browser has focus |
| Modular | focus | the Modular canvas has focus |

Two actions **conflict** on a combination when
1. they are in the same scope, or
2. both scopes are window-layer (they are live together, in a fixed order the user can't see), or
3. either binding is a *menu chord* (the action has a menu item, and the combination is a
   chord or a bare F-key) — on macOS it fires before the focused view, so it reaches every scope;

— **unless both bindings are their shipped defaults**. Shipped defaults are curated (and
smoke-tested): today ⌘L is both Loop and the Files browser's path field, ⌘G both Group tracks
and Group devices, Esc both the arrangement and the rack card; those keep working as they do.

A focus-scope key that reuses a window-layer key is **not** a conflict (the focused view wins,
deterministically); the editor shows it as a note ("Elsewhere ⌘K is Save").

Shared editing actions (FR-010) — `edit.copy/cut/paste/duplicate/delete/selectAll` and a new
`edit.cancel` (Esc) — are Global actions read by every view that supports them, so they
never conflict with themselves across contexts.

**Alternatives**: "same scope only" (the issue's sketch) — rejected: Global vs Arrangement would
let ⌘S be both Save and Split, and on macOS the menu would win silently. "Global overlaps
everything" — rejected: it flags today's defaults (Return = Stop vs Return = launch slot).

## R4 — Key tokens and matching

**Decision**: a binding is a `Shortcut` (existing value type: modifiers + key token). Key
tokens (canonical, stored in settings): `A`–`Z`, `0`–`9`, `F1`–`F24`, `Space`, `Return`, `Tab`,
`Esc`, `⌫` (Backspace — the macOS Delete key), `⌦` (forward delete), `←` `→` `↑` `↓`, `PgUp`,
`PgDn`, `Home`, `End`, and the punctuation `, . / ; ' [ ] \ - = \``. A new
`KeyTokens` (Nota.App/Input) maps Avalonia `Key` ⇄ token; `ToGesture` uses it so menus accept
every token. Keypad digits match the digit token (today ⌘1 and ⌘Num1 both narrow the grid).
Modifiers match exactly (Primary = ⌘ on macOS / Ctrl elsewhere, Ctrl = ⌃ on macOS only).

Layouts: Avalonia reports `Key` per OS key code; capture records exactly the `Key` the app
receives, so a binding always matches the key that was pressed when it was recorded, on any
layout. Display uses the token (Latin names), plus the layout character from `KeySymbol` in
the capture field as a hint when it differs.

**Rationale**: reuses `Shortcut` (glyph formatting, platform spelling, key caps already tested).

## R5 — OS-reserved combinations

**Decision**, refused in capture with a reason (FR-008):
- macOS: ⌘Q (Quit lives in the app menu; `file.exit` is fixed on macOS), ⌘H, ⌥⌘H, ⌘Tab,
  ⌘⇧Tab, ⌘Space, ⌃Space, ⌘\`, ⌘⇧3, ⌘⇧4, ⌘⇧5, ⌃⌘Q, ⌥⌘Esc.
- Windows: Alt+F4, Alt+Tab, Alt+Esc, Alt+Space, Ctrl+Esc, Ctrl+Shift+Esc, Ctrl+Alt+Delete,
  any Win-key combination.
- Linux: Alt+F4, Alt+Tab, Ctrl+Alt+Delete, Ctrl+Alt+F1–F12, any Super combination.
The editor says it can't detect every external conflict (global shortcuts of other apps).

## R6 — Persistence

**Decision**: `Settings.KeyBindings : Dictionary<string, KeyBindingOverride>` keyed by action
id; `KeyBindingOverride { string? Primary; string? Alternative; }` — `null` = the default for
that slot, `""` = unassigned, else glyph text (`Shortcut.ToGlyphs`). An entry is removed when
both slots are back to default. On load: unknown ids and unparsable glyphs are skipped and
logged; a user override that now collides with a *new default* (rule R3) makes the default
side inactive at run time and the page lists it under "Needs a decision" — nothing is written
until the user resolves it (FR-021).

**Rationale**: only changed slots are stored (FR-020); JSON stays readable by older versions
(they ignore the unknown property).

## R7 — Propagation

- **Menus**: `MenuKeys` re-applies `item.Gesture` for every menu command on `KeyBindings.Changed`
  (Avalonia's native menu exporter updates the AppKit key equivalent on property change) and
  rebuilds `_menuShortcuts`. Actions flagged `NoMenuKey` (copy / cut / paste / delete — a menu
  key equivalent would steal them from text fields) show no gesture, as today.
- **Tooltips**: tooltip strings embed the default glyph ("Record (⌘R)"). `KeyTips.Set(control,
  text, actionId)` swaps the default glyph for the current key (or drops the parenthesis when
  unassigned) and re-applies on change — translations need no new placeholders. Covers the
  code-set tips (record, loop record, metronome, maximize, automation, snap chip, loop) and the
  XAML tips that name catalog keys (#60 extends this later to every control).
- **Palette**: `PaletteProviders` reads the current key from `KeyBindings` instead of
  `PaletteCommand.Gesture`; the palette index is rebuilt on change.
- **Docs generator / tests**: `ShortcutGroups` becomes a layout list of action ids and fixed
  rows; `nota-docs/scripts/shortcuts.mjs` needs a matching update (separate nota-docs PR), and
  `PaletteTests.RegistryDrift` checks every catalog action appears in the layout.

## R8 — Capture and suppression

**Decision**: capture mode is owned by the Preferences window: while a row captures, the
window's tunnel `KeyDown` handler takes every key (`Handled = true`), and a static
`Keys.Capturing` flag makes `MainWindow` (menu shortcuts on Windows / Linux, transport tunnel,
note map) ignore keys. On macOS menu key equivalents are disabled during capture by clearing
the menu gestures and restoring them afterwards. Esc cancels; an "Assign Esc" button assigns
it. Losing window focus cancels capture.
