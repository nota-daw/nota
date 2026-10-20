// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Forge implementation. Control-rate and
// modulation-steering math (LFO, envelopes, coefficients) stays std::-exact, the shaper
// tanh uses tanhPrecise — downstream per-sample saturation only, never steering a read
// pointer or loop — while silent stretches auto-sleep via isIdle().

#include "Forge.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {

Forge::Forge() {
    p_[Amount].store(0.35f);
    p_[Tone].store(0.5f);
    p_[Wet].store(1.0f);
    p_[Output].store(0.5f);
    p_[Bias].store(0.5f);
    p_[Width].store(0.5f);
    p_[Routing].store(0.0f);
    p_[LfoDrive].store(0.0f);
    p_[EnvTone].store(0.0f);
    p_[LfoRate].store(0.4f);
    p_[LfoSync].store(1.0f);
    setStage(0, 0, 0.4f, 0.5f, 0.0f, 1.0f); // Tube, on
    setStage(1, 1, 0.3f, 0.5f, 0.0f, 0.0f); // Diode, off
    setStage(2, 4, 0.2f, 0.5f, 0.0f, 0.0f); // Digital, off
    p_[Oversampling].store(0.0f);
    for (int s = 0; s < kStages; ++s) {
        p_[S1Bias + s * 3].store(0.5f);
        p_[S1Tone + s * 3].store(0.5f);
        p_[S1Width + s * 3].store(0.5f);
    }
    setSampleRate(44100.0, 4096);
}

void Forge::setSampleRate(double sr, int32_t maxBlock) {
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    clearState();
    maxFrames_ = maxBlock > 0 ? maxBlock : 4096;
    os_.prepare(maxFrames_);
    dryL_.assign(maxFrames_, 0.0f);
    dryR_.assign(maxFrames_, 0.0f);
    driveMod_.assign(maxFrames_, 0.0f);
    tone_.assign(maxFrames_, 0.0f);
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
}

