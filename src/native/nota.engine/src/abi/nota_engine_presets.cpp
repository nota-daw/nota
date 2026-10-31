// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — preset tags (specs/048): which preset a device was loaded from and whether it still
// holds that preset's values. Thin wrappers over Engine_Presets.cpp.

#include "nota_engine_internal.h"

#include <cstring>

namespace {
void copyOut(const std::string& s, char* out, int32_t cap) {
    if (!out || cap <= 0) return;
    const size_t n = std::min(s.size(), static_cast<size_t>(cap - 1));
    std::memcpy(out, s.data(), n);
    out[n] = '\0';
}
} // namespace

extern "C" {

int32_t nota_track_device_preset_tag(const NotaEngine* e, int32_t track_id, int32_t surface, int32_t index) {
    return e ? CENG(e)->presetTagOf(track_id, surface, index) : -1;
}

int32_t nota_track_device_set_preset_tag(NotaEngine* e, int32_t track_id, int32_t surface, int32_t index,
    const char* preset_id, const char* name, const float* base, int32_t base_count) {
    return e ? ENG(e)->setPresetTag(track_id, surface, index, preset_id, name, base, base_count) : -1;
}

int32_t nota_preset_tag_info(const NotaEngine* e, int32_t tag, char* preset_id, int32_t id_cap, char* name,
    int32_t name_cap, float* base, int32_t base_cap) {
    if (!e) return -1;
    const auto* t = CENG(e)->presetTagData(tag);
    if (!t) return -1;
    copyOut(t->id, preset_id, id_cap);
    copyOut(t->name, name, name_cap);
    const int32_t n = static_cast<int32_t>(t->base.size());
    if (base)
        for (int32_t i = 0; i < n && i < base_cap; ++i) base[i] = t->base[static_cast<size_t>(i)];
    return n;
}

int32_t nota_track_device_preset_modified(const NotaEngine* e, int32_t track_id, int32_t surface, int32_t index) {
    return e ? CENG(e)->presetModified(track_id, surface, index) : -1;
}

int32_t nota_engine_preset_tag_selftest(NotaEngine* e) {
    return (e && ENG(e)->presetTagSelfTest()) ? 1 : 0;
}

} // extern "C"
