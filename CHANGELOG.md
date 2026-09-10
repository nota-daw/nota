<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Changelog

Every notable change to Nota is recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
project follows [semantic versioning](https://semver.org/): `MAJOR.MINOR.PATCH`. The
version in `VERSION` is the single source of truth. Entries run newest first. The app's
"What's New" window shows the user the new entries once, after the first launch on a new
version.

Changes accumulate under `## [Unreleased]` as they merge into `main`, **without** bumping
`VERSION` — that is not a release yet. A release is a separate, deliberate act:
`[Unreleased]` is promoted to a dated version, `VERSION` is bumped, a `vX.Y.Z` git tag is
created, and the build-and-publish run starts.

Entry categories: `Added`, `Changed`, `Fixed`, `Removed`.

## [Unreleased]

### Added
- **Nota Pentad — a 5-voice polyphonic synth (Prophet-5).** A new built-in instrument
  (kind 14) modelled on the Prophet-5 Rev 3: Osc A (saw and pulse together, hard sync to
  B) and Osc B (saw, triangle, pulse; Lo-Freq mode; keyboard tracking off), white/pink
  noise, a mixer that overdrives the filter, a 24 dB/oct resonant low-pass that
  self-oscillates as a clean sine and follows the keyboard (off / ½ / full or any amount),
  and filter and amplifier envelopes with analog curves that retrigger from their current
  level. **Poly-Mod** routes the filter envelope and Osc B — at audio rate, for FM, PWM and
  sync sweeps — to Osc A frequency, Osc A pulse width and the cutoff; **Wheel-Mod** mixes
  the LFO (saw/triangle/square, free or tempo-synced) with noise. 5, 10 or 16 voices with
  round-robin or oldest-note stealing (a 4 ms fade, no clicks), Poly / Unison / Mono,
  unison stacks with detune, glide (off / on / legato), the Release switch, velocity and
  aftertouch amounts, pitch-bend range up to ±12. **Vintage Drift** gives every voice its
  own pitch, cutoff and envelope spread plus slow drift, from a seed saved with the
  project, so the same patch always renders the same way and a freeze matches playback
  exactly. Band-limited oscillators and ×2/×4 oversampling keep aliasing below -80 dB.
  A card editor (Wheels column, Oscillators / Filter · Amp / Poly Mod and Mixer / Output
  tabs, a voice-activity strip, meters, a Voice setup flyout), 40 factory presets, and full
  automation, MIDI learn, persistence and MCP support. It also builds as a standalone VST3
  (`-DNOTA_BUILD_PENTAD_VST3=ON`) for testing in other hosts.
- **Fine knob adjustment.** Hold ⌘ or ⇧ (Ctrl on Windows and Linux) while dragging a device
  knob to change its value in steps ten times finer.
- **MCP: `set_instrument_param_by_id`** sets an instrument parameter by its stable id
  rather than by index.
- **Freeze and Live Freeze from the track's context menu.** Right-clicking a track header in
  the arrangement now offers the whole freeze workflow, not just the Devices panel buttons:
  **Freeze track** and **Live Freeze** on a live track; **Unfreeze track** and **Flatten to
  audio track** on a frozen one; and on either half of a live-freeze pair — the sleeping
  source or its frozen audio track — **Edit source**, or **Done — re-freeze** / **Discard
  edits** while you're editing, plus **Unfreeze (wake source)** and **Flatten (remove
  source)**. Each command selects its track, so the Devices panel follows along; **Edit**
  always jumps to the source, where the notes and devices live.

### Fixed
- **Preferences: the end of long sections can be scrolled into view.** The last rows of
  the Shortcuts list sat below the window's edge.
- **Deleting one half of a live-freeze pair no longer leaves the other half stuck.** If the
  frozen track is deleted (or undone away), its source wakes up and plays again. If the
  source is deleted, the frozen audio becomes an ordinary track, and if you were in the middle
  of an edit it is no longer left muted.
- **Flatten during a live-freeze edit no longer leaves a silent track.** The kept audio track
  is unmuted, and its "(frozen)" suffix is dropped, since it's now an ordinary track.
- **Unfreezing a live freeze while its frozen track is selected** now selects the source
  track, instead of leaving the selection on a track that no longer exists.

## [0.37.1] — 2026-08-25

### Highlights
- **Nota is now open source** — released under the GNU AGPLv3 and developed in the open.
- **Play notes from a game controller on macOS** — Xbox, DualShock/DualSense, Switch Pro
  and 8BitDo pads act as a small keyboard.
- **Full third-party credits** — the About window and a new notices file attribute every
  bundled library and its copyright.
- **Removed the in-app "Send Feedback"** — bug reports and requests now live in the public
  issue tracker.

### Added
- **Gamepad as a source of live notes (macOS).** A connected controller (Xbox / DualShock /
  DualSense / Switch Pro / 8BitDo) acts as a small keyboard: the face buttons A/B/X/Y play
  C4 D4 E4 F4, the bumpers and triggers L1/R1/L2/R2 play G4 A4 B4 C5, the D-pad up/down
  shifts the octave and left/right changes velocity. The notes travel the same path as
  typed ones (live play on an armed or auditioned track, recording while Record is on,
  highlighting in the piano roll). Enable it in Preferences → Gamepads, where the
  controller list updates on hot-plug and a live indicator beside each pad shows the last
  button pressed — so you can see at a glance that the controller is connected and
  responding. Unplugging a pad releases any held note. Built on Apple's GameController
  framework, which is why Switch Pro and 8BitDo decode correctly (raw HID reported their
  packet counter as phantom presses and spammed notes).
- **Third-party attribution notices.** The About window now shows the copyright notice and
  points to a new [`LICENSES/THIRD-PARTY-NOTICES.md`](LICENSES/THIRD-PARTY-NOTICES.md) that
  credits every bundled or linked dependency (JUCE, Avalonia, miniaudio, RtMidi, dr_libs,
  Signalsmith, HIIR, …) with its copyright and license.

### Changed
- **Open-sourced under the GNU AGPLv3.** Nota is now a single-license open-source project.
  The separate Pro / commercial edition — and the `NOTA_EDITION` / `NotaEdition` build
  flags, the runtime edition string and the FREE/PRO badge — were removed; every source
  file now carries the `AGPL-3.0-only` SPDX header.

### Removed
- **Send Feedback.** The in-app feedback window, the "Share feedback" title-bar button
  and the Help → Send Feedback… item are gone, along with the HTTP client that submitted
  reports. Nota is developed in the open now, so bugs and requests belong in the public
  issue tracker, where they are visible and can be discussed. This also removes the
  baked-in submit credentials the feature needed.

## [0.37.0] — 2026-08-21
### Added
- **Nota Monolith — a monophonic synth (Minimoog Model D).** A new built-in instrument
  (kind 13): three oscillators (6 waveforms, LO/32'–2' foot ranges, ±7 semitone detune,
  osc 3 with switchable keyboard tracking), a mixer (white/pink noise, external input,
  feedback overdrive), a 24 dB/oct Moog ladder filter with self-oscillation and 1/3·2/3
  tracking, two contours (filter and amplitude, sharing a Decay-release switch), glide,
  Low/High/Last note priority, single and multiple trigger (Legato), unison with detune,
  osc and filter modulation from the wheel, and tuning drift. A card editor (a Wheels
  column plus Osc/Modifiers and Mixer/Output tabs), 20 factory presets, and full
  automation, MIDI learn, persistence and MCP support.
- **Session: audio-slot editor (double-click).** Double-clicking a MIDI slot opened the
  piano roll; an audio slot did nothing. Double-clicking an audio slot now opens a compact
  editor: the take's waveform, **gain** (a fader plus dB, affecting playback) and **loop
  length** (presets 1/2/4/8/16). The engine does not render trim, warp or pitch for
  session audio yet, so those are absent from the editor.
- **Session: empty audio slots are no longer dead.** An empty slot on an audio track shows
  a dim ring ○ (a hint that you can record or drop into it), and on an armed track a red ●
  (click to record). Clicking an empty audio slot now records the input into it in one
  move, arming the track if needed; dropping a file still fills it as before.
- **Session: scene management and deleting slot clips.** The **+ Scene** button now
  actually adds a scene (it used to be an N/A placeholder), and right-clicking a scene row
  offers "Delete scene" (at least one always remains). A slot has a right-click →
  **Delete clip** for any track type; MIDI slots also offer loop length and "Copy to
  arrangement" there. The scene count is saved and restored with the project.
- **Session: Session and Arrangement are separated, plus "Back to Arrangement".**
  Launching a single session clip used to start the global transport and roll the whole
  arrangement under it. A clip launch is now **session-only**: only launched slots play,
  and tracks without an active slot stay silent (live keyboard input still works). The
  main **Play** — or recording into the arrangement — still runs the arrangement, with
  session clips layered on top. The Session panel gained a **Back to Arrangement** button
  that lights up while the session has taken tracks over, and on click stops every session
  clip and returns all tracks to the timeline.
- **Live Freeze (linked freeze) — a live mirror of a track in audio.** A **🔗 Live
  Freeze** button beside the regular Freeze bounces the source into a **separate linked
  audio track** directly beneath it and puts the source to sleep (an implicit mute — the
  engine stops computing its instrument and effects, freeing CPU). Unlike a classic
  freeze, the link stays live: the source grows an **✎ Edit** button — press it and the
  source wakes up and sounds live, you edit notes and devices, then **✓ Done** redraws the
  audio and puts the source back to sleep; **✗** exits without rewriting. Linked tracks are
  marked in the arrangement (🔗 on the sleeping source, ❄ on the frozen one) with icy
  header tints, and the sleeping source's chain is dimmed until you enter Edit. The
  button's context menu offers **Unfreeze** (remove the freeze and wake the source) and
  **Flatten** (turn the frozen track into an ordinary one and delete the source). The link
  **is saved with the project** (as a sidecar), and the source opens already asleep.
  *v1 defers: unloading plugins from RAM, automatic Live/Manual modes, a revision cache,
  cascading through dependent tracks, partial freezing by clip or range, the whole session
  as one undo step, and realtime rendering of an external input.*
