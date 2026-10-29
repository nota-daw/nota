# Feature Specification: Aurora, both wavetables on one card, and custom wavetables from samples

**Feature Branch**: `feat/aurora-one-card` (based on `ui/style-guide`)

**Created**: 2026-10-29

**Status**: Draft

**Input**: Egor's review of the Aurora card: "In Aurora, switching between wavetables (1 and 2)
is actually quite inconvenient. It might be worth trying to combine both wavetables into a
single card — incorporating two visualizations, two type selectors, and all the necessary
knobs. The only concern is performance, as rendering this proved to be quite heavy in the
past. Also, it's essential to add the ability to import custom samples."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The main area is the UI (the Osc tab of the Aurora card). Custom wavetables also touch the
engine (a table that is not one of the four built-in banks) and project / preset persistence.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

### What Aurora does today (ui/style-guide at cc1531b6)

**The card.** 700 × 260 (`AuroraInstrumentCard.cs:49`, `DeviceCardKit.cs:22`): a 56 px
wheels column, a centre panel with six tabs (Osc, Filter, Env, LFO, Mod, FX) and a 186 px
rail (Global / Macros) (`AuroraInstrumentCard.cs:51-53`). That leaves about 438 px × 166 px
for a tab's body (700 − 2 × 5 margin − 56 − 186 − 2 × 5 gaps; body 186 − tab row 20).

**How switching works.** The Osc tab already shows both oscillators' knob rows: name and
on/off, Position, Warp, Level, Oct, Semi, Fine (`AuroraInstrumentCard.cs:444-479`). What is
shared, and therefore switched, is:

- one table window, 128 px wide, drawing only the picked oscillator's table
  (`AuroraInstrumentCard.cs:534`, fed at `:168-169`);
- one strip on top that holds the bank chips (Analog / Pulse / Formant / Chroma) and the
  warp-mode chips (Off / Sync / Bend / PWM / Fold) of the picked oscillator only
  (`:423-442`, shown or hidden by `applyOsc` at `:480-487`).

You pick the oscillator with a "1 / 2" segment (`:431`) or by clicking its name (`:465-469`).
So to see or change Osc 2's table you first switch the strip and the window away from
Osc 1, and the two tables are never on screen together. The window's caption names the
table, the frame ("frame 5/16") and what modulates position (`:509-526`). The sub and
unison values sit in one row under both oscillators (`:489-507`).

**The table window and its performance history.** `AuroraStack` draws the 16 frames as a
skewed 3-D pile, the frame Position lands on in bright ink (`Controls/AuroraStack.cs:4-8`).
It does not read the engine: it rebuilds the shapes from its own copy of the bank recipe,
with fewer harmonics (`:6-7`, `:77-84`). It used to rebuild those shapes inside every paint:
"thirty thousand sines and a thousand line segments on every one of the thirty ticks a
second the device panel runs, which is what made switching to an Aurora track stutter"
(`:9-13`). The fix computes all four banks' frames once per process and shares them across
cards (`:47-49`, `:86-115`). Each frame is now drawn as one geometry (`:118-153`), and the
window only repaints when the bank or the frame changes (`:66-75`). On the card, the
30-per-second live tick (`AuroraInstrumentCard.cs:1171`, driven from
`DeviceChainView.Instruments.cs:120-126`) first checks whether any of the 156 parameters
moved and returns at once if none did (`AuroraInstrumentCard.cs:147-176`). The window does
not follow modulation: it shows the set Position, not the position being played.

`specs/036-aurora-perf` was about Aurora's **audio** CPU, not the UI. It measured offline
render cost with `tests/perf/nota_perf.cpp` (scenario `aurora-supersaw-2x`, `:195`), on a
fixed reference machine (i7-12700, Linux, `036-aurora-perf/baseline-env.md:6-13`), with a
null test for "same sound" (036 FR-003). The result was about 28 ms of render per second of
audio (`baseline-env.md:21`). UI CPU in this project has been measured per thread with
`top -H` / `pidstat` (`.specify/bugs/linux-idle-cpu/assessment.md:27`, `fix.md:77`), and as
"UI CPU within N % of a baseline" (`specs/017-live-midi-record-notes/spec.md:176`).