void Forge::setTransport(double beatStart, double spb, bool playing) {
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

// Live LFO value (0..1) on the generic per-device live-scalar channel (kept for older readers).
float Forge::gainReductionDb() const {
    return lfoLive_.load(std::memory_order_relaxed);
}

void Forge::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

void Forge::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (resetReq_.exchange(false, std::memory_order_acquire)) {
        lfoPhase_ = 0;
        inPkM_ = outPkM_ = 0;
        msIn_ = msOut_ = 0;
        inLvl_ = 0;
    }
    const Cfg c = cfg();
    const float toneBase = (get(Tone) - 0.5f) * 2.0f;              // −1..+1
    const float width = std::clamp(get(Width), 0.0f, 1.0f) * 2.0f; // 0..2
    const float lfoDepth = std::clamp(get(LfoDrive), 0.0f, 1.0f);
    const float envDepth = std::clamp(get(EnvTone), 0.0f, 1.0f);

    // LFO increment (free Hz, or synced division).
    const bool sync = get(LfoSync) >= 0.5f && spb_ > 0.0;
    const double lfoHz = expMap(get(LfoRate), 0.05, 20.0);
    const double divBeats = kLfoDiv[divIndex()];
    const double phaseInc = lfoHz / sr_;

    // Oversampling: Off/2×/4×/8×. The nonlinear stages (which alias) and everything that
    // lives between them (per-stage tilts, the multiband crossovers) run at the higher
    // rate; control (LFO/env), the master tilt, width and dry/wet stay at base.
    const int factor = 1 << osIndex(); // 1,2,4,8
    os_.setActive(factor);
    const double osr = sr_ * factor;

    const float toneCoef = (float)std::exp(-2.0 * kPi * 700.0 / sr_); // master tilt (base rate)
    const float envAtt = (float)std::exp(-1.0 / (0.005 * sr_));
    const float envRel = (float)std::exp(-1.0 / (0.15 * sr_));
    const float msCoef = (float)std::exp(-1.0 / (0.3 * sr_));
    const float lvlRel = (float)std::exp(-1.0 / (0.08 * sr_));
    Core k;
    k.tiltCoef = (float)std::exp(-2.0 * kPi * 800.0 / osr); // per-stage tilt pivot
    k.loCoef = (float)std::exp(-2.0 * kPi * kXoLo / osr);
    k.hiCoef = (float)std::exp(-2.0 * kPi * kXoHi / osr);

    if (frames > maxFrames_) frames = maxFrames_;

    // Idle skip: silent input on settled states leaves the buffer untouched. The
    // free-running LFO phase keeps advancing and the slow followers decay in closed
    // form so the wake-up modulation matches; the contractive shaper/filter states
    // are already at dust after the hangover. Sleep engages only after the flight
    // time plus the hangover, tracked below via feedPk_/quietSamples_.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    if (inSilent && idle_) {
        if (!sync) {
            lfoPhase_ += phaseInc * frames;
            lfoPhase_ -= std::floor(lfoPhase_);
        }
        const float envD = (float)std::pow((double)envRel, (double)frames);
        const float lvlD = (float)std::pow((double)lvlRel, (double)frames);
        const float msD = (float)std::pow((double)msCoef, (double)frames);
        env_ *= envD;
        inLvl_ *= lvlD;
        msIn_ *= msD;
        msOut_ *= msD;
        return;
    }

    // --- Base-rate pre-pass: LFO/env → per-sample control, meters, and stash the dry. ---
    float inPk = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        double phase;
        if (sync) {
            double beat = beatStart_ + (double)i / spb_;
            phase = beat / divBeats;
            phase -= std::floor(phase);
        } else
            phase = lfoPhase_;
        const float lfo =
            (float)(0.5 + 0.5 * std::sin(2.0 * kPi * phase)); // 0..1 (verbatim std::: control-rate steering)
        lfoLive_.store(lfo, std::memory_order_relaxed);

        const float xl = buf[i * 2], xr = buf[i * 2 + 1];
        dryL_[i] = xl;
        dryR_[i] = xr;
        const float a = std::max(std::fabs(xl), std::fabs(xr));
        inPk = std::max(inPk, a);
        inLvl_ = a > inLvl_ ? a : a + lvlRel * (inLvl_ - a);
        msIn_ = 0.5f * (xl * xl + xr * xr) + msCoef * (msIn_ - 0.5f * (xl * xl + xr * xr));
        const float det = a * c.amt;
        if (det > env_) env_ = det + envAtt * (env_ - det);
        else
            env_ = det + envRel * (env_ - det);
        tone_[i] = std::clamp(toneBase + envDepth * std::min(env_, 1.0f) * 1.5f, -1.0f, 1.5f);
        driveMod_[i] = lfoDepth * (lfo - 0.5f) * 0.6f; // ±0.3 to stage drive

        if (!sync) {
            lfoPhase_ += phaseInc;
            if (lfoPhase_ >= 1.0) lfoPhase_ -= 1.0;
        }
    }
    if (frames > 0) {
        driveModLive_.store(driveMod_[frames - 1], std::memory_order_relaxed);
        toneLive_.store(tone_[frames - 1], std::memory_order_relaxed);
    }

    // --- Oversampled nonlinear core: pre-gain + saturation stages. ---
    float wetPk = 0.0f;
    os_.process(buf, frames, [&](float& l, float& r, int i) {
        l *= c.amt;
        r *= c.amt;
        route(c, k, driveMod_[i], l, r);
        wetPk = std::max(wetPk, std::max(std::fabs(l), std::fabs(r)));
    });

    // --- Base-rate post: master tilt, width, output, dry/wet, meters. ---
    float outPk = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        float wl = buf[i * 2], wr = buf[i * 2 + 1];

        lpTone_[0] = wl + toneCoef * (lpTone_[0] - wl);
        lpTone_[1] = wr + toneCoef * (lpTone_[1] - wr);
        wl += tone_[i] * (wl - lpTone_[0]);
        wr += tone_[i] * (wr - lpTone_[1]);

        if (c.routing != 2) { // Mid/Side routing already shaped its image
            const float m = 0.5f * (wl + wr), s2 = 0.5f * (wl - wr) * width;
            wl = m + s2;
            wr = m - s2;
        }

        wl *= c.outGain;
        wr *= c.outGain;
        const float ol = dryL_[i] * (1.0f - c.wet) + wl * c.wet;
        const float orr = dryR_[i] * (1.0f - c.wet) + wr * c.wet;
        buf[i * 2] = ol;
        buf[i * 2 + 1] = orr;
        outPk = std::max(outPk, std::max(std::fabs(ol), std::fabs(orr)));
    }

    // Saturated-path peak (decays slowly so ringing states block accrual); sleep
    // additionally waits out the flight time so in-flight oversampler ringing is
    // never cut: output silence alone proves nothing while the core is hot.
    feedPk_ = wetPk > feedPk_ ? wetPk : feedPk_ * 0.90f;
    if (inSilent && outPk <= 1.0e-5f && feedPk_ <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > kMaxFlight + kIdleSamples) idle_ = true;

    if (frames > 0) {
        const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
        inPkM_ = std::max(inPk, inPkM_ * fall);
        outPkM_ = std::max(outPk, outPkM_ * fall);
        inPkA_.store(db(inPkM_), std::memory_order_relaxed);
        outPkA_.store(db(outPkM_), std::memory_order_relaxed);
        inRmsA_.store(dbMs(msIn_), std::memory_order_relaxed);
        outRmsA_.store(dbMs(msOut_), std::memory_order_relaxed);
        inLvlA_.store(inLvl_, std::memory_order_relaxed);
        envA_.store(std::min(env_, 1.0f), std::memory_order_relaxed);
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

const char* Forge::paramName(int32_t i) const {
    switch (i) {
    case Amount: return "Amount";
    case Tone: return "Tone";
    case Wet: return "Wet";
    case Output: return "Output";
    case Bias: return "Bias";
    case Width: return "Width";
    case Routing: return "Routing";
    case LfoDrive: return "LFO Drive";
    case EnvTone: return "Env Tone";
    case LfoRate: return "LFO Rate";
    case LfoSync: return "LFO Sync";
    case S1Type: return "S1 Type";
    case S1Drive: return "S1 Drive";
    case S1Out: return "S1 Out";
    case S1FB: return "S1 FB";
    case S1On: return "S1 On";
    case S2Type: return "S2 Type";
    case S2Drive: return "S2 Drive";
    case S2Out: return "S2 Out";
    case S2FB: return "S2 FB";
    case S2On: return "S2 On";
    case S3Type: return "S3 Type";
    case S3Drive: return "S3 Drive";
    case S3Out: return "S3 Out";
    case S3FB: return "S3 FB";
    case S3On: return "S3 On";
    case Oversampling: return "Oversampling";
    case S1Bias: return "S1 Bias";
    case S1Tone: return "S1 Tone";
    case S1Width: return "S1 Width";
    case S2Bias: return "S2 Bias";
    case S2Tone: return "S2 Tone";
    case S2Width: return "S2 Width";
    case S3Bias: return "S3 Bias";
    case S3Tone: return "S3 Tone";
    case S3Width: return "S3 Width";
    default: return "";
    }
}

float Forge::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Forge::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

void Forge::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id == 0) resetReq_.store(true, std::memory_order_release);
    wake();
}

