# Bug Fix: Folded CC lane gutter

- **Slug**: folded-cc-gutter (from the assessment context)
- **Fixed**: 2026-11-02
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

New CC lane columns use the existing `_rowsArePads` state to select the same gutter
as the note grid at construction. Registration for subsequent fold changes is retained.

## Changes

| File | Change |
| --- | --- |
| `src/managed/Nota.App/PianoRollView.ControllerLanes.cs` | Initialize the current gutter width |
| `specs/071-multiple-cc-lanes/validation/Program.cs` | Add six real Avalonia regression assertions and capture folded layout |
| `CHANGELOG.md` | Record the alignment correction |
| `specs/071-multiple-cc-lanes/validation.md` | Record review follow-up evidence |

## Tests Added or Updated

The probe opens a folded Drum Rack before adding its CC lane, double-clicks beneath
a beat-2 note and checks the saved CC point's beat. It also checks initial alignment,
unchanged-row refresh, unfolding, refolding and removal/readdition.

## Local Verification

Before the fix, `dotnet run --project specs/071-multiple-cc-lanes/validation/CCProbe.csproj
-c Release -- specs/071-multiple-cc-lanes/validation` fails with
`click directly below a folded drum note writes CC at the same beat`.
Matching screenshots are captured before the pointer edit. Post-fix results are in test.md.

## Deviations from Assessment

None. No extension hooks are registered. Nota design uses the existing gutter constants;
the nota-docs audit requires no manual edit because this restores documented alignment.
Capabilities and shortcuts are unchanged.

## Follow-ups

Run the expanded probe and Windows build gate, then push the existing PR branch.
