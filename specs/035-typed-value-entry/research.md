# Research: Typed value entry on every parameter control

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-10-21

Findings from reading the code on `develop` (d4cfb96), and the decisions taken from them.

## R1 — Gesture scheme

**Decision**: double-click (control, read-out or graph handle) → text box; ⌥/Alt-click →
reset to default (or clear a route / point); right-click → a parameter menu with
**Type value…** and **Reset to default** (or **Clear**) where the control has no menu of
its own, merged into the existing menu where it has one. No Enter-to-open on a focused
control (see R2).

**Rationale**: Ctrl/⌘ and Shift are fine-drag on `Knob`, `MiniFader`, `SliderTrack`,
`RangeSliderTrack` and `VoltMatrix` (`(KeyModifiers.Control | Meta | Shift)`), so
⌘/Ctrl-click cannot be reset. ⌥ is free on every parameter control; where it is taken
(Consort step / pitch lanes paint with ⌥, `ConsortJackField` pulls with ⌥, Rhythm and
Arp grids, arrangement off-grid) it is not a parameter control or it already means
"remove" (`ConsortPatchMatrix`: ⌥-click or double-click removes — ⌥ keeps that meaning,
double-click moves to typing). Device-card knobs have no right-click menu today (only
`ModularView` knobs have the CV-modulate menu, via `ContextRequested`), so the parameter
menu is new there and merges into `ModularView`'s and `MacroPanel`'s.

**Alternatives considered**: ⌘-click reset (collides with fine drag; on macOS
Ctrl-click is a right-click); keep double-click = reset and type on ⌥-double-click
(hidden, and the issue asks for double-click = type).

## R2 — Enter on a focused control

**Decision**: dropped. Return stays **Stop** (and *launch* in the Session grid).
Text entry opens by double-click, the context menu, and Tab from an open box.

**Rationale**: `MainWindow.OnGlobalTransportKey` handles Return in the tunnel phase for
any focus except a `TextBox` / `NumericUpDown` — deliberately, it fixed "the transport
buttons periodically don't work". Knobs and faders are not focusable today; making them
focusable and letting Return open entry would break that guarantee for whatever control
was last clicked. Inside the open box Return is a `TextBox` key, so commit-on-Enter works.

**Alternatives considered**: Return opens entry when a parameter control has focus
(breaks the transport rule, surprising after a click on a knob); a chord such as ⌘Return
(undiscoverable, one more shortcut to document). The spec's FR-002 and Story 3 were
updated to match.

## R3 — One helper, pure value ↔ text

**Decision**: split `Controls/ValueEntry.cs` in two:

- `Controls/ValueText.cs` — pure: `Read` (the quantity parser) and `Solve` (the
  read-out search), no Avalonia. Linked into `tests/Nota.SmokeTest` the way `Changelog.cs`
  is, so the parser and solver get managed tests.
- `Controls/ValueEntry.cs` — Avalonia: the box (adorner), attaching to controls and
  hosts, the Tab chain, the parameter menu.

A typeable value is a **field** (see [data-model.md](data-model.md)): `Get` (0..1),
`TextAt(n)` (the read-out at position n — **pure**), `Set(n)`, `Begin` / `End`,
optional `Default`. Commit = `Begin(); Set(Solve(...)); End();`.

**Rationale**: the existing slider path probes the read-out through the row's own setter
(`setNorm` then `text()`), which writes the engine ~70 times per commit before restoring.
That is safe for undo (the gesture opens after the restore and the engine drops no-op
steps — `Engine::endMixGesture`: "exact: no net change, no step") but sends throwaway
parameter changes to the audio thread. New attachments pass a pure `TextAt`; the
`SliderRow` / `ClipEditorKit` signature keeps its probing adapter so its ~60 callers don't
all change in this feature, and gains an optional pure `textAt` that the converted bodies
use.

**Alternatives considered**: an inverse map per parameter (every card would need one;
`Solve` already exists and handles stepped read-outs, words and unit changes); probing
through `Knob.ValueChanged` (writes the engine, and the read-out `TextBlock` is updated
by card code, not the knob).

## R4 — How controls get typed entry

**Decision**:

- Single-value controls (`Knob`, `PanKnob`, `MiniFader`, `VFader`, `SliderTrack`, bars):
  `ValueEntry.Attach(control, field, readout?)` adds **tunnel** pointer handlers on the
  control and its read-out: `ClickCount == 2` (left) opens the box over the read-out (or
  over the control when it has none); left + ⌥ resets. Handled in tunnel, so the
  control's own `OnPointerPressed` never sees them; the controls' own double-click reset
  branches are removed. `Knob` gains `Format : Func<double, string>?` so
  `DeviceCardKit.KnobCell` can attach automatically when the knob has one.
- Multi-value graph controls implement `IValueEntryHost.FieldsAt(Point)` → the fields of
  the handle under the pointer (time then level; frequency → gain → Q; X → Y) and the
  rectangle to anchor the box to; `null` = not on a handle, so the event passes through
  and an existing empty-space double-click (add band / point) is kept.
  `ValueEntry.AttachHost(control)` wires the same tunnel handlers.