**Engine.** `WavetableSynth` (kind 5) has 4 built-in banks × 16 frames × 2048 samples
(`WavetableSynth.h:195`), generated once per process from an additive recipe with at most
64 harmonics, each frame normalised to its own peak (`WavetableSynth.cpp:705-743`). The
banks are shared, read-only, by every instance (`WavetableSynth.h:16-18`, `:291-295`).
Playback blends neighbouring frames (`readMorph`, `WavetableSynth.h:281-290`). A table is
picked by a normalised parameter rounded to four steps (`bankOf`, `WavetableSynth.h:226`):
`table` and `osc2table`, Analog 0 / Pulse .333 / Formant .667 / Chroma 1
(`FactoryPresetCatalog.cs:178`). The parameter order is the saved layout, append-only
(`WavetableSynth.h:19`). The instrument's state is just the 156 parameter values
(`WavetableSynth.cpp:227-244`), and `clone()` copies only those (`:246-252`). Aurora has no
scope or telemetry output today.

**Sample infrastructure to reuse.**

- Decoding: WAV, FLAC and MP3 through dr_libs (`src/media/AudioFile.cpp:29-73`,
  `LICENSES/third-party.md:19`). The managed side lists the same three as the formats the
  engine decodes (`SampleIndex.cs:18-21`). There is already a call that decodes a file's
  head as mono (`nota_file_decode_mono`, `nota_engine.h:718-721`), and a background,
  block-by-block import job (`nota_engine.h:613-631`).
- Loading a sample into an instrument: Sampler, Grain and Rhythm voices take a file path
  (`nota_engine.h:727`, `:778-780`). Rhythm's card shows the two entry points to copy: a
  file picker (`RhythmInstrumentCard.cs:205-218`) and dropping from the browser onto a
  target, with a hover highlight (`:220-235`, `BrowserView.IsAcceptableDrag` /
  `DroppedItems`). Rhythm's picker also lists AIFF and OGG (`:211`), which the engine does
  not decode.
- With the project: a `.nota` bundle embeds every referenced sample as `samples/<hash>.wav`.
  The name is content-addressed and deduplicated, shared by every version in the project
  history (`ProjectService.cs:4-8`, `BundleContent.cs:4-9`, `ProjectHistory.cs:4-18`).
  Rhythm is the closest precedent: its state blob holds flags and its per-voice samples
  are saved next to it (`ProjectService.cs:155-167`) and reloaded with a warning on failure
  (`:661-666`). Aurora today saves only its state blob (`ProjectService.cs:150-153`).
- Missing audio: the load path warns ("Couldn't reload…", `ProjectService.cs:582`,
  `:662`; "A referenced sample was missing and was skipped.", `:1075`), and Mosaic's card
  shows a missing panel ("Samples unavailable · notes muted",
  `MosaicInstrumentCard.cs:448-507`, `:1171`).
- Format version: projects are at version 27 (`ProjectService.cs:40`). A new field is a
  version bump with a no-op migration (`ProjectMigrations.cs:25-50`). Presets store
  instrument values by parameter id (`PresetModel.cs:30-34`), with optional extra payloads
  for sample-based instruments (Grain source, Mosaic program, `PresetModel.cs:35-49`).
- Undo: Mosaic's program change is one undo step that republishes the instrument
  (`nota_engine.h:705-709`). Structural edits publish a new graph snapshot (AR-5).

