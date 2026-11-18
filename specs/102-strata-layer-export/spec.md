# Feature Specification: Export one Strata layer on its own

**Feature Branch**: `feat/strata-layer-export`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "Issue #482 (approved): Export an individual retained Strata overdub layer as its own audio file without flattening the loop. From Strata's layer list, export one retained layer to a WAV with a clear time origin aligned to the loop start; state precisely what is exported (captured input / after input gain / rendered playback with speed, reverse, feedback); export must not mute, clear or rewrite the source layer; works after save+reopen; if the layer is unavailable show it clearly rather than exporting the mixed loop."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

Issue: [nota-daw/nota#482](https://github.com/nota-daw/nota/issues/482) (approved,
milestone 0.61.1). Related but out of scope: #430 (MCP export of tracks and Session
clips), #171 (project interchange).

## Clarifications

### Session 2026-11-18

- Q: What audio does a layer export contain — the captured input, the input after
  Input gain, or the layer as Strata plays it (speed, reverse, feedback, layer level)?
  → A: The layer as it was recorded: the input after Strata's Input gain, exactly the
  samples Strata keeps for that layer. Speed, Reverse, Feedback, the layer's level and
  its mute are playback settings and are not applied. This is the material for editing
  elsewhere, and it is the only version that lines up sample for sample with the loop
  at the loop's own tempo. The existing loop Export already covers "as you hear it".
- Q: Where does the user find the per-layer export? → A: In the layer list itself: a
  right-click menu on a layer row, with "Export layer N as WAV…". The rail's Export
  button keeps exporting the whole loop as heard.
- Q: What is the file's time origin and length? → A: Sample 0 is the loop's top
  (bar 1, beat 1 of the loop); the file is exactly one loop cycle long at the loop's
  sample rate. A pass that started mid-loop, or was stopped before a full cycle, keeps
  its position in the cycle, with silence where nothing was recorded.
- Q: Can a muted layer be exported? → A: Yes. Mute affects playback only; the export
  of a muted layer contains its recorded audio, and the export does not change mute.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Export the vocal overdub on its own (Priority: P1)

A performer builds a live loop on a Strata: a four-bar cello pass on layer 1, then a
vocal overdub on layer 2. After the set they want the vocal pass as its own file to
edit separately. They right-click the vocal layer in Strata's layer list, choose
"Export layer 2 as WAV…", pick a file name, and get a WAV that contains only the vocal,
starts at the loop's top and is exactly one loop long. The loop in Strata is untouched:
both layers still play, with the same levels and mutes.

**Why this priority**: This is the request in the issue; everything else supports it.

**Independent Test**: Record two layers with distinguishable content (e.g. different
pitches or one silent channel), export layer 2, and compare the file with the layer:
same length as the loop, sample-identical to layer 2, no trace of layer 1, and the
device state before and after the export is byte-identical.

**Acceptance Scenarios**:

1. **Given** a Strata with a closed loop and two layers, **When** the user exports
   layer 2, **Then** the file holds layer 2's recorded audio only, sample 0 is the
   loop's top, and its length is one loop cycle.
2. **Given** the same Strata, **When** the export finishes, **Then** both layers are
   still present, playing or stopped as before, with unchanged levels and mutes.
3. **Given** a layer that is muted, **When** the user exports it, **Then** the file
   holds its recorded audio and the layer stays muted.
4. **Given** Speed, Reverse, Feedback or a layer level that is not at its default,
   **When** the user exports a layer, **Then** none of them is applied to the file.

---

### User Story 2 - Export after saving and reopening (Priority: P1)

The performer saves the project after the set and opens it the next day. The Strata's
layers are still in its list, and each one can be exported exactly as on the night.

**Why this priority**: The issue's example explicitly goes through save and reopen;
the layers persist with the project, so the export must work from the persisted state.

**Independent Test**: Record two layers, save, reopen, export layer 2 — the file is
sample-identical to the one exported before saving.

**Acceptance Scenarios**:

1. **Given** a saved and reopened project with a two-layer Strata, **When** the user
   exports layer 2, **Then** the file equals the export made before saving.

---

### User Story 3 - A layer that cannot be exported says so (Priority: P2)

A layer that is still being recorded, or a loop whose first pass hasn't closed, has
no finished audio to export. The menu shows the entry unavailable and says why; the
user never gets the mixed loop instead of the layer they asked for.

