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