**Design rules.** Cards are 260 high with fixed widths S 260 / L 700 / Wide 1050
(`specs/038-style-guide/spec.md:430`, `DESIGN.md:529`). A card keeps its layout unless a
layout change is decided on its own; this spec is that decision (`DESIGN.md:1175-1178`). Tab
rows, graphs (one `NotaGraph` window: `SurfaceInset` ground, a `Rule` frame, a title top
left, corner labels, no fills under curves, room for the words) and the interaction
contract are set out in `DESIGN.md:674-682` and `:795-855`. Values are orange; there are no
identity colours, so the two oscillators are told apart by shade and label
(`DESIGN.md:1163-1165`, 038 US acceptance 6). Units take a thin space (`DESIGN.md:460`).

## Clarifications

### Session 2026-10-29

- Q: How is a longer recording cut into frames? → A: 16 evenly spaced 2048-sample windows
  aligned to zero crossings, no pitch detection.
- Q: Does each window follow the modulated position live? → A: Yes; the performance budget
  covers both windows following live.
- Q: Does the card stay 700 wide or move to the Wide 1050 size? → Open, put to the
  project owner.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and shape both oscillators at once (Priority: P1)

A sound designer builds a pad from two tables: Formant on Osc 1, Analog on Osc 2. On the
Osc tab they see both tables side by side, each with its own type selector, warp mode and
knobs. They sweep Osc 2's Position while watching Osc 1 stay put, without any 1 / 2
switching.

**Why this priority**: This is the reported problem. It is useful on its own, with no
import feature.

**Independent Test**: Open any two-oscillator factory preset (e.g. Glacier Pad). Without
clicking anything, check that the card shows both tables, both type selectors, both warp
modes and all six knobs per oscillator. Change each one and check that only its own
oscillator changes.

**Acceptance Scenarios**:

1. **Given** an Aurora on the Osc tab, **When** the card is shown, **Then** two table
   windows are visible at once, labelled Osc 1 and Osc 2. Each shows its own table type, its
   current frame and what modulates its position. There is no "1 / 2" switch.
2. **Given** both oscillators are visible, **When** the user picks Chroma in Osc 2's type
   selector, **Then** only Osc 2's window and sound change, and Osc 1's selector still
   reads its own table.
3. **Given** Osc 2 is switched off, **When** the card is shown, **Then** Osc 2's window and
   controls take the unavailable look and stay where they are. Switching it on again needs
   one click.
4. **Given** a project saved before this feature, **When** it is opened, **Then** every
   Aurora sounds bit-identical and shows the same table, warp mode and knob values as
   before.
5. **Given** the new layout, **When** any Osc control is used, **Then** it keeps today's
   interaction contract: vertical drag, double-click to reset, Alt-click or double-click
   the read-out to type, right-click menu, MIDI learn, automation follow.

---

### User Story 2 - Turn my own sample into a wavetable (Priority: P1)

A producer has a recorded vocal "aaa-ooo" and a single-cycle wave they made elsewhere. They
drag the single cycle onto Osc 1's window and the vocal onto Osc 2's. Osc 1 now plays that
exact cycle; Osc 2 becomes a table whose frames morph from "aaa" to "ooo" as Position
moves. Both stay with the project and in a saved preset.

**Why this priority**: Egor called it essential. It needs no part of US1 to be useful, but
it lands in the same card.

**Independent Test**: Import a 2048-sample single cycle, a standard multi-frame wavetable
file and a two-second recording into a fresh Aurora. Play each one, sweep Position, save,
close, move the source files away, reopen. Everything sounds the same as before saving.

**Acceptance Scenarios**:

1. **Given** an Aurora, **When** the user drops a WAV, FLAC or MP3 file from the browser or
   the system file manager onto Osc 1's window, **Then** Osc 1 plays a table made from that
   file. Its type selector names the file. Osc 2 is unaffected.
2. **Given** an Aurora, **When** the user picks "Import…" in an oscillator's type selector
   and chooses a file, **Then** the result is the same as a drop.
