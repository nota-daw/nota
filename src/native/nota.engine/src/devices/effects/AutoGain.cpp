// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Level implementation. K-weighting on
// shared dsp::BiquadSection stages; per-sample dB mapping via FastMath;
// auto-sleep (the silence-hold design keeps frozen gains correct; the limiter
// delay advances through zeros).

#include "AutoGain.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {

AutoGain::AutoGain() {
    p_[Target].store(0.611f); // −14 LUFS
    p_[Scale].store(0.5f);    // Short-term
    p_[Auto].store(1.0f);
    p_[Trim].store(0.5f);     // 0 dB
    p_[Response].store(1.0f); // Slow
    p_[Window].store(0.841f); // 6 s
    p_[MaxGain].store(0.5f);  // 12 dB
    p_[Safe].store(1.0f);
    p_[Ceiling].store(0.833f); // −1 dBTP
    p_[Gain].store(0.5f);      // 0 dB
    setSampleRate(44100.0, 0);
}

void AutoGain::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    computeKWeighting();
    kIn_[0] = kIn_[1] = kSc_[0] = kSc_[1] = kOut_[0] = kOut_[1] = KW{};
    msMom_ = msMomG_ = msShortG_ = msIntAbs_ = msInt_ = msShort_ = kFloor;
    msScMom_ = msScMeas_ = kFloor;
    msOutShort_ = msOutMom_ = kFloor;
    cLR_ = cLL_ = cRR_ = 1e-7;
    primed_ = scPrimed_ = false;
    sigRun_ = scRun_ = 0;
    levelDb_ = 0.0f;
    limGrSm_ = 0.0f;
    limHold_ = 0.0f;
    tpPk_ = 0.0f;
    for (int c = 0; c < 2; ++c) {
        tp0_[c] = tp1_[c] = tp2_[c] = 0.0f;
        ti0_[c] = ti1_[c] = ti2_[c] = 0.0f;
    }
    // Look-ahead buffers sized for the longest window at this rate (allocation-free after).
    wMax_ = (int)std::ceil(0.020 * sr_) + 1;
    cap_ = wMax_ + 4;
    dly_.assign((size_t)cap_ * 2, 0.0f);
    box_.assign((size_t)cap_, 1.0f);
    dqV_.assign((size_t)cap_, 1.0f);
    dqT_.assign((size_t)cap_, 0);
    curW_ = -1; // force a limiter reset on the next block
    quietSamples_ = 0;
    idle_ = false;
    latency_.store(0, std::memory_order_relaxed);
}

void AutoGain::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

void AutoGain::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id == 0) reseedReq_.store(true, std::memory_order_release);
    else if (id == 1 && primedA_.load(std::memory_order_relaxed)) {
        // MATCH: the gain that puts the measured input on the target (after Trim), in Manual.
        const float mg = get(MaxGain) * 24.0f;
        const float want = std::clamp(matchGainDb(), -mg, mg);
        p_[Gain].store(std::clamp(0.5f + want / 48.0f, 0.0f, 1.0f), std::memory_order_relaxed);
        p_[Auto].store(0.0f, std::memory_order_relaxed);
    }
}

float AutoGain::matchGainDb() const {
    return target_.load(std::memory_order_relaxed) - measured_.load(std::memory_order_relaxed) - trimDb();
}

double AutoGain::expMap(float v, double lo, double hi) {
    return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
}

float AutoGain::db(float lin) {
    return 20.0f * std::log10(std::max(1e-6f, lin));
}

float AutoGain::lufs(double ms) {
    return ms > 1e-10 ? (float)(-0.691 + 10.0 * std::log10(ms)) : -120.0f;
}

