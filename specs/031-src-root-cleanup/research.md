# Research: `src/` Root Cleanup (spec 031)

Two open questions must be answered by measurement before P3/P4. Fill in the verdicts during T001/T002.

## RQ-001: Which `Oversampler.h` do the effects actually get?

**Context**: `devices/effects/{Amp,Vintage,Forge}.h` write `#include "Oversampler.h"`. Two candidates exist: `src/Oversampler.h` (flat legacy) and `src/dsp/primitives/Oversampler.h` (030 primitive). Quoted-include resolution order: (1) including file's directory (`devices/effects/` — no match), (2) `-I` dirs in CMake order.

**To determine**:
1. List `target_include_directories` order for `nota_engine` from `src/native/nota.engine/CMakeLists.txt` — whichever of `src/` vs `src/dsp/primitives/` (if the latter is even listed) comes first wins.
2. Compare the two headers' APIs (class names, method sets, oversampling ratios, filter topology). If both define the same class/methods with different bodies → active ODR violation today (two TUs could mix them).
3. Check `dsp/primitives/Oversampler.cpp` — it includes its sibling (same-dir-first, unambiguous).

**Verdict (T001)**: UNIFY ON THE PRIMITIVE. No ODR violation today (different qualifications: flat `nota::Oversampler` vs primitive `nota::dsp::Oversampler`), but two implementations of the same HIIR oversampler must not coexist — `Amp`/`Vintage`/`Forge` resolve to the flat via `-I` order (`src/` precedes everything; `src/dsp/primitives/` is not on the path, so same-dir-first saves only the primitive's own TU). Migration = include + qualify + per-rate null-test (T007).

## RQ-002: Flat vs primitive `CompensationDelay` — behavior delta

**Context**: `engine/Track.h:267` holds `std::shared_ptr<CompensationDelay>` from `src/CompensationDelay.h`. The primitive twin (`dsp/primitives/CompensationDelay.h`, T032) adds `isIdle()`.

**To determine** (side-by-side diff):
1. Read-index/wrap semantics of `process()` (any off-by-one vs `DelayLine`-style taps?).
2. `ready_`/`delay_` atomic protocol (store order, acquire/release pairing).
3. `cap_` sizing and `pos_` advance; behavior when `d <= 0`, `!ready`, null `buf`.
4. Whether the primitive's `isIdle()` (`!ready || delay <= 0`) exactly matches the flat's early-return conditions (if yes, engine-level idle checks become available for free).

**Verdict (T002)**: DROP-IN MIGRATION. The primitive is a verbatim move of the flat (identical `setDelay`/`process` bodies, same atomics, same `kCap`) plus added `isIdle()`, whose condition (`!ready || delay <= 0`) exactly matches the flat's early-return conditions — so engine-level idle checks become available for free. Migration = include + qualify `dsp::` in `Track::pdc` + null-test (impulse + tail, several delays incl. 0/unprepared).

## Notes carried from 030

- T041 proved read-index equivalence must be tested, never assumed (`orig.read(d) == DelayLine.readLinear(d-1)`).
- T051 deliberately left Track on the flat PDC (engine-level skip would save two loads; migration deferred here).
- `PluginHostBridge.h` move needs `pluginhost/` on the engine include path — verify no other target breaks (scanner INTERFACE target must stay JUCE-clean per the T012–T014 enforcement comment in CMakeLists).
