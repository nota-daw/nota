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
- **Nota Pendulum's editor is now the almanac's card, and it shows the balls the engine is
  actually swinging.** The card moves onto the 700 × 260 frame the other instruments use — a
  **Balls** / **Voice** tab panel with a one-line reading beside the tabs, a fixed 186 px ball
  rail and a status strip:
  - **Balls** — Free / Sync, the division (or, in Free, the swing time), a bipolar Rate and
    the Motion curve; then **one lane per ball**: x is pitch, low to high, the chord's degrees
    as ticks, the wall the ball came from, the wall it strikes ("hit"), and in teal how long
    until it reaches the wall ahead. Under the lanes, the **bar**: the generated notes land on
    its 1/16 grid — this bar in brass, the last bar's notes ahead of the playhead in deep
    brass — so the lanes say why a note, the bar says when.
  - **Voice** — Keys / Glass / Saw / Sqr / Bell, Tone, Bright, FM and **Length** (the note
    gate, now on the card), the envelope and Volume, with the last generated note and a level
    bar per sounding voice.
  - **Rail** — every ball's division, rate, phase and note, always in view, the one that fired
    last lit. Under it: the ball count, Sort and Quantize, Hold / First note / Reset on Balls;
    the spread (rate, detune, pan and — new on the card — the **Humanize amount**), a
    **Scale** menu (root and mode in one place) and Humanize / Reset on Voice.
  The header shows the ball count. Everything comes from the engine's telemetry, so the lanes
  and the bar follow the audio thread (a Pendulum inside a rack chain swings from its
  parameters instead).
- **MCP: `read_pendulum` and `set_pendulum`.** Read a Nota Pendulum track — a summary, a guide
  to every parameter, the held chord, each ball's phase, position, direction, note and time to
  the wall, and the notes on the bar's grid — and shape it in musical terms: balls, rate in
  percent, division or free seconds, motion, quantize, sort, spread, hold, first note, wave and
  a scale like "A minor penta".
- **Nota Pendulum ships 28 factory presets** (was 6) — music boxes and bells, pentatonic and
  modal patterns (Pentatonic Rain, Dorian Drift, Mixolydian Sun), saw and square arps, FM
  glints, reverse and bouncing patterns, a held pad, and slow free-running swings.

- **Nota Chorus.** A new built-in audio effect (kind 25): modulated delay voices in three modes —
  **Classic** (two voices, left and right, on 8 ± 5 ms), **Ensemble** (three voices 120° apart on
  12 ± 6 ms, the string-machine shimmer) and **Vibrato** (the pitch wobble alone, wet only). The card
  follows the new mockup on the 700 × 260 frame:
  - **LFO.** Hz or Sync, the mode, the rate large with its period, a free rate from 0.02 to 8 Hz or a
    synced note value from 4/1 to 1/16 with triplets (locked to the bar while the transport plays),
    **Offset** — the right voice behind the left, 0–180° (Ensemble spreads its voices on its own) —
    and an **HPF** on the wet input (off, or 20 Hz – 2 kHz) that keeps the lows dry and centred.
  - **Voices.** Every voice over two LFO cycles — its delay (τ) or its detune in cents (ct) — with a
    running head; drag up or down for Amount, double-click resets it. Under it, the stereo field:
    where each voice sits and how far it is detuned right now.
  - **Voice.** Amount, a bipolar Feedback (±90 %), Width (0–200 %: mono to side +6 dB), Warmth (a
    darker, gently saturated bucket-brigade line) and Mix (locked to wet in Vibrato), with the largest
    detune and the delay range. The header names the state: 2 or 3 voices, wet only, or resonant
    past ±60 % feedback. Changing the mode fades instead of jumping the delay.
  - A status line says what you'll hear — the detune and delay range, or a warning when Mix is at
    zero or the feedback rings.
  All eleven controls are device parameters, so automation, MIDI learn, A/B compare and project save
  work as everywhere else. **34 factory presets**, from Wide Duo and Dark BBD to String Machine,
  Solina Strings, Tape Wobble and Rotary Throb.
- **MCP: `read_chorus` and `set_chorus`.** Read what a Nota Chorus is doing (the settings in units,
  each voice's delay and detune now, the sweep and the largest detune) and set it in units — mode,
  rate or division, offset degrees, HPF in Hz, amount, feedback, width, warmth and mix in percent.
- **Nota Phaser.** A new built-in audio effect (kind 24): a chain of all-pass stages whose
  corner an LFO sweeps, cutting moving notches into the spectrum — from a gentle 4-stage swirl to a
  deep, whistling 12-stage sweep. The card follows the new mockup on the 700 × 260 frame:
  - **LFO.** Sine, Tri or Saw; a free rate from 0.02 to 8 Hz or a synced note value from 4/1 to
    1/16 with dotted and triplet values, locked to the bar while the transport plays. The rate
    reads large with its period, and **Stereo** offsets the right channel's sweep by up to 180°.
  - **All-pass response.** The live frequency response of both channels, left in brass and right
    in teal, with the corner dashed and every notch ticked on the top edge. Drag the graph left or
    right to move Center, up or down for Feedback; double-click resets both. Under it, the corner
    frequency of both channels over two LFO cycles with a running head.
  - **Stages.** 2, 4, 6, 8 or 12 stages (half as many notches), Center (50 Hz – 5 kHz), Depth up
    to ±3 octaves, a bipolar Feedback (±95 %) and Mix, plus the range the corner sweeps with the
    first notch and how deep the notches cut. The loop is solved without a unit delay, so the
    sound matches the drawn curve; negative feedback flips the wet polarity, moving and widening
    the notches (one fewer). Changing the stage count fades instead of clicking. The header names
    the mode: positive, negative, resonant or clean.
  - A status line says what you'll hear — stages, notches and the corner's sweep in Hz and
    octaves, or a warning when the phaser whistles or Mix is at zero.
  All ten controls are device parameters, so automation, MIDI learn, A/B compare and project save
  work as everywhere else. **35 factory presets**, from Script 45 and Small Stone to Deep Space,
  Hollow Step, Whistler and Rotary Fast.
- **MCP: `read_phaser` and `set_phaser`.** Read what a Nota Phaser is doing (the settings in units,
  the live corner per channel, the notches and their sweep, the null depth and the peaks) and set
  it in units — stages, waveform, rate or division, stereo degrees, center in Hz, depth in
  octaves, feedback and mix in percent.
- **Nota Flanger.** A new built-in audio effect (kind 23): a comb filter on a short delay
  that an LFO sweeps up and down — the classic jet whoosh, a hollow tube tone, or a metallic
  ring. The card follows the new mockup on the 700 × 260 frame:
  - **LFO.** Sine, Tri or Saw; a free rate from 0.02 to 8 Hz or a synced note value from 2/1
    to 1/16 with dotted and triplet values, locked to the bar while the transport plays. The
    rate reads large with its period, and **Stereo** offsets the right channel's sweep by up
    to 180°.
  - **Comb response.** The live frequency response of both channels, left in brass and right
    in teal, with the first notch marked. Drag the graph left or right to move the notch
    (Delay), up or down for Feedback; double-click resets both. Under it, the delay of both
    channels over two LFO cycles with a running head.
  - **Delay line.** Delay (0.1–8 ms), Depth, a bipolar Feedback (±95 %) with − / 0 / + snaps,
    and Mix, plus the range the first notch sweeps and how deep the notches cut. Negative
    feedback also flips the wet signal's polarity, so the comb mirrors instead of flattening
    out. The header names the mode: positive, negative, resonant or through.
  - A status line says what you'll hear — the notch sweep in Hz and the delay range, or a
    warning when the comb rings or Mix is at zero.
  All nine controls are device parameters, so automation, MIDI learn, A/B compare and project
  save work as everywhere else. **31 factory presets**, from Jet Plane and Hollow Bar to Tin
  Robot, Laser Zap and Vibrato.
- **MCP: `read_flanger` and `set_flanger`.** Read what a Nota Flanger is doing (the settings in
  units, the live delay per channel, the notch and its sweep, the null depth and the peaks)
  and set it in units — waveform, rate or division, stereo degrees, delay in ms, depth,
  feedback and mix in percent.
- **MCP: `read_physical`.** Ask a Nota Physical track what it is: a one-line summary of the
  patch, a guide to every parameter's values, the sounding voices, the last struck pitch, the
  output peak and both resonators' 16 partials as the engine tunes them (Hz, level, ring
  time, whether each one sounds).
- **Nota Lens — analyzer and oscilloscope.** A new built-in audio effect (kind 22) that shows
  you what a track is actually doing. Drop it anywhere in a chain; it passes the audio through
  untouched and adds no latency. One card, three views behind tabs:
  - **Spectrum** — FFT of 512 / 2048 / 4096 / 16384 points with a Hann, Blackman-Harris or
    flat-top window, frame averaging, fractional-octave smoothing, adjustable fall time, a
    pink-noise tilt so a balanced mix reads flat, and a peak-hold trace with its own hold
    time. Move the pointer over the curve to read a frequency, its note and cents, and its
    level. A 0.25 sine reads its own −12.0 dBFS.
  - **Scope** — a triggered oscilloscope that behaves like a bench instrument: the trace is
    pinned to the trigger point and stands still, older passes fade out behind it. Auto /
    Normal / Single triggering on a rising or falling edge, with a level you drag on the
    trace and a holdoff. Time/div from 20 µs to 20 ms, volt/div from 0.02 to 2. A/B cursors
    you drag for Δt, 1/Δt, ΔV and the matching note, and **Export WAV** to write the captured
    window to disk.
  - **Waterfall** — a spectrogram over the last 4, 12 or 60 seconds, with gain, floor,
    contrast and FFT overlap.
  Plus a permanent SCALE rail (dB top and range, or volt/div and time/div), a live status
  line (peak, crest, momentary LUFS, L/R correlation), Freeze, an optional note grid, a
  linear or logarithmic frequency axis, and an L+R / L / R source that can read Mid/Side
  instead. All 37 controls are device parameters, so automation, MIDI learn, A/B compare and
  project save work as they do everywhere else. **32 factory presets** across the three views.
- **MCP: `read_analyzer`.** Ask for what a Nota Lens is measuring and get it back parsed —
  the summary line, the scope measurements, the A/B cursor readings, 31 third-octave band
  levels and the strongest spectral peaks with note names. `get_device_text` on a Lens
  returns the same reports as text.
- **MCP: sidechain routing and `read_dynamics`.** `set_device_sidechain` / `get_device_sidechain`
  route a key into a Compressor (or any device that takes one) — source track, pre/post tap,
  detector gain — so an assistant can set up ducking on its own. `read_dynamics` returns
  what a Nota Compressor is doing: gain reduction, input / output peak and RMS, the key
  level, the effective attack and release, look-ahead latency and how often it kicked in.

### Changed
- **Nota Pendulum's automation menu is grouped.** Its parameters list under **Balls**,
  **Motion**, **Voice**, **Spread** and **Scale** (e.g. Motion › Rate, Spread › Humanize); the
  ids are unchanged, so existing lanes and projects keep working.
