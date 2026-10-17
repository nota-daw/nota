# Implementation Plan: Cross-Platform Key Shortcut API & Builder

**Branch**: `023-shortcut-builder` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

## Summary

Centralize all keyboard shortcut and modifier representations into a type-safe `Shortcut` value object and `ShortcutBuilder` in `Nota.Application.Input`, accompanied by `KeyGlyphs` constants. This replaces raw Unicode strings (`"⌘N"`, `"⌘⇧S"`, `"⌥⌘S"`, `"⌫"`) in `CommandCatalog` and throughout UI spellers. The API handles platform formatting (macOS glyphs vs. Windows/Linux `Ctrl+...` strings), key cap breakdown for UI badge rendering, and Avalonia `KeyGesture` conversion via extension methods in `Nota.App`.

## Technical Context

- **Layers & Dependencies**:
  - `Nota.Application` has no Avalonia dependency and targets .NET 10. Core abstractions (`KeyGlyphs`, `ShortcutModifiers`, `Shortcut`, `Shortcut.SpellText`) reside in `src/managed/Nota.Application/Input/`.
  - `Nota.App` contains Avalonia UI. Extension methods for `KeyGesture` conversion (`ShortcutAvaloniaExtensions.ToGesture`) live in `src/managed/Nota.App/Input/` or `src/managed/Nota.App/Commands.cs`.
- **String Interop**:
  - `Shortcut` provides an `implicit operator string(Shortcut s) => s.ToGlyphs();` ensuring all existing signatures accepting `string` continue to compile and function without breaking changes.
- **Canonical Ordering**:
  - `ToGlyphs()` enforces consistent order (`⌥`, `⌘`, `⇧` or `⌃`, `⌥`, `⇧`, `⌘` matching macOS HIG standards and Nota's existing catalog).
- **Spelling / Localization**:
  - `Shortcut.SpellText(string text)` centralizes replacement logic currently duplicated in `MenuKit.SpellKeys`, accurately turning `"Show automation (⌘⇧A)"` into `"Show automation (Ctrl+Shift+A)"` on non-macOS platforms while preserving punctuation like `⌥-click` and `⌘ + wheel`.
- **UI Adaptations**:
  - `CommandPaletteView`: Uses `Shortcut.ToKeyCaps()` to generate individual `KeyCap` badges and platform-aware hint badges (`↵`, `⌘↵`, `⌥↵`, `⇧↵`).
  - `PreferencesWindow`: Reconciles shortcut display with `Shortcut.ToPlatformString()`.
- **Testing**:
  - `PaletteTests.cs` checks that every catalog command is bound and listed in Settings.
  - New unit tests in `Nota.SmokeTest` validating `Shortcut` parsing, platform formatting, canonical ordering, and gesture conversion.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ No native/audio thread involvement; managed UI and metadata only. |
| II. Boundary discipline | ✅ Pure managed-layer architecture; no C ABI changes needed. |
| III. Licensing hygiene | ✅ All new files carry AGPL-3.0-only SPDX header; no new external dependencies. |
| IV. Verification gate | ✅ Existing smoke tests and new shortcut verification tests pass. |
| V. One logical change | ✅ Shortcut builder and glyph refactoring only. |
| VI. Docs in sync | ✅ All code comments and docstrings in English; CHANGELOG updated if user-facing. |
| VII. Spec-first | ✅ Follows spec-driven workflow under `specs/023-shortcut-builder/`. |
| VIII. Rules not weakened | ✅ Adheres strictly to all architectural principles. |

## Files Touched

```
src/managed/Nota.Application/Input/KeyGlyphs.cs
src/managed/Nota.Application/Input/ShortcutModifiers.cs
src/managed/Nota.Application/Input/Shortcut.cs
src/managed/Nota.Application/Palette/CommandCatalog.cs
src/managed/Nota.App/Input/ShortcutAvaloniaExtensions.cs (or Commands.cs)
src/managed/Nota.App/Commands.cs
src/managed/Nota.App/MenuKit.cs
src/managed/Nota.App/CommandPaletteView.cs
src/managed/Nota.App/KeyGlyphs.cs (migrated/superseded by Nota.Application)
tests/Nota.SmokeTest/ShortcutBuilderTests.cs
tests/Nota.SmokeTest/PaletteTests.cs
tests/Nota.SmokeTest/Program.cs
```
