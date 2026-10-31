# Tasks: Tempo-synced scope in Nota Lens

- [x] T1 Engine: five appended params (Sync, Span, Key, Key Gain, Layout), `M_KeyLive`
- [x] T2 Engine: transport + sidechain overrides; key rings; one-block-behind capture with a key
- [x] T3 Engine: beat-stamped min/max history; `analyzeSync()` (synced, free, seek / wrap restart)
- [x] T4 Engine: `L_KeyTrace` on the main trace's trigger; `L_Sync` frame; MCP text (id 2)
- [x] T5 UI: `LensScopeView` synced frame (beat / bar grid, sweep, previous pass), key overlay,
      Overlay / Stacked, legend
- [x] T6 UI: Lens card Sync switch, Span segments, Key Gain knob, KEY row (source + show), Layout
- [x] T7 UI: `LensWindow` pop-out (expand button, closes with the device / on project load)
- [x] T8 Smoke test `--lens-sync`; Lens param / scope counts in the main run
- [x] T9 CHANGELOG, user guide EN + RU (nota-docs)
- [x] T10 Screenshots, macOS check
