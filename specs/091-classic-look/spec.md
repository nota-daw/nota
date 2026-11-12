# Feature Specification: Classic look

**Feature Branch**: `feat/classic-look`

**Created**: 2026-11-11

**Status**: Draft

**Issue**: [nota-daw/nota#572](https://github.com/nota-daw/nota/issues/572)

**Input**: User description: "Classic look: a switch back to the 0.59.2 appearance (issue nota-daw/nota#572). The new look (#296) stays the default; Preferences → Appearance gets a "Look: New / Classic" switch, stored with appearance settings and applied live, working with any of the four colour themes. Classic = the 0.59.2 palettes of the four built-in themes (tokens added in #296 get values that read like 0.59.2), the 0.59.2 corner scale (controls and cards 3, clips 2, bars 1, modals 4) via the skin radius steps from #336, and Geist Mono back in the bundle with a "measurement" text role (values, units, counters, tempo, position): Geist with tabular figures in the new look, Geist Mono in classic. With a user skin worn, the skin wins wherever it sets something (colours always, font and radii when set); Classic supplies radii and fonts the skin leaves unset; skins get an optional second font slot for the measurement role. MCP theme tools can read and set the look; previews render in either. Out of scope: pre-#296 layouts (device cards, toolbar, track headers, control shapes stay as now)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Related specs**: [038 style guide](../038-style-guide/spec.md) (the new look this
switches away from), [087 user skins](../087-user-skins/spec.md) (theme file format 2,
typeface and radius settings, MCP theme tools), [011 colour themes](../011-colour-themes/)
(the four built-in themes and the token catalogue).

**Background.** Version 0.59.2 is the last release before the new look (#296). Testers
who tried the new look asked for three things from 0.59.2 back: the colour themes as they
were, the tighter corners, and the pairing of Geist for words with Geist Mono for values
and readouts. The new look stays the default; this feature adds a way to choose the
classic appearance instead. Layouts drawn for the new look (device cards, toolbar, track
headers, control shapes) are not part of the switch.

## Clarifications

### Session 2026-11-11

- Q: Where does Classic set values in the monospaced face — everywhere, or only where
  0.59.2 did? → A: Only where 0.59.2 did; value displays added after 0.59.2 stay in
  Geist under both looks.
- Q: If a 0.59.2 colour fails today's readability check, does Classic lift it or keep it?
  → A: Keep it exactly as in 0.59.2; readability warnings are allowed for the classic
  palettes.

### Session 2026-11-11 (scope widened)

- Q: Can the classic look also bring back 0.59's solid clips? → A: Yes — #572 was widened
  with a Clips: Solid / Tinted choice. Solid is 0.59.2's Arrangement clip; Session slots were
  a tint in 0.59.2 too and are not part of it. The default follows the look (Classic → Solid,
  New → Tinted); a choice the user makes is kept whatever the look.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Switch to the classic look (Priority: P1)

A user who prefers 0.59.2 opens Preferences → Appearance and sets **Look** to
**Classic**. The whole app changes at once, without a restart: the colours of the chosen
colour theme turn back to their 0.59.2 values, corners become as tight as in 0.59.2, and
values and readouts (tempo, position, dB, Hz, ms, percentages, counters) are set in a
monospaced face wherever 0.59.2 set them so, while names and words stay in Geist. On the next launch Nota opens in the
classic look. Setting Look back to **New** restores today's appearance just as live.

**Why this priority**: This is the request itself. Without it, nothing else matters; with
only this, the users who asked are served.

**Independent Test**: With a built-in theme worn, flip Look to Classic and compare the
main window, mixer, a device card and the piano roll against 0.59.2 screenshots of the
same theme: colours, corners and the two typefaces match; restart and confirm the choice
held; flip back and confirm today's look returns.

**Acceptance Scenarios**:

1. **Given** a fresh install, **When** the user opens Preferences → Appearance, **Then**
   Look shows **New** and the app looks exactly as it does before this feature.
2. **Given** Look is New and a built-in theme is worn, **When** the user picks Classic,
   **Then** within a moment every open window — main window, Preferences, open plug-in
   and device windows, menus, dialogs and tooltips opened afterwards — wears the 0.59.2
   palette of that theme, the 0.59.2 corner scale and the classic typefaces, with no
   restart and no change to any layout size.
3. **Given** Look is Classic, **When** the user switches between the four colour themes,
   **Then** each one appears in its 0.59.2 colours.
4. **Given** Look is Classic, **When** Nota is quit and started again, **Then** it opens in
   the classic look, splash and first frame included, with no flash of the new look.
5. **Given** Look is Classic, **When** the user picks New, **Then** the app returns to
   today's appearance, live, and the choice is kept.

---

### User Story 2 - Classic with a user skin (Priority: P2)

A user who wears their own skin also likes the classic typefaces and corners. With
Look set to Classic, the skin's own choices are kept wherever it makes them (its colours
always; its typeface and radii when it sets them), and Classic fills in what the skin
leaves open. A skin author can also name a typeface for values and readouts, next to the
existing typeface for everything else.

