# Feature Specification: Typed value entry on every parameter control

**Feature Branch**: `035-typed-value-entry`

**Created**: 2026-10-21

**Status**: Draft

**Input**: User description: "Type exact values into every parameter control (knobs, sliders, faders, ADSR/envelope editors, mod matrices, graph controls) — GitHub issue nota-daw/nota#240." The issue asks that every control editing a numeric parameter can switch into a text-entry mode, with one gesture scheme everywhere (double-click → text box; reset to default moves to ⌥/Alt-click; Enter on a focused control), the existing value parser reused, Enter / click-away commit, Esc cancel, Tab to the next parameter, one undo step and one automation gesture per commit, typed editing of envelope breakpoints plus editable A/D/S/R read-outs, typed mod-matrix amounts, and a flat Studio-styled text box. Acceptance: an audit list of every numeric control type, each checked off; the Shortcuts pane, user docs and FEATURES.md updated.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

All the controls live in `src/managed/Nota.App`. Typed entry already exists in two forms:
`Controls/DragNumber.cs` (double-click → text box) and `Controls/ValueEntry.cs`, an
adorner attached to slider read-outs by `DeviceCardKit` / `ClipEditorKit` and by a dozen
device bodies (EQ, Dynamic EQ, Forge, Strata, AutoGain, the MIDI effects, the rack card).
`ValueEntry` already reads sign, numbers and fractions, k / ms / µs multipliers, L/R pan,
∞ and words the read-out shows, and searches the control position whose read-out shows
the typed quantity — so it needs no inverse value mapping.

Controls without typed entry today, and what double-click does on them now:

- `Knob` (≈44 files build knobs directly) and `PanKnob` — double-click resets to default.
- `MiniFader` — double-click resets; `VFader` — no double-click behaviour.
- `SliderTrack` / `RangeSliderTrack` used outside the kits — double-click calls `Reset`.
- Envelope / ADSR editors — `VoltEnv`, `SynthViz` and the other instrument graph editors,
  `ClipAdsrKit`: breakpoints can only be dragged.
- `VoltMatrix` — a cell's amount can only be dragged; right-click or double-click clears it.
- Graph controls in device views that set a number (filter / compressor / EQ curves,
  `FluxVectorPad` and other XY pads, bars such as `ValueBar` / `PanBar` / `MixBar`).

Modifier constraints found in the code: Ctrl/⌘ and Shift already mean *fine drag* on
`Knob`, `MiniFader`, `SliderTrack` and `RangeSliderTrack`, so reset cannot move to
⌘/Ctrl-click. ⌥/Alt already carries a meaning in a few step / paint editors (Consort,
Rhythm, Arp grids) and in the arrangement (off-grid). Context menus were restyled in
specs/034-context-menus, so a "Reset" / "Clear" menu item is available as a fallback.
No engine change is expected: values reach the engine through the same parameter setters
and gestures a drag uses.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Type a value into a knob, fader or slider (Priority: P1)

A producer wants a filter cutoff at exactly 1.2 kHz, a send at −6 dB, a pan at 30 L. They
double-click the knob, fader or slider (or its read-out), a small text box opens over the
read-out with the current value selected, they type the value in the units shown and
press Enter. The control lands on that value exactly as the read-out would show it.

**Why this priority**: Knobs, faders and sliders are the bulk of the parameter controls in
Nota and the most common reason a user needs an exact value. This alone closes most of
the gap the issue describes.

**Independent Test**: On any device card, the mixer and a rack pad, double-click a knob,
a fader and a slider, type a value with and without a unit, press Enter, and check the
read-out, the parameter value, one undo step and one automation gesture.

**Acceptance Scenarios**:

