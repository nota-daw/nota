# Bug Fix: multi-output plugins crash on their first block (inactive buses get null channels)

- **Slug**: plugin-inactive-bus-crash
- **Fixed**: 2026-10-14
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Hosted plugins now run with every bus active. Before, `prepare()` called JUCE's `setPlayConfigDetails`, which
**explicitly disables all non-main buses** (`juce_AudioProcessor.cpp:367`, `disableNonMainBuses()`), and JUCE
passes inactive VST3 buses to `process()` with `nullptr` channel pointers. A multi-output instrument that writes to
every output bus anyway (Vienna Synchron Pianos, Windows, 0.52.1) faults on the first block. Nota still mixes only
the main stereo pair; aux outputs are rendered and discarded.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/pluginhost/PluginHost.cpp` | modified | new `enableEveryBus()` + `logBuses()`; `PluginInstrument::prepare` and `PluginEffect::setSampleRate` set the main bus(es) to stereo, enable every bus, then `setRateAndBufferSizeDetails` (no more `setPlayConfigDetails`); `stereoIn_`/`stereoOut_` from the main bus's real channel count |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry |

## Diff Highlights

```cpp
void enableEveryBus(juce::AudioPluginInstance& p) {
    if (p.enableAllBuses()) return;
    for (const bool isInput : { true, false })   // all-or-nothing failed: take what it accepts
        for (int i = 1; i < p.getBusCount(isInput); ++i)
            if (auto* bus = p.getBus(isInput, i); bus != nullptr && !bus->isEnabled()) bus->enable(true);
}

// PluginInstrument::prepare
if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
enableEveryBus(*plugin_);
logBuses(*plugin_);
plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
plugin_->prepareToPlay(sr, maxBlock_);
```

With aux buses active, channel 1 of the process buffer belongs to the *next* bus when a plugin's main bus is mono
(previously all other buses were off, so it was just a cleared channel). So the right channel is now picked from
the main bus's own channel count: the instrument and effect output fall back to L, and a mono effect input drops
R into the scratch channel instead of feeding the next bus.

`NOTA_DEBUG_PLUGIN_BUSES=1` prints each bus (name, channels, enabled) on prepare. Ask the Windows reporter for
this output if the crash persists.

## Tests Added or Updated

None in the repo: the smoke test has no hosted multi-output plugin. Verified with real plugins below.

## Local Verification

- `scripts/build.sh` → native + managed build OK, **SMOKE TEST PASSED**.
- `/tmp/widenharness/run2.py` (Python ctypes over `libnota_engine.dylib`, `NOTA_DATA_DIR` isolated), **Reaktor 6
  VST3** instrument with `NOTA_DEBUG_PLUGIN_BUSES=1`: input bus 0 and all **8 stereo output buses enabled=1**
  (before: only out 0 active, 7 inactive with null pointers). Bus state also survives the second prepare from
  `propagateSampleRate`. 200 × 512-frame blocks rendered, exit 0 (peak 0.0, since no ensemble is loaded).
- `/tmp/widenharness/fx.py`: built-in instrument track + VST3 effect, 100 blocks, peak L/R:
  no effect 0.054/0.054 · Solid Bus Comp 0.071/0.071 (sidechain bus in 1 enabled) · Replika 0.074/0.076 ·
  BYOD 0.054/0.054 · Supercharger 0.050/0.050 (sidechain bus enabled). Stereo is intact through every effect,
  exit 0.
- Not done: Windows, Vienna Synchron Pianos (not available here), and the GUI app.

## Deviations from Assessment

- The mono-main handling (`stereoIn_`/`stereoOut_`) wasn't in the assessment. It's needed because enabling aux
  buses changes what channel 1 of the process buffer is.
- The assessment's `kMaxBlock = 16384` secondary suspect is left as is, pending the bus log from the reporter.

## Follow-ups

- Windows retest with Synchron Pianos on a build with this fix (`/speckit-bug-test`). If it still crashes, get
  `NOTA_DEBUG_PLUGIN_BUSES=1` output and a dump.
- Multi-out drum/sampler plugins that route parts to aux outputs *when those are active* now play those parts
  into discarded outputs. Most default to the main out. A later multi-out routing feature (or summing aux into
  main as an option) would cover them.
- A hosted test plugin with an aux output bus in the smoke test would pin this down in CI.
