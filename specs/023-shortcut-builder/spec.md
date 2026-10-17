# Feature Specification: Cross-Platform Key Shortcut API & Builder

**Feature Branch**: `023-shortcut-builder`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "To jets DAW nota, stworzyłem kolejnie lepszy parser keyów między macos a windows i linux, przeszukaj kod w poszukiwaniu glyphów macos, trzeba zamienić te znaki na jakiś sensowny builder, postaraj się opracować to API"

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — application, presentation, UI
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The shortcut model lives in `Nota.Application` (pure .NET 10, no Avalonia dependencies) so that the command catalog and presentation models can define shortcuts cleanly. UI bindings and Avalonia `KeyGesture` conversion live in `Nota.App`.

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, AGPL headers, no Polish comments in code),
[`DESIGN.md`](../../DESIGN.md) (Studio visual style, key cap styling),
[`FEATURES.md`](../../FEATURES.md),
`.claude/skills/nota-shortcuts/SKILL.md` (Shortcuts synchronization in Preferences).

---

## User Scenarios & Testing

### User Story 1 - Type-Safe Shortcut Construction in Command Catalog (Priority: P1)

A developer defining or maintaining actions in `CommandCatalog` uses a fluent, type-safe `Shortcut` API (such as `Shortcut.Cmd('S')`, `Shortcut.CmdShift('S')`, `Shortcut.CmdAlt('S')`) rather than hardcoded Unicode strings (`"⌘S"`, `"⌘⇧S"`, `"⌥⌘S"`). The API automatically produces canonical glyph sequences, prevents typos, and enforces consistent modifier ordering.

**Why this priority**: Eliminates brittle magic characters across the catalog and establishes the single source of truth for all command shortcuts.

**Independent Test**: Define commands using `Shortcut.Cmd(...)` in `CommandCatalog.cs`. Verify that `PaletteTests` passes and all command shortcuts round-trip without discrepancies.

**Acceptance Scenarios**:

1. **Given** a command definition in `CommandCatalog`, **When** declared via `Shortcut.Cmd("N")` or `Shortcut.CmdShift("S")`, **Then** it produces the expected canonical glyph string (`"⌘N"`, `"⌘⇧S"`) without string concatenation errors.
2. **Given** a shortcut created with modifier combinations (e.g. `Shortcut.Cmd("S").WithAlt()`), **Then** it outputs a canonical modifier order consistent with project standards.
3. **Given** existing consumers expecting `string`, **When** passing a `Shortcut`, **Then** implicit conversion to `string` satisfies string parameters seamlessly.

---

### User Story 2 - Cross-Platform Display and Key Cap Rendering (Priority: P1)

When running on macOS, shortcuts in tooltips, menus, and the command palette display with standard Apple glyphs (`⌘`, `⌥`, `⌃`, `⇧`, `⌫`, `↵`). When running on Windows or Linux, shortcuts automatically format into platform-idiomatic names (`Ctrl`, `Alt`, `Shift`, `Del`, `Enter`) with `+` separators. In UI views like `CommandPaletteView` and `PreferencesWindow`, the API decomposes shortcuts into discrete key-cap tokens for visual styling.

**Why this priority**: Users on Windows and Linux must see familiar PC key combinations (`Ctrl+Shift+P` rather than `⌘⇧P`), and UI components must not duplicate custom character-parsing logic.

**Independent Test**: Invoke `.ToPlatformString()` and `.ToKeyCaps()` on both macOS and non-macOS environments for various combinations (`⌘⇧P`, `⌥Return`, `⌫`, `F11`); verify string output and key-cap lists match platform conventions.

**Acceptance Scenarios**:

1. **Given** `Shortcut.CmdShift("P")`, **When** rendered on macOS, **Then** `.ToPlatformString()` returns `"⌘⇧P"` and `.ToKeyCaps()` returns `["⌘", "⇧", "P"]`.
2. **Given** `Shortcut.CmdShift("P")`, **When** rendered on Windows or Linux, **Then** `.ToPlatformString()` returns `"Ctrl+Shift+P"` and `.ToKeyCaps()` returns `["Ctrl", "Shift", "P"]`.
3. **Given** a composite UI tooltip text like `"Show parameter-automation lanes (⌘⇧A)"`, **When** processed through `Shortcut.SpellText(text)`, **Then** it renders `"Show parameter-automation lanes (Ctrl+Shift+A)"` on Windows/Linux and remains unchanged on macOS.

---

### User Story 3 - Avalonia KeyGesture Integration (Priority: P2)

When Nota registers shortcuts for menu items or handles key events, the `Shortcut` struct seamlessly converts into Avalonia's `KeyGesture` with correct `KeyModifiers` mapping (`Primary` maps to `KeyModifiers.Meta` on macOS and `KeyModifiers.Control` on Windows/Linux).

