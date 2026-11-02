<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Implementation Plan: Logic Grey theme

**Branch**: `dev/windows-fixes` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

## Summary

Register a complete logic-grey.json preset in NotaThemes.BuiltInOrder. Reuse existing
Appearance selection, settings persistence, live palette slots and repaint service.
Add Playhead and SurfaceRaisedTop semantic slots. Old themes map them to their existing
Accent and SurfaceRaised values. Logic Grey gets a gold cursor and a slight static raised
gradient; equal stops retain the original solid brush. No new controls or engine changes.

## Nota: Read Before Planning

Reviewed CONTRIBUTING.md, ARCHITECTURE.md, constitution, DESIGN.md and nota-design.
The user's explicit subtle-gradient request permits a centrally implemented raised-fill
exception for this theme; existing geometry and readability requirements still apply.

## Technical Context

**Language/Version**: C# / .NET 10, JSON, existing Python design generator.
**Primary Dependencies**: existing Avalonia 12.1.4; no additions.
**Storage**: existing ColourTheme/SystemDarkTheme/SystemLightTheme settings.
**Audio-Thread Impact**: none; colour resources only.
**Testing**: existing readability/appearance smoke checks plus actual-window diagnostics.
**Target Platform**: Windows 11 x64.
**Project Type**: desktop DAW.
**Performance Goals**: no new animation/per-frame allocation; compare playback frame cadence.
**Constraints**: original palettes/default, track/note/status hues and prior playhead work preserved.
**Scale/Scope**: one preset, two palette slots, one computed shared resource.

## Constitution Check

Pre/post design: PASS. No dependency, native ABI, DSP, graph or JUCE changes. New sources
carry AGPL headers. Windows build/smoke gate required. Design/feature/changelog docs updated.

## Project Structure

Feature documents: spec, plan, research, data-model, contracts/theme, quickstart, tasks,
validation; reproducible UI diagnostics and screenshots under validation/.
Source: Theme/Themes/*.json, NotaColourTheme.cs, NotaPalette.cs, NotaThemeService.cs,
NotaTheme.axaml; cursor brush aliases in ArrangementView, PianoRollView, AudioClipEditorView;
DesignTokenCheck.Themes.cs; scripts/design-html.py; DESIGN.md/html, FEATURES.md, CHANGELOG.md.

**Structure Decision**: extend the existing semantic palette and selector.
Keep KeyMap's RaisedFill alias as the representative SurfaceRaised colour. ApplyResources
publishes the computed IBrush after palette changes; shared XAML raised fills use DynamicResource.
View code constructs no gradients. Data colours retain Studio Dark values.

## Complexity Tracking

No constitution violations. The only design exception is the user-requested static raised
fill; original themes receive their exact prior solid brush. No theme-ID branches in views.

## Validation

Run existing theme/data checks for all five presets and both gradient endpoints.
Exercise actual Appearance selection, new-process persistence, project-data invariance,
dark/light/system transitions and requested 100/125/150/200% scales. Capture Arrangement,
piano roll and mixer PNGs. Compare high-refresh frame cadence against Studio Dark.
Run scripts/build-win.ps1 once stable; record all results and feature-modified files.
Sibling C:/Dev/nota-docs is absent: record an in-repo manual handoff, not external edits.
