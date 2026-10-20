# Feature Specification: Nota Vocoder audio effect

**Feature Branch**: `feat/vocoder-effect`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "Vocoder audio effect (nota-daw/nota#113): two inputs — carrier
(the track the effect sits on, or an internal Saw/Noise carrier) and a modulator tapped from
another track as a sidechain (pre/post fader). Design mockup: design/vocoder/index.html —
card 700×260 with Modulator (source picker, gain, key meter), Carrier (Input/Saw/Noise,
tilt), Bands (8–32 log-spaced bands, low/high range draggable in the envelope graph, Q,
formant ±12 st, attack, release), Output (unvoiced noise, mix, gain, hold, output meter)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The main area is the engine: a new built-in audio effect kind (DSP + kind registration),
plus its device card, browser / palette / MCP entries and factory presets. It reuses the
sidechain source mechanism Nota Compressor already has (another track, pre / post tap);
no new routing model.

**Design reference**: [`design/vocoder/index.html`](../../design/vocoder/index.html) — the
card at true size (running, picker open, no modulator), in all four colour themes.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-20

- Q: Where does the internal Saw carrier get its pitch? → A: A **Pitch** control on the
  card (fixed note, default C3); it does not follow the track's notes.
- Q (user testing): adding the vocoder silenced the track, and with a modulator the user
  mostly heard the voice track itself. → A: interim fix — pass-through with no source, level
  normalised by the voice's energy (research R-14). Superseded by the next answer.
- Q (user testing): "I drop the vocoder on a track, there is no modulator, a robot-voice
  preset is on — the track's own sound should come out as a robot voice (internal carrier)".
  → A: **The track the vocoder sits on is the modulator (the voice).** The carrier is an
  internal **Saw** (the default — a robot voice at once), **Noise**, or another **Track**
  picked on the card. The earlier model (the track as the carrier, the voice picked from
  another track) is dropped: the same result comes from putting the vocoder on the vocal
  with Carrier = Track (the pad). Research R-15.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A robot voice from the track itself (Priority: P1)

A producer drops Nota Vocoder on a vocal (or any) track. With its defaults — or a preset like
Robot choir — the track's own sound comes out as a robot voice: the vocal's band levels shape
an internal saw at the Pitch note. No other track is needed.

**Why this priority**: It is what users expect when they drop a vocoder on a track, and the
quickest way to hear the effect.

**Independent Test**: Put the vocoder on a speech track with the defaults and play: a buzzy,
intelligible robot voice at C3 that follows the syllables; Carrier = Noise gives a whisper.

**Acceptance Scenarios**:

1. **Given** a vocal track, **When** the user adds Nota Vocoder and plays, **Then** the vocal
   is heard as a robot voice (Saw at C3) at about the vocal's own level, and the key meter and
   the envelope graph move with the vocal.
2. **Given** Carrier = Noise, **When** playing, **Then** the output is a whisper and Tilt is
   shown inactive.
3. **Given** the track is silent, **When** playing, **Then** the vocoder is silent too.
4. **Given** Carrier = Saw, **When** switching to Noise and back, **Then** the switch is
   click-free.

---

### User Story 2 - Make a pad "talk" with the vocal (Priority: P1)

The producer has a sustained pad on another track. On the vocal track's vocoder they set
Carrier = **Track** and pick the pad track: the pad articulates the vocal's words. They mute
the pad track so only the vocoded sound is heard.

**Why this priority**: The two-input case of #113.

**Independent Test**: Vocal track with the vocoder, Carrier = Track, a chord track picked:
the chord follows the speech's spectral envelope; muting the chord track keeps it working.

**Acceptance Scenarios**:

1. **Given** Carrier = Track and a pad track picked, **When** playing, **Then** the pad is
   heard shaped by the vocal, at about the pad's level whatever the vocal's level.
2. **Given** Carrier = Track and no track picked yet, **When** playing, **Then** the vocal
   track is heard unchanged, as if the vocoder were bypassed.
3. **Given** the carrier track is muted (Pre tap), **When** playing, **Then** the vocoded pad
   is still heard.
4. **Given** the carrier picker, **When** the user opens it, **Then** it lists every other
   track and return with its colour and a **None** entry; this track and its groups are not
   listed; a Pre / Post tap choice sits next to it. Picking a track sets Carrier = Track.

---

### User Story 3 - Shape the character (Priority: P2)

The user tunes the sound: fewer bands for a lo-fi robot, more for intelligibility; moves
the low / high band range by dragging its edges in the graph; narrows Q; shifts formants
up or down to change the perceived size of the voice; sets attack / release for snappy or
smeared articulation; adds unvoiced noise so "s" and "t" stay audible.

**Why this priority**: Without these the effect sounds one way; with them it covers robot,
choir, talk-box and whisper presets.

**Independent Test**: With a fixed carrier and voice, change each control and observe the
expected audible and visual change (graph bar count, range edges, spectrum).

**Acceptance Scenarios**:

1. **Given** Bands = 8, **When** the user sets 32, **Then** the graph shows 32 bars,
   log-spaced between Low and High, and the result is more intelligible.
