// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Vintage implementation. The memoryless
// saturation + output stage run on fast_math::tanhPrecise with a hoisted kTanh06; the
// wow/flutter LFO sin/cos stay std::-exact (they steer the delay read pointer).
// Silent stretches auto-sleep via isIdle() (sleep only when no RNG path can fire,
// so the skip is bit-exact — see process()).

#include "Vintage.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>

namespace nota {

Vintage::Vintage() {
    p_[Mode].store(0.0f); // Vinyl
    p_[Drive].store(0.35f);
    p_[Tone].store(0.5f); // neutral tilt
    p_[Wow].store(0.30f);
    p_[Flutter].store(0.25f);
    p_[Noise].store(0.30f);
    p_[Crackle].store(0.45f);
    p_[Wear].store(0.20f);
    p_[Mix].store(1.0f);
    p_[Output].store(0.5f); // 0 dB
    p_[ToneLow].store(0.5f);
    p_[ToneHigh].store(0.5f);
    p_[ToneModel].store(0.0f); // Warm = the voicing older projects had
    p_[Character].store(1.0f);
    p_[WowRate].store(kWowRateDefault);      // 0.55 Hz
    p_[FlutterRate].store(kFlutRateDefault); // 7 Hz
}

void Vintage::setSampleRate(double sr, int32_t maxBlock) {
    sr_ = sr > 0 ? sr : 44100.0;
    for (int c = 0; c < 2; ++c) {
        toneSh_[c].reset();
        bumpSh_[c].reset();
        bandLp_[c].reset();
        hp_[c].reset();
        lowSh_[c].reset();
        highSh_[c].reset();
        hissHp_[c] = 0.0f;
        hissPrev_[c] = 0.0f;
        clickEnv_[c] = 0.0f;
        for (int i = 0; i < kDelay; ++i) dl_[c][i] = 0.0f;
    }
    dw_ = 0;
    wowPh_ = 0.0;
    flutPh_ = 0.0;
    flutNoise_ = flutNoiseT_ = 0.0f;
    inEnv_ = 0.0f;
    inMs_ = 0.0;
    wetMs_ = 0.0;
    compG_ = 1.0f;
    maxFrames_ = maxBlock > 0 ? maxBlock : 4096;
    os_.prepare(maxFrames_);
    dry_.assign(maxFrames_ * 2, 0.0f);
    click_.assign(maxFrames_ * 2, 0.0f);
    follow_.assign(maxFrames_, 1.0f);
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
    srA_.store((float)sr_, std::memory_order_relaxed);
}

void Vintage::setTransport(double beatStart, double spb, bool playing) {
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

void Vintage::deviceAction(int32_t id, int32_t, float) {
    if (id == A_ResetWear) resetReq_.store(true, std::memory_order_relaxed);
    wake();
}

void Vintage::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

void Vintage::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (resetReq_.exchange(false, std::memory_order_relaxed)) {
        wowPh_ = 0.0;
        flutPh_ = 0.0;
        flutNoise_ = flutNoiseT_ = 0.0f;
        clickEnv_[0] = clickEnv_[1] = 0.0f;
    }
    const Params P = params();
    const ModeChar& md = P.md;
    const Curve cv = curveFor(P);

    // Tone: the model's tilt (Warm = the era's tilt + head bump, Flat = the Tone knob
    // alone, Dark = a lower, darker tilt) + the low / high shelves; the band-limit
    // narrows with Wear; the low cut models the era's limited low end.
    const double tiltDb = (P.model == 0      ? md.tilt + (P.tone - 0.5)
                              : P.model == 1 ? (P.tone - 0.5)
                                             : (P.tone - 0.5) - 0.3) *
                          14.0;
    const double tiltHz = P.model == 2 ? 2000.0 : 3500.0;
    const double bumpDb = P.model == 0 ? md.bumpDb : 0.0;
    const double bandHz = bandFor(P);
    Biquad::Coef toneC = Biquad::highShelf(sr_, tiltHz, tiltDb);
    Biquad::Coef bumpC = Biquad::lowShelf(sr_, 90.0, bumpDb);
    Biquad::Coef bandC = bandHz > 0 ? Biquad::lowpass(sr_, bandHz, 0.707) : Biquad::Coef{};
    Biquad::Coef hpC = Biquad::highpass(sr_, md.hpHz, 0.707);
    Biquad::Coef lowC = Biquad::lowShelf(sr_, 120.0, (P.low - 0.5) * 24.0);
    Biquad::Coef highC = Biquad::highShelf(sr_, 6000.0, (P.high - 0.5) * 24.0);
    for (int c = 0; c < 2; ++c) {
        toneSh_[c].set(toneC);
        bumpSh_[c].set(bumpC);
        bandLp_[c].set(bandC);
        hp_[c].set(hpC);
        lowSh_[c].set(lowC);
        highSh_[c].set(highC);
    }

    // Wow (slow) + flutter (fast) pitch modulation via a fractional delay line. Wow
    // runs free or locked to the beat; flutter wanders a little more as Wear rises.
    const double baseDelay = kBaseDelayS * sr_;
    const double wowSamp = md.wowMul * P.wow * 0.0040 * sr_;
    const double flutSamp = md.flutMul * P.flutter * 0.0015 * sr_;
    const bool modOn = (wowSamp + flutSamp) > 0.25;
    const bool sync = P.wowSync && spb_ > 0.0;
    const double wowHz = wowHzFor(P);
    const double flutHz = flutHzFor(P);
    const double wowInc = wowHz / sr_;
    const double flutInc = flutHz / sr_;
    const double stereoOff = P.stereo ? 0.25 : 0.0;
    const float flutWander = 0.35f * P.wear;

    const float hissLvl = P.noise * md.noiseMul * (1.0f + P.wear) * 0.05f;
    const float crkAmt = P.crackle * md.crackleMul * (1.0f + 2.0f * P.wear);
    const float crkProb = crkAmt * 0.0012f;
    const float hissHpA = (float)std::exp(-2.0 * kPi * expMap(get(HissHp), 20.0, 2000.0) / sr_);
    const float envAtt = (float)std::exp(-1.0 / (0.005 * sr_));
    const float envRel = (float)std::exp(-1.0 / (0.150 * sr_));
    const double msA = std::exp(-1.0 / (0.3 * sr_)); // auto-comp RMS window
    const float compA = (float)std::exp(-1.0 / (0.05 * sr_));
    const int histStep = std::max(1, (int)std::lround(sr_ * kHistMs * 0.001));

    const float mix = P.mix;
    const float outGain = std::pow(10.0f, (get(Output) - 0.5f) * 24.0f / 20.0f); // ±12 dB

    // Oversample the memoryless saturation + stage only. Wow/flutter (a pitch-mod
    // delay) runs at base rate before the up-sampling; the filters and the additive
    // hiss + crackle stay at base rate after it.
    const int osIdx = std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3);
    os_.setActive(1 << osIdx);