// Static saturation transfer (kept in sync with process()). x is the pre-gained input,
// bias the combined bias −1..+1. tanhPrecise: downstream per-sample saturation only.
float Forge::shape(int type, float x, float bias) {
    const float b = bias * 0.6f;
    switch (type) {
    case 0: return fast_math::tanhPrecise(x + b) - fast_math::tanhPrecise(b); // Tube (asymmetric soft)
    case 1:
        return x + b >= 0 ? fast_math::tanhPrecise((x + b) * 1.3f)
                          : fast_math::tanhPrecise((x + b) * 0.55f); // Diode (asymmetric hard)
    case 2: return fast_math::tanhPrecise(x + b * 0.4f);             // Tape (soft symmetric)
    case 3: {
        float y = (x + b) / (1.0f + std::fabs(x + b));
        return fast_math::tanhPrecise(y * 3.0f);
    } // Fuzz
    case 4: return std::clamp(x + b, -1.0f, 1.0f); // Digital (hard clip)
    default: return std::sin((x + b) * 0.9f);      // Fold (wavefolder; radians, keep std::)
    }
}

// ---- telemetry -------------------------------------------------------------------------
int32_t Forge::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kScope] = {};
    const Cfg c = cfg();
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeak] = outPkA_.load(std::memory_order_relaxed);
    t[S_InRms] = inRmsA_.load(std::memory_order_relaxed);
    t[S_OutRms] = outRmsA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_Latency] = (float)latencySamples();
    t[S_OsFactor] = (float)(1 << osIndex());
    t[S_Lfo] = lfoLive_.load(std::memory_order_relaxed);
    const float mod = driveModLive_.load(std::memory_order_relaxed);
    t[S_DriveMod] = mod;
    t[S_Env] = envA_.load(std::memory_order_relaxed);
    t[S_ToneNow] = toneLive_.load(std::memory_order_relaxed);
    t[S_InLevel] = inLvlA_.load(std::memory_order_relaxed);
    t[S_Signal] = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
    t[S_Alias] = aliasing(c) ? 1.0f : 0.0f;
    t[S_Routing] = (float)c.routing;
    t[S_OnCount] = (float)onCount(c);
    const bool sync = get(LfoSync) >= 0.5f;
    const double bpm = spb_ > 0.0 ? 60.0 * sr_ / spb_ : 0.0;
    t[S_LfoHz] = (float)(sync ? (bpm > 0 ? bpm / 60.0 / kLfoDiv[divIndex()] : 0.0) : expMap(get(LfoRate), 0.05, 20.0));
    t[S_Bpm] = (float)bpm;
    t[S_AmountDb] = get(Amount) * 30.0f;
    t[S_OutputDb] = (get(Output) - 0.5f) * 48.0f;

    // Harmonics: chain + each stage alone, the master and stage tilts leaning the partials.
    const float masterTiltDb = 20.0f * std::log10(std::max(0.06f, 1.0f + t[S_ToneNow]));
    float chainTilt = 0.0f;
    int n = 0;
    for (int s = 0; s < kStages; ++s)
        if (c.on[s]) {
            chainTilt += c.toneDb[s];
            ++n;
        }
    if (c.routing == 1 && n > 0) chainTilt /= (float)n;
    float odd, even;
    t[S_ThdChain] =
        harmonics([&](float x) { return chainStatic(c, x, 0.0f); }, masterTiltDb + chainTilt, t + kHarmOff, odd, even);
    t[S_FlavorChain] = (float)flavor(t[S_ThdChain], odd, even);
    for (int s = 0; s < kStages; ++s) {
        t[S_ThdS1 + s] = harmonics([&](float x) { return stageFull(c, s, x, 0.0f); }, masterTiltDb + c.toneDb[s],
            t + kHarmOff + kHarm * (s + 1), odd, even);
        t[S_FlavorS1 + s] = (float)flavor(t[S_ThdS1 + s], odd, even);
    }

    // Transfer curves over input −1..+1: static and with the live LFO drive offset.
    for (int i = 0; i < kPts; ++i) {
        const float x = -1.0f + 2.0f * (float)i / (float)(kPts - 1);
        float* cv = t + kCurveOff;
        cv[0 * kPts + i] = chainStatic(c, x, 0.0f);
        cv[1 * kPts + i] = chainStatic(c, x, mod);
        for (int s = 0; s < kStages; ++s) {
            cv[(2 + s * 2) * kPts + i] = stageFull(c, s, x, 0.0f);
            cv[(3 + s * 2) * kPts + i] = stageFull(c, s, x, mod);
        }
    }
    const int n2 = std::min<int32_t>(maxSamples, kScope);
    for (int i = 0; i < n2; ++i) out[i] = std::isfinite(t[i]) ? t[i] : 0.0f;
    return n2;
}

