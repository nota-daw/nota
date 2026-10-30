# Data model: Take lanes and comping

## Engine (`Track.h`)

```cpp
struct AudioTake {
    std::shared_ptr<SampleBuffer> sample; // loop-length buffer; frame 0 = originBeat
    int32_t id = 0;       // engine-unique; comp clips name it
    int32_t number = 0;   // "Take N" on its track, recording order
    int32_t group = 0;    // the loop recording it came from (flatten works per group)
    double originBeat = 0.0;          // timeline beat of sample frame 0 (the loop start)
    double sourceOffsetFrames = 0.0;  // the range the pass recorded…
    int64_t lengthFrames = 0;         // …inside the buffer
};
// Track: std::vector<AudioTake> takes;   (recording order)
// AudioClip: int32_t takeId = 0;         (0 = not a comp section)
```

Timeline range of a take: `[originBeat + offset·bpf, originBeat + (offset+length)·bpf)` with
`bpf = deviceRate / sampleRate / samplesPerBeat` (the same mapping recorded clips use).

Rules:

- A comp section of take *t*: `sample == t.sample`, `startBeat == originBeat + sourceOffsetFrames·bpf`, `takeId == t.id`.
- Deleting *t* clears `takeId` on its sections (they keep playing).
- Flatten (per group) merges sections that are still aligned, active, unwarped, unreversed,
  unpitched and without envelopes/ADSR into one clip over a new buffer; the rest stay as
  untagged clips; the group's takes are removed.

## Project file (format v29)

```json
"tracks": [{
  "audioClips": [{ "sample": "samples/<hash>.wav", "startBeat": 0, "takeId": 7, … }],
  "takes": [{ "id": 7, "number": 3, "group": 1, "sample": "samples/<hash>.wav",
              "originBeat": 0, "sourceOffsetFrames": 0, "lengthFrames": 96000 }]
}]
```

`takes` and `takeId` are omitted when empty / 0. Older files have neither and load unchanged.

## View state (not saved)

- `ArrangementView._takesOpen`: track ids whose take lanes are open (folded by default).
