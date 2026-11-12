# Feature Specification: Phrase lines in the arrangement

**Feature Branch**: `092-phrase-lines`

**Created**: 2026-11-11

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#517: "See the arrangement's phrase structure at a glance.
[…] An option in **Settings → Appearance**: **Phrase lines — Off / every 4 / 8 / 16 / 32
bars**, off by default. When on, the arrangement draws a heavier grid line (a step above the bar
line) every N bars, across the lanes and on the ruler, counting from bar 1. The line is a neutral
grey from the theme, not the accent, and works in all four themes. It stays behind clips. It
counts bars, not beats, so it follows time-signature changes once those exist (#421).
Optionally the same lines in the piano roll's and audio editor's timeline. The choice is a user
preference, not saved in the project."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/*`) — the arrangement's grid and ruler, Settings
- [x] UI (Avalonia) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: the arrangement draws one vertical line per snap-grid
position at the drawn step (`SnapGrid.GridLines`), weighted bar › beat › sub-beat with the
`GridBar`, `GridBeat` and `GridSubBeat` tokens, on the track lanes and the return / master rows
(`ArrangementView.DrawGrid`). The ruler draws a 7 px `GridBar` tick at every bar and names every
bar or every 4th. Settings → Appearance already holds arrangement looks (Waveform, Track
headers) as segmented controls saved in `settings.json`; saving fires `Settings.Changed`, which
the main window applies to the arrangement at once.

## User Scenarios & Testing

### User Story 1 - See where a phrase starts (Priority: P1)

A producer arranges a song in 8-bar phrases. They open Settings → Appearance, set **Phrase
lines** to **8**, and every 8th bar (1, 9, 17 …) carries a heavier line through every lane, the
return and master rows and the ruler.

**Independent Test**: set Phrase lines to 8 in a 4/4 project; bars 1, 9 and 17 draw the
heavier line on the lanes and the ruler, bar 5 draws an ordinary bar line.

**Acceptance Scenarios**:

1. **Given** Phrase lines Off (the default), **Then** the arrangement looks as before.
2. **Given** Phrase lines set to N, **Then** the bar lines at bars 1, N+1, 2N+1 … draw a line a
   step heavier than a bar line, on the lanes, the footer rows and the ruler (the full ruler
   height there), behind the clips.
3. **When** the setting changes, **Then** the open arrangement repaints at once.
4. **When** Nota restarts, **Then** the choice is kept; opening another project keeps it too.

### User Story 2 - Readable in every theme (Priority: P1)

The line is a neutral grey that reads one step above the bar line in Studio Dark / Light,
Ember Graphite / Paper and in a user theme, and never takes the accent.

**Acceptance Scenarios**:

1. **Given** any theme, **Then** the phrase line sits between the bar line and the tertiary
   text in tone, and follows a theme switch without a restart.
2. **Given** a user theme file from before this feature, **Then** it still loads (no new token
   is required).

### Edge Cases

- **Zoomed far out**: the grid's coarsest step is 4 bars and every phrase length is a multiple
  of every bar step, so a phrase line is always a drawn grid line; it only changes the pen,
  never adds lines between the grid's own.
- **Odd meters**: a phrase counts bars — in 3/4, every 8 bars is every 24 beats.
- **Ruler in time mode** (the read-out on m:ss): its ticks are seconds, not bars, so it draws
  no phrase lines; the lanes still do.
- **A hand-edited settings value** (3, 64, −4) reads as Off.

## Requirements

### Functional Requirements

- **FR-001**: Settings → Appearance MUST offer **Phrase lines**: Off, 4, 8, 16, 32 bars; Off by
  default; saved as a user preference, never in the project.
- **FR-002**: When on, the arrangement MUST draw the bar lines at bars 1, N+1, 2N+1 … with a
  phrase pen a step above the bar line on the lanes and the return / master rows, behind clips.
- **FR-003**: The ruler MUST draw the same bars' line in the phrase pen over its full height.
- **FR-004**: The phrase pen MUST be a neutral grey derived from the theme, never the accent,
  and MUST NOT add a theme token (user themes must keep loading).
- **FR-005**: Phrase positions MUST count bars, following the project's time signature.
- **FR-006**: A change MUST apply to the open arrangement at once.

## Success Criteria

- **SC-001**: With Phrase lines on, the start of every N-bar phrase is visible without counting
  bar numbers on the ruler.

## Assumptions

- The piano roll and audio editor timelines (optional in the issue) are left out: their
  clip-relative bar counts make "from bar 1" ambiguous inside a clip; the arrangement is the
  view the issue is about.
- Time-signature changes along the song (#421) don't exist yet; with a single meter "every N
  bars" is every N × beats-per-bar beats from beat 0.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: none — UI-thread drawing and a setting
- [x] No new managed/native crossings
- [x] Smoke-test impact: `PhraseLinesTests` cover the choices, normalising, which bars carry a
  line in several meters, and that every phrase line is a drawn grid line
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (neutral grid ink, segmented control like Waveform)
