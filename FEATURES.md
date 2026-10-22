<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Nota — full feature list

A consolidated list of what Nota (a cross-platform DAW) can do, as of version
**0.56.1** (plus changes in development on `main`). This document describes what is
implemented in the code, not what is planned. Sources: `CHANGELOG.md`, `README.md`,
`ARCHITECTURE.md`.

Contents
- [Platforms and distribution](#platforms-and-distribution)
- [Project and transport](#project-and-transport)
- [Views](#views)
- [Tracks and mixer](#tracks-and-mixer)
- [MIDI and virtual instruments](#midi-and-virtual-instruments)
- [Audio: recording, clips, warp](#audio-recording-clips-warp)
- [Automation](#automation)
- [Built-in devices](#built-in-devices)
- [Plugin hosting](#plugin-hosting)
- [Browser and assets](#browser-and-assets)
- [Export](#export)
- [Windows and interface](#windows-and-interface)
- [Preferences](#preferences)
- [MCP / AI control](#mcp--ai-control)
- [Other](#other)

---

## Platforms and distribution

- **macOS** (≥ 13.0, arm64 + x86_64): CoreAudio, CoreMIDI, **AU + VST3**
  hosting. Distributed as a separate `.dmg` for Apple Silicon and for Intel (ad-hoc signed).
- **Windows** (x64): WASAPI (shared + **exclusive mode** for minimum latency), WinMM
  MIDI, **VST3** hosting. Distributed as an Inno Setup installer
  (`Nota-Setup-<version>-x64.exe`; arm64 is also built).
- **Linux**: PulseAudio/ALSA (via miniaudio), ALSA MIDI (RtMidi), **VST3** hosting.
  Distributed as a portable **AppImage** (`Nota-<version>-<arch>`) that runs on any distro
  with glibc ≥ 2.28 (RHEL/Alma/Rocky 8+, Debian 10+, Ubuntu 18.10+). Settings live in
  `~/.config/nota` (or `$XDG_CONFIG_HOME/nota`).

Architecture: engine and DSP in C++20 (CMake + Ninja), UI in .NET 10 / Avalonia 12 (C#),
with a C ABI + P/Invoke boundary between them. Plugin hosting is isolated in a separate
JUCE module; the engine core is JUCE-free.

---

## Project and transport

- **Project**: create / open / save (`.nota`), autosaves, undo/redo for every editing
  operation — including volume, pan and master-volume moves and the parameters of effects,
  MIDI effects, instruments and plug-ins, from any source (device cards, Mixer, track
  headers, Session mixer, **MST**, Nota Remote, a MIDI controller, MCP), one step per move,
  and every arrangement section edit; a recorded automation pass undoes with its value.
  Mute, solo, sends and devices inside racks are not in the history; automation playback,
  opening a project and freezing add no steps.
- **New project template**: a new project (and the workspace Nota opens with) starts with an
  instrument track (Nota Volt), an audio track and two returns — **Reverb** (Nota Reverb)
  and **Delay** (Nota Delay), both Wet Only — ready to send to; it opens unmodified, with
  nothing to undo.
- **Version history**: every save records a version inside the project. The browser's
  **History** tab draws them as a branching tree (the current one has an orange dot), each
  described by what it changed ("Added Bass · Tempo 120.00 → 124.00"); switch
  to any version, open one as a copy, or name, note, star and delete versions. Saving on top
  of an older version branches. **Save Version with Note…** (⌥⌘S). The tab's menu deletes all
  unnamed versions at once; Settings → Library turns history off (erasing it, after a warning). Versions share their
  audio, so each costs only what it adds; **Save As** carries the history along.
- **Start window**: the logo follows the theme, recent projects sit in one compact list with
  their dates — click to select, double-click (or the arrow keys and Return) to open. Escape
  or a click outside closes it, keeping the current project.
- **Quit without saving** dialog names the project and how long its changes have gone
  unsaved; Return saves (right in the dialog, then quits), Escape cancels.
- **Project key** (KEY cell in the transport bar, saved with the project): loops dropped on
  the arrangement can be transposed to it.
- **Transport**: play / stop / record, playhead position.
  - **Stop returns to the launch point** (a seek sets the start anchor).
  - **Pause** (⇧Space) — the playhead stays where it stopped and Play carries on from there.
  - **Follow** — the arrangement follows the cursor (playhead stays centred); scrolling away
    during playback pauses it until **Show playhead** (P), the button or the next Play.
  - **Show playhead** (P, View menu, or the arrow the ruler shows when it is off screen) —
    scrolls the arrangement to the playhead without moving it.
  - **Launch quantize** (Q): None … 1/16 … 4 Bars — applied when launching slots and
    scenes in Session.
  - **Position**: clicking the readout toggles bars ↔ time (mm:ss.ms).
  - **DSP meter**: the share of each audio buffer the engine needs; its tooltip adds the
    CPU used by the whole Nota process, in the system monitor's scale.
- **Tempo (BPM)** and **time signature** (edited by dragging; the denominator snaps to a
  power of two), grid and quantization.
- **Tap tempo**: a TAP cell beside the BPM (and *Tap Tempo* in the command palette) sets the
  tempo from tapped beats — mean of the last eight gaps, restarts on a pace change or a 3 s pause.
- **Loop region** (on/off and range; Cmd/Ctrl+L loops the selection; drag an end of the brace
  to resize it, the brace to move it; double-click the brace to switch the loop off and on;
  right-click the ruler to clear it). Notes held across the
  loop seam keep sounding and release tails ring into the next pass; an arpeggiator keeps its
  pattern going across the seam.
- **Metronome** and **count-in** before recording, Session slot takes from stop included (notes played on the count-in are recorded).
- **Transport shortcuts** (Space play/stop, Enter stop, R/M/A, clip copy-paste, typing
  notes, undo/redo) work in detached windows too.

---

## Views

Three top-level views, switched by three icon keys in the transport (**Arrangement ·
Session · Modular**; each names its view in a tooltip); the mixer opens as a separate
floating window (**View → Mixer**, ⌘M) that is as wide as its channel strips, follows the
track count and closes with Escape.

- **Arrangement View** — a linear timeline:
  place, move, trim and duplicate clips; the snap grid (below); scroll and zoom;
  recording into the arrangement (audio and MIDI); split at the cursor or across a
  selection (Cmd/Ctrl+E cuts every track in the selection); clip scrubbing on the ruler.
  ⌘A selects every clip; moving several selected clips is one undo step.
  **Duplicate time** (⌘⇧D) copies a selected time range right after itself and **Insert
  silence** (⌘⇧I) opens a gap of its length, across the whole song — clips and automation
  after it move right, clips crossing it are cut (Edit menu and the range's right-click menu).
  An **Overview** strip above the ruler shows the whole project in miniature with the
  visible span as a window over it — drag it to scroll, drag its edges (or drag vertically)
  to zoom, double-click to fit the project.
  - **Sections** — a lane between the Overview and the ruler names spans of the song
    (Intro, Verse, Drop). Drag the empty lane to mark one, click it to jump the playhead
    there, double-click to rename, drag its body or edges to move and resize it (always on
    whole bars). Its menu loops the section, selects it as a time range across every track,
    duplicates it or deletes it. Each edit is one undo step. Saved with the project (MCP
    `list_sections` / `set_sections`); View ▸ Toggle sections hides it.
  - **Resizable tracks and track list** — drag a track's bottom edge (header or lane) to
    set its height, 30–256 px, with a light snap to Compact / Standard / Large; a
    multi-selection resizes together, ⌥-drag resizes every track, double-click resets.
    The track menu's **Height** keys (Compact · Standard · Large) size the selection in one
    click; ⌥-click sizes every track. Headers
    fit their height: one line (name, M / S / arm) when short, input, fader and pan on rows
    of their own when tall. Drag the seam between headers and lanes to set the header
    width (160–480 px). Both are saved with the project.
  - **Group rows are slim** — a group collapses to a titled bar with its disclosure,
    mute/solo and level, and opens to the full control set when selected, so groups read as
    families rather than as more tracks. A track without a colour of its own takes its
    group's hue, shaded per sibling.
  - **Return and Master rows stay pinned** under the tracks, each with a level meter, a
    volume fader and its dB value (the Master fader is the same master volume as the
    transport's and the Mixer's).
  - **Clip names appear where a run begins** — a clip is a colour band over its waveform,
    and the name prints only at the head of each run so a repeated pattern reads as one
    block. View ▸ Clip names cycles every clip / first of a run / none; a clip named by
    hand always shows its name.
- **Snap grid** (the arrangement, and the clip editors — piano roll and audio — which
  share one setting; remembered app-wide): **Auto** follows the zoom so you snap to the
  lines you see — finer as you zoom in, whole bars zoomed out — in **Narrow / Medium /
  Wide** density; or a fixed step from 1/32 to 4 bars (its label dims when it is finer
  than the drawn lines). **Triplets** in both modes. The toolbar's GRID cell and the piano
  roll's header chip open a menu with every option, as does the audio clip editor's header
  (trim brackets, warp markers and envelope points snap); ⌘1 / ⌘2 narrow / widen (in Auto,
  the density), ⌘3 triplets, ⌘4 snap on / off, ⌘5 Auto ↔ fixed (also Edit → Grid), acting
  on the editor used last. Hold ⌥ to drag freely; clip edges still attract a dragged clip.
- **Session View** — a clip grid (tracks × scenes) with an inspector that the toolbar's
  **Inspector** key (the sidebar icon) shows or hides (remembered); on a narrow window the least-used
  toolbar controls fold into a **⋯** menu. Named, coloured clips with note / waveform
  previews and a progress pie on the clip's own clock; selection apart from launching,
  keyboard editing (arrows, Return, ⌘C/X/V, ⌘D, ⌘I, F2, Delete) and drag between slots;
  launch modes (Trigger / Gate / Toggle / Repeat), per-clip quantize, legato, one-shot,
  velocity amount and follow actions with a global switch; scenes with names, colours,
  tempo / signature and scene follow, inserted, duplicated, captured and reordered without
  stopping playback; Session rec with fixed length; group columns that fold; returns +
  master; I/O · Sends · Mixer sections; removable stop buttons; per-track Back to
  Arrangement; and moving material between Session and Arrangement — the clipboard works
  both ways, and **⌘⇧C** (**Copy to Session** / **Copy to Arrangement** / **Copy to
  scene**) sends clips, slots or a whole scene across with notes, envelopes, warp, pitch
  and gain, as one undo step.
- **Modular View** — a signal-graph editor for the track's chain: MIDI FX → instrument →
  effects shown as nodes you can expand, bypass, duplicate, delete and reorder right on the
  canvas (node positions are saved with the project), plus a **Global view** that shows
  tracks as islands with cross-track connections. Its centrepiece is **CV modulation** —
  drag a modulator's CV output onto any device, instrument or MIDI-FX parameter to link
  them with a patch cable; click an edge to edit depth and mode, select and delete edges.
  Modulators: **LFO** (with phase), an **envelope follower**, **MIDI→CV**
  (velocity/gate/note), **ADSR** (gated by notes), **Macro** (a manual control → CV) and
  **Math** (two CV inputs → one output); any parameter can also be a CV source
  (param → param). A **CV Scope** oscilloscope reads any modulator signal, and CV ports are
  drawn only where they actually work.

---

## Tracks and mixer

- **Track types**: Audio, MIDI/Instrument, Return, Master, **Group** (nestable submixes —
  ⌘/Ctrl+G to group, ⌘/Ctrl+Shift+G to ungroup).
- **Adding a track**: the `+ Instrument` / `+ Audio` / `+ Return` buttons above the
  arrangement, or the context menu — right-click the empty space below the tracks, or use
  `Add track ▸` in a track's own menu (the new track goes right after that track, or inside
  it when it is a group). `Add track ▸ Recent` (Arrangement and Session) lists the five
  instruments added most recently — built-in or plug-in, however they were added — and
  adds one in a click; the list is per user, and an uninstalled plug-in drops out of it.
- **Several tracks at once**: ⇧-click headers to build a selection (or ⌘A with the headers
  focused); ⌘C / ⌘X / ⌘V / ⌘D and Delete copy, cut, paste, duplicate and delete the
  selected tracks, and right-clicking the selection groups, colours or freezes them all —
  each as one undo step. Copying, duplicating or deleting a group takes its tracks along;
  pasted tracks land after the selected track, in its group.
- **Per track**: volume, pan (bipolar bar), mute, solo, arm (ready to record).
  Double-click resets to the default.
- **Pan law** (per project, in the Mixer footer: **Pan law** Stereo / Balance): under
  Stereo, panning a stereo track or return moves its whole image — at full pan both
  channels play from one side; under Balance the opposite channel fades out. Mono
  material and centred tracks sound the same either way. New projects use Stereo; projects
  from earlier versions keep Balance and are marked **Older project**. Switching is
  click-free during playback and one undo step. Groups keep their balance (marked **BAL**).
- **Stereo Map** (Mixer window, **Strips | Stereo map** in its title bar): every track,
  return and the master as a dot at its pan (left–right) and fader (up–down), with a measured
  cloud — where the sound really sits, how wide it is (from the phase correlation) and how
  loud — and the master correlation under the map. Drag a dot to set pan and volume at once
  (one undo step), double-click for centre, ⌥ double-click for 0 dB; hover shows pan, volume,
  balance, width, correlation and level and flags a source whose image is off its pan; click
  selects the track. Muted tracks show no cloud, solo dims the rest.
- **Reordering** by dragging the name row (with an accent insertion line); returns stay
  after regular tracks; the group hierarchy is saved with the project.
- **Routing**:
  - Input (record source) and output (to master, a bus, or a group).
  - **MIDI routing between tracks** — an instrument track can take MIDI from another
    instrument track ("MIDI In") and play the same material through its own device
    (layering). The source's MIDI output is passed on **after its MIDI effects**, so an
    arpeggiator or other MIDI FX drives the receiver. Selectors live in the track header,
    the mixer channel I/O, and the context menu. A muted source keeps sending MIDI.
    Recording on the receiver prints the incoming MIDI into a clip. Saved with the
    project.
  - **Send/Return** buses (sends to returns) with a return list.
- **Level meters** (peak/RMS) on every channel and on the master, plus true peak.
- **Device chain** per track: instrument + effects + MIDI effects, laid out in a row.
  - Double-click a device's header (or Fold in its menu) to fold it into a narrow strip with
    its rotated name and on/off switch; double-click the strip to unfold. Saved with the project.

---

## MIDI and virtual instruments

- **MIDI clip**: notes (pitch, start, length, velocity).
- **Piano Roll**: draw, move, stretch and delete notes on the snap grid; **QUANTIZE** with
  its own step (1/32 … 1/2, triplets; independent of the zoom) and strength; a velocity
  editor; transposing the selection.
  - A note drawn takes the length and velocity of the note last touched (clicked, moved,
    resized or velocity-changed); before any note is touched, one grid step at velocity 0.8.
  - **Note audition** (the speaker switch in the header, on by default): a note sounds while
    it is placed, clicked or dragged, re-sounding at each new pitch. Nothing is auditioned
    while a take is recording.
  - **Playable keys**: click a key to hear it, ⇧-drag to glide, ⌘-click to select every note
    of that pitch; a vertical drag on the keys zooms the rows.
  - Click the roll's ruler to put the playhead there, or drag along it to scrub (snapped to
    the grid); the roll shows the playhead while stopped too, where playback will start.
  - Drag a note's left edge to move its start (the end stays put); stacked notes keep
    separate velocity stems (grab a stem by its cap, ⇧-click to add it to the selection).
  - Notes replace the same-pitch notes they land on instead of stacking: moved, stretched,
    drawn or pasted notes cut or remove what they cover once the selection is let go
    (click elsewhere, Esc, another clip, save); until then the part that will go is drawn
    faint with a dashed edge. ⌘D duplicates notes over whatever they land on, like clips
    in the arrangement.
  - ⌘C / ⌘V pastes the copy selected, on top of the copied notes, ready to move off with
    the arrows or the mouse.
  - Keyboard: ←/→ move by the grid, ↑/↓ by a semitone (⇧ an octave), ⇧←/⇧→ shorten or
    lengthen by the grid, ⌥↑/⌥↓ change velocity by 10 (⌥⇧ by 1).
  - **Remove out of scale** (next to the Scale key and mode) deletes every note outside the
    scale in one undo step.
  - Zoomed out, the area past the clip's end is darkened and marked with a line.
  - **Vertical zoom**: rows from 6 to 36 px (all 88 keys fit at the smallest) with
    ⌘⌥ + wheel (Ctrl + Alt on Windows / Linux), a drag on the keys (down taller, up
    shorter) or ⌥ + pinch on a trackpad, keeping the pitch under the pointer in place.
    **Fit to notes** (⇧F, the **Fit** button in the clip editor header, or the command
    palette) sizes and scrolls the rows so every note of the clip is in view.
  - Scroll and both zooms are remembered per clip; a new clip opens at the default row
    height, centred on its own notes.
  - A MIDI clip trimmed in the arrangement mutes note tails at its boundary.
  - Live edits (moving a note, changing its length) reach the engine immediately, and a
    whole drag is a single undo step.
  - **Several clips at once**: double-click one of two or more selected MIDI clips (one
    track or several), or right-click → **Edit selected clips together** (also in the
    command palette), to open them together on the arrangement's bars. Selecting, moving,
    transposing, quantizing, deleting and clip tools work across clips, one undo for the
    whole edit; a note moved past the seam into the next open clip of its track moves into
    that clip. A **clip strip** under the ruler shows the open clips: click one to make it
    the active clip (header, inspector and envelopes follow it; new notes go into it), or
    click its lock to keep it as a dimmed, read-only reference. While the Clip tab is
    open it follows the arrangement selection. ⌘D copies that run past a clip's end continue
    into the next open clip; past the last one they are left out.
- **Clip tools (generate and transform)**: a **Tools** rail beside the piano roll with
  fifteen tools that write or rewrite notes. Six **generators** — *Rhythm* (a pattern that
  leans on the strong beats), *Seed* (learns the clip's own intervals and rhythm, then
  writes a new take in the same voice), *Stacks* (grows each note into a voiced chord),
  *Euclidean*, *Melodic Steps* and *Shape* — and nine **transformations** — *Arpeggiate*,
  *Connect*, *Ornament*, *Quantize*, *Recombine*, *Span*, *Strum*, *Time Warp* and
  *Velocity Shaper*.
  - Every knob move is **previewed in the roll and in the engine** — you hear the result
    before deciding — and **Apply** keeps it as a single undo step. Backing out leaves no
    trace in the history.
  - A selection scopes the tool to those notes; with nothing selected it works the whole
    clip. Output can **replace** what it was given or be **added** on top. With several
    clips open, the tool runs on each clip that has selected notes (or the active clip),
    and what it writes stays in that clip.
  - Everything is **seeded**, so a result is repeatable and "New seed" re-rolls it.
  - Tools that think in pitch follow the roll's **Set Scale** key, so what they write
    stays in key.
- **Pattern (step sequencer)**: on a Drum Rack track the detail panel gains a **Pattern**
  tab between Devices and Clip — a drum-machine grid with one row per loaded pad. Click a
  step to place a hit, shift-click for an accent, drag across the grid to paint, and use
  the velocity lane under the selected row. The grid is 1/4 to 1/32, it follows the clip's
  length (paging in 64-step windows on long clips), the playing step lights up, and the
  pads audition by name. It is not a second copy of the pattern: the grid **is** the MIDI
  clip, so steps written here appear in the piano roll and notes drawn there light up as
  steps.
- **MIDI file import**: `.mid` files show in the browser's Files tab and drop from there or
  from Finder / Explorer. On the arrangement the file becomes MIDI clips at the drop point —
  the first part on the target track, every other part on its own new track (drum parts get
  a Nota Rhythm); on a session slot the whole file becomes that slot's clip; double-clicking
  a MIDI file in the browser puts it on a new track.
- **Input**: MIDI keyboard (live play and recording), and the **computer keyboard** plays
  MIDI (the S–K row, sharps on W E T Y U) — including while a hosted plugin window has
  focus.
- **MIDI recording**: a note appears in the take as soon as its key goes down and grows
  with the playhead while held, settling in from the accent colour — in the arrangement, in a
  recording Session slot and in the piano roll showing the take, for keyboard, MIDI and
  routed ("MIDI In") input. Notes played on the count-in and keys already held when
  recording starts are recorded where they started: the take's clip begins earlier to hold
  them (at 1.1 the part before the song start is kept beyond the clip's left edge).
- **Loop recording: Overdub / Replace** — with the loop on, **Overdub** adds every pass to the
  MIDI take; **Replace** keeps only the latest pass you play on (a silent pass keeps the take,
  notes already in the clip stay). Switched by the cell next to Loop in the transport,
  Cmd/Ctrl+Shift+R or the Transport menu; applies from the next pass; remembered between
  sessions. A note held across the loop seam is recorded as two notes, up to the loop end
  and on from the loop start.
- **MIDI controllers in clips** — recording keeps the sustain pedal, pitch wheel, mod wheel,
  channel pressure and any other CC with the notes (a pedal already down at Record is in the
  take from its start), and the clip plays them back to the instrument. Starting mid-clip sets
  each controller to the clip's value there; at a clip's end or on stop its pedals are
  released and the wheel centred. In loop recording a pass replaces a controller only over the
  range it moved it, so a pedal-only pass re-records the pedal and keeps the notes.
  Controllers move, copy, split, trim and consolidate with the clip and are saved with the
  project. Polyphonic aftertouch from an ordinary keyboard is kept as each note's pressure.
  Live and from clips, hosted plug-ins get every controller (the pedal with its real value, for
  half-pedalling); built-ins act on those they know — the pedal, the wheel, pressure, CC74,
  and the mod wheel on synths with a *Mod Wheel* control (Bass, Monolith, Volt, Aurora,
  Operator, Pentad, Consort). The sustain pedal holds released keys on every melodic built-in
  (Keys and Mosaic model their own damper; drums ignore it); a re-struck held key restarts.
  MCP `get_clip_controllers` / `set_clip_controller`.
- **MPE (per-note expression)** — an MPE controller's notes each carry their own pitch
  bend, pressure and slide (CC74): channels 2–16 are per-note (lower zone, ±48 semitones by
  default; the controller's MPE Configuration and bend-range messages are honoured), channel 1
  is an ordinary keyboard whose pitch wheel, channel aftertouch and CC74 move the whole
  instrument. **Nota Synth, Volt, Aurora, Operator, Pentad, Physical and Keys** play it: bend moves
  the note's pitch; pressure raises its level and opens its filter (Operator: deepens the FM,
  Pentad: poly aftertouch through *Aftertouch Cutoff*, Physical: bows the struck body so a held
  note sustains); slide sweeps the filter (Aurora: the wavetable position, Operator: the FM
  index, Physical: how long the body rings, Keys: drives the tine into the pickup / shifts its
  symmetry). Instrument Racks pass it to their chains; hosted plug-ins get polyphonic
  aftertouch (and every instrument-wide controller, see above). **Recording** keeps each note's bend, pressure and slide with the note: it plays back
  from the clip, moves, copies and quantizes with the note, is saved with the project, and the
  piano roll draws it over the note (bend as the pitch it played, pressure as a shade).
  - **MPE lanes in the piano roll** — the lane under the notes switches between **VEL**,
    **BEND**, **PRES** and **SLIDE**. In an MPE lane every note's curve sits over its own
    span; drag across a selected note to draw its curve (⌥ for a straight line), drag a point
    to move it, double-click to add or delete a point, right-click for **Clear Bend /
    Pressure / Slide** and **Clear All Expression**. The bend lane scales itself (±2, 12, 24,
    48 or 96 semitones); each gesture is one undo step and plays back straight away.
- **Nota Remote — a phone as a controller**: **Remote** in the top bar (View → Connect
  Phone…) shows a QR code; the phone opens a PWA with pads, keys in the project's scale with
  chords, an XY pad with tilt, the mixer, macros and the Session grid — over Wi-Fi or a USB
  cable, nothing to install. Pairing by a rotating four-digit code, trusted phones and
  permissions (play only / play and control) in **Settings → Remote**. Phone notes record like
  the computer keyboard's; knobs and XY axes map through MIDI Learn (XY and tilt per track);
  several phones play at once, each on its own track, with players' names on the tracks.
- **Nota Remote in Nota's theme**: the phone wears the colour theme Nota wears and follows
  every change live (including Follow system); **Follow phone** (⋯ → Theme) wears the
  In dark mode / In light mode themes by the phone's own scheme. Its screens follow the
  Studio look — one orange accent for what is live or chosen, semantic mute / solo / arm
  colours, sentence-case labels — and a returning phone paints in its last theme at once.
- **Nota Remote Keys — Expressive**: on a track whose instrument takes MPE, the phone's
  Keyboard and Scale modes can keep each finger on the note it struck: sliding sideways bends
  it (a key's width is its pitch step), the height on the key is its slide, and a screen that
  reports touch force sends pressure.
- **Gamepad input (macOS)** — a connected controller (Xbox / DualShock / DualSense /
  Switch Pro / 8BitDo) acts as a small keyboard: the face buttons and bumpers/triggers play
  notes, the D-pad shifts octave and velocity. Notes travel the same path as typed ones
  (live play, recording, roll highlighting); unplugging releases held notes. Enable and
  monitor controllers in Preferences → Gamepads (hot-plug aware).
  - **Any button can be mapped instead** through MIDI Learn — a mapped button drives that
    control and stops playing its note, while the rest of the pad keeps the layout above,
    so one controller both plays and mixes. Nothing is reserved: mapping the D-pad takes
    it over from octave/velocity. Mappings match on the control, not the pad slot, so a
    replug does not break them.
  - **Sticks and triggers map as continuous controls** — both stick axes on each stick
    plus the two trigger travels, so a pad can sweep a filter or ride a fader, not just
    switch things. They play no notes and do nothing until mapped. Sticks are bipolar and
    rest centred (a deadzone keeps a worn stick from drifting a mapped parameter);
    triggers are unipolar and rest at zero.
- **Highlighting** of the pressed key on the roll's keyboard and as a bar along its row.
- **Audio→MIDI** (right-click an audio clip → Convert). With the **basic-pitch** AI model
  installed (Settings → Downloads → AI Models), Melody and Harmony use it instead: every note,
  chords included, with velocities from the playing; Melody keeps the strongest note where notes
  overlap. Without it they use the DSP below, and the Convert menu offers the model.
  - **Convert Melody** — monophonic pitch detection (YIN) → a new Nota Volt track.
  - **Convert Harmony** — polyphonic (STFT + spectral peak picking) → chords: overtones,
    chorus shimmer and slight detune don't add stray notes, held chords stay whole, and
    notes start on the audio's attacks.
  - **Convert Drums** — hit detection + kick/snare/hat classification → a 3-pad kit and a
    MIDI pattern.
  - **Slice to New MIDI Track** — slices the audio at transients (or into 16 beats) across
    the pads of a new Drum Rack, plus a MIDI clip.
- **MIDI effects** — see [Built-in devices](#built-in-devices).

---

## Audio: recording, clips, warp

- **Recording** from an input (microphone, line, interface) onto an audio track, with
  monitoring (in/auto/off).
- **Audio clips as samples** — drag an arrangement clip onto a track with a Sampler, Nota
  Grain, Drum Rack (next free pad) or Instrument Rack, or anywhere a sample goes (a pad, a
  Rhythm voice, a Session slot); hovering an instrument track opens its devices. The sample
  is the part of the clip you hear; the clip stays put.
- **Recording from an internal bus** (a group, return or master output into a new audio
  track) — dropped frames are padded with silence, so there is no cumulative drift.
- **Import** of audio files by drag and drop: WAV / AIFF / FLAC / MP3.
- **Audio clip**: start and end, waveform drawing, trimming, split, fade in/out, clip
  gain. Changing the length shows the material actually being revealed or hidden.
- **Reverse** (clip editor ▸ DIRECTION, or the clip's context menu): plays the clip
  backwards without touching the file, so it costs nothing to toggle and composes with
  gain, pitch and warp. The arrangement lane and the clip editor both draw it the way it
  sounds — the editor mirrors the whole view (brackets, warp markers, BPM chips, grid and
  cursor included) so the screen reads left-to-right in playing order — and trims and
  splits mirror, so the audible head and tail follow the edit.
- **ADSR per audio clip**: attack, decay, sustain and release shape the clip's level in
  played time. Every audio clip in the arrangement carries three handles, shown on hover or
  selection — attack and release squares at the top corners, a decay/sustain dot — and the
  clip editor has an ADSR tab with the same handles over the waveform plus ATTACK / DECAY /
  SUSTAIN / RELEASE sliders. The waveform is drawn shaped the way it sounds. By default the
  sample plays its full length at full level; double-click a handle to reset its stage.
  Editing in either place updates the other, each edit is one undo step, split keeps the
  attack on the left piece and the release on the right, and the shape is saved with the
  project.
- **Separate Stems** (right-click an audio clip): the **htdemucs** AI model (Demucs v4)
  splits the clip into Drums, Bass, Other and Vocals — a group of four audio tracks under the
  clip's track. Each stem clip copies the original's shape exactly (region, warp markers, trim,
  reverse, envelopes, ADSR) over a stem file as long as the source, so it lines up sample for
  sample; the original clip is switched off. One undo step; runs locally (about a third of the
  clip's length on Apple Silicon) behind a progress dialog with Cancel; clips up to 10 minutes.
  The first use offers to download the model.
- **Warp / time-stretch**: **Complex** and **Complex Pro** modes (the latter with formant
  preservation, correct even when the sample rate and the device rate differ), plus
  transient detection. Trim brackets, warp markers and envelope points snap to the clip
  editors' snap grid. Warping runs in the background (the track shows "Processing…" and a
  re-warped clip plays its previous version until the new one is ready), so long files
  don't stall the app. The clip editor redraws the visible waveform at screen resolution,
  down to single samples, and its ruler thins or adds bars, beats and 16ths with the zoom.
- **Changing the sample rate** does not break third-party plugins or warped clips — the
  caches are rebuilt once when the rate changes.

---

## Automation

- **Automation lanes** on tracks, with curved segments (Alt+drag) and points (double-click
  to add, double-click a point to remove).
- **Automation mode** (⌘⇧A): the first time it opens, each track that already has automation
  shows its first automated parameter.
- **Draw mode** (the `A` key): drawn automation changes the device parameter in real time,
  and the whole drag is one undo step.
- **The lane follows focus** — touch any knob or fader on any device (built-in, plugin, or
  mixer) and the automation lane switches to that parameter.
- **Already-automated parameters are highlighted** in the target picker (a dot, and
  they sort to the top of the list).
- **Selection and paste**: shift-select a range, Cmd/Ctrl+D to duplicate, and paste that
  does not leave redundant points behind.
- **Recording** automation: there are no record modes — lanes always play back, and with
  the transport **Record** button engaged, moving a control writes that parameter's lane.
  A mouse gesture stops writing on release; a hardware control (MIDI knob, fader, gamepad
  stick or trigger) latches and keeps writing until the transport stops.
- **Stable across device moves** — reordering or removing effects keeps every lane on its own
  device and parameter; removing a device removes its automation.
- **Overriding** — touch an automated parameter while not recording and the lane hands
  control to you; the toolbar's **Re-enable Automation** button gives it back.
- **Reading** during playback and scrubbing (volume and pan controls follow the automation).
- **MIDI Learn** — the MIDI button in the top right enters learn mode: mappable controls
  light up, and a click plus a move on the controller creates the binding. Mappable
  targets cover every built-in instrument, effect and MIDI FX parameter (switches, ADSR
  and filter included), track volume/pan/mute/solo, the master, the transport, rack
  macros, and chain and Drum Rack pad mute/solo. The **Map** tab in the browser exposes
  range, inversion and deletion. Saved with the project.
  - Sources are a **CC**, a **MIDI note**, or a **gamepad button, stick or trigger**
    (macOS) — all of them share one mapping table. A button is momentary: it fires a
    toggle target once on press, and holds a continuous one at the top of its range until
    released. A stick or trigger drives a continuous target over its whole travel, and
    fires a toggle target once as it crosses the half-way point.

---

## Built-in devices

Every built-in device uses the same 700×260 shell with a shared header: on/bypass dot,
name, subtitle, preset picker (with **Init** to return to every default), **A/B comparison** (parameter snapshots), stereo meter,
voice count (on synths), move arrows, delete, and the ⠿ drag handle. All support factory
and user presets, automation, persistence and cloning. Instrument cards (built-in, racks
and hosted plug-ins) have the same on/off switch as effects: off silences the instrument
(after a one-block fade it isn't rendered at all) while the MIDI effects before it and the
audio effects after it keep running and keep their settings; saved with the project.

### Instruments
- **Nota Synth** — *no longer offered for new tracks: Nota Volt covers it and carries its
  presets (specs/026); saved tracks still load and play.* Subtractive synthesizer (the basic one): one oscillator
  (Saw/Square/Triangle/Sine) with pulse width, detune, octave and a 1/2/4/7-voice unison
  stack with stereo spread; ADSR; a filter with type Off/LP/HP/BP, resonance and an
  Env → Cutoff amount; voice modes Poly 16 / Mono / Legato with glide, pan and Vel→Vol.
  Osc · Env · Filter tabs with draggable envelope and filter graphs. Two sizes (L 700 /
  S 260, S by default): S is the draggable filter curve over Cutoff · Reso · Attack ·
  Release; the size is saved with the project. 25 factory presets.
- **Nota Sampler** — sampler (one-shot and loop: Off / Fwd / Ping / Rev), voice modes
  Poly 16 / Mono / Choke, Glide (legato in Mono), loop crossfade, sample Gain, pitch
  Keytrack, filter key-tracking and Env → Cutoff, Vel→Vol, Output; Sample · Pitch · Env ·
  Filter tabs over an editable waveform, a clickable keyboard for the root, a draggable
  envelope and a draggable filter response, with Snap to zero, Trim silence and Detect
  from file name; 30 factory presets; MCP `read_sampler` / `set_sampler`.
- **Nota Grain** — granular sampler: a read Position that Scans through the file, Freezes
  or follows the keyboard, grains of a chosen size, density and window shape (Hann /
  Gauss / Tukey / Tri) sprayed around it, coarse/fine pitch, per-grain position, pitch and
  pan variation, stereo spread, **Dry/Wet** against the sample itself, an LP/HP/BP filter
  and an amp envelope. The card draws the sample with the live grain cloud on it (drag to
  move Position). Drop a sample from the browser to replace the built-in pad; a **ROOT**
  control (Pitch tab) sets the key that plays it at its recorded pitch, taken from a note in
  the file name ("Pad_F#3.wav") or C4. 50 factory
  presets in nine folders; every preset outside Basics brings its own source (29 synthesized
  on first use — choir, strings, bells, kalimba, a drum break, an arpeggio, ocean, radio …),
  while Basics shape whatever sample is loaded.
- **Nota Volt** — the default instrument. Subtractive synth, two paths: two oscillators
  (saw/square/triangle/sine, a Shape knob that morphs toward the next wave, pulse width) and
  noise, each routed to one of two filters (LP/HP/BP/Notch, 12/24 dB, with Filter 1 able to
  feed Filter 2, a drive in front of both) and its own amp; amp and filter envelopes, two
  LFOs (shapes, depth, fade-in, tempo sync) that the mod wheel can open onto the cutoffs
  (Wheel → LFO), a dedicated vibrato that can ride the mod wheel, a 7×6 modulation matrix,
  8 macros over 12 destinations, unison (one detuned partner, or a 2/4/7-voice stack with
  stereo spread), output drive, glide, oscillator start phase, pitch-bend (±2/±5/±12) and
  modulation wheels, VEL→AMP, VEL→FILTER, Poly/Mono/Legato (Mono falls back to a held key).
  74 factory presets: its own 25 plus the Nota Synth and Nota Bass libraries.
- **Nota Aurora** — wavetable synth: two wavetable oscillators (16 frames per bank, warp
  Off/Sync/Bend/PWM/Fold) plus a sub oscillator and a stereo unison stack (voices, detune
  in cents, spread), two filters (LP/HP/BP/Notch/Morph, 12/24 dB) with per-source routing,
  two envelopes, two tempo-syncable LFOs, an 8×7 mod matrix, 8 macros over 12
  destinations, pitch-bend (±2/±5/±12) and modulation wheels, output pan, a switchable FX
  chain (drive Tube/Tape/Fold with tone · chorus 1×/2×/4× · reverb Room/Hall/Plate with
  size), Mono/Poly. 25 factory presets.
- **Nota Operator** — FM synth: 4 operators (wave, coarse ratio, fine detune, level and
  their own ADSR each), 11 algorithms with interactive diagrams and drag-to-reroute,
  feedback, a live harmonic spectrum, an LP/HP/BP filter with keyboard tracking, pitch-bend
  (±2/±5/±12) and modulation wheels, VEL→FM, VEL→LEVEL, KEY→LEVEL, Glide, Mono.
- **Nota Pendulum** — a "pendulum" sequencer: swinging balls generate notes, with swing
  curves (Linear/Pendulum/Ease/Bounce), bipolar Rate, a held chord, Scale, Hold, First
  Note, Reset, Humanize and Pan Spread. The card draws one lane per ball from the engine's
  telemetry (direction, the wall it strikes, the time to the next one) over a bar strip of
  the generated notes; 28 factory presets; MCP `read_pendulum` / `set_pendulum`.
- **Nota Bass** — *no longer offered for new tracks: Nota Volt covers it and carries its
  presets (specs/026); saved tracks still load and play.* Mono-first bass synth: a morphing oscillator (sine → tri → saw → pulse,
  pulse width), a sub oscillator (sine/square/tri, −1/−2 oct), a filter (LP/HP/BP/Notch,
  12/24 dB) with pre-drive, its own envelope, key tracking and LFO, amp envelope, LFO to
  cutoff and pitch, unison, output drive and pan, glide, Legato, pitch-bend (±2/±5/±12)
  and mod wheels (the wheel opens the LFO onto the cutoff), VEL→AMP, VEL→FILTER,
  Mono/Poly. 25 factory presets.
- **Nota Physical** — modal percussion: a mallet (stiffness, strike noise, colour) and a
  filtered noise burst with its own ADSR and envelope → filter strike one or two tuned
  resonator banks (Beam / Marimba / String / Membrane / Plate / Pipe partial series with
  decay, material, brightness, inharmonicity, ratio, hit position and tune), in series
  (1→2) or parallel (1+2) with a Res 1 / Res 2 mix; the partials drawn as the engine tunes
  them (drag for ratio and brightness), Poly (8 voices) / Mono, tune, fine, note-off
  damping, volume, pan and a meter. 32 factory presets. MCP: `read_physical`.
- **Nota Flux** — vector-morphing analog synth: an XY pad with four "timbre worlds"
  (WARM/GLASS/MOOG/GRAIN) whose resonance, drive and unison the vector blends, **React**
  (sidechain modulation from another track: Filter/Pitch/Space/Vector, with a live scope of
  its envelope, transients and tilt, and transients per bar), 5 macro knobs
  (Age/Motion/Filter/Env/Space), glide, tune and gain. 25 factory presets.
- **Nota Rhythm** — drum machine: 8 voices (analog and FM kick, noise snare, metallic
  hats, clap, rim, tom, perc), a 16-step sequencer (locked to the transport), 4 pattern
  banks (A–D, copy / clear), accents, quiet steps, per-step velocity, Swing/Humanize, a
  **Glue** bus compressor, and a per-voice **Sample** mode (load your own one-shot, with a
  Start / Length region and Reverse). The 700 × 260 card: kit list, an interactive hit
  contour or sample region, Perform rail with an output meter, a full-width step strip with
  a velocity lane. Each voice has its own **FX** chain of built-in effects (the voice's FX
  tab). Its kits are the 25 factory drum kits it shares with the Drum Rack — samples and
  per-voice effects — from the card's kit picker or the browser. Eight **macros** (the
  centre panel's MACRO mode) drive voice params and the voices' effects; each kit loads with
  its own. MCP `get_rhythm`,
  `set_rhythm_pattern` (text rows), `set_rhythm_voice`, `set_rhythm_perform`,
  `copy_rhythm_bank`, `audition_rhythm_voice`, `get_rhythm_voice_fx` / `add_rhythm_voice_fx` /
  `set_rhythm_voice_fx`, `add_rhythm_kit_track`, `get_rhythm_macros` / `set_rhythm_macro` /
  `map_rhythm_macro` / `unmap_rhythm_macro`.
- **Nota Pentad** — 5-voice analog poly in the Prophet-5 mould: Osc A (saw + pulse, hard
  sync) and Osc B (saw + triangle + pulse, Lo-Freq, keyboard off), noise, a 24 dB/oct
  resonant filter that self-oscillates and tracks the keyboard, two analog-curve ADSRs,
  **Poly-Mod** (filter envelope and Osc B at audio rate → Osc A freq / PW / cutoff),
  Wheel-Mod (LFO ↔ noise), 5/10/16 voices, round-robin or oldest-note stealing,
  Poly/Unison/Mono, poly unison stacks, glide, the Release switch, and **Vintage Drift**
  (seeded per-voice spread — a render is identical for the same seed, so freezing is
  exact). 40 factory presets. Also builds as a standalone VST3 for other hosts.
- **Nota Consort** — paraphonic semi-modular synth: four oscillators (tri / saw / square /
  pulse, sync 2→1 and 4→3), a mixer with noise, drive and a feedback-normalled EXT input, dual
  ladder filters (HP→LP series, LP/LP or HP/LP parallel stereo with spacing), two ADSRs,
  MONO / DUO / PARA paraphony or 16-voice true poly, per-oscillator glide (LCR / LCT / EXP),
  a six-shape LFO, a stereo bucket-brigade delay, a 16-step sequencer / arpeggiator with
  ratchets and ties, and a 12-cable patch bay over 42 points (matrix, jack strip or full-card
  overlay). Cables are parameters: saved, preset-able and automatable. 28 factory presets.
- **Nota Keys** — modelled electric piano: Tine, Suitcase, Reed and Clav models (a hammer
  pulse into tine + tone bar, reed or string modes). The PICKUP graph is the transfer curve the
  sound passes — drag for symmetry (X) and distance (Y), with H1–H8 bars and the bark readout;
  Clav shows its string and Upper / Both / Lower pickups. Hammer (hardness, vel → hardness,
  noise), resonator (decay, body, bright, key → bright), damper with release noise and a sustain
  pedal (button, automation or CC64), tune / age / stretch, 8 / 16 / 32 / 64 voices. FX tab:
  preamp (drive, bass, treble), mono or stereo-pan tremolo (free or tempo-synced), phaser,
  chorus, Suitcase / Combo / DI cabinet, volume and pan. Two sizes: L (Sound / FX tabs) and S
  (model, pickup, four knobs). 40 factory presets; MCP `read_keys` / `set_keys`.
- **Nota Mosaic** — multisample instrument: zones (a sample on a key × velocity rectangle with
  its root, fine tune, gain, pan, start / end and loop) in groups with a shared gain / tune and
  a round-robin (Sequential / Random / No repeat), key and velocity crossfades, exclusive groups
  (choke by `off_by`), release samples on key-up or pedal-up that get quieter the longer the
  key was held. Poly 16 / 32 / 64 / 128 (steals released, then the quietest, then the oldest
  voices with an adjustable fade; up to 1–4 strikes per key), Mono with glide, Choke; sustain
  pedal (CC64); MPE (bend with a set range, pressure → level, slide → cutoff); amp ADSR,
  LP / HP / BP filter with keytrack and env → cutoff, a velocity curve. Card tabs Zones (drag
  tiles and edges on the map, the keyboard plays, Attack / Release view), Sample, Groups,
  Pitch, Env, Filter, and a Voices rail; S size shows a mini map. Samples load into RAM in the
  background (notes silent until ready) and stay referenced, not copied — a project or preset
  on a computer without the pack offers to install it or to locate the folder. **SFZ import** for
  installed packs' SFZ programs and over MCP (`#include` / `#define`, Windows paths, CC articulations picked from the tab strip, an import
  report of what was ignored); **Create multisample** maps a folder by file names
  (`trombone_gb4.wav`, `_f_`, `_rr2`, `_rel`), checks the octave convention by pitch detection
  and lists gaps, duplicates and guessed notes (browser folder menu, or files dropped on the
  card). Installed sample packs become presets under Nota Mosaic → Packs. 11 factory presets
  on built-in multisamples (felt piano with release samples, mallets, nylon guitar, brass, pad,
  music box) rendered on first use; Init in the card's preset list empties the instrument; MCP `read_mosaic`, `set_mosaic`, `set_mosaic_zone`,
  `set_mosaic_group`, `import_sfz`, `create_multisample`, `list_mosaic_presets`,
  `load_mosaic_preset`, `scan_sample_packs`.

### Audio effects
- **Nota EQ-8** (kind 0) — 8-band parametric: each band on / off, Low cut / Low shelf / Bell /
  Notch / High shelf / High cut, frequency, gain, Q (resonance on the cuts), a 12 / 24 / 48 dB/oct
  slope for the cuts and a channel — stereo, Mid, Side, Left or Right. Scale (0–200 % of every
  shelf and bell gain), Output ±12 dB and Auto gain. A full-width response graph (draggable
  numbered nodes, the selected band's curve, a separate Side curve, an input / output spectrum
  analyzer switched Pre / Post / Off), band chips, a panel for the selected band; 34 factory
  presets; MCP `read_eq8`, `set_eq8_band`, `set_eq8`.
- **Nota Compressor** (1) — 5 character models (Clean/Glue/Punch/Opto/FET), soft knee,
  look-ahead (reported as latency for PDC), Peak/RMS/Auto detection, hold, auto-release,
  auto-gain, Range, MIX; a sidechain key (internal or another track, pre/post tap, External
  key switch) through 2-pole HP/LP filters with Q and SC Gain, Listen, Stereo link on/off.
  Tabs: Curve (draggable transfer curve + gain-reduction history, crest in → out) / Motion
  (reduction envelope against the input, measured transient and recovery) / Sidechain (key
  spectrum under draggable filters), and Dynamics / Output, with a LEVEL column and a status
  strip. 31 factory presets. MCP: `read_dynamics`, `set_device_sidechain` /
  `get_device_sidechain`.
- **Nota Reverb** (2) — Hall/Room/Plate/Chamber, a decay calibrated as a true RT60, HF damp,
  pre-delay, size, diffusion (input diffuser + tail allpasses), early reflections spaced by
  the algorithm and size, low/high cut, width, modulation on the tail or on the early part
  only, Vintage (band-limited, 12-bit colour); Freeze (tail held, input muted) and Kill
  tail; output stage with dry level, bass mono, wet only and latency compensation for the
  diffuser. Tabs: Space / Tone · Mod and Levels / Output, over a live decay-tail window
  (drag the pre-delay marker or the tail). 30 factory presets.
- **Nota Delay** (3) — independent L/R times (ms or tempo-synced 1/16…1/2 with triplets
  and dotted values), Link, feedback, spread, ping-pong; a tone stage in the loop (low cut,
  high cut, allpass diffusion, tape saturation) with tape WOW modulation; Freeze (loop held,
  input muted) and Clear loop; a time change either crossfades (Fade on change) or glides,
  bending the pitch tape-style; output stage with dry level, width, bass mono, wet only and
  latency compensation for the diffuser. Tabs: Time / Loop · Wow and Levels / Output, over a
  live repeat window that lights the repeat sounding now. Tap tempo. 29 factory presets.
- **Nota Utility** (4) — gain, L/R balance, stereo width 0–400 % with an L/R or M/S law, channel
  mode (Stereo / Left / Right / Swap), mono below a cutoff at 6 / 12 / 24 dB/oct, mute, phase
  invert per side; Gain match and continuous Level match to the input or a Target (LUFS-S / peak /
  RMS), a zero-latency true-peak limiter. Tabs: Field (the stereo field by frequency, drag balance /
  width) / Mono (width over frequency, drag the cutoff) / Levels (in and out over 8 s against the
  target) and Routing / Output, with a LEVEL column, correlation and a status strip. 30 factory
  presets. MCP: `read_utility`.
- **Nota Valve** (6, formerly Amplifier) — guitar amp: 7 models (Clean / Boost / Blues /
  Rock / Lead / Heavy / Bass), gain, a Bass / Middle / Treble / Presence tone stack with a
  sweepable middle, Bright and Deep, an even-harmonics-only preamp; a cabinet (Match / 1×12 /
  2×12 / 4×12 / 1×15 with their resonance and presence peak) heard through a Dynamic /
  Condenser / Ribbon mic at a distance, off axis, at the cap or the edge; low / high cut,
  noise gate, auto gain compensation, mix, output, oversampling. Tabs: Amp (the tone stack's
  response, drag the middle) / Cab (the cabinet against on-axis, drag the mic) / Harmonics
  (f … 7f, THD, aliasing) and Cabinet / Output, with a STAGE column and a status strip.
  31 factory presets. MCP: `read_valve`.
- **Nota Auto Filter** (7) — LP/BP/HP/Notch with morph, 12/24 slope, Clean/Analog, drive;
  an envelope follower (attack, release, timed or infinite hold, on the input or a sidechain
  key) and an LFO (5 waves, morph, free or tempo-synced, start phase, stereo phase, Retrig on
  onsets) driving the cutoff, the resonance or both, with Smooth. Tabs: Filter (draggable
  response over the spectrum, modulation range) / Envelope (cutoff over time) / LFO (motion
  over two bars) and Mod / Output, with a SHAPE column and a status strip. 30 factory
  presets. MCP: `read_filter_motion`.
- **Nota Auto Shift** (10) — real-time vocal pitch correction: an FFT NSDF pitch tracker
  with a detection range (voice-type presets), sensitivity and sibilant detection; Key +
  Scale (5 scales, 8 modes, a Custom 12-note set), Auto key and one-shot Learn (Krumhansl
  key profiles over the sung-note histogram), Follow scale device, or a MIDI target from
  another track's notes (Note / Scale, Latch, Oct lock, Glide within a bend range); Amount,
  Speed, Range, Human (keeps vibrato); a PSOLA shifter with Shift, Fine, Preserve formants
  and Formant shift, Skip sibilants, Mix, latency compensated. Trace / Scale / MIDI graphs, a
  PITCH column, Shift / Detect panel; 34 factory presets; MCP `read_auto_shift`.
- **Nota Vintage** (8) — degradation and saturation: 6 era characters
  (Vinyl/Cassette/Reel/VHS/Tube/Analog, with the voicing switchable off), drive, tone with a
  Warm / Flat / Dark model and low / high shelves, wow (free or tempo-synced) and flutter with
  their rates, noise with a hiss high-pass, crackle, wear, wear that follows the input, stereo
  drift, a Tube / Analog output stage, even-harmonics-only saturation, auto gain compensation,
  mix, output, oversampling. Tabs: Curve (the transfer curve, the input on it) / Wear (pitch
  drift over 4 s in cents) / Output (harmonics against the hiss floor) and Tone / Output, with
  a STATE column and a status strip. 30 factory presets. MCP: `read_vintage`.
- **Nota Beat Repeat** (11) — tempo-synced repeater: Interval (1/8 … 4 bars, bars follow the
  time signature), Offset, Grid with Triplet, Variation, Chance, Gate; Mix / Insert / Gate
  modes; Pitch and Pitch Decay, Decay, Volume; a repeat filter (LP / BP / HP, width in
  octaves, optionally narrowing with each repeat); Repeat (hold or Latch) and Mix. Timeline /
  Slices / Filter graphs (drag the offset, the gate and the filter), a REPEAT column, a state
  box and meters; 31 factory presets; MCP `read_beat_repeat`.
- **Nota Orbit** (9, formerly Auto Pan) — auto-pan and tremolo on one LFO: L/R phase (0° =
  tremolo … 180° = pan, with 0° / 90° / 180° buttons), Sine / Tri / Saw / Sqr / S&H, Shape
  (Glide on S&H), Amount, Mix; rate free (0.01–40 Hz) or synced (4/1 … 1/32 with dotted and
  triplet values, locked to the bar). A gain graph over two cycles (drag Amount and Phase),
  the pan line with its swing, OUT L / R meters and a status strip; 31 factory presets; MCP
  `read_orbit`.
- **Nota Crush** (12) — bit crusher: Bits (1–24), Rate (fractional sample and hold), Drive,
  Wet; Digital / Analog / Fold modes; Anti-alias (4th order); GRIT (Dither / Jitter / Noise);
  a post filter, output gain, Auto gain and a DC filter. Quantiser / Spectrum (engine FFT:
  signal, added content, images above the Nyquist) / Transfer graphs (drag Bits and Rate, the
  Nyquist and the filter, Drive), a CRUSH in/out column, Grit / Output panels; 33 factory
  presets; MCP `read_crush`.
- **Nota Ceiling** (14) — look-ahead brick-wall limiter: Gain, Ceiling, Release (with a
  program-dependent auto stage), Clean / Punch / Glue characters, look-ahead, stereo link,
  True Peak (4× inter-sample detection), Delta (hear what it removes), a key high-pass and an
  external key; a BS.1770 / R128 loudness meter (LUFS M / S / I, LRA, PLR, true peak) against
  a Target. Level / Reduction / Loudness graphs (drag the ceiling and the target), a LIMIT
  column, Meters / Detector panels; 32 factory presets; MCP `read_ceiling`.
- **Nota Dynamic EQ-8** (13) — 8-band parametric with dynamics: each band Static / Duck
  (above the threshold) / Lift (below it), threshold, signed range (cut or boost — upward and
  downward expansion too), attack / release and its own key (Self or a key track); a Dynamic
  master switch, Solo, output. A full-width response graph (live curve, static curve while
  they differ, the output spectrum, draggable numbered nodes with a whisker to each dynamic
  band's range and its gain now, a tooltip with a 2 s gain history), a strip of band chips
  with their activity, a panel for the selected band with the band's level on the threshold;
  33 factory presets; MCP `read_dynamic_eq`, `set_dynamic_eq_band`.
- **Nota Strata** (15) — multi-layer overdub looper: layers with waveform, level and mute;
  Record/Overdub/Play/Stop, Undo/Clear, Feedback, input gain, speed/reverse, quantize,
  count-in, set-tempo, Export; audio layers persist into the project.
- **Nota Forge** (17) — multi-stage saturator: 3 stages (Tube/Tape/Diode/Fuzz/Fold/Digital),
  each with drive, output trim, feedback and its own Bias / Tone / Width; Serial / Parallel /
  Mid-Side (Mid · Side · M+S) / Multiband (< 180 Hz · mids · > 2.4 kHz) routings; Amount /
  Wet / Out; LFO→Drive (free or synced) and Env→Tone; a transfer curve (whole device or the
  selected stage, each stage faint, the modulated curve, the live input) and harmonics 2–9
  with THD from the engine; **OVERSAMPLE** (Off/2×/4×/8×) with an aliasing warning; 32 factory
  presets; MCP `read_forge`, `set_forge`, `set_forge_stage`.
- **Nota EQ-3** (16) — three-band DJ isolator: Low / Mid / High faders (−24 … +6 dB, or the
  Classic ±15 dB range) with a 0 dB detent and KILL buttons, two crossovers, 24 / 48 dB/oct
  Linkwitz-Riley slopes (phase-aligned, flat at unity), output gain. A response graph with each
  band's curve, the brass sum and the output spectrum (engine FFT) — drag the crossover handles,
  or ride a band's gain in its zone; a status strip; 30 factory presets; MCP `read_eq3`,
  `set_eq3`.
- **Nota Level** (18) — loudness leveler (BS.1770 LUFS): Auto rides the gain to a Target
  (−36 … 0 LUFS, standards menu) over Window (Fast / Slow), Manual holds a draggable fader and
  MATCH sets it to the distance to the target; Momentary / Short-term / Integrated (gated)
  scales; Max Gain, Trim; holds the gain in silence and release tails; True-peak safe
  look-ahead limiter (5 / 20 ms, PDC) with a Ceiling; match to a reference track (sidechain).
  An 8 s loudness graph (drag the target), IN / OUT / TP / correction meters and a status
  strip; 30 factory presets; MCP `read_level`, `set_level`, `level_match`.
- **Nota Shutter** (19) — noise gate and ducker: Threshold/Return (hysteresis),
  Attack/Hold/Release with a Linear / Log / Snap shape, Floor (range), Lookahead, Flip
  (ducker), Retrigger (trigger mode), a 12 dB/oct detector band-pass (switchable) with Listen,
  Peak hold, internal or external key; Signal / Envelope / Sidechain graphs (drag the
  threshold, the envelope nodes and the key filter), a STATE column, a state box, meters
  with peak GR and openings per bar; a Pattern tab — a tempo-synced rhythmic gate: an 8 / 16 / 32
  step grid at 4 / 8 / 16 levels drawn with Draw / Line / Erase / Flip, pattern shapes, a
  Complexity-weighted dice, Smooth and Swing on the gain curve, Rate (1/4 … 4 bars in Sync,
  4 … 0.25 Hz in Free), Depth, Copy / Paste / Clear, MIDI-note retrigger, Gated (pattern × gate)
  and a Volume or Map target (Map drives the Pattern Out CV source instead of the audio), every
  step automatable; 45 factory presets (15 pattern); MCP `read_shutter`.
- **Nota Chamber** (20) — hybrid reverb: a zero-latency convolution engine (16 synthesised
  IRs — halls, rooms, plates, spring, spaces, FX — or your own WAV/FLAC/MP3, mono / stereo /
  4-ch true stereo, dropped on the IR view) with Start/Decay trims, Attack, Size, Reverse,
  True Stereo, and an algorithmic engine (Dark Hall / Plate / Quartz / Shimmer) with
  per-band decay, diffusion, damping, modulation, Freeze + Hold in, Vintage and a pitch
  shifter; Blend (parallel or serial), separate free/synced pre-delays, a 4-band EQ at the
  input / tail / output, ducking, width + bass mono, Dry/Wet, wet-only send mode. IR edits
  morph the tail without clicks; user IRs persist in the project.
- **Nota Prism** (21) — three-band dynamics (multiband compressor / expander): Linkwitz-Riley
  crossovers dragged on a live input/output spectrum, 3 / 2 / 1-band modes; per band a
  compressor above one threshold (ratio to ∞:1, knee) and an expander or upward compressor
  below another (limited by Floor), each with its own attack/release (auto release above), plus
  band gain and solo. Amount scales every ratio; Peak/RMS detection, lookahead (PDC), a
  band-split external sidechain with Listen, auto makeup, Mix, output gain and a −0.3 dBFS soft
  clip. Transfer curve with drag handles, detector-envelope trace, per-band GR meters.
- **Nota Lens** (22) — analyzer: spectrum, triggered oscilloscope and waterfall in one card,
  passing the audio through untouched. *Spectrum*: FFT 512–16384, Hann / Blackman-Harris /
  flat-top window, frame averaging, fractional-octave smoothing, fall time, pink tilt, peak
  hold, and a pointer cursor reading frequency, note, cents and level. *Scope*: the trace is
  pinned to the trigger point and stands still while older passes fade; Auto / Normal / Single
  on a rising or falling edge, a level dragged on the trace, holdoff, 20 µs–20 ms per division,
  A/B cursors for Δt / 1/Δt / ΔV with the matching note, and Export WAV of the captured window.
  *Waterfall*: a 4 / 12 / 60-second spectrogram with gain, floor, contrast and FFT overlap.
  A permanent SCALE rail, Freeze, a note grid, a linear or log frequency axis, an L+R / L / R
  source that can read Mid/Side, and a status line with peak, crest, momentary LUFS and L/R
  correlation. Readable from MCP with `read_analyzer`.
- **Nota Flanger** (23) — a comb filter on a short LFO-swept delay: Delay 0.1–8 ms, Depth,
  bipolar Feedback ±95 % (positive = jet; negative flips the wet polarity so the comb mirrors
  into a hollow tone; − / 0 / + snaps), Mix; Sine / Tri / Saw LFO, free (0.02–8 Hz) or synced
  (2/1 … 1/16 with dotted and triplet values, locked to the bar), Stereo offset 0–180°. A live
  comb-response graph of L and R (drag the notch for Delay, up / down for Feedback), the delay
  of both channels over two cycles, the first notch's sweep and null depth, and a status
  strip; 31 factory presets; MCP `read_flanger` / `set_flanger`.
- **Nota Phaser** (24) — 2 / 4 / 6 / 8 / 12 LFO-swept all-pass stages (half as many notches) with
  delay-free feedback: Center 50 Hz – 5 kHz, Depth ±3 oct, bipolar Feedback ±95 % (negative flips
  the wet polarity: the notches move and widen), Mix; Sine / Tri / Saw LFO, free (0.02–8 Hz) or
  synced (4/1 … 1/16 with dotted and triplet values, locked to the bar), Stereo offset 0–180°. A
  live all-pass response of L and R with the notches ticked (drag for Center, up / down for
  Feedback), the corner of both channels over two cycles, the sweep and null depth, and a status
  strip; 35 factory presets; MCP `read_phaser` / `set_phaser`.
- **Nota Chorus** (25) — modulated delay voices: Classic (2 voices L / R, 8 ± 5 ms), Ensemble
  (3 voices 120° apart, 12 ± 6 ms) and Vibrato (3 ± 2.5 ms, wet only); sine LFO free (0.02–8 Hz) or
  synced (4/1 … 1/16 with triplets, locked to the bar), Offset 0–180°, a wet HPF (off / 20 Hz – 2 kHz),
  Amount, bipolar Feedback ±90 %, Width 0–200 %, Warmth (dark, saturated BBD line), Mix. Every voice's
  delay or detune over two cycles (drag for Amount), the voices' stereo field, the detune and delay
  ranges, and a status strip; 34 factory presets; MCP `read_chorus` / `set_chorus`.
- **Nota Vocoder** (26) — a channel vocoder: the track it sits on is the voice (modulator, Mod gain,
  key meter); the carrier is an internal Saw (at a Pitch, C0–C7 — a robot voice by default), Noise,
  or another track picked on the card with its colour (Pre / Post tap; keeps feeding while muted),
  with a Tilt. 8–32 bands between Low and High, Q, Formant ±12 st, Attack, Release; Unvoiced noise
  for consonants, Hold, Mix, Gain. A live graph of each band's voice level and the carrier, with
  draggable range edges; 7 factory presets; a carrier track is routed over MCP with
  `set_device_sidechain`.
- **Forge**, **Valve** and **Vintage** each carry an **OVERSAMPLE** selector
  (Off/2×/4×/8×) to suppress aliasing under heavy drive.

### MIDI effects
- **Nota Arp** — arpeggiator in two sizes (L 700 / S 260, S·L toggle; one preset list and
  current preset for both): 9 orders (Up, Down,
  Up·Down, Down·Up, Converge, Diverge, Random, Chord, As played) over 1–4 octaves; a live
  Pattern view of the notes each step plays and a Groove view of five per-step lanes
  (Velocity/Length/Chance/Ratchet/Transpose) plus step mute; Free/Sync (1/1…1/32), Gate,
  Swing, Vel Amt, Retrig Off/Note/Beat, Transpose, Steps 1–16, Hold, Restart. 30 presets,
  full automation, MCP tools (`get_arp` / `set_arp` / `set_arp_lane` / `set_arp_step` / `restart_arp`).
- **Nota Scale** — snaps notes into a scale, in two sizes (L 700 / S 260, S·L toggle; one
  preset list for both): Root, Major/Minor/Dorian/Phryg/Penta/Custom (older scales such as
  Lydian or Harmonic minor still load and show by name), Fold Nearest/Down/Up, Transpose, a
  clickable NOTE MAP (C → B as key cells: scale notes filled, root outlined, the rest showing
  where they fold; the note playing lights up with its target and fades out), last IN → OUT,
  Range (outside passes through), Follow key (the root follows what you play), Learn (a mode
  that builds a Custom scale from the notes you play) and Clear. Held notes release correctly
  when the scale changes. 35 presets, full automation, MCP tools (`get_scale` / `set_scale` /
  `set_scale_note`).
- **Nota Length** (formerly Note Length) — forces note lengths: Sync (1/32 … 1/1, 1/8., 1/4T) /
  ms / Gate % (of the held length; clip notes can also shorten), start from note-on or
  note-off, bipolar Vel → Len and Key → Len, Random, Legato (to the next note), Clip length
  limit (the bar line). Two sizes (L 700 / S 260) with an animated GATE window: one bar of five
  notes, input outlined, output filled, Random's range as a band. 32 presets, full
  automation, MCP tools (`get_length` / `set_length`).
- **Nota Velocity** — velocity transformation: Curve / Compand / Fixed, Drive (0.25 … 4) or
  the Fixed value, Out range (1 … 127, never 0), Random ±0 … 64 with its own switch and Both /
  Up / Down. Two sizes (L 700 / S 260) with an animated TRANSFER graph (curve, Random's band,
  the last 12 notes as dots) and a LAST 12 NOTES histogram (in outlined, out filled). 32
  presets, full automation, MCP tools (`get_velocity` / `set_velocity`).
- **Nota Random** — randomization: Chance; Note (±12 st) / Velocity (±64) / Timing (up to
  100 ms late) / Skip / Octave (±2) amounts; Gauss / Even / Walk; Per note / Per bar; Stay in
  scale (C major); seed with Reroll and Lock (holds the roll on screen, repeating it every bar).
  Two sizes (L 700 / S 260) with an animated OUTPUT window (one bar of eight notes, input dashed,
  output filled by velocity, × for skipped) and a DICE histogram drawn from the same generator.
  32 presets, full automation, MCP tools (`get_random` / `set_random`).
- **Nota Chord** — chord generator in two sizes (L 700 / S 260, S·L toggle; one preset list
  for both): TYPE Maj7/Min7/Sus4/5th/Custom, six switchable shifts (±12 st ruler + interval
  name + velocity offset), Strum 0–100 ms (low → high), Spread (every other / all shifts up an
  octave), Keep root, Fold in scale (any key, major / minor); a three-octave RESULT keyboard
  that lights a held chord as it strums (and previews the strum once per bar). 34 presets,
  full automation, MCP tools (`get_chord` / `set_chord` / `set_chord_shift`).

### Racks
- **Nota Instrument Rack** — 8 named macros (mapped with Linear/Exp/Log/S curves), chains
  with **key and velocity zones**, gain/meter/M·S, horizontal device cards (GUI / Params),
  a zone map, rack output (Volume/Glide), Fold/Save.
- **Nota Drum Rack** — 4×4 pads (banks C1–C4, up to 64 pads), Pads, Mixer, Chain and Macro views,
  per-pad Volume/Pan/Tune/Decay, named pads (rename, re-choke or remove from a pad's
  right-click menu), choke groups (monophonic cut), Swing/Humanize, hot-swap, a pad
  oscillogram with a playhead, and each pad's own effect chain. Eight **macros** over the kit
  map onto pad controls (Volume / Pan / Tune / Decay), pad instruments and the pads' effects,
  with draggable ranges per receiver. A **kit picker** in the
  card header loads any factory kit in place and names the kit a rack holds. Its clips can
  be stepped in the detail panel's **Pattern** tab (see below).
- **25 factory kits** under Nota Drum Rack and Nota Rhythm in the browser and in their cards' kit pickers:
  *Volta* (warm analog boom), *Kompakt* (punchy analog house), *Micron* (small vintage
  rhythm box), *Linnwood* (80s PCM machine), *Atelier* (acoustic studio kit), *Cellar*
  (dusty vinyl break), *Neon* (modern sub-forward, tuned 808 bass), *Foundry* (industrial
  metal), *Terra* (hand percussion), *Aether* (ambient), *Brass Room* (live rock kit in a
  big room), *Breakline* (jungle break), *Bunker* (warehouse techno), *Pixel* (minimal),
  *Velvet* (jazz brushes), *Circuit* (electro box), *Yard* (dub and reggae), *Byte* (8-bit
  chip), *Lagoon* (amapiano log drums), *Titan* (trailer percussion), *Shuffle* (UK
  garage), *Mirrorball* (70s disco), *Pit* (orchestral percussion), *Hyper* (hyperpop) and
  *Crate* (90s boom bap). 16 pads each on the General MIDI map (36–51), hats choked together,
  with the kit's own swing and humanize, and each pad's own effects (kick saturation, snare and
  clap reverb, hat and percussion delay, scaled to the kit), and the kit's own macros (Tune,
  Decay, Hats, Drive / Tape, Room, Echo, 808 / Sub, as the kit has them). Load onto a new track or drop onto
  an existing Drum Rack or Rhythm to replace its pads / voices. The one-shots are synthesized on first launch rather than
  shipped as audio (nothing added to the installer) and appear in the Files tab under
  **Nota Kits**, ready to drag anywhere a sample goes.
- **Nota Audio Effect Rack** — **Parallel / Series / Select** modes (by input level),
  Dry/Wet + Gain, PDC, Fold, Save; chains with gain/meter/M·S; named macros and mappings;
  the "+ Device" menu lists built-in effects and a Plug-ins submenu.
- Devices inside chains appear as cards with knobs, a **Full** button (the device's
  complete UI in a popup — the native GUI for a plugin, the tabbed editor for the
  Sampler), a ✕ delete button, and drag-to-reorder.

---

## Plugin hosting

- **AU** (macOS) and **VST3** (all platforms) — instruments and effects.
- **Scanning** and a catalogue of installed plugins (a separate `nota-scanworker`), scan
  paths in Preferences, and Rescan. Four plugins load at a time, each with a time limit
  (a stuck one is retried alone, then skipped); progress shows as "312 of 1,000"; results
  are saved as the scan goes; plugins that failed are skipped until they change or a Rescan;
  each scan writes `logs/plugin-scan.log`.
- **Downloads** (Settings → Downloads, formerly Get Plug-ins) — open-source VST3 plugins
  from the [Nota plugin registry](https://github.com/nota-daw/nota-plugins-registry)
  (Surge XT, Dexed, Dragonfly Reverb, CHOW Tape Model, Stochas …), installed, updated and
  removed with a click (checksum-verified; archives only, installers never run). Instant
  search and filters with counts, macOS / Windows / Linux icons per plugin (hover for the
  supported processors), download sizes, and plugins this computer can't install listed
  last with the reason. A progress bar with **Cancel** stays at the bottom of Settings while
  an install runs. A project that needs a missing registry plugin offers to install it and
  reopens with its saved settings. Plugins are found even when installed in a different
  folder than on the computer that saved the project.
- **AI Models** (Settings → Downloads, third side): **htdemucs** (Separate Stems, MIT) and
  **basic-pitch** (Convert to MIDI, Apache-2.0), plus the **ONNX Runtime** 1.23.2 library they
  run on, which comes with the first model and goes with the last. Every asset is pinned to a
  URL, size and sha256 in the app; models install into Nota's data folder and run offline.
- **Plugin device card** in two sizes: the small card shows vendor, format, parameter count
  and latency with Open editor and Save preset; the full card adds every parameter as a knob
  (with filter and pages, the plugin's own value text, double-click for its default) and a
  sidechain source where the plugin has one.
- **Loading** into a track or rack chain, with the plugin's **native GUI** (an editor
  window that opens on top and focused, and closes when the device is removed).
- **State save and restore** in the project, and **bypass**.
- **PDC** — plugin delay compensation (including a toggle in the Audio Effect Rack).
- **Transport sync** — plugins receive tempo, position, play state and loop boundaries
  through the host playhead (synced devices, tempo delays and LFOs, arps, loopers).
- **The computer keyboard plays MIDI** while a hosted plugin window has focus.
- Plugin parameters are **MIDI-learnable** and **automatable**.

---

## Browser and assets

- An **island panel** with an icon rail down its left edge: **Instruments / Audio Effects /
  MIDI Effects**, then **Files** (with folder tree navigation), **Presets** (a tree of
  category → device → preset), **Projects**, **Map** (MIDI Learn mappings) and **History**
  (the open project's versions). A hairline
  separates the device tabs from the library tabs, and the active one carries an orange edge.
- **Category chips**: above each device tab's list, one chip per kind of device — Synths /
  Keys & physical / Drums / Samplers / Racks; EQ & filter / Dynamics / Reverb & delay /
  Modulation / Distortion / Pitch & time / Utility / Racks; Generate / Shape. A click
  narrows the list to that category, and a second click shows everything again. Each tab
  keeps its own pick, and it combines with search and the Favorites and tag chips.
  Installed plug-ins join the same categories from the category they report, under an
  **Other** chip when Nota doesn't recognise it.
- **A compact index**: single-line rows, sectioned into **Built-in** and **Plug-ins** with
  counts, so it is clear where Nota's own devices end and the scanned plug-ins begin. Built-in devices show their short name ("Aurora" — search still matches
  "Nota Aurora"), and the device type sits at the right edge as a quiet tag (a plug-in's
  tag names its format and vendor, which tells the AU and VST3 builds of one plug-in
  apart). The name always wins the room: the tag gives way rather than truncating it.
- **Factory presets in folders**: a built-in device expands into category folders — Pads /
  Bass / Leads / Keys & Plucks … for instruments, Vocals / Drums / Mix & Bus / Mastering /
  Ambient / Sidechain … for effects, Triads / Sevenths, Grooves / Ratchets … for MIDI
  effects — with its Init loose on top. Search opens the folders around the hits, and a
  folder name ("Mastering") lists that folder in every device.
- **View options** (the ⋮ button beside the search box): show type tags, category chips,
  group by source, favourites first — each remembered across sessions — plus the tag editor.
- A **status line** counts what the tab is showing (`13 built-in · 4 plug-ins`), and the
  search box counts the matches.
- **Sample player** under the Files tab: the file's real waveform, drawn as Bars or Solid
  like the arrangement (Settings → Appearance → Waveform; click to play from a point,
  hover for the time), Play / Stop, time readout, Loop, Auto (play on select) and a
  preview volume fader; ↑ ↓ step through files and Space plays the selected one. It stays
  hidden until you use a list and tightens in a narrow browser. Space in the arrangement
  always drives the project transport.
- **Smart samples**: the Samples folder is analysed in the background — tempo, key, loop or
  one-shot, and a timbre fingerprint — once per file (results kept across launches, a newly
  installed pack first; the status line shows the progress). Names win over the analysis
  ("Loop_124_Am", "Pad C#m 90bpm", a "One Shots" folder), and a loop's exact tempo comes from
  its length (16 beats in 7.742 s is 124.0). Rows carry `124 · Am` (a tuned hit, its note).
  **Filter chips** on the Files tab: Loops, One-shots, a BPM range (with "project tempo
  ±5 %") and a Key (with the project key; a key also matches its relative). **Show similar
  sounds** (right-click a sample) lists the closest samples of the same kind.
- **Drag and drop** onto a track, into the grid, or into a rack chain.
  - A **loop** dropped on the arrangement warps to the project tempo from its known tempo
    (⋮ → Warp loops to project tempo, on by default); a **one-shot** is never warped. With
    ⋮ → **Transpose to project key**, a loop moves to the project key the shorter way
    (±6 semitones, via the relative across major and minor).
  - Dropping an effect onto Devices puts it between cards (an accent bar marks the spot) or
    in place of the card under the pointer; effects and presets also drop onto a return
    track or the master. While dragging over the arrangement, the target track is outlined
    in its header too.
  - Dropping an instrument onto an existing track **replaces the instrument** in place
    (clips, devices and volume are kept); dropping onto empty space creates a new track.
    Racks are not replaced in place. An instrument preset dropped onto an instrument track
    loads into that track's instrument (switching it if needed; the effects stay).
  - A drop selects the track it lands on (a newly created one included), in the
    arrangement and the session view.
- **Double-click an instrument** to put it on the selected MIDI / instrument track in place
  of its instrument (clips and effects stay; one undo step), or on a new track when no such
  track is selected or a rack is. ⌥ + double-click always starts a new track.
- **Preset preview**: the same player sits under the Instruments, Audio Effects, MIDI
  Effects and Presets tabs and plays the selected factory preset, saved preset, drum kit or
  built-in device (its default sound). These tabs have their own **Auto**, off by default:
  Space / Play auditions, Auto plays on select. The sound is rendered offline on a standalone
  chain (not a track) in a few milliseconds and cached, and the row below is rendered ahead,
  so ↑ ↓ through a folder plays each sound at once. Instruments play a phrase that suits
  them — a chord for pads, a riff for basses, a line for leads, an arpeggio for plucks and
  bells, a held chord for Pendulum / Consort. **Drum Rack and Nota Rhythm kits** play a
  groove on their own pads (gain, pan, choke groups and pad effects as when loaded) at a
  tempo that fits the kit. MIDI effects play a phrase that shows what they do into a plain
  keys patch. Audio effects process a **demo track**: Drums / Keys / Bass parts, or genre
  loops (Pop, House, Techno, Hip-Hop, Trap, Drum & Bass, Synthwave, Ambient) built from
  Nota's factory kits and presets and levelled alike. A track that suits the effect is
  picked for you (dynamics hear drums, reverbs and delays keys, EQ and drive the pop mix);
  the track button in the player picks another (scroll over it to step), or the **sample**
  last selected in Files.
  **WAVE / SPEC** in the well switches to a live spectrum of what is playing. Instrument
  Racks and plug-in presets aren't previewed (the well says why).
- **Favourites** (★) and **tags** (assign and clear, an editor with a title and colour,
  filter chips in the header); favourited devices sort to the top of their section. The
  chips keep to one line — whatever does not fit collapses into a **+N** that opens the rest.
- **Context menus**: Projects — Open / Reveal in Finder / Delete (to the Trash, with
  confirmation); Files — Show similar sounds / Reveal in Finder; Presets — Reveal in Finder.
- **Hints for empty tabs** — explaining what the tab is and how to add content to it.

---

## Export

- **Export the master** to WAV, FLAC or MP3: range (the loop region, the full song, or a
  Custom start and end typed as bar.beat), sample rate and, per format,
  bit depth (**pcm16 / pcm24 / float32** WAV; **16/24-bit** FLAC) or MP3 quality
  (**CBR 128/192/256/320 kbps**, 320 default). MP3 tops out at 48 kHz, so 96 kHz is
  not selectable for it; the footer size estimate follows the format.
- **Export stems** (individual tracks) in the selected format.
- **Normalize −1 dBTP** — against the true (inter-sample) peak, estimated with 4×
  oversampling: one render to a temporary file, peak measurement, then a rewrite with the
  exact make-up gain, so there is no risk of overshooting the ceiling because a second
  render differed. Stems share a single gain (taken from the full mix peak), so they still
  sum to −1 dBTP and keep their balance.
- **Dither** — TPDF dithering before quantizing to 16 bit (offered only for 16-bit WAV
  and FLAC targets; 24 bit, float and MP3 do not need it).
- **Add 1-bar release tail** — a decay tail after the end of the project.
- **Bit-identical** chunked rendering (caches rebuilt once when the rate changes) — no
  clicks at block boundaries, and warped clips keep their length at any export rate.
- **Progress** for long operations: export and conversion show a modal progress dialog;
  audio import shows a thin bar in the status bar.

---

## Windows and interface

- **Command palette (⌘⇧P / Ctrl+Shift+P)** — one search field for everything, from any
  window and any focus (a text field included): every menu and view command (with its
  shortcut), every track including returns and the master, built-in devices, scanned
  VST3 / AU plug-ins (vendor and format shown), factory and user presets, drum kits, and —
  when opened from Modular — Modular nodes (LFO, Envelope Follower, MIDI → CV, ADSR, Macro,
  Math, Scope). The chosen item goes **where the palette was opened**: in Devices next to the
  selected card, in Modular onto the shown track (a node unconnected, at the pointer or the
  middle of the view), elsewhere the selected track; the field shows the target
  ("→ Bass · after Nota Vintage") and why an item can't go anywhere. ⌘Enter puts an
  instrument on a new track, ⌥Enter replaces the selected device, ⇧Enter keeps the palette
  open for the next one; every apply is one undo step, with an Undo toast. Search is fuzzy
  ("afl" → Auto Filter), forgives one typo per word, reads a query typed in the wrong
  keyboard layout ("кумуки" → reverb) and understands meaning in English and Russian —
  "warm pad", "reverb for vocals", "компрессор на барабаны" — through a semantic descriptor
  every device, plug-in and preset carries (role, character, part, source, task, genre); a
  semantic hit says why it was found ("warm · pad"). Prefixes `>` `@` `+` `#` `~` (or Tab)
  filter by type; an empty query shows Recent and suggestions for the context. Ranking
  learns from what you use in the palette and the browser.
- **Studio look, in colour themes** — **Studio Dark** (neutral studio greys with one
  orange accent; the default, and what a DAW wants for long sessions in a dark room),
  **Studio Light** (the same system in light greys, the orange a shade deeper), and
  **Ember Graphite** / **Ember Paper** (warm browns with a brass accent, dark and light).
  A theme changes colours only — track colours included — never the layout. Panels dock edge
  to edge on thin seams; orange marks only what is playing, selected or switched on, and a
  knob or fader turns orange while you hover or drag it; mute is amber, solo blue,
  record-arm red; labels are in sentence case. Switching applies immediately, across every
  open window — including the custom-drawn arrangement, piano roll, mixer and device cards.
- **Right-click menus, one system** — every context menu opens with the object it acts on
  (name, colour, type and position: "Vocals · Audio · 3", "3 tracks", "MIDI clip") and lists
  its commands in one order: properties → settings → actions → create / group → Copy · Cut ·
  Paste · Duplicate → Delete, last and in red. Icons are neutral, a chosen option carries a
  tick, a submenu shows its current value (Record input **Ext**, MIDI from **None**) and
  shortcuts read ⌘G / ⌘⇧G on macOS, Ctrl+G elsewhere. The track menu sets the colour (27
  shades and Auto) and the height in one click, and shows **Live freeze** as a switch —
  on while the track is live-frozen, off to unfreeze.
- **A single frameless window style**: centred title, drag by the header, the macOS
  traffic lights in a left inset (Windows gets its own title bar; Linux uses the system
  frame). **Full screen** — the macOS green button, or View → Toggle Full Screen (F11)
  on Windows and Linux — hides the title bar and its separator so the workspace fills the
  screen edge to edge; leaving full screen brings the bar back. Maximized keeps the bar.
- **What's New** — a window showing the release notes (`release-notes/X.Y.Z.en.md`, the GitHub
  release text; the CHANGELOG section for versions without them) newer than `LastSeenVersion`, once
  after the first launch on a new version; any time from Help ▸ What's New or the version
  number in the status bar.
- **Self-update** — Nota looks for a newer GitHub release on every launch and once a day while
  it runs (Settings ▸ Updates turns that off), and on demand from Help ▸ Check for updates…
  (also in the macOS app menu), which also says when you're up to date or couldn't check. A
  newer release shows in the start screen's banner and in the status bar beside the version.
  **Update** downloads the build for this platform (with progress, checked against the
  release's sha256). **Restart now** installs it and relaunches, and **Later** installs it on
  quit. This covers the macOS .app, the Windows installer (run silently) and the Linux
  AppImage. Other copies fall back to a link to the release page.
- **About** — the app and engine versions, the copyright notice, and a pointer to the
  third-party attribution notices (`LICENSES/THIRD-PARTY-NOTICES.md`).
- **Settings** — a larger window with a sidebar grouped into Devices, Plug-ins and General
  (each page with an icon and a short description), large switches, latency in
  milliseconds, colour themes shown as miniature Nota windows, and key-cap shortcuts with a filter.
- **Device window** (a device opened from a Drum Rack, Instrument Rack, Audio Effect Rack or
  a Nota Rhythm voice): the title bar carries the device, track, preset picker, bypass and
  S / L size switch; a footer shows where the device sits, its live voice count and type.
- **Edit Tags** — the browser's tag editor.
- **Devices/Clip panel in its own window** — the ⧉ button in the bottom panel's header
  detaches it (the selected clip's piano roll on top, the device chain below, both at
  once). The content moves across as-is: edits, meters and graphs all keep working live.
- **Popup rack editors** (full UI / Params).
- **Typed values** — double-click the number next to a slider in a device card, the clip
  editor or a rack macro and type it: "−6", "1.5k", "250 ms", "L 30", "1/16".
- **User manual** (EN / RU, https://nota-daw.github.io/nota-docs/) — **Help ▸ Documentation**
  opens it in the system language, the start window links to the first-track tutorial, and
  each device header menu's **Documentation** item opens that device's page.
- **A shortcut list** in Preferences → Shortcuts, grouped by section (File, Command palette,
  Transport, Arrangement & Editing, Piano roll, Play notes, Mouse).

---

## Preferences

- **Audio**: device, sample rate, buffer size (latency); **WASAPI exclusive mode**
  (Windows).
- **MIDI**: which MIDI inputs are enabled (the house checkboxes); **MPE** on/off (on by
  default) and the per-note bend range (±12 / 24 / 48 / 96 semitones).
- **Gamepads** (macOS): enable gamepad input (notes and mapped controls); the controller
  list updates on hot-plug, with a live activity indicator beside each pad that names the
  note played or the control driven.
- **Plug-ins**: scan paths, Rescan.
- **Downloads**: Plug-ins, Sample Packs and AI Models (see [Plugin hosting](#plugin-hosting)).
- **Library**: library folders.
- **Appearance**: **Colour theme** — **Dark**, **Light** or **Follow system** over two
  rows of cards (Studio Dark and Ember Graphite; Studio Light and Ember Paper), each a
  miniature Nota window in its own colours. Pick one theme, or with Follow system one for
  dark mode and one for light mode, switched with the OS appearance; the choice applies
  live and is remembered. **Interface scale** — 75 / 100 / 125 / 150 / 200 % for the whole
  interface (every window, menu, tooltip and dialog; text stays sharp), applied live and
  remembered; timeline, piano roll and audio editor zoom work on top of it. Also
  **AI control (MCP)** on/off, a port field, and "Copy config" (copies a
  ready-made client JSON config to the clipboard).
- **Shortcuts**: the shortcut list.
- Settings, logs and autosaves live in `~/Library/Application Support/Nota/` (macOS) and
  `%APPDATA%\Nota` (Windows).

---

## MCP / AI control

A built-in **MCP server** (over HTTP, loopback `127.0.0.1` only) lets an AI (Claude Desktop
or Claude Code) write music directly in the open Nota project. Enable it in Preferences
(Appearance → "AI control (MCP)"); it is off by default. Every edit goes through undo and
appears in the UI immediately.

Coverage:
- **Transport**: play/stop, tempo, time signature, loop, metronome, loop record mode
  (`set_loop_record_mode`; `get_transport` reports it).
- **Project**: `get_overview` (a full snapshot); the arrangement's sections
  (`list_sections`, `set_sections`); version history — `list_versions` (with what
  each changed), `save_version` with a note, `switch_version`, `annotate_version` (name, note,
  star). Deleting versions stays with the user.
- **Tracks**: add and remove, volume/pan/mute/solo, groups, sends.
- **Instruments**: add by kind, read and write parameters (by index or by stable id), turn
  on or off (`set_device_bypass` with device index -1; `get_overview` shows it); read /
  shape Nota Keys in musical terms (`read_keys`, `set_keys`).
- **Audio effects**: add, remove, reorder, bypass, parameters; load an audio file into a
  device (a Nota Chamber impulse response) and read its resource text (IR names).
- **MIDI clips and notes**: piano roll — add a clip, read/write/append/clear notes, move
  and split; read and write a clip's controller curves (sustain, wheels, any CC).
- **Automation**: create a lane, read and write points.
- **MIDI effects** and engine status.
- **Session**: the clip launcher (grid snapshot with clip and scene properties, create, copy,
  move and clear slots, read and write slot notes, loop length, clip name / colour / launch
  mode / quantize / legato / loop / follow actions, scene name / colour / tempo / signature /
  follow, insert / duplicate / capture / move / delete scenes, stop buttons, launch and
  release a slot, launch and stop a scene, launch quantization, the follow switch, slot and
  scene recording with a fixed length, back to arrangement, slot ↔ arrangement).
- **Racks** (Instrument/Drum): chain snapshot, add and remove a chain and change its
  instrument, parameters, mix (gain/pan/mute/solo), key and velocity zones, trigger note,
  8 macros and their mappings, rack volume and glide; for the Drum Rack, per-pad
  choke/tune/decay and kit swing/humanize.
- **Samples**: create a Sampler or Grain with a file (optionally taking the root from the file
  name), load a sample onto a track or into a rack chain, read sample info, and read / shape a
  Sampler in real units (`read_sampler`, `set_sampler`).
- **Mixer**: sends to returns, the return list, track and master meters, record source.
- **Plugins**: list the AU/VST3 catalogue, look up by id, add as an instrument or insert
  effect, open and close the editor, read and write parameters and state.
- **Presets**: factory (list, apply to a new track or in place) and user (list, apply,
  save).
- **Audio Effect Rack**: snapshot, Parallel/Series/Select mode, dry/wet, volume,
  chain-select, add and remove chains, parameters, 8 macros.
- **Export** the master or stems to WAV (pcm16/pcm24/float32), FLAC (pcm16/pcm24) or
  MP3 (CBR 128–320 kbps) — `format` picks it, `bitrateKbps` tunes MP3, `startBeat` sets
  where the render starts.
- **AI models**: `list_ai_models` (installed or not), `separate_stems` (a clip → a group of
  Drums / Bass / Other / Vocals tracks), `convert_audio_to_midi` (melody or harmony with
  basic-pitch → a new MIDI track). Installing models stays with the user.
- **Nota Mosaic**: read and shape it, edit zones and groups, import SFZ, map a folder of
  samples by name, list and load its factory and pack presets (see Nota Mosaic above).
- **Sample library**: `search_samples` (kind, BPM range, key, words in the path),
  `get_sample_info`, `find_similar_samples`, `get_project_key` / `set_project_key`.
- **MIDI devices and MIDI Learn over MCP**: list connected MIDI inputs, toggle listening,
  survey the controller's CCs and notes, view and edit bindings (range, inversion,
  deletion), and enter learn mode.

---

## Other

- **Undo/redo** for every editing operation.
- **Semantic versioning** with a single source of truth (`VERSION`), a changelog in Keep a
  Changelog format, and `vX.Y.Z` git tags.
- **License**: AGPL-3.0-only.
- **Building**: `scripts/build.sh` (macOS), `scripts/build-win.ps1` (Windows),
  `scripts/build-linux.sh` (Linux); packaging via `bundle-mac.sh`, `package-dmg.sh`,
  `package-win.ps1`, `package-linux.sh`.
- **Release CI**: a GitHub Actions workflow builds and packages every target (x64 + arm64
  across macOS, Windows and Linux) on a `vX.Y.Z` release tag, and drafts the GitHub release
  with the matching `CHANGELOG.md` section as its body.
- **Tests**: an engine smoke test after every build, and a performance gate on engine
  pull requests: instruction counts (fails above 1%) and timing (above 15%) per scenario
  against the base branch, plus an idle-CPU check on all three OSes
  (`scripts/perf-gate.sh`).
- **Third-party notices**: bundled/linked dependencies and their copyrights are recorded in
  [`LICENSES/THIRD-PARTY-NOTICES.md`](LICENSES/THIRD-PARTY-NOTICES.md).
