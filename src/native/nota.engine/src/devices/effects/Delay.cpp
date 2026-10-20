// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Delay implementation. Main lines on
// shared dsp::DelayLine (bitmask wrap, Hermite reads); WOW LFO via
// fast_math::sin; tape saturation via tanhPrecise with hoisted inverse drive;
// silent loops auto-sleep (frozen loops excluded).

#include "Delay.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {

Delay::Delay() {
    set(SyncMode, 1.0f);
    set(TimeL, 0.35f);
    set(TimeR, 0.35f);
    set(DivL, 2.0f / 7.0f);
    set(DivR, 2.0f / 7.0f);
    set(LinkLR, 0.0f);
    set(Feedback, 0.38f);
    set(Spread, 0.0f);
    set(PingPong, 0.0f);
    set(WowRate, 0.3f);
    set(WowDepth, 0.15f);
    set(Freeze, 0.0f);
    set(DryWet, 0.3f);
    set(Output, 0.5f);
    set(DryLevel, 0.70711f);
    set(Diffuse, 0.0f);
    set(LowCut, 0.0f);
    set(HighCut, 1.0f);
    set(TapeMode, 0.0f);
    set(FadeChange, 1.0f);
    set(Width, 0.5f);
    set(BassMono, 0.0f);
    set(WetOnly, 0.0f);
    set(LatencyComp, 1.0f);
}

void Delay::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    const int need = static_cast<int>(sr_ * (kMaxTimeMs / 1000.0)) + static_cast<int>(sr_ * 0.12) + 4;
    bufL_.prepare(need);
    bufR_.prepare(need);
    // Diffusion allpasses: two mutually prime lengths per channel, R slightly offset so
    // the pair decorrelates. Lengths scale with the sample rate; the loop is longer by
    // their sum, which Latency Comp subtracts from the read pointer.
    ap_[0].resize(static_cast<int>(sr_ * 0.00201));
    ap_[1].resize(static_cast<int>(sr_ * 0.00343));
    ap_[2].resize(static_cast<int>(sr_ * 0.00229));
    ap_[3].resize(static_cast<int>(sr_ * 0.00387));
    fadeLen_ = std::max(64, static_cast<int>(sr_ * 0.030));
    fadePos_ = fadeLen_;
    curL_ = curR_ = oldL_ = oldR_ = 0.0;
    hpL_ = hpR_ = lpL_ = lpR_ = tapeL_ = tapeR_ = monoL_ = monoR_ = 0.0f;
    wowPhase_ = 0.0;
    quietSamples_ = 0;
    idle_ = false;
    loopPk_ = 0.0f;
    srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
}

void Delay::setTransport(double, double samplesPerBeat, bool) {
    if (samplesPerBeat > 0.0) spb_ = samplesPerBeat;
}

void Delay::wake() {
    idle_ = false;
    quietSamples_ = 0;
    loopPk_ = 0.0f;
}

double Delay::delaySamples(int timeParam, int divParam, bool sync) const {
    if (sync) return kDivBeats[divIndex(divParam)] * spb_;
    const double ms =
        std::clamp(static_cast<double>(clamp01(timeParam)) * kMaxTimeMs, 1.0, static_cast<double>(kMaxTimeMs));
    return ms * 0.001 * sr_;
}

void Delay::wipe() {
    bufL_.reset();
    bufR_.reset();
    for (auto& a : ap_) a.wipe();
    hpL_ = hpR_ = lpL_ = lpR_ = tapeL_ = tapeR_ = monoL_ = monoR_ = 0.0f;
    loopPk_ = 0.0f;
}

