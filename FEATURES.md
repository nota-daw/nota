<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Nota — full feature list

A consolidated list of what Nota (a cross-platform DAW) can do, as of version
**0.38.0** (plus changes in development on `main`). This document describes what is
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

- **macOS** (≥ 13.0, universal arm64 + x86_64): CoreAudio, CoreMIDI, **AU + VST3**
  hosting. Distributed as a universal `.dmg` / `.app` (ad-hoc signed).
- **Windows** (x64): WASAPI (shared + **exclusive mode** for minimum latency), WinMM
  MIDI, **VST3** hosting. Distributed as an Inno Setup installer
  (`Nota-Setup-<version>-x64.exe`; arm64 is also built).
- **Linux**: PulseAudio/ALSA (via miniaudio), ALSA MIDI (RtMidi), **VST3** hosting.
  Distributed as a portable **AppImage** (`Nota-<version>-<arch>`).

Architecture: engine and DSP in C++20 (CMake + Ninja), UI in .NET 10 / Avalonia 12 (C#),
with a C ABI + P/Invoke boundary between them. Plugin hosting is isolated in a separate
JUCE module; the engine core is JUCE-free.

---

## Project and transport

- **Project**: create / open / save (`.nota`), autosaves, undo/redo for every editing
  operation.
- **Transport**: play / stop / record, playhead position.
  - **Stop returns to the launch point** (a seek sets the start anchor).
  - **Follow** — the arrangement follows the cursor (playhead stays centred).
  - **Launch quantize** (Q): None … 1/16 … 4 Bars — applied when launching slots and
    scenes in Session.
  - **Position**: clicking the readout toggles bars ↔ time (mm:ss.ms).
- **Tempo (BPM)** and **time signature** (edited by dragging; the denominator snaps to a
  power of two), grid and quantization.
- **Loop region** (on/off and range; Cmd/Ctrl+L loops the selection).
- **Metronome** and **count-in** before recording.
- **Transport shortcuts** (Space play/stop, Enter stop, R/M/A, clip copy-paste, typing
  notes, undo/redo) work in detached windows too.

---

Three top-level views, switched from the toolbar (**Arrangement · Session · Modular**);
the mixer opens as a separate floating window (**View → Mixer**, ⌘M).

- **Arrangement View** — a linear timeline, floating on the app ground as its own island:
  place, move, trim and duplicate clips; grid and snap to bars/beats; scroll and zoom;
  recording into the arrangement (audio and MIDI); split at the cursor or across a
  selection (Cmd/Ctrl+E cuts every track in the selection); clip scrubbing on the ruler.
  An **Overview** strip above the ruler shows the whole project in miniature with the
  visible span as a window over it — drag it to scroll, drag its edges (or drag vertically)
  to zoom, double-click to fit the project.
  - **Sections** — a lane between the Overview and the ruler names spans of the song
    (Intro, Verse, Drop). Drag the empty lane to mark one, click it to jump the playhead
    there, double-click to rename, drag its body or edges to move and resize it (always on
    whole bars). Its menu loops the section, selects it as a time range across every track,
    duplicates it or deletes it. Saved with the project; View ▸ Toggle sections hides it.
  - **Group rows are slim** — a group collapses to a titled bar with its disclosure,
    mute/solo and level, and opens to the full control set when selected, so groups read as
    families rather than as more tracks. A track without a colour of its own takes its
    group's hue, shaded per sibling.
  - **Clip names appear where a run begins** — a clip is a colour band over its waveform,
    and the name prints only at the head of each run so a repeated pattern reads as one
    block. View ▸ Clip names cycles every clip / first of a run / none; a clip named by
    hand always shows its name.
- **Session View** — a clip grid (tracks × scenes): launch and stop a clip, launch a whole
  scene, launch quantization, recording into a clip slot (MIDI or audio), loop clips with
  a configurable length, and moving material between Session and Arrangement.
- **Modular View** — a signal-graph editor for the track's chain, on its own island: MIDI FX → instrument →
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
  `Add track ▸` in a track's own menu.
- **Per track**: volume, pan (bipolar bar), mute, solo, arm (ready to record).
  Double-click resets to the default.
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

---

## MIDI and virtual instruments

- **MIDI clip**: notes (pitch, start, length, velocity).
- **Piano Roll**: draw, move, stretch and delete notes; quantization; grid length; a
  velocity editor; transposing the selection.
  - Scroll and zoom are remembered per clip; a new clip centres on its own notes.
  - A MIDI clip trimmed in the arrangement mutes note tails at its boundary.
  - Live edits (moving a note, changing its length) reach the engine immediately, and a
    whole drag is a single undo step.
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
    clip. Output can **replace** what it was given or be **added** on top.
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
- **Input**: MIDI keyboard (live play and recording), and the **computer keyboard** plays
  MIDI (the S–K row, sharps on W E T Y U) — including while a hosted plugin window has
  focus.
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
- **Audio→MIDI** (right-click an audio clip → Convert):
  - **Convert Melody** — monophonic pitch detection (YIN) → a new Nota Synth track.
  - **Convert Harmony** — polyphonic (STFT + spectral peak picking) → chords.
  - **Convert Drums** — hit detection + kick/snare/hat classification → a 3-pad kit and a
    MIDI pattern.
  - **Slice to New MIDI Track** — slices the audio at transients (or into 16 beats) across
    the pads of a new Drum Rack, plus a MIDI clip.
- **MIDI effects** — see [Built-in devices](#built-in-devices).

---

## Audio: recording, clips, warp

- **Recording** from an input (microphone, line, interface) onto an audio track, with
  monitoring (in/auto/off).
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
- **Warp / time-stretch**: **Complex** and **Complex Pro** modes (the latter with formant
  preservation, correct even when the sample rate and the device rate differ), plus
  transient detection. A grid-snap toggle governs trimming a warped clip.
- **Changing the sample rate** does not break third-party plugins or warped clips — the
  caches are rebuilt once when the rate changes.

---

## Automation

- **Automation lanes** on tracks, with curved segments (Alt+drag) and points (double-click
  to add, double-click a point to remove).
- **Draw mode** (the `A` key): drawn automation changes the device parameter in real time,
  and the whole drag is one undo step.
- **The lane follows focus** — touch any knob or fader on any device (built-in, plugin, or
  mixer) and the automation lane switches to that parameter.
- **Already-automated parameters are highlighted** in the target picker (a brass dot, and
  they sort to the top of the list).
- **Selection and paste**: shift-select a range, Cmd/Ctrl+D to duplicate, and paste that
  does not leave redundant points behind.
- **Recording** automation: there are no record modes — lanes always play back, and with
  the transport **Record** button engaged, moving a control writes that parameter's lane.
  A mouse gesture stops writing on release; a hardware control (MIDI knob, fader, gamepad
  stick or trigger) latches and keeps writing until the transport stops.
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
name, subtitle, preset picker, **A/B comparison** (parameter snapshots), stereo meter,
voice count (on synths), move arrows, delete, and the ⠿ drag handle. All support factory
and user presets, automation, persistence and cloning.

### Instruments
- **Nota Synth** — subtractive synthesizer (the basic one): one oscillator
  (Saw/Square/Triangle/Sine) with pulse width, detune, octave and a 1/2/4/7-voice unison
  stack with stereo spread; ADSR; a filter with type Off/LP/HP/BP, resonance and an
  Env → Cutoff amount; voice modes Poly 16 / Mono / Legato with glide, pan and Vel→Vol.
  Osc · Env · Filter tabs with draggable envelope and filter graphs. 25 factory presets.
- **Nota Sampler** — sampler (one-shot and loop), voice modes Poly 16 / Mono / Choke, loop
  crossfade, reverse loop, filter key-tracking, Vel→Vol; Sample · Pitch · Env · Filter
  tabs; an editable waveform.
- **Nota Grain** — granular sampler: a read Position that Scans through the file, Freezes
  or follows the keyboard, grains of a chosen size, density and window shape (Hann /
  Gauss / Tukey / Tri) sprayed around it, coarse/fine pitch, per-grain position, pitch and
  pan variation, stereo spread, **Dry/Wet** against the sample itself, an LP/HP/BP filter
  and an amp envelope. The card draws the sample with the live grain cloud on it (drag to
  move Position). Drop a sample from the browser to replace the built-in pad. 25 factory
  presets.
- **Nota Volt** — subtractive synth, two paths: two oscillators and noise, each routed to
  one of two filters (LP/HP/BP/Notch, 12/24 dB, with Filter 1 able to feed Filter 2) and
  its own amp; amp and filter envelopes, two LFOs (shapes, depth, fade-in, tempo sync), a
  dedicated vibrato that can ride the mod wheel, a 7×6 modulation matrix, 8 macros over 12
  destinations, unison, glide, oscillator start phase, pitch-bend (±2/±5/±12) and
  modulation wheels, VEL→AMP, VEL→FILTER, Mono/Poly. 25 factory presets.
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
  Note, Reset, Humanize and Pan Spread.
- **Nota Bass** — mono-first bass synth: a morphing oscillator (sine → tri → saw → pulse,
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
  banks (A–D), accents, per-step velocity, Swing/Humanize, and a per-voice **Sample** mode
  (load your own one-shot). Six factory kits (808/909/Trap/House/Lo-Fi/Techno).
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

### Audio effects
- **Nota EQ-8** (kind 0) — 8-band parametric.
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
- **Nota Beat Repeat** (9) — beat repeat with Mix/Insert/Gate modes, Chance, Gate, Repeat,
  Latch, INTERVAL/GRID, Pitch/Decay/Volume, FILTER, Mix; a TIMELINE visualiser.
- **Nota Orbit** (10, formerly Auto Pan) — auto-pan and tremolo, L/R phase (0° = tremolo …
  180° = pan), 5 LFO shapes, Shape, Mix.
- **Nota Crush** (12) — bit crusher: Bit Depth, Sample Rate, Drive, Wet, Anti-Alias;
  Digital/Analog/Fold modes; GRIT (Dither/Jitter/Noise); an OUTPUT filter; quantizer and
  aliasing-spectrum visualisers.
- **Nota Ceiling** (14) — look-ahead brickwall limiter: drive, ceiling, release (with
  auto), Clean/Punch/Glue characters, lookahead, stereo link, sidechain; a LIVE strip with
  GR, input/output history, LUFS-S / LUFS-I / true peak.
- **Nota Dynamic EQ-8** (13) — 8-band parametric with dynamics: each band Static/Duck/Lift,
  threshold, range, attack/release, sidechain, Solo band; a dual curve (static plus
  momentary) and a band table with GR.
- **Nota Strata** (15) — multi-layer overdub looper: layers with waveform, level and mute;
  Record/Overdub/Play/Stop, Undo/Clear, Feedback, input gain, speed/reverse, quantize,
  count-in, set-tempo, Export; audio layers persist into the project.
- **Nota Forge** (17) — multi-stage saturator: 3 stages
  (Tube/Diode/Tape/Fuzz/Digital/Fold), Serial/Parallel/Mid-Side/Multiband routings,
  Amount/Tone/Wet, Bias/Width, LFO→Drive and Env→Tone; a transfer curve and a harmonics
  chart; **OVERSAMPLE** (Off/2×/4×/8×).
- **Nota EQ-3** (16) — 3-band performance EQ: Low/Mid/High faders with a 0 dB detent and
  KILL buttons, two crossover frequencies, 24/48 dB/oct slope (Linkwitz-Riley); a response
  curve with a real-time spectrum.
- **Nota Level** (18) — automatic loudness matching (LUFS): LUFS measurement (BS.1770),
  AUTO/MATCH, true-peak safe, sidechain (match to reference); loudness history and
  IN/OUT/TP/correlation meters.
- **Nota Shutter** (19) — noise gate and ducker: Threshold/Return (hysteresis),
  Attack/Hold/Release with a Linear / Log / Snap shape, Floor (range), Lookahead, Flip
  (ducker), Retrigger (trigger mode), a 12 dB/oct detector band-pass (switchable) with Listen,
  Peak hold, internal or external key; Signal / Envelope / Sidechain graphs (drag the
  threshold, the envelope nodes and the key filter), a STATE column, a state box, meters
  with peak GR and openings per bar; 30 factory presets; MCP `read_shutter`.
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
- **Nota Chorus** — modulation (chorus) effect.
- **Forge**, **Valve** and **Vintage** each carry an **OVERSAMPLE** selector
  (Off/2×/4×/8×) to suppress aliasing under heavy drive.

### MIDI effects
- **Nota Arp** — arpeggiator: step sequencer with Velocity/Length/Chance/Ratchet/Transpose
  lanes, Order, Oct 1–4, Dir (↑↓↕?), Hold/Retrig, Free/Sync, Gate/Swing.
- **Nota Scale** — snap to a scale: Root, Major/Minor/Dorian/Phryg/Penta/Custom, Fold
  (Nearest/Down/Up), NOTE MAP, Range, Follow Key, Learn/Clear.
- **Nota Length** (formerly Note Length) — note lengths: Sync/ms/Gate %, Vel→Len, Key→Len,
  Random, Legato, clip length limit; a GATE visualiser.
- **Nota Velocity** — velocity transformation: Curve/Compand/Fixed, Drive, Random, Out
  Range, Random Dir, and a Last 12 histogram.
- **Nota Random** — randomization: Chance, Gauss/Even/Walk, Lock seed / Re-roll,
  Note/Velocity/Timing/Skip/Octave amounts, Distribution, Rate, Stay in scale.
- **Nota Chord** — chord generator: Maj7/Min7/Sus4/5th/Custom, 6 voices with offset and
  velocity, Strum, Keep root, Fold in scale, Spread; a preview keyboard.

### Racks
- **Nota Instrument Rack** — 8 named macros (mapped with Linear/Exp/Log/S curves), chains
  with **key and velocity zones**, gain/meter/M·S, horizontal device cards (GUI / Params),
  a zone map, rack output (Volume/Glide), Fold/Save.
- **Nota Drum Rack** — 4×4 pads (banks C1–C4, up to 64 pads), Pads, Mixer and Chain views,
  per-pad Volume/Pan/Tune/Decay, named pads (rename, re-choke or remove from a pad's
  right-click menu), choke groups (monophonic cut), Swing/Humanize, hot-swap, a pad
  oscillogram with a playhead, and each pad's own effect chain. A **kit picker** in the
  card header loads any factory kit in place and names the kit a rack holds. Its clips can
  be stepped in the detail panel's **Pattern** tab (see below).
- **25 factory kits** under Nota Drum Rack in the browser and in the card's kit picker:
  *Volta* (warm analog boom), *Kompakt* (punchy analog house), *Micron* (small vintage
  rhythm box), *Linnwood* (80s PCM machine), *Atelier* (acoustic studio kit), *Cellar*
  (dusty vinyl break), *Neon* (modern sub-forward, tuned 808 bass), *Foundry* (industrial
  metal), *Terra* (hand percussion), *Aether* (ambient), *Brass Room* (live rock kit in a
  big room), *Breakline* (jungle break), *Bunker* (warehouse techno), *Pixel* (minimal),
  *Velvet* (jazz brushes), *Circuit* (electro box), *Yard* (dub and reggae), *Byte* (8-bit
  chip), *Lagoon* (amapiano log drums), *Titan* (trailer percussion), *Shuffle* (UK
  garage), *Mirrorball* (70s disco), *Pit* (orchestral percussion), *Hyper* (hyperpop) and
  *Crate* (90s boom bap). 16 pads each on the General MIDI map (36–51), hats choked together,
  with the kit's own swing and humanize. Load onto a new track or drop onto an existing
  Drum Rack to replace its pads. The one-shots are synthesized on first launch rather than
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
  paths in Preferences, and Rescan.
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
  category → device → preset), **Projects**, and **Map** (MIDI Learn mappings). A hairline
  separates the device tabs from the library tabs, and the active one carries a brass edge.
- **A compact index**: single-line rows, sectioned into **BUILT-IN** and **PLUG-INS** with
  counts, so it is clear where Nota's own devices end and the scanned plug-ins begin. The
  "Nota" prefix drops to tertiary ink — names scan on their distinctive word — and the
  device type sits at the right edge as a quiet tag (a plug-in's tag names its format and
  vendor, which tells the AU and VST3 builds of one plug-in apart). The name always wins
  the room: the tag gives way rather than truncating it.
- **View options** (the ⋮ button beside the search box): show type tags, group by source,
  favourites first — each remembered across sessions — plus the tag editor.
- A **status line** counts what the tab is showing (`13 built-in · 4 plug-ins`), and the
  search box counts the matches.
- **Preview** a sample from the browser, and **drag and drop** onto a track, into the grid,
  or into a rack chain.
  - Dropping an instrument onto an existing track **replaces the instrument** in place
    (clips, devices and volume are kept); dropping onto empty space creates a new track.
    Racks are not replaced in place.
- **Favourites** (★) and **tags** (assign and clear, an editor with a title and colour,
  filter chips in the header); favourited devices sort to the top of their section. The
  chips keep to one line — whatever does not fit collapses into a **+N** that opens the rest.
- **Context menus**: Projects — Open / Reveal in Finder / Delete (to the Trash, with
  confirmation); Files — Reveal in Finder; Presets — Reveal in Finder.
- **Hints for empty tabs** — explaining what the tab is and how to add content to it.

---

## Export

- **Export the master** to WAV: range, sample rate, bit depth
  (**pcm16 / pcm24 / float32**).
- **Export stems** (individual tracks).
- **Normalize −1 dBTP** — against the true (inter-sample) peak, estimated with 4×
  oversampling: one render to a temporary file, peak measurement, then a rewrite with the
  exact make-up gain, so there is no risk of overshooting the ceiling because a second
  render differed. Stems share a single gain (taken from the full mix peak), so they still
  sum to −1 dBTP and keep their balance.
- **Dither** — TPDF dithering before quantizing to 16 bit (offered only for 16 bit; 24 bit
  and float do not need it).
- **Add 1-bar release tail** — a decay tail after the end of the project.
- **Bit-identical** chunked rendering (caches rebuilt once when the rate changes) — no
  clicks at block boundaries, and warped clips keep their length at any export rate.
- **Progress** for long operations: export and conversion show a modal progress dialog;
  audio import shows a thin bar in the status bar.

---

## Windows and interface

- **Two palette variants** — **Ember Graphite** (warm graphite neutrals with a brass
  accent; the default, and what a DAW wants for long sessions in a dark room) and
  **Ember Paper** (the same hues and roles on a warm light ground, with brass darkened to
  bronze so marks keep their weight). Switching applies immediately, across every open
  window — including the custom-drawn arrangement, piano roll, mixer and device cards.
- **A single frameless window style**: centred title, drag by the header, the macOS
  traffic lights in a left inset (Windows gets its own title bar; Linux uses the system
  frame).
- **What's New** — a window showing changelog entries newer than `LastSeenVersion`, once
  after the first launch on a new version.
- **About** — the app and engine versions, the copyright notice, and a pointer to the
  third-party attribution notices (`LICENSES/THIRD-PARTY-NOTICES.md`).
- **Preferences** — a consistent design (the house checkboxes, sunken fields).
- **Edit Tags** — the browser's tag editor.
- **Devices/Clip panel in its own window** — the ⧉ button in the bottom panel's header
  detaches it (the selected clip's piano roll on top, the device chain below, both at
  once). The content moves across as-is: edits, meters and graphs all keep working live.
- **Popup rack editors** (full UI / Params).
- **A shortcut list** in Preferences → Shortcuts, grouped by section (Transport,
  Arrangement & Editing, Piano roll, Play notes, Mouse).

---

## Preferences

- **Audio**: device, sample rate, buffer size (latency); **WASAPI exclusive mode**
  (Windows).
- **MIDI**: which MIDI inputs are enabled (the house checkboxes).
- **Gamepads** (macOS): enable gamepad input (notes and mapped controls); the controller
  list updates on hot-plug, with a live activity indicator beside each pad that names the
  note played or the control driven.
- **Plugins**: scan paths, Rescan.
- **Library**: library folders.
- **Appearance**: **Theme** — Ember Graphite (dark), Ember Paper (light) or System, which
  follows the OS appearance and switches with it; the choice applies live and is
  remembered. Also **AI control (MCP)** on/off, a port field, and "Copy config" (copies a
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
- **Transport**: play/stop, tempo, time signature, loop, metronome.
- **Project**: `get_overview` (a full snapshot).
- **Tracks**: add and remove, volume/pan/mute/solo, groups, sends.
- **Instruments**: add by kind, read and write parameters (by index or by stable id).
- **Audio effects**: add, remove, reorder, bypass, parameters; load an audio file into a
  device (a Nota Chamber impulse response) and read its resource text (IR names).
- **MIDI clips and notes**: piano roll — add a clip, read/write/append/clear notes, move
  and split.
- **Automation**: create a lane, read and write points.
- **MIDI effects** and engine status.
- **Session**: the clip launcher (grid snapshot, create a MIDI or audio slot, read and
  write slot notes, loop length, launch and stop a slot or scene, launch quantization,
  slot recording, slot ↔ arrangement).
- **Racks** (Instrument/Drum): chain snapshot, add and remove a chain and change its
  instrument, parameters, mix (gain/pan/mute/solo), key and velocity zones, trigger note,
  8 macros and their mappings, rack volume and glide; for the Drum Rack, per-pad
  choke/tune/decay and kit swing/humanize.
- **Samples**: create a Sampler or Grain with a file, load a sample onto a track or into a
  rack chain, and read sample info.
- **Mixer**: sends to returns, the return list, track and master meters, record source.
- **Plugins**: list the AU/VST3 catalogue, look up by id, add as an instrument or insert
  effect, open and close the editor, read and write parameters and state.
- **Presets**: factory (list, apply to a new track or in place) and user (list, apply,
  save).
- **Audio Effect Rack**: snapshot, Parallel/Series/Select mode, dry/wet, volume,
  chain-select, add and remove chains, parameters, 8 macros.
- **Export** the master or stems to WAV (pcm16/pcm24/float32).
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
- **Tests**: an engine smoke test after every build.
- **Third-party notices**: bundled/linked dependencies and their copyrights are recorded in
  [`LICENSES/THIRD-PARTY-NOTICES.md`](LICENSES/THIRD-PARTY-NOTICES.md).