- `Default` NaN / absent → ⌥-click does nothing and the menu item is disabled.

**Rationale**: one place decides the gesture (FR-002, FR-009) instead of ~40 controls
each re-implementing it; graph controls only describe their handles. The first click of
a double-click starts a drag gesture with no change — the engine drops it, so a typed
commit is still one step.

**Alternatives considered**: a base class for all controls (they derive from `Control`
directly and differ a lot); reusing `IMidiLearnRegions` (only four controls implement it
and it maps strips, not handles; it may be built on top of `FieldsAt` later).

## R5 — Tab chain and scope

**Decision**: `ValueEntry.Scope` attached property marks a card root (device card body,
instrument card, rack card, mixer strip, clip editor panel, detail Synth tab). Tab from an
open box commits, then collects the attached controls and host fields inside the nearest
scope that are visible and enabled, orders them by reading order (rows bucketed by top
edge within half a row height, then left edge; a host's fields in its own order), and
opens the next one; wraps; Shift+Tab reverses.

**Rationale**: no focus order exists to reuse (the controls are not focusable) and cards
are laid out in different panels; geometric order matches what the user sees, including
after a tab / section switch (only shown controls count). Tab is handled in the box, so
the window-level Tab (detail-tab cycling) is not affected — `MainWindow.OnKeyDown`
already returns early for a `TextBox` source.

## R6 — Keys while the box is open

**Decision**: nothing to add. Space / Return / note keys are skipped for a `TextBox`
source in both `OnGlobalTransportKey` and `OnKeyDown`, and
`ReleaseTextFocusOnOutsidePress` drops focus on an outside press, which the box treats
as commit (it already closes on `LostFocus` and on an outside press).

## R7 — Box look

**Decision**: keep `TextBox.field` (well fill, 1 px rule edge, accent edge while
focused — DESIGN.md § Readouts and fields), radius `NotaRadius.Badge`, mono family and the
read-out's own size, right-aligned, at least 48 wide, growing left over the control;
kept inside the top level's bounds (clamped against `TopLevel.ClientSize`). Value
pre-selected. Same in every theme because it is drawn from tokens only.

## R8 — Envelope editors

**Decision**:

- `VoltEnv` (Volt, Bass, Aurora ×2, Operator, Physical noise env): `IValueEntryHost` —
  attack node → A; decay/sustain node → D then S; release node → R; segment hit
  (nearest segment within 6 px) → that stage's time. `TextAt` uses the instrument's own
  stage formatters (the same ones its knobs or read-outs use); where the card has no A/D/S/R
  knobs next to the graph (Volt amp / filter env, Bass, Aurora), a compact four-column
  read-out row (`DeviceCardKit.EnvReadouts`, mono 8, labels A · D · S · R) is added under
  the graph, each read-out typeable and in the Tab chain.
- `SynthViz` Env tab: same host, breakpoints → A, D/S, R; its read-outs are already
  drawn into the graph, so typing opens over the read-out caption.
- Clip ADSR handles (`ClipAdsrKit`, arrangement lane): host over the three handles; commit
  through the lane's existing `CommitAdsr` (one undo step). The audio clip editor already
  has typeable ADSR slider rows (`ClipEditorKit`).
- Other envelope graphs found in the audit (`AfEnvView`, `ShEnvelopeView`, Monolith /
  Pentad / Consort env strips with inline knobs) follow the same rule: host on the graph,
  read-out row only if no numeric control sits beside it.

## R9 — Modulation matrices

**Decision**: `VoltMatrix` becomes a host: one field per cell, amount shown and typed in
% (−100…+100; the parameter is 0..1 with 0.5 = 0). Double-click types; ⌥-click and the
menu's **Clear** reset to 0. Clear is wrapped in `Begin/End` — today right-click /
double-click writes `PluginParamSet(…, 0.5f)` outside a gesture, so clearing a cell is
not undoable; this fixes it. `ConsortPatchMatrix` (depth cells, `DepthChanged` /
`Removed`) gets the same: double-click types the depth (creating the cable), ⌥-click and
**Clear** remove. Right-click on a `VoltMatrix` cell opens the menu instead of clearing.

## R10 — Inventory size (for tasks)

| Family | Sites |
|---|---|
| `Knob` | 44 files construct knobs; most bodies go through one local helper or `DeviceParamControls` / `InstrumentControls` |
| `DeviceCardKit.KnobCell` | 65 call sites |
| `SliderTrack` outside the kits | 13 bodies (most already call `ValueEntry.Attach` on their read-out) |
| `RangeSliderTrack` | Velocity MIDI body |
| `MiniFader` | arrangement header ×2, rack ×2, main window, mixer, session mixer ×2 |
| `VFader`, `PanKnob` | mixer |
| `PanBar`, `MixBar`, `PerformWheel`, `ChordShiftTrack` | 2 / 2 / 4 / 2 |
| Graph controls in `Controls/` | ~40 classes in 30 files (curves, faders, pads, envelope and step views) |
| Matrices | `VoltMatrix`, `ConsortPatchMatrix` |

The full per-class list with decisions is [contracts/control-audit.md](contracts/control-audit.md).
