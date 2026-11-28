// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Internal helpers shared across the C ABI translation units.
// Translates pure C handles/structs strictly to public nota::Engine OOP APIs
// and public module headers (zero internal engine runtime includes).

#pragma once

#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/engine/NotaEngine.h"
#include "nota/nota_engine.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <utility>
#include <vector>

struct NotaEngine {
    nota::Engine engine;
    mutable std::vector<nota::engine::MidiPortRow> midiPortsCache;
    mutable std::vector<std::string> sectionNamesCache;
};

struct NotaAudition {
    nota::engine::AuditionSession session;
    explicit NotaAudition(double sr) : session(sr) {}
};

struct NotaAuroraTable {
    std::shared_ptr<const nota::dsp::WavetableAsset> table;
};

namespace nota::c_abi {

inline Engine& eng(NotaEngine* e) noexcept {
    return e->engine;
}
inline const Engine& ceng(const NotaEngine* e) noexcept {
    return e->engine;
}

inline Engine::Track trackHandle(Engine& e, int32_t trackId) {
    return trackId == engine::kMasterTrackId ? e.tracks().master() : e.tracks().byId(trackId);
}

inline const Engine::Track trackHandle(const Engine& e, int32_t trackId) {
    return trackId == engine::kMasterTrackId ? e.tracks().master() : e.tracks().byId(trackId);
}

inline Engine::Device deviceOrInst(Engine& e, int32_t trackId, int32_t deviceIndex) {
    auto chain = trackHandle(e, trackId).devices();
    return deviceIndex < 0 ? chain.instrument() : chain.device(deviceIndex);
}

inline Engine::Device deviceOrInst(const Engine& e, int32_t trackId, int32_t deviceIndex) {
    auto chain = trackHandle(e, trackId).devices();
    return deviceIndex < 0 ? chain.instrument() : chain.device(deviceIndex);
}

inline Engine::Rack rackHandle(Engine& e, int32_t trackId, int32_t deviceIndex) {
    auto chain = trackHandle(e, trackId).devices();
    if (deviceIndex == -1) return chain.instrument().asRack();
    if (deviceIndex <= engine::kMidiRackBase && !engine::isNestedRackAddress(deviceIndex))
        return chain.midiEffect(engine::kMidiRackBase - deviceIndex).asRack();
    return chain.device(deviceIndex).asRack();
}

inline Engine::Rack rackHandle(const Engine& e, int32_t trackId, int32_t deviceIndex) {
    auto chain = trackHandle(e, trackId).devices();
    if (deviceIndex == -1) return chain.instrument().asRack();
    if (deviceIndex <= engine::kMidiRackBase && !engine::isNestedRackAddress(deviceIndex))
        return chain.midiEffect(engine::kMidiRackBase - deviceIndex).asRack();
    return chain.device(deviceIndex).asRack();
}

// Thread-local staging buffer for C ABI functions returning transient UTF-8 strings.
inline const char* cacheThreadString(std::string s) {
    thread_local std::string buf;
    buf = std::move(s);
    return buf.c_str();
}

// Writes up to cap - 1 bytes + NUL and returns the full source length.
inline int32_t copyCStringNul(const std::string& s, char* out, int32_t cap) noexcept {
    const int32_t n = static_cast<int32_t>(s.size());
    if (out && cap > 0) {
        const int32_t m = std::min(n, cap - 1);
        if (m > 0) std::memcpy(out, s.data(), static_cast<size_t>(m));
        out[m] = '\0';
    }
    return n;
}

// Writes up to cap bytes without adding a NUL terminator and returns the full length.
inline int32_t copyRawBytes(const std::string& s, char* out, int32_t cap) noexcept {
    const size_t n = s.size();
    if (out && cap > 0) {
        const size_t m = std::min(n, static_cast<size_t>(cap));
        if (m > 0) std::memcpy(out, s.data(), m);
    }
    return static_cast<int32_t>(n);
}

inline int32_t copyByteVector(const std::vector<uint8_t>& v, uint8_t* out, int32_t cap) noexcept {
    const int32_t n = static_cast<int32_t>(v.size());
    if (out && cap > 0) {
        const int32_t m = std::min(n, cap);
        if (m > 0) std::memcpy(out, v.data(), static_cast<size_t>(m));
    }
    return n;
}

inline engine::NoteData toCppNote(const NotaNoteData& n) noexcept {
    return {n.pitch, n.start_beat, n.length_beats, n.velocity, n.expr_id, n.articulation_id};
}

inline NotaNoteData toCNote(const engine::NoteData& n) noexcept {
    return {n.pitch, n.startBeat, n.lengthBeats, n.velocity, n.exprId, n.articulationId};
}

inline engine::AutomationPointData toCppAutoPoint(const NotaAutomationPoint& p) noexcept {
    return {p.beat, p.value, p.curve};
}

inline NotaAutomationPoint toCAutoPoint(const engine::AutomationPointData& p) noexcept {
    return {p.beat, p.value, p.curve};
}

inline std::vector<engine::AutomationPointData> convertAutoPointsIn(const NotaAutomationPoint* pts, int32_t count) {
    std::vector<engine::AutomationPointData> out;
    if (pts && count > 0) {
        out.reserve(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) out.push_back(toCppAutoPoint(pts[i]));
    }
    return out;
}

inline int32_t rackMappingInfoImpl(const Engine& engRef, int32_t t, int32_t di, int32_t index, int32_t* macro,
    int32_t* chain, int32_t* targetDev, int32_t* pi, float* lo, float* hi) {
    int32_t mo = 0, ch = 0, dv = 0, px = 0;
    float rmin = 0.0f, rmax = 1.0f;
    if (!rackHandle(engRef, t, di).mappingInfo(index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo;
    if (chain) *chain = ch;
    if (targetDev) *targetDev = dv;
    if (pi) *pi = px;
    if (lo) *lo = rmin;
    if (hi) *hi = rmax;
    return 1;
}

void fillAuroraInfo(const dsp::WavetableAsset* t, NotaAuroraTableInfo* out) noexcept;

} // namespace nota::c_abi
