# Feature Specification: Style guide — the new Nota look

**Feature Branch**: `ui/style-guide`

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "Let's start a new branch where we implement this new design/style
guide. Both DESIGN.html and DESIGN.md need to be completely updated, as well as anywhere else
relevant to this new UI polish." Added the same day: "In EQ-8 allow the band settings on the
right to be collapsed and expanded."

The style guide is the "Nota style guide" page agreed with Chris between 2026-10-20 and
2026-10-24 (sections Overview, Colour, Orange, Text, Shapes, Buttons, Knobs, Fields, Cards,
Icons, Devices, Changes). It redraws Nota without moving or removing any control. This spec
turns it into requirements; where the guide and this spec differ, this spec wins and the
guide is corrected.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Everything lives in `src/managed/Nota.App` plus the design docs. What the code does today
(develop at 96f58ea):

- **Tokens.** Colours come from `Theme/Themes/{studio-dark,studio-light,ember-graphite,ember-paper}.json`
  through `NotaColourTheme` / `NotaThemeService` into `Brush.*` resources (`NotaTheme.axaml`)
  and the C# mirror `NotaPalette`. There is no brush for a well, groove, pressed-in device
  section, bevel, soft accent tint, knob cap, meter zones or selected track header/lane;
  views mix those from `Surface*` brushes or literals.
- **Type.** `NotaFonts` carries two families and a hard rule: Geist for words, Geist Mono for
  every number. `NotaFonts.Mono*` / `Font.Mono` are used 258 times in 126 files.
  `DesignTokenCheck` asserts both families exist and match the XAML.
- **Geometry.** `NotaRadius` is a 0–4 scale (Docked 0, Bar 1, Clip/Badge 2, Control 3, …).
  `DesignTokenCheck` forbids `CornerRadius` literals and off-scale `RadiusX/Y` (ratchet file
  `RadiusAllowList.txt`). Sizes in `NotaSize` (shell button 26, chip 20, knobs 34/36/44).
- **Device cards.** `DeviceCardKit` (card 260 high, header 22) and `IDeviceBody` /
  `IInstrumentCard` (width 700 for almost every device). Most cards end in a status line fed
  through `DeviceCardContext.Status`. 27 effect bodies in `DeviceCards/Bodies`, 17 instrument
  cards in `DeviceCards/Instruments`, MIDI effects in `DeviceCards/Midi`, racks in
  `DeviceCards/Rack`, plus `Mosaic` and `Sampler`.
