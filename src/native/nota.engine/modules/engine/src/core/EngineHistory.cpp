// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — lifecycle, audio/MIDI device config, preview, tone/transport setters, immutable-graph publish/clone/undo/reset, post-fader meters. Definitions of the rest of the class live in the sibling Engine_*.cpp slices.

#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "model/Track.h"
#include "model/Graph.h"
#include "model/EngineTypes.h"
#include "core/TempoMap.h"
#include "render/RenderRoutingPlan.h"
#include "model/TimelineIndex.h"
#include "nota/dsp/capabilities/ISlotAnnotationDevice.h"
#include "nota/devices/racks/RackCore.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota::detail {

void EngineRuntime::pushUndo() {
    if (!authoring_) return;
    // Inside an undo group only the first snapshot counts (endUndoGroup keeps the pre-group
    // graph), so later ones aren't stacked: a gesture of many edits (a kit filling a Drum
    // Rack's pads) would otherwise push the older history off the depth limit.
    if (undoGroupDepth_ > 0) {
        for (size_t i = undoGroupBase_; i < undoStack_.size(); ++i)
            if (undoStack_[i].graph) {
                ++undoPushCount_;
                redoStack_.clear();
                return;
            }
    }
    // authoring_ is immutable after publish. A take's own checkpoint is marked so a retry can
    // take it out without touching the user's edits made meanwhile (specs/078).
    undoStack_.push_back({authoring_, {}, takePushing_ && takeAttempt_.active});
    ++undoPushCount_;
    if (undoStack_.size() > kMaxUndoDepth) {
        undoStack_.erase(undoStack_.begin());
        if (undoGroupDepth_ > 0 && undoGroupBase_ > 0) --undoGroupBase_;               // keep the group's anchor
        if (takeAttempt_.active && takeAttempt_.undoBase > 0) --takeAttempt_.undoBase; // specs/078
        if (sessionPass_.active && passUndoBase_ > 0) --passUndoBase_;                 // #971: the pass's anchor
    }
    redoStack_.clear(); // a new edit invalidates the redo branch
}

// Undo/redo: a snapshot entry swaps the graph (the current state becomes the opposite
// stack's target); then its mix changes write their before (undo) / after (redo) values —
// after the graph, so a restored track gets its value. An entry that changes nothing (all
// of its tracks gone) is passed over, so one call always does something if it can.
bool EngineRuntime::undo() {
    while (!undoStack_.empty()) {
        UndoEntry e = std::move(undoStack_.back());
        undoStack_.pop_back();
        bool applied = false;
        redoStack_.push_back({e.graph ? authoring_ : nullptr, e.mix});
        // Restore without a new checkpoint; the restored chains may add or drop a latent
        // device (a removed look-ahead coming back, #109), so compensation follows them.
        if (e.graph) {
            if (authoring_) keepLiveSends(*e.graph, *authoring_);
            publishRaw(std::move(e.graph), true);
            recorder().placeTakesForTempo(); // the snapshot may predate a tempo change
            devices().recomputePdc();
            reapplyRackMacros();
            racks().reapplyCcRoutes();
            applied = true;
        }
        for (const auto& c : e.mix) applied |= applyMix(c, false);
        if (applied) return true;
    }
    return false;
}

// specs/069. Message thread, before the restored graph is published. A shared track object
// already holds the live levels, so it is left alone (the audio thread may be reading it).
void EngineRuntime::keepLiveSends(Graph& restored, const Graph& live) const {
    // No allocations: this runs on every undo/redo (perf-gate edit-warp-split).
    const auto busOf = [](const std::shared_ptr<Track>& t) {
        return t && t->type() == TrackType::Return && t->returnIndex() >= 0 && t->returnIndex() < kMaxReturns
                   ? t->returnIndex()
                   : -1;
    };
    // The track with `id` in `g`: track lists rarely reorder, so try the same index first.
    const auto findById = [](const Graph& g, size_t hint, int32_t id) -> const Track* {
        if (hint < g.tracks.size() && g.tracks[hint] && g.tracks[hint]->id() == id) return g.tracks[hint].get();
        for (const auto& t : g.tracks)
            if (t && t->id() == id) return t.get();
        return nullptr;
    };

    // A return edit remaps buses. Copying a live slot onto the restored graph would put
    // that level on a different return. The snapshot already has the levels for its buses.
    // Same buses = the same (return id, bus) pairs on both sides; ids are unique.
    size_t liveReturns = 0, oldReturns = 0;
    for (const auto& t : restored.tracks)
        if (busOf(t) >= 0) ++oldReturns;
    for (size_t i = 0; i < live.tracks.size(); ++i) {
        const int32_t bus = busOf(live.tracks[i]);
        if (bus < 0) continue;
        ++liveReturns;
        const Track* old = findById(restored, i, live.tracks[i]->id());
        if (!old || old->type() != TrackType::Return || old->returnIndex() != bus) return;
    }
    if (liveReturns != oldReturns) return;

    for (size_t i = 0; i < restored.tracks.size(); ++i) {
        auto& t = restored.tracks[i];
        if (!t || t->type() == TrackType::Return) continue;
        const Track* src = findById(live, i, t->id());
        if (!src || src == t.get()) continue;
        for (int b = 0; b < kMaxReturns; ++b)
            if (t->send(b) != src->send(b)) t->setSend(b, src->send(b));
    }
}

