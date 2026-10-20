# Implementation Plan: Nota Vocoder audio effect

**Branch**: `feat/vocoder-effect` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/028-vocoder-effect/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

A new built-in audio effect, **Nota Vocoder** (builtin kind **26**): a classic channel vocoder
in one header, `src/native/nota.engine/src/Vocoder.h` (`: public Device`). The track the
effect sits on is the **modulator** (the voice). The **carrier** is an internal band-limited
saw at a **Pitch** (the default — a robot voice at once), white noise, or another track that
comes in through the **existing device sidechain** (`acceptsSidechain() → true`, the engine's
route buses, pre / post tap, generic project persistence and the MCP `set_device_sidechain` /
`get_device_sidechain` tools), so no new routing, ABI or project format is needed. 8–32
analysis band-passes with envelope followers drive matching synthesis band-passes (shifted
by **Formant**), plus sibilance-gated unvoiced noise, Mix and Gain. Live data for the card
goes out through the generic `nota_device_scope`. The managed side is the light
audio-effect path: `VocoderDeviceBody` (700 × 260), a `VocoderGraph` control, catalogue /
category entries, factory presets, smoke test. (The model was turned round after user
testing — research R-15; the earlier "track = carrier" plan text below is kept where it
does not conflict.)

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (DSP class, kind registration across C++ and C#, device-card UI).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

All five apply. The design reference is [`design/vocoder/index.html`](../../design/vocoder/index.html).

## Technical Context

**Language/Version**: C++20 (engine DSP + three factory switches), C# / .NET 10 (card,
catalogue, presets, MCP description, smoke test).

**Primary Dependencies**: None new. Avalonia (existing) for the card.

**Storage**: Generic — device params are persisted by `builtinKind()` + param values;
the sidechain source / tap / gain are already in `ProjectModel` (format v16). No format bump.

**Audio-Thread Impact** *(required — state explicitly)*: New DSP runs inside
`Device::process` on the audio thread. All buffers (band filter states for 32 bands × 3
paths, scope buffer) are fixed-size members, sized at construction — no allocation, lock or
I/O (AR-6). Params are `std::atomic<float>` written by the message thread (existing device
param path, AR-4). Filter coefficients are recomputed at control rate (every 32 samples)
when a param moves; band-count changes are applied by a short output duck, not by
reallocating. The modulator buffer is the engine's previous-block route bus (read-only,
existing). Scope data is written to an atomic-free float array that the UI reads with
torn reads allowed (existing `scope()` contract).

**Testing**: `tests/Nota.SmokeTest` (kind / name / param count / defaults / round-trip /
clone / render: carrier + modulator → audible and finite, no modulator → silent wet,
modulator silent → silent wet, determinism of two offline renders); the `--palette` and
design guard checks; a visual check of the card per `nota-ui-verify`.

**Target Platform**: macOS (arm64 + x86_64), Windows, Linux — the engine is portable C++.

**Project Type**: Desktop DAW (native engine + managed UI).

**Performance Goals**: ≤ the CPU of Nota Chamber per instance at 32 bands, 48 kHz
(SC-004); 8 instances without xruns at a 128-sample buffer.

**Constraints**: Zero added latency (no PDC). Click-free param changes. The modulator is
one block late (route-bus contract) — accepted, see research R-2. At most 8 distinct
sidechain source tracks per project (`Engine::kMaxRoutes`), shared with other devices.

**Scale/Scope**: 1 DSP header (~600 lines), 3 engine switch entries, 1 card body + 1 graph
control, catalogue / category / presets, smoke-test block, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I. Real-Time Audio Safety | Pass | Pre-sized state, atomics for params, no alloc / lock / I/O in `process`; band-count change without reallocation (R-4). |
| II. Boundary Discipline | Pass | No new ABI: params, sidechain and scope already cross through `nota_engine.h`. Only the `nota_device_accepts_sidechain` doc comment gains "Nota Vocoder". No JUCE. |
| III. Licensing Hygiene | Pass | New files carry the SPDX header; no dependency. |
| IV. Verification Gate | Pass | Smoke test extended (see Testing). |
| V. One Logical Change per PR | Pass | One effect, one PR; the mockup in `design/vocoder/` goes with it or is dropped before merge (user's call). |
| VI. Documentation Stays in Sync | Pass | CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs` (EN + RU page for the effect). No shortcut changes. |
| VII. Spec-First | Pass | This spec → plan → tasks. |
| VIII. Safety rules not weakened | Pass | Nothing bent. |

**Post-design re-check (after Phase 1)**: unchanged — Pass on all eight. The design adds no
ABI, no format change and no allocation path.

## Project Structure

### Documentation (this feature)

```text
specs/028-vocoder-effect/
├── plan.md              # This file
├── research.md          # Phase 0: DSP and integration decisions
├── data-model.md        # Phase 1: params, scope layout, sidechain state
├── quickstart.md        # Phase 1: how to verify end-to-end
├── contracts/
│   ├── device-params.md # the param table (persisted, append-only) + scope layout
│   └── mcp.md           # what MCP exposes and how
└── tasks.md             # /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h              # doc comment only: accepts_sidechain lists the Vocoder
└── src/
    ├── Vocoder.h                           # NEW — the DSP (Device, builtinKind 26)
    └── Engine_Devices.cpp                  # +include, +case 26 in addTrackBuiltinDevice / cloneDevice / makeBuiltinDevice

src/managed/
├── Nota.App/
│   ├── DeviceCards/Bodies/VocoderDeviceBody.cs   # NEW — the 700 × 260 card
│   ├── DeviceCards/DeviceCardFactory.cs          # [26] = new VocoderDeviceBody()
│   └── Controls/VocoderGraph.cs                  # NEW — envelope graph (NotaGraph), Low / High edge drag
├── Nota.Application/Palette/
│   ├── BuiltinDeviceCatalog.cs                   # "Nota Vocoder", AudioEffect, kind 26, descriptor
│   └── DeviceCategories.cs                       # under "Modulation"
├── Nota.Infrastructure/Persistence/
│   └── FactoryPresetCatalog.cs                   # Fx("vocoder", 26, …) × 6
└── Nota.Mcp/Tools/DeviceTools.cs                 # sidechain tool descriptions mention the Vocoder

tests/Nota.SmokeTest/Program.cs                    # Vocoder block (+ a helper file if Program.cs grows)

CHANGELOG.md · FEATURES.md · ../nota-docs (EN + RU)
```

**Structure Decision**: The light built-in audio-effect path from the `nota-instrument`
skill (§2 "Audio effects"): one DSP header, three engine switches, one card body. The
modulator reuses the device sidechain end to end (engine routing, persistence, MCP), the
card's live data reuses `nota_device_scope`. Nothing in `Engine.h`, the C ABI, the project
model or the add / drop path changes.

## Complexity Tracking

No constitution violations; nothing to justify.
