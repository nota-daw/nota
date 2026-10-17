# Tasks: Cross-Platform Key Shortcut API & Builder

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1 — Core Model in `Nota.Application`

- [x] T001 Define `KeyGlyphs` in `Nota.Application.Input` with Unicode constants for `Cmd`, `Option`, `Ctrl`, `Shift`, `Backspace`, `Return` with AGPL-3.0 header and English docstrings (`src/managed/Nota.Application/Input/KeyGlyphs.cs`)
- [x] T002 Define `ShortcutModifiers` enum flags (`None`, `Primary`, `Shift`, `Alt`, `Ctrl`) in `Nota.Application.Input` (`src/managed/Nota.Application/Input/ShortcutModifiers.cs`)
- [x] T003 Implement `Shortcut` readonly record struct with fluent modifiers (`WithShift`, `WithAlt`, `WithCtrl`), factory methods (`Cmd`, `CmdShift`, `CmdAlt`, `Shift`, `Alt`, `None`), `ToGlyphs()`, `ToPlatformString(bool mac)`, `ToKeyCaps(bool mac)`, `Parse(string)`, `SpellText(string, bool mac)`, and `implicit operator string` (`src/managed/Nota.Application/Input/Shortcut.cs`)

## Phase 2 — Avalonia Integration in `Nota.App`

- [x] T004 Implement `ToGesture(this Shortcut s, bool mac)` extension in `Nota.App` to convert `Shortcut` into Avalonia `KeyGesture` (`src/managed/Nota.App/Input/ShortcutAvaloniaExtensions.cs`)
- [x] T005 Refactor `Commands.Parse`, `Commands.IsChord`, and `Commands.CanMenuGesture` to leverage `Shortcut` (`src/managed/Nota.App/Commands.cs`)
- [x] T006 Update `MenuKit.Keys` and `MenuKit.SpellKeys` to use `Shortcut.SpellText` (`src/managed/Nota.App/MenuKit.cs`)
- [x] T007 Update `CommandPaletteView.Caps` to use `Shortcut.ToKeyCaps` (`src/managed/Nota.App/CommandPaletteView.cs`)
- [x] T008 Route gesture parsing in `Commands.cs` through `Shortcut.Parse` and `ToGesture` (`src/managed/Nota.App/Commands.cs`)

## Phase 3 — Command Catalog & UI Adoption

- [x] T009 Refactor `CommandCatalog.cs` to use `Shortcut` factories (`Shortcut.Cmd`, `Shortcut.CmdShift`, `Shortcut.CmdAlt`, `Shortcut.None`) for all 33 command shortcuts (`src/managed/Nota.Application/Palette/CommandCatalog.cs`)
- [x] T010 Update `SessionView.Inspector.cs` action shortcuts (`"⌫"`, `"⌘⇧C"`, `"⌘V"`, `"↵"`) to use `Shortcut` (`src/managed/Nota.App/SessionView.Inspector.cs`)
- [x] T011 Verify plain text hint strings continue formatting correctly via `MenuKit.Keys` across platforms

## Phase 4 — Verification & Smoke Test

- [x] T012 Implement unit tests in `Nota.SmokeTest` validating `Shortcut` construction, canonical ordering, platform strings, key cap breakdown, and gesture conversion (`tests/Nota.SmokeTest/ShortcutBuilderTests.cs`, `Program.cs`)
- [x] T013 Verify that `PaletteTests` runs and passes (ensuring all catalog shortcuts match Settings → Shortcuts)
- [x] T014 Run `dotnet build` across the solution and verify zero compilation errors or warnings
