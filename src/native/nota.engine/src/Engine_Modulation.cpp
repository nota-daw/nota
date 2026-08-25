// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Engine — CV modulation (Phase 3, Modular editor). Modulator (LFO) sources and
// CvLink edges to built-in device params. Add/remove are structural (clone +
// republish); field/depth/mode edits hit the live object's atomics. The block-rate
// apply/restore runs inside mixGraph (Engine_Render.cpp): each block we swing every
// modulated param around its base (= the device's atomic, which the UI wrote) and
// restore the base afterwards, so cards/knobs keep showing the base while the audio
// hears the modulation. Modulators are stateless (see Modulation.h), so this whole
// layer copies trivially across graph snapshots and needs no per-block phase state.

#include "Engine.h"

#include <algorithm>

namespace nota {

// ---- helpers ---------------------------------------------------------------

Modulator* Engine::modulatorPtr(Track& t, int32_t modId) {
    for (auto& m : t.modulators) if (m.id == modId) return &m;
    return nullptr;
}

// ---- modulator CRUD (message thread, structural) ---------------------------

int32_t Engine::addModulator(int32_t trackId, int32_t kind) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    auto nt = cloneTrack(*old);
    Modulator m;
    m.id = nt->nextModId++;
    m.kind = static_cast<ModulatorKind>(kind);
    const int32_t id = m.id;
    nt->modulators.push_back(m);
    republishWithTrack(trackId, nt);
    return id;
}

bool Engine::removeModulator(int32_t trackId, int32_t modId) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    const size_t before = nt->modulators.size();
    nt->modulators.erase(
        std::remove_if(nt->modulators.begin(), nt->modulators.end(),
                       [&](const Modulator& m) { return m.id == modId; }),
        nt->modulators.end());
    if (nt->modulators.size() == before) return false;
    // Restore + drop links fed by the removed modulator (leave param-source links alone).
    auto fedBy = [&](const CvLink& l) {
        return l.sourceKind == static_cast<int32_t>(CvSourceKind::Modulator) && l.sourceModId == modId;
    };
    for (auto& l : old->cvLinks) if (fedBy(l)) restoreLinkTarget(trackId, l);
    nt->cvLinks.erase(std::remove_if(nt->cvLinks.begin(), nt->cvLinks.end(), fedBy), nt->cvLinks.end());
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::modulatorCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->modulators.size()) : 0;
}

int32_t Engine::modulatorIdAt(int32_t trackId, int32_t index) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->modulators.size())) return -1;
    return t->modulators[index].id;
}

int32_t Engine::modulatorKind(int32_t trackId, int32_t modId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return -1;
    for (auto& m : t->modulators) if (m.id == modId) return static_cast<int32_t>(m.kind);
    return -1;
}

float Engine::modulatorGet(int32_t trackId, int32_t modId, int32_t field) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    for (auto& m : t->modulators) if (m.id == modId) return m.get(static_cast<ModField>(field));
    return 0.0f;
}

void Engine::modulatorSet(int32_t trackId, int32_t modId, int32_t field, float value) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    if (auto* m = modulatorPtr(*t, modId)) m->set(static_cast<ModField>(field), value);
}

float Engine::modulatorValue(int32_t trackId, int32_t modId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    const double spb = transport_.samplesPerBeat();
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    const double samples = static_cast<double>(transport_.playheadSamples());
    const double beat = spb > 0 ? samples / spb : 0.0;
    for (auto& m : t->modulators) if (m.id == modId) return m.eval(beat, samples / sr);
    return 0.0f;
}

// ---- CV link CRUD ----------------------------------------------------------

// ---- modulation target read/write (device / instrument / MIDI-FX) ----------

