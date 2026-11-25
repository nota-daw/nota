# Bug Verification: Ceiling true peak

- **Slug**: issue-1088
- **Tested**: 2026-11-25
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Checks performed

The component reproduction measured +0.790 dBTP before and -1.000 dBTP after at both 32 and 257 frames. An independent FFmpeg ebur128 run measured -1.0 dBTP after the fix. `--issue-1088` passed both block sizes through the engine. Component tests also covered three characters and 0/3/10 ms lookahead, plus deterministic noise.

## Residual risks

The desktop render and changing settings during playback have not been exercised. Independent measurement was run for the reported Clean/3 ms fixture only.

## Recommendation

Run the complete suite and benchmark the final tree; verify the outstanding desktop cases.
