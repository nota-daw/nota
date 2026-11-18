<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Mapped move spans and scroll extent

- **Slug**: tempo-map-move-scroll (continued from the existing assessment)
- **Fixed**: 2026-11-14
- **Assessment**: ./assessment.md
- **Status**: applied
- **Issue**: #535, open and approved; PR #568

## Summary

Audio moves now lift automation over the clip's original beat span while clearing
and carving over its destination span. Mapped scrolling derives its last origin
from project-end seconds minus viewport duration, independent of the current origin.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | Separate source and landing lengths in single, cross-track and block moves, including mix-only source removal. |
| `src/managed/Nota.App/ArrangementView.cs` | modified | Calculate mapped maximum from end time, clamp origin, then refresh viewport size; preserve the ordinary beat axis. |
| `tests/Nota.SmokeTest/TempoMapMoveTests.cs` | added existing draft test | Exercise both tempo directions, four move paths, automation lock, layout filtering and undo/redo. |
| `tests/Nota.SmokeTest/Program.cs` | modified | Register move regressions in focused and full runs. |
| `tests/Nota.TempoUiTest/Program.cs` | modified | Real-view scrollbar regressions and merge-specific keyboard/gesture checks. |
| `CHANGELOG.md` | modified | Preserve both branches' entries; keep unreleased tempo work out of historical releases; record fixes. |
| `src/managed/Nota.App/PreferencesWindow.cs` | conflict resolved | Retain develop's extracted, editable Shortcuts pane. |
| `src/managed/Nota.Application/Input/ShortcutLayout.cs` | modified | Retain all five tempo mouse gestures in the new layout. |
| `src/managed/Nota.App/ArrangementView.Tempo.cs` | modified | Read shared Delete/Cancel bindings, respect shortcut capture, and allow cancellation while mapping modifiers are held. |
| `src/managed/Nota.Application/Localization/en.json` | modified | Drop two obsolete fixed-key descriptions superseded by shared editing actions. |

## Tests Added or Updated

- The existing move-test draft contributes 42 checks: all four move paths in both
  directions, locked automation, source-neighbor preservation, destination clearing,
  device-layout filtering and undo/redo.
- The production headless view covers fast-to-slow, slow-to-fast, mapped constant
  and legacy constant tempo; scrollbar maximum stability, actual final-clip
  visibility, zoom, zero width, content shorter than the viewport and preview maps.
- Merge checks cover rebound Delete/Cancel, shortcut capture, Ctrl+Alt cancellation
  with a modified binding, and preservation of the tempo gesture group.

## Local Verification

- Rebuilt the unmodified native move implementation after merging develop; the new
  focused tests failed 14 assertions (`build/pr568-baseline-tempo.log`).
- Before the scrollbar fix, six headless checks failed, including final-clip reach
  (`build/pr568-baseline-ui.log`).
- After remediation: all 143 focused tempo checks and 73 headless checks passed.
- Complete build and final headless verification are recorded in `test.md`.

## Deviations from Assessment

- Also merged `origin/develop` at `8390c344` into PR head `91c01299`. The only text
  conflicts were the changelog and PreferencesWindow's obsolete static shortcut
  list. Develop introduced rebindable keys, requiring the tempo lane and its
  reference rows to join that system rather than restore duplicate UI code.
- No extension hooks are registered. The existing assessment is retained verbatim.

## Follow-ups

- No dependency, ABI, new mouse gesture or audio-thread work was added. The
  automation changes run on the authoring thread.
- nota-features/nota-docs audit: the corrections restore documented automation
  follow and navigation behavior; capability descriptions and workflow defaults
  remain valid. No sibling documentation files were changed. The sibling checkout's
  shortcut generator still expects the pre-develop static list; regeneration needs
  that repository's update for the new catalog/layout.
- Hosted CI and performance checks still need to run when the local branch is pushed.
