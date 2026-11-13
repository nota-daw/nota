# Bug Assessment: PR #549 articulation playback and project map selection

- **Slug**: pr549-review (generated from the PR number)
- **Created**: 2026-11-09
- **Source**: https://github.com/nota-daw/nota/pull/549
- **URL policy**: github.com, HTTPS, allowlisted
- **Approved scope**: nota-daw/nota#547 (`approved`, checked with GitHub CLI)
- **Verdict**: valid
- **Severity**: high — missing notes, stuck switches, wrong techniques and overwritten project settings

## Report and reproduction

The review identifies seven faults in the original PR:

1. Seek into a latching phrase using 512-frame blocks at 48 kHz: the chased switch's 960-sample release falls outside the first block and is discarded.
2. Loop a one-beat Session clip at 120 BPM with a +1500 ms offset: source passes more than one loop away are skipped, losing notes and switches.
3. Launch a Session clip with a beat-zero note and a trigger lead, or legato into a phrase: its switch occurs before launch and is never restored.
4. Apply a Session note offset to a recorded expression curve: expression remains at the original time.
5. Save a shared map edit, undo the track changes, and reopen the manager: its library copy hides the restored project copy.
6. Overlap clip A (articulation 1 at beats 0 and 2) with clip B (articulation 2 at beat 1): clip A's second switch was discarded before playback.
7. Overlap momentary notes sharing a keyswitch: the shorter note releases a key still required by another note, including a different articulation using the same key.

The engine performance check also fails on four inexpensive instrument scenarios (+1.38–1.59% instructions) and `edit-warp-split` (+1.17%). The full CI report was read with `gh run view 42585182796 --log-failed`.

## Suspected code paths and root causes

- `ArticulationSchedule.cpp`: stateless chase releases discarded by the block window; clip-local latching decisions; independent momentary releases.
- `ArticulationTable.cpp`: removes repeated latching notes before other clips can be considered.
- `Engine_Render.cpp`: neighboring Session passes only, no launch chase, unshifted expression, and articulation event array default initialization on every render.
- `Engine.cpp`: graph scans before the cheap no-map timeline check during publication.
- `ArticulationMapUsage.cs`: library-first deduplication by map ID.

Confidence: high; all seven reports follow directly from the original code. Existing uncommitted fixes are present in these files and must be preserved and completed. Their fixed 32-pass limit still loses valid events, and their shortened chase hold does not preserve the specified 20 ms duration.

## Proposed remediation

Keep the existing draft's track-wide latching decisions, project-first catalog, no-map fast paths, and shifted expression. Derive Session pass ranges from event times and note ends without an arbitrary neighboring-pass limit. Preserve the last chase origin so a stateless scheduler can emit its 20 ms release in a later block. Check shared momentary keys across all audible notes and clips. Extend native self-tests and C ABI smoke tests for exact timing, small blocks, short loops, overlapping clips/keys, launch/legato, and map save/undo/reopen.

## Risks and validation

The audio thread must remain allocation-free, lock-free and free of I/O. Use immutable clip/table data and caller-owned fixed event arrays. Keep tracks without maps on their fast path. Run the focused articulation suite and the required Windows build/smoke/scanner script. Linux instruction-count validation cannot run locally: WSL has no installed distribution and virtualization is disabled; report this explicitly instead of claiming CI passed. No commit or push is part of this local repair.