**Why this priority**: Skins shipped in #336 just before this; users already wear them,
and the switch must not silently override a skin or behave unpredictably with one.

**Independent Test**: Wear a skin with no typeface or radius settings, flip to Classic:
colours stay the skin's, corners and typefaces turn classic. Wear a skin that sets a
typeface and roundness 0, flip to Classic: nothing the skin sets changes, only the
values-and-readouts face turns monospaced (if the skin leaves that slot empty).

**Acceptance Scenarios**:

1. **Given** a user skin with colours only, **When** Look is Classic, **Then** the skin's
   colours are worn unchanged, and corners and typefaces are classic.
2. **Given** a user skin that sets a typeface and radii, **When** Look is Classic,
   **Then** the skin's typeface and radii are worn; only what the skin leaves unset comes
   from Classic.
3. **Given** a user skin that sets a typeface for values and readouts, **When** worn
   under either look, **Then** values and readouts use that typeface.
4. **Given** a skin file in the current format without the new slot, **When** loaded,
   **Then** it loads as before; a file using the new slot declares a newer format
   version, and older Nota versions list it as a load problem naming the format version,
   as they do for any format they do not know.
5. **Given** a user duplicates a built-in theme while Look is Classic, **When** the copy
   is created, **Then** it carries the colours the user sees (the 0.59.2 palette), so a
   classic theme can be the start of a user skin.

---

### User Story 3 - Look from MCP clients (Priority: P3)

An MCP client (an AI assistant designing a skin, or a script) can read which look is
worn, set it, and render the theme preview images in either look without changing what
the user sees.

**Why this priority**: The skin design loop from #336 runs over MCP; without this a
client cannot show or test how a skin reads under Classic.

**Independent Test**: Over MCP, read the current look, set Classic, confirm Preferences
shows Classic; render previews of one theme in New and in Classic and compare: only
colours, corners and the two typefaces differ, and the live window did not change.

**Acceptance Scenarios**:

1. **Given** an MCP client, **When** it reads the appearance state, **Then** it learns the
   current look alongside the current theme.
2. **Given** an MCP client, **When** it sets the look, **Then** the app changes as from
   Preferences, stores the choice, and Preferences (if open) shows it.
3. **Given** an MCP client, **When** it asks for previews of a theme in a named look,
   **Then** the images show that look and the live window keeps the user's look.
4. **Given** an MCP client, **When** it reads a built-in theme with a look named,
   **Then** it receives that look's colours (so the classic palette can be saved as a
   user skin).

---

### User Story 4 - Solid or tinted clips (Priority: P2)

A user who preferred 0.59's arrangement wants clips drawn as solid blocks of the track
colour again. Under Classic that is what they get by default; under New clips stay a tint.
**Preferences → Appearance → Clips: Solid | Tinted** lets anyone pick either, in either look.

**Why this priority**: Users named the solid clips as part of "how it was"; it is the most
visible surface after the colours.

**Independent Test**: Fresh settings: under New clips are tinted, switch Look to Classic and
they turn solid; pick Tinted, switch Look back and forth — they stay tinted; restart — the
pick holds.

**Acceptance Scenarios**:

1. **Given** no clip style was ever picked, **When** Look is New, **Then** Arrangement clips
   are tinted (today's drawing); **When** Look is Classic, **Then** they are solid.
2. **Given** Look is Classic, **When** the user picks Tinted, **Then** clips turn tinted at
   once and stay tinted when Look changes or Nota restarts.
3. **Given** Look is New, **When** the user picks Solid, **Then** clips turn solid at once:
   a full track-colour body, a title band in the track colour mixed 30 % toward black (the
   accent when selected) with the name in readable ink, notes or waveform in the track
   colour mixed 58 % toward black, no outline.
4. **Given** solid clips, **When** a clip is short, deactivated, being resized, or shows its
   ADSR, **Then** each reads as it did in 0.59.2 (a 2 px band on a short row, the dark veil,
   the trim-edge bar, the curve in the content ink).

---

### Edge Cases

- **Typeface missing.** The classic monospaced face ships with Nota, so it is always
  present. A skin's values-and-readouts typeface that is not installed falls back to the
  look's default for that role (Geist with tabular figures under New, the bundled
  monospaced face under Classic), the same way the skin's main typeface already falls back.
