// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "WavetableSynth.h"
#include "WavetableInternals.h"
#include "common/DeviceMath.h"
#include <vector>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <atomic>
#include <array>
#include <algorithm>
#include "nota/dsp/rt/RenderExecutor.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/dsp/poly/PolyVoiceScheduler.h"
#include "common/EnvShape.h"

namespace nota {
int32_t WavetableSynth::scopeRead(float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    const int32_t n = std::min(maxN, kScopeN);
    for (int32_t i = 0; i < n; ++i) out[i] = scope_[i].load(std::memory_order_relaxed);
    // The serials come from the set the message thread holds, so they are current even
    // while the synth sleeps and is not rendered.
    const auto cur = tableSet_.current();
    for (int o = 0; o < 2 && 2 + o < n; ++o)
        out[2 + o] = (cur && cur->slot[o]) ? static_cast<float>(cur->slot[o]->serial & 0xFFFFFFu) : 0.0f;
    // Which oscillators play their imported table (bit 0 = Osc 1, bit 1 = Osc 2), so a card
    // sees a source switch made anywhere else (another card, an undo, a state load).
    if (n > 5) out[5] = static_cast<float>((useCustom(0) ? 1 : 0) | (useCustom(1) ? 2 : 0));
    if (maxN <= kScopeN) return n;
    return kScopeN + readEnvTele(envTele_, 2 * kEnvTeleFloats, out + kScopeN, maxN - kScopeN);
}

void WavetableSynth::noteOn(int32_t pitch, float velocity) {
    const bool mono = get(MonoMode) > 0.5f;
    Voice* v = mono ? &voices_[0] : findFreeVoice();
    const bool legato = mono && v->active && v->stage != Stage::Off;
    v->active = true;
    v->pitch = pitch;
    v->seq = ++noteSeq_;
    v->freq = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
    {
        float add[kExprDims];
        gexpr_.offsets(wheelRange(), add);
        v->ex.reset(add);
        v->exBendAt = 0.0f;
        v->exBendMul = 1.0;
    }
    v->velocity = std::clamp(velocity, 0.0f, 1.0f);
    v->ktOct = (pitch - 60) / 12.0;
    lastVoice_ = static_cast<int>(v - voices_);
    rng_ = rng_ * 1664525u + 1013904223u;
    v->rand = ((rng_ >> 8) / 16777216.0) * 2.0 - 1.0;
    if (!legato) {
        v->env = 0.0f;
        v->stage = Stage::Attack;
        v->env2 = 0.0f;
        v->stage2 = Stage::Attack;
        v->shape = {};
        v->shape2 = {};
        v->ic1L[0] = v->ic2L[0] = v->ic1R[0] = v->ic2R[0] = 0.0;
        v->ic1L[1] = v->ic2L[1] = v->ic1R[1] = v->ic2R[1] = 0.0;
        v->clearSide();
        for (int u = 0; u < kUnison; ++u) {
            rng_ = rng_ * 1664525u + 1013904223u;
            v->ph1[u] = (rng_ >> 8) / 16777216.0;
            rng_ = rng_ * 1664525u + 1013904223u;
            v->ph2[u] = (rng_ >> 8) / 16777216.0;
        }
        v->subPh = 0.0;
    }
    wake();
}

void WavetableSynth::noteOff(int32_t pitch) {
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage != Stage::Release) {
            v.stage = Stage::Release;
            v.stage2 = Stage::Release;
        }
    wake();
}

void WavetableSynth::allNotesOff() {
    gexpr_.reset();
    for (auto& v : voices_) v.active = false;
    wake();
}

NOTA_ALWAYS_INLINE void WavetableSynth::setSvf(SvfCoef& c, double fc, double k) const {
    double g = std::tan(kPi * fc / sampleRate_);
    c.a1 = 1.0 / (1.0 + g * (g + k));
    c.a2 = g * c.a1;
    c.a3 = g * c.a2;
    c.k = k;
}

NOTA_ALWAYS_INLINE double WavetableSynth::svf(const SvfCoef& c, double& ic1, double& ic2, double in, int type) {
    double v3 = in - ic2, v1 = c.a1 * ic1 + c.a2 * v3, v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
    ic1 = 2 * v1 - ic1;
    ic2 = 2 * v2 - ic2;
    switch (type) {
    case 1: return in - c.k * v1 - v2;
    case 2: return v1;
    case 3: return in - c.k * v1;
    case 4: return in + (c.k > 0 ? v1 * (2.0 - c.k) : v1);
    default: return v2;
    }
}

double WavetableSynth::filterPair(const SvfCoef* f, double* ic1a, double* ic2a, double* ic1b, double* ic2b, double in1,
    double in2, bool series, int t1, int t2, bool slope1, bool slope2) {
    if (series) {
        double a = svf(f[0], ic1a[0], ic2a[0], in1 + in2, t1);
        if (slope1) a = svf(f[0], ic1b[0], ic2b[0], a, t1);
        double b = svf(f[1], ic1a[1], ic2a[1], a, t2);
        if (slope2) b = svf(f[1], ic1b[1], ic2b[1], b, t2);
        return b;
    }
    double a = svf(f[0], ic1a[0], ic2a[0], in1, t1);
    if (slope1) a = svf(f[0], ic1b[0], ic2b[0], a, t1);
    double b = svf(f[1], ic1a[1], ic2a[1], in2, t2);
    if (slope2) b = svf(f[1], ic1b[1], ic2b[1], b, t2);
    return a + b;
}

