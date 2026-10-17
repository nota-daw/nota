# Bug Verification: dbus errors spam syslog

- **Slug**: linux-ibus-log-spam
- **Tested**: 2026-10-17
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial — every runnable check passes and the filter provably drops the exact
  message from the issue at the logging boundary, but the end-to-end reproduction
  (Nota running on Linux with the affected ibus daemon, watching the system log) could not
  be exercised on this macOS machine. Downgraded from `verified` for that reason alone.

## Summary

The fix holds everywhere it can be observed here: the verbatim syslog message from
nota-daw/nota#36 is dropped by the production `FilteringSink`, benign and non-benign
messages are correctly separated, and the full build + smoke-test pipeline is green.
No regressions found; final confirmation needs one run on an affected Linux + IBus setup.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix), automated equivalent | scratch project `/tmp/imefilter-check`: compiled `FilteringSink` + real `Logger.TryGet(...)?.Log(...)` path, 5 messages | pass | verbatim issue message and its `UnknownObject` twin dropped; real IME error, other-area message, non-benign destroy error forwarded — exactly 3 of 5, in order |
| Reproduction (post-fix), end-to-end | Nota on Linux + IBus, watch `journalctl`/syslog | not-run | no Linux/IBus environment on this machine; needs a confirmation run on an affected setup |
| New / updated tests | none in-repo | not-run | no managed unit-test project exists (recorded in fix.md); the ad-hoc check above stands in |
| Regression suite | `./scripts/build.sh` (after all source changes) | pass | native engine + `dotnet build Nota.sln -c Release` + `SMOKE TEST PASSED` |
| Lint / type-check | via `dotnet build` (no separate lint configured) | pass | 0 errors; 11 warnings, all pre-existing in unrelated files |

## Output Excerpts

```
forwarded 3 messages (expected 3)
PASS forwarded[0] = 'IME: Unable to create IME input con'
PASS forwarded[1] = 'FreeDesktopPlatform: Error while destroying the con'
PASS forwarded[2] = 'IME: Error while destroying the con'
```

```
SMOKE TEST PASSED
==> Done. Run the app with: dotnet run --project src/managed/Nota.App
```

## Residual Risks

- No run on an actually affected Linux + IBus machine — the syslog being quiet end-to-end
  is proven by construction (the message the journal captured is dropped before the sink),
  not by observation.
- Upstream message drift: if Avalonia rewords `"Error while destroying the context:"`, the
  filter stops matching and the spam returns (fail-open, no crash).
- Only the destroy-context path is filtered; the separate `"Error:\n"` queue path seen in
  AvaloniaUI/Avalonia#19917 reports is deliberately still logged — it can carry real
  failures.

## Recommendation

Ship it: the code-level verification is complete and nothing runnable fails. Before closing
nota-daw/nota#36, have the reporter (or any Linux + IBus user) run a build with this fix
and confirm the syslog stays clean — that single data point upgrades this to `verified`.
