# Nota Color prototype — verification

## Implemented
- Built-in audio effect kind 27, full card, root/four scales/Custom note mask, coloration/band/mix/output controls.
- Three presets; palette/category integration, shared factory and rack factory registration.
- Generic parameter automation, clone and complete project recall, verified through the C ABI.
- No new dependency or ABI entry point. All working FFT/OLA state is allocated in preparation, not process().

## Evidence
- Native DSP: 25 passing checks in `build/color-prototype/results.txt`. Four sample rates (44.1/48/88.2/96 kHz), target pitch within 10 cents (440 → 415.3047 Hz), dual independent partials, mono/antiphase/right-only stereo, subbass, empty mask, exact delayed dry, arbitrary block sizes, non-finite input/params, changing controls, bounded resonance and decaying tails. Global operator-new instrumentation sees zero render allocations.
- Focused managed test (`--color`): six checks passed, including complete 21-parameter clone and save/load round-trip, presets and audible finite audio. Initial design/catalog checks also pass after updating expected catalog size and text metadata.
- Card rendered and visually inspected in Studio Dark and Studio Light through the real Avalonia headless application, not a mockup. Images are in sibling nota-docs `src/assets/shots/{dark,light}/devices/audio-effects/color/card.webp`.
- EN/RU manual pages and device index updated in sibling nota-docs on its `feat/color-bass-prototype` branch. `npm run build` succeeded (340 pages); `npm run check` succeeded (0 errors, translation/staleness warnings elsewhere).
- WAVs: `build/color-prototype/{fm,growl,chord}-{dry,wet}.wav`, stereo PCM16, 48 kHz, 5 seconds. Wet latency is removed from listening copies only, then RMS level matched; common attenuation prevents clipping. DSP processing keeps its reported latency.

## Limits and listening acceptance
Latency is 2048 samples at 44.1/48 kHz and 4096 at 88.2/96 kHz (46.44/42.67 ms). One quality mode; no MIDI chord following. Exact Chroma equivalence has not been established: no Chroma instance/reference recordings were provided. Integer-bin peak-region placement and exact phase advance can still produce artifacts on moving/overlapping partials; the user's audition determines whether another DSP iteration is needed.

Use Mix=0 for a time-aligned dry comparison. Host bypass retains existing Nota behavior (the engine skips bypassed latent devices).

Documentation changes remain local and staged: the requested docs commit could not be signed because the configured GPG pinentry has no usable terminal; signing settings were preserved. No push or PR was made.

## Performance
Final standalone measurements at 48 kHz / 512-frame blocks: FM 75.24 ms, growl 79.21 ms and chord 66.30 ms to process 5 seconds (1.50%, 1.58% and 1.33% of one realtime core). These are local wall-clock measurements, not a cross-platform CPU guarantee. No algorithm or latency quality switch is exposed.

## Full build
Initial full run found one descriptor-vocabulary error in the new palette entry; the metadata was corrected to existing `bright` / `clean` vocabulary. Final `scripts/build.sh` exited 0: native universal arm64+x86_64 build, scanner self-test, managed Release build, complete smoke suite, plugin scan scale check, default scan-path and upgrade checks all passed. Log: `/tmp/nota-color-final2-build.log`.


## Convergence
Converged: all 8 functional requirements, 5 success criteria (with subjective audition explicitly left to the user), 3 user journeys and the implementation decisions were checked against the resulting code and evidence. All 10 tasks are complete. No new tasks were needed. Real-time, ABI, licensing and UI boundaries are preserved; issue deferral remains the explicit user-authorized exception for this local prototype. No extension hooks are configured.

## Spectrum update
Native Color tests pass for separated telemetry, Mix zero, in-key tones, short buffers and allocation-free processing. The standalone 48 kHz measurements were 2.18%, 2.06% and 2.37% realtime for FM, growl and chord; earlier prototype measurements were 1.50%, 1.58% and 1.33%, so the visualization adds measurable processing cost (individual runs vary). Documentation EN/RU build and check passed. Dark and light card screenshots were regenerated with silent input; active spectrum appearance still needs visual audition.

Full scripts/build.sh: smoke test failed two overloaded render-pool deadline checks (62/4125 late blocks; 6 in a burst). An isolated --perf rerun also failed those two checks (82/4125 late; 7 in a burst). Color smoke checks passed. These checks do not specifically exercise Color.

### Smooth spectral line correction
Replaced stepped opacity tiles with continuous theme-aware gradients and narrow cores. Release build and focused Color/design smoke tests pass. The actual app was rendered with a playing pad in Studio Dark and Studio Light; both active spectra were visually inspected without horizontal banding. The docs manifest now includes the playing clip. See .specify/bugs/color-spectrum-banding/.

