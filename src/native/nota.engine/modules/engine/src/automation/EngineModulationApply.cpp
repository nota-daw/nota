// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationOps — block-rate CV modulation apply/restore and modulation self-test.

#include "automation/AutomationOps.h"
#include "automation/Modulation.h"
#include "automation/ModulationInternal.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
#include "model/Graph.h"
#include "model/ParamResolve.h"
#include "render/RenderRoutingPlan.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota::detail {

// ---- block-rate apply / restore (audio thread, called from mixGraph) -------

namespace {

bool readCompiledSourceParam(const RenderRoutingPlan::CompiledCvLinkEntry& e, float& outMv) {
    const auto& src = e.sourceParam;
    Device* sdev = src.device;
    float mn = src.minVal;
    float span = src.spanVal;
    if (src.nested) {
        if (!src.rack || src.rack->liveGeneration() != src.expectedRackGen) {
            if (!e.ownerTrack || src.paramIndex < 0) return false;
            sdev = resolveNested(*e.ownerTrack, src.host, src.chainUid, src.slot).device;
            if (!sdev || src.paramIndex >= sdev->paramCount()) return false;
            mn = sdev->paramMin(src.paramIndex);
            span = sdev->paramMax(src.paramIndex) - mn;
        }
    }
    if (!sdev || src.paramIndex < 0 || src.paramIndex >= sdev->paramCount()) return false;
    outMv = span > 1e-9f ? (sdev->getParam(src.paramIndex) - mn) / span : 0.0f;
    return true;
}

float combineCvValue(const CvLink& link, float base, float mv, float mn, float span) {
    const float ld = link.depth.load(std::memory_order_relaxed);
    float v;
    switch (link.mode.load(std::memory_order_relaxed)) {
    case 1: v = base * (1.0f + ld * mv); break;
    case 2: {
        const float target = mn + (mv * 0.5f + 0.5f) * span;
        v = base + ld * (target - base);
        break;
    }
    default: v = base + ld * mv * span; break;
    }
    return std::clamp(v, mn, mn + span);
}

} // namespace

