// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceOps — sidechain sub-sources/keys, editor state/sleep, PDC latency, and self-test.

#include "automation/AutomationOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
#include "devices/DeviceOps.h"
#include "devices/RackOps.h"
#include "model/Graph.h"
#include "model/Track.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/pluginhost/PluginHostModule.h"
#include "render/HybridBufferController.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

// ---- Sidechain sub-sources (specs/050) -------------------------------------------------

int32_t DeviceOps::trackPartCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->instrument ? t->instrument->partCount() : 0;
}

std::string DeviceOps::trackPartName(int32_t trackId, int32_t index) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || index < 0 || index >= t->instrument->partCount()) return {};
    return t->instrument->partName(index);
}

int32_t DeviceOps::partIndexOf(int32_t sourceTrackId, uint64_t part) const {
    if (part == 0) return -1;
    auto t = rt_.findTrackAuthoring(sourceTrackId);
    const int32_t i = t && t->instrument ? t->instrument->partIndex(part) : -1;
    return i >= 0 ? i : -2;
}

bool DeviceOps::partIdAt(int32_t sourceTrackId, int32_t index, uint64_t* part) const {
    if (index == -1) {
        *part = 0;
        return true;
    }
    auto t = rt_.findTrackAuthoring(sourceTrackId);
    if (!t || !t->instrument || index < 0 || index >= t->instrument->partCount()) return false;
    *part = t->instrument->partId(index);
    return *part != 0;
}

void DeviceOps::setDeviceSidechainSource(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId) {
    if (auto* d = deviceAt(trackId, deviceIndex)) {
        d->setSidechainSourceTrackId(sourceTrackId);
        d->setSidechainPart(0); // a whole-track key
    }
    recomputeRouting();
}

int32_t DeviceOps::deviceSidechainSource(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainSourceTrackId() : -1;
}

bool DeviceOps::deviceAcceptsSidechain(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->acceptsSidechain() : false;
}

void DeviceOps::setDeviceSidechainGain(int32_t trackId, int32_t deviceIndex, float db) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainGainDb(db);
}
float DeviceOps::deviceSidechainGain(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainGainDb() : 0.0f;
}
void DeviceOps::setDeviceSidechainMix(int32_t trackId, int32_t deviceIndex, float mix) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainMix(mix);
}
float DeviceOps::deviceSidechainMix(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainMix() : 1.0f;
}
void DeviceOps::setDeviceSidechainTapPre(int32_t trackId, int32_t deviceIndex, int32_t pre) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainTapPre(pre);
}
int32_t DeviceOps::deviceSidechainTapPre(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainTapPre() : 0;
}

// Instrument sidechain ("React", Nota Flux): route a listening instrument to a source track.
void DeviceOps::setInstrumentSidechainSource(int32_t trackId, int32_t sourceTrackId) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (t && t->instrument) {
        t->instrument->setSidechainSourceTrackId(sourceTrackId);
        t->instrument->setSidechainPart(0); // a whole-track key
    }
    recomputeRouting();
}
int32_t DeviceOps::instrumentSidechainSource(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->instrument ? t->instrument->sidechainSourceTrackId() : -1;
}
bool DeviceOps::instrumentAcceptsSidechain(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->instrument ? t->instrument->acceptsSidechain() : false;
}

