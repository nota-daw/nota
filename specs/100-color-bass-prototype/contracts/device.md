# Device contract
Use existing add builtin device, get/set parameter, clone, preset and project APIs. No C ABI changes. Kind 27 is experimental and stable on this branch. All parameters use 0..1. Reject non-finite parameter values and clamp finite out-of-range values. Scope telemetry returns sample rate and latency samples. Display name Nota Chroma. UI controls follow automation gestures, MIDI Learn and live updates.

Scope telemetry: sample rate, latency, 512 dry and 512 tuned log-frequency values (40 Hz–20 kHz). Tuned identifies shifted components and follows Mix.

Parameter 28 is `FFT size`. Normalized values 0, 0.5 and 1 select 512, 1024 and 2048 samples.
The default is 0.5. The existing project parameter array preserves it. Explicit edits require
stopped transport. Chroma presets and continuous controllers use the sound parameters.

Appended: 21 Morph [0,1], default 0; 22 Gate off/on, default 0; 23 Gate threshold −80 + 80v dBFS, default .25. Indices 0–20 remain stable.

The device reports 1024 samples at all sample rates. Indices 0–23 keep their
existing audio parameters. Index 24 remains a reserved placeholder that
reads as zero and ignores writes. Indices 25 and 26 are Mute low and Mute
high (0 = audible, 1 = muted). Index 27 is View (0 = L, 1 = S) and persists
with the project. Presets include the mutes and omit View and the reserved
slot. Older projects open in L with both mutes off.

Chroma stores the MIDI note source in the existing whole-track sidechain
reference. No source selects Manual. A MIDI source supplies post-MIDI-FX
held notes and timed note events. It leaves the stored manual parameters alone.
A scope buffer with room for 1027 values gets the active MIDI pitch classes
as a 12-bit mask at index 1026. The first 1026 values keep their original layout.
