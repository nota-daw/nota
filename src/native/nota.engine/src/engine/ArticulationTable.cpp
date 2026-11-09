// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Building and reading articulation tables and clip timelines (specs/085). build and
// buildArticulationTimeline run on the message thread; resolve / find / inForceAt are the
// audio thread's, and only search sorted arrays.

#include "ArticulationTable.h"

#include "MidiClip.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

namespace {

bool validAction(const ArticulationAction& a) {
    const int32_t k = static_cast<int32_t>(a.kind);
    if (k < 0 || k > static_cast<int32_t>(ArticulationActionKind::BankSelectLsb)) return false;
    if (a.channel < 0 || a.channel > 16) return false;
    return a.data1 >= 0 && a.data1 <= 127 && a.data2 >= 0 && a.data2 <= 127;
}

} // namespace

std::shared_ptr<const ArticulationTable> ArticulationTable::build(int32_t defaultId, const ArticulationEntry* entries,
    int32_t entryCount, const ArticulationAction* actions, int32_t actionCount) {
    if (entryCount < 0 || actionCount < 0 || entryCount > kArticulationMaxEntries || actionCount > kArticulationMaxActions)
        return nullptr;
    if ((entryCount > 0 && !entries) || (actionCount > 0 && !actions)) return nullptr;
    int32_t prev = 0;
    bool defaultFound = defaultId == 0;
    for (int32_t i = 0; i < entryCount; ++i) {
        const ArticulationEntry& e = entries[i];
        if (e.id <= prev) return nullptr; // > 0 and strictly ascending
        prev = e.id;
        const int32_t t = static_cast<int32_t>(e.trigger);
        if (t < 0 || t > static_cast<int32_t>(ArticulationTrigger::Momentary)) return nullptr;
        if (!std::isfinite(e.offsetMs) || std::abs(e.offsetMs) > kArticulationMaxOffsetMs) return nullptr;
        if (!std::isfinite(e.leadMs) || e.leadMs < 0.0 || e.leadMs > kArticulationMaxLeadMs) return nullptr;
        if (e.actionStart < 0 || e.actionCount < 0 || e.actionStart > actionCount ||
            e.actionCount > actionCount - e.actionStart)
            return nullptr;
        if (e.id == defaultId) defaultFound = true;
    }
    if (defaultId < 0 || !defaultFound) return nullptr;
    for (int32_t i = 0; i < actionCount; ++i)
        if (!validAction(actions[i])) return nullptr;

    auto t = std::make_shared<ArticulationTable>();
    t->defaultId_ = defaultId;
    t->entries_.assign(entries, entries + entryCount);
    t->actions_.assign(actions, actions + actionCount);
    return t;
}

const ArticulationEntry* ArticulationTable::find(int32_t id) const {
    if (id <= 0) return nullptr;
    auto it = std::lower_bound(entries_.begin(), entries_.end(), id,
        [](const ArticulationEntry& e, int32_t v) { return e.id < v; });
    return it != entries_.end() && it->id == id ? &*it : nullptr;
}

const ArticulationEntry* ArticulationTable::resolve(int32_t id) const {
    if (const ArticulationEntry* e = find(id)) return e;
    return find(defaultId_);
}

const ArticulationChange* ArticulationTimeline::inForceAt(double beat) const {
    auto it = std::upper_bound(changes.begin(), changes.end(), beat,
        [](double b, const ArticulationChange& c) { return b < c.beat; });
    return it == changes.begin() ? nullptr : &*(it - 1);
}

uint64_t articulationNotesKey(const MidiClip& clip) {
    // FNV-1a over the fields the timeline reads, in vector order.
    uint64_t h = 1469598103934665603ull;
    auto mix = [&](uint64_t v) {
        for (int i = 0; i < 8; ++i) {
            h ^= (v >> (i * 8)) & 0xFFu;
            h *= 1099511628211ull;
        }
    };
    mix(clip.notes.size());
    for (const Note& n : clip.notes) {
        uint64_t bits = 0;
        static_assert(sizeof(bits) == sizeof(n.startBeat));
        std::memcpy(&bits, &n.startBeat, sizeof(bits));
        mix(bits);
        mix(static_cast<uint64_t>(static_cast<uint32_t>(n.pitch)) << 32 | static_cast<uint32_t>(n.articulation));
    }
    return h;
}

std::shared_ptr<const ArticulationTimeline> buildArticulationTimeline(
    const MidiClip& clip, const std::shared_ptr<const ArticulationTable>& table) {
    auto tl = std::make_shared<ArticulationTimeline>();
    tl->builtFor = table;
    tl->notesKey = articulationNotesKey(clip);
    if (!table) return tl;

    // The notes in start order; at a shared start, ascending pitch, so the order is fixed and
    // the highest note of a chord sets the articulation that stays in force.
    std::vector<const Note*> order;
    order.reserve(clip.notes.size());
    for (const Note& n : clip.notes) order.push_back(&n);
    std::stable_sort(order.begin(), order.end(), [](const Note* a, const Note* b) {
        return a->startBeat < b->startBeat || (a->startBeat == b->startBeat && a->pitch < b->pitch);
    });

    // Every latching note is a point; whether it sends is decided at playback across the whole
    // track (another clip may change the articulation in between). Only a repeat at the same beat
    // (a chord's notes) is folded here: nothing can come between them.
    for (const Note* n : order) {
        const ArticulationEntry* e = table->resolve(n->articulation);
        if (!e || e->trigger != ArticulationTrigger::Latching) continue; // momentary: per note, latch untouched
        if (!tl->changes.empty() && tl->changes.back().beat == n->startBeat && tl->changes.back().entryId == e->id) continue;
        tl->changes.push_back({n->startBeat, e->id, n->pitch});
    }
    return tl;
}

bool articulationTimelineCurrent(const MidiClip& clip, const std::shared_ptr<const ArticulationTable>& table) {
    if (!table) return clip.articulations == nullptr;
    const ArticulationTimeline* tl = clip.articulations.get();
    return tl && tl->builtFor == table && tl->notesKey == articulationNotesKey(clip);
}

} // namespace nota
