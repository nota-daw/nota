# Data Model: Docked Mixer

No project-file or engine data changes. Everything below is per-user UI state.

## MixerPlacement (persisted in `Settings`, `src/managed/Nota.Application/Settings.cs`)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `MixerView` *(exists)* | string | `"strips"` | Strips or Stereo map (`"map"`), specs/008 |
| `MixerFloating` | bool | `false` | `false` = docked panel, `true` = own window |
| `MixerShown` | bool | `false` | Mixer was shown when last changed by the user |
| `MixerDockHeight` | double | `0` | Docked row height in design units; `0` = full strips, but no more than half of the body |

Validation:
- `MixerDockHeight` is clamped on use to `[MixerPanelMin, available]`, never rewritten by
  a clamp; only a finished splitter drag stores a new value.
- Unknown / missing fields in an older `settings.json` take the defaults (docked, hidden).

## Runtime state (MainWindow, not persisted)

- `Placement`: `Hidden | Docked | Floating` — derived from `MixerShown` + `MixerFloating`
  while the app runs; `MixerView.IsVisible == (Placement != Hidden)` (drives meter ticks).
- `MixerTier` (inside `MixerView`): `Full | NoIo | NoIoNoSends | Compact(faderHeight)`.
- `PinnedSections` (inside `MixerView`): set of {I/O, Sends} the user forced visible while
  auto-hidden; turns on vertical strip scrolling. Session-only, like today's toggles.

## State transitions

```text
Hidden ──view.mixer──▶ Docked | Floating   (by MixerFloating)
Docked ──view.mixer / panel ×──▶ Hidden
Floating ──view.mixer / window close / Esc──▶ Hidden
Docked ──panel pop-out / view.mixerFloat──▶ Floating   (MixerFloating = true)
Floating ──window dock button / view.mixerFloat──▶ Docked   (MixerFloating = false)
Hidden ──view.mixerFloat──▶ Hidden   (only flips MixerFloating)
App exit: no transition recorded (MixerShown keeps its value)
App start: Hidden ──restore──▶ state from settings
```

Every user-driven transition writes `MixerShown` / `MixerFloating` and saves settings.