- **Track Freeze — bounce to audio to free up CPU.** A **Freeze** button in the Devices
  panel header bounces the selected track's chain (instrument + MIDI FX + effects) into an
  audio buffer that plays instead of the live chain — heavy synths and plugins stop being
  computed, while **the mixer stays live** (volume, pan, sends, mute/solo and the meter all
  keep working). Freezing runs offline with a progress bar, like an export. Clicking again
  **unfreezes** and the live chain returns untouched. A frozen track is marked clearly: a
  **snowflake ❄ and an icy tint** in the arrangement track header, a "Frozen" state on the
  button, and a dimmed device chain (editing is blocked until you unfreeze). A freeze
  **survives saving and reloading the project** — the audio is stored in the project file
  and the track opens already frozen. Right-clicking the button offers **Flatten**, which
  turns the frozen track into a plain audio track holding a single clip (the instrument,
  MIDI and effects are removed; irreversible, with a confirmation).
- **Modular Editor — a graph view of a track's chain with CV modulation.** A new editor
  mode showing a track's device chain as a signal graph: MIDI FX → instrument → effects as
  nodes that can be expanded, bypassed, duplicated, deleted and reordered right on the
  canvas (node positions are saved with the project). There is a **Global view** showing
  tracks as islands, and cross-track connections. The centrepiece is **CV modulation**:
  drag a modulator's CV output onto any device, instrument or MIDI FX parameter to link
  them with a patch cable; clicking an edge edits depth and mode, and edges can be selected
  and deleted. The modulators are **LFO** (with phase), an **envelope follower**,
  **MIDI→CV** (velocity/gate/note), **ADSR** (gated by notes), **Macro** (a manual control
  → CV) and **Math** (two CV inputs → one output). Any parameter can also be a CV source
  (param → param). A **CV Scope** oscilloscope reads any modulator signal. CV ports are
  drawn only where they actually work.
- **Deactivate a clip with the 0 key.** Select one or more clips in the arrangement (audio
  or MIDI) and press **0** — the clip switches off: it stays where it is but plays nothing
  and is drawn dimmed grey. Press 0 again to switch it back on. There is also a
  "Deactivate clip / Activate clip" item in the clip context menu. The state is saved with
  the project. Deactivating while a MIDI note is sounding releases it properly, so no note
  hangs.

### Changed
- **The mixer moved into its own window.** The **Mixer** tab was removed from the view
  switcher (Arrangement · Session · Modular). The mixer now opens as a separate window
  from **View → Mixer** (or **⌘M**), floating above the main window; invoking it again
  closes it. Meters and faders keep updating while the window is open, and stay
  MIDI-mappable.
- **Computer-keyboard layout for typing notes.** Notes start from **A** again (the white
  keys A S D F G H J K = C4…C5, the black keys W E T Y U). **Z / X** shift the octave and
  **C / V** change velocity, with a hint in the status bar. Toggling automation mode moved
  off bare **A** onto **⌘A** in the arrangement; in the piano roll ⌘A still selects all
  notes.
- **Projects with warped clips open instantly — warp caches build in the background with a
  progress bar.** After the move to an offline cache, every clip was stretched immediately
  on load, which stalled opening for a long time (close to a minute on a large project at
  96 kHz). Loading now defers cache building: the arrangement appears at once and the
  caches are computed incrementally in the background (a "Preparing warped clips…" bar in
  the status bar), with the app responsive throughout. Warped clips stay silent until
  their cache is ready, a few seconds; everything else plays immediately. This also
  removed redundant rebuilds: on load each clip used to be stretched several times, once
  per marker, window or pitch edit — now exactly once, cutting the total work several-fold.

### Fixed
- **Device cards: UI fixes.** • **Reverb** — the MOD section no longer overflowed the card:
  MOD became a slim island (Rate/Depth stacked vertically) to the right of the tail graph,
  with SPACE and TONE in their own column. • **Dynamic EQ-8** — the graph and band table
  were no longer clipped at the bottom (the card was squeezed under 260px), and the
  Threshold/Range/Attack/Release knobs became legible when a band is not in dynamic mode
  (dimming 0.35 → 0.5). • **Utility** — the goniometer (phase graph) badly under-used its
  area: instantaneous samples are far quieter than peaks and collapsed to a dot at the
  centre. A display gain (~2.4×) was added, so the signal now fills the scope and peaks
  reach the edges, as a goniometer should.
- **Dynamic EQ-8: the sidechain source was not always saved.** Choosing a key in the combo
  box did not mark the project dirty (`NotifyChanged` was never called), so it could be
  lost. It now marks it, as Ceiling and Compressor already did.
- **Dynamic EQ-8: GR history is no longer permanently empty.** A band defaulted to
  Range = 0, so switching to DUCK/LIFT moved nothing (zero gain → no GR). Enabling
  dynamics on a band with zero Range now fills in a musical default (DUCK −6 dB, LIFT
  +6 dB), both from the chips and from a right-click on a graph point.
- **Session: a batch of interaction fixes.** • Adding an audio or return track now refreshes
  the Session grid (previously only instruments did). • The per-column mini-mixer shows send
  faders **for the number of real return channels** (it used to always show "A", even with
  no returns) and updates when a return is added. • An arrangement clip (MIDI **and audio**)
  can be copied into a slot via right-click → "Copy to session → Scene N". • Recording audio
  into a slot became clearer: **clicking a recording slot stops the recording** (and the take
  loops), and **the global Stop no longer loses the take** — it materialises into the slot.
  • The launch/stop/scene buttons in Session gained hover and press states (they used to look
  dead). • A slot now accepts **audio dragged from the browser and from external
  applications** (Finder and the like), highlighting on hover; previously only internal drags
  from the browser were accepted.
