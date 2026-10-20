# Research: CPM Vendor Removal (spec 032)

P0 verdicts to record before wiring. Methods: read version headers/changelogs inside the snapshots; `git log` upstream tags exist (no cloning needed — use `git ls-remote`).

## RQ-001: signalsmith upstream layout

`vendor/signalsmith-linear/` (`fft.h`, `linear.h`, `stft.h`, …) + `vendor/signalsmith-stretch/` — one upstream repo or two? Check `SUPPORT.txt`/`README.md` + upstream (SignalsmithAudio org). Record exact repo URL(s), license file(s), and which headers our code includes (grep `signalsmith` in `src/`).

## RQ-002: pinned versions read off snapshots

For each dep, record the CURRENT version from the snapshot (no upgrades in this spec):
- miniaudio (`miniaudio.h` `MA_VERSION_*`), rtmidi (`RtMidi.h` version macros), dr_libs (repo has no version macro — record file dates/hashes + nearest upstream state), hiir (header changelog), flac (CMakeLists version), lame (3.100 per wrapper README), JUCE (8.0.14 verified).
Map each to an upstream tag/commit + tarball URL where available.

## RQ-003: lame + JUCE fetch mechanics

- LAME 3.100: confirm a stable tarball URL (SourceForge `lame/3.100/lame-3.100.tar.gz` + SHA256 from a local hash of the snapshot's files if needed); confirm our thin wrapper builds file-identical sources (diff wrapper `LAME_SOURCES` against tarball listing when fetched).
- JUCE: confirm tag `8.0.14` exists upstream; confirm `GIT_SHALLOW` + `juce_add_plugin` + juceaide work from a FetchContent source dir (no in-place assumptions in our CMake: grep `vendor/JUCE` outside `add_subdirectory`).

## RQ-004: stray `vendor/` references

Grep `scripts/`, docs (`*.md`), `.github/`, IDE configs for `vendor/` paths (export maps? symbol files? packaging lists?) so FR-001's final grep has no surprises.

## Verdicts

- T001 (signalsmith + versions): UPSTREAMS CONFIRMED via `git ls-remote` (no clones):
  - signalsmith-linear → `Signalsmith-Audio/linear` (tags …0.6.4, 0.6.5); snapshot has no version macro — implementation pins the tag whose `fft.h` diffs clean against the snapshot, else latest + convolution null-test.
  - signalsmith-stretch → `Signalsmith-Audio/signalsmith-stretch`; snapshot `version[3] = {1, 3, 2}` → pin tag `1.3.2` (verify exists at implementation).
  - miniaudio 0.11.25 → `mackron/miniaudio` tag `0.11.25` (verified).
  - rtmidi 6.0.0 → `thestk/rtmidi` tag `6.0.0` (verified).
  - dr_libs → `mackron/dr_libs` commit `cd99e2cca5ccb48f8d000f1cd844424dcdbbaf59` (2026-06-29, PROVEN byte-identical to the snapshot for all 3 headers via `--find-object` archaeology) — version-neutral AND hash-pinned. (wav 0.14.6 / flac 0.13.4 / mp3 0.7.4 have no single covering tag.)
  - flac 1.5.0 (snapshot `project(FLAC VERSION 1.5.0)`) → `xiph/flac` tag `1.5.0` (verified).
  - hiir 1.40 → NO fetchable 1.40 (official site offers only `hiir-1.50.zip` now; no canonical git mirror — `aldson/hiir` nonexistent). DECISION: fetch 1.50 URL+HASH + run the 031 oversampler null harness (1x–8x); accept the 1.40→1.50 bump iff ≤ −60 dB (documented in row + commit); else keep the 60 K headers with a documented FR-001 exception.
  - lame 3.100 → SourceForge tarball URL+HASH; wrapper parity diffed against tarball listing at implementation.
  - JUCE 8.0.14 → `juce-framework/JUCE` tag `8.0.14` (verified `2cdfca8…`), `GIT_SHALLOW TRUE`.
- T002 (lame/JUCE mechanics + stray refs): lame wrapper parity + JUCE shallow mechanics deferred to implementation tasks (need the fetched trees); stray `vendor/` refs found ONLY in skill templates (Go/Ruby/PHP gitignore examples — unrelated), `BUILD.md`, and `LICENSES/third-party.md` (both updated in P4). No script/CI references to vendor paths. NOTE: `vendor/JUCE` + `vendor/flac` are git SUBMODULES (`.gitmodules`) — removal uses `git submodule deinit + git rm`, not plain delete.
