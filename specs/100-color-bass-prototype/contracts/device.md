# Device contract
Use existing add builtin device, get/set parameter, clone, preset and project APIs. No C ABI changes. Kind 27 is experimental and stable on this branch. All parameters use 0..1. Reject non-finite parameter values and clamp finite out-of-range values. Scope telemetry returns sample rate and latency samples. Display name Nota Chroma. UI controls follow automation gestures, MIDI Learn and live updates.

Scope telemetry: sample rate, latency, 512 dry and 512 tuned log-frequency values (40 Hz–20 kHz). Tuned identifies shifted components and follows Mix.

Appended: 21 Morph [0,1], default 0; 22 Gate off/on, default 0; 23 Gate threshold −80 + 80v dBFS, default .25. Indices 0–20 remain stable.

Index 24 is Mode (normalized, values below 0.5 choose Spectral and other values
choose Polyphonic). Existing layouts remain append-only. Polyphonic consumes
Root, Scale, Custom notes, Amount, Mix and Output. Both modes report the same
latency and scope shape.
