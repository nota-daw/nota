# Bug Fix: Nota Chroma spectrum column gaps

- **Slug**: chroma-spectrum-column-gaps
- **Fixed**: 2026-11-20
- **Assessment**: [assessment.md](assessment.md)
- **Status**: applied

The renderer now visits each display column. It takes the peak of the sample
centers in the column or interpolates between sample centers when there are
none. Dry and Tuned use the same sampling rule. The renderer still draws one
physical pixel per active column with a one-pixel gap.

The focused headless verification probes the production column sampler and
captures the rendered spectrum at 1× and 2×. The documentation shots were also
refreshed for both card sizes and both Studio themes.