## Naturalness extension — 2026-11-17
- Implemented local one-to-one peak tracking and phase propagation across bin/reference-channel changes. New peaks start with input phase; stereo uses a common rotation.
- Appended normalized parameters 21 Morph, 22 Gate, 23 Gate threshold. Defaults 0/off/−60 dBFS. Morph uses stereo-linked magnitudes at matching frequencies; Gate uses dry spectral sidechain, 6 dB knee, 5/50 ms smoothing. Both act after resonance inside the processing band and update Tuned telemetry.
- 50 native checks passed in `/tmp/nota-color-natural-verified2-native.log`, including phase continuity, 10-cent tuning, Morph endpoints/intermediate value/carrier phase, Gate attenuation/tails, stereo phase/reference changes, automation, four sample rates, silence and block invariance. Render allocation counter remains zero. Strong shaping changes the 55 Hz outside-band waveform by at most 1.67e-5 in these tests due to window leakage; original unshaped dry reconstruction checks retain 1e-5 tolerance.
- Focused managed smoke checks passed: 24-param identity, clone and save/load, legacy 21-param state with off defaults, presets, audible finite output and telemetry, plus initial design/localization guards.
- Actual active card rendered and inspected in Studio Dark and Studio Light, 560×260, second control row fitting and graph above its 96px minimum. Documentation EN/RU and active screenshot manifest updated; docs build produced 340 pages and check reported 0 errors (89 existing warnings).
- Listening: `build/color-prototype/before-natural/*-wet.wav` is the pre-change engine from HEAD compiled with the same five-scene generator; current `*-wet.wav` uses improved phase with shaping off, and `*-natural-wet.wav` uses Morph .35 and Gate −60 dBFS. Five scenes: FM, growl, chord, attack and decay. Copies are latency-trimmed and RMS-matched; source audio and live latency are unchanged. `natural-comparison.csv` records peak/sample-step/tail metrics. Naturalness remains for human listening evaluation.
- Local costs for 5 seconds at 48 kHz: prior engine FM/growl/chord 88.54/93.29/81.33 ms; improved phase about 102/106/87 ms, Morph/Gate about 105/109/90 ms. This is a measurable CPU increase, with run-to-run timing variation; no claim of a free performance improvement.
- Full `scripts/build.sh` built universal native and managed Release successfully, but the complete smoke run failed only the two known overloaded-render-pool deadline guards: 168/4125 late blocks and maximum burst 17 (limits 1% and 4). All Color checks passed. Full log: `/tmp/nota-color-natural-full-build.log`. These pre-existing unrelated performance failures were also present before this extension; a green full-suite result remains outstanding.
- Convergence review: FR-010–015 and T012–016 are implemented, with native, managed, old-state, active UI and docs evidence above. Global SC-004 remains unverified because of those render-pool failures; tracked separately as T017. Subjective audition remains open, as explicitly intended by the prototype.

## Clean color bass iteration — 2026-11-18

- The checked-in runner `scripts/test-color.sh` compiles the production effect and
  prepares the same sources for the baseline and current renders. On macOS with
  Accelerate, the native suite reports **60 checks, 0 failures** for the current build
  and **24 failures** on baseline `1015a76d` under the same new criteria.
- At 44.1, 48, 88.2 and 96 kHz, boundary tones of 220, 440, 880 and 1760 Hz
  settle at the lower allowed note. The steady output level is within 0.8 dB of the
  input at Resonance=0 and the residual outside the target sinusoid is below −81 dB.
  Vibrato stays in the held note, a sweep past the hysteresis band changes notes,
  and a new tone after silence resolves its own tie. Mono, antiphase and right-only
  checks, arbitrary block sizes, old latency and allocation instrumentation pass.
- The first 5 ms of three repeated test attacks has an error of −4.37 dB relative
  to the aligned input, down from +3.01 dB on the baseline. This is a test of onset
  shape preservation. Crossing partials and fast automation remain finite and bounded.
- With Gate off and maximum Decay, the tail 0.5–0.75 seconds after input stops
  measures −21.84 dBFS RMS, versus −70.50 on the baseline. The new Gate test confirms
  attenuation of the long tail. Sleep waits for output and pending synthesis history
  to drop below −90 dBFS and new input wakes the device.
- The C ABI smoke checks cover the 24-parameter layout, old-state restore and
  the boundary tone. All six presets apply, and the full audition test renders
  them audibly. The EN and RU manual builds to 340 pages and checks with zero errors.