NOTA_ALWAYS_INLINE double WavetableSynth::warpRead(int mode, double phase, double amt) {
    switch (mode) {
    case 1: {
        double p = phase * (1.0 + amt * 3.0);
        return p - std::floor(p);
    } // Sync
    case 2: {
        double d = std::clamp(0.5 + (amt - 0.5) * 0.9, 0.05, 0.95);
        return phase < d ? 0.5 * phase / d : 0.5 + 0.5 * (phase - d) / (1.0 - d);
    } // Bend
    case 3: {
        double w = 0.05 + amt * 0.9;
        return phase < w ? 0.5 * phase / w : 0.5 + 0.5 * (phase - w) / (1.0 - w);
    } // PWM-ish
    default: return phase; // Off / Fold (no phase change)
    }
}

NOTA_ALWAYS_INLINE float WavetableSynth::foldOut(float s, double amt) {
    const float g = 1.0f + static_cast<float>(amt) * 3.0f;
    // Smooth wavefolder — downstream shaper, so the fast sine is transparent here.
    return fast_math::sin(g * s * 0.25f); // == sin(π/2·g·s)
}

NOTA_ALWAYS_INLINE float WavetableSynth::oscStack(double* ph, double inc, int bank, float pos, int mode, double warpAmt,
    int uniN, const double* detF, const float* panL, const float* panR, float norm, float& sideOut) const {
    const float posF = pos * (kFrames - 1);
    const int f0 = std::clamp(static_cast<int>(posF), 0, kFrames - 1), f1 = std::min(f0 + 1, kFrames - 1);
    const float ff = posF - static_cast<float>(f0);
    const float* t0 = frame(bank, f0);
    const float* t1 = frame(bank, f1);
    float accL = 0.0f, accR = 0.0f;
    for (int u = 0; u < uniN; ++u) {
        double rp = warpRead(mode, ph[u], warpAmt);
        float s = readMorph(t0, t1, ff, rp);
        if (mode == 4) s = foldOut(s, warpAmt);
        // Detune factors and equal-power gains are block-constant (see snapshotBlockParams()).
        accL += s * panL[u];
        accR += s * panR[u];
        ph[u] += inc * detF[u];
        if (ph[u] >= 1.0) ph[u] -= 1.0;
    }
    accL *= norm;
    accR *= norm;
    sideOut = (accL - accR) * 0.5f;
    return (accL + accR) * 0.5f;
}

NOTA_ALWAYS_INLINE float WavetableSynth::oscStackCustom(double* ph, double inc, const AuroraTable& t, float pos,
    int mode, double warpAmt, int uniN, const double* detF, double maxDet, const float* panL, const float* panR,
    float norm, float& sideOut) const {
    // Mip pick (research R4): the smallest level k whose top harmonic (below 1024 >> k)
    // stays under 0.45 of the sample rate at the fastest unison voice, with the warp
    // mode's bias — Sync squeezes the cycle up to 8×, Bend up to 4×.
    double bias = 1.0;
    if (mode == 1) bias = 1.0 + 7.0 * warpAmt;
    else if (mode == 2)
        bias = 1.0 + 3.0 * warpAmt;
    const double fEff = std::fabs(inc) * maxDet * bias;
    int k = 0;
    while (k < AuroraTable::kLevels - 1 && static_cast<double>(AuroraTable::harmonics(k)) * fEff > 0.45) ++k;
    const int len = AuroraTable::len(k);
    const int nf = t.frames;
    const float posF = pos * static_cast<float>(nf - 1);
    const int f0 = std::clamp(static_cast<int>(posF), 0, nf - 1), f1 = std::min(f0 + 1, nf - 1);
    const float ff = posF - static_cast<float>(f0);
    const float* t0 = t.frame(k, f0);
    const float* t1 = t.frame(k, f1);
    float accL = 0.0f, accR = 0.0f;
    for (int u = 0; u < uniN; ++u) {
        double rp = warpRead(mode, ph[u], warpAmt);
        float s = readMorphSized(t0, t1, ff, rp, len);
        if (mode == 4) s = foldOut(s, warpAmt);
        accL += s * panL[u];
        accR += s * panR[u];
        ph[u] += inc * detF[u];
        if (ph[u] >= 1.0) ph[u] -= 1.0;
    }
    accL *= norm;
    accR *= norm;
    sideOut = (accL - accR) * 0.5f;
    return (accL + accR) * 0.5f;
}

NOTA_ALWAYS_INLINE double WavetableSynth::lfoValue(int shape, double ph, float sh) {
    switch (shape) {
    case 1: return ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph;
    case 2: return ph < 0.5 ? 1 : -1;
    case 3: return sh;
    // Sine LFO: REVERTED to std:: (measured -44.8 dB with fast_math::sin).
    default: return std::sin(kTwoPi * ph);
    }
}

NOTA_ALWAYS_INLINE float WavetableSynth::driveSample(int mode, float x, float amt, float invNorm) {
    const float d = 1.0f + amt * 8.0f;
    switch (mode) {
    case 1: {
        float u = x * d + 0.35f * x * x * d;
        float y = u / (1.0f + std::fabs(u));
        return y / (1.0f - 1.0f / (d * 0.7f + 2.0f));
    }
    case 2: {
        float y = std::sin(static_cast<float>(kHalfPi) * std::clamp(x * d, -3.0f, 3.0f));
        return y * 0.8f;
    }
    default: return fast_math::tanhPrecise(x * d) * invNorm;
    }
}

NOTA_ALWAYS_INLINE float WavetableSynth::tone(float& z, float x, float coef) {
    const float a = 0.12f + 0.55f * (1.0f - std::fabs(coef));
    z += (x - z) * a;
    return coef < 0 ? z + (x - z) * (1.0f + coef) : x + (x - z) * coef * 1.4f;
}

