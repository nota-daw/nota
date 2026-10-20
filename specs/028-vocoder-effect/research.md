# Research: Nota Vocoder audio effect

Phase 0 of [plan.md](plan.md). Each entry: decision, rationale, alternatives.

## R-1 · How the modulator reaches the effect

- **Decision**: Reuse the device sidechain. `Vocoder::acceptsSidechain()` returns true;
  `sidechainSourceTrackId` / `setSidechainSourceTrackId` / `setSidechain` store the
  source and the per-block buffer exactly like `Compressor.h`. The engine
  (`Engine_Render.cpp`, step 2 of the track render) already hands every sidechain consumer
  its source's route bus before `process()`; `recomputeRouting()` already assigns slots.
- **Rationale**: Routing, the pre / post tap, project persistence (`ProjectModel`
  `SidechainSource` / `SidechainTapPre`, format v16), undo, and the MCP tools all exist and
  are generic over devices. Zero new ABI or format.
- **Alternatives**: A dedicated second input bus on `Device` (a real two-input device) —
  more correct for latency but touches the render loop, routing, ABI and persistence; no
  gain for this request.

## R-2 · Modulator timing (one block late) and FR-017

- **Finding**: Route buses are double-buffered (`Engine.h`, "Signal routing (Phase B)"):
  a consumer reads the source's **previous** block. Offline export pumps `renderOffline`
  with caller-chosen block sizes.
- **Decision**: Accept one block of modulator delay (≤ 2.7 ms at 128 / 48 kHz, ≤ 10.7 ms at
  512). FR-017 is read as: two offline renders of the same project are identical, and a
  real-time render differs from an offline one only by that block offset when the block
  sizes differ. The spec's FR-017 is updated to say so.
- **Rationale**: Vocoder envelopes have ≥ 0.5 ms attack and typically 5–60 ms; a few ms of
  modulator lag is inaudible in use and matches how the compressor behaves.
- **Alternatives**: Render the source track first (order dependency, double render) —
  rejected by the engine's routing design.

## R-3 · Tap labels: "pre-fader" vs "pre-FX"

- **Finding**: `tapPre = 1` taps **pre-FX, pre-fader**; `0` taps **post-FX, post-fader**
  (`Device.h`). The compressor card labels them **Pre / Post**.
- **Decision**: Same tap and labels ("Pre" / "Post" segments at the picker menu foot; tooltip
  "Pre: before the source's effects and fader · Post: after both"). The spec's
  "pre-fader / post-fader" wording is satisfied (pre is also pre-fader); the mockup's
  "Pre-fader / Post-fader" button text becomes "Pre" / "Post" for consistency.

## R-4 · Filter bank

- **Decision**: Each band is a 4th-order band-pass = two cascaded TPT (Cytomic) SVF
  band-pass sections, constant-peak-gain. Three banks of `kMaxBands = 32`: analysis (mono
  modulator), synthesis L, synthesis R. State in fixed `std::array`s. Centres log-spaced:
  `f_i = Low · (High/Low)^((i+0.5)/N)`; synthesis centres × `2^(Formant/12)`, clamped to
  `0.45 · fs`; bands whose centre exceeds that are muted. Bandwidth from **Q** (1–10).
  Coefficients recomputed every 32 samples only when Bands / Low / High / Q / Formant moved
  (compare to cached normalized values), with the old/new Q and centres smoothed per block.
- **Band-count change**: the new layout is staged; output gain ducks to 0 over 5 ms,
  states are reset, the layout switches, gain returns over 5 ms. No allocation.
- **Rationale**: SVFs are stable under modulation (Low / High dragged during playback,
  SC-003), cheap (≈ 6 mul per section), and already used across the engine (Volt, Auto
  Filter). 32 × 3 × 2 sections ≈ 190 SVF ticks / sample — well inside SC-004.
- **Alternatives**: FFT / STFT vocoder (latency + PDC, spectral smearing; spec puts it out
  of scope); 2nd-order bands (too much overlap at 32 bands → mushy).

## R-5 · Envelope followers

- **Decision**: Full-wave rectify the analysis band output, one-pole smoothing with
  separate attack / release coefficients (`exp(−1 / (t · fs))`), times from **Attack**
  (0.5–100 ms, exp map) and **Release** (5–1000 ms, exp map). **Hold** freezes the follower
  (skip the update). **Mod gain** (−24…+24 dB, device param) is applied to the modulator
  before the bank, summed in dB with the base `sidechainGainDb()` (default 0) so
  `set_device_sidechain gainDb` stays meaningful.

