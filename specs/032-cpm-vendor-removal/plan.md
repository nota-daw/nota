# Plan: Replace `vendor/` Snapshots with Pinned CPM Dependencies (spec 032)

## Summary

Delete `src/native/nota.engine/vendor/` (9 snapshots, ~104 MB) by mapping each onto a pinned CPM fetch in `cmake/dependencies.cmake`, following the established `nlohmann_json` 3.12.0 precedent (URL+HASH tarball, tests off). Header-only deps wire include dirs only; CMake deps build in-tree; LAME keeps our thin wrapper (moved to `cmake/`); JUCE stays single-source via shallow fetch. License rows/texts move to `LICENSES/`.

## Nota: Read Before Planning

CONTRIBUTING dependency policy applies (check it before adding sources); AGPL SPDX headers on new files (`cmake/*.cmake` included — the lame wrapper already carries one); AR-7 C ABI frozen; `./scripts/build.sh` green + smoke PASS before each commit; `git commit --no-gpg-sign`; spec folder untracked until merge.

Rules carried from 030/031:
- Never float on `main`/`master`: URL+HASH tarballs preferred; GIT_TAG only with a full commit hash where no tarball exists.
- Equivalence first for ear-critical paths: re-run the 031 oversampler null harness (1x–8x) against fetched hiir before deleting the snapshot.
- Mechanical moves stay byte-identical (`git mv` where possible); behavior deltas only via null-gated swaps.

## Technical Context

Current inventory (`src/native/nota.engine/vendor/`, ~104 MB) and target mapping:

| Snapshot | Size | License | Target | Mechanism |
|---|---|---|---|---|
| `dr_libs` (dr_wav/flac/mp3 headers) | 1.1 M | Public-domain/MIT-0 | `mackron/dr_libs` headers | CPM GIT_TAG (+hash), include dir only |
| `miniaudio` | 3.9 M | MIT | `mackron/miniaudio` tag | CPM, header only (miniaudio.h) — verify single-header layout at pin time |
| `rtmidi` (RtMidi.cpp + CMake) | 200 K | MIT | `thestk/rtmidi` tag | CPM, upstream CMake, tests/examples off |
| `flac` (own CMake, built static per current file) | 5.8 M | BSD | `xiph/flac` tag | CPM, keep current BUILD_*=OFF options |
| `lame` 3.100 + our thin `CMakeLists` (static, LGPL note 7) | 1.1 M | LGPL-2.0-or-later | SourceForge 3.100 tarball URL+HASH + wrapper moved to `cmake/lame.cmake` | Same sources/linkage/license note, new fetch path |
| `hiir` (headers, WTFPL) | 60 K | WTFPL | `aldson/hiir` @ pin | CPM, headers only; preserve LICENSE.txt into `LICENSES/` |
| `signalsmith-linear` + `signalsmith-stretch` | 208+64 K | verify (MIT/Unlicense?) | upstream repo(s) — P0 verifies one repo vs two | CPM headers only |
| `JUCE` 8.0.14 | 92 M | AGPLv3 (as used) | `juce-framework/JUCE` tag `8.0.14`, `GIT_SHALLOW TRUE` | Same `juce_add_plugin` flow; verify Pentad VST3 + scan smoke |

Current wiring to unwind: `vendor/rtmidi/RtMidi.cpp` in sources; `add_subdirectory(vendor/flac|lame|JUCE)`; include dirs `vendor/dr_libs` + `vendor` (Signalsmith/miniaudio/RtMidi); BUILD_*=OFF block for flac; juceaide via JUCE subdir. P0 greps `scripts/` + docs for further `vendor/` references.

Pinned versions must be read off the CURRENT snapshots first (version headers/changelogs) so the swap is version-neutral; upgrades are follow-ups, not part of this spec.

Offline story: `CPM_SOURCE_CACHE` documented in `BUILD.md`; CI pre-warms it; cold-cache-no-network fails fast with the existing nlohmann message (kept, extended to all deps).

## Constitution Check

- No new runtime deps beyond 1:1 snapshot replacements; no JUCE outside `pluginhost/` (unchanged); licenses preserved per-dep (no GPL taint beyond today's AGPL/LGPL posture — JUCE AGPL and lame LGPL postures explicitly unchanged); no C ABI change. Proceed.

## Project Structure

### Documentation (this feature)

- `specs/032-cpm-vendor-removal/spec.md`, `plan.md` (this file), `tasks.md`, `research.md` (P0 verdicts: signalsmith repo layout, per-dep pinned versions read off snapshots, lame/JUCE fetch mechanics).

### Source Code Changes

- `cmake/dependencies.cmake` (+9 CPM blocks), `cmake/lame.cmake` (new wrapper, moved thin build), `src/native/nota.engine/CMakeLists.txt` (wire-up, remove vendor paths), `LICENSES/third-party.md` + texts, `BUILD.md` (cache story). `vendor/` deleted last.

## Complexity Tracking

| Item | Why risky | Mitigation |
|---|---|---|
| JUCE fetch (92 MB, juceaide, plugin targets) | Slow/brittle fetch breaks every build | Shallow tag fetch; verify default + Pentad-VST3 + scan smoke before deleting snapshot |
| LAME wrapper move | No upstream CMake; SourceForge reliability | URL+HASH tarball (integrity even if mirror changes); wrapper diffed against current thin file |
| hiir/signalsmith numeric identity | Ear-critical DSP paths | Re-run 031 null harnesses against fetched headers before delete |
| Offline CI | Cold cache + no network = red CI | Pre-warm step + documented `CPM_SOURCE_CACHE`; fail-fast message otherwise |
| License drift | Texts scattered in snapshots today | Per-dep LICENSES rows/texts landed BEFORE its snapshot is deleted |
