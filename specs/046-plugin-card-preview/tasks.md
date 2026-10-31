---
description: "Tasks: plug-in card shows a preview of the plug-in's interface"
---

# Tasks: Plug-in card shows a preview of the plug-in's interface

**Input**: Design documents from `specs/046-plugin-card-preview/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: The constitution requires smoke-test coverage for new C ABI surface, so the
smoke test is included (T020).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (preview on the card), US2 (survives save/reopen), US3 (plain card
  when none). US4 (offscreen capture on add) is deferred (research R7).

---

## Phase 1: Setup

- [X] T001 Add `pluginhost/hosting/EditorSnapshot.cpp` to `nota_pluginhost` and to the
  `LANGUAGE OBJCXX` list on Apple in `src/native/nota.engine/CMakeLists.txt`

---

## Phase 2: Foundational (engine storage + C ABI)

- [X] T002 [P] Create `src/native/nota.engine/src/devices/EditorPreview.h`: struct
  `EditorPreview { std::shared_ptr<const std::vector<uint8_t>> jpeg; uint32_t serial = 0; bool empty() const; static EditorPreview make(std::vector<uint8_t> bytes); }`
  where `make` assigns a process-wide atomic serial starting at 1 ("0 = none"); SPDX header
- [X] T003 Add `editorPreview()` / `setEditorPreview(EditorPreview)` (private member,
  comment: view data, message thread only, never read by the audio thread, follows moves/
  copies/undo) to `src/native/nota.engine/src/devices/Device.h`, and a public
  `EditorPreview editorPreview;` next to `folded` in `src/native/nota.engine/src/devices/Instrument.h`
- [X] T004 Copy the preview in `Engine::cloneDevice` (next to `setFolded`) and in the
  hosted-plug-in branch of `Engine::cloneInstrumentFrom` in
  `src/native/nota.engine/src/engine/Engine_Devices.cpp`
- [X] T005 Add `Engine::trackEditorPreview(trackId, di) const` and
  `Engine::setTrackEditorPreview(trackId, di, EditorPreview)` (lookup as in
  `trackEditorOpen`; -1 = instrument) to `src/native/nota.engine/src/engine/Engine.h` and
  `Engine_Devices.cpp`
- [X] T006 Declare `nota_plugin_preview_get/_set/_serial` in
  `src/native/nota.engine/include/nota/nota_engine.h` and implement them in
  `src/native/nota.engine/src/abi/nota_engine_device.cpp` per contracts/c-abi.md
  ("`size` < 0 or > 4 MB, or `data` NULL with size > 0 → `NOTA_ERR_INVALID_ARG`"; size 0 clears;
  unknown track/index: get/serial return 0, set returns `NOTA_ERR_INVALID_ARG`)
- [X] T007 [P] Add the three P/Invokes to
  `src/managed/Nota.Infrastructure/Interop/NativeMethods.Devices.cs`, the members
  `GetPluginPreview` / `SetPluginPreview` / `PluginPreviewSerial` to
  `src/managed/Nota.Application/Engine/IAudioEngine.cs` and their implementation to
  `src/managed/Nota.Infrastructure/Interop/NotaEngine.Devices.cs`

**Checkpoint**: the preview can be stored, read and cloned through the ABI.

---

## Phase 3: User Story 1 — the card shows the plug-in's look (P1) 🎯 MVP

**Goal**: closing a plug-in window puts a darkened, undistorted picture of it behind the card.

**Independent test**: quickstart steps 3–7.

- [X] T008 [P] [US1] Create `src/native/nota.engine/pluginhost/hosting/EditorSnapshot.h`:
  `std::vector<uint8_t> snapshotEditor(juce::DocumentWindow&)` returning JPEG bytes or empty
- [X] T009 [US1] Implement `EditorSnapshot.cpp`: macOS `CGWindowListCreateImage` via `dlsym`
  on the window's own `windowNumber`, crop the title bar, draw into a CG bitmap backed by a
  `juce::Image` at the target size with `kCGInterpolationHigh`; Windows `PrintWindow`
  (`PW_CLIENTONLY | PW_RENDERFULLCONTENT`) + `HALFTONE` `StretchBlt`; Linux returns {}.
  Shared: target size "shorter side ≤ 520 px, longer ≤ 1040 px, never upscale"; blank check
  "16 × 16 samples, luma range < 6/255 → reject"; JPEG quality 0.8 via `juce::JPEGImageFormat`
  (research R1, R2, R4, R5)
- [X] T010 [US1] In `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.h/.cpp`: add
  `std::function<void()> onHide` called by `closeButtonPressed()` before `setVisible(false)`;
  `EditorHost` gains `std::function<void(std::vector<uint8_t>)> onPreview`, wires `onHide` at
  window creation, and snapshots in `close()` while the window is still visible; empty
  snapshots are dropped
- [X] T011 [P] [US1] Wire `editor_.onPreview` to `setEditorPreview(EditorPreview::make(...))` in
  `src/native/nota.engine/pluginhost/hosting/PluginEffect.cpp`
- [X] T012 [P] [US1] Wire `editor_.onPreview` to `editorPreview = EditorPreview::make(...)` in
  `src/native/nota.engine/pluginhost/hosting/PluginInstrument.cpp`
- [X] T013 [P] [US1] Create `src/managed/Nota.App/Controls/PluginPreviewBackdrop.cs`: a control
  holding `Bitmap? Image`, drawing it cover-fit (uniform scale, top-anchored, centred
  horizontally, clipped) and over it a vertical gradient in `NotaPalette.SurfaceCard`
  (alpha 0.80 top, 0.62 middle, 0.90 bottom); redraws on `NotaPalette.Changed` while
  attached; a static decode cache keyed by serial (last 32) via `PluginPreviewBackdrop.For(engine, track, di)`
- [X] T014 [US1] In `src/managed/Nota.App/DeviceChainView.Plugin.cs`: on S put the card body on a
  backdrop (backdrop under the padded body in a `Panel`); in
  `src/managed/Nota.App/DeviceCards/PluginCard.cs` on L put the info column's section on a
  backdrop; both poll `PluginPreviewSerial` in a device refresher and swap the image when it
  changes; also refresh right after the card's Close-editor click
- [X] T015 [US1] Check the S/L cards in Studio Dark and Studio Light with the headless
  harness (memory `nota-ui-verify`): text legible, no stretching

---

## Phase 4: User Story 2 — survives save and reopen (P1)

**Goal**: previews are saved in the project and restored on open.

**Independent test**: quickstart step 8; smoke test T020.

- [X] T016 [US2] Add `Preview` (`string?`) to `DeviceDto` and `InstrumentDto` in
  `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`
- [X] T017 [US2] In `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` save:
  `Preview = RegisterBlob(doc, engine.GetPluginPreview(...))` for the instrument and for each
  device in `CaptureDevices`, only when non-empty
- [X] T018 [US2] In `ProjectService.cs` load (`ApplyDevices` and the instrument path): after
  the device/state is applied, `SetPluginPreview(track, di, ReadBlob(...))` when `Preview` is set;
  a missing blob is skipped silently

---

## Phase 5: User Story 3 — plain card without a preview (P2)

**Goal**: no capture → today's card; a failed capture never replaces a good one.

- [X] T019 [US3] Confirm `PluginPreviewBackdrop` draws nothing (no gradient either) when
  `Image` is null, so a card without a preview is pixel-identical to today; and that
  `EditorHost` never calls `onPreview` with an empty/blank snapshot (FR-009, FR-010)

---

## Phase 6: Polish & cross-cutting

- [X] T020 Create `tests/Nota.SmokeTest/PluginPreviewTests.cs` (model: `DeviceFoldTests.cs`) and
  register it: set/get/serial round trip on an effect and the instrument; serial changes on
  set; size 0 clears (serial 0); invalid args; duplicate (⌘D path) and copy/paste carry it;
  project save/load keeps it; a project saved without previews loads with none
- [X] T021 Run the quickstart by hand on macOS with a real plug-in (Supercharger / Massive) — done with a
  harness on the real engine: real Supercharger VST3 window captured (53.8 KB, title bar cropped),
  recapture changes the serial, close ≈ 60 ms; cards rendered S/L in Studio Dark, Studio Light,
  Ember Paper and with no preview. Not covered: the live app's undo/save UI flow, Windows capture
  (compiled only in CI)

### Nota closing tasks

- [X] T022 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T023 AGPL-3.0-only SPDX two-line header on every new source file
- [X] T024 New dependency: N/A (JUCE `JPEGImageFormat`, CoreGraphics and Win32 are already linked)
- [X] T025 `CHANGELOG.md` `[Unreleased]` entry
- [X] T026 FEATURES.md updated (plug-in card entry); nota-docs page — left for a follow-up; shortcuts N/A
- [X] T027 Engine behaviour change covered by T020

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → (US1, US2 in parallel) → US3 → Polish.
- T002 before T003–T006; T006 before T007's implementation compiles against the ABI.
- T008 before T009/T010; T010 before T011/T012. T013 before T014.
- US2 (T016–T018) depends only on Phase 2.

### Parallel opportunities

- T002 ∥ T007 (header vs managed interop once the ABI shape is fixed by contracts/c-abi.md).
- T011 ∥ T012 ∥ T013.
- US2 ∥ US1 native capture work.

## Implementation Strategy

MVP = Phases 1–3 (capture + card). US2 makes it persistent and ships in the same PR.
US4 (offscreen capture on add) is a follow-up issue.
