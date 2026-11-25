# Internal Engine Contracts: Phase 4 (`specs/117-track-cow-arena-simd`)

## 1. Track Copy-on-Write Ownership Rules
- No published `Track` (reachable via `authoring_`, `graphHist_`, `undoStack_`, `redoStack_`) may have non-null `CowSlice::mutable_`. `publishRaw()` invokes `t->sealContent()` on every track in `g`.
- Every `Track` copy inside `Engine::cloneTrack()` copies `CowSlice` `data_` pointers only (`mutable_` starts null) and preserves `t.timelineIndex`.
- Whenever any engine mutation modifies a vector (`clips.write()`, `takes.write()`, `midiClips.write()`, `sessionSlots.write()`, `automation.write()`, `modulators.write()`, `cvLinks.write()`), `CowSlice::write()` guarantees isolation from every live snapshot.

## 2. Audio-Thread Memory & Arena Preparation Contract
- Hardware device callbacks (`Engine::render`) operate strictly with `numFrames <= backend_->bufferFrames()` which is bounded below `kDefaultLiveFrames` (`2048`) or pre-sized in `Engine::start()` -> `propagateSampleRate(sr, maxBlock)`!
- `Engine::renderOffline(out, frames)` ensures `ensureRenderScratch(tracks, frames)` and `ensureScratchCapacity(frames)` BEFORE invoking `processBlock()` / worker pool tasks when `frames` exceeds the currently prepared frame capacity (`preparedMaxBlock_`).
- `RackDevice::process` / `RackInstrument::render` NEVER allocate on live audio blocks (`frames <= arena_.capacityFrames()`).

## 3. Eq Scalar Parity Contract
- Cached `BiquadCoeffs` yield bit-identical coefficients to uncached `buildBand` for identical inputs.
- Flat-stereo `double2` SIMD biquad loop executes identically ordered DF2T multiply-adds per channel (`b0*x + z1`, `b1*x - a1*y + z2`, `b2*x - a2*y`), staying within `< -240 dBFS` (far below the `-120 dBFS` gate) of the scalar reference while guaranteeing `diff == 0.0` between `0` and `N` worker threads.
