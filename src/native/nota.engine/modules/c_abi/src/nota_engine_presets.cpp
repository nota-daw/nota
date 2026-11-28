// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — preset tags on built-in & plugin devices (specs/048).

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Engine.h"
#include "nota_engine_internal.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <optional>
#include <string>

namespace {

nota::Engine::Device deviceForSurface(nota::Engine& engRef, int32_t trackId, int32_t surface, int32_t index) {
    auto chain = nota::c_abi::trackHandle(engRef, trackId).devices();
    switch (surface) {
    case 0: return chain.device(index);
    case 1: return chain.instrument();
    case 2: return chain.midiEffect(index);
    default: return {};
    }
}

nota::Engine::Device deviceForSurface(const nota::Engine& engRef, int32_t trackId, int32_t surface, int32_t index) {
    auto chain = nota::c_abi::trackHandle(engRef, trackId).devices();
    switch (surface) {
    case 0: return chain.device(index);
    case 1: return chain.instrument();
    case 2: return chain.midiEffect(index);
    default: return {};
    }
}

void copyTruncatedCString(const std::string& src, char* out, int32_t cap) noexcept {
    if (!out || cap <= 0) return;
    const size_t n = std::min(src.size(), static_cast<size_t>(cap - 1));
    if (n > 0) std::memcpy(out, src.data(), n);
    out[n] = '\0';
}

} // namespace

extern "C" {

int32_t nota_track_device_preset_tag(const NotaEngine* e, int32_t track_id, int32_t surface, int32_t index) {
    if (!e) return -1;
    return deviceForSurface(nota::c_abi::ceng(e), track_id, surface, index).presetTag();
}

int32_t nota_track_device_set_preset_tag(NotaEngine* e, int32_t track_id, int32_t surface, int32_t index,
    const char* preset_id, const char* name, const float* base, int32_t base_count) {
    if (!e) return -1;
    return deviceForSurface(nota::c_abi::eng(e), track_id, surface, index)
        .setPresetTag(preset_id, name, base, base_count);
}

int32_t nota_preset_tag_info(const NotaEngine* e, int32_t tag, char* preset_id, int32_t id_cap, char* name,
    int32_t name_cap, float* base, int32_t base_cap) {
    if (!e) return -1;
    const std::optional<nota::engine::PresetTagInfo> info = nota::c_abi::ceng(e).tracks().presetTagData(tag);
    if (!info) return -1;
    copyTruncatedCString(info->id, preset_id, id_cap);
    copyTruncatedCString(info->name, name, name_cap);
    const int32_t n = static_cast<int32_t>(info->base.size());
    if (base && base_cap > 0) {
        const int32_t m = std::min(n, base_cap);
        for (int32_t i = 0; i < m; ++i) base[i] = info->base[static_cast<size_t>(i)];
    }
    return n;
}

int32_t nota_track_device_preset_modified(const NotaEngine* e, int32_t track_id, int32_t surface, int32_t index) {
    if (!e) return -1;
    return deviceForSurface(nota::c_abi::ceng(e), track_id, surface, index).presetModified();
}

int32_t nota_engine_preset_tag_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).history().presetTagSelfTest()) ? 1 : 0;
}

} // extern "C"
