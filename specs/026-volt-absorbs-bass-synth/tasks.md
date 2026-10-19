# Tasks: Fold Nota Bass and Nota Synth into Nota Volt

**Spec**: [spec.md](spec.md)

- [x] T001 Engine: append Osc1/2 Shape, Osc1/2 PW, Filter Drive, Drive, Unison Voices, Unison
      Spread, Legato, Wheel → LFO to `VoltSynth.h` (133 → 143); render paths only when used.
- [x] T002 Engine: mono held-note stack, Legato switch, retrigger on a releasing voice.
- [x] T003 Engine: Instrument Rack's default chain is Volt.
- [x] T004 `VoltPort` converter (Synth / Bass → Volt) and `PortToVolt` in the factory catalog;
      49 presets filed into Volt folders, colliding names renamed.
- [x] T005 Hide Synth / Bass: `BuiltinDeviceCatalog`, `DeviceCategories`, rack chain and Drum
      Rack pad menus, MCP `list_instrument_kinds` (kinds 0 / 7 still accepted).
- [x] T006 New-track call sites → `AddVoltSynthTrack` (toolbar, template, MIDI import, convert,
      MCP, load fallbacks).
- [x] T007 Volt card: Shape / PW columns, Filter Drive and Wheel → LFO, Poly/Mono/Legato, output
      Drive, unison voices / detune / spread; 790 wide (DESIGN.md exception).
- [x] T008 Smoke tests: param count, v6 controls audible + finite, defaults neutral, stereo
      stack, mono fallback, retrigger, every port plays; tests that assumed Synth as default.
- [x] T009 CHANGELOG, FEATURES, DESIGN; nota-docs EN + RU.
