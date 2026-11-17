# Implementation Plan: Tempo as master-track automation

**Branch**: `feat/master-tempo-automation` | **Date**: 2026-11-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/099-master-tempo-automation/spec.md` (issue #93)

## Summary

Tempo stops being a separate lane and becomes one more automation target — **Tempo** — on
the master track, next to Volume, Pan and master device parameters. The engine keeps #568's
compiled tempo map but derives it from the master Tempo lane on every snapshot publish, using
the same curve function as all automation, so drawn = heard and undo/section edits stay
consistent. The #568 tempo row, its Bezier/step point model and the Ctrl-drag time-scale
mapping are removed; #568 projects migrate (format v34). Master lanes start playing in
`applyAutomation`, and a master pan stage is added. Delivered as two PRs (research R10).

## Nota: Read Before Planning

- Engine: [`ARCHITECTURE.md`](../../ARCHITECTURE.md) AR-4/5/6/7/9 — the map is still built on
  the authoring thread and published in the snapshot; Transport remains the only source of
  musical time.
- UI: [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (lane header picker, range
  fields, read-only transport field).
- Anything: [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app, Avalonia UI, MCP)

**Primary Dependencies**: existing only (Avalonia); no new dependency

**Storage**: `.nota` project JSON, format v33 → **v34** (migration step, see data-model.md)

**Audio-Thread Impact**: Tempo map derivation, curve integration and clip reprojection run on
the authoring thread inside publish (AR-5); render keeps only search/interpolate on the
immutable map. `applyAutomation` (already block-rate on the audio thread) additionally walks
the master track's lanes — no allocation, same code paths as track lanes (AR-6). Master pan:
one relaxed atomic read and a gain pair per block (AR-6). Fixed-tempo projects (no Tempo
lane) keep `hasTempoMap() == false` and the existing fast path (SC-006).

**Testing**: `tests/Nota.SmokeTest` (engine self-tests through the C ABI, `--tempo` group,
migration fixtures), headless Avalonia harness for UI checks and screenshots, Linux
performance gate (valgrind instruction counts).

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: no regression on the 64-scenario performance gate (1% instructions /
15% wall); map re-derivation while dragging a Tempo point fast enough for live audition
(< 2 ms for 256 points on the authoring thread).

**Constraints**: tempo 20–300 BPM; beat↔seconds round trip < 1 sample at 48 kHz; migrated
bar starts within 1 ms (SC-002).

**Scale/Scope**: master lane up to 4096 tempo points (existing map limit).

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | ✅ #93 carries `approved` (2026-11-15) |
| I Real-time safety | ✅ derivation off the audio thread; render-side additions allocation-free |
| II Boundary discipline | ✅ all changes through `nota_engine.h` (contracts/c-abi.md) |
| III Licensing | ✅ no new dependency; new files get SPDX headers |
| IV Verification gate | ✅ new self-test + smoke tests + migration fixtures (quickstart.md) |
| V One logical change per PR | ✅ split into PR 1 (tempo) and PR 2 (master pan/devices), R10 |
| VI Docs in sync | ✅ CHANGELOG, FEATURES.md, nota-docs (tempo pages from #568), nota-shortcuts (removed group) |
| VII Spec-first | ✅ this spec/plan/tasks |
| VIII Rules not weakened | ✅ |

Post-design re-check: no violations; Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/099-master-tempo-automation/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── ui-and-mcp.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # target 6 doc, tempo_automated, master pan, selftest; remove tempo point/preview ABI
└── src/
    ├── engine/Automation.h               # AutomationTarget::Tempo
    ├── engine/TempoMap.h                 # single curve shape, built from lane points, `source`
    ├── engine/Engine.h / Engine.cpp      # deriveTempoMap in publish/publishRaw; remove setTempoPoints; set_bpm guard
    ├── engine/Engine_Automation.cpp      # master lanes in applyAutomation; Tempo add rules; selftest
    ├── engine/Engine_Render.cpp          # master pan stage (PR 2)
    └── abi/nota_engine_core.cpp, nota_engine_automation.cpp

src/managed/
├── Nota.Application/Engine/              # AutomationTarget.Tempo, IAudioEngine (IsTempoAutomated, master pan); drop TempoPoint/ITempoPreview
├── Nota.Application/Editing/             # drop TempoMapping.cs
├── Nota.Application/Input/ShortcutLayout.cs   # drop tempo group
├── Nota.Application/Localization/en.json # drop tempo.* strings; add picker/notice/tooltip strings
├── Nota.Infrastructure/Interop/          # NativeMethods + NotaEngine.Tempo.cs reshaped
├── Nota.Infrastructure/Persistence/      # MasterDto.Automation/Pan, v34 migration, TempoMigration.cs
├── Nota.App/ArrangementView.Tempo.cs     # deleted; axis helpers → ArrangementView.TimeAxis.cs
├── Nota.App/ArrangementView.Automation.cs, .Controls.cs   # master target picker + generic master lane
├── Nota.App/MixerView*.cs                # master pan knob (PR 2)
├── Nota.Presentation/ViewModels/TransportViewModel.cs     # read-only automated tempo field
└── Nota.Mcp/Tools/AutomationTools.cs, ProjectTools.cs, TransportTools.cs

tests/
├── Nota.SmokeTest/                       # tempo lane tests, migration fixtures, master automation
└── Nota.TempoUiTest/                     # removed (relevant checks moved)
```

**Structure Decision**: Existing Nota layout; no new projects. One project removed
(`tests/Nota.TempoUiTest`).

## Phasing

- **PR 1 — Tempo as master automation** (US1, US2, US3, US5; FR-001…FR-011, FR-013): engine
  target + derivation + map reshape, ABI removal, managed interop, master picker with
  Volume/Tempo, Tempo lane range fields, transport field, removal of the #568 row and
  mapping, migration v34 + notice, MCP, tests, docs.
- **PR 2 — Master Pan and device parameters** (US4; FR-012, SC-007): master lanes in
  `applyAutomation`, master pan stage + ABI + mixer knob + persistence, picker entries,
  device-removal lane cleanup on master, tests, docs. Closes #93.

## Complexity Tracking

Not needed — no constitution violations.
