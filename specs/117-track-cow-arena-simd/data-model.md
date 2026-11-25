# Data Model: Phase 4 (`specs/117-track-cow-arena-simd`)

## 1. `nota::CowSlice<T>` (`src/native/nota.engine/src/engine/TrackContent.h`)
- **Fields**:
  - `std::shared_ptr<const std::vector<T>> data_` — immutable shared storage copied across snapshots (`cloneTrack()`).
  - `std::shared_ptr<std::vector<T>> mutable_` — non-null only while a freshly cloned authoring `Track` is actively mutating before `seal()`. Cleared on copy/move and in `seal()`.
- **Operations**:
  - `const std::vector<T>& read() const noexcept` (returns static empty vector when null)
  - `bool empty() const noexcept`, `size_t size() const noexcept`, `const T& operator[](size_t) const`, `begin()/end()/data()`
  - `const void* storageToken() const noexcept` (`data_ ? data_->data() : nullptr` — or `data_.get()`)
  - `bool sharesStorageWith(const CowSlice<T>& other) const noexcept`
  - `std::vector<T>& write()` — allocates new vector or clones `*data_` if `!mutable_ || data_.use_count() > 1`
  - `void seal() noexcept` — drops `mutable_` when publishing snapshot.

## 2. `Track` Containers (`Track.h`) & `TimelineIndex` Match Guard (`TimelineIndex.h`)
- On `Track`:
  - `CowSlice<AudioClip> clips;`
  - `CowSlice<AudioTake> takes;`
  - `CowSlice<MidiClip> midiClips;`
  - `CowSlice<SessionSlot> sessionSlots;`
  - `CowSlice<TrackLane> automation;`
  - `CowSlice<Modulator> modulators;`
  - `CowSlice<CvLink> cvLinks;`
  - Helper `void sealContent() noexcept` called at publish time.
- On `TimelineIndex`:
  - Tracks `(clipsStorage_, midiClipsStorage_, artTableStorage_)` tokens from build time so `Engine::publishRaw()` reuses existing `timelineIndex` in $O(1)$ across non-clip edits while automatically rebuilding whenever `clips`, `midiClips`, or `articulationTable` backing pointers change.

## 3. `nota::dsp::StereoScratchArena<N>` (`src/native/nota.engine/src/dsp/core/ScratchArena.h`)
- `static constexpr int32_t kDefaultLiveFrames = 2048;`
- Holds `N` contiguous aligned stereo buffers (`frameCap_ * 2` floats each, 64-byte / 16-byte aligned).
- `void prepare(int32_t maxFrames)` — pre-allocates `std::max(frameCap_, std::clamp(maxFrames, kDefaultLiveFrames, kMaxFrames))` safely outside RT callback.
- `float* channelBuf(size_t slot)` — zero-overhead pointer for `RackDevice` (`slot 0=input, 1=acc, 2=scratch`) and `RackInstrument` (`slot 0=scratch`).
- Same session frame budget applied to `Engine::ensureRenderScratch(tracks, minFrameCap)`.

## 4. `Eq` Cached Band Descriptor (`Eq.h` / `Eq.cpp`)
- Per-band cache struct `BandCache`:
  - Inputs: `double sr = -1.0; float freq = 0, gain = 0, q = 0, scale = -1; int type = -1, slope = -1, ch = -1;`
  - Outputs: `double logF = 0, logQ = 0; dsp::BiquadCoeffs coeffs[kMaxSec]; int nSec = 0; bool valid = false;`
- `StereoBiquadKernel`: flat-stereo vectorized `double2` runner processing `(L, R)` together through flattened `z1/z2` registers.

## 5. `nota::simd::CpuCaps` & `SimdDispatch` (`SimdDispatch.h/.cpp`, `SimdAvx2.cpp`)
- `struct CpuCaps { bool sse2; bool avx2; bool fma; bool neon; };`
- Probed once via `std::call_once` at startup using CPUID (`__x86_64__` / `_M_X64`) or compile-time NEON (`__aarch64__`).
- Dispatch function pointers (`add`, `addScaled`, `scale`, `mul`, `maxAbs`, `peakAndSumSquares`) bound once at startup to AVX2+FMA (x86_64 when supported), SSE2/FMA, or NEON kernels.
