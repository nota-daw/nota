# Bug Fix: The render pool runs away to every thread under a mild overload

- **Slug**: render-pool-sizing
- **Fixed**: 2026-11-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Changes

| File | Change |
|------|--------|
| `src/native/nota.engine/src/engine/Engine.h` | `sizeWorkNs_` (the work the pool is sized for), `overrunShare_` |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | helpers sized from `max(leafWorkNs_, sizeWorkNs_)`; the parallel phase is timed; `sizeWorkNs_` grows ×1.25 when the smoothed share of overrunning phases passes 1/5 (capped at `max(parWorkNs_, leafWorkNs_)`), eases toward `leafWorkNs_` while phases take under half the buffer, and starts at the measured cost when the pool joins. Leaving the pool (`parWorkNs_`) is unchanged |

## Review follow-up (2026-11-16): the callback's serial work

Greptile (PR #619): the phase was compared with the whole buffer, but the callback still owes
its serial work after it (tracks that can't go to the pool, groups, returns, the master). With
the leaves at 75 % and that work at 30 % every callback is late while no phase "overruns", so
the sizing never grew.

| File | Change |
|------|--------|
| `src/native/nota.engine/src/engine/Engine.h` | `phaseNs_` (this callback's parallel phase), `serialNs_` (the rest, smoothed over 16 callbacks), `kOverrunShare` = 0.05, `adjustPoolSizing` |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | `render()` times the callback and hands it to `adjustPoolSizing`, which learns the serial work and grows the sizing on callbacks that were late with the phase over its room (period − serial work, at least a quarter of it); a late callback whose serial work alone missed the deadline doesn't count. `mixGraph` joins, leaves and sizes by that room instead of the whole period |
