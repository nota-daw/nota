# Bug assessment — Chroma CPU use in silence

- **Slug** chroma-idle-dsp
- **Source** User reports about 30% DSP with Chroma during silence
- **Verdict** Valid
- **Severity** Medium

Chroma reaches its idle flag after the audible tail fades. A caller that
continues rendering its chain, such as the master bus or a rolling audio
track, still invokes `process`. Chroma runs its filters, correction FFT and
both scope analyses on those silent blocks. A direct reproduction after an
audible tone and three seconds of silence confirmed that the flag is true
and the output is zero while the device still uses callback time.

Implement sleep inside Chroma. Preserve the existing tail floor and minimum
quiet time. Require sanitized silent input, since even quiet nonzero input
must retain its sample delay. Clear telemetry once at sleep, skip silent
samples while keeping the sample clock, and restart with the current settings
and fresh DSP history when input returns. Apply an FFT size change while
sleeping. Cover silence, wake, delay, tails, weak signals and block
partition invariance with the native and C ABI tests. Compare callback
costs before and after with the same silent input.
