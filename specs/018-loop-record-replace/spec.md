# Feature Specification: Loop record mode — Overdub / Replace

**Feature Branch**: `fix/loop-seam-note-cut`

**Created**: 2026-10-16

**Status**: Clarified

**Input**: User description: "Loop-record mode toggle, Overdub / Replace (nota-daw/nota#29, the second half of the issue). Today, when MIDI is recorded with the Arrangement transport loop on, every pass overdubs into the same take, so playing the same phrase on each pass stacks the notes at slightly different timings. Add a loop-record mode: Overdub (today's behaviour: each pass adds to the take) and Replace (each new pass replaces what earlier passes of this take recorded in the loop region, so only the latest pass remains). It should be quick to switch: the reporter asks for "a shortcut or an easy button" because some people want overdub. The seam bug (notes cut at the loop seam, a note held across the seam) is already fixed on this branch (.specify/bugs/loop-seam-note-cut) and is out of scope."

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

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Re-record a loop until it's right (Priority: P1)

A player loops one bar and records MIDI. They play the phrase, don't like it, and play it again on
the next pass, and again, until it's right. In Replace mode, each new pass that they actually play
on takes the place of what earlier passes of this take recorded. When they stop, the take holds
only the latest pass, not several near-identical phrases stacked a few milliseconds apart.