void AutoGain::resetLimiter(int W) {
    curW_ = W;
    std::fill(dly_.begin(), dly_.end(), 0.0f);
    std::fill(box_.begin(), box_.end(), 1.0f);
    boxSum_ = (double)std::max(0, W);
    boxI_ = 0;
    dqH_ = 0;
    dqN_ = 0;
    t_ = 0;
    dW_ = 0;
    env_ = 1.0f;
    for (int c = 0; c < 2; ++c) ti0_[c] = ti1_[c] = ti2_[c] = 0.0f;
}

float AutoGain::kweight(KW& s, float x) {
    float y1 = b0_[0] * x + s.z1a;
    s.z1a = b1_[0] * x - a1_[0] * y1 + s.z1b;
    s.z1b = b2_[0] * x - a2_[0] * y1;
    float y2 = b0_[1] * y1 + s.z2a;
    s.z2a = b1_[1] * y1 - a1_[1] * y2 + s.z2b;
    s.z2b = b2_[1] * y1 - a2_[1] * y2;
    return y2;
}

void AutoGain::computeKWeighting() {
    {
        double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
        double K = std::tan(kPi * f0 / sr_);
        double Vh = std::pow(10.0, G / 20.0), Vb = std::pow(Vh, 0.4996667741545416);
        double a0 = 1.0 + K / Q + K * K;
        b0_[0] = (float)((Vh + Vb * K / Q + K * K) / a0);
        b1_[0] = (float)(2.0 * (K * K - Vh) / a0);
        b2_[0] = (float)((Vh - Vb * K / Q + K * K) / a0);
        a1_[0] = (float)(2.0 * (K * K - 1.0) / a0);
        a2_[0] = (float)((1.0 - K / Q + K * K) / a0);
    }
    {
        double f0 = 38.13547087602444, Q = 0.5003270373238773;
        double K = std::tan(kPi * f0 / sr_);
        double a0 = 1.0 + K / Q + K * K;
        a1_[1] = (float)(2.0 * (K * K - 1.0) / a0);
        a2_[1] = (float)((1.0 - K / Q + K * K) / a0);
        b0_[1] = (float)(1.0 / a0);
        b1_[1] = (float)(-2.0 / a0);
        b2_[1] = (float)(1.0 / a0);
    }
    coefInt_ = coefFor(12.0);
}

float AutoGain::catmull(float y0, float y1, float y2, float y3, float t) {
    const float b = 0.5f * (y2 - y0), d = 0.5f * (y3 - y1);
    return y1 + t * (b + t * ((3 * (y2 - y1) - 2 * b - d) + t * (2 * (y1 - y2) + b + d)));
}

float AutoGain::truePeak(int c, float x) {
    float y0 = tp0_[c], y1 = tp1_[c], y2 = tp2_[c], y3 = x;
    float pk = std::fabs(y2);
    for (int k = 1; k < 4; ++k) pk = std::max(pk, std::fabs(catmull(y0, y1, y2, y3, k * 0.25f)));
    tp0_[c] = y1;
    tp1_[c] = y2;
    tp2_[c] = y3;
    return pk;
}

float AutoGain::tpInterp(int c, float x) {
    float y0 = ti0_[c], y1 = ti1_[c], y2 = ti2_[c], y3 = x;
    float pk = std::max(std::fabs(y1), std::max(std::fabs(y2), std::fabs(y3)));
    for (int k = 1; k < 4; ++k) pk = std::max(pk, std::fabs(catmull(y0, y1, y2, y3, k * 0.25f)));
    ti0_[c] = y1;
    ti1_[c] = y2;
    ti2_[c] = y3;
    return pk;
}

