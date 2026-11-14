# Implementation Plan: A Mixer button in the toolbar

**Branch**: `092-toolbar-mixer-toggle` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

## Summary

A `tp-icon` toggle after Automation that runs `view.mixer` and mirrors whether the Mixer is
shown.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: None.

**Testing**: the smoke test's localization check (the new texts, the removed one), icon check
(the glyph lives in `Glyph.cs`) and design checks; manual check in the running app of every
path in User Story 2.

## Design

- `Glyph.cs` — `GlyphKind.Mixer`: three 16-unit fader slots (stroke) with 4 × 3.5 solid caps
  (fill) at three levels.
- `MainWindow.axaml` — `MixerToggle` after `AutomationToggle`, `Click="OnToggleMixerButton"`.
- `MainWindow.Mixer.cs`:
  - `OnToggleMixerButton` runs `view.mixer` through the command registry, then
    `SyncMixerToggle` (a ToggleButton flips itself on click; the Mixer may not open).
  - `SyncMixerToggle` sets `IsChecked = IsMixerShown`. Called where the shown state changes:
    the end of `DockMixer`, `UndockMixer` and `OpenMixerWindow`, and the window's
    `WindowClosed`. Every path (menu, shortcut, close buttons, placement switches, the restore
    at launch) goes through those.
- `MainWindow.MenuKeys.cs` — the tooltip through `MenuKit.Keys`, like Automation's.
- `MainWindow.TransportFit.cs` — `MixerToggle` sheds right after `AutomationToggle`.
- `PreferencesWindow` — the ⌘⇧M row says "Show or hide the Mixer · also the Mixer button in
  the toolbar" (a new key; the old one goes).
- `DESIGN.md` — the Tools list names Mixer.

## Constitution Check

UI-only, no engine change, no dependency, no new ABI. Texts through `Loc`, geometry in
`Glyph`, colours from the shared toggle style.