// Assign a route-bus slot to each distinct sidechain source track referenced by a
// device or a listening instrument. Message thread; scans the authoring graph (device /
// instrument pointers are stable across snapshots, so the table the audio thread reads
// stays valid).
void DeviceOps::recomputeRouting() {
    // Compute candidate slot assignments first; if identical to live table, skip atomic clears
    // and avoid invalidating audio-thread routeSlot caches unnecessarily.
    struct Candidate {
        int32_t src = -1;
        uint64_t part = 0;
    };
    Candidate nextList[kMaxRoutes];
    int32_t next = 0;

    auto exists = [&](int32_t id, uint64_t part) {
        for (auto& t : rt_.authoring_->tracks)
            if (t->id() == id) return part == 0 || (t->instrument && t->instrument->partIndex(part) >= 0);
        return false;
    };
    auto already = [&](int32_t src, uint64_t part) {
        for (int32_t s = 0; s < next; ++s)
            if (nextList[s].src == src && nextList[s].part == part) return true;
        return false;
    };
    auto claim = [&](int32_t src, uint64_t part) {
        if (src < 0 || next >= kMaxRoutes || already(src, part) || !exists(src, part)) return;
        nextList[next++] = {src, part};
    };
    forEachRouteKey([&](int32_t src, uint64_t part) { claim(src, part); });

    const int32_t used = rt_.routeSlotCount_.load(std::memory_order_relaxed);
    if (next == used) {
        bool unchanged = true;
        for (int32_t s = 0; s < used; ++s) {
            if (rt_.routeSlotTrackId_[s].load(std::memory_order_relaxed) != nextList[s].src ||
                rt_.routeSlotPart_[s].load(std::memory_order_relaxed) != nextList[s].part) {
                unchanged = false;
                break;
            }
        }
        if (unchanged) return;
    }

    rt_.routeSlotCount_.store(0, std::memory_order_release);
    for (int32_t s = 0; s < used; ++s) rt_.routeSlotTrackId_[s].store(-1, std::memory_order_relaxed);
    for (int32_t s = 0; s < next; ++s) {
        rt_.routeSlotPart_[s].store(nextList[s].part, std::memory_order_relaxed);
        rt_.routeSlotTrackId_[s].store(nextList[s].src, std::memory_order_relaxed);
    }
    rt_.routeSlotCount_.store(next, std::memory_order_release);
    rt_.routingEpoch_.fetch_add(1, std::memory_order_release);
}

// Every route a consumer asks for, in claim order: track instruments and devices (and keyed
// children inside their racks, specs/044), then a monitoring audio track's input source.
// Meter-only keys (an armed audio track that isn't monitoring, #352) come after every audible
// one, so with the pool full arming a track never takes a route a device or monitor hears.
template <typename Fn> void DeviceOps::forEachRouteKey(Fn&& fn, bool meters) const {
    for (auto& t : rt_.authoring_->tracks) {
        auto children = [&](const RackCore& r) {
            r.forEachChildDevice([&](Device& c) { fn(c.sidechainSourceTrackId(), c.sidechainPart()); });
            r.forEachChildInstrument([&](Instrument& i) { fn(i.sidechainSourceTrackId(), i.sidechainPart()); });
        };
        if (t->instrument) {
            fn(t->instrument->sidechainSourceTrackId(), t->instrument->sidechainPart());
            if (auto* r = t->instrument->asRack()) children(*r);
        }
        for (auto& d : t->devices)
            if (d) {
                fn(d->sidechainSourceTrackId(), d->sidechainPart());
                if (auto* r = d->asRack()) children(*r);
            }
        // A monitoring audio track hears its source track's post-fader tap.
        if (t->type() == TrackType::Audio && t->monitor() && t->recordInputSource() > 0)
            fn(t->recordInputSource(), uint64_t{0});
    }
    if (!meters) return;
    for (auto& t : rt_.authoring_->tracks) // an armed one meters it (#352): last, see above
        if (t->type() == TrackType::Audio && t->armed() && !t->monitor() && t->recordInputSource() > 0)
            fn(t->recordInputSource(), uint64_t{0});
}

// Set a key, then keep it only if it got a route and took none from another listener: a full
// pool leaves the previous key in place. Routes are claimed in track and chain order, so a new
// source on an early device could otherwise push out a later device's. Re-deriving the table
// (rather than counting free slots) lets a consumer move to another source while the pool is
// full when it was its old source's only listener.
template <typename Consumer>
int32_t DeviceOps::applySidechainKey(Consumer& c, int32_t src, uint64_t part, bool routed) {
    if (!routed) { // the master's devices are never fed a key (their key is only kept): no route to check
        c.setSidechainSourceTrackId(src);
        c.setSidechainPart(part);
        recomputeRouting();
        return 0;
    }
    const int32_t oldSrc = c.sidechainSourceTrackId();
    const uint64_t oldPart = c.sidechainPart();
    std::pair<int32_t, uint64_t> before[kMaxRoutes];
    const int32_t nBefore = rt_.routeSlotCount_.load(std::memory_order_relaxed);
    for (int32_t s = 0; s < nBefore; ++s)
        before[s] = {rt_.routeSlotTrackId_[s].load(std::memory_order_relaxed),
            rt_.routeSlotPart_[s].load(std::memory_order_relaxed)};
    c.setSidechainSourceTrackId(src);
    c.setSidechainPart(part);
    recomputeRouting();
    bool kept = rt_.routeSlotFor(src, part) >= 0;
    // Does a device, instrument or monitor still listen to a key? A route only an armed track's
    // meter reads may go (#352); so may the old key's when this was its only listener.
    auto heard = [&](int32_t ts, uint64_t ps) {
        bool h = false;
        forEachRouteKey([&](int32_t t, uint64_t p) { h = h || (t == ts && p == ps); }, false);
        return h;
    };
    for (int32_t s = 0; kept && s < nBefore; ++s) {
        const auto [t, p] = before[s];
        if (rt_.routeSlotFor(t, p) < 0 && heard(t, p)) kept = false; // every other route must stay
    }
    if (kept) return 0;
    c.setSidechainSourceTrackId(oldSrc);
    c.setSidechainPart(oldPart);
    recomputeRouting();
    return -2;
}

