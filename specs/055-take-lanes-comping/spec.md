# Feature Specification: Take lanes and comping (audio loop recording)

**Feature Branch**: `cursor/take-lanes-comping-3af4`

**Created**: 2026-10-31

**Status**: Clarified

**Input**: User description: "Takes to Layers / Comping (nota-daw/nota#187). Today Nota replaces
existing clips when recording over them. Record multiple takes without overwriting the previous
ones, each take stored on its own layer, and comp the best sections from different takes into one
performance, rather than having to choose a single take or risk losing a great moment from an
earlier recording."

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

### User Story 1 - Every loop pass is kept as a take (Priority: P1)

A singer loops four bars on an audio track and sings the verse five times in a row. Today only
the last pass survives. With take lanes, when they stop, each pass is kept as its own take. The
track plays the latest pass, exactly as before, and the earlier passes are there to go back to.

**Why this priority**: This is the core of the request. Without it a great earlier pass is lost
the moment the loop comes round again, and nothing else in the feature has material to work on.

**Independent Test**: Loop 1 bar on an armed audio track, record three full passes with a
different signal on each, and stop. The track holds three takes, and playback sounds like the
third pass.

**Acceptance Scenarios**:

1. **Given** an armed audio track and the transport loop on, **When** the player records three full passes and stops, **Then** the track holds three takes, numbered in recording order, and the track plays the third pass over the loop.
2. **Given** a recording that started mid-loop and stopped mid-loop, **When** it stops, **Then** the first and last takes hold only the part of the loop they actually recorded, and the track plays, at every point of the loop, the most recent pass that covered it (the same audio as today).
3. **Given** a loop recording that never completed a pass (stopped before the loop came round), **When** it stops, **Then** it makes one ordinary clip and no takes, as today.
4. **Given** loop recording with the loop off (linear recording), **When** it stops, **Then** behaviour is unchanged: one clip, overwriting what it lands on, no takes.

---

### User Story 2 - See the takes in lanes under the track (Priority: P1)

The takes show as lanes under the track, one lane per take, with its waveform. The track's own
row stays on top and is the comp: what actually plays. The lanes are folded away by default and
open with a disclosure control on the track header. On each take lane, the sections the comp
uses are highlighted so it's clear where each part of the performance comes from.

**Why this priority**: Comping is impossible without seeing the takes.

**Independent Test**: After recording three passes, the track header shows a closed takes
disclosure. Opening it shows three lanes, Take 1 to Take 3, under the track; the third is
highlighted over the whole loop.

**Acceptance Scenarios**:

1. **Given** a track with takes, **When** the arrangement is shown, **Then** the take lanes are folded and the track header shows a takes disclosure with the take count.
2. **Given** folded take lanes, **When** the player opens the disclosure, **Then** one lane per take appears under the track's row, named "Take N", each showing its waveform over the range it recorded.
3. **Given** open take lanes, **When** a section of the comp comes from a take, **Then** that section of that take's lane is highlighted; sections not in use are dimmed.
4. **Given** a track without takes, **Then** it shows no disclosure and no lanes.

---

### User Story 3 - Comp by swiping a range on a take lane (Priority: P1)

The player drags across a section of a take lane. That section of the comp is replaced by the
same section of that take, so playback switches to it there and only there. Doing it again on
other lanes builds the final performance out of the best parts.

**Why this priority**: This is the comping the issue asks for.

**Independent Test**: With three takes, swipe beats 1–2 on Take 1. Playback plays Take 1 over
beats 1–2 and Take 3 elsewhere. Undo puts Take 3 back over beats 1–2.

**Acceptance Scenarios**:

1. **Given** open take lanes, **When** the player drags across a range on a take lane, **Then** the comp plays that take over the range (clamped to what the take recorded), and the rest of the comp is unchanged.
2. **Given** a swipe, **When** the player drags, **Then** the range follows the arrangement's snap grid (a modifier bypasses snapping, as for other arrangement drags) and shows a live preview on the lane.
3. **Given** a comp edit, **When** the player presses Undo, **Then** exactly that comp edit is undone; Redo puts it back.
4. **Given** a click on a take lane without a drag, **Then** the comp is not changed.

---

### User Story 4 - Delete takes and flatten (Priority: P2)

Once the comp is right the player either deletes takes they don't want, or flattens the takes:
the comp becomes one ordinary clip and the takes go away.

**Why this priority**: Without it, takes pile up in the project and in the arrangement forever.

**Independent Test**: Delete Take 2: it disappears and playback is unchanged. Flatten: the take
lanes disappear, the comp region holds one clip that sounds identical to the comp before.

**Acceptance Scenarios**:

1. **Given** a track with takes, **When** the player deletes a take, **Then** its lane goes away and what plays is unchanged (sections of the comp that came from it keep playing). One undo step.
2. **Given** a track with takes and a comp built from several of them, **When** the player chooses Flatten takes, **Then** the comp over the takes' range becomes one clip with the same audio, and every take of that recording is removed. One undo step.
3. **Given** a flatten or delete, **When** the player presses Undo, **Then** the takes and the comp are back exactly as before.

---

### User Story 5 - Takes survive save and reopen (Priority: P1)

Every take is saved with the project. Reopening the project shows the same takes, the same comp,
and the same highlighted sections.

