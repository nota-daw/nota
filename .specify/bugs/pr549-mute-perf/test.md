# Bug Verification: PR #549 muted releases and edit overhead

- **Slug**: pr549-mute-perf (from assessment/fix context)
- **Tested**: 2026-11-09
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified — functional regression and Linux instruction-count gate pass

## Checks performed

| Check | Result | Evidence |
|---|---|---|
| Old-code reproduction | expected failure | The rebuilt native engine with regression check 38 and the old mute path fails the focused suite at check 38 (`reproduce.log`, exit 1). |
| Focused articulation suite | pass | 414 checks, zero failures; native self-test returns zero (`articulations.log`). |
| Complete Windows build | pass, exit 0 | `scripts/build-win.ps1`: native and managed builds, scanner self-test, full smoke suite and scan-scale checks all pass on the first full run (`build-win.log`). |
| Source whitespace | pass | `git diff --check`. |
| Before/after edit benchmark | measured | Identical Release harness, 9 interleaved rounds, 1000 cycles of 8 splits and 8 undos, split fades disabled. See chart, raw data and machine details below. |
| Linux instruction-count gate | pass | [Performance run 42627356463](https://github.com/nota-daw/nota/actions/runs/42627356463), PR source `9eb6382c`: all engine scenarios pass; `edit-warp-split` is +0.45% work versus the base, within the 1% limit. |
| Cross-platform idle CPU | pass | Linux, macOS and Windows idle-CPU jobs all pass in the same performance workflow. |

## Reproduction coverage

Native check 38 renders the real engine/Session path. A latching key starts at frame 0; the track is muted or solo-gated during the 20 ms hold; on unmute a momentary articulation presses the same key. The old pending release at offset 448 must not cut the new key pressed at offset 357. Both mute and solo-gating cases pass after clearing the flushed queue.

The allocation fix is commit `bc131d63`; it constructs graph clones with the existing Session player rather than allocating and discarding a fresh player and its release storage. The combined source tree was built and tested before committing.

## Benchmark evidence

Before: `8abb3ad1`. After: `bc131d63`. Windows Release/MSVC, AMD Ryzen 9 7900X, 24 logical CPUs.

| Revision | Median µs/edit | Min | Max |
|---|---:|---:|---:|
| Before | 3.818 | 3.731 | 4.525 |
| After | 3.903 | 3.740 | 4.345 |

The +2.2% median wall-time difference has overlapping ranges. No Windows timing speedup or Linux instruction-count improvement is inferred from these numbers.

The authoritative Linux/Valgrind result is separate: run `42627356463` compares base `ad5b1c6f` with PR merge `4a87a167` (PR source `9eb6382c`). It passes all engine scenarios. `edit-warp-split` is +0.45% instructions versus the base, down from +4.59% on `8abb3ad1`, and within the 1% gate. The mute review thread is resolved; no unresolved review threads remained when checked after this run.

![Warped split/undo wall times](https://raw.githubusercontent.com/thoi1/nota/pr-assets/pr549-mute-perf/edit-warp-split.svg)

[Raw CSV](https://github.com/thoi1/nota/blob/pr-assets/pr549-mute-perf/edit-warp-split.csv) · [Method, commits and machine](https://github.com/thoi1/nota/blob/pr-assets/pr549-mute-perf/summary.md)

Assets are committed on `pr-assets`, not the source branch. Local logs and harness binaries remain ignored. No tests or performance thresholds were weakened. Linux/macOS were not built locally.