- **EQ-8 / Dynamic EQ-8.** `EqDeviceBody` / `DynamicEqDeviceBody` (700 × 260): a row of eight
  band chips over a response graph (`Controls/Eq8Views.cs`, `DynamicEqCurve`), with the band
  panel beside it. Per-card view state elsewhere (Prism's picked band and tabs) lives in a
  static dictionary keyed by track and device index, for the session only.
- **Shared controls** in `Controls/` (86 files): `Knob`, `ValueBar`, `MiniFader`,
  `PerformWheel` (round pitch/mod wheel used by Aurora, Bass, Operator, Volt), `PentadViews`
  (Pentad's flat wheels), `SliderTrack`, `ToggleSwitch`, `NotaGraph`.
- **Shell.** Toolbar and transport (`MainWindow.axaml`, transport views) box every value;
  track headers (`ArrangementView.*`) are three rows of boxed controls with a pan bar; clips
  are solid track colour with a title band and turn orange when selected; groups indent
  members; `SessionView.*` slots likewise turn orange; the piano roll (`PianoRollView`) and
  audio editor (`AudioClipEditorView`) have a narrow scrolling settings column on the left.
- **Track colours.** Nine slots `Brush.Track1..9` (green, teal, sky, blue, amber, violet,
  rose, olive, slate). New tracks take `ArrangementView.AutoTrackColorIndex(nth)`, which walks
  the slots in table order. Projects store the slot index, so the table order must not change.
- **Modular view** (`ModularView`): node cards with rows of ports that don't line up, a
  status dot and a bypass button, and a grid of large knobs.
- **Icons** are drawn on a 16 px grid with a 1.5 px line; Follow is a playhead flag on a ruler.
- **Docs.** `DESIGN.md` (883 lines, "Studio") is the written rule set; `DESIGN.html` is
  generated from the theme files by `scripts/design-html.py`. `.claude/skills/nota-design`
  repeats the rules for agents. The user manual in nota-docs has screenshots of every view
  and device.

No engine or C ABI change is expected, and no project-format change.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One set of colours, type and shapes (Priority: P1)

A musician opens any project and the whole app reads as one design: a flat grey shell, one
typeface for words and numbers, rounder corners on a single scale, and orange used in three
clear strengths (solid for what is live, a soft tint for what they picked, orange lines for
values inside a device). A contributor building a new screen finds every colour, size and
corner as a named token, and the written rules (DESIGN.md, DESIGN.html, the design skill)
describe exactly what they see.

**Why this priority**: every later story draws with these tokens. On its own it already
changes the look everywhere (type, corners, orange, greys) and gives contributors the new
rules.

**Independent Test**: switch between all four colour themes on the demo project; every
number is Geist with fixed-width digits, picked options show the soft tint (never solid
orange), and the token guard tests pass with the new scale. DESIGN.md and DESIGN.html list
the same tokens the theme files hold.

**Acceptance Scenarios**:

1. **Given** any theme, **When** a value changes (tempo, knob readout, position), **Then** it
   is set in Geist and the text beside it does not shift.
2. **Given** a segment group or a view switch, **When** the user picks an option, **Then** the
   option shows the soft orange tint with orange text, and only live things (playing, record,
   loop, power, a switch that is on inside a device) are solid orange.
3. **Given** an unavailable option, **When** it is shown, **Then** it is drawn in ink 4 with
   nothing picked, never faded with transparency.
4. **Given** the theme files, **When** DESIGN.html is regenerated, **Then** it shows the new
   tokens with their brush names for every theme, and DESIGN.md states the same values.

---

### User Story 2 - Shared controls drawn the new way (Priority: P2)

Buttons, segments, chips, pills, switches, tabs, fields, dropdowns, search, knobs, value bars,
sliders, meters and the pitch/mod wheels each look one way wherever they appear, with a
"device" variant inside device cards (grooves, a raised pick, capped knobs, orange values)
and a flat variant in the shell.

**Why this priority**: device cards and the shell are built from these controls, so redrawing
them once carries most of the change.

**Independent Test**: open a device and the mixer side by side; a device knob has a centre cap
and an orange arc, a mixer knob is small and grey; a device slider is a 16 px value bar in a
groove with an orange line, a track-volume slider is a flat bar with its value inside.

**Acceptance Scenarios**:

1. **Given** a value bar in a device, **When** the user drags anywhere on it, **Then** the value
   follows without jumping to the pointer, and double-click resets it.
2. **Given** a slider outside a device (track volume, Master, sends, crossfader, quantize
   strength), **When** the fill is too short to hold the number, **Then** the number sits just
   past the value line, and under the pointer or while dragged the bar takes the soft tint and
   an orange line.
3. **Given** any instrument with pitch and mod wheels, Pentad included, **When** it is shown,
   **Then** it uses the round wheel (never in a groove); dragging needs 140 px for the full
   range (×0.25 with Shift), double-click resets, and the pitch wheel springs back.
4. **Given** a switch, **When** it is on, **Then** it is light grey outside a device and orange
   inside a device or as a power switch.

---

### User Story 3 - A quieter shell (Priority: P3)

The toolbar, left rail, browser, track headers, clips, groups, Session slots, piano roll and
audio editor follow the guide: no boxes in the toolbar, two-row track headers with a small
pan knob, clips as a pale tint of the track colour, selection as an outline in the text
colour, a group bar running down past every member, all piano roll settings in one strip
above the notes, and the audio editor in one column that doesn't scroll. New tracks take
colours in an order where neighbours contrast.

**Why this priority**: the shell is what users see most, but it can ship after the tokens and
controls because it only re-arranges how existing controls are drawn.

**Independent Test**: on the demo project, add nine tracks; each new track's colour contrasts
with the one before it (green, blue, amber, violet, rose, teal, slate, olive, sky, then green
again). Select a clip and a Session slot: both get an outline in the text colour, not orange.
Open a MIDI clip: every setting is in the strip above the notes.

**Acceptance Scenarios**:

1. **Given** a project saved before this change, **When** it is opened, **Then** every track
   keeps the colour it had.
2. **Given** a track header, **When** it is shown, **Then** it is 229 wide and 54 high with a name
   row (name, MIDI/Audio tag, M, S, arm or monitor) and a controls row (volume, pan knob,
   input picker), a 3 px colour bar down the left and a meter down the right; every control
   it had before is still there.
3. **Given** a group, **When** it is expanded, **Then** a 4 px bar in the group colour runs from
   the group row down through every member and ends at the last one.
4. **Given** the audio editor, **When** a clip is opened, **Then** its settings fit one 232-wide
   column with Start and Length side by side and the Warp switch on its title row.

---

### User Story 4 - Device cards with depth, and every device redrawn (Priority: P4)

Every one of the 50 built-in devices (16 instruments, 25 audio effects, 6 MIDI effects, 3 racks)
sits on a raised card, always 260 high and as wide as it needs, with a 32 px header (name,
preset menu, type tag, power switch at the right on every device) and pressed-in sections.
No card has a status line; anything that lived there moves into the card. Each device
matches its picture and notes in the guide's Devices section.

EQ-8 and Dynamic EQ-8 are laid out like Ableton's EQ Eight so the graph leads: the picked
band's Freq, Gain and Q stack at the left, a wide graph runs across the middle, and a band row
under it gives each band a numbered on/off toggle and a filter-shape chooser. On EQ-8 the
right-hand Band and Global column can be folded away and brought back.

**Why this priority**: the largest body of work and the most visible change inside devices; it
depends on stories 1 and 2.

**Independent Test**: load each device from the browser onto a track and compare it with its
picture in the style guide; every control in 0.56.1 is present and works. On EQ-8, fold the
right column: the card narrows and the graph and band row stay usable; unfold it and every
Band and Global control is back with the same values.

**Acceptance Scenarios**:

1. **Given** any built-in device, **When** it is shown, **Then** the card is 260 high, has no line
   of text under it, and has a power switch at the right of its header.
2. **Given** EQ-8, **When** the user clicks a band's number in the band row, **Then** the band
   switches on or off (the same action as the band's On control); **When** they click the
   shape chooser, **Then** they can pick Low cut, Low shelf, Bell, Notch, High shelf or High cut.
3. **Given** EQ-8 with the right column shown, **When** the user clicks its fold arrow, **Then** the
   column shrinks to a narrow strip with an arrow to bring it back and the card narrows
   (920 → 776 wide); the band row, graph and left column keep working.
4. **Given** EQ-8 folded, **When** the user unfolds it, switches track, or closes and reopens the
   device panel in the same session, **Then** the column shows the same state it was left in.
5. **Given** Dynamic EQ-8, **When** a band is set to Duck or Lift, **Then** its cell in the band
   row shows an activity bar along its bottom; **When** the band is Static, **Then** the dynamics
   rows are drawn in ink 4.
6. **Given** a device whose 0.56.1 colours used teal or olive as identity colours (Orbit, Phaser,
   Flanger, Prism, EQ-3, Aurora, Keys, Mosaic, Operator, Pendulum, Physical, Rhythm), **When**
   it is shown, **Then** those marks are greys told apart by shade, and only values are orange.

---

### User Story 5 - Modular node cards (Priority: P5)

In the Modular view each device is a 214-wide card raised like a device card, with a header
tinted by category (blue instruments, green effects, grey output; plain grey when bypassed),
a power switch on every device, ports on the card edges lined up so the main cable runs
straight, a different port shape per signal (round audio, square MIDI, diamond CV), and the
parameters as a short list of value-bar rows that scrolls after eight.

**Why this priority**: self-contained view, used less than the arrangement and devices.

**Independent Test**: open the demo project's Keys track in Modular view; Pentad → EQ-8 →
Chamber → Track output connect with straight cables, bypassing Chamber greys its header, and
expanding Chamber lists its 45 parameters as bars.

**Acceptance Scenarios**:

1. **Given** a chain in Modular view, **When** nodes sit side by side, **Then** the first port row
   lines up on every node.
2. **Given** a node, **When** it is selected, **Then** it gets a thin orange outline.

---

### User Story 6 - Icons on the new grid (Priority: P6)

Every icon is redrawn on a 24 px grid with a 1.6 px line, shown at 16 px in buttons. Follow
playhead gets its own shape: a playhead with a short arrow pointing right.

**Why this priority**: polish that can land last without blocking anything.

**Independent Test**: look over the toolbar, rail, browser and context menus in both Studio
themes; no icon is blurry or clipped at 16 px, and the Follow button shows the new shape.

**Acceptance Scenarios**:

1. **Given** the toolbar, **When** Follow is on, **Then** the button shows the soft tint with the
   new Follow icon and its tooltip reads "Scroll the arrangement to keep the playhead in view".

---

### Edge Cases

- **Ember themes**: the new brushes take the guide's Ember starting values; any that fail the
  readability bar in DESIGN.md are adjusted and the adjustment recorded there.
- **UI scale**: at every UI scale step the 260-high card, 54-high track header and 16-high
  bars scale together; nothing clips.
- **Narrow windows**: a card wider than the device panel scrolls sideways as today; the EQ-8
  fold is the way to make it narrower, not shrinking the graph.
- **Long names**: track and device names trim with an ellipsis in the new header heights.
- **Nota Vocoder** was added after 0.56.1, so the guide has no picture of it. It follows the
  same rules (card shell, sections, value bars, capped knobs, no status line) and gets a
  picture added to the guide's Devices section.
- **Plugin cards** (VST/AU/CLAP) use the new card shell (header, power, sections) but keep their
  own parameter list; they are not redrawn per plugin.
- **Disabled controls inside a device** (EQ-8 Slope on a shelf band, Synth Pulse W off Square,
  Sampler Crossfade with loop off) are drawn in ink 4 with nothing picked.
- **Nota Remote** (the phone app) keeps its own styling; only shared colour tokens it reads
  change, and its guard test keeps passing.
- **Light themes**: the soft tint, grooves and bevel read in Studio Light and Ember Paper as in
  the guide's light pictures.
- **Folded EQ-8 with the band panel needed**: Slope and Channel stay reachable by unfolding;
  folding hides nothing that can't be brought back with one click.

## Requirements *(mandatory)*

### Functional Requirements

**Tokens, type and shape (US1)**

- **FR-001**: Every colour the guide names MUST exist as a theme token in all four themes, with
  the new brushes Well, WellTrack, KnobTrack, Groove, DeviceSection, DeviceSectionEdge,
  DeviceHeader, Bevel, AccentSoft, KnobCap, MeterSafe, MeterHot, MeterOver,
  TrackHeaderSelected and LaneSelected, and the changed values (Studio Dark Panel, SurfaceRaised,
  TextDisabled, VizInk; Studio Light Mute).
- **FR-002**: The app MUST use Geist for every word and number, with fixed-width digits; Geist
  Mono MUST no longer be used or shipped.
- **FR-003**: The type scale MUST be the guide's: Readout 17/500, Device name 13/600, Track name
  13/580, Body 12/400, Value 11.5/400, Label 11/400, Device label 10.5/400, Column head 10/400,
  Key letter 10/650. Tags and units are sentence case.
- **FR-004**: Corners MUST follow one scale: 9 device card and toolbar view group; 7 icon button,
  chip, segment group, device section, search; 6 field, dropdown, clip, toggle pill; 5 segment
  cell, track button, EQ band cell, tooltip; 4 value bar, fader cap, overview strip; 3 switch;
  2 switch knob, notes.
- **FR-005**: Orange MUST appear only as solid (live), soft tint with orange text (picked), or
  orange lines for values inside a device card. Knobs, faders and meters outside devices MUST
  NOT be orange.
- **FR-006**: Depth (raised, pressed in, grooves, bevel) MUST appear only inside device cards and
  Modular nodes, built from greys, thin lines and 1–2 px shadows, with no gradients or blur.
- **FR-007**: Unavailable options MUST be drawn in ink 4, never with opacity.
- **FR-008**: DESIGN.md MUST be rewritten to describe the new design in full (principles,
  tokens with values per theme and brush names, type, corners, sizes, depth, every control,
  cards, shell, Modular nodes, icons, devices, what is enforced by tests, accepted exceptions)
  and MUST no longer describe the old rules.
- **FR-009**: DESIGN.html MUST be regenerated from the theme files by the generator and show the
  same content as DESIGN.md with live examples, in the style guide's sections.
- **FR-010**: The design guard tests MUST enforce the new rules: new tokens present in every
  theme and matching the C# mirror, the new corner scale, a single typeface, and no
  opacity-faded text, with any remaining offenders listed in the ratchet files.
- **FR-011**: The nota-design skill MUST state the same rules as DESIGN.md.

**Shared controls (US2)**

- **FR-012**: Icon buttons, segments, chips, pills, track buttons, switches, tabs, fields,
  dropdowns and search MUST match the guide's "How it's drawn" sizes and states, with no
  borders (they sit on the well) and a device variant where the guide gives one.
- **FR-013**: Knobs inside devices MUST be 26 px with a centre cap and orange arc (22 px when the
  label sits beside it); knobs outside devices stay small, flat and grey.
- **FR-014**: Every slider inside a device MUST be a 16 px value bar in a groove, draggable
  anywhere, double-click to reset; vertical faders inside devices become upright value bars.
- **FR-015**: Sliders outside devices (track volume, Master, sends, crossfader, quantize strength)
  MUST be flat 16 px bars with the value written inside, keeping today's drag behaviour.
- **FR-016**: Every instrument's pitch and mod wheels MUST be the round wheel, Pentad included;
  Pentad's flat wheels go.
- **FR-017**: Track meters MUST be green to 70 %, amber to 88 %, red above, 3 px channels on the
  well.

**Shell (US3)**

- **FR-018**: The toolbar MUST be 44 high with values as plain text (unit in ink 3), groups split
  by short thin lines, Overdub and Tap as pills, tools as icon buttons, and Master as a flat bar
  with its value inside.
- **FR-019**: The left rail, browser rows, chips and footer MUST follow the guide's sizes; the
  current rail page gets a 2 × 14 orange tick and the soft tint.
- **FR-020**: Track headers MUST be two rows (229 × 54) keeping every control, with a small pan
  knob, a 3 px colour bar, a meter on the right, and a lighter background with its lane when
  selected.
- **FR-021**: Clips and Session slots MUST be a 20 % tint of the track colour with content in the
  full colour; selection MUST be an outline in the text colour, never orange.
- **FR-022**: Groups MUST show a 4 px bar in the group colour through every member (Arrangement),
  one spanning bar (Session, Mixer) and a colour dot beside each member's name.
- **FR-023**: New tracks MUST take colours in the order green, blue, amber, violet, rose, teal,
  slate, olive, sky, repeating; existing projects MUST keep their colours.
- **FR-024**: The piano roll MUST put every clip setting in one strip above the notes; the audio
  editor MUST fit its settings in one 232-wide column that doesn't scroll.

**Device cards (US4)**

- **FR-025**: Every built-in device card MUST be 260 high with a width that fits the device, a
  32 px header (name, preset menu where the device has one, type tag, power switch at the
  right), pressed-in sections 8 from the edge, 10 apart, 12 padding.
- **FR-026**: No device card MUST show a status line; each control or readout that lived there
  MUST move into the card as the guide's notes for that device say, or be dropped where the
  notes say so.
- **FR-027**: Each of the 50 devices in the guide MUST match its picture and notes in the guide's Devices
  section (widths, regrouping, colours), keeping every control and its behaviour; Nota
  Vocoder (added since) MUST follow the same rules.
- **FR-028**: EQ-8 and Dynamic EQ-8 MUST use the EQ Eight layout: picked band's Freq, Gain and Q
  at the left, the graph across the middle, a band row underneath with a numbered on/off toggle
  and a filter-shape chooser per band (plus the activity bar on Dynamic EQ-8), and the band and
  global settings at the right. Channel uses the short names St, M, S, L, R.
- **FR-029**: On EQ-8 the right-hand Band and Global column MUST fold to a narrow strip and unfold
  again with one click; folding narrows the card (920 → 776) and hides nothing that unfolding
  doesn't bring back. The fold state MUST be kept per device for the session.
- **FR-030**: Plugin cards MUST use the new card shell.

**Modular and icons (US5, US6)**

- **FR-031**: Modular nodes MUST follow the guide (214 wide, category-tinted 34 px header, power
  switch on every device, green dot on the track output, aligned edge ports with per-signal
  shapes, parameters as 18 px value-bar rows scrolling after eight, orange outline when
  selected).
- **FR-032**: Every icon MUST be redrawn on a 24 px grid with a 1.6 px line, keeping its shape,
  except Follow, which becomes a playhead with an arrow pointing right.

**Docs and release**

- **FR-033**: CHANGELOG `[Unreleased]` MUST describe the new look and the EQ-8 fold; FEATURES.md
  MUST mention the EQ-8 fold and the EQ Eight band row.
- **FR-034**: The style guide's own stale sizes MUST be corrected (EQ-8 is 920 wide, 776 folded).

### Key Entities

- **Theme token**: a named colour with a value per theme and a brush name the views use.
- **Fold state (EQ-8)**: shown or folded, per device on a track, kept for the session, not saved
  in the project.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 50 built-in devices show at 260 high with no status line and a header power
  switch, checked one by one against the guide.
- **SC-002**: No control present in 0.56.1 is missing: every device parameter and shell control
  is still reachable (checked device by device against 0.56.1 and by a pass over each view).
- **SC-003**: Zero uses of the monospace typeface remain, and the guard tests report no new
  offenders against the corner scale or opacity rule.
- **SC-004**: DESIGN.md, DESIGN.html and the nota-design skill agree with the theme files: every
  token value they state matches, checked by the generator and the guard tests.
- **SC-005**: A project saved before the change opens with every track in its old colour.
- **SC-006**: Folding and unfolding EQ-8 takes one click each way and changes no parameter value.
- **SC-007**: The build script and smoke test pass on Linux and macOS, and CI passes on Windows.

## Assumptions

- **EQ card height** stays 260 like every other card. Chris was asked whether EQ cards may be
  taller so the graph can grow; until he says so, the graph grows sideways (and with the fold)
  only.
- **Dynamic EQ-8 fold**: Chris asked for the fold on EQ-8. Dynamic EQ-8 gets the same layout
  but not the fold until he asks; adding it later is the same control.
- **Ember themes** take the guide's starting values, adjusted by eye where the readability bar
  needs it.
- **Fold state** is session-only (like Prism's picked band), so no project-format change.
- **nota-docs** screenshots (EN and RU manual) are re-shot in a follow-up nota-docs PR once the
  app change lands; this spec covers the app repo and its docs.
- **Nota Remote** is out of scope beyond the shared tokens it already reads.
- One branch carries the whole change in phased commits (tokens and docs, controls, shell,
  device cards, Modular, icons); it can be split into stacked PRs if review needs it.

## Round 2: review follow-ups (Oct 25)

ambertape reviewed the PR (nota#296, Oct 25); Chris accepted every point. Already done in
this branch: title back in the window title, Loop and loop-record as icons, typed entry on
the shell bars, fixed card widths (S 260 / L 700 / Wide 1050), the Volt pass. The rest:

- **FR-035 (key panel)**: The key picker MUST be redesigned: a one-octave keyboard picks the
  tonic and a Major / Minor switch picks the mode, with the chosen key named in full; "No key"
  (project) or "Any key" (browser filter) and the browser's shortcut buttons stay. Picking a
  tonic keeps the mode; switching the mode keeps the tonic. Both callers share it.
- **FR-036 (Large track header)**: A track header at the Large tier MUST show the track's
  send level to each return (the return's letter and colour, a value bar with the amount
  inside, double-click resets to 0, typed entry like the other bars), two per row, as far as
  the row's height allows. Sends follow changes made elsewhere (mixer, Sends card,
  automation). Return tracks show none (they don't send); groups do.
- **FR-037 (smaller shell text)**: The shell type scale MUST come down one point (Title,
  Heading, Track name 12; Name, Body 11; Caption, section labels 10; values 10.5; the
  transport readout 16; clip names 10; eyebrows 9.5), in the theme, NotaType and every
  literal size in the shell. The device scale (already one step down) stays.
- **FR-038 (text size setting)**: Preferences → Appearance MUST offer Text size: Smaller,
  Default, Larger, Largest (−1, 0, +1, +2 pt on every text the shell draws; device cards keep their size, their
  layouts being fixed, per Chris on Oct 26),
  applied live to every open window and kept in the settings file, independent of Interface
  scale. Larger sizes may trim long labels with an ellipsis; nothing may overlap.
- **FR-039 (device header strip)**: A device card's main tab row MUST move into the header's
  empty strip, after the preset picker, with its trailing readout; the section below gains
  the 22 px row. The main row is the one with the most tabs; on a tie, the one with a trailing
  readout (a centre section's, not a right panel's Levels / Output). When only the tabs fit,
  they move alone and the readout stays below; a long readout trims with an ellipsis. A card
  whose header has no room keeps the row in place (compact and utility cards always do).
- **FR-040**: DESIGN.md, the style guide page and the manual (EN + RU) MUST follow.

Assumptions: the key panel has no reference design; the keyboard layout is our proposal.
Text size is a settings-file value, not a project value (no format change).

## Round 3: device layouts back (Oct 26)

Chris, Oct 26: the redrawn device layouts made the cards cramped and too large; keep the
exact 0.57 layouts and only swap in the new elements. This supersedes FR-025's "width fits
the device", the "no status line" part of User Story 4 and SC-001, the fixed card widths
(S 260 / L 700 / Wide 1050) and FR-039.

- **FR-041**: Every built-in device card MUST have its develop (0.57) layout, width (700;
  the stubs 190), status line and element sizes: knobs 34 / 36 / 44 (24 in a table row),
  value bars 9 high, switches 18 × 10, header 22, the `NotaType.Device*` text scale of 0.57.
  Only the drawing of the elements follows the style guide (knob cap and orange arc, value
  bar groove and line, segment and switch look, pressed-in sections, Geist, greys for the
  identity colours, ink 4 for inactive).
- FR-039 (device header strip) is dropped: tabs stay in the body.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency (Geist is already bundled; Geist Mono is removed)
- [x] Audio-thread impact considered: UI only; no engine or audio-thread change
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no new crossings
- [x] Smoke-test impact assessed: no engine change; design guard tests (`DesignTokenCheck*`) change with the rules
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md planned; manual re-shoot in nota-docs as a follow-up
- [x] UI change → follows `DESIGN.md`, which this change rewrites
