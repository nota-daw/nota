# Research: Default plugin scan paths

**Feature**: [spec.md](spec.md) (issue nota-daw/nota#622, approved)

## R1 — Where do the OS-standard VST3 locations come from?

**Decision**: Native asks the JUCE VST3 format for its default search path
at runtime (`AudioPluginFormat::getDefaultLocationsToSearch()`), instead of
a hardcoded per-OS table.

**Rationale**: The scanner already searches exactly those locations
(`pluginhost/scanner/abi_pluginscanner.cpp` line 162), so the listed
defaults can never drift from what is actually scanned. A table would rot
on every JUCE update.

**Alternatives considered**: Hardcoded `%ProgramFiles%/Common Files/VST3`,
`/Library/Audio/Plug-Ins/VST3`, `~/.vst3` etc. — rejected (duplicates JUCE
knowledge, rots silently).

## R2 — How is CLAP/LV2 groundwork shaped without implementing them?

**Decision**: The provider is keyed by format name (`"VST3"` today). Adding a
format later means one new provider entry (its JUCE format's default
locations); the backfill / remove / restore / list behavior is
format-agnostic from day one.

**Rationale**: The scan loop is already per-format (`kFormatOrder` in
`abi_pluginscanner.cpp`); keying defaults the same way fits the existing
seam. No CLAP/LV2 code in this feature (FR-010).

## R3 — Shape of the new C ABI

**Decision**: Three functions next to the existing scan-path ABI, pure C
(AR-7), string results in thread-local buffers like the existing
`nota_pluginhost_scan_path`:

- `nota_pluginhost_default_scan_path_count(const char* format)`
- `nota_pluginhost_default_scan_path(const char* format, int32_t index)`
- `nota_pluginhost_restore_default_scan_paths(const char* format)` → re-adds
  missing OS defaults, returns how many were added

Unknown/unsupported format → count 0, restore 0, path NULL. See
[contracts/c-abi.md](contracts/c-abi.md).

## R4 — Where does backfill happen?

**Decision**: Inside `PluginCatalog::loadScanPathsOnce()`, which already
holds the unique lock: after loading `scanpaths.txt`, append missing OS
defaults for known formats and save only when something was added.

**Rationale**: Single choke point — every entry (scan, UI, tests) loads
through it, so fresh and upgraded profiles converge without a second
code path. The store folder is still ensured managed-side by the existing
`PluginScan.EnsureStoreScanPath` (native must not learn the store layout).

## R5 — "Restore defaults" semantics

**Decision**: Additive only — re-adds missing defaults (OS via native
restore, store via the existing ensure), never deletes or reorders
user-added entries. No-op when nothing is missing.

**Rationale**: Matches the agreed UX (removable defaults with a safe way
back) and keeps the operation idempotent and undo-free.

## R6 — UI approach

**Decision**: `PreferencesWindow.PluginsPane` tags defaults via the new
count/path API (no hardcoded list in C#), keeps Remove enabled for them,
adds a "Restore defaults" button, and lists missing folders as-is. One
caption string is reworded (`extra_folders_are_searched_for`).

## R7 — How are tests kept OS-independent?

**Decision**: Smoke tests assert shape, not literal paths: defaults
non-empty, contain no duplicates, backfill/restore idempotent, user paths
preserved. Exact OS strings are JUCE's domain and vary per platform.

## R8 — Removal persists across restarts (PR #623 review)

**Decision**: Native persists user-removed scan paths in
`scanpaths-removed.txt` (one path per line, next to `scanpaths.txt`).

**Rationale**: `scanPathsLoaded_` is per-process, so a plain backfill would
re-add removed defaults on every launch. The removed-set makes "removed"
sticky: backfill skips listed paths; an explicit `addScanPath` (Add folder,
store ensure via restore) clears the entry; `restore` clears every path it
re-adds. Recording applies to any removed path (uniform — no format check
at record time); the set is only ever consulted for defaults during
backfill. The store folder consults the same set through a new query ABI
(`nota_pluginhost_is_scan_path_removed`), so the managed startup ensure
stops re-adding a removed store folder while restore still brings it back.

**Alternatives considered**: A "backfill done" marker file — rejected: it
can't tell "removed by the user" from "never backfilled", so it breaks
restore-after-remove and multi-format backfill ordering.

## Open verifications (build time, not clarifications)

- JUCE VST3 defaults resolve to absolute paths on all three OSes (asserted
  by the no-tilde / non-empty smoke check).
- `scripts/build.sh` green (native + managed + smoke).
