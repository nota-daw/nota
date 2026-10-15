# Bug Assessment: Linux per-user data split across ~/.config/nota and ~/.config/Nota

- **Slug**: linux-config-dir-case
- **Created**: 2026-10-15
- **Source**: pasted text (user observation)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

> Заметил проблему, что создаются такие директории с файлами: часть в ~/.config/nota, а часть в ~/.config/Nota

(Noticed that directories with files are created in two places: some in `~/.config/nota`,
some in `~/.config/Nota`.)

## Symptom

On Linux Nota keeps its per-user files in two folders. The managed layer writes settings,
logs, recovery and presets to `~/.config/nota`, while the native engine writes `audio.json`,
`midi.json`, `plugins.xml` and `scanpaths.txt` to `~/.config/Nota`. Expected: one folder.
Linux filesystems are case-sensitive, so these really are two different directories.

## Reproduction

1. Run Nota on Linux (no `NOTA_DATA_DIR`).
2. Change the audio device in Preferences, or scan plugins.
3. `ls ~/.config` shows both `nota/` (settings.json, logs/, …) and `Nota/` (audio.json,
   midi.json, plugins.xml, scanpaths.txt).

## Suspected Code Paths

- `src/managed/Nota.Infrastructure/NotaPaths.cs:52`: Linux root is `<XDG_CONFIG_HOME|~/.config>/nota` (lowercase).
- `src/native/nota.engine/src/NotaConfigFile.cpp:102` (`cfgfile::dataDir`): appends `"Nota"` on Linux; used by `AudioConfig_miniaudio.cpp` and `MidiConfig_rtmidi.cpp`.
- `src/native/nota.engine/pluginhost/PluginHost.cpp:146` (`notaSupportDir`): JUCE `userApplicationDataDirectory` (resolves `XDG_CONFIG_HOME` / `~/.config`) + `"Nota"`.
- Comments repeat the split: `NotaConfigFile.h:10` and `PluginHost.cpp:129` say `Nota`, `NotaPaths.cs:9` says `nota`.
- `.github/ISSUE_TEMPLATE/bug_report.yml:69` tells users that Linux logs live in `~/.config/nota/logs/`.

Related: `cfgfile::dataDir` ignores `NOTA_DATA_DIR`, but `NotaPaths` and `notaSupportDir`
honour it. So on Windows and Linux, test runs still write audio.json and midi.json into the
real user folder.

## Root Cause Hypothesis

Both casings go back to the first commit (e5e2a5bf). When the native config layer was
written, the casing was mirrored from Windows/macOS (`Nota`). The managed layer, though,
followed the XDG lowercase convention on Linux. This only shows up on case-sensitive
filesystems. Confidence: high.

## Proposed Remediation

**Preferred**: make lowercase `nota` the single Linux folder (decided with the user). It
matches XDG convention, it already holds most of the data, and it is what the issue template
documents.

- `cfgfile::dataDir()`: honour `NOTA_DATA_DIR` first, like `NotaPaths`. On Linux use
  `<root>/nota`. On Windows keep `%APPDATA%\Nota`.
- One-time migration on Linux: if `<root>/Nota` exists and is not the same directory as
  `<root>/nota`, move each entry into `nota/` unless it is already there. Then remove
  `Nota/` if it is empty. Never overwrite anything, and keep the work best-effort
  (`error_code` overloads). Run it once per process with a static guard.
- `PluginHost.cpp notaSupportDir()`: off macOS, delegate to `nota::cfgfile::dataDir()`
  (its header is already on the pluginhost include path, and the library links into
  `nota_engine`, which compiles `NotaConfigFile.cpp` on Windows and Linux). That leaves one
  resolver for the native config files, with the migration in it.
- Fix the comments in `NotaConfigFile.h`, `PluginHost.cpp` and `NotaPaths.cs`.

**Alternatives**:
- Switch managed to `Nota`. This moves the larger data set and contradicts the published
  issue template. Rejected.
- Skip the migration. Existing Linux users would lose their audio and MIDI device choice and
  their plugin catalog (a rescan). That is cheap to avoid.

**Files likely to change**:
- `src/native/nota.engine/src/NotaConfigFile.cpp`
- `src/native/nota.engine/src/NotaConfigFile.h`
- `src/native/nota.engine/pluginhost/PluginHost.cpp`
- `src/managed/Nota.Infrastructure/NotaPaths.cs` (comment only)
- `CHANGELOG.md` ([Unreleased] → Fixed)

**Tests to add or update**:
- The smoke test runs on macOS, where this code path is not compiled (macOS uses
  `~/Library/Application Support/Nota`). Verification is a Linux build plus a manual check:
  `XDG_CONFIG_HOME=/tmp/x`, seed `/tmp/x/Nota/audio.json`, then start the engine and
  confirm the file moved to `/tmp/x/nota/`.

## Risks & Considerations

- Case-insensitive filesystem on Linux (rare): `Nota` and `nota` are the same directory, so
  the `equivalent()` check must skip the migration.
- A file present in both folders: keep the `nota/` copy and leave the legacy one in place
  (`Nota/` is then not removed). No data loss.
- Windows and macOS paths are unchanged.
- `dataDir()` is called on config load/save (message thread), never on the audio thread.
  Filesystem work there is fine.

## Open Questions

- None.