NOTA_ALWAYS_INLINE void WavetableSynth::stepFilterSmoothing(
    const BlockScalars& s, double& f1base, double& f2base, double& reso1, double& reso2) noexcept {
    f1Sm_ += (s.f1tgt - f1Sm_) * s.fSm;
    f2Sm_ += (s.f2tgt - f2Sm_) * s.fSm;
    r1Sm_ += (s.r1tgt - r1Sm_) * s.fSm;
    r2Sm_ += (s.r2tgt - r2Sm_) * s.fSm;
    f1base = std::exp2(f1Sm_);
    f2base = std::exp2(f2Sm_);
    reso1 = r1Sm_;
    reso2 = r2Sm_;
}

NOTA_ALWAYS_INLINE void WavetableSynth::stepLfos(const BlockScalars& s, double& lfo1, double& lfo2) noexcept {
    if (lfoPhase1_ >= 1.0) {
        lfoPhase1_ -= 1.0;
        lfoSh1_ = frand() * 2.0f - 1.0f;
    }
    if (lfoPhase2_ >= 1.0) {
        lfoPhase2_ -= 1.0;
        lfoSh2_ = frand() * 2.0f - 1.0f;
    }
    lfo1 = lfoValue(s.ls1, lfoPhase1_, lfoSh1_) * s.lfo1Depth;
    lfo2 = lfoValue(s.ls2, lfoPhase2_, lfoSh2_) * s.lfo2Depth;
    lfoPhase1_ += s.lfo1Inc;
    lfoPhase2_ += s.lfo2Inc;
}

NOTA_ALWAYS_INLINE bool WavetableSynth::renderVoiceSample(const BlockScalars& s, const BlockArrays& a, Voice& v,
    double lfo1, double lfo2, const BatchWork* bw, int32_t bwIdx, double scalarF1, double scalarF2, double scalarR1,
    double scalarR2, float& outL, float& outR) const noexcept {
    stepEnv(v.env, v.stage, v.shape, s.atk, s.dec, s.sus, s.rel, s.bend1);
    if (v.stage == Stage::Off) {
        v.active = false;
        return false;
    }
    stepEnv(v.env2, v.stage2, v.shape2, s.atk2, s.dec2, s.sus2, s.rel2, s.bend2);

    double dst[kDests] = {s.mPitch, s.mOsc2Pitch, s.mPos, s.mCut, s.mReso, s.mLevel, s.mPan};
    if (s.amN) {
        const double src[kSources] = {
            v.env, v.env2, lfo1, lfo2, v.velocity, std::clamp(v.ktOct / 4.0, -1.0, 1.0), s.modWheel, v.rand};
        for (int k = 0; k < s.amN; ++k) dst[a.am[k].dst] += src[a.am[k].src] * a.am[k].amt;
    }
    v.ex.tick(a.exAdd, s.exprCoef);
    if (v.ex.bend() != v.exBendAt) {
        v.exBendAt = v.ex.bend();
        v.exBendMul = std::exp2(v.exBendAt / 12.0);
    }
    const float exPos = v.ex.slide() * 0.5f;
    const double pMul = s.pitchStatic ? 1.0 : std::exp2(dst[0] * 2.0);
    const float pos1 = std::clamp(s.basePos1 + static_cast<float>(dst[2]) + exPos, 0.0f, 1.0f);
    const float pos2 = std::clamp(s.basePos2 + static_cast<float>(dst[2]) + exPos, 0.0f, 1.0f);
    const double base = v.freq * s.invSr * pMul * s.bendMul * v.exBendMul;

    float o1 = 0.0f, o2 = 0.0f, sd1 = 0.0f, sd2 = 0.0f;
    if (s.lvl1 > 1e-5f) {
        if (s.ct1)
            o1 = oscStackCustom(v.ph1, base * s.m1, *s.ct1, pos1, s.wm1, s.warp1, s.uniN, a.uniDetF, s.uniMaxDet,
                a.uniPanL, a.uniPanR, s.uniNorm, sd1);
        else
            o1 = oscStack(v.ph1, base * s.m1, s.b1, pos1, s.wm1, s.warp1, s.uniN, a.uniDetF, a.uniPanL, a.uniPanR,
                s.uniNorm, sd1);
    }
    if (s.lvl2 > 1e-5f) {
        const double inc2 = base * s.m2 * (s.osc2PitchStatic ? 1.0 : std::exp2(dst[1] * 2.0));
        if (s.ct2)
            o2 = oscStackCustom(v.ph2, inc2, *s.ct2, pos2, s.wm2, s.warp2, s.uniN, a.uniDetF, s.uniMaxDet, a.uniPanL,
                a.uniPanR, s.uniNorm, sd2);
        else
            o2 = oscStack(
                v.ph2, inc2, s.b2, pos2, s.wm2, s.warp2, s.uniN, a.uniDetF, a.uniPanL, a.uniPanR, s.uniNorm, sd2);
    }
    float sub = 0.0f;
    if (s.subLvl > 1e-5f) {
        sub = subOsc(s.subWave, v.subPh);
        v.subPh += base * s.m1 * s.subMul;
        if (v.subPh >= 1.0) v.subPh -= 1.0;
    }
    o1 *= s.lvl1;
    o2 *= s.lvl2;
    sub *= s.subLvl;

    double in1 = 0, in2 = 0, dryS = 0;
    routeAdd(s.r1, o1, in1, in2, dryS);
    routeAdd(s.r2, o2, in1, in2, dryS);
    routeAdd(s.rs, sub, in1, in2, dryS);

    if (v.modCount-- <= 0) {
        v.modCount = 15;
        // Load smoothed filter targets only on control-rate update frames (1/16th of samples).
        const bool useArr = (bw != nullptr && !s.settled);
        const double curF1 = useArr ? bw->f1[bwIdx] : scalarF1;
        const double curF2 = useArr ? bw->f2[bwIdx] : scalarF2;
        const double curR1 = useArr ? bw->r1[bwIdx] : scalarR1;
        const double curR2 = useArr ? bw->r2[bwIdx] : scalarR2;
        const double cutMod = dst[3] * 4.0 + v.ex.pressure() * 2.0, resoMod = dst[4];
        double c1 = s.f1Env * 4.0 * v.env2 + s.f1Key * v.ktOct + s.f1Lfo * 2.0 * lfo1 + cutMod;
        double c2 = s.f2Env * 4.0 * v.env2 + s.f2Key * v.ktOct + s.f2Lfo * 2.0 * lfo2 + cutMod;
        setSvf(v.f[0], std::clamp(curF1 * std::exp2(c1), 20.0, s.maxCutoffHz), resoToK(curR1 + resoMod));
        setSvf(v.f[1], std::clamp(curF2 * std::exp2(c2), 20.0, s.maxCutoffHz), resoToK(curR2 + resoMod));
    }
    double sig =
        filterPair(v.f, v.ic1L, v.ic2L, v.ic1R, v.ic2R, in1, in2, s.series, s.t1type, s.t2type, s.slope1, s.slope2) +
        dryS;
    double side = 0.0;
    if (s.stereoUni) {
        double sIn1 = 0, sIn2 = 0, sDry = 0;
        routeAdd(s.r1, sd1 * s.lvl1, sIn1, sIn2, sDry);
        routeAdd(s.r2, sd2 * s.lvl2, sIn1, sIn2, sDry);
        side = filterPair(
                   v.f, v.sc1L, v.sc2L, v.sc1R, v.sc2R, sIn1, sIn2, s.series, s.t1type, s.t2type, s.slope1, s.slope2) +
               sDry;
    }

    const float amp = v.env * v.velocity * (1.0f + static_cast<float>(dst[5])) * (1.0f + 0.5f * v.ex.pressure());
    const float sigL = static_cast<float>(sig + side) * amp, sigR = static_cast<float>(sig - side) * amp;
    const float pan = static_cast<float>(std::clamp(dst[6], -1.0, 1.0));
    outL = sigL * (pan <= 0 ? 1.0f : 1.0f - pan);
    outR = sigR * (pan >= 0 ? 1.0f : 1.0f + pan);
    return true;
}

