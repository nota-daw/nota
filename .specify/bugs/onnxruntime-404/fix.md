# Bug Fix: AI models can't be installed — ONNX Runtime download returns 404

- **Slug**: onnxruntime-404
- **Date**: 2026-11-23
- **Issue**: https://github.com/nota-daw/nota/issues/969
- **Assessment**: [assessment.md](assessment.md)

## What changed

- `src/managed/Nota.Infrastructure/ModelStore/ModelCatalog.cs` — `RuntimeFor()` no longer points
  at `microsoft/onnxruntime` release archives. Each platform's runtime is now a `"file"` asset:
  the native library alone from `nota-daw/nota-models` release `onnxruntime-1.23.2`, installed
  under the same name as before (`onnxruntime.dll`, `libonnxruntime.1.23.2.dylib`,
  `libonnxruntime.so.1.23.2`), with `license.txt` and `third-party-notices.txt` as extras. New
  sha256/sizes pinned. Homepage link moved to onnxruntime.ai (the GitHub one is dead).
- `tests/Nota.SmokeTest/ModelStoreTests.cs` — catalog test updated (`Archive: "file"`), new test:
  every runtime comes from the mirror, one distinct library per platform, with both license files.
- `LICENSES/third-party.md`, `CHANGELOG.md` — source of the native library.

## Where the mirrored bytes come from

`Microsoft.ML.OnnxRuntime` 1.23.2 from nuget.org, `dotnet nuget verify --all` passing (author
signature Microsoft Corporation, repository signature NuGet.org). Files taken from
`runtimes/<rid>/native/` plus the package's `LICENSE` and `ThirdPartyNotices.txt`. All six
library sizes equal the library sizes pinned for the old GitHub archives.

| Release asset (`onnxruntime-1.23.2` tag) | NuGet source | Size | sha256 |
|---|---|---|---|
| `onnxruntime-1.23.2-osx-arm64.dylib` | `runtimes/osx-arm64/native/libonnxruntime.dylib` | 35138784 | d306d2bc…ef299 |
| `onnxruntime-1.23.2-osx-x86_64.dylib` | `runtimes/osx-x64/native/libonnxruntime.dylib` | 39742608 | 8c9c78de…4c054 |
| `onnxruntime-1.23.2-win-x64.dll` | `runtimes/win-x64/native/onnxruntime.dll` | 14186016 | dec964ab…1f1ea |
| `onnxruntime-1.23.2-win-arm64.dll` | `runtimes/win-arm64/native/onnxruntime.dll` | 14868512 | 99a11bb0…2c2f0 |
| `onnxruntime-1.23.2-linux-x64.so` | `runtimes/linux-x64/native/libonnxruntime.so` | 22326072 | 13ab8084…4c68b |
| `onnxruntime-1.23.2-linux-aarch64.so` | `runtimes/linux-arm64/native/libonnxruntime.so` | 18693384 | 648ffa64…c286 |
| `onnxruntime-1.23.2-LICENSE.txt` | `LICENSE` | 1094 | c250d627…2b674 |
| `onnxruntime-1.23.2-ThirdPartyNotices.txt` | `ThirdPartyNotices.txt` | 333022 | e00f828e…647b6 |

Full hashes are in `ModelCatalog.cs`.

### Why these bytes are trusted despite the incident

Why the repository is gone (checked 2026-11-23): `microsoft/onnxruntime`, together with
`onnxruntime-github-actions`, `olive` and others, went 404 around 2026-11-18. According to a
Microsoft spokesperson quoted by The Register, and relayed on Hacker News (item 50539610), the
company "made some repositories private while we investigate malicious content". In the same
week, Microsoft's `agent-framework` on PyPI (1.26.1, 1.26.2) was compromised with a credential
stealer (Socket, 2026-11-17). No source names ONNX Runtime 1.23.2 or its NuGet package as
affected, and no official statement or known-good hashes have been published.

The mirrored files predate all of this:
- nuget.org lists `Microsoft.ML.OnnxRuntime` 1.23.2 as published 2025-10-25. NuGet versions
  are immutable, so the package can't be replaced under the same version.
- Microsoft's author signature carries an RFC 3161 timestamp of 2025-10-24 (DigiCert), and it
  verifies even though the signing certificate has since expired.
- All six library sizes equal the ones Nota pinned on 2026-10-02 (commit 1dd35c75) from the
  GitHub archives.

What remains open: we never pinned the library bytes themselves, only the archives, so a
byte-for-byte comparison with the old GitHub archives isn't possible. If Microsoft
publishes guidance on affected artifacts, check it.

## Behaviour for existing installs

A runtime installed before this fix has a marker holding the old archive's sha256. Each
platform's runtime lists that sha256 as `Legacy` (`ModelAsset.Legacy`), and `ModelStore`
counts such a folder as installed without requiring the new license extras. The installed
library is the same 1.23.2 build. This matters because Manual search has no prompt to fetch a
missing runtime: without it, meaning-based search would silently stop after the update (found
in PR review). New installs come from the mirror.

## Prerequisite outside this repository

The fix works only once the release `onnxruntime-1.23.2` with the eight assets above exists in
`nota-daw/nota-models`.
