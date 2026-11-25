# Data Model: Phase 5 (`specs/118-render-dag-planar-chains`)

## 1. `AudioThreadPool` Cooperative Sub-Slot & Cache Alignment (`engine/AudioThreadPool.h/.cpp`)
- **`struct alignas(64) JobSlot`**: Prevents false sharing across adjacent generation slots.
- **Cooperative Sub-Slot (`alignas(64) SubSlot subSlot_`)**:
  - `std::atomic<uint64_t> subState_{0}` packing `[gen:32][publishing:1][active:1][refCount:30]`.
  - `RunFn = void (*)(void* ctx, int index)`, `void* ctx`, `std::atomic<int> nextIdx`, `std::atomic<int> done`, `int total`.
  - `tryHelpSubTask()` called while waiting on `done_ < need` in `dispatch()` and inside `workerLoop()` before spinning down (`spins`). Single atomic relaxed check returns immediately (`false`) when `subState_` has `kActive == 0`.

## 2. `dsp::RenderExecutor` (`dsp/core/RenderExecutor.h`)
- Pure JUCE-free, lock-free thread-local scoped bridge:
  - `struct Host { void* obj; bool (*isInTopDispatch)(void*); void (*dispatchTop)(void*, int, void(*)(void*, int), void*); bool (*tryRunSub)(void*, int, void(*)(void*, int), void*); };`
  - `ScopedRenderExecutor(Host host, bool allowParallelSubTasks)`: binds thread-local active executor during `mixGraph()` and worker task execution (`AudioThreadPool::dispatch`).
  - `RenderExecutor::parallelFor(int count, bool eligible, Fn&& fn)`:
    - Executes inline `for (0..count)` when `!eligible || count <= 1 || !current()`.
    - When `isInTopDispatch`: delegates to `tryRunSub` (runs concurrently with idle finishers, zero outer `epoch_` conflict).
    - When NOT `isInTopDispatch` (single-track top-level caller): delegates to `dispatchTop` (wakes workers immediately up to allowed headroom).

## 3. `RenderGraphExecutor` (`engine/RenderGraphExecutor.h/.cpp`) & `Engine::RenderScratch`
- **Aligned `LeafWork`**: `struct alignas(64) LeafWork` (`Engine.h`).
- **`RenderScratch` Extensions** (`Engine.h` / `Engine.cpp`, published off-RT):
  - `groupScratch` (`kMaxGroups * frameCapacity * 2` floats), `returnScratch` (`kMaxReturns * frameCapacity * 2` floats): exclusive per-bus scratch buffers for parallel Group & Return chain execution.
  - `groupWork` (`std::array<GroupWork, kMaxGroups>`) & `returnWork` (`std::array<ReturnWork, kMaxReturns>`).
  - `PlanarStereoBuffer` pool (`leafPlanar` per thread / preallocated `kMaxPlanarSlots` or per worker slot + caller scratch `callerPlanar_`) sized `frameCapacity` frames per channel on `ensureRenderScratch()`.
- **Wave Scheduler**:
  - Builds intra-depth group sidechain dependency levels (`depth` descending `maxDepth..1`, topological sidechain pass within each depth `kMaxGroups`) and return sidechain dependency levels.
  - Runs eligible buses in parallel per wave, reduces in strict ascending track index order (`maxdiff == 0.0`).
  - Runs `processDeviceChain(devices, pdc, interleavedBuf, planarScratch, ...)` detecting contiguous `prefersPlanar()` stretches (`PluginEffect`, optionally `PluginInstrument::renderPlanar` at head) cleanly via `PlanarStereoBuffer`.

## 4. `dsp::PlanarStereoBuffer` (`dsp/core/AudioBufferView.h`)
- Non-RT resizable aligned planar L/R buffer (`ensureFrames(int32_t)`, `left()`, `right()`, `span(int32_t frames) -> PlanarAudioSpan`), plus helpers:
  - `deinterleave(const float* interleaved, int32_t frames)`
  - `interleaveTo(float* interleaved, int32_t frames)`
  - `addToInterleaved(float* dst, int32_t frames)` (for `PluginInstrument::renderPlanar` fast hand-off).

## 5. `dsp::ChainScratchBank` (`dsp/core/ScratchArena.h`) & `dsp::PolyVoiceScheduler` (`dsp/poly/PolyVoiceScheduler.h`)
- **`ChainScratchBank`**: Dynamically preallocated array of `S` aligned stereo buffers (`frameCap_ * 2` floats each), grown on non-RT rack commit / `ensureBlockCapacity()` when active chain count reaches `>= 2`.
- **`PolyVoiceScheduler`**: Fixed upper bound `kMaxBatches = 8`, holds active voice indices `int activeIdx[MaxVoices]`, preallocated oversampled/base scratch banks (`left_[kMaxBatches]`, `right_[kMaxBatches]`, sized on `prepare(maxFrames)`), partitions `activeCount` deterministically across `numBatches = std::min(kMaxBatches, activeCount)`, runs batches via `RenderExecutor::parallelFor`, and accumulates `0..numBatches-1` in strict index order into destination buffers.
