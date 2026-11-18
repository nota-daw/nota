# Implementation Plan: Modular device cards and a searchable cable-drop picker

**Branch**: `feat/modular-device-cards` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/101-modular-device-cards/spec.md`
(issue [nota-daw/nota#656](https://github.com/nota-daw/nota/issues/656))

## Summary

Modular nodes for chain devices (MIDI FX, instrument, effects, plug-ins) show the real
Detail card body at S or L, under Modular's own header and port block. XS keeps
today's compact node. The size is Modular's own: an engine proxy
(`CardSizeEngineProxy`) presents it to the unchanged card bodies as the device's
card-size param, so Detail is untouched. The size is saved in the existing
`modular-layout.json` sidecar. A CV cable dropped on a card control links that parameter
directly (identity from the existing `MidiLearn` bindings). A cable dropped elsewhere,
on another track's header, or via the two right-click "Modulate from…" items opens one
shared picker panel: a search field focused on open, a virtualized list, keyboard
navigation, and a fixed maximum size. UI-only; no engine or ABI change.

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (DSP class, kind registration across C++ and C#, device-card UI).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

For this feature: UI work (DESIGN.md + nota-design for the picker, size chip and brass
outline) and CONTRIBUTING.md. The device-card section of `nota-instrument` describes
the bodies being hosted, which are not changed.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia (managed app only).

**Primary Dependencies**: Avalonia (existing). `System.Reflection.DispatchProxy` (BCL,
already used by `RackEngineProxies`). No new dependency.

**Storage**: `modular-layout.json` sidecar in the `.nota` bundle. Value arrays gain an
optional third element (size code); backward and forward compatible (data-model.md).

**Audio-Thread Impact** *(required — state explicitly)*: None. UI-only. Card controls
write parameters through the same `IAudioEngine` calls Detail uses (already queued to
the engine per AR-4). The proxy only intercepts card-size param reads and writes in
managed code.

**Testing**: new headless program `tests/Nota.ModularUiTest` (same style as
`tests/Nota.TempoUiTest`: real engine, Avalonia headless, no audio device), plus the
manual quickstart scenarios. `scripts/build.sh` with the smoke test as the gate (no
smoke-test change: no engine change).

**Target Platform**: macOS, Windows, Linux (desktop app).

**Project Type**: desktop app (UI layer).

**Performance Goals**: picker opens < 150 ms at 5,000 params; < 50 ms per keystroke;
Modular first draw < 300 ms for 6 devices; card live-follow at the UI tick (60 Hz).

**Constraints**: picker ≤ 320 × 420; no change to Detail's card size; old layout files
load.

**Scale/Scope**: ~5 files changed in `Nota.App` (ModularView split into partials), 1
new proxy in `Nota.Application`, 1 model in `Nota.Presentation`, 1 control in
`Nota.App/Controls`, 1 new test program, EN + RU strings.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| 0. Approved issue first | Pass | #656 carries `approved`; the scope matches the issue plus the clarifications recorded in the spec. |
| I. Real-time audio safety | Pass | No engine or audio-thread code. Parameter writes use the existing queued engine calls. |
| II. Boundary discipline | Pass | No ABI change; the proxy wraps the managed `IAudioEngine` interface only. |
| III. Licensing hygiene | Pass | New files get the SPDX header; no dependency. |
| IV. Verification gate | Pass | `scripts/build.sh` + smoke test unchanged and must stay green; the headless UI test is added. |
| V. One logical change per PR | Watch | Two user-facing parts in one issue. Planned as **two PRs** if the diff grows: (1) picker + right-click (Stories 1–2), (2) cards + sizes + drop-on-knob (Stories 3–4). Both close parts of #656. |
| VI. Docs in sync | Planned | CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), nota-docs Modular page EN + RU with re-shot screenshots. |
| VII. Spec-first | Pass | This spec, plan and tasks. |
| VIII. Safety rules not weakened | Pass | — |

*Post-design re-check (after Phase 1)*: still all Pass/Planned. The proxy decision
(R2) avoids touching ~40 card bodies, which keeps principle V's diff small.

## Project Structure

### Documentation (this feature)

```text
specs/101-modular-device-cards/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R8 decisions
├── data-model.md        # node size, sidecar, picker model
├── quickstart.md        # Q1–Q6 validation
├── contracts/
│   └── ui-contract.md   # node, drag feedback, picker behaviour, strings
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   └── Engine/
│       └── CardSizeEngineProxy.cs        # NEW — Modular size seen by card bodies as the device's View param (R2)
├── Nota.Presentation/
│   └── ParamPickerModel.cs               # NEW — entries, filter, headings, highlight, fallback labels (R6)
├── Nota.App/
│   ├── Controls/
│   │   └── ParamPickerPanel.cs           # NEW — Flyout: search TextBox + virtualized ListBox, keys (R6)
│   ├── ModularView.cs                    # node build: size → XS rows or card body; NodeW → n.Width; FinishConnect
│   ├── ModularView.Cards.cs              # NEW partial — card context, body builders per slot, size chip, proxy wiring (R1–R3)
│   ├── ModularView.Picker.cs             # NEW partial — the four picker callers; drop-on-control hit test + overlay (R5–R7)
│   ├── DeviceCards/DeviceCardContext.cs  # (only if a hook is missing for Modular's tick wiring — prefer none)
│   └── Strings (EN/RU resources)         # modular.size_*, modular.picker_*
tests/
└── Nota.ModularUiTest/                   # NEW headless checks (R8)
```

**Structure Decision**: the picker's logic sits in Nota.Presentation, so it can be
tested without UI, and the Avalonia view sits in Nota.App/Controls next to the other
shared controls. The proxy lives next to the rack proxies in
Nota.Application/Engine. The new ModularView code goes into partials, following the
existing `ModularView.Scope.cs` split, so the 2,579-line file doesn't grow.

## Complexity Tracking

No constitution violations to justify.