// True if (kind, dev, param) names a live target on track t.
static bool targetValid(Track& t, int32_t kind, int32_t dev, int32_t param) {
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument))
        return t.instrument && param >= 0 && param < t.instrument->pluginParamCount();
    if (kind == static_cast<int32_t>(CvTargetKind::MidiFx))
        return dev >= 0 && dev < static_cast<int32_t>(t.midiEffects.size()) && t.midiEffects[dev]
               && param >= 0 && param < t.midiEffects[dev]->paramCount();
    return dev >= 0 && dev < static_cast<int32_t>(t.devices.size()) && t.devices[dev]
           && param >= 0 && param < t.devices[dev]->paramCount();
}
// Current value + range of a target (instrument params are normalized 0..1).
static float targetGet(Track& t, int32_t kind, int32_t dev, int32_t param, float& mn, float& mx) {
    mn = 0.0f; mx = 1.0f;
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument))
        return t.instrument ? t.instrument->pluginParamGet(param) : 0.0f;
    if (kind == static_cast<int32_t>(CvTargetKind::MidiFx)) {
        auto* m = t.midiEffects[dev].get();
        mn = m->paramMin(param); mx = m->paramMax(param); return m->getParam(param);
    }
    auto* d = t.devices[dev].get();
    mn = d->paramMin(param); mx = d->paramMax(param); return d->getParam(param);
}
static void targetSet(Track& t, int32_t kind, int32_t dev, int32_t param, float v) {
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument)) { if (t.instrument) t.instrument->pluginParamSet(param, v); return; }
    if (kind == static_cast<int32_t>(CvTargetKind::MidiFx)) {
        if (dev >= 0 && dev < static_cast<int32_t>(t.midiEffects.size())) if (auto* m = t.midiEffects[dev].get()) m->setParam(param, v);
        return;
    }
    if (dev >= 0 && dev < static_cast<int32_t>(t.devices.size())) if (auto* d = t.devices[dev].get()) d->setParam(param, v);
}

// Generalized link creation. Source: modulator or param. Target: device / instrument / MIDI-FX.
int32_t Engine::addCvLinkFull(int32_t trackId, int32_t sourceKind, int32_t modId, int32_t srcDev, int32_t srcParam,
                              int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    if (sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
        if (srcDev < 0 || srcDev >= static_cast<int32_t>(old->devices.size())) return -1;
    } else if (modulatorKind(trackId, modId) < 0) return -1;
    const int32_t tgt = targetTrack < 0 ? trackId : targetTrack;
    auto tgtTrack = findTrackAuthoring(tgt);
    if (!tgtTrack || !targetValid(*tgtTrack, targetKind, targetDevice, targetParam)) return -1;
    const int32_t storedTarget = tgt == trackId ? -1 : tgt;
    for (auto& l : old->cvLinks)
        if (l.sourceKind == sourceKind && l.sourceModId == modId && l.sourceDevice == srcDev && l.sourceParam == srcParam
            && l.targetKind == targetKind && l.targetTrack == storedTarget && l.targetDevice == targetDevice && l.targetParam == targetParam)
            return -1;
    auto nt = cloneTrack(*old);
    CvLink l;
    l.sourceKind = sourceKind; l.sourceModId = modId; l.sourceDevice = srcDev; l.sourceParam = srcParam;
    l.targetKind = targetKind; l.targetTrack = storedTarget; l.targetDevice = targetDevice; l.targetParam = targetParam;
    float mn, mx; l.base.store(targetGet(*tgtTrack, targetKind, targetDevice, targetParam, mn, mx), std::memory_order_relaxed);
    nt->cvLinks.push_back(l);
    const int32_t idx = static_cast<int32_t>(nt->cvLinks.size()) - 1;
    republishWithTrack(trackId, nt);
    return idx;
}

int32_t Engine::addCvLink(int32_t trackId, int32_t modId, int32_t device, int32_t param) {
    return addCvLinkFull(trackId, 0, modId, -1, -1, 0, -1, device, param);
}
int32_t Engine::addCvLinkTo(int32_t trackId, int32_t modId, int32_t targetTrack, int32_t device, int32_t param) {
    return addCvLinkFull(trackId, 0, modId, -1, -1, 0, targetTrack, device, param);
}
int32_t Engine::addCvLinkToTarget(int32_t trackId, int32_t modId, int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam) {
    return addCvLinkFull(trackId, 0, modId, -1, -1, targetKind, targetTrack, targetDevice, targetParam);
}

int32_t Engine::addCvLinkFromParam(int32_t trackId, int32_t srcDevice, int32_t srcParam,
                                   int32_t targetTrack, int32_t targetDevice, int32_t targetParam) {
    return addCvLinkFull(trackId, 1, 0, srcDevice, srcParam, 0, targetTrack, targetDevice, targetParam);
}
int32_t Engine::addCvLinkFromParamToTarget(int32_t trackId, int32_t srcDevice, int32_t srcParam,
                                           int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam) {
    return addCvLinkFull(trackId, 1, 0, srcDevice, srcParam, targetKind, targetTrack, targetDevice, targetParam);
}