## R-6 · Carriers

- **Input**: the track's stereo buffer, processed per channel.
- **Saw**: polyBLEP saw at the **Pitch** note (C0–C7, 85 discrete steps, default C3 =
  130.81 Hz); written to both channels. Pitch change is glided over 10 ms (no click).
- **Noise**: xorshift32 white noise, independent per channel.
- **Tilt** (−12…+12 dB, default +3): first-order shelf pair pivoting at 1 kHz on the
  carrier before the synthesis bank; skipped for Noise (spec: shown inactive).
- Switching carrier crossfades over 10 ms (two sources rendered only during the fade).

## R-7 · Unvoiced (sibilance) noise

- **Decision**: A separate follower on the modulator high-passed at 4 kHz; its ratio to
  the full-band follower (clamped 0..1, squared) gates high-passed (4 kHz) white noise,
  scaled by **Unvoiced** and the HF follower level. Added to the wet path before Mix.
- **Alternatives**: Copy the modulator's top band straight through (cheaper, but leaks the
  voice unprocessed, which users perceive as "dry vocal bleed").

## R-8 · Level and output

- Wet sum is normalised by `sqrt(16 / N) · g(Q)` (calibrated in the smoke test so the
  default preset with a −12 dBFS speech key and a −12 dBFS saw peaks at ≈ −6 dBFS across
  band counts). **Mix** is an equal-power dry / wet blend; **Gain** (−12…+12 dB) last.
- A silent modulator: envelopes decay to 0 → wet silent. No modulator routed at all: the
  track passes through (FR-018, R-14).
- Latency 0; `latencySamples()` not overridden.

## R-9 · Live data for the card

- **Decision**: `scope(float* out, int max)` (existing `nota_device_scope`) returns a fixed
  layout (see [contracts/device-params.md](contracts/device-params.md)): key peak, out
  peak L/R, active band count, band envelopes [32], carrier band levels [32], band
  centres [32]. Written once per block on the audio thread into a member array; the UI reads
  at 60 Hz with torn reads allowed (existing contract).
- **Carrier line**: drawn through the per-band carrier levels (band resolution), not a fine
  FFT spectrum as the mockup suggests — no FFT on the audio thread, and it shows exactly
  what the bands see. The mockup's harmonic comb is illustrative.

## R-10 · Card build (managed)

- `VocoderDeviceBody : IDeviceBody`, `Width = 700`. Columns via `DeviceCardKit.Section`:
  Modulator + Carrier (152) → Bands (fill) → Output (138), as in the mockup.
- Modulator picker: a raised field with the source's track colour
  (`ArrangementView.TrackBrush`) and name (`TrackNames.Of`); click opens a menu built
  like the compressor's source list (every other track / return, "None" first) with a
  Pre / Post `Segments` row at its foot. Writes through `SetDeviceSidechainSource` /
  `SetDeviceSidechainTapPre` + `ctx.NotifyChanged()`.
- Knobs: `KnobCell` × 7 (Bands, Low, High, Q, Formant, Attack, Release); sliders:
  `SliderRow` (Gain, Tilt, Pitch, Unvoiced, Mix, Gain); `Segments` for Carrier;
  `DeviceCardKit.Switch` for Hold; `MeterBar` for key and output. Inactive rules: Tilt
  inactive on Noise, Pitch inactive unless Saw, band knobs + Mod gain inactive with no
  source (`Inactive.Set`).
- `VocoderGraph` (Controls/): `NotaGraph` well; viz-ink bars per band, teal carrier
  polyline, `Rule` edges at Low / High, accent edge only while hovered / dragged; edge drag
  writes Low / High params with `BeginAutomationWrite` / `EndAutomationWrite`; empty state
  "Pick a modulator track to shape the carrier" (Ink 5).
- 60 Hz refresh through `ctx.AddDeviceRefresher` reading `DeviceScope`.

## R-11 · Presets

Six `Fx("vocoder", 26, …)` presets by param name (values normalized):
Init, Robot choir, Talk box, Whisper (Noise), Lo-fi robot (8 bands, Saw), Strings choir
(32 bands, slow release). Folder "vocoder" — add it to `PresetDescriptors`' folder map if
it is not vocabulary.

## Spec adjustments from research

- FR-017 reworded per R-2.
- FR-003 / user story 1.4: tap labels "Pre" / "Post" per R-3 (meaning unchanged).