**Why this priority**: The issue asks for this explicitly ("show that clearly rather
than silently exporting the mixed loop"), but it guards the main flow rather than
adding to it.

**Independent Test**: Start an overdub on layer 3 and open the menu on layer 3 — the
export entry is disabled with a reason. Open the menu on layer 1 during the same
overdub — it is available.

**Acceptance Scenarios**:

1. **Given** layer 3 is being recorded, **When** the user opens its menu, **Then** the
   export entry is unavailable and the menu says the layer is still recording.
2. **Given** the first pass is still recording, **When** the user opens layer 1's menu,
   **Then** the export entry is unavailable and says the loop hasn't closed yet.
3. **Given** the user chose to export layer 2 and, before picking a file name, undoes
   or clears that layer, **When** the file is about to be written, **Then** nothing is
   written and the status bar says the layer is no longer there.

### Edge Cases

- Another layer is being recorded while the user exports a finished one: the finished
  layer exports normally; the recording carries on unaffected.
- The overdub started mid-loop or was stopped before a full cycle: the file is still
  one cycle long with the pass at its position in the cycle and silence elsewhere.
- The layer slot is undone and recorded again while the save dialog is open: the take
  the user chose is gone, so nothing is written and the status bar says so — the new
  take in the same slot is never exported in its place.
- The project was saved during an overdub: after reopening, nothing is recording, so
  that pass is a finished (partial) layer and exports like any other.
- The layer is silent (e.g. recorded with no input): a silent file of one cycle is
  written; no special case.
- Writing fails (full disk, locked file): the status bar reports the failure, as the
  loop export does today, and no partial file is left claimed as a success.
- The device moves in the chain or another device is added while the save dialog is
  open: the export follows the same Strata and writes its layer as it is when the file
  name is confirmed. If that Strata was removed, nothing is written and the status bar
  says so.
- The S (mini) card size has no layer list; the per-layer export is available on the
  L card only, as the layer list is.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each layer row in Strata's layer list MUST offer "Export layer N as WAV…"
  from a right-click menu.
- **FR-002**: The exported file MUST contain exactly the audio Strata keeps for that
  layer: the input after Input gain, before Speed, Reverse, Feedback, the layer's
  level and its mute.
- **FR-003**: The exported file MUST start at the loop's top and be exactly one loop
  cycle long, at the loop's sample rate, in stereo.
- **FR-004**: The exported file MUST contain no audio from any other layer.
- **FR-005**: Exporting MUST NOT change the device: no layer is muted, cleared,
  re-levelled or rewritten, and transport state and position are unchanged.
- **FR-006**: The export MUST work from a project that was saved and reopened.
- **FR-007**: When the layer is being recorded, or the first loop hasn't closed, the
  export entry MUST be shown unavailable with a short reason.
- **FR-008**: If the layer is gone by the time the file would be written, the export
  MUST write nothing and say so in the status bar; it MUST NOT fall back to the
  mixed loop.
- **FR-009**: The save dialog and the menu entry MUST state what is exported (the
  layer as recorded, after Input gain, before playback settings), so the user doesn't
  have to guess.
- **FR-010**: The whole-loop Export button MUST keep its current behaviour.

### Key Entities

- **Layer**: one recorded pass in a Strata — its audio for one loop cycle, its level,
  its mute and whether it is still recording. Indexed from 1 in the UI.
- **Layer export**: a WAV file of one layer's audio for one loop cycle, starting at the
  loop's top, at the loop's sample rate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An exported layer is sample-identical to that layer's recorded audio,
  and its length equals the loop length, in 100 % of exports.
- **SC-002**: Exporting any layer leaves the device's saved state byte-identical to
  before the export.
- **SC-003**: A user gets one layer's file in three steps (right-click, choose the
  entry, confirm the file name), without muting or soloing anything.
- **SC-004**: Exporting the same layer before and after a save/reopen gives identical
  files.

## Assumptions

- The file format follows the existing loop export: 32-bit float WAV, stereo, at the
  loop's sample rate, so the recorded samples are kept exactly.
- The suggested file name is "Nota Strata layer N.wav".
- Layer names in the UI stay "Layer N"; renaming layers is out of scope.
- Exporting all layers at once (stems) is out of scope; the user exports them one at
  a time. MCP / assistant access to layer export is out of scope (see #430).
- Russian strings follow the usual reference-sync path; the English strings land with
  the feature.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