void AutomationOps::applyModulation(Graph* g, double beat, double timeSec) {
    if (const RenderRoutingPlan* plan = g ? g->routingPlan.get() : nullptr) {
        for (const auto& e : plan->cvLinks()) {
            if (!e.ownerTrack || !e.targetTrack || e.linkIndex < 0 ||
                static_cast<size_t>(e.linkIndex) >= e.ownerTrack->cvLinks.size())
                continue;
            const CvLink& link = e.ownerTrack->cvLinks[static_cast<size_t>(e.linkIndex)];
            float mv = 0.0f;
            if (e.sourceIsParam) {
                if (!readCompiledSourceParam(e, mv)) continue;
            } else {
                if (!e.sourceMod) continue;
                mv = e.sourceMod->eval(beat, timeSec);
            }

            const float base = link.base.load(std::memory_order_relaxed);
            const auto& dst = e.destParam;
            using Kind = RenderRoutingPlan::CompiledParamEndpoint::Kind;
            const bool nestedFresh = !dst.nested || (dst.rack && dst.rack->liveGeneration() == dst.expectedRackGen);
            if (nestedFresh) {
                if (dst.kind == Kind::Invalid) continue;
                const float clamped = combineCvValue(link, base, mv, dst.minVal, dst.spanVal);
                switch (dst.kind) {
                case Kind::InstrumentPlugin:
                case Kind::NestedRackInstrumentPlugin:
                    if (dst.instrument) dst.instrument->pluginParamSet(dst.paramIndex, clamped);
                    break;
                case Kind::MidiDeviceGeneric:
                case Kind::NestedRackMidiGeneric:
                    if (dst.midi) dst.midi->setParam(dst.paramIndex, clamped);
                    break;
                case Kind::DeviceGeneric:
                case Kind::NestedRackDeviceGeneric:
                    if (dst.device) dst.device->setLiveParam(dst.paramIndex, clamped);
                    break;
                default: break;
                }
                continue;
            }

            if (!targetValid(
                    *e.targetTrack, e.targetKind, e.targetDevice, e.targetParam, e.targetChainUid, e.targetSlot))
                continue;
            float mn, mx;
            targetGet(
                *e.targetTrack, e.targetKind, e.targetDevice, e.targetParam, mn, mx, e.targetChainUid, e.targetSlot);
            const float clamped = combineCvValue(link, base, mv, mn, mx - mn);
            targetSet(
                *e.targetTrack, e.targetKind, e.targetDevice, e.targetParam, clamped, e.targetChainUid, e.targetSlot);
        }
        return;
    }

    for (auto& tptr : g->tracks) {
        Track& t = *tptr;
        if (t.cvLinks.empty()) continue;
        for (auto& link : t.cvLinks) {
            // Source value `mv`: modulator output, or a source param's normalized value.
            float mv;
            if (link.sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
                const int32_t sd = link.sourceDevice, sp = link.sourceParam;
                Device* sdev =
                    link.sourceChainUid != 0 ? resolveNested(t, sd, link.sourceChainUid, link.sourceSlot).device
                    : sd >= 0 && sd < static_cast<int32_t>(t.devices.size()) ? t.devices[static_cast<size_t>(sd)].get()
                                                                             : nullptr;
                if (!sdev || sp < 0 || sp >= sdev->paramCount()) continue;
                const float smn = sdev->paramMin(sp), smx = sdev->paramMax(sp), sspan = smx - smn;
                mv = sspan > 1e-9f ? (sdev->getParam(sp) - smn) / sspan : 0.0f; // 0..1
            } else {
                const Modulator* src = nullptr;
                for (auto& m : t.modulators)
                    if (m.id == link.sourceModId) {
                        src = &m;
                        break;
                    }
                if (!src) continue;
                mv = src->eval(beat, timeSec); // [-1,1] * depth
            }
            // Target track: this track for a self-link, else find it in the graph.
            Track* tgt = &t;
            if (link.targetTrack >= 0) {
                tgt = nullptr;
                for (auto& op : g->tracks)
                    if (op->id() == link.targetTrack) {
                        tgt = op.get();
                        break;
                    }
                if (!tgt) continue;
            }
            const int32_t tk = link.targetKind, di = link.targetDevice, pi = link.targetParam;
            const uint64_t tu = link.targetChainUid;
            const int32_t ts = link.targetSlot;
            if (!targetValid(*tgt, tk, di, pi, tu, ts)) continue;

            // Swing around the stored base (the target atomic holds the live modulated
            // value continuously — no per-block restore — so the UI shows it smoothly).
            const float base = link.base.load(std::memory_order_relaxed);
            float mn, mx;
            targetGet(*tgt, tk, di, pi, mn, mx, tu, ts);
            const float span = mx - mn;
            const float ld = link.depth.load(std::memory_order_relaxed);
            float v;
            switch (link.mode.load(std::memory_order_relaxed)) {
            case 1: v = base * (1.0f + ld * mv); break; // Multiply
            case 2: {
                const float target = mn + (mv * 0.5f + 0.5f) * span; // Override
                v = base + ld * (target - base);
                break;
            }
            default: v = base + ld * mv * span; break; // Add
            }
            targetSet(*tgt, tk, di, pi, std::clamp(v, mn, mx), tu, ts);
        }
    }
}

void AutomationOps::updateModulators(Graph* g, double beat, double timeSec, double dt) {
    auto updateTrack = [&](Track& t) {
        // Stateful sources first (env / MIDI / ADSR smooth into envValue).
        for (auto& m : t.modulators) {
            if (m.kind == ModulatorKind::EnvFollower) {
                m.updateEnv(0.5f * (t.meterRmsL() + t.meterRmsR()), dt); // own post-fader level (last block)
            } else if (m.kind == ModulatorKind::MidiToCv) {
                const int32_t sel = m.waveform.load(std::memory_order_relaxed); // 0 vel, 1 gate, 2 note
                const float target = sel == 1   ? (t.midiGate() ? 1.0f : 0.0f)
                                     : sel == 2 ? t.midiNoteNorm()
                                                : t.midiVelocity();
                m.updateEnv(target, dt);
            } else if (m.kind == ModulatorKind::Adsr) {
                m.updateAdsr(t.midiGate(), dt); // gated by held MIDI notes
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
                m.outValue.store(v, std::memory_order_relaxed); // pass-through
                m.pushScope(v, static_cast<float>(dt));
            }
        }
    };
    if (const RenderRoutingPlan* plan = g ? g->routingPlan.get() : nullptr) {
        for (int32_t idx : plan->modulatorTrackIndices())
            if (idx >= 0 && static_cast<size_t>(idx) < g->tracks.size() && g->tracks[static_cast<size_t>(idx)])
                updateTrack(*g->tracks[static_cast<size_t>(idx)]);
        return;
    }
    for (auto& tptr : g->tracks) updateTrack(*tptr);
}