    // Idle skip: silent input on a settled line leaves the buffer untouched. The
    // skip is exact only when no RNG path can fire (hiss/crackle off, no flutter
    // wander — each consumes whiteUni() draws, which a skip would desync); with
    // any of them active the output never reads silent anyway, so sleep never
    // engages. The LFO phases advance so the wake-up has no timing jump (beat-
    // synced wow is recomputed from the transport on wake); the follower and the
    // auto-comp windows decay exactly as the silent path would.
    const bool rngIdle = (hissLvl == 0.0f) && (crkAmt == 0.0f) && (flutWander == 0.0f);
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    if (inSilent && idle_ && rngIdle && !bypassed()) {
        if (!(sync && playing_)) {
            wowPh_ += wowInc * frames;
            wowPh_ -= std::floor(wowPh_);
        }
        flutPh_ += flutInc * frames;
        flutPh_ -= std::floor(flutPh_);
        for (int32_t i = 0; i < frames; ++i) inEnv_ *= envRel;
        const double m = std::pow(msA, frames);
        inMs_ *= m;
        wetMs_ *= m;
        return;
    }

    if (frames > maxFrames_) frames = maxFrames_;
    float inPk = 0.0f, outPk = 0.0f, feedPk = 0.0f;

    // --- Base-rate pre: wow/flutter tap into buf, crackle + input follower, stash dry. ---
    for (int32_t i = 0; i < frames; ++i) {
        const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
        dry_[i * 2] = dryL;
        dry_[i * 2 + 1] = dryR;
        const float a = std::max(std::fabs(dryL), std::fabs(dryR));
        inPk = std::max(inPk, a);
        inEnv_ = a > inEnv_ ? a + (inEnv_ - a) * envAtt : a + (inEnv_ - a) * envRel;
        follow_[i] = P.follow ? std::min(1.0f, inEnv_ * 4.0f) : 1.0f;

        // Beat-locked wow: the phase is the beat position within the division.
        if (sync && playing_) {
            const double beats = kSyncBeats[std::clamp((int)std::lround(get(WowRate) * 5.0f), 0, 5)];
            const double beat = beatStart_ + (double)i / spb_;
            wowPh_ = beat / beats - std::floor(beat / beats);
        }
        // Flutter wander: a smoothed random target, re-drawn a few times per cycle.
        if (flutWander > 0.0f) {
            if (whiteUni() < flutInc * 3.0) flutNoiseT_ = whiteUni() * 2.0f - 1.0f;
            flutNoise_ += (flutNoiseT_ - flutNoise_) * 0.002f;
        }

        dl_[0][dw_] = dryL;
        dl_[1][dw_] = dryR;
        float wl = dryL, wr = dryR;
        double centsW = 0.0, centsF = 0.0;
        if (modOn) {
            const double fl = 1.0 + flutWander * flutNoise_;
            const double sW = std::sin(2.0 * kPi * wowPh_), sF = std::sin(2.0 * kPi * flutPh_);
            wl = readFrac(0, baseDelay + wowSamp * sW + flutSamp * fl * sF);
            if (stereoOff > 0.0) {
                const double sWr = std::sin(2.0 * kPi * (wowPh_ + stereoOff)),
                             sFr = std::sin(2.0 * kPi * (flutPh_ + stereoOff));
                wr = readFrac(1, baseDelay + wowSamp * sWr + flutSamp * fl * sFr);
            } else {
                wr = readFrac(1, baseDelay + wowSamp * sW + flutSamp * fl * sF);
            }
            // Pitch = the read head's speed: 1 − dD/dn. In cents ≈ −1731 · dD/dn.
            centsW = -1731.2 * wowSamp * 2.0 * kPi * wowInc * std::cos(2.0 * kPi * wowPh_);
            centsF = -1731.2 * flutSamp * fl * 2.0 * kPi * flutInc * std::cos(2.0 * kPi * flutPh_);
        }
        feedPk = std::max(feedPk, std::max(std::fabs(wl), std::fabs(wr)));
        dw_ = (dw_ + 1) & (kDelay - 1);
        wowPh_ += wowInc;
        if (wowPh_ >= 1.0) wowPh_ -= 1.0;
        flutPh_ += flutInc;
        if (flutPh_ >= 1.0) flutPh_ -= 1.0;

        if (++histCount_ >= histStep) {
            histCount_ = 0;
            const uint32_t hw = histW_.load(std::memory_order_relaxed);
            histWow_[hw & (kHist - 1)] = (float)centsW;
            histFlut_[hw & (kHist - 1)] = (float)centsF;
            histW_.store(hw + 1, std::memory_order_relaxed);
        }

        float cl = 0.0f, cr = 0.0f;
        if (crkAmt > 0.0f) {
            for (int c = 0; c < (P.stereo ? 2 : 1); ++c) {
                clickEnv_[c] *= 0.55f;
                if (whiteUni() < crkProb) {
                    clickEnv_[c] = (whiteUni() * 0.7f + 0.2f) * (whiteUni() < 0.5f ? -1.0f : 1.0f);
                    crackles_.fetch_add(1, std::memory_order_relaxed);
                }
            }
            cl = clickEnv_[0] * 0.6f;
            cr = (P.stereo ? clickEnv_[1] : clickEnv_[0]) * 0.6f;
        }
        click_[i * 2] = cl * follow_[i];
        click_[i * 2 + 1] = cr * follow_[i];
        buf[i * 2] = wl;
        buf[i * 2 + 1] = wr;
    }