2. **Given** the envelope graph, **When** the user drags the low or high range edge,
   **Then** the matching knob follows, the edge is accent-coloured while dragged, and the
   band layout updates without clicks or dropouts.
3. **Given** Formant = 0 st, **When** set to +12 st, **Then** the voice sounds smaller /
   brighter; −12 st larger / darker; the pitch does not change.
4. **Given** a sibilant voice, **When** Unvoiced is raised, **Then** high-frequency noise
   follows the sibilants and consonants become clearer.
5. **Given** Hold is switched on, **When** the voice keeps changing, **Then** the band
   envelopes freeze at their current levels (the carrier keeps sounding through them)
   until Hold is switched off.

---

### User Story 4 - Recall, automate and reuse (Priority: P3)

Settings, the carrier track and the tap are saved with the project, survive undo / redo,
can be automated and MIDI-learned like any other device parameter, and come in factory
presets (e.g. Robot choir, Talk box, Whisper, Lo-fi robot, Strings choir).

**Why this priority**: Expected of every built-in device; not needed to prove the effect.

**Independent Test**: Configure the effect, save, reopen: identical sound and carrier track.
Automate Formant across a clip: audible sweep. Load each factory preset on a vocal track:
each is audible and none clips.

**Acceptance Scenarios**:

1. **Given** a configured vocoder, **When** the project is saved and reopened, **Then**
   every parameter, the carrier track and the tap are restored.
2. **Given** the carrier track is deleted, **When** the user undoes the deletion,
   **Then** the vocoder's carrier points to the restored track again.
3. **Given** an agent connected over MCP, **When** it sets Carrier = Track and the device's
   sidechain, **Then** it routes the carrier the same way it keys a Nota Compressor.

---

### Edge Cases

- **Carrier = Track, no track picked** (None): the track passes through unchanged; the graph
  shows one line, "Pick a carrier track · until then the sound passes through"; band knobs
  are shown inactive.
- **Carrier track deleted** while selected: the picker falls back to None and the track
  passes through unchanged; undo restores the carrier track.
- **Feedback**: the vocoder's own track and the groups it sits in are not offered as
  carriers (their signal contains the vocoder's output, which would loop back through the
  one-block route bus). Returns are offered; a return fed by this track can still loop, so
  the envelopes and the wet path are level-limited and cannot run away (research R-12).
- **Carrier is a return / group / master**: returns and groups are allowed; master is not
  offered.
- **Effect bypassed** (power off): the track passes unchanged; the graph stops moving.
- **Low ≥ High**: Low is clamped below High so the range always spans at least one octave.
- **Sample-rate change** or very high band count at 44.1 kHz: bands above the Nyquist
  limit are dropped silently; no instability.
- **Mono / stereo**: a stereo carrier is vocoded per channel with the same envelopes; the
  voice is summed to mono for analysis.
