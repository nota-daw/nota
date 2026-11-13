# Data model: Custom keyboard shortcuts (096)

All types live in `Nota.Application/Input` unless noted. Pure managed state; nothing reaches
the engine.

## KeyScope (enum)

`Global, Arrangement, Notes, Session, PianoRoll, AudioEditor, Devices, Browser, Modular`.
`IsWindowLayer(scope)` = Global | Arrangement | Notes. See research R3.

## KeyAction

| Field | Type | Notes |
|-------|------|-------|
| Id | string | stable across versions; command ids reuse `CommandCatalog` ids |
| Title | string | localized; the row text on the Shortcuts page |
| Category | string | localized group title (the page's section; for commands their palette category) |
| Scope | KeyScope | where it is live |
| DefaultPrimary | Shortcut? | per platform (some differ off macOS) |
| DefaultAlternative | Shortcut? | e.g. `⌦` for Delete, Alt+← for browser back off macOS |
| IsMenuCommand | bool | has a `NativeMenuItem` (from `MainWindow.axaml`) |
| NoMenuKey | bool | the menu shows no gesture (copy / cut / paste / delete) |
| Fixed | bool | listed but not editable (macOS ⌘Q) |
| Aliases | string[] | extra filter words (from the command catalog) |

## KeyBindingOverride (settings, `Nota.Application.Settings.KeyBindings`)

`Dictionary<string actionId, KeyBindingOverride>`; `KeyBindingOverride { string? Primary;
string? Alternative; }` — `null` = default, `""` = unassigned, else glyph text.

## KeyBindings (service)

State: catalog, overrides, effective map `(scope, Shortcut) → actionIds`, `NeedsDecision`
set (defaults shadowed by user keys after an update).

| Operation | Behaviour |
|-----------|-----------|
| `Current(id, slot)` | effective Shortcut or none |
| `IsEdited(id)` | any slot differs from default |
| `Resolve(scope, chord)` | the action in that scope bound to chord, or null |
| `Matches(id, chord)` | chord equals the primary or the alternative |
| `FindConflicts(id, slot, chord)` | actions that conflict (rule R3), each with scope |
| `FindShadows(id, chord)` | non-conflicting reuses in other layers (informational) |
| `Validate(chord)` | `Ok / Reserved(reason) / Incomplete` |
| `Apply(change)` | one atomic change: set slot (+ clear the conflicting slots on Replace) |
| `Reset(id)` / `ResetAll()` | back to defaults; `Reset` reports conflicts first |
| `Changed` event | raised after every applied change; persisted via `ISettingsService.Save` |

### State transitions of a row

`Idle → Capturing → Proposed(chord) → [Conflict(list) →] Saved | Cancelled`;
`Idle → Cleared` (Clear), `Edited → Idle` (Reset / equals default again).

## Shortcuts page layout (`Nota.App`)

`ShortcutLayout` (Nota.Application/Input): ordered groups `(title, rows)` built from the catalog categories; a row is `Action(id)` (editable, from the
catalog) or `Fixed(keyText, description)` (mouse gestures, navigation / text keys, gamepad).
Every catalog action appears in exactly one group (smoke-tested).
