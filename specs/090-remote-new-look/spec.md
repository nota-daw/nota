# Feature Specification: Nota Remote in the new look, with clip previews in Scenes

**Feature Branch**: `feat/remote-new-look`

**Created**: 2026-11-11

**Status**: Draft

**Input**: User description: "Nota Remote: restyle to the new look from the style guide (GitHub issue nota-daw/nota#556, approved). Restyle every Nota Remote screen and overlay (src/web/remote: header, transport, screen switcher; Scenes, Mixer, Keys, Pads, XY, Macros, Big transport; sheets, banners, pairing/empty states) to the spec 038 design language in DESIGN.md: flat grey shell where track colours carry the content, orange in three strengths (solid for live, soft tint for picked, orange lines only for values inside devices), Geist with tabular figures (no Geist Mono), sentence-case labels, the 038 corner scale, U+2212 minus and thin-space units. Colours keep coming from the theme tokens Nota sends (spec 014), never hand-copied hex. Must account for the new theme support: user skins (spec 087, theme file v2 with font.family and radius scale/steps) and the accent colour (spec 084) — the phone should wear the skin's corner radii and, where the phone has it, its typeface (falling back to Geist), following the desktop theme or the phone's own pick (Follow Nota / Follow phone). Keep touch targets, contrast and readability on phone and tablet in portrait and landscape no worse. No change to Remote protocol behaviour beyond adding the skin's font/radius to the theme message — look only. Evidence: before/after screenshots of each screen on phone and tablet in each theme."

**Input (extension, 2026-11-11)**: "Extend this branch with nota-daw/nota#557 (approved): show clip contents in the Remote Scenes grid as the desktop Session view does — a mini piano-roll of a MIDI clip's notes, an audio clip's waveform peaks — in the track colour, behind the name, play mark and progress line; updated when a clip is edited, recorded or replaced; a recording clip shows its growing content; sent compactly and only on change so a big grid loads neither the Wi-Fi link nor the audio thread."

