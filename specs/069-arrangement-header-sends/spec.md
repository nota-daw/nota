# Feature Specification: Arrangement header sends, Large only

**Feature Branch**: `cursor/arrangement-header-sends-2734`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "Issue #143, maintainer decision A. Send controls in the arrangement track headers only. Do not make sends undoable or automatable. Show them only at Large track height."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.App`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`)
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md), [`CONTRIBUTING.md`](../../CONTRIBUTING.md),
[`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md).

This narrows what already shipped on `develop` in specs/072 (#441). That change showed send
knobs from 88 px and on every large row, added a **Sends** key, and made every send undoable
and automatable. The maintainer chose the smaller reading of #143 instead.

## User Scenarios & Testing

### User Story 1 - Set a send from a Large header (Priority: P1)

A person working on the timeline sets how much of a track goes to a return without leaving
the Arrangement, once the track is at the Large height.

**Independent Test**: Make a track Large. Drag its send knob. The Mixer and the Sends card
show the same level. Set the track back to Standard. The knobs are gone and the level stays.

**Acceptance Scenarios**:

1. **Given** a project with returns and a track at the Large height (128 px) or taller, **When** the person looks at its header, **Then** they see one send knob per return, in bus order, with the level beside it when the header is wide enough, and the return's name on hover.
2. **Given** that knob, **When** they drag it, type a percent, or double-click it, **Then** the send moves, takes the typed level, or turns off, and the audio follows.
3. **Given** a track shorter than the Large height, **When** its header is shown, **Then** it has no send knobs.
4. **Given** no return tracks, **When** a track is Large, **Then** its header has no send row.
5. **Given** a return row, the master row, or a collapsed group bar, **When** it is shown, **Then** it has no send knobs. A selected group at Large height does.

### User Story 2 - The same level everywhere (Priority: P2)

The header, Mixer, Session mixer and Sends card stay on one level.

**Acceptance Scenarios**:

1. **Given** a Large header, **When** the same send is moved in the Mixer, the Session mixer or the Sends card, **Then** the header knob follows unless it is being dragged.
2. **Given** a send moved in the header, **When** the person looks at the Mixer or the Sends card, **Then** it shows the new level.

### User Story 3 - Sends stay out of undo and automation (Priority: P1)

A send move is immediate and is not a history step, and there is no send envelope.

**Independent Test**: Move a send, then undo. The send stays. The lane target menu has Volume and Pan, and no Send entry. `set_send` does not add an undo step.

**Acceptance Scenarios**:

1. **Given** a send moved from the header, the Mixer, the Session mixer, the Sends card or MCP `set_send`, **When** the person undoes, **Then** the send does not step back. Volume and pan still do.
2. **Given** the automation target menu, **When** it opens on a track, **Then** it does not offer a send. A request to add a send lane does not create one, and a send lane does not move the level.

### Edge Cases

- The header column has no **Sends** key. A project that saved that key hidden still shows the knobs on Large rows.
- At most four returns. Narrow headers show the knobs without the letter and level.
- Adding or removing a return updates the knobs on the next refresh.

## Requirements

- **FR-001**: A track header at the Large height preset or taller MUST show one send knob per return.
- **FR-002**: A shorter header MUST NOT show send knobs. There is no control that hides the Large-row knobs.
- **FR-003**: Each knob MUST set that track's send, from off to full, heard immediately. Double-click turns it off. Option-click types a percent.
- **FR-004**: Knobs MUST follow a send changed in the Mixer, Session mixer or Sends card, and MUST NOT jump while dragged.
- **FR-005**: A send change MUST NOT be an undo step, from any surface, including MCP `set_send`.
- **FR-006**: Sends MUST NOT be an automation target: no menu entry, no new lane, and a send lane MUST NOT move the level.
- **FR-007**: Return rows, the master row and a collapsed group bar MUST NOT show send knobs.

### Key Entities

- **Track send**: how much of one track goes to one return, off to full. Already stored. This feature only shows and edits it on Large headers.
- **Return**: one knob per return, lettered A, B, C, D.

## Success Criteria

- **SC-001**: On a Large track, a person sets a send without opening the Mixer, Session or the Devices panel.
- **SC-002**: Compact and Standard headers have no send knobs, and there is no Sends key above the headers.
- **SC-003**: Undo after a send move leaves the send where it is. No send appears in the automation target list.

## Assumptions

- The maintainer chose header controls only, at the Large preset (128 px) and taller, not a toggle and not the 88 px standard-row rule from specs/072.
- The knobs already in the header stay. This change decides when they appear, and takes sends back out of undo and automation.
- Audio, instrument and a selected group get the knobs. Returns, the master and a collapsed group do not.

## Nota Compliance Checklist

- [x] No new source file
- [x] No new dependency
- [x] Audio-thread impact: send automation no longer writes the send from a lane. The level stays the atomic the audio thread already reads. No new allocation, lock or I/O on the audio thread.
- [x] No new C ABI. The send mix-gesture kind is rejected. The send automation target is rejected.
- [x] Smoke test updated: a send move is not an undo step, and a send lane is not created
- [x] CHANGELOG, FEATURES, user docs
- [x] UI follows DESIGN.md; the existing inline knob stays
