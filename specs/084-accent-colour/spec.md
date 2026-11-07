# Feature Specification: Choose the accent colour

**Feature Branch**: `feat/accent-colour`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Issue #311: Preferences → Appearance lets the user choose the accent colour (today fixed orange in every theme). The choice is made with a colour picker in Settings: a row of preset (curated) colours plus "Custom" (the user's own colour). The chosen accent drives all accent strengths of the style guide — solid for live, soft tint for picked, accent lines for device values — in all four themes (Studio Dark/Light, Ember Graphite/Paper), tuned per theme so every theme still passes the readability bar (contrast on surfaces, ink on the accent fill) and the accent stays distinct from the record red, mute amber, solo blue and the meter green. Applies live, is saved, reaches Nota Remote."

Issue: [nota-daw/nota#311](https://github.com/nota-daw/nota/issues/311) (approved).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.App`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md). Builds on [`specs/011-colour-themes`](../011-colour-themes/)
(themes, the readability bar) and [`specs/014-remote-studio`](../014-remote-studio/) (Nota Remote wears the host's theme).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pick a preset accent (Priority: P1)

A musician who finds the orange accent tiring (or clashing with their track colours) opens
Settings → Appearance, sees a row of colour swatches under the colour theme, and clicks
one. Everything that is live or chosen — the playhead, the loop, selected clips, toggles
that are on, the play key, menu hover, the device power switch, a knob being dragged —
switches to that colour at once, in every open window. Closing and reopening Nota keeps
the choice.

**Why this priority**: This is the request in the issue; the presets alone deliver it and
are guaranteed readable in every theme.

**Independent Test**: Open Settings → Appearance, click each preset swatch, check the main
window's accent uses change instantly; restart; the choice persists.

**Acceptance Scenarios**:

1. **Given** the default appearance, **When** the user opens Settings → Appearance,
   **Then** an "Accent" row shows the preset swatches with Orange marked as chosen.
2. **Given** Studio Dark, **When** the user clicks a preset, **Then** every accent use in
   every open window (solid fills, the soft "picked" tint, accent lines) takes that hue
   without a restart, and the swatch is marked as chosen.
3. **Given** a preset is chosen, **When** the user switches the colour theme (any of the
   four, or Follow system flips), **Then** the same accent is worn in the new theme, tuned
   to that theme.
4. **Given** a preset is chosen, **When** Nota restarts, **Then** it opens with that accent
   and no frame is painted in orange first.
5. **Given** any preset, **When** the user picks Orange again, **Then** every theme looks
   exactly as it does today (same values as the theme files).

---

### User Story 2 - Use my own colour (Priority: P2)

A user wants their studio's brand colour. They choose "Custom", pick a colour in a colour
picker (spectrum + hue, and a hex field), and see it applied live. If the colour would
not be readable or would be confused with the record red, mute, solo or meter green, Nota
says so and does not wear an unreadable accent.

**Why this priority**: The user explicitly asked for "preset colours + your own"; it builds
on the P1 machinery.

**Independent Test**: Choose Custom, enter `#3FA7FF`, see the accent change; enter a colour
close to the record red, see the explanation and the accent remain readable.

**Acceptance Scenarios**:

1. **Given** Settings → Appearance, **When** the user clicks the Custom swatch, **Then** a
   colour picker opens next to it with the current accent preselected.
2. **Given** the picker is open, **When** the user drags in the spectrum or types a valid
   hex value, **Then** the accent previews live and is saved when the picker closes;
   Esc restores the accent that was worn before opening.
3. **Given** a custom colour that fails the readability bar in some theme, **When** it is
   applied, **Then** Nota keeps the colour's hue and saturation and adjusts only its lightness, per
   theme, by the smallest step that passes the bar — so the hex typed is worn exactly in a
   theme where it already passes, and a close, readable relative of it elsewhere; the
   picker says when the colour was adjusted for the current theme
4. **Given** a custom colour too close to the record red, mute amber, solo blue or meter
   green in some theme, **When** the user picks it, **Then** the picker shows a one-line
   note naming the colour it clashes with, and that colour is not applied.
5. **Given** a custom accent, **When** the user later clicks a preset and then Custom
   again, **Then** the last custom colour is offered again.

---

### User Story 3 - The phone follows (Priority: P3)

With Nota Remote connected, the phone wears the same accent as the desktop window, and
changes with it.

**Why this priority**: Remote already mirrors the theme; leaving it orange would be a
visible inconsistency, but it is secondary to the desktop.

**Independent Test**: Connect Nota Remote, change the accent in Settings, the phone's play
key, selection and toggles change within a second.

**Acceptance Scenarios**:

1. **Given** a connected phone, **When** the accent changes, **Then** the phone repaints in
   the new accent without reconnecting, in whichever theme it wears (including its own
   dark/light pick under Follow phone).

---

### Edge Cases

- A settings file from an older Nota (no accent stored) → Orange; nothing changes visually.
- A stored accent that is unknown (a preset removed later) or a malformed custom value →
  falls back to Orange, never throws, the stored value is not rewritten until the user
  picks again.
- A custom colour that cannot pass the bar in some theme at any lightness (e.g. a grey) →
  refused with a note; the previous accent stays.
- Achromatic or near-grey custom colours → refused: an accent must read as a colour
  (minimum saturation), otherwise "live" and "rest" become indistinguishable.
- Theme preview cards in the picker show each theme *with the chosen accent*, so the user
  judges the combination.
- Device identity hues (`Ink`), graph chromas, track colours, the record red, mute, solo,
  meter colours never change with the accent.
- Follow system: switching OS mode keeps the accent, tuned for the other theme.

## Clarifications

### Session 2026-11-05

- Q: How is a custom colour worn across themes? → A: Tuned per theme — hue and saturation
  are the user's, lightness is moved by the smallest step that passes the bar; exact where
  it already passes; refused only when no lightness passes.
- Q: One accent for the whole app or per theme? → A: One for all themes, tuned to each.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Settings → Appearance MUST show an "Accent" row directly under the colour
  theme picker: one swatch per preset, then a Custom swatch; the chosen one is marked by
  a selection ring (colour only, no size change).
- **FR-002**: The preset list MUST contain Orange (the default, first) and a small curated
  set of further hues (6–8 in total), each of which passes FR-005/FR-006 in all four
  built-in themes; a smoke test MUST enforce that for every preset and theme.
- **FR-003**: Choosing an accent MUST re-colour every accent token of the active theme —
  the solid accent and its hover/deep/dim steps, the soft "picked" tint, the accent line
  (brass), the accent text on light grounds, the ink on the accent, and the platform
  accent ramp used by built-in controls — and nothing else.
- **FR-004**: With Orange chosen, every theme MUST produce exactly its theme-file values
  (no visual change for anyone who never touches the setting).
- **FR-005**: For every chosen accent and theme, the result MUST meet the readability bar
  rows that involve the accent: accent line on the grounds (T3.4), derived ink on the
  accent fill (T3.5), and Accent vs Mute / Solo / Record pairwise (T3.8).
- **FR-006**: The accent MUST also stay distinct (ΔE ≥ 15) from the meter/positive green
  (`Success`) in every theme.
- **FR-007**: A custom colour MUST be chosen with a colour picker offering a
  saturation/value area, a hue strip, a hex field, and a live preview. Its hue and
  saturation are kept; its lightness is moved per theme by the smallest step that passes
  FR-005/FR-006 (none when it already passes). A colour for which no lightness passes in
  some theme MUST be rejected with a one-line reason, keeping the previous accent.
- **FR-008**: The change MUST apply live to every open window, popup and custom-drawn view,
  with no restart, and be saved to settings immediately.
- **FR-009**: The accent MUST be applied before the first window paints on startup.
- **FR-010**: The accent MUST be one choice for the whole app, worn in whichever theme is
  active (including both themes of Follow system), tuned per theme.
- **FR-011**: Nota Remote MUST receive the accented theme(s) the same way it receives the
  theme today, and repaint when the accent changes.
- **FR-012**: The picker and swatches MUST follow `DESIGN.md` (Studio): tokens only, Geist
  Mono for the hex value, sentence-case labels, no new accent use outside "live or
  chosen".
- **FR-013**: All new user-visible strings MUST go through the localization keys
  (`en.json`).

### Key Entities

- **Accent choice**: either a preset id (e.g. `orange`, `violet`) or a custom colour
  (one sRGB value). Stored in settings; absent means Orange.
- **Accent preset**: an id, a display name, and one base colour (its hue and saturation);
  the per-theme values are derived, not stored.
- **Accented theme**: a colour theme with its accent tokens replaced for the chosen accent —
  what the app, the theme previews and Nota Remote wear.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can change the accent from the default to any preset in two clicks
  from Settings → Appearance, and see it applied in under 100 ms.
- **SC-002**: 100 % of presets × themes pass every accent row of the readability bar and
  FR-006 (checked by the smoke test).
- **SC-003**: With the default accent, a pixel comparison of the main window before and
  after this change shows no difference.
- **SC-004**: After restart, the chosen accent is worn on the first painted frame in 100 %
  of launches.
- **SC-005**: A connected Nota Remote shows the new accent within 1 s of the change.

## Assumptions

- The accent applies to the four built-in themes; user-made themes (not shipped yet) will
  inherit the same mechanism.
- Accent is an app preference, not per project.
- Track colours keep their palette; selection outlines on clips are drawn so that an accent
  close to a track colour is still visible (as orange vs the amber track today, ΔE ≈ 25);
  presets are additionally kept ΔE ≥ 15 from every track colour.
- Nota Remote needs no new UI: it only wears what the desktop sends.
- Presets are named in English in `en.json`; other languages translate through the usual
  pipeline.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — the colour picker is drawn with Nota's own controls
- [x] Audio-thread impact considered: none (UI/settings only)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); none added
- [x] Smoke-test impact assessed: no engine change; the theme/readability checks
      (`RunThemes`) are extended to every preset × theme and the accent resolution logic
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md, user docs
- [x] UI change → follows `DESIGN.md` (Studio design language)
