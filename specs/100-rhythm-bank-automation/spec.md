# Feature Specification: Automate the Nota Rhythm pattern bank

**Feature Branch**: `feat/rhythm-bank-automation`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "Nota Rhythm: automate the pattern bank (A–D) from the arrangement. Make the bank a stepped, automatable parameter so arrangement automation, macros and MIDI learn can drive it; the A–D buttons set the same parameter and follow it during playback. Open question: the moment the switch takes effect (next step vs next bar), possibly a setting. Morphing between banks is optional and can be split out. Also check where else the same can be added. Issue: nota-daw/nota#609 (approved)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-11-17

The user handed every clarification to the agent ("весь clarify возьми на себя"); each answer
below is the agent's decision, with the reason.

- Q: When does a bank change take effect — the next step or the next bar? → A: Both, as a
  setting: a new stepped parameter **Bank Switch** with **Step** (default) and **Pattern**
  (the change waits for the pattern's first step). Step is the default because it is what the
  A–D buttons do today, so no saved project changes behaviour; Pattern matches how Nota Twin
  switches its sequencer patterns and keeps a bar whole when a breakpoint is drawn a little
  late.
- Q: Which moment does an automation lane decide the bank for a step — the start of the audio
  block, or the step itself? → A: The step itself (its grid position, before swing). Automation
  is otherwise applied once per audio block, so a breakpoint on a bar line would reach the
  downbeat one step late — and differently in real time and in export, whose block sizes differ.
  The bank must not depend on the block size.
- Q: Is morphing between two banks (probability blend / velocity crossfade) in this change? →
  A: No. It needs its own design (the issue calls it optional); it stays out and can be
  proposed as a follow-up issue.
- Q: Where else does the same problem exist? → A: Nota Twin's sequencer **Pattern** is already
  an automatable parameter, but it reads the lane at the block start too, so a pattern change
  drawn on a bar line is queued one whole pattern late. The same exact-time lane read applies
  to it (User Story 4). No other built-in device keeps a performance selector outside its
  parameters (surveyed every device's action channel: only Rhythm's bank and Twin's pattern
  selector exist; Twin's is already a parameter).
- Q: What does the card show while the bank is automated or queued? → A: The bank that is
  playing is filled (as today); in Pattern mode a bank that is chosen but not yet playing is
  tinted until it takes over. The step strip edits the chosen bank — the one the parameter
  holds.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Draw the bank in the arrangement (Priority: P1)

A producer has a groove in bank A and a fill in bank B on one Nota Rhythm track. In the
arrangement they open the track's automation menu, pick **Perform › Bank**, and draw A for
bars 1–7, B for bar 8, A again from bar 9. On playback and in the exported file bar 8 plays
the fill from its first step, and bar 9 is back on the groove.

**Why this priority**: This is the request in #609 — today a fill needs a second Rhythm track.

**Independent Test**: Draw an A→B→A lane on bar lines, play it and export it; every bar plays
the bank the lane holds at its downbeat, and the export matches the real-time playback.

**Acceptance Scenarios**:

1. **Given** a Rhythm track, **When** the user opens its automation menu, **Then** *Bank* (values A–D) and *Bank Switch* are listed with the other Perform params.
2. **Given** a Bank lane that changes A→B exactly on bar 8 and back on bar 9, **When** the song plays, **Then** the first step of bar 8 already plays bank B and the first step of bar 9 plays bank A.
3. **Given** the same lane, **When** the song is exported, **Then** the steps played match real-time playback step for step, whatever the export block size.
4. **Given** a Bank lane, **When** it is drawn, **Then** it shows four levels named A–D.

---

### User Story 2 - The card follows and drives the bank (Priority: P1)

While the lane plays, the A–D buttons on the Rhythm card light up the bank that is playing and
the step strip shows that bank's steps. Clicking a bank button is an edit of the Bank parameter
like turning any other control: it is undoable, MIDI learn and macros can drive it, and with
automation write armed it records into the lane.

**Why this priority**: Without it the card shows a stale bank and edits land in the wrong one.

**Independent Test**: Play a project with a Bank lane and watch the card; stop, click B,
undo, and check the bank returns to A.

**Acceptance Scenarios**:

1. **Given** a playing Bank lane, **When** it changes bank, **Then** the card's bank buttons and step strip follow within one UI refresh.
2. **Given** no lane, **When** the user clicks bank C, **Then** C plays from the next step (Step mode) and the click is one undo step.
3. **Given** Pattern mode and a running transport, **When** the user clicks bank C, **Then** C is shown as queued (tinted) and starts on the pattern's first step, then shows as playing.
4. **Given** a MIDI controller learned to Bank, **When** it is turned, **Then** the bank changes like any other learned control.

---

### User Story 3 - Old projects open unchanged (Priority: P1)

A project saved before this change opens on the bank it was saved with, and plays exactly as
before.

**Why this priority**: Saved songs must not change.

**Independent Test**: Load a pre-change project whose Rhythm was saved on bank C; the card shows
C, and it plays C.

**Acceptance Scenarios**:

1. **Given** a project saved with the Rhythm on bank C, **When** it is opened, **Then** the Bank parameter is C and Bank Switch is Step.
2. **Given** a factory or user drum kit, **When** it is loaded, **Then** the patterns and bank behave as before.

---

### User Story 4 - Nota Twin's pattern lane lands on time (Priority: P2)

A producer automates Nota Twin's sequencer **Pattern** to move from pattern 1 to pattern 2 on
bar 5. Today the change is read a fraction of a step late and so queues behind the whole of
pattern 1's next cycle; after this change pattern 2 starts on bar 5, in playback and export.

**Why this priority**: The same defect, already shipped, in the other built-in sequencer —
found while checking "where else this applies". It reuses the mechanism of Story 1.

**Independent Test**: Draw a Twin Pattern lane changing on a bar line that is also the end of
the playing pattern; pattern 2's first step plays on that bar.

**Acceptance Scenarios**:

1. **Given** a Twin with a one-bar pattern 1 and a lane changing to pattern 2 on bar 5, **When** the song plays or exports, **Then** pattern 2 starts on bar 5.
2. **Given** no lane, **When** the user picks a pattern on the card, **Then** it still switches at the playing pattern's end, as today.

---

### Edge Cases

- A lane breakpoint drawn slightly after a bar line (Step mode): the bar's first steps play the old bank until the breakpoint, then the new one; in Pattern mode the whole bar keeps the old bank and the new one starts on the next pattern.
- A lane ramp (two points with different banks): each step plays the bank the ramp holds at that step's grid position (rounded to the nearest of A–D).
- Swing: an odd step plays late, but its bank is the one at its grid position, so swing never moves a switch.
- Loop wrap and seeking: the bank is read again at the first step after the jump.
- Transport stopped: the card shows the bank the lane holds at the playhead; live pads (finger drumming) are not affected by the bank.
- Writing automation (Touch/Latch): while the user holds the control the lane is not read, as for every param.
- The Rhythm inside an Instrument Rack chain: its Bank lane behaves the same.
- A macro of the Rhythm's own cannot target Bank (as today, macros can't target params after the macro block); rack macros can.
- MCP `select_rhythm_bank` keeps working and sets the same parameter.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Nota Rhythm MUST expose a stepped parameter *Bank* with values A, B, C, D, automatable, learnable and macro-targetable like its other params, listed in the automation menu under Perform.
- **FR-002**: Nota Rhythm MUST expose a stepped parameter *Bank Switch* with values *Step* (default) and *Pattern*.
- **FR-003**: In Step mode each step MUST play the bank the Bank parameter (or its lane) holds at that step's grid position; in Pattern mode the bank MUST change only on the pattern's first step, to the bank held there.
- **FR-004**: When a Bank (or Twin Pattern) lane is playing, the instrument MUST read the lane at the step's own musical position, not at the start of the audio block, so the result is the same in real time and in export at any block size.
- **FR-005**: The A–D buttons MUST set the Bank parameter as a discrete control edit (one undo step; records while automation write is armed) and MUST show the playing bank, plus a queued bank (tinted) in Pattern mode.
- **FR-006**: Step edits (toggle, velocity, accent, clear voice) MUST apply to the bank the Bank parameter holds.
- **FR-007**: Projects and kits saved before this change MUST open with Bank set to the saved bank and Bank Switch set to Step.
- **FR-008**: The MCP Rhythm guide and the snapshot MUST describe the Bank and Bank Switch params; the bank tool MUST set the parameter.
- **FR-009**: Nota Twin's sequencer Pattern MUST read its lane at the step's musical position (FR-004), keeping its pattern-end switch.
- **FR-010**: Reading a lane at a step MUST NOT allocate, lock or do I/O on the audio thread.

### Key Entities

- **Bank parameter**: which of the four patterns plays — normalized 0..1, four steps (A = 0, B = ⅓, C = ⅔, D = 1).
- **Bank Switch parameter**: when a new bank takes over — Step or Pattern.
- **Playing bank**: the bank the last step played; can differ from the Bank parameter in Pattern mode until the next pattern start.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a lane switching banks on bar lines, 100 % of bars play the bank the lane holds at their downbeat, in real time and in export.
- **SC-002**: An export rendered at two different block sizes produces the same step triggers (identical audio for a synth-only kit with Humanize at 0).
- **SC-003**: A project saved before the change reopens on its saved bank in 100 % of cases (smoke test covers an RTH3 blob).
- **SC-004**: A Twin Pattern lane change on a bar line starts the new pattern on that bar (not one pattern later).

## Assumptions

- The issue's part 2 (morph between banks) is out of scope; it can be a follow-up issue.
- "Bar" in the issue means the Rhythm's pattern: 16 steps of 1/16, which is a 4/4 bar.
- The automation lane draws discrete values as it does for other stepped params; no new lane drawing mode is added.
- MIDI clip envelopes are not part of this change: track lanes (and lanes of a device inside a rack) are what drives the bank.
- A user-visible change gets a `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md and user-docs updates.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: the lane is read from the immutable Graph the block already holds; no alloc / lock / I/O (AR-5, AR-6)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7) — no new ABI needed (params already cross)
- [x] Smoke-test impact assessed: engine self-test for lane timing + RTH3 load
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md, user docs
- [x] UI change → follows `DESIGN.md` (Studio design language)
