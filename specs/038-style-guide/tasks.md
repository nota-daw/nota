# Tasks: Style guide — the new Nota look

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Format: `[ID] [P?] [Story] Description` — `[P]` can run in parallel (different files).
All paths are under `src/managed/Nota.App/` unless they start with a top-level folder.

## Phase 1: Foundation (US1) — tokens, type, corners, docs, guard tests

- [x] T001 [US1] Add the 15 new colours to `Theme/Themes/studio-dark.json`, `studio-light.json`,
  `ember-graphite.json`, `ember-paper.json` (guide values; Ember starting values) and change
  Studio Dark Panel, SurfaceRaised, TextDisabled, VizInk and Studio Light Mute
- [x] T002 [US1] Mirror the new brushes in `Theme/NotaPalette.cs` and `Theme/NotaTheme.axaml`
  (`Brush.Well`, `WellTrack`, `KnobTrack`, `Groove`, `DeviceSection`, `DeviceSectionEdge`,
  `DeviceHeader`, `Bevel`, `AccentSoft`, `KnobCap`, `MeterSafe`, `MeterHot`, `MeterOver`,
  `TrackHeaderSelected`, `LaneSelected`)
- [x] T003 [US1] One typeface: `Theme/NotaFonts.cs` drops the Mono family; `NotaTheme.axaml`
  sets `TextElement.FontFeatures="+tnum"` on the top level; a `NotaText` helper builds
  `FormattedText` with `tnum`; migrate every `NotaFonts.Mono*` / `Font.Mono` use (258 in 126
  files) and every `new FormattedText` site
- [x] T004 [US1] Remove `assets/fonts/GeistMono-*.ttf`, their project items and the Geist Mono
  part of `LICENSES/third-party.md`
- [x] T005 [US1] Type scale in `NotaType` and the TextBlock role classes in `NotaTheme.axaml`
  (Readout 17/500 … Key letter 10/650; device scale label 10.5, value 11)
- [x] T006 [US1] Corner scale in `Theme/NotaGeometry.cs` + `NotaTheme.axaml` (Docked 0, Card 9,
  Group 7, Field 6, Cell 5, ValueBar 4, Switch 3, Fine 2); migrate each `NotaRadius.*` and
  `Radius.*` use to the name for what it draws; sizes in `NotaSize` per the guide
