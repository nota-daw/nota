# Research: EQ spectrum analyzer

## R1. What is wrong with today's analyzer

`Eq::analyse()` (Eq.cpp) already uses a Hann-windowed 4096/8192-point FFT, so resolution is not
the problem. What reads as coarse and jumpy:

- **96 points**, drawn as a 1 px polyline; at a 400 px card that is a visible step every 4 px.
- **Bands narrower than a bin** below ~300 Hz: `k0 = k1 = round(centre / binHz)`, so several
  points repeat one bin and the low end is a staircase.
- **Symmetric smoothing**: a 200 ms one-pole on power, both up and down. Hits are smeared on the
  way up and the shape keeps wobbling with the noise of each frame.
- **Freezes on silence**: when the ring stops advancing (`w == lastAnaW_`, e.g. EQ asleep) the last
  shape is returned as is.
- **Line only**: no fill, so it competes with the EQ curve instead of sitting under it.
- **A ring bug (the main cause)**: in `Eq::process` the post-ring loop carried on from the pre
  pass's write index instead of starting at the block's, so each block wrote its pre samples at
  [w, w+F) and its post samples at [w+F, w+2F), and the index advanced 2F per block. Every FFT
  window was half stale data, in both rings, which is what made the spectrum jump from frame to
  frame. Fixed by restarting the post loop at the block's first index. Audio is not affected.

EQ-3 (Eq3.cpp) has a copy of the same code with one ring.

## R2. Band energy instead of tilt

Decision: each of 256 points sums bin power over a 1/6-octave band around it.
Rationale: a constant-Q band holds all of a sine's energy (Hann main lobe ±1 bin), so a sine reads
its level (+0.5 dB for the Hann lobe) wherever it sits between bins; and pink noise has equal
energy per fraction of an octave, so it reads flat with no tilt parameter. Bands overlap (point
spacing 1/25.6 octave), which is the smoothing.
Alternatives: per-bin magnitude plus a +3 or +4.5 dB/oct display tilt (Lens, Pro-Q): a sine then
reads off its level by the tilt, and a smoothing pass is needed; rejected for the EQ cards.
Below the frequency where a 1/6-octave band is narrower than a bin (about 100 Hz at 4096/48 kHz)
the point interpolates the two nearest bins' power. Noise there reads per bin (rising 3 dB/oct
towards the bottom); sines still read right.

## R3. Ballistics

Rise: one-pole in dB with τ = 20 ms (at 30 updates/s that is 80 % of the way per frame, reads as
instant but takes the edge off frame noise). Fall: linear in dB at 66.7 dB/s (20 dB in 300 ms).
Peak: holds 1.5 s, then falls at 33.3 dB/s, never below the curve. Silence: if the ring has not
advanced since the last update, the target is the floor (−150 dB), so the curve falls away.

## R4. Where the analyzer lives

`src/dsp/primitives/SpectrumAnalyzer.{h,cpp}`, a plain class with no locks: the owning device
calls `analyse()` under its own `mx_` from `scopeRead` (the card's reading thread), as today.
It reads the device's existing power-of-two ring, so the audio thread is untouched (FR-001,
SC-006). Signalsmith RealFFT, as Eq already uses.

## R5. Parameters and persistence

Two params appended to EQ-8 (`AnaRange` 0/1/2 = 60/90/120 dB, default 1; `AnaPeak` 0/1, default
0), making `kNumParams` 62. Device params are stored by index; an older project has 60 values and
leaves the two at their defaults. The UI already guards on `DeviceParamCount > AnalyzerP`; the
new ones get the same guard. Neither param is read by `process()`, so audio is bit-identical.

## R6. Telemetry layout

EQ-8: `kTele` 16, then input curve, output curve and peak curve, 256 floats each (`S_Bands` =
256). The peak curve follows what is shown: the output on Post, the input on Pre. EQ-3: `kTele` +
256. Both cards read `kScope` from the same constants; nothing else reads these layouts (MCP
`read_eq` reads only the telemetry block).

## R7. Drawing

A shared `SpectrumFill` helper in the app: a closed StreamGeometry from the curve down to the
bottom edge, filled with a low-alpha neutral wash; points below the range floor clamp to the
bottom so silence draws nothing visible. The input on Post is a dim 1 px line; the peak a 1 px
line at a slightly higher alpha. EQ-3 uses the same helper with its own dB mapping.