- **Hold on with no voice ever heard**: envelopes are zero; the wet path is silent.
- **Freeze / bounce / offline export**: offline renders are deterministic and do not
  depend on track order (a carrier track comes from the previous block's route bus).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Nota MUST offer a built-in audio effect named **Nota Vocoder** in the
  browser, the command palette, rack menus and MCP's effect list, in the same category
  group as other modulation / creative effects.
- **FR-002**: The **modulator** MUST be the audio arriving at the effect on its own track
  (the voice), summed to mono.
- **FR-003**: The **carrier** MUST be chosen by a segmented control: **Saw** (internal,
  default), **Noise** (internal), or **Track** — another track or return picked in a
  picker, tapped **Pre** (before its effects and fader, default) or **Post**, using the same
  sidechain mechanism, cycle rules and persistence as Nota Compressor. **None** MUST be
  selectable; picking a track MUST set Carrier = Track.
- **FR-004**: The effect MUST split the voice into **Bands** band-pass channels
  (8–32, default 16), log-spaced between **Low** (20 Hz–1 kHz, default 80 Hz) and **High**
  (2–20 kHz, default 8 kHz), with bandwidth set by **Q** (1.0–10.0), follow each band's
  level with an envelope follower set by **Attack** (0.5–100 ms) and **Release**
  (5–1000 ms), and apply each envelope to the matching carrier band.
- **FR-005**: **Formant** (−12 to +12 st, default 0) MUST shift the carrier (synthesis)
  band frequencies relative to the analysis bands without changing the analysis.
- **FR-006**: **Tilt** (−12 to +12 dB, default +3 dB) MUST apply a spectral tilt to the
  Saw or Track carrier before the bands; it is inactive (shown dim) when Carrier = Noise.
- **FR-007**: When Carrier = Saw, the saw's pitch MUST be set by a **Pitch** control in
  the Carrier section (a note, C0–C7, default C3, automatable). Pitch is shown inactive
  otherwise. The saw does not follow notes played into the track.
- **FR-008**: **Unvoiced** (0–100 %) MUST mix high-pass filtered noise into the output,
  shaped by the voice's high-frequency (sibilant) energy.
- **FR-009**: **Mix** (0–100 %, default 100 %) MUST blend the track (dry) and the vocoded
  (wet) signal; **Gain** (−12 to +12 dB) MUST set the output level. The modulator **Gain**
  (−24 to +24 dB) MUST trim the voice before analysis.
- **FR-010**: **Hold** (switch) MUST freeze all band envelopes at their current values
  while on.
- **FR-011**: Every continuous control and Carrier / Hold MUST be an automatable,
  MIDI-learnable device parameter; changes MUST be click-free (smoothed) and MUST NOT
  cause dropouts.
- **FR-012**: The device card MUST follow the spec's design mockup: 700 × 260; header with
  name, preset picker, "Vocoder" badge, power; columns **Modulator** ("This track", Gain,
  key meter) / **Carrier** (Saw · Noise · Track, carrier track picker with Pre / Post, Tilt,
  Pitch) → **Bands** (envelope graph with voice bars, carrier line and draggable Low / High
  edges; knobs Bands, Low, High, Q, Formant, Attack, Release) → **Output** (Unvoiced, Mix,
  Gain, Hold, output meter). It MUST follow `DESIGN.md` in every colour theme.
- **FR-013**: The envelope graph MUST show, live, the per-band voice envelope and the
  carrier's level per band; dragging a range edge MUST change Low / High exactly as the
  knobs do.
- **FR-014**: The effect MUST report zero added latency, or report its latency for delay
  compensation if the chosen design needs any.
- **FR-015**: Parameters, the carrier track and the tap MUST persist in the project and in
  user presets (presets never name a track); the effect MUST ship with at least 5 factory
  presets covering robot, choir, talk-box, whisper and lo-fi characters.
- **FR-016**: MCP MUST expose the vocoder like other built-in effects (add, read / set
  parameters) and MUST reuse the existing device sidechain tools to set and read its
  carrier track.
- **FR-017**: Offline rendering (export, freeze, bounce) MUST be deterministic — two
  renders of the same project are identical. A real-time render may differ from an
  offline one only by a carrier track's one-block routing offset when block sizes differ
  (the same rule as every sidechain consumer; see research R-2).
- **FR-018**: With Carrier = Track and no track routed (None, or its track deleted) the
  effect MUST pass the track's audio through unchanged, fading over about 10 ms.
- **FR-019**: With Carrier = Saw or Noise the vocoded level MUST follow the voice's level
  (at about the voice's own level with the defaults). With Carrier = Track it MUST follow
  the carrier track's level, not the voice's: a voice 20 dB quieter MUST vocode within 3 dB,
  and the result MUST sit within 6 dB of the carrier track alone. A silent voice MUST close
  the bands.
- **FR-020**: Muting the carrier track (or soloing another track) MUST NOT cut its Pre tap,
  so the user can hear the vocoded sound alone. A muted source's Post tap is silent. This
  holds for every keyed device (engine sidechain rule).

### Key Entities

- **Vocoder device**: a built-in audio effect instance on a track's chain; holds the
  parameters above and, for a Track carrier, a carrier track reference.
- **Carrier track**: a reference to another track or return plus a tap point (pre / post),
  or none; the same concept as the compressor's sidechain key.
- **Band**: one analysis / synthesis frequency slot; its centre comes from Low, High,
  Bands (and Formant for synthesis), its width from Q, its level from the voice's
  envelope.
- **Factory preset**: a named set of parameter values shipped with the app (never a
  carrier track, since it depends on the project).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user who has never used the effect hears a robot voice from their own
  track the moment they add the vocoder and press play, with no settings changed; a vocoded
  pad (Carrier = Track, pad picked) takes under 30 seconds.
- **SC-002**: With the default settings, a speech modulator and a sustained chord, a
  listener can understand at least 8 of 10 short spoken test words.
- **SC-003**: Sweeping any control or dragging a range edge during playback produces no
  audible click, dropout, or xrun at a 128-sample buffer.
- **SC-004**: At 32 bands and 48 kHz, one vocoder instance stays within the CPU budget of
  the heaviest existing built-in effect, and a project with 8 instances plays without
  dropouts on the reference machine.
- **SC-005**: A saved project reopened later sounds identical (null test of the exported
  render ≤ −90 dBFS residual).
- **SC-006**: Every factory preset, dropped on the reference voice track, is audible and
  peaks below 0 dBFS.
- **SC-007**: The card passes the design guard tests in every colour theme, and its
  layout matches the design reference at true size.

## Assumptions

- The sidechain source mechanism (track list, pre / post tap, cycle refusal, deletion
  fallback, persistence, MCP tools) built for Nota Compressor can serve any built-in
  effect that declares a sidechain input; the vocoder uses it as is.
- The vocoder is a classic channel vocoder (filter bank + envelope followers); spectral
  (FFT) vocoding and "talk box" style formant-only modes are out of scope for v1.
- The voice is analysed in mono; the carrier keeps its channel count.
- Band count changes may re-lay out bands at a block boundary with a short crossfade;
  it is not required to morph continuously between counts.
- The internal Noise carrier is white noise; the Saw carrier is band-limited.
- Factory presets never name a carrier track; a Track preset passes the voice through
  until the user picks one.
- No new third-party dependency is needed.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