- **Nota Level is redrawn as a full leveler.** The card follows the new mockup on the 700 × 260
  frame, in the language of EQ-8 and Forge:
  - **Correction.** Manual / Auto sit over a fader around 0 dB. Auto rides it in teal. In
    Manual you drag it, and switching to Manual starts from the gain Auto was riding. The gain
    applied shows in large type, with the reason when something holds it (max gain, max cut,
    true-peak, silence). **MATCH** (Manual) sets the fader to the distance to the target.
    **RESET** (Auto) restarts the measurement from the loudness now, so the ride catches up at
    once. Target and Trim sit under it. Click the Target value for the standards (−9 … −27
    LUFS: streaming, podcast, EBU R128, ATSC A/85 …) or to match another track's loudness.
  - **Loudness.** Mom / Short / Integ over the last 8 s: the input, the output and the target
    with its ±1 LU band. A dot marks the output now. Drag the line to move the target. Under
    it, IN · OUT · Δ · TP.
  - **Meters and response.** IN and OUT against the target, TP against the ceiling, and the
    correction. Fast / Slow, Window and Max Gain. **True-peak safe** with its ceiling in dBTP
    (drag it). A status strip says what the leveler is doing and shows the sample rate, scale,
    look-ahead and correlation.
  - **Scales.** Momentary 400 ms, Short-term 3 s, Integrated 12 s with BS.1770 gating.
    **Window** is now the glide time: Slow glides over all of it, Fast over a quarter.
  - **Silence holds.** Before the first signal, and while the input is too quiet to level
    (more than 20 LU under its own 12 s level, more than Max Gain + 12 LU under the target, or
    under −70 LUFS), the measurement and the gain hold. Pauses and release tails no longer get
    pumped up into noise.
  - **Look-ahead limiter.** True-peak safe is now a look-ahead limiter (5 ms on Fast, 20 ms on
    Slow) that never lets a true peak through above the ceiling. Its latency is reported, so
    delay compensation keeps the track in time.
  - **Compatibility.** The manual **Gain** is appended (0 dB by default), so projects, racks
    and presets saved before this open. Older projects that had Auto off held the correction
    they had reached. They now apply the manual Gain (0 dB), so press MATCH once.
  - **30 factory presets**, up from six: delivery targets (EBU R128, ATSC A/85, Apple Music,
    YouTube, Audiobook ACX, Cinema Dialogue, Game Audio …), speech riders, music and mixing
    (Gentle Rider, Gain Staging −18, Mix Bus −20, Music Bed −28 …) and static ones (True-Peak
    Guard, Static Boost +6). The six earlier presets keep their names.
  - **MCP: `read_level`, `set_level`, `level_match`.** `read_level` reports the mode and state,
    the target (or the reference), the loudness in and out on every scale, the distance to the
    target, the correction and what holds it, the limiter, true peak and latency. `set_level`
    edits Level in LUFS, dB and seconds and routes a reference. `level_match` is MATCH.
    `get_device_text` gives a summary, a live reading and a parameter guide. `device_action` 0
    is RESET and 1 is MATCH. Every parameter automates, MIDI-learns and saves in a preset.
