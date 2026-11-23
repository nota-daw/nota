# Tasks: A plug-in that hangs while loading doesn't freeze Nota

**Input**: [spec.md](spec.md), [plan.md](plan.md) · Issue: nota-daw/nota#979

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Foundational (native)

- [x] T001 [US1] `pluginhost/hosting/PluginLoader.{h,cpp}`: load tickets, one loader thread, per-format binary preload, `service()` (main-thread instance creation, deadline → not responding), keep waiting / give up / try again, stuck-loader rule (FR-001, FR-002, FR-005, FR-010)
- [x] T002 [US3] Freeze guard in `PluginLoader`: `plugin-loading.txt` around the main-thread step, leftover → `plugins-froze-nota.txt` + one-time notice, autoload scope skips marked plug-ins, success unmarks (FR-008, FR-009)
- [x] T003 [US1] `Device.h` / `Instrument.h`: `pluginLoadState()`, `pluginLoadReason()`, `pluginLoadAction()` (JUCE-free)
- [x] T004 [US1] `PluginEffect` / `PluginInstrument`: unloaded constructor from a catalog description, `attach()`, pending state kept and saved byte for byte, `live_` gate on every audio-thread entry, atomic `parallelRender_`, deferred `clone()` (FR-003, FR-004, FR-007)
- [x] T005 [US1] `PluginHost.cpp` factories return unloaded adapters; bridge functions for service / wait / scope / notice
- [x] T006 [US2] Engine: `servicePluginLoads()` (+ PDC, re-resolve plug-in-param lanes), `waitPluginLoads()`, per-device state / action; C ABI in `nota_engine.h` (FR-012)
- [x] T007 [US4] Log plug-in, format, identifier, step and elapsed time on every failure / give-up (FR-011)
- [x] T008 [P] Render probe: `NOTA_RENDER_PROBE_LOAD_DELAY_MS` static-initialiser delay

## Phase 2: Managed / UI

- [x] T009 Interop + `IAudioEngine` (+ test fakes) for the new calls
- [x] T010 [US1] `App.axaml.cs`: service plug-in loads on a UI timer (all OSes); refresh the device panel on a change
- [x] T011 [US1] `PluginCard`: loading / waiting / not responding (Keep waiting, Give up) / couldn't load (Try again) body, rebuilding on state change; removal is the card shell's delete; `en.json` strings
- [x] T012 [US2] `ProjectService` open runs in the autoload scope; no "parameter not found" warning for a device still loading
- [x] T013 [US3] `MainWindow`: one-time status notice naming the plug-in that froze Nota

## Phase 3: Tests

- [x] T014 [US1] Smoke `--plugin-load` (isolated child): delayed probe insert returns at once and is "loading", the caller isn't blocked, it attaches and processes after service; short deadline → not responding → keep waiting → loads; give up → couldn't load; state set while loading survives byte for byte; lane made while loading resolves after attach
- [x] T015 [US3] Smoke: leftover `plugin-loading.txt` → notice once, marked plug-in skipped inside the autoload scope, loaded outside it, unmarked after
- [x] T016 Existing hosted tests (`HostedRenderPoolTests`, `HostedPluginTests`) wait for loads after inserting
- [x] T017 Smoke ABI checks without a plug-in: built-in devices report state 0; service / wait with nothing pending return 0

### Nota closing tasks *(PR gates)*

- [x] T018 SPDX headers on new files
- [x] T019 `CHANGELOG.md` `[Unreleased]` entry; FEATURES.md / docs via nota-features / nota-docs
- [ ] T020 `scripts/build-win.ps1` passes (native + managed + smoke) on the dev machine
- [ ] T021 `/speckit-converge` → Converged

## Phase 4: Convergence

- [x] T022 Resolve plug-in-param lanes still waiting for their index on every service tick, so an undo that brings back a lane made before its plug-in loaded still resolves per FR-007 (partial)
- [ ] T023 Show the load state (loading / not responding / couldn't load, with its buttons) on plug-in cards inside rack chains — the rack engine proxies don't route the new calls yet per FR-005 (partial)
- [ ] T024 Verify quitting Nota while a binary load is stuck (Windows loader lock, macOS dyld lock) doesn't hang the exit per Edge Cases: quitting while a plug-in is loading (partial)
- [ ] T025 Update the user manual's plug-in hosting page (EN + RU) in nota-docs — the docs repo isn't on the dev machine per Constitution VI (partial)

## Dependencies & Execution Order

T001–T005 before T006; T006 before T009; T009 before T010–T013; tests after their story.

## Notes

- macOS-only preload code (CFBundle, AU) is compiled by CI's macOS job; it can't be run
  without an SSL / iLok licence — the reporter is asked to try the PR build.
