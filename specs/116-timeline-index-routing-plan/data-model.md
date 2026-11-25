# Data Model: Phase 3 — `TimelineIndex` & `RenderRoutingPlan`

## 1. `TimelineIndex` (`src/native/nota.engine/src/engine/TimelineIndex.h`)

Attached to `Track` as `std::shared_ptr<const TimelineIndex> timelineIndex` (rebuilt or reused on `publishRaw()`):

```cpp
struct IndexedNoteSpan {
    double startBeat;       // clamped pickup >= 0.0 (original note.startBeat stored if < 0)
    double endBeat;         // min(clipLengthBeats, note.startBeat + note.lengthBeats)
    int32_t originalIndex;  // index in MidiClip::notes
};

struct IndexedMidiClip {
    int32_t originalIndex;
    double startBeat;
    double endBeat;
    bool hasVolumeEnvelope;
    std::vector<IndexedNoteSpan> notesByStart; // sorted by (startBeat, originalIndex)
    std::vector<double> noteBlockMaxEnd;       // max endBeat per block of kBlockNotes (32) + tree/suffix skip
    std::vector<double> notePrefixMaxEnd;      // cumulative max(endBeat) for [0..i]
    double maxEndBeat = 0.0;
};

struct IndexedAudioClip {
    int32_t originalIndex;
    double startBeat;
    bool warped;
    double warpLenBeats;    // valid when warped
    double unwarpedSec;     // effectiveLength() / (sourceSampleRate * pitchRatio()) when unwarped
    double maxWarpEndPrefix;
    double maxUnwarpedSecPrefix;
    double minStartBeatAfter;
};
```

- **Fingerprint (`uint64_t signature` / track clip sharing)**: Since `publishRaw()` already knows which `Track` objects are shared verbatim from `authoring_->tracks` (`shared(t)`), unchanged tracks keep their existing `timelineIndex` in $O(1)$ without inspecting a single clip or note. Only modified/new tracks rebuild `TimelineIndex` during `publishRaw()`.

## 2. `RenderRoutingPlan` (`src/native/nota.engine/src/engine/RenderRoutingPlan.h`)

Attached to `Graph` as `std::shared_ptr<const RenderRoutingPlan> routingPlan`:

```cpp
struct TrackIdSlot {
    int32_t trackId;
    int32_t trackIndex;
};

struct CompiledGroupSlot {
    int32_t trackIndex = -1;
    int32_t trackId = -1;
    int32_t parentGroupSlot = -1;
    int32_t depth = 1;
    // Fixed ancestor slot chain [self, parent, grandparent, ...] for O(depth) mute/solo folding without slotOf().
    int8_t ancestors[kMaxGroups] = {};
    int8_t ancestorCount = 0;
};

struct CompiledLeafRoute {
    int8_t parentGroupSlot = -1;
    int32_t expectedMidiFromId = -1;
    int32_t midiFromTrackIndex = -1; // -1 if none or non-instrument
};

struct CompiledParamTarget {
    enum class Kind : uint8_t { Invalid, Volume, Pan, Device, MidiDevice, InstrumentPlugin, DevicePlugin, NestedRackDevice, NestedRackMidi, NestedRackInstrument };
    Kind kind = Kind::Invalid;
    Track* track = nullptr;
    Device* device = nullptr;
    MidiDevice* midi = nullptr;
    Instrument* instrument = nullptr;
    const RackCore* rack = nullptr;
    uint64_t expectedRackGen = 0;
    uint64_t chainUid = 0;
    int32_t host = -1;
    int32_t slot = -1;
    int32_t paramIndex = -1;
    float minVal = 0.0f;
    float spanVal = 1.0f;
    AutomationTarget laneTarget = AutomationTarget::Volume;
    bool suppressRead = false;
};

struct CompiledAutomationTrack {
    int32_t trackIndex = -1;
    std::vector<CompiledParamTarget> laneTargets; // 1:1 with Track::automation
    int32_t firstPluginStepLane = -1;
};

struct CompiledCvLinkEntry {
    const Track* ownerTrack = nullptr;
    int32_t linkIndex = -1;
    bool sourceIsParam = false;
    const Modulator* sourceMod = nullptr;
    CompiledParamTarget sourceParam;
    CompiledParamTarget destParam;
};
```

- **Lifetime Guarantee**:
  - Top-level `Track*`, `Device*`, `MidiDevice*`, `Instrument*`, and `const Modulator*` pointers stored inside `RenderRoutingPlan` are owned by `Graph` and retained throughout `SnapshotHistory<Graph>::RenderScope`.
  - Nested rack targets additionally store `(rack, chainUid, host, slot, expectedRackGen)` and validate `rack->liveGeneration() == expectedRackGen` before reading `device/midi/instrument`; on mismatch, they resolve dynamically via `resolveNested(...)` safely inside the active rack `RenderScope`.
