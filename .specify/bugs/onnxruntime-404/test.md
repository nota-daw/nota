# Bug Test: AI models can't be installed — ONNX Runtime download returns 404

- **Slug**: onnxruntime-404
- **Date**: 2026-11-23
- **Platform**: Windows 10 x64 (4 CPUs)
- **Result**: resolved

## Checks

| Check | Result |
|---|---|
| Each of the 8 assets of `nota-daw/nota-models` release `onnxruntime-1.23.2`, downloaded by its public URL, matches the sha256 pinned in `ModelCatalog.cs` | 8/8 match |
| `--models`: catalog and store tests, including "the runtime comes from Nota's own mirror, one library per platform, with its license and notices (#969)" | pass |
| `--models-live <dir> --stems`: runtime + basic-pitch + htdemucs install from their pinned URLs into an empty folder; runtime folder holds `onnxruntime.dll`, `license.txt`, `third-party-notices.txt` | pass |
| …basic-pitch on the downloaded runtime hears an A4 tone as A4 (69) | pass |
| …htdemucs on the downloaded runtime separates | pass |

## Not verified

- macOS and Linux libraries were not loaded on their platforms (only hashes and sizes were
  checked; the sizes equal the libraries in the previously pinned GitHub archives).
- `scripts/build-win.ps1` built the engine and the app, but the full smoke test reported
  8 failures. Every one is a timing or CPU-budget check (VST3 render-pool deadlines, the
  Consort stress budget, render speed, one-core busy-loop accounting at 20.6 %). The set of
  failures and their numbers changed between runs. The machine was running another session's
  builds and tests at the time (7 dotnet processes on 4 CPUs). None of these checks touches
  the model store.
