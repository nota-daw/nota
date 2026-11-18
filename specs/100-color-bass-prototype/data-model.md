# Data model
Device kind 27. Append-only normalized parameter layout: Root (0..11), Scale (Major/Minor/Major pentatonic/Minor pentatonic/Custom), Amount, Resonance, Decay (20..1000 ms exponential), Low (40..2000 Hz exponential), High (1000..20000 Hz exponential), Mix, Output (-24..12 dB), Note C through Note B (12 booleans, absolute classes). Defaults root C, minor, amount 1, resonance .25, decay 120 ms, band 150..8000, mix 1, output 0 dB, custom C minor. Editing notes in the card first copies the current effective mask, then switches to Custom and applies the edit. Native setters have no cross-parameter side effects, ensuring ordered generic persistence is exact. Custom empty mask preserves delayed input.

Append-only additions: Morph (21, default 0), Gate (22, off/on, default off), Gate threshold (23, −80..0 dBFS, default −60). Spectral shaping shares gains between stereo channels.

Clean iteration adds transient detector, selected-note, peak-motion and placement-transition
state only to prepared DSP buffers. This state is not serialized or exposed as parameters.
Reset it on sample-rate preparation; carry it through matched peak identities during render.
The parameter count stays 24, and no project format or C ABI changes are needed.

Polyphonic adds Mode (24, Spectral by default) to generic effect persistence.
The audio instance holds fixed pitch-tracking slots, up to four active voice
assignments, and temporary per-peak evidence. A target mask change recomputes
voicing without changing the sustained input notes. DSP history is transient.