// The restored racks drive their mapped targets again: the children they share with the
// copies an edit made still hold what those copies' mappings gave them (specs/040).
void EngineRuntime::reapplyRackMacros() {
    if (!authoring_) return;
    auto each = [](const std::shared_ptr<Track>& t) {
        if (!t) return;
        if (auto* r = dynamic_cast<RackCore*>(t->instrument.get())) r->reapplyMacros();
        for (auto& d : t->devices)
            if (auto* r = dynamic_cast<RackCore*>(d.get())) r->reapplyMacros();
        for (auto& m : t->midiEffects)
            if (auto* r = dynamic_cast<RackCore*>(m.get())) r->reapplyMacros();
    };
    for (auto& t : authoring_->tracks) each(t);
    each(authoring_->masterTrack);
}

bool EngineRuntime::redo() {
    while (!redoStack_.empty()) {
        UndoEntry e = std::move(redoStack_.back());
        redoStack_.pop_back();
        bool applied = false;
        undoStack_.push_back({e.graph ? authoring_ : nullptr, e.mix});
        if (e.graph) {
            if (authoring_) keepLiveSends(*e.graph, *authoring_);
            publishRaw(std::move(e.graph), true);
            recorder().placeTakesForTempo(); // the snapshot may predate a tempo change
            devices().recomputePdc();
            reapplyRackMacros();
            racks().reapplyCcRoutes();
            applied = true;
        }
        for (const auto& c : e.mix) applied |= applyMix(c, true);
        if (applied) return true;
    }
    return false;
}

// --- mix gestures (specs/009) ----------------------------------------------

namespace {
bool isParamKind(MixKind k) {
    return k == MixKind::DeviceParam || k == MixKind::MidiDeviceParam || k == MixKind::PluginParam;
}
// The CV-modulation target kind (Engine_Modulation) a parameter value step addresses.
int32_t cvKindOf(MixKind k) {
    return k == MixKind::DeviceParam ? 0 : k == MixKind::PluginParam ? 1 : 2;
}
} // namespace

// Does the parameter a value step addresses exist right now? (specs/016: a step whose
// device is gone is skipped, like one whose track is gone.)
bool EngineRuntime::paramTargetExists(MixKind kind, int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    if (paramIndex < 0) return false;
    switch (kind) {
    case MixKind::DeviceParam: {
        auto* d = devices().deviceAt(trackId, deviceIndex);
        return d && paramIndex < d->paramCount();
    }
    case MixKind::MidiDeviceParam: {
        auto* m = devices().midiDeviceAt(trackId, deviceIndex);
        return m && paramIndex < m->paramCount();
    }
    case MixKind::PluginParam: return paramIndex < automation().pluginParamCount(trackId, deviceIndex);
    default: return false;
    }
}

bool EngineRuntime::mixValue(const MixTarget& t, float& v) const {
    if (t.kind == MixKind::PresetTag) { // specs/048: the tag index, exact in a float
        const int32_t tag = devices().presetTagOf(t.trackId, t.paramIndex, t.deviceIndex);
        if (tag < 0) return false;
        v = static_cast<float>(tag);
        return true;
    }
    if (t.kind == MixKind::MasterVolume) {
        v = masterVolume();
        return true;
    }
    if (isParamKind(t.kind)) {
        if (!paramTargetExists(t.kind, t.trackId, t.deviceIndex, t.paramIndex)) return false;
        // A CV-modulated parameter: the user edits (and undo restores) the link's base, not
        // the value the modulation is driving right now.
        if (automation().cvBaseValue(cvKindOf(t.kind), t.trackId, t.deviceIndex, t.paramIndex, v)) return true;
        switch (t.kind) {
        case MixKind::DeviceParam: v = devices().deviceGetParam(t.trackId, t.deviceIndex, t.paramIndex); break;
        case MixKind::MidiDeviceParam: v = devices().midiEffectGetParam(t.trackId, t.deviceIndex, t.paramIndex); break;
        default: v = automation().pluginParamGet(t.trackId, t.deviceIndex, t.paramIndex); break;
        }
        return true;
    }
    auto tr = findTrackAuthoring(t.trackId);
    if (!tr) return false;
    if (t.kind == MixKind::TrackSend) return false; // specs/069: a send is not an undo step
    v = t.kind == MixKind::TrackVolume ? tr->volume() : tr->pan();
    return true;
}

