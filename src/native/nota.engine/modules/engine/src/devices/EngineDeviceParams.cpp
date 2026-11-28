// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceOps — factory defaults, generic param access, EQ bands, and basic sidechain sources.

#include "automation/AutomationOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "devices/DeviceOps.h"
#include "model/Graph.h"
#include "model/Track.h"
#include "nota/devices/DeviceIds.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/capabilities/ISlotAnnotationDevice.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::detail {

// --- factory-default parameter values (double-click reset) -----------------
float DeviceOps::deviceParamDefault(int32_t t, int32_t d, int32_t p) const {
    auto* dev = deviceAt(t, d);
    if (!dev) return 0.0f;
    if (dev->builtinKind() < 0) return dev->getParam(p); // plugin: keep current
    auto fresh = dsp::DeviceRegistry::createChainEffect(dev->builtinKind());
    return fresh ? fresh->getParam(p) : dev->getParam(p);
}
// Default of a built-in effect KIND (no instance needed) — for rack chain devices, which
// have no track/device address of their own.
float DeviceOps::builtinDeviceParamDefault(int32_t kind, int32_t p) const {
    auto fresh = dsp::DeviceRegistry::createChainEffect(kind);
    return fresh ? fresh->getParam(p) : 0.0f;
}
float DeviceOps::instrumentParamDefault(int32_t t, int32_t p) const {
    auto tr = rt_.findTrackAuthoring(t);
    if (!tr || !tr->instrument) return 0.0f;
    const int32_t kind = tr->instrument->kind();
    if (kind < 0 || tr->instrument->asRack() != nullptr) return tr->instrument->pluginParamGet(p); // plugin / rack
    // A fresh instance's values, read once per kind: a preset asks for every param it doesn't
    // name, and building an instrument per question (Nota Twin: ~900, each with its effect
    // devices) made applying a preset take tens of milliseconds. Message thread only.
    auto it = rt_.instrumentDefaults_.find(kind);
    if (it == rt_.instrumentDefaults_.end()) {
        auto fresh = dsp::DeviceRegistry::createInstrument(kind);
        if (!fresh) return tr->instrument->pluginParamGet(p);
        std::vector<float> d(static_cast<size_t>(fresh->pluginParamCount()));
        for (size_t i = 0; i < d.size(); ++i) d[i] = fresh->pluginParamGet(static_cast<int32_t>(i));
        it = rt_.instrumentDefaults_.emplace(kind, std::move(d)).first;
    }
    return p >= 0 && static_cast<size_t>(p) < it->second.size() ? it->second[static_cast<size_t>(p)]
                                                                : tr->instrument->pluginParamGet(p);
}
float DeviceOps::midiEffectParamDefault(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    if (!m) return 0.0f;
    auto fresh = dsp::DeviceRegistry::createMidiEffect(m->midiKind());
    return fresh ? fresh->getParam(p) : m->getParam(p);
}

const char* DeviceOps::deviceName(int32_t trackId, int32_t deviceIndex) const {
    if (deviceIndex < 0) { // the track's instrument (Synth/Sampler/plugin name)
        auto t = rt_.findTrackAuthoring(trackId);
        return (t && t->instrument) ? t->instrument->displayName() : "";
    }
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->displayName() : "";
}
int32_t DeviceOps::deviceParamCount(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramCount() : 0;
}
const char* DeviceOps::deviceParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramName(paramIndex) : "";
}
float DeviceOps::deviceParamMin(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramMin(paramIndex) : 0.0f;
}
float DeviceOps::deviceParamMax(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramMax(paramIndex) : 1.0f;
}
float DeviceOps::deviceGetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->getParam(paramIndex) : 0.0f;
}
void DeviceOps::deviceSetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float value) {
    if (auto* d = deviceAt(trackId, deviceIndex)) {
        if (d->isLatencyConfigurationParam(paramIndex)) {
            setLatencyConfigurationParam(*d, paramIndex, value);
            if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
            return;
        }
        // Keep the CV base and the immediately visible value in step.
        rt_.automation().routeCvBaseEdit(0, trackId, deviceIndex, paramIndex, value);
        // A param can change the device's latency (the Compressor's look-ahead): keep PDC in step.
        const int32_t latBefore = d->latencySamples();
        d->setParam(paramIndex, value);
        if (d->latencySamples() != latBefore) recomputePdc();
        if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
    }
}

