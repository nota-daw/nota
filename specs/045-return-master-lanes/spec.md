# Feature Specification: Automation and resizable height for return and master rows

**Feature Branch**: `045-return-master-lanes`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Return tracks and the Master track: (1) Return tracks currently cannot have automation — add automation lanes for return tracks (like regular tracks). (2) Return and Master tracks cannot change their track height — allow resizing their height, including the context-menu Compact / Standard / Large height modes that regular tracks have. (3) By default, Return and Master tracks should be in Compact mode."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Today the return and master rows live in the arrangement's pinned footer
(`FooterLaneControl` in `src/managed/Nota.App/ArrangementView.Controls.cs`) at a fixed
40 px (`FooterRowH`). The master row edits only master-volume automation; return rows
show no automation at all. Track rows already have per-track heights with
Compact 30 / Standard 64 / Large 128 presets and size-adaptive headers (specs/015,
`ArrangementView.Sizing.cs`), and full automation lanes (`ArrangementView.Automation.cs`).
In the engine a return is an ordinary track of type Return and the block-rate automation
pass already walks every track, so return automation is expected to need no engine
change — the plan confirms this and adds a smoke check.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md), [`specs/015-track-list-sizing`](../015-track-list-sizing/spec.md).

## Clarifications

### Session 2026-10-28

- Q: When the footer grows too tall, cap it and scroll, or just stop growing? → A: Cap at about half the arrangement height and scroll the footer internally.
- Q: Do the "all tracks" height gestures also resize return and master rows? → A: No — regular tracks only; returns and master are resized on their own.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automate a return track (Priority: P1)

A producer sends several tracks to a reverb return and wants the reverb to swell in the
chorus and duck in the verse. In automation mode they pick a parameter on the return row
— its volume, its pan, or a parameter of a device on the return's chain (e.g. the
reverb's decay) — draw points into the return's lane, and on playback the return follows
them exactly as a regular track's automation would.

**Why this priority**: This is a missing capability, not a layout preference — today the
only workaround is automating every send on every source track.

**Independent Test**: Add a return with a device, enter automation mode, draw a volume
ramp and a device-parameter ramp on the return, play, and confirm the return's level and
the device parameter follow the drawn curves; save, reopen, and confirm the lanes are
still there.

**Acceptance Scenarios**:

1. **Given** a project with a return track, **When** the user enters automation mode, **Then** the return's row offers the same automation-target choice as a regular track: volume, pan, and the parameters of the devices on its chain.
2. **Given** a chosen return target, **When** the user clicks, drags and removes points in the return's row, **Then** the points behave as on a regular track (add, move, snap, remove with right-click / double-click, curve editing where tracks have it).
3. **Given** return automation exists, **When** the song plays, **Then** the return's volume / pan / device parameter follow the curve, and the return's controls (header, mixer, device card) move with it.
4. **Given** return automation exists, **When** the project is saved and reopened, **Then** every return lane and point is restored.
5. **Given** return automation exists, **When** the user undoes / redoes an automation edit, **Then** it undoes / redoes as on a regular track.
6. **Given** the master row, **When** in automation mode, **Then** master-volume automation keeps working as today.

---

### User Story 2 - Resize return and master rows (Priority: P1)

A user mixing with three returns wants the reverb return tall to see its automation
clearly and the others short. They drag the bottom edge of a return or the master row,
or right-click it and pick Compact / Standard / Large under "Track height", exactly as on
a regular track.

**Why this priority**: Return automation (US1) is hard to edit in a fixed 40 px row; the
footer also takes space the user may want for tracks.

**Independent Test**: Resize one return and the master by edge drag and by each context
menu preset, confirm only that row changes, its header adapts like a track header, and
the sizes survive save/reopen.

**Acceptance Scenarios**:

1. **Given** a return or master row, **When** the user drags its bottom edge, **Then** that row's height follows the pointer within the track height limits, snapping to the presets as track rows do, and the rest of the footer moves accordingly.
2. **Given** a return or master row, **When** the user right-clicks its header, **Then** the context menu offers "Track height" with Compact / Standard / Large, the current one marked, and picking one sets that row's height.
3. **Given** several returns are selected, **When** the user picks a preset or drags one of their edges, **Then** every selected return takes the same height.
4. **Given** a resized return or master, **When** the user double-clicks its bottom edge, **Then** the row returns to its default height (Compact).
5. **Given** a return or master row at any height, **Then** its header uses the same compact / standard / large layout rules as a track header, with no control clipped or overlapping.
6. **Given** resized returns and master, **When** the project is saved and reopened, **Then** they have the heights they had when saved.

---

### User Story 3 - Compact by default (Priority: P2)

A user opens a new project or adds a return: the return and master rows start compact,
so the footer takes as little room as possible and the tracks get the space.

**Why this priority**: A small default that only makes sense once rows are resizable.

**Independent Test**: Create a new project and add a return; confirm the master and the
return are at the Compact height. Open an old project saved before this feature; confirm
its returns and master are Compact too.

**Acceptance Scenarios**:

1. **Given** a new project, **Then** the master row is at the Compact height.
2. **Given** any project, **When** the user adds a return, **Then** the new return row is at the Compact height.
3. **Given** a project saved before this feature (no stored return/master heights), **When** it opens, **Then** returns and master are at the Compact height.
4. **Given** a regular track, **Then** its default stays Standard (unchanged).

