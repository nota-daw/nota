# Bug Verification: MIDI controller slice

- **Slug**: issue-1072
- **Tested**: 2026-11-25
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

`dotnet run --project tests/Nota.SmokeTest -c Release -- --midi-ctl` passed the reported Duplicate Time case with values 0.375, 0.500 and 0.875, and the native slice self-test. Desktop interaction, curved controllers and undo/redo remain to be checked.
