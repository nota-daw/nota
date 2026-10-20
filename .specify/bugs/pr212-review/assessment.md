# Bug Assessment: Review findings on the engine rebuild (#212)

- **Slug**: pr212-review
- **Created**: 2026-10-20
- **Source**: review of https://github.com/nota-daw/nota/pull/212 (host: github.com — allowlisted), list pasted by the maintainer
- **Verdict**: valid
- **Severity**: high (use-after-free on the audio thread; ARM64 data race), the rest low

## Report (summarized)

Findings left on the merged PR #212 (`64250a5`):

1. Use-after-free: `ensureTrackScratch` (Engine.cpp:601) reallocates `leafBuf_`/`leafWork_` on the
   message thread inside `publishRaw` while the audio thread and pool workers write the current
   block into them. Adding a track during playback can corrupt memory or crash.
2. `AudioThreadPool` ordering: `done_.fetch_add` is `relaxed`, so the caller's acquire load has
   nothing to synchronise with; on ARM64 the accumulate can read half-written worker buffers. Fix:
   `release` in both places.
3. Workers spin forever — already fixed in `idle-pool-spin` (#223).
4. The parallel-render eligibility check doesn't look inside racks; v4 racks can hold hosted
   plugins, which would then run on a pool thread.
5. `stageSilent_` is computed every block but never read — wasted passes on the RT thread.
6. Pool scratch is reserved but unused.
7. Scanner dead code, including a second `indexOfId` with different logic.
8. Two copies of `AudioPluginFormatManager` in the plugin host.
9. `instantiateCatalog` copies the whole catalog for one entry and is TOCTOU-prone.
10. CI not updated for CPM: no cache, `submodules: recursive` left over, JUCE/flac/miniaudio pinned
    by tag instead of commit.

## Symptom

(1) memory corruption / crash when the track count grows while audio renders; (2) possible torn
reads of worker output on ARM64; (4) a hosted plugin inside a rack processed off the audio thread;
(5)–(9) wasted RT work and maintenance hazards; (10) slow, unpinned CI dependency fetches.

## Reproduction

1. Start playback with N tracks, add tracks until N exceeds the previous high-water mark — the
   `std::vector` growth in `ensureTrackScratch`/`ensureMidiScratch` frees the buffers the audio
   thread holds. (Not reproduced as a crash here; it is a code-evident race.)
2. Put a VST3/AU inside an Audio Effect Rack on a track: `RackDevice::builtinKind()` is 5, so the
   track is marked eligible and the rack (with its plugin) runs in `pool_.dispatch`.
3. Others: read the code paths below.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine.cpp:584-604` `ensureMidiScratch`/`ensureTrackScratch` —
  grow `blockMidi_`, `blockExpr_`, `blockMidiN_`, `blockExprN_`, `leafWork_`, `leafBuf_` in place
  from `publishRaw` while `processBlock` may be mid-block on the audio thread. `blockMidi_`/`blockExpr_`
  have the same flaw as the two named in the report (outer vector reallocation).
- `src/native/nota.engine/src/engine/AudioThreadPool.h:dispatch()` and `.cpp:workerLoop()` —
  `done_.fetch_add(1, relaxed)`.
- `src/native/nota.engine/src/engine/Engine_Render.cpp:2163-2175` — eligibility uses
  `instrument->kind() < 0` and `device->builtinKind() < 0`; racks (`RackDevice` 5, `RackInstrument`
  3, `DrumRack`) pass even when a chain holds a plugin.
- `Engine_Render.cpp:1777,2310,2385,2476`, `Engine.h:1776-1777` — `stageSilence_`/`stageSilent_`
  written only.
- `AudioThreadPool.{h,cpp}` `reserveScratch`/`workerScratchBuffer`/`scratch_`; `Engine.cpp:25,602`.
- `pluginhost/scanner/OutOfProcessScanner.{h,cpp}` `ScanResult`, `scanOne`;
  `pluginhost/scanner/PluginCatalog.{h,cpp}` `snapshot`, `hasFileOrIdentifier`, free
  `catalog::indexOfId` (suffix match instead of format+name+hex-uid); `bridge/PluginCatalogBridge.h`.
- `pluginhost/scanner/abi_pluginscanner.cpp:formatManager()` vs
  `pluginhost/hosting/PluginCommon.cpp:sharedFormatManager()`.
- `pluginhost/hosting/PluginCommon.cpp:instantiateCatalog` (`catalog.types()` deep-copies every
  description) and `PluginHost.cpp:createPluginInstrument` (`entryAt` then `instantiateCatalog` under
  two separate locks: a scan in between shifts indexes).
- `.github/workflows/ci.yml`, `.github/workflows/release.yml`,
  `src/native/nota.engine/cmake/dependencies.cmake`.

## Root Cause Hypothesis

Confidence: high for all items — each is visible in the code as described.

## Proposed Remediation

**Preferred**:
1. Move the per-block render scratch (leaf work + leaf buffers + MIDI/expression pre-pass buffers)
   into one `RenderScratch` object published through the engine's existing `SnapshotHistory`
   (message thread builds a bigger one with headroom, publishes it before the graph swap; the
   audio thread reads it once per block after loading the graph and ends the block with a
   `RenderScope`; retired scratch is freed two renders later). Nothing the audio thread holds is
   ever reallocated.
2. `done_.fetch_add(1, std::memory_order_release)` in the worker and caller claim loops.
4. Add `virtual bool hostsPlugin() const` to `Device` (default `builtinKind() < 0`) and `Instrument`
   (default `kind() < 0`); racks override it by walking their live snapshot recursively. The
   eligibility check uses it.
5. Remove `stageSilence_`/`stageSilent_` and the four detector passes.
6. Remove the pool's scratch API.
7. Remove the dead scanner code and the JUCE-free bridge it was the only user of.
8. The scanner ABI uses `hosting::sharedFormatManager()`.
9. `PluginCatalog::typeAt(index, out)` copies one description under one lock; instantiation checks
   instrument-ness on that same copy.
10. CI: drop `submodules: recursive`, set `CPM_SOURCE_CACHE` and cache it with `actions/cache`
    keyed on `dependencies.cmake`; pin miniaudio, flac, JUCE by commit SHA (tag in a comment).

**Files likely to change**: `Engine.{h,cpp}`, `Engine_Render.cpp`, `AudioThreadPool.{h,cpp}`,
`devices/Device.h`, `devices/Instrument.h`, `devices/racks/RackCore.h`, `RackDevice.h`,
`RackInstrument.h`, `pluginhost/scanner/*`, `pluginhost/bridge/PluginCatalogBridge.h`,
`pluginhost/hosting/PluginCommon.{h,cpp}`, `PluginHost.cpp`, `cmake/dependencies.cmake`,
`.github/workflows/{ci,release}.yml`.

**Tests to add or update**:
- Throwaway harness: grow the track count repeatedly while a second thread renders (ASan) — no
  errors.
- Pool stress harness from `idle-pool-spin` still passes.
- `scripts/build.sh` smoke test (renders end-to-end, scans plugins, rack round-trips).

## Risks & Considerations

- Scratch headroom costs memory: 256 KB per track slot (kMaxBlock 16384 × 4 floats); growth
  rounds up to multiples of 8 tracks.
- Retired scratch is freed only on the next publish (SnapshotHistory semantics) — one extra copy
  may linger.
- The rack walk runs on the audio thread once per block per rack — reads only, no allocation.
- Not addressed here: pool workers run at normal priority while the audio callback waits for them
  (priority inversion). Needs per-platform priority work (macOS audio workgroups, Windows MMCSS,
  Linux SCHED_FIFO) — follow-up.

## Open Questions

- None blocking.