---

### Edge Cases

- **Footer taller than the view**: the footer is pinned, so tall returns could crowd out the tracks. The footer is capped at about half the arrangement height; past the cap it scrolls vertically on its own, and its rows keep their heights (see FR-015).
- **"All tracks" resize** (Alt-drag on a track edge, the "all tracks" preset) resizes regular tracks only; return and master rows keep their own heights and are resized only through their own edges and context menu (see FR-016).
- **Return removed**: its stored height and automation go with it; the remaining returns keep theirs.
- **Device on a return removed or moved**: return lanes on that device follow the same fix-up rules as track lanes (removed with the device, re-pointed when moved).
- **Return lanes during automation write / record**: if regular tracks support writing automation from moving controls, returns do too; if a mode is track-only today, it stays out of scope here and is noted in the plan.
- **Compact master with automation**: master-volume points remain editable at 30 px (value axis compressed, hit targets unchanged).
- **Resizing is a view change**: not in undo history, does not change audio (consistent with specs/015); it may mark the project unsaved.
- **Older Nota builds** opening a project saved with return/master heights or return lanes ignore the extra data without error (return lanes are already engine track data, so an older build may play them).
- **Session view, mixer and Nota Remote** are not changed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: In automation mode, each return row MUST offer the automation-target choice regular tracks offer — volume, pan, and the parameters of devices (built-in and hosted plugins) on its chain.
- **FR-002**: Return automation lanes MUST support the same point editing as regular track lanes (add, move, snap, remove, curves, copy/paste of selected points where tracks support it).
- **FR-003**: During playback, return automation MUST drive the return's volume, pan and device parameters, and the related controls MUST reflect the automated value.
- **FR-004**: Return automation MUST be saved with the project, restored on open, and its edits MUST be undoable like track automation edits.
- **FR-005**: Master-volume automation MUST keep working unchanged at every master row height.
- **FR-006**: Users MUST be able to resize each return row and the master row by dragging its bottom edge, within the track height limits (30–256 px) and with the same preset snapping as track rows.
- **FR-007**: The context menu of a return or master row MUST offer "Track height" → Compact / Standard / Large (30 / 64 / 128 px), marking the current one, applying to the selected returns when several are selected.
- **FR-008**: Double-clicking a return's or the master's bottom edge MUST reset it to Compact.
- **FR-009**: Return and master headers MUST pick their layout from their height with the same compact / standard / large rules as track headers; no control may be clipped or overlap at any height or track-list width within the limits.
- **FR-010**: The footer lanes (automation, grid, loop wash, playhead, browser drop highlight) MUST draw at each row's own height.
- **FR-011**: Return and master heights MUST be saved with the project and restored on open.
- **FR-012**: New projects, newly added returns, and projects without stored return/master heights MUST show returns and master at the Compact height (30 px). Regular tracks keep Standard as their default.
- **FR-013**: Existing footer interactions (selecting a return/master, its context menu, browser drag-and-drop onto a return or master, the track-list width seam) MUST keep working; the resize zone is limited to a thin band at the row seam.
- **FR-014**: The new handles and layouts MUST follow `DESIGN.md` (Studio): neutral seams, no new accent use.
- **FR-015**: The pinned footer MUST NOT grow beyond about half the arrangement's visible height; when its rows add up to more, the footer MUST scroll vertically (headers and lanes together), leaving the track list its space.
- **FR-016**: The "all tracks" height gestures (Alt-drag on a track edge, the "all tracks" preset) MUST resize regular tracks only and leave return and master heights unchanged.

### Key Entities

- **Return / master row height**: per return and one for the master; Compact when absent. Stored with the project like track heights.
- **Return automation lane**: the same kind of lane a regular track has (target + points), owned by the return.
- **Master volume automation**: unchanged graph-level lane edited in the master row.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can draw a return volume ramp and a return device-parameter ramp and hear both on playback, with no per-send workaround.
- **SC-002**: Every return and the master can be set to each of the three presets with one context-menu pick, and to any height in the limits with one drag.
- **SC-003**: With default settings, a project with two returns uses less footer height than today (2 × 30 + 30 = 90 px instead of 120 px).
- **SC-004**: Return lanes and return/master heights survive save/reopen in 100% of cases; projects from previous versions open with returns and master at Compact and no data loss.
- **SC-005**: At every height and track-list width within the limits, no return or master header control is clipped or overlaps another (verified by screenshots at Compact, Standard, Large).

## Assumptions

- The engine already stores and applies automation lanes on return tracks (a return is a track of type Return and the automation pass visits all tracks); the work is the managed UI plus a smoke check. If the plan finds an engine gap, it is fixed within this feature.
- The master row keeps only master-volume automation; master device-parameter or pan automation is out of scope.
- "Compact by default" applies to returns and master only; regular track defaults are unchanged.
- The return and master rows stay in the pinned footer (they do not move into the scrolling track list).
- The new mouse gestures on footer rows are the same as on track rows, so Preferences → Shortcuts only needs a wording check (nota-shortcuts skill).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — reuses the existing automation path; no new audio-thread work expected
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — existing automation ABI is reused
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (add a return-automation check)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
