# Bug Assessment: Nota Chroma spectrum column gaps

- **Slug**: chroma-spectrum-column-gaps
- **Created**: 2026-11-20
- **Source**: User report with screenshot
- **Verdict**: valid
- **Severity**: low

## Symptom

The spectrum in the L card shows regularly spaced gaps wider than the intended
one-pixel separation between active lines.

## Reproduction

Open the live L spectrum at 2× with broadband input. The 522 output columns in
the headless L layout exceed the 512 spectrum samples in each trace. The current
mapping leaves 10 columns unassigned at regular intervals.

## Root cause

ChromaSpectrumView iterates over incoming samples and emits only columns to
which a sample was assigned. The display grid can have more columns than input
samples. Column placement, rather than the input levels, creates the gaps.

## Proposed remediation

Iterate over physical output columns in the existing ChromaDeviceBody.cs
renderer. Preserve peak aggregation for columns containing samples. Interpolate
between neighboring sample centers for an unassigned column and treat non-finite
levels as silent. Keep the physical grid, spectral colors and audio telemetry.

## Checks

Check flat data at 512, 522 and 783 columns, a narrow spike in a compact view
and a silent interval. Inspect live S and L at 1× and 2×, using the headless app.