std::string Forge::deviceText(int32_t id) const {
    const Cfg c = cfg();
    char b[1200];
    if (id == 0) {
        std::string s = kRouteNames[c.routing];
        std::snprintf(b, sizeof b, " - amount +%.1f dB - wet %.0f %% - output %+.1f dB", get(Amount) * 30.0f,
            c.wet * 100.0f, (get(Output) - 0.5f) * 48.0f);
        s += b;
        for (int st = 0; st < kStages; ++st) {
            if (!c.on[st]) {
                std::snprintf(b, sizeof b, " - S%d off", st + 1);
                s += b;
                continue;
            }
            std::snprintf(b, sizeof b, " - S%d %s%s drive %.0f %%", st + 1, kAlgoNames[c.type[st]],
                roleWord(c.routing, st), get(S1Drive + st * 5) * 100.0f);
            s += b;
            if (std::fabs(get(S1Out + st * 5) - 0.5f) > 0.002f) {
                std::snprintf(b, sizeof b, " out %+.1f dB", (get(S1Out + st * 5) - 0.5f) * 24.0f);
                s += b;
            }
            if (get(S1FB + st * 5) > 0.005f) {
                std::snprintf(b, sizeof b, " fb %.0f %%", get(S1FB + st * 5) * 100.0f);
                s += b;
            }
            if (std::fabs(get(S1Bias + st * 3) - 0.5f) > 0.002f) {
                std::snprintf(b, sizeof b, " bias %+.0f %%", (get(S1Bias + st * 3) - 0.5f) * 200.0f);
                s += b;
            }
            if (std::fabs(c.toneDb[st]) > 0.05f) {
                std::snprintf(b, sizeof b, " tone %+.1f dB", c.toneDb[st]);
                s += b;
            }
            if (std::fabs(c.width[st] - 1.0f) > 0.005f) {
                std::snprintf(b, sizeof b, " width %.0f %%", c.width[st] * 100.0f);
                s += b;
            }
        }
        if (get(LfoDrive) > 0.005f) {
            std::snprintf(b, sizeof b, " - LFO -> drive %.0f %% at %s", get(LfoDrive) * 100.0f, rateText().c_str());
            s += b;
        }
        if (get(EnvTone) > 0.005f) {
            std::snprintf(b, sizeof b, " - env -> tone %.0f %%", get(EnvTone) * 100.0f);
            s += b;
        }
        if (std::fabs(get(Tone) - 0.5f) > 0.002f) {
            std::snprintf(b, sizeof b, " - master tone %+.0f %%", (get(Tone) - 0.5f) * 200.0f);
            s += b;
        }
        if (std::fabs(get(Bias) - 0.5f) > 0.002f) {
            std::snprintf(b, sizeof b, " - master bias %+.0f %%", (get(Bias) - 0.5f) * 200.0f);
            s += b;
        }
        if (std::fabs(get(Width) - 0.5f) > 0.002f) {
            std::snprintf(b, sizeof b, " - master width %.0f %%", get(Width) * 200.0f);
            s += b;
        }
        std::snprintf(
            b, sizeof b, " - oversampling %s", osIndex() == 0 ? "off" : (std::to_string(1 << osIndex()) + "x").c_str());
        s += b;
        if (aliasing(c)) s += " (Digital / Fold without oversampling aliases)";
        return s;
    }
    if (id == 1) {
        float sc[kCurveOff];
        scopeRead(sc, kCurveOff);
        auto lv = [](float v) {
            char t[24];
            if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf");
            else
                std::snprintf(t, sizeof t, "%.1f", v);
            return std::string(t);
        };
        static const char* fl[4] = {"clean", "odd-heavy", "even-heavy", "mixed"};
        std::snprintf(b, sizeof b,
            "in %s dBFS peak / %s RMS - out %s dBFS peak / %s RMS - THD (-6 dB sine) %.1f %% %s "
            "(S1 %.1f %%, S2 %.1f %%, S3 %.1f %%) - LFO %.2f (drive %+.2f) at %.2f Hz - envelope %.2f - "
            "master tilt %+.2f - oversampling %.0fx, latency %.0f smp - CPU %.2f %%",
            lv(sc[S_InPeak]).c_str(), lv(sc[S_InRms]).c_str(), lv(sc[S_OutPeak]).c_str(), lv(sc[S_OutRms]).c_str(),
            sc[S_ThdChain], fl[std::clamp((int)sc[S_FlavorChain], 0, 3)], sc[S_ThdS1], sc[S_ThdS2], sc[S_ThdS3],
            sc[S_Lfo], sc[S_DriveMod], sc[S_LfoHz], sc[S_Env], sc[S_ToneNow], sc[S_OsFactor], sc[S_Latency],
            sc[S_Cpu] * 100.0f);
        return b;
    }
    if (id == 2) {
        return "All params are normalized 0..1. Amount: 0..+30 dB into the stages (0.35 = +10.5 dB). Wet: dry/wet. "
               "Output: -24..+24 dB (0.5 = 0 dB). Routing: 0 Serial (1 -> 2 -> 3), 0.333 Parallel (averaged), 0.667 "
               "Mid/Side (S1 = Mid, S2 = Side, S3 = the recombined M+S), 1 Multiband (S1 < 180 Hz, S2 mids, S3 > 2.4 "
               "kHz; "
               "a stage that is off passes its band clean). Per stage n (1..3): Sn Type round(v*5): 0 Tube, 0.2 Diode, "
               "0.4 Tape, 0.6 Fuzz, 0.8 Digital, 1 Fold; Sn Drive 0..1 (gain 1 + 20 v^2, auto-makeup); Sn Out -12..+12 "
               "dB "
               "(0.5 = 0); Sn FB self-feedback 0..85 %; Sn On >= 0.5; Sn Bias asymmetry -100..+100 % (0.5 = none); Sn "
               "Tone "
               "a tilt after the stage, -12..+12 dB (0.5 = flat); Sn Width the stage's stereo image 0..200 % (0.5 = "
               "100 %; "
               "in Mid/Side, S2's width scales the side). Master offsets: Bias adds to every stage's bias, Tone is the "
               "post "
               "tilt (0.5 = flat) that Env Tone pushes brighter on loud parts, Width the post image 0..200 %. LFO "
               "Drive: "
               "LFO -> every stage's drive, up to +-0.3. LFO Rate: free 0.05..20 Hz exponential, or with LFO Sync >= "
               "0.5 "
               "a division round(v*7): 2/1, 1/1, 1/2, 1/4, 1/8, 1/16, 1/32, 1/64 (0.43 = 1/4). Oversampling "
               "round(v*3): "
               "Off, 2x, 4x, 8x (0 latency, minimum phase).";
    }
    return {};
}

