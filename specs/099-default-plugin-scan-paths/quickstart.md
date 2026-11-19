# Quickstart: Default plugin scan paths

Validates issue nota-daw/nota#622 end to end. Run from the repo root on a
machine with no dependency on the developer's own plug-ins — point the
data dir at scratch space (`NOTA_DATA_DIR=<dir under /tmp>`), like the
existing catalog smoke tests do.

## 1. Build + smoke

```sh
scripts/build.sh   # or build-linux.sh / build-win.ps1 per OS
```

Must exit green (native + managed + smoke test).

## 2. Fresh profile lists defaults

```sh
NOTA_DATA_DIR=/tmp/nota-fresh <app>   # open Settings → Plug-ins
```

Expect: the OS VST3 locations plus the Nota store folder, each badged
`Default` — including any location that doesn't exist on disk.

## 3. Upgrade backfills once

Start from a pre-feature `scanpaths.txt` (custom folders + store), launch,
reopen Settings → Plug-ins: missing defaults appended exactly once.
Relaunch: list unchanged.

## 4. Remove → restore roundtrip

Remove one default, add a custom folder, press "Restore defaults":
the default returns, the custom folder stays, nothing duplicated.
Press again: no change. Quit Nota and start it again: the removed
default (if not restored) is still gone — removals persist across
restarts; only "Restore defaults" (or Add folder…) brings one back.
Removing the Downloads folder works the same way: the startup ensure
skips it until restored.

## 5. Scan unchanged

Rescan and confirm the catalog holds the same plug-ins as before for the
same on-disk state (defaults were always searched; only their listing
is new).
