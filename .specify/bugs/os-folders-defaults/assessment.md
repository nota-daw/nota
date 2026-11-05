# Bug Assessment: OS Documents/Music locations ignored for default folders

- **Slug**: os-folders-defaults
- **Created**: 2026-11-05
- **Source**: https://github.com/nota-daw/nota/issues/522 (host: github.com, allowlisted; fetch permitted without prompting)
- **Verdict**: valid
- **Severity**: medium (wrong default browser/save locations on relocated/translated Documents/Music; creates unwanted `~/Documents`; no data loss or crash, but affects every Linux user with custom XDG user-dirs and Windows users with redirected Known Folders)

## Report (verbatim or summarized)

> **Respect OS Documents/Music locations for default Projects/Samples folders** — Nota hardcodes `~/Documents/Nota Projects` (`SettingsService.ResolvedProjectsFolder`) and `~/Music/Nota Samples`, ignoring XDG user-dirs on Linux and Known Folders on Windows. With relocated/translated Documents this creates an unwanted `~/Documents`.
>
> Expected: resolve via `XDG_DOCUMENTS_DIR` / `XDG_MUSIC_DIR` on Linux, `MyDocuments`/`MyMusic` on Windows/macOS, with `~/Documents` fallback; existing `settings.json` override always wins.

Source URL: https://github.com/nota-daw/nota/issues/522

## Symptom

On Linux with a relocated or translated Documents folder (e.g. `~/Dokumenty`), and on Windows with redirected Documents/Music (OneDrive, custom location), Nota still browses/suggests `~/Documents/Nota Projects` and `~/Music/Nota Samples`, creating the hardcoded directory. Expected: defaults follow the OS locations; an explicit `settings.json` `ProjectsFolder`/`SamplesFolder` always wins.

## Reproduction

1. On Linux, set `XDG_DOCUMENTS_DIR="$HOME/Dokumenty"` in `~/.config/user-dirs.dirs` (or use a system with translated user dirs).
2. Ensure `settings.json` has empty `ProjectsFolder`/`SamplesFolder` (defaults).
3. Start Nota, open browser Files/Projects tab or Save dialog (`MainWindow.Files.cs`).
4. Observed: root/suggested location is `~/Documents/Nota Projects`, and `~/Documents` gets created. Expected: `~/Dokumenty/Nota Projects`.

[NEEDS CLARIFICATION: exact behavior when `XDG_*_DIR` is disabled (`"$HOME"`) — fallback should be `$HOME/Nota Projects`, not recreating `Documents`. To confirm with reporter/maintainer.]

## Suspected Code Paths

- `src/managed/Nota.Infrastructure/Settings/SettingsService.cs:45-51` — `ResolvedSamplesFolder()` / `ResolvedProjectsFolder()` do `Path.Combine(Home, "Music", "Nota Samples")` and `Path.Combine(Home, "Documents", "Nota Projects")` when the setting is empty, then `EnsureDir()`. This is the root.
- `src/managed/Nota.Infrastructure/NotaPaths.cs:21` — `Home => Environment.GetFolderPath(UserProfile)` (`~`); no XDG user-dirs or Known-Folder lookup anywhere.
- `src/managed/Nota.Application/Settings.cs:37-40` — `SamplesFolder`/`ProjectsFolder` empty-means-default contract; unchanged by fix.
- Callers that inherit the bug (no change needed, they use the resolvers): `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs:633,704,906`, `src/managed/Nota.App/MainWindow.Files.cs:37,231,387`, `src/managed/Nota.App/PreferencesWindow.cs:946,949`, `src/managed/Nota.Infrastructure/Mosaic/MosaicLibrary.cs:32,162`, `src/managed/Nota.Infrastructure/SampleStore/SampleStore.cs:38`.

Grep confirms no existing `XDG_DOCUMENTS`, `user-dirs.dirs`, `xdg-user-dir`, or `SpecialFolder.MyDocuments`/`MyMusic` handling for these defaults.

