# Bug Fix: Linux per-user data split across ~/.config/nota and ~/.config/Nota

- **Slug**: linux-config-dir-case
- **Fixed**: 2026-10-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The native engine now uses the same lowercase `nota` folder on Linux as the managed layer.
On first use it moves what an earlier build left in `Nota/` into `nota/`, never
overwriting. The plugin host now takes its folder from the same resolver. `cfgfile::dataDir`
now honours `NOTA_DATA_DIR`.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/NotaConfigFile.cpp` | modified | `NOTA_DATA_DIR` override; Linux folder `nota`; one-time `migrateLegacyDir(<root>/Nota → <root>/nota)` behind a static guard |
| `src/native/nota.engine/src/NotaConfigFile.h` | modified | comment: Linux path, `NOTA_DATA_DIR`, plugin catalog lives here |
| `src/native/nota.engine/pluginhost/PluginHost.cpp` | modified | off macOS, `notaSupportDir()` returns `cfgfile::dataDir()` (include guarded by `__APPLE__`, since `JUCE_MAC` isn't defined before the JUCE headers) |
| `src/managed/Nota.Infrastructure/NotaPaths.cs` | modified | comment only |
| `CHANGELOG.md` | modified | [Unreleased] → Fixed entry |

## Diff Highlights

```cpp
// NotaConfigFile.cpp — Linux
std::filesystem::path dir = root / "nota";
...
static const bool migrated = (migrateLegacyDir(root / "Nota", dir), true);
```

`migrateLegacyDir` skips the move when the two paths are the same directory
(`std::filesystem::equivalent`, case-insensitive FS). It renames each entry only if the
target is absent, then calls `remove(legacy)`, which succeeds only when the folder is empty.

## Tests Added or Updated

- No automated test. The code path is compiled only on Windows/Linux, and the smoke test
  runs on macOS, where the data dir is `~/Library/Application Support/Nota`. Checked with
  a throwaway harness instead (below).

## Local Verification

- A throwaway harness (`dataDir()` printed, built with clang++ -std=c++20 -Wall -Wextra
  against `NotaConfigFile.cpp`, which takes the non-Windows branch on macOS):
  - Case-sensitive APFS image, `XDG_CONFIG_HOME` set:
    - Both folders present: `audio.json` and `plugins.xml` moved to `nota/`.
      `nota/settings.json` was kept; the legacy `settings.json` stayed in `Nota/`.
    - Only `Nota/` present: files moved and `Nota/` removed.
  - Case-insensitive APFS: the `equivalent()` guard skipped the move and the files stayed
    in place.
  - `NOTA_DATA_DIR` set: that folder was returned and created.
- `scripts/build.sh` (macOS) → exit 0, SMOKE TEST PASSED (the macOS PluginHost.cpp branch compiles; Windows/Linux branches not compiled here).

## Deviations from Assessment

None.

## Follow-ups

- Run a Linux build (`scripts/build-linux.sh` or CI) to compile the `PluginHost.cpp` branch
  that does not compile on macOS.