// Rack children (Engine_Rack.cpp) set their keys through the same check.
template int32_t DeviceOps::applySidechainKey<Device>(Device&, int32_t, uint64_t, bool);
template int32_t DeviceOps::applySidechainKey<Instrument>(Instrument&, int32_t, uint64_t, bool);

int32_t DeviceOps::setDeviceSidechainKey(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId, int32_t part) {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return -1;
    if (sourceTrackId < 0) {
        setDeviceSidechainSource(trackId, deviceIndex, -1);
        return 0;
    }
    // A MIDI key (Nota Auto Shift) follows the source track's notes; a pad has none of its own.
    if (part >= 0 && d->wantsMidiKey()) return -1;
    uint64_t id = 0;
    if (!rt_.findTrackAuthoring(sourceTrackId) || !partIdAt(sourceTrackId, part, &id)) return -1;
    return applySidechainKey(*d, sourceTrackId, id, trackId != kMasterTrackId);
}

bool DeviceOps::deviceSidechainKey(int32_t trackId, int32_t deviceIndex, int32_t* source, int32_t* part) const {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return false;
    const int32_t src = d->sidechainSourceTrackId();
    if (source) *source = src;
    if (part) *part = src < 0 ? -1 : partIndexOf(src, d->sidechainPart());
    return true;
}

int32_t DeviceOps::setInstrumentSidechainKey(int32_t trackId, int32_t sourceTrackId, int32_t part) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument) return -1;
    if (sourceTrackId < 0) {
        setInstrumentSidechainSource(trackId, -1);
        return 0;
    }
    uint64_t id = 0;
    if (!rt_.findTrackAuthoring(sourceTrackId) || !partIdAt(sourceTrackId, part, &id)) return -1;
    return applySidechainKey(*t->instrument, sourceTrackId, id);
}

bool DeviceOps::instrumentSidechainKey(int32_t trackId, int32_t* source, int32_t* part) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument) return false;
    const int32_t src = t->instrument->sidechainSourceTrackId();
    if (source) *source = src;
    if (part) *part = src < 0 ? -1 : partIndexOf(src, t->instrument->sidechainPart());
    return true;
}

int32_t DeviceOps::sidechainRoutesFree() const {
    int32_t n = 0;
    for (auto& s : rt_.routeSlotTrackId_)
        if (s.load(std::memory_order_relaxed) < 0) ++n;
    return n;
}

bool DeviceOps::sidechainRouteInUse(int32_t sourceTrackId, int32_t part) const {
    uint64_t id = 0;
    return partIdAt(sourceTrackId, part, &id) && rt_.routeSlotFor(sourceTrackId, id) >= 0;
}

void DeviceOps::openTrackEditor(int32_t trackId, int32_t deviceIndex) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->openEditor();
    } else if (deviceIndex < static_cast<int32_t>(t->devices.size())) {
        if (t->devices[static_cast<size_t>(deviceIndex)]) t->devices[static_cast<size_t>(deviceIndex)]->openEditor();
    }
    if (rt_.hybrid_ && rt_.hybrid_->mode() == HybridBufferMode::Auto) rt_.hybrid_->invalidate(true);
}

void DeviceOps::closeTrackEditor(int32_t trackId, int32_t deviceIndex) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->closeEditor();
    } else if (deviceIndex < static_cast<int32_t>(t->devices.size())) {
        if (t->devices[static_cast<size_t>(deviceIndex)]) t->devices[static_cast<size_t>(deviceIndex)]->closeEditor();
    }
    if (rt_.hybrid_ && rt_.hybrid_->mode() == HybridBufferMode::Auto) rt_.hybrid_->invalidate(true);
}

