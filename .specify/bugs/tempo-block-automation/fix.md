<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Block automation depends on selection order

- **Slug**: tempo-block-automation (from this session's assessment)
- **Fixed**: 2026-11-14
- **Assessment**: ./assessment.md
- **Status**: applied
- **Issue**: #535; PR #568

## Summary

Block paste/duplicate and block move now clear every applicable destination span
before inserting any captured automation. Expanded audio spans cannot erase
points inserted by another item in the block.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine.h` | modified | Private Replace/Clear/Insert helper modes; Replace remains the default. |
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | Separate clearing/insertion in both multi-item paths, after shared placement and source lifting respectively. |
| `tests/Nota.SmokeTest/TempoBlockAutomationTests.cs` | added | 243 production C ABI checks for order independence and existing contracts. |
| `tests/Nota.SmokeTest/Program.cs` | modified | Register regressions in focused tempo and full smoke runs. |
| `CHANGELOG.md` | modified | Describe selection-order-independent block automation. |

## Diff Highlights

- Existing helper matching remains shared: target, device/parameter identity,
  chain uid, slot and mix-only filtering follow the same path in every pass.
- Clear-only visits existing lanes; it does not create a new or filtered-out lane.
  Insert-only creates any required lane and appends/sorts points without clearing.
- Single-clip callers continue using Replace with the previous behavior.
- `placeBlock` clears after computing its final shared shift, using mapped
  destination lengths. Only afterward are clips and automation inserted.
- `moveClipBlock` retains its lift-all-sources pass, then clears all destinations
  and inserts all snippets. The entire block still publishes once.

## Tests Added or Updated

27 scenarios, each run in both selection orders: expanding, shrinking and legacy
constant tempo across duplicate, block paste, range paste, same-track move,
cross-track moves with matching/mismatched layouts, cross-track paste with
existing/missing lanes, and paste that must shift past existing audio.

Assertions cover Volume, Pan and device lanes; every carried point, destination
clearing, outside points and gaps, source retention/removal, layout filtering,
missing-lane creation, order equality and atomic undo/redo of clips and automation.

## Local Verification

- Before remediation: 16 failures on `98d17569`, covering lost copied points and
  differing selection-order results in all eight applicable expanded-span paths.
  Log: `build/tempo-block-automation-before.log`.
- After remediation: all 572 focused tempo checks pass, including 243 new checks.
  Log: `build/tempo-block-automation-after.log`.
- Final full Windows verification is recorded in `test.md`.
- The first full run and first isolated performance run failed the existing export
  worker-CPU participation guard (reported 0% while parallel export remained about
  five times faster). A clean build of unchanged `98d17569` passed it at 91%; the
  exact patched DLL then passed the same isolated harness at 96%. No source,
  threshold or test setting was changed between patched runs. Failed logs are
  retained; the complete Windows check was rerun before publication.

## Deviations from Assessment

None. The related block-move defect was included in the assessment before edits
and reproduced by the same regression matrix.

## Documentation Audit

nota-docs/nota-features: the change restores documented multi-clip automation
follow/copy behavior. No new capability, control, label or shortcut is introduced;
the capability list and arrangement/automation manual pages need no edits.
No sibling repository files were changed.

## Follow-ups

The existing hosted cross-platform and engine performance checks must pass on the
new commit. Changes are confined to authoring edits; no render/DSP/callback path
or public C ABI changed. No extension hooks are registered.
