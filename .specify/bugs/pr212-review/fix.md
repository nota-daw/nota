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

## Regression guards (in the smoke test)

`tests/Nota.SmokeTest/RenderPoolTests.cs` (full run and `-- --perf`), on top of two new ABI
calls — `nota_engine_set_render_threads` / `nota_engine_render_threads` (0 = render on the audio
thread alone, device stopped only) and `nota_engine_render_threads_cpu_seconds` (the render
threads' own CPU clocks):

1. idle engine: render threads < 20 ms CPU over 1 s;
2. after playback stops: the same;
3. real-time playback of 16 tracks: parallel CPU <= 5x single-thread + 5 % of a core;
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
