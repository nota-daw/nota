# Device contract
Use existing add builtin device, get/set parameter, clone, preset and project APIs. No C ABI changes. Kind 27 is experimental and stable on this branch. All 24 parameters use 0..1. Reject non-finite parameter values; clamp finite out-of-range values. Scope telemetry returns sample rate and latency samples. Display name Nota Color (prototype). UI controls follow automation gestures, MIDI Learn and live updates.

Scope telemetry: sample rate, latency, 512 dry and 512 tuned log-frequency values (40 Hz–20 kHz). Tuned identifies shifted components and follows Mix.

Appended: 21 Morph [0,1], default 0; 22 Gate off/on, default 0; 23 Gate threshold −80 + 80v dBFS, default .25. Indices 0–20 remain stable.
