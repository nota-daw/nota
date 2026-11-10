# Bug Assessment: PR #549 muted releases and edit overhead

- **Slug**: pr549-mute-perf
- **Created**: 2026-11-09
- **Source**: https://github.com/nota-daw/nota/pull/549 (github.com, HTTPS, allowlisted)
- **Approved issue**: #547, approved and open
- **Verdict**: valid
- **Severity**: high for premature switch releases; medium for edit overhead

## Reproduction and root causes

Review thread `PRRT_kwDOUD4x7861Q6P7` reports that Session release timers stop while a track is muted. The silent-track path flushes instrument voices and advances the slot clock but retains queued releases. Unmuting into a momentary note using the same key lets an obsolete release cut the new hold. Solo gating takes the same path.

The engine instruction gate on revision `8abb3ad1` failed `edit-warp-split` at +4.59% work versus the base (run `42622338564`). `cloneTrack` constructs a default Session player, including its now larger fixed release storage, then immediately replaces it with the old track's player. Avoid constructing and destroying playback state that a clone will never use.

## Remediation and validation

1. Clear pending Session releases when the silent-track path flushes voices. Add an end-to-end native regression that launches a latching switch, mutes or solo-gates during its hold, then unmutes into a momentary switch on the same key. Demonstrate failure on the old code.
2. Allow `Track` construction with an existing Session player, and have `cloneTrack` pass it directly. Preserve the shared player's state across edits. Measure the actual warped-split/undo scenario before and after using identical binaries of an amplified local performance harness. Linux instruction-count validation remains CI-only on this Windows host.
3. Run focused articulation tests and the required complete Windows build/scanner/smoke checks, commit the fixes independently, push to PR #549, and resolve the verified review thread.

No audio-thread allocation, locks, ABI changes, or weakened performance thresholds. Changes stay within the articulation feature's approved issue. Raw local validation/benchmark output remains ignored.
