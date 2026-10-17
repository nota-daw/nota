# Bug Verification: a tempo set over MCP never reaches the transport bar, and the stale value is saved

- **Slug**: mcp-tempo-ui-sync
- **Tested**: 2026-10-18
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

A tempo set over MCP now reaches the BPM field, is saved with the project and comes back on
reopen. Dragging the field doesn't snap back. The new smoke checks pass with the fix and
fail without it, and the full smoke test passes.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | Local build, tempo driven over MCP `set_tempo` from a hardware knob (a Maschine Mikro MK2 through a HID→MCP bridge) | pass | Reporter: "knob updated the field" |
| Save keeps the MCP tempo | Set 97 BPM from the knob, save `test.nota`, read `project.json` | pass | `"bpm": 97` in the saved file |
| Reopen keeps it | Relaunch, project reopens, MCP `get_transport` | pass | `"bpm":97`; reporter: "saved tempo loaded correctly" |
| No snap-back mid-drag | Drag the BPM field quickly back and forth | pass | Reporter: "seemed stable" |
| New / updated tests | `dotnet run --project tests/Nota.SmokeTest -c Release` (`-- tempo follow --`) | pass | 9/9 |
| Tests catch the bug | Same, against `develop`'s `TransportViewModel.cs` | fail (expected) | 5 follow checks fail |
| Tests catch the race | Same, against a naive follow (no in-flight guard) | fail (expected) | 4 checks fail: snap-back mid-drag, rounded echo |
| Regression suite | full smoke test | pass | `SMOKE TEST PASSED` |
| Build | `dotnet build Nota.sln -c Release -nodeReuse:false -m:1` | pass | 0 errors, no new warnings |

## Output Excerpts

```
-- tempo follow --
  [PASS] an outside tempo change reaches the BPM field
  [PASS] …rounded to the field's hundredths
  [PASS] …without echoing the rounded value back to the engine
  [PASS] consecutive outside changes are each followed
  [PASS] a tick before the engine applies the field's tempo keeps it
  [PASS] …and once applied, field and engine agree
  [PASS] a drag ahead of the engine keeps the latest value
  [PASS] …and lands on it
  [PASS] outside changes are followed again after the field's own lands
SMOKE TEST PASSED
```

## Residual Risks

- The 1 s in-flight timeout isn't covered by a test (that would need a 1 s wait in the
  smoke run). It only matters when no audio thread drains engine commands.
- Tested on Linux only (Ubuntu 26.04 x64). The change is platform-neutral view-model code.
- Other transport settings set over MCP (time signature, metronome, master volume, loop)
  still don't reach the view-model. They're out of scope (see the assessment, *Related*).

## Recommendation

Close the bug. It's verified end-to-end on Linux, and the smoke run now guards against
regressions.
