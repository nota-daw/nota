# Feature Specification: Preset name and "modified" state

**Feature Branch**: `feat/preset-state`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Presets: show "(modified)" after a change, and keep the preset name
across view switches and project reload (nota-daw/nota#157)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See whether a device still sounds like its preset (Priority: P1)

A producer loads "Warm Pad" on Volt and turns the filter down. The header now reads
**Warm Pad \*** so they know the sound is their edit, not the preset. They undo the change, or
pick Warm Pad again, and the mark goes away.

**Why this priority**: It is the issue's first ask; without it the preset name misleads.

**Independent Test**: Apply a factory preset to a built-in instrument, turn one knob, see the
mark; undo, see it go.

**Acceptance Scenarios**:

1. **Given** a device loaded from a preset, **When** any sound parameter differs from the
   values the preset set, **Then** its header shows the preset name followed by a mark
   (" \*") and the picker's tooltip says "Changed since you loaded <name>".
2. **Given** a modified device, **When** the user undoes back to the preset's values, sets the
   parameter back by hand, or re-selects the preset, **Then** the mark goes.
3. **Given** a device, **When** only a view setting changes (card size S / L, EQ-8's analyzer
   mode, range and peak), **Then** it is not marked modified.
4. **Given** a device at Init (no preset applied), **Then** the header shows "Init" without a
   mark, as today.
5. **Given** the user applies a preset and then undoes that step, **Then** the header shows the
   preset it had before (and its own modified state), not the undone one.

---

### User Story 2 - The name stays with the device (Priority: P1)

The preset name and the modified state follow the device: switching to another track and back,
copy and paste, duplicating the device or its track, moving it to another track, saving,
closing and reopening the project.

**Why this priority**: The issue reports the name being lost or wrong over time.

**Independent Test**: Apply a preset, tweak it, save and reopen: "Warm Pad \*". Duplicate the
track: the copy says the same.

**Acceptance Scenarios**:

1. **Given** a device with a preset, **When** the user switches tracks or views and comes back,
   **Then** the header shows the same name and mark.
2. **Given** a device with a preset, **When** it is copied and pasted, duplicated, its track
   duplicated, or it is moved to another track, **Then** the new device carries the same name and
   the same "loaded" values (so the same mark).
3. **Given** a project saved with devices loaded from presets, **When** it is reopened, **Then**
   each device shows its preset name and its modified state. A project saved before this change
   opens with the names the app can still work out (a Drum Rack kit, a Mosaic program) and Init
   otherwise.

---

### User Story 3 - Save a modified preset under its old name (Priority: P2)

From a modified device, **Save preset…** starts with the preset's name, so the user can keep it
or add to it ("Warm Pad 2").

**Acceptance Scenarios**:

1. **Given** a device loaded from "Warm Pad", **When** the user picks Save preset…, **Then** the
   name field starts as "Warm Pad".
2. **Given** a device saved as a user preset, **Then** the header shows that preset's name
   without a mark.

---

### User Story 4 - Rack chains (Priority: P2)

A chain instrument or chain effect in an Instrument Rack, Drum Rack pad or Audio Effect Rack
keeps its preset name and mark in the rack card and its pop-out window, saved with the project.

### Edge Cases

- **A preset that leaves a parameter alone** (the Sampler's trim and loop points): the loaded
  values are what the device held right after the preset applied, so those params count from
  there.
- **Automation or CV moving a parameter**: the live value moves, so the mark can come and go
  during playback. The comparison reads the parameter's base (the value the user set), not the
  modulated one, where the engine keeps both; automation lanes that overwrite a parameter do
  count as a change.
- **Plug-ins**: a plug-in's user preset keeps its name; the mark compares the plug-in's exposed
  parameters with the values after loading.
- **A deleted user preset**: the name still shows; picking another preset replaces it.
- **Small float differences**: a parameter counts as changed when it differs by more than
  1/10000 of its range.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each device (audio effect, MIDI effect, instrument, rack chain device) MUST carry a
  preset tag: the preset's id and display name and the parameter values right after it applied.
- **FR-002**: Applying a preset MUST set the tag in the same undo step as the parameter change;
  undo and redo restore the tag.
- **FR-003**: The device header MUST show "<name> \*" when any sound parameter differs from the
  tag's values by more than 1/10000 of its range; view parameters are left out.
- **FR-004**: Copy / paste, duplicate (device and track), move to track and rack grouping MUST
  keep the tag.
- **FR-005**: The tag MUST be saved per device in the project (optional fields, no format bump);
  older projects open without tags.
- **FR-006**: Save preset… MUST start with the tag's name; saving sets the tag to the new
  preset.
- **FR-007**: The MCP device readers MUST report the preset name and whether it is modified.

### Key Entities

- **Preset tag**: preset id, display name, loaded values (one per parameter of the device).

## Success Criteria *(mandatory)*

- **SC-001**: After applying a preset, changing one parameter shows the mark on the next UI
  frame; setting it back clears it.
- **SC-002**: A project with 50 tagged devices saves, reopens and shows the same names and marks.
- **SC-003**: Checking the mark for every visible device costs under 0.2 ms per UI frame.

## Assumptions

- The mark is " \*" after the name (fits the compact header); the tooltip spells it out.
- Init is not a preset: applying Init clears the tag.
- #57 (getting back to Init) is unchanged.