int32_t AutomationOps::modulatorScope(int32_t trackId, int32_t modId, float* values, float* dts, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0;
    for (auto& m : t->modulators)
        if (m.id == modId) return m.readScope(values, dts, cap);
    return 0;
}

// A modulator's current output value by id (0 if not found), for Math inputs.
float AutomationOps::modOutput(Track& t, int32_t modId, double beat, double timeSec) {
    if (modId < 0) return 0.0f;
    for (auto& m : t.modulators)
        if (m.id == modId) return m.eval(beat, timeSec);
    return 0.0f;
}

// ---- self-test -------------------------------------------------------------

bool AutomationOps::modulationSelfTest() {
    auto tid = rt_.devices().addInstrumentTrack();
    const int32_t dev = rt_.devices().addTrackBuiltinDevice(tid, 3); // Delay (has params)
    if (dev < 0) return false;
    const int32_t mod = addModulator(tid, 0); // LFO
    if (mod < 0) return false;
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::Waveform), 2); // saw (monotonic in phase)
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::TempoSync), 1);
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::RateSyncBeats), 1.0f);
    modulatorSet(tid, mod, static_cast<int32_t>(ModField::Depth), 1.0f);
    const int32_t link = addCvLink(tid, mod, dev, 0);
    if (link < 0) return false;
    if (!deviceParamModulated(tid, dev, 0)) return false;

    // Two different beats produce two different modulated values; the stored base is
    // untouched by apply (the atomic holds the live modulated value, no restore).
    Device* d = rt_.devices().deviceAt(tid, dev);
    const float base = cvLinkBase(tid, link);
    applyModulation(rt_.authoring_.get(), 0.10, 0.10); // saw near trough
    const float v0 = d->getParam(0);
    applyModulation(rt_.authoring_.get(), 0.90, 0.90); // saw near crest
    const float v1 = d->getParam(0);
    const bool moved = std::fabs(v1 - v0) > 1e-4f && std::fabs(cvLinkBase(tid, link) - base) < 1e-5f;

    // Cross-track: tid's LFO modulates a device param on a second track.
    auto tid2 = rt_.devices().addInstrumentTrack();
    const int32_t dev2 = rt_.devices().addTrackBuiltinDevice(tid2, 3);
    const int32_t xlink = addCvLinkTo(tid, mod, tid2, dev2, 0);
    const bool xTargetsOther =
        xlink >= 0 && cvLinkTargetTrack(tid, xlink) == tid2 && deviceParamModulated(tid2, dev2, 0);
    Device* d2 = rt_.devices().deviceAt(tid2, dev2);
    const float base2 = cvLinkBase(tid, xlink);
    applyModulation(rt_.authoring_.get(), 0.10, 0.10); // saw near trough → moves down from base
    const bool xMoved = std::fabs(d2->getParam(0) - base2) > 1e-4f;
    // Removing the cross-track link restores the target param to its base.
    removeCvLink(tid, xlink);
    const bool restored = std::fabs(rt_.devices().deviceAt(tid2, dev2)->getParam(0) - base2) < 1e-4f;
    addCvLinkTo(tid, mod, tid2, dev2, 0); // re-add for the remap checks below

    // Remap: reordering the target device follows the link; removing it drops the link.
    rt_.devices().addTrackBuiltinDevice(tid2, 4); // Utility at index 1
    rt_.devices().moveDevice(tid2, dev2, 1);      // move the modulated device 0 -> 1
    const bool followed = cvLinkTargetTrack(tid, xlink) == tid2 && cvLinkDevice(tid, xlink) == 1;
    rt_.devices().removeDevice(tid2, 1);        // remove the (now index-1) modulated device
    const bool dropped = cvLinkCount(tid) == 1; // cross-track link gone; self link remains

    // Envelope follower: kind 1, and driving it with a level raises its output.
    const int32_t env = addModulator(tid, 1);
    modulatorSet(tid, env, static_cast<int32_t>(ModField::Attack), 1.0f); // fast attack
    bool envOk = modulatorKind(tid, env) == 1;
    if (auto et = rt_.findTrackAuthoring(tid))
        if (auto* ep = modulatorPtr(*et, env))
            for (int i = 0; i < 100; ++i) ep->updateEnv(0.8f, 0.01); // ~1s of high level
    envOk = envOk && modulatorValue(tid, env) > 0.3f;

    // ADSR: gate on → rises to sustain; gate off → releases to 0.
    const int32_t adsr = addModulator(tid, 3);
    modulatorSet(tid, adsr, static_cast<int32_t>(ModField::Attack), 5.0f);
    modulatorSet(tid, adsr, static_cast<int32_t>(ModField::Decay), 5.0f);
    modulatorSet(tid, adsr, static_cast<int32_t>(ModField::Sustain), 0.5f);
    bool adsrOk = modulatorKind(tid, adsr) == 3;
    if (auto at = rt_.findTrackAuthoring(tid))
        if (auto* ap = modulatorPtr(*at, adsr)) {
            for (int i = 0; i < 100; ++i) ap->updateAdsr(true, 0.01); // attack → decay → sustain
            const float held = modulatorValue(tid, adsr);
            for (int i = 0; i < 300; ++i) ap->updateAdsr(false, 0.01); // release → 0
            adsrOk = adsrOk && held > 0.3f && modulatorValue(tid, adsr) < 0.05f;
        }
    envOk = envOk && adsrOk;

    // Math: two macros (0.3, 0.4) added → 0.7.
    const int32_t ma = addModulator(tid, 4);
    modulatorSet(tid, ma, static_cast<int32_t>(ModField::Depth), 0.3f);
    const int32_t mb = addModulator(tid, 4);
    modulatorSet(tid, mb, static_cast<int32_t>(ModField::Depth), 0.4f);
    const int32_t mth = addModulator(tid, 5);
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::Waveform), 0); // op Add
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::InputA), static_cast<float>(ma));
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::InputB), static_cast<float>(mb));
    updateModulators(rt_.authoring_.get(), 0.0, 0.0, 0.01);
    bool mathOk = std::fabs(modulatorValue(tid, mth) - 0.7f) < 1e-3f;
    // Scale with a gain above 1 (the Math gain runs 0..2, #92): 0.3 × 1.5 = 0.45.
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::Waveform), 4); // op Scale
    modulatorSet(tid, mth, static_cast<int32_t>(ModField::Depth), 1.5f);
    updateModulators(rt_.authoring_.get(), 0.0, 0.0, 0.01);
    mathOk = mathOk && std::fabs(modulatorValue(tid, mth) - 0.45f) < 1e-3f;
    envOk = envOk && mathOk;

    // Removing the modulator must drop all its remaining links.
    removeModulator(tid, mod);
    const bool cleaned = cvLinkCount(tid) == 0;

    // Param → param source: dev's param 0 drives its own param 1 (no modulator).
    const int32_t psl = addCvLinkFromParam(tid, dev, 0, -1, dev, 1);
    const bool psOk = psl >= 0 && cvLinkSourceKind(tid, psl) == 1 && cvLinkSourceDevice(tid, psl) == dev &&
                      cvLinkSourceParam(tid, psl) == 0;

    rt_.tracks().removeTrack(tid);
    rt_.tracks().removeTrack(tid2);
    return moved && restored && cleaned && xTargetsOther && xMoved && followed && dropped && envOk && psOk;
}

} // namespace nota::detail