bool DeviceOps::trackEditorOpen(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) return t->instrument && t->instrument->editorOpen();
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[static_cast<size_t>(deviceIndex)])
        return t->devices[static_cast<size_t>(deviceIndex)]->editorOpen();
    return false;
}

EditorPreview DeviceOps::trackEditorPreview(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->editorPreview : EditorPreview{};
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[static_cast<size_t>(deviceIndex)])
        return t->devices[static_cast<size_t>(deviceIndex)]->editorPreview();
    return {};
}

bool DeviceOps::setTrackEditorPreview(int32_t trackId, int32_t deviceIndex, EditorPreview p) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) {
        if (!t->instrument) return false;
        t->instrument->editorPreview = std::move(p);
        return true;
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size()) || !t->devices[static_cast<size_t>(deviceIndex)])
        return false;
    t->devices[static_cast<size_t>(deviceIndex)]->setEditorPreview(std::move(p));
    return true;
}

int32_t DeviceOps::getPluginState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0;
    std::vector<uint8_t> v;
    if (deviceIndex < 0) {
        if (t->instrument) v = t->instrument->getState();
    } else if (deviceIndex < static_cast<int32_t>(t->devices.size())) {
        if (t->devices[static_cast<size_t>(deviceIndex)]) v = t->devices[static_cast<size_t>(deviceIndex)]->getState();
    }
    if (out && cap >= static_cast<int32_t>(v.size())) std::copy(v.begin(), v.end(), out);
    return static_cast<int32_t>(v.size());
}

bool DeviceOps::setPluginState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) {
        if (!t->instrument) return false;
        t->instrument->setState(data, size);
        return true;
    }
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[static_cast<size_t>(deviceIndex)]) {
        t->devices[static_cast<size_t>(deviceIndex)]->setState(data, size);
        return true;
    }
    return false;
}

void DeviceOps::setTrackDeviceBypassed(int32_t trackId, int32_t deviceIndex, bool bypassed) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex == -1) {
        if (t->instrument) t->instrument->setBypassed(bypassed);
        return;
    } // the instrument (#148)
    if (deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return;
    if (t->devices[static_cast<size_t>(deviceIndex)])
        t->devices[static_cast<size_t>(deviceIndex)]->setBypassed(bypassed);
}

bool DeviceOps::trackDeviceBypassed(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex == -1) return t->instrument && t->instrument->bypassed();
    if (deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return false;
    return t->devices[static_cast<size_t>(deviceIndex)] && t->devices[static_cast<size_t>(deviceIndex)]->bypassed();
}

bool DeviceOps::setPluginSleepMode(int32_t trackId, int32_t deviceIndex, int32_t mode) {
    if (mode != static_cast<int32_t>(PluginSleepMode::Auto) && mode != static_cast<int32_t>(PluginSleepMode::Never))
        return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    const auto policy = static_cast<dsp::PluginSleepMode>(mode);
    if (deviceIndex == -1) {
        if (!t->instrument) return false;
        t->instrument->setSleepMode(policy);
        return true;
    }
    if (deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size()) ||
        !t->devices[static_cast<size_t>(deviceIndex)])
        return false;
    t->devices[static_cast<size_t>(deviceIndex)]->setSleepMode(policy);
    return true;
}

int32_t DeviceOps::pluginSleepMode(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return static_cast<int32_t>(PluginSleepMode::Auto);
    if (deviceIndex == -1)
        return t->instrument ? static_cast<int32_t>(t->instrument->sleepMode())
                             : static_cast<int32_t>(PluginSleepMode::Auto);
    if (deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size()) ||
        !t->devices[static_cast<size_t>(deviceIndex)])
        return static_cast<int32_t>(PluginSleepMode::Auto);
    return static_cast<int32_t>(t->devices[static_cast<size_t>(deviceIndex)]->sleepMode());
}

void DeviceOps::setTrackDeviceFolded(int32_t trackId, int32_t deviceIndex, bool folded) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->folded = folded;
        return;
    }
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[static_cast<size_t>(deviceIndex)])
        t->devices[static_cast<size_t>(deviceIndex)]->setFolded(folded);
}

bool DeviceOps::trackDeviceFolded(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) return t->instrument && t->instrument->folded;
    return deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[static_cast<size_t>(deviceIndex)] &&
           t->devices[static_cast<size_t>(deviceIndex)]->folded();
}

// --- plugin delay compensation (M3-7) --------------------------------------

