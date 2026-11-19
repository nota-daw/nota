# Bug Fix: Export puts sidechain pumping a chunk late

- **Slug**: sidechain-same-block
- **Fixed**: 2026-11-19
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

A sidechain key now reaches its device in the same block. Pass 1 renders a keyed leaf a level
after the leaves keying it and writes each level's taps before the next level renders; a per-slot
"fresh" flag tells `keyBuffer()` to hand out this block's bus. Groups and returns hear leaves (and
earlier groups / returns) in the same block. The export of `Bugged.nota` now pumps on the kick.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine.h` | modified | `LeafWork` gains `keyNeeds`, `level`, `preRendered`, `tapsDone`, `vol`/`pan`; `routeFresh_[16]` + `kMaxKeyLevels` appended last; the route-bus comment describes same-block keys |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | `keyBuffer()` reads Next when fresh; `mixGraph()` clears the flags, computes leaf levels, dispatches the pool per level with `finishLevel()` (serial leaves, taps, flags) between, accumulate skips taps already written; groups / returns mark their slot after writing; comments |
| `src/native/nota.engine/src/devices/Device.h` | modified | `setSidechainLate(bool)` (default no-op), called by `feedDeviceKey` after `setSidechain` |
| `src/native/nota.engine/src/devices/effects/Lens.h`, `Lens.cpp` | modified | Nota Lens holds its block a block back only when the key is late (it assumed every key was) |
| `tests/Nota.SmokeTest/SidechainSyncTests.cs` | added test | `--sidechain-sync` |
| `tests/Nota.SmokeTest/Program.cs` | modified | `--sidechain-sync` entry + full run |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed |

## Diff Highlights (optional)

```cpp
const float* Engine::keyBuffer(int32_t src, uint64_t part, bool pre) const {
    const int32_t s = routeSlotFor(src, part);
    if (s >= 0) {
        if (routeFresh_[s]) return pre ? routeBusPreNext_[s].data() : routeBusNext_[s].data();
        return pre ? routeBusPre_[s].data() : routeBus_[s].data();
    }
    ...
```

```cpp
for (int L = 0; L <= maxLevel; ++L) {
    const int n = levelEnd[L] - off;
    if (n > 0) pool_->dispatch(n, [&](int wid, int j) { task(wid, j); }, h);
    off = levelEnd[L];
    if (staged) finishLevel(L); // serial leaves of L, then L's taps, then its slots fresh
}
```

Without a leaf-to-leaf key every leaf is on level 0: one dispatch, `finishLevel` never runs, the
accumulate writes the taps as before (the perf-gated path is unchanged but for a 16-bool clear).

## Tests Added or Updated

- `SidechainSyncTests.Onsets` (×4) — a kick-keyed Compressor on a tone track after the kick,
  before it, on a group, on a return: the duck starts right after the kick at offline blocks
  256 / 512 / 1024 / 4096 / 8192 and live 512 (spread < 2 ms, < 15 ms after the beat). On the
  pre-fix engine the onset is exactly one block: 5.3 / 10.7 / 21.3 / 85.3 / 170.7 ms — all four fail.
- `SidechainSyncTests.Cycle` — two tracks keying each other plus a third keyed off one render
  bit-identically from five fresh engines (no thread-timing dependence).

## Local Verification

- `dotnet Nota.SmokeTest.dll --sidechain-sync` → SIDECHAIN SYNC PASSED (5/5); same binary with
  the v0.60.3 engine → 4 FAIL (onsets above).
- `/private/tmp/nbugged` on the user's `Bugged.nota` (Volt soloed, from 8.1): dip at 0.156 of the
  beat live and offline at 256 … 8192 frames, lag 0 (before: 0.406 and ~125 ms at 8192).
- `scripts/build.sh` → exit 0: SCANNER SELFTEST, SMOKE TEST (7835 checks, incl. --lens-sync, --sidechain-parts, --sidechain-sync, render pool), SCAN SCALE, DEFAULT SCAN PATHS (+ UPGRADE) PASSED. The first run, before the Lens change, failed one check (LensSyncTests "Sync off" key trace).

## Deviations from Assessment

- Scope grew to Nota Lens (`Device.h`, `Lens.h/.cpp`): its key overlay held each block back one
  block to meet a key it knew arrived one late. With same-block keys its "Sync off" key trace sat
  a block off the main trace (`LensSyncTests`, the one full-run failure of the first build.sh). The
  engine now tells a keyed device whether the key it handed over is late
  (`Device::setSidechainLate`); Lens delays only then, and when a key stops being late it captures
  the held block without one. No other device compensated for the lag (searched `devices/`).

## Follow-ups

- A leaf keyed by a group or return still hears it a block late (groups render after leaves).
- Before/after engine benchmark for the PR (nota-pr: engine render path change) — sidechain
  projects add one pool dispatch per key level.
- Reply on #676 once released, with the export of `Bugged.nota`.
