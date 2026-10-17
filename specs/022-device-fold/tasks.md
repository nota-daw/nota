# Tasks: Fold a device into a narrow strip

**Input**: [spec.md](spec.md), [plan.md](plan.md), [contracts/c-abi.md](contracts/c-abi.md)

## Phase 1 — Engine + ABI

- [x] T001 Add a message-thread `folded` flag to `Device`, `MidiDevice` and `Instrument` (src/native/nota.engine/src/{Device.h,MidiDevice.h,Instrument.h})
- [x] T002 `Engine::setTrackDeviceFolded/trackDeviceFolded` (index -1 = instrument) and `setMidiEffectFolded/midiEffectFolded` (src/native/nota.engine/src/{Engine.h,Engine_Devices.cpp})
- [x] T003 Export the four C functions and document them (include/nota/nota_engine.h, src/nota_engine_device.cpp)

## Phase 2 — Managed + persistence

- [x] T004 P/Invoke + `NotaEngine` + `IAudioEngine` members (Nota.Infrastructure/Interop, Nota.Application/Engine/IAudioEngine.cs)
- [x] T005 `Folded` on `DeviceDto`, `MidiDeviceDto`, `InstrumentDto`; capture + apply in `ProjectService`; format v23 + no-op migration

## Phase 3 — Devices panel (US1)

- [x] T006 Double-click on a card header outside its controls toggles the fold; Fold / Unfold in the header menu (DeviceChainView.Shell.cs)
- [x] T007 `FoldedStrip`: 28 px strip, bypass switch on top, rotated name, selection ring, menu, drag (DeviceChainView.Shell.cs)
- [x] T008 Effect, MIDI-effect, instrument and plug-in builders return the strip when folded, without building the body (DeviceChainView*.cs)

## Phase 4 — Verification + docs

- [x] T009 Smoke test: ABI round-trip, out-of-range safety, fold follows move and delete + undo, save/load for all three kinds, v22 file opens unfolded (tests/Nota.SmokeTest/DeviceFoldTests.cs, Program.cs `--fold`)
- [x] T010 CHANGELOG `[Unreleased]`, FEATURES.md, nota-docs devices page EN + RU
- [ ] T011 `scripts/build-linux.sh` green; screenshots of the real app for the PR
