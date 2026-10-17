**Native Chord Resonator for Dub Techno and Expressive Sound Design**

Create a native Nota audio effect centered on rich, tunable resonance: turn percussion, noise, loops, instruments and textures into playable chord stabs, ringing tones and evolving harmonic spaces.

My main sound reference is Ableton Live’s Resonators. Its character is especially important to me for dub-techno chords. The goal is to cover its functional scope and achieve a closely comparable sonic response, then add useful musical control without making the interface complicated.

**1. Reference baseline to preserve**

Provide:
- Five parallel, audio-excited resonators.
- A pre-resonator filter with low-pass, band-pass, high-pass and notch types, plus frequency control.
- Two contrasting resonance characters corresponding to the reference’s natural A behavior and low-register B character.
- Global decay, pitch-dependent ringing and a constant-decay option.
- Brightness/color control.
- Root tuning covering C−1 to C5; four relative voices covering ±24 semitones or ±21 scale degrees.
- Individual cents tuning, enable and gain for every resonator.
- Independent root configuration even when its resonator is muted.
- Scale-aware root selection and alternative tuning-system support.
- A reference stereo configuration: voice I receives L+R; II/IV receive L; III/V receive R.
- Wet-only width, collapsing the side voices to mono at zero.
- Resonated-signal gain and dry/wet balance.

These capabilities form the baseline, not proof of identical DSP or sound.

**2. Sound quality is the central requirement**

The effect should respond musically to the actual input signal. It should not simply replace incoming audio with an unrelated synthesized chord.

Prioritize:
- Clear, punchy chord attacks.
- Rich ringing that retains the character and dynamics of the excitation.
- Dark, warm dub stabs as well as bright, metallic and airy tones.
- Defined harmony at long decay settings.
- Controlled low-frequency buildup.
- Useful subtle settings, not only dramatic special effects.

Do not sacrifice the core resonator sound to add more modes. Extras should be independently bypassable.

**3. Simple front panel, deep editing when needed**

Use a compact main view with:
- Root and chord/voicing.
- Decay.
- Damping/tone.
- Character.
- Stereo width.
- Excitation amount.
- Wet/dry balance.

Expose detailed controls through clearly labelled expanded sections. Keep all baseline functions accessible, with exact values, units, default indicators and tooltips.

Show the active chord notes and intervals visually. A chord preset should populate editable voices, not hide how the chord is constructed.

**4. Musical chord and tuning workflow**

Add:
- Chord templates such as minor, major, suspended, sixth, seventh and ninth voicings.
- Inversions, octave shifts, spread voicings and individual voice editing.
- A minor-nine starting preset such as 0, +3, +7, +10 and +14 semitones.
- Project-key/scale following with an explicit opt-out.
- User chord memories and import/export of device presets.
- Optional additional resonators for extended harmony, with visible CPU cost and a practical limit.
- Independent voice solo for tuning and troubleshooting.
- A choice between semitone, note-name, frequency and tuning-relative displays where appropriate.

**5. Optional MIDI and expressive performance**

Audio-only use must remain fully practical. MIDI should be optional.

Provide distinct modes:
- Fixed chord: incoming audio excites a manually configured chord.
- Root transposition: one incoming note transposes the configured voicing.
- Played chord: incoming notes define the resonator pitches.
- Chord capture/hold: store a played chord and continue using it without holding the keys.

Define note priority, note-off behavior, retriggering and sustain clearly. Distinguish multiple resonators within one chord from several independent chord banks sounding polyphonically.

Optional expression can map velocity, pitch bend, pressure or MPE to suitable controls, with explicit ranges and predictable voice allocation.

**6. Detailed decay, damping and stereo**

Beyond the baseline:
- Per-voice decay offsets and optional independent decay.
- High- and low-frequency damping separate from overall duration.
- An intuitive material/tone control for how brightness changes through a tail.
- Per-voice pan and detuning alongside the reference stereo configuration.
- Mono compatibility monitoring.
- Optional very subtle drift with bounded depth and repeatable behavior.
- Clear separation between changing the input spectrum and changing an already-ringing tail.

