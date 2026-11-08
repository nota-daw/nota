---
description: "Tasks: Device cards XL panel (#540)"
---

# Tasks: Device cards XL — a large editor panel above the device chain

**Input**: Design documents from `/specs/085-device-xl-panel/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: the spec doesn't ask for TDD. The smoke check (T003) is the constitution's gate
for the engine change. The UI is verified through the headless harness (T020).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

No setup: existing projects, no new dependency.

---

## Phase 2: Foundational (blocking all stories)

- [X] T001 [P] Engine: in `src/native/nota.engine/src/devices/effects/Eq.cpp`, return `2.0f` from `paramMax(View)`. In `Eq.h`, update the `View` comment to "0 = L, 1 = S, 2 = XL (#540)". Do the same in `DynamicEq.cpp` / `DynamicEq.h`.
- [X] T002 [P] `src/managed/Nota.Application/Instruments/DeviceView.cs`: add `enum CardSize { L = 0, S = 1, XL = 2 }`, plus `Size(engine, track, device)` (round of View, L when there is no View), `SetSize(engine, track, device, CardSize)` (XL is written only when `SupportsXlParam`) and `SupportsXlParam` (`DeviceParamMax(View) >= 2`). `IsMini` becomes `Size == S`, and `SetMini` keeps its signature. Update the header comment.
- [X] T003 [P] Smoke check `tests/Nota.SmokeTest/DeviceXlTests.cs` (new, with the SPDX header), wired in `tests/Nota.SmokeTest/Program.cs`. On an audio track, EQ-8 (kind 0) and Dynamic EQ-8 (kind 13) report View max 2, store 2 and read back 2, and `DeviceView.Size` is XL. EQ-3 (kind 16) still reports max 1. Applying an EQ-8 factory preset leaves View at 2.
- [X] T004 [P] `src/managed/Nota.App/DeviceCards/IDeviceBody.cs`: add `bool SupportsXl => false`. In `DeviceCardContext.cs`, add an optional ctor arg `bool canXl = false` and a `CanXl` property.
- [X] T005 `src/managed/Nota.App/DeviceCards/Midi/MidiCardHeader.cs`: add a three-way `SizeToggle(ctx, Func<CardSize> get, Action<CardSize> set)` (segments `S / L / XL`, same metrics as S/L), with a tooltip in the localization catalog. In `src/managed/Nota.App/DeviceCards/CardLayout.cs`, add `SizeToggle(ctx, deviceIndex, bool xl)`. It shows the three-way toggle when `xl && ctx.CanXl && DeviceView.SupportsXlParam`, and S/L otherwise. Picking XL first sets every other XL device on the track to L. `WidthFor` returns `MiniWidth` for XL too.
- [X] T006 `src/managed/Nota.App/DeviceChainView.Shell.cs`: add `double Height = CardH` and `bool Xl = false` to `ShellSpec`. The card uses `s.Height`. With `Xl`, there is no drag hook and no fold on header double-click. `NotaTextSize.SetFixed` stays (R7). Add `Placeholder`, which hides the preset picker.

**Checkpoint**: the engine stores XL and the toggle exists. Nothing renders a panel yet.

---

## Phase 3: User Story 1 + 2 — EQ-8 and Dynamic EQ-8 in XL (P1) 🎯 MVP

**Goal**: picking XL opens the panel above the chain with the whole editor stretched. The
card becomes a 260 × 260 placeholder. Leaving XL works from either toggle, the close key
and Esc.

**Independent Test**: quickstart § 2, rows 1 and 3, for both devices.

- [X] T007 [P] [US1] `src/managed/Nota.App/DeviceCards/Bodies/EqDeviceBody.cs`: set `SupportsXl => true`. `HeaderAccessory` calls `CardLayout.SizeToggle(ctx, deviceIndex, xl: true)`. Note XL in the header comment.
- [X] T008 [P] [US2] `src/managed/Nota.App/DeviceCards/Bodies/DynamicEqDeviceBody.cs`: the same as T007.
- [X] T009 [US1] New `src/managed/Nota.App/DeviceChainView.Xl.cs` (SPDX header):
  - `XlHeight = 460`, `XlMinHeight = 180`, and the `_xlHost` Border.
  - `XlPanel(index, count)` builds the body with a context that has `canXl: true`, wraps it in `BuildCardShell` (`Width = NaN`, `Height = NaN`, `Xl = true`, full name, type badge, header extra = the size toggle plus a close key).
  - `XlPlaceholder(index, count)`: a 260-wide compact shell whose body is a centred caption "Open in XL above". Clicking it selects the device.
  - `CloseXl(index)` sets L and rebuilds. The panel handles Esc (when not already handled) by calling `CloseXl`.
- [X] T010 [US1] `src/managed/Nota.App/DeviceChainView.cs`:
  - Content becomes a two-row Grid (panel row over the existing chain panel). Without XL the chain row is `*`, exactly as today. With XL the rows are `*` (panel, min `XlMinHeight`) and `Auto` (chain).
  - `RebuildRow` resolves the track's XL device before the loop (T014 rule) and fills `_xlHost`.
  - `DeviceCard` returns `XlPlaceholder` for that index and skips `strategy.Build` for it, so refreshers register once.
  - Pass `canXl: true` in `NewCardContext()`.
- [X] T011 [US1] `src/managed/Nota.App/DeviceChainView.cs`: `PreferredHeight` is `2·ChainInset + CardH`, plus `XlHeight + ChainInset` with a panel. `PreferredHeightChanged` is raised from `RebuildRow` when the value changes.
- [X] T012 [US1] `src/managed/Nota.App/MainWindow.Detail.cs`: `ShowDevices` sizes the panel with `1 + 32 + _deviceChain.PreferredHeight`. Subscribe to `_deviceChain.PreferredHeightChanged` (in `MainWindow.axaml.cs`, where `_deviceChain` is created) and re-apply `ShowDetail(..., honorPersist: false, resizable: false)` while the docked Devices tab is shown (`DetailDevicesBtn.IsChecked == true`, not floating, panel visible).
- [X] T013 [P] [US1] Localization: add the new strings (placeholder caption, XL toggle tooltip, close tooltip) to the catalog the `Loc.T` keys come from, in every language file that the localization check requires.

**Checkpoint**: XL works for both devices on one track.

---

## Phase 4: User Story 3 — One XL panel at a time (P2)

**Independent Test**: quickstart § 2, row 2, and the duplicate row.

- [X] T014 [US3] `src/managed/Nota.App/DeviceChainView.Shell.cs` / `DeviceChainView.Xl.cs`: add `bool XlOwner` to `CardExtra`. `ResolveXl()` collects the track's devices with Size XL and picks the owner flag, or else the first. The others are set to L through `DeviceView.SetSize`. The winner's `CardExtra.XlOwner` is set and the rest are cleared.

---

## Phase 5: User Story 4 — Remembered with the project (P2)

**Independent Test**: quickstart § 3.

- [X] T015 [US4] Verify that save and reopen keep View = 2. The param already persists, so this is only a check in the harness (T020), with no code unless it fails. Duplicate: T014 makes the copy L.

---

## Phase 6: User Story 5 — Follows the selected track (P3)

**Independent Test**: quickstart § 2, row 4.

- [X] T016 [US5] Confirm that `Show(trackId)` → `RebuildRow` hides or shows the panel per track with no size writes. Make sure `ResolveXl` never writes when there is exactly one XL device. `PreferredHeightChanged` drives the Detail height on a track switch (T012).

---

## Phase 7: Polish & Cross-Cutting

- [X] T017 [P] `DESIGN.md` § Devices › The card shell: describe the XL panel (where it sits, its height, the 260 placeholder, one per track, follows the track) and the `S / L / XL` toggle.
- [X] T018 [P] `CHANGELOG.md` `[Unreleased]` › Added: XL for Nota EQ-8 and Dynamic EQ-8 (#540).
- [X] T019 FEATURES.md via the `nota-features` skill, and the EQ-8 / Dynamic EQ-8 user pages via `nota-docs` (EN + RU). No new shortcut beyond Esc in the panel, which goes to `nota-shortcuts` if the Shortcuts pane lists per-panel keys.
- [X] T020 Headless harness (see the Nota UI verify recipe): run quickstart § 2 and § 3, and capture the panel in Studio Dark and Light. Check SC-001 (graph ≥ 2× the L card's in both dimensions at 1440 wide).

### Nota closing tasks

- [ ] T021 Run `scripts/build.sh` until native, managed and the smoke test are green. 2026-11-08: everything builds and every XL, localization and label check passes. The only failures are the render-pool "overloaded" timing checks (`RenderPoolTests`, staged work on this branch, not touched here); they flake under load.
- [X] T022 AGPL-3.0-only SPDX header on `DeviceChainView.Xl.cs` and `DeviceXlTests.cs`.
- [X] T023 New dependency: N/A.
- [X] T024 CHANGELOG entry: T018.
- [X] T025 FEATURES and docs: T019.
- [X] T026 Engine behaviour change (View range): smoke check T003.

### Found while building (R7)

- [X] T027 `src/managed/Nota.Infrastructure/Persistence/PresetService.cs`: `ResetInPlace` (Init) leaves an effect's View alone (FR-011).
- [X] T028 `src/managed/Nota.App/Theme/NotaText.cs`: `XL` is a fixed acronym, so the toggle reads `XL`, not `Xl`.
- [X] T029 `src/managed/Nota.App/DeviceCardKit.cs`: `IconKey(GlyphKind, …)`. The panel closes with `Shrink`, not a second ×.
- [X] T030 `src/managed/Nota.App/PreferencesWindow.cs` and `en.json`: the Shortcuts pane lists Esc in the XL panel (nota-shortcuts).

---

## Dependencies & Execution Order

- T001–T006 block everything. T001–T004 are parallel, T005 needs T002 and T004, and T006 is independent.
- US1/US2 (T007–T013) need Phase 2. T007, T008 and T013 are parallel. T009 → T010 → T011 → T012.
- US3 (T014) needs T010. US4 and US5 are checks over US1 and US3.
- Polish after the stories. T021 is last.

## Parallel Example

```text
T001 (engine) ‖ T002 (DeviceView) ‖ T003 (smoke) ‖ T004 (interfaces)
T007 (EQ-8) ‖ T008 (Dynamic EQ-8) ‖ T013 (strings)
T017 (DESIGN) ‖ T018 (CHANGELOG)
```

## Implementation Strategy

MVP = Phase 2 + Phase 3 (both devices are P1 and share all the code). Then one per track
(T014), then the checks, docs and the gate.

## Phase 9: Convergence

- [X] T031 Run the render-pool guards alone (`dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --perf`) on an idle machine and record whether the "overloaded" failures belong to the branch's staged render work or are load flakiness, per Constitution IV / T021 (partial). 2026-11-08: they fail alone too (`four indivisible VST3 jobs`, `overloaded` ×2) with Rider running. The XL diff touches no render code or render test (`Engine_Render.cpp`, `RenderPoolTests.cs` and `HostedRenderPoolTests.cs` are the branch's staged work), so they belong to that work, not to XL.
- [X] T032 Scroll `src/managed/Nota.App/DeviceChainView.cs` vertically when it is shorter than `XlMinHeight + ChainInset + 2·ChainInset + CardH` with a panel open, so the chain is never clipped, per Spec Edge case "Small window" (partial)
- [X] T033 Shoot the panel in Studio Light in the headless harness via `NotaThemeService.Set(new AppearanceChoice(...))` and check it, per FR-014 / T020 (partial)
- [X] T034 In `DeviceChainView.DeviceTransferred`, return a device moved or copied onto a track that already has another XL device to L, per Spec Edge case "moved onto a track with XL" (partial)
- [X] T035 Make a click on the XL placeholder bring the panel into view (`_xlHost.BringIntoView()`) as well as select the device, in `src/managed/Nota.App/DeviceChainView.Xl.cs`, per FR-006 (partial)
- [ ] T036 Real-app spot check (quickstart § 4): add EQ-8, pick XL, drag a band, Esc. This is a manual check by the user, because GUI launch is not available to the agent, per quickstart § 4 (partial)
