# Bug Fix: OS Documents/Music locations for default folders

- **Slug**: os-folders-defaults
- **Fixed**: 2026-11-05
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Defaults for the Projects/Samples library now resolve through the OS user folders: freedesktop `user-dirs.dirs` on Linux, Known Folders (`MyDocuments`/`MyMusic`) on Windows/macOS, with `~/Documents` / `~/Music` fallback. An explicit `settings.json` value still always wins; directory creation stays best-effort.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.Infrastructure/UserDirs.cs` | added | XDG parser + OS-dispatched resolver, pure/testable parts `internal` |
| `src/managed/Nota.Infrastructure/Settings/SettingsService.cs` | modified | defaults via `UserDirs.GetDocumentsDir()` / `GetMusicDir()`; removed now-unused `Home` |
| `tests/Nota.SmokeTest/UserDirsTests.cs` | added test | 13 checks: parser, disabled dirs, fallbacks, XDG path |
| `tests/Nota.SmokeTest/Program.cs` | modified | `--user-dirs` standalone run + full-run wiring |
| `CHANGELOG.md` | modified | `[Unreleased] Fixed` entry referencing #522 |
| Review follow-up (PR #523) | modified | `Unquote()` decodes `\"` `\\` `\$` `` \` `` inside quotes (P2); test expectations built with `Path.Combine` so they hold on Windows (P1); +2 checks (15 total) |

## Diff Highlights (optional)

- `SettingsService.ResolvedProjectsFolder()` default changed from `Path.Combine(Home, "Documents", "Nota Projects")` to `Path.Combine(UserDirs.GetDocumentsDir(), "Nota Projects")` (same shape for samples).
- Disabled XDG dir (`XDG_*_DIR="$HOME"`) resolves to `$HOME`, so the final default is `$HOME/Nota Projects` instead of recreating `~/Documents`.

## Tests Added or Updated

- `UserDirsTests.Run` — quoted `$HOME` expansion, absolute path with spaces, missing key → null, disabled `$HOME`/`$HOME/` → home, unquoted value, Linux hit/fallback/disabled, Known-Folder hit/fallback, `user-dirs.dirs` path default + `XDG_CONFIG_HOME` override.

## Local Verification

- Commands run: `dotnet build src/managed/Nota.Infrastructure/Nota.Infrastructure.csproj` → success (1 pre-existing nullable warning in `LanguageCatalog.cs`, unrelated).
- `dotnet run --project tests/Nota.SmokeTest -- --user-dirs` → `USER DIRS PASSED` (13/13).
- `dotnet run --project tests/Nota.SmokeTest -- --linux-desktop` → `LINUX DESKTOP PASSED` (no regression in neighbouring area).
- Full `scripts/build-linux.sh` not run (heavy native+full-smoke); recommended before PR.

## Deviations from Assessment

None. Open question on disabled dirs resolved as assessed: base returns `$HOME`, giving `$HOME/Nota Projects` / `$HOME/Nota Samples`.

## Follow-ups

- Run `scripts/build-linux.sh` (full smoke) before opening the PR; target `develop`, `Closes #522`.
- Consider `nota-docs`/`nota-features` only if maintainers deem the changed default location user-visible enough (CHANGELOG entry already added).
