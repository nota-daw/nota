# Implementation Plan: Style guide — the new Nota look

**Branch**: `ui/style-guide` | **Date**: 2026-10-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/038-style-guide/spec.md`

## Summary

Redraw the app to the agreed style guide without moving or removing a control. Work goes
bottom-up so each phase only uses what the one before it finished: tokens, type and corners
first (with DESIGN.md, DESIGN.html, the design skill and the guard tests rewritten in the same
phase), then the shared controls, the shell, the device-card shell and every device, the
Modular node card, and the icons. Fixed-width digits come from Geist's own `tnum` feature
(R1), so Geist Mono goes. New tracks get a contrasting colour order through
`AutoTrackColorIndex` without touching stored indices (R4). EQ-8 and Dynamic EQ-8 move to the
EQ Eight layout; EQ-8's right column folds, with the fold kept per device for the session
(R5). No engine, ABI or file-format change. Details: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill. Both are rewritten
  in phase 1, so later phases follow the new text.
- **Device cards** → the `nota-instrument` skill's device-card section for how bodies plug in.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app); Python 3 for `scripts/design-html.py`

**Primary Dependencies**: Avalonia 12.1.4 (existing). `TextElement.FontFeatures` (inherited)
and `FormattedText.SetFontFeatures` carry `tnum`. No new dependency; Geist Mono files and
their licence row are removed.

**Storage**: N/A — EQ-8 fold is session-only view state; no project, preset or settings change

**Audio-Thread Impact**: None. UI only.

**Testing**: `scripts/build-linux.sh` (smoke test incl. `DesignTokenCheck*`), with `CI=true` in
the container. Guard tests change with the rules (token list, corner scale, one typeface, no
opacity text). Visual checks: real-app screenshots of every view and device, both Studio
themes, compared with the guide's pictures; macOS via Chris's Mac session; Windows by CI only.

**Target Platform**: macOS, Windows, Linux (desktop)

**Project Type**: desktop app

**Performance Goals**: no slower drawing than today; text shaping with `tnum` costs nothing
measurable (one feature on the shaper)

**Constraints**: every control and behaviour stays (spec principle "keep every control");
cards stay 260 high; Ember themes keep passing the readability bar

**Scale/Scope**: `Theme/*` (tokens, fonts, geometry), `Controls/*` (≈30 of 86 files),
`DeviceCardKit` and all 50 device bodies/cards, shell views (`MainWindow.axaml`, transport,
rail, `BrowserView`, `ArrangementView.*`, `SessionView.*`, `MixerView`, `PianoRollView`,
`AudioClipEditorView`, `ModularView`), icons, `DESIGN.md`, `scripts/design-html.py`,
`DESIGN.html`, `.claude/skills/nota-design`, `tests/Nota.SmokeTest/DesignTokenCheck*`,
CHANGELOG, FEATURES.md

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code |
| II. Boundary discipline | Pass — no new C ABI use |
| III. Licensing hygiene | Pass — SPDX headers on new files; Geist Mono removed with its licence row; Geist unmodified (RFN respected) |
| IV. Verification gate | Pass — build script + smoke test; guard tests updated, not weakened |
| V. One logical change per PR | **Justified exception** — see Complexity Tracking |
| VI. Docs in sync | Planned — DESIGN.md, DESIGN.html, nota-design skill, CHANGELOG, FEATURES.md; manual screenshots in a nota-docs follow-up |
| VII. Spec first | Pass — spec, research, this plan, tasks |
| VIII. Rules not weakened | Pass — the design rules are replaced, and the guard tests enforce the new ones |

Post-design re-check: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/038-style-guide/
├── spec.md
├── research.md
├── plan.md
├── quickstart.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code

```text
src/managed/Nota.App/
├── Theme/                     # tokens (Themes/*.json), NotaPalette, NotaTheme.axaml,
│                              # NotaFonts, NotaGeometry (corner scale, sizes), NotaText
├── Controls/                  # shared controls: Knob, ValueBar, MiniFader, PerformWheel,
│                              # ToggleSwitch, SliderTrack, segments, fields, Eq8Views …
├── DeviceCardKit.cs           # card shell helpers
├── DeviceCards/{Bodies,Instruments,Midi,Rack,Mosaic,Sampler}/   # the 50 devices
├── DeviceChainView*.cs        # device panel and card shell
├── MainWindow.axaml(.cs)      # toolbar, rail
├── ArrangementView.*, SessionView.*, MixerView, BrowserView,
│   PianoRollView, AudioClipEditorView, ModularView
└── Assets/ (fonts linked from assets/fonts)
assets/fonts/                  # Geist only after this change
DESIGN.md, DESIGN.html, scripts/design-html.py
.claude/skills/nota-design/SKILL.md
tests/Nota.SmokeTest/DesignTokenCheck*.cs (+ ratchet lists)
```

**Structure Decision**: no new projects; changes stay in the files that draw each thing.

## Phases

1. **Foundation (US1)** — tokens in four themes + C# mirror; Geist with `tnum` everywhere and
   Mono removed; new corner scale and type scale; guard tests; DESIGN.md rewritten,
   `design-html.py` rewritten to the guide's sections and DESIGN.html regenerated; nota-design
   skill rewritten.
2. **Controls (US2)** — icon button, segments, chips/pills, track buttons, switch, tabs,
   fields, dropdown, search; device knob with cap; value bar; shell slider (value inside);
   round wheels for Pentad; meter.
3. **Shell (US3)** — toolbar, rail, browser, track header (two rows), clips and Session slots
   (tint + outline), groups, track colour order, piano roll strip, audio editor column,
   mixer.
4. **Device cards (US4)** — card shell (raised, 32 header, power everywhere, sections, no
   status line); then every device per its guide notes; EQ-8 and Dynamic EQ-8 EQ Eight layout
   with EQ-8 fold; plugin cards on the new shell.
5. **Modular (US5)** — node card.
6. **Icons (US6)** — 24 px grid redraw; new Follow icon.
7. **Release** — CHANGELOG, FEATURES.md, build + smoke, screenshots, Mac check, PR.

Each phase is one or more commits that build and pass the smoke test on their own.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| One PR for a whole design change (V) | Chris asked for one branch implementing the guide; a half-applied design (new tokens with old controls) looks broken between PRs | Stacked PRs per phase stay possible if review needs it: each phase is its own commit range that builds on its own |