// Return a link's target param to its base (so it doesn't stick at the last modulated
// value once the link is gone). Targets are shared across the clone, so this is live.
void Engine::restoreLinkTarget(int32_t ownerTrack, const CvLink& l) {
    const int32_t tgt = l.targetTrack < 0 ? ownerTrack : l.targetTrack;
    if (auto tt = findTrackAuthoring(tgt))
        if (targetValid(*tt, l.targetKind, l.targetDevice, l.targetParam))
            targetSet(*tt, l.targetKind, l.targetDevice, l.targetParam, l.base.load(std::memory_order_relaxed));
}

bool Engine::removeCvLink(int32_t trackId, int32_t index) {
    auto old = findTrackAuthoring(trackId);
    if (!old || index < 0 || index >= static_cast<int32_t>(old->cvLinks.size())) return false;
    restoreLinkTarget(trackId, old->cvLinks[index]);
    auto nt = cloneTrack(*old);
    nt->cvLinks.erase(nt->cvLinks.begin() + index);
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::cvLinkCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->cvLinks.size()) : 0;
}

static const CvLink* linkAt(const std::shared_ptr<Track>& t, int32_t index) {
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return nullptr;
    return &t->cvLinks[index];
}

int32_t Engine::cvLinkSource(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->sourceModId : -1;
}
int32_t Engine::cvLinkSourceKind(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->sourceKind : 0;
}
int32_t Engine::cvLinkSourceDevice(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->sourceDevice : -1;
}
int32_t Engine::cvLinkSourceParam(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->sourceParam : -1;
}
int32_t Engine::cvLinkTargetKind(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->targetKind : 0;
}
int32_t Engine::cvLinkTargetTrack(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->targetTrack : -1;
}
int32_t Engine::cvLinkDevice(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->targetDevice : -1;
}
int32_t Engine::cvLinkParam(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index); return l ? l->targetParam : -1;
}
float Engine::cvLinkDepth(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index);
    return l ? l->depth.load(std::memory_order_relaxed) : 0.0f;
}
int32_t Engine::cvLinkMode(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index);
    return l ? l->mode.load(std::memory_order_relaxed) : 0;
}
void Engine::setCvLinkDepth(int32_t trackId, int32_t index, float depth) {
    auto t = findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return;
    t->cvLinks[index].depth.store(std::clamp(depth, -1.0f, 1.0f), std::memory_order_relaxed);
}
void Engine::setCvLinkMode(int32_t trackId, int32_t index, int32_t mode) {
    auto t = findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return;
    t->cvLinks[index].mode.store(mode, std::memory_order_relaxed);
}
float Engine::cvLinkBase(int32_t trackId, int32_t index) const {
    auto* l = linkAt(findTrackAuthoring(trackId), index);
    return l ? l->base.load(std::memory_order_relaxed) : 0.0f;
}
void Engine::setCvLinkBase(int32_t trackId, int32_t index, float base) {
    auto t = findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return;
    t->cvLinks[index].base.store(base, std::memory_order_relaxed);
}

bool Engine::deviceParamModulated(int32_t trackId, int32_t device, int32_t param) const {
    return paramModulated(0, trackId, device, param);
}
bool Engine::paramModulated(int32_t targetKind, int32_t trackId, int32_t device, int32_t param) const {
    if (!authoring_) return false;
    // Modulated if any track's link targets (kind, trackId, device, param) — including
    // cross-track links owned by another track.
    for (auto& op : authoring_->tracks) {
        const int32_t owner = op->id();
        for (auto& l : op->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? owner : l.targetTrack;
            if (tgt == trackId && l.targetKind == targetKind && l.targetDevice == device && l.targetParam == param) return true;
        }
    }
    return false;
}

// A UI edit of a modulated target sets the link's base (the atomic is driven by the
// modulation each block). Called from the param setters; returns nothing.
void Engine::routeCvBaseEdit(int32_t targetKind, int32_t trackId, int32_t device, int32_t param, float value) {
    if (!authoring_) return;
    for (auto& op : authoring_->tracks) {
        const int32_t owner = op->id();
        for (auto& l : op->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? owner : l.targetTrack;
            if (tgt == trackId && l.targetKind == targetKind && l.targetDevice == device && l.targetParam == param)
                l.base.store(value, std::memory_order_relaxed);
        }
    }
}

// ---- device edit → CV-link index fix-up (message thread) -------------------