int32_t DeviceOps::trackLatency(const Track& t) {
    int32_t lat = t.instrument ? t.instrument->latencySamples() : 0;
    for (const auto& d : t.devices)
        if (d) lat += d->latencySamples();
    return lat;
}

int32_t DeviceOps::trackLatencySamples(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? trackLatency(*t) : 0;
}

// Delay every track by (maxLatency - itsOwnLatency) so all outputs stay aligned.
void DeviceOps::recomputePdc(bool resetChangedDelays) {
    const size_t n = rt_.authoring_->tracks.size();
    if (n == 0) return;
    if (n == 1 && !resetChangedDelays) {
        const auto& t = rt_.authoring_->tracks[0];
        if (t && t->pdc && t->pdc->delay() != 0) t->pdc->setDelay(0, false);
        return;
    }
    std::array<int32_t, 512> stackLat{};
    std::vector<int32_t> heapLat;
    int32_t* lats = n <= stackLat.size() ? stackLat.data() : (heapLat.resize(n), heapLat.data());
    int32_t maxLat = 0;
    for (size_t i = 0; i < n; ++i) {
        const auto& t = rt_.authoring_->tracks[i];
        const int32_t lat = t ? trackLatency(*t) : 0;
        lats[i] = lat;
        if (lat > maxLat) maxLat = lat;
    }
    for (size_t i = 0; i < n; ++i) {
        const auto& t = rt_.authoring_->tracks[i];
        if (t && t->pdc) {
            const int32_t target = maxLat - lats[i];
            if (resetChangedDelays || t->pdc->delay() != target) t->pdc->setDelay(target, resetChangedDelays);
        }
    }
}