bool EngineRuntime::applyMix(const MixChange& c, bool after) {
    const float v = after ? c.after : c.before;
    if (c.kind == MixKind::PresetTag)
        return devices().setPresetTagIndex(
            c.trackId, c.paramIndex, c.deviceIndex, static_cast<int32_t>(v)); // gone: skip
    if (c.kind == MixKind::DeviceNotes) {                                     // specs/049: an EQ-8's labels and notes
        auto* dev = devices().deviceAt(c.trackId, c.deviceIndex);
        const auto at = static_cast<size_t>(v);
        if (!dev || !dynamic_cast<const dsp::capabilities::ISlotAnnotationDevice*>(dev) || at >= noteStates_.size())
            return false; // gone (or no longer annotatable): skip
        const auto& st = noteStates_[at];
        dev->setState(st.data(), static_cast<int32_t>(st.size()));
        return true;
    }
    if (c.kind == MixKind::InstrumentEdit) { // an instrument's edit state (sequencer patterns)
        Instrument* inst = devices().editStateInstrument(c.trackId, c.deviceIndex, c.paramIndex);
        const auto at = static_cast<size_t>(v);
        if (!inst || at >= noteStates_.size()) return false; // gone: skip
        const auto& st = noteStates_[at];
        inst->setEditState(st.data(), static_cast<int32_t>(st.size()));
        return true;
    }
    if (c.kind == MixKind::InstrumentState) { // a rack chain instrument's whole state
        Instrument* inst = devices().editStateInstrument(c.trackId, c.deviceIndex, c.paramIndex);
        const auto at = static_cast<size_t>(v);
        if (!inst || at >= noteStates_.size()) return false; // gone: skip
        const auto& st = noteStates_[at];
        inst->setState(st.data(), static_cast<int32_t>(st.size()));
        return true;
    }
    if (c.kind == MixKind::MasterVolume) {
        setMasterVolume(v);
        return true;
    }
    if (isParamKind(c.kind)) {
        if (!paramTargetExists(c.kind, c.trackId, c.deviceIndex, c.paramIndex)) return false; // gone: skip
        switch (c.kind) { // the user-facing setters: CV bases, PDC and plugin hosts follow
        case MixKind::DeviceParam: devices().deviceSetParam(c.trackId, c.deviceIndex, c.paramIndex, v); break;
        case MixKind::MidiDeviceParam: devices().midiEffectSetParam(c.trackId, c.deviceIndex, c.paramIndex, v); break;
        default: automation().pluginParamSet(c.trackId, c.deviceIndex, c.paramIndex, v); break;
        }
        return true;
    }
    auto tr = findTrackAuthoring(c.trackId);
    if (!tr) return false;                          // the track is gone: skip quietly
    if (c.kind == MixKind::TrackSend) return false; // specs/069: nothing to restore
    if (c.kind == MixKind::TrackVolume) tr->setVolume(v);
    else
        tr->setPan(v);
    return true;
}

// Fold `later` into `into`: per target the older before, the newer after; a target that
// ends where it started is no change at all.
void EngineRuntime::mergeMix(std::vector<MixChange>& into, const std::vector<MixChange>& later) {
    for (const auto& c : later) {
        auto it = std::find_if(into.begin(), into.end(), [&](const MixChange& m) {
            return m.kind == c.kind &&
                   (c.kind == MixKind::MasterVolume ||
                       (m.trackId == c.trackId && m.deviceIndex == c.deviceIndex && m.paramIndex == c.paramIndex));
        });
        if (it == into.end()) into.push_back(c);
        else
            it->after = c.after;
    }
    into.erase(
        std::remove_if(into.begin(), into.end(), [](const MixChange& m) { return m.before == m.after; }), into.end());
}

// --- Stereo Map measurements (specs/008) ------------------------------------