- [x] T007 [US1] Guard tests: `tests/Nota.SmokeTest/DesignTokenCheck*.cs` — new token list in
  every theme, new radius scale (re-ratchet `RadiusAllowList.txt`), one typeface (no Mono
  files or URIs), no opacity-faded text (ratchet of today's offenders)
- [x] T008 [US1] Rewrite `DESIGN.md` for the new design (sections follow the guide; tokens per
  theme with brush names; enforced-by-tests; accepted exceptions; known drift)
- [x] T009 [US1] Rewrite `scripts/design-html.py` to the guide's sections, generated from the
  theme files, and regenerate `DESIGN.html`
- [x] T010 [US1] Rewrite `.claude/skills/nota-design/SKILL.md` to match DESIGN.md
- [x] T011 [US1] Orange in three strengths: replace solid-orange "picked" states with
  `AccentSoft` + orange text across segments, view switch, rail, tools; opacity-faded
  disabled states → ink 4
- [x] T012 [US1] Build + smoke test; screenshots of the shell in both Studio themes

## Phase 2: Shared controls (US2)

- [x] T013 [P] [US2] Icon button, segment group/cell (shell + device variants), chip, pill,
  track buttons, device number buttons (`Controls/*`, `DeviceCardKit.cs`)
- [x] T014 [P] [US2] Switch 20 × 12 (`Controls/ToggleSwitch.cs`): grey on in the shell, orange in
  devices and for power
- [x] T015 [P] [US2] Tabs (device: orange underline, 16 apart; shell: text-colour underline)
- [x] T016 [P] [US2] Field, dropdown (device preset: raised with prev/next), search
- [x] T017 [US2] Device knob 26 with centre cap, 22 with label beside; shell knob small and grey
  (`Controls/Knob.cs`)
- [x] T018 [US2] Value bar 16 in a groove, soft fill, 3 px orange line, drag anywhere
  (`Controls/ValueBar.cs`, `SliderTrack.cs`); upright variant for device faders
- [x] T019 [US2] Shell slider with the value inside (`Controls/MiniFader.cs` and callers:
  track volume, Master, sends, crossfader, quantize strength)
- [x] T020 [US2] Pentad uses `PerformWheel` (round wheels); remove the flat wheels from
  `Controls/PentadViews.cs`
- [x] T021 [US2] Meter: 3 px channels on the well, MeterSafe/Hot/Over at 70 / 88 %

## Phase 3: Shell (US3)

- [x] T022 [US3] Toolbar 44: plain values with ink-3 units, group dividers, Overdub/Tap pills,
  tool icon buttons, Master as a flat bar (`MainWindow.axaml`, transport views)
- [x] T023 [US3] Left rail 40 with the orange tick; browser rows 30, search 30, chips 24, footer
  30 (`BrowserView`)
- [x] T024 [US3] Track header 229 × 54 in two rows, pan knob, colour bar, meter, selected
  header + lane colours (`ArrangementView.*`)
- [x] T025 [US3] Clips: 20 % tint, content in full colour, selection outline in text colour
  (`ArrangementView.*`); Session slots the same (`SessionView.*`)
- [x] T026 [US3] Groups: group-colour bar through members; Session/Mixer spanning bar and
  member dots (`ArrangementView.*`, `SessionView.*`, `MixerView`)
- [x] T027 [US3] New-track colour order in `ArrangementView.AutoTrackColorIndex`
- [x] T028 [US3] Piano roll settings strip above the notes (`PianoRollView`)
- [x] T029 [US3] Audio editor in one 232 column (`AudioClipEditorView`)
- [x] T030 [US3] Mixer strips with flat sliders and grey knobs (`MixerView`)

## Phase 4: Device cards (US4)

- [x] T031 [US4] Card shell: raised card (corner 9, bevel), 32 header with name, preset menu,
  type tag, power switch on every device; pressed-in sections (8 / 10 / 12); no status line
  (`DeviceCardKit.cs`, `DeviceChainView*.cs`, `DeviceCards/DeviceCardContext.cs`,
  `Midi/MidiCardHeader.cs`, `Rack/RackCardView*.cs`, `PluginCard.cs`)
- [x] T032 [US4] EQ-8 EQ Eight layout + fold (920 / 776) (`Bodies/EqDeviceBody.cs`,
  `Controls/Eq8Views.cs`)
- [x] T033 [US4] Dynamic EQ-8 EQ Eight layout with activity bars (`Bodies/DynamicEqDeviceBody.cs`)
- [x] T034 [P] [US4] Instruments per guide notes: Aurora, Bass, Consort, Flux, Grain, Keys,
  Monolith, Mosaic, Operator, Pendulum, Pentad, Physical, Rhythm, Sampler, Synth, Volt
  (`DeviceCards/Instruments/*`, `Mosaic/`, `Sampler/`)
- [x] T035 [P] [US4] Audio effects per guide notes: Auto Filter, Auto Shift, Beat Repeat,
  Ceiling, Chamber, Chorus, Compressor, Crush, Delay, EQ-3, Flanger, Forge, Lens, Level,
  Orbit, Phaser, Prism, Reverb, Shutter, Strata, Utility, Valve, Vintage, and Vocoder
  (`DeviceCards/Bodies/*`)
- [x] T036 [P] [US4] MIDI effects: Arp, Chord, Length, Random, Scale, Velocity (`DeviceCards/Midi/*`)
- [x] T037 [P] [US4] Racks: Audio Effect Rack, Drum Rack, Instrument Rack (`DeviceCards/Rack/*`)
- [x] T038 [US4] Screenshots of every device in both Studio themes, compared with the guide

## Phase 5: Modular (US5)

- [x] T039 [US5] Node card 214 wide, tinted header, power on every device, aligned edge ports
  with per-signal shapes, value-bar parameter list, orange selection (`ModularView`)

## Phase 6: Icons (US6)

- [x] T040 [US6] Redraw every icon on the 24 grid with a 1.6 line; new Follow icon and tooltip

## Phase 7: Release

- [x] T041 CHANGELOG `[Unreleased]`, FEATURES.md (EQ-8 band row and fold)
- [ ] T042 (open: the guide is the shared style-guide page, not a repo file) Correct the style guide's EQ-8 sizes (920 / 776) and add a Vocoder picture
- [x] T043 `scripts/build-linux.sh` with `CI=true`; fix anything red
- [x] T044 Mac check through the Mac session (build, smoke test, hands-on look)
- [x] T045 Push `ui/style-guide`, draft PR against develop with before/after images in
  `pr-assets/pr-NNN/`, then ready for review when CI is green
- [x] T046 Follow-up nota-docs PR: re-shoot manual screenshots (EN + RU)

## Phase 8: Review follow-ups (FR-035 – FR-040)

- [x] T047 Key picker: keyboard + Major / Minor (Controls/KeyPicker.cs), both callers
- [x] T048 Large track header: send rows (ArrangementView), synced on the meter tick
- [x] T049 Shell scale −1 pt: NotaTheme.axaml, NotaType, shell literals; DESIGN.md table
- [x] T050 Text size preference (settings, Preferences → Appearance, live re-apply)
- [x] T051 Device header strip: lift the first tab row into the header (DeviceChainView.Shell,
  the four tab helpers); audit every card
- [ ] T052 Build + smoke, Mac hands-on, docs re-shoot, CHANGELOG / FEATURES

## Dependencies

Phase 1 → Phase 2 → Phases 3 and 4 (independent of each other) → 5 → 6 → 7. Inside Phase 4,
T031 comes first; T034–T037 run in parallel after it.

## Nota Compliance (re-check)

- [x] SPDX header on every new file
- [x] No new dependency; Geist Mono removed with its licence row
- [x] No audio-thread or ABI change
- [x] Guard tests updated, none weakened without a ratchet
- [x] CHANGELOG and FEATURES.md updated; DESIGN.md, DESIGN.html and nota-design rewritten
