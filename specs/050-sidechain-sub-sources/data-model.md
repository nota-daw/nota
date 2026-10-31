# Data model: Sidechain from individual pads, voices and chains

## Engine (C++)

### Part id

`uint64_t`. 0 = whole track. Rack chain → `RackChain::uid` (base uid, no `instGen`). Rhythm voice
`v` (0..7) → `v + 1`. Only meaningful together with the source track's current instrument.

### Consumer key (on `Device` and `Instrument` base classes)

| Field | Type | Notes |
|---|---|---|
| source track | `int32_t` (existing, per subclass) | -1 = none |
| `scPart_` | `std::atomic<uint64_t>` (new, base class) | 0 = whole track |
| tap | `int32_t` (existing on devices) | 0 post, 1 pre; Instrument (React) has none = post |

Copied with the track id wherever a device / instrument is cloned, grouped into a rack, or moved;
cleared with it.

### Multi-part instrument surface (`Instrument`, virtual, message thread unless noted)

| Member | Meaning |
|---|---|
| `int32_t partCount() const` | 0 for single-part instruments |
| `uint64_t partId(int32_t index) const` | stable id of the part at `index` |
| `int32_t partIndex(uint64_t id) const` | current index, -1 if gone |
| `std::string partName(int32_t index) const` | chain name, else its instrument's name; Rhythm "1 Kick" |
| `void setPartTaps(const PartTap*, int32_t n)` | **audio thread**, per block, before render |

`PartTap { uint64_t part; float* pre; float* post; }` — `pre`/`post` point at the slot's Next
buffers (stereo interleaved, `frames * 2`), added into (not overwritten).

Implemented by `RackCore` (Drum Rack, Instrument Rack; first level) and `RhythmMachine`.

### Route slot (`Engine`)

| Field | Type |
|---|---|
| `routeSlotTrackId_[kMaxRoutes]` | `std::atomic<int32_t>`, -1 free |
| `routeSlotPart_[kMaxRoutes]` (new) | `std::atomic<uint64_t>`, 0 whole track |
| `routeBus_`, `routeBusNext_`, `routeBusPre_`, `routeBusPreNext_` | existing, `kMaxRoutes` 8 → 16 |

Rules: one slot per distinct *(track, part)*; a key whose track or part is missing claims none; a
consumer whose key has no slot hears nothing (nullptr). A track owning any slot keeps rendering while
muted / solo-gated (existing `keying`, extended to part slots).

### Lifecycle of a part key

```text
set(track, partIndex) ──► stored (track, partId) ──► recomputeRouting: slot claimed ──► live
        │                                   │
        │ no free slot → -2, not stored     └─ part deleted / instrument replaced → no slot (dormant)
        │                                        └─ undo restores chain (same uid) → live again
```

## Managed (C#)

### `IAudioEngine` additions

`TrackPartCount(track)`, `TrackPartName(track, index)`, `SetDeviceSidechainKey(track, device, source,
part) → int`, `DeviceSidechainKey(track, device) → (Source, Part)`,
`SetInstrumentSidechainKey(track, source, part) → int`, `InstrumentSidechainKey(track) → (Source,
Part)`, `SidechainRoutesFree()`, `SidechainRouteInUse(source, part)`, rack-child
`RackChainSidechainPart` / `RackChainSetSidechainPart`. `Part` = current index, -1 whole track, -2
missing (dormant).

### Project format v28

| DTO | New field | Default |
|---|---|---|
| `DeviceDto` | `int SidechainPart` | -1 |
| `InstrumentDto` | `int SidechainPart` | -1 |
| `NestedSidechainDto` | `int Part` | -1 |

Value = part index at save time on the source track's instrument. A dormant key (Part -2) saves as
`SidechainSource = -1`. Migration v27 → v28: no-op.
