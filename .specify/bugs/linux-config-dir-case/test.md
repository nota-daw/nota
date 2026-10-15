# Bug Verification: Linux per-user data split across ~/.config/nota and ~/.config/Nota

- **Slug**: linux-config-dir-case
- **Tested**: 2026-10-15
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

On a real Linux build (Docker, `mcr.microsoft.com/dotnet/sdk:10.0`, arm64), a Nota run that
starts with old engine files in `~/.config/Nota` ends with a single `~/.config/nota`. The
legacy files were moved in with their contents intact, and `~/.config/Nota` was removed. The
Linux and macOS builds and their smoke tests pass, with no regressions.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix, Linux) | Seed `/root/.config/Nota/{audio.json,plugins.xml,scanpaths.txt}`, `NOTA_DATA_DIR` unset, run `scripts/build-linux.sh` (its smoke test drives the engine) | pass | Afterwards only `/root/.config/nota/` exists; it holds the migrated files plus the managed side's `analysis-cache`, `kits`, `grain-sources`, `mosaic-sources`. `scanpaths.txt` still reads `/opt/legacy-vst3` |
| Linux compile of changed TUs | same run | pass | `NotaConfigFile.cpp.o` and `pluginhost/PluginHost.cpp.o` built, no errors/warnings in them |
| Migration edge cases | throwaway harness on a case-sensitive APFS image + default APFS (see fix.md) | pass | no-overwrite on conflict, legacy dir kept when not empty, case-insensitive FS untouched, `NOTA_DATA_DIR` honoured |
| Regression suite (Linux) | `scripts/build-linux.sh` | pass | `SMOKE TEST PASSED` |
| Regression suite (macOS) | `scripts/build.sh` | pass | `SMOKE TEST PASSED` |
| Windows build | — | not-run | No Windows host; the Windows path is unchanged in behaviour (`%APPDATA%\Nota`), but the plugin host now gets it from `cfgfile::dataDir()` instead of JUCE |

## Output Excerpts

```
build rc=0
[33/155] Building CXX object CMakeFiles/nota_engine.dir/src/NotaConfigFile.cpp.o
[37/155] Building CXX object CMakeFiles/nota_pluginhost.dir/pluginhost/PluginHost.cpp.o
SMOKE TEST PASSED
--- ~/.config after the smoke test:
/root/.config/nota/:
analysis-cache  audio.json  grain-sources  kits  mosaic-sources  plugins.xml  scanpaths.txt
scanpaths:
/opt/legacy-vst3
```

## Residual Risks

- Windows was not compiled or run. The plugin catalog path should be the same as before
  (`$APPDATA` vs JUCE's `SHGetSpecialFolderPath(CSIDL_APPDATA)`), but CI should confirm it.
- The first container run hit an OOM in the JUCE compile at default parallelism (8 GB Docker
  VM). This is an environment limit: `CMAKE_BUILD_PARALLEL_LEVEL=4` fixed it. Not related to
  the fix.

## Recommendation

Close the bug: verified end-to-end on Linux. Let Windows CI confirm the plugin-host path
before merging.
