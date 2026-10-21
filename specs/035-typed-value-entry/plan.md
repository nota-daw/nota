# Implementation Plan: Typed value entry on every parameter control

**Branch**: `035-typed-value-entry` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/035-typed-value-entry/spec.md`

## Summary

Every numeric parameter control gets the same text entry: double-click opens a flat box
over the read-out, ⌥/Alt-click resets to default, right-click offers **Type value…** /
**Reset to default**; Enter or a click away commits as one undo step and one automation
gesture, Esc cancels, Tab walks the card. The existing `ValueEntry` adorner and parser are
generalised into one helper: a pure parser / solver (`ValueText`, tested from the smoke
test), and an Avalonia side that attaches to single-value controls (`Knob`, faders,
sliders, bars) or to *hosts* — graph controls, envelopes and matrices that describe the
fields of the handle under the pointer. Return keeps its transport meaning, so a focused
control does not open entry on Enter (research R2). UI-only; no engine, ABI or file-format
change. See [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) § Controls / Interaction contract (changes
  here) and § Readouts and fields (the box look); the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12 (existing); no new dependency

**Storage**: N/A — no project, preset or settings change

**Audio-Thread Impact**: None. A commit is `Begin` → one parameter set → `End` through the
setters a drag uses. The new pure `TextAt` removes the ~70 throwaway engine writes the
current slider probe makes per commit; the legacy probe path stays only where listed in
the audit (it already restores the value before the gesture opens).

**Testing**: `scripts/build.sh` (smoke test, design checks); new `ValueEntryTests` in
`tests/Nota.SmokeTest` (`--valueentry`) over `ValueText` linked as source, like
`Changelog.cs`; in-app walk per [quickstart.md](quickstart.md)

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: desktop app (Avalonia)

**Performance Goals**: a commit (solve + write) under one frame (16 ms) on any control; the
solver is ≤ 48 samples + ≤ 22 bisection steps of a pure formatter

**Constraints**: one undo step and one gesture per commit (engine drops no-op gestures —
`Engine::endMixGesture`); Return = Stop and Space = Play stay global outside text boxes;
fine-drag modifiers unchanged; tokens only (design checks)

**Scale/Scope**: ~15 shared controls, 44 knob-building files, ~40 graph classes in 30
files, 2 matrices, 7 envelope sites — inventory in
[contracts/control-audit.md](contracts/control-audit.md). Estimated ~1 500 lines changed,
mostly small per-site edits.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ no audio-thread change; fewer stray parameter writes than today |
| II. Boundary discipline | ✅ no ABI change; existing `BeginAutomationWrite` / `EndAutomationWrite` and setters |
| III. Licensing hygiene | ✅ new files (`ValueText.cs`, `IValueEntryHost.cs`, `ValueEntryTests.cs`) get the SPDX header; no dependency |
| IV. Verification gate | ✅ `scripts/build.sh`; parser / solver covered by managed checks in the smoke test; no engine behaviour change to cover |
| V. One logical change | ⚠️ one behaviour ("type exact values everywhere"), but wide. Delivered as one PR per story group (see Delivery) so each stays reviewable |
| VI. Docs in sync | ⏳ CHANGELOG, FEATURES.md, user docs EN + RU, Shortcuts pane, DESIGN.md interaction contract |
| VII. Spec-first | ✅ |
| VIII. Rules not weakened | ✅ |

Post-design re-check: unchanged — the design adds no audio-thread work, no ABI and no
dependency.

## Design

### 1. Parser / solver — `Controls/ValueText.cs` (new, pure)

Move `Read`, `Solve`, `Pick`, `Plateau`, `Edge(s)`, `Clean` and the regex out of
`ValueEntry.cs` unchanged, then extend `Read`: `s` (×1, seconds), `Hz` / `kHz`, `dB`,
`%`, `st`, `ct`, `°` as named units (multiplier 1 except k-prefixed), `-inf`. No Avalonia
`using`s, so `tests/Nota.SmokeTest` links it (`<Compile Include=… Link=…>`).

### 2. Helper — `Controls/ValueEntry.cs`

- `ValueField` record and `IValueEntryHost` ([contract](contracts/value-entry.md)).
- `Attach(control, field, readout?)` / `AttachHost(host)`: tunnel `PointerPressed`
  handlers — `ClickCount == 2` + left → `Open`; left + ⌥ → reset (`Clear` if present,
  else `Default`; skipped for a host whose `FieldsAt` is null so its own ⌥ meaning
  stays); `ContextRequested` → parameter menu unless already handled.
- `Open`: the current adorner box (unchanged look, see research R7), clamped inside the
  top level; one session per top level; Tab / Shift+Tab handled on the box in tunnel.
- Tab chain: `Scope` attached property; registry of attached controls (weak, removed
  on detach); ordering per [data-model.md](data-model.md#tab-chain).
- `MenuItems(control, field)` for controls with their own menus (ModularView, MacroPanel,
  Rack drum pads).
- The old `Attach(TextBlock, norm, setNorm, text, …)` wraps a probing `ValueField`.

### 3. Single-value controls

`Knob`, `MiniFader`, `SliderTrack`, `RangeSliderTrack`: delete the double-click reset
branch (now unreachable, handled in tunnel). `Knob.Format` property; `KnobCell` attaches
when `Format` is set, using its `value` TextBlock as the read-out. `PanKnob`, `VFader`,
`PanBar`, `MixBar`, `ValueBar`, `PerformWheel`, faders inside graph files: attached by
their owners with the owner's formatter. `SliderRow` / `ClipEditorKit`: `reset` goes to
⌥ (through the field's `Default`/reset action), optional `textAt`.

### 4. Hosts

Each graph control in the audit implements `IValueEntryHost` next to its existing hit
test (most already have a "nearest handle" function for dragging), and its owner card
passes formatters. Envelopes (`VoltEnv`, `SynthViz` Env, clip ADSR) and matrices
(`VoltMatrix`, `ConsortPatchMatrix`) per research R8 / R9. `VoltMatrix` clear goes
through a gesture (fixes non-undoable clear). Read-out row
`DeviceCardKit.EnvReadouts(fields)` for envelope graphs without A/D/S/R controls nearby.

### 5. Docs and checks

`PreferencesWindow` Shortcuts rows; DESIGN.md § Interaction contract and § Known gaps
("Slider reset on double-click…" line); CHANGELOG; FEATURES.md; nota-docs EN + RU. The
audit file is ticked as the last task.

### Delivery

One spec, three PRs (each passes `scripts/build.sh` on its own):

1. **Core + P1** — `ValueText` + tests, helper, Knob / faders / sliders / bars and every
   knob site, ⌥ reset, parameter menu, Tab chain, DESIGN.md, Shortcuts, CHANGELOG.
2. **Envelopes + matrices** (Stories 4, 5).
3. **Graph controls** (Story 6), final audit tick, FEATURES.md and user docs.

## Project Structure

### Documentation (this feature)

```text
specs/035-typed-value-entry/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── value-entry.md       # gestures, field, helper API, parser grammar
│   └── control-audit.md     # the acceptance inventory
└── tasks.md                 # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/Nota.App/
├── Controls/
│   ├── ValueText.cs              # NEW — pure parser / solver (linked into the smoke test)
│   ├── ValueEntry.cs             # box, Attach / AttachHost, Tab chain, parameter menu
│   ├── IValueEntryHost.cs        # NEW — host interface + ValueField
│   ├── Knob.cs, PanKnob.cs, MiniFader.cs, VFader.cs, SliderTrack.cs, RangeSliderTrack.cs,
│   │   DragNumber.cs, PanBar.cs, ValueBar.cs, MixBar.cs, PerformWheel.cs, ChordShiftTrack.cs
│   ├── VoltEnv.cs, SynthViz.cs, VoltMatrix.cs, ConsortViews.cs
│   └── *Views.cs / *Curve.cs / FluxVectorPad.cs …   # hosts per the audit
├── DeviceCardKit.cs, ClipEditorKit.cs, ClipAdsrKit.cs
├── DeviceCards/**                # Format on knob sites, Scope on card roots, read-out rows
├── MixerView.cs, ArrangementView*.cs, SessionView.Mixer.cs, MainWindow.axaml.cs, ModularView.cs
└── PreferencesWindow.cs          # Shortcuts rows

tests/Nota.SmokeTest/
├── ValueEntryTests.cs            # NEW — parser / solver checks (--valueentry, and in the default run)
└── Nota.SmokeTest.csproj         # link Controls/ValueText.cs

DESIGN.md, CHANGELOG.md, FEATURES.md, ../nota-docs (EN + RU)
```

**Structure Decision**: managed UI only, all in `src/managed/Nota.App` plus one smoke-test
file; no engine, `Nota.Presentation` or `Nota.Application` change.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Wide change (V) | The issue's acceptance is "same gesture everywhere" — a partial rollout leaves double-click meaning reset on some controls and type on others | One PR per control family: more churn on the shared helper and a mixed-gesture app between merges; three PRs by story keep each reviewable and each consistent |
