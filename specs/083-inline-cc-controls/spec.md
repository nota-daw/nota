<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Feature Specification: Controls inside MIDI CC lanes

**Feature Branch**: `feat/multiple-midi-cc-lanes` (existing PR #425)
**Created**: 2026-11-04
**Status**: Ready for planning
**Input**: Redesign PR #425 with in-lane controller dropdowns and remove buttons,
a sidebar + lane action, and a 127/64/0 value scale, preserving editing and layouts.

## User Scenarios & Testing

### US1 — Manage controllers where their curves appear (P1)

A musician identifies and changes each lane's controller using its in-lane dropdown,
and hides a lane using the adjacent × control.
**Independent test**: Add three lanes, select different controllers, hide and restore
one, and compare all notes and MIDI curves before and after.

**Acceptance scenarios**:
1. Given multiple lanes, each dropdown displays its controller name and CC number.
2. Changing a dropdown affects only that lane; hiding it preserves its MIDI data.
3. Clicking controls or opening their menus never starts an automation gesture.

### US2 — Add lanes without a full-width toolbar (P1)

A musician uses a compact **+ lane** control below the left sidebar's existing entries.
**Independent test**: Add lanes from each primary lane mode and in a short editor.

**Acceptance scenarios**:
1. The full-width **CC lanes** toolbar and its external plus button are absent.
2. The sidebar action adds an independent lane using the current creation rules.
3. The action, primary tabs and note viewport remain usable when clips are stacked.

### US3 — Read and edit controller values consistently (P1)

A musician uses the left-hand scale to relate curve position to MIDI values.
**Independent test**: Compare graph and pointer mapping at scale endpoints and midpoint,
then resize, zoom, scroll, fold Drum Rack rows and reopen the editor.

**Acceptance scenarios**:
1. Each CC lane shows 127 at the top, 64 near the middle and 0 at the bottom.
2. Graph values and pointer edits align with the scale at every supported lane height.
3. Resizing persists independent heights; layouts work docked and floating, with
   several independent or stacked clips and folded Drum Rack rows.

### Edge cases

- Minimum-height lanes, narrow editors and long controller labels.
- Pitch wheel and channel pressure entries (no invented CC numbers).
- Stale dropdown callbacks after a clip changes or its lane is removed.
- Many extra lanes require scrolling without hiding all notes.
- Scale labels and lane controls must not obscure editable graph points.

## Requirements

### Functional Requirements

- **FR-001**: Each visible controller lane MUST contain a dropdown showing its name
  and CC number where applicable, and an adjacent × hide action.
- **FR-002**: Controller selection and lane removal MUST retain their current routing,
  reserved-controller restrictions, gesture cancellation and MIDI preservation.
- **FR-003**: The left sidebar MUST offer a compact **+ lane** action below its entries;
  the full-width **CC lanes** toolbar MUST be removed.
- **FR-004**: Each CC lane MUST show 127, 64 and 0 aligned with its graph value mapping.
- **FR-005**: Lane controls and the value scale MUST not trigger automation edits.
- **FR-006**: Multiple lanes, independent resizing and saved heights MUST keep working.
- **FR-007**: Notes and curves MUST stay aligned through zoom, scrolling, Drum Rack
  folding, clip changes, stacked clips, and docked or floating editor layouts.
- **FR-008**: The interface MUST follow Nota's themes, typography and localization.
- **FR-009**: The change MUST append commits to PR #425's existing branch and preserve
  all existing commits; it MUST NOT create or replace a pull request.

### Key Entities

- **Controller lane**: Selected controller, independent height and transient gesture.
- **Controller curve**: Existing clip MIDI data, unaffected by lane visibility.
- **Editor layout**: Shared time axis and gutter, primary tabs and scrollable extra lanes.

## Success Criteria

- **SC-001**: All visible CC lanes identify their controller and expose adjacent removal.
- **SC-002**: The editor has one sidebar **+ lane** action and no **CC lanes** toolbar.
- **SC-003**: Endpoint and midpoint edits agree with the value scale after resizing.
- **SC-004**: Regression checks pass for MIDI preservation, saved heights, folded rows,
  stacked clips, zoom/scroll and both editor hosts; Windows build/smoke gates pass.
- **SC-005**: PR #425's head includes the redesign commit as a descendant of its
  original head `957d52865886483dd63f81f1065c70915c3f3667`.

## Assumptions and clarification

- Reference: PR #425's design-review image, section “Proposed — controls inside the lane”
  (`pr-assets/pr425-cc-lanes-review/cc-lanes-mockup.png`). Compact backed controls,
  sidebar creation and gutter scales guide the layout. A shallow control row preserves
  access to every graph point; its unused width uses the graph background.
- Existing pitch wheel/channel pressure semantics and ranges remain intact; CC value
  labels apply to CC controllers, with appropriate signed labels for pitch wheel.
- Hiding the primary controller lane returns its existing panel to Velocity.
- Scope excludes reordering, collapse controls and persistence of visible lane choices.
- Clarification review: behavior, scope, data retention and validation are clear;
  no blocking questions or unresolved markers remain.