## Root Cause Hypothesis

Defaults are hardcoded to English `~/Documents` / `~/Music` instead of resolving the OS user folders. Confidence: **high** — directly visible in `SettingsService.cs:45-51`; Linux XDG user-dirs and Windows Known Folders are never consulted. `.NET Environment.SpecialFolder.MyDocuments` alone is not sufficient as the fix without verification on Linux, hence an explicit `user-dirs.dirs` parser is proposed.

## Proposed Remediation

**Preferred**: add a small testable resolver in `Nota.Infrastructure` (next to `NotaPaths`), used by both `Resolved*Folder()` methods:

- Linux: parse `$XDG_CONFIG_HOME/user-dirs.dirs`, fallback `~/.config/user-dirs.dirs`; keys `XDG_DOCUMENTS_DIR` / `XDG_MUSIC_DIR`; handle quotes, `$HOME/...`, absolute paths, missing file, and disabled (`"$HOME"` → `$HOME/<fallback-name>`); final fallback `~/Documents` / `~/Music`. No shell-out to `xdg-user-dir` (deterministic, testable, no process spawn on UI path).
- Windows/macOS: `Environment.SpecialFolder.MyDocuments` / `MyMusic` (respects redirection/OneDrive), fallback to `Home/Documents` / `Home/Music` when empty.
- Keep contract: non-empty `settings.json` value always wins; keep `EnsureDir()` best-effort creation; no migration of existing `~/Documents/Nota Projects`.
- Shape: e.g. `UserDirs.GetDocumentsDir(Home)` / `GetMusicDir(Home)` with injectable env/file readers for tests; `SettingsService` appends `Nota Projects` / `Nota Samples`.

**Alternatives**:
- Use only `Environment.SpecialFolder.MyDocuments/MyMusic` everywhere: smaller, but relies on undocumented .NET-on-Linux XDG mapping; less deterministic for tests.
- Shell out to `xdg-user-dir DOCUMENTS/MUSIC`: reuses freedesktop logic, but adds process dependency and failure modes on startup/UI path.

**Files likely to change**:
- `src/managed/Nota.Infrastructure/Settings/SettingsService.cs`
- `src/managed/Nota.Infrastructure/NotaPaths.cs` (or new `UserDirs.cs` next to it)
- `tests/Nota.SmokeTest/*` (new resolver tests; existing `SampleStoreTests.cs:178-179` fakes remain valid)

**Tests to add or update**:
- Parser: quoted values, `$HOME` expansion, absolute path, missing file, disabled (`$HOME`), `XDG_CONFIG_HOME` override.
- Resolver: Linux XDG hit, fallback to `~/Documents`/`~/Music`; Windows `MyDocuments` path when available.
- Existing smoke test must stay green (`scripts/build-linux.sh` / `scripts/build.sh`).

## Risks & Considerations

- Creating directories stays best-effort; permission failures must remain swallowed (existing behavior).
- Disabled Documents (`XDG_DOCUMENTS_DIR="$HOME"`) must not recreate `~/Documents` — use `$HOME/Nota Projects`.
- Localized names with spaces/Unicode must survive parsing and `Path.Combine`.
- No audio-thread impact (settings/UI path only); no C ABI change; no new dependency (AGPL hygiene clean).
- User-visible default path change needs `CHANGELOG.md [Unreleased]` entry; `nota-docs`/`nota-features` only if maintainers deem the default-location change user-visible enough.

## Open Questions

- [NEEDS CLARIFICATION: disabled-dir fallback — confirm `$HOME/Nota Projects` vs `~/Documents/Nota Projects` when `XDG_DOCUMENTS_DIR="$HOME"`.]
- [NEEDS CLARIFICATION: should Windows also migrate existing custom-folder users, or only affect fresh defaults? Presumed defaults-only.]