NOTA_ALWAYS_INLINE float WavetableSynth::processFxAndMixSample(
    const BlockScalars& s, float mixL, float mixR, float* dstSample) noexcept {
    float l = mixL * 0.4f, r = mixR * 0.4f;
    if (s.driveOn && s.drive > 1e-4f) {
        l = driveSample(s.driveMode, l, s.drive, s.driveInvNorm);
        r = driveSample(s.driveMode, r, s.drive, s.driveInvNorm);
    }
    if (s.driveOn && s.toneCoef != 0.0f) {
        l = tone(toneL_, l, s.toneCoef);
        r = tone(toneR_, r, s.toneCoef);
    }
    if (s.chorusOn && s.chAmt > 1e-4f) {
        float cl, cr;
        chorus(l, r, s.chAmt, s.chVoices, s.chRate, cl, cr);
        l = cl;
        r = cr;
    }
    if (s.reverbOn && s.rvAmt > 1e-4f) {
        float rl, rr;
        reverb(l, r, s.revFb, s.revDamp, rl, rr);
        l += rl * s.rvAmt;
        r += rr * s.rvAmt;
    }

    const float al = (l * s.opL) * s.gain, ar = (r * s.opR) * s.gain;
    dstSample[0] += al;
    dstSample[1] += ar;
    return std::max(std::fabs(al), std::fabs(ar));
}

void WavetableSynth::renderVoiceBatch(const BatchParams& bp, const BatchWork& bw, int32_t chunk, Voice* const* vList,
    int vCount, float* dstL, float* dstR) const {
    const BlockScalars& s = bp.s;
    for (int vi = 0; vi < vCount; ++vi) {
        Voice& v = *vList[vi];
        for (int32_t i = 0; i < chunk; ++i) {
            float sL = 0.0f, sR = 0.0f;
            if (!renderVoiceSample(
                    s, bp.a, v, bw.lfo1[i], bw.lfo2[i], &bw, i, s.f1base, s.f2base, s.reso1, s.reso2, sL, sR))
                break;
            dstL[i] += sL;
            dstR[i] += sR;
        }
    }
}

void WavetableSynth::render(float* out, int32_t frames) {
    SnapshotHistory<const AuroraTableSet>::RenderScope tableScope(tableSet_);
    bool skip = false;
    const uint32_t gateEpoch = gate_.beginBlock(&skip);
    if (skip) return;

    int actN = 0;
    for (const auto& v : voices_)
        if (v.active) ++actN;

    // Keep low-polyphony and high-polyphony DSP in separate noinline frames so SilenceGate
    // and BatchWork state never spill registers from the hot linear sample loop.
    const bool useBatched =
        (actN >= 12 && frames >= 32 && batchWork_ && batchWork_->voiceScratch_.capacityFrames() >= 32);
    const bool keepAwake = useBatched ? renderBatched(out, frames) : renderLinear(out, frames);
    gate_.observe(keepAwake, frames, gateEpoch);
}