int32_t DeviceOps::pluginEditorSleepSelftest(int32_t catalogIndex) {
    if (catalogIndex < 0) return 1;
    constexpr int32_t kBlock = 128;
    constexpr double kSr = 48000.0;
    std::vector<float> buf(static_cast<size_t>(kBlock) * 2, 0.0f);

    auto initWorkGain = [](EngineRuntime& eng, int32_t tid, int32_t dev) {
        const int32_t pc = eng.automation().pluginParamCount(tid, dev);
        for (int32_t i = 0; i < pc; ++i) {
            const std::string nm = eng.automation().pluginParamName(tid, dev, i);
            if (nm == "Work" || nm == "Gain") eng.automation().pluginParamSet(tid, dev, i, 1.0f);
        }
    };
    auto maxAbsAndSig = [](const std::vector<float>& b, double& outSig) {
        float pk = 0.0f;
        for (float v : b) pk = std::max(pk, std::fabs(v));
        outSig = static_cast<double>(b[0]) + 2.0 * static_cast<double>(b[2]);
        return pk;
    };

    // --- Test A: PluginInstrument (stopped past hangover), muted keep-alive, and Hybrid live bypass ---
    bool supportsInstrument = false;
    {
        auto ep = std::make_unique<EngineRuntime>();
        EngineRuntime& e = *ep;
        e.setRenderThreads(0);
        e.renderOffline(buf.data(), kBlock, kSr);
        const int32_t t = e.devices().addPluginInstrumentTrack(catalogIndex);
        if (t > 0) {
            supportsInstrument = true;
            if (e.devices().waitPluginLoads(60000) != 0) return 2;
            auto trk = e.findTrackAuthoring(t);
            if (!trk || !trk->instrument) return 2;
            initWorkGain(e, t, -1);
            e.devices().setPluginSleepMode(t, -1, static_cast<int32_t>(PluginSleepMode::Auto));

            for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
            if (!trk->instrument->isIdle()) return 3;
            e.renderOffline(buf.data(), kBlock);
            double sigSleep = 0.0;
            if (maxAbsAndSig(buf, sigSleep) != 0.0f) return 4;

            // Open editor: must wake immediately without mutating PluginSleepMode::Auto.
            trk->instrument->setEditorOpenForTest(true);
            if (!trk->instrument->editorOpen() || trk->instrument->isIdle() ||
                !trk->instrument->requiresActiveProcessing())
                return 5;
            if (e.devices().pluginSleepMode(t, -1) != static_cast<int32_t>(PluginSleepMode::Auto)) return 6;

            int32_t activeAdvances = 0;
            for (int i = 0; i < 160; ++i) {
                e.renderOffline(buf.data(), kBlock);
                double s = 0.0;
                if (maxAbsAndSig(buf, s) > 0.0f) ++activeAdvances;
            }
            if (trk->instrument->isIdle()) return 7;
            const std::string instName = trk->instrument->displayName() ? trk->instrument->displayName() : "";
            const bool isProbe = instName.find("Render Probe") != std::string::npos;
            if (isProbe && activeAdvances < 150) return 8;

            // Muted track with open editor continues processing without leaking audio to master.
            e.tracks().setTrackMute(t, true);
            for (int i = 0; i < 160; ++i) {
                e.renderOffline(buf.data(), kBlock);
                double s = 0.0;
                if (maxAbsAndSig(buf, s) != 0.0f) return 9;
            }
            if (trk->instrument->isIdle()) return 10;
            e.tracks().setTrackMute(t, false);
            e.renderOffline(buf.data(), kBlock);
            double sigAfterUnmute = 0.0;
            if (isProbe && maxAbsAndSig(buf, sigAfterUnmute) == 0.0f) return 11;

            // Closing editor leaves SleepMode::Auto intact and allows sleep again after hangover.
            trk->instrument->setEditorOpenForTest(false);
            if (e.devices().pluginSleepMode(t, -1) != static_cast<int32_t>(PluginSleepMode::Auto)) return 12;
            for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
            if (!trk->instrument->isIdle()) return 13;
            e.renderOffline(buf.data(), kBlock);
            if (maxAbsAndSig(buf, sigSleep) != 0.0f) return 14;
        }
    }

    if (supportsInstrument) {
        auto ehp = std::make_unique<EngineRuntime>();
        EngineRuntime& eh = *ehp;
        eh.setRenderThreads(2);
        eh.setHybridBufferingMode(HybridBufferMode::Auto);
        eh.renderOffline(buf.data(), kBlock, kSr);
        const int32_t th = eh.devices().addPluginInstrumentTrack(catalogIndex);
        if (th <= 0) return 15;
        if (eh.devices().waitPluginLoads(60000) != 0) return 15;
        auto trk = eh.findTrackAuthoring(th);
        eh.transportPlay();
        eh.renderDeviceBlock(buf.data(), kBlock);
        if (eh.hybridStats().anticipativeTracks < 1) return 16;

        trk->instrument->setEditorOpenForTest(true);
        if (eh.hybrid_) eh.hybrid_->invalidate(true);
        eh.renderDeviceBlock(buf.data(), kBlock);
        const auto hsOpen = eh.hybridStats();
        if (hsOpen.anticipativeTracks != 0 || hsOpen.liveTracks < 1) return 17;

        trk->instrument->setEditorOpenForTest(false);
        if (eh.hybrid_) eh.hybrid_->invalidate(true);
        eh.renderDeviceBlock(buf.data(), kBlock);
        if (eh.hybridStats().anticipativeTracks < 1) return 18;
        eh.transportStop();
    }

    // --- Test B: PluginEffect on stopped Audio track + Group bus ---
    {
        auto ep = std::make_unique<EngineRuntime>();
        EngineRuntime& e = *ep;
        e.setRenderThreads(0);
        e.renderOffline(buf.data(), kBlock, kSr);
        const int32_t grp = e.tracks().addGroupTrack();
        const int32_t audio = e.tracks().addAudioTrack();
        e.tracks().setTrackGroup(audio, grp);
        const int32_t fxAudio = e.devices().addTrackEffectPlugin(audio, catalogIndex);
        const int32_t fxGrp = e.devices().addTrackEffectPlugin(grp, catalogIndex);
        if (fxAudio < 0 || fxGrp < 0) return 19;
        if (e.devices().waitPluginLoads(60000) != 0) return 19;
        initWorkGain(e, audio, fxAudio);
        initWorkGain(e, grp, fxGrp);

        auto tAudio = e.findTrackAuthoring(audio);
        auto tGrp = e.findTrackAuthoring(grp);
        auto dAudio = tAudio->devices[static_cast<size_t>(fxAudio)];
        auto dGrp = tGrp->devices[static_cast<size_t>(fxGrp)];

        // Activate audio track with brief transport roll, then stop so tail & SilenceGate settle to sleep.
        e.transportPlay();
        for (int i = 0; i < 4; ++i) e.renderOffline(buf.data(), kBlock);
        e.transportStop();
        for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
        if (!dAudio->isIdle() || !dGrp->isIdle()) return 20;

        const std::string devName = dAudio->displayName() ? dAudio->displayName() : "";
        const bool isProbe = devName.find("Render Probe") != std::string::npos;

        // Stopped audio track with settled tailQuiet < 0 must wake immediately on editor open.
        dAudio->setEditorOpenForTest(true);
        int32_t audioAdvances = 0;
        for (int i = 0; i < 160; ++i) {
            e.renderOffline(buf.data(), kBlock);
            double s = 0.0;
            if (maxAbsAndSig(buf, s) > 0.0f) ++audioAdvances;
        }
        if (dAudio->isIdle()) return 21;
        if (isProbe && audioAdvances < 150) return 22;

        dAudio->setEditorOpenForTest(false);
        for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
        if (!dAudio->isIdle()) return 23;

        // Group bus keeps processing while child plugin editor is open.
        dGrp->setEditorOpenForTest(true);
        int32_t grpAdvances = 0;
        for (int i = 0; i < 160; ++i) {
            e.renderOffline(buf.data(), kBlock);
            double s = 0.0;
            if (maxAbsAndSig(buf, s) > 0.0f) ++grpAdvances;
        }
        if (dGrp->isIdle()) return 24;
        if (isProbe && grpAdvances < 150) return 25;

        dGrp->setEditorOpenForTest(false);
        for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
        if (!dGrp->isIdle()) return 26;
    }

    // --- Test C: RackDevice & InstrumentRack propagation ---
    {
        auto ep = std::make_unique<EngineRuntime>();
        EngineRuntime& e = *ep;
        e.setRenderThreads(0);
        e.renderOffline(buf.data(), kBlock, kSr);
        const int32_t trk = e.tracks().addAudioTrack();
        const int32_t rackIdx = e.devices().addTrackBuiltinDevice(trk, 5); // RackDevice
        if (rackIdx < 0) return 27;
        const int32_t ch = e.racks().addChain(trk, rackIdx, 0);
        const int32_t fxIdx = e.racks().addPluginChainDevice(trk, rackIdx, ch, catalogIndex);
        if (fxIdx < 0) return 28;
        if (e.devices().waitPluginLoads(60000) != 0) return 28;

        auto tAuth = e.findTrackAuthoring(trk);
        auto* rackDev = tAuth->devices[static_cast<size_t>(rackIdx)].get();
        auto* rackCore = rackDev ? rackDev->asRack() : nullptr;
        Device* childFx = rackCore ? rackCore->chainDeviceAt(ch, fxIdx) : nullptr;
        if (!childFx) return 29;

        e.transportPlay();
        for (int i = 0; i < 4; ++i) e.renderOffline(buf.data(), kBlock);
        e.transportStop();
        for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
        if (!rackDev->isIdle() || !childFx->isIdle()) return 30;

        childFx->setEditorOpenForTest(true);
        if (rackDev->isIdle() || !rackDev->requiresActiveProcessing() || childFx->isIdle()) return 31;
        for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
        if (rackDev->isIdle() || childFx->isIdle()) return 32;

        childFx->setEditorOpenForTest(false);
        for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
        if (!rackDev->isIdle() || !childFx->isIdle()) return 33;

        if (supportsInstrument) {
            const int32_t ir = e.racks().addInstrumentTrack();
            const int32_t pChain = e.racks().addPluginInstrumentChain(ir, -1, catalogIndex);
            if (pChain < 0) return 34;
            if (e.devices().waitPluginLoads(60000) != 0) return 34;
            auto irAuth = e.findTrackAuthoring(ir);
            auto* irCore = irAuth->instrument ? irAuth->instrument->asRack() : nullptr;
            Instrument* childInst = irCore ? irCore->chainInstrument(pChain) : nullptr;
            if (!childInst) return 35;

            for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
            if (!irAuth->instrument->isIdle() || !childInst->isIdle()) return 36;

            childInst->setEditorOpenForTest(true);
            if (irAuth->instrument->isIdle() || !irAuth->instrument->requiresActiveProcessing() || childInst->isIdle())
                return 37;
            for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
            if (irAuth->instrument->isIdle() || childInst->isIdle()) return 38;

            childInst->setEditorOpenForTest(false);
            for (int i = 0; i < 160; ++i) e.renderOffline(buf.data(), kBlock);
            if (!irAuth->instrument->isIdle() || !childInst->isIdle()) return 39;
        }
    }

    return 0;
}

} // namespace nota::detail
