# Implementation Plan: Menu button and a hideable menu bar

**Branch**: `feat/menu-button-title-bar` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/093-menu-button-title-bar/spec.md`

## Summary

On Windows and Linux the in-window menu row goes away by default and a ☰ Menu key leads the
transport bar. Its menu is produced, each time it opens, from the very `NativeMenu` the menu
row renders (MainWindow.axaml) — the one menu definition, whose items already carry the
command catalogue's labels, shortcut gestures and check marks — so nothing is listed twice.
A new `ShowMenuBar` user setting (default off) brings the row back; it is switched from
Settings → Appearance and from a new `view.menuBar` check item in View, following the
existing "Track headers on the right" pattern (setting + View check item kept in step through
`Settings.Changed`). With the row hidden the window opens the Menu key's menu itself on F10
and on a lone Alt tap. On Windows the title-bar caption reads the OS window title
("Nota — My song"). macOS is untouched: no key, no setting, no View item, no key handling.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: the Menu key is
  a `seg view` key in its own `segmented view` well, the same 28-px icon key as the view
  switch; the glyph is drawn as geometry in `Glyph.cs` (no icon font, no emoji).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): AGPL headers on new files, no
  new dependency.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12.1.4 (already used) — no new dependency

**Storage**: the user settings file (`Settings.cs`, saved by `ISettingsService`): one new bool,
`ShowMenuBar`, default `false`

**Audio-Thread Impact**: None — UI-only change; no engine call is added or changed

**Testing**: `tests/Nota.SmokeTest` (console smoke runner): a new `MenuMirrorTests` check that
builds a `NativeMenu` in code and compares it with the Menu key's mirrored menu; the existing
`PaletteTests.RegistryDrift` source checks cover the new `view.menuBar` command (bound, in the
menu); manual / harness checks per [quickstart.md](quickstart.md)

**Target Platform**: Windows and Linux (behaviour); macOS (must stay identical)

**Project Type**: desktop app

**Performance Goals**: the Menu key's menu opens with no visible delay (~150 items mirrored per
open — negligible); showing / hiding the row applies within one frame

**Constraints**: one menu definition (FR-003); no stale state (FR-005); macOS unchanged (FR-020)

**Scale/Scope**: ~8 files in `Nota.App`, 1 in `Nota.Application` (setting), 1 catalogue entry,
localisation keys, 1 smoke test file, docs (CHANGELOG, FEATURES, nota-docs EN + RU)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|---|---|
| 0. Approved issue first | ✅ nota-daw/nota#518 carries `approved` |
| I. Real-time audio safety | ✅ no audio-thread code touched |
| II. Boundary discipline | ✅ no C ABI / JUCE change; Avalonia stays in `Nota.App` |
| III. Licensing hygiene | ✅ new files carry the two-line SPDX header; no new dependency |
| IV. Verification gate | ✅ `scripts/build.sh` incl. smoke test before PR |
| V. One logical change per PR | ✅ the menu / title chrome of #518 only (#41 out) |
| VI. Documentation in sync | ✅ CHANGELOG, FEATURES.md, nota-docs EN + RU, Settings → Shortcuts |
| VII. Spec-first | ✅ this spec / plan / tasks |
| VIII. Safety rules not weakened | ✅ none touched |

Post-design re-check: unchanged — all pass.

## Project Structure

### Documentation (this feature)

```text
specs/093-menu-button-title-bar/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── ui.md            # the user-facing contract: Menu key, setting, View item, keys
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                      # + ShowMenuBar (default false)
│   ├── Palette/CommandCatalog.cs        # + view.menuBar ("Show menu bar")
│   └── Localization/en.json (+ others)  # + main.show_menu_bar, main.menu, preferences.menu_bar(_caption), commands.show_menu_bar
└── Nota.App/
    ├── MenuMirror.cs                    # NEW: NativeMenu → MenuItem tree, rebuilt per open (Avalonia base only; linked into the smoke test)
    ├── MainWindow.MenuButton.cs         # NEW: the Menu key, ShowMenuBar apply, Alt-tap / F10, menu-row attach/detach
    ├── MainWindow.axaml                 # Menu key at the start of the transport bar; View ▸ Show menu bar item; x:Name on the menu-row border
    ├── MainWindow.MenuKeys.cs           # drop view.menuBar on macOS (as file.exit)
    ├── MainWindow.Commands.cs           # bind view.menuBar → ToggleMenuBar
    ├── MainWindow.Files.cs              # Windows caption = Title
    ├── MainWindow.axaml.cs              # wire InitMenuButton(); Settings.Changed → ApplyMenuBar
    ├── MainWindow.Input.cs              # route key down / up to the Alt-tap / F10 detector
    ├── MainWindow.TransportFit.cs       # (no change: the key is not in the shed order — documented)
    ├── PreferencesWindow.cs             # Appearance: "Menu bar" Show/Hide picker (not on macOS); Shortcuts: Alt / F10 row (not on macOS)
    └── Controls/Glyph.cs                # + GlyphKind.Menu (three strokes)

tests/Nota.SmokeTest/
├── MenuMirrorTests.cs                   # NEW
├── Nota.SmokeTest.csproj                # link MenuMirror.cs
└── Program.cs (or the suite list)       # register the new suite
```

**Structure Decision**: all behaviour lives in `Nota.App` next to the existing menu code
(`MainWindow.MenuKeys.cs`, `MainWindow.RecentMenu.cs`); the mirroring is a separate,
Avalonia-base-only file so the smoke test can compile it in, as it already does for
`ShortcutAvaloniaExtensions.cs` and others.

## Complexity Tracking

No constitution violations — nothing to justify.
