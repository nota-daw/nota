# Implementation Plan: Default plugin scan paths

**Branch**: `099-default-plugin-scan-paths` | **Date**: 2026-11-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/099-default-plugin-scan-paths/spec.md`
(issue nota-daw/nota#622, `approved` + `enhancement`)

## Summary

Settings → Plug-ins lists the OS-standard VST3 locations as `Default`
folders next to the Nota store folder: always shown, removable, restored
by a "Restore defaults" action, backfilled once on upgrade. The provider
is keyed by format name (`"VST3"` today) and reads JUCE's default search
locations at runtime, so CLAP/LV2 later mean one new entry, not a rework.
The scan itself is untouched — those locations were always searched. See
[research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR rules binding — AR-6 (no file I/O on the audio thread; catalog I/O stays on message/background threads as today), AR-7 (pure C ABI, extend `nota_engine.h`), JUCE confined to `pluginhost/`.
- **UI work** → [`DESIGN.md`](../../DESIGN.md) + `nota-design` skill (well rows, tag, buttons per existing Plug-ins pane).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md) (AGPL headers, changelog, one logical change per PR).

## Technical Context

**Language/Version**: C++20 (engine scanner) + C# / .NET 10 (managed app)

**Primary Dependencies**: JUCE 8 (pluginhost only, existing — no new dependency, no license change)

**Storage**: `scanpaths.txt` in the per-user data dir (one dir per line, unchanged format); defaults backfilled in place

**Audio-Thread Impact**: None. Catalog load/save/scan-path mutation already run on message/background threads; this feature adds no audio-thread touchpoints and no DSP changes.

**Testing**: `tests/Nota.SmokeTest` (end-to-end through the C ABI) + `scripts/build.sh` gate; new contract tests per [contracts/c-abi.md](contracts/c-abi.md)

**Target Platform**: macOS / Windows / Linux (defaults differ per OS; tests assert shape, not literals — see research R7)

**Project Type**: desktop-app (real-time audio DAW)

**Performance Goals**: Backfill/restore run once per trigger on a handful of entries (<1 ms); zero scan-time cost (search set unchanged).

**Constraints**: AR-7 pure C ABI; JUCE stays in `pluginhost/`; no new dependency; `scanpaths.txt` format unchanged (old builds stay readable).

**Scale/Scope**: ~10 files across native ABI + managed interop + one settings pane + smoke tests + changelog.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **0. Approved Issue First** — PASS: nota-daw/nota#622 carries `approved` (+ `enhancement`); scope stays inside it (VST3 listing only, no CLAP/LV2 scanning).
- **I. Real-Time Audio Safety** — PASS: no audio-thread code touched; catalog I/O remains off the audio thread as today.
- **II. Boundary Discipline** — PASS: new surface goes through `nota_engine.h` as pure C (return codes, thread-local strings); JUCE usage stays inside `pluginhost/scanner`.
- **III. Licensing Hygiene** — PASS: no new dependency; new files (if any) carry the SPDX header.
- **IV. Verification Gate** — PASS: smoke test extended (fresh/backfill/remove/restore); `scripts/build.sh` must be green.
- **V. One Logical Change per PR** — PASS: single feature, single spec dir.
- **VI. Documentation Stays in Sync** — PASS: `CHANGELOG.md [Unreleased]` entry; user-visible settings change (no new shortcuts; FEATURES.md only if the maintainers' skill flags it).
- **VII. Spec-First** — PASS: this plan + spec + forthcoming tasks before implementation.
- **VIII. Safety Rules** — PASS: no rule bent.

*Post-Phase-1 re-check*: design keeps JUCE querying inside `pluginhost/scanner`, strings cross as UTF-8 `const char*`, persistence format unchanged — gates still pass, no violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/099-default-plugin-scan-paths/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── c-abi.md         # new default-scan-path ABI + managed counterparts
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── native/nota.engine/
│   ├── include/nota/nota_engine.h       # +3 default-scan-path functions
│   └── pluginhost/scanner/
│       ├── PluginCatalog.{h,cpp}        # per-format default provider + backfill
│       └── abi_pluginscanner.cpp        # ABI implementation
├── managed/
│   ├── Nota.Application/Ports/IPluginCatalog.cs   # per-format default members
│   ├── Nota.Infrastructure/Interop/
│   │   ├── NativeMethods.PluginHost.cs  # P/Invoke entries
│   │   ├── NotaEngine.PluginHost.cs     # checked wrappers
│   │   └── PluginCatalog.cs             # IPluginCatalog impl
│   └── Nota.App/
│       ├── PluginScan.cs                # restore orchestration (OS + store)
│       └── PreferencesWindow.cs         # Default tag, Remove, Restore button
└── tests/
    └── Nota.SmokeTest/                  # contract tests (fresh/backfill/roundtrip)
```

**Structure Decision**: Native owns OS defaults (runtime JUCE query, no
hardcoded table), app owns the store folder (existing `EnsureStoreScanPath`);
"Restore defaults" orchestrates both managed-side. See [research.md](research.md)
R1/R4/R5.

## Complexity Tracking

No constitution violations — table not applicable.
