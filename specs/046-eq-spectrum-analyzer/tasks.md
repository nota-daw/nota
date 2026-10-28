# Tasks: EQ spectrum analyzer

- [ ] T001 `SpectrumAnalyzer` (prepare, reset, analyse, curve, peak) in src/dsp/primitives; add to CMake
- [ ] T002 EQ-8: use two analyzers, append AnaRange / AnaPeak params (names, ranges, defaults, text, MCP help)
- [ ] T003 EQ-8: telemetry layout 16 + 3 × 256; skip analysis when Off; reset on deviceAction / prepare
- [ ] T004 EQ-3: use one analyzer, layout 16 + 256
- [ ] T005 Smoke tests `--eq-analyzer`: sine level 50 Hz–15 kHz, pink flatness, flicker, fall time, silence, peak hold, timing, old project defaults, bit-identical audio
- [ ] T006 App: SpectrumFill helper; Eq8Views fill + dim input + peak + range mapping
- [ ] T007 App: EQ-8 card Range and Peak controls
- [ ] T008 App: Eq3Views fill through the helper; Eq3DeviceBody layout
- [ ] T009 CHANGELOG, FEATURES, nota-docs EN + RU (EQ-8 and EQ-3 pages)
- [ ] T010 Full smoke run, macOS check with before/after screenshots