**Why this priority**: Replaces the hand-rolled parsing loop in `Commands.Parse` and eliminates discrepancies between menu display and keyboard dispatch.

**Independent Test**: Parse catalog shortcuts and verify `ShortcutAvaloniaExtensions.ToGesture()` generates valid `KeyGesture` instances matching existing behavior.

**Acceptance Scenarios**:

1. **Given** `Shortcut.Cmd("S")`, **When** converted to gesture on macOS, **Then** the modifier is `KeyModifiers.Meta` and key is `Key.S`.
2. **Given** `Shortcut.Cmd("S")`, **When** converted to gesture on Windows/Linux, **Then** the modifier is `KeyModifiers.Control` and key is `Key.S`.
3. **Given** special keys (e.g., `,`, `.`, `Esc`, `Space`, `Return`, `F1`–`F12`), **When** converted to gesture, **Then** the corresponding Avalonia `Key` enum is correctly resolved.

---

### Edge Cases

- Bare keys without modifiers (e.g. `Space`, `Return`, `0`, `F11`): Must format without extraneous `+` signs or delimiters.
- Modifier ordering: Whether specified as `Cmd + Alt` or `Alt + Cmd`, the canonical string representation must produce a deterministic order (`⌥⌘` or `⌘⌥` matching the established catalog convention).
- Backspace / Delete: `⌫` maps to `Del` on Windows/Linux, while mapping to Backspace/Delete gesture appropriately per OS.
- Return / Enter: `↵` formats as `↵` on macOS and `Enter` on Windows/Linux in hints and key caps.
- Free text with dashes or spaces: `Shortcut.SpellText` must preserve punctuation in phrases like `⌥-click` ("Alt-click") or `⌘ + wheel` ("Ctrl + wheel").

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a centralized `KeyGlyphs` class containing Unicode constants (`Cmd = '\u2318'`, `Option = '\u2325'`, `Ctrl = '\u2303'`, `Shift = '\u21E7'`, `Backspace = '\u232B'`, `Return = '\u21B5'`).
- **FR-002**: `KeyGlyphs`, `ShortcutModifiers`, and the `Shortcut` struct MUST reside in `Nota.Application.Input` so they are accessible to `CommandCatalog` without Avalonia dependencies.
- **FR-003**: `Shortcut` MUST provide static factory methods (`Cmd`, `CmdShift`, `CmdAlt`, `Shift`, `Alt`, `None`, `Key`) and fluent chaining methods (`WithShift`, `WithAlt`, `WithCtrl`).
- **FR-004**: `Shortcut` MUST provide a `Parse(string glyphs)` method capable of parsing existing catalog and UI shortcut strings into strongly typed `Shortcut` instances.
- **FR-005**: `Shortcut` MUST implement `ToGlyphs()` returning deterministic, canonically ordered macOS glyph strings.
- **FR-006**: `Shortcut` MUST implement `ToPlatformString()` returning macOS glyphs on macOS and formatted PC text (e.g., `Ctrl+Shift+S`) on Windows and Linux.
- **FR-007**: `Shortcut` MUST implement `ToKeyCaps()` returning an ordered list of individual key cap strings adapted to the running platform.
- **FR-008**: `Shortcut` MUST provide an implicit conversion operator to `string` (`implicit operator string(Shortcut s) => s.ToGlyphs()`) for transparent backward compatibility with existing APIs.
- **FR-009**: System MUST provide Avalonia extension methods in `Nota.App` (`ToGesture(this Shortcut s)`) converting shortcuts to `Avalonia.Input.KeyGesture`.
- **FR-010**: All code comments, docstrings, and headers in new or modified files MUST be written strictly in English with AGPL-3.0-only SPDX licensing headers.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: 100% of command shortcut definitions in `CommandCatalog.cs` use the typed `Shortcut` builder rather than raw Unicode string literals.
- **SC-002**: All existing smoke tests in `Nota.SmokeTest` (including `PaletteTests` catalog consistency checks) pass without failure or regression.
- **SC-003**: Zero hardcoded Unicode key glyph literals (`'⌘'`, `'⌥'`, `'⌃'`, `'⇧'`, `'⌫'`) remain in parsing or spelling loops across `MenuKit`, `Commands`, and `CommandPaletteView`.

---

## Assumptions

- Operating system detection relies on standard `System.OperatingSystem.IsMacOS()`.
- Keyboard bindings on Windows and Linux map macOS `⌘` (Command/Meta) to `Ctrl`.
- Custom-drawn text in views (`SessionView`, `PreferencesWindow`, `StereoMapView`) will leverage `Shortcut.SpellText` or `Shortcut.ToPlatformString()` for consistent presentation across platforms.
