# Bug Assessment: Curved automation segments jump almost instantly (ease-out is a near-step)

- **Slug**: automation-curve-step
- **Created**: 2026-11-12
- **Source**: https://github.com/nota-daw/nota/issues/591 (host: github.com, allowlisted; written by the reporter in this session)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

After bending an automation segment, the ease-out direction draws as a near-vertical drop then
a flat line, and playback jumps the same way. Ease-in looks smooth. Expected: both directions
smooth and mirror-symmetric, no jump at either end.

## Symptom

A bent segment with positive curve reaches almost its whole change at the very start of the
segment — audibly a step — while a negative curve looks fine.

## Reproduction

1. Add an automation lane (track volume) with points low → high → low.
2. Drag the middle of the falling segment to bend it.
3. The segment drops vertically right after the peak, then stays flat; playback matches.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Automation.h:52` — `AutomationLane::shape`: `t^e`,
  `e = 2^(−4·curve)` ∈ [1/16, 16]. Used by every lane, clip envelope and controller curve.
- Managed mirrors: `ArrangementView.cs:3564` (`Shape`), `ClipEditorView.cs:398`,
  `AudioClipEditorView.cs:979` (`EnvShape`), `PianoRollView.Controllers.cs:257`,
  `Nota.Application/Editing/ControllerStroke.cs:164`, plus the oracle in
  `tests/Nota.SmokeTest/ControllerStrokeTests.cs`.
- Bend-handle inversions (`0.5^e = frac`): `ArrangementView.AutoEditor.cs:222`,
  `ClipEditorView.cs:496`, `AudioClipEditorView.cs:1325`.
- Segment slicing that relied on the power curve being self-similar:
  `ControllerStroke.Apply` (left piece keeps its curve), `sliceCtlPoints`
  (`MidiController.cpp`), the controller-take fold in `Engine_Render.cpp`.

## Root Cause Hypothesis

For `e < 1` the power curve has an infinite slope at t = 0: at `curve = 1` the value is half
way by t ≈ 2·10⁻⁵ — effectively a step in the engine as well as on screen. The curve family is
also asymmetric (ease-in is gentle, ease-out is a cliff). The 16-step polyline makes it look
worse (≈84 % of the change in the first step). Confidence: high.

## Proposed Remediation

**Preferred**: replace the shape with a symmetric exponential,
`shape(t, c) = expm1(k·t) / expm1(k)`, `k = −c·K` (K = 8). Slopes are finite at both ends,
ease-in and ease-out mirror each other, and the bend handle inverts in closed form
(`shape(½) = 1/(e^{k/2}+1)` ⇒ `k = 2·ln(1/frac − 1)`). Because exponentials are
shift-invariant, any sub-range [t0, t1] of a segment is exactly the same family with
`c·(t1 − t0)`: slicing becomes exact (`ControllerStroke` no longer needs its fit search, and
native slicing scales the curve instead of keeping it). Put the managed math in one shared
helper; draw curved segments with pixel-based sampling.

**Alternatives**:
- Keep the power curve but narrow the exponent range — still an infinite slope at t = 0 for
  every ease-out, so the step only softens.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Automation.h`, `Engine_Automation.cpp` (self-test),
  `MidiController.cpp`, `Engine_Render.cpp`
- `src/managed/Nota.Application/Editing/AutomationCurve.cs` (new), `ControllerStroke.cs`
- `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.AutoEditor.cs`,
  `ClipEditorView.cs`, `AudioClipEditorView.cs`, `PianoRollView.Controllers.cs`
- `tests/Nota.SmokeTest/ControllerStrokeTests.cs`

**Tests to add or update**:
- Native self-test: at `curve = 1`, t = 0.01 reads well under 0.1 (no jump); ±1 mirror.
- Managed: shape matches the native formula; midpoint inversion round-trips; slicing a
  shaped segment keeps its values on both sides of a stroke.

## Risks & Considerations

- Saved projects keep their curve numbers but sound gentler at the extremes (the old ±1 was a
  near-step). Worth a CHANGELOG note.
- `std::expm1` on the audio thread costs about what the two `pow` calls did.

## Open Questions

- None.
