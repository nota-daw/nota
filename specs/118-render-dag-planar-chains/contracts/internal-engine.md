# Internal Engine Contracts: Phase 5 (`specs/118-render-dag-planar-chains`)

## 1. Lock-Free Cooperative Sub-Task Contract
- Inside `AudioThreadPool`, workers or the audio caller inside `dispatch()` MUST NEVER invoke `dispatch()` reentrantly (`epoch_` / `doneBase_` are single-publisher owned).
- All intra-rack and intra-synth sub-tasks route exclusively through `dsp::RenderExecutor::parallelFor()`.
- When called from a leaf running directly on the audio thread (`m == 1` eligible leaf in `mixGraph()`), `RenderExecutor` uses `dispatch()` to wake available workers (when `allowParallel` is true); when called inside a pool task (`m > 1`), `RenderExecutor` claims the single lock-free `SubSlot` via CAS (falling back inline if busy so workers never spin waiting for sub-task submission).
- Light real-time playback (`!offline && !rtParallel_`) sets `allowParallelSubTasks = false`, guaranteeing workers remain parked (`0.0%`–`< 2%` CPU).

## 2. Deterministic IEEE-754 Reduction Order (`0` vs `N` Threads == `0.0`)
- Every parallel pass (leaves, group depth/sidechain waves, return waves, multi-chain racks, and polyphonic voice batches) MUST base the decision to accumulate per-slot/per-batch buffers strictly on deterministic scene/DSP state (e.g., bus eligibility, active chain count `>= 2`, active voice count `>= threshold && frames >= minFrames`), NEVER on `workerCount()` or runtime worker availability.
- Accumulation across buffers MUST always walk slots/batches in strictly ascending canonical order `0, 1, ..., K-1`.
- Consequently, `RenderThreads == 0` and `RenderThreads == N` execute the exact same sequence of floating-point additions (`maxdiff == 0.0` verbatim).

## 3. Contiguous Planar Device Chain Contract (`AudioBufferView` / `PluginEffect` / `PluginInstrument`)
- Devices defaulting to `prefersPlanar() == false` continue receiving interleaved stereo `float* buf` via `process(buf, frames)`.
- When a `PluginInstrument` (`prefersPlanar()`) feeds adjacent `PluginEffect` (`prefersPlanar()`) inserts (and `toPreFxTap` is not needed, OR pre-FX tap is copied from planar before effects), `processPlanar()` keeps audio in `PlanarAudioSpan` until an interleaved device, PDC delay line, or fader/tap boundary converts cleanly back to interleaved `float*`.
- `PluginEffect::processPlanar(PlanarAudioSpan io, const float* scInterleaved, int32_t scFrames, int32_t frames)` preserves exact `SilenceGate` semantics (waking on non-silent planar input OR sidechain, sleeping identically).

## 4. macOS AudioUnit Parallel Execution Contract
- `hosting::isParallelRenderSafe(const juce::AudioPluginInstance&)` checks format `"VST3"` or `"AudioUnit"` (macOS) against explicit thread-safety denylist rules (shared-state Apple utility/network AUs, explicit blacklist identifiers, or diagnostic env opt-out `NOTA_SERIAL_AUDIOUNIT=1`).
- Instances opting into `parallelRender_` execute exclusively on at most one thread per block (`AudioThreadPool` worker or caller — never reentrant); parameter writes remain single-atomic and editor GUI work remains message-thread only.