- **Glyphs the monospaced face lacks** (Cyrillic in the Russian UI, the thin space,
  symbols like ■ ▾ ✓ ★): per-character fallback, as in 0.59.2; no tofu boxes.
- **Width of values.** Monospaced digits are wider than Geist's tabular ones. A value that
  fits its field, cell or knob label under New must not clip, overlap or wrap under
  Classic at 100 % and at the largest UI scale.
- **Text sized for Geist.** Where the new look sized a field to its text, Classic must not
  push neighbouring controls or change layout sizes; the text may be tighter, never cut.
- **Readability.** A 0.59.2 colour that falls below the readability bar the theme
  validator enforces today keeps its 0.59.2 value; the validator's readability warnings
  for it are accepted for the classic palettes and listed in the plan.
- **Tokens without a 0.59.2 value.** The 15 colour tokens added by the new look (wells,
  groove, pressed-in device sections, device header, bevel, knob cap, soft accent, meter
  zones, selected track header and lane) get classic values chosen to read like 0.59.2:
  e.g. wells and grooves in the 0.59.2 control and track fills, the soft accent as the
  0.59.2 selection tint.
- **Accent option.** The accent choice in Appearance applies under both looks.
- **Try-on.** An unsaved try-on theme from MCP is worn under the current look; reverting
  returns to the stored theme under the current look.
- **Switch while busy.** Changing the look during playback or recording causes no audio
  dropout and does not stop the transport.
- **Nota Remote.** The phone remote keeps its own new look; the switch covers the
  desktop app only.

## Requirements *(mandatory)*

### Functional Requirements

**The switch**

- **FR-001**: Preferences → Appearance MUST offer a **Look** choice with two options,
  **New** and **Classic**, next to the colour theme choice. New MUST be the default for new
  and existing installs.
- **FR-002**: Changing Look MUST apply to every open window at once without a restart,
  and to every window, menu, dialog and tooltip opened afterwards.
- **FR-003**: The chosen look MUST be stored with the other appearance settings and worn
  from the first frame of the next launch, splash included.
- **FR-004**: Look MUST be independent of the colour theme choice: any of the four
  built-in themes and any user skin can be worn under either look.
- **FR-005**: Changing Look MUST NOT change any layout size, control shape, device card
  arrangement, toolbar, track header or other layout introduced by the new look; it
  changes colours, corner radii and typefaces only.

**Classic colours**

- **FR-006**: Under Classic, each of the four built-in themes MUST wear its 0.59.2 colour
  values for every token that existed in 0.59.2.
- **FR-007**: Under Classic, each colour token added by the new look MUST have a value
  per built-in theme that reads like 0.59.2. Every classic palette MUST pass the same
  format checks as the built-in themes do today; readability warnings are allowed where
  they come from a 0.59.2 value, and only there.

**Classic corners**

- **FR-008**: Under Classic, corners MUST follow the 0.59.2 scale — 3 for controls, cards,
  panels and popovers, 2 for keys, rows and badges, 0 for docked panels — mapped onto
  today's radius steps. Where one step now covers parts that differed in 0.59.2, the step
  takes the controls' value: modal windows and clips get 3 (were 4 and 2), bars inside
  plots 2 (were 1) (plan, research R4).

**Typefaces**

- **FR-009**: The app MUST distinguish a **values and readouts** text role — numbers with
  their units, tempo, time position, counters, measurements and format badges — from the
  role used for names, labels and words. The role MUST cover exactly the value displays
  that 0.59.2 set in the monospaced face, including those whose surface was redrawn by
  the new look (the same value on the same device, panel or bar keeps the role). Value
  displays added after 0.59.2 use the words role (Geist) under both looks.
- **FR-010**: Under New, the values-and-readouts role MUST look exactly as it does today
  (Geist with tabular figures); under Classic, it MUST use Geist Mono, which MUST ship with
  the app again. Names, labels and words use Geist under both looks.

**User skins**

- **FR-011**: When a user skin is worn, its colours MUST be used under both looks; its
  typeface and radius settings MUST be used where it sets them; Classic MUST supply only
  the radii and typefaces the skin leaves unset.
