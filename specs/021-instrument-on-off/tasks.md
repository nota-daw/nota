---

description: "Task list for 021 An On/Off Switch for Instruments"
---

# Tasks: An On/Off Switch for Instruments

**Input**: [spec.md](spec.md), [plan.md](plan.md)

**Tests**: `tests/Nota.SmokeTest/InstrumentBypassTests.cs`

## Phase 1: Engine

- [x] T001 `Instrument.h`: atomic `bypassed()` / `setBypassed()`, audio-thread `offSettled`
- [x] T002 `Engine_Render.cpp` `playEvents`: fade the switch-off block, release voices and held controllers, then skip rendering
- [x] T003 `Engine_Devices.cpp`: device index −1 in `setTrackDeviceBypassed` / `trackDeviceBypassed`; comments in `Engine.h`, `nota_engine.h`
- [x] T004 `Engine_Tracks.cpp`: a duplicated track keeps the instrument's switch

## Phase 2: App (US1, US2)

- [x] T005 Instrument shells (built-in, racks, hosted plug-in) `Bypassable: true`, read the state from index −1
- [x] T006 `BuildCardShell`: the switch writes index −1 for an instrument; on/off tooltip

## Phase 3: Persistence and MCP (US3)

- [x] T007 `InstrumentDto.Bypassed`, captured and applied in `ProjectService`
- [x] T008 MCP: `set_device_bypass` documents −1; `get_overview` reports `InstrumentOff`

## Phase 4: Verification and docs

- [x] T009 Smoke: fade, exact silence, release, next note, reverb tail, switches separate, clip playback, duplicate, save / load, older project, MCP
- [x] T010 CHANGELOG, FEATURES.md, nota-docs (EN + RU)
- [x] T011 `scripts/build-linux.sh` green (`CI=true`)
