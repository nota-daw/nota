# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time), alongside the native modular & readability gates (`PUBLIC include/` vs `PRIVATE src/`, acyclic DAG, self-contained `< 180`-line public `Engine::*` headers passing `test_headers_compile`, split `> 400`-line devices / no `> 1,000`-line monoliths, Objective-C++ in `*_mac.mm`, zero leaked SDK headers or `juce::*` exports, RAII without raw `new`/`delete` in `modules/*` domain code, `NOTA_STRICT_WARNINGS_AS_ERRORS=ON` + `dev-asan` + clean `.clangd`, and mandatory `clang-format -i` before concluding engine edits).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (DSP class, kind registration across C++ and C#, device-card UI).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [C++20 (engine), C# / .NET 10 (managed app) — fill in what this feature touches]

**Primary Dependencies**: [e.g., JUCE (pluginhost only), Avalonia — or N/A; any NEW dependency needs a license check per CONTRIBUTING.md]

**Storage**: [if applicable, e.g., project files, `NOTA_DATA_DIR` — or N/A]

**Audio-Thread Impact** *(required — state explicitly)*: [e.g., "None: UI-only change", or "New DSP parameter delivered via the lock-free command queue (AR-4); no allocation on the audio thread (AR-6)"]

**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]

**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]

**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]

**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]

**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]

**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

[Gates determined based on constitution file]

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Nota's real layout is below. Keep only the parts this
  feature touches and expand with concrete paths. The delivered plan must
  not include unused branches.
-->

```text
src/
├── native/nota.engine/        # C++20 engine (JUCE-free except pluginhost/)
│   ├── include/nota_engine.h  # the pure C ABI boundary (AR-7)
│   └── pluginhost/            # the ONLY place JUCE is allowed
├── managed/                   # .NET 10 / Avalonia app
│   ├── Nota.App/              # entry point
│   ├── Nota.Application/      # use cases / services
│   ├── Nota.Domain/           # domain model
│   ├── Nota.Infrastructure/   # engine P/Invoke, persistence
│   ├── Nota.Presentation/     # Avalonia UI (follow DESIGN.md)
│   ├── Nota.Mcp/              # AI-control MCP server
│   └── Nota.Remote/           # phone-controller backend
└── web/remote/                # phone-controller web page

tests/
└── Nota.SmokeTest/            # end-to-end C# → C ABI → engine; the PR gate

scripts/                       # build.sh, build-linux.sh, build-win.ps1, packaging
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
