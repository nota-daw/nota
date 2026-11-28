// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInternal — shared chain-edit nested mapping helpers across RackOps sources.

#pragma once

#include "devices/DeviceTypes.h"
#include "model/ParamPath.h"
#include "nota/devices/racks/RackCore.h"

#include <cstdint>
#include <utility>

namespace nota::detail {

inline constexpr int32_t kDropRef = -2;

inline int32_t hostOf(const DeviceRef& r) noexcept {
    return r.fam == 1 ? midiHost(r.idx) : r.fam == 2 ? -1 : r.idx;
}

// Slot maps for the common chain edits: a child removed at `idx`, moved within its chain,
// or a new one inserted at `at` (MIDI slots use RackCore::midiTarget encoding).
inline NestedMap slotRemoved(uint64_t uid, int32_t idx, bool midi) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u != uid || (midi ? !RackCore::isMidiTarget(sl) : sl < 0)) return {u, sl};
        const int32_t i = midi ? RackCore::midiTargetIndex(sl) : sl;
        if (i == idx) return {0, -1};
        const int32_t ni = i > idx ? i - 1 : i;
        return {u, midi ? RackCore::midiTarget(ni) : ni};
    };
}
inline NestedMap slotInserted(uint64_t uid, int32_t at, bool midi) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u != uid || (midi ? !RackCore::isMidiTarget(sl) : sl < 0)) return {u, sl};
        const int32_t i = midi ? RackCore::midiTargetIndex(sl) : sl;
        const int32_t ni = i >= at ? i + 1 : i;
        return {u, midi ? RackCore::midiTarget(ni) : ni};
    };
}
inline NestedMap slotMoved(uint64_t uid, int32_t from, int32_t to, bool midi) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u != uid || (midi ? !RackCore::isMidiTarget(sl) : sl < 0)) return {u, sl};
        int32_t i = midi ? RackCore::midiTargetIndex(sl) : sl;
        if (i == from) i = to;
        else if (from < to && i > from && i <= to)
            --i;
        else if (from > to && i >= to && i < from)
            ++i;
        return {u, midi ? RackCore::midiTarget(i) : i};
    };
}
inline NestedMap slotToChain(uint64_t uid, int32_t idx, uint64_t toUid, int32_t toIdx, bool midi) {
    auto removed = slotRemoved(uid, idx, midi);
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u == uid && sl == (midi ? RackCore::midiTarget(idx) : idx))
            return {toUid, midi ? RackCore::midiTarget(toIdx) : toIdx};
        return removed(u, sl);
    };
}
inline NestedMap chainDropped(uint64_t uid, bool instrumentOnly) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u == uid && (!instrumentOnly || sl == -1)) return {0, -1};
        return {u, sl};
    };
}

} // namespace nota::detail