**Why this priority**: This is the reported problem (#29). Without it, looping a bar to practise
and record a part leaves a smeared, doubled take that has to be cleaned up by hand.

**Independent Test**: Turn on the loop over 1 bar, choose Replace, and record. Play the same
4-note phrase on three passes with slightly different timing, then stop. The take contains exactly
the 4 notes of the last pass.

**Acceptance Scenarios**:

1. **Given** Replace mode, a 1-bar loop and Record on, **When** the player plays a phrase on pass 1 and a different phrase on pass 2, **Then** after stopping the take contains only the pass-2 notes.
2. **Given** Replace mode, **When** the player plays on pass 1 and plays nothing on pass 2, **Then** pass 1's notes remain (a silent pass does not wipe the take).
3. **Given** Replace mode and a note held across the loop seam (split into two notes by the seam fix), **When** the player then plays on the following pass, **Then** the split note is treated consistently: the continuation that started the new pass belongs to that pass.

---

### User Story 2 - Keep overdubbing when that's the intent (Priority: P1)

A player builds a drum pattern on a loop: kick on pass 1, snare on pass 2, hats on pass 3. In
Overdub mode every pass adds to the take, exactly as today.

**Why this priority**: The reporter stresses that some people rely on overdubbing. Changing the
behaviour without a way back would break their workflow.

**Independent Test**: Choose Overdub, loop 1 bar, and record a different pitch on each of three
passes. After stopping, the take holds all three.

**Acceptance Scenarios**:

1. **Given** Overdub mode, **When** the player records on three passes, **Then** the take holds every note from every pass (today's behaviour, unchanged).

---

### User Story 3 - Switch quickly, even while recording (Priority: P2)

The player switches between Overdub and Replace with one click or a keyboard shortcut, without
opening Preferences. The current mode is visible near the transport, and the choice is remembered
next time.

**Why this priority**: The reporter asked for "a shortcut or an easy button". If the mode is buried,
people won't find it or will forget which one is on.

**Independent Test**: Press the shortcut. The visible mode indicator flips, and the next recorded
pass follows the new mode. Restart the app and the mode is the same.

**Acceptance Scenarios**:

1. **Given** the app is open, **When** the player presses the loop-record-mode shortcut or clicks the control, **Then** the mode toggles and the control shows the new mode.
2. **Given** a take is being recorded in a loop, **When** the player switches the mode, **Then** the switch applies from the next pass on. Notes already recorded are not touched at the moment of switching.
3. **Given** the player chose a mode, **When** they quit and reopen Nota, **Then** the same mode is active.

### Edge Cases

- Loop off: the mode has no effect, and recording is linear as today.
- Session slot recording: it has its own overdub-into-slot behaviour and is out of scope. The mode applies to Arrangement loop recording only.
- Recording into an existing clip that already holds notes inside the loop: Replace only removes notes recorded by earlier passes of the current take. Notes that were in the clip before Record was pressed are kept (the existing record-onto-a-clip rules apply).
- Pickup notes played on the count-in before the loop start belong to the first pass.
- Routed MIDI ("MIDI In" from another track) follows the same mode as the live keyboard.
- Undo after a take recorded in Replace mode undoes the whole take in one step, as today. Replaced passes are not recoverable one by one.
- Switching the mode while the loop is off, or while not recording, just changes the setting.

## Clarifications

### Session 2026-10-16

- Q: What does a new pass replace in Replace mode? → A: The whole earlier passes of this take, removed at the first note of the new pass. A pass with no notes changes nothing.
- Q: Default mode on a new install? → A: Overdub (today's behaviour).
- Q: Where is the mode switched? → A: A toggle in the transport console next to Loop (always visible), plus a menu item and a keyboard shortcut.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Nota MUST offer a loop record mode with two values, **Overdub** and **Replace**, that applies to MIDI recorded into the Arrangement while the transport loop is on.
- **FR-002**: In **Overdub**, every pass MUST add its notes to the take (today's behaviour).
- **FR-003**: In **Replace**, a new pass on which the player plays at least one note MUST remove the notes that earlier passes of the same take recorded inside the loop region, so the take ends with only the latest played pass. The removal happens at the first note of the new pass and covers whole earlier passes, not just the time range played.
- **FR-004**: A pass on which the player plays no notes MUST NOT remove anything.
- **FR-005**: Replace MUST NOT remove notes that were in the clip before the take started, and MUST NOT touch other tracks or clips.
- **FR-006**: The player MUST be able to switch the mode with a single visible control near the transport and with a keyboard shortcut, while stopped, playing or recording. The control is a small Overdub/Replace toggle in the transport console next to Loop, always visible, plus a menu item and a shortcut.
- **FR-007**: A switch made while recording MUST take effect from the next pass and MUST NOT alter notes already recorded.
- **FR-008**: The current mode MUST be visible at a glance, and MUST persist across app restarts as an app preference, like count-in.
- **FR-009**: The default mode for a new install MUST be **Overdub** (today's behaviour).
- **FR-010**: The live preview of held notes (the growing note while a key is down) MUST stay consistent with the mode. In Replace, the replaced notes disappear from the take view when they are removed.
- **FR-011**: The shortcut and control MUST appear in Preferences → Shortcuts and in the app's menu next to Loop and Metronome.
- **FR-012**: The mode MUST be readable and settable by MCP tools that already control the transport, so agents see the same state.

### Key Entities

- **Loop record mode**: An app preference with two values (Overdub, Replace) that controls how successive loop passes of one MIDI take combine.
- **Pass**: One trip of the playhead through the loop region during a take. Notes are attributed to the pass in which they start. A note held across the seam has its continuation attributed to the new pass.
- **Take**: The MIDI recorded between pressing Record and stopping. It is one undo step.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In Replace mode, after recording the same phrase on 5 passes of a 1-bar loop, the take contains exactly the note count of one phrase (0 duplicates), compared with 5× today.
- **SC-002**: In Overdub mode, the recorded result is identical to today's for the same input (no behaviour change for overdub users).
- **SC-003**: Switching the mode takes one action (one click or one key press) from any state, with no dialog.
- **SC-004**: The mode survives an app restart in 100% of cases.
- **SC-005**: No audible glitch or dropout when a pass replaces earlier notes. The audio keeps playing without a gap at the moment of replacement.

## Assumptions

- The issue reporter's "toggle" means a two-state mode, not take lanes or comping (multiple stored takes per pass). Take lanes are out of scope.
- The mode is a global app preference, not stored per project or per track, mirroring count-in.
- Audio recording in a loop (loop punch) already keeps only the latest pass and is unchanged.
- The seam fix on this branch (notes split at the seam, voices not cut) is in place and is relied on.
- Session slot recording keeps its own overdub behaviour.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
