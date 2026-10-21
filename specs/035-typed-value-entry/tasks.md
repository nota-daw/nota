---

description: "Task list for typed value entry on every parameter control"
---

# Tasks: Typed value entry on every parameter control

**Input**: Design documents from `/specs/035-typed-value-entry/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The plan asks for managed checks of the parser / solver in `tests/Nota.SmokeTest` (`ValueEntryTests`, `--valueentry`). UI behaviour is verified by the quickstart walk.

**Organization**: Tasks are grouped by user story. US1 and US2 are both P1 and ship together: once double-click is intercepted for typing, the old double-click reset is gone, so ⌥ reset must land in the same PR.

All app paths are under `src/managed/Nota.App/` unless they start with `tests/`, `specs/` or a repo-root file.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1…US6)

---

## Phase 1: Setup

**Purpose**: Confirm the inventory before code moves.

- [X] T001 Re-check `specs/035-typed-value-entry/contracts/control-audit.md` against the code: grep `new Knob`, `new MiniFader`, `new VFader`, `new PanKnob`, `new SliderTrack`, `new RangeSliderTrack`, `new PanBar`, `new ValueBar`, `new MixBar`, `new PerformWheel`, `new ChordShiftTrack`, `ClickCount`, `KeyModifiers.Alt` and `BeginAutomationWrite` in `src/managed/Nota.App`; resolve every "(check)" / "(verify)" row (does the control drag a parameter? what does its double-click do?), add missing sites, move display-only views to exempt with a reason

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared parser, field model and helper every story uses.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Create `Controls/ValueText.cs` (SPDX header, no Avalonia `using`s): move `Read`, `Solve`, `Pick`, `Plateau`, `Edge`, `Edges`, `Distance`, `Same`, `Clean`, the `Quantity` regex and the `Samples` / `Steps` constants out of `Controls/ValueEntry.cs` unchanged into `internal static partial class ValueText`; `ValueEntry` calls `ValueText.Solve`
- [X] T003 Extend `ValueText.Read` in `Controls/ValueText.cs` per contracts/value-entry.md § Parser: named units `s`, `Hz`, `kHz`, `dB`, `%`, `st`, `ct`, `°` (multiplier 1 except a `k` prefix = 1e3; `ms` 1e-3; `µs`/`us` 1e-6), `-inf` / `−∞`, `L30` as well as `30L` / `30 L`, `C` = 0; decimal comma; unreadable → null
- [X] T004 [P] Create `tests/Nota.SmokeTest/ValueEntryTests.cs` (SPDX header) with checks over `ValueText.Read` / `ValueText.Solve` using pure formatters: units and multipliers, fractions (`1/4`), pan (`30L`, `L30`, `C`), ∞, words (`off`, `HOLD`), decimal comma, out-of-range clamp to an end, stepped read-out lands mid-step (typing `0` on a `0.0 dB` read-out), unit switch (`500` on a s/ms read-out → 500 ms; `1.5` on a kHz read-out → 1.5 kHz), `abc` → null
- [X] T005 Link the source and register the run in `tests/Nota.SmokeTest/Nota.SmokeTest.csproj` (`<Compile Include="..\..\src\managed\Nota.App\Controls\ValueText.cs" Link="ValueText.cs" />`) and `tests/Nota.SmokeTest/Program.cs` (`--valueentry` section, and in the default run)
- [X] T006 Create `Controls/IValueEntryHost.cs` (SPDX header): `ValueField` record (`Name`, `Get`, `TextAt`, `Set`, `Begin`, `End`, `Default` — "0..1 position of the default; null = no reset", `Clear` optional, `After` optional) and `IValueEntryHost` (`FieldsAt(Point)` — "null = not on a handle, so the event passes through", `AnchorOf(ValueField)`, `AllFields`) per contracts/value-entry.md
- [X] T007 Rework `Controls/ValueEntry.cs`: `Attach(Control, ValueField, TextBlock? readout)` and `AttachHost(Control)` with **tunnel** `PointerPressedEvent` handlers on the control and the read-out — left + `ClickCount == 2` → `Open`; left + ⌥ → `Clear` if present else reset to `Default` (Begin / Set / End / After; no-op when `Default` is null or already at default); a host whose `FieldsAt` returns null lets both events through
- [X] T008 `ValueEntry.Open(Control, ValueField)` in `Controls/ValueEntry.cs`: keep the current adorner box look (`TextBox.field`, `NotaRadius.Badge`, mono family, read-out font size, right-aligned, min 48 wide, grows left) anchored to the read-out, the control or `host.AnchorOf(field)`; clamp it inside the `TopLevel` client area; one session per top level (opening a second commits the first); Enter / outside press / `LostFocus` commit, Esc cancels, owner detached → close without commit
- [X] T009 Commit rule in `Controls/ValueEntry.cs` per data-model.md: `n = ValueText.Solve(text, TextAt(Get()), TextAt)`; "if `n` is null or `Field.TextAt(n) == Field.TextAt(Before)` → no-op; else `Begin(); Set(n); End(); After()`"
- [X] T010 Probing adapter in `Controls/ValueEntry.cs`: keep the old `Attach(TextBlock readout, norm, setNorm, text, begin, end, after)` signature as a wrapper that builds a `ValueField` whose `TextAt` probes through `setNorm` + `text()` and restores the original position before `Begin` (current behaviour, specs/016)
- [X] T011 Parameter menu in `Controls/ValueEntry.cs`: `MenuItems(Control, ValueField)` → **Type value…** (calls `Open`) and **Reset to default** (disabled when `Default` is null or the value is at default) or **Clear** when `Clear` is set, built with `MenuKit.Item`; `Attach` / `AttachHost` add a `ContextRequested` handler that shows them in a `MenuFlyout` unless the event is already handled
- [X] T012 Add `Format` (`Func<double, string>?`, the read-out text at a value) to `Controls/Knob.cs`; update its header comment and the `Default` doc comment ("restored on ⌥-click")

**Checkpoint**: parser tested, helper ready — `scripts/build.sh` green with no visible change yet except the existing slider rows (now through the new helper)

---

## Phase 3: User Story 1 — Type a value into a knob, fader or slider (Priority: P1) 🎯 MVP

**Goal**: Double-click any knob, fader, slider or bar (or its read-out), type, Enter → exact value, one undo step, one gesture.

**Independent Test**: quickstart § 2 rows 1–3, 6–7, mixer fader and pan, automation armed.

### Shared controls

- [X] T013 [US1] `DeviceCardKit.KnobCell` in `DeviceCardKit.cs`: when `knob.Format` is set, `ValueEntry.Attach(knob, field, value)` with `Get = knob.Value / max`, `Set` raising the knob's `ValueChanged` path, `Begin`/`End` = the knob's `GestureBegin`/`GestureEnd` subscribers (expose `Knob.BeginGesture()` / `EndGesture()` / `SetByUser(double)` in `Controls/Knob.cs` so a typed value runs the same handlers as a drag), `Default` from `knob.Default`
- [X] T014 [US1] `DeviceCardKit.SliderRow` in `DeviceCardKit.cs` and the row builders in `ClipEditorKit.cs`: optional pure `Func<double, string>? textAt` parameter (used instead of probing when given); attach the track as well as the read-out so double-click on the track also types
- [X] T015 [P] [US1] `Controls/MiniFader.cs`, `Controls/VFader.cs`, `Controls/PanKnob.cs`: add `BeginGesture` / `EndGesture` / `SetByUser` like `Knob` so owners can build a `ValueField`; no gesture code in the controls themselves
- [X] T016 [P] [US1] `Controls/PanBar.cs`, `Controls/ValueBar.cs`, `Controls/MixBar.cs`, `Controls/PerformWheel.cs`: same helpers (or mark exempt in the audit per T001)

### Knob factories (set `Format`)

- [X] T017 [P] [US1] `DeviceCards/DeviceParamControls.cs`: set `Format = frac => Fmt((float)(min + frac * span))` on the device-param knob and `Format = fmt` on the normalised knob helper
- [X] T018 [P] [US1] `DeviceCards/InstrumentControls.cs`: set `Format` from the instrument param formatter
- [X] T019 [P] [US1] `DeviceCards/PluginCard.cs`: VST3/AU generic knobs — field with probing `TextAt` via `PluginParamText` (documented exception in the audit)
- [X] T020 [P] [US1] Effect bodies — set `Format` in each body's knob helper: `DeviceCards/Bodies/AutoFilterDeviceBody.cs`, `AutoShiftDeviceBody.cs`, `BeatRepeatDeviceBody.cs`, `CeilingDeviceBody.cs`, `ChamberDeviceBody.cs`, `CompressorDeviceBody.cs`, `CrushDeviceBody.cs`, `DelayDeviceBody.cs`
- [X] T021 [P] [US1] Effect bodies (cont.): `DeviceCards/Bodies/DynamicEqDeviceBody.cs`, `EqDeviceBody.cs`, `ForgeDeviceBody.cs`, `LensDeviceBody.cs`, `PrismDeviceBody.cs`, `ReverbDeviceBody.cs`, `ShutterDeviceBody.cs`, `UtilityDeviceBody.cs`, `ValveDeviceBody.cs`, `VintageDeviceBody.cs`, `VocoderDeviceBody.cs`
- [X] T022 [P] [US1] MIDI bodies: `DeviceCards/Midi/ArpMidiBody.cs`, `ChordMidiBody.cs`, `GenericMidiBody.cs`, `LengthMidiBody.cs`, `RandomMidiBody.cs`, `VelocityMidiBody.cs` (knobs → `Format`; their `SliderTrack`s → confirm the read-out is attached and pass `textAt`)
- [X] T023 [P] [US1] Instrument cards A–M: `DeviceCards/Instruments/AuroraInstrumentCard.cs`, `BassInstrumentCard.cs`, `ConsortInstrumentCard.cs`, `FluxInstrumentCard.cs`, `GrainInstrumentCard.cs`, `MonolithInstrumentCard.cs`, `MosaicInstrumentCard.cs`
- [X] T024 [P] [US1] Instrument cards P–V: `DeviceCards/Instruments/PendulumInstrumentCard.cs`, `PhysicalInstrumentCard.cs`, `RhythmInstrumentCard.cs`, `SamplerInstrumentCard.cs`, `VoltInstrumentCard.cs`
- [X] T025 [P] [US1] Racks: `DeviceCards/Rack/RackCardView.cs` (5 knobs, 2 `MiniFader`s), `DeviceCards/Rack/RackCardView.Drum.cs` (`SliderTrack`), `DeviceCards/Rack/MacroPanel.cs` (knob `Format`; append `ValueEntry.MenuItems` to `ShowMenu`)
- [X] T026 [P] [US1] `ModularView.cs` (5 knobs: `Format`; append `ValueEntry.MenuItems` in `ShowModulateMenu`) and `DeviceChainView.cs` (1 knob)
- [X] T027 [P] [US1] Remaining `SliderTrack` bodies: `DeviceCards/Bodies/AutoGainDeviceBody.cs`, `DynamicEqDeviceBody.cs`, `EqDeviceBody.cs`, `ForgeDeviceBody.cs`, `StrataDeviceBody.cs` — confirm the attach, pass `textAt`, attach the track too

### Mixer and track headers

- [X] T028 [P] [US1] `MixerView.cs`: `VFader` (dB, default 0 dB), `PanKnob` (L/R/C, default C), `MiniFader`s — `ValueEntry.Attach` with the strip's dB / pan formatters and their existing gesture hooks
- [X] T029 [P] [US1] `ArrangementView.cs` (2 `MiniFader`s, `PanBar`) and `SessionView.Mixer.cs` (2 `MiniFader`s, `PanBar`): attach with track volume / pan formatters
- [X] T030 [P] [US1] `MainWindow.axaml.cs` (`MiniFader`, preview volume): attach; preview volume default 0 dB
- [X] T031 [US1] `Controls/DragNumber.cs`: open its typing through `ValueEntry.Open` / `ValueText` so the box and parser are the shared ones; behaviour otherwise unchanged

**Checkpoint**: every knob, fader, slider and bar types — but reset still needs US2 before this can ship

---

## Phase 4: User Story 2 — One reset gesture everywhere (Priority: P1)

**Goal**: ⌥/Alt-click resets every control that had a double-click reset, and the parameter menu offers Reset / Type value….

**Independent Test**: quickstart § 2 "⌥-click a moved knob" and "Right-click a device knob" on each control family.

- [X] T032 [P] [US2] `Controls/Knob.cs`: remove the `ClickCount == 2` reset branch from `OnPointerPressed` (reset now comes from `ValueEntry` on ⌥); update the header comment ("double-click types a value, ⌥-click resets")
- [X] T033 [P] [US2] `Controls/MiniFader.cs`: remove the double-click reset branch; header comment
- [X] T034 [P] [US2] `Controls/SliderTrack.cs` and `Controls/RangeSliderTrack.cs`: remove the double-click `Reset` branch; `Reset` is invoked by the field's ⌥ reset (`DeviceCardKit.SliderRow` maps `reset` into the field); header comments
- [X] T035 [US2] `DeviceCardKit.cs` / `ClipEditorKit.cs`: wire each row's `reset` action as the field's reset (`Default` or a custom reset action on `ValueField`; extend `ValueField` with `Reset` if a row resets by action rather than position)
- [X] T036 [US2] Graph-control and step-editor ⌥ conflicts: confirm `ConsortStepGrid`, `ConsortPitchLane`, `ConsortJackField` (`Controls/ConsortViews.cs`), `RhythmViews.cs`, `ArpGrid.cs` keep their ⌥ meaning (they are not attached, or their `FieldsAt` returns null there) and that reset is reachable from the menu where they set a parameter
- [X] T037 [US2] Parameter menu on cards without one: verify right-click on a device-card knob, a slider row and a mixer fader opens **Type value… / Reset to default** and that `ModularView` / `MacroPanel` show their own items plus these

**Checkpoint**: US1 + US2 = PR 1 content (with US3 and the Phase 9 docs tasks marked "PR 1")

---

## Phase 5: User Story 3 — Keyboard: Tab to the next parameter (Priority: P2)

**Goal**: Tab / Shift+Tab in an open box commits and moves through the card in reading order, wrapping.

**Independent Test**: quickstart § 2 "open a box, Tab ×N", "Space / letters", "Return with the box closed".

- [X] T038 [US3] `ValueEntry.Scope` attached property and a weak registry of attached controls / hosts (removed on `DetachedFromVisualTree`) in `Controls/ValueEntry.cs`
- [X] T039 [US3] Tab chain in `Controls/ValueEntry.cs` per data-model.md § Tab chain: built on each Tab, "visible (`IsEffectivelyVisible`, inside the scope's bounds) and enabled", rows grouped by top edge "within half the smaller height", then left edge, host `AllFields` in order at the host's position; wraps; Shift+Tab reverses; handle Tab on the box in the tunnel phase so the window's detail-tab cycling (`MainWindow.Input.cs`) never sees it
- [X] T040 [P] [US3] Mark scopes: device / instrument card body root in `DeviceChainView.Shell.cs` (and the folded strip in `DeviceChainView.Fold.cs` if it shows controls), rack card in `DeviceCards/Rack/RackCardView.cs`, mixer strip in `MixerView.cs`, clip editor panel in `ClipEditorView.cs` / `AudioClipEditorView.cs`, the detail Synth tabs, the arrangement track header in `ArrangementView.cs`
- [X] T041 [US3] Verify keys while the box is open: Space, Return, note letters, ⌘Z go to the box (the `TextBox` exemptions in `MainWindow.OnGlobalTransportKey` / `OnKeyDown` and `DetailWindow` key routing); Return with the box closed still stops — fix any path that doesn't (e.g. floating `DeviceWindow` / `MixerWindow`)

---

## Phase 6: User Story 4 — Type envelope times and levels (Priority: P2)

**Goal**: Double-click an envelope breakpoint or segment to type its time / level; editable A/D/S/R read-outs where the graph has no numeric controls beside it.

**Independent Test**: quickstart § 2 Volt env rows; each `VoltEnv` site; clip ADSR handle in the arrangement.

- [ ] T042 [US4] `Controls/VoltEnv.cs`: implement `IValueEntryHost` — attack node → A; decay/sustain node → D then S; release node → R; nearest segment within 6 px → that stage's time; fields use `PluginParamGet/Set`, `Gest`-style Begin/End per param, `TextAt` from a formatter set by the owner (`VoltEnv.Format(Func<string, double, string>)` keyed by stage); `ValueEntry.AttachHost(this)`
- [ ] T043 [US4] `DeviceCardKit.EnvReadouts(IValueEntryHost env)` in `DeviceCardKit.cs`: a compact four-column read-out row (labels A · D · S · R, mono 8, Studio tokens) whose read-outs are typeable fields, live-followed via the card's refresher
- [ ] T044 [P] [US4] `DeviceCards/Instruments/VoltInstrumentCard.cs` (amp + filter env) and `BassInstrumentCard.cs` (amp + filter env): pass stage formatters; add `EnvReadouts` where no A/D/S/R knobs sit beside the graph
- [ ] T045 [P] [US4] `DeviceCards/Instruments/AuroraInstrumentCard.cs` (×2), `OperatorInstrumentCard.cs`, `PhysicalInstrumentCard.cs` (noise env): same
- [ ] T046 [P] [US4] `Controls/SynthViz.cs` Env tab: host over the breakpoints (A, D→S, R), box anchored over the read-out caption drawn in the graph
- [ ] T047 [US4] Clip ADSR handles: `ClipAdsrKit.cs` exposes the handle fields (Attack time, Decay time → Sustain level, Release time, `TextAt` from `ClipAdsrKit.Readout`'s formatting); `ArrangementView.LaneControl.cs` attaches them as a host on the lane, commits through `CommitAdsr` (one undo step), and the current double-click reset (`ClipAdsrKit.Reset`) moves to ⌥
- [ ] T048 [P] [US4] Other envelope graphs from the audit (`AfEnvView` in `Controls/AutoFilterViews.cs`, `ShEnvelopeView` in `Controls/ShutterViews.cs`): host over their handles; `EnvReadouts` only if no numeric controls sit beside them

---

## Phase 7: User Story 5 — Type modulation amounts (Priority: P2)

**Goal**: Double-click a matrix cell to type its amount; ⌥-click or **Clear** removes it, undoably.

**Independent Test**: quickstart § 2 Volt matrix row; Consort patch matrix.

- [ ] T049 [US5] `Controls/VoltMatrix.cs`: implement `IValueEntryHost` — one field per routed cell, `TextAt` = signed % ("−100…+100 %", 0.5 = 0), `Set` via `PluginParamSet`, Begin/End via `BeginAutomationWrite` / `EndAutomationWrite` on `mtx{s}_{d}`, `Clear` = Begin → set 0.5 → End (makes clearing undoable); remove the right-click / double-click clear from `OnPointerPressed` (right-click now opens the parameter menu with **Type value…** / **Clear**); update the header comment
- [ ] T050 [US5] `ConsortPatchMatrix` in `Controls/ConsortViews.cs`: host per cell — double-click types the depth (raising `DepthChanged`, which creates the cable), ⌥-click and **Clear** raise `Removed` (⌥ keeps its meaning); remove the double-click removal; owner `DeviceCards/Instruments/ConsortInstrumentCard.cs` supplies Begin/End and `TextAt`

---

## Phase 8: User Story 6 — Type values into graph controls (Priority: P3)

**Goal**: Double-click a graph handle to type each value it sets, Tab through them; empty-space double-click unchanged.

**Independent Test**: quickstart § 2 EQ-8 row; each graph control in contracts/control-audit.md § 3.

- [ ] T051 [P] [US6] `Controls/Eq8Views.cs` (`Eq8Curve`) and `Controls/DynamicEqCurve.cs`: host — band handle → frequency, gain, Q; `FieldsAt` null off a handle (keeps add-band double-click); owners `DeviceCards/Bodies/EqDeviceBody.cs` / `DynamicEqDeviceBody.cs` pass formatters and gestures
- [ ] T052 [P] [US6] `Controls/Eq3Views.cs` (`Eq3Fader` attach, `Eq3Graph` host) with `DeviceCards/Bodies/Eq3DeviceBody.cs`
- [ ] T053 [P] [US6] `Controls/AutoFilterCurve.cs` (cutoff → resonance), `Controls/AutoFilterViews.cs` (`AfFader` attach, `AfLfoView` host) with `DeviceCards/Bodies/AutoFilterDeviceBody.cs`
- [ ] T054 [P] [US6] `Controls/CompViews.cs` (`CompFader` attach, `CompTransferView` threshold, `CompMotionView`, `CompKeyView` hosts) with `DeviceCards/Bodies/CompressorDeviceBody.cs`
- [ ] T055 [P] [US6] `Controls/DelayViews.cs` (`DelayTaps` host, `DelayFader` attach, `DelayFilterCurve` low cut → high cut) with `DeviceCards/Bodies/DelayDeviceBody.cs`
- [ ] T056 [P] [US6] `Controls/ChorusViews.cs`, `Controls/FlangerViews.cs`, `Controls/PhaserViews.cs`, `Controls/OrbitViews.cs` hosts / attaches with their bodies (`ChorusDeviceBody.cs`, `FlangerDeviceBody.cs`, `PhaserDeviceBody.cs`, `AutoPanDeviceBody.cs` for Orbit)
- [ ] T057 [P] [US6] `Controls/CrushViews.cs`, `Controls/LevelViews.cs`, `Controls/LensViews.cs` (`LensVSlider`), `Controls/ShutterViews.cs` (`ShSignalView`, `ShKeyView`), `Controls/ReverbViews.cs`, `Controls/VintageViews.cs`, `Controls/VocoderGraph.cs` — the rows the audit marks H/A, with their bodies
- [ ] T058 [P] [US6] Instrument graphs: `Controls/VoltFilter.cs` (cutoff → resonance), `Controls/SynthViz.cs` Filter pad (X → Y), `Controls/FluxVectorPad.cs` (X → Y), `Controls/KeysViews.cs` (`KeysPickupView`, `KeysTremView`), `Controls/GrainWaveViz.cs`, `Controls/RhythmViews.cs` (`RhythmHitView`, `RhythmSampleView`), `Controls/ChordShiftTrack.cs`, `DeviceCards/Sampler/SamplerViews.cs`, `DeviceCards/Mosaic/MosaicViews.cs` — per audit
- [ ] T059 [US6] `Controls/RangeSliderTrack.cs` in `DeviceCards/Midi/VelocityMidiBody.cs`: host with low / high fields

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T060 [P] DESIGN.md § Controls → Interaction contract: "Double-click types a value (a flat field over the read-out); ⌥/Alt-click restores the default; right-click opens the parameter menu (Type value…, Reset to default) merged with CV-modulate / MIDI Learn where present"; `DragNumber` row; remove the "Slider reset on double-click…" known-gap line (PR 1)
- [X] T061 [P] Preferences → Shortcuts in `PreferencesWindow.cs` via the `nota-shortcuts` skill: add "Double-click a knob, fader, slider or graph handle — type a value", "⌥-click — reset to default", "In the value field: Enter / Tab / ⇧Tab / Esc"; change "Double-click volume" (preview) and the clip ADSR handle row ("double-click resets the stage" → ⌥-click) (PR 1, extended in PR 2)
- [X] T062 `CHANGELOG.md` `[Unreleased]`: Added — type exact values into every parameter control; Changed — double-click on knobs / faders / sliders now types a value, ⌥-click resets; Fixed — clearing a Volt mod-matrix cell is undoable (PR 1 / PR 2 entries)
- [ ] T063 FEATURES.md via `nota-features`; user docs EN + RU in `../nota-docs` via `nota-docs` (device-card basics, mixer, envelopes, mod matrix pages)
- [ ] T064 Tick every row of `specs/035-typed-value-entry/contracts/control-audit.md` (☑ or exempt with reason)
- [ ] T065 Run quickstart.md § 2 in the app in every colour theme (Studio Dark / Light, Ember Graphite / Paper); capture before/after screenshots of the box for the PR (`nota-pr`)
- [ ] T066 `/speckit-converge` until **Converged**

### Nota closing tasks *(PR gates)*

- [X] T067 Run `scripts/build.sh` until native + managed + smoke test (incl. `--valueentry` and the design checks) are green
- [X] T068 AGPL-3.0-only SPDX two-line header on `Controls/ValueText.cs`, `Controls/IValueEntryHost.cs`, `tests/Nota.SmokeTest/ValueEntryTests.cs`
- [X] T069 New dependency: N/A
- [X] T070 Engine behaviour change: N/A — no engine change (parser covered by T004)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)** → **Foundational (T002–T012)** → stories.
- T002 → T003 → T004/T005; T006 → T007 → T008–T011; T012 independent of T006–T011.
- **US1 (Phase 3)** and **US2 (Phase 4)** ship together (PR 1). US2's control edits (T032–T034) can run alongside US1's site edits.
- **US3 (Phase 5)** needs Foundational; ships in PR 1.
- **US4 / US5 (Phases 6–7)** need Foundational and T043 for read-outs; PR 2.
- **US6 (Phase 8)** needs Foundational; PR 3.
- **Polish**: T060–T062 per PR; T063–T066 at the end.

### Within Each Story

- Control API (helpers on the control) before the call sites that use it (T013/T015/T016 before T017–T030).
- A host's control file before its owner card.

### Parallel Opportunities

- T004 with T006–T012.
- T017–T030 are separate files: all [P] once T013–T016 are in.
- T032–T034 in parallel.
- T044–T046, T048 in parallel after T042/T043.
- T051–T058 in parallel.

---

## Parallel Example: User Story 1

```bash
Task: "T020 Effect bodies A–D: set Format in each body's knob helper"
Task: "T021 Effect bodies D–V: set Format in each body's knob helper"
Task: "T023 Instrument cards A–M: set Format"
Task: "T028 MixerView: VFader, PanKnob, MiniFader attach"
```

## Parallel Example: User Story 6

```bash
Task: "T051 Eq8Curve + DynamicEqCurve hosts"
Task: "T054 CompViews hosts"
Task: "T058 Instrument graphs"
```

---

## Implementation Strategy

### MVP (PR 1)

1. Phase 1 + Phase 2.
2. Phase 3 (US1) + Phase 4 (US2) together — the gesture swap is atomic.
3. Phase 5 (US3).
4. T060–T062, T067–T069; `scripts/build.sh`; quickstart § 2 rows for US1–US3.

### PR 2 — envelopes and matrices

Phases 6–7, Shortcuts / CHANGELOG additions, build.

### PR 3 — graph controls

Phase 8, T063–T066, final build. Until PR 2 lands, a Volt matrix double-click still clears (noted in PR 1's description).

---

## Notes

- Do not commit: the maintainer reviews and commits (see project memory).
- Keep the Studio box look identical everywhere — the design checks (`RunControls`, `RunMenus`) must stay green.
- When a graph control already has a "nearest handle" hit test for dragging, reuse it for `FieldsAt`.
