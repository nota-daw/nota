# Bug Fix: Curved automation segments jump almost instantly

- **Slug**: automation-curve-step
- **Fixed**: 2026-11-12
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The segment shape is now `expm1(k·t)/expm1(k)` with `k = −curve·8`, in the engine
(`AutomationLane::shape`) and in one shared managed helper (`AutomationCurve`) that replaces
five copies. Slopes are finite at both ends and ±curve mirror each other. The shape is
shift-invariant, so a piece [t0, t1] of a segment is exactly the same shape with
`curve·(t1 − t0)`. Every place that cuts a curved segment now scales the curve that way
(`AutomationLane::subCurve` / `pieceCurve`, `AutomationCurve.Sub`). Before, those places relied
on the power curve being self-similar at the start of a segment, or laid the cut as straight
pieces, or as a fitted curve. The bend handles invert in closed form, and curves are drawn
with about one step every 3 px instead of a fixed 12–16.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Automation.h/.cpp` | modified | New `shape`; `kCurveSteepness`, `subCurve`, `pieceCurve`. |
| `src/native/nota.engine/src/engine/Engine_Automation.cpp` | modified | `automationCurveSelfTest`: no jump at the steep end, ease-in mirrors ease-out, a cut piece reads the same values. |
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | `appendLaneFrom` is one exact point, not 32 straight pieces. `endSegmentAt` shortens the last point before a cut (`laneRemap`, `laneDuplicate`, `laneInsertGap`). After an inserted gap (Insert Silence / Duplicate Time), the resumed point carries the rest of the segment through the cut (review follow-up). `laneSliceExact` and `stitchEnvelopes` scale interior/edge curves. |
| `src/native/nota.engine/src/engine/MidiController.h/.cpp` | modified | `ctlPieceCurve`. `sliceCtlPoints` keeps the shape at both edges (the start used to go linear). |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | A controller take shortens the curve of the point before its range, and where it hands back to the old curve (`closeCtlRange`) the hand-back point carries the rest of the old segment (review follow-up). |
| `src/managed/Nota.Application/Editing/AutomationCurve.cs` | added | `Shape`, `Lerp`, `FromMidpoint`, `Sub`, `DrawSteps`. |
| `src/managed/Nota.Application/Editing/ControllerStroke.cs` | modified | The left piece is shortened exactly, and the right remainder is exact. The 41-candidate fit search and recursive splitting are removed. |
| `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.AutoEditor.cs`, `ClipEditorView.cs`, `AudioClipEditorView.cs`, `PianoRollView.Controllers.cs` | modified | Use `AutomationCurve`. Bend via `FromMidpoint` (handle range 1–99 % so ±1 is reachable). Pixel-based curve steps. |
| `tests/Nota.SmokeTest/ControllerStrokeTests.cs` | modified | New oracle. Both sides of a stroke are exact (< 1e-4). Shape checks: no jump, mirror, midpoint round-trip, managed = engine formula. |
| `tests/Nota.SmokeTest/SectionRearrangeTests.cs` | modified | The value oracle uses the new shape. |
| `tests/Nota.SmokeTest/Program.cs` | modified | Insert Silence and Duplicate Time keep a curved segment on both sides of the gap. |
| `src/native/nota.engine/src/engine/Engine_Automation.cpp` (`midiCtlSelfTest`) | modified | 9: a controller take over a curved segment keeps the old shape before it and after the hand-back. 10: a slice of curved controller segments matches at both edges. |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry. |

## Diff Highlights

```cpp
static double shape(double t, float curve) {
    const double k = -static_cast<double>(curve) * kCurveSteepness;
    if (std::fabs(k) < 1e-6) return t;
    return std::expm1(k * t) / std::expm1(k);
}
```

## Risks

- Saved curves keep their numbers but sound gentler at the extremes (`curve = 1` reaches
  50 % at t ≈ 0.09 instead of 0.00002). This is noted in the CHANGELOG.
- Audio-thread cost: two `expm1` calls replace two `pow` calls.
