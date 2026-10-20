// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Auto Filter implementation. The SVF core
// runs through shared dsp::SvfFilter (all-taps form, host-side warping kept
// bit-close), saturation and the LFO sine use FastMath, the scope ring index
// is block-decimated, and silent stretches auto-sleep via isIdle().

#include "AutoFilter.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {
namespace {

// Block-rate ballistics keep std::exp (exact envelopes; ~5 calls/block is
// noise next to the per-sample loop). FastMath covers the per-sample path
// (tanh/tanhPrecise/sin) and all pitch/dB maps (log2f/exp2f/dbToLin).

} // namespace

AutoFilter::AutoFilter() {
    p_[Freq].store(0.55f); // ~760 Hz
    p_[Res].store(0.1f);
    p_[Type].store(0.0f);  // LP
    p_[Slope].store(0.0f); // 12 dB
    p_[Morph].store(0.0f);
    p_[EnvAmt].store(0.5f); // 0 (bipolar)
    p_[EnvAtt].store(0.15f);
    p_[EnvRel].store(0.45f);
    p_[EnvHold].store(0.0f);
    p_[LfoAmt].store(0.0f);
    p_[LfoRate].store(0.4f);
    p_[LfoWave].store(0.0f);
    p_[LfoMorph].store(0.0f);
    p_[Drive].store(0.0f);
    p_[DryWet].store(1.0f);
    p_[EnvOn].store(1.0f);
    p_[LfoOn].store(1.0f);
    p_[Gain].store(0.5f);        // 0 dB
    p_[Circuit].store(0.0f);     // Clean
    p_[LfoSync].store(0.0f);     // Free
    p_[LfoPhase].store(0.0f);    // 0° (mono)
    p_[ModTarget].store(0.0f);   // Freq
    p_[EnvHoldTime].store(1.0f); // ∞ — what Hold did before it had a length
    p_[ModSmooth].store(0.0f);
    p_[LfoOffset].store(0.0f);
    p_[LfoRetrig].store(0.0f);
}

void AutoFilter::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    for (auto& s : svf_) s.reset();
    env_ = 0.0f;
    lfoPhase_ = 0.0;
    shHeld_ = 0.0f;
    shCycle_ = -1.0;
    smOct_ = smRes_ = 0.0;
    holdLeft_ = 0;
    armed_ = true;
    primed_ = false;
    histAcc_ = 0.0;
    histEnvMax_ = 0.0f;
    quietSamples_ = 0;
    idle_ = false;
    srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
}