    // --- Oversampled saturation + output stage (memoryless). ---
    os_.process(buf, frames, [&](float& l, float& r, int /*i*/) {
        l = cv.eval(l);
        r = cv.eval(r);
    });

    // --- Base-rate post: tone/bump/shelves/band/hp, + hiss + crackle, auto-comp, dry/wet. ---
    for (int32_t i = 0; i < frames; ++i) {
        float yL = buf[i * 2], yR = buf[i * 2 + 1];
        yL = toneSh_[0].process(yL);
        yL = bumpSh_[0].process(yL);
        yL = lowSh_[0].process(yL);
        yL = highSh_[0].process(yL);
        yL = bandLp_[0].process(yL);
        yL = hp_[0].process(yL);
        yR = toneSh_[1].process(yR);
        yR = bumpSh_[1].process(yR);
        yR = lowSh_[1].process(yR);
        yR = highSh_[1].process(yR);
        yR = bandLp_[1].process(yR);
        yR = hp_[1].process(yR);
        if (hissLvl > 0.0f) {
            const float g = hissLvl * follow_[i];
            yL += hiss(0, hissHpA) * g;
            yR += hiss(1, hissHpA) * g;
        }
        yL += click_[i * 2];
        yR += click_[i * 2 + 1];
        if (P.autoComp) {
            const float dl = dry_[i * 2], dr = dry_[i * 2 + 1];
            inMs_ = inMs_ * msA + (1.0 - msA) * 0.5 * (dl * dl + dr * dr);
            wetMs_ = wetMs_ * msA + (1.0 - msA) * 0.5 * (yL * yL + yR * yR);
            if (inMs_ > 1e-8 && wetMs_ > 1e-10) {
                const float want = (float)std::clamp(std::sqrt(inMs_ / wetMs_), 0.125, 8.0); // ±18 dB
                compG_ = want + (compG_ - want) * compA;
            }
            yL *= compG_;
            yR *= compG_;
        } else if (compG_ != 1.0f) {
            compG_ = 1.0f;
            inMs_ = wetMs_ = 0.0;
        }
        const float oL = dry_[i * 2] * (1.0f - mix) + yL * outGain * mix;
        const float oR = dry_[i * 2 + 1] * (1.0f - mix) + yR * outGain * mix;
        buf[i * 2] = oL;
        buf[i * 2 + 1] = oR;
        outPk = std::max(outPk, std::max(std::fabs(oL), std::fabs(oR)));
    }

