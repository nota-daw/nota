# Implementation Plan: Section renaming — cancel edits and restore automatic names

**Branch**: `cursor/section-automatic-names-d981` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

Represent automatic naming as an empty stored name and derive "Section N" from the section's
position when it is shown. The engine already stores section names in its undoable project
snapshot (specs/016), so text and mode undo, redo, save and load together with no engine, ABI or
file-format change. The rename flyout gains a mode caption and a Reset button; the section menu
and the command palette gain Reset.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia 12
**Testing**: `tests/Nota.SmokeTest` (engine-backed), `scripts/build-linux.sh`
**Constraints**: no audio-thread impact; one logical change.

## Constitution Check

- I. Real-time safety — no engine or audio-thread change. PASS
- II. Boundary discipline — no new C ABI; managed code only. PASS
- III. Licensing — no dependency; new file carries the SPDX header. PASS
- IV. Verification — smoke tests added; build script run before the PR. PASS
- VI. Docs in sync — `FEATURES.md`, `CHANGELOG.md`, Shortcuts pane, MCP descriptions. PASS

## Audio-thread impact and architecture rules

None of the work runs on the audio thread. The naming rules, the rename flyout, the menu,
the palette command, MCP and Remote are managed UI / control-thread code.

- **No allocation, locks, I/O or logging on the audio thread (AR-6)**: nothing new reaches it.
  `SectionNames` allocates strings, but only on the UI / MCP threads.
- **Structural edits publish a new graph snapshot (AR-5)**: a rename or reset still goes through
  `Engine::setSections`, which copies the `Graph` and publishes it. That path is unchanged.
- **Only the C ABI crosses managed/native (AR-7)**: no new ABI. `nota_engine_set_sections` /
  `nota_engine_section_*` are used as before.
- **No JUCE outside `pluginhost/`**: untouched.
- **Transport is the only source of musical time**: the palette command reads the view's
  playhead position, which the Transport already reports, and only reads it.

## Design

- `Nota.Application/Engine/SectionNames.cs` (new): `IsAutomatic(name)`, `Automatic(index)`,
  `Display(name, index)`, `Normalize(typed)` (trim; whitespace → ""), `Displayed(list)`.
- `ArrangementView.Sections.cs`: new sections get `""`; the lane draws `Display`; the rename
  flyout shows caption + placeholder + Reset; Enter commits `Normalize(text)`, Escape / dismiss
  discard; menu gains "Reset to default name"; `ResetSectionNameAtPlayhead()` for the palette.
- `CommandCatalog` + `MainWindow.Commands.cs`: `arrangement.resetSectionName`, no default key.
- `MainWindow.Remote.cs`: Remote gets displayed names.
- `Nota.Mcp/Tools/ProjectTools.cs`: `Section` gains `Automatic`; list returns displayed names;
  descriptions explain empty name = automatic.
- Shortcuts pane: Esc / Return rows for the rename box.

## Project Structure

Source touched: `src/managed/Nota.Application/{Engine/SectionNames.cs, Palette/CommandCatalog.cs}`,
`src/managed/Nota.App/{ArrangementView.Sections.cs, MainWindow.Commands.cs, MainWindow.Remote.cs, PreferencesWindow.cs}`,
`src/managed/Nota.Mcp/Tools/ProjectTools.cs`, `tests/Nota.SmokeTest/{SectionNamingTests.cs, Program.cs}`.