// New index of `idx` after the device at `from` is moved to `to` (vector erase+insert).
static int32_t remappedMoveIndex(int32_t idx, int32_t from, int32_t to) {
    if (idx == from) return to;
    if (from < to) { if (idx > from && idx <= to) return idx - 1; }
    else           { if (idx >= to && idx < from) return idx + 1; }
    return idx;
}

void Engine::remapCvLinksAfterDeviceChange(int32_t track, int32_t removedIndex, int32_t from, int32_t to) {
    if (!authoring_) return;
    bool anyChange = false;
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& ot : authoring_->tracks) {
        const bool ownerIsEdited = ot->id() == track;
        bool needs = false;
        for (auto& l : ot->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
            const bool paramSrcHere = ownerIsEdited && l.sourceKind == static_cast<int32_t>(CvSourceKind::Param);
            if (tgt == track || paramSrcHere) { needs = true; break; }
        }
        if (!needs) { g->tracks.push_back(ot); continue; }
        auto nt = cloneTrack(*ot);
        std::vector<CvLink> kept;
        kept.reserve(nt->cvLinks.size());
        for (auto& l : nt->cvLinks) {
            bool drop = false;
            // Target index fix-up (device-target links whose target is the edited track).
            const int32_t tgt = l.targetTrack < 0 ? nt->id() : l.targetTrack;
            if (tgt == track && l.targetKind == static_cast<int32_t>(CvTargetKind::Device)) {
                if (removedIndex >= 0) {
                    if (l.targetDevice == removedIndex) drop = true;
                    else if (l.targetDevice > removedIndex) l.targetDevice -= 1;
                } else l.targetDevice = remappedMoveIndex(l.targetDevice, from, to);
            }
            // Source index fix-up (param source lives on the owner = edited track).
            if (!drop && ownerIsEdited && l.sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
                if (removedIndex >= 0) {
                    if (l.sourceDevice == removedIndex) drop = true;
                    else if (l.sourceDevice > removedIndex) l.sourceDevice -= 1;
                } else l.sourceDevice = remappedMoveIndex(l.sourceDevice, from, to);
            }
            if (!drop) kept.push_back(l);
        }
        nt->cvLinks = std::move(kept);
        anyChange = true;
        g->tracks.push_back(nt);
    }
    if (anyChange) publishRaw(std::move(g));   // no extra undo step: part of the device edit
}

// ---- block-rate apply / restore (audio thread, called from mixGraph) -------

void Engine::applyModulation(Graph* g, double beat, double timeSec) {
    for (auto& tptr : g->tracks) {
        Track& t = *tptr;
        if (t.cvLinks.empty()) continue;
        for (auto& link : t.cvLinks) {
            // Source value `mv`: modulator output, or a source param's normalized value.
            float mv;
            if (link.sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
                const int32_t sd = link.sourceDevice, sp = link.sourceParam;
                if (sd < 0 || sd >= static_cast<int32_t>(t.devices.size())) continue;
                Device* sdev = t.devices[sd].get();
                if (!sdev || sp < 0 || sp >= sdev->paramCount()) continue;
                const float smn = sdev->paramMin(sp), smx = sdev->paramMax(sp), sspan = smx - smn;
                mv = sspan > 1e-9f ? (sdev->getParam(sp) - smn) / sspan : 0.0f;   // 0..1
            } else {
                const Modulator* src = nullptr;
                for (auto& m : t.modulators) if (m.id == link.sourceModId) { src = &m; break; }
                if (!src) continue;
                mv = src->eval(beat, timeSec);                // [-1,1] * depth
            }
            // Target track: this track for a self-link, else find it in the graph.
            Track* tgt = &t;
            if (link.targetTrack >= 0) {
                tgt = nullptr;
                for (auto& op : g->tracks) if (op->id() == link.targetTrack) { tgt = op.get(); break; }
                if (!tgt) continue;
            }
            const int32_t tk = link.targetKind, di = link.targetDevice, pi = link.targetParam;
            if (!targetValid(*tgt, tk, di, pi)) continue;

            // Swing around the stored base (the target atomic holds the live modulated
            // value continuously — no per-block restore — so the UI shows it smoothly).
            const float base = link.base.load(std::memory_order_relaxed);
            float mn, mx; targetGet(*tgt, tk, di, pi, mn, mx);
            const float span = mx - mn;
            const float ld = link.depth.load(std::memory_order_relaxed);
            float v;
            switch (link.mode.load(std::memory_order_relaxed)) {
                case 1: v = base * (1.0f + ld * mv); break;                                     // Multiply
                case 2: { const float target = mn + (mv * 0.5f + 0.5f) * span;                 // Override
                          v = base + ld * (target - base); break; }
                default: v = base + ld * mv * span; break;                                      // Add
            }
            targetSet(*tgt, tk, di, pi, std::clamp(v, mn, mx));
        }
    }
}