    // Delay-tap peak (decays slowly so a flushing line blocks accrual); sleep
    // additionally waits out the line's flight time so a late tap is never cut:
    // output silence alone proves nothing while the ring is full.
    feedPk_ = feedPk > feedPk_ ? feedPk : feedPk_ * 0.90f;
    if (inSilent && rngIdle && !bypassed() && outPk <= 1.0e-5f && feedPk_ <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > kMaxFlight + kIdleSamples) idle_ = true;

    const float fall = std::exp(-(float)frames / (float)(0.3 * sr_)); // ~300 ms peak fall
    inPkA_.store(std::max(inPk, inPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
    outPkA_.store(std::max(outPk, outPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
    compA_.store(compG_, std::memory_order_relaxed);
    wowPhA_.store((float)wowPh_, std::memory_order_relaxed);
    delayA_.store(modOn ? (float)baseDelay : 0.0f, std::memory_order_relaxed);
    bpmA_.store(spb_ > 0.0 ? (float)(60.0 * sr_ / spb_) : 0.0f, std::memory_order_relaxed);
    srA_.store((float)sr_, std::memory_order_relaxed);
    if (frames > 0) {
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

// Telemetry block, the transfer curve, then the pitch histories (see the header note).
// Writes as much of that layout as fits in maxSamples. Lock-free; torn reads are fine.
int32_t Vintage::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    const Params P = params();
    const Curve cv = curveFor(P);
    float t[kTele] = {};
    t[S_InPk] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPk] = outPkA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    t[S_Delay] = delayA_.load(std::memory_order_relaxed);
    const double wowHz = wowHzFor(P), flutHz = flutHzFor(P);
    t[S_WowHz] = (float)wowHz;
    t[S_FlutHz] = (float)flutHz;
    t[S_WowCents] = (float)depthCents(P.md.wowMul * P.wow * 0.0040, wowHz);
    t[S_FlutCents] = (float)depthCents(P.md.flutMul * P.flutter * 0.0015 * (1.0 + 0.35 * P.wear), flutHz);
    t[S_BandHz] = (float)bandFor(P);
    const float hiss = P.noise * P.md.noiseMul * (1.0f + P.wear) * 0.05f / std::sqrt(3.0f);
    t[S_HissDb] =
        hiss > 1e-6f ? 20.0f * std::log10(hiss * std::pow(10.0f, (get(Output) - 0.5f) * 1.2f) * P.mix) : -120.0f;
    const float lvl = t[S_InPk] > 0.004f ? std::min(t[S_InPk], 1.0f) : 0.5f;
    t[S_TestLvl] = lvl;
    double h[8] = {};
    harmonics(cv, lvl, h);
    double sq = 0.0;
    for (int k = 2; k <= 7; ++k) sq += h[k] * h[k];
    t[S_Thd] = h[1] > 1e-9 ? (float)(std::sqrt(sq) / h[1]) : 0.0f;
    for (int k = 1; k <= 7; ++k)
        t[S_H1 + k - 1] =
            h[1] > 1e-9 && h[k] > 1e-9 ? (float)std::max(-120.0, 20.0 * std::log10(h[k] / h[1])) : -120.0f;
    const float fp = cv.eval(0.5f), fn = cv.eval(-0.5f);
    t[S_Asym] = std::fabs(fp) > 1e-6f ? std::min(1.0f, std::fabs(fp + fn) / std::fabs(fp)) : 0.0f;
    const float cg = compA_.load(std::memory_order_relaxed);
    t[S_CompDb] = P.autoComp && cg > 1e-6f ? 20.0f * std::log10(cg) : 0.0f;
    t[S_Mode] = (float)P.mode;
    t[S_Crackles] = (float)crackles_.load(std::memory_order_relaxed);
    t[S_HistLen] = (float)kHist;
    t[S_HistMs] = (float)kHistMs;
    t[S_WowPhase] = wowPhA_.load(std::memory_order_relaxed);
    int32_t n = 0;
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    for (int k = 0; k < kCurve && n < maxSamples; ++k) out[n++] = cv.eval((float)k / (kCurve - 1));
    const uint32_t hw = histW_.load(std::memory_order_relaxed);
    for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = histWow_[(hw + (uint32_t)k) & (kHist - 1)];
    for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = histFlut_[(hw + (uint32_t)k) & (kHist - 1)];
    return n;
}

const char* Vintage::paramName(int32_t i) const {
    static const char* nm[kNumParams] = {"Mode", "Drive", "Tone", "Wow", "Flutter", "Noise", "Crackle", "Wear", "Mix",
        "Output", "Oversampling", "Tone Low", "Tone High", "Tone Model", "Character", "Auto Comp", "Wow Rate",
        "Flutter Rate", "Wow Sync", "Hiss HP", "Wear Follow", "Stereo Drift", "Output Stage", "Even Only"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float Vintage::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Vintage::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

// 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
std::string Vintage::deviceText(int32_t id) const {
    char b[1024];
    const Params P = params();
    if (id == 0) {
        std::string s = kModeName[P.mode];
        if (!P.character) s += " (voicing off)";
        std::snprintf(b, sizeof b, " - drive %.0f %% - tone %+.1f - %s", P.drive * 100.0f, (P.tone - 0.5f) * 10.0f,
            kModelName[P.model]);
        s += b;
        if (std::fabs(P.low - 0.5f) > 0.004f || std::fabs(P.high - 0.5f) > 0.004f) {
            std::snprintf(b, sizeof b, " (low %+.1f / high %+.1f dB)", (P.low - 0.5f) * 24.0f, (P.high - 0.5f) * 24.0f);
            s += b;
        }
        if (P.wow > 0.004f || P.flutter > 0.004f) {
            std::snprintf(b, sizeof b, " - wow %.0f %% @ %s - flutter %.0f %% @ %.1f Hz", P.wow * 100.0f,
                wowRateText(P).c_str(), P.flutter * 100.0f, flutHzFor(P));
            s += b;
        }
        std::snprintf(b, sizeof b, " - noise %.0f %% - crackle %.0f %% - wear %.0f %%", P.noise * 100.0f,
            P.crackle * 100.0f, P.wear * 100.0f);
        s += b;
        if (P.stage > 0) s += P.stage == 1 ? " - tube stage" : " - analog stage";
        if (P.even) s += " - even harmonics only";
        if (P.follow) s += " - wear follows input";
        if (P.stereo) s += " - stereo drift";
        std::snprintf(b, sizeof b, " - mix %.0f %% - out %+.1f dB - OS %s%s", P.mix * 100.0f,
            (get(Output) - 0.5f) * 24.0f, kOsName[std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3)],
            P.autoComp ? " - auto-comp" : "");
        s += b;
        return s;
    }
    if (id == 1) {
        float sc[kTele];
        scopeRead(sc, kTele);
        auto db = [](float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; };
        std::snprintf(b, sizeof b,
            "in %.1f dB - out %.1f dB - THD %.1f %% at %.1f dBFS (2nd %.0f dB, 3rd %.0f dB) - asym %.0f %% - "
            "wow +/-%.1f ct @ %.2f Hz - flutter +/-%.1f ct @ %.1f Hz - band %s - hiss %.0f dBFS - comp %+.1f "
            "dB - crackles %.0f",
            db(sc[S_InPk]), db(sc[S_OutPk]), sc[S_Thd] * 100.0f, db(sc[S_TestLvl]), sc[S_H1 + 1], sc[S_H1 + 2],
            sc[S_Asym] * 100.0f, sc[S_WowCents], sc[S_WowHz], sc[S_FlutCents], sc[S_FlutHz],
            sc[S_BandHz] > 0 ? hzText(sc[S_BandHz]).c_str() : "open", sc[S_HissDb], sc[S_CompDb], sc[S_Crackles]);
        return b;
    }
    if (id == 2) {
        return "All params are 0..1. Mode: 0 Vinyl, 0.2 Cassette, 0.4 Reel, 0.6 VHS, 0.8 Tube, 1 Analog. Drive: "
               "saturation amount. "
               "Tone: tilt, 0.5 = neutral (+/-7 dB at 3.5 kHz). Tone Model: 0 Warm (the era's tilt + head bump), 0.5 "
               "Flat (the Tone knob "
               "only), 1 Dark (darker tilt at 2 kHz). Tone Low: shelf at 120 Hz, Tone High: shelf at 6 kHz, both 0.5 = "
               "0 dB, +/-12 dB. "
               "Character: 1 = the Mode's voicing (shape, band-limit, bump, hiss/crackle/wow/flutter weights), 0 = "
               "neutral. "
               "Wow / Flutter: depth. Wow Rate: free 0.1*40^v Hz (0.462 = 0.55 Hz); with Wow Sync on round(v*5) picks "
               "4 bars, 2 bars, "
               "1 bar, 1/2, 1/4, 1/8. Flutter Rate: 2*10^v Hz (0.544 = 7 Hz). Noise: hiss. Hiss HP: 20*100^v Hz. "
               "Crackle: clicks. "
               "Wear: age macro (narrows the band, more hiss/crackle, flutter wanders). Wear Follow: hiss + crackle "
               "follow the input level. "
               "Stereo Drift: the right channel's wow/flutter a quarter-cycle apart, own crackle. Output Stage: 0 off, "
               "0.5 Tube, 1 Analog. "
               "Even Only: the saturation adds even harmonics only. Auto Comp: output level matched to the input. "
               "Mix: dry/wet. Output: 0.5 = 0 dB, +/-12 dB. Oversampling: 0 off, 0.333 2x, 0.667 4x, 1 8x. Toggles: >= "
               "0.5 = on.";
    }
    return {};
}

std::string Vintage::hzText(double hz) {
    char b[32];
    if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.1f kHz", hz / 1000.0);
    else
        std::snprintf(b, sizeof b, "%.0f Hz", hz);
    return b;
}

Vintage::Params Vintage::params() const {
    Params P{};
    P.mode = std::clamp((int)std::lround(get(Mode) * 5.0f), 0, kModes - 1);
    P.character = get(Character) >= 0.5f;
    P.md = P.character ? kMode[P.mode] : kNeutral;
    P.model = std::clamp((int)std::lround(get(ToneModel) * 2.0f), 0, 2);
    P.stage = std::clamp((int)std::lround(get(OutputStage) * 2.0f), 0, 2);
    P.drive = std::clamp(get(Drive), 0.0f, 1.0f);
    P.tone = std::clamp(get(Tone), 0.0f, 1.0f);
    P.wow = std::clamp(get(Wow), 0.0f, 1.0f);
    P.flutter = std::clamp(get(Flutter), 0.0f, 1.0f);
    P.noise = std::clamp(get(Noise), 0.0f, 1.0f);
    P.crackle = std::clamp(get(Crackle), 0.0f, 1.0f);
    P.wear = std::clamp(get(Wear), 0.0f, 1.0f);
    P.mix = std::clamp(get(Mix), 0.0f, 1.0f);
    P.low = get(ToneLow);
    P.high = get(ToneHigh);
    P.autoComp = get(AutoComp) >= 0.5f;
    P.wowSync = get(WowSync) >= 0.5f;
    P.follow = get(WearFollow) >= 0.5f;
    P.stereo = get(StereoDrift) >= 0.5f;
    P.even = get(EvenOnly) >= 0.5f;
    return P;
}

double Vintage::wowHzFor(const Params& P) const {
    if (P.wowSync && spb_ > 0.0) {
        const double beats = kSyncBeats[std::clamp((int)std::lround(get(WowRate) * 5.0f), 0, 5)];
        return sr_ / spb_ / beats;
    }
    return expMap(get(WowRate), 0.1, 4.0);
}

std::string Vintage::wowRateText(const Params& P) const {
    if (P.wowSync) return kSyncName[std::clamp((int)std::lround(get(WowRate) * 5.0f), 0, 5)];
    char b[32];
    std::snprintf(b, sizeof b, "%.2f Hz", wowHzFor(P));
    return b;
}

double Vintage::bandFor(const Params& P) const {
    if (P.md.bandHz <= 0.0) return 0.0;
    return std::clamp(P.md.bandHz * (1.0 - 0.55 * P.wear), 1500.0, sr_ * 0.45);
}

double Vintage::depthCents(double seconds, double hz) {
    const double slope = seconds * 2.0 * kPi * hz; // peak dD/dt (s/s)
    return slope > 1e-9 ? 1200.0 * std::log2(1.0 + std::min(slope, 0.5)) : 0.0;
}

float Vintage::Curve::eval(float x) const {
    float y;
    if (even) {
        // An even function of the driven input added to the clean one: only even
        // harmonics (and DC, which the low cut removes).
        const float pre = x * g;
        const float e = 0.5f * (fast_math::tanhPrecise(pre + 0.6f) + fast_math::tanhPrecise(0.6f - pre)) - kTanh06;
        y = x - e * evenAmt;
    } else {
        y = saturate(shape, x * g, bias, biasDC) * makeup;
    }
    switch (stage) {
    case 1: y = (fast_math::tanhPrecise(1.4f * y + 0.12f) - 0.11943f) * 0.75f; break; // tube: warm, asymmetric
    case 2: y = y / (1.0f + 0.3f * std::fabs(y)) * 1.1f; break;                       // analog: console soft clip
    default: break;
    }
    return y;
}

Vintage::Curve Vintage::curveFor(const Params& P) const {
    Curve c;
    c.shape = P.md.shape;
    c.stage = P.stage;
    c.even = P.even;
    c.g = 1.0f + P.md.driveMul * P.drive * 3.0f;
    c.bias = P.md.bias * (0.3f + P.drive); // bias grows with drive → even harmonics
    c.biasDC = std::tanh(c.bias);
    c.makeup = 1.0f / (0.7f + 0.5f * P.md.driveMul * P.drive);
    c.evenAmt = (0.2f + 1.3f * P.drive) * 0.9f / (0.35f + 0.25f * c.g);
    return c;
}

float Vintage::saturate(int shape, float pre, float bias, float biasDC) {
    switch (shape) {
    case 1: return fast_math::tanhPrecise(pre + bias) - biasDC; // tube: DC-corrected asymmetry
    case 2: return (pre / (1.0f + std::fabs(pre))) * 1.2f;      // tape: soft knee
    default: return fast_math::tanhPrecise(pre);                // soft
    }
}

void Vintage::harmonics(const Curve& cv, float a, double* h) {
    constexpr int N = 128;
    float y[N];
    for (int n = 0; n < N; ++n) y[n] = cv.eval(a * (float)std::sin(2.0 * kPi * n / N));
    for (int k = 1; k <= 7; ++k) {
        double re = 0.0, im = 0.0;
        for (int n = 0; n < N; ++n) {
            const double w = 2.0 * kPi * k * n / N;
            re += y[n] * std::cos(w);
            im += y[n] * std::sin(w);
        }
        h[k] = 2.0 * std::sqrt(re * re + im * im) / N;
    }
}

float Vintage::hiss(int c, float a) {
    const float x = whiteUni() * 2.0f - 1.0f;
    const float y = a * (hissHp_[c] + x - hissPrev_[c]);
    hissPrev_[c] = x;
    hissHp_[c] = y;
    return y;
}

float Vintage::readFrac(int c, double tap) const {
    double rp = (double)dw_ - tap;
    while (rp < 0.0) rp += kDelay;
    const int i0 = (int)rp;
    const double fr = rp - i0;
    const int i1 = (i0 + 1) & (kDelay - 1);
    return (float)(dl_[c][i0 & (kDelay - 1)] * (1.0 - fr) + dl_[c][i1] * fr);
}

Vintage::Biquad::Coef Vintage::Biquad::norm(double b0, double b1, double b2, double a0, double a1, double a2) {
    Coef c;
    c.b0 = (float)(b0 / a0);
    c.b1 = (float)(b1 / a0);
    c.b2 = (float)(b2 / a0);
    c.a1 = (float)(a1 / a0);
    c.a2 = (float)(a2 / a0);
    return c;
}

double Vintage::Biquad::w0(double sr, double f) {
    f = std::clamp(f, 10.0, sr * 0.45);
    return 2.0 * kPi * f / sr;
}

Vintage::Biquad::Coef Vintage::Biquad::lowpass(double sr, double f, double q) {
    double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
    return norm((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + a, -2 * cw, 1 - a);
}

Vintage::Biquad::Coef Vintage::Biquad::highpass(double sr, double f, double q) {
    double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
    return norm((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + a, -2 * cw, 1 - a);
}

Vintage::Biquad::Coef Vintage::Biquad::lowShelf(double sr, double f, double dB) {
    if (std::fabs(dB) < 1e-3) return Coef{};
    double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0),
           s = 2 * std::sqrt(A) * a;
    return norm(A * ((A + 1) - (A - 1) * cw + s), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - s),
        (A + 1) + (A - 1) * cw + s, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - s);
}

Vintage::Biquad::Coef Vintage::Biquad::highShelf(double sr, double f, double dB) {
    if (std::fabs(dB) < 1e-3) return Coef{};
    double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0),
           s = 2 * std::sqrt(A) * a;
    return norm(A * ((A + 1) + (A - 1) * cw + s), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - s),
        (A + 1) - (A - 1) * cw + s, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - s);
}

float Vintage::Biquad::process(float x) {
    const float y = b0 * x + z1;
    z1 = b1 * x - a1 * y + z2;
    z2 = b2 * x - a2 * y;
    return y;
}

} // namespace nota
