# Implementation Plan: Device cards XL — a large editor panel above the device chain

**Branch**: `feat/new-lool-ambertape` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md) | **Issue**: [#540](https://github.com/nota-daw/nota/issues/540)

**Input**: Feature specification from `/specs/085-device-xl-panel/spec.md`

## Summary

Add a third card size, XL, for Nota EQ-8 and Nota Dynamic EQ-8. XL is stored as
`View = 2` in the device's existing size param. The engine only widens `paramMax(View)`
to 2 for those two devices. `DeviceChainView` grows a panel row above the chain. The
panel is the shared card shell around the same device body the card builds, stretched
to the panel. The card in the chain becomes a 260 × 260 placeholder. One XL device per
track. The panel follows the selected track. `MainWindow` sizes the Devices panel from
the chain view's new `PreferredHeight`. See [research.md](research.md).

## Nota: Read Before Planning

Consulted: `DESIGN.md` § Devices (the one shell, the chain sizing, § Card layout),
the `nota-design` skill, `CardLayout` / `DeviceView` (the #531 S/L mechanism),
`LensWindow` (specs/050, the precedent for a body built again in a larger host),
`Engine_Presets.cpp` (presets skip `View`).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app); C++20, two one-line `paramMax` changes

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: project file through the device's existing `View` param (no format change)

**Audio-Thread Impact** *(required — state explicitly)*: None in practice. `View` is an
existing atomic param that DSP never reads. Only its clamp range widens.

**Testing**: `tests/Nota.SmokeTest` (engine param contract), plus a headless Avalonia
harness for the panel (screens and behaviour, see [quickstart.md](quickstart.md))

**Target Platform**: macOS, Windows, Linux desktop (Avalonia)

**Project Type**: desktop app

**Performance Goals**: no extra per-tick work. Only one body (panel or card) per device
is built, so live refreshers run once (FR-013). Switching sizes is one chain rebuild, as
S/L is today.

**Constraints**: the chain row keeps its 272 height. The panel takes the rest, between a
minimum of 180 and a desired 460.

**Scale/Scope**: two devices, about 8 managed files, 2 engine lines, and one smoke check

## Constitution Check

| Principle | Status |
|-----------|--------|
| 0 Approved issue | ✅ #540 carries `approved` |
| I Real-time safety | ✅ no audio-thread change. The `View` clamp range only, the atomic store as before |
| II Boundary discipline | ✅ no new C ABI. Existing `device_param_max/set/get` |
| III Licensing | ✅ no new dependency. New files carry the SPDX header |
| IV Verification gate | ✅ smoke check for the widened `View` (engine behaviour change), plus `scripts/build.sh` |
| V One logical change | ✅ XL for two devices. The mechanism is generic, with no other devices opted in |
| VI Docs in sync | ✅ CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs EQ pages (nota-docs), DESIGN.md § Devices (the XL panel) |
| VII Spec-first | ✅ spec, plan and tasks |
| VIII Rules not weakened | ✅ |

Post-design re-check: still passes. The spec's "no engine changes" is narrowed to "no
sound or new-param changes" (R1), which its Assumptions already allow.

## Project Structure

### Documentation (this feature)

```text
specs/085-device-xl-panel/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/ui-contract.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/src/devices/effects/
├── Eq.cpp, Eq.h                 # paramMax(View) = 2; comment 0 = L, 1 = S, 2 = XL
└── DynamicEq.cpp, DynamicEq.h   # same
src/managed/Nota.Application/Instruments/
└── DeviceView.cs                # CardSize enum, Size / SetSize / SupportsXlParam; IsMini = Size == S
src/managed/Nota.App/
├── DeviceCards/IDeviceBody.cs           # bool SupportsXl => false
├── DeviceCards/DeviceCardContext.cs     # bool CanXl
├── DeviceCards/CardLayout.cs            # SizeToggle with XL; WidthFor: XL → 260
├── DeviceCards/Midi/MidiCardHeader.cs   # a three-way toggle variant
├── DeviceCards/Bodies/EqDeviceBody.cs, DynamicEqDeviceBody.cs  # SupportsXl = true, toggle with XL
├── DeviceChainView.cs                   # the panel row, PreferredHeight, XL resolve per track
├── DeviceChainView.Xl.cs  (new)         # the panel, the placeholder card, close, Esc, one-per-track
├── DeviceChainView.Shell.cs             # ShellSpec Height / Xl
└── MainWindow.Detail.cs                 # size Devices from PreferredHeight, follow its changes
src/managed/Nota.App/Localization (or the catalog in use)  # new strings: "Open in XL above", tooltips
tests/Nota.SmokeTest/
└── DeviceXlTests.cs (new)               # the engine View contract; wired in Program.cs
```

**Structure Decision**: managed UI in `Nota.App`, size semantics in `Nota.Application`,
the engine clamp only.

## Complexity Tracking

None.
