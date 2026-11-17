# Data Model: Default plugin scan paths

**Feature**: [spec.md](spec.md) · **Research**: [research.md](research.md)

## Entities

### Default scan folder

A folder Nota searches for plug-ins without the user adding it.

- `format`: plug-in format it applies to (`"VST3"` today; `"CLAP"`, `"LV2"` later)
- `path`: absolute folder path, UTF-8, OS-native separators
- `origin`: `os-standard` (from the format's standard locations) or
  `store` (Nota's own install folder)

**Rules**:

- The (`format`, `path`) pair identifies a default.
- Shown even when the folder does not exist or is unreadable.
- A path present both as default and user-added appears once, as a default.

### Scan folder list

The ordered set of folders searched for plug-ins (persisted as today).

- Ordered, unique by path (case-sensitive compare; Windows paths come
  from one source so casing is stable).
- Mixes defaults and user-added folders; order of existing entries never
  changes on backfill/restore — missing defaults append at the end.

## State transitions

```text
fresh profile      → seeded with OS defaults (+ store ensured app-side)
upgrade (old list) → missing defaults appended once, customs untouched
remove default     → entry absent; restore re-appends it
restore (complete) → no-op
```

Backfill and restore are idempotent: applying them twice changes nothing
the second time.