void Engine::updateModulators(Graph* g, double beat, double timeSec, double dt) {
    for (auto& tptr : g->tracks) {
        Track& t = *tptr;
        // Stateful sources first (env / MIDI / ADSR smooth into envValue).
        for (auto& m : t.modulators) {
            if (m.kind == ModulatorKind::EnvFollower) {
                m.updateEnv(0.5f * (t.meterRmsL() + t.meterRmsR()), dt);   // own post-fader level (last block)
            } else if (m.kind == ModulatorKind::MidiToCv) {
                const int32_t sel = m.waveform.load(std::memory_order_relaxed);  // 0 vel, 1 gate, 2 note
                const float target = sel == 1 ? (t.midiGate() ? 1.0f : 0.0f)
                                   : sel == 2 ? t.midiNoteNorm()
                                              : t.midiVelocity();
                m.updateEnv(target, dt);
            } else if (m.kind == ModulatorKind::Adsr) {
                m.updateAdsr(t.midiGate(), dt);   // gated by held MIDI notes
            }
        }
        // Math / Scope read other modulators' current outputs (forward refs one block late).
        for (auto& m : t.modulators) {
            if (m.kind == ModulatorKind::Math) {
                const float a = modOutput(t, m.inputA.load(std::memory_order_relaxed), beat, timeSec);
                const float b = modOutput(t, m.inputB.load(std::memory_order_relaxed), beat, timeSec);
                m.outValue.store(m.mathEval(a, b), std::memory_order_relaxed);
            } else if (m.kind == ModulatorKind::Scope) {
                const float v = modOutput(t, m.inputA.load(std::memory_order_relaxed), beat, timeSec);
                m.outValue.store(v, std::memory_order_relaxed);   // pass-through
                m.pushScope(v);
            }
        }
    }
}

int32_t Engine::modulatorScope(int32_t trackId, int32_t modId, float* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    for (auto& m : t->modulators) if (m.id == modId) return m.readScope(out, cap);
    return 0;
}

// A modulator's current output value by id (0 if not found), for Math inputs.
float Engine::modOutput(Track& t, int32_t modId, double beat, double timeSec) {
    if (modId < 0) return 0.0f;
    for (auto& m : t.modulators) if (m.id == modId) return m.eval(beat, timeSec);
    return 0.0f;
}

// ---- self-test -------------------------------------------------------------

