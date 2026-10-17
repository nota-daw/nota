# Bug Assessment: dbus errors spam syslog

- **Slug**: linux-ibus-log-spam
- **Created**: 2026-10-17
- **Source**: https://github.com/nota-daw/nota/issues/36 (fetched; host `github.com`, policy branch: `allowlisted`)
- **Verdict**: valid
- **Severity**: low (log noise only — no functional impact; but unbounded syslog/journal spam on affected Linux setups)

## Report (verbatim or summarized)

Issue title: "dbus errors spam syslog". Linux Mint 22.3 Cinnamon, Nota 0.53.1 built from
source; "running nota after compiling everything works as expected, but the syslog has
lots of entries like the following":

```
2026-10-14T19:56:38.876857+02:00 xxxxxxx Nota.App: [IME] Error while destroying the context:#012Tmds.DBus.Protocol.DBusException: org.freedesktop.DBus.Error.UnknownMethod: Method Destroy is not implemented on interface org.freedesktop.IBus.Service#012   at Tmds.DBus.Protocol.InnerConnection.MyValueTaskSource`1...#012   at Avalonia.FreeDesktop.DBusIme.DBusTextInputMethodBase.Dispose() (IBusX11TextInputMethod #22714424)
```

## Symptom

On Linux systems whose IBus daemon does not implement `Destroy` on
`org.freedesktop.IBus.Service` for portal-created input contexts, every dispose of an
Avalonia per-window IME client (each popup, tooltip and focus change gets one) logs a
full `Tmds.DBus.Protocol.DBusException` stack trace at Error level. Nota forwards
Avalonia logs to stderr via `.LogToTrace()`, and the desktop session journal captures
stderr — so the system log fills with repeated benign errors. Expected: no recurring
benign errors in the system log.

## Reproduction

1. Run Nota on a Linux desktop using IBus with the affected daemon build
   (reporter: Linux Mint 22.3 Cinnamon).
2. Use the app normally — open menus/popups/tooltips, move focus around.
3. Watch `journalctl` / syslog accumulate `[IME] Error while destroying the context:`
   entries.

[NEEDS CLARIFICATION: exact ibus daemon version/portal configuration that triggers the
`UnknownMethod` reply — not all distros are affected. The fix does not depend on it.]

## Suspected Code Paths

- `src/managed/Nota.App/Program.cs:26` — `.LogToTrace()` installs Avalonia's
  `TraceLogSink` as `Avalonia.Logging.Logger.Sink`; Error-level events go to stderr →
  journald.
- Third-party, outside this repo (verified against Avalonia master, 2026-10-17):
  - `src/Avalonia.FreeDesktop/DBusIme/IBus/IBusX11TextInputMethod.cs` —
    `DisconnectAsync() => _service?.DestroyAsync()` is the failing D-Bus call.
  - `src/Avalonia.FreeDesktop/DBusIme/DBusTextInputMethodBase.cs` — `Dispose()` and
    `QueueOnErrorAsync()` catch and log `"Error while destroying the context:\n" + ex`
    at `LogEventLevel.Error`, area `"IME"`, embedding the whole exception into the
    message template.
  - Tracked upstream: [AvaloniaUI/Avalonia#15551](https://github.com/AvaloniaUI/Avalonia/issues/15551)
    (open) and [#19917](https://github.com/AvaloniaUI/Avalonia/issues/19917) (closed as
    duplicate). Unfixed in master as of 12.1.4 — a package bump alone does not fix it.

## Root Cause Hypothesis

Avalonia bug, surfaced by Nota's default logging config. Avalonia creates a D-Bus IME
input context per X11 window; on teardown it calls `Destroy` on
`org.freedesktop.IBus.Service`, which some ibus builds (portal-created contexts) do not
implement, replying `org.freedesktop.DBus.Error.UnknownMethod`. Avalonia treats that as
an Error and logs the full stack every time; Nota's `.LogToTrace()` sink passes it to
stderr, which the user session's journal records as syslog entries. Confidence: high
(matching upstream reports + source inspection of Avalonia 12.1.4 / master).

## Proposed Remediation

**Preferred**: install a filtering log sink in `Program.BuildAvaloniaApp()`, right
after `.LogToTrace()`: read `Avalonia.Logging.Logger.Sink` and replace it with a small
delegating `ILogSink` that suppresses the known-benign IME dispose noise — area
`"IME"` AND message contains `"Error while destroying the context:"` AND the embedded
D-Bus error name is `org.freedesktop.DBus.Error.UnknownMethod` (optionally also
`...UnknownObject`, the "context already gone" twin seen in upstream reports) — and
forwards everything else verbatim to the wrapped sink. `ILogSink` is a 3-member
interface (`IsEnabled` + two `Log` overloads); the exception text lives inside the
message template, so a cheap substring check suffices. The pattern can never match on
Windows/macOS, so the filter is inert there.

**Alternatives**:
- Drop all `"IME"`-area events (e.g. exclude the area from `LogToTrace`) — too broad;
  hides genuine failures ("Unable to create IME input context", "WatchNameOwner ...
  failed").
- Wait for an upstream Avalonia fix — this branch already bumps 12.0.3 → 12.1.4 and the
  bug is still in master; nothing to pick up.
- Disable the D-Bus IME (fall back to XIM) — regresses text input for IME users;
  unacceptable.

**Files likely to change**:
- `src/managed/Nota.App/Program.cs` (wrap the sink in the builder chain)
- `src/managed/Nota.App/<new file>` for the filtering sink (following the repo's
  one-class-per-file layout, AGPL header)
- `CHANGELOG.md` [Unreleased] Fixed entry

**Tests to add or update**:
- No managed unit-test project exists in the repo (`tests/` holds only the engine smoke
  test). Lock the fix in via an ad-hoc check in `/speckit-bug-test`: feed captured
  benign and non-benign message templates through the filter and assert drop/forward.
  Revisit a managed test project if one lands in the repo.

## Risks & Considerations

- Over-filtering: a future genuine destroy failure matching the pattern would be
  silenced. Mitigated by the narrow match (message prefix + specific D-Bus error names,
  `UnknownMethod` scoped to the destroy path).
- Ordering: the wrapper must be installed after `LogToTrace()` sets the sink. Doing it
  as the next fluent step in the same builder chain makes the order explicit; anything
  logging between the two calls is forwarded unfiltered (harmless).
- Upstream drift: if Avalonia rewords the message, the filter silently stops matching
  and the old behavior returns (fail-open). A code comment should link the upstream
  issue so the intent is discoverable.
- Observability: suppressed events are gone entirely rather than downgraded. Acceptable
  for a message with zero diagnostic value; note it in the changelog entry.

## Open Questions

- [NEEDS CLARIFICATION: which ibus builds/versions reply `UnknownMethod` for
  `org.freedesktop.IBus.Service.Destroy` — affects how many users see the spam, not the
  fix itself.]