void AutoFilter::setTransport(double beatStart, double spb, bool playing) {
    if (playing && !playing_) playStarted_ = true; // Retrig restarts the LFO at play
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

void AutoFilter::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

double AutoFilter::expMap(float v, double lo, double hi) {
    return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
}

std::string AutoFilter::hzText(double hz) {
    char b[32];
    if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.2f kHz", hz / 1000.0);
    else
        std::snprintf(b, sizeof b, "%.0f Hz", hz);
    return b;
}

std::string AutoFilter::holdText() const {
    if (getParam(EnvHold) < 0.5f) return {};
    const float v = getParam(EnvHoldTime);
    if (v >= 0.995f) return ", hold inf";
    char b[32];
    std::snprintf(b, sizeof b, ", hold %.0f ms", 400.0f * v * v);
    return b;
}

float AutoFilter::filterOne(int ch, float xin, float drive, float driveGain, float driveComp, int type, float morph,
    float wet, float outGain, bool analog) {
    double x = xin;
    if (drive > 0.0001f) x = fast_math::tanhPrecise((float)(x * driveGain)) * driveComp;

    float lp, bp, hp, notch;
    svf_[ch * 2].processAll((float)x, lp, bp, hp, notch);
    double outs1[4] = {lp, bp, hp, notch};
    if (slope24cache_) {
        float lp2, bp2, hp2, notch2;
        svf_[ch * 2 + 1].processAll((float)outs1[type], lp2, bp2, hp2, notch2);
        outs1[0] = lp2;
        outs1[1] = bp2;
        outs1[2] = hp2;
        outs1[3] = notch2;
    }
    double y = outs1[type];
    if (morph > 0.0f) y = y * (1.0 - morph) + outs1[(type + 1) & 3] * morph; // blend toward next type
    if (analog) y = fast_math::tanhPrecise((float)(y * 1.3)) / 0.86172f;     // Analog: gentle saturation
    return (float)((xin * (1.0f - wet) + y * wet) * outGain);
}

double AutoFilter::lfoValue(int wave, float morph, double ph) const {
    switch (wave) {
    case 0: return std::sin(2.0 * 3.14159265358979 * ph);          // sine
    case 1: return 4.0 * std::fabs(ph - 0.5) - 1.0;                // triangle
    case 2: return 2.0 * ph - 1.0;                                 // saw
    case 3: return ph < (0.5 + (morph - 0.0) * 0.49) ? 1.0 : -1.0; // square (PWM via morph)
    default: return shHeld_;                                       // sample & hold
    }
}

int32_t AutoFilter::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    t[S_Cut] = cutA_.load(std::memory_order_relaxed);
    t[S_CutR] = cutRA_.load(std::memory_order_relaxed);
    t[S_Res] = resA_.load(std::memory_order_relaxed);
    t[S_Env] = envA_.load(std::memory_order_relaxed);
    t[S_Lfo] = lfoA_.load(std::memory_order_relaxed);
    t[S_LfoPhase] = lfoPhA_.load(std::memory_order_relaxed);
    t[S_InPk] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPk] = outPkA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    t[S_Onsets] = static_cast<float>(onsets_.load(std::memory_order_relaxed));
    t[S_EnvHeld] = heldA_.load(std::memory_order_relaxed);
    t[S_HistLen] = static_cast<float>(kHist);
    t[S_HistMs] = 1.0f;
    int32_t n = 0;
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    const uint32_t hw = histW_.load(std::memory_order_relaxed);
    for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = histEnv_[(hw + (uint32_t)k) & (kHist - 1)];
    for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = histCut_[(hw + (uint32_t)k) & (kHist - 1)];
    const uint32_t w = scopeW_.load(std::memory_order_relaxed);
    for (int k = 0; k < kScope && n < maxSamples; ++k) out[n++] = scope_[(w + (uint32_t)k) & (kScope - 1)];
    return n;
}

void AutoFilter::deviceAction(int32_t id, int32_t, float) {
    if (id == A_RetrigLfo) retrigReq_.store(true, std::memory_order_relaxed);
    else if (id == A_ResetEnv)
        envResetReq_.store(true, std::memory_order_relaxed);
}

void AutoFilter::controlChunk(CtlOut& o, double beat, bool sync, bool& doRetrig, double envBip, double offset,
    double stPhase, int lfoWave, float lfoMorph, double divBeats, bool retrig, double lfoDep, double smCoef,
    double baseOct, double resBase, double glide, bool toFreq, bool toRes, double nyq) {
    if (doRetrig) {
        doRetrig = false;
        lfoPhase_ = 0.0;
        anchorBeat_ = sync ? beat : 0.0;
        shCycle_ = -1.0;
    }

    // --- control-rate modulation: LFO phase (free or tempo-locked), per-channel cutoff ---
    double phase;
    if (sync) {
        const double rel = (beat - (retrig ? anchorBeat_ : 0.0)) / divBeats;
        phase = rel - std::floor(rel);
        const double cyc = std::floor(rel);
        if (cyc != shCycle_) {
            shCycle_ = cyc;
            shHeld_ = whiteNoise();
        } // S&H reseed per cycle
    } else {
        phase = lfoPhase_;
    }
    phase += offset;
    phase -= std::floor(phase);

    const double lfoL = lfoValue(lfoWave, lfoMorph, phase);
    double pr = phase + stPhase;
    pr -= std::floor(pr);
    const double lfoR = stPhase > 1e-4 ? lfoValue(lfoWave, lfoMorph, pr) : lfoL;

    // Modulation in "depth units": env ±1 · amount, LFO ±1 · amount — then smoothed.
    const double envMod = envBip * (double)env_;
    const double rawL = envMod * 6.0 + lfoDep * 4.0 * lfoL; // octaves (Freq)
    const double rawR = envMod * 6.0 + lfoDep * 4.0 * lfoR;
    const double rawRes = envMod * 0.8 + lfoDep * 0.5 * lfoL; // resonance offset
    smOct_ = rawL + smCoef * (smOct_ - rawL);
    smOctR_ = rawR + smCoef * (smOctR_ - rawR);
    smRes_ = rawRes + smCoef * (smRes_ - rawRes);

    // The knobs glide toward their targets (snapping once there, so a still knob is exact).
    glOct_ = settle(glOct_ + (baseOct - glOct_) * glide, baseOct, 1e-5);
    glRes_ = settle(glRes_ + (resBase - glRes_) * glide, resBase, 1e-6);

    o.phase = phase;
    o.lfoL = lfoL;
    o.lfoR = lfoR;
    o.fcL = std::clamp(std::exp2(glOct_ + (toFreq ? smOct_ : 0.0)), 20.0, nyq);
    o.fcR = std::clamp(std::exp2(glOct_ + (toFreq ? smOctR_ : 0.0)), 20.0, nyq);
    o.resN = std::clamp(glRes_ + (toRes ? smRes_ : 0.0), 0.0, 1.0);
}

