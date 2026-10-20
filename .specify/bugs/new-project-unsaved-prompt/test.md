# Bug Verification: New project (Ctrl+N) discards unsaved changes and leaves a blank detail panel

- **Slug**: new-project-unsaved-prompt
- **Tested**: 2026-10-20
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The reproduction from the issue no longer reproduces. New with unsaved edits asks first,
Cancel keeps the project, and the detail panel closes fully. Opening from the browser and
Save from the prompt work too. The build and smoke test pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | Headless harness `/tmp/newprojharness`: real `MainWindow`, add a track, open Devices panel, fire `OnMenuNew` | pass | "New project" prompt; Cancel keeps 5 tracks + dirty; Don't save → template, row 0 px, splitter hidden |
| Clean project | `OnMenuNew` with no edits | pass | no prompt |
| Popped-out detail window | `OnToggleDetailWindow`, edit, New → Don't save | pass | window shows track 1's chain, not the old one |
| Browser open | `OpenProjectConfirmedAsync(<saved bundle>)` while dirty | pass | "Open project" prompt; Cancel keeps the current project |
| Save from prompt (named) | New on a named dirty project, Enter (= Save) in the prompt | pass | saved in place, then New; reopening the bundle restores the saved 5 tracks |
| Regression suite | `scripts/build.sh` (native + managed build, smoke test) | pass | SMOKE TEST PASSED |
| Lint / type-check | C# compiler as part of the build | pass | no new warnings in changed files |

## Output Excerpts

```
New #1: prompt=New project
after Cancel: tracks=5 (was 5) dirty=True panel=True splitter=True row=305 actual=305
after Don't save: tracks=4 dirty=False panel=False splitter=False row=0 actual=0
floating after: window=True chainTrack=1 firstTrack=1 …
browser open: prompt=Open project
after Save: dialogOpen=False tracks=4 dirty=False path=null
reopen saved: prompt=False tracks=5 (expect 5)
```

## Residual Risks

- Save from the prompt on an **untitled** project (closes the prompt, then the native file
  panel) was not exercised — the headless platform has no file panel. The same sequence was
  the quit prompt's existing behaviour.
- File → Open goes through the native folder picker before the prompt. Not driven headless.
  It uses the same `ConfirmLeaveChangesAsync` call as the browser path.
- Verified headless, not on a real display with the Ctrl+N key itself. The shortcut maps to
  `file.new` → `OnMenuNew`, which was called directly.

## Recommendation

Close the bug. It is verified end-to-end through the real window code. A quick manual
Ctrl+N → Save on an untitled project before release would cover the file-panel path.
