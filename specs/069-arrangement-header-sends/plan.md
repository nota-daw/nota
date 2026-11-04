# Implementation Plan: Arrangement header sends, Large only

**Branch**: `cursor/arrangement-header-sends-2734` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Keep the header send knobs from specs/072. Show them only when the row is at least the Large preset (128 px). Remove the **Sends** key and its saved flag. Stop recording send moves as undo steps, and stop send lanes from being created or played.

## Technical Context

**Language/Version**: C# / .NET 10 and C++20. The C++ change rejects a mix kind and an automation target that specs/072 added. No new ABI entry.

**Primary Dependencies**: None added.

**Storage**: Send levels are unchanged. The track-view sidecar no longer writes `showSends`. A saved `showSends: false` is ignored, so Large rows show their knobs.

**Audio-Thread Impact**: None added. `applyAutomation` no longer calls `setSend` for a send lane, so playback does not write that atomic from an envelope. The live send level is still the atomic the audio thread already reads. No allocation, lock, I/O or logging on the audio thread. No new graph snapshot on the audio thread. No JUCE. Transport is untouched.

**Testing**: `scripts/build-linux.sh`. `tests/Nota.SmokeTest/SendAutomationTests.cs` is rewritten to fail if a send move becomes an undo step or a send lane is created.

**Target Platform**: Built and tested on Linux. macOS and Windows are CI.

**Project Type**: Desktop app.

**Performance Goals**: Header rebuild is unchanged. One less control in the zoom strip.

**Constraints**: Do not renumber `AutomationTarget` or `MixKind`. Send stays value 5 and TrackSend stays 8 so a stored lane does not land on another target. Both are rejected.

**Scale/Scope**: Header visibility, the send-control wiring, the lane menu, MCP `set_send` and `add_automation_lane`, and the engine apply paths.

## Constitution Check

- **I. Real-time**: Pass. The audio thread does not gain work. One existing write from a send lane is removed.
- **II. Boundary**: Pass. No new C ABI. `nota_engine.h` says the send mix kind is ignored.
- **III. Licensing**: Pass. No new file, no new dependency.
- **IV. Verification**: Pass. Smoke test covers the engine half (not an undo step, lane rejected).
- **V. One change**: Pass. #143 as the maintainer decided it.
- **VI. Docs**: CHANGELOG, FEATURES, arrangement page EN + RU.
- **VII. Spec-first**: this directory.
- **VIII. Safety**: Pass. Nothing weakened.

Post-design: the same.

## Project Structure

```text
specs/069-arrangement-header-sends/
src/managed/Nota.App/ArrangementView.Sends.cs
src/managed/Nota.App/ArrangementView.cs
src/managed/Nota.App/ArrangementView.Sizing.cs
src/managed/Nota.App/ArrangementView.Automation.cs
src/managed/Nota.App/SendControl.cs
src/managed/Nota.Mcp/Tools/TrackTools.cs
src/managed/Nota.Mcp/Tools/AutomationTools.cs
src/native/nota.engine/src/engine/Engine.cpp
src/native/nota.engine/src/engine/Engine_Automation.cpp
src/native/nota.engine/src/engine/Automation.h
src/native/nota.engine/include/nota/nota_engine.h
tests/Nota.SmokeTest/SendAutomationTests.cs
```

## Design

`ShowSendsOn` is true only for a non-return card whose height is at least `LargeRowHeight`, when the project has returns. The live relayout already rebuilds a card when `ShowSendsOn` flips, including on drag-release.

`SendControl` writes `SetTrackSend` and nothing else. Mixer, Session and the Sends card already use it, so they stop opening a mix gesture and an automation write.

`mixValue` returns false for `MixKind::TrackSend`, so a gesture that names it records no step. `applyAutomation` skips `AutomationTarget::Send`. `addAutomationLane` returns -1 for it. The lane menu no longer lists sends. MCP `set_send` calls `SetTrackSend` directly. MCP `add_automation_lane` with `"send"` returns -1.