bool WavetableSynth::renderLinear(float* out, int32_t frames) {
    BlockArrays a;
    const BlockScalars s = snapshotBlockParams(frames, a);
    double f1base = s.f1base, f2base = s.f2base, reso1 = s.reso1, reso2 = s.reso2;

    float peak = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        if (!s.settled) stepFilterSmoothing(s, f1base, f2base, reso1, reso2);
        double lfo1 = 0.0, lfo2 = 0.0;
        stepLfos(s, lfo1, lfo2);

        float mixL = 0.0f, mixR = 0.0f;
        for (auto& v : voices_) {
            if (!v.active) continue;
            float sL = 0.0f, sR = 0.0f;
            if (renderVoiceSample(s, a, v, lfo1, lfo2, nullptr, 0, f1base, f2base, reso1, reso2, sL, sR)) {
                mixL += sL;
                mixR += sR;
            }
        }

        peak = std::max(peak, processFxAndMixSample(s, mixL, mixR, out + i * 2));
    }

    return publishBlockTelemetry(s, a) || peak > 1.0e-5f;
}

bool WavetableSynth::renderBatched(float* out, int32_t frames) {
    BatchParams bp;
    bp.s = snapshotBlockParams(frames, bp.a);
    const BlockScalars& s = bp.s;
    BatchWork& bw = *batchWork_;
    Voice* act[kVoices];
    int batchedActN = 0;
    for (auto& v : voices_)
        if (v.active) act[batchedActN++] = &v;

    float peak = 0.0f;
    for (int32_t off = 0; off < frames;) {
        const int32_t chunk = std::min<int32_t>(frames - off, kModChunk);
        for (int32_t i = 0; i < chunk; ++i) {
            if (!s.settled) stepFilterSmoothing(s, bw.f1[i], bw.f2[i], bw.r1[i], bw.r2[i]);
            stepLfos(s, bw.lfo1[i], bw.lfo2[i]);
        }

        Voice* chunkAct[kVoices];
        int chunkActN = 0;
        for (int vi = 0; vi < batchedActN; ++vi)
            if (act[vi]->active) chunkAct[chunkActN++] = act[vi];

        std::fill_n(bw.mixL, chunk, 0.0f);
        std::fill_n(bw.mixR, chunk, 0.0f);
        if (chunkActN > 0) {
            if (chunkActN >= 12 && chunk >= 32 && bw.voiceScratch_.capacityFrames() >= chunk) {
                std::array<dsp::PolyVoiceBatchRange, dsp::PolyVoiceScheduler::kMaxBatches> batches{};
                const int32_t batchesN = dsp::PolyVoiceScheduler::planBatches(chunkActN, 4, batches);
                dsp::RenderExecutor::parallelFor(
                    batchesN, true, [this, &bp, &bw, chunk, &chunkAct, &batches](int32_t b) {
                        bw.voiceScratch_.clearBatch(b, chunk);
                        const auto range = batches[static_cast<size_t>(b)];
                        renderVoiceBatch(bp, bw, chunk, chunkAct + range.start, range.count, bw.voiceScratch_.left(b),
                            bw.voiceScratch_.right(b));
                    });
                for (int32_t b = 0; b < batchesN; ++b) {
                    dsp::PolyVoiceScheduler::accumulateMonoAdd(bw.mixL, bw.voiceScratch_.left(b), chunk);
                    dsp::PolyVoiceScheduler::accumulateMonoAdd(bw.mixR, bw.voiceScratch_.right(b), chunk);
                }
            } else {
                renderVoiceBatch(bp, bw, chunk, chunkAct, chunkActN, bw.mixL, bw.mixR);
            }
        }

        float* chunkOut = out + off * 2;
        for (int32_t i = 0; i < chunk; ++i)
            peak = std::max(peak, processFxAndMixSample(s, bw.mixL[i], bw.mixR[i], chunkOut + i * 2));
        off += chunk;
    }

    return publishBlockTelemetry(s, bp.a) || peak > 1.0e-5f;
}