**Issues**: nota-daw/nota#556 — "Nota Remote: restyle to the new look from the style guide" (approved);
nota-daw/nota#557 — "Nota Remote: show clip contents (notes or waveform) in the Session grid, as on the desktop" (approved).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — the theme message and a new clip-preview message Nota sends the phone (`Nota.Remote`)
- [x] UI — Nota Remote, the phone web app (`src/web/remote`); follows `DESIGN.md` (spec 038)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Builds on spec 014 (Nota Remote wears Nota's theme, live), spec 038 (the style guide, which
moved the desktop to the new look after 014 shipped), spec 084 (accent colour worn over any
theme) and spec 087 (user skins: colours, typeface, corner radii). The clip previews (#557)
mirror the desktop Session slot (`SessionView.Grid.cs`: the note preview, `GetSessionSlotPeaks`,
the recording preview of #56 / spec 086).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md),
[`../014-remote-studio/spec.md`](../014-remote-studio/spec.md),
[`../087-user-skins/contracts/theme-file-v2.md`](../087-user-skins/contracts/theme-file-v2.md).

## Clarifications

### Session 2026-11-11

- Q: Should the phone get its own pick of a specific theme or skin, beyond Follow Nota / Follow phone? → A: No — keep the two modes; skins (with typeface and radii) work in both. No new theme UI on the phone.
- Q: How are values drawn on the Macros and XY screens? → A: In the playing track's colour (a quiet tint of it as the fill, a solid line/handle in it), not orange; Mixer values stay grey.
- Q: How is a pad or key under the finger drawn? → A: Solid accent, as now (live); a note the track itself plays keeps the track's colour.
- Q: Does the phone's Scenes cell show the clip's name along with its preview (#557)? → A: Yes, as on the desktop slot: the clip name top left, the length top right, the preview below; the name travels with the preview data.

## Background

Spec 014 already made the phone wear Nota's theme colours live, as custom properties named
after the theme tokens, with Follow Nota / Follow phone in the phone's menu. It styled the
phone in the *previous* Studio look: radii 2 / 3 / 4, Geist Mono for numbers, solid orange
for anything chosen. Spec 038 since changed the desktop: a new corner scale (2–9), one
typeface with tabular figures and no monospace, orange in three strengths (a chosen option
is a soft tint, never solid), the shell type scale, U+2212 minus and thin-space units.
Spec 087 then let a theme carry its own typeface and corner radii, which Nota does not send
the phone today. So the phone now differs from the desktop in shape, type, the meaning of
orange, and — under a user skin — in typeface and roundness.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The phone looks like the desktop app (Priority: P1)

A musician plays from Nota Remote on a phone next to the computer. Every phone screen
(Scenes, Mixer, Keys, Pads, XY, Macros, Big transport), the header with its transport, the
screen switcher (tabs in portrait, rail in landscape and on a tablet), the sheets and menus,
the banners and the pairing / empty / connection states read as the same product as the
desktop: the same flat grey shell on seams, the same corner scale, the same typeface with
fixed-width figures, the same sentence-case labels, and orange used the way the desktop
uses it.

**Why this priority**: this is what the issue asks for; everything else refines it.

**Independent Test**: connect a phone (or a phone-sized browser) to Nota in Studio Dark;
open each screen and overlay and compare it with the desktop and DESIGN.md — shapes, type,
orange, labels and numbers follow the rules below.

**Acceptance Scenarios**:

1. **Given** the transport is playing or recording, **When** the user looks at the header
   or Big transport, **Then** Play (while running) and Record (while recording) are solid
   orange with readable ink — the only solid orange on screen apart from a playing Session
   clip's own live mark.
2. **Given** a chosen option — the current screen in the tabs or rail, a chosen segment
   (octave, scale, pad bank, XY mode…), a toggle that is on (Loop, Metronome, Follow,
   Learn), the selected track or row, **When** it is drawn, **Then** it wears the soft tint
   with orange text or icon (the bright orange in dark themes, the pale one in light ones),
   never a solid orange fill.
3. **Given** the Mixer, **When** faders, pans and meters are drawn at rest, **Then** they
   are grey (values outside a device stay neutral); a fader being dragged borrows the soft
   tint with an orange line while the finger is on it, as the desktop's shell slider does.
4. **Given** the Macros and XY screens (they drive device values), **When** values are
   drawn, **Then** they use the playing track's colour — a quiet tint of it as the fill and
   a solid line or handle in it — so the track colour carries the content; with no track
   they use neutral ink.
4a. **Given** Pads or Keys, **When** a pad or key is under a finger, **Then** it is solid
   accent with derived ink (live); **When** the track itself plays a note, **Then** that
   pad or key carries the track's colour.
5. **Given** a mute, solo or arm button that is on, **When** drawn, **Then** it uses the
   theme's Mute, Solo and Record colours with derived ink, as the desktop's track buttons.
6. **Given** Scenes, Mixer, Pads and Keys, **When** tracks and clips are drawn, **Then**
   the track colours carry the content (clip fills, strip marks, pad tints) while the shell
   around them stays grey; a selected clip or slot gets an outline in primary ink, not
   orange.
7. **Given** any number on screen (tempo, position, dB, percentages, note names, values),
   **When** drawn, **Then** it uses the shell typeface's fixed-width figures (no monospace
   face), a true minus sign (−), a thin space before its unit, and fixed precision so its
   width does not jump as it changes.
8. **Given** any label, button, heading, menu item or banner text, **When** drawn,
   **Then** it is sentence case with no letter spacing; acronyms and units (MIDI, XY, dB,
   BPM, USB) keep their own case.

---

### User Story 2 - The phone wears the user's skin (Priority: P1)

A user wears a skin of their own (spec 087) on the desktop — their colours over their
accent, a typeface such as IBM Plex Sans, and square corners (roundness 0). With the phone
on Follow Nota it wears the same colours, the same corner radii and, if the phone has that
typeface, the same typeface; otherwise the phone's text stays in Geist.

**Why this priority**: the issue asks to account for the new theme support; without it a
skinned desktop and its phone look like two apps again.

**Independent Test**: on the desktop, wear a user skin with `radius.scale` 0 and a font
family the phone has (and then one it lacks); the phone repaints live with square corners
and that typeface (then Geist).

**Acceptance Scenarios**:

1. **Given** the phone on Follow Nota and Nota wearing a user skin, **When** the skin is
   picked or edited on the desktop, **Then** the phone repaints in the skin's colours within
   a second, without a reload, exactly as for a built-in theme.
2. **Given** a skin with a roundness scale and/or exact step values, **When** worn, **Then**
   every rounded corner on the phone follows the same corner scale the desktop computes for
   that skin; docked panels (header, tabs, rail) stay square.
3. **Given** a skin naming a typeface, **When** worn on a phone that has that typeface
   installed, **Then** the phone's text uses it at the same sizes and weights; **When** the
   phone does not have it, **Then** the phone uses Geist and stays fully usable.
4. **Given** the phone on Follow phone, **When** the user's dark- or light-mode pick is a
   skin, **Then** the phone wears that skin's colours, radii and typeface for the phone's
   scheme.
5. **Given** the user's accent colour (spec 084), **When** the phone is connected, **Then**
   every orange on the phone is the user's accent in all three strengths, live.
6. **Given** a built-in theme, **When** worn, **Then** the phone uses Geist and the design
   corner scale.

---

### User Story 3 - Still easy to hit at arm's length (Priority: P2)

On a phone in a stand or on a tablet on a music stand, the user still hits pads, keys,
faders and transport reliably and reads values at a glance, in portrait and landscape and
in every built-in theme.

**Why this priority**: a restyle that shrinks targets or loses contrast would make the
remote worse at its job; it is the guard rail on stories 1–2.

**Independent Test**: measure hit areas and text contrast on each screen before and after
in phone portrait, phone landscape and tablet; none gets smaller or less readable.

**Acceptance Scenarios**:

1. **Given** any interactive control, **When** measured after the restyle, **Then** its hit
   area is at least what it was before (and never under 36 px on its short side).
2. **Given** any text, **When** measured in each built-in theme, **Then** its contrast is
   no lower than before the restyle and meets the desktop's readability bar for its role.
3. **Given** text on the phone, **When** compared with the desktop scale, **Then** it may
   be larger than the desktop's (phone-sized), never smaller than it was before the restyle.

---

### User Story 4 - See what is in each clip on the phone's Scenes grid (Priority: P2)

A musician launches clips from the phone. Like the desktop's Session view, every filled cell
on the Scenes screen shows what is in the clip — the notes of a MIDI clip as a mini
piano-roll across the clip's length, the waveform of an audio clip — in the track's colour,
behind the cell's text, play mark and progress line. Clips with similar lengths or names are
told apart at a glance. Editing, recording or replacing a clip on the desktop updates its
cell on the phone; a clip being recorded shows its content growing, as on the desktop.

**Why this priority**: the second approved issue folded into this branch; it builds on the
restyled Scenes cell (story 1) but is useful on its own.

**Independent Test**: with a project holding MIDI and audio clips in Session, open Scenes on
the phone: each filled cell shows its notes or waveform matching the desktop slot; edit a
clip's notes on the desktop and the phone's cell follows within a second; record a take and
watch it grow.

**Acceptance Scenarios**:

0. **Given** a filled slot, **When** shown, **Then** the cell reads like the desktop slot:
   the clip's name top left, its length top right, the preview below.
1. **Given** a MIDI clip in a Session slot, **When** the Scenes screen shows it, **Then** the
   cell draws its notes as short bars across the clip's length, pitch from the clip's lowest
   to highest note top to bottom, as the desktop slot does.
2. **Given** an audio clip in a Session slot, **When** shown, **Then** the cell draws its
   waveform as centred peak bars scaled to the clip's loudest part, as the desktop does.
3. **Given** a clip that is stopped, queued or playing, **When** drawn, **Then** the preview
   is in the track's colour on the stopped cell, and in readable ink on the cell when it is
   filled with the track colour (playing / queued); the play mark, length and progress line
   stay on top and readable.
4. **Given** a clip edited, replaced, cleared, copied or resized on the desktop, **When**
   the phone shows Scenes, **Then** the cell's preview follows within one second.
5. **Given** a slot recording, **When** shown, **Then** the cell shows the take growing — the
   recorded peaks of an audio take, the notes of a MIDI take so far — in the record colour.
6. **Given** the Scenes screen is not shown on a phone, **When** clips change, **Then** no
   preview data is sent to that phone; when it opens Scenes, it receives the current
   previews once.
7. **Given** a group or return track, **When** the grid is shown, **Then** nothing changes
   for it (the phone's grid has no group or return columns today).

### Edge Cases

- **Older Nota** that sends no typeface or radius information: the phone uses Geist and
  the design corner scale.
- **A returning phone before Nota answers**: it paints in the colours, radii and typeface
  it last wore, as it already does for colours.
- **Skin's typeface lacks glyphs** the phone draws (−, thin space, ▸): those characters fall
  back per character to a system font; layout does not shift.
- **A wide or very condensed typeface** pushes a label past its space: the label truncates
  with an ellipsis; controls do not resize.
- **Roundness 2×** on small parts (pads, keys, Session slots): corners never exceed half
  the part's short side, so nothing turns into a pill shape that hides its edges.
- **A light theme or a skin with a pale accent**: the soft tint and the picked-option text
  still read; ink on solid orange is chosen by contrast, as on the desktop.
- **Track colours under Follow phone**: unchanged from spec 014 — track colours are Nota's.
- **Link lost**: the dimmed surface and the reconnect banner use the new look too.
- **An empty MIDI clip or a silent audio clip**: the cell shows no preview, only its text.
- **A clip with hundreds or thousands of notes**: the preview stays capped in size (notes
  merged per pitch row and time column); what is drawn still reads as the clip's shape.
- **A big grid** (e.g. 32 tracks × 32 scenes): previews are sent once per clip and then only
  for the clips that change; a phone that joins late gets them once.
- **A tiny cell** (phone portrait with many tracks): the preview scales into the cell's
  preview area and never covers the text row.
- **A clip longer than its loop / a note past the end**: drawn up to the clip's length, as
  on the desktop.

## Requirements *(mandatory)*

### Functional Requirements

**Look (all screens and overlays)**

- **FR-001**: Every Nota Remote surface — header and transport, tabs and rail, Scenes,
  Mixer, Keys, Pads, XY, Macros, Big transport, sheets and menus, banners, pairing, empty
  and connection states — MUST follow DESIGN.md (spec 038) for shapes, type, orange, labels
  and numbers, adapted only in size for touch.
- **FR-002**: Orange MUST be used in the three strengths of DESIGN.md § Orange: solid only
  for live state (Play while running, Record, a recording or playing indicator, the
  playhead/loop in Big transport, a pad or key under a finger); soft tint with orange text/icon for a picked option or a
  tool that is on. Device values on Macros and XY MUST use the playing track's colour (tint
  fill, solid line or handle), not orange. A pad or key under a finger MUST be solid accent
  (live). A picked option MUST NOT be solid orange; Mixer faders, pans and meters MUST stay
  grey at rest.
- **FR-003**: Corners MUST follow the 038 corner scale (Card, Group, Field, Cell, Value bar,
  Switch, Fine; Docked = 0) mapped to each phone part by what it is, as one set of named
  radii so a skin can re-round them all.
- **FR-004**: All phone text MUST use one shell typeface (Geist by default) with fixed-width
  figures; the monospace face MUST no longer be used or shipped with the phone app.
- **FR-005**: Every label, button, heading, menu item, banner and state message MUST be
  sentence case without letter spacing, keeping acronyms and units in their own case.
- **FR-006**: Every number MUST use U+2212 for negatives, a thin space before its unit,
  fixed precision per DESIGN.md § Numbers (dB one decimal, tempo two, percent whole), and
  an en dash for ranges.
- **FR-007**: Mute, solo and arm MUST use the theme's Mute, Solo and Record colours with
  derived ink; a selected clip or slot MUST be outlined in primary ink.
- **FR-008**: The phone MUST keep sourcing every colour from theme tokens (spec 014): no
  colour value in the phone's sources beyond the build-time first-paint fallback generated
  from the theme files.

**Theme support (skins and accent)**

- **FR-009**: Nota MUST tell the phone, per theme it sends, the theme's typeface family and
  corner radii (the resolved corner scale, or the roundness scale and step overrides), so
  the phone computes the same radii the desktop wears. Themes without them (built-ins,
  older format) mean Geist and the design scale.
- **FR-010**: The phone MUST wear the sent typeface when the phone can render it, and
  Geist otherwise; it MUST NOT download fonts from anywhere but Nota.
- **FR-011**: Follow Nota and Follow phone MUST apply typeface and radii the same way they
  apply colours (the worn theme, or the user's dark/light pick for the phone's scheme),
  live on change, and the last worn typeface and radii MUST be remembered for first paint.
- **FR-012**: The user's accent (spec 084) MUST be worn by the phone in all three strengths,
  live.
- **FR-013**: The change to the theme message MUST be additive: an older phone ignores the
  new fields, a newer phone without them falls back to Geist and the design scale. Apart from
  this and the clip-preview message (FR-018–FR-023), no Remote message or behaviour changes.

**Usability guard rails**

- **FR-014**: No control's hit area MAY shrink, and none MAY be under 36 px on its short
  side; Pads, Keys and transport keep their current sizes or grow.
- **FR-015**: No text's contrast MAY drop below its value before the restyle in any
  built-in theme, and text MUST NOT be smaller than before.
- **FR-016**: Phone portrait, phone landscape and tablet layouts MUST all be restyled and
  keep their current arrangement of controls.

**Clip previews in Scenes (#557)**

- **FR-018**: Every filled Session cell on the phone's Scenes screen MUST show a preview of
  the clip's content: a MIDI clip's notes as bars (time across the clip's length, pitch over
  the clip's own pitch range), an audio clip's waveform as centred peak bars scaled to its
  loudest bucket — the same picture the desktop slot draws.
- **FR-018a**: Each filled cell MUST show the clip's name (as the desktop slot names it)
  top left and its length top right, above the preview, truncating the name with an ellipsis.
- **FR-019**: The preview MUST be drawn in the track's colour on a stopped cell and in the
  derived ink on a cell filled with the track colour; a recording cell's preview MUST use
  the record colour. Text, play mark and progress line MUST stay above it and readable.
- **FR-020**: A clip's preview MUST update on the phone within one second when its content,
  length or kind changes on the desktop (edit, record, replace, clear, copy, resize); a slot
  being recorded MUST show its take growing at least twice a second.
- **FR-021**: Preview data MUST be sent only to phones showing Scenes, only for clips whose
  content changed since that phone last got them, and compactly — at most 64 peak buckets
  for audio and at most 128 note bars for MIDI per clip — so a 32 × 32 grid's full set fits
  in a few tens of kilobytes.
- **FR-022**: Building previews MUST NOT touch the audio thread beyond the reads the desktop
  Session view already makes (no new locks or allocations on it), and MUST NOT re-read
  every clip's content every tick: unchanged clips cost a cheap change check only.
- **FR-023**: The preview message MUST be additive: an older phone ignores it; a phone
  without previews still launches clips as before.

**Evidence**

- **FR-017**: The pull request MUST carry before/after screenshots of each screen and the
  main overlays on a phone and a tablet, in each of the four built-in themes, plus one user
  skin with a custom typeface and roundness; and the Scenes grid with MIDI and audio clip
  previews (stopped, playing, recording) on a phone and a tablet beside the desktop's
  Session view.

### Key Entities

- **Theme message (spec 014)**: what Nota tells the phone about appearance — the worn theme,
  the dark and light picks, each theme's colour tokens and the ink pair. Gains, per theme,
  an optional typeface family and corner radii.
- **Corner scale**: seven named radii (Card 9, Group 7, Field 6, Cell 5, Value bar 4,
  Switch 3, Fine 2) plus Docked 0, re-rounded by a skin's scale and step overrides.
- **Clip preview**: per Session slot — the clip's name, kind (notes or peaks), the clip length it spans,
  and either note bars (start, length, pitch, relative to the clip) or peak buckets;
  identified by track and scene, with a content signature so unchanged previews are not
  re-sent.
- **Phone part → corner name / type role map**: which corner and which type role each phone
  part takes (a pad is a Cell or a Field, a sheet is a Card…), decided in the plan.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On side-by-side screenshots of the desktop and the phone in each built-in
  theme, a reviewer finds no element where the phone uses orange, corners, typeface,
  label case or number format differently from DESIGN.md.
- **SC-002**: Changing theme, accent or skin on the desktop repaints the phone within one
  second, with no reload, in 100 % of tries.
- **SC-003**: Under a skin with roundness 0, no rounded corner remains on the phone; under
  a skin naming a typeface the phone has, 100 % of the phone's text uses it.
- **SC-004**: Every control's hit area and every text's contrast measured after the
  restyle is ≥ its value before, on phone portrait, phone landscape and tablet.
- **SC-005**: The phone's sources contain zero colour literals and zero monospace font
  references outside the generated fallback.
- **SC-006**: The phone app's download does not grow (dropping the monospace font file
  offsets any additions).
- **SC-007**: In a side-by-side check of 10 MIDI and 10 audio clips, a reviewer matches every
  phone Scenes cell to its desktop Session slot by its preview alone.
- **SC-008**: An edit to a clip on the desktop shows on the phone's cell within one second in
  100 % of tries; a recording take visibly grows on the phone while it records.
- **SC-009**: With a 32 × 32 grid of filled clips and the phone on Scenes, preview traffic
  after the first full send is zero while nothing changes, and the first full send is under
  64 KB.

## Assumptions

- "Each theme" for the screenshots means the four built-ins (Studio Dark, Studio Light,
  Ember Graphite, Ember Paper) plus one user skin; Follow Nota vs Follow phone is shown once.
- Phone sizes stay phone-sized: the desktop's type and control sizes are the design's
  proportions, scaled up for touch as spec 014 did; only the scale's relationships (which
  text is larger, which is quieter) are matched, not the desktop's point sizes.
- The phone can only render a skin's typeface if the phone itself has it installed; Nota
  does not ship the desktop's system fonts to the phone (licensing, size).
- Text size (Preferences → Appearance) and Interface scale are desktop settings and are not
  sent to the phone; the phone keeps its own sizes.
- The phone keeps exactly two appearance modes, Follow Nota and Follow phone (spec 014); it
  gets no picker of its own for a specific theme or skin.
- The Remote's behaviour, protocol (other than additive theme fields and the clip-preview
  message), screens and their
  arrangement stay as they are; this is look only.
- The preview reuses the engine reads the desktop Session view already makes
  (`GetSessionNotes`, `GetSessionSlotPeaks`, the recording peaks / notes); no engine change
  is expected. If change detection needs a cheaper signal than re-reading content, the plan
  decides it without touching the audio thread.
- The phone's grid keeps its current columns (no group or return columns), so group cells'
  child bars from the desktop are out of scope.
- The built phone page is not committed (spec #90); CI / the app build produces it.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — Geist is already bundled; Geist Mono is removed
- [x] Audio-thread impact considered: UI thread only — previews reuse the desktop Session view's existing engine reads (FR-022); no new audio-thread work
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no engine change
- [x] Smoke-test impact assessed: the theme payload is covered by the smoke test (spec 014); extend it for the typeface and radius fields and add the clip-preview message (sent on Scenes only, on change only, capped)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, FEATURES.md and the Remote docs page via nota-features / nota-docs
- [x] UI change → follows `DESIGN.md` (spec 038)