- **Nota Forge is redrawn, and every stage gets its own shape.** The card follows the new
  mockup on the 700 × 260 frame, in the language of EQ-8:
  - **Stages.** On the left the routing (Serial / Parallel / M/S / Multi) sits over the three
    stages. Each stage shows its on dot, its type and the role it plays: in **M/S** stage 1
    drives the Mid, stage 2 the Side and stage 3 the recombined signal; in **Multi** stage 1
    takes the lows (below 180 Hz), stage 2 the mids and stage 3 the highs (above 2.4 kHz).
    Drag a stage's output dB up or down to trim it. Drive and a teal Feedback sit under it.
    Click a stage to select it.
  - **Shape per stage.** The selected stage gets its type (Tube, Tape, Diode, Fuzz, Fold,
    Digital) and three knobs: **Bias** (asymmetry, ±100 %), **Tone** (a ±12 dB tilt after the
    stage) and **Width** (the stage's stereo image, 0 … 200 %). Before, Bias, Tone and Width
    were shared by all stages.
  - **Transfer and harmonics.** Amount, Wet and Out sit over the transfer curve. In M/S and
    Multi the curve shows the selected stage. Every stage also shows faintly on its own, the
    LFO-modulated curve is dashed teal, and a dot marks where the input sits now. Drag the
    curve for Amount. Under it, harmonics 2 … 9 of a −6 dB sine show odd partials in brass,
    even ones in ink, and the THD with its character (odd-heavy, even-heavy, mixed). The
    curves and harmonics come from the engine's own shapers, so they match what you hear.
  - **Modulation and quality.** LFO → Drive, Env → Tone and Rate, with a Sync switch. Sync
    offers eight divisions from 2 bars to 1/64. Oversampling Off / 2× / 4× / 8×. A status strip
    warns when Digital or Fold runs without oversampling. It also shows the sample rate,
    oversampling, latency and CPU.
  - **Compatibility.** The per-stage Bias / Tone / Width are appended and default to neutral.
    The old shared Bias, Tone and Width stay as master offsets, so projects, racks and presets
    saved before this sound the same. When a master offset is set, the status strip says so.
    Mid/Side and Multiband now follow the stage roles above (Multiband crossovers moved from
    200 Hz / 2 kHz to 180 Hz / 2.4 kHz), so older projects that use these two routings sound
    somewhat different.
  - **32 factory presets**, up from six: warmth, drive, destruction, parallel, mid/side,
    multiband and moving presets (Drum Glue, Warm Master, Wide Air, Low-End Only, Breathing
    Drive …). The six earlier presets keep their names.
  - **Automation** lists the stage params under Stage 1 / 2 / 3 and the LFO in its own group.
  - **MCP: `read_forge`, `set_forge`, `set_forge_stage`.** `read_forge` reports the routing,
    each stage with its role, THD and harmonics, the curve, the modulation, the meters and
    the aliasing warning. `set_forge` and `set_forge_stage` edit Forge in dB, % and names.
    `get_device_text` gives a summary, a live reading and a parameter guide, and
    `device_action` 0 restarts the LFO and resets the meters.
- **Nota Orbit is redrawn, and its LFO can follow the tempo.** The card follows the new mockup on
  the 700 × 260 frame, laid out like Level and EQ-8:
  - **LFO.** Pick the waveform and switch between **Hz** and **Sync**. The rate shows as a large
    readout with its period in ms. Rate and Shape sit under it.
  - **Sync.** The rate becomes a note value from 4/1 to 1/32, with dotted and triplet values.
    While the transport plays, the LFO locks to the song position and restarts on the bar (a
    2/1 or 4/1 cycle restarts every 2 or 4 bars). When the transport stops, it keeps running
    at the synced rate.
  - **Glide on S&H.** On S&H, Shape becomes **Glide**, a slide from one random step to the
    next. The right channel now plays the same steps as the left, one Phase later. At 0° the
    steps move both channels together, as a random tremolo; at 180° they pan.
  - **Gain graph.** Both channels' gain over two cycles (L brass, R teal), with the floor the
    depth reaches in dB and a running head with the gain dots. Drag up / down for Amount and
    left / right for Phase. Double-click resets both. Under the graph, a line shows where the
    sound sits between left and right, and the swing it covers.
  - **Motion.** Amount, Phase and Mix; **0° / 90° / 180°** buttons for the phase; the mode the
    phase makes (tremolo, auto-pan or offset pan); and OUT L / OUT R meters with each
    channel's gain. A status strip explains what you hear and shows the sample rate, tempo and
    whether the LFO is free or synced.
  - The channel gains now glide over about 1 ms, so Square and a jump in the transport no
    longer click.
  - **Compatibility.** Sync and Division are appended and default to Hz, so projects, racks
    and presets saved before this open unchanged.
  - **31 factory presets**, up from six: free and synced pans, tremolos (Chop Trem, Vintage Amp
    Trem, Sidechain Pump, Helicopter), random steps and glides, and subtle motion. The six
    earlier presets keep their names.
  - **MCP: `read_orbit`.** It reports the settings, the rate and period, the mode, each
    channel's gain now, the pan and its swing, whether the LFO is locked to the song, the
    tempo and the levels. `get_device_text` gives a summary, a live reading and a parameter
    guide with the Division table. `device_action` 0 restarts the LFO and 1 resets the
    meters. Every parameter automates, MIDI-learns and saves in a preset.
- **Nota EQ-8 is redrawn with slopes, mid/side and global controls.** The card follows the new
  mockup on the 700 × 260 frame, laid out like Dynamic EQ-8:
  - **Band chips and graph.** A row of eight chips (number, type, channel, frequency) sits over
    the response graph. Drag a node for frequency and gain, use the wheel for Q. Double-click a
    node to switch its band on or off; double-click empty space to switch on a free band there.
    Right-click a node for its type, slope, channel and on.
  - **Slope.** Low cut and high cut now go 12, 24 or 48 dB/oct. The steeper slopes are
    Butterworth, and Q still sets the resonance.
  - **Channel.** Each band works on the stereo signal, the Mid, the Side, or the left or right
    channel alone. When any band works on the Side, the graph draws the Side curve in teal
    next to the Mid.
  - **Global.** Scale multiplies every shelf and bell gain (0 … 200 %). Output is ±12 dB.
    Auto gain takes back the average lift of the shelves and bells, so you hear edits at the
    same loudness.
  - **Analyzer.** The spectra now come from the engine. Pre shows the input; Post shows the
    output over a dimmer input; Off hides both.
  - **Selected band panel.** Type, FREQ / GAIN / Q (RESO on the cuts), SLOPE, CHANNEL and the
    GLOBAL controls. A status strip shows the summary, the output with the auto gain in
    effect, the sample rate and CPU.
  - **Compatibility.** Projects, racks and presets saved before this open unchanged: the new
    params default to 12 dB/oct, stereo, 100 %, 0 dB and auto gain off.
  - **34 factory presets.** They cover mix, vocal, drums, instruments, mid/side, left/right
    and repair (hum removal). The four earlier presets keep their names.
  - **MCP: `read_eq8`, `set_eq8_band` and `set_eq8`.** `read_eq8` reports the settings, every
    band, the in / out peaks and both spectra. `set_eq8_band` edits a band, and `set_eq8` sets
    Scale, Output, Auto gain and the analyzer. Both record like hand edits.
    `get_device_text` gives a summary, a live reading and a parameter guide, and
    `device_action` 0 resets the meters. In the automation menu, the params are grouped by
    band.
- **Nota EQ-3 is redrawn as a DJ isolator.** The card follows the new mockup on the
  700 × 260 frame. The crossover slider row is gone; you now drag the crossovers on the graph:
  - **Band strips.** LOW, MID and HIGH each get a strip in their own colour with the band's
    frequency span, a fader from −24 to +6 dB, the value and a KILL. The fader moves by
    relative drag in 0.5 dB steps with a 0 dB detent; hold Shift for 0.1 dB steps.
    Double-click returns it to 0 dB and lifts the kill.
  - **Graph.** Each band's own curve is drawn thin in its colour, the sum in brass, and the
    output spectrum (now from the engine's FFT) behind them. The two crossovers are dashed
    lines with handles along the top. Drag a handle left or right; the low/mid crossover stays
    at most half the mid/high one. Drag anywhere else to ride the gain of the band under the
    pointer. Double-click a handle or a band to reset it.
  - **Over the graph.** The crossover readouts, Output (drag, double-click resets), Range and
    Slope (24 / 48 dB). A status strip closes the card: slope, crossovers, kills, range,
    output, then sample rate, latency and CPU.
  - **Range** is a new parameter. It picks the fader throw: +6 (−24 … +6 dB, the default for
    a new EQ-3) or ±15 (the classic range). Switching it keeps each band's dB. Projects,
    racks and presets saved before it open in the ±15 range and sound exactly as before.
  - **30 factory presets**: DJ kills, bass swaps and isolations, scoops and smiles, clean-up
    curves, and character presets (telephone, AM radio, megaphone). The six earlier presets
    keep their names.
  - **MCP: `read_eq3` and `set_eq3`.** `read_eq3` reports the settings, each band's gain,
    kill, level and gain now, the in / out peaks and the output spectrum. `set_eq3` plays the
    EQ in dB and Hz in one call, recorded like a hand edit. `get_device_text` gives a summary,
    a live reading and a parameter guide, and `device_action` 0 resets the meters.
- **Nota EQ-3 now sums flat.** The 48 dB/oct slope is now a true Linkwitz-Riley 8.
  Before, it left a 6 dB dip at each crossover. The low band is also phase-aligned with the
  mid/high split, so with every band at 0 dB the output matches the input at any crossover
  setting and either slope.
- **Nota Dynamic EQ-8 is redrawn around its graph, and each band gets its own key.** The card
  follows the new mockup on the 700 × 260 frame: the response graph takes almost the whole
  width, a row of eight band chips sits over it, the selected band is edited in a panel on the
  right, and a status strip closes the card:
  - **Graph** — the live response in brass, the static one as a teal dashed line while they
    differ, the output spectrum under them (from the engine's FFT) and the selected band's own
    curve. Every band is a numbered node, off bands included; a dynamic band grows a whisker
    to where its range can take it, with a dot riding it at the gain it has now. A tooltip by
    the selected node reads the band and, for a dynamic band, its gain now with a 2-second
    history. Drag a node for frequency and gain, the wheel over it for Q, double-click it to
    switch the band on or off, double-click empty space for a new bell there; right-click
    keeps the type / mode / on / solo menu.
  - **Band chips** — number, type and frequency of all eight bands, with a bar showing how
    much of its range each dynamic band uses right now. Click to select, double-click to
    switch a band on or off.
  - **Band panel** — On, the type (HP · LS · Bell · Notch · HS · LP), FREQ / GAIN / Q knobs (Q
    reads RESO on the cuts), the dynamics mode (Static · Duck · Lift), THRESH with the band's
    level marked on it, RANGE, ATTACK, RELEASE, the key and Solo. Range is set by magnitude;
    Duck starts as a cut and Lift as a boost, and the arrow by the value flips it (upward /
    downward expansion).
  - New **Dynamic** master switch: off parks every band on its static gain, to hear what the
    dynamics do. New per-band **Key**: Self, or Ext — the key track (click Ext to pick it). The
    old all-bands Sidechain switch still works and hands over to the per-band keys when a
    band is set back to Self.
  - The **Output** gain now has a control: drag the OUT readout in the status strip.
  - **33 factory presets**, up from six — vocal (de-essers, tame, presence, proximity, air,
    podcast), mix and master (bus air, glue, mud, harshness, loudness contour), drums, bass,
    keyed ducking (kick ducks bass, vocal pocket, voice-over space), instruments, expansion
    and a static cleanup. The new parameters are appended and default to the old sound, so
    older projects and presets open unchanged; the automation menu lists the parameters
    under Band 1 … Band 8. `get_device_text` returns the status line, the live reading and a
    guide to the parameters, the new `read_dynamic_eq` returns every band with its level and
    gain now plus the spectrum, and `set_dynamic_eq_band` edits a band in one call.
- **Nota Crush shows its steps, its spectrum and its transfer curve, and gains Auto gain and a
  DC filter.** The card moves onto the 700 × 260 frame of Ceiling and Beat Repeat — a CRUSH
  column (input and output), a centre panel with **Quantiser**, **Spectrum** and **Transfer**
  tabs, a **Grit** / **Output** panel and a status strip. The slider row over the graph
  becomes Bits / Rate / Drive / Wet knobs under it, Mode a switch over it, Anti-alias moves
  next to it:
  - **Quantiser** — 10 ms of a sine held and quantised to the real hold length, the steps
    over the level grid (enlarged above 4 bits, and it says so). Drag up / down for Bits,
    left / right for Rate. Shows the rate against the project's, one sample of how many is
    kept, and the quantisation noise.
  - **Spectrum** — the output in 40 bands from an FFT in the engine: brass where it is the
    signal, teal where the crush added something — bright above the reduced Nyquist (the
    images); a dashed outline where the post filter took signal away. Drag the Nyquist line
    for Rate and the filter line for the post filter. Shows the images above the Nyquist and
    THD+N.
  - **Transfer** — the input → output curve through Drive, the mode and the real level count,
    with a dot where the signal peaks, next to the form it makes of a sine. Drag for Drive.
    Shows how many times Fold folds the signal.
  - **Grit** holds Dither, Jitter, Noise, the post filter and the gain, with a Result box —
    bits and rate, and whether the filter cuts the images. **Output** measures in, out and
    the crest factor, and holds the new switches.
  - New **Auto gain**: rides the output back to the input's loudness (300 ms RMS, ±24 dB), so
    Bits and Drive stop jumping in level. New **DC filter**: a 10 Hz high-pass on the crushed
    signal.
  - Rate is now a fractional sample and hold, so it sweeps smoothly and automation doesn't
    zipper. Anti-alias is a 4th-order
    Butterworth low-pass instead of a one-pole, so far less folds back into the audible range.
  - **33 factory presets**, up from six — classic lo-fi (tape, sampler, SP-1200, telephone,
    AM, walkie-talkie), retro machines (8-bit, 4-bit, NES, 16-bit console, Game Boy, Speak &
    Spell), subtle texture for buses and hats, destruction (1-bit, aliasing, broken clock,
    bitrot) and folds. The two new parameters are appended and default off, so older projects
    and presets open unchanged. Every parameter automates, MIDI-learns, saves in a preset and
    is reachable over MCP. `get_device_text` returns the status line, the live reading and a
    guide to the parameter values, `device_action` 0 resets the meters, and the new
    `read_crush` returns the settings, the meters, THD+N, the images, the folds, the
    auto-gain correction and the spectrum.
- **Nota Ceiling shows its level, its reduction and its loudness, and gains True Peak, Delta, a
  key high-pass and a loudness target.** The card moves onto the 700 × 260 frame of Beat Repeat
  and Shutter — a LIMIT column (input with the ceiling, reduction, output), a centre panel with
  **Level**, **Reduction** and **Loudness** tabs, a **Meters** / **Detector** panel and a status
  strip. The slider row over the graph becomes Gain / Ceiling / Release knobs under it,
  Character a switch over it, Look-ahead and Link move to the right panel:
  - **Level** — the last 4 s: the input, the part over the ceiling in red and the output, with
    the reduction under it; drag the ceiling line. Shows how far in went to out and how much
    of the time the input was over the ceiling.
  - **Reduction** — the reduction over 4 s, scaled to its depth, with its mean; teal where a
    transient got through to the clip (Punch lets the attack by). Shows the release the auto
    stage is actually using and how many transients reached the clip.
  - **Loudness** — momentary, short-term and integrated LUFS over the last 60 s around a
    **Target** (−23 broadcast … −8 very loud; drag the line for any value), with the loudness
    range (LRA) and PLR. Meters shows the integrated reading against the target.
  - New **True peak**: the detector reads the 4× inter-sample peak, so the ceiling holds in
    dBTP. New **Delta**: hear only what the limiter removes. New **SC HP**: take the lows out
    of the detector so the bass stops pumping the mix. The key source picker stays.
  - Auto release now slows smoothly as the reduction deepens (×1 at 1 dB up to ×5 at 7 dB)
    instead of jumping at 2 dB. Peak holds, max GR and the clip count live in the engine;
    **Reset peaks** clears them, **Reset** on the Loudness tab starts the loudness again.
  - **32 factory presets**, up from six — mastering for streaming, Apple Music, podcast,
    broadcast R128, club and CD, bus and drum limiting, track peak catchers, zero-latency live
    use and squash effects. The four new parameters are appended and default to the old sound,
    so older projects and presets open unchanged. Every parameter automates, MIDI-learns,
    saves in a preset and is reachable over MCP. `get_device_text` returns the status line, the
    live reading and a guide to the parameter values, `device_action` 0 resets the peaks and 1
    the loudness, and the new `read_ceiling` returns the reduction, the peaks and their holds,
    true peak, LUFS M / S / I with the distance to the target, LRA, PLR, the release in use,
    the clip count and both windows.
- **Nota Beat Repeat shows what it captures and repeats, and gains a triplet grid, a filter
  type and a Repeat you can latch.** The card moves onto the 700 × 260 frame of Shutter and
  Auto Shift — a REPEAT column (the input and the repeats' level, the repeat count), a centre
  panel with **Timeline**, **Slices** and **Filter** tabs, a **Character** / **Output** panel
  and a status strip. Interval and Grid move out of the button grid into the row over the
  graph; Chance, Gate, Offset and Variation become knobs under it:
  - **Timeline** — two intervals on the grid, each step a bar of the input level: dry steps
    dim, the captured slice in full brass, every repeat a step dimmer as it decays, with the
    trigger points, bar numbers and the playhead.
  - **Slices** — the last interval's sound with the gate shaded from the offset and the
    captured slice in brass; drag the offset line or the gate's end. New **Triplet** switch
    turns any grid into triplets (the old 1/8T and 1/16T settings still load as they were).
  - **Filter** — the repeat filter's curve, now **LP**, **BP** or **HP**, with Width in real
    octaves; drag the node for the frequency and a band edge for the width. New **Narrow with
    repeats**: each repeat narrows the band and follows the pitch down.
  - **Character / Output** — pitch, pitch decay, decay and volume; a state box (waiting,
    capture, repeat n of N, held); in / repeat / out meters, Mix, **Repeat** and **Reset**.
    New **Latch** makes the Repeat button stay on after a click. Repeat now holds the slice
    it caught until you let go, instead of catching a new one every gate.
  - Bars follow the time signature (1 bar in 3/4 is three beats). Variation now lets the grid
    float up to six doubling / halving steps per trigger. Slice edges are faded so repeats
    don't click.
  - **31 factory presets**, up from six — stutters, glitch, pitch drops (Tape Drop, Dive
    Bomb), filtered repeats, drum and vocal chops, and performance presets for the Repeat
    button. The four new parameters are appended and default to the old sound, so older
    projects open unchanged. Parameter 15 is renamed from *Latch* to **Repeat** (it always
    meant "repeat now"), so a user preset saved with the old *Latch* on now turns on the new
    Latch switch instead. Every parameter automates (the filter's grouped under *Filter* in
    the lane menu), MIDI-learns, saves in a preset and is reachable over MCP.
    `get_device_text` returns the status line, the live reading and a guide to the parameter
    values, `device_action` 0 resets and 1 fires a repeat now, and the new `read_beat_repeat`
    returns the state, the pass, the slice, the repeat's gain / pitch / filter, the levels,
    bar and beat, and the timeline.
- **Nota Auto Shift is a full vocal tuner: it keeps the voice's character, learns the key and
  can follow a MIDI part.** The card moves onto the 700 × 260 frame of Shutter, Utility and
  Valve — a PITCH column (where the voice sits and the correction now), a centre panel with
  **Trace**, **Scale** and **MIDI** tabs, a **Shift** / **Detect** panel and a status strip:
  - **Trace** — the sung pitch and the corrected one over the last two seconds on the
    scale's note lanes, the target lit; key, scale, Auto / Manual / MIDI and Learn above it;
    Amount, Speed, Range and the new **Human**, which keeps vibrato and drift around the
    corrected note instead of flattening them.
  - **Scale** — how long each note was sung (the last ~16 bars) under the scale's notes, and
    twelve note buttons: click one to add or take it out, which makes a **Custom** scale. The
    scale list adds Harmonic and Melodic Minor, Dorian, Phrygian, Lydian, Mixolydian, Blues and
    Whole Tone. **Learn** listens, then sets the key and scale that fit what was sung; **Auto**
    follows it as you go (both match Krumhansl key profiles, with the runner-up shown).
  - **MIDI** — pick an instrument track and the voice is pulled to its notes: the last held
    note or the held notes as a scale, Latch between notes, Oct lock, and **Pitch bend from
    MIDI** with Glide and Bend (steps within the bend range glide, wider leaps jump). The
    guide track can stay muted.
  - **Shift** — Shift, the new **Fine** (±100 ¢), the new **Formant** shift and Mix, the note
    being sung with its frequency and clarity, **Preserve formants**. **Detect** — a voice-type
    source list, the detection range (Low / High), Sensitivity, the Learn result and **Skip
    sibilants** (s, sh and breaths pass unshifted instead of turning metallic).
  - The shifter is new: pitch-synchronous grains taken a whole period apart, so with Preserve
    formants the vowel stays where it was while the pitch moves (a +12 st shift no longer
    sounds like a chipmunk), Formant moves the vowel on its own, the shifted voice keeps its
    level, and with nothing to correct the sound passes through unchanged. The pitch tracker runs on an FFT and only looks inside
    the detection range, so low voices are found and CPU stays low.
  - **34 factory presets**, up from six — correction styles (hard tune, robot, natural, pop,
    R&B, ballad, rap, choir, auto key), scales (blues, dorian, pentatonic …), voice types,
    creative shifts (octaves, chipmunk, monster, gender up / down, doubler, fifth harmony) and
    MIDI targets (harmony lock, melody replace). The 26 new parameters are appended and default
    to the old behaviour, so older projects open unchanged; every one of them automates (the
    scale notes, the detector and the MIDI target are grouped in the lane menu), MIDI-learns,
    saves in a preset and is reachable over MCP. `get_device_text` returns the status line,
    the live reading and a guide to the parameter values, `device_action` 0 resets the
    analysis and 1 runs Learn, `set_device_sidechain` picks the MIDI source, and the new
    `read_auto_shift` returns the pitch, target, correction, scale, sung notes, key guesses,
    the MIDI note and the last two seconds of pitch.
- **Nota Shutter shows the gate working, and gains a shape, trigger mode and a real key
  switch.** The card moves onto the 700 × 260 frame of Utility, Valve and Vintage — a STATE
  column (the input, or the key when a sidechain drives it, with the threshold tick, and the
  reduction), a centre panel with **Signal**, **Envelope** and **Sidechain** tabs, a
  **Detector** / **Meters** panel and a status strip. The threshold and return no longer sit
  in a strip above everything — they are lines on the graph where the gate acts:
  - **Signal** — the input and what passes over the last 250 ms, 1 s or 4 s (click the
    window in the tab bar), with the threshold and the close level to drag up and down, Gate /
    Duck, Lookahead, **Live** to freeze the picture, and how much of the window was open.
  - **Envelope** — one opening drawn from attack, hold, release, shape and floor (upside
    down for Duck); drag the nodes for the times and the floor for the range. New **Shape**:
    Linear ramps, Log (the classic one-pole) or Snap (stays open, then shuts hard). New
    **Retrig**: trigger mode, where each hit fires one attack → hold → release however long
    the sound lasts — for gated reverbs and chopped pads. Openings per bar are counted.
  - **Sidechain** — the key source list, the detector's band-pass (now 12 dB/oct) with its
    HP / LP nodes over the reduction, Listen, and **SC EQ** to switch the band-pass out.
  - **Detector / Meters** — source, threshold, return, floor and a state box (closed, attack,
    open, hold, release with the gain); in / out / GR with the peak, SC HP / LP, range,
    Listen and **Reset**. New switches: **External sidechain** (keep a routed key but listen
    to the track itself) and **Peak hold** (the detector holds peaks, so low notes don't
    chatter the gate).
  - **30 factory presets**, up from six — drums (kick, snare, toms, hats, overheads, room,
    gated reverb, drum trigger), voice (podcast, breaths, dialogue), guitar and bass, pads,
    rhythmic chops and ducking (sidechain pump, bass under kick, voice-over). The five new
    parameters are appended and default to the old behaviour, so older projects open
    unchanged; every one of them automates (the detector's grouped under *Det* in the lane
    menu), MIDI-learns, saves in a preset and is reachable over MCP. `get_device_text`
    returns the status line, the live reading and a guide to the parameter values,
    `device_action` 0 resets the meters and 1 sets the window, and the new `read_shutter`
    returns the state, the gain and reduction, the levels, the open share, the openings per
    bar and the window's history.
- **Nota Physical's editor is now the almanac's card, with a voice mode, a resonator mix and
  the partials it actually rings.** The card moves onto the 700 × 260 frame the other
  instruments use — an **Exciter** / **Resonator** tab panel with a one-line reading of the
  chain beside the tabs, a fixed **Output** rail and a status strip:
  - **Exciter** — the mallet's four knobs › the noise burst: LP / BP / HP, a draggable ADSR
    with its stage times written under it, and level, envelope → filter (in octaves), freq
    and reso.
  - **Resonator** — pick bank 1 or 2, switch bank 2 on, choose its material and the
    structure. The partial window now comes from the engine itself: every partial where the
    last struck note puts it, how loud it is struck and how long it rings, the ones past
    Nyquist left out, the other bank drawn faintly behind, and a count of what sounds. Drag
    it sideways to spread the series (Ratio) and up / down to tilt the highs (Bright). Knobs
    read in real units — decay in seconds, tune in semitones, ratio as its exponent.
  - **Output** — **Poly / Mono**, tune (semitones), fine (cents), note-off, volume, pan and
    the track's meter. The header shows the sounding voices (x/8) or MONO.
- **Nota Physical: Mono and Res Mix.** Two new parameters: **Mono** plays one note at a time
  — a new strike chokes the sounding one with a 4 ms fade, so repeated hits don't smear —
  and **Res Mix** balances resonator 1 against resonator 2 (both at full level in the
  middle, which is how every project saved before sounds). Both automate, save and clone.
- **Nota Physical ships 32 factory presets** (was 6) — mallets (xylophone, glockenspiel,
  bass marimba, soft vibes, balafon), bells (church bell, music box, crystal chime, gamelan,
  singing bowl, wind chimes), percussion (hand drum, steel drum, log drum, cowbell, clave,
  kalimba), plucked, blown and bowed (harp, koto, tine keys, pan flute, blown bottle, bowed
  glass) and textures (metal plate, mono kalimba, sub thump).
- **Instrument automation menus flatten single entries.** A built-in instrument's parameter
  whose name shares its first word with no other one is listed under its full name
  ("Note Off"), not as a one-item submenu.

- **Nota Utility shows where the stereo field and the level go, not just its knobs.** The card
  moves onto the Valve / Vintage / Auto Filter frame — a LEVEL column (input and output meters
  with their level), a centre panel with **Field**, **Mono** and **Levels** tabs, a
  **Routing / Output** panel and a status strip. The goniometer gives way to pictures that say
  which part of the signal does what:
  - **Field** — the output's stereo field by frequency over the last second, lows at the bottom:
    where each band sits between L and R, how far it spreads, and how far the width setting
    spreads an uncorrelated band. Drag sideways for the balance, up and down for the width.
  - **Mono** — the width over frequency as the settings make it, the mono region shaded, against
    the output's measured width. Drag sideways for the mono cutoff, up and down for the width.
  - **Levels** — input and output over the last 8 s in LUFS-S, sample peak or RMS, with the
    target, the delta and the true peak. Drag up and down for the target.
  - **New in the engine:** an **M/S width law** (mid and side are traded, so widening does not get
    louder; 200 % = side only), a **mono-below slope** of 6, 12 or 24 dB/oct with 60 / 120 / 240 Hz
    quick picks, **level matching** — a one-shot **Gain match** and a continuous **Level match**,
    to the input's level or to a **Target**, in the meter's unit, measured before Gain so a
    second press does not chase the first — a zero-latency **true-peak limiter** with its
    ceiling, and LUFS-S, peak, RMS and true-peak metering of both sides. Gain, width and balance
    are now smoothed, so automating them no longer clicks.
  - **30 factory presets**, up from six — width, bass mono, routing and level targets (streaming
    −14, podcast −16, broadcast −23 LUFS …); the six old ones keep their names. The first nine
    parameters keep their order, units and defaults and the eight new ones are appended and
    default to the old sound, so older projects open unchanged. Every one of them automates
    (mono, phase and true-peak params grouped in the lane menu), MIDI-learns, saves in a preset
    and is reachable over MCP. `get_device_text` returns the status line, the live reading and a
    guide to the parameter values, `device_action` 0 gain-matches and 1 resets the meters, and
    the new `read_utility` returns the levels in every unit, the delta, true peak, correlation,
    width, energy balance, the auto-match gain, the limiter's reduction and eight bands of the
    stereo field.
- **Nota Valve shows the amp it runs, not just its knobs.** The card moves onto the Vintage /
  Auto Filter / Lens frame — a STAGE column (gain and output faders), a centre panel with
  **Amp**, **Cab** and **Harmonics** tabs, a **Cabinet / Output** panel and a status strip.
  The seven models are now a list in the tab's head row instead of a row of buttons, and the
  cabinet and mic get a tab of their own with a real response instead of chips with no
  feedback:
  - **Amp** — the tone stack's response (read from the engine, so it is the real one) against
    the stack flat; drag sideways for the middle's frequency, up and down for the middle.
  - **Cab** — the cabinet as the mic hears it against the same mic on axis at the cap, and how
    much the placement costs at 4 kHz; drag sideways to move the mic off axis, up to bring it
    closer.
  - **Harmonics** — the harmonics the preamp adds at the input's level (f … 7f), the THD and an
    estimate of the aliasing at the current oversampling.
  - **New in the engine:** a **sweepable middle** (200 Hz … 2 kHz), **Bright** and **Deep**
    switches, an **even-harmonics-only** preamp, cabinets with their own **low resonance and
    presence peak**, **mic distance** (proximity bass up close, thinner far away) and **cap /
    edge position**, a stronger **off-axis** roll-off, **low and high cuts** and **auto gain
    compensation**. The panel's gate switch turns the gate on at its last threshold.
  - **31 factory presets**, up from six, level-matched to the dry signal — cleans, blues,
    rock, leads, heavy, bass and a few for keys, vocals and drums. The first fourteen
    parameters keep their units and the nine new ones are appended and default to the old
    sound, so older projects open with the same settings (a project that used one of the
    four fixed cabinets or the mic off axis now hears the cab's peaks and the stronger
    off-axis roll-off); every one of them automates (the mic ones grouped
    in the lane menu), MIDI-learns, saves in a preset and is reachable over MCP.
    `get_device_text` returns the status line, the live reading and a guide to the parameter
    values, `device_action` 0 resets the amp, and the new `read_valve` returns the THD with
    the harmonics, the aliasing estimate, the gate state, the auto-comp gain, the cabinet's
    loss at 4 kHz and the tone stack's and cabinet's responses.
- **Nota Vintage shows what the era does to the signal, not just its knobs.** The card moves
  onto the Auto Filter / Lens / Compressor frame — a STATE column (drive and wear faders), a
  centre panel with **Curve**, **Wear** and **Output** tabs, a **Tone / Output** panel and a
  status strip. The six characters are now a list in each tab's head row instead of a grid
  of buttons:
  - **Curve** — the transfer curve the saturation runs (read from the engine, so it is the
    real one), what you hear after Mix, Output and auto-comp, a dot where the input peak sits,
    the THD and the curve's asymmetry. Drag the window for the drive.
  - **Wear** — the pitch drift over the last four seconds in cents, wow in brass and flutter
    in teal. Drag up and down for the depth, sideways for the rate.
  - **Output** — the harmonics the curve adds at the input's level (f … 7f) against the hiss
    floor, with the input / output peaks.
  - **New in the engine:** a **tone model** (Warm — the era's own tilt and head bump, as
    before; Flat; Dark) with **low and high shelves**, **Character** (the era's voicing on or
    off), **wow and flutter rates** (the wow free or synced from 4 bars to 1/8), a **hiss
    high-pass**, **wear that follows the input** (hiss and crackle only under the signal),
    **stereo drift**, a **Tube / Analog output stage**, **even-harmonics-only** saturation and
    **auto gain compensation**. Flutter wanders a little as Wear rises.
  - **30 factory presets**, up from six, level-matched to the dry signal — records, tape
    decks, VHS, tube and console colour, telephone and AM radio. The thirteen new parameters
    are appended and default to the old sound, so older projects open unchanged; every one of
    them automates (the tone and wow ones grouped in the lane menu), MIDI-learns, saves in a
    preset and is reachable over MCP. `get_device_text` returns the status line, the live
    reading and a guide to the parameter values, `device_action` 0 resets the wear, and the
    new `read_vintage` returns the THD with the harmonics, the asymmetry, the wow and flutter
    in cents, the pitch drift's range, the band-limit, the hiss level and the auto-comp gain.
- **Nota Auto Filter shows the cutoff moving, not just where it is set.** The card moves onto
  the Lens / Compressor / Delay frame — a SHAPE column (cutoff and resonance faders, with a
  mark where the modulation has them now), a centre panel with **Filter**, **Envelope** and
  **LFO** tabs, a **Mod / Output** panel and a status strip:
  - **Filter** — the response you drag (sideways for the cutoff, up and down for the
    resonance) over the input's spectrum, the band the envelope and LFO can walk the cutoff
    over, and the response where they have it right now.
  - **Envelope** — the input's envelope and the cutoff it drives over the last 0.6–2 s, so
    attack, hold and release read as shapes; the onsets in the window are counted.
  - **LFO** — the LFO's movement over two bars (or a few cycles in free time) with a playhead
    at its live phase; the right channel is drawn when stereo phase is on.
  - **New in the engine:** a **modulation target** (the cutoff, the resonance or both),
    **Hold** gets a length (12 ms … 400 ms, or ∞ as before), **Smooth** slews the modulation,
    the LFO has a **start phase** and **Retrig** (it restarts on each input onset and at play).
    The envelope's **Up / Down** flips the amount's sign; the sidechain key's source and gain
    live behind its switch.
  - **30 factory presets**, up from six, level-matched to the dry signal — wahs, swells,
    synced wobbles and gates, random steps, stereo swirls and sidechain pumps. The five new
    parameters are appended, so older projects open unchanged and every one of them
    automates (grouped under *Env*, *LFO* and *Mod* in the lane menu), MIDI-learns, saves in
    a preset and is reachable over MCP; `get_device_text` returns the status line, the live
    reading and a guide to the parameter values, `device_action` restarts the LFO or resets
    the envelope, and the new `read_filter_motion` returns the modulated cutoff with its
    note, the envelope and LFO state and the cutoff's range over the last two seconds.
- **Nota Compressor shows its dynamics, not just its settings.** The card moves onto the
  Lens / Delay / Reverb frame — a LEVEL column (threshold against the live key level ·
  make-up), a centre panel with **Curve**, **Motion** and **Sidechain** tabs, a
  **Dynamics / Output** panel and a status strip:
  - **Curve** — the transfer curve you drag (sideways for the threshold, up and down for the
    ratio) beside four seconds of gain-reduction history with its peak, plus the average /
    peak reduction and the crest factor in and out.
  - **Motion** — the reduction envelope against the input over a window short enough to
    read attack and release as shapes, with the effective timings after the character
    voicing and the measured transient and recovery of the last hit.
  - **Sidechain** — pick the key source and its pre/post tap, see the key's spectrum under
    its filters and drag the HP / LP handles (up and down sets Q); **Listen** hears the key.
  - **New in the engine:** an **Auto** detector (RMS body, still catches spikes), 2-pole key
    filters with **Q**, **SC Gain**, **Hold**, an **External key** switch and **Stereo
    link** (off = each channel compresses on its own). **Look-ahead now reports its
    latency**, so delay compensation keeps the other tracks in time.
  - **31 factory presets**, up from six — drums, bus, vocals (with a de-esser), bass and
    instruments, and sidechain ducking. The five new parameters are appended, so older
    projects open unchanged and every one of them automates (grouped under *SC* in the
    lane menu), MIDI-learns, saves in a preset and is reachable over MCP; `get_device_text`
    returns the compressor's status line.

### Fixed
- **Nota Dynamic EQ-8's key track did nothing.** The device never stored the sidechain source
  it was given, so a chosen key track was forgotten at once and every band kept listening to
  its own track. The key is now kept, saved with the project and heard by the bands set to
  Ext.
- **Nota Crush's Fold went silent under Drive, and Rate moved in steps.** Fold clamped its
  input at four times the fold point, so with a few dB of Drive whole stretches of the wave
  collapsed to zero; it now folds on for as far as the signal goes, and more Drive means more
  folds. The sample and hold rounded the hold to whole samples, so the upper half of Rate
  jumped between a few rates (22 → 14.7 → 11 kHz); it now holds fractional lengths.
- **Nota Ceiling's integrated loudness and true peak.** The integrated LUFS gated 100 ms
  slices instead of the 400 ms blocks BS.1770 asks for, so quiet passages were weighed wrong;
  it now gates overlapping 400 ms blocks. The true-peak meter interpolated with a spline that
  read inter-sample peaks up to 1 dB low; it now uses a 4× windowed-sinc interpolator.
- **Nota Beat Repeat's first pass and filter width.** With Pitch above 0 the capture pass
  read ahead of the audio just written, so the first slice of every burst played stale audio
  from seconds earlier; the capture now plays through as it is and the pitch applies to the
  repeats. The filter's Width was labelled in octaves but raised the Q, so turning it up made
  the band narrower; it is now the band in octaves (Filtered Chops is retuned to match).
- **Nota Auto Shift's timing, Mix and Follow.** The shifter delayed the voice by about 16 ms
  without telling the engine, so a tuned vocal sat late against the other tracks; the delay is
  now reported and compensated. Mix blended that delayed voice with an undelayed dry one, which
  combed; both are aligned now. Follow scale device wrote the Nota Scale's root into the key as
  a raw number, so it landed on B for any root but C — it now copies the key and the scale. The Speed
  readout showed 1–250 ms while the engine used 2–300 ms.
- **Device commands inside a rack.** A card in a rack chain (Reset on Shutter, Learn on Auto
  Shift) sent its command to the track's own device at the same position instead; it now does
  nothing there.
- **Nota Shutter's detector and Duck.** The detector's follower let go of the key within a
  sample instead of over 3 ms, so the gate leaned on Hold to stay open on low notes; it now
  follows the key as intended, and the level meters decay smoothly. In Duck, Attack now sets
  how fast the signal goes down and Release how fast it comes back (they were swapped).
- **Nota Physical's 1→2 structure no longer blows up.** Resonator 1 fed resonator 2 at full
  gain, so a partial landing on one of resonator 2's rang it up by thousands of times and the
  track clipped. Resonator 2 now works as a set of resonant band-passes at the level of its
  partials, and Res Mix sets how much of resonator 1's own sound comes through with it.

## [0.40.0] — 2026-09-18

### Highlights
- **Clip tools.** A new Tools rail in the piano roll writes and rewrites notes — rhythms,
  chords, Euclidean patterns, arpeggios, ornaments, strums and more — and you hear every
  change before you apply it.
- **Every built-in instrument redesigned.** Synth, Volt, Aurora, Operator, Bass, Flux, Grain
  and the Drum Rack now share one compact card with tabs, live graphs and a status line;
  most gained pitch-bend and mod wheels and new controls, and every synth ships 25 presets.
- **25 drum kits and a step sequencer.** The Drum Rack gets factory kits synthesized on your
  machine, a kit picker, named pads and a Pattern tab for programming beats step by step.
- **Song sections and a Snap switch.** Mark Intro, Verse and Drop over the ruler to jump,
  loop and select by section; switch Snap off to place clips freely.
- **A lighter, cleaner workspace.** A new light theme (Ember Paper), a single-row transport,
  a compact browser and calmer clips and groups give the arrangement more room.
- **Simpler automation and a gamepad as a controller.** Arm Record and move a control to
  write automation — no more modes — and map a game controller's buttons and sticks like
  MIDI (macOS).

### Added
- **Nota Grain's editor is now the almanac's card, with a Dry/Wet knob and a live grain
  cloud.** The vertical tab rail and the separate playhead strip give way to one layout:
  the sample fills the centre as a graph — the Spray band around the dashed read Position,
  each held voice's read head and, new, **every grain the engine is playing** as a teal dot
  at its read position (high for left, low for right, larger the louder its window) — with
  the five tabs (Grain · Pitch · Variation · Filter · Amp) along its top and each tab's
  controls beneath it. **Drag across the sample to move Position**; double-click puts it
  back. The 186 px rail on the right keeps **READ** (Scan / Freeze / Key, Position, Scan
  speed, Spray) and the output — volume, **Dry/Wet** and the track's meter — on screen on
  every tab, and a status strip reads the tab back in words beside the sample, voices and
  live grain count. Knobs now read in the engine's units (grain size in ms, density as an
  overlap, coarse in semitones, fine in cents, cutoff in Hz, envelope times), the header
  shows grains per second, and the built-in pad draws its waveform too.
- **Nota Grain: Dry/Wet.** A new parameter blends the grain cloud with the sample itself,
  played straight from the read position at the note's pitch, before the filter and amp.
  It is appended to the parameter list and defaults fully wet, so older projects and
  presets sound as they did.
- **Nota Grain ships 25 factory presets** (was 6) — clouds and pads, patches that move
  through the file, keys and plucks that put the sample's own attack under the cloud,
  glitch and stutter textures, basses, and Dry/Wet blends down to the plain sample,
  including the six that existed before.
- **Nota Flux's editor is now the almanac's card, and the vector's worlds carry their own
  unison.** The card shrinks from 900 to **700 × 260** and reads left to right: the
  **Vector** field (the four worlds in their graph colours, the vector as a brass dot, and a
  dashed teal ring where Motion and React pull it), **React** (a source picker that lists
  the tracks when it opens, a scope of what Flux hears — envelope, transients and tilt —
  beside the reaction's own level, LISTEN and the four targets) and **Macros** (Age, Motion,
  Filter, Env and Space, then **glide, tune and gain**, which had no control on the card
  before). A status strip reads the patch back in words: the blend of the four worlds, or
  the source, target, **transients per bar** and where the vector is being pulled. FILTER
  now shows the cutoff the engine actually uses at the current vector, and double-clicking
  the field puts the vector back on its default. The "adaptive unison" the card promised is
  real now: each world detunes a twin of the oscillator by its own amount (Warm the most,
  Moog not at all), a little wider with Age. Projects load unchanged — no parameter moved.
- **Nota Flux ships 25 factory presets** (was 6) — pads, keys and plucks, basses, leads and
  a set of reactive patches meant for a drum or bass source, including the six that existed
  before under their old names.
- **The Nota Drum Rack's editor is now the almanac's card, with a kit picker in its header.**
  The header's **‹ Kit ⌄ ›** picker loads any factory kit into the rack in place (it
  replaces the pads) and steps through them; a rack names its kit even after a project is
  reopened or a pad or two is swapped. The body is the 700 × 260 card: **Pads** (the 4 × 4
  grid, pad 1 bottom-left, each pad in its own hue, the choke group on the pad) and
  **Mixer** (the bank's pads as rows: volume, pan, mute · solo · choke) under a strip with
  the bank, swing, humanize and Fold, beside a **selected-pad** panel — its one-shot drawn
  with a playhead, volume, pan, tune and decay, the choke group and a button into its
  instrument — and a status line that reads the selection back in words. New: a **Chain**
  view shows the selected pad's instrument and effects and adds effects to it (the panel's
  *Open chain*), and a right-click on a pad **renames**, **re-chokes** or **removes** it.
  An empty pad now also offers a Sampler.
- **Fifteen new factory drum kits, 25 in all:** *Brass Room* (live rock kit in a big room),
  *Breakline* (jungle break), *Bunker* (warehouse techno), *Pixel* (minimal clicks and
  blips), *Velvet* (jazz brushes), *Circuit* (electro box), *Yard* (dub and reggae),
  *Byte* (8-bit chip), *Lagoon* (amapiano log drums), *Titan* (trailer percussion),
  *Shuffle* (UK garage), *Mirrorball* (70s disco), *Pit* (orchestral percussion), *Hyper*
  (hyperpop) and *Crate* (90s boom bap). Like the first ten they are rendered on your
  machine on first launch, sit on the General MIDI map with the hats choked together,
  and bring their own swing and humanize.
- **Nota Bass grew performance wheels, a Legato switch and an output pan, and its editor
  is now the almanac's card.** A **pitch-bend** wheel with a selectable **range** (±2 / ±5
  / ±12 semitones) and a **mod wheel** that opens the LFO onto the cutoff — a wobble you
  play by hand — sit in a rail that stays on screen on both tabs. **Legato** decides what
  a note played over a held one does in mono: slide into it without a new attack (the
  303 way), or — the default — attack afresh with the glide still sliding the pitch. The
  card shrinks from the full window width to **700 × 260** and lays the synth out as
  **Signal** (Osc, Sub and Filter as three rows: wave chips, shape, pulse width, tuning and
  level; the sub's wave, octave and level with the mix sum; the filter's type and slope,
  its response as a drag pad and cutoff, resonance, drive, env and key amounts) and
  **Mod** (both envelopes drawn and draggable, the LFO with its destinations and the
  velocity amounts), beside a **Global** rail — voice mode, glide, unison, drive, bend
  range, legato, volume, pan and the track's meter — and a status strip that reads the
  patch back in words. Projects saved before this load unchanged: the existing parameters
  keep their index and the new ones start neutral.
- **Nota Bass ships 25 factory presets** (was 6) — subs, plucks, acid lines, wobbles,
  reeses, stabs and slides, including the six that existed before under their old names.
- **Nota Aurora grew performance wheels, eight macros and a switchable FX chain, and its
  editor is now the almanac's card.** A **pitch-bend** wheel with a selectable **range**
  (±2 / ±5 / ±12 semitones) joins the mod wheel in a rail that stays on screen on every
  tab. There are now **eight macros** instead of four, reaching **twelve** destinations
  (warp, osc 2 level, sub level, unison detune and drive on top of the old seven), each
  named after what it drives. **LFO 2** can lock to the tempo like LFO 1. The **unison**
  stack is stereo: detune is its own dial in cents and **spread** widens the stack across
  the field. The FX block became a chain of three switchable blocks: **drive** (Tube / Tape
  / Fold, with a tone tilt), **chorus** (1× / 2× / 4×) and **reverb** (Room / Hall / Plate,
  with size). The output also gets a **pan** control. The card lays the synth out as
  **Osc · Filter · Env · LFO · Mod · FX** tabs over a 186 px rail that switches between
  **Global** and **Macros**, with a status strip that reads the patch back in words.
  Projects saved before this load: the existing parameters keep their index, the new
  ones start at values that sound as before, and a macro saved against the old seven
  destinations still points at the same one. The one exception is unison (see Changed).
- **Nota Aurora ships 25 factory presets** (was 8): pads, keys, bells and plucks, leads,
  basses and motion patches, including the eight that existed before under their old
  names.
- **Nota Volt grew performance wheels and wider macros, and its editor is now the
  almanac's card.** A **pitch-bend** wheel with a selectable **range** (±2 / ±5 / ±12
  semitones) joins the mod wheel in a rail that stays on screen on every tab, and the
  vibrato can be put **on the mod wheel** so the patch's depth becomes the wheel's
  ceiling. A **macro** now reaches **twelve** destinations instead of six — the three
  source levels, Filter 2's cutoff and either LFO's rate on top of pitch, osc 2 pitch,
  cutoff, resonance, level and pan — and names itself after what it drives (*BRIGHT*,
  *DRIVE*, *MOTION*). The card lays the synth out as **Osc · Filter · Env · LFO · Mod ·
  Macro** tabs — the three sources as a table, the selected filter's response as a drag
  pad beside its type, slope and amounts, both envelopes drawn and draggable, the LFOs
  each naming where they land, the matrix as cells you drag up for + and down for −, and
  the eight macros as tiles — over a 186 px rail that switches between **Global** (voice
  mode, bend range, cutoff, resonance, glide, gain, pan and the track's meter) and
  **Voice** (both output amps, unison and the two velocity amounts), with a status strip
  that reads the patch back in words. Projects saved before this load unchanged: the
  existing parameters keep their index, the new ones start neutral, and a macro saved
  against the old six destinations still points at the same one.
- **Nota Volt ships 25 factory presets** (was 8) — basses, leads, pads, keys and motion
  patches, including the eight that existed before under their old names.
- **Nota Operator grew two performance wheels and keyboard tracking, and its editor is now
  the almanac's card.** A **pitch-bend** wheel with a selectable **range** (±2 / ±5 / ±12
  semitones) and a **mod wheel** on the modulation index sit in a rail that stays on screen
  on every tab; the filter gains **keyboard tracking** (0 / ½ / 1 octave per octave) and
  the amplifier a **Vel → Level** amount. The card lays the synth out as **Operators ·
  Algorithm · Filter · Amp** tabs — the four operators as a table that names each one's
  role in words, the eleven topologies as sketches over a routing diagram you can still
  drag to re-route, and the filter response beside the live harmonic spectrum — over a
  186 px rail that switches between **Global** (FM depth, feedback, tone, glide, bend
  range, voice mode, volume and the track's meter) and **Env** (one operator's ADSR, drawn
  and draggable, with key and velocity tracking), and a status strip that reads the patch
  back in words. Projects saved before this load unchanged: the existing parameters keep
  their index and the new ones start at values that sound exactly as before.