void AutoGain::process(float* buf, int32_t frames) {
    const double targetP = -36.0 + (double)get(Target) * 36.0;
    const int scale = scaleIndex();
    const bool autoOn = get(Auto) >= 0.5f;
    const float trim = trimDb();
    const bool fast = get(Response) < 0.5f;
    const double winSec = expMap(get(Window), 0.4, 10.0);
    const float maxG = get(MaxGain) * 24.0f;
    const float manualDb = std::clamp((get(Gain) - 0.5f) * 48.0f, -maxG, maxG);
    const bool safe = get(Safe) >= 0.5f;
    const float ceilLin = std::pow(10.0f, ceilingDb() / 20.0f);

    const double measTau = scale == 0 ? 0.4 : scale == 1 ? 3.0 : 12.0;
    const float measC = coefFor(measTau), momC = coefFor(0.4), shortC = coefFor(3.0), corrC = coefFor(0.4);
    const float glide = coefFor(fast ? winSec * 0.25 : winSec);
    const float manGlide = coefFor(0.02);
    const float relC = coefFor(fast ? 0.06 : 0.2);
    const float holdDecay = coefFor(1.0);
    const int primeN = (int)(0.4 * sr_);
    // Hold gate: too quiet to level (see the header), as a K-weighted mean square.
    const double gateLufs = std::max(-70.0, (double)curTarget_ - maxG - 12.0);
    const double gateMs = std::pow(10.0, (gateLufs + 0.691) / 10.0);

    // Limiter window follows Safe + Response; a change restarts it (a brief gap, rare).
    const int W = safe ? lookW() : 0;
    if (W != curW_) resetLimiter(W);

    if (reseedReq_.exchange(false, std::memory_order_acq_rel)) {
        if (msMom_ > kGateMs) {
            msMomG_ = msShortG_ = msIntAbs_ = msInt_ = msMom_;
            primed_ = true;
        }
        if (msScMom_ > kGateMs) {
            msScMeas_ = msScMom_;
            scPrimed_ = true;
        }
    }

    const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
    const bool useSc = sc != nullptr && scTrackId_.load(std::memory_order_relaxed) >= 0;
    if (!useSc) scPrimed_ = false;

    // Idle skip: silent key on settled gains leaves the buffer untouched. The
    // device's own silence-hold keeps frozen measurements correct; the limiter
    // snaps to its settled-silence state (unity envelope/box/deque) and the
    // delay advances through zeros, so PDC alignment survives the sleep.
    // Momentary/short-term meters keep decaying in closed form.
    const bool selfSilent = simd_audio::isSilent(buf, frames * 2);
    const bool keySilent = sc == nullptr || simd_audio::isSilent(sc, frames * 2);
    if (selfSilent && keySilent && idle_) {
        for (int32_t i = 0; i < frames; ++i) {
            dly_[dW_ * 2] = 0.0f;
            dly_[dW_ * 2 + 1] = 0.0f;
            dW_ = (dW_ + 1) % cap_;
            ++t_;
        }
        if (W > 0) {
            env_ = 1.0f;
            std::fill(box_.begin(), box_.end(), 1.0f);
            boxSum_ = (double)std::max(0, W);
            boxI_ = 0;
            dqH_ = 0;
            dqN_ = 1;
            dqV_[0] = 1.0f;
            dqT_[0] = t_;
        }
        msMom_ *= std::pow(1.0 - momC, frames);
        msShort_ *= std::pow(1.0 - shortC, frames);
        msOutShort_ *= std::pow(1.0 - shortC, frames);
        msOutMom_ *= std::pow(1.0 - momC, frames);
        inLufs_.store(lufs(msShort_), std::memory_order_relaxed);
        outLufs_.store(lufs(msOutShort_), std::memory_order_relaxed);
        inMom_.store(lufs(msMom_), std::memory_order_relaxed);
        outMom_.store(lufs(msOutMom_), std::memory_order_relaxed);
        truePeak_.store(-120.0f, std::memory_order_relaxed);
        // Replicated hold gate (cheap): silence must be published — freezing
        // it at 0 would hold the UI in 'tracking' forever (smoke tripwire).
        const bool audibleNow = msMom_ >= gateMs;
        const bool silentNow = !audibleNow || (primed_ && msMom_ < msIntAbs_ * kHoldGate);
        silent_.store(silentNow ? 1 : 0, std::memory_order_relaxed);
        primedA_.store(primed_ ? 1 : 0, std::memory_order_relaxed);
        scBuf_ = nullptr;
        return;
    }

    float level = levelDb_;
    int clamp = 0;
    bool silent = false;
    float limGr = 0.0f;
    double desired = 0.0;
    float outPk = 0.0f;

    for (int32_t i = 0; i < frames; ++i) {
        const float l = buf[i * 2], r = buf[i * 2 + 1];

        // ---- measurement (K-weighted mean square) ----
        const float kl = kweight(kIn_[0], l), kr = kweight(kIn_[1], r);
        const double ms = (double)kl * kl + (double)kr * kr;
        msMom_ += (ms - msMom_) * momC;
        msShort_ += (ms - msShort_) * shortC;
        const bool audible = msMom_ >= gateMs;
        // msIntAbs_ (the 12 s level) follows everything audible, so the relative hold below
        // lets go after a lasting drop.
        if (audible && primed_) msIntAbs_ += (msMom_ - msIntAbs_) * coefInt_;
        silent = !audible || (primed_ && msMom_ < msIntAbs_ * kHoldGate);
        if (!silent) {
            if (!primed_) {
                if (++sigRun_ >= primeN) {
                    msMomG_ = msShortG_ = msIntAbs_ = msInt_ = msMom_;
                    primed_ = true;
                }
            } else {
                msMomG_ += (ms - msMomG_) * momC;
                msShortG_ += (ms - msShortG_) * shortC;
                if (msMom_ > msIntAbs_ * kRelGate) msInt_ += (msMom_ - msInt_) * coefInt_;
            }
        } else if (!audible)
            sigRun_ = 0;
        const double meas = scale == 0 ? msMomG_ : scale == 1 ? msShortG_ : msInt_;

        // ---- target: the param, or the reference's loudness on the same scale ----
        double targetLufs = targetP;
        if (useSc) {
            const float sl = kweight(kSc_[0], sc[i * 2]), sr2 = kweight(kSc_[1], sc[i * 2 + 1]);
            const double sms = (double)sl * sl + (double)sr2 * sr2;
            msScMom_ += (sms - msScMom_) * momC;
            if (msScMom_ > kGateMs) {
                if (!scPrimed_) {
                    if (++scRun_ >= primeN) {
                        msScMeas_ = msScMom_;
                        scPrimed_ = true;
                    }
                } else
                    msScMeas_ += (sms - msScMeas_) * measC;
            } else
                scRun_ = 0;
            if (scPrimed_) targetLufs = lufs(msScMeas_);
        }

        // ---- correction ----
        desired = targetLufs - lufs(meas);
        clamp = desired > maxG ? 1 : desired < -maxG ? 2 : 0;
        desired = std::clamp(desired, -(double)maxG, (double)maxG);
        const bool hold = !primed_ || silent || (useSc && !scPrimed_);
        if (autoOn) {
            if (!hold) level += (float)(desired - level) * glide;
        } else
            level += (manualDb - level) * manGlide;
        level = std::clamp(level, -maxG, maxG);

        const float gLin = fast_math::exp2f((level + trim) * 0.166096404744f);
        float xl = l * gLin, xr = r * gLin;

        // ---- look-ahead true-peak limiter ----
        float ol = xl, or_ = xr;
        if (W > 0) {
            // Peak of the last three samples and the 4× interpolation between the older two,
            // so the window below covers the in-between peak before it leaves the delay.
            const float pk = std::max(tpInterp(0, xl), tpInterp(1, xr));
            const float req = pk > ceilLin ? ceilLin / pk : 1.0f;
            // Sliding minimum over W samples (monotonic deque).
            while (dqN_ > 0 && dqV_[(dqH_ + dqN_ - 1) % cap_] >= req) --dqN_;
            dqV_[(dqH_ + dqN_) % cap_] = req;
            dqT_[(dqH_ + dqN_) % cap_] = t_;
            ++dqN_;
            while (dqT_[dqH_] <= t_ - W) {
                dqH_ = (dqH_ + 1) % cap_;
                --dqN_;
            }
            const float held = dqV_[dqH_];
            // Release upward only; attack is instant (the box below smooths it).
            env_ = held < env_ ? held : env_ + (held - env_) * relC;
            // Box average of the envelope over W: never above any req its window covers.
            boxSum_ += (double)env_ - (double)box_[boxI_];
            box_[boxI_] = env_;
            boxI_ = (boxI_ + 1) % W;
            const float g = (float)std::min(1.0, boxSum_ / (double)W);
            // Delay the audio by W + 1.
            const int D = W + 1;
            dly_[dW_ * 2] = xl;
            dly_[dW_ * 2 + 1] = xr;
            const int rd = (dW_ - D + cap_) % cap_;
            ol = dly_[rd * 2] * g;
            or_ = dly_[rd * 2 + 1] * g;
            dW_ = (dW_ + 1) % cap_;
            ++t_;
            limGr = std::max(0.0f, -fast_math::linToDb(std::max(1e-6f, g)));
            ol = std::clamp(ol, -ceilLin, ceilLin);
            or_ = std::clamp(or_, -ceilLin, ceilLin);
        }
        limGrSm_ += (limGr - limGrSm_) * measC;
        limHold_ = std::max(limGr, limHold_ + (0.0f - limHold_) * holdDecay);

        buf[i * 2] = ol;
        buf[i * 2 + 1] = or_;
        outPk = std::max(outPk, std::max(std::fabs(ol), std::fabs(or_)));

        // ---- output metering (what actually ships) ----
        const float ko = kweight(kOut_[0], ol), koR = kweight(kOut_[1], or_);
        const double oms = (double)ko * ko + (double)koR * koR;
        msOutShort_ += (oms - msOutShort_) * shortC;
        msOutMom_ += (oms - msOutMom_) * momC;
        tpPk_ = std::max(tpPk_ * 0.9995f, std::max(truePeak(0, ol), truePeak(1, or_)));
        cLR_ += ((double)ol * or_ - cLR_) * corrC;
        cLL_ += ((double)ol * ol - cLL_) * corrC;
        cRR_ += ((double)or_ * or_ - cRR_) * corrC;
        curTarget_ = (float)targetLufs;
    }

    if (selfSilent && keySilent && outPk <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > kIdleSamples) idle_ = true;

    levelDb_ = level;

    // ---- publish ----
    const double meas = scale == 0 ? msMomG_ : scale == 1 ? msShortG_ : msInt_;
    const float measL = primed_ ? lufs(meas) : -120.0f;
    const float outMeas = primed_ ? measL + level + trim - limGrSm_ : -120.0f;
    inLufs_.store(lufs(msShort_), std::memory_order_relaxed);
    outLufs_.store(lufs(msOutShort_), std::memory_order_relaxed);
    inMom_.store(lufs(msMom_), std::memory_order_relaxed);
    outMom_.store(lufs(msOutMom_), std::memory_order_relaxed);
    target_.store(curTarget_, std::memory_order_relaxed);
    applied_.store(level + trim - limGr, std::memory_order_relaxed);
    truePeak_.store(db(tpPk_), std::memory_order_relaxed);
    const double corr = cLR_ / std::sqrt(std::max(1e-12, cLL_ * cRR_));
    corr_.store((float)std::clamp(corr, -1.0, 1.0), std::memory_order_relaxed);
    scLufs_.store(useSc && scPrimed_ ? lufs(msScMeas_) : -120.0f, std::memory_order_relaxed);
    desired_.store((float)desired + trim, std::memory_order_relaxed);
    measured_.store(measL, std::memory_order_relaxed);
    outMeas_.store(outMeas, std::memory_order_relaxed);
    level_.store(level, std::memory_order_relaxed);
    limGr_.store(limGr, std::memory_order_relaxed);
    limHoldA_.store(limHold_, std::memory_order_relaxed);
    clamp_.store(autoOn ? clamp : 0, std::memory_order_relaxed);
    silent_.store(silent ? 1 : 0, std::memory_order_relaxed);
    primedA_.store(primed_ ? 1 : 0, std::memory_order_relaxed);
    latency_.store(W > 0 ? W + 1 : 0, std::memory_order_relaxed);
    scBuf_ = nullptr;
}

