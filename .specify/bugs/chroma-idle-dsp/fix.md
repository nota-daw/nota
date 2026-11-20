# Bug fix — Chroma CPU use in silence

- **Slug** chroma-idle-dsp
- **Assessment** [Chroma CPU use in silence](assessment.md)
- **Status** Applied

Chroma starts asleep with empty history. After the input has stayed silent
and the output has stayed below the existing −90 dBFS tail floor for the
settling period, it clears the scope and skips the silent frames. It keeps
the sample clock and wakes on the first stereo frame with a usable sample.
Wake restores the current controls and starts with fresh filter, FFT and
delay history. Selecting an FFT while asleep updates the configured delay.

The native regression covers the tail, empty scope, low-level right-channel
input, all FFT sizes, allocation and block partitioning. The C ABI smoke
test also verifies that Chroma on the master bus wakes with sample-exact
delay in the live render path. The changelog and EN/RU manual cover sleep.