3. **Given** a file whose length is an exact multiple of 2048 samples (a standard wavetable
   file), **When** it is imported, **Then** each 2048-sample block becomes one frame, in
   order, up to 256 frames.
4. **Given** a single cycle (any length up to 4096 samples that is not a standard wavetable
   file), **When** it is imported, **Then** it becomes a one-frame table that plays the
   cycle at the played note's pitch. Position then has no audible effect, and the window
   says so.
5. **Given** a longer recording, **When** it is imported, **Then** it becomes a table of 16
   frames taken across the whole file of evenly spaced 2048-sample windows aligned to zero
   crossings (no pitch detection; decided Oct 29, see Clarifications).
6. **Given** an imported table, **When** the user saves the project, closes Nota, deletes or
   moves the source file and reopens the project, **Then** the oscillator sounds the same,
   because the table is stored inside the project.
7. **Given** an imported table, **When** the user saves an Aurora preset and loads it on
   another track, or on another machine, **Then** the preset brings the imported table
   with it.
8. **Given** an import just made, **When** the user presses Undo, **Then** the oscillator
   returns to the table it had before (built-in or an earlier import). Redo brings the
   import back.

---

### User Story 3 - Two tables on screen without a slower app (Priority: P1)

A user on the reference Linux machine has a busy project with an Aurora whose two
positions are both swept by LFOs. They keep the Aurora card open while the song plays. The
interface stays as smooth as it is with today's single window, and switching to the Aurora
track shows the card with no hitch.

**Why this priority**: Egor's stated concern. A two-window card that brings back the old
stutter would be a regression.

**Independent Test**: Measure the worst-case scenario in SC-003 to SC-005 before and after,
with the same method, on the same machine.

**Acceptance Scenarios**:

1. **Given** both windows are visible and both positions are modulated, **When** the
   transport plays for 60 seconds, **Then** the UI thread's CPU stays within the budget in
   SC-003.
2. **Given** a project with four Aurora tracks, each with two 256-frame imported tables,
   **When** the user clicks from track to track, **Then** each card appears within the time
   in SC-005, with no visible stall.
3. **Given** the device panel is hidden or the window is minimised, **When** the song
   plays, **Then** the table windows do no drawing work.

---

### User Story 4 - See where modulation has taken the table (Priority: P3)

While an LFO sweeps Osc 1's position, the lit frame in Osc 1's window moves with the
position actually being played, not just the knob's set value (live follow is in scope; decided Oct 29, see
Clarifications).

**Why this priority**: It makes the windows more informative, but it is the most expensive
part to draw and the windows work without it.

**Independent Test**: Route LFO 1 to Position at 1 Hz, hold a note, and watch the lit frame
travel through the stack once a second. With no note held, it rests at the set Position.

**Acceptance Scenarios**:

1. **Given** a held note with position modulation, **When** the window is shown, **Then**
   the lit frame follows the played position at the card's normal refresh rate.
2. **Given** no notes sound, **When** the window is shown, **Then** the lit frame sits at
   the set Position and the window stops repainting.

---

### Edge Cases

- **Unsupported or broken file** (AIFF, OGG, a text file, a truncated WAV): nothing
  changes, and a short message on the card says the file could not be read and which
  formats work. The picker offers only WAV, FLAC and MP3.
- **Stereo or multichannel file**: the channels are mixed to mono.
- **Silence, or a file that is almost silent** (peak below −60 dBFS): the import is refused
  with a message, rather than being normalised up into noise.
- **DC offset or a cycle that does not end where it starts**: DC is removed from each frame
  and the frame edges are matched, so a held note does not click or buzz.
- **Very bright material** (noise, a hard sync sweep): the table is band-limited the same
  way as the built-in tables, so high notes do not fold back into the audible range any
  more than a built-in table of similar brightness.
- **Long file**: only the first 60 seconds are read. The card says the file was shortened.
- **More than 256 frames in a wavetable file**: the first 256 are kept, and the card says
  so.
