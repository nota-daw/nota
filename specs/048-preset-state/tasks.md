# Tasks: Preset name and "modified" state

- [x] T001 Engine: preset tag on devices, MIDI effects and instruments; tag table; clone paths copy it
- [x] T002 Engine: `MixKind::PresetTag` in mix gestures (undo / redo)
- [x] T003 Engine: `presetModified` (tolerance 1e-4 of range, view params skipped, CV base values) + self-test
- [x] T004 C ABI + managed interop (`IAudioEngine`, `NotaEngine.Presets.cs`)
- [x] T005 `PresetEdit.Apply` / `Tag`: one undo step with the tag
- [x] T006 Device card: header name from the tag, "Name \*" + tooltip while modified
- [x] T007 Browser drops and MCP in-place apply tag the device
- [x] T008 Save preset… starts from the tag's name and retags
- [x] T009 Persistence: `PresetTagDto`, format v26 + migration
- [x] T010 MCP `read_preset_state`
- [x] T011 Smoke tests `--preset-state` (engine self-test, undo/redo, Init, copy/duplicate/move, view params, save/load, migration)
- [x] T012 CHANGELOG, FEATURES, docs EN + RU
- [x] T013 macOS check with before/after screenshots (Mac mini, Oct 28: all 7 hands-on checks pass; `--preset-state` 156 PASS)
- [ ] T014 Follow-up: rack chain devices (US4)