**7. Chord transitions and tail behavior**

Make harmony changes intentional:
- Retune an existing tail.
- Let the previous chord decay while the new chord starts.
- Replace the old tail with a short click-suppression transition.

Offer immediate or tempo-quantized changes where useful, plus configurable glide or morphing. Quantization must indicate its intentional scheduling delay.

Define behavior on note-off, transport stop, bypass, preset changes and project reload. Provide a panic/tail-clear action.

A hold/freeze function could be valuable, provided it has explicit level management and a safe exit.

**8. Excitation and dub workflow**

Provide separate input-send and wet-output controls so users can feed a brief sound into the resonator and let it ring after the send is reduced.

Useful optional additions:
- Input filter bypass and clean gain staging.
- Transient emphasis or softening.
- Gentle drive with explicit input/output compensation.
- External sidechain excitation.
- A clearly labelled audition impulse or noise burst for tuning without playback.
- Wet-only audition and optional automatic level matching for comparisons.

Use Nota racks or existing devices for tempo-synced delays and dark reverbs. Supply ready-to-use dub chains without forcing a duplicate delay/reverb system into the resonator itself.

**9. Optional advanced sound families**

After the core chord sound is convincing, consider:
- Harmonic-density or overtone-character choices.
- A modal/material mode that changes overtone relationships while retaining each voice’s fundamental.
- Subtle LFO, envelope-following and random modulation through Nota’s existing modulation system.
- Parameter locks during preset browsing or constrained randomization.

These are proposed extensions. Spectral processing, extensive physical modeling and automatic audio pitch tracking should remain optional investigations rather than prerequisites for a usable chord resonator.

**10. Performance, safety and integration**

- Implement a real-time-safe, low-overhead audio path.
- Aim for no additional buffering in the core mode where technically feasible; report any unavoidable processing latency accurately.
- Keep graphics and background work off the audio thread.
- Avoid processing disabled voices unnecessarily while respecting chosen tail behavior.
- Ensure stable operation at extreme decay, repeated excitation and rapid automation.
- Provide input/output metering, headroom indication and clearly disclosed overload protection.
- Avoid hidden limiting that changes the sound during reference comparisons.
- Preserve settings and mappings through save/load and undo/redo.
- Support automation, MIDI mapping, keyboard operation and precise numerical entry.
- Keep controls comfortable for mouse, trackpad and touch.

**11. Acceptance criteria**

Functional coverage alone is insufficient. Compare the implementation against the reference using the same dry audio and matched perceived loudness.

Test:
- Short noise bursts, percussion, chord stabs, sustained textures and live instrument recordings.
- Dark minor-nine chords with both short and long tails.
- Low and high notes with pitch-dependent and constant decay.
- Left-only and right-only excitation.
- Detuning, mono playback and downstream dub delay/reverb.
- Chord changes, bypass, silence and extreme parameter settings.
- CPU usage and latency under realistic project load.

Use listening tests alongside impulse-response, tuning, spectral and decay measurements. Document audible differences rather than claiming perfect equivalence before it has been established.

**Goal**

A musically inspiring chord resonator with the sonic depth needed for dub techno, an immediately understandable default interface, and optional advanced control for demanding sound design.

**References and inspiration**
- [Ableton Resonators: principal functional and sonic reference](https://www.ableton.com/en/live-manual/12/live-audio-effect-reference/#resonators)
- [Ableton Spectral Resonator: expressive musical control](https://www.ableton.com/en/live-manual/12/live-audio-effect-reference/#spectral-resonator)
- [Ableton Corpus: resonant-body and tail-control concepts](https://www.ableton.com/en/live-manual/12/live-audio-effect-reference/#corpus)
- [Kilohearts Resonator: compact controls and harmonic character](https://kilohearts.com/products/resonator)
- [AAS Objeq Delay 2: modal/material coloration](https://www.applied-acoustics.com/objeq-delay-2/manual/c-objeq/)

The extensions above are suggestions for Nota, not claims that every reference includes them.
