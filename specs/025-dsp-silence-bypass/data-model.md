# Phase 1 Data Model: DSP Silence Detection & Dynamic Sleep Bypass

## 1. Entities & Structures

### Device Sleep State Machine (`Device.h`)

```cpp
namespace nota {

class Device {
public:
    // ... existing interface ...

    // Sleep / Silence Bypass Lifecycle
    virtual bool isIdle() const { return false; }
    virtual bool hasTail() const { return false; }
    virtual void wake() {}

protected:
    // Device-internal sleep tracking primitives
    bool idle_ = false;
    int32_t tailFramesRemaining_ = 0;
};

} // namespace nota
```

### State Fields per Device

| Field | Type | Storage | Description |
| :--- | :--- | :--- | :--- |
| `idle_` | `bool` | In-place member variable | True when device is sleeping; `process()` is skipped when input is silent. |
| `tailFramesRemaining_` | `int32_t` | In-place member variable | Frame countdown tracking remaining tail decay time before sleep engages. |
| `hangoverFrames_` | `int32_t` | In-place member variable | Target hangover duration in frames (calculated as `static_cast<int32_t>(0.05 * sampleRate)`). |

### Track Invariant Checking (`Track.h` / `Engine_Render.cpp`)

```cpp
inline bool isTrackChainIdle(const Track& t) {
    for (const auto& d : t.devices) {
        if (d && !d->bypassed() && !d->isIdle()) {
            return false;
        }
    }
    return true;
}
```

---

## 2. Memory & Real-Time Invariants (AR-6)

- **Heap Allocations**: Exactly 0. All fields are inline primitive scalars (`bool`, `int32_t`).
- **Locks / Atomics**: No mutexes. Device sleep state is updated and checked exclusively on the real-time audio thread within `Engine::mixGraph()` and `Device::process()`.
- **Cache Alignment**: Primitives packed efficiently alongside existing device state to maintain 64-byte cache line locality.