void Forge::setStage(int s, float type, float drive, float out, float fb, float on) {
    p_[S1Type + s * 5].store(type / (kAlgos - 1));
    p_[S1Drive + s * 5].store(drive);
    p_[S1Out + s * 5].store(out);
    p_[S1FB + s * 5].store(fb);
    p_[S1On + s * 5].store(on);
}

void Forge::clearState() {
    for (auto& z : fb_) z = 0;
    for (auto& z : tilt_) z = 0;
    for (auto& z : lpTone_) z = 0;
    for (auto& z : mbLo_) z = 0;
    for (auto& z : mbHi_) z = 0;
    env_ = 0;
    lfoPhase_ = 0;
    inPkM_ = outPkM_ = 0;
    msIn_ = msOut_ = 0;
    inLvl_ = 0;
}

Forge::Cfg Forge::cfg() const {
    Cfg c;
    const float masterBias = (get(Bias) - 0.5f) * 2.0f;
    for (int s = 0; s < kStages; ++s) {
        c.type[s] = std::clamp((int)std::lround(get(S1Type + s * 5) * (kAlgos - 1)), 0, kAlgos - 1);
        c.drive[s] = std::clamp(get(S1Drive + s * 5), 0.0f, 1.0f);
        c.trim[s] = std::pow(10.0f, (get(S1Out + s * 5) - 0.5f) * 24.0f / 20.0f);
        c.fb[s] = std::clamp(get(S1FB + s * 5), 0.0f, 1.0f) * 0.85f;
        c.on[s] = get(S1On + s * 5) >= 0.5f;
        c.bias[s] = std::clamp(masterBias + (get(S1Bias + s * 3) - 0.5f) * 2.0f, -1.0f, 1.0f);
        c.toneDb[s] = (get(S1Tone + s * 3) - 0.5f) * 24.0f;
        c.lowG[s] = std::pow(10.0f, -c.toneDb[s] / 40.0f);
        c.highG[s] = std::pow(10.0f, c.toneDb[s] / 40.0f);
        c.width[s] = std::clamp(get(S1Width + s * 3), 0.0f, 1.0f) * 2.0f;
    }
    c.amt = std::pow(10.0f, get(Amount) * 30.0f / 20.0f);
    c.wet = std::clamp(get(Wet), 0.0f, 1.0f);
    c.outGain = std::pow(10.0f, (get(Output) - 0.5f) * 48.0f / 20.0f);
    c.routing = std::clamp((int)std::lround(get(Routing) * 3.0f), 0, 3);
    return c;
}