**Why this priority**: Takes the user can't keep are worse than no takes.

**Independent Test**: Record three passes, comp two sections, save, reopen. Three takes, same
comp, same audio.

**Acceptance Scenarios**:

1. **Given** a project with takes and a comp, **When** it is saved and reopened, **Then** the takes (audio, range, number) and the comp (which sections come from which take) are restored.
2. **Given** a project saved by an older Nota, **When** it is opened, **Then** it opens as before with no takes.

### Edge Cases

- Recording again over the same loop adds new takes after the existing ones (numbering carries on) and the comp switches to the newest pass, as recording does today.
- A take lane is shown only over the range its take recorded; swiping outside it does nothing.
- Comp sections are ordinary clips: they can be moved, trimmed, split, faded or deleted like any clip. A section that was edited away from where its take sits stops counting as that take's for flatten (it stays as a clip of its own) — and keeps playing.
- Flatten only merges comp sections that still sit where their take recorded them, unwarped, unreversed and unpitched, with no clip envelope; any other section is kept as a separate clip.
- Deleting the last take of a track removes the disclosure.
- Changing the loop length mid-take, or turning the loop on mid-take, follows today's loop punch rules; passes are counted from where loop punch starts.
- Recording into a Session slot is unchanged (no takes).
- MIDI recording is unchanged (Overdub / Replace, specs/018).

## Clarifications

### Session 2026-10-31

- Q: Audio only, or audio and MIDI? → A: Audio only. MIDI keeps Overdub / Replace (specs/018).
- Q: What creates a new take? → A: Each loop pass. Linear recording (loop off) is unchanged.
- Q: How is it switched on? → A: Always on — no mode, no preference.
- Q: Lane UI? → A: Expandable lanes under the track, one row per take, with the comp (what plays) on top as the track's own row; collapsed by default.
- Q: How do you comp? → A: Swipe a range on a take lane to use that section.
- Q: Storage and undo? → A: All takes are saved in the project; takes can be deleted or flattened into one clip; one undo step is a single comp edit.
- Q: Session view? → A: Out of scope; slot recording is unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An Arrangement audio loop recording that completes at least one pass MUST keep every pass as a separate take on the track, in recording order, instead of overwriting earlier passes.
- **FR-002**: A pass that started or stopped mid-loop MUST keep only the range it recorded.
- **FR-003**: After recording, the comp (what plays) MUST be, at every point of the loop, the most recent pass that recorded there — the same audio today's loop recording leaves.
- **FR-004**: Recordings that complete no pass, linear recordings (loop off), Session slot recordings and MIDI recordings MUST behave exactly as today and create no takes.
- **FR-005**: A track with takes MUST show a takes disclosure on its header with the take count; the take lanes MUST be folded by default and open/close from it.
- **FR-006**: Each open take lane MUST be named "Take N" (N in recording order, continuing across recordings on the track), show its waveform over its recorded range, and highlight the sections the comp currently uses.
- **FR-007**: Dragging across a range on a take lane MUST make the comp play that take over the range (clamped to the take's recorded range), leaving the rest of the comp untouched. The range MUST follow the arrangement's snap grid, with the usual modifier to bypass it.
- **FR-008**: Each comp edit MUST be one undo step.
- **FR-009**: The player MUST be able to delete a take (one undo step, what plays is unchanged) and to flatten a track's takes (one undo step): the comp over each recording's range becomes one clip with the same audio and that recording's takes are removed.
- **FR-010**: Takes and which comp sections come from which take MUST be saved with the project and restored on open; projects without takes MUST open unchanged.
- **FR-011**: Recording, comping, deleting and flattening MUST NOT cause an audible glitch: no new work on the audio thread beyond playing ordinary clips.

### Key Entities

- **Take**: One loop pass of one audio recording on one track: its audio, the timeline range it recorded, its number ("Take N"), and the recording it belongs to.
- **Recording (take group)**: The takes of one loop recording; they share the loop range. Flatten works per recording.
- **Comp**: What the track plays — its ordinary clips. A comp section is a clip that came from a take and remembers which take.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After recording N full passes of a loop, the track holds N takes and 0 passes are lost (today: N − 1 lost).
- **SC-002**: Immediately after recording, the comp plays audio identical, sample for sample, to what today's loop recording leaves.
- **SC-003**: Using a section of another take takes one drag; undoing it takes one Undo.
- **SC-004**: Flattening produces audio identical, sample for sample, to the comp before flattening.
- **SC-005**: Saving and reopening restores 100% of the takes and the comp.
- **SC-006**: No dropout or audio-thread allocation is introduced; playback of a comp costs the same as playing the same number of ordinary clips.

## Assumptions

- No automatic crossfades at comp section boundaries in this version. Automatic crossfades at clip splits are tracked separately (#286) and can apply to comp boundaries when they land.
- Takes are recorded and stored at the same alignment (each take covers the loop region of its recording), so a comp section's position inside its take is its position in the loop.
- The take lanes' open/closed state is view state, not saved with the project (like the clip editor height); lanes start folded.
- Comp sections are ordinary clips, so every existing clip edit works on them; the take link only drives the highlight and flatten.
- MCP / Nota Remote control of takes is out of scope for this version.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
