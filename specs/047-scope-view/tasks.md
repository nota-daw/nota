# Tasks: Scope view

- [x] T001 Engine: `ScopeHistory` ring (value, dt), shared by snapshot copies, allocated for Scope nodes only
- [x] T002 Engine: `readScope` returns the newest entries; timed ABI `nota_track_modulator_scope_timed`
- [x] T003 Engine: modulator fields 13–18 (window ms, sync, beats, zoom, trigger, height) with clamps
- [x] T004 Interop + IAudioEngine `ModulatorScopeTimed`
- [x] T005 `ScopeTrace`: window, labels, knob maps, `Layout` (free / rising-edge trigger)
- [x] T006 Scope node: resize grip, window + Sync, zoom, Free / Rise / Freeze, pop-out glyph
- [x] T007 `ScopeWindow`: same trace and controls, closes when the Scope is gone
- [x] T008 Persistence: `ModulatorDto` Scope fields, Capture / Apply
- [x] T009 Smoke `--scope` (21 checks), in the full run
- [x] T010 CHANGELOG, FEATURES, nota-docs EN + RU (Modular → Scope)
- [ ] T011 Full smoke run, macOS check with before/after screenshots, perf gate
