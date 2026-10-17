# Feature Specification: An On/Off Switch for Instruments

**Feature Branch**: `fix/issue-148-lksrgg`

**Created**: 2026-10-17

**Status**: Implemented

## Clarifications

### Session 2026-10-17

- Q: What does "off" do to notes that are sounding at the moment the switch is flipped? →
  A: They fade out over one audio block (a few milliseconds, no click) and are released.
  Turning the instrument back on does not bring them back; it plays from the next note.
- Q: Where does the switch live in the engine API? → A: On the existing device bypass
  call with device index −1, which already means "the instrument" for names, parameters
  and state. No new C ABI function.
- Q: Does a replaced instrument (a different synth dropped on the track) inherit the off
  state? → A: No. A new instrument is a new card and starts on, like a newly added effect.
  Duplicating the track keeps it.
- Q: Is the switch an undo step? → A: No, as for the effects' switches (bypass is not in
  the undo history today; the engine's undo comment lists it as not covered).

**Input**: nota-daw/nota#148 — "Devices panel: an on/off switch for instruments. Quickly
silence an instrument (e.g. to compare layers or hear the track without it) without muting
the whole track or deleting it. Instrument cards get the same on/off switch as effects.
Off = the instrument makes no sound (and ideally uses no CPU); the MIDI effects and audio
effects after it keep their settings. Instrument shells are `Bypassable: false`
(`DeviceChainView.Instruments.cs`); the engine needs an instrument bypass."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply
- [x] Managed app (C#, `src/managed/*`)
- [x] UI (Avalonia) — follow `DESIGN.md`
- [ ] Plugin hosting (`pluginhost/`)
- [ ] Build / packaging / CI
- [ ] Docs only

**Current state**: every audio effect and MIDI effect card has a power switch in its
header (`SwitchTrack`); instrument cards reserve the same slot but leave it empty
(`Bypassable: false`). The engine's `Instrument` has no bypass flag, so the only way to
silence an instrument is to mute the whole track (which also silences returns fed from it
and the effects' tails) or to delete the instrument.

## User Scenarios & Testing

### User Story 1 - Silence an instrument from its card (Priority: P1)

A producer layering two synths on separate tracks clicks the switch on one synth's card:
it goes quiet at once, without a click, while the other keeps playing. The card takes the
inactive look but its knobs stay editable. Clicking again brings it back on the next note.

**Independent Test**: play a held note or a clip, switch the instrument off and on.

**Acceptance Scenarios**:

1. **Given** any instrument card (built-in synth or sampler, Instrument Rack, Drum Rack,
   hosted plug-in), **Then** its header shows the same on/off switch as an effect card.
2. **Given** a sounding instrument, **When** it is switched off, **Then** it fades to
   silence within one block and then outputs exact silence while clips and live notes go on.
3. **Given** an instrument that is off, **When** it is switched on, **Then** the next note
   sounds; notes held across the switch do not resume.
4. **Given** an instrument that is off, **Then** the engine does not render it (no CPU).

### User Story 2 - The rest of the chain is untouched (Priority: P1)

**Acceptance Scenarios**:

1. **Given** audio effects after the instrument, **When** it is switched off, **Then**
   they keep running (a reverb's tail rings out) and keep their settings and switches.
2. **Given** MIDI effects before it, **Then** they keep their settings and switches.

### User Story 3 - It stays the way you left it (Priority: P2)

**Acceptance Scenarios**:

1. **Given** an instrument that is off, **When** the project is saved and reopened,
   **Then** it is still off. A project saved before this feature loads with every
   instrument on.
2. **Given** a track whose instrument is off, **When** it is duplicated, **Then** the copy's
   instrument is off too.
3. **Given** the AI assistant (MCP), **Then** `set_device_bypass` with device index −1
   turns the instrument on or off and `get_overview` reports it.

### Edge Cases

- **Frozen track**: the freeze already baked the instrument; the switch affects the live
  render only, as for an effect's switch.
- **Session clips**: an instrument that is off is silent for Session slots too.
- **MIDI routed to another track** (MIDI In): the source track's MIDI is computed before
  its instrument, so a destination still receives it.
- **Recording**: notes are still recorded; only the sound is off.

## Requirements

- **FR-001**: Every instrument card in the Devices panel MUST show the on/off switch in the
  header's power slot, with the same look and the inactive body when off.
- **FR-002**: Off MUST fade the instrument out over one block, release its voices and held
  controllers, and then skip rendering it entirely until it is switched on.
- **FR-003**: The MIDI effects and audio effects of the track MUST keep running and keep
  their own state and switches.
- **FR-004**: The state MUST be saved with the project (optional `bypassed` member of the
  track's `instrument`, no format bump) and kept on track duplication.
- **FR-005**: The engine API is the device bypass call with device index −1; MCP reaches it
  through `set_device_bypass` and reports it in `get_overview`.
- **FR-006**: The switch is set from the message thread and read on the audio thread
  without locks or allocation.

## Success Criteria

- **SC-001**: After switching off, the instrument's output is exactly zero from the second
  block on (smoke test).
- **SC-002**: Save / load, duplicate and an older project behave as in US3 (smoke test).
