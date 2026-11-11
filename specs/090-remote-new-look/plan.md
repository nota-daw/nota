# Implementation Plan: Nota Remote in the new look, with clip previews in Scenes

**Branch**: `feat/remote-new-look` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/090-remote-new-look/spec.md` (issues
nota-daw/nota#556 and nota-daw/nota#557).

## Summary

Restyle Nota Remote (`src/web/remote`) to the spec 038 design language and make it wear
the desktop's skins (spec 087) — typeface and corner radii join the theme message — and
show clip contents (notes / waveform, with the clip name) in the Scenes grid, sent by the
hub as a compact `clips` message only to phones on Scenes and only on change.

Approach: (1) a token layer in `app.css` — radius variables, `--font-ui`, `--picked-ink`,
live / picked utility classes — then every component moves onto it; (2) `RemoteTheme`
gains `Font` / `Radii`, filled from `SkinGeometry`; (3) a `ClipPreviews` part of the hub
with round-robin change detection and per-phone signatures; a canvas per Scenes cell.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (§ Orange, § Type, § Numbers, § Labels,
  § Corner scale, § Session slot) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: TypeScript + Svelte 5 (phone page); C# / .NET 10 (`Nota.Remote`,
`Nota.App` host side, smoke test).

**Primary Dependencies**: Svelte, Vite (existing). No new dependency; Geist Mono removed.

**Storage**: phone `localStorage` (`nota-remote:vars` gains font and radii) — no project
or settings format change.

**Audio-Thread Impact**: None. The hub runs on the UI tick; previews use the
control-side engine reads the desktop Session view already makes (`GetSessionNotes`,
`GetSessionSlotPeaks` → `Engine::getClipPeaks` const, `GetSessionClipName`,
`AudioRecordPeaksOf`). No C ABI change.

**Testing**: `tests/Nota.SmokeTest` (`DesignTokenCheck.Remote`, `RemoteTests`),
`npm run check`, visual checks via the mock and the real-hub harness.

**Target Platform**: iOS Safari 16+, Chrome 100+ (Vite target unchanged), phone and tablet.

**Project Type**: desktop app + embedded mobile web page.

**Performance Goals**: theme / skin change on the phone < 1 s; preview change < 1 s;
round-robin ≤ 24 slot reads per UI tick and only while a phone is on Scenes.

**Constraints**: `clips` first full send for 32 × 32 < 64 KB; zero preview traffic while
nothing changes; no `color-mix()` (target lacks it).

**Scale/Scope**: ~16 Svelte / TS files restyled; 2 hub files; 1 host file; 2 test files;
mock extended.

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | ✅ #556 and #557 both `approved`; both linked from the PR |
| I Real-time safety | ✅ no audio-thread change (see Audio-Thread Impact) |
| II Boundary | ✅ no C ABI change, no JUCE |
| III Licensing | ✅ SPDX headers on new files; no new dependency; a font file removed |
| IV Verification | ✅ smoke test extended (theme geometry, clips message, design scan) |
| V One logical change | ⚠️ two issues in one PR — at the user's request; both are "Remote Scenes / look" and the clip cell is designed in the new look, so splitting would restyle the Scenes cell twice. Recorded in Complexity Tracking |
| VI Docs | ✅ CHANGELOG, FEATURES.md, nota-docs Remote page (EN + RU) |
| VII Spec first | ✅ this spec |
| VIII | ✅ nothing weakened |

Post-design re-check: unchanged — the design adds no engine work and no dependency.

## Project Structure

### Documentation (this feature)

```text
specs/090-remote-new-look/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/theme-message.md   # font + r per theme
├── contracts/clips-message.md   # clip previews
└── tasks.md                     # /speckit-tasks
```

### Source Code

```text
src/web/remote/
├── index.html                    # head script: also restore font / radii (already generic over vars)
├── theme-plugin.ts               # fallback unchanged; defaults for --r-* / --font-ui live in app.css
├── public/fonts/                 # GeistMono-Medium.woff2 removed
├── mock/nota-mock.mjs            # ?skin=, clips message with demo notes / peaks
└── src/
    ├── app.css                   # radius / font / picked tokens, .live / .picked, .btn .seg .switch restyled
    ├── App.svelte                # tabs / rail: picked strength
    ├── lib/theme.svelte.ts       # wear font + radii; store them
    ├── lib/palette.ts            # cssVars(...) also emits --font-ui / --r-*
    ├── lib/types.ts              # ThemeMessage font/r; ClipsMessage
    ├── lib/fmt.ts                # NEW: db / pct / bpm / bars with − and thin space
    ├── lib/app.svelte.ts         # clips map, 'clips' handler
    ├── components/{Header,Banner,Overlays,Sheets}.svelte
    ├── components/ClipPreview.svelte   # NEW: canvas notes / peaks
    └── screens/{Scenes,Mixer,Keys,Pads,XY,Macros,BigTransport,Empty}.svelte

src/managed/Nota.Remote/
├── RemoteContracts.cs            # RemoteTheme(Font, Radii); IRemoteHost.ClipColor
├── RemoteHub.State.cs            # ThemeJson writes font / r; Push sends clips on Scenes
└── RemoteHub.Clips.cs            # NEW: previews, signatures, round-robin, JSON

src/managed/Nota.App/MainWindow.Remote.cs   # RemoteThemeOf fills Font / Radii; ClipColor

tests/Nota.SmokeTest/
├── DesignTokenCheck.Remote.cs    # radii vars, no monospace, picked ink
└── RemoteTests.cs                # theme geometry; clips on Scenes / on change / capped
```

**Structure Decision**: the existing Remote split (web page ↔ `Nota.Remote` hub ↔ host in
`Nota.App`) is kept; previews get their own hub partial file so `RemoteHub.State.cs` stays
about pushing state.

## Phases

1. **Foundation** — tokens in `app.css`, `fmt.ts`, theme geometry end-to-end (host → hub →
   phone), Geist Mono removed, smoke scan updated. Unblocks everything.
2. **US1 restyle** — header / transport / tabs / rail, sheets, overlays, banner; then each
   screen. Mixer grey, Macros / XY track colour, pads / keys live accent.
3. **US2 skins** — verify font / radii live under Follow Nota and Follow phone; stored for
   first paint.
4. **US4 clip previews** — hub `RemoteHub.Clips.cs`, host `ClipColor`, `clips` message,
   phone store + `ClipPreview.svelte`, Scenes cell layout with name / length / preview;
   recording growth; mock support; smoke tests.
5. **US3 guard rails + evidence** — hit-area and contrast measurement before / after,
   screenshots, docs, CHANGELOG, FEATURES.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Two issues (#556, #557) in one PR (principle V) | The user asked to extend this branch with #557; the Scenes cell is redesigned by both | Two PRs would restyle the Scenes cell twice and conflict in the same files; the PR keeps the two as separate commits and links both issues |