- **Huge or slow file** (for example on a network drive): the import runs in the background,
  the card shows that it is loading, the oscillator keeps playing its old table until the
  new one is ready, and audio never drops out.
- **Import while notes are held**: the switch to the new table is click-free or, at most,
  stops the sounding notes the way a Mosaic program change does. It never glitches or
  drops out.
- **Dropping on the wrong place**: a drop counts only on a table window (or on the whole
  oscillator column). The target window is highlighted during the drag. Dropping a MIDI
  file or a preset there does nothing.
- **Missing table data on load** (the bundle's copy was deleted, or the project was copied
  without its `samples/` folder): the project still opens. A load warning names the
  oscillator and the file, as for other missing samples. The type selector reads
  "Missing: <name>" and the oscillator plays its stored built-in table so the track still
  sounds. Importing again, or picking a built-in, clears the state.
- **Copy, duplicate, clone and racks**: duplicating the track, copying the device, freezing,
  and putting the Aurora inside an Instrument Rack chain or on a Drum Rack pad all keep the
  imported table.
- **Automation of the type parameter while an imported table is active**: the imported
  table keeps playing. The built-in type value is remembered and shown dimmed in the
  selector, and it takes over only when the user picks a built-in again (see FR-010).
- **One-frame table with Position modulated**: no audible change, no error. The window
  shows the one frame and the caption says "1 frame".
- **Both oscillators import the same file**: it is stored once in the project.
- **Opening a project made with this feature in an older Nota**: the existing format check
  refuses it, as for any newer project. Projects without imported tables keep the same
  values, so they lose nothing in other versions.

## Requirements *(mandatory)*

### Functional Requirements

**One card, both tables**

- **FR-001**: The Osc tab MUST show both oscillators at once, each with its own: name, on/off
  switch, table window, table-type selector, warp-mode selector, and the Position, Warp,
  Level, Oct, Semi and Fine knobs. No control of either oscillator may sit behind a switch
  or a picked state.
- **FR-002**: The "1 / 2" oscillator pick, and every place where one oscillator's controls
  replace the other's, MUST be removed from the Osc tab. The sub-oscillator and unison
  controls stay on the Osc tab, shared, and still visible.
- **FR-003**: Each table window MUST show its oscillator's table as a stack of frames with
  the current frame lit, a caption with the table's name, the frame as "n/N" (N = that
  table's frame count) and what modulates its position (as today). It MUST follow the
  style guide's graph rules (one graph window, title top left, room for the words, no
  fills under curves).
- **FR-004**: The two oscillators MUST be told apart by their labels and position on the
  card, not by identity colours (style guide). An oscillator that is off MUST take the
  unavailable look without moving anything.
