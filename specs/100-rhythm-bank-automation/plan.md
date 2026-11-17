# Implementation Plan: Automate the Nota Rhythm pattern bank

**Branch**: `feat/rhythm-bank-automation` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/100-rhythm-bank-automation/spec.md`

## Summary

Nota Rhythm's bank becomes two appended plugin params — `bank` (A–D) and `bank_switch`
(Step / Pattern) — so automation, MIDI learn and rack macros drive it like any control. Because
the engine applies automation once per audio block, a new `Instrument::pluginParamLane` hook
hands an instrument the lane of one of its params together with the block's beat range; the
instrument samples the lane at the grid position of every step in that range into a small fixed
table, and its sequencer reads the table at the step. The bank (and Nota Twin's sequencer
Pattern, which has the same late-switch defect) then follow the lane exactly, at any block
size, in real time and in export. The Rhythm card's A–D buttons write the param as a discrete
control edit and follow the playing bank; a queued bank (Pattern mode) is tinted.

## Nota: Read Before Planning

- Engine: [`ARCHITECTURE.md`](../../ARCHITECTURE.md) AR-5 (the lane lives in the immutable
  Graph being rendered), AR-6 (no alloc / lock / I/O: the table is a fixed array), AR-9 (the
  beat range comes from the block's transport position).
- UI: [`DESIGN.md`](../../DESIGN.md), `nota-design` skill — the bank segment keeps its look;
  a queued bank is a tint of the playing bank's fill.
- `nota-instrument` skill: the param layout is append-only and persisted — new state magic.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (app, MCP)

**Primary Dependencies**: none new

**Storage**: the Rhythm state blob gains a magic `RTH4` (95 params); RTH1–RTH3 still load and
set `bank` from the saved bank byte

**Audio-Thread Impact**: `applyAutomation` makes one extra virtual call per instrument
plugin-param lane per block (no-op by default). Rhythm / Twin evaluate the lane at ≤ 32 grid
points per block into a fixed array; no allocation, no lock. The lane is only read during the
call, never kept.

**Testing**: `tests/Nota.SmokeTest` (end-to-end through the C ABI): bank params and names,
exact lane timing (bank switches on the downbeat at odd block sizes), real-time vs. chunked
render equality, Pattern mode, RTH3 load, Twin pattern lane on a bar line.

**Target Platform**: macOS / Windows / Linux desktop

**Project Type**: desktop app (engine + managed UI)

**Performance Goals**: no measurable change to the perf gate (the hook is a no-op for every
instrument without such a lane)

**Constraints**: param order is persisted — append only; saved projects must not change

**Scale/Scope**: 2 instruments, ~10 source files

## Constitution Check

- AGPL headers: one new header (`devices/ParamLaneSteps.h`) carries it. ✅
- No new dependency. ✅
- Audio thread: fixed table, read-only lane access in the block that owns the Graph. ✅
- C ABI: unchanged — params already cross; `nota_engine.h` untouched. ✅
- Smoke test extended. ✅
- CHANGELOG / FEATURES / user docs planned. ✅

## Project Structure

### Documentation (this feature)

```text
specs/100-rhythm-bank-automation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/instrument-param-lane.md
└── tasks.md
```

### Source Code (repository root)

```text
src/native/nota.engine/src/
├── devices/Instrument.h                    # + pluginParamLane hook
├── devices/ParamLaneSteps.h                # new: per-step lane samples of one block
├── devices/instruments/RhythmMachine.{h,cpp}  # bank / bank_switch params, RTH4, playing bank
├── devices/instruments/Twin.{h,cpp}        # GSeqPattern read through the lane table
└── engine/Engine_Automation.cpp, Engine.h, Engine_Render.cpp  # pass the lane + block beat range
src/managed/
├── Nota.Application/Instruments/RhythmModel.cs   # ids, RTH4 parse, guide
├── Nota.App/DeviceCards/Instruments/RhythmInstrumentCard.cs  # buttons write the param, follow it
├── Nota.Mcp/Tools/RhythmTools.cs                 # snapshot + select bank via the param
└── localization (EN/RU strings for the new tooltip)
tests/Nota.SmokeTest/Program.cs
CHANGELOG.md, FEATURES.md, ../nota-docs (Rhythm page EN + RU)
```

**Structure Decision**: engine hook + two instruments; managed changes stay in the Rhythm model,
card and MCP tool.

## Complexity Tracking

None.