- **Session: audio clips in slots now play.** Launching a slot only worked on instrument
  tracks — audio could be recorded or dropped into a slot (which then showed as filled), but
  clicking it played nothing. `Launch` and `Launch scene` now start audio slots too (the
  engine's render loop was already in place), and an audio take recorded into a slot loops
  back on its own.
- **Session: a slot is no longer lost when saving during playback.** The project saved only
  slots in the "filled" state, skipping playing, queued and recording ones — saving in the
  middle of a jam threw those clips away. Any non-empty slot is now saved.
- **Modular: card and toolbar polish.** Several fixes to the signal-graph editor:
  • a new track now appears in modular immediately, both in the track list and as an island —
  previously the view did not refresh until you switched away and back;
  • a device or instrument card shows **only the knobs that have a CV patch attached**
  (patched ones first), and the ↕ button expands the full list; instruments had no ↕ button
  at all, so one was added (along with double-click to expand), and on effects, VST and AU it
  no longer sits there empty when there is nothing to expand;
  • the modulator buttons (+ LFO / + Env / …) collapsed into a single **+ Add** dropdown;
  • the LFO layout was fixed — the Sync/Free button no longer overflows the edge and moved
  next to the Rate knob;
  • the **+ / −** zoom buttons are visible again (the base button style gave them padding
  that ate the glyph on a narrow button);
  • **each incoming CV connection is drawn as its own input** — with several modulations on
  one card you now see N inputs and N cables, where previously they all converged on a single
  port;
  • **a CV cable is hit-tested along its whole line**, not just on the dashes of the animated
  stroke (the edge layer became hit-testable as a whole);
  • **cross-track modulation can be patched with a cable**: drag a modulator's CV output onto
  the target track in the left-hand list and pick a parameter (the right-click command on a
  knob remains).
- **Audio dropouts ("static freezes") at warped-clip boundaries are gone.** Warping
  (time-stretching) was computed in real time on the audio thread, and at the start of each
  warped clip the stretcher primed itself once inside a single render block. At 96 kHz in
  ComplexPro mode this produced a ~1.6–3.9 ms spike in one block — over the buffer budget,
  so every warped-clip boundary dropped out. A clip is now pre-stretched once into a
  device-rate buffer (the offline stretch model) and the audio thread simply copies from it;
  the peak on the same project fell from 121% of budget (an overrun) to ~13–76%. Changing
  tempo rebuilds the warp caches (so dragging the BPM got heavier with many warped clips),
  and each clip keeps its played window in memory at the device rate.
- **The arrangement follows the playhead again when Follow is on.** While scrolling behind
  the playhead only the timeline moved; the tracks themselves stayed put.

## [0.36.0] — 2026-08-18
### Changed
- **The shortcut list in Preferences is up to date.** The "Shortcuts" section was rewritten
  against the real bindings and grouped (Transport / Arrangement & Editing / Piano roll /
  Play notes / Mouse). It reflects the layout changes: **A** now toggles automation mode,
  computer-keyboard notes play on the **S–K** row (sharps on W E T Y U), and the missing
  commands were added (recording, metronome, grouping, copy/paste, note editing in the
  piano roll).
- **Preferences now follows one design.** The checkboxes (MIDI inputs, WASAPI exclusive,
  enabling MCP) no longer look like system controls — they are the house's compact
  checkboxes with a dark tick on a brass fill, as in the export dialog. The MCP port field
  is styled as a sunken field matching the dropdowns. The checkbox and field styles moved
  into the theme (`NotaTheme.axaml`), so they look right in every window, not just
  Preferences.
### Added
- **MIDI routing between tracks.** An instrument track gained a **"MIDI In"** selector —
  take MIDI from another instrument track and play the same material through your own
  device (layering), mirroring the input selector on audio tracks. The source's MIDI output
  is passed on **after its MIDI effects**, so an arpeggiator (or any other MIDI FX) on the
  source drives the receiver too. The selector appears in three places: the arrangement
  track header, the mixer channel's I/O section, and the context menu. The source keeps
  sounding through its own device; mute it and it goes silent, but the MIDI still flows.
  **Arm the receiving track** and the incoming MIDI is printed into its clip. The setting
  is saved with the project.
- **Progress for long operations.** Long tasks no longer look like a hang: operations that
  block the interface (WAV export, converting a clip to MIDI) show a modal progress dialog
  with a caption, while background ones (audio import) show a thin labelled bar in the
  status bar. Export and conversion report real progress, and the heavy analysis pass in
  conversion moved to a background thread so the window does not freeze.
- **Export: Normalize and Dither actually work.** Two toggles in the export dialog stopped
  being placeholders. **Normalize −1 dBTP** brings the bounce to the target ceiling by true
  (inter-sample) peak, estimated with 4× oversampling: the audio is rendered once to a
  temporary file, the peak is measured from it, then the same samples are rewritten with
  the exact make-up gain — with no risk of overshooting because a second render differed.
  Stems share a single gain (from the full mix peak), so they still sum to −1 dBTP and keep
  their balance. **Dither** adds TPDF dithering before quantizing to 16 bit (offered only
  for 16 bit; 24 bit and float do not need it). The "Add 1-bar release tail" checkbox was
  redrawn to match (a compact checkbox with a dark tick on a brass fill).
### Fixed
- **Warp Complex Pro: formant preservation broke when the sample and device rates
  differed.** In Complex Pro mode formant preservation drifted by the sample-rate ratio
  (`srSR/devSR`) — the tone sounded odd, and differently at different device rates, while
  Complex was fine. The formant multiplier now accounts for that correction: at pitch = 0
  Complex Pro matches Complex at any rate, and with a pitch shift only the musical shift
  remains.
- **Export: clicks at block boundaries are gone, as is speed-up at higher rates.** An
  offline bounce runs in chunks, and the engine re-prepared devices and effects **for every
  chunk** — which reset DSP state (delay buffers, reverbs, plugins) and produced a click at
  each block boundary; on top of that, when the export rate was higher than the current one
  the warp streams stayed at the old rate and played the track faster. Preparation
  (including the master, MIDI effects, racks and warp reconfiguration) now happens once when
  the rate changes: chunked rendering became bit-identical to a single-pass render, and
  warped clips keep their length at any export rate.
- **Automation: live response in draw mode.** (1) Drawn automation changes the device
  parameter in real time while you drag a point or curve, instead of only on release. The
  whole drag remains a single undo step. (2) The automation lane now shows the current
  value: turning a device knob on a non-automated parameter makes the dashed baseline follow
  it live.
- **Changing the sample rate no longer breaks third-party plugins.** When switching SR (to
  96 kHz, say) plugins on the master bus and MIDI effects were not re-prepared for the new
  rate and kept running at the old one — the sound became faster and inharmonic. On an audio
  restart the new rate is now propagated to every node, including the master track and rack
  chains.
- **Recording from an internal bus: growing drift eliminated.** Recording a group, return or
  master output into a new audio track could let the captured audio gradually drift away
  from the source on long takes, and the heavier the project the worse it got. The capture
  ran through a ring buffer drained by the UI thread, and when that stalled, frames were
  lost without a trace, shifting everything recorded after them. Lost frames are now counted
  and padded with silence, so the take stays anchored to the timeline (at worst a short
  local gap instead of a cumulative shift), and the capture buffer was enlarged.
- **Racks: every built-in effect can go into a chain.** Dynamic EQ-8, Ceiling and Strata, as
  well as EQ-3, Forge, Level, Shutter and Crush, could not be created inside Instrument or
  Audio Effect Rack chains — the rack's device factory had fallen behind the main list. They
  can now be added both from the "+ Device" menu and by dragging from the browser.
- **Racks: a device in a chain gained a delete button.** The device card (both collapsed and
  in Series view) got a "✕" that removes the device from the chain.
- **Nota Bass: mono mode no longer swallows the next note.** If a new note was taken while
  the previous one was still releasing, the envelope stayed in its decay phase and the note
  barely sounded. Legato (no retrigger, with a smooth pitch glide) is now applied only when
  held notes genuinely overlap, and a note after a release retriggers properly — it sounds
  immediately, with a smooth pitch transition.
- **Nota Bass: poly mode no longer runs away into resonance.** Every new voice started at
  the previous note's frequency and glided to its own, so in a chord the voices swept through
  each other and beat. Glide is now removed in poly: a voice takes its pitch immediately.
### Changed
- **Racks: macros and controls take part in MIDI Learn.** In MIDI Learn mode the Instrument
  and Audio Effect Rack macro sliders, the rack volume, chain mute/solo and Drum Rack pad
  volume/mute/solo now highlight and accept an assignment from the controller, just like
  ordinary knobs.

## [0.35.0] — 2026-08-14
### Added
- **Linux support.** Nota now builds and runs on Linux: the native engine (audio through
  PulseAudio/ALSA, MIDI through ALSA, VST3 hosting) and a portable AppImage that runs on any
  distribution without installation.
### Changed
- **Linux: native window frames.** On Linux every window — the main one and the dialogs
  (Preferences, Export, feedback, About and so on) — uses the system title bar instead of
  the custom dark strip used on macOS and Windows; the "Send Feedback" button moved into the
  Help menu.
### Fixed
- **Arrangement: stalls on large projects are gone.** Clicking clips and working with the
  loop enabled no longer stutters on long arrangements — the rendering was reworked (the
  playhead and loop moved to their own layer, the selection updates surgically, and
  off-screen tracks are not redrawn).

## [0.34.0] — 2026-08-13
### Fixed
- **Reverb: a random metallic burst is gone.** The occasional loud "ring" that broke through
  now and then (on a Size or algorithm change, or late in a long tail) was eliminated: the
  delay-line length is now smoothed, and the output uses a soft saturator instead of a hard
  clip.
- **The keyboard in the Devices/Clip window.** The detached window now handles transport
  shortcuts (Space play/stop, Enter stop) and the rest (R/M/A, clip copy-paste, typed notes,
  undo/redo) — previously the events bypassed the main window and nothing fired while focus
  was in the popup.
- **Scroll and zoom on a trackpad.** In the arrangement, piano roll and audio-clip editor,
  horizontal wheel scroll and zoom no longer fly to their extremes on a trackpad: gestures
  are normalised per device (mouse vs trackpad) and step-limited, so scrolling became smooth
  and predictable — and independent of the zoom level. The piano roll also understands a
  two-finger horizontal swipe now.
- **Piano roll: notes no longer overlap the keys.** Scrolling right no longer lets notes at
  the left edge climb onto the keyboard — the grid, ruler and velocity lane are clipped to
  their own column.
- **Arrangement: the wheel in the empty area below the tracks.** With only one or two tracks
  added, wheel and trackpad zoom and horizontal scroll now work over the empty space beneath
  them as well (previously nothing happened there). Scroll speed was nudged up at the same
  time.

### Changed
- **One modern window style.** Every secondary window and dialog (Preferences, Export, Send
  Feedback, About, What's New, Edit Tags, prompts, the popup rack editors and the new
  Devices/Clip window) received the same frameless header as the main window: a dark centred
  title, drag by the header, and the macOS traffic lights in a left inset — instead of a grey
  system frame.

### Added
- **MIDI Learn.** The **MIDI** button in the top right (next to CPU) enters learn mode:
  mappable controls light up, you click one and move a knob or fader (or press a note) on the
  MIDI controller, and the binding is made. Mappable targets cover every built-in instrument,
  effect and MIDI effect parameter — now including mode switches, chips and toggles, plus the
  **ADSR** and **filter** editors (each A/D/S/R stage and cutoff/resonance is separately
  addressable) — along with track volume/pan/mute/solo, master volume and the transport
  (play/stop/record). Highlighting works in detached windows too: the **Devices/Clip** panel
  and the popup rack editors (full UI / Params). Rack macros are learnable as well — map a
  parameter to a macro, then learn the macro. A dedicated **Map** tab in the browser shows and
  edits the mappings (range, inversion, deletion); they are saved with the project.
- **MIDI devices and MIDI Learn over MCP.** The MCP server gained tools for hardware
  controllers: listing connected MIDI inputs and toggling listening, surveying which CCs and
  notes a controller sends (turn a knob and see it), plus viewing and editing MIDI Learn
  bindings (range, inversion, deletion) and entering learn mode.
- **Oversampling for nonlinear devices.** **Forge**, **Nota Valve** and **Vintage** gained an
  **OVERSAMPLE** selector (Off/2×/4×/8×): the nonlinear stages (saturation, preamp) are
  computed at a raised rate, removing digital aliasing under heavy drive. Off by default.
- **The Devices/Clip panel in its own window.** The ⧉ button in the bottom panel's header
  detaches it into a separate window, with the selected clip's piano roll on top and the
  device chain below — both at once, with no tab switching. The panel hides in the main
  window meanwhile, and closing the window puts it back. The content moves across as-is:
  edits, meters and graphs keep working in real time.
- **Automation: the lane follows focus.** In automation mode (the `A` key), touching any knob
  or fader on any device — a built-in instrument or effect, a hosted plugin, or the mixer —
  immediately switches the track's automation lane to that parameter; touch another and it
  follows.
- **Automation: already-automated parameters are highlighted.** In the target picker (the pill
  on the lane), parameters that already carry automation are marked with a brass dot and
  gathered into a separate block at the top of the list for quick access.
### Changed
- **Automation: a tidier lane header.** The target pill's text is vertically centred and sized
  to the name so it is no longer clipped, and the record button was redrawn as a recognisable
  REC button (a dot plus "REC", lighting red when armed).

## [0.33.0] — 2026-08-11
### Added
- **Transport: clicking the position readout toggles bars ↔ time** (minutes:seconds).
  Clicking "1.1.00" shows "0:00.000", and clicking again returns to bars.
- **Audio→MIDI: Convert Melody / Harmony / Drums / Slice to New MIDI Track** (right-click an
  audio clip → Convert). *Convert Melody* — monophonic pitch detection (YIN) onto a new Nota
  Synth track (best on clean single-voice lines and vocals). *Convert Harmony* — polyphony
  via STFT plus spectral peak picking with harmonic suppression → chords on a Nota Synth
  (approximate; best on sustained chords and pads). *Slice* cuts the audio at transients (or
  into 16 equal beats) across the pads of a new Drum Rack, plus a MIDI clip in order.
  *Convert Drums* detects hits, roughly classifies them as kick/snare/hat, and assembles a
  3-pad kit out of the loop's own hits plus a MIDI pattern. All the DSP is our own (energy
  onsets, YIN, an in-house FFT) and permissively licensed. The result is aligned to the
  clip's displayed length, so warp and tempo are taken into account.
