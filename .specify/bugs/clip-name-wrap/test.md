# Bug Test: clip-name-wrap

- **Slug**: clip-name-wrap
- **Tested**: 2026-11-12
- **Fix**: ./fix.md
- **Verdict**: resolved

## Checks

- `scripts/build.sh`: the managed and native builds pass. In the smoke test every curve,
  stroke and section check passes, including the new ones: the steep end has no jump,
  ease-in mirrors ease-out, the bend round-trips through the midpoint, the managed shape
  matches the engine, both sides of a stroke are exact, and section move / duplicate keep
  curved automation. The engine `automationCurveSelfTest` passes.
- Remaining smoke failures: the render-pool load checks ("four indivisible VST3 jobs",
  "overloaded"). They fail the same way on clean `develop` on this machine (Rider running:
  117 late blocks, burst 13), so they are unrelated to this fix.
- Headless render (docs shots harness, Studio Dark, whole song fitted): an "Audio 8" clip
  narrower than its name reads "Audi…" on one line. A low → high (−0.8) → low (+1)
  volume lane eases smoothly on both sides of the peak, with no vertical drop.
