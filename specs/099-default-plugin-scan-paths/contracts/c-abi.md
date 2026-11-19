# Contracts: Default scan-path C ABI

**Feature**: [spec.md](../spec.md) · pure C, AR-7 (no C++ types, no
exceptions unwind — errors as return codes, NULL for missing strings).

New functions in `src/native/nota.engine/include/nota/nota_engine.h`,
next to `nota_pluginhost_scan_path*`. String results follow the existing
convention: owned by the engine, valid until the next call on the same
thread.

## `nota_pluginhost_default_scan_path_count`

```c
int32_t nota_pluginhost_default_scan_path_count(const char* format);
```

Number of OS-standard default folders for `format` (`"VST3"` today).
Unknown or NULL format → 0. Never negative.

## `nota_pluginhost_default_scan_path`

```c
const char* nota_pluginhost_default_scan_path(const char* format, int32_t index);
```

Absolute UTF-8 path of default `index` for `format`, or NULL when out of
range / unknown format. Absolute means: never `~`-relative, never empty.

## `nota_pluginhost_restore_default_scan_paths`

```c
int32_t nota_pluginhost_restore_default_scan_paths(const char* format);
```

Appends the missing OS defaults for `format` to the persisted list;
existing entries (defaults and user-added) are untouched and keep their
order. Clears the restored paths from the removed-set, so they stay.
Returns how many were added, 0 when complete or format unknown.

## `nota_pluginhost_is_scan_path_removed`

```c
int32_t nota_pluginhost_is_scan_path_removed(const char* dir);
```

1 when `dir` is in the user-removed set (backfill skips it), else 0
(NULL `dir` → 0). Lets the app tell an explicit removal apart from
"never had it": the startup ensure skips removed folders, while
"Restore defaults" (an explicit add) still brings them back.

## Managed counterparts (same semantics, existing patterns)

- `NativeMethods.PluginHost{DefaultScanPathCount, DefaultScanPath, RestoreDefaultScanPaths}`
  (`LibraryImport`, UTF-8 marshalling)
- `NotaEngine.{DefaultScanPathCount, DefaultScanPath, RestoreDefaultScanPaths}`
  with the `Marshal.PtrToStringUTF8` pattern
- `IPluginCatalog` gains `DefaultScanPathCount(string format)`,
  `DefaultScanPath(string format, int index)`,
  `RestoreDefaultScanPaths(string format)`; test fakes in
  `tests/Nota.SmokeTest` (`BrowserTestSupport.cs`, `PaletteTests.cs`) gain
  no-op members so they still compile
- The Nota store folder is NOT part of this contract — it stays
  app-owned (`PluginScan.EnsureStoreScanPath`); "Restore defaults" in the
  UI calls both

## Contract tests (smoke)

- Count ≥ 1 for `"VST3"`; every returned path absolute and unique.
- Unknown format (`"CLAP"` today): count 0, restore 0, path NULL.
- Remove-then-restore roundtrip preserves user paths, no duplicates.
- Double restore is a no-op (second call returns 0).