void Delay::process(float* buf, int32_t frames) {
    const int size = bufL_.capacity();
    if (size <= 4 || frames <= 0) return;
    const auto t0 = std::chrono::steady_clock::now();
    if (clear_.exchange(false, std::memory_order_relaxed)) wipe();

    const bool sync = on(SyncMode) && spb_ > 0.0;
    const bool link = on(LinkLR);
    const bool ping = on(PingPong);
    const bool freeze = on(Freeze);
    const bool tape = on(TapeMode);
    const bool fade = on(FadeChange);
    const bool wetOnly = on(WetOnly);

    const float fb = freeze ? 1.0f : std::clamp(get(Feedback), 0.0f, 0.98f);
    const float inGain = freeze ? 0.0f : 1.0f;
    const float mix = wetOnly ? 1.0f : clamp01(DryWet);
    const float dryG = wetOnly ? 0.0f : (1.0f - mix) * dryGain(clamp01(DryLevel));
    const float wetG = mix;
    const float outG = clamp01(Output) * 2.0f;
    const float width = clamp01(Width) * 2.0f;

    // ---- delay targets -------------------------------------------------------
    const float spreadMs = clamp01(Spread) * 50.0f;
    double tgtL = delaySamples(TimeL, DivL, sync);
    double tgtR = link ? tgtL : delaySamples(TimeR, DivR, sync);
    tgtR += spreadMs * 0.001 * sr_;
    const double diffuse = clamp01(Diffuse);
    const double apLen = static_cast<double>(ap_[0].n + ap_[1].n); // loop group delay
    if (on(LatencyComp)) {
        tgtL -= apLen;
        tgtR -= apLen;
    }
    const double lo = 1.0, hi = static_cast<double>(size) - 3.0;
    tgtL = std::clamp(tgtL, lo, hi);
    tgtR = std::clamp(tgtR, lo, hi);
    if (curL_ <= 0.0) {
        curL_ = tgtL;
        curR_ = tgtR;
        oldL_ = tgtL;
        oldR_ = tgtR;
    }

    if (fade) {
        // Crossfade to the new tap; a fade already running finishes first, so dragging
        // a time knob does not restart it every block.
        if (fadePos_ >= fadeLen_ && (std::fabs(tgtL - curL_) > 0.5 || std::fabs(tgtR - curR_) > 0.5)) {
            oldL_ = curL_;
            oldR_ = curR_;
            curL_ = tgtL;
            curR_ = tgtR;
            fadePos_ = 0;
        }
    } else {
        fadePos_ = fadeLen_; // repitch: one tap, glided below
    }
    // Tape glide: the read pointer moves 0.06 samples per sample, so a time change bends
    // the pitch by at most ±6 % while it slides — the tape-delay sound.
    constexpr double glide = 0.06;

    const double wowInc = expMap(clamp01(WowRate), 0.05, 8.0) / sr_;
    const float wowDepth = clamp01(WowDepth) * (tape ? 12.0f : 6.0f) * static_cast<float>(sr_ / 44100.0);

    // ---- loop tone (control rate) --------------------------------------------
    const float hpC = onePole(expMap(clamp01(LowCut), 20.0, 2000.0));
    const float lpC = onePole(expMap(clamp01(HighCut), 200.0, 20000.0));
    const bool hpOn = clamp01(LowCut) > 0.001f, lpOn = clamp01(HighCut) < 0.999f;
    const float tapeC = onePole(8000.0); // tape head loss
    const float drive = tape ? 1.0f + 2.0f * clamp01(Feedback) : 0.0f;
    const float invTanhDrive = tape ? 1.0f / fast_math::tanhPrecise(drive) : 1.0f; // hoisted reciprocal
    const float apG = static_cast<float>(0.62 * diffuse);
    const float monoC = onePole(std::max(20.0, expMap(clamp01(BassMono), 30.0, 500.0)));
    const bool monoOn = clamp01(BassMono) > 0.001f;

    // Idle skip: silent input on a settled loop leaves the buffer untouched. A
    // frozen loop never sleeps (infinite tail); the WOW phase keeps advancing
    // so the wake-up echo has no timing jump. Sleep engages only after the
    // loop's full flight time (an echo can arrive long after the input and the
    // output both read silent), tracked below via loopPk_/quietSamples_.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    if (inSilent && !freeze && idle_) {
        wowPhase_ += wowInc * frames;
        if (wowPhase_ >= 1.0) wowPhase_ -= std::floor(wowPhase_);
        return;
    }

    float wetPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f, pushPk = 0.0f;

    for (int32_t i = 0; i < frames; ++i) {
        if (!fade) {
            curL_ += std::clamp(tgtL - curL_, -glide, glide);
            curR_ += std::clamp(tgtR - curR_, -glide, glide);
        } else if (fadePos_ < fadeLen_) {
            ++fadePos_;
        }
        const float lfo = fast_math::sin((float)wowPhase_);
        wowPhase_ += wowInc;
        if (wowPhase_ >= 1.0) wowPhase_ -= 1.0;
        const double mod = static_cast<double>(lfo * wowDepth);

        float rdL, rdR;
        if (fadePos_ < fadeLen_) {
            const float t = static_cast<float>(fadePos_) / static_cast<float>(fadeLen_);
            const float a = 1.0f - t;
            // NOTE: DelayLine.readLinear(x) == orig read(x + 1) — the original
            // interpolates toward the newer sample, so all taps are offset by −1.
            rdL = bufL_.readLinear((float)(oldL_ + mod) - 1.0f) * a + bufL_.readLinear((float)(curL_ + mod) - 1.0f) * t;
            rdR = bufR_.readLinear((float)(oldR_ - mod) - 1.0f) * a + bufR_.readLinear((float)(curR_ - mod) - 1.0f) * t;
        } else {
            rdL = bufL_.readLinear((float)(curL_ + mod) - 1.0f);
            rdR = bufR_.readLinear((float)(curR_ - mod) - 1.0f);
        }

        const float l = buf[i * 2], r = buf[i * 2 + 1];
        float wl = ping ? rdR : rdL, wr = ping ? rdL : rdR;
        float inpL = l * inGain + wl * fb;
        float inpR = r * inGain + wr * fb;
        if (!freeze) {
            // The loop's tone: cuts, tape head loss + saturation. Frozen, the loop is
            // left untouched so it holds instead of slowly dulling.
            if (hpOn) {
                hpL_ += hpC * (inpL - hpL_);
                inpL -= hpL_;
                hpR_ += hpC * (inpR - hpR_);
                inpR -= hpR_;
            }
            if (lpOn) {
                lpL_ += lpC * (inpL - lpL_);
                inpL = lpL_;
                lpR_ += lpC * (inpR - lpR_);
                inpR = lpR_;
            }
            if (tape) {
                tapeL_ += tapeC * (inpL - tapeL_);
                inpL = fast_math::tanhPrecise(tapeL_ * drive) * invTanhDrive;
                tapeR_ += tapeC * (inpR - tapeR_);
                inpR = fast_math::tanhPrecise(tapeR_ * drive) * invTanhDrive;
            }
        }
        inpL = ap_[1].tick(ap_[0].tick(inpL, apG), apG);
        inpR = ap_[3].tick(ap_[2].tick(inpR, apG), apG);
        pushPk = std::max(pushPk, std::max(std::fabs(inpL), std::fabs(inpR)));
        bufL_.push(inpL);
        bufR_.push(inpR);

        // ---- output stage: width, bass mono, mix, trim ----
        float outWetL = rdL, outWetR = rdR;
        if (width != 1.0f || monoOn) {
            float mid = (outWetL + outWetR) * 0.5f, side = (outWetL - outWetR) * 0.5f * width;
            if (monoOn) {
                monoL_ += monoC * (side - monoL_);
                side -= monoL_;
            }
            outWetL = mid + side;
            outWetR = mid - side;
        }
        const float oL = (dryG * l + wetG * outWetL) * outG;
        const float oR = (dryG * r + wetG * outWetR) * outG;
        buf[i * 2] = oL;
        buf[i * 2 + 1] = oR;

        wetPk = std::max(wetPk, std::fabs(outWetL * wetG));
        outPkL = std::max(outPkL, std::fabs(oL));
        outPkR = std::max(outPkR, std::fabs(oR));
    }

    // Loop-feed peak (decays slowly so a ringing tail blocks accrual); sleep
    // additionally waits out the loop's flight time so an in-flight echo is
    // never cut: output silence alone proves nothing while the line is full.
    loopPk_ = pushPk > loopPk_ ? pushPk : loopPk_ * 0.90f;
    const double flight = std::max(curL_, curR_) + (ap_[0].n + ap_[1].n);
    if (inSilent && !freeze && std::max(outPkL, outPkR) <= 1.0e-5f && loopPk_ <= 1.0e-5f)
        quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > (uint64_t)flight + kIdleSamples) idle_ = true;

    // ---- telemetry -----------------------------------------------------------
    mWet_.store(decayPeak(mWet_.load(std::memory_order_relaxed), wetPk), std::memory_order_relaxed);
    mOutL_.store(decayPeak(mOutL_.load(std::memory_order_relaxed), outPkL), std::memory_order_relaxed);
    mOutR_.store(decayPeak(mOutR_.load(std::memory_order_relaxed), outPkR), std::memory_order_relaxed);
    msL_.store(static_cast<float>(curL_ * 1000.0 / sr_), std::memory_order_relaxed);
    msR_.store(static_cast<float>(curR_ * 1000.0 / sr_), std::memory_order_relaxed);
    diffA_.store(static_cast<float>(apLen), std::memory_order_relaxed);
    bpmA_.store(spb_ > 0.0 ? static_cast<float>(60.0 * sr_ / spb_) : 0.0f, std::memory_order_relaxed);
    srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
    cpuA_.store(static_cast<float>(cpuS_), std::memory_order_relaxed);
}

