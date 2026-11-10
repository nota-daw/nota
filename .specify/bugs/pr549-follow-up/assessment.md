# Bug Assessment: PR #549 follow-up review

- **Slug**: pr549-follow-up
- **Created**: 2026-11-09
- **Source**: https://github.com/nota-daw/nota/pull/549 (github.com, HTTPS, allowlisted)
- **Approved scope**: issue #547; continuation of the articulation-map review fixes
- **Verdict**: valid
- **Severity**: high for stuck keyswitches; medium for manager availability

## Symptoms and reproduction

1. Thread `discussion_r4512772111`: legato-launch a Session phrase at beat 8, 120 BPM, 48 kHz, 512-frame blocks. Before the 20 ms chased-key hold ends, change to 60 BPM. The stored sample position belongs to the old beat-to-sample conversion; the next block's position is rebuilt with the new tempo and skips the release. Natural latching switches have the same clock problem.
2. Thread `discussion_r4512772121`: copy a readable `.notamap` file to another filename in the user's articulation folder. `ArticulationLibraryStore.List()` returns both maps with the same ID, and `Catalog()` throws in `ToDictionary`.

## Root causes and remediation

The Session player's absolute sample chase origin is derived from musical position. Replace that reconstruction for Session latching key releases with a bounded, audio-thread-owned pending-release queue, storing the actual channel/key and remaining rendered frames. Count down by rendered frames independently of tempo and preserve releases when map data changes. Queue only successfully emitted key-ons, prioritize pending releases, and reject additional key-ons if the fixed queue is full; no audio-thread allocation or locking.

Build the library lookup with duplicate-tolerant insertion, retaining the first readable copy for each ID. Continue preferring project copies when their contents differ. Test copied files with the same ID in both project-backed and library-only catalogs.

## Files and validation

- Native: `ArticulationSchedule.h/.cpp`, `SessionPlayer.h/.cpp`, `Engine_Render.cpp`; extend native self-test through the real Session renderer for slower/faster tempo changes, natural/chased switches, relaunch and bounded queue behavior.
- Managed: `ArticulationMapUsage.cs`, `ArticulationManagerTests.cs`.
- Run the focused articulation suite and the full required Windows build/smoke/scanner script. Commit each fix independently, push to the existing PR, and resolve the two verified threads.

The prior pushed revision `96733e0e` passed the remote engine instruction-count gate (`42617643537`). These additional changes require their own CI run.
