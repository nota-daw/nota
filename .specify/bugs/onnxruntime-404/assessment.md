# Bug Assessment: AI models can't be installed — ONNX Runtime download returns 404

- **Slug**: onnxruntime-404
- **Created**: 2026-11-23
- **Source**: https://github.com/nota-daw/nota/issues/969 (host: github.com — allowlisted; read with `gh issue view`)
- **Verdict**: valid
- **Severity**: high

## Report (summarized)

Issue #969, Nota 0.60.3 on Linux x64: installing htdemucs from Settings → Downloads → AI
Models fails with "Couldn't download ONNX runtime (404)". The runtime is fetched from
`https://github.com/microsoft/onnxruntime/releases/download/v1.23.2/onnxruntime-linux-x64-1.23.2.tgz`,
and the whole `microsoft/onnxruntime` repository now answers 404. The model files themselves
are still reachable. A commenter confirms the same for basic-pitch. The reporter suggests
hosting the runtime next to the models in `nota-daw/nota-models`.

## Symptom

Installing any AI model on a machine without the runtime already installed fails at the
runtime download with HTTP 404. Expected: the runtime and the model install.

## Reproduction

1. Fresh setup (no `<data>/models/onnxruntime/` folder).
2. Settings → Downloads → AI Models → install any model (Separate Stems, Convert to MIDI, Manual search).
3. The runtime download fails with 404; nothing is installed.

Confirmed on 2026-11-23: `https://github.com/microsoft/onnxruntime` → 404 (all platforms are
affected, since every runtime URL is under that repository).

## Suspected Code Paths

- `src/managed/Nota.Infrastructure/ModelStore/ModelCatalog.cs:41` — `OrtRelease` points every
  runtime asset at `github.com/microsoft/onnxruntime/releases`.
- `src/managed/Nota.Infrastructure/ModelStore/ModelCatalog.cs:87` `RuntimeFor()` — pins the six
  per-platform archives (size + sha256) and the files kept from them.
- `src/managed/Nota.Infrastructure/ModelStore/ModelStore.cs:78` — the runtime is installed before
  any model, so its failure blocks every model.

## Root Cause Hypothesis

An external dependency vanished: the upstream repository hosting the pinned release archives
is gone (404 for the repository itself, not one asset). Nota has no other source for the
runtime. Confidence: high.

## Proposed Remediation

**Preferred**: Mirror the runtime in `nota-daw/nota-models` (release tag `onnxruntime-1.23.2`)
and pin the catalog to it. The original GitHub archives are no longer obtainable and no local
copy exists, so the mirror is built from the official NuGet package
`Microsoft.ML.OnnxRuntime` 1.23.2 (still on nuget.org, Microsoft-signed — verify with
`dotnet nuget verify` before extracting). Publish each platform's native library on its own
(`runtimes/<rid>/native/…`, renamed per platform so names don't clash in the flat release),
plus `LICENSE` and `ThirdPartyNotices.txt` once. The catalog asset becomes a `"file"` download
of the library (installed under its expected name) with the two license files as `Extra`s.
This also shrinks the download (win-x64: 78 MB archive → ~14 MB library). New sha256/sizes
are pinned; installed runtimes re-install once, because the marker holds the old sha256.

**Alternatives**:
- Download the 123 MB NuGet package directly — no hosting, but ~10× the bytes per user and
  still an upstream dependency.
- Bundle the runtime in the installers — larger installers, a product decision outside this fix.

**Files likely to change**:
- `src/managed/Nota.Infrastructure/ModelStore/ModelCatalog.cs`
- `tests/Nota.SmokeTest/ModelStoreTests.cs` (catalog shape assertions)
- `CHANGELOG.md`

**Tests to add or update**:
- Catalog test: every runtime is a `"file"` asset under `nota-daw/nota-models`, pinned to an
  https URL, size and sha256, with the license files as extras.
- `--models-live` run against the published mirror (install + transcribe an A4 tone).

## Risks & Considerations

- Supply chain: the upstream disappearance coincides with reports of a compromise
  (linked in the issue thread). The NuGet package must be signature-verified, and the
  libraries' bytes should match the sizes already pinned in the catalog for 1.23.2 where the
  file is the same.
- The mirror must be published (a release in `nota-daw/nota-models`) before the fix works;
  that is an outward action for a maintainer.
- Licensing: ONNX Runtime is MIT; redistribution needs LICENSE + ThirdPartyNotices, which are kept.
- Existing users with an installed runtime will re-download it once (new sha256 in the marker).

## Open Questions

- None blocking.