- **Nota Operator ships 25 factory presets** (was 6) — keys, bells and mallets, basses,
  leads, pads and percussion, including the six that existed before under their old names.
- **Nota Synth grew an oscillator and a voice section, and its editor is now three tabs.**
  The oscillator gains **pulse width** (on the square), **detune**, **octave** and a
  **unison** stack of 1 / 2 / 4 / 7 voices with a stereo **spread**; the filter gains a
  **type** (Off / LP / HP / BP) and an **Env → Cutoff** amount; the voice section gains
  **Poly 16 / Mono / Legato**, **glide**, **pan** and **Vel → Vol**. The card lays them out
  as **Osc · Env · Filter** tabs — each one showing its graph full size with its knobs under
  it — over a rail that keeps voice mode, volume, pan, glide, spread, velocity tracking and
  the track's meter in view on every tab, and a status strip that reads the patch back in
  words. The envelope's breakpoints and the filter's curve are draggable. Projects saved
  before this load unchanged: the original eight parameters keep their index, and the new
  ones start at values that sound exactly as before.
- **Nota Synth ships 25 factory presets** (was 4) — pads, basses, leads, keys and moving
  patches, including the four that existed before under their old names.
- **Clip tools — generative writing and rewriting in the piano roll.** A **Tools** rail
  opens beside the roll with fifteen tools. Six **generators** write notes: *Rhythm*
  (a pattern weighted toward the strong beats, with density, variation and accent),
  *Seed* (reads the clip's own intervals, note lengths and onset spacing, then writes a
  fresh take in the same voice), *Stacks* (grows every note into a chord — 3rds, 4ths,
  5ths, octaves or clusters, voiced close / open / drop 2 / spread / power), *Euclidean*
  (hits spread as evenly as the steps allow, with rotation and a second Euclidean ride
  picking the accents), *Melodic Steps* (a step sequencer in scale degrees with a contour)
  and *Shape* (a curve traced across the clip). Nine **transformations** rewrite them:
  *Arpeggiate*, *Connect* (runs that walk to the next pitch, carved out of the source note
  when the part is legato), *Ornament* (grace notes, trills, mordents, turns, flams and
  rolls), *Quantize* (with swing and humanize), *Recombine* (pulls pitch and rhythm apart
  and puts them back together differently, in phrase-sized chunks), *Span*, *Strum*,
  *Time Warp* and *Velocity Shaper*.
