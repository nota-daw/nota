// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Twin.h"
#include "TwinMod.h"
#include "effects/chorus/Chorus.h"
#include "effects/delay/Delay.h"
#include "effects/flanger/Flanger.h"
#include "effects/phaser/Phaser.h"
#include "effects/reverb/Reverb.h"
#include "common/DeviceMath.h"
#include "nota/dsp/poly/PolyVoiceScheduler.h"
#include "nota/dsp/rt/RenderExecutor.h"
#include "nota/dsp/Instrument.h"
#include "instruments/twin/TwinVoice.h"
#include "instruments/twin/TwinSeq.h"

#include <cstdint>
#include <cmath>
#include <atomic>
#include <array>
#include <algorithm>
#include <cstring>
#include <string>
#include <memory>
#include <vector>

namespace nota {

namespace {

void putU32(std::vector<uint8_t>& b, uint32_t v) {
    for (int i = 0; i < 4; ++i) b.push_back(static_cast<uint8_t>(v >> (i * 8)));
}

uint32_t getU32(const uint8_t* p) {
    return static_cast<uint32_t>(p[0]) | static_cast<uint32_t>(p[1]) << 8 | static_cast<uint32_t>(p[2]) << 16 |
           static_cast<uint32_t>(p[3]) << 24;
}

} // namespace

Twin::Twin() {
    const auto& t = twin::paramTable();
    for (int i = 0; i < kNumParams; ++i) pn_[i].store(t[static_cast<size_t>(i)].def, std::memory_order_relaxed);
    // The effects (US5): one set per part, built and sized here so render never allocates.
    for (int p = 0; p < twin::kParts; ++p) {
        fx_[p][FxChorus] = std::make_unique<Chorus>();
        fx_[p][FxFlanger] = std::make_unique<Flanger>();
        fx_[p][FxPhaser] = std::make_unique<Phaser>();
        fx_[p][FxDelay] = std::make_unique<Delay>();
        fx_[p][FxReverb] = std::make_unique<Reverb>();
        fx_[p][FxDelay]->setParam(Delay::LinkLR, 1.0f); // one time for both sides; Ping-pong spreads it
        for (auto& d : fx_[p]) d->setSampleRate(44100.0, twin::kCtl);
    }
    patAuth_ = std::make_shared<twin::PatternSet>();
    publishPatterns();
    tele_[ScRecStep].store(-1.0f, std::memory_order_relaxed);
    tele_[ScLastKey].store(-1.0f, std::memory_order_relaxed);
    resetAll();
}

void Twin::setSampleRate(double sr) {
    sr = sr > 0 ? sr : 44100.0;
    sampleRate_.store(sr, std::memory_order_relaxed);
    for (auto& part : fx_)
        for (auto& d : part) d->setSampleRate(sr, twin::kCtl);
}

void Twin::setTransportInfo(const TransportInfo& ti) {
    for (auto& part : fx_)
        for (auto& d : part) d->setTransportInfo(ti);
}

// ---- render ------------------------------------------------------------------------------

// A patch-wide source's value (the same for every voice of the part).
float Twin::globalValue(int part, const twin::PartCtl& c, int src) const {
    using namespace twin;
    // The pad, or a motion lane playing it (FR-063).
    auto pad = [&](int g) {
        const float o = motionOvr_[part].pad[g - GPadX];
        return o >= 0.0f ? o : get(g);
    };
    auto bip = [&](int g) { return (pad(g) - 0.5f) * 2.0f; };
    switch (src) {
    case SrcLfo1: return glfo_[part][0].val * gLfoFade_[part][0] * c.lfoAmt[0];
    case SrcLfo2: return glfo_[part][1].val * gLfoFade_[part][1] * c.lfoAmt[1];
    case SrcLfo3: return glfo_[part][2].val * gLfoFade_[part][2] * c.lfoAmt[2];
    case SrcModWheel: return modWheel_;
    case SrcPitchWheel: return gexpr_.wheel;
    case SrcPadX: return bip(GPadX);
    case SrcPadY: return bip(GPadY);
    case SrcPadZ: return pad(GPadZ);
    default:
        if (src >= SrcMacro1 && src < SrcMacro1 + kMacros) return get(partIdx(part, PMacro0 + src - SrcMacro1));
        return 0.0f;
    case SrcSeqGate: return seqRunning_ && seqNoteN_ > 0 ? 1.0f : 0.0f; // a step's notes are sounding
    case SrcSeqStep: return seqRunning_ ? seqStepV_ : 0.0f;
    }
}

// A per-note source's value for one voice.
float Twin::sourceValue(const twin::Voice& V, const twin::PartCtl& c, int src) const {
    using namespace twin;
    auto lfo = [&](int l) { return V.lfo[l].val * V.lfoFadeLvl[l] * c.lfoAmt[l]; };
    switch (src) {
    case SrcLfo1: return lfo(0);
    case SrcLfo2: return lfo(1);
    case SrcLfo3: return lfo(2);
    case SrcFiltEnv: return V.fenv.lvl;
    case SrcAmpEnv: return V.aenv.lvl;
    case SrcModEnv: return V.menv.lvl * (1.0f - c.mVel * (1.0f - V.vel));
    case SrcVelocity: return V.vel;
    case SrcKey: return std::clamp((static_cast<float>(V.note) - 60.0f) / 60.0f, -1.0f, 1.0f);
    case SrcPressure: return V.ex.pressure();
    case SrcSlide: return V.ex.slide();
    case SrcBend: return std::clamp(V.ex.bend() / kRoutePitchSt, -1.0f, 1.0f);
    case SrcRandom: return V.rnd;
    case SrcRelVelocity: return V.relVel;
    default: return 0.0f;
    }
}

namespace {

// A source that is the same for every voice of a part: its routes act on the whole part.
bool globalSource(int src, const twin::PartCtl& c) {
    using namespace twin;
    switch (src) {
    case SrcLfo1: return c.lfoGlobal[0];
    case SrcLfo2: return c.lfoGlobal[1];
    case SrcLfo3: return c.lfoGlobal[2];
    case SrcModWheel:
    case SrcPitchWheel:
    case SrcPadX:
    case SrcPadY:
    case SrcPadZ:
    case SrcSeqGate:
    case SrcSeqStep: return true;
    default: return src >= SrcMacro1 && src < SrcMacro1 + kMacros;
    }
}

// The effect controls a route can reach, as bits (per-note routes onto the part's effects).
enum FxField : uint32_t {
    FxModRate = 1,
    FxModDepth = 2,
    FxModMix = 4,
    FxDlyTime = 8,
    FxDlyFb = 16,
    FxDlyMix = 32,
    FxRevSize = 64,
    FxRevDamp = 128,
    FxRevMix = 256
};
uint32_t fxFieldOf(int side) {
    using namespace twin;
    switch (side) {
    case ModFxRate: return FxModRate;
    case ModFxDepth: return FxModDepth;
    case ModFxMix: return FxModMix;
    case DlyTime: return FxDlyTime;
    case DlyFb: return FxDlyFb;
    case DlyMix: return FxDlyMix;
    case RevSize: return FxRevSize;
    case RevDamp: return FxRevDamp;
    case RevMix: return FxRevMix;
    default: return 0;
    }
}

// One route's amount onto the offsets: a side param, the two pitches or the vibrato.
void addRoute(const twin::Dst& d, float x, float* off, float* pitch, float& vib) {
    using namespace twin;
    switch (d.kind) {
    case DstPitch: pitch[0] += x * kRoutePitchSt, pitch[1] += x * kRoutePitchSt; break;
    case DstPitch1: pitch[0] += x * kRoutePitchSt; break;
    case DstPitch2: pitch[1] += x * kRoutePitchSt; break;
    case DstVibrato: vib += x * kRouteVibratoSt; break;
    case DstSide: off[d.side] += x; break;
    default: break;
    }
}

} // namespace

// The part's routes from patch-wide sources, once per interval: offsets the part's PartCtl and
// every voice of it start from. The routes to Morph go first, at the knob's morph; the rest are
// weighed at the morph that gives, so a route that lives only on B starts as the part reaches B.
void Twin::partModulation(int part, float morph) {
    using namespace twin;
    const PartCtl& c = ctl_[part];
    float* off = partOff_[part];
    std::fill(off, off + kSideN, 0.0f);
    partMorphOff_[part] = partVib_[part] = partPitch_[part][0] = partPitch_[part][1] = 0.0f;
    bool any = false;
    for (int i = 0; i < routeN_[part]; ++i) {
        const LiveRoute& r = routes_[part][i];
        if (dstAt(r.dst).kind != DstMorph || !globalSource(r.src, c)) continue;
        const float x = r.at(morph) * globalValue(part, c, r.src);
        if (x == 0.0f) continue;
        partMorphOff_[part] += x;
        any = true;
    }
    const float pm = std::clamp(morph + partMorphOff_[part], 0.0f, 1.0f);
    for (int i = 0; i < routeN_[part]; ++i) {
        const LiveRoute& r = routes_[part][i];
        const Dst d = dstAt(r.dst);
        if (d.kind == DstMorph || !globalSource(r.src, c)) continue;
        const float x = r.at(pm) * globalValue(part, c, r.src);
        if (x == 0.0f) continue;
        any = true;
        addRoute(d, x, off, partPitch_[part], partVib_[part]);
    }
    partHasOff_[part] = any;
}

// One voice's routes from its own sources (envelopes, LFOs, velocity, key, expression, random):
// when any moves something, the voice evaluates its own values from the part's offsets plus its own.
// Its routes to Morph go first (at the knob's morph). When they move this voice's morph away from
// the part's, every other route — the part's patch-wide ones too — is weighed again at the voice's.
void Twin::modulateVoice(twin::Voice& V, float morph, const twin::LiveRoute* routes, int n) {
    using namespace twin;
    const PartCtl& pc = ctl_[V.part];
    const PartCtl& cur = V.hasOwn ? V.own : pc; // this voice's LFO rates and fades last interval
    const int p = V.part;
    float mo = 0.0f;
    for (int i = 0; i < n; ++i) {
        if (dstAt(routes[i].dst).kind != DstMorph || globalSource(routes[i].src, pc)) continue;
        mo += routes[i].at(morph) * sourceValue(V, cur, routes[i].src);
    }
    const float pm = std::clamp(morph + partMorphOff_[p], 0.0f, 1.0f);
    const float vm = std::clamp(morph + partMorphOff_[p] + mo, 0.0f, 1.0f);
    const bool own = vm != pm; // the voice sounds another morph: the part's offsets don't hold
    float pitch[2] = {own ? 0.0f : partPitch_[p][0], own ? 0.0f : partPitch_[p][1]};
    float vib = own ? 0.0f : partVib_[p];
    bool any = mo != 0.0f, sideAny = false;
    if (own) {
        std::fill(modOff_, modOff_ + kSideN, 0.0f);
        sideAny = true;
    }
    for (int i = 0; i < n; ++i) {
        const LiveRoute& r = routes[i];
        const Dst d = dstAt(r.dst);
        if (d.kind == DstMorph) continue;
        const bool global = globalSource(r.src, pc);
        if (global && !own) continue; // already in the part's values
        const float x = r.at(vm) * (global ? globalValue(p, pc, r.src) : sourceValue(V, cur, r.src));
        if (x == 0.0f) continue;
        any = true;
        if (d.kind == DstSide && !sideAny) {
            std::copy(partOff_[p], partOff_[p] + kSideN, modOff_);
            sideAny = true;
        }
        addRoute(d, x, modOff_, pitch, vib);
    }
    V.morphOff = mo;
    V.hasOwn = any;
    if (!any) return;
    evalPart(pn_, p, vm, spb_, sr_, V.own, sideAny ? modOff_ : (partHasOff_[p] ? partOff_[p] : nullptr),
        motionOvr_[p].n > 0 ? &motionOvr_[p] : nullptr);
    std::copy(pc.exAdd, pc.exAdd + kExprDims, V.own.exAdd); // expression is the part's, not a side's
    V.own.exCoef = pc.exCoef;
    V.own.semi[0] += pitch[0];
    V.own.semi[1] += pitch[1];
    V.own.vibExtra = vib;
}

// The part's effect values onto its devices (normalized params, smoothed by the devices).
void Twin::pushFx(int part) {
    const twin::PartCtl& c = ctl_[part];
    auto& fx = fx_[part];
    Device* mod = fx[c.modType == 1 ? FxFlanger : c.modType == 2 ? FxPhaser : FxChorus].get();
    if (mod == fx[FxChorus].get()) {
        mod->setParam(Chorus::Mode, c.modType == 3 ? 0.5f : 0.0f); // Classic, or Ensemble
        mod->setParam(Chorus::Rate, c.modRate);
        mod->setParam(Chorus::Amount, c.modDepth);
        mod->setParam(Chorus::Mix, c.modMix);
    } else if (mod == fx[FxFlanger].get()) {
        mod->setParam(Flanger::Rate, c.modRate);
        mod->setParam(Flanger::Depth, c.modDepth);
        mod->setParam(Flanger::Mix, c.modMix);
    } else {
        mod->setParam(Phaser::Rate, c.modRate);
        mod->setParam(Phaser::Depth, c.modDepth);
        mod->setParam(Phaser::Mix, c.modMix);
    }
    Device* dl = fx[FxDelay].get();
    dl->setParam(Delay::SyncMode, c.dlySync ? 1.0f : 0.0f);
    dl->setParam(Delay::TimeL, c.dlyTime);
    dl->setParam(Delay::TimeR, c.dlyTime);
    dl->setParam(Delay::DivL, c.dlyDiv);
    dl->setParam(Delay::DivR, c.dlyDiv);
    dl->setParam(Delay::Feedback, c.dlyFb);
    dl->setParam(Delay::PingPong, c.dlyPing ? 1.0f : 0.0f);
    dl->setParam(Delay::DryWet, c.dlyMix);
    Device* rv = fx[FxReverb].get();
    rv->setParam(Reverb::Size, c.revSize);
    rv->setParam(Reverb::Decay, 0.15f + 0.8f * c.revSize);
    rv->setParam(Reverb::HFDamp, c.revDamp);
    rv->setParam(Reverb::DryWet, c.revMix);
}

void Twin::updateControl(double now) {
    using namespace twin;
    ctlInit_ = true;
    static constexpr float kBendRanges[4] = {2.0f, 5.0f, 12.0f, 24.0f};
    const float bendRange = kBendRanges[stepOf(get(GBendRange), 4)];
    const int layout = layoutMode();
    const int parts = layout == 0 ? 1 : 2;
    const int recLane = recLane_.load(std::memory_order_relaxed);
    for (int part = 0; part < parts; ++part) {
        // The sequencer's motion lanes (paused on a lane that is recording): this interval's values.
        MotionOverride& mo = motionOvr_[part];
        mo = MotionOverride{};
        if (seqRunning_)
            for (int l = 0; l < kLanes; ++l) {
                const Motion& M = motion_[l];
                if (!M.set || l == recLane) continue;
                const float t = M.smooth ? static_cast<float>(std::clamp((now - M.t0) / M.len, 0.0, 1.0)) : 0.0f;
                const float v = M.from + (M.to - M.from) * t;
                if (M.target == partIdx(part, PMorph)) mo.morph = v;
                if (M.target >= GPadX && M.target <= GPadZ) mo.pad[M.target - GPadX] = v;
                for (int sd = 0; sd < kSides; ++sd) {
                    const int s0 = sideIdx(part, sd, 0);
                    if (M.target >= s0 && M.target < s0 + kSideN && mo.n < 4) {
                        mo.side[mo.n] = sd;
                        mo.param[mo.n] = M.target - s0;
                        mo.v[mo.n++] = v;
                    }
                }
            }
        const MotionOverride* ovr = mo.n > 0 ? &mo : nullptr;
        const float morph = mo.morph >= 0.0f ? mo.morph : partMorph(pn_, part);
        morphNow_[part] = morph;
        PartCtl& c = ctl_[part];
        evalPart(pn_, part, morph, spb_, sr_, c, nullptr, ovr);
        routeN_[part] = liveRoutes(pn_, part, routes_[part]);
        partModulation(part, morph);
        c.vibExtra = 0.0f; // a route that stopped (the pad let go, the route removed) leaves none
        if (partHasOff_[part]) {
            evalPart(pn_, part, std::clamp(morph + partMorphOff_[part], 0.0f, 1.0f), spb_, sr_, c, partOff_[part], ovr);
            c.semi[0] += partPitch_[part][0];
            c.semi[1] += partPitch_[part][1];
            c.vibExtra = partVib_[part];
        }
        gexpr_.offsets(bendRange, c.exAdd);
        c.exCoef = exprSmoothCoef(sr_);
    }
    // Structural changes release (never cut) what no longer fits: a new layout, voice share or
    // cap releases everything; a part's new voice mode releases that part.
    const int cap = voiceCap();
    const int shape = layout * 1000 + stepOf(get(GShare), 5) * 100 + cap;
    if (lastLayout_ >= 0 && (shape != lastLayout_ || cap < lastCap_)) {
        for (auto& V : voices_)
            if (V.gate) release(V);
        heldN_[0] = heldN_[1] = 0;
    }
    // Back to Single: the Upper part's voices were released above (never cut); it goes on
    // rendering — voices and effects — until its tail is quiet (renderSegment), then stops.
    if (parts == 1) {
        for (auto& V : voices_)
            if (V.active && V.part != 0) {
                V.pendNote = -1;
                upperTail_ = true;
            }
    } else
        upperTail_ = false;
    for (int part = 0; part < kParts; ++part) {
        const int vm = ctl_[part].voiceMode;
        if (part < parts && lastMode_[part] >= 0 && vm != lastMode_[part]) {
            for (auto& V : voices_)
                if (V.gate && V.part == part) release(V);
            heldN_[part] = 0;
        }
        lastMode_[part] = vm;
    }
    lastLayout_ = shape;
    lastCap_ = cap;
    // A global LFO's fade-in (all three: LFO 1 feeds the matrix too).
    for (int part = 0; part < parts; ++part)
        for (int l = 0; l < 3; ++l) {
            const PartCtl& pc = ctl_[part];
            float& f = gLfoFade_[part][l];
            f = pc.lfoGlobal[l] && pc.lfoFade[l] > 0.0f
                    ? std::min(1.0f, f + static_cast<float>(kCtl / (pc.lfoFade[l] * sr_)))
                    : 1.0f;
        }
    // The LFOs that only feed the matrix move once per interval (LFO 1 runs per sample, for vibrato).
    for (int part = 0; part < parts; ++part)
        for (int l = 1; l < 3; ++l)
            if (ctl_[part].lfoGlobal[l])
                glfo_[part][l].step(ctl_[part].lfoHz[l] / sr_ * kCtl, ctl_[part].lfoShape[l], ctl_[part].lfoSym[l],
                    ctl_[part].lfoCurve[l]);
    for (auto& V : voices_) {
        if (!V.active) continue;
        const PartCtl& vc = V.hasOwn ? V.own : ctl_[V.part];
        for (int l = 1; l < 3; ++l) {
            if (!vc.lfoGlobal[l]) V.lfo[l].step(vc.lfoHz[l] / sr_ * kCtl, vc.lfoShape[l], vc.lfoSym[l], vc.lfoCurve[l]);
            if (V.lfoFadeLvl[l] < 1.0f) V.lfoFadeLvl[l] = std::min(1.0f, V.lfoFadeLvl[l] + V.lfoFadeInc[l] * kCtl);
        }
        modulateVoice(V, morphNow_[V.part], routes_[V.part], routeN_[V.part]);
        voiceControl(V, V.hasOwn ? V.own : ctl_[V.part], sr_);
    }
    // The effects are one per part: a route from a per-note source (velocity, an envelope…) to an
    // effect control follows the newest note of the part, as on a poly synth's global destinations.
    for (int part = 0; part < parts; ++part) {
        PartCtl& c = ctl_[part];
        if (!c.fxOn) continue;
        uint32_t mask = 0;
        for (int i = 0; i < routeN_[part]; ++i) {
            const LiveRoute& r = routes_[part][i];
            const Dst d = dstAt(r.dst);
            if (d.kind == DstSide && !globalSource(r.src, c)) mask |= fxFieldOf(d.side);
        }
        // The newest sounding note sets them, and they stay where it left them once every note is
        // gone (an effect's tail keeps the last note's mix).
        const Voice* newest = nullptr;
        if (mask != 0)
            for (const auto& V : voices_)
                if (V.active && V.part == part && V.hasOwn && (!newest || V.order > newest->order)) newest = &V;
        if (newest) fxHold_[part] = newest->own, fxHeld_[part] = true;
        if (mask == 0) fxHeld_[part] = false;
        if (fxHeld_[part]) {
            const PartCtl& o = fxHold_[part];
            if (mask & FxModRate) c.modRate = o.modRate;
            if (mask & FxModDepth) c.modDepth = o.modDepth;
            if (mask & FxModMix) c.modMix = o.modMix;
            if (mask & FxDlyTime) c.dlyTime = o.dlyTime;
            if (mask & FxDlyFb) c.dlyFb = o.dlyFb;
            if (mask & FxDlyMix) c.dlyMix = o.dlyMix;
            if (mask & FxRevSize) c.revSize = o.revSize;
            if (mask & FxRevDamp) c.revDamp = o.revDamp;
            if (mask & FxRevMix) c.revMix = o.revMix;
        }
        pushFx(part);
    }
    // LFO 1 in global mode: this interval's values, per part.
    for (int part = 0; part < parts; ++part)
        if (ctl_[part].lfoGlobal[0]) {
            Lfo& L = glfo_[part][0];
            const double inc = ctl_[part].lfoHz[0] / sr_;
            for (int i = 0; i < kCtl; ++i) gLfo1_[part][i] = L.step(inc, ctl_[part].lfoShape[0]);
        }
}

void Twin::renderSegment(float* out, int n) {
    const int os = os_;
    int active = 0;
    float pk = blockPeak_;
    const int parts = layoutMode() != 0 || upperTail_ ? 2 : 1;
    for (int part = 0; part < parts; ++part) {
        const float* lfo1 = gLfo1_[part] + (twin::kCtl - ctlLeft_);
        bool sounding = false;
        twin::Voice* partVoices[twin::kMaxVoices];
        int partCount = 0;
        bool anyPending = false;
        for (auto& V : voices_) {
            if (!V.active || V.part != part) continue;
            partVoices[partCount++] = &V;
            if (V.pendNote >= 0) anyPending = true;
        }
        sounding = partCount > 0;
        const int minParallelVoices = (n < 64) ? 8 : 6;
        if (!anyPending && partCount >= minParallelVoices && n >= 32 && voiceScratch_.capacityFrames() >= n * os) {
            std::array<dsp::PolyVoiceBatchRange, dsp::PolyVoiceScheduler::kMaxBatches> batches{};
            const int32_t maxBatches = (n < 64 && partCount <= 12) ? 3 : 4;
            const int32_t batchesN = dsp::PolyVoiceScheduler::planBatches(partCount, maxBatches, batches);
            const int samples = n * os;
            dsp::RenderExecutor::parallelFor(batchesN, true, [&](int32_t b) {
                voiceScratch_.clearBatch(b, samples);
                float* bL = voiceScratch_.left(b);
                float* bR = voiceScratch_.right(b);
                const auto range = batches[static_cast<size_t>(b)];
                for (int32_t vi = range.start; vi < range.start + range.count; ++vi) {
                    twin::Voice& V = *partVoices[vi];
                    twin::renderVoice(
                        V, V.hasOwn ? V.own : ctl_[V.part], 0, n, os, sr_, modWheel_, fadeLen_, lfo1, bL, bR);
                }
            });
            std::copy_n(voiceScratch_.left(0), samples, osL_);
            std::copy_n(voiceScratch_.right(0), samples, osR_);
            for (int32_t b = 1; b < batchesN; ++b) {
                dsp::PolyVoiceScheduler::accumulateMonoAdd(osL_, voiceScratch_.left(b), samples);
                dsp::PolyVoiceScheduler::accumulateMonoAdd(osR_, voiceScratch_.right(b), samples);
            }
            for (int vi = 0; vi < partCount; ++vi) active += partVoices[vi]->active ? 1 : 0;
        } else {
            std::fill(osL_, osL_ + n * os, 0.0f);
            std::fill(osR_, osR_ + n * os, 0.0f);
            for (int vi = 0; vi < partCount; ++vi) {
                twin::Voice& V = *partVoices[vi];
                int k = 0;
                while (k < n) {
                    k = twin::renderVoice(
                        V, V.hasOwn ? V.own : ctl_[V.part], k, n, os, sr_, modWheel_, fadeLen_, lfo1, osL_, osR_);
                    if (k >= n) break;
                    if (V.active && V.fade == 0 && V.pendNote >= 0) startPending(V);
                    else
                        break;
                }
                active += V.active ? 1 : 0;
            }
        }
        if (os == 2) {
            dn_[part][0].process_block(dsL_, osL_, n);
            dn_[part][1].process_block(dsR_, osR_, n);
        } else {
            std::copy(osL_, osL_ + n, dsL_);
            std::copy(osR_, osR_ + n, dsR_);
        }
        for (int i = 0; i < n; ++i) {
            fxBuf_[2 * i] = dsL_[i] * kOutGain;
            fxBuf_[2 * i + 1] = dsR_[i] * kOutGain;
        }
        // The part's effects: Mod FX ▸ Delay ▸ Reverb. One switched on starts from silence.
        const twin::PartCtl& c = ctl_[part];
        const bool on[kFxN] = {c.fxOn && c.modOn && (c.modType == 0 || c.modType == 3),
            c.fxOn && c.modOn && c.modType == 1, c.fxOn && c.modOn && c.modType == 2, c.fxOn && c.dlyOn,
            c.fxOn && c.revOn};
        for (int f = 0; f < kFxN; ++f) {
            if (!on[f]) {
                fxWasOn_[part][f] = false;
                continue;
            }
            if (!fxWasOn_[part][f]) {
                static constexpr int kClear[kFxN] = {
                    Chorus::A_Clear, Flanger::A_Clear, Phaser::A_Clear, Delay::A_ClearLoop, Reverb::A_KillTail};
                fx_[part][f]->deviceAction(kClear[f], 0, 0.0f);
                fx_[part][f]->wake();
                fxWasOn_[part][f] = true;
            }
            fx_[part][f]->process(fxBuf_, n);
        }
        float partPk = 0.0f;
        for (int i = 0; i < n; ++i) {
            out[2 * i] += fxBuf_[2 * i];
            out[2 * i + 1] += fxBuf_[2 * i + 1];
            partPk = std::max(partPk, std::max(std::fabs(fxBuf_[2 * i]), std::fabs(fxBuf_[2 * i + 1])));
        }
        pk = std::max(pk, partPk);
        // Upper's tail after a switch to Single: done once no voice of it sounds and it is quiet.
        if (part == 1 && layoutMode() == 0) {
            upperQuiet_ = sounding || partPk > 1.0e-5f ? 0 : upperQuiet_ + static_cast<uint64_t>(n);
            const uint64_t hang = kIdleHangover + (fxWasOn_[1][FxDelay] ? static_cast<uint64_t>(sr_ * kDelayHoldSec)
                                                                        : 0); // echoes rest in silence
            if (upperQuiet_ > hang) upperTail_ = false, upperQuiet_ = 0;
        }
    }
    blockPeak_ = pk;
    activeVoices_.store(active, std::memory_order_relaxed);
}

// Picks up a new device rate; voices keep their notes, only rate-derived state restarts.
void Twin::applyRate() {
    const double sr = sampleRate_.load(std::memory_order_relaxed);
    if (sr == sr_) return;
    sr_ = sr;
    os_ = sr_ >= 88000.0 ? 1 : 2;
    fadeLen_ = std::max(1, static_cast<int>(0.004 * sr_));
    static const double kSteep[12] = {0.017347915108876406, 0.067150480426919179, 0.14330738338179819,
        0.23745131944299824, 0.34085550201503761, 0.44601111310335906, 0.54753112652956148, 0.6423859124721446,
        0.72968928615804163, 0.81029959388029904, 0.88644514917318362, 0.96150605146543733};
    for (auto& part : dn_)
        for (auto& d : part) {
            d.set_coefs(kSteep);
            d.clear_buffers();
        }
    ctlInit_ = false;
    ctlLeft_ = 0;
}

void Twin::render(float* out, int32_t frames) {
    SnapshotHistory<twin::PatternSet>::RenderScope patScope(pats_);
    dsp::ScopedNoDenormals ftz;
    applyRate();
    // Asleep — no voice, a quiet hangover, no sequencer that could play — so nothing to add
    // until a note, a pattern edit or a transport start wakes it (SC-006).
    if (wakeReq_.exchange(false, std::memory_order_acquire)) {
        quiet_ = 0;
        idle_.store(false, std::memory_order_relaxed);
    }
    if (idle_.load(std::memory_order_relaxed) && !seqActive()) {
        tFrames_ += frames;
        if (spb_ > 0.0) patLane_.rendered(clockAt(0) / spb_, playing_);
        return;
    }
    for (int32_t done = 0; done < frames;) {
        if (ctlLeft_ == 0) {
            updateControl(clockAt(done));
            ctlLeft_ = twin::kCtl;
        }
        const int toEvent = seqService(done); // fires due steps, says when the next one is
        const int n = std::min<int>({ctlLeft_, frames - done, toEvent});
        renderSegment(out + done * 2, n);
        ctlLeft_ -= n;
        done += n;
    }
    tFrames_ += frames;
    if (!playing_) freeT_ += frames;
    if (spb_ > 0.0) patLane_.rendered(clockAt(0) / spb_, playing_); // the block's lane samples go with it
    if (activeVoices_.load(std::memory_order_relaxed) == 0 && blockPeak_ <= 1.0e-5f && !seqRunning_)
        quiet_ += static_cast<uint64_t>(frames);
    else
        quiet_ = 0;
    // A delay holds its echoes in silence: while one is on, wait out its longest time too.
    bool delayOn = false;
    for (const auto& part : fxWasOn_) delayOn |= part[FxDelay];
    const uint64_t hang =
        kIdleHangover +
        (delayOn ? static_cast<uint64_t>(sampleRate_.load(std::memory_order_relaxed) * kDelayHoldSec) : 0);
    idle_.store(quiet_ > hang, std::memory_order_relaxed);
    publishTelemetry(blockPeak_);
    blockPeak_ = 0.0f;
    heldPub_[0].store(heldBits_[0], std::memory_order_relaxed);
    heldPub_[1].store(heldBits_[1], std::memory_order_relaxed);
}

void Twin::publishTelemetry(float peak) {
    auto set = [&](int i, float v) { tele_[i].store(v, std::memory_order_relaxed); };
    set(ScStep, seqRunning_ ? static_cast<float>(playStep_) : -1.0f);
    set(ScPattern, static_cast<float>(curPat_));
    set(ScPending, static_cast<float>(pendPat_));
    set(ScRevision, static_cast<float>(rev_.load(std::memory_order_relaxed)));
    int lo = 0, up = 0;
    const twin::Voice* newest = nullptr;
    for (int v = 0; v < kMaxVoices; ++v) {
        const twin::Voice& V = voices_[v];
        if (V.active) {
            (V.part == 0 ? lo : up)++;
            if (!newest || V.order > newest->order) newest = &V;
        }
        const float base = std::clamp(twin::partMorph(pn_, V.part) + partMorphOff_[V.part], 0.0f, 1.0f);
        set(ScVoiceMorph0 + v, V.active ? std::clamp(base + V.morphOff, 0.0f, 1.0f) : -1.0f);
    }
    set(ScVoicesLo, static_cast<float>(lo));
    set(ScVoicesUp, static_cast<float>(up));
    for (int p = 0; p < twin::kParts; ++p)
        set(ScMorphLo + p, std::clamp(twin::partMorph(pn_, p) + partMorphOff_[p], 0.0f, 1.0f));
    // An LFO's phase: the shared one in global mode, else the newest voice's.
    for (int l = 0; l < 3; ++l) {
        const bool global = ctl_[0].lfoGlobal[l];
        const double ph = global ? glfo_[0][l].ph : newest ? newest->lfo[l].ph : 0.0;
        set(ScLfoPhase0 + l, static_cast<float>(ph));
    }
    set(ScPeakDb, peak > 1e-9f ? 20.0f * std::log10(peak) : -180.0f);
    // Each part's newest voice on its three envelopes, for the Env tab's graphs (#531).
    for (int p = 0; p < twin::kParts; ++p) {
        const twin::Voice* nv = nullptr;
        for (int v = 0; v < kMaxVoices; ++v)
            if (voices_[v].active && voices_[v].part == p && (!nv || voices_[v].order > nv->order)) nv = &voices_[v];
        const int base = p == 0 ? ScEnvLo : ScEnvUp;
        const twin::Env* envs[3] = {nv ? &nv->fenv : nullptr, nv ? &nv->aenv : nullptr, nv ? &nv->menv : nullptr};
        for (int e = 0; e < 3; ++e) {
            set(base + e * kEnvTeleFloats, envs[e] ? static_cast<float>(envs[e]->teleStage()) : 4.0f);
            set(base + e * kEnvTeleFloats + 1, envs[e] ? envs[e]->progress() : 0.0f);
        }
    }
}

int32_t Twin::scopeRead(float* out, int32_t maxN) const {
    const int32_t n = std::min<int32_t>(maxN, kScopeN);
    for (int32_t i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
    return n;
}

int32_t Twin::heldNotes(int32_t* out, int32_t maxN) const {
    int32_t n = 0;
    for (int w = 0; w < 2; ++w) {
        const uint64_t bits = heldPub_[w].load(std::memory_order_relaxed);
        for (int b = 0; b < 64 && n < maxN; ++b)
            if (bits & (uint64_t{1} << b)) out[n++] = w * 64 + b;
    }
    return n;
}

// ---- project state ----------------------------------------------------------------------

std::vector<uint8_t> Twin::getState() const {
    std::vector<uint8_t> b;
    b.reserve(12 + kNumParams * sizeof(float));
    putU32(b, kStateMagic);
    putU32(b, static_cast<uint32_t>(kNumParams));
    for (int i = 0; i < kNumParams; ++i) {
        const float v = pn_[i].load(std::memory_order_relaxed);
        uint32_t u;
        std::memcpy(&u, &v, sizeof u);
        putU32(b, u);
    }
    const auto pat = twin::serializePatterns(*patAuth_);
    putU32(b, static_cast<uint32_t>(pat.size()));
    b.insert(b.end(), pat.begin(), pat.end());
    return b;
}

void Twin::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 8 || getU32(data) != kStateMagic) return;
    const uint32_t count = getU32(data + 4);
    const size_t avail = (static_cast<size_t>(size) - 8) / sizeof(float);
    const int n = static_cast<int>(std::min<size_t>({count, avail, static_cast<size_t>(kNumParams)}));
    // A state from before the envelope shapes (#531) has shorter side blocks: each value goes
    // to its place in today's layout, and the shapes keep their defaults.
    const bool v1 = count == static_cast<uint32_t>(twin::kNumParamsV1);
    auto slotOf = [v1](int i) { return v1 ? twin::paramFromV1(i) : i; };
    if (v1) {
        const auto& t = twin::paramTable();
        for (int i = 0; i < kNumParams; ++i) pn_[i].store(t[static_cast<size_t>(i)].def, std::memory_order_relaxed);
    }
    for (int i = 0; i < n; ++i) {
        const uint32_t u = getU32(data + 8 + static_cast<size_t>(i) * sizeof(float));
        float v;
        std::memcpy(&v, &u, sizeof v);
        if (std::isfinite(v)) pn_[slotOf(i)].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    // The pattern block after the params (a preset may carry only it: paramCount 0).
    const size_t at = 8 + static_cast<size_t>(count) * sizeof(float);
    if (static_cast<size_t>(size) >= at + 4) {
        const uint32_t bytes = getU32(data + at);
        if (static_cast<size_t>(size) >= at + 4 + bytes) {
            auto next = std::make_shared<twin::PatternSet>();
            if (twin::deserializePatterns(data + at + 4, static_cast<int32_t>(bytes), *next)) {
                // Motion lanes name their control by param index: an old state's lanes move
                // with the values, or they would drive whatever sits at the old index now.
                if (v1)
                    for (auto& P : next->pat)
                        for (auto& L : P.lane)
                            if (L.target >= 0 && L.target < twin::kNumParamsV1)
                                L.target = static_cast<int16_t>(slotOf(L.target));
                patAuth_ = next;
                publishPatterns();
            }
        }
    }
}

std::vector<uint8_t> Twin::editState() const {
    return twin::serializePatterns(*patAuth_);
}

void Twin::setEditState(const uint8_t* data, int32_t size) {
    auto next = std::make_shared<twin::PatternSet>();
    if (!twin::deserializePatterns(data, size, *next)) return;
    patAuth_ = next;
    publishPatterns();
}

void Twin::publishPatterns() {
    pats_.publish(std::make_shared<twin::PatternSet>(*patAuth_));
    rev_.fetch_add(1, std::memory_order_relaxed);
    wake();
}

std::shared_ptr<Instrument> Twin::clone() const {
    auto s = std::make_shared<Twin>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->patAuth_ = std::make_shared<twin::PatternSet>(*patAuth_);
    s->publishPatterns();
    s->setSampleRate(sampleRate_.load(std::memory_order_relaxed));
    return s;
}

} // namespace nota