void AutoFilter::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (frames <= 0) return;
    const double baseHz = expMap(get(Freq), 30.0, 18000.0);
    const double resBase = std::clamp(get(Res), 0.0f, 1.0f);
    const int type = std::clamp((int)std::lround(get(Type) * 3.0f), 0, 3);
    const bool slope24 = get(Slope) >= 0.5f;
    slope24cache_ = slope24;
    const double morph = std::clamp(get(Morph), 0.0f, 1.0f);
    const bool envOn = get(EnvOn) >= 0.5f;
    const double envBip = envOn ? ((double)get(EnvAmt) - 0.5) * 2.0 : 0.0; // −1..1
    const float attMs = (float)expMap(get(EnvAtt), 0.1, 500.0);
    const float relMs = (float)expMap(get(EnvRel), 1.0, 2000.0);
    const bool hold = get(EnvHold) >= 0.5f;
    const float holdV = std::clamp(get(EnvHoldTime), 0.0f, 1.0f);
    const bool holdInf = holdV >= 0.995f;
    const int64_t holdN = (int64_t)(400.0 * holdV * holdV * 0.001 * sr_);
    const bool lfoOn = get(LfoOn) >= 0.5f;
    const double lfoDep = lfoOn ? (double)std::clamp(get(LfoAmt), 0.0f, 1.0f) : 0.0; // 0..1
    const double lfoHz = expMap(get(LfoRate), 0.01, 40.0);
    const int lfoWave = std::clamp((int)std::lround(get(LfoWave) * 4.0f), 0, 4);
    const float lfoMorph = std::clamp(get(LfoMorph), 0.0f, 1.0f);
    const float drive = std::clamp(get(Drive), 0.0f, 1.0f);
    const float wet = std::clamp(get(DryWet), 0.0f, 1.0f);
    const float outGain = fast_math::dbToLin((get(Gain) - 0.5f) * 48.0f); // ±24 dB
    const bool analog = get(Circuit) >= 0.5f;                             // mild saturation
    const int target = std::clamp((int)std::lround(get(ModTarget) * 2.0f), 0, 2);
    const bool toFreq = target != 1, toRes = target != 0;
    const double smMs = 120.0 * (double)get(ModSmooth) * (double)get(ModSmooth);
    const double smCoef = smMs < 0.05 ? 0.0 : std::exp(-(double)kCtrl / (smMs * 0.001 * sr_));
    const double offset = (double)std::clamp(get(LfoOffset), 0.0f, 1.0f);
    const double baseOct = std::log2(baseHz);
    const double glide = 1.0 - std::exp(-(double)kCtrl / (kGlideS * sr_)); // per control block
    const double glideS = 1.0 - std::exp(-1.0 / (kGlideS * sr_));          // per sample (Morph)
    if (!primed_) {
        glOct_ = baseOct;
        glRes_ = resBase;
        glMorph_ = morph;
        primed_ = true;
    }
    const bool retrig = get(LfoRetrig) >= 0.5f;

    // Per-sample envelope coefficients (one-pole).
    const float aCoef = attMs <= 0.0f ? 0.0f : (float)std::exp(-1.0 / (attMs * 0.001 * sr_));
    const float rCoef = relMs <= 0.0f ? 0.0f : (float)std::exp(-1.0 / (relMs * 0.001 * sr_));
    const float scGainLin = fast_math::dbToLin(sidechainGainDb());
    const float driveGain = 1.0f + drive * drive * 24.0f; // up to ~+28 dB into the shaper
    const float driveComp = 1.0f / (1.0f + drive * 1.5f); // loudness makeup
    const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;

    const double phaseInc = lfoHz / sr_;
    const bool sync = get(LfoSync) >= 0.5f && spb_ > 0.0;
    const double divBeats = kLfoDiv[std::clamp((int)std::lround(get(LfoRate) * 7.0f), 0, 7)];
    const double stPhase = (double)std::clamp(get(LfoPhase), 0.0f, 1.0f) * 0.5; // 0..0.5 cycle (0..180°)
    const double nyq = std::min(20000.0, sr_ * 0.45);
    const double histStep = sr_ * 0.001; // 1 ms per history point

    if (envResetReq_.exchange(false, std::memory_order_relaxed)) {
        env_ = 0.0f;
        holdLeft_ = 0;
    }
    bool doRetrig = retrigReq_.exchange(false, std::memory_order_relaxed);
    if (playStarted_) {
        playStarted_ = false;
        if (retrig) doRetrig = true;
    }

    float inPk = 0.0f, outPk = 0.0f;
    double lastLfoL = 0.0, lastPhase = 0.0, fcLast = baseHz, fcRLast = baseHz, resLast = resBase;
    bool held = false;

    // Idle skip: silent input on settled filters leaves the buffer untouched.
    // Sleep only when the sidechain is silent too — otherwise the frozen
    // envelope would wake up stale against a live key signal. Modulation
    // (LFO phase, smoothed cutoff, envelope decay, histories) keeps running
    // through the shared controlChunk so the wake-up transient matches
    // continuous rendering; only the per-sample filter loop is skipped.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    const bool keySilent = sc == nullptr || simd_audio::isSilent(sc, frames * 2);
    if (inSilent && keySilent && idle_) {
        scBuf_ = nullptr; // consume once
        // Closed-form follower decay (det = 0: silent main, silent key).
        if (hold && (holdInf || holdLeft_ > 0)) {
            if (!holdInf) holdLeft_ = holdLeft_ > frames ? holdLeft_ - frames : 0;
            held = true;
        } else {
            env_ = (float)(env_ * std::pow((double)rCoef, (double)frames));
        }
        if (armed_ && env_ >= kOnsetOn) {
            armed_ = false;
            onsets_.fetch_add(1, std::memory_order_relaxed);
            if (retrig) doRetrig = true;
        } else if (!armed_ && env_ < kOnsetOff) {
            armed_ = true;
        }
        int32_t j = 0;
        while (j < frames) {
            const int32_t block = std::min(kCtrl, frames - j);
            const double beat = sync ? beatStart_ + (double)j / spb_ : 0.0;
            CtlOut co;
            controlChunk(co, beat, sync, doRetrig, envBip, offset, stPhase, lfoWave, lfoMorph, divBeats, retrig, lfoDep,
                smCoef, baseOct, resBase, glide, toFreq, toRes, nyq);
            lastLfoL = co.lfoL;
            lastPhase = co.phase;
            fcLast = co.fcL;
            fcRLast = co.fcR;
            resLast = co.resN;
            histEnvMax_ = std::max(histEnvMax_, env_);
            histAcc_ += block;
            while (histAcc_ >= histStep) {
                histAcc_ -= histStep;
                const uint32_t hw = histW_.load(std::memory_order_relaxed);
                histEnv_[hw & (kHist - 1)] = histEnvMax_;
                histCut_[hw & (kHist - 1)] =
                    (float)std::clamp(std::log(co.fcL / 30.0) / std::log(18000.0 / 30.0), 0.0, 1.0);
                histW_.store(hw + 1, std::memory_order_relaxed);
                histEnvMax_ = env_;
            }
            if (!sync) {
                lfoPhase_ += phaseInc * block;
                if (lfoPhase_ >= 1.0) {
                    lfoPhase_ -= std::floor(lfoPhase_);
                    shHeld_ = whiteNoise();
                }
            }
            j += block;
        }
        uint32_t sws = scopeW_.load(std::memory_order_relaxed);
        for (int32_t s = 0; s < frames; ++s) scope_[(sws++) & (kScope - 1)] = 0.0f;
        scopeW_.store(sws, std::memory_order_relaxed);
        publishTelemetry(t0, fcLast, fcRLast, resLast, lastLfoL, lastPhase, held, lfoOn, 0.0f, 0.0f, frames, sr_);
        return;
    }

    uint32_t sw = scopeW_.load(std::memory_order_relaxed); // block-decimated ring index
    int32_t i = 0;
    while (i < frames) {
        const int32_t block = std::min(kCtrl, frames - i);
        const double beat = sync ? beatStart_ + (double)i / spb_ : 0.0;

        CtlOut co;
        controlChunk(co, beat, sync, doRetrig, envBip, offset, stPhase, lfoWave, lfoMorph, divBeats, retrig, lfoDep,
            smCoef, baseOct, resBase, glide, toFreq, toRes, nyq);
        const double k = 1.0 / (0.5 + co.resN * 14.5);
        const double gL = std::tan(3.14159265358979 * co.fcL / sr_);
        const double gR = std::tan(3.14159265358979 * co.fcR / sr_);
        svf_[0].setCoeffs(1.0 / (1.0 + gL * (gL + k)), gL / (1.0 + gL * (gL + k)), gL * gL / (1.0 + gL * (gL + k)), k);
        svf_[1].setCoeffs(1.0 / (1.0 + gL * (gL + k)), gL / (1.0 + gL * (gL + k)), gL * gL / (1.0 + gL * (gL + k)), k);
        svf_[2].setCoeffs(1.0 / (1.0 + gR * (gR + k)), gR / (1.0 + gR * (gR + k)), gR * gR / (1.0 + gR * (gR + k)), k);
        svf_[3].setCoeffs(1.0 / (1.0 + gR * (gR + k)), gR / (1.0 + gR * (gR + k)), gR * gR / (1.0 + gR * (gR + k)), k);
        lastLfoL = co.lfoL;
        lastPhase = co.phase;
        fcLast = co.fcL;
        fcRLast = co.fcR;
        resLast = co.resN;

        for (int32_t n = 0; n < block; ++n, ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(l), std::fabs(r)));

            // Publish the pre-filter mono signal for the UI spectrum analyzer.
            scope_[(sw++) & (kScope - 1)] = 0.5f * (l + r);

            // Envelope detector: sidechain source if routed, else this track.
            const float det = sc ? scGainLin * std::max(std::fabs(sc[i * 2]), std::fabs(sc[i * 2 + 1]))
                                 : std::max(std::fabs(l), std::fabs(r));
            if (det > env_) {
                env_ = det + aCoef * (env_ - det);
                holdLeft_ = holdN;
            } else if (hold && (holdInf || holdLeft_ > 0)) {
                if (holdLeft_ > 0) --holdLeft_; // hold = keep the peak a while
                held = true;
            } else
                env_ = det + rCoef * (env_ - det);
            if (env_ > 1.0f) env_ = 1.0f;

            glMorph_ = settle(glMorph_ + (morph - glMorph_) * glideS, morph, 1e-6); // a crossfade: glide per sample
            const float morphNow = (float)glMorph_;
            buf[i * 2] = filterOne(0, l, drive, driveGain, driveComp, type, morphNow, wet, outGain, analog);
            buf[i * 2 + 1] = filterOne(1, r, drive, driveGain, driveComp, type, morphNow, wet, outGain, analog);
            outPk = std::max(outPk, std::max(std::fabs(buf[i * 2]), std::fabs(buf[i * 2 + 1])));
        }

        // Onsets: the follower rising through −24 dBFS (re-arms under −30). Retrig restarts the LFO.
        if (armed_ && env_ >= kOnsetOn) {
            armed_ = false;
            onsets_.fetch_add(1, std::memory_order_relaxed);
            if (retrig) doRetrig = true;
        } else if (!armed_ && env_ < kOnsetOff)
            armed_ = true;

        // History: 1 ms per point — the follower's peak over the point, and the cutoff.
        histEnvMax_ = std::max(histEnvMax_, env_);
        histAcc_ += block;
        while (histAcc_ >= histStep) {
            histAcc_ -= histStep;
            const uint32_t hw = histW_.load(std::memory_order_relaxed);
            histEnv_[hw & (kHist - 1)] = histEnvMax_;
            histCut_[hw & (kHist - 1)] =
                (float)std::clamp(std::log(co.fcL / 30.0) / std::log(18000.0 / 30.0), 0.0, 1.0);
            histW_.store(hw + 1, std::memory_order_relaxed);
            histEnvMax_ = env_;
        }

        if (!sync) {
            lfoPhase_ += phaseInc * block;
            if (lfoPhase_ >= 1.0) {
                lfoPhase_ -= std::floor(lfoPhase_);
                shHeld_ = whiteNoise();
            }
        }
    }
    scopeW_.store(sw, std::memory_order_relaxed); // single block-rate publish (was per-sample)
    scBuf_ = nullptr;                             // consume once

    if (inSilent && outPk <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else
        quietSamples_ = 0;
    idle_ = quietSamples_ > kIdleSamples;

    // ---- telemetry -----------------------------------------------------------
    publishTelemetry(t0, fcLast, fcRLast, resLast, lastLfoL, lastPhase, held, lfoOn, inPk, outPk, frames, sr_);
}

