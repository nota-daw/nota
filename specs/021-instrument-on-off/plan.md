# Implementation Plan: An On/Off Switch for Instruments

**Branch**: `fix/issue-148-lksrgg` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

## Summary

`Instrument` gains the same relaxed atomic bypass flag `Device` has. The engine's device
bypass call treats device index −1 as the instrument (as names, parameters and state already
do). `playEvents`, which drives every instrument render (arrangement, live notes, Session
slots), checks the flag: on the first block after the switch goes off it renders as usual,
applies a linear fade to zero, then calls `allNotesOff` and releases held controllers; from
the next block it returns at once, leaving the zeroed buffer. The MIDI pre-pass and the
insert chain are untouched. The card shells pass `Bypassable: true` and route the switch to
index −1; the project stores `instrument.bypassed`; MCP gains the −1 meaning.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed)

**Primary Dependencies**: none new

**Storage**: `project.json` → `tracks[].instrument.bypassed` (optional, default false; no
format bump — older builds keep it in the DTO's extension data)

**Audio-Thread Impact**: one relaxed atomic load per instrument per block, plus a
plain `bool` the audio thread alone writes (`Instrument::offSettled`). No locks, no
allocation. Off saves the instrument's whole render.

**Testing**: smoke test `InstrumentBypassTests` (`--instrument-off`, also in the full run)

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ atomic flag, no alloc / lock; fade then early return |
| II. Boundary discipline | ✅ existing C ABI call, documented meaning for −1 |
| III. Licensing hygiene | ✅ new test file has the SPDX header; no dependency |
| IV. Verification gate | ✅ smoke test |
| V. One logical change | ✅ #148 only |
| VI. Docs in sync | ✅ CHANGELOG, FEATURES.md, nota-docs EN + RU |
| VII. Spec-first | ✅ |
| VIII. Rules not weakened | ✅ |

## Files

- Engine: `Instrument.h` (flag), `Engine_Render.cpp` (`playEvents`), `Engine_Devices.cpp`
  (−1 in set/get bypass), `Engine_Tracks.cpp` (duplicate), `Engine.h` + `nota_engine.h`
  (comments).
- App: `DeviceChainView.Instruments.cs`, `DeviceChainView.Plugin.cs`,
  `DeviceChainView.Shell.cs` (switch, tooltip, index −1).
- Persistence: `ProjectModel.cs` (`InstrumentDto.Bypassed`), `ProjectService.cs`.
- MCP: `DeviceTools.cs` (description), `ProjectTools.cs` (`InstrumentOff`).
- Tests: `tests/Nota.SmokeTest/InstrumentBypassTests.cs`, `Program.cs`.