const char* AutoGain::paramName(int32_t i) const {
    static const char* nm[] = {
        "Target", "Scale", "Auto", "Trim", "Response", "Window", "Max Gain", "Safe", "Ceiling", "Gain"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float AutoGain::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void AutoGain::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

int32_t AutoGain::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kScope] = {};
    t[S_InLufs] = inLufs_.load(std::memory_order_relaxed);
    t[S_OutLufs] = outLufs_.load(std::memory_order_relaxed);
    t[S_InMom] = inMom_.load(std::memory_order_relaxed);
    t[S_Target] = target_.load(std::memory_order_relaxed);
    t[S_Applied] = applied_.load(std::memory_order_relaxed);
    t[S_TruePeak] = truePeak_.load(std::memory_order_relaxed);
    t[S_Corr] = corr_.load(std::memory_order_relaxed);
    t[S_ScLufs] = scLufs_.load(std::memory_order_relaxed);
    t[S_Desired] = desired_.load(std::memory_order_relaxed);
    t[S_Measured] = measured_.load(std::memory_order_relaxed);
    t[S_OutMeasured] = outMeas_.load(std::memory_order_relaxed);
    t[S_OutMom] = outMom_.load(std::memory_order_relaxed);
    t[S_Level] = level_.load(std::memory_order_relaxed);
    t[S_LimGr] = limGr_.load(std::memory_order_relaxed);
    t[S_Clamp] = (float)clamp_.load(std::memory_order_relaxed);
    t[S_Silent] = (float)silent_.load(std::memory_order_relaxed);
    t[S_Latency] = (float)latency_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Primed] = (float)primedA_.load(std::memory_order_relaxed);
    t[S_Delta] = t[S_Primed] > 0.5f ? t[S_OutMeasured] - t[S_Target] : 0.0f;
    t[S_LimHold] = limHoldA_.load(std::memory_order_relaxed);
    const int n = std::min<int32_t>(maxSamples, kScope);
    for (int i = 0; i < n; ++i) out[i] = std::isfinite(t[i]) ? t[i] : 0.0f;
    return n;
}

