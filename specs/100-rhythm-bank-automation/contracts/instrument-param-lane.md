# Contract: `Instrument::pluginParamLane`

```cpp
// devices/Instrument.h
virtual void pluginParamLane(int32_t index, const AutomationLane& lane, double beatFrom, double beatTo) {}
```

- **Caller**: `Engine::applyAutomation`, on the audio thread, once per block for every lane
  that targets one of the instrument's plugin params (track lanes and lanes of an instrument
  inside a rack chain), right after `pluginParamSet(index, lane.valueAt(beatFrom))`. Not called
  while the lane is being written (`suppressRead`).
- **beatFrom / beatTo**: the block's musical span in beats, `[blockStart, blockStart + frames) / spb`.
- **Lifetime**: `lane` is valid only for the duration of the call. An implementation copies
  the numbers it needs (fixed storage) and must not keep the reference.
- **Real-time**: no allocation, lock or I/O.
- **Default**: no-op.

Implementations: `RhythmMachine` (`bank`), `Twin` (`GSeqPattern`).
