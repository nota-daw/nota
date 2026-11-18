# Implementation Plan: Export one Strata layer on its own

**Branch**: `feat/strata-layer-export` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/102-strata-layer-export/spec.md`

## Summary

Strata already keeps every layer as its own loop-aligned stereo buffer (sample 0 = the
loop's top, written after Input gain; Speed, Reverse, Feedback, level and mute are
applied only at playback) and already persists them in its state blob. So a layer
export needs no engine change: read the device state blob on the UI thread, cut out
layer N's PCM, write it as a 32-bit float stereo WAV. The work is a small parser/
writer in `Nota.Infrastructure` (testable from the smoke test), a right-click menu on
each layer row in `StrataDeviceBody`, strings, smoke-test checks and docs.

## Nota: Read Before Planning

- `ARCHITECTURE.md` AR rules: no change to the audio thread; the blob read is the same
  `DeviceGetState` the loop export and project save already use.
- `DESIGN.md` / nota-design: the menu uses `MenuKit` items; no new tokens.
- `CONTRIBUTING.md`: SPDX header on the new file, CHANGELOG `[Unreleased]`, FEATURES.md,
  user docs (nota-docs: `devices/audio-effects/strata`).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing) — no new dependency

**Storage**: N/A — reads the existing Strata state blob (persisted with the project)

**Audio-Thread Impact** *(required — state explicitly)*: None. The export reads the
device state through the existing `DeviceGetState` C ABI call on the UI thread, as the
whole-loop export and project save do; no engine code changes.

**Testing**: `tests/Nota.SmokeTest` (engine end-to-end + managed helpers), headless
Avalonia harness for the card menu shot

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: desktop app

**Performance Goals**: The WAV write runs off the UI thread (a 16 s stereo loop at
192 kHz is ~25 MB of float PCM per layer).

**Constraints**: Must not change the device state (FR-005); must not fall back to the
mixed loop (FR-008).

**Scale/Scope**: 1 new file, 1 edited device body, strings, smoke test, docs.

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue first | #482 carries `approved` (milestone 0.61.1) |
| I. Real-time audio safety | No audio-thread change |
| II. Boundary discipline | Only existing C ABI calls (`DeviceGetState`, `DeviceScope`) |
| III. Licensing hygiene | SPDX header on the new file; no dependency |
| IV. Verification gate | `scripts/build.sh` incl. smoke test before the PR |
| V. One logical change | Layer export only; loop export untouched |
| VI. Docs in sync | CHANGELOG, FEATURES.md, nota-docs Strata page |
| VII. Spec-first | This spec |
| VIII. Safety rules | Unchanged |

Post-design re-check: unchanged — the design stays in the managed layer.

## Project Structure

### Documentation (this feature)

```text
specs/102-strata-layer-export/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/strata-layer-export.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/managed/Nota.Infrastructure/Audio/StrataLayerExport.cs   # NEW: blob parse + layer WAV
src/managed/Nota.App/DeviceCards/Bodies/StrataDeviceBody.cs  # layer-row context menu, export flow
src/managed/Nota.Application/Localization/en.json            # strings
tests/Nota.SmokeTest/Program.cs                              # Strata section: layer export checks
CHANGELOG.md, FEATURES.md                                    # entries
../nota-docs (EN + RU Strata page)                           # docs PR
```

**Structure Decision**: The blob layout knowledge for the layer export lives in
`Nota.Infrastructure` next to `WavWriter`, so the smoke test (which references
Infrastructure, not Nota.App) can exercise it against a real engine. The card only
decides availability for the menu and runs the picker.

## Complexity Tracking

None.