## R-12 · Feedback through the modulator

- **Finding**: The compressor's picker only hides its own track. For a compressor a loop
  through a group is harmless (gain reduction is bounded); for a vocoder the wet output
  could feed its own key and, with Mod gain up, grow.
- **Decision**: The vocoder's picker hides its own track and every group it is nested in.
  The envelope followers are clamped to 0..4 (≈ +12 dB) after Mod gain, so a loop through
  a return still cannot run away. No change to the compressor's picker.

## R-13 · Implementation notes (from /speckit-implement)

- **Level**: the wet make-up is ×96 with a soft knee above −6 dBFS (never past 0 dBFS), and
  envelopes clamp at 1.0; the test speech over the test chord peaks at about −5 dBFS.
- **Unvoiced** is also gated by the carrier's level, so a silent carrier stays silent (US1.2).
- **Preset audition**: the browser's audition rig has no other tracks, so it would play every
  vocoder preset as silence. `Device::setPreviewKey` (no-op by default, called only by the
  rig) hands the vocoder the phrase as its modulator for that preview.
- **Duplicate track**: `Engine::cloneDevice` did not copy the sidechain source / tap / gain /
  mix for any device; it does now (a compressor's key was lost on duplicate as well).
- **Card**: the Pre / Post tap is a segment row under the picker rather than inside its menu
  (a segmented control in a flyout does not fit the shared menu style).

## R-14 · Level follows the carrier; no modulator passes through (user testing)

- **Finding** (a Volt chord as carrier, the test speech as modulator): with the first build the
  vocoded level tracked the modulator's level one-for-one — −17 dB at the test speech level,
  −26 dB 10 dB quieter, −36 dB 20 dB quieter — while the voice track itself played at −23 dB,
  so a normal vocal left the pad buried under the voice. With no modulator the output was
  −180 dB: adding the effect silenced the track.
- **Decision**: each band's weight is its envelope divided by the L2 norm of all envelopes,
  followed by a slow AGC (5 ms up, 400 ms down) with a floor (0.003) under which the bands
  stay shut; the wet sum takes √N · √(Q/5) · 7 make-up. Measured: the vocoded level stays
  within ~3 dB over a 30 dB range of modulator level and within ~3 dB across 8 / 16 / 32
  bands, and sits at the carrier's level. Unvoiced noise scales with the carrier's level.
- **No modulator** (none routed, or the source track deleted): the output crossfades
  (10 ms) to the input unchanged. The engine no longer gives a route slot to a source track
  that is not in the graph, so a deleted source means "no key" for every keyed device (a
  compressor then keys off its own input, as with no source) instead of a silent key.
- **Mute**: a muted (or solo-gated) source track was skipped entirely, so muting the vocal
  to hear only the pad silenced the vocoder. A sidechain source now keeps rendering for its
  taps (pre tap live, post tap silent while muted) — FR-020.
- **Alternatives**: an output AGC matching the wet level to the carrier's (drifts in
  pauses and pumps); L1 normalisation (8 bands came out 7 dB louder than 16).

## R-15 · The track is the voice; the carrier is Saw, Noise or another track (user testing)

- **Finding**: the user dropped the vocoder on a track with a robot preset and no other track
  and expected that track to come out as a robot voice — the classic hardware / Ableton model
  (the input is the modulator, the carrier internal or external). The first build had it the
  other way round (the input was the carrier, the voice came from another track), so nothing
  vocoded until a second track was picked.
- **Decision**: the input is always the modulator. Carrier (param 8) = **Saw** (0, default) ·
  **Noise** (0.5) · **Track** (1, the device sidechain). The "pad on its own track hosts the
  vocoder" setup is the same sound with the vocoder on the vocal and Carrier = Track (the
  pad) — one model instead of two.
- **Levels**: Saw / Noise weigh bands by the raw envelopes × 6.5, so the robot follows the
  voice's level (measured: at the voice's level with the defaults; −20 dB voice → −20 dB
  out). Track keeps R-14's energy-normalised weights, so the pad sits at its own level
  whatever the voice's. The two blend through the carrier crossfade.
- **No source**: only Carrier = Track with no track passes the voice through (FR-018);
  Saw / Noise never need one. The preset audition's phrase stands in for a missing carrier
  track.
- **Persistence**: param 8 changes meaning (0 was Input). The effect is unreleased, so there
  is no saved state to migrate.