// Keep block setup and telemetry non-inlined so the hot per-sample DSP loops stay small
// enough for GCC's inliner to fold oscStack, filterPair, svf, and warpRead completely.
WavetableSynth::BlockScalars WavetableSynth::snapshotBlockParams(int32_t frames, BlockArrays& a) {
    BlockScalars s{};
    double macroBlk[5] = {0, 0, 0, 0, 0}; // warp · osc2 level · sub level · unison · drive
    for (int m = 0; m < kMacros; ++m) {
        const double x =
            get(static_cast<Param>(macroIdx(m, 0))) * (get(static_cast<Param>(macroIdx(m, 2))) - 0.5) * 2.0;
        if (std::fabs(x) <= 1e-4) continue;
        switch (macroDestOf(get(static_cast<Param>(macroIdx(m, 1))))) {
        case 0: s.mPitch += x; break;     // Pitch
        case 2: s.mOsc2Pitch += x; break; // Osc 2 Pitch
        case 4: s.mPos += x; break;       // Position
        case 6: s.mCut += x; break;       // Cutoff
        case 7: s.mReso += x; break;      // Reso
        case 9: s.mLevel += x; break;     // Level
        case 11: s.mPan += x; break;      // Pan
        case 1: macroBlk[0] += x; break;  // Warp
        case 3: macroBlk[1] += x; break;  // Osc 2 Level
        case 5: macroBlk[2] += x; break;  // Sub Level
        case 8: macroBlk[3] += x; break;  // Unison detune
        default: macroBlk[4] += x; break; // Drive
        }
    }

    // ---- block snapshot ----
    s.b1 = bankOf(Table);
    s.b2 = bankOf(Osc2Table);
    // Imported tables (specs/053): null unless the oscillator holds one and plays it.
    const AuroraTableSet* tset = tableSet_.live();
    s.ct1 = (tset && useCustom_[0].load(std::memory_order_relaxed)) ? tset->slot[0].get() : nullptr;
    s.ct2 = (tset && useCustom_[1].load(std::memory_order_relaxed)) ? tset->slot[1].get() : nullptr;
    s.wm1 = warpModeOf(Osc1WarpMode);
    s.wm2 = warpModeOf(Osc2WarpMode);
    s.basePos1 = get(Position);
    s.basePos2 = get(Osc2Position);
    s.warp1 = std::clamp(get(Warp) + macroBlk[0], 0.0, 1.0);
    s.warp2 = std::clamp(get(Osc2Warp) + macroBlk[0], 0.0, 1.0);
    s.lvl1 = get(Osc1On) > 0.5f ? get(Osc1Level) : 0.0f;
    s.lvl2 = get(Osc2On) > 0.5f ? static_cast<float>(std::clamp(get(Osc2Level) + macroBlk[1], 0.0, 2.0)) : 0.0f;
    s.subLvl = static_cast<float>(std::clamp(get(SubLevel) + macroBlk[2], 0.0, 2.0));
    s.subWave = static_cast<int>(std::lround(get(SubWave) * 2.0f));
    s.m1 = pitchMul(Osc1Oct, Osc1Semi, Osc1Detune);
    s.m2 = pitchMul(Osc2Oct, Osc2Semi, Osc2Detune);
    s.subMul =
        std::exp2(static_cast<double>(std::lround((get(SubOct) - 0.5f) * 4.0f)) - 1.0); // default one octave down
    s.uniN = std::clamp(1 + static_cast<int>(std::lround(get(Unison) * 6.0f)), 1, kUnison);
    const double uniDet = std::clamp(get(UniDetune) + macroBlk[3], 0.0, 1.0) * 50.0; // ± cents
    const float uniSpread = get(UniSpread);
    for (int u = 0; u < s.uniN; ++u) {
        const double d = (s.uniN > 1) ? (static_cast<double>(u) / (s.uniN - 1) - 0.5) * 2.0 : 0.0;
        a.uniDetF[u] = std::exp2(d * uniDet / 1200.0);
        const float pan = static_cast<float>(d) * uniSpread;
        const float pan01 = (pan + 1.0f) * 0.125f;
        a.uniPanL[u] = fast_math::cos(pan01);
        a.uniPanR[u] = fast_math::sin(pan01);
    }
    s.uniNorm = 1.41421356f / std::sqrt(static_cast<float>(s.uniN));
    s.uniMaxDet = 1.0;
    for (int u = 0; u < s.uniN; ++u) s.uniMaxDet = std::max(s.uniMaxDet, a.uniDetF[u]);
    s.stereoUni = s.uniN > 1 && uniSpread > 1e-6f;
    if (!s.stereoUni)
        for (auto& v : voices_) v.clearSide();

    s.atk = rateOf(Attack, 0.001, 2.0);
    s.dec = rateOf(Decay, 0.002, 3.0);
    s.rel = rateOf(Release, 0.002, 4.0);
    s.sus = get(Sustain);
    s.atk2 = rateOf(Env2Attack, 0.001, 2.0);
    s.dec2 = rateOf(Env2Decay, 0.002, 3.0);
    s.rel2 = rateOf(Env2Release, 0.002, 4.0);
    s.sus2 = get(Env2Sustain);
    s.bend1 = envBends({get(DCurve), get(RCurve), get(EnvExp)});
    s.bend2 = envBends({get(E2DCurve), get(E2RCurve), get(E2EnvExp)});

    s.t1type = filtType(Fil1Type);
    s.t2type = filtType(Fil2Type);
    s.slope1 = get(Fil1Slope) > 0.5f;
    s.slope2 = get(Fil2Slope) > 0.5f;
    s.f1tgt = std::log2(expMap(get(Cutoff), 20.0, 18000.0));
    s.f2tgt = std::log2(expMap(get(Fil2Freq), 20.0, 18000.0));
    s.r1tgt = get(Resonance);
    s.r2tgt = get(Fil2Reso);
    if (f1Sm_ < -1e8) {
        f1Sm_ = s.f1tgt;
        f2Sm_ = s.f2tgt;
        r1Sm_ = s.r1tgt;
        r2Sm_ = s.r2tgt;
    }
    s.fSm = 1.0 - std::exp(-1.0 / (0.005 * sampleRate_));
    s.f1Env = bip(Fil1Env);
    s.f1Lfo = bip(Fil1Lfo);
    s.f1Key = get(Fil1Key);
    s.f2Env = bip(Fil2Env);
    s.f2Lfo = bip(Fil2Lfo);
    s.f2Key = get(Fil2Key);
    s.r1 = routeOf(RouteOsc1);
    s.r2 = routeOf(RouteOsc2);
    s.rs = routeOf(RouteSub);
    s.series = get(FilSeries) > 0.5f;

    s.ls1 = shapeOf(Lfo1Shape);
    s.ls2 = shapeOf(Lfo2Shape);
    s.lfo1Inc = lfoInc(Lfo1Rate, Lfo1Sync);
    s.lfo2Inc = lfoInc(Lfo2Rate, Lfo2Sync);
    const double sliceBeat = spb_ > 0.0 ? beat_ + static_cast<double>(sinceBlock_) / spb_ : beat_;
    sinceBlock_ += frames;
    if (playing_ && spb_ > 0.0) {
        auto lock = [&](double& ph, float& sh, double cycleBeats) {
            if (cycleBeats <= 0.0) return;
            const double c = sliceBeat / cycleBeats;
            const double p = c - std::floor(c);
            if (p + 0.5 < ph) sh = frand() * 2.0f - 1.0f; // crossed a cycle start: new S&H step
            ph = p;
        };
        lock(lfoPhase1_, lfoSh1_, syncBeats(Lfo1Sync));
        lock(lfoPhase2_, lfoSh2_, syncBeats(Lfo2Sync));
    }
    s.lfo1Depth = get(Lfo1Depth);
    s.lfo2Depth = get(Lfo2Depth);
    s.gain = get(Gain);
    s.modWheel = get(ModWheel);

    s.bendMul = std::exp2((get(Bend) - 0.5) * 2.0 * wheelRange() / 12.0);
    gexpr_.offsets(wheelRange(), a.exAdd);
    s.exprCoef = exprCoef_;
    s.maxCutoffHz = sampleRate_ * 0.49;

    const float outPan = (get(OutPan) - 0.5f) * 2.0f;
    s.opL = std::cos((outPan + 1.0f) * 0.25f * static_cast<float>(kPi));
    s.opR = std::sin((outPan + 1.0f) * 0.25f * static_cast<float>(kPi));

    s.driveOn = get(FxDriveOn) > 0.5f;
    s.chorusOn = get(FxChorusOn) > 0.5f;
    s.reverbOn = get(FxReverbOn) > 0.5f;
    s.drive = static_cast<float>(std::clamp(get(FxDrive) + macroBlk[4], 0.0, 1.0));
    s.driveMode = std::clamp(static_cast<int>(std::lround(get(FxDriveMode) * 2.0f)), 0, 2);
    const float driveD = 1.0f + s.drive * 8.0f;
    s.driveInvNorm = 1.0f / fast_math::tanhPrecise(driveD * 0.7f + 1.0f);
    s.toneCoef = static_cast<float>(std::clamp((get(FxTone) - 0.5) * 2.0, -1.0, 1.0));
    s.chAmt = get(FxChorus);
    s.chRate = expMap(get(FxChorusRate), 0.1, 6.0);
    s.chVoices = 1 << std::clamp(static_cast<int>(std::lround(get(FxChorusVoices) * 2.0f)), 0, 2);
    s.rvAmt = get(FxReverb);
    const int revMode = std::clamp(static_cast<int>(std::lround(get(FxReverbMode) * 2.0f)), 0, 2);
    static const float fbBase[3] = {0.76f, 0.86f, 0.83f}, dampBase[3] = {0.45f, 0.72f, 0.18f};
    s.revFb = std::clamp(fbBase[revMode] + (get(FxReverbSize) - 0.5f) * 0.16f, 0.5f, 0.96f);
    s.revDamp = dampBase[revMode];

    s.amN = 0;
    s.pitchStatic = s.mPitch == 0.0;
    s.osc2PitchStatic = s.mOsc2Pitch == 0.0;
    for (int src = 0; src < kSources; ++src)
        for (int dst = 0; dst < kDests; ++dst) {
            double amt = (get(static_cast<Param>(matrixIdx(src, dst))) - 0.5) * 2.0;
            if (std::fabs(amt) > 1e-4) {
                a.am[s.amN++] = {static_cast<uint8_t>(src), static_cast<uint8_t>(dst), amt};
                if (dst == 0) s.pitchStatic = false;
                if (dst == 1) s.osc2PitchStatic = false;
            }
        }

    s.invSr = 1.0 / sampleRate_;
    s.settled = std::fabs(s.f1tgt - f1Sm_) < 1e-7 && std::fabs(s.f2tgt - f2Sm_) < 1e-7 &&
                std::fabs(s.r1tgt - r1Sm_) < 1e-7 && std::fabs(s.r2tgt - r2Sm_) < 1e-7;
    if (s.settled) {
        f1Sm_ = s.f1tgt;
        f2Sm_ = s.f2tgt;
        r1Sm_ = s.r1tgt;
        r2Sm_ = s.r2tgt;
    }
    s.f1base = std::exp2(f1Sm_);
    s.f2base = std::exp2(f2Sm_);
    s.reso1 = r1Sm_;
    s.reso2 = r2Sm_;
    return s;
}

