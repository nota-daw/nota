# Bug Fix: dbus errors spam syslog

- **Slug**: linux-ibus-log-spam
- **Fixed**: 2026-10-17
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Added a filtering `ILogSink` wrapper installed right after `LogToTrace()` in
`Program.BuildAvaloniaApp()`. It drops the known-benign
`Error while destroying the context` IME errors — the D-Bus `UnknownMethod`/`UnknownObject`
replies some ibus builds give when Avalonia disposes an input context — so they no longer
reach stderr and through it the system log. Everything else is forwarded untouched.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/ImeLogNoiseFilter.cs` | added | `FilteringSink` (`ILogSink` decorator) + `WithImeLogNoiseFiltered()` `AppBuilder` extension |
| `src/managed/Nota.App/Program.cs` | modified | builder chain now ends `.LogToTrace().WithImeLogNoiseFiltered()` |
| `CHANGELOG.md` | modified | `[Unreleased]` → `Fixed` entry referencing nota-daw/nota#36 |

## Diff Highlights

```csharp
// Program.cs — install the filter right after the sink it wraps
    .LogToTrace()
    .WithImeLogNoiseFiltered();

// ImeLogNoiseFilter.cs — the suppression predicate (everything else passes through)
private static bool IsBenignNoise(string area, string? message) =>
    area == ImeArea
    && message is not null
    && message.StartsWith(DestroyContextMessage, StringComparison.Ordinal)
    && (message.Contains("org.freedesktop.DBus.Error.UnknownMethod", StringComparison.Ordinal)
        || message.Contains("org.freedesktop.DBus.Error.UnknownObject", StringComparison.Ordinal));
```

## Tests Added or Updated

- None in-repo: `tests/` holds only the engine smoke test, and no managed unit-test project
  exists (as the assessment noted). Locked in via an ad-hoc check instead — below.

## Local Verification

- Commands run: `dotnet build src/managed/Nota.App/Nota.App.csproj` → 0 errors
  (11 warnings, all pre-existing in unrelated files).
- Ad-hoc filter check — scratch console project (`/tmp/imefilter-check`) referencing
  `Nota.App.csproj`, installing the **compiled** `FilteringSink` over a recording sink and
  logging through the real `Avalonia.Logging.Logger.TryGet(...)?.Log(...)` path, exactly the
  way Avalonia's IME code logs:
  - the verbatim `UnknownMethod`/`Destroy` spam from the issue → **dropped**
  - the `UnknownObject` "context already gone" twin → **dropped**
  - a real IME failure ("Unable to create IME input context", `ServiceUnknown`) → forwarded
  - the same message under a different log area → forwarded
  - an IME destroy failure with a non-benign D-Bus error (`AccessDenied`) → forwarded
  - result: exactly 3 of 5 forwarded, in order, all assertions PASS, exit 0
- `scripts/build.sh` → full pipeline green: native engine (universal), 
  `dotnet build Nota.sln -c Release`, `SMOKE TEST PASSED`.
- Manual checks: none possible on macOS — the IBus path only exists on Linux/X11; verified
  at the logging API boundary instead.

## Deviations from Assessment

None. The assessment's optional `UnknownObject` coverage was included (same benign
destroy path, locale-independent error name).

## Follow-ups

- Upstream is still unfixed (AvaloniaUI/Avalonia#15551). If a future Avalonia rewords the
  message, the filter silently stops matching and the old behavior returns (fail-open) —
  the code comment links the upstream issue for discoverability.
- A Linux user report confirming the syslog is now quiet would close the loop on
  nota-daw/nota#36.
- If more managed UI-layer logic accumulates, consider a managed unit-test project; the
  filter would be its first tenant.
