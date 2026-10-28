// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preset tags (specs/048): which preset a device was loaded from, and whether it still holds
// that preset's values. A device stores an index into the engine's append-only tag table, so
// a preset applied inside a mix gesture undoes together with its tag, and copies, moves and
// duplicates keep it with the device object.

#include "Engine.h"
#include "../devices/Device.h"
#include "../devices/Instrument.h"
#include "../devices/MidiDevice.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

namespace {
// View settings are not part of the sound: the card size, EQ-8's analyzer.
bool isViewParam(const std::string& name) {
    return name == "View" || name == "view" || name == "Analyzer" || name == "Analyzer Range" ||
           name == "Analyzer Peak";
}
constexpr float kTolerance = 1e-4f; // of the param's range
} // namespace

int32_t Engine::presetTagOf(int32_t trackId, int32_t surface, int32_t index) const {
    switch (surface) {
    case 0:
        if (auto* d = deviceAt(trackId, index)) return d->presetTag();
        return -1;
    case 1: {
        auto t = findTrackAuthoring(trackId);
        return t && t->instrument ? t->instrument->presetTag : -1;
    }
    case 2:
        if (auto* m = midiDeviceAt(trackId, index)) return m->presetTag();
        return -1;
    default: return -1;
    }
}

bool Engine::setPresetTagIndex(int32_t trackId, int32_t surface, int32_t index, int32_t tag) {
    if (tag < 0 || tag >= static_cast<int32_t>(presetTags_.size())) return false;
    switch (surface) {
    case 0:
        if (auto* d = deviceAt(trackId, index)) { d->setPresetTag(tag); return true; }
        return false;
    case 1: {
        auto t = findTrackAuthoring(trackId);
        if (!t || !t->instrument) return false;
        t->instrument->presetTag = tag;
        return true;
    }
    case 2:
        if (auto* m = midiDeviceAt(trackId, index)) { m->setPresetTag(tag); return true; }
        return false;
    default: return false;
    }
}

bool Engine::presetSurfaceValues(int32_t trackId, int32_t surface, int32_t index, std::vector<float>& out) const {
    out.clear();
    int32_t n = 0;
    switch (surface) {
    case 0: { auto* d = deviceAt(trackId, index); if (!d) return false; n = d->paramCount(); break; }
    case 1: { auto t = findTrackAuthoring(trackId); if (!t || !t->instrument) return false; n = pluginParamCount(trackId, -1); break; }
    case 2: { auto* m = midiDeviceAt(trackId, index); if (!m) return false; n = m->paramCount(); break; }
    default: return false;
    }
    const int32_t dev = surface == 1 ? -1 : index;
    out.resize(static_cast<size_t>(std::max(0, n)));
    for (int32_t p = 0; p < n; ++p) {
        float v;
        if (!cvBaseValue(surface, trackId, dev, p, v)) // a modulated param: its base
            v = surface == 0 ? deviceGetParam(trackId, index, p)
                : surface == 2 ? midiEffectGetParam(trackId, index, p)
                               : pluginParamGet(trackId, -1, p);
        out[static_cast<size_t>(p)] = v;
    }
    return true;
}

int32_t Engine::setPresetTag(int32_t trackId, int32_t surface, int32_t index, const char* id, const char* name,
    const float* base, int32_t n) {
    if (presetTagOf(trackId, surface, index) < 0) return -1;
    if (!id || !*id) { // Init / no preset
        setPresetTagIndex(trackId, surface, index, 0);
        return 0;
    }
    PresetTagData tag;
    tag.id = id;
    tag.name = name ? name : "";
    if (base && n > 0) tag.base.assign(base, base + n);
    else presetSurfaceValues(trackId, surface, index, tag.base);
    presetTags_.push_back(std::move(tag));
    const int32_t at = static_cast<int32_t>(presetTags_.size()) - 1;
    setPresetTagIndex(trackId, surface, index, at);
    return at;
}

const Engine::PresetTagData* Engine::presetTagData(int32_t tag) const {
    return tag > 0 && tag < static_cast<int32_t>(presetTags_.size()) ? &presetTags_[static_cast<size_t>(tag)] : nullptr;
}

int32_t Engine::presetModified(int32_t trackId, int32_t surface, int32_t index) const {
    const auto* tag = presetTagData(presetTagOf(trackId, surface, index));
    if (!tag) return -1;
    std::vector<float> now;
    if (!presetSurfaceValues(trackId, surface, index, now)) return -1;
    const size_t n = std::min(now.size(), tag->base.size());
    for (size_t i = 0; i < n; ++i) {
        const auto p = static_cast<int32_t>(i);
        std::string name;
        float lo = 0.0f, hi = 1.0f; // instrument params are normalized
        if (surface == 0) {
            const char* nm = deviceParamName(trackId, index, p);
            name = nm ? nm : "";
            lo = deviceParamMin(trackId, index, p);
            hi = deviceParamMax(trackId, index, p);
        } else if (surface == 2) {
            const char* nm = midiEffectParamName(trackId, index, p);
            name = nm ? nm : "";
            lo = midiEffectParamMin(trackId, index, p);
            hi = midiEffectParamMax(trackId, index, p);
        } else {
            name = pluginParamId(trackId, -1, p);
        }
        if (isViewParam(name)) continue;
        const float span = std::max(1e-6f, std::fabs(hi - lo));
        if (std::fabs(now[i] - tag->base[i]) > kTolerance * span) return 1;
    }
    return 0;
}

// Device-free: a tag set inside a gesture undoes and redoes with it.
bool Engine::presetTagSelfTest() {
    const auto t = addInstrumentTrack();
    const int32_t d = addTrackBuiltinDevice(t, 3); // Delay
    if (d < 0) return false;
    const int32_t h0 = static_cast<int32_t>(presetTags_.size());
    MixTarget tg{MixKind::PresetTag, t, d, 0};
    const int32_t g = beginMixGesture(&tg, 1);
    const int32_t tag = setPresetTag(t, 0, d, "factory:delay:test", "Test", nullptr, 0);
    endMixGesture(g);
    bool ok = tag == h0 && presetTagOf(t, 0, d) == tag && presetModified(t, 0, d) == 0;
    const int32_t p = 6; // Feedback: a continuous param well inside its range
    const float lo = deviceParamMin(t, d, p), hi = deviceParamMax(t, d, p);
    deviceSetParam(t, d, p, deviceGetParam(t, d, p) > (lo + hi) * 0.5f ? lo : hi);
    ok = ok && presetModified(t, 0, d) == 1;
    ok = ok && undo() && presetTagOf(t, 0, d) == 0 && redo() && presetTagOf(t, 0, d) == tag;
    removeTrack(t);
    clearUndoHistory();
    return ok;
}

} // namespace nota