- **Every change is previewed before you commit to it.** Turning a knob re-runs the tool,
  shows the result in the roll and streams it to the engine, so a generator is audible
  while you shape it. **Apply** keeps it as one undo step; **Revert** drops it without
  leaving anything in the history. A selection scopes a tool to those notes and carries
  the rest of the clip through untouched; output can replace what it was given or be added
  on top. Every tool is seeded, so a result is repeatable and "New seed" re-rolls it — and
  the ones that think in pitch follow the roll's **Set Scale** key, so what they write
  stays in key.
- **Ten factory kits for the Drum Rack.** *Volta* (warm analog boom), *Kompakt* (punchy
  analog house), *Micron* (small vintage rhythm box), *Linnwood* (80s PCM machine, gated
  snare), *Atelier* (acoustic studio kit), *Cellar* (dusty vinyl break), *Neon* (modern
  sub-forward, with tuned 808 bass hits), *Foundry* (industrial metal), *Terra* (hand
  percussion) and *Aether* (ambient, long tails). Each is 16 pads laid out on the General
  MIDI drum map (36–51), with the three hats sharing a choke group and the kit's own swing
  and humanize. Load one from the browser — they sit under **Nota Drum Rack** in the
  Instruments tab — onto a new track, or drop it on an existing Drum Rack to replace its
  pads. The result is an ordinary Drum Rack: every pad is a Sampler you can retune,
  reshape and add effects to.
