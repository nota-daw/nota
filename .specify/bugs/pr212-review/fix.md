# Bug Fix: Review findings on the engine rebuild (#212)

- **Slug**: pr212-review
- **Fixed**: 2026-10-20
- **Assessment**: ./assessment.md
- **Status**: applied (item 3 in `../idle-pool-spin/`)

## Summary

Per-block render scratch is now published as one immutable-size object through `SnapshotHistory`
instead of being grown in place under the audio thread; pool completions publish with `release`;
racks report nested hosted plugins so such tracks stay off the pool; unused stage-silence passes and
pool scratch are gone; the plugin scanner lost its dead code, shares one format manager with hosting
and instantiates from a single locked copy of one entry; CI caches CPM sources, no longer checks out
submodules, and every git dependency is pinned by commit.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/engine/Engine.h` | modified | `RenderScratch` (leaf work + buffers, MIDI/expr pre-pass) + `scratchHist_`/`rs_`; removed `leafWork_`, `leafBuf_`, `blockMidi*_`, `blockExpr*_`, `stageSilence_`/`stageSilent_` |
| `src/engine/Engine.cpp` | modified | `ensureRenderScratch()` builds + publishes a new scratch (steps of 8 tracks) before the graph swap; replaces `ensureMidiScratch`/`ensureTrackScratch`; drops pool scratch reserve and detector rate setup |
| `src/engine/Engine_Render.cpp` | modified | `processBlock` takes this block's scratch after the graph load under a `RenderScope`; all users read `rs_->…`; eligibility uses `hostsPlugin()`; four detector passes removed |
| `src/engine/AudioThreadPool.{h,cpp}` | modified | `done_.fetch_add(1, release)` in caller + worker; scratch API removed |
| `src/devices/Device.h`, `Instrument.h` | modified | `virtual bool hostsPlugin()` (default: not a built-in) |
| `src/devices/racks/RackCore.{h,cpp}`, `RackDevice.h`, `RackInstrument.h` | modified | `chainsHostPlugin()` walks the live snapshot recursively; racks (and Drum Rack) override `hostsPlugin()` |
| `pluginhost/scanner/PluginCatalog.{h,cpp}` | modified | indexed `types` copy synced on every mutation (reads no longer copy the whole list); `typeAt()`; `sharedFormatManager()` lives here; removed `snapshot`, `entryAt`, `hasFileOrIdentifier`, `types()`, free `catalog::indexOfId` |
| `pluginhost/scanner/OutOfProcessScanner.{h,cpp}` | modified | removed `ScanResult`, `scanOne` |
| `pluginhost/scanner/abi_pluginscanner.cpp` | modified | uses `scanner::sharedFormatManager()` |
| `pluginhost/bridge/PluginCatalogBridge.h` | removed | only served the dead code |
| `pluginhost/hosting/PluginCommon.{h,cpp}`, `PluginHost.cpp` | modified | `instantiateCatalog(index, sr, block, instrumentOnly)` copies one entry and checks it; second format manager removed |
| `src/native/nota.engine/CMakeLists.txt`, `ARCHITECTURE.md` | modified | comments/docs for the removed bridge header |
| `src/native/nota.engine/cmake/dependencies.cmake` | modified | miniaudio `9634bed`, flac `1507800`, JUCE `2cdfca8` (tag in comment) |
| `.github/workflows/ci.yml`, `release.yml` | modified | no `submodules: recursive`; `actions/cache@v6` on `.cpm-cache` + `CPM_SOURCE_CACHE` (container job sets it from `$GITHUB_WORKSPACE`) |
| `.gitignore`, `README.md`, `LICENSES/third-party.md` | modified | `.cpm-cache/`; plain `git clone`; pins described as commits |

## Tests Added or Updated

Throwaway harnesses in `/tmp/pool223/` (not committed; the engine has no in-repo native test target):

- `grow.cpp` — a render thread runs 256-frame blocks non-stop while the main thread adds 40 tracks
  (instrument + clip / audio + Reverb) and removes them, 4 rounds.
- `stress.cpp`, `idle.cpp`, `bench.cpp` — from `idle-pool-spin`.

## Local Verification

- `grow` on pre-fix `develop`: **SIGSEGV (exit 139) 3/3**. After the fix: exit 0 5/5 (Release) and
  2/2 under AddressSanitizer with no reports.
- Fresh configure with an empty `CPM_SOURCE_CACHE` fetched all deps; JUCE/flac/miniaudio checkouts
  are at the pinned SHAs; JUCE shallow fetch by SHA works.
- `stress` 0 bad tickets; `idle` 0 %; bench: 32 tracks offline 22.3 ms wall/s (unchanged), rt 32
  tracks 556 ms CPU/s (was 608 before removing the detector passes).
- `scripts/build.sh` — see test.md.

## Deviations from Assessment

- `blockMidi_`/`blockExpr_` had the same in-place growth race as `leafBuf_`/`leafWork_` and were
  moved into the same scratch object.
- Beyond `instantiateCatalog`, every catalog read (`count`, `idAt`, `descLine`, `indexOfId`, scan
  helpers) copied the whole list via `KnownPluginList::getTypes()`; the indexed copy fixes them all.
- The shared format manager moved to `scanner/` (not `hosting/`), keeping the rule that the scanner
  never includes hosting code.

## Later change: adaptive linger in the pool

Benchmarking against pre-#212 and post-#212 builds showed the park-at-once workers made
**offline** renders (export, freeze) slower than post-#212 on light loads (32 synth tracks:
8.8 vs 6.3 ms per second of audio): back-to-back blocks are only tens of µs apart, so every block
paid a wake-up. A fixed longer spin fixed that but cost 8–40 % more CPU in real time. `dispatch()`
now measures the gap since the previous job: under 200 µs, idle workers linger up to twice the gap
(≤ 100 µs) before parking; a device callback's gap is a whole buffer, so they park at once.
Result: offline matches post-#212 (3.7 / 6.5 / 21.2 ms), real time stays low, idle 0 %.

## Later change: real-time policy — one thread unless it can't keep up

The three-way benchmark still showed the pool spending 2–2.7x the CPU of the single-threaded
engine while playing (32 audio tracks: ~70 % vs ~31 % of a core). Measured, step by step:

- Waking only as many workers as the job needs (`dispatch(n, fn, helpers)`, `notify_one` x k)
  changed nothing: on a light block the woken threads cost the same either way.
- On Apple Silicon the default worker count spilled onto efficiency cores (13 workers, 10
  performance cores); capping at performance cores − 1 cut offline CPU by ~22 % at the same speed.
  QoS classes made no difference.
- The real cost: a block that fits the buffer, spread over parked threads, wakes idle cores at
  low clocks. With small buffers parallel rendering also missed **more** deadlines than one
  thread (128 tracks at 64 frames: 352 late blocks vs 2), because waking takes a good part of a
  1.3 ms period.

So with a device the audio thread renders alone; the pool joins only under sustained overload —
when the single-thread cost of the parallel leaves (measured on blocks rendered alone, smoothed
by 1/16) passes the buffer period — and leaves after 2 s to re-measure (while it runs, a block
rendered alone reads 1.5–2x its real cost, so it can't tell when the overload ends). Offline
renders (back-to-back `renderOffline` blocks) still use every thread. Two traps found on the way
and closed: a slow device block looked "back-to-back" and switched to offline mode (now only
`renderOffline` can be offline), and lingering workers stole the tickets of a job that woke no one
(no linger without helpers). New ABI `nota_engine_render_device_block` renders one block through
the callback's path without a device, so tests and the benchmark measure what a device does.

Device path, same machine: 32 audio tracks at 512 — 30 % (= one thread, = before #212);
128 tracks at 64 frames — 75 %, same late blocks as one thread; 192 tracks at 64 frames
(overload) — 547 late blocks of 7500 vs 2970 on one thread.

## Regression guards (in the smoke test)

`tests/Nota.SmokeTest/RenderPoolTests.cs` (full run and `-- --perf`), on top of two new ABI
calls — `nota_engine_set_render_threads` / `nota_engine_render_threads` (0 = render on the audio
thread alone, device stopped only) and `nota_engine_render_threads_cpu_seconds` (the render
threads' own CPU clocks):

1. idle engine: render threads < 20 ms CPU over 1 s;
2. after playback stops: the same;
3. real-time playback of 16 tracks (device path) that one thread keeps up with: render threads
   < 2 % of a core, total CPU <= 1.5x single-thread + 3 %;
3b. overload (256 tracks, 64-frame buffer, most blocks late on one thread): render threads join,
   late blocks not more than on one thread (skipped when this machine keeps up alone);
4. bounce of 16 tracks: parallel CPU work <= 3x single-thread; wall time <= 1.3x (timing budget,
   lenient under CI);
5. 8 synth tracks render bit-identically on N threads and on one;
6. 48 tracks added/removed while blocks render: no crash.

Proven against the old behaviour: with the #212 spin put back, checks 1–3 fail (11.6 s of worker
CPU per idle second); with the scratch grown in place again, check 6 crashes the run.

## Follow-ups

- Priority inversion: pool workers run at normal priority while the audio callback waits on them.
  Needs per-platform work (macOS audio workgroup / QoS, Windows MMCSS, Linux SCHED_FIFO).
- `specs/030-dsp-architecture-overhaul/` still describes `PluginCatalogBridge.h` (historical spec
  artifacts, left as is).
- The rack-plugin eligibility path was not exercised at runtime (no plugin installed in the test
  environment); covered by build + code reading.
