# Implementation Plan: Plug-in card shows a preview of the plug-in's interface

**Branch**: `046-plugin-card-preview` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/046-plugin-card-preview/spec.md`

## Summary

When a hosted plug-in's editor window is hidden (by the card's "Close editor" or by the
window's own close button), `pluginhost/` captures the window's content: on macOS
through `CGWindowListCreateImage` on our own window (Metal-safe, no permission), on
Windows through `PrintWindow`. It scales the capture to card size, rejects blank
captures, and stores it as a small JPEG on the device object, the way the fold flag is
stored. So it follows moves, copies, duplicates, rack grouping and undo for free. Three
C ABI calls expose it, and the project saves it as a blob next to the plug-in state. The
plug-in card draws it as its background on S (behind the info column on L), scaled to
cover without distortion, under a vertical gradient in the theme's card colour. See
[research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5/AR-6/AR-7 — the
  preview is message-thread data on the device; the audio thread never reads it.
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine + `pluginhost/`, ObjC++ for the macOS capture TU),
C# / .NET 10 (managed).

**Primary Dependencies**: JUCE (existing, `pluginhost/` only): `juce_graphics`
`JPEGImageFormat`. macOS CoreGraphics/AppKit (already linked), Win32 user32/gdi32
(already linked by JUCE). Avalonia (existing). **No new dependency.**

**Storage**: `.nota` bundle. The preview is registered as a state blob
(`RegisterBlob`, content-hashed) and referenced from the device's DTO as `Preview`.
See [contracts/project-format.md](contracts/project-format.md).

**Audio-Thread Impact**: None. Capture, storage and ABI calls are on the message/UI
thread. The new device member is never touched by `process()`/`render()`.

**Testing**: `tests/Nota.SmokeTest/PluginPreviewTests.cs`: ABI set/get/serial round
trip on a device, carried by duplicate and copy/paste, cleared by size 0, survives
project save/load, an old project without previews loads. Live capture of a real
plug-in window is checked by hand on macOS ([quickstart](quickstart.md)), since CI has
no plug-ins or display.

**Target Platform**: macOS (capture), Windows (capture, CI-compiled), Linux (no
capture, plain card).

**Project Type**: desktop app (native engine + Avalonia).

**Performance Goals**: capture ≤ ~30 ms once per window close; preview ≤ 100 KB;
decode ≤ 2 ms, cached.

**Constraints**: capture must happen before the window is ordered out; no Screen
Recording prompt; never a stretched image.

**Scale/Scope**: ~4 native files, 3 ABI calls, ~5 managed files, 1 smoke test.

## Constitution Check

| Principle | Status | Note |
|-----------|--------|------|
| I. Real-time audio safety | Pass | Message-thread only; audio thread never reads the preview |
| II. Boundary discipline | Pass | 3 new pure-C calls in `nota_engine.h`; JUCE/AppKit/Win32 confined to `pluginhost/` |
| III. Licensing hygiene | Pass | No dependency; new files carry the SPDX header |
| IV. Verification gate | Pass | `scripts/build.sh` + new smoke test for the ABI and persistence |
| V. One logical change | Pass | One PR: plug-in card preview |
| VI. Docs in sync | Planned | CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), nota-docs plug-in page |
| VII. Spec first | Pass | spec → plan → tasks |
| VIII. Rules not weakened | Pass | — |

Post-design re-check: unchanged, all pass.

## Project Structure

### Documentation (this feature)

```text
specs/046-plugin-card-preview/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md              # the three nota_plugin_preview_* calls
│   └── project-format.md     # DeviceDto/InstrumentDto.Preview
└── tasks.md                  # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # + nota_plugin_preview_get/_set/_serial
├── src/devices/EditorPreview.h           # NEW: { jpeg, serial } + make() with a global serial
├── src/devices/Device.h                  # + editorPreview()/setEditorPreview()
├── src/devices/Instrument.h              # + EditorPreview editorPreview (like `folded`)
├── src/engine/Engine.h, Engine_Devices.cpp  # track lookup get/set; cloneDevice +
│                                         #   cloneInstrumentFrom carry it
├── src/abi/nota_engine_device.cpp        # ABI wrappers
└── pluginhost/hosting/
    ├── PluginEditorWindow.h/.cpp         # onHide hook before hiding; EditorHost snapshots
    │                                     #   on hide and hands bytes to onPreview
    ├── EditorSnapshot.h                  # NEW: std::vector<uint8_t> snapshotEditor(DocumentWindow&)
    ├── EditorSnapshot.cpp                # NEW: one source, ObjC++ on macOS like PluginEditorWindow:
    │                                     #   macOS CGWindowListCreateImage via dlsym; Windows
    │                                     #   PrintWindow; Linux returns {}; shared blank check + JPEG
    ├── PluginEffect.cpp                  # editor_.onPreview → setEditorPreview
    └── PluginInstrument.cpp              # editor_.onPreview → editorPreview

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs          # + GetPluginPreview/SetPluginPreview/PluginPreviewSerial
├── Nota.Infrastructure/Interop/NativeMethods.Devices.cs, NotaEngine.Devices.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs  # DeviceDto/InstrumentDto.Preview
├── Nota.Infrastructure/Persistence/ProjectService.cs # save/load the blob
├── Nota.App/Controls/PluginPreviewBackdrop.cs       # NEW: cover-fit image + theme gradient
├── Nota.App/DeviceCards/PluginCard.cs               # info column on L sits on the backdrop
└── Nota.App/DeviceChainView.Plugin.cs               # S body sits on the backdrop; serial poll

tests/Nota.SmokeTest/PluginPreviewTests.cs           # NEW
```

**Structure Decision**: Capture stays inside `pluginhost/` (the only place JUCE and
platform window APIs are allowed). The per-platform code lives in its own TU (`EditorSnapshot.cpp`, built as ObjC++ on
macOS like `PluginEditorWindow.cpp`) so the window class stays small. Storage mirrors the fold flag on
the device (R3).

## Complexity Tracking

No constitution violations.