- **The kits are synthesized on your machine rather than bundled as audio**, so they cost
  the installer nothing: Nota ships the recipes and renders the WAVs once, on first launch,
  in well under a second. They land in Nota's data folder and appear in the browser's Files
  tab under **Nota Kits**, so the individual one-shots can be dragged anywhere a sample
  goes. The synthesis models the instruments rather than sampling them — rung resonators
  for the analog voices, struck bodies with real mode spacing for the acoustic ones — and
  runs at 4× the output rate through a zero-phase decimator, so nothing aliases.
- **Drum Rack pads can be named.** Pads used to read "Nota Sampler" all sixteen times over;
  now a pad carries its own name, shown on the pad, in the mixer view, and as the row label
  in the Pattern grid. Kits name their pads, a dropped sample names its pad after the file,
  and the name travels with the project.
- **A Snap latch in the transport.** Separate from the grid denomination, which only says
  *how far apart*: switch Snap off to position clips freely for a while, instead of holding
  ⌥ for every drag. The grid keeps its spacing, so switching back resumes where you were.
- **A MIDI input light** on the MIDI Learn button, lit by incoming control events — a
  controller that is plugged in but on the wrong port or channel is now visible without
  opening the mappings tab.
- **A Pattern tab for the Drum Rack.** Select a MIDI clip on a Drum Rack track and the
  detail panel offers **Pattern** between Devices and Clip: a drum-machine step grid with
  one row per loaded pad. Click a step to place a hit, shift-click it for an accent, drag
  across the grid to paint a run, and ride the velocity lane under the selected row; click
  a pad name to hear it. The grid runs from 1/4 to 1/32, follows the clip's length (long
  clips page in bar windows), and lights the step being played. The pattern is not a copy —
  the grid *is* the clip, so what you step shows up in the piano roll and notes drawn there
  show up as steps. Tab now cycles Devices → Pattern → Clip.