std::string AutoGain::deviceText(int32_t id) const {
    static const char* scaleNm[3] = {"momentary 400 ms", "short-term 3 s", "integrated (gated)"};
    char b[1400];
    const bool autoOn = get(Auto) >= 0.5f, safe = get(Safe) >= 0.5f, fast = get(Response) < 0.5f;
    const bool ref = scTrackId_.load(std::memory_order_relaxed) >= 0;
    if (id == 0) {
        std::string s;
        if (ref)
            std::snprintf(b, sizeof b, "target = the reference track (%d)", scTrackId_.load(std::memory_order_relaxed));
        else
            std::snprintf(b, sizeof b, "target %.1f LUFS", -36.0f + get(Target) * 36.0f);
        s += b;
        std::snprintf(b, sizeof b, " - %s - %s", scaleNm[scaleIndex()], autoOn ? "auto" : "manual");
        s += b;
        if (autoOn)
            std::snprintf(b, sizeof b, " (%s, glide %.1f s)", fast ? "fast" : "slow",
                expMap(get(Window), 0.4, 10.0) * (fast ? 0.25 : 1.0));
        else
            std::snprintf(b, sizeof b, " gain %+.1f dB", (get(Gain) - 0.5f) * 48.0f);
        s += b;
        std::snprintf(b, sizeof b, " - max gain %.0f dB", get(MaxGain) * 24.0f);
        s += b;
        if (std::fabs(trimDb()) > 0.05f) {
            std::snprintf(b, sizeof b, " - trim %+.1f dB", trimDb());
            s += b;
        }
        if (safe)
            std::snprintf(b, sizeof b, " - true-peak safe %.1f dBTP, look-ahead %d ms", ceilingDb(), fast ? 5 : 20);
        else
            std::snprintf(b, sizeof b, " - true-peak safe off");
        s += b;
        return s;
    }
    if (id == 1) {
        float t[kScope];
        scopeRead(t, kScope);
        auto lv = [](float v) {
            char x[24];
            if (v <= -119.0f) std::snprintf(x, sizeof x, "-inf");
            else
                std::snprintf(x, sizeof x, "%.1f", v);
            return std::string(x);
        };
        const char* state = t[S_Primed] < 0.5f   ? "waiting for signal"
                            : t[S_Silent] > 0.5f ? "input silent, holding"
                            : !autoOn            ? "manual"
                            : t[S_Clamp] > 0.5f  ? (t[S_Clamp] < 1.5f ? "held at +max gain" : "held at -max gain")
                            : std::fabs(t[S_Delta]) < 1.0f ? "on target"
                                                           : "tracking";
        std::snprintf(b, sizeof b,
            "%s - in %s LUFS (%s), momentary %s, short %s - out %s LUFS, momentary %s, short %s - target %s - "
            "delta %+.1f LU - correction %+.1f dB, applied %+.1f dB (wanted %+.1f) - limiter %.1f dB (held %.1f) - "
            "true peak %s dBTP - correlation %+.2f - latency %.0f smp",
            state, lv(t[S_Measured]).c_str(), scaleNm[scaleIndex()], lv(t[S_InMom]).c_str(), lv(t[S_InLufs]).c_str(),
            lv(t[S_OutMeasured]).c_str(), lv(t[S_OutMom]).c_str(), lv(t[S_OutLufs]).c_str(), lv(t[S_Target]).c_str(),
            t[S_Delta], t[S_Level], t[S_Applied], t[S_Desired], std::max(0.0f, t[S_LimGr]),
            std::max(0.0f, t[S_LimHold]), lv(t[S_TruePeak]).c_str(), t[S_Corr], t[S_Latency]);
        return b;
    }
    if (id == 2) {
        return "All params are normalized 0..1. Target: -36..0 LUFS (v = (LUFS + 36) / 36: 0.361 = -23, 0.556 = -16, "
               "0.611 = -14, 0.75 = -9); ignored while a reference track is routed (sidechain) - then the target is "
               "that "
               "track's loudness. Scale round(v*2): 0 Momentary (0.4 s), 0.5 Short-term (3 s), 1 Integrated (12 s with "
               "BS.1770 gating). Auto >= 0.5 rides the gain to the target; below, Manual applies Gain. Trim: -12..+12 "
               "dB "
               "after the correction (0.5 = 0). Response: 0 Fast (glide = Window / 4, look-ahead 5 ms), 1 Slow (glide "
               "= "
               "Window, look-ahead 20 ms). Window: the glide time 0.4 * 25^v s (0.626 = 3 s, 0.841 = 6 s, 1 = 10 s). "
               "Max Gain: 0..24 dB (v * 24), limits the correction both ways. Safe >= 0.5: the look-ahead true-peak "
               "limiter. Ceiling: -6..0 dBTP (v * 6 - 6; 0.833 = -1). Gain: the manual gain -24..+24 dB (0.5 = 0), "
               "limited to +-Max Gain. The measurement and the gain hold while the input is too quiet to level: more "
               "than 20 LU "
               "under its own 12 s level, more than Max Gain + 12 LU under the target, or under -70 LUFS.";
    }
    return {};
}

} // namespace nota