bool Forge::aliasing(const Cfg& c) const {
    if (osIndex() != 0) return false;
    for (int s = 0; s < kStages; ++s)
        if (c.on[s] && (c.type[s] == 4 || c.type[s] == 5)) return true;
    return false;
}

const char* Forge::roleWord(int routing, int s) {
    static const char* ms[3] = {" (mid)", " (side)", " (M+S)"};
    static const char* mb[3] = {" (low)", " (mid)", " (high)"};
    return routing == 2 ? ms[s] : routing == 3 ? mb[s] : "";
}

std::string Forge::rateText() const {
    char b[48];
    if (get(LfoSync) >= 0.5f) std::snprintf(b, sizeof b, "%s sync", kDivNames[divIndex()]);
    else
        std::snprintf(b, sizeof b, "%.2f Hz", expMap(get(LfoRate), 0.05, 20.0));
    return b;
}

// ---- DSP ---------------------------------------------------------------------------------
// One stage's shaper on one channel, with self-feedback and the stage tilt. `slot` = stage*2+ch.
float Forge::shapeCh(const Cfg& c, const Core& k, int s, int slot, float driveMod, float x) {
    const float g = driveGain(std::clamp(c.drive[s] + driveMod, 0.0f, 1.0f));
    const float in = (x + c.fb[s] * fb_[slot]) * g;
    float y = shape(c.type[s], in, c.bias[s]) * (1.0f / std::sqrt(g)); // gentle auto-makeup
    fb_[slot] = y;
    y *= c.trim[s];
    float& lp = tilt_[slot];
    lp = y + k.tiltCoef * (lp - y);
    return lp * c.lowG[s] + (y - lp) * c.highG[s];
}