- **Song sections.** A lane over the ruler holds named spans — Intro, Verse, Drop — so the
  timeline says where you are in the song rather than only which bar you are on. Drag on
  the empty lane to mark one, click it to jump the playhead there, double-click to rename,
  drag its body or edges to move and resize it (always on whole bars). Its menu loops the
  section, selects it as a time range across every track, duplicates it or deletes it.
  Sections travel with the project and can be hidden from View ▸ Toggle sections.
- **View ▸ Clip names** cycles how much of the arrangement prints clip names: every clip,
  the first clip of each run (the default), or none. A clip named by hand always shows it.
- **A gamepad can be mapped like a MIDI controller** (macOS). Arm MIDI Learn, click a
  control, then press a button or move a stick — that control is now driven by the pad.
  A mapped button stops playing its note while every unmapped one keeps the built-in
  layout, so one pad both plays and mixes; nothing is reserved, and mapping the d-pad
  takes it over from octave/velocity.
  - **Sticks and triggers** come along as continuous sources — both axes of each stick and
    the two trigger travels — so a pad can sweep a filter or ride a fader, not just switch
    things. They play no notes and do nothing until mapped. Sticks rest centred behind a
    deadzone so a worn one does not drift a mapped parameter; triggers rest at zero.
  - Gamepad sources share the mapping table with CCs and notes, appear in the browser's
    MIDI Map tab, and travel with the project. Mappings match on the control rather than
    the pad slot, so unplugging and reconnecting a controller does not break them.
- **Light theme.** *Ember Paper* joins *Ember Graphite*: the same design system on a warm
  paper ground, with brass darkened to bronze so marks keep their weight against a light
  background. Preferences → Appearance picks Ember Graphite, Ember Paper or System (which
  follows the OS appearance and switches with it). Switching applies immediately — no
  restart — and the choice is remembered.

### Changed
- **Nota Reverb moves onto the Chamber frame and grows the parts it was missing.** A TAIL
  column (decay · HF damp), a centre panel with **Space** and **Tone · Mod** tabs over a
  live decay-tail window, a **Levels / Output** panel and a status strip. The window
  replays the sound: the impulse flashes at the pre-delay, the early reflections light in
  turn and the RT60 curve draws itself while the reverb is sounding. Drag the teal marker
  for the pre-delay or the tail for the decay.
  - **Decay is now a true RT60**: the number on the knob is the time the tail takes to fall
    60 dB, whatever the size or algorithm. Older projects keep their settings, but long
    decays ring a little differently than before.
  - **Early reflections**, spaced by the algorithm (a hall's walls far apart, a plate's a
    tight cluster) and the size, and an **input diffuser** for a denser onset.
    **Latency comp.** shortens the pre-delay by the diffuser's group delay, so the tail
    starts on the set pre-delay.
  - **Mod on tail** chooses where the modulation goes (the tail itself, or only the early
    part so the tail stays still), and **Vintage** gives it an early-digital colour.
  - **An output stage:** separate **dry level**, **bass mono** on the tail and **Wet only**
    for a return track. **Kill tail** empties the reverb at once, even when frozen.
  - **30 factory presets**, up from four. The seven new parameters are appended, so every
    one of them automates, MIDI-learns, saves in a preset and is reachable over MCP;
    `get_device_text` returns the reverb's status line, and `device_action` kills the tail.
- **Nota Delay is a full delay, not a time-and-feedback box.** The card moves onto the same
  frame as Nota Chamber — a LOOP column (feedback · spread), a centre panel with **Time**
  and **Loop · Wow** tabs over a live repeat window, a **Levels / Output** panel and a
  status strip — and the engine grows the parts that were missing:
  - **A tone stage inside the loop:** a low cut and a high cut you drag on the FILTER
    graph, allpass **Diffuse** that smears the repeats towards reverb, and **Tape mode**,
    which adds head loss and soft saturation and doubles the WOW depth.
  - **Changing the delay time is a choice.** *Fade on change* crossfades to the new tap, so
    dragging a time knob or picking another division stays clean; switched off, the read
    head glides to it and bends the pitch like a tape machine.
  - **An output stage:** a separate **dry level**, stereo **width** and **bass mono** on the
    repeats, **Wet only** for a return track, and **latency compensation** that pulls the
    read pointer back by the diffuser's group delay so each repeat still lands on the beat.
  - **Freeze** now truly holds — the loop bypasses its filters and saturation while it is
    on, so a held loop neither dulls nor grows — and **Clear loop** empties the buffer.
    **Tap tempo** sets the time by ear.
  - The repeat window draws the two channels on a dB scale, lights the repeat that is
    sounding, and reads the same in Ember Paper. **29 factory presets**, up from three.
  - The ten new parameters are appended, so older projects open unchanged and every one of
    them automates, MIDI-learns, saves in a preset and is reachable over MCP;
    `get_device_text` returns the delay's status line, and the new `device_action` tool
    clears the loop.
- **Nota Aurora's unison dials now do what they say.** **Detune** is in cents (0–50) and
  no longer scales with the voice count, and **spread**, which the engine used to ignore,
  now pans the stack across the stereo field. Patches that use unison, including the
  default one, therefore sound wider and more detuned than before. Turn spread down to
  zero to get the old mono stack back.
- **The top chrome is one row instead of two.** The transport bar and the toolbar under it
  were a 48px row and a 34px row mixing four unrelated jobs; they are now a single 60px
  bar, handing 22px back to the arrangement. It reads left to right: which view you are
  in · what plays · the numbers you set · the switches you flip · then, pinned right, what
  the machine is doing. Transport, position and loop share one recessed console so
  playback reads as a single object, and tempo, signature, grid and launch quantize use
  one value-over-label cell so the row scans as a strip of readouts. The position readout
  is now large enough to read across the room and says underneath which clock it is
  showing. The four switches — metronome, follow playhead, snap and automation — are icons
  now instead of words, each drawing the thing it does. Nothing was dropped: the three
  add-track buttons collapse into a **+ Track** menu, launch quantize appears in Session
  where it applies, and Follow playhead is both a button in the row and **View ▸ Follow
  playhead**, which stay in step with each other.
- **Master shows the gain it is applying,** in dB, next to its fader and meter — the
  handle position is no longer the only readout.
- **The arrangement and the modular canvas are islands, and groups stop weighing as much as
  their tracks.** Both now float on the app ground as rounded panels, matching the browser —
  the modular view keeps its toolbar, track rail and status line inside those corners. A group
  row collapses to a 26px titled bar carrying its disclosure, mute/solo and level, and
  opens to the full control set when you select it — so a session of ten tracks under
  three groups reads as three families instead of thirteen equal rows.
- **One hue per group.** A track that has not been given a colour of its own takes its
  group's, varied by shade so siblings stay apart inside the family. An explicitly coloured
  track keeps its colour, and every view — arrangement, mixer, clip editor — agrees.
- **Clips carry a colour band, not a name strip.** The 14px title strip is gone: a clip is
  a 2px band in the track colour over the full-height waveform, and the name appears only
  where a run of clips begins, so a repeated pattern reads as one block rather than the
  same word printed twenty times.
- **The browser is a compact index.** It is now an island panel — rounded, floating on the
  app ground — and its rows are single-line: sectioned into **BUILT-IN** and **PLUG-INS**
  with counts, the repeated "Nota" prefix dropped to tertiary ink so names scan on their
  distinctive word, and the device type moved to the right edge as a quiet tag. The name
  keeps its room: a long one pushes the tag out rather than being truncated by it. A
  plug-in's tag carries its format and vendor, so the AU and VST3 builds of one plug-in are
  no longer two identical rows. The icon rail was redrawn on a 24-unit grid with round caps
  — the glyphs are sized against each other rather than each filling its box — and lost its
  chip backgrounds along with the per-row amber icons; the active tab is marked by a brass
  edge, with a hairline between the device tabs and the library tabs. The panel also opens
  a little wider than before, so a device name and its type both fit on the line.
  - A **⋮ button** beside the search box holds the view options — show type tags, group by
    source, favourites first — and the tag editor, which used to need a chip of its own.
  - The filter chips keep to **one line**; whatever does not fit collapses into a **+N**
    that opens the rest, instead of wrapping and pushing the list down.
  - A **status line** along the bottom counts what the tab is showing.
- **The splitters between the browser, the arrangement and the detail panel are invisible
  now** — 2px transparent grab strips inside the gutter, since the browser island's own
  edge already draws the line.
- **Automation has no record modes any more.** Read / Touch / Latch / Write are gone, and
  the **Automation** button now only shows and edits the lanes. Automation always plays
  back, and recording is contextual, the way Live does it: engage the transport **Record**
  button and move a control, and that parameter's lane records what you do. A control you
  move with the mouse stops writing the moment you let go; a hardware knob, fader, stick
  or trigger keeps writing its last value until the transport stops, because a physical
  control has no release. The per-parameter **REC** arm button on the lane header is gone
  with the Write mode it existed for.
- **Touching an automated parameter while not recording overrides its lane**, so the knob
  answers your hand instead of being dragged back by the envelope on the next block. A
  **Re-enable Automation** button appears in the toolbar while anything is overridden;
  one click hands every overridden lane back to playback.
- The colour tokens in `NotaTheme.axaml` now live in theme dictionaries, and `NotaPalette`
  hands out brushes that re-tint in place, so both the XAML and the custom-drawn layer
  follow the active variant. The ~970 hex literals that had accumulated across the device
  cards, visualisers and editors now route through the palette; colours that are genuinely
  data — tag swatches, Strata layer hues, Drum Rack kit dots — are re-tinted per variant
  at the point of use.
- `WaveformView` was still painted in the pre-Ember cool-slate palette; it now uses the
  tokens its own comments named.

### Fixed
- **Nota Grain: Position is live.** A held note used to read from wherever Position was
  when it started, so moving the knob or automating it did nothing until the next note.
  Held notes now follow it — Scan adds its moving offset to it, Freeze sits on it and Key
  offsets it by the note.
- **Nota Grain: a repeated note no longer cuts itself off.** A note-off released every
  voice of its pitch, so a note that started a hair before the previous one of the same
  pitch ended went silent with it. A note-off now releases only the oldest held one.
- **Nota Flux: with no source assigned, React is off.** It used to listen to Flux's own
  output instead, so a patch aimed at the filter or the vector wobbled with its own chords.
  The React followers now also run on time constants rather than once per audio block at a
  fixed rate, so the reaction feels the same at any buffer size.
- **Nota Flux: a repeated note no longer cuts itself off.** A note-off released every voice
  of its pitch, so a note that started a hair before the previous one of the same pitch
  ended went silent with it. A note-off now releases only the oldest held one.
- **Nota Bass: back-to-back notes no longer drop out.** Two things silenced the next note of
  a tight line. A note-off released *every* note of its pitch, so when a repeated note
  started a sample before the previous one ended (float rounding, a Note Length or arp
  device, notes drawn overlapping) the new note was cut off the moment the old one ended —
  in mono and poly alike. And in mono with glide on, any overlap was taken as legato and
  skipped the attack, so on a plucky patch whose envelope had already decayed the next
  note never sounded. A note-off now releases only the oldest held instance of its pitch,
  and an overlapping note attacks afresh unless the new **Legato** switch is on. A
  retrigger of a still-sounding voice starts from where its envelopes are, so it never
  clicks. Switching Poly → Mono while chords are held no longer leaves those voices
  hanging.
