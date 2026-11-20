# Chroma root picker verification

- **Slug** chroma-root-clipping
- **Tested** 2026-11-20
- **Assessment** [assessment.md](assessment.md)
- **Fix** [fix.md](fix.md)
- **Result** Verified

The original large card trimmed C# and G# in the headless reproduction.
The fixed card displays all 12 root names at 1× and 2× across all four
themes. The test checked the laid-out text and the gaps between the root,
scale and MIDI controls. The MIDI caption fits and the Release build passes.