bool WavetableSynth::publishBlockTelemetry(const BlockScalars& s, const BlockArrays& a) noexcept {
    int live = 0;
    const Voice* newest = nullptr;
    for (const auto& v : voices_)
        if (v.active) {
            ++live;
            if (!newest || v.seq > newest->seq) newest = &v;
        }
    activeVoices_.store(live, std::memory_order_relaxed);
    float follow[2] = {-1.0f, -1.0f};
    if (newest) {
        double d = s.mPos;
        if (s.amN) {
            auto wrap = [](double ph) { return ph >= 1.0 ? ph - 1.0 : ph; };
            const double lfo1 = lfoValue(s.ls1, wrap(lfoPhase1_), lfoSh1_) * s.lfo1Depth;
            const double lfo2 = lfoValue(s.ls2, wrap(lfoPhase2_), lfoSh2_) * s.lfo2Depth;
            const double src[kSources] = {newest->env, newest->env2, lfo1, lfo2, newest->velocity,
                std::clamp(newest->ktOct / 4.0, -1.0, 1.0), s.modWheel, newest->rand};
            for (int k = 0; k < s.amN; ++k)
                if (a.am[k].dst == 2) d += src[a.am[k].src] * a.am[k].amt;
        }
        const float off = static_cast<float>(d) + newest->ex.slide() * 0.5f;
        follow[0] = std::clamp(s.basePos1 + off, 0.0f, 1.0f);
        follow[1] = std::clamp(s.basePos2 + off, 0.0f, 1.0f);
    }
    scope_[0].store(follow[0], std::memory_order_relaxed);
    scope_[1].store(follow[1], std::memory_order_relaxed);
    scope_[4].store(static_cast<float>(live), std::memory_order_relaxed);
    Voice* lv = lastVoice_ >= 0 ? &voices_[lastVoice_] : nullptr;
    const bool on = lv && lv->active;
    publishEnvTele(envTele_, on, on ? lv->stage : Stage::Off, on ? &lv->shape : nullptr, on ? lv->env : 0.0f, s.sus);
    publishEnvTele(envTele_ + kEnvTeleFloats, on, on ? lv->stage2 : Stage::Off, on ? &lv->shape2 : nullptr,
        on ? lv->env2 : 0.0f, s.sus2);
    return live > 0;
}

