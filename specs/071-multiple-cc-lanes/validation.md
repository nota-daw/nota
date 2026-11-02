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
scope. Submission is tracked separately as T011. No extension hooks are registered.

This is a partial implementation of issue #422: lane reordering, a dedicated collapse
control, and persistence of which extra lanes are visible remain follow-up work.
The current picker retains its reserved-controller restrictions. Lane sizes persist
by mode or extra-lane slot/controller; the extra view list itself is editor-local.

## Limits

Windows x64 was built and exercised; macOS/Linux and physical mixed-DPI monitor moves
were not tested. The sibling nota-docs checkout is absent. Paired English/Russian
manual updates are in `manual-handoff.md`; no external manual-site build is claimed.
This UI contribution changes no audio thread or render-pool code and makes no DSP
performance claim; the complete smoke suite includes the existing performance guards.
