# Feature Specification: Bypass one Session clip's follow actions

**Feature Branch**: `094-follow-bypass`

**Created**: 2026-11-11

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#499: "During a live set, I want to keep one clip playing
for longer while other clips' Follow Actions remain active, then restore that clip's prepared
behavior without rebuilding its A/B choices, probabilities or timing. […] While bypassed, keep
its A/B actions, weights, Jump target and timing intact and show clearly that they are
temporarily inactive. Other clips and the global Follow setting should be unaffected.
Save/reopen should preserve the configuration and its bypass state. […] Please define the next
eligible musical boundary; an action whose deadline passed while bypassed should not fire
unexpectedly as a catch-up event. Ordinary clip playback and manual launching should remain
available."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — the Session follow check
- [x] Managed app (C#, `src/managed/*`) — project format, MCP
- [x] UI (Avalonia) — the Session clip inspector, follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: a Session clip has follow actions A and B with relative
chances, a follow time (0 = one pass) and a Jump target (`SessionSlot`, `NotaSessionClipProps`).
Each block, `sessionPreApply` checks the playing slot: with the global Follow switch on and an
action set, once `playedBeats` (beats since the launch) reaches the follow time it picks A or B
and queues the target. An action of None restarts the count, so the clip plays another follow
time. The only way to hold one clip today is to set both actions to None, which loses them.

## User Scenarios & Testing

### User Story 1 - Hold one clip on its loop (Priority: P1)

A four-bar clip moves on after two passes. During a solo, the performer bypasses its follow
actions in the clip inspector; the clip keeps looping while other clips still follow theirs.
Afterwards they turn Bypass off and the clip moves on as prepared.

**Independent Test**: two clips on two tracks, each with Next after one beat; bypass the first;
launch the scene and play past the follow time: the first still plays its slot, the second has
moved on. The first's A, chance and time read back unchanged.

**Acceptance Scenarios**:

1. **Given** a clip with follow actions, **When** Bypass is turned on, **Then** its A/B, chances,
   Jump target and time stay as set, and neither action fires.
2. **Given** a bypassed clip, **Then** the inspector shows **Bypassed** in the Follow action
   head and dims the A/B, Jump and After rows; Bypass stays bright and clickable.
3. **Given** a bypassed clip, **Then** other clips' follow actions run and the global Follow
   switch is unchanged.
4. **Given** a bypassed clip, **When** another slot on its track is launched by hand, **Then**
   it launches as usual.

### User Story 2 - No catch-up when Bypass goes off (Priority: P1)

The clip has played past two follow times while bypassed. Turning Bypass off half-way through
the third doesn't fire at once: the action fires where the third time ends.

**Acceptance Scenarios**:

1. **Given** a follow time of one beat and Bypass on for 2.5 beats, **When** Bypass is turned
   off, **Then** nothing fires before beat 3, and the action fires at beat 3.

### User Story 3 - Saved with the project (Priority: P2)

**Acceptance Scenarios**:

1. **Given** a bypassed clip, **When** the project is saved and reopened, **Then** the clip is
   still bypassed with its follow actions intact.
2. **Given** a project saved before this change (format 32), **When** it opens, **Then** no clip
   is bypassed.

### Edge Cases

- **Global Follow off**: the whole section is dimmed and reads **Off globally**, as before;
  Bypass can still be set, and takes effect when Follow comes back on.
- **Both actions None**: nothing fires either way; Bypass changes nothing audible.
- **Copy, duplicate, capture, scene insert and move**: the bypass travels with the slot, like
  its other properties.
- **A follow time changed while bypassed**: the count continues against the new time from the
  last period boundary.

## Requirements

### Functional Requirements

- **FR-001**: A Session clip MUST have a follow bypass, off by default, independent of its
  follow actions, chances, Jump target and time.
- **FR-002**: While bypassed, neither follow action MUST fire; every follow time that ends
  starts the next one from there, as an action of None does.
- **FR-003**: After the bypass is turned off, the next action MUST fire at the end of the
  follow time then running (counted in whole follow times from the launch), never earlier.
- **FR-004**: The clip inspector MUST offer the bypass in the Follow action section and show
  clearly that the actions are inactive.
- **FR-005**: The bypass MUST be saved with the project; older projects open with it off.
- **FR-006**: The MCP session tools MUST read and set it.

## Success Criteria

- **SC-001**: Holding a clip for a solo and giving it back its prepared follow behaviour takes
  two clicks, with no settings to rebuild.

## Assumptions

- The issue asks for a per-clip bypass; the global Follow switch and scene follow are left as
  they are.
- The boundary after the bypass is the follow-time grid of the clip's launch, the same grid an
  action of None keeps, so a performer can predict it from the clip's own follow time.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: one bool per slot and one double per Session player, read and
  written by the audio thread's follow check; no allocation, lock or I/O
- [x] ABI: `NotaSessionClipProps` gains `follow_bypass` at the end; the managed struct matches
- [x] Project format 33, with a no-op migration from 32
- [x] Smoke-test impact: `FollowBypassTests` (`-- --follow-bypass`)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (inspector row and chip, `Inactive` dimming, texts
  through `Loc`)