const char* Delay::paramName(int32_t i) const {
    static const char* nm[] = {"Sync", "Time L", "Time R", "Div L", "Div R", "Link L/R", "Feedback", "Spread",
        "Ping-Pong", "Wow Rate", "Wow Depth", "Freeze", "Dry/Wet", "Output", "Dry Level", "Diffuse", "Low Cut",
        "High Cut", "Tape Mode", "Fade on Change", "Width", "Bass Mono", "Wet Only", "Latency Comp"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float Delay::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Delay::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

void Delay::deviceAction(int32_t id, int32_t, float) {
    if (id == A_ClearLoop) clear_.store(true, std::memory_order_relaxed);
    wake();
}

int32_t Delay::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples < kScope) return 0;
    out[S_WetPk] = mWet_.load(std::memory_order_relaxed);
    out[S_OutL] = mOutL_.load(std::memory_order_relaxed);
    out[S_OutR] = mOutR_.load(std::memory_order_relaxed);
    out[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    out[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    out[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    out[S_DiffuseSmp] = diffA_.load(std::memory_order_relaxed);
    out[S_Frozen] = getParam(Freeze) >= 0.5f ? 1.0f : 0.0f;
    out[S_MsL] = msL_.load(std::memory_order_relaxed);
    out[S_MsR] = msR_.load(std::memory_order_relaxed);
    out[S_Latency] = static_cast<float>(latencySamples());
    return kScope;
}

std::string Delay::deviceText(int32_t id) const {
    if (id != 0) return {};
    char b[256];
    const bool sync = getParam(SyncMode) >= 0.5f && spb_ > 0.0;
    const bool link = getParam(LinkLR) >= 0.5f;
    const float msl = msL_.load(std::memory_order_relaxed), msr = msR_.load(std::memory_order_relaxed);
    if (getParam(Freeze) >= 0.5f) {
        std::snprintf(b, sizeof b, "Freeze: loop held, input muted - %.0f ms / %.0f ms - mix %.0f %%", msl, msr,
            getParam(DryWet) * 100.0f);
    } else if (sync) {
        const int dl = divIndex(DivL), dr = link ? dl : divIndex(DivR);
        static const char* nm[kNumDiv] = {"1/16", "1/8T", "1/8", "1/8.", "1/4T", "1/4", "1/4.", "1/2"};
        std::snprintf(b, sizeof b, "Sync %s / %s%s - feedback %.0f %% - mix %.0f %%", nm[dl], nm[dr],
            getParam(PingPong) >= 0.5f ? " - ping-pong" : "", getParam(Feedback) * 100.0f, getParam(DryWet) * 100.0f);
    } else {
        std::snprintf(b, sizeof b, "Free %.0f ms / %.0f ms%s - feedback %.0f %% - mix %.0f %%", msl, msr,
            getParam(PingPong) >= 0.5f ? " - ping-pong" : "", getParam(Feedback) * 100.0f, getParam(DryWet) * 100.0f);
    }
    return std::string(b);
}

} // namespace nota