- **FR-012**: The theme file format MUST gain an optional typeface for the
  values-and-readouts role; files without it MUST load exactly as before; the format's
  validation MUST report a malformed value with a reason that names the field.
- **FR-013**: Duplicating a built-in theme MUST copy the colours of the look being worn.

**MCP**

- **FR-014**: The MCP theme tools MUST let a client read the current look and set it; a
  set MUST behave as the Preferences switch does (live, stored, reflected in Preferences
  if open, run on the UI thread).
- **FR-015**: The MCP preview and read tools MUST accept a look, render or return that
  look without changing the live window, and default to the worn look when none is named.

**Clips**

- **FR-019**: Preferences → Appearance MUST offer **Clips: Solid | Tinted**. Tinted MUST draw
  Arrangement clips exactly as today. Solid MUST draw them as 0.59.2 did (User Story 4,
  scenario 3), with today's corner radius for the step clips use.
- **FR-020**: With no stored pick, the clip style MUST follow the look (Classic → Solid, New
  → Tinted) and change with it live; a pick MUST be stored and win over the look. Session
  slots, the clip editors and other views are unchanged.

**Quality**

- **FR-016**: Values and readouts MUST NOT clip, overlap or wrap under Classic on any
  surface covered by the theme preview set, at 100 % and at the largest UI scale.
- **FR-017**: The design checks that guard the new look (one typeface, radius scale,
  token use) MUST keep guarding it under New, and MUST accept Classic's second typeface
  and corner scale without loosening the checks for New.
- **FR-018**: All new interface text MUST be available in English and Russian.

### Key Entities

- **Look**: the appearance mode, New or Classic; one per user; stored with the appearance
  settings; independent of the theme.
- **Clip style**: Solid or Tinted, or unset (follows the look); one per user.
- **Classic palette**: per built-in theme, the full set of colour tokens — 0.59.2 values
  plus classic values for the tokens added since.
- **Classic corner scale**: the 0.59.2 radii expressed per today's radius steps.
- **Text role**: *words* (names, labels, prose) or *values and readouts*; each maps to a
  typeface per look, which a skin may override.
- **Theme file (skin)**: the #336 format, extended with an optional values-and-readouts
  typeface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from the new look to the classic look in under 15 seconds
  (open Preferences, one choice), and the change shows in under 1 second with no restart.
- **SC-002**: For each of the four built-in themes, every colour token that existed in
  0.59.2 matches its 0.59.2 value under Classic (100 %, no exceptions).
- **SC-003**: Side-by-side with 0.59.2 on the same theme, testers who asked for the old
  appearance identify the classic look's colours, corners and typefaces as "the old
  look" in at least 4 of 5 comparisons (main window, mixer, device card, piano roll,
  Preferences).
- **SC-004**: Under New, the theme preview images are identical to those rendered
  before this feature for every surface drawn from the sample scene (no regression for
  the default look).
- **SC-005**: Under Classic, no value or readout is clipped or overlapping on any preview
  surface at 100 % and the largest UI scale (0 defects in a visual check of the set).
- **SC-006**: Every user skin in the #336 test set loads and looks the same under New as
  before this feature, and changes under Classic only in what it leaves unset.
- **SC-007**: Switching the look during playback causes no audible dropout or xrun.
- **SC-008**: Under New with no clip-style pick, Arrangement previews are pixel-identical to
  those before this feature; under Solid a clip matches its 0.59.2 drawing in body, band,
  name and content colours.

## Assumptions

- "0.59.2 appearance" means the colours, corners and typefaces at tag `v0.59.2`; it does
  not include 0.59.2's control shapes or layouts (issue #572, out of scope).
- Geist Mono is under the SIL OFL 1.1 like Geist and was bundled until #296, so bringing
  it back adds no new dependency; its licence entry returns with it.
- The Look choice is a per-user appearance setting like the accent option, not a
  per-project one.
- Nota Remote, the docs site and the marketing site keep the new look; user docs gain a
  short note on the switch, with screenshots in the default (new) look.
- Classic is maintained going forward: new colour tokens get a classic value, enforced by
  the design checks. New value displays are not required to take the values-and-readouts
  role (see Clarifications), so the inventory of role uses is fixed by 0.59.2.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned (Geist Mono returns, OFL 1.1, row restored)
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI-only change, no engine path touched
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no crossing added
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (no engine change; design/layout checks in the smoke test cover both looks)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language) — DESIGN.md gains a Classic section
