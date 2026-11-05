<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Validation: multiple CC lanes and saved heights

Date: 2026-11-02. Base: official develop `535da00f`.
Approved issue: [#422](https://github.com/nota-daw/nota/issues/422).

## Results

- `scripts/build-win.ps1`: PASS, exit 0, native scanner self-test, Release managed
  build (zero errors), complete `SMOKE TEST PASSED` and `SCAN SCALE PASSED`.
- Real Windows Avalonia probe: 16 checks PASS. Covers distinct controller views,
  continuous/switch controllers, removal/re-add data preservation, existing single
  commit routing, stale-gesture cancellation, shared gutters, zoom/scroll, short
  windows, real pointer resizing, duplicate-controller heights and editor reopening.
- Fresh process: PASS, saved lane heights restored through SettingsService.
- Lane-height normalization/serialization smoke checks: PASS in the complete suite.
- Studio Dark and Studio Light rendered. Existing design/localization guards pass;
  French covers every new label and the divider gesture is in Shortcuts.
- Before/after screenshots: same 1100x850 window, Studio Dark, 100% scale, note data
  and CC1/CC11/CC64 data. Captured from separate base/branch binaries and inspected.
- Native/ABI, transport/playheads, themes, zoom bounds, velocity drawing/picking,
  workspace activation and app identity have no changes in this contribution.
- `git diff --check`: PASS. Existing unrelated compiler warnings remain.

Capture source: `validation/Capture/`. Images live on the fork's `pr-assets` branch:

- [Before](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/cc-lanes-before.png)
- [After](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/cc-lanes-after.png)

The capture and regression harness sources are included; logs, generated images,
probe data and binaries are excluded from the code branch.

## Convergence

Converged: nine functional requirements, five success criteria, three user stories,
implementation tasks and plan decisions checked against source and fresh evidence.
No missing, partial, conflicting or unrequested work remains in the specified PR
scope. Submission task T011 is complete: [PR #425](https://github.com/nota-daw/nota/pull/425) targets develop.
No extension hooks are registered.

This is a partial implementation of issue #422: lane reordering, a dedicated collapse
control, and persistence of which extra lanes are visible remain follow-up work.
The current picker retains its reserved-controller restrictions. Lane sizes persist
by mode or extra-lane slot/controller; the extra view list itself is editor-local.

## PR #425 folded Drum Rack review correction

The expanded probe reproduces the reported bug on commit `836f736c`: a double-click
directly beneath a beat-2 drum note fails to write its CC point at beat 2. Initializing
new columns from the current pad-row state fixes it. All 22 UI checks pass, including
the six new checks for pointer beat mapping, immediate alignment, unchanged-row refresh,
unfold/refold and removal/readdition. Fresh-process height restoration also passes.
The full `scripts/build-win.ps1` gate passes again, including the native scanner
self-test, complete smoke suite and scan-scale check.

Matching Studio Light captures (1000 × 720, identical clip, before the pointer edit):
[before](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/folded-cc-before.png),
[after](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/folded-cc-after.png).
Assessment, fix and verification records: `.specify/bugs/folded-cc-gutter/`.

## PR #425 primary lane tab correction

The primary row now has a 72-pixel layout minimum after available-space fitting,
including when restoring an older 48-pixel preference. Extra CC lanes keep their
48-pixel minimum. The expanded probe performs actual window hit testing and pointer
selection for all five tabs after dragging the divider; it repeats the controller
selection in a short reopened editor with disk-restored settings. It also checks the
note viewport, extra lane minimum and absence of controller commits.

The corrected harness fails against `20cbc8f3`: the controller-tab position hits the
toolbar's ContentPresenter with a 48-pixel primary row. The fixed implementation passes
all 39 UI assertions, and the full Windows build/smoke/scan-scale gate passes again.
Assessment and evidence: `.specify/bugs/primary-lane-tabs/`.

Matching Studio Light captures (1000 × 720, identical clip and divider drag):
[before](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/primary-tabs-before.png),
[after](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/primary-tabs-after.png).

## PR #425 docked note-area correction

The real MainWindow/ClipEditorView regression reproduces the issue on `a80d1654`:
a 190-pixel docked panel leaves a 119-pixel roll and a zero-height note viewport.
The MIDI-specific 300-pixel host minimum instead leaves a 104-pixel note viewport;
with clip strip, horizontal scrollbar and extra lanes present it still leaves 57
pixels (over four default note rows). The primary tabs retain their 72-pixel minimum.

All 60 UI checks pass. The 21 new checks include real note selection and all five
tab selections through window hit testing, restored/default/hidden/maximized panels,
extra lane overflow, actual panel-edge dragging, a docked Mixer fitting inside the
window, restoration of the ordinary window minimum after hiding the Mixer, and the
unchanged minimum for non-MIDI content. Saved lane heights still restore in a fresh
process. The full Windows build/smoke/scanner gate is recorded in the bug report.

Matching full-window Studio Light evidence (1200 × 711 render bounds; identical
clip and requested 190-pixel panel height):
[before](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/docked-notes-before.png),
[after](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422/docked-notes-after.png).
Assessment and verification: `.specify/bugs/docked-note-area/`.

## Limits

Windows x64 was built and exercised; macOS/Linux and physical mixed-DPI monitor moves
were not tested. The sibling nota-docs checkout is absent. Paired English/Russian
manual updates are in `manual-handoff.md`; no external manual-site build is claimed.
This UI contribution changes no audio thread or render-pool code and makes no DSP
performance claim; the complete smoke suite includes the existing performance guards.