// A stage on a stereo pair: both channels, then the stage's width.
void Forge::stagePair(const Cfg& c, const Core& k, int s, float driveMod, float& l, float& r) {
    l = shapeCh(c, k, s, s * 2, driveMod, l);
    r = shapeCh(c, k, s, s * 2 + 1, driveMod, r);
    if (c.width[s] != 1.0f) {
        const float m = 0.5f * (l + r), sd = 0.5f * (l - r) * c.width[s];
        l = m + sd;
        r = m - sd;
    }
}

void Forge::route(const Cfg& c, const Core& k, float driveMod, float& l, float& r) {
    switch (c.routing) {
    case 0: // Serial
        for (int s = 0; s < kStages; ++s)
            if (c.on[s]) stagePair(c, k, s, driveMod, l, r);
        break;
    case 1: { // Parallel: every stage from the same input, averaged
        float sl = 0, sr = 0;
        int n = 0;
        for (int s = 0; s < kStages; ++s)
            if (c.on[s]) {
                float a = l, b = r;
                stagePair(c, k, s, driveMod, a, b);
                sl += a;
                sr += b;
                ++n;
            }
        if (n) {
            l = sl / (float)n;
            r = sr / (float)n;
        }
        break;
    }
    case 2: { // Mid/Side: S1 → Mid, S2 → Side (width scales it), S3 → the recombined stereo
        float m = 0.5f * (l + r), sd = 0.5f * (l - r);
        if (c.on[0]) m = shapeCh(c, k, 0, 0, driveMod, m);
        if (c.on[1]) sd = shapeCh(c, k, 1, 3, driveMod, sd) * c.width[1];
        l = m + sd;
        r = m - sd;
        if (c.on[2]) stagePair(c, k, 2, driveMod, l, r);
        break;
    }
    default: { // Multiband: S1 lows, S2 mids, S3 highs (one-pole split, sums back flat)
        float bl[3], br[3];
        split(0, l, k, bl);
        split(1, r, k, br);
        l = r = 0;
        for (int s = 0; s < kStages; ++s) {
            if (c.on[s]) stagePair(c, k, s, driveMod, bl[s], br[s]);
            l += bl[s];
            r += br[s];
        }
    }
    }
}