- **Browser: favourites and tags** for instruments, audio effects and MIDI effects.
  Right-click gives "Add to Favorites" and a "Tags" submenu (assign and clear). Tags are
  edited (title plus colour) in a separate window; the browser header gained filter chips —
  "★ Favorites" and one per tag — and clicking one filters the active tab. Rows show ★ and
  coloured tag dots.
- **Browser → Projects: a context menu** — "Open", "Reveal in Finder" and "Delete…" (with a
  confirmation; moves the `.nota` to the Trash).
- **Browser → Files: a context menu** — "Reveal in Finder" for samples and folders.
- **Browser: hints for empty tabs** — when a list is empty, a centred description explains
  what the tab is and how to put content into it (presets, samples, projects and so on).
- **Transport: a Follow button** — the arrangement follows the playhead (which stays
  centred while the view scrolls).
- **Transport: the time signature is editable** — the numerator and denominator fields drag
  with the mouse (the denominator snaps to a power of two), and the value is saved with the
  project.
- **Browser → Presets: saved device presets** now appear in the Preset tab as a tree:
  category (Instruments / Audio Effects / MIDI Effects) → device → preset (built-in as well
  as VST/AU). Right-click a preset for "Reveal in Finder".

### Changed
- **Browser → Files: tree navigation** — samples are now grouped into expandable and
  collapsible folders instead of one flat list.
- **Browser: favourited devices sort to the top** of their lists (Instr/FX/MIDI).
- **Browser: the presets icon** changed from a star to sliders (faders).
- **Transport: the Q button (launch quantize) is live** — clicking cycles the value (None …
  1/16 … 4 Bars); it applies when launching slots and scenes in Session.
- **Transport: METRO and LOOP are now icons** rather than text (a metronome and a loop);
  LOOP keeps its range beside it.
- **Dropping an instrument from the browser onto an existing track replaces the
  instrument** instead of creating a new track: dragging a synth or plugin onto an
  instrument track swaps its instrument in place, keeping clips, devices and volume. A new
  track is created only when dropping into empty space; racks (Instrument and Drum Rack) are
  not replaced in place.

### Fixed
- **Automation: clicks and edits** — a single click in automation mode no longer drops a
  point: as in the arrangement, it moves the playhead (when stopped) or does nothing (when
  playing). A point is added by double-clicking, including between two existing points, and
  double-clicking a point deletes it. Segment curvature is on Alt+drag. As a result, undo
  (Ctrl/Cmd+Z) after a copy or paste is no longer swallowed by accidental points. Ctrl/Cmd+D
  duplicates a shift-selected automation range.
- **Automation: selection and paste** — a shift-selected range can now start on a point
  (previously clicking a point went into fine-drag, so a range bounded by points could not
  be selected, which is why Ctrl/Cmd+D did not work). Pasting and duplicating no longer
  overwrite the boundary point with a redundant one: a point exactly at the paste position
  is kept, and the pasted fragment's first point, if it coincides, is skipped.
- **Presets: saving built-in synths** — Operator, Bass, Pendulum and Flux (and any new
  fully-parameterised synth) no longer answer "Nothing to save"; they save as presets. The
  capture now excludes only devices holding samples or patterns (Sampler, Grain, Rhythm,
  racks) rather than being limited to an old list of four synths.
- **Transport: Stop returns playback to the launch point.** Put the cursor at 5.1, Play,
  Stop, Play again — playback starts from 5.1, not from where it stopped. A seek sets the
  start anchor that Stop rewinds the playhead to.
- **Transport buttons stopped working intermittently** — Space and Enter are now caught on
  the window during the tunnel phase, so a focused control (a slider, checkbox, combo box, or
  a knob in a built-in device) no longer intercepts the space bar and fires instead of
  Play/Stop.

## [0.32.0] — 2026-08-10
### Added
- **A built-in MCP server** (phase 1) — an AI (Claude Desktop or Claude Code) can now write
  music directly in the open Nota project. The app raises a local MCP server over HTTP
  (loopback only, `127.0.0.1`), and the model drives the live session through a set of
  tools: transport, tempo, time signature, loop and metronome; a full project snapshot
  (`get_overview`); tracks (add, remove, volume, pan, mute, solo, groups, sends);
  instruments (add by kind, read and write parameters); audio effects (add, remove, reorder,
  bypass, parameters); MIDI clips and notes (piano roll — add a clip, read, write, append and
  clear notes, move and split); automation (create a lane, read and write points); MIDI
  effects; and engine status. Every edit goes through undo and appears in the UI at once.
  Enable it in Preferences (Appearance → "AI control (MCP)"); it is off by default.
  Preferences also has a "Copy config" button that copies a ready-made client JSON config,
  with the current port, to the clipboard.
- **MCP server, phase 2** — coverage extended to the session, racks, samples and mixer: the
  clip launcher (a session grid snapshot, create a MIDI or audio slot, read and write slot
  notes, loop length, launch and stop a slot or scene, launch quantization, slot recording,
  slot ↔ arrangement); Instrument and Drum Rack (a chain snapshot, add and remove a chain and
  change its instrument, read and write a chain instrument's parameters, chain mix
  gain/pan/mute/solo, key and velocity zones, trigger note, 8 macros and their mappings, rack
  volume and glide, plus per-pad choke/tune/decay and kit swing/humanize on the Drum Rack);
  sample loading (create a Sampler or Grain with a file, load a sample onto a track or into a
  rack chain, read sample info); and the mixer (sends to returns, the return list, track and
  master meters, record source).