bool Engine::modulationSelfTest() {
    auto tid = addInstrumentTrack();
    const int32_t dev = addTrackBuiltinDevice(tid, 3);   // Delay (has params)
    if (dev < 0) return false;
    const int32_t mod = addModulator(tid, 0);            // LFO
    if (mod < 0) return false;
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::Waveform), 2);   // saw (monotonic in phase)
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::TempoSync), 1);
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::RateSyncBeats), 1.0f);
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::Depth), 1.0f);
    const int32_t link = addCvLink(tid, mod, dev, 0);
    if (link < 0) return false;
    if (!deviceParamModulated(tid, dev, 0)) return false;

    // Two different beats produce two different modulated values; the stored base is
    // untouched by apply (the atomic holds the live modulated value, no restore).
    Device* d = deviceAt(tid, dev);
    const float base = cvLinkBase(tid, link);
    applyModulation(authoring_.get(), 0.10, 0.10);   // saw near trough
    const float v0 = d->getParam(0);
    applyModulation(authoring_.get(), 0.90, 0.90);   // saw near crest
    const float v1 = d->getParam(0);
    const bool moved = std::fabs(v1 - v0) > 1e-4f && std::fabs(cvLinkBase(tid, link) - base) < 1e-5f;

    // Cross-track: tid's LFO modulates a device param on a second track.
    auto tid2 = addInstrumentTrack();
    const int32_t dev2 = addTrackBuiltinDevice(tid2, 3);
    const int32_t xlink = addCvLinkTo(tid, mod, tid2, dev2, 0);
    const bool xTargetsOther = xlink >= 0 && cvLinkTargetTrack(tid, xlink) == tid2
                               && deviceParamModulated(tid2, dev2, 0);
    Device* d2 = deviceAt(tid2, dev2);
    const float base2 = cvLinkBase(tid, xlink);
    applyModulation(authoring_.get(), 0.10, 0.10);   // saw near trough → moves down from base
    const bool xMoved = std::fabs(d2->getParam(0) - base2) > 1e-4f;
    // Removing the cross-track link restores the target param to its base.
    removeCvLink(tid, xlink);
    const bool restored = std::fabs(deviceAt(tid2, dev2)->getParam(0) - base2) < 1e-4f;
    addCvLinkTo(tid, mod, tid2, dev2, 0);            // re-add for the remap checks below

    // Remap: reordering the target device follows the link; removing it drops the link.
    addTrackBuiltinDevice(tid2, 4);          // Utility at index 1
    moveDevice(tid2, dev2, 1);               // move the modulated device 0 -> 1
    const bool followed = cvLinkTargetTrack(tid, xlink) == tid2 && cvLinkDevice(tid, xlink) == 1;
    removeDevice(tid2, 1);                    // remove the (now index-1) modulated device
    const bool dropped = cvLinkCount(tid) == 1;   // cross-track link gone; self link remains

    // Envelope follower: kind 1, and driving it with a level raises its output.
    const int32_t env = addModulator(tid, 1);
    modulatorSet(tid, env, static_cast<int32_t>(ModField::Attack), 1.0f);   // fast attack
    bool envOk = modulatorKind(tid, env) == 1;
    if (auto et = findTrackAuthoring(tid))
        if (auto* ep = modulatorPtr(*et, env))
            for (int i = 0; i < 100; ++i) ep->updateEnv(0.8f, 0.01);        // ~1s of high level
    envOk = envOk && modulatorValue(tid, env) > 0.3f;

    // ADSR: gate on → rises to sustain; gate off → releases to 0.
    const int32_t adsr = addModulator(tid, 3);
    modulatorSet(tid, adsr, static_cast<int32_t>(ModField::Attack), 5.0f);
    modulatorSet(tid, adsr, static_cast<int32_t>(ModField::Decay), 5.0f);
    modulatorSet(tid, adsr, static_cast<int32_t>(ModField::Sustain), 0.5f);
    bool adsrOk = modulatorKind(tid, adsr) == 3;
    if (auto at = findTrackAuthoring(tid))
        if (auto* ap = modulatorPtr(*at, adsr)) {
            for (int i = 0; i < 100; ++i) ap->updateAdsr(true, 0.01);   // attack → decay → sustain
            const float held = modulatorValue(tid, adsr);
            for (int i = 0; i < 300; ++i) ap->updateAdsr(false, 0.01);  // release → 0
            adsrOk = adsrOk && held > 0.3f && modulatorValue(tid, adsr) < 0.05f;
        }
    envOk = envOk && adsrOk;

    // Math: two macros (0.3, 0.4) added → 0.7.
    const int32_t ma = addModulator(tid, 4); modulatorSet(tid, ma, static_cast<int32_t>(ModField::Depth), 0.3f);
    const int32_t mb = addModulator(tid, 4); modulatorSet(tid, mb, static_cast<int32_t>(ModField::Depth), 0.4f);
    const int32_t mth = addModulator(tid, 5);
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::Waveform), 0);          // op Add
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::InputA), (float)ma);
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::InputB), (float)mb);
    updateModulators(authoring_.get(), 0.0, 0.0, 0.01);
    const bool mathOk = std::fabs(modulatorValue(tid, mth) - 0.7f) < 1e-3f;
    envOk = envOk && mathOk;

    // Removing the modulator must drop all its remaining links.
    removeModulator(tid, mod);
    const bool cleaned = cvLinkCount(tid) == 0;

    // Param → param source: dev's param 0 drives its own param 1 (no modulator).
    const int32_t psl = addCvLinkFromParam(tid, dev, 0, -1, dev, 1);
    const bool psOk = psl >= 0 && cvLinkSourceKind(tid, psl) == 1
                      && cvLinkSourceDevice(tid, psl) == dev && cvLinkSourceParam(tid, psl) == 0;

    removeTrack(tid);
    removeTrack(tid2);
    return moved && restored && cleaned && xTargetsOther && xMoved && followed && dropped && envOk && psOk;
}

} // namespace nota