1. **Given** a knob showing "800 Hz", **When** the user double-clicks it, types "1.2k" and presses Enter, **Then** the read-out shows "1.20 kHz" (or the read-out's own rounding of 1200 Hz) and the parameter is at that value.
2. **Given** a mixer fader at 0 dB, **When** the user double-clicks it, types "-6" and presses Enter, **Then** the fader sits at −6 dB and a single undo restores 0 dB.
3. **Given** a pan knob, **When** the user types "30L", **Then** pan is 30 % left; typing "C" centres it.
4. **Given** an open text box, **When** the user presses Esc, **Then** the box closes and the value is unchanged, with no undo step and no automation written.
5. **Given** an open text box, **When** the user clicks elsewhere, **Then** the typed value is committed as if Enter had been pressed.
6. **Given** a parameter with range 20 Hz–20 kHz, **When** the user types "50k", **Then** the value is clamped to 20 kHz.
7. **Given** text that the control cannot read (e.g. "abc"), **When** the user commits, **Then** the value stays where it was and no undo step is added.
8. **Given** automation recording (write / touch) is armed on that parameter, **When** a typed value is committed, **Then** it is recorded as one gesture, exactly like a drag that ended at that value.

---

### User Story 2 - One reset gesture everywhere (Priority: P1)

Since double-click now opens text entry, a user who wants the default value back
⌥/Alt-clicks the control. This works on every control that had a double-click reset
before, and on those that had none (e.g. `VFader`) wherever the parameter has a default.

**Why this priority**: Moving double-click to text entry removes the existing reset
gesture; without a replacement in the same change, users lose a feature. It ships with
Story 1.

**Independent Test**: ⌥/Alt-click each control family from the audit list and check that
it returns to its default in one undo step.

**Acceptance Scenarios**:

1. **Given** a knob moved away from its default, **When** the user ⌥/Alt-clicks it, **Then** it returns to its default value as one undo step and one automation gesture.
2. **Given** a control with no defined default, **When** the user ⌥/Alt-clicks it, **Then** nothing changes and nothing is written.
3. **Given** a control's context menu, **When** it is opened, **Then** it offers "Reset to default" (disabled when already at default or without a default) and "Type value…", which opens the same text box.
4. **Given** a graph editor where ⌥/Alt already has a meaning (paint / remove / off-grid), **When** the user ⌥/Alt-clicks there, **Then** the existing meaning is kept and reset is reachable from the context menu.

---

### User Story 3 - Keyboard: Tab to the next parameter (Priority: P2)

A user tuning a device by keyboard opens the text box on one parameter, types a value and
presses Tab: the value commits and the next parameter on the same card opens for typing.
Shift+Tab goes back. (Return outside a text box stays Stop / Session launch, so a focused
control does not open entry on Enter — see research R2.)

**Why this priority**: It turns setting several exact values into one fast pass instead of
a double-click per control, but Story 1 is already useful without it.

**Independent Test**: On a device card with several knobs and sliders, open entry on the
first, press Tab repeatedly and check the order, the commits and that focus stays on the
card.

**Acceptance Scenarios**:

1. **Given** an open text box on a card, **When** the user presses Tab, **Then** the typed value commits (one undo step) and the next typeable parameter on that card, in reading order (left to right, top to bottom), opens with its value selected.
2. **Given** the last typeable parameter on a card, **When** the user presses Tab, **Then** entry wraps to the first parameter of the same card; Shift+Tab moves backwards the same way.
3. **Given** a control the user just clicked, **When** the user presses Return, **Then** the transport stops as before (no text box opens).
4. **Given** the text box is open, **When** the user presses any app-wide shortcut key (Space, numbers, letters), **Then** the key goes into the text box and does not trigger transport or other commands.

---

### User Story 4 - Type envelope times and levels (Priority: P2)

A sound designer wants an attack of exactly 12 ms and a sustain of −6 dB. They
double-click the attack breakpoint (or the attack segment) on the envelope graph and type
"12", or type into the A/D/S/R read-outs shown next to the graph.

**Why this priority**: Envelopes are the second most common place where exact values
matter, and today the graph is drag-only.

**Independent Test**: On each instrument with an envelope graph, double-click each
breakpoint / segment, type a value, and edit each A/D/S/R read-out; check the graph, the
parameter, undo and automation.

**Acceptance Scenarios**:

1. **Given** an envelope graph, **When** the user double-clicks the attack breakpoint, **Then** a text box opens for the attack time; typing "12ms" (or "12") sets attack to 12 ms.
2. **Given** a breakpoint that sets two values (e.g. decay time and sustain level), **When** the user double-clicks it, **Then** the box edits the value the breakpoint moves along the time axis, and Tab moves to its level value.
3. **Given** a segment between two breakpoints, **When** the user double-clicks the segment, **Then** the box edits that segment's time (attack, decay or release).
4. **Given** an envelope graph without adjacent numeric controls for its stages, **When** it is shown, **Then** compact editable A/D/S/R read-outs are shown next to it in the units of each stage (ms / s for times, dB or % for sustain as the instrument shows it).
5. **Given** an envelope editor that already sits beside A/D/S/R knobs, **When** it is shown, **Then** no duplicate read-outs are added — those knobs are typeable through Story 1.

---

### User Story 5 - Type modulation amounts (Priority: P2)

In a modulation matrix the user double-clicks a cell and types "-35" to set a −35 %
route. ⌥/Alt-click (or "Clear" in the context menu) removes the route.

**Why this priority**: Mod amounts are fiddly to drag precisely in small cells, and today
double-click destroys a route instead of editing it.

**Independent Test**: In Volt's matrix (and any other mod matrix in the audit), type
amounts into empty and filled cells, clear cells with ⌥/Alt-click and the context menu,
check undo and automation.

**Acceptance Scenarios**:

1. **Given** an empty matrix cell, **When** the user double-clicks it and types "50", **Then** the route is created at +50 %.
2. **Given** a cell, **When** the user types "-150", **Then** the amount is clamped to −100 %.
3. **Given** a filled cell, **When** the user ⌥/Alt-clicks it, **Then** the route is cleared as one undo step.
4. **Given** a filled cell, **When** the user right-clicks it, **Then** a context menu offers "Type value…" and "Clear".

---

### User Story 6 - Type values into graph controls (Priority: P3)

On a filter, compressor or EQ curve, or an XY pad, a handle or point sets one or two
numbers. Double-clicking the handle opens text entry for its value(s): an EQ band's
frequency then (Tab) gain then (Tab) Q; an XY pad's X then Y.

**Why this priority**: These controls usually have neighbouring knobs or read-outs for the
same parameters, which Story 1 already makes typeable; typing on the graph itself is a
convenience.

**Independent Test**: For each graph control in the audit list, double-click a handle,
type each value it sets, and check the graph and the paired controls.

**Acceptance Scenarios**:

1. **Given** an EQ band handle, **When** the user double-clicks it, **Then** a text box opens for the band frequency next to the handle; Tab moves to gain, then Q.
2. **Given** an XY pad, **When** the user double-clicks its point, **Then** the box edits X, and Tab moves to Y.
3. **Given** a graph control whose double-click already does something else (e.g. adds a band or point on an empty area), **When** the user double-clicks empty space, **Then** that existing behaviour is kept; only double-clicking a handle opens entry.

---

### Edge Cases

- A control's read-out shows a word at one end ("off", "−∞", "HOLD"): typing that word lands on that end; typing a number past it clamps.
- A stepped or rounded read-out ("0.0 dB"): the typed value lands in the middle of the step that shows it, not on its edge (current `ValueEntry` behaviour).
- A bare number on a control whose read-out changes unit across its range (ms ↔ s, Hz ↔ kHz): the number takes the unit shown now when it fits, else the unit that fits.
- The parameter changes underneath the open box (automation playback, a remote/MCP edit): the box keeps the user's text; commit writes the typed value; Esc leaves whatever the parameter now is.
- The control's card is folded, scrolled away, removed or the track deleted while the box is open: the box closes without committing.
- The control is disabled or the device bypassed / inactive: double-click does not open entry on a disabled control; a bypassed device still accepts typed values (as it accepts drags).
- Locale: a decimal comma ("1,5") is read as 1.5; a typed thousands separator is not supported.
- Typing during playback must not glitch audio: the commit is one ordinary parameter change.
- Two values on one handle (EQ, XY, decay/sustain): Esc cancels only the value whose box is open; values already committed with Tab stay committed (each its own undo step).
- A Tab chain on a card whose layout changes (a tab or section switch) continues among the controls currently shown.
- A double-click that starts a drag (press, move): it is a drag, not text entry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every control that edits a numeric parameter MUST offer a text-entry mode in which the user types the value in the units the control displays.
- **FR-002**: Text entry MUST open on double-click of the control or of its read-out, from a "Type value…" item in the control's context menu, and by Tab / Shift+Tab from another open box on the same card. Return outside a text box keeps its transport meaning (Stop; launch in the Session grid).
- **FR-003**: Typed text MUST be read by one shared parser everywhere: sign, decimals (dot or comma) and fractions, unit multipliers (k, ms, µs, s, Hz/kHz, dB, %, st/ct), L/R/C for pan, ∞, and any word the control's read-out shows (off, HOLD …).
- **FR-004**: A typed value outside the parameter's range MUST be clamped to the range; text that cannot be read MUST leave the value unchanged and add nothing to undo or automation.
- **FR-005**: Enter and a click outside the box MUST commit; Esc MUST cancel and restore the value shown before the box opened.
- **FR-006**: A commit MUST be exactly one undo step and exactly one automation gesture (begin / end), indistinguishable in history and automation from a drag that ends at the same value.
- **FR-007**: Tab MUST commit and open entry on the next typeable parameter of the same card (or editor panel) in reading order, wrapping at the end; Shift+Tab MUST go the other way.
- **FR-008**: While the box is open, keystrokes MUST go to the box and not trigger app-wide shortcuts.
- **FR-009**: Reset to default MUST move from double-click to ⌥/Alt-click on every control that previously reset on double-click, and be available on every control that has a default; it MUST be one undo step and one automation gesture.
- **FR-010**: Every typeable control's context menu MUST offer "Reset to default" (and "Clear" where the value is a route or point that can be removed), so reset is reachable where ⌥/Alt already has another meaning.
- **FR-011**: Fine-drag modifiers (Shift, Ctrl/⌘) MUST keep their current behaviour.
- **FR-012**: Envelope / ADSR graph editors MUST open typed entry on double-click of a breakpoint (time first, Tab to level when it has one) and of a segment (that stage's time).
- **FR-013**: Envelope graph editors that have no adjacent numeric controls for their stages MUST show compact editable A/D/S/R (or the editor's own stage) read-outs next to the graph.
- **FR-014**: Modulation matrix cells MUST open typed entry for their amount (−100…+100 %) on double-click; clearing a cell MUST move to ⌥/Alt-click and a "Clear" context-menu item.
- **FR-015**: Graph controls whose handles set numeric parameters (filter / compressor / EQ curves, XY pads, value / pan / mix bars) MUST open typed entry on double-click of a handle, cycling with Tab through the values that handle sets; existing double-click actions on empty graph space MUST be kept.
- **FR-016**: The text box MUST look the same on every control and in every colour theme: a flat box over (or beside) the read-out, Studio tokens, mono figures, current value pre-selected; it MUST stay inside the window.
- **FR-017**: The feature MUST ship with an audit list of every numeric control type and every place that builds one outside the shared kits, each item checked off as converted (or recorded as exempt with a reason).
- **FR-018**: The Shortcuts pane, the user docs (EN + RU) and FEATURES.md MUST describe the new gestures (double-click = type, ⌥/Alt-click = reset, Enter / Tab / Esc in the box), and CHANGELOG `[Unreleased]` MUST note that double-click no longer resets.

### Key Entities

- **Typeable parameter control**: any on-screen control that sets a numeric parameter — its current value, its read-out text at any position, its range, an optional default, and the gesture hooks (begin / end) that bracket a change.
- **Value entry session**: one open text box — the control it belongs to, the value before it opened, the typed text; ends in commit or cancel.
- **Card tab order**: the reading-order sequence of typeable controls on one device card or editor panel, used by Tab / Shift+Tab.
- **Control audit list**: the inventory of numeric control types and their non-kit call sites, with conversion status.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of the control types in the audit list accept typed values; none is left drag-only without a recorded reason.
- **SC-002**: Typing a value that the read-out can show lands on a position whose read-out shows exactly that value in at least 99 % of probed cases across all audited controls (excluding clamped inputs).
- **SC-003**: A user can set five parameters on one card to exact values by keyboard (one double-click, then typing and Tab) in under 15 seconds.
- **SC-004**: Every committed typed value and every ⌥/Alt-click reset produces exactly one undo step and one automation gesture — verified on every control family.
- **SC-005**: Reset to default is reachable on 100 % of controls that had it before the change.
- **SC-006**: The text box renders identically (same metrics, Studio tokens) in all four colour themes.

## Assumptions

- ⌥/Alt-click is the reset gesture (not ⌘/Ctrl-click), because Ctrl/⌘ and Shift already mean fine drag on knobs, faders and sliders.
- Where ⌥/Alt already carries a meaning (Consort / Rhythm / Arp step and paint editors, arrangement off-grid), that meaning wins and reset is reached through the context menu.
- The Tab order stays inside one device card or editor panel and wraps; it does not jump across devices.
- "Units shown" means the units of the control's own read-out; controls without a visible read-out (bare knobs in tight rows) use the parameter's display text — the same text their tooltip or hover read-out shows.
- Plugin (VST3/AU) generic parameter views are covered when they use the same Nota controls; plugins' own editors are out of scope.
- Step sequencer cells, note velocity bars in the piano roll and arrangement automation breakpoints are outside this feature unless they turn out to use one of the audited parameter controls; the audit records them either way.
- No new dependency and no engine change: values go through the existing parameter setters and gesture hooks.
- Nota Remote (phone UI) is out of scope.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only; commits use existing parameter setters
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no new crossings
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change; parser / solver covered by managed tests
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