- Listening material is in `build/color-prototype/clean/{baseline,current}/`.
  It includes six generated sources and two recordings. `recording-1` is
  [Funky Slap Bass line by Serolillo](https://commons.wikimedia.org/wiki/File:Funky_Slap_Bass_line.ogg)
  ([CC BY 2.5](https://creativecommons.org/licenses/by/2.5/)). `recording-2` is
  [Heavy metal gallop on bass with a pick by Skimel](https://commons.wikimedia.org/wiki/File:Heavy_metal_gallop_on_bass_with_a_pick.ogg)
  ([CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)).
  Both excerpts were trimmed, converted to 48 kHz stereo, peak-scaled and processed.
  The modified excerpts retain their source licenses. The WAVs are local build outputs.
- On an Apple M2 at 48 kHz with 256-sample blocks, the median wall time of five runs fell by
  about 9–35% across these eight local scenes. Audio-thread math now reuses each
  peak's phase factor and caches each bin's resonance note and rotation for the
  selected scale. The slap recording's p99 rose from 229 to 295 μs. Maximum block
  times were sensitive to scheduling. The paired CSVs retain medians, p99, maxima,
  raw levels and normalization gain for each scene.
- The final `scripts/build.sh` built the universal native engine and managed Release
  successfully. The full smoke test passed all Chroma checks and the factory preset
  auditions. The only two failures were the existing 64-frame overloaded render-pool
  deadlines: 75/4125 late blocks after warm-up (limit 1%) and a maximum burst of 11
  (limit 4). That scenario uses audio playback tracks without Chroma.
  The plugin scan scale, default paths and upgrade checks passed when run separately.
- Convergence review covers FR-016–021 and T018–025. The Chroma implementation and
  evidence are complete. The shared full-suite gate (SC-004) remains open as T017.
  Subjective listening acceptance is pending the user's audition.

## Polyphonic scale correction — 2026-11-18

- Native harness: **80 checks, 0 failures**. C4–E4–G4 becomes C4–Eb4–G4, and
  E's overtone follows, at 44.1, 48, 88.2 and 96 kHz. All four target pitch
  checks meet ±10 cents. In-scale chords reconstruct with the original
  latency. A minor, a four-voice inversion, Custom scale assignment without
  collisions, and changing scale on held notes pass. Close E4/Eb4 fundamentals
  and E2 outside the correction range stay intact in the regression fixtures.
- The audio tests verify a live mode switch through time-aligned dry audio,
  arbitrary blocks, antiphase stereo and zero C++ render allocations. The
  Spectral suite passes as part of the same run.
- The actual 560×260 card was checked in Studio Dark and Studio Light.
  Both modes have screenshots. In Polyphonic the band handles are absent and
  the spectral controls are dimmed. The EN/RU manual builds and checks with
  zero errors.
- A/B files in `build/color-prototype/polyphonic/current` play the RMS-matched
  original first and Polyphonic second. They use
  [Piano C major chord by Em3rgent0rdr](https://commons.wikimedia.org/wiki/File:Piano-C-major-chord.ogg)
  ([CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)) and
  [C major triad inversions by Rosier-HR](https://commons.wikimedia.org/wiki/File:Chord-triad-C-major-inversions.ogg)
  (public domain). The excerpts are trimmed to 4.5 seconds, resampled and
  level-adjusted. Processed excerpts retain their respective source licenses.
- In the first piano recording, a 250 ms analysis window starting 200 ms after
  the start measured E4 amplitude 0.02188 dry and 0.00010 wet, and Eb4 amplitude
  0.00013 dry and 0.02238 wet. C4 and G4 stayed within 0.12 dB in that window.
  These measurements verify the intended conversion on the reference audio.
  Subjective timbre and attack acceptance require listening.
- On Apple M2, the two piano excerpts took about 81–88 ms median wall time per
  five seconds of input at 48 kHz with 256-frame blocks. The observed p99 was
  about 0.15 ms per block over five runs. The CSV includes the measured maximum
  and output level for each scene.
- The universal macOS engine and .NET Release build succeeded. The focused
  C ABI test confirms the corrected triad, scope and latency, mode persistence
  through clone/save/load, and Spectral defaults for older projects and presets.
  All 1,549 factory presets covered by the full audition test render audibly.
- Full smoke reported a MIDI-controller timing failure and the two existing
  overloaded render-pool timing failures (162/4,125 late blocks and a burst of
  seven). The MIDI-controller suite passes in isolation. Those timings concern
  scenarios without Chroma. The shared full-suite gate remains open as T017.
- Convergence: FR-022–025 and T026–030 are covered by the current code, tests,
  recordings and manual. Subjective listening and the shared build gate remain.
