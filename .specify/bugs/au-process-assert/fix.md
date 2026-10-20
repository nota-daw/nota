# Bug Fix: Audio Unit plug-ins get 31 channels per block — JUCE assertion and dropouts

- **Slug**: au-process-assert
- **Fixed**: 2026-10-20
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The 31-channel headroom from `plugin-add-crash-windows` (06057f89) now applies to VST3 plug-ins only. Any other format (Audio
Unit) gets a process buffer exactly as wide as its current layout, `max(1, in, out)`. That is no wider than the layout JUCE's AU
wrapper was prepared for, so `jassert(buffer.getNumChannels() <= preparedChannels)` no longer fires and the wrapper no longer
grows its `inputBuffer` on the audio thread or copies 31 channels per block.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/pluginhost/PluginHost.cpp` | modified | `pluginChannelPtrs()` takes a `minChans` floor. New `minProcessChannels()` (VST3 → `kNoAllocChans`, else 0). `PluginInstrument` / `PluginEffect` cache it as `chanFloor_` in `prepare()` / `setSampleRate()` on the message thread, next to the other prepare-time state the audio thread reads |

## Diff Highlights

```cpp
int pluginChannelPtrs(const juce::AudioProcessor& p, juce::AudioBuffer<float>& storage,
                      float* spare, int frames, int minChans, float* (&ptrs)[kMaxChans]) {
    const int have = juce::jmin(storage.getNumChannels(), kMaxChans);
    const int want = juce::jmin(kMaxChans, juce::jmax(minChans > 0 ? juce::jmax(minChans, have) : 1,
                                                      p.getTotalNumInputChannels(), p.getTotalNumOutputChannels()));
    ...
}

int minProcessChannels(const juce::AudioPluginInstance& p) {
    return p.getPluginDescription().pluginFormatName == "VST3" ? kNoAllocChans : 0;
}
```

For VST3, `max(max(31, have), in, out)` is the old `max(31, have, in, out)`: the behaviour is unchanged. For AU, `storage_`
(padded to ≥ 2) no longer counts, so a mono AU gets 1 channel. The callers read channel 1 only when `stereoOut_`, which implies
≥ 2 output channels.

## Tests Added or Updated

None in the repo. The opt-in `--hostcheck` mode of the smoke test already hosts an AU instrument and an AU effect. It can't see
the assertion, though: JUCE prints it to the native stderr and keeps going, and the smoke test has no hook into that.

## Local Verification

Engine built in the existing Debug configuration (`cmake --build src/native/nota.engine/build`, assertions on). Run with
`Nota.SmokeTest --hostcheck build/nota-scanworker`, `NOTA_DATA_DIR` isolated. It picks Apple's AUSampler (instrument) and
AUVectorPanner (effect):

- **Before** (HEAD's `PluginHost.cpp`): `HOSTCHECK PASSED`, but **38 ×** `JUCE Assertion failure in
  juce_AudioUnitPluginFormatImpl.h:1346` on stderr. This reproduces the report.
- **After**: `HOSTCHECK PASSED` (instrument rms 0.2157, through the effect 0.0096, restored project audible), **0 ×** `:1346`.
  The remaining assertions are pre-existing and unrelated: 2 × `juce_MessageListener.cpp:50` at start, 5 ×
  `LeakedObjectDetector` at exit. Both appear in the "before" run too.
- Not run: `scripts/build.sh`. It reconfigures the engine build as Release and would replace the user's Debug build dir, and the
  full smoke run on this branch already fails 4 timing/stress checks (Pentad/Consort stress, reverb preview, render speed) on the
  Debug engine regardless of this change.
- Not run: the Reaktor 6 VST3 layout-widening harness from `plugin-add-crash-windows` (`/tmp/widenharness` no longer exists). The
  VST3 width is algebraically the same as before (see above).
- Not done by hand in the GUI app: open the user's project and press play.

## Deviations from Assessment

- No automated assertion check was added to the smoke test (assessment suggested failing on stderr). The managed test can't
  capture the engine's native stderr without redirecting fd 2 for the whole process. The `--hostcheck` before/after above is the
  reproducible check.

## Follow-ups

- `/speckit-bug-test slug=au-process-assert`: open the reporter's project in the app (Debug), press play, and confirm no `:1346`
  and no `[xrun]` lines.
- Re-check Reaktor 6 VST3 Play/Patch/Build switching (guard for 06057f89).
- `scripts/build.sh` before the PR.
- CHANGELOG `[Unreleased]` → Fixed entry. Not added here because this working tree is `feat/ui-scale`, which already has unrelated
  CHANGELOG edits. The fix belongs on its own `fix/…` branch from `develop`.
- Separate, debug-only: JUCE leak reports at exit and `MessageListener.cpp:50` at start.
