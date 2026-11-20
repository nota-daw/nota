# Data model
Device kind 27. Append-only normalized parameter layout: Root (0..11), Scale (Major/Minor/Major pentatonic/Minor pentatonic/Custom), Amount, Resonance, Decay (20..1000 ms exponential), Low (40..2000 Hz exponential), High (1000..20000 Hz exponential), Mix, Output (-24..12 dB), Note C through Note B (12 booleans, absolute classes). Defaults root C, minor, amount 1, resonance .25, decay 120 ms, band 150..8000, mix 1, output 0 dB, custom C minor. Editing notes in the card first copies the current effective mask, then switches to Custom and applies the edit. Native setters have no cross-parameter side effects, ensuring ordered generic persistence is exact. Empty and full Custom masks preserve delayed input through Output.

Append-only additions: Morph (21, default 0), Gate (22, off/on, default off), Gate threshold (23, −80..0 dBFS, default −60). Spectral shaping shares gains between stereo channels.

The HQ instance prepares mid/full and separate low/high delay lines, two independent mask paths
with fixed-capacity IIR banks, two HQ shifters per path, and spectral-analysis
scratch. Filter history, detector state and scope data reset on sample-rate
preparation and are transient. Slots 0–23 retain their audio parameters. Slot 24 was
Mode in a past prototype and old values are ignored. Slots 25 and 26 hold
the sideband mutes. Slot 27 holds the editor View (L = 0, S = 1).