namespace {
// Level, balance (on the pan scale — the equal-power pan that gives this energy split, so a
// mono source panned to p reads p), correlation, and width = 1 - correlation (0 for any mono
// source wherever it's panned, 1 for unrelated or anti-phase material). specs/008 research R2.
StereoMeasure deriveStereo(int32_t id, float eL, float eR, float eLR) {
    StereoMeasure m{id, -200.0f, 0.0f, 0.0f, 0.0f};
    const double l = std::max(0.0f, eL), r = std::max(0.0f, eR), sum = l + r;
    if (sum > 1e-20) m.levelDb = static_cast<float>(10.0 * std::log10(sum * 0.5));
    if (sum > 1e-20)
        m.balance = static_cast<float>(std::atan2(std::sqrt(r), std::sqrt(l)) * (4.0 / 3.14159265358979323846) - 1.0);
    if (l > 1e-12 && r > 1e-12) m.correlation = static_cast<float>(std::clamp(eLR / std::sqrt(l * r), -1.0, 1.0));
    m.width = std::clamp(1.0f - m.correlation, 0.0f, 1.0f);
    return m;
}
} // namespace

int32_t EngineRuntime::stereoMeasures(StereoMeasure* out, int32_t cap) const {
    int32_t n = 0;
    auto put = [&](const StereoMeasure& m) {
        if (out && n < cap) out[n] = m;
        ++n;
    };
    if (authoring_)
        for (const auto& t : authoring_->tracks)
            if (t && t->type() != TrackType::Group)
                put(deriveStereo(t->id(), t->stereoL(), t->stereoR(), t->stereoLR()));
    put(deriveStereo(kMasterTrackId, masterStereoL_.load(std::memory_order_relaxed),
        masterStereoR_.load(std::memory_order_relaxed), masterStereoLR_.load(std::memory_order_relaxed)));
    return n;
}

bool EngineRuntime::mixUndoSelfTest() {
    clearUndoHistory();
    const float m0 = masterVolume();
    undoStack_.push_back({nullptr, {{MixKind::MasterVolume, -1, m0, 0.25f}}}); // a valid step…
    setMasterVolume(0.25f);
    undoStack_.push_back({nullptr, {{MixKind::TrackVolume, 0x7fff0001, 1.0f, 0.5f}}}); // …under a dangling one
    const bool undid = undo() && masterVolume() == m0 && undoStack_.empty() && redoStack_.size() == 2;
    const bool redid = redo() && masterVolume() == 0.25f; // the valid step is on top of redo
    const bool rest = !redo() && redoStack_.empty();      // the dangling one applies nothing
    clearUndoHistory();
    setMasterVolume(m0);
    return undid && redid && rest;
}

int32_t EngineRuntime::beginMixGesture(const MixTarget* targets, int32_t count) {
    if (!targets || count <= 0) return 0;
    OpenMixGesture g{nextMixGesture_, {}, undoPushCount_, undoStack_.size()};
    for (int32_t i = 0; i < count; ++i) {
        float v = 0.0f;
        if (mixValue(targets[i], v))
            g.targets.push_back(
                {targets[i].kind, targets[i].trackId, v, v, targets[i].deviceIndex, targets[i].paramIndex});
    }
    if (g.targets.empty()) return 0;
    if (++nextMixGesture_ <= 0) nextMixGesture_ = 1;
    mixGestures_.push_back(std::move(g));
    return mixGestures_.back().handle;
}

void EngineRuntime::endMixGesture(int32_t handle) {
    auto it = std::find_if(
        mixGestures_.begin(), mixGestures_.end(), [&](const OpenMixGesture& g) { return g.handle == handle; });
    if (it == mixGestures_.end()) return;
    OpenMixGesture g = std::move(*it);
    mixGestures_.erase(it);
    std::vector<MixChange> changes;
    for (auto& c : g.targets) {
        float v = 0.0f;
        if (!mixValue({c.kind, c.trackId, c.deviceIndex, c.paramIndex}, v)) continue; // gone mid-gesture
        if (v == c.before) continue;                                                  // exact: no net change, no step
        c.after = v;
        changes.push_back(c);
    }
    if (changes.empty()) return;
    // Checkpoints pushed during the gesture (automation recording: the lane's creation and
    // the pre-write state) and this value change are one user action: collapse everything
    // the gesture stacked into its first entry, as an undo group would, and add the values.
    if (undoPushCount_ != g.pushCountAtBegin && !undoStack_.empty()) {
        const size_t base = std::min(g.stackSizeAtBegin, undoStack_.size() - 1);
        auto first = undoStack_.begin() + static_cast<std::ptrdiff_t>(base);
        for (auto entryIt = first + 1; entryIt != undoStack_.end(); ++entryIt) {
            if (!first->graph) {
                first->graph = entryIt->graph;
                first->take = entryIt->take; // a take's checkpoint stays the take's (specs/078)
            }
            mergeMix(first->mix, entryIt->mix);
        }
        undoStack_.erase(first + 1, undoStack_.end());
        mergeMix(first->mix, changes);
        return;
    }
    undoStack_.push_back({nullptr, std::move(changes)});
    if (undoStack_.size() > kMaxUndoDepth) {
        undoStack_.erase(undoStack_.begin());
        if (undoGroupDepth_ > 0 && undoGroupBase_ > 0) --undoGroupBase_;
    }
    redoStack_.clear();
}

