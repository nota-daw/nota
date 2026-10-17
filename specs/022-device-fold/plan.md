# Implementation Plan: Fold a device into a narrow strip

**Branch**: `022-device-fold` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

## Summary

The fold is a per-device flag kept **on the engine's device object** (beside bypass), so it
follows the device through every structural edit the engine already handles — move, remove,
undo/redo of a removed device (the retained graph snapshot holds the same object) — with no
managed-side index bookkeeping. The Devices panel reads it while rebuilding the chain and,
for a folded device, builds a 28 px strip instead of the card (the body is not built). The
project format stores it as `folded` on each device, MIDI-effect and instrument DTO (v23).

## Technical Context

- Engine: `Device` / `MidiDevice` / `Instrument` get a `folded` flag. Message thread only
  (written by the UI, read by the UI and by project save); the audio thread never touches it,
  so it is a plain `bool` — no atomics, no command queue (AR-4/AR-6 unaffected).
- C ABI (`nota_engine.h`): two pairs — `nota_track_set_device_folded` /
  `nota_track_device_folded` (device index -1 = the instrument, as in the device-name and
  plugin-param calls) and `nota_midi_effect_set_folded` / `nota_midi_effect_folded`. See
  [contracts/c-abi.md](contracts/c-abi.md).
- Managed: `NativeMethods` + `NotaEngine` + `IAudioEngine` (`SetDeviceFolded`,
  `DeviceFolded`, `SetMidiEffectFolded`, `MidiEffectFolded`).
- Persistence: `DeviceDto.Folded`, `MidiDeviceDto.Folded`, `InstrumentDto.Folded`; format
  v22 → v23 with a no-op migration (absent = false = unfolded).
- UI: `DeviceChainView.Shell.cs` — a double-click (`ClickCount == 2`) on the header, outside
  its controls, toggles the fold; a `FoldedStrip` builder; "Fold" / "Unfold" in the header
  menu. The four card builders (effect, MIDI effect, instrument, plug-in) return the strip
  early when the device is folded. `ClickCount` is used rather than `DoubleTapped` because the
  first click selects the card and rebuilds the row, so the second click lands on a new control.

## Constitution Check

- I Real-time safety: the flag is never read on the audio thread. ✓
- II Boundary: new surface is pure C (`int32_t` in/out). ✓
- III Licensing: no new files outside specs except a test file with the SPDX header; no dependencies. ✓
- IV Verification: smoke test covers the ABI, move/undo following and save/load. ✓
- V One change: fold only. ✓
- VI Docs: CHANGELOG, FEATURES.md, nota-docs devices page (EN + RU). ✓

## Project Structure (touched)

```
src/native/nota.engine/include/nota/nota_engine.h
src/native/nota.engine/src/{Device.h,MidiDevice.h,Instrument.h,Engine.h,Engine_Devices.cpp,nota_engine_device.cpp}
src/managed/Nota.Infrastructure/Interop/{NativeMethods.Devices.cs,NotaEngine.Devices.cs}
src/managed/Nota.Application/Engine/IAudioEngine.cs
src/managed/Nota.Infrastructure/Persistence/{ProjectModel.cs,ProjectService.cs,ProjectMigrations.cs}
src/managed/Nota.App/DeviceChainView*.cs
tests/Nota.SmokeTest/{DeviceFoldTests.cs,Program.cs}
CHANGELOG.md, FEATURES.md
```