- A MIDI mapping onto a switch (mute, solo, a transport button) fired on every incoming
  message past the half-way point, so sweeping a mapped CC across it made the target
  flutter instead of toggling once. It now fires on the crossing only — which is also what
  makes a mapped gamepad trigger usable.
- Nota Volt's modulation matrix said "drag a cell · up +, down −" but actually took a
  horizontal drag, and drew each route as a small bar that vanished at narrow cell widths.
  It now drags vertically, as it always claimed, and a route lights its whole cell with a
  brass wash that deepens with the amount.
- The filter-response graph shared by Nota Volt, Aurora, Bass and Operator labelled its
  frequency axis `20 · 100 · 1k · 10k · 20k` regardless of where those points actually
  fell, and read the cutoff off Volt's own map on every instrument. It now takes the
  instrument's real cutoff range, so both the scale and the readout say what the engine
  does.

## [0.38.0] — 2026-09-12

### Highlights
- **Two new synths.** *Nota Pentad* is a five-voice Prophet-5 with Poly-Mod and per-voice
  vintage drift; *Nota Consort* is a paraphonic four-oscillator Moog with a 42-point patch
  bay you wire by dragging, a step sequencer and a bucket-brigade delay.
- **Two new effects.** *Nota Chamber* is a hybrid reverb — 16 impulse responses (or your own
  file) blended with four algorithmic modes, with Freeze and a shimmer pitch shifter.
  *Nota Prism* is multiband dynamics, compressing above one threshold and expanding or
  lifting quiet detail below another, per band.
- **Overview.** The whole project sits on one strip above the arrangement: drag it to scroll,
  drag up or down to zoom, click to jump anywhere.
- **Consolidate (⌘J).** Merge a time range or a handful of clips into one clip per track —
  notes merged, audio rendered with gain, transpose, warp, fades and envelopes baked in.
- **Reverse audio clips.** Play a clip backwards from its right-click menu or the clip
  editor. Nothing is re-rendered, so it is instant and free to try, and both waveform views
  flip to match what you hear.
- **More in the right-click menus, and a long list of fixes.** Freeze, Live Freeze and adding
  instrument / audio / return tracks are now in the arrangement's context menus; device cards
  step through presets with ‹ › and keep their preset names; knobs take ⌘ or ⇧ for fine
  steps; and the clip editor now follows clips you resize or move in the arrangement.

### Added
- **Add a track from the arrangement's context menus.** Right-clicking the empty space
  below the tracks now offers `Add instrument track` / `Add audio track` / `Add return
  track` above `Paste track`, and a track's own menu carries the same three under
  `Add track ▸`. They route through the toolbar's handlers, so a track made this way is
  seeded and reported exactly like one made with the `+` buttons.
- **Reverse for audio clips.** A `Reverse` item in a clip's context menu and a DIRECTION
  toggle in the clip editor play a clip backwards. It is non-destructive — the file and
  any warp cache stay in playing order and only the read direction flips — so toggling is
  instant, undoable and free to audition, and it composes with clip gain, pitch, warp and
  the clip envelopes. Both the arrangement and the clip editor draw the waveform the way
  it now sounds — the editor mirrors the whole view, so its Start/End brackets, warp
  markers, BPM chips, beat grid and playback cursor all flip together and the screen still
  reads left-to-right in playing order (the clip envelope is authored in played time, so
  it stays put). Trimming, resizing and splitting a reversed clip mirror too, so the
  audible head and tail follow the edit instead of jumping. Consolidate and Freeze bake
  the reversal in.
- **Overview — a project strip above the arrangement.** The whole project on one band,
  a mini-clip per clip in its track colour (a collapsed group still shows the clips it
  hides), with the visible span drawn as a brass window over it and everything outside it
  dimmed. Drag the window sideways to scroll at a fixed zoom, drag up or down to zoom out
  or in, drag either edge to zoom by resizing it, click anywhere to jump there, double-click
  to fit the whole project
  on screen. The loop region and the playhead show along it, and the header cell reads out
  the visible bar range. View ▸ Toggle overview hides it.
- **Nota Consort — a paraphonic semi-modular synth.** A new built-in instrument (kind 15) in
  the spirit of a four-oscillator paraphonic Moog. Four oscillators (triangle, saw, square or
  pulse with PWM; 32′–2′; oscillator 2 syncs to 1 and 4 to 3) feed a mixer with noise, drive
  and an EXT input that is normalled to the output for the classic feedback growl, then two
  ladder filters — HP→LP in series, or parallel stereo as LP/LP or HP/LP with a Spacing
  offset between them — and two ADSR envelopes. **MONO**, **DUO** and **PARA** share one
  filter and envelope pair the way a paraphonic synth does (each held note gets its own
  oscillators; Multi trig retriggers the envelopes; Unison doubles idle oscillators with a
  detune), and **True poly** gives 16 complete voices instead. Glide per oscillator — linear
  rate, linear time or exponential, optionally legato-only. An LFO with six shapes (free or
  tempo-synced) modulates pitch, cutoff and PWM, and a stereo **bucket-brigade delay** (darker
  as it gets longer, with compander, overload and ping-pong; or a clean digital line) sits at
  the end. A 16-step **sequencer / arpeggiator** with ratchets, ties, rests, swing, forward /
  backward / random order and latch plays in time with the song. The **patch bay** takes up to
  12 cables across 42 points — oscillator, filter, envelope, LFO, keyboard, sequencer and
  utility outputs into pitch, PWM, cutoff, resonance, VCA, delay and LFO-rate inputs, plus
  Gate, Filter, VCA and EXT inputs whose internal connection a cable replaces; feedback loops
  are allowed. Patch it as a matrix, as a strip of jacks inside the Patch tab, or on a
  full-card overlay with every point; drag from jack to jack to connect, ⌥-click to pull a
  cable. Cables are ordinary parameters, so patches are saved with the project and in presets,
  duplicate with the track, and their depths can be automated. 28 factory presets; a freeze
  matches playback exactly.
- **Nota Prism — multiband dynamics.** A new built-in audio effect (kind 21) that splits the
  signal into Low / Mid / High with phase-coherent crossovers — or two bands, or one — and gives
  each band a compressor above a threshold and an expander (or an upward compressor, which lifts
  quiet detail) below a second one, with their own attack and release. Drag the crossovers on
  the live input/output spectrum, drag the threshold dots on the transfer curve, watch each
  band's detector against its threshold on the *Time* tab, and solo a band to hear it alone.
  Amount scales all the dynamics at once; there is Peak / RMS detection, auto release, lookahead
  (latency-compensated), an external sidechain split into the same bands (with Listen), auto
  makeup, Mix for parallel compression, output gain and a soft clip at −0.3 dBFS. The card
  follows the design mockup (Global column, Bands / Band detail / Time and Meters / Output tabs,
  a status line with CPU). 12 factory presets (Bus Glue, Upward Squash, Vocal Control, De-Esser
  and more); every control can be automated, MIDI learned and A/B compared.
- **Nota Chamber — a hybrid reverb.** A new built-in audio effect (kind 20) that runs a
  convolution reverb and an algorithmic reverb side by side, as in Ableton's Hybrid Reverb.
  **Blend** mixes the two — in parallel, or in **Serial** with the convolution feeding the
  algorithm. The convolution engine comes with 16 impulse responses (concert hall, stone
  vault, cathedral, scoring stage, wood chamber, live and drum rooms, tiled bathroom, two
  plates, a spring tank, car park, stairwell, forest, gated room, metal tank), and loads your
  own WAV / FLAC / MP3 file — mono, stereo or 4-channel true stereo — with **Load…** or by
  dropping it on the waveform. Drag the waveform's brass lines to trim the IR (Start / Decay),
  and shape it with Attack, Size (stretch 50–200 %), Reverse and True Stereo; a *Result* lane
  under it shows the IR exactly as it is convolved, after the pre-delay. The engine has
  no latency, and changing the IR re-renders the whole tail without a click. The algorithm
  has four modes — **Dark Hall**, **Plate**, **Quartz** and **Shimmer** — with Decay, Size,
  Diffusion, Damping and separate low / high decay times you drag on the *Decay per band*
  graph — drawn over the algorithm's real, rendered impulse response — plus modulation, **Freeze** (the tail holds forever), **Hold in** (keep layering the
  input into a frozen tail), a **Vintage** colour and a pitch shifter (−12 / +7 / +12, fed
  back into the tail or on top). Each engine has its own pre-delay, free or tempo-synced.
  Around them: a four-band EQ you can place at the input, on the tail or at the output (with
  a drag-handle curve), ducking of the wet while you play, width and bass mono, Dry/Wet with
  a dry level, a **Wet only** switch for send tracks, output gain and meters. The card follows
  the design mockup (Blend column, Convolution / Algorithm / EQ · Mod and Levels / Output
  tabs, a status line with CPU). 22 factory presets; every control can be automated, MIDI
  learned and A/B compared, and a loaded IR is saved inside the project. MCP can add it,
  load an IR file and read the IR names.
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
- **Consolidate (⌘J).** Merges clips into one, as in Ableton: select a time range, or some
  clips, and press ⌘J (Ctrl+J on Windows and Linux), use **Edit ▸ Consolidate**, or pick
  **Consolidate** from the clip's right-click menu. Each track gets one clip covering the
  whole range, and any clip parts outside it stay as they were. On MIDI tracks the notes are
  merged: notes cut short by a clip's end stay cut, and the velocity envelope is baked into
  the notes. On audio tracks the clips are rendered into a new sample exactly as they play,
  with clip gain, transpose, warping, fades and clip envelopes baked in and silence in the
  gaps. If any of the source clips was warped, the new clip is warped too, so it keeps
  following tempo changes. Deactivated clips count as silence. The new clips are selected,
  and one Undo reverts the whole operation. Also available over MCP as `consolidate_clips`.
- **Shortcuts screen: the missing Edit-menu keys.** ⌘Z / ⌘⇧Z, ⌘D, ⌘E and ⌘L are now listed
  alongside the new ⌘J.
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
- **Step through presets from a device card.** The preset box in a card's header now has ‹
  and › buttons on either side that load the previous or next factory preset, wrapping
  around at the ends. The preset list marks the one currently loaded.

### Fixed
- **Resizing a clip in the arrangement now updates its open editor.** Dragging a clip's edge
  left the clip editor showing the old length — its Start/Length read-outs, the piano roll's
  own span and the envelope editor's beat axis all kept the length they had when the editor
  was opened. Moving a clip likewise leaves its Start read-out correct now.
- **Editing a clip in the arrangement no longer throws you out of the Clip tab.** Clicking a
  clip — or grabbing its edge to resize it — selects its track, which used to switch the
  detail panel to Devices. The panel now stays on Clip and follows the clip you picked.
- **Deleting the selected track clears the device panel.** Its instrument and effect cards
  stayed on screen, still editing a track that no longer existed; the Modular view kept the
  removed track's nodes the same way.
- **A device card keeps showing its preset name.** Switching to another track and back no
  longer resets the name to Init. The name stays with its device when devices are
  reordered, deleted or copied, and presets loaded from the browser now show their name on
  the card too.
- Applying a built-in effect's factory preset now resets the parameters the preset doesn't
  name to their defaults, so switching presets no longer keeps leftovers from the last one.
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