// --- project pan law (specs/006) -------------------------------------------

bool EngineRuntime::setPanLaw(PanLaw law, bool checkpoint) {
    if (law != PanLaw::Balance && law != PanLaw::Stereo) return false;
    if (!authoring_ || authoring_->panLaw == law) return true;
    auto g = std::make_shared<Graph>(*authoring_); // copy: tracks shared, graph-level fields kept
    g->panLaw = law;
    if (checkpoint) publish(std::move(g));
    else { // load / baseline: no crossfade from the previous project's law
        publishRaw(std::move(g));
        panLawSnap_.store(true, std::memory_order_release);
    }
    return true;
}

const TrackLane* EngineRuntime::masterTempoLane(const Graph& g) {
    if (!g.masterTrack) return nullptr;
    for (const auto& lane : g.masterTrack->automation)
        if (lane.target == AutomationTarget::Tempo) return &lane;
    return nullptr;
}

bool EngineRuntime::tempoAutomated() const {
    return tempoMap() != nullptr;
}

void EngineRuntime::deriveTempoMap(Graph& g) const {
    const TrackLane* lane = masterTempoLane(g);
    const bool hasPoints = lane && !lane->points.empty();
    const auto& old = authoring_ ? authoring_->tempoMap : nullptr;
    if (!hasPoints && !g.tempoMap) return;
    if (hasPoints && g.tempoMap && g.tempoMap->source.size() == lane->points.size() &&
        std::equal(lane->points.begin(), lane->points.end(), g.tempoMap->source.begin(),
            [](const AutomationPoint& a, const AutomationPoint& b) {
                return a.beat == b.beat && a.value == b.value && a.curve == b.curve;
            }))
        return;
    g.tempoMap = hasPoints ? TempoMap::fromLane(lane->points) : nullptr;
    if (g.tempoMap == old) return;
    // Natural-rate recordings are physical-time anchors: reproject their beat placement,
    // never their source samples, when the musical ruler changes. Only tracks this edit left
    // alone (still shared with the published graph): an edit that moved clips itself (a
    // section move carrying clips and tempo together) placed them already.
    auto shared = [&](const std::shared_ptr<Track>& t) {
        if (!authoring_) return false;
        for (const auto& o : authoring_->tracks)
            if (o == t) return true;
        return false;
    };
    auto toBeat = [&](double seconds) {
        return g.tempoMap ? g.tempoMap->beatAt(seconds) : seconds * authoringBpm_ / 60.0;
    };
    for (auto& t : g.tracks) {
        if (!shared(t)) { // this edit's own clone: its placement stands, its takes follow the map
            for (auto& take : t->takes.write()) take.tempoMap = g.tempoMap;
            continue;
        }
        if (t->takes.empty() &&
            std::none_of(t->clips.begin(), t->clips.end(), [](const auto& c) { return !c.warpEnabled; }))
            continue;
        t = cloneTrack(*t);
        for (auto& c : t->clips.write())
            if (!c.warpEnabled) c.startBeat = toBeat(beatToSeconds(c.startBeat));
        for (auto& take : t->takes.write()) {
            take.originBeat = toBeat(beatToSeconds(take.originBeat));
            take.tempoMap = g.tempoMap;
        }
    }
}

// --- arrangement sections (specs/016) ---------------------------------------

void EngineRuntime::setSections(std::vector<Section> sections, bool checkpoint) {
    if (!authoring_) return;
    sections.erase(std::remove_if(sections.begin(), sections.end(),
                       [](const Section& s) { return !(s.lengthBeats > 0.0); }), // also drops NaN
        sections.end());
    for (auto& s : sections)
        if (!(s.startBeat > 0.0)) s.startBeat = 0.0;
    std::stable_sort(
        sections.begin(), sections.end(), [](const Section& a, const Section& b) { return a.startBeat < b.startBeat; });
    if (sections == authoring_->sections) return;  // nothing changed: no step
    auto g = std::make_shared<Graph>(*authoring_); // copy: tracks shared, graph-level fields kept
    g->sections = std::move(sections);
    if (checkpoint) publish(std::move(g));
    else
        publishRaw(std::move(g));
}

} // namespace nota::detail