void AutoFilter::publishTelemetry(std::chrono::steady_clock::time_point t0, double fcLast, double fcRLast,
    double resLast, double lastLfoL, double lastPhase, bool held, bool lfoOn, float inPk, float outPk, int32_t frames,
    double sr) {
    auto norm = [](double hz) { return (float)std::clamp(std::log(hz / 30.0) / std::log(18000.0 / 30.0), 0.0, 1.0); };
    cutA_.store(norm(fcLast), std::memory_order_relaxed);
    cutRA_.store(norm(fcRLast), std::memory_order_relaxed);
    resA_.store((float)resLast, std::memory_order_relaxed);
    envA_.store(env_, std::memory_order_relaxed);
    lfoA_.store(lfoOn ? (float)lastLfoL : 0.0f, std::memory_order_relaxed);
    lfoPhA_.store((float)lastPhase, std::memory_order_relaxed);
    heldA_.store(held ? 1.0f : 0.0f, std::memory_order_relaxed);
    const float fall = std::exp(-(float)frames / (float)(0.3 * sr_)); // ~300 ms peak fall
    inPkA_.store(std::max(inPk, inPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
    outPkA_.store(std::max(outPk, outPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
    bpmA_.store(spb_ > 0.0 ? (float)(60.0 * sr_ / spb_) : 0.0f, std::memory_order_relaxed);
    srA_.store((float)sr_, std::memory_order_relaxed);
    if (frames > 0) {
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

const char* AutoFilter::paramName(int32_t i) const {
    static const char* nm[kNumParams] = {"Freq", "Res", "Type", "Slope", "Morph", "Env Amt", "Env Attack",
        "Env Release", "Env Hold", "LFO Amt", "LFO Rate", "LFO Wave", "LFO Morph", "Drive", "Dry/Wet", "Env On",
        "LFO On", "Gain", "Circuit", "LFO Sync", "LFO Stereo", "Mod Target", "Env Hold Time", "Mod Smooth",
        "LFO Offset", "LFO Retrig"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float AutoFilter::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void AutoFilter::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

std::string AutoFilter::deviceText(int32_t id) const {
    char b[1024];
    static const char* types[4] = {"LP", "BP", "HP", "Notch"};
    static const char* waves[5] = {"Sine", "Tri", "Saw", "Square", "S&H"};
    static const char* divs[8] = {"2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64"};
    static const char* targets[3] = {"Freq", "Reso", "Freq+Reso"};
    if (id == 0) {
        const int type = std::clamp((int)std::lround(getParam(Type) * 3.0f), 0, 3);
        const int target = std::clamp((int)std::lround(getParam(ModTarget) * 2.0f), 0, 2);
        std::string s;
        std::snprintf(b, sizeof b, "%s %d dB/oct%s - %s - Q %.2f", types[type], getParam(Slope) >= 0.5f ? 24 : 12,
            getParam(Circuit) >= 0.5f ? " analog" : "", hzText(expMap(getParam(Freq), 30.0, 18000.0)).c_str(),
            0.5 + getParam(Res) * 14.5);
        s = b;
        if (getParam(EnvOn) >= 0.5f && std::fabs(getParam(EnvAmt) - 0.5f) > 0.004f) {
            std::snprintf(b, sizeof b, " - ENV -> %s %+.0f %% (%.1f / %.1f ms%s)", targets[target],
                (getParam(EnvAmt) - 0.5f) * 200.0f, expMap(getParam(EnvAtt), 0.1, 500.0),
                expMap(getParam(EnvRel), 1.0, 2000.0), holdText().c_str());
            s += b;
        }
        if (getParam(LfoOn) >= 0.5f && getParam(LfoAmt) > 0.004f) {
            const bool sync = getParam(LfoSync) >= 0.5f;
            char rate[32];
            if (sync)
                std::snprintf(
                    rate, sizeof rate, "%s", divs[std::clamp((int)std::lround(getParam(LfoRate) * 7.0f), 0, 7)]);
            else
                std::snprintf(rate, sizeof rate, "%.2f Hz", expMap(getParam(LfoRate), 0.01, 40.0));
            std::snprintf(b, sizeof b, " - LFO %s %s -> %s %.0f %%%s",
                waves[std::clamp((int)std::lround(getParam(LfoWave) * 4.0f), 0, 4)], rate, targets[target],
                getParam(LfoAmt) * 100.0f, getParam(LfoRetrig) >= 0.5f ? " retrig" : "");
            s += b;
        }
        if (getParam(Drive) > 0.004f) {
            std::snprintf(b, sizeof b, " - drive %.0f %%", getParam(Drive) * 100.0f);
            s += b;
        }
        std::snprintf(b, sizeof b, " - mix %.0f %%", getParam(DryWet) * 100.0f);
        s += b;
        if (sidechainSourceTrackId() >= 0) s += " - sidechain key";
        return s;
    }
    if (id == 1) {
        const double hz = 30.0 * std::pow(18000.0 / 30.0, (double)cutA_.load(std::memory_order_relaxed));
        const double midi = 69.0 + 12.0 * std::log2(hz / 440.0);
        const int note = (int)std::lround(midi);
        static const char* nn[12] = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
        const float env = envA_.load(std::memory_order_relaxed);
        std::snprintf(b, sizeof b,
            "cutoff %s (%s%d %+.0f ct) - reso %.0f %% - env %.1f dB%s - lfo %+.2f phase %.0f deg - onsets %u",
            hzText(hz).c_str(), nn[((note % 12) + 12) % 12], note / 12 - 1, (midi - note) * 100.0,
            resA_.load(std::memory_order_relaxed) * 100.0f, env > 1e-6f ? 20.0f * std::log10(env) : -120.0f,
            heldA_.load(std::memory_order_relaxed) > 0.5f ? " (held)" : "", lfoA_.load(std::memory_order_relaxed),
            lfoPhA_.load(std::memory_order_relaxed) * 360.0f, onsets_.load(std::memory_order_relaxed));
        return b;
    }
    if (id == 2) {
        return "All params are 0..1. Freq: 30*600^v Hz. Res: Q 0.5+14.5v. Type: 0 LP, 0.333 BP, 0.667 HP, 1 Notch. "
               "Slope: 0 = 12, 1 = 24 dB/oct. Morph: blend toward the next type. Env Amt: bipolar, 0.5 = 0 (+/-6 oct "
               "on Freq, "
               "+/-80 % on Res). Env Attack: 0.1*5000^v ms. Env Release: 2000^v ms. Env Hold: on/off; Env Hold Time: "
               "400v^2 ms, "
               ">= 0.995 = infinite. LFO Amt: 0..4 oct on Freq (+/-50 % on Res). LFO Rate: free 0.01*4000^v Hz; with "
               "LFO Sync on "
               "round(v*7) picks 2/1,1/1,1/2,1/4,1/8,1/16,1/32,1/64. LFO Wave: 0 Sine, 0.25 Tri, 0.5 Saw, 0.75 Square, "
               "1 S&H. "
               "LFO Morph: skew / pulse width. LFO Stereo: right-channel phase 0..180 deg. LFO Offset: start phase "
               "0..360 deg. "
               "LFO Retrig: restart on each input onset (-24 dBFS) and at play. Mod Target: 0 Freq, 0.5 Reso, 1 both. "
               "Mod Smooth: 120v^2 ms slew. Drive: pre-filter saturation. Circuit: 0 Clean, 1 Analog. Gain: 0.5 = 0 "
               "dB, +/-24 dB. "
               "Env On / LFO On / toggles: >= 0.5 = on.";
    }
    return {};
}

} // namespace nota