- **FR-005**: The card MUST stay 260 high, with every control it has today still on it
  [NEEDS CLARIFICATION: does the card stay 700 wide, fitting both oscillators into the
  current ~438 px centre panel (type selectors become compact drop-downs and the windows
  get smaller), or move to the Wide 1050 size so each oscillator gets a full-width row
  with its chips and a window as large as today's?]. The other five tabs, the wheels and
  the rail keep their content.
- **FR-006**: Factory presets, saved projects and saved user presets MUST load with the
  same values and sound bit-identical (null test as in 036 FR-003). This feature changes
  the layout and adds a table source. It does not change how a built-in table sounds.

**Custom wavetables**

- **FR-007**: Each oscillator MUST be able to load its own custom table from an audio file,
  by dropping a file onto its table window (from Nota's browser or the system file manager)
  and from an "Import…" entry in its type selector.
- **FR-008**: The import MUST accept WAV, FLAC and MP3, in any sample rate and channel
  count, mixed to mono. Only the first 60 seconds are read.
- **FR-009**: The import MUST turn the file into a table of 2048-sample frames by these
  rules, in order:
  (a) a file that declares its frame size, or whose length is an exact multiple of 2048
  samples, is a standard wavetable file: each frame is read as stored, up to 256 frames;
  (b) otherwise a file of up to 4096 samples is one cycle, resampled to one 2048-sample
  frame;
  (c) otherwise the file is a recording, cut into 16 frames as decided in US2
  acceptance 5.
  Each table MUST be normalised to the same peak level as the built-in tables, with the
  frames' relative levels kept. DC MUST be removed from each frame, and each frame MUST
  loop without a click. No pitch detection is used, unless the US2 clarification picks it.
- **FR-010**: An imported table MUST be an extra choice in that oscillator's type selector,
  named after the file, beside the four built-in tables. Picking a built-in table MUST keep
  the import, so one click can return to it, until the user removes or replaces it. While
  an imported table is chosen, automation or modulation of the built-in type value MUST
  NOT replace it.
- **FR-011**: An imported table MUST play through everything a built-in table does: all
  five warp modes, Position (blending between neighbouring frames), unison, the mod matrix,
  macros, MPE slide and the filters. It MUST be band-limited so it does not alias more
  than a built-in table of similar brightness.
- **FR-012**: Importing MUST NOT interrupt audio. The file is read and prepared away from
  audio playback. The oscillator keeps its old table until the new one is ready, then
  switches without a dropout. While it loads, the card MUST show that it is loading.
- **FR-013**: An import (and removing an import) MUST be one undo step. Undo returns the
  oscillator to its previous table, built-in or imported, and redo repeats the import.
- **FR-014**: An imported table MUST be saved inside the project, so the project needs
  nothing outside its bundle. It MUST be stored once if both oscillators, or several
  Auroras, use the same file. It MUST survive save / reopen, project version history,
  "save as", collecting the project, duplicating a track or device, and Instrument Rack /
  Drum Rack chains. The original file's name MUST be kept for display.
- **FR-015**: A user preset saved from an Aurora with an imported table MUST carry the table
  itself, so it works on another machine. Loading a preset MUST set both oscillators'
  tables to what the preset holds: built-in or imported. Factory presets do not change.
- **FR-016**: If a project's stored table cannot be read when it opens, the project MUST
  still open. A load warning MUST name the track, the oscillator and the file. The type
  selector MUST read "Missing: <name>", and the oscillator MUST play its stored built-in
  table until the user imports again or picks a built-in.
- **FR-017**: A failed import (unreadable file, unsupported format, silence) MUST leave the
  oscillator unchanged and show a one-line reason on the card.

**Performance**

- **FR-018**: The two table windows MUST NOT repaint unless what they show has changed:
  table, lit frame, on/off, theme or size (and, if US4 is in, the played position while
  notes sound). Neither window may draw while the card is hidden or the app is minimised.
- **FR-019**: A table window's cost to draw MUST NOT depend on the table's frame count. A
  table with more frames than the window can show is drawn with a fixed number of
  representative frames plus the lit one. Frame shapes for drawing MUST be prepared once
  per table (on import or load), not on each paint or tick.
- **FR-020**: Showing the two windows MUST meet the UI budgets in SC-003 to SC-005, and
  Aurora's audio cost MUST stay within SC-006.

### Key Entities

- **Oscillator table choice**: per oscillator, either one of the four built-in tables
  (Analog, Pulse, Formant, Chroma) or that oscillator's imported table. The built-in value
  stays a normal, automatable parameter with today's four steps.
- **Imported table**: a mono table of 1 to 256 frames, each 2048 samples. It carries the
  source file's name, its frame count, and how it was made (standard wavetable file, single
  cycle, or recording cut into frames). It is stored inside the project, and inside a
  preset when one is saved. It is shared when its content is identical.
- **Table window**: one per oscillator. It shows the chosen table's frames, the lit frame,
  the caption (name · frame n/N · position modulation) and the load / missing / error
  state, and it is the drop target for imports.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the card open on the Osc tab, every control of both oscillators can be
  reached in zero clicks (today, reaching Osc 2's table or warp mode from Osc 1 takes one
  switch, and the two tables are never visible together).
- **SC-002**: Importing a 2048-sample cycle, a 256-frame standard wavetable file or a
  10-second recording onto an oscillator takes one drop or three clicks (selector,
  "Import…", file), and the new table plays within 1 second for files up to 10 seconds on
  the reference machine.
- **SC-003**: **UI CPU budget.** On the 036 reference machine (i7-12700, Linux), with the
  same per-thread measurement before and after (see Assumptions; 60 s average): an
  Aurora card open on the Osc tab, both oscillators on 256-frame imported tables, both
  positions modulated by LFOs at 2 Hz, and a chord loop playing. The UI thread uses no more
  than **2 percentage points of one core above** the same project shown with today's card,
  and no more than 5 % of one core in total for the card's drawing.
- **SC-004**: In the SC-003 scenario, painting both table windows takes no more than **2 ms
  per frame** at the 99th percentile, so the card stays inside a 60 fps frame with room to
  spare.
- **SC-005**: Switching to an Aurora track shows the full card within **100 ms**, with no UI
  frame longer than 50 ms. This holds for a project with four such Auroras, clicked through
  in turn.
- **SC-006**: Aurora's audio cost in the `aurora-supersaw-2x` benchmark stays within ±10 %
  of the 036 baseline (about 28 ms of render per second of audio). Factory presets pass the
  036 null test bit-identically. Two Auroras playing imported tables cost no more than the
  same patch on built-in tables plus 10 %.
- **SC-007**: In 10 out of 10 trials, a project with imported tables, saved, closed and
  reopened after the source files were deleted, renders bit-identically to the render
  before it was saved.
- **SC-008**: The build and the smoke test pass. The smoke test covers importing a table,
  saving and reloading it, and a missing table on load.

## Assumptions

- **Frame size and count.** Imported frames use the same 2048-sample size as the built-in
  tables. Standard wavetable files keep their frame count up to 256. Recordings become 16
  frames, like the built-in tables. One-cycle files become 1 frame.
- **Normalisation** is per table (frames keep their relative loudness), to the built-in
  tables' peak. Built-in tables stay normalised per frame, as today.
- **Formats** are what the engine already decodes (WAV, FLAC, MP3). Adding AIFF or OGG is
  out of scope; it would be a new dependency.
- **Length cap**: 60 seconds read. Longer files are shortened, with a notice.
- **No pitch detection** for single cycles: a one-cycle file is taken to be exactly one
  period. Pitch detection for recordings depends on the US2 clarification.
- **Storage**: the imported table is kept in the project bundle the same way as other
  samples (embedded and content-addressed, not a link to the original file), so a moved or
  deleted source file never breaks a project. The project format version goes up by one,
  with a no-op migration.
- **No user wavetable library.** Imported tables live in the project and in user presets.
  A browser category of "my wavetables" is out of scope.
- **Importing does not switch an oscillator on.** Importing into an off oscillator loads
  the table and leaves the oscillator off; its window shows the table in the unavailable
  look.
- **The table choice is not automatable as a new parameter.** Choosing an imported table
  works like loading a sample: it is an edit with undo, not an automation target. The
  built-in type stays automatable, as today.
- **No in-card table editor** (drawing frames, re-ordering, spectral tools). Only import,
  replace and remove.
- **The other tabs** (Filter, Env, LFO, Mod, FX) and the rail are unchanged, apart from what
  FR-005's clarification decides about width.
- **Measurement**: UI budgets are measured on the 036 reference machine with the same
  per-thread method before and after (UI thread CPU sampled once a second with `top -H` /
  `pidstat`, as in the linux-idle-cpu report), and recorded in the plan. macOS numbers are not
  acceptance evidence, as in 036.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