void Forge::split(int ch, float x, const Core& k, float* band) {
    float& lo = mbLo_[ch];
    float& hi = mbHi_[ch];
    lo = x + k.loCoef * (lo - x); // low band (LP 180 Hz)
    const float aboveLo = x - lo;
    hi = aboveLo + k.hiCoef * (hi - aboveLo); // mid band (LP 2.4 kHz of the part above 180 Hz)
    band[0] = lo;
    band[1] = hi;
    band[2] = aboveLo - hi;
}

// ---- static model (UI / MCP) -------------------------------------------------------------
// A stage's static (DC) response to the pre-gained input v: the feedback settles on a fixed
// point (y = f((v + fb·y)·g)), found by a few damped iterations. Tilt / width are frequency /
// stereo effects and don't bend the curve.
float Forge::stageStaticY(const Cfg& c, int s, float v, float mod) {
    const float g = driveGain(std::clamp(c.drive[s] + mod, 0.0f, 1.0f));
    const float mk = 1.0f / std::sqrt(g);
    float y = shape(c.type[s], v * g, c.bias[s]) * mk;
    if (c.fb[s] > 0.0f)
        for (int it = 0; it < 8; ++it) y = 0.5f * y + 0.5f * shape(c.type[s], (v + c.fb[s] * y) * g, c.bias[s]) * mk;
    return y * c.trim[s];
}

float Forge::finish(const Cfg& c, float x, float y) {
    return (c.wet * y + (1.0f - c.wet) * x) * c.outGain;
}

// The whole device on a mono input x (−1..1). Mid/Side: the mono signal is all Mid (S1 then
// S3); Multiband: a broadband sample — each band's shaper sees it, so the stages average.
float Forge::chainStatic(const Cfg& c, float x, float mod) {
    float v = x * c.amt, y = v;
    switch (c.routing) {
    case 0:
        for (int s = 0; s < kStages; ++s)
            if (c.on[s]) y = stageStaticY(c, s, y, mod);
        break;
    case 2:
        if (c.on[0]) y = stageStaticY(c, 0, y, mod);
        if (c.on[2]) y = stageStaticY(c, 2, y, mod);
        break;
    default: {
        float sum = 0;
        int n = 0;
        for (int s = 0; s < kStages; ++s)
            if (c.on[s]) {
                sum += stageStaticY(c, s, v, mod);
                ++n;
            }
        if (n) y = sum / (float)n;
    }
    }
    return finish(c, x, y);
}

float Forge::stageFull(const Cfg& c, int s, float x, float mod) {
    return finish(c, x, stageStaticY(c, s, x * c.amt, mod));
}

int Forge::flavor(float thd, float odd, float even) {
    if (thd < 0.3f) return 0;
    if (odd > even * 1.6f) return 1;
    if (even > odd * 1.6f) return 2;
    return 3;
}

} // namespace nota