- **MCP server, phase 3** — plugin hosting, presets, the effect rack and export: plugins
  (list the AU/VST3 catalogue, look up by id, add as a track instrument or an insert effect,
  open and close the editor, read and write parameters and state); factory presets (list,
  apply to a new track or in place) and user presets (list, apply, save); the Audio Effect
  Rack (snapshot, Parallel/Series/Select mode, dry/wet, volume, chain-select, add and remove
  a chain, a chain's instrument and parameters, mix, 8 macros and their mappings); and
  exporting the master or stems to WAV (pcm16/pcm24/float32). Opening and saving projects
  stays outside MCP for now, as it needs transport state on the app side.
- **Nota Rhythm** — a built-in drum machine (instrument kind 12): eight voices, each with
  its own electronic-drum synthesis engine (analog and FM kick, noise snare, metallic hats,
  clap, rim, tom, perc), programmed on an internal 16-step sequencer that runs in sync with
  the transport; MIDI notes trigger the voices live as well. Each voice has 7 knobs
  (Tune/Decay/Punch/Tone/Drive/Level/Pan), plus Swing/Humanize/Accent; there are 4 pattern
  banks (A–D), accents (shift+step) and per-step velocity, with a teal playhead. Automation
  (knobs grouped per voice), presets (6 kits: 808/909/Trap/House/Lo-Fi/Techno), persistence
  and cloning are all supported. Any voice can be switched to **Sample** mode and given your
  own one-shot (the Sample button opens a file picker, or drag a sample from the browser or
  Finder onto the voice row) — the sample plays through the same logic with the
  Tune/Decay/Tone/Drive/Level knobs, and samples are saved with the project. The drum
  machine is also reachable over MCP: add a track (kind 12), snapshot the pattern
  (`get_rhythm`), program steps and rows (on/velocity/accent), select and clear a bank,
  switch a voice between synth and sample, and load a one-shot (voice knobs go through the
  common instrument parameters).
- **Windows (x64) support** — Nota now builds and runs on Windows, not only macOS. Audio
  goes through WASAPI, MIDI input through WinMM, and plugin hosting is VST3 (Audio Units
  remain macOS-only). Settings, logs and autosaves are written to `%APPDATA%\Nota`. The
  distributable is an Inno Setup installer (`Nota-Setup-<version>.exe`). The whole engine
  and DSP is shared cross-platform code; the macOS path (CoreAudio/CoreMIDI/AU) is
  unchanged.
- **WASAPI Exclusive Mode (Windows)** — Preferences → Audio gained a "WASAPI exclusive mode"
  toggle. With it on, Nota opens the output device exclusively for minimum latency, and other
  applications cannot play through it meanwhile. A device in exclusive mode dictates its own
  native format (the sample rate and buffer size in Preferences are ignored), so the native
  values apply. If the device is busy or refuses exclusive access, Nota falls back to shared
  mode automatically and says so in the status bar. The option is hidden on macOS, where
  CoreAudio already talks to the device directly.
### Fixed
- **The application icon on Windows** — the .exe and the taskbar/alt-tab entries now show
  Nota's own icon (the .exe had no embedded Win32 icon, and the window showed the default).
  The icon comes from `assets/icons/windows/nota.ico`.
- **The Share feedback button on Windows** — the "Share feedback" pill in the title bar was
  no longer overlapped by the close and minimise buttons: space is now reserved on the right
  for the caption buttons on Windows.
- **The scanner worker on Windows** — "Rescan plugins" in Preferences no longer reports
  "Scanner worker not found": the binary name now accounts for the `.exe` extension on
  Windows.
- **Settings and About in the menus on Windows** — Settings moved into the File menu
  (`Ctrl+,`) and About into Help, because Windows has no "Nota" application menu, which made
  Preferences unreachable.

## [0.31.0] — 2026-08-08
### Added
- **Track groups** (submixes, group tracks): combine tracks into a group (⌘/Ctrl+G or the
  "Group tracks" menu item), and each group gets its own device chain plus
  fader/pan/mute/solo. Groups **nest** (a group inside a group), collapse via the triangle in
  the header (hiding the child tracks), and child rows are shown indented. Dragging a track
  onto a group adds it to that group; dragging to the top level takes it out. Ctrl/Cmd-click
  on headers builds a multi-selection to group; ⌘/Ctrl+Shift+G ungroups. The hierarchy is
  saved with the project.
- Reordering tracks by dragging: grab a track by its name row in the arrangement header and
  drag it to a new position — an accent insertion line shows where it will land. Returns stay
  after regular tracks. The action goes into undo.
- Devices panel: devices can now be dragged by the ⠿ handle to reorder them within their own
  group (MIDI effects / audio effects) — the insertion point is highlighted with an accent
  bar.
- Devices panel: device selection (an accent frame) plus a clipboard — Ctrl/Cmd+C, X, V and
  Delete, along with a context menu on the header (Copy/Cut/Paste/Delete/Save preset). A
  copied device (built-in, plugin or MIDI effect) can be pasted onto another track, and the
  paste lands immediately after the selected one.
- Nota Flux (built-in instrument, kind 11) — a vector-morphing analog synth built to the
  "Nota Flux" design: an XY pad with four "timbre worlds" at the corners
  (WARM/GLASS/MOOG/GRAIN) and its own four-corner radial gradient — you move a point rather
  than turn oscillators. The centrepiece is **React**: the synth listens to another track
  (sidechain) and turns its envelope, transients and spectral tilt into modulation; a Target
  switch chooses what the reaction drives — Filter, Pitch, Space, or the vector itself
  (pulling the point in time). There are five macro knobs (Age — analog wear, Motion —
  tempo-synced drift, Filter, Env — pad⇢pluck, Space), a live sidechain scope, and a "ghost"
  of the point on the pad. Automation, persistence (including the React source), cloning and
  6 factory presets.

## [0.30.0] — 2026-08-07
### Added
- Nota EQ-3 (built-in effect, kind 16) — a three-band DJ-style "performance EQ" built to
  mockup 3j: Low/Mid/High bands, each with a vertical fader with a 0 dB detent and a KILL
  button that removes the band entirely, two crossover frequencies (Low/Mid and Mid/High)
  and a 24/48 dB/oct slope (Linkwitz-Riley). A response curve with a real-time spectrum
  behind it, automation, persistence, cloning and 6 factory presets.
- Nota Forge (built-in effect, kind 17) — a multi-stage saturator built to mockup 3m: three
  saturation stages on screen at once, each with its own algorithm
  (Tube/Diode/Tape/Fuzz/Digital/Fold), drive, output trim and teal feedback (a disabled
  stage dims); four routings — Serial/Parallel/Mid-Side/Multiband — as icons; global
  Amount/Tone/Wet, Bias/Width shaping and LFO→Drive / Env→Tone modulation (free or synced).
  Visualisers: a transfer curve (static brass plus a modulated teal dashed line) and a
  harmonics bar chart (THD, even/odd), with automation, persistence, cloning and 6 factory
  presets.
- Nota Level (built-in effect, kind 18) — an automatic loudness-matching utility (LUFS): it
  measures the input LUFS (ITU-R BS.1770 K-weighting) and applies the gain needed to hit
  TARGET; AUTO tracks a sliding window while MATCH freezes the correction for an honest
  A/B, and a true-peak-safe stage stops a boost from clipping. A sidechain was added: pick a
  reference track and the target follows its loudness (match to reference). Visualisers: a
  loudness history (input/output/target) plus IN/OUT/TP/correlation meters; automation,
  persistence, cloning and 6 factory presets (Stream/Podcast/Broadcast/Club/Fast/Match
  Reference).
- Nota Shutter (built-in effect, kind 19) — a noise gate: the shutter opens when the signal
  crosses Threshold and closes when it falls below Return (hysteresis against chatter);
  Attack/Hold/Release shape each opening, Floor sets the closed-signal level (−∞ = mute,
  higher = ducking), Lookahead opens the gate early, and Flip turns the gate into a ducker.
  The detector keys off its own track or an external sidechain through its own band-pass
  (HP/LP) with Listen monitoring. Visualisers: a signal graph (input level, the gate-gain
  curve, and threshold/return lines) plus IN/GR meters and an open/shut LED; automation,
  persistence, cloning and 6 factory presets (Tight Drums/Gentle/Vocal/Hard Slice/Kick
  Duck/Trance Gate).
### Changed
- Built-in effects renamed for a consistent naming style: **Amplifier → Nota Valve** and
  **Auto Pan → Nota Orbit** (the kind and the parameters are unchanged, so projects open as
  before).
- Clip editor (piano roll):
  - the key being pressed right now (computer S–K or a MIDI keyboard) is highlighted on the
    roll's keyboard and as a bar along its row, regardless of which track is armed;
  - the roll's scroll and zoom are remembered per clip and no longer reset when you return;
    for a new clip the vertical view centres on its notes rather than always on C5;
  - moving a note shows the arrows cursor (⤡), and changing its length shows the resize
    cursor;
  - moving a note or changing its length reaches the engine immediately (live, without
    cluttering undo — one undo step per gesture), so playback follows the edit at once;
  - a MIDI clip trimmed on the arrangement grid now mutes note tails at its real (shortened)
    boundary instead of playing the full note length.

## [0.29.0] — 2026-08-06
### Added
- The computer keyboard plays MIDI straight into a hosted plugin's window: while the plugin
  editor has focus, the S–K row (the note-typing layout) sounds that instrument's notes.
- Audio clip editor: a grid-snap toggle above the waveform (on by default) — the blue trim
  handles of a warped clip land exactly on beats, so cuts stay clean.
- Clicking a clip in the arrangement now also moves the playhead to the click point, as
  clicking an empty track does, in addition to selecting it.
- Shift+drag creates a time range even when the drag starts over a clip (a plain shift-click
  still makes a rectangular clip selection).
- Cmd/Ctrl+L loops exactly the selected time range and enables the loop; with no selection
  it still toggles the loop as before.
- Cmd/Ctrl+E cuts every track inside the time selection at its boundaries, isolating that
  span into separate clips in a single undo step; with no selection it still splits at the
  cursor.
- The track header gained a PAN control — a horizontal bipolar bar (50L … C … 50R) beside
  the volume (one row: 2× volume, 1× pan).
- Double-clicking the volume and pan controls in the track header resets them to their
  defaults (0 dB / centre).

### Changed
- **Nota Utility** was reworked to the new design (mockup 3j, "show what you cannot hear").
  Instead of three controls (Gain/Width/Mono) there is now gain, L/R balance, stereo width
  (mid/side, 0–400%), channel mode (Stereo/Left/Right/Swap), mono below a chosen frequency
  (bass mono), mute, and L/R phase invert. Plus live visualisation: a goniometer/vectorscope
  of the stereo field and a correlation meter with a red "mono risk" zone, input and output
  meters, and a Gain match button. Presets and automation work as on every other built-in
  effect.
- Changing an audio clip's length on a track now shows the material actually being revealed
  or hidden, instead of squeezing or stretching the waveform in place.

### Fixed
- Nota Flux no longer overloads: the output gained polyphony headroom (gain staging like the
  other synths) and a soft tanh limiter — dense chords and drive no longer clip past ±1.
- Nota Reverb no longer produces a periodic "metallic" overdriven roar: a NaN or Inf (from
  the source or from denormals) can no longer latch into the comb and allpass feedback loops
  or the tone filters — a bad sample is damped instead of ringing forever.
- During playback, clicking the arrangement grid no longer jumps the playhead — scrubbing
  happens on the top ruler, where the loop is set.
- The volume and pan controls in the track header now follow automation (they move during
  playback and scrubbing) — previously their positions never updated.
- A hosted plugin's GUI window opens above the others and focused — it used to hide behind
  the Nota window and had to be hunted down.
- A plugin window closes when its track or its device is removed from the chain — it used to
  stay on screen.

## [0.28.0] — 2026-08-06
### Added
- **Nota Strata** — a built-in audio-effect looper (device kind 15): a multi-layer overdub
  looper where each pass is a separate named layer with its own waveform, level and mute. The
  Record / Overdub / Play / Stop transport and Undo / Clear go through a new device command
  channel and are applied quantized to the loop's bar grid; the first Record pass sets the
  loop length and later ones are per layer. Feedback (teal) attenuates the layers already
  recorded, while input gain and speed/reverse work on the audio path. The card follows
  mockup 3n (700×260): a large bar.beat counter with a running cycle bar, four transport
  buttons captioned with the next action, a layer stack, and a LOOP rail (feedback / gain /
  speed / quantize / count-in / set-tempo / reverse · Undo / Export / Clear). Recorded layer
  audio persists into the project (as device blob state) and carries over when a track is
  duplicated; the settings behave as ordinary parameters (presets, automation). A shared
  device command and state ABI was introduced (deviceAction / layerWave / getState-setState
  blob).
- **Nota Ceiling** — a built-in audio effect (device kind 14): a look-ahead brickwall
  limiter with input drive, a ceiling, release (plus auto-release), three characters
  (Clean / Punch / Glue), lookahead and stereo link, plus a sidechain detector. The card
  follows mockup 3o (700×260): a LIVE strip with a large GR figure, a 4-second input/output
  history with the ceiling line and the "eaten" top in red, a separate gain-reduction
  ribbon, and a permanent loudness panel — LUFS-S / LUFS-I (BS.1770) and true peak, with
  peak holds and Reset peaks. Presets, automation and the sidechain work as on the other
  devices.

## [0.27.0] — 2026-08-06
### Added
- **Nota Dynamic EQ-8** — a built-in audio effect (device kind 13): an eight-band
  parametric where every band can react to level, like a personal compressor/expander. On
  top of the static half (type / frequency / gain / Q, as in EQ-8), each band gained a
  **Static / Duck / Lift** mode, a threshold, a signed range ("−" attenuates, "+"
  emphasises) and attack/release. The detector is a band-pass filter at the band's
  frequency.
- A dual curve: the **static** response in brass and the **momentary** one (including the
  current dynamic compensation) as a teal dashed line — the gap between them is the dynamics
  at work. Dynamic bands are marked with a teal handle and a dashed "reach" line showing how
  far the band can travel. Dragging a point sets frequency and gain, the wheel sets Q,
  double-click adds a band, and right-click chooses type and mode.
- A LIVE strip for the selected band: **Freq/Q** in brass, **Thresh/Range/Attack/Release**
  in teal (they modulate gain), plus **Sidechain** (keying from another track) and **Solo
  band**. A table of the eight bands with DYN columns (direction and range) and a live GR
  bar, and below it a 2-second GR history sparkline. Every parameter is an ordinary device
  parameter, so automation, persistence and cloning work as standard. Factory presets:
  De-Ess, De-Harsh Vox, Bass Control, Vocal Presence, Warm Master, Punch Tighten.
### Fixed
- Hosted plugin (AU/VST3) GUI windows open again without crashing. The engine exported its
  own static copy of JUCE, so the symbols coalesced with the plugin's own JUCE and corrupted
  the heap while the editor was being built — only the engine's C ABI is now exposed, and
  each plugin keeps its own JUCE.
- The scan no longer "loses" plugins that write their log to stdout while loading (**Maschine
  2**, for instance): their description was corrupted by the noise and silently discarded.
  The worker's output is now framed with markers and parsed reliably, so after a rescan such
  plugins show up in the browser.
- Hosted plugins sync with the DAW transport again: they receive tempo (BPM), position, play
  state and loop boundaries through the host playhead — synced devices (Maschine, tempo
  delays and LFOs, arps, loopers) run on the project's clock.

## [0.26.1] — 2026-08-05
### Fixed
- The universal `.dmg` now launches on **Intel Macs**. The "universal" build used to work
  only on Apple Silicon: the bundle carried the arm64 build of
  `System.Private.CoreLib.dll` (which cannot be merged with `lipo` — it is PE, not Mach-O),
  so on Intel the runtime died with `BADIMAGEFORMAT` at launch (the app bounced in the Dock
  and quit). The `.app` now carries both complete runtimes (`arm64` and `x64`), with a
  universal launcher as the entry point that starts the right one for the processor.
- The minimum macOS version was lowered to **13.0** and is now consistent across every
  binary. The engine (`libnota_engine.dylib`) and `nota-scanworker` used to be built with
  `minos = 26.0` (the build machine's version), so on older macOS the system refused to
  launch the app ("requires macOS 26.0…") or dyld would not load the engine.

## [0.26.0] — 2026-08-05
### Added
- **Nota Crush** — a built-in bit crusher (audio effect, device kind 12) built to mockup 3l
  (700×260). The core idea: you see the destruction before you hear it. The main display is
  a **quantizer**: a thin line for the source signal, a stepped brass output, and the level
  grid the amplitude is quantized onto. Fewer bits means coarser steps; a lower rate makes
  them wider. Real units instead of percentages: "7.9-bit @ 3.5 kHz", "239 levels", "hold 13
  smp".
- A LIVE strip: **Bit Depth**, **Sample Rate**, **Drive** (pre-crush), **Wet** and an
  **Anti-Alias** toggle. Three modes with character: **Digital** (hard truncate) /
  **Analog** (soft clip) / **Fold** (wavefold).
- A **GRIT** panel (teal spine): **Dither** (TPDF), **Jitter** (sample-rate wobble) and
  **Noise** (a noise floor) — they dirty the quantization separately from the clean path. An
  **OUTPUT** panel: a post filter (lowpass) and output **Gain**. An aliasing spectrum:
  signal in brass, mirror images in teal, and the Nyquist line.
- **Init** (reset to defaults) and **Bypass** buttons. Parameters are normalised 0..1, so
  automation, persistence and cloning work as standard. Factory presets: Broken Radio, 8-Bit
  Arcade, Lo-Fi Warmth, Digital Grit, Folded Crunch, Telephone Line.

## [0.25.0] — 2026-08-02
### Added
- Nota Random (MIDI effect) reworked to mockup 3b (700×260) — the last of the five MIDI
  devices to get its own card instead of generic faders. The LIVE strip carries **Chance**,
  the **Gauss / Even / Walk** distribution and the **Lock seed** / **Re-roll** buttons
  (reproducibility).
- A **WHAT VARIES** panel: one row per dimension — **Note** (± semitones), **Velocity**,
  **Timing** (micro-shift), **Skip** (drop a note) and **Octave** — each with its own
  amount; colour follows the category: pitch brass, timing teal, skip red.
- The right rail: a drawn **Distribution** shape, a **Rate: Per note / Per bar** tempo, and
  a **Stay in scale** toggle (randomness stays diatonic).
- New parameters (append-only, slots 0–1 preserved: Chance/Range): Vel Amt, Time Amt, Skip,
  Oct Amt, Dist, Rate, Stay In Scale, Locked, Seed. Under **Locked** the randomness is keyed
  to the note's position plus pitch, so it reproduces across runs. Factory presets: Human
  Drift, Pitch Roulette, Ghost Notes, Octave Jumps.

## [0.24.0] — 2026-08-02
### Added
- Nota Velocity (MIDI effect) reworked to mockup 3b (700×260): its own card instead of
  generic faders. The LIVE strip carries the **Curve / Compand / Fixed** shape with
  Drive/Fixed and Random.
- An interactive **TRANSFER curve** (input velocity → output): a dashed identity diagonal,
  the brass transfer curve, teal dots for the last notes and a bright dot for the current
  one.
- The right rail: **Out Range** (a two-sided limiter on output velocity), **Random on** plus
  a **Both / Up / Down** direction, and a **Last 12 notes** histogram.
- A new MIDI-effect telemetry channel, **midiScope** (a float buffer), plus reuse of
  midiLastIn/Out for velocity — this feeds the curve, the dots and the histogram.
- New parameters (append-only, old projects load as they are): Mode, Out High, Random Dir
  (slots 0–3 preserved: the old Scale→Drive, Fixed amt→Out Low). Factory presets: Soft
  Hands, Humanize, Compress, Fixed 100.

## [0.23.0] — 2026-08-02
### Added
- Nota Length (formerly Nota Note Length, a MIDI effect) reworked to mockup 3b (700×260):
  its own card instead of generic faders. The LIVE strip carries the **Sync / ms / Gate %**
  length mode (with 1/16 · 1/8 · 1/8D · 1/4 rate chips and a **Length** slider) and an **On
  note-on / On note-off** trigger.
- A **GATE visualiser**: the resulting note length drawn as a bar on a 0…2 s scale; with
  modifiers active it shows the min/max spread.
- A **MODIFIERS** rail: **Vel → Len**, **Key → Len**, **Random** (per-note length
  modulation), **Legato** (retrigger held notes) and **Clip length limit** (the forced note
  is never longer than the one played).
- New parameters (append-only, old projects load as they are — Rate/Gate stay in place):
  Mode, Ms, Percent, Trigger, Vel to Len, Key to Len, Random, Legato, Clip Limit.
- Factory presets: Staccato, Tenuto 1/4, Half Gate, Fixed 120ms.
### Changed
- **Nota Note Length was renamed to Nota Length** (the same MIDI kind 3). The Sync rate
  divisions were reduced to 1/16 · 1/8 · 1/8D · 1/4 — in previously saved projects the Rate
  value may shift (the effect is new and pre-1.0).

## [0.22.0] — 2026-08-02
### Added
- Nota Scale (MIDI effect) reworked to mockup 3b (700×260): its own card instead of generic
  faders. The LIVE strip carries a **Root** stepper, the
  **Major/Minor/Dorian/Phryg/Penta/Custom** scale types and the **Fold: Nearest / Down / Up**
  folding mode.
- An interactive **NOTE MAP**: 12 notes in a 6×2 grid — clicking adds or removes a note from
  the scale (making it Custom); notes outside the scale show an arrow pointing where they
  fold to (per the Fold mode); the root is highlighted; and a counter reads "N of 12
  remapped".
- The right rail: a live **LAST IN → OUT** (the last remapped note plus the distance in
  semitones, through the new MIDI-effect telemetry channel), **Range** (a limiter: notes
  outside the range pass through untouched), **Follow Key** (the root follows the notes
  being played, via a decaying histogram) and **Learn** (build a Custom scale from what was
  played) / **Clear**.
- New parameters (append-only, old projects load as they are): Mask 0–11 (a custom 12-note
  mask, used when Scale = Custom), Fold, Follow Key, Range Low/High, Learn.
- Scale factory presets: **Dorian Up** added, and Penta Minor now uses Fold Down.

## [0.21.0] — 2026-08-02
### Added
- Nota Chord (MIDI effect) reworked to mockup 3b (700×260): its own card instead of generic
  faders. The LIVE strip carries quick chord types **Maj7 / Min7 / Sus4 / 5th / Custom**,
  **Strum** (spreading the notes in time), and **Keep root** and **Fold in scale** toggles.
- A **SHIFTS** stack: six voices, each a semitone offset (a bipolar slider) and a relative
  **velocity**; active voices are highlighted.
- A **RESULT** panel: the resulting chord for a reference C3 as note names plus a two-octave
  **preview keyboard** (played bright / added), a **Voices N of 6** counter and **Spread**
  (opening the voices across octaves).
- New parameters (append-only, old projects load as they are): Voice 6, Strum, Keep Root,
  Spread, Fold, Vel 1–6. Strum is implemented as a queue of deferred note-ons (staggered
  note-ons, with unplayed ones cancelled on release).
- Factory presets: **Maj7 Wide** and **Strummed Guitar** added.
### Note
- Detune (in cents) was deliberately left out — the note stream carries an integer pitch to
  the built-in instruments, so microtuning cannot be represented there.

## [0.20.0] — 2026-08-02
### Added
- Nota Operator reworked to mockup 3g (700×260): a card with an **OPS / ALGO / FILTER** tab
  rail on the left instead of three fixed panels.
- The **OPS** tab: four operator rows naming their role in words (**CARRIER** / **MOD → X**)
  in the role's colour (brass for a carrier, teal for a modulator), with wave · ratio · fine
  · level; beside them a live **SPECTRUM** (the harmonic spectrum of the current patch on
  the note being played) and a **VEL → FM** toggle.
- A separate **ALGO** screen: 11 algorithms as drawn sketch diagrams (a picker, with the
  active one highlighted), a full-width **ROUTING** diagram with real connection lines and
  **drag one operator onto another** to change the route, plus two per-op envelopes (carrier
  and modulator) and **KEY → LEVEL**.
- The **FILTER** tab: LP/HP/BP plus Freq/Reso and Volume, with an interactive response curve.
- New parameters (append-only): **FM Depth**, **Glide** (portamento), **Vel → FM**, **Key →
  Level** and **Mono** (mono mode with legato under glide). Instrument spectrum telemetry
  was added as a new channel (Instrument scope).
### Changed
- Operator's algorithm count grew from 8 to 11. The "Algorithm" parameter value now maps as
  `round(v·10)` — in previously saved Operator projects the algorithm choice may shift to a
  neighbouring one (the instrument is new and pre-1.0; the other parameters are unaffected).

## [0.19.0] — 2026-08-02
### Added
- Nota Beat Repeat reworked to mockup 3h (700×260): a LIVE strip with the **Mix / Insert /
  Gate** mode, Chance, Gate, a **Repeat** button (momentary latching repeat) and a **Latch**
  toggle (hold the repeat).
- A new **TIMELINE** visualiser: the captured slice → decaying repeats → a playhead moving
  in time with the interval, over a beat and bar grid, captioned "capture N bar → M rep",
  showing the BPM, with a legend (captured / repeat·decay / playhead).
- **INTERVAL** and **GRID** rails — button grids that name the musical consequence ("every 1
  bar", "slice 125 ms"); plus Offset and Variation.
- A **REPEAT CHARACTER** rail: Pitch/Pitch Dec/Decay/Volume, a collapsible **FILTER**
  (Freq/Width) and **Mix** (dry/wet). Old projects load as they are (append-only, +2
  parameters: Mix, Latch).

## [0.18.0] — 2026-08-02
### Added
- Nota Auto Shift reworked to mockup 3i (700×260): a LIVE strip with the detected note, the
  cents value and a bipolar cents meter, plus an **Auto / Manual / MIDI in** key source.
- A new **PITCH TRACE** visualiser: two traces — the detected pitch (a teal point cloud) and
  the corrected output (a brass line) — laid over note lanes with the target note
  highlighted, so the amount of correction over the last few seconds is visible.
- A KEY panel (a 6×2 grid) plus a SCALE list and a **Follow scale device** toggle (inheriting
  the key from a Nota Scale on the track); the **Auto** key and **Learn key** are derived from
  the detected pitch.
- A parameter rail: **CORRECT** (Amount/Speed/**Range** ±5 st — in teal) and **SHIFT**
  (Shift/**Formant**/Mix — in brass); Range (protection against octave jumps) and Formant
  (formant compensation) were added. Old projects load as they are (append-only, +4
  parameters).

## [0.17.0] — 2026-08-02
### Added
- Nota Audio Effect Rack reworked to mockup 2r (700×260, sharing the shell with 2p/2q): a
  strip of named macros, a chain list with gain/meter/M·S, horizontal device cards for the
  selected chain, and a rack rail on the right.
- A **MODE — Parallel / Series / Select** switch:
  - Parallel — chains sum in parallel (as before);
  - Series — chains line up into one serial path (the view changes to chain columns);
  - Select — only chains whose zone on the level axis covers the selector are active; the
    axis carries a live input-level marker and an "in −N dB → K chains" counter.
- Rack output: **Dry/Wet + Gain** (instead of volume+glide), a **PDC** switch, **Fold**
  (collapse the device cards) and **Save** (save the rack as a preset).
- Everything is saved with the project (rack blob format v7; old projects load as they are).
- Shared across racks: chains gained a delete button (✕); the **+ Device** field now has a
  dashed outline and highlights on hover and drop, and its menu carries a **Plug-ins**
  submenu with third-party effect plugins alongside the built-in effects.
### Fixed
- The Audio Effect Rack chain list is clickable again and scales correctly (the card was
  stretched to the full shell width — the previous fixed size ate mouse hits).

## [0.16.0] — 2026-08-01
### Added
- Nota Drum Rack reworked to mockup 2q (700×260) with two views:
  - **Pads** — a 4×4 grid with each pad coloured from the track palette, the selected pad
    framed in the accent, the sounding pad highlighted, and a panel for the selected pad:
    the sample waveform, Volume/Pan/Tune/Decay with real values, the choke group and
    Hot-swap.
  - **Mixer** — every loaded pad as a mixer row (volume/pan/M/S/choke), with no scrolling.
  - A LIVE strip: banks C1–C4 (up to 64 pads), Swing, Humanize and Fold (hide empty pads).
- Per-pad shaping in the engine: choke groups (a monophonic cut), Tune (transposing the pad)
  and Decay (the pad's amplitude envelope); kit-level Swing and Humanize shift the timing of
  hits. All of it is saved with the project (rack blob format v6; old projects load as they
  are).

## [0.15.1] — 2026-08-01
### Changed
- Every built-in device is now named consistently with a "Nota" prefix: Nota EQ-8, Nota
  Compressor, Nota Reverb, Nota Delay, Nota Utility, Nota Amp, Nota Auto Filter, as well as
  Nota Instrument Rack, Nota Drum Rack and Nota Audio Effect Rack (some effects and racks
  used to go without the prefix). This affects display names only — old projects and presets
  load as they are.

## [0.15.0] — 2026-08-01
### Added
- Instrument Rack reworked to mockup 2p (700×260): a strip of 8 named macro sliders
  (unmapped ones dimmed), a chain list with key and velocity zones, level (dB), a meter and
  M/S, a row of horizontal device cards (name · type · two buttons: **GUI** for the plugin's
  or built-in's full editor and **Params** for a knob grid · assigned macros), a zone map
  beneath it (chain ranges above a keyboard — drag the bar ends) and a RACK OUT rail
  (Volume · Glide · Macro map · Fold/Save).
- **Key and velocity zones per chain** — a note reaches a chain only inside its key and
  velocity range (a SPLIT Key/Vel switch above the zone map); real note routing, not every
  chain layered together.
- Named macros (double-click a name to rename), rack output (volume affects the sound, glide
  is stored), and a per-chain meter.
- A second **Macro map** view: a macro list plus the selected macro's targets with a
  draggable range and values, a curve choice (Linear/Exp/Log/S) and Unmap all.
### Changed
- Rack blob v4 → v5 (append-only, old projects load): + chain zones, macro names, rack output
  (volume/glide) and the mapping curve. A macro's curve now affects how it is applied
  (Linear/Exp/Log/S).
### Fixed
- Amp, Auto Filter, Vintage, Auto Pan, Auto Shift and Beat Repeat can now be added to a rack
  chain of any type — the rack device factory only knew EQ/Comp/Reverb/Delay/Utility (kinds
  0–4), so the newer effects (6–11) were never created (the menu and a drop from the browser
  silently added nothing).
- The **GUI** button on a built-in instrument in a chain now opens its real editor
  (Volt/Aurora/Operator/…) instead of a parameter grid: the editor is hosted over the chain's
  param surface through a proxy engine (a DispatchProxy redirects plugin-param calls
  (track,−1,i) → (track,chain,i)). Plugins still open their native GUI, and the Sampler its
  tabbed editor.
- A sample file can now be dropped into the Sampler window opened from a chain — from the
  browser or from Finder: it loads into the chain's sampler and the window rebuilds (the
  engine's `rackSetChainSamplerSample` swaps the sample through a state swap, preserving the
  chain's parameters and devices).

## [0.14.0] — 2026-08-01
### Changed
- Drum Rack and Instrument Rack chains: every device (instrument and effects) is now a
  consistent compact card with parameter knobs, and the card header carries a **Full** button
  that opens the device's complete UI in a popup: for the built-in Sampler that is the full
  tabbed editor (Sample/Pitch/Env/Filter, waveform, envelope, filter — through
  `ISamplerAccess`); for a hosted plugin (instrument **or** effect) its native GUI window;
  and for the other built-ins a roomy grid of every knob. (A Sampler in a chain used to
  expand into an unwieldy 700×260 card, breaking the row of compact cards.)
### Fixed
- Hosted effect plugins in a rack chain now open their native window from the Full button
  instead of a parameter grid (the engine gained `rackOpenChainDeviceEditor` →
  `Device::openEditor()` for a chain effect; previously only an instrument plugin could open
  its window).
### Removed
- The old compact `SamplerEditor` was deleted, fully replaced by the reusable
  `SamplerInstrumentCard.BuildEditor`.

## [0.13.0] — 2026-07-31
### Added
- Auto Filter reworked to mockup 2g — from a wide 1440px card down to the shared 700×260
  shell: a LIVE strip (LP/BP/HP/NO type icons · 12/24 slope · Freq/Res sliders ·
  Clean/Analog) above a body of graph | modulation column | output rail. The graph remains
  the centrepiece: a solid brass current response, a dashed teal response at the modulation
  peak, and a teal band covering the whole sweep range. ENV and LFO are two teal lanes (a
  switch plus a shape/graph and three knobs). The output rail carries Drive/Dry-Wet/Gain plus
  a compact sidechain (source and gain).
- Auto Filter: the LFO gained tempo sync (Sync — Rate picks a division from 1/1 to 1/64, with
  the phase locked to the transport) and a stereo phase (Stereo 0…180° — the right channel
  runs its own filter cutoff, giving width).
### Changed
- Auto Filter: 19 → 21 parameters (append-only, old projects load): +LFO Sync, +LFO Stereo.
  The response graph was brought in line with the mockup (brass = current, teal dashes =
  modulated, teal band = the sweep).

## [0.12.0] — 2026-07-31
### Added
- The Sampler was renamed **Nota Sampler** and reworked to mockup 2o — on the shared 700×260
  shell with a LIVE strip (Vol/Pan/Cutoff sliders plus voice modes Poly 16 / Mono / Choke)
  and Sample · Pitch · Env · Filter tabs. The Sample tab holds an editable waveform (green
  start / red end, a brass loop with a timecode, a live cursor, an axis in seconds) plus a
  loop row (Off/Fwd/Ping/Rev, loop crossfade in ms, Snap zero to zero crossings) and an AMP
  ENV rail (curve plus A/D/S/R and Vel→Vol). Pitch holds Transpose/Detune/Root with a mini
  keyboard; Env a large ADSR editor; Filter type/Cutoff/Reso/Key Track with a response graph.
- Nota Sampler gained the capabilities it was missing: voice mode (Poly/Mono/Choke), loop
  crossfade, reverse loop (Rev), filter key-tracking by note, and Vel→Vol.
### Changed
- Nota Sampler: 17 → 21 parameters (append-only, old projects load); the instrument moved to
  the shared shell (it used to have its own SimpleCard). A rack-embedded Sampler (Drum Rack
  pads, chains) keeps the compact editor.
- A Zones tab (multi-sample mapping) was deliberately left out of this rework — the engine is
  single-sample.

## [0.11.0] — 2026-07-31
### Added
- Nota Pendulum reworked to mockup 2l — the pendulum field is now the instrument itself:
  Balls and Voice tabs on the shared 700×260 shell. The Balls tab holds a large field
  labelled with the held chord and lane lines, both swing trajectories (∧ solid, ∨ dimmed
  teal), a dashed trigger line through the apex with a ring, and **trails behind the balls**
  (a fading tail showing direction); the sounding ball is highlighted. On the right is a ball
  list (division · signed rate · phase · direction · note, with the playing row highlighted)
  plus Sort Up/Down, Quantize and Hold / First note / Reset buttons. On the Voice tab the
  field collapses into a 62px strip (so it stays in view) and the centre goes to waveforms
  plus voice and envelope knobs in real units (ATTACK 3 ms, DECAY, RELEASE, DETUNE 4 c,
  VOLUME −1.9 dB, TONE "warm") and a SPREAD rail (Rate/Detune/Pan plus Humanize and a Scale
  choice).
- Nota Pendulum: the envelope gained the Decay it was missing; side parameters Pan Spread,
  Humanize (teal), Hold (latch the chord), First Note (restart the pattern on a new chord),
  Scale (Root plus mode: Off/Major/Minor/Dorian/Mixo/Penta, snapping notes) and Reset.
- Motion became a choice of four swing curves (Linear · Pendulum · Ease · Bounce) instead of
  a percentage; Rate became a bipolar slider with a centre detent and a sign (reverse).
- Pendulum presets were updated (Warm Cascade, Slow Bloom, Fast Sparkle, Reverse Drift, Down
  Runs, Glass Bounce) for the new curves and parameters.
### Changed
- Nota Pendulum: 18 → 26 parameters (append-only, old projects load; Motion is now discrete).
  A new engine query for the held chord (`nota_track_held_notes`) feeds the field labels and
  the ball list with live notes.

## [0.10.0] — 2026-07-31
### Added
- The Compressor was reworked to mockup 2n: five character models
  (Clean/Glue/Punch/Opto/FET), soft knee, look-ahead, Peak/RMS detection, auto-release,
  auto-gain, a Range limit, sidechain HP/LP filters plus Listen, and MIX. A transfer plot (a
  dashed unity diagonal, the threshold line, the knee of the curve, and a draggable operating
  point: X = threshold, Y = ratio) plus a scrolling gain-reduction history in "record red"
  (the colour from the design system) with current and peak values; the labels moved into
  their own strip on top, so they stay legible as the graph fills.
- Compressor presets: Drum Glue, Drum Punch, Vocal Opto, Bus Parallel, FET Slam, Brick
  Limit.
### Changed
- Compressor: 5 → 16 parameters (the names were kept, so old projects load); a 700×260 card
  on the shared shell — a top strip (character models plus Thresh/Ratio/Mix sliders) ·
  graphs (labels drawn on the graph) · TIMING (Auto-release on the right) · a teal sidechain
  (HP/LP sliders in two columns, Listen on the right) · an OUTPUT rail.

## [0.9.0] — 2026-07-31
### Changed
- Nota Arp reworked to mockup 2k: 700×260 on the shared shell — a LIVE strip (Free/Sync plus
  divisions, Gate/Swing and outlined Hold/Retrig toggles), the lanes became a tab rail on the
  left (Velocity/Length/Chance/Ratchet/Transpose plus a STEPS counter at the bottom), a real
  step sequencer in the centre (height = velocity with opacity following the value, shading
  every 4 steps, step numbers under the columns — clicking mutes a step, the playing step is
  highlighted, muted ones are empty cells, ratchets read "×2/×3" in teal above the bar, ⌥
  draws a ramp), and a right rail (Order spelled out in two columns · Oct 1-4 · Dir ↑↓↕? ·
  Transp — each on its own row, fitting into 120px). Toggles are now outlined, and fill
  encodes only value or selection.

## [0.8.0] — 2026-07-31
### Added
- Nota Aurora reworked to mockup 2j — a full wavetable synth: two wavetable oscillators (each
  with its own table, position, warp, level and tuning) plus a sub oscillator and unison
  (voices/detune/spread); warp modes (Off/Sync/Bend/PWM/Fold); two filters with source
  routing (F1/F2/Both/Dry) and parallel/series mode; two envelopes (Env1 amp, Env2 free) and
  two LFOs (one synced); an 8×7 mod matrix plus 4 macros; built-in drive/chorus/reverb
  effects; and Mono/Poly.
- The Aurora card on the shared 700×260 shell with a rail of 6 tabs
  (Osc/Filter/Env/LFO/Mod/FX) and a new 3-D wavetable stack: 16 frames in a pile, with the
  active one highlighted.
- Aurora presets: Glass Choir, Super Saw, Sub Bass, Fold Lead (plus the original 4).
### Changed
- Nota Aurora: 11 → 132 parameters (append-only, old projects load); the wavetable is now 16
  frames per bank instead of 8. The Osc and Filter tabs were brought exactly in line with
  mockup 2j (the 3-D view with overlay labels plus a bank/warp row and oscillator lanes; the
  filter with type and slope in the top strip, and the graph plus knobs and routing below).
- The mod-matrix cells (Aurora and Volt) were redrawn to the mockup: a horizontal bipolar bar
  from a centre line (right +, left −) with a number; editing is a horizontal drag.
### Fixed
- Aurora's 3-D wavetable stack no longer overflows its panel (the geometry fits all 16 frames
  and clips).

## [0.7.0] — 2026-07-31
### Changed
- MIDI effects (Nota Arp, for instance) moved onto the same shared shell as instruments and
  effects: a common header with the on/bypass dot, name, MIDI tag, preset picker, A/B
  comparison, meter, and move/delete/drag buttons. The separate MIDI shell was removed — one
  wrapper card now serves all three chains.

## [0.6.0] — 2026-07-31
### Added
- Reverb reworked to mockup 2h: Hall/Room/Plate/Chamber algorithms, RT60 decay, HF damp,
  pre-delay, size, diffusion, low/high cut, width, tail modulation and Freeze; an interactive
  decay-tail graph (drag the tail for decay, the gap edge for pre-delay) marked with pre-delay
  and RT60. All on the shared 700×260 shell with a LIVE strip.
- Delay reworked to mockup 2h: independent L/R times (ms or tempo-synced 1/16…1/2 with
  triplets and dotted values), Link L/R, feedback, spread, ping-pong, tape WOW modulation and
  Freeze; a per-channel echo-tap graph (L above the axis, R below) with a ruler in bars and
  division buttons instead of a dropdown.
### Changed
- Reverb and Delay: parameters moved to normalised 0..1 (3 raw ones became 14).
### Fixed
- Reverb: a zero pre-delay no longer produces a whole buffer of delay (200 ms) instead of
  passthrough.

## [0.5.0] — 2026-07-31
### Added
- A single shell for device cards: every built-in instrument and effect now shares one
  header — the on/bypass dot, name, subtitle, preset picker, A/B comparison (snapshots of
  every parameter), a small stereo meter with a pinned dB figure, a voice counter (on
  synths), ◀▶ move arrows, ✕ delete and the ⠿ drag handle.
- Preset selection directly in the device header (applied in place) and A/B comparison of
  two parameter snapshots, with a copy of the active slot.
### Changed
- Every built-in synth (Volt, Grain, Synth, Aurora, Operator, Pendulum, Bass, Physical)
  moved onto the single shell: a card supplies only its body and the shared control draws the
  header — bringing every instrument into one visual language.
- Volt's meter moved from the output rail into the shared header (the rail keeps Gain/Pan).

## [0.4.0] — 2026-07-31
### Added
- Nota Volt — a 7×6 modulation matrix: sources (Amp Env, Filter Env, LFO 1/2, Velocity, Key,
  Mod Wheel) route to destinations (Pitch, Osc2 Pitch, Cutoff, Reso, Level, Pan) through
  bipolar cells — drag a cell up (+) or down (−), right-click to reset.
- Nota Volt — 8 macros: each macro knob adds an offset to a chosen matrix destination (the
  button above the knob switches the target).
- Nota Volt — extended LFOs: shape (sin/tri/sqr/S&H), depth, per-note fade-in and tempo sync
  (1/1…1/32); oscillator start phase (retrigger), a 12/24 dB filter slope choice, Mono/Poly
  mode, and an active-voice counter in the header.
### Changed
- Nota Volt was reworked to the new design (`design/`): a compact 700×260 card with a
  vertical tab rail (Osc · Filter · Env · LFO · Mod · Macro), an always-visible LIVE strip
  (Cutoff/Res/Glide plus Poly/Mono) and an output rail (Gain, peak meter, Pan) — matching
  Nota Grain.

## [0.3.0] — 2026-07-30
### Added
- Nota Auto Shift — a built-in real-time vocal pitch-correction effect: pitch detection,
  snapping to a key and scale (Key + Scale), correction amount and speed, a manual shift
  (±12 semitones) and a tuning meter with a scrolling ribbon of the deviation in cents. 6
  presets.
- Nota Auto Pan — a built-in modulation effect: auto-panning and tremolo (an L/R phase shift
  from 0° = tremolo to 180° = pan), 5 LFO shapes, Shape and Mix. 6 presets.
- Nota Vintage — a built-in degradation and saturation effect: 6 era modes (Vinyl, Cassette,
  Reel, VHS, Tube, Analog), wow/flutter, noise and crackle, and wear. 6 presets.

## [0.2.0] — 2026-07-30
### Added
- Semantic versioning for the app with a single source of truth (the `VERSION` file) and a
  changelog in Keep a Changelog format.
- A "What's New" window, showing the new changelog entries once after the first launch on an
  updated version.
- The About window now shows the app version separately from the engine version.

## [0.1.0] — 2026-07-29
### Added
- The first version of Nota: the engine (CoreAudio, C ABI), arrangement, session and mixer,
  the piano roll, the browser, and a set of built-in instruments and effects.