void DeviceOps::setLatencyConfigurationParam(Device& device, int32_t param, float value) {
    // The device refills its history at the next render. Clear changed compensation
    // taps too, so they cannot play back samples from before the change.
    const auto oldLatency = device.latencySamples();
    device.setParam(param, value);
    if (device.latencySamples() != oldLatency) recomputePdc(true);
    if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
}
float DeviceOps::deviceGainReduction(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->gainReductionDb() : 0.0f;
}

int32_t DeviceOps::deviceScope(int32_t trackId, int32_t deviceIndex, float* out, int32_t maxSamples) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->scopeRead(out, maxSamples) : 0;
}

void DeviceOps::deviceAction(int32_t trackId, int32_t deviceIndex, int32_t id, int32_t iarg, float farg) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->deviceAction(id, iarg, farg);
}

int32_t DeviceOps::deviceLayerWave(
    int32_t trackId, int32_t deviceIndex, int32_t layer, float* out, int32_t maxSamples) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->layerWave(layer, out, maxSamples) : 0;
}

int32_t DeviceOps::deviceGetState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return 0;
    auto s = d->getState();
    const int32_t n = static_cast<int32_t>(s.size());
    if (out && cap >= n) std::memcpy(out, s.data(), static_cast<size_t>(n));
    return n; // returns required size even when out is null / too small (probe then fetch)
}

void DeviceOps::deviceSetState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setState(data, size);
}

bool DeviceOps::setEqBandText(
    int32_t trackId, int32_t deviceIndex, int32_t band, const std::string& label, const std::string& note) {
    auto* dev = deviceAt(trackId, deviceIndex);
    auto* eq = dynamic_cast<dsp::capabilities::ISlotAnnotationDevice*>(dev);
    if (!eq || band < 0 || band >= eq->slotAnnotationCount()) return false;
    auto before = dev->getState();
    eq->setSlotAnnotationText(band, label, note);
    auto after = dev->getState();
    if (after == before) return false;
    // One mix-only undo step that swaps the device's annotation state (specs/049).
    rt_.noteStates_.push_back(std::move(before));
    rt_.noteStates_.push_back(std::move(after));
    const auto at = static_cast<float>(rt_.noteStates_.size() - 2);
    rt_.undoStack_.push_back({nullptr, {MixChange{MixKind::DeviceNotes, trackId, at, at + 1.0f, deviceIndex, -1}}});
    if (rt_.undoStack_.size() > EngineRuntime::kMaxUndoDepth) {
        rt_.undoStack_.erase(rt_.undoStack_.begin());
        if (rt_.undoGroupDepth_ > 0 && rt_.undoGroupBase_ > 0) --rt_.undoGroupBase_;
    }
    rt_.redoStack_.clear();
    compactNoteStates();
    return true;
}

std::string DeviceOps::eqBandText(int32_t trackId, int32_t deviceIndex, int32_t band, int32_t which) const {
    auto* eq = dynamic_cast<const dsp::capabilities::ISlotAnnotationDevice*>(deviceAt(trackId, deviceIndex));
    if (!eq || band < 0 || band >= eq->slotAnnotationCount()) return {};
    return which == 1 ? eq->slotAnnotationNote(band) : eq->slotAnnotationLabel(band);
}

bool DeviceOps::deviceLoadFile(int32_t trackId, int32_t deviceIndex, const std::string& path) {
    auto* d = deviceAt(trackId, deviceIndex);
    return d && d->loadFile(path);
}

std::string DeviceOps::deviceText(int32_t trackId, int32_t deviceIndex, int32_t id) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->deviceText(id) : std::string{};
}

} // namespace nota::detail
