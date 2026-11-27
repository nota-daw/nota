// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/DeviceRegistry.h"

#include "nota/dsp/Device.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiDevice.h"

#include <algorithm>
#include <memory>
#include <cstdint>
#include <mutex>
#include <unordered_map>
#include <utility>
#include <vector>

namespace nota::dsp {
namespace {

struct RegistryState {
    std::mutex mutex;
    std::unordered_map<int32_t, DeviceRegistry::AudioEffectFactory> audioEffects;
    std::unordered_map<int32_t, DeviceRegistry::AudioEffectFactory> chainEffects;
    std::unordered_map<int32_t, DeviceRegistry::InstrumentFactory> instruments;
    std::unordered_map<int32_t, DeviceRegistry::MidiEffectFactory> midiEffects;
};

RegistryState& state() {
    static RegistryState s;
    return s;
}

template <typename Map> std::vector<int32_t> sortedKeys(const Map& map) {
    std::vector<int32_t> keys;
    keys.reserve(map.size());
    for (const auto& [kind, unused] : map) {
        (void)unused;
        keys.push_back(kind);
    }
    std::sort(keys.begin(), keys.end());
    return keys;
}

} // namespace

void DeviceRegistry::registerAudioEffect(int32_t kind, AudioEffectFactory factory, bool allowInRackChain) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    if (!factory) {
        st.audioEffects.erase(kind);
        st.chainEffects.erase(kind);
        return;
    }
    st.audioEffects[kind] = factory;
    if (allowInRackChain) { st.chainEffects[kind] = std::move(factory); }
}

void DeviceRegistry::registerChainEffect(int32_t kind, AudioEffectFactory factory) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    if (!factory) {
        st.chainEffects.erase(kind);
        return;
    }
    st.chainEffects[kind] = std::move(factory);
}

void DeviceRegistry::registerInstrument(int32_t kind, InstrumentFactory factory) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    if (!factory) {
        st.instruments.erase(kind);
        return;
    }
    st.instruments[kind] = std::move(factory);
}

void DeviceRegistry::registerMidiEffect(int32_t kind, MidiEffectFactory factory) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    if (!factory) {
        st.midiEffects.erase(kind);
        return;
    }
    st.midiEffects[kind] = std::move(factory);
}

std::shared_ptr<Device> DeviceRegistry::createAudioEffect(int32_t kind) {
    AudioEffectFactory fn;
    {
        auto& st = state();
        std::lock_guard<std::mutex> lock(st.mutex);
        if (const auto it = st.audioEffects.find(kind); it != st.audioEffects.end()) fn = it->second;
    }
    return fn ? fn() : nullptr;
}

std::shared_ptr<Device> DeviceRegistry::createChainEffect(int32_t kind) {
    AudioEffectFactory fn;
    {
        auto& st = state();
        std::lock_guard<std::mutex> lock(st.mutex);
        if (const auto it = st.chainEffects.find(kind); it != st.chainEffects.end()) fn = it->second;
    }
    return fn ? fn() : nullptr;
}

std::shared_ptr<Instrument> DeviceRegistry::createInstrument(int32_t kind) {
    InstrumentFactory fn;
    {
        auto& st = state();
        std::lock_guard<std::mutex> lock(st.mutex);
        if (const auto it = st.instruments.find(kind); it != st.instruments.end()) fn = it->second;
    }
    return fn ? fn() : nullptr;
}

std::shared_ptr<MidiDevice> DeviceRegistry::createMidiEffect(int32_t kind) {
    MidiEffectFactory fn;
    {
        auto& st = state();
        std::lock_guard<std::mutex> lock(st.mutex);
        if (const auto it = st.midiEffects.find(kind); it != st.midiEffects.end()) fn = it->second;
    }
    return fn ? fn() : nullptr;
}

bool DeviceRegistry::hasAudioEffect(int32_t kind) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return st.audioEffects.contains(kind);
}

bool DeviceRegistry::hasChainEffect(int32_t kind) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return st.chainEffects.contains(kind);
}

bool DeviceRegistry::hasInstrument(int32_t kind) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return st.instruments.contains(kind);
}

bool DeviceRegistry::hasMidiEffect(int32_t kind) {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return st.midiEffects.contains(kind);
}

std::vector<int32_t> DeviceRegistry::audioEffectKinds() {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return sortedKeys(st.audioEffects);
}

std::vector<int32_t> DeviceRegistry::chainEffectKinds() {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return sortedKeys(st.chainEffects);
}

std::vector<int32_t> DeviceRegistry::instrumentKinds() {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return sortedKeys(st.instruments);
}

std::vector<int32_t> DeviceRegistry::midiEffectKinds() {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    return sortedKeys(st.midiEffects);
}

void DeviceRegistry::clearForTests() {
    auto& st = state();
    std::lock_guard<std::mutex> lock(st.mutex);
    st.audioEffects.clear();
    st.chainEffects.clear();
    st.instruments.clear();
    st.midiEffects.clear();
}

} // namespace nota::dsp