float WavetableSynth::harmAmp(int bank, float t, int n) {
    switch (bank) {
    case 0: return n == 1 ? 1.0f : t * (1.0f / static_cast<float>(n));
    case 1: return (n % 2 == 1) ? (n == 1 ? 1.0f : t * (1.0f / static_cast<float>(n))) : 0.0f;
    case 2: {
        float peak = 1.0f + t * 16.0f, w = 2.0f + t * 3.0f, x = (static_cast<float>(n) - peak) / w;
        return std::exp(-x * x) + (n == 1 ? 0.15f : 0.0f);
    }
    default:
        return (1.0f / static_cast<float>(n)) *
               std::fabs(std::cos(static_cast<float>(kPi) * static_cast<float>(n) * (0.25f + 0.75f * t)));
    }
}

const float* WavetableSynth::sharedTables() {
    static const std::vector<float> t = [] {
        std::vector<float> tab(static_cast<size_t>(kBanks) * kFrames * kTableSize, 0.0f);
        const int nMax = std::min(kTableSize / 2, 64);
        for (int b = 0; b < kBanks; ++b)
            for (int f = 0; f < kFrames; ++f) {
                const float tt = static_cast<float>(f) / (kFrames - 1);
                float* p = &tab[static_cast<size_t>(((b * kFrames) + f) * kTableSize)];
                float peak = 1e-6f;
                // The recipe is fixed per (bank, frame): evaluate it once, not per sample.
                float amp[65];
                for (int n = 1; n <= nMax; ++n) amp[n] = harmAmp(b, tt, n);
                for (int j = 0; j < kTableSize; ++j) {
                    const double ph = static_cast<double>(j) / kTableSize;
                    double acc = 0;
                    for (int n = 1; n <= nMax; ++n)
                        if (amp[n] > 1e-5f) acc += amp[n] * std::sin(2.0 * kPi * n * ph);
                    p[j] = static_cast<float>(acc);
                    peak = std::max(peak, std::fabs(p[j]));
                }
                const float inv = 1.0f / peak;
                for (int j = 0; j < kTableSize; ++j) p[j] *= inv;
            }
        return tab;
    }();
    return t.data();
}

void WavetableSynth::chorus(float l, float r, float amt, int voices, double rateHz, float& outL, float& outR) {
    const int sz = static_cast<int>(chorusBuf_.size());
    if (sz < 4) {
        outL = l;
        outR = r;
        return;
    }
    chorusBuf_[static_cast<size_t>(chorusW_)] = (l + r) * 0.5f;
    chorusPh_ += rateHz / sampleRate_;
    if (chorusPh_ >= 1.0) chorusPh_ -= 1.0;
    // Delay-time LFO — REVERTED to std:: (measured ≈ −51 dB with fast_math::sin: a
    // 0.2-sample tap error on full-band wavetable content is audible in the null).
    auto tap = [&](double phase) {
        double d = (0.008 + 0.006 * std::sin(kTwoPi * (phase - std::floor(phase)))) * sampleRate_;
        double rp = chorusW_ - d;
        while (rp < 0) rp += sz;
        int i0 = static_cast<int>(rp);
        float f = static_cast<float>(rp - i0);
        if (i0 >= sz) i0 -= sz;
        int i1 = (i0 + 1) % sz;
        return chorusBuf_[static_cast<size_t>(i0)] +
               (chorusBuf_[static_cast<size_t>(i1)] - chorusBuf_[static_cast<size_t>(i0)]) * f;
    };
    float a = 0.0f, b = 0.0f;
    // Taps sit evenly around the LFO cycle; odd ones go left, even ones right.
    for (int k = 0; k < voices; ++k) {
        float t = tap(chorusPh_ + static_cast<double>(k) / voices);
        if (k % 2 == 0) a += t;
        else
            b += t;
    }
    const float na = 1.0f / static_cast<float>(std::max(1, (voices + 1) / 2)),
                nb = voices > 1 ? 1.0f / (static_cast<float>(voices / 2)) : 1.0f;
    a *= na;
    b = voices > 1 ? b * nb : a;
    if (++chorusW_ >= sz) chorusW_ = 0;
    outL = l * (1 - amt * 0.5f) + a * amt;
    outR = r * (1 - amt * 0.5f) + b * amt;
}

void WavetableSynth::reverb(float l, float r, float fb, float damp, float& outL, float& outR) {
    const float in = (l + r) * 0.015f;
    float o = 0.0f;
    for (auto& c : revC_) {
        float out = c.buf[static_cast<size_t>(c.idx)];
        c.store = out * (1.0f - damp) + c.store * damp;
        c.buf[static_cast<size_t>(c.idx)] = in + c.store * fb;
        if (++c.idx >= static_cast<int>(c.buf.size())) c.idx = 0;
        o += out;
    }
    for (auto& a : revA_) {
        float bo = a.buf[static_cast<size_t>(a.idx)];
        float out = -o + bo;
        a.buf[static_cast<size_t>(a.idx)] = o + bo * 0.5f;
        if (++a.idx >= static_cast<int>(a.buf.size())) a.idx = 0;
        o = out;
    }
    outL = o;
    outR = o;
}

} // namespace nota
