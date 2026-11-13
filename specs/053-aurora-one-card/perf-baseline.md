# Performance record: 053-aurora-one-card

Baselines (T005) and results (T045) for SC-003 to SC-006.

## Where these numbers come from

**These are not acceptance numbers from the 036 reference machine (i7-12700).** They were
taken in a 4-core Linux container with no display. Each comparison below was run on that
same container, before and after, so the deltas mean something; the absolute values do
not carry over. The reference-machine runs (quickstart § 4 and § 5, with `pidstat`) are
still to do.

- Engine: Release. The UI was measured in a Release build of a headless Avalonia
  harness (Skia, 2× scale) that opens the fixture project, shows the device chain on the
  Osc tab of each Aurora in turn, and plays the loop.
- Fixtures: `--aurora-ui-fixture <dir> --builtin` writes `baseline.nota` (stamped format 27
  so the base build opens it). `--aurora-ui-fixture <dir>` writes `scenario.nota`, with
  256-frame tables on both oscillators of all 4 Auroras. Both have LFOs on both positions
  and a 4-bar chord loop.
- **base**: the card before this feature (6b8a83e2), on `baseline.nota`.
  **feat-base**: this branch on `baseline.nota`. **feat-scen**: this branch on
  `scenario.nota` (the SC-003 scenario).
- **UI work**: wall time of the live tick (`RefreshSynthLive` plus the jobs it posts) per
  second, over 600 ticks at 60 Hz. It stands in for UI-thread %CPU.
  **Table paint**: one `AuroraTableView` rendered alone into a bitmap, 600 to 1200 samples.
  **Probe paint**: `NOTA_UI_PROBE=aurora`, measured in the tree.
  **Card build**: from the device-chain rebuild to the first frame, 12 switches.

## UI (SC-003, SC-004, SC-005)

| Run | UI work | Table paint p50 / p99 | Probe paint p99 | Card build → first frame p50 |
|-----|---------|-----------------------|-----------------|------------------------------|
| base, run 1 / 2 | 10.4 / 8.1 ms/s (1.0 / 0.8 % of a core) | 1.80 / 4.44, 1.80 / 3.70 ms | — | 204 / 205 ms |
| feat-base | 7.5 ms/s (0.75 %) | 0.46 / 1.26 ms | 1.46 ms | 191 ms |
| feat-scen | 7.9 ms/s (0.79 %) | 0.43 / 1.65 ms | 1.48 ms | 184 ms (probe: 115 ms to first paint) |

The first version of the imported view, with no cache, failed SC-004: feat-scen painted at
6.4 / 10.6 ms (p50 / p99) and the tick took 47 to 54 ms/s. That triggered T046. The view
now draws the window and the depth stack once into a bitmap, keyed by table serial, bank,
size, inset, dim, scale and ink colour, and paints only the lit frame on top each tick. The
lit index moves on a 64-step grid, so a paint happens only on a step.

- **SC-003 (≤ +2 points over today's card, ≤ 5 % in total)**: met in the container:
  0.79 % against the base's 0.8 to 1.0 %.
- **SC-004 (≤ 2 ms p99 per paint)**: met: 1.5 to 1.65 ms.
- **SC-005 (≤ 100 ms card build)**: **not met here, and the unchanged card misses it too**
  (about 204 ms in this container). This feature is no slower than the base (184 to 191
  ms). It needs checking on the reference machine before it can be called.
- **FR-018 (no work while hidden)**: the tick that drives `RefreshSynthLive` is skipped
  while the device chain is not effectively visible (`MainWindow.axaml.cs`, the
  `IsEffectivelyVisible` gate), and the whole tick returns early while the window is
  minimised, unless the floating detail window is still showing. No table-view work runs
  in either state; the probe counts no paints.

## Audio (SC-006): `nota_perf`

Callgrind instructions for 4 s of render. These repeat exactly between runs, and they are
what the perf gate compares.

| Engine | Scenario | Instructions | vs base |
|--------|----------|--------------|---------|
| base (6b8a83e2) | `aurora-supersaw-2x` | 3 724 778 244 | — |
| this branch | `aurora-supersaw-2x` | 3 754 726 913 | +0.80 % |
| this branch | `aurora-imported-2x` | 4 056 732 784 | +8.0 % over built-in tables on this branch |

The rise on built-in tables is the snapshot read at the top of each block, plus the
per-oscillator custom-table check. It stays under the gate's 1 % limit. The imported
tables cost more because each read picks a mip level and reads a sized level. That stays
within SC-006's +10 %.

Wall time (ms of render per s of audio, 20 s, six rounds interleaved): base 60.2 to
71.1, this branch 64.6 to 69.6, imported 65.9 to 80.7. The fastest rounds were 60.2, 64.6
and 65.9. In this container that is 2.4× the 036 reference figure (about 28 ms/s), so only
the ratios carry over.

`aurora-imported-2x` reports "base error" against an engine without the table calls. The
harness looks those calls up at run time, so it still builds against the base header, and
the gate accepts that row.

Factory presets: the null gate in `--aurora-tables` renders each factory preset on this
branch and compares it with the stored built-in render. The residual is −240 dBFS, so the
renders are bit-identical.
