# Manual index

`manual-index.json.gz` is the user manual (nota-daw/nota-docs) cut into sections for the
Assistant panel's search and Show me (specs/098). It is generated — never edit it by hand:

```bash
scripts/build-manual-index.sh ../nota-docs
```

Rebuild it whenever the manual changes for a release (the `release` skill's `prepare` phase
does). The smoke test (`--assistant`) checks every Show me anchor in it against the app.
