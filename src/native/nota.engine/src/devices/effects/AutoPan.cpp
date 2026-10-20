// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Orbit implementation.
// Fast sine LFO, hoisted tanh reciprocal, in-process silence bypass.

#include "AutoPan.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {

AutoPan::AutoPan() {
    p_[Rate].store(0.60f); // ~1.45 Hz
    p_[Amount].store(0.70f);
    p_[Waveform].store(0.0f); // Sine
    p_[Shape].store(0.0f);    // smooth
    p_[Phase].store(0.5f);    // 180° → classic auto-pan
    p_[Mix].store(1.0f);
    p_[Sync].store(0.0f); // Free (Hz)
    p_[Division].store((float)kDefaultDiv / (kDivs - 1));
    setSampleRate(44100.0, 0);
}

void AutoPan::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    cycle_ = 0;
    frac_ = 0.0;
    gLs_ = gRs_ = -1.0f; // snap to the first target
    resetMeters();
}

void AutoPan::setTransport(double beatStart, double spb, bool playing) {
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

void AutoPan::setTransportInfo(const TransportInfo& ti) {
    if (ti.tsNum > 0 && ti.tsDenom > 0) barBeats_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 0.5, 64.0);
}

void AutoPan::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (restartReq_.exchange(false, std::memory_order_acquire)) {
        cycle_ = 0;
        frac_ = 0.0;
    }
    if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

    const float amount = std::clamp(get(Amount), 0.0f, 1.0f);
    const int wave = waveIndex();
    const float shape = std::clamp(get(Shape), 0.0f, 1.0f);
    const double phOff = std::clamp((double)get(Phase), 0.0, 1.0); // 0..1 cycle → 0..360°
    const float mix = std::clamp(get(Mix), 0.0f, 1.0f);
    const bool sync = get(Sync) >= 0.5f;
    const double div = kDivBeats[divIndex()];
    const double spb = spb_ > 0.0 ? spb_ : sr_ * 0.5; // 120 BPM until the engine says otherwise
    const double rateHz = sync ? sr_ / (spb * div) : expMap(get(Rate), 0.01, 40.0);
    const double inc = rateHz / sr_;
    const bool locked = sync && playing_ && spb_ > 0.0;
    // The restart span: the bar, or as many bars as a cycle longer than one needs.
    const double span = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
    const float slew = 1.0f - std::exp(-1.0f / (0.001f * (float)sr_)); // ~1 ms

    const float invTanhKk = shape > 1.0e-3f ? 1.0f / fast_math::tanhPrecise(1.0f + shape * 12.0f) : 1.0f;

    // Instant silence bypass (transparency proof): the output is dry × gain, so a
    // silent input is silent out for ANY LFO position or gain — the device holds
    // no delay lines, no filters, no envelopes. The free-run phase advances in
    // closed form (locked mode re-derives it absolutely per sample, so there is
    // nothing to advance); meters decay in closed form; the ~1 ms gain slew
    // freezes and re-snaps on wake (snap_ — continuous, so no click).
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);

    float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;
    if (inSilent) {
        if (!locked) {
            frac_ += inc * (double)frames;
            if (frac_ >= 1.0) {
                const double w = std::floor(frac_);
                frac_ -= w;
                cycle_ += (int64_t)w;
            }
        }
        snap_ = true;
    } else
        for (int32_t i = 0; i < frames; ++i) {
            if (locked) {
                const double beat = beatStart_ + (double)i / spb_;
                const double spans = std::floor(beat / span);
                const double c = (beat - spans * span) / div;
                const double k = std::floor(c);
                cycle_ = (int64_t)spans * 4096 + (int64_t)k;
                frac_ = c - k;
            }

            const float lfoL = lfoAt(wave, shape, cycle_, frac_, invTanhKk);
            const double fr = frac_ + phOff;
            const float lfoR = fr >= 1.0 ? lfoAt(wave, shape, cycle_ + 1, fr - 1.0, invTanhKk)
                                         : lfoAt(wave, shape, cycle_, fr, invTanhKk);

            // Amplitude per channel: lfo = +1 → full, lfo = −1 → (1 − amount); Mix blends toward 1.
            const float tL = 1.0f - mix * amount * 0.5f * (1.0f - lfoL);
            const float tR = 1.0f - mix * amount * 0.5f * (1.0f - lfoR);
            if (gLs_ < 0.0f || snap_) {
                gLs_ = tL;
                gRs_ = tR;
                snap_ = false;
            }
            gLs_ += slew * (tL - gLs_);
            gRs_ += slew * (tR - gRs_);

            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(dryL), std::fabs(dryR)));
            const float oL = dryL * gLs_, oR = dryR * gRs_;
            buf[i * 2] = oL;
            buf[i * 2 + 1] = oR;
            outPkL = std::max(outPkL, std::fabs(oL));
            outPkR = std::max(outPkR, std::fabs(oR));

            if (!locked) {
                frac_ += inc;
                if (frac_ >= 1.0) {
                    const double w = std::floor(frac_);
                    frac_ -= w;
                    cycle_ += (int64_t)w;
                }
            }
        }

    if (frames > 0) {
        const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
        inPkM_ = std::max(inPk, inPkM_ * fall);
        outPkLM_ = std::max(outPkL, outPkLM_ * fall);
        outPkRM_ = std::max(outPkR, outPkRM_ * fall);
    }
    const float gL = gLs_ < 0.0f ? 1.0f : gLs_, gR = gRs_ < 0.0f ? 1.0f : gRs_;
    inPkA_.store(db(inPkM_), std::memory_order_relaxed);
    outPkLA_.store(db(outPkLM_), std::memory_order_relaxed);
    outPkRA_.store(db(outPkRM_), std::memory_order_relaxed);
    gainLA_.store(gL, std::memory_order_relaxed);
    gainRA_.store(gR, std::memory_order_relaxed);
    panPos_.store(0.5f + 0.5f * (gR - gL), std::memory_order_relaxed);
    cycleA_.store(cycle_, std::memory_order_relaxed);
    fracA_.store((float)frac_, std::memory_order_relaxed);
    rateA_.store((float)rateHz, std::memory_order_relaxed);
    bpmA_.store((float)(60.0 * sr_ / spb), std::memory_order_relaxed);
    playingA_.store(playing_ ? 1 : 0, std::memory_order_relaxed);
    lockedA_.store(locked ? 1 : 0, std::memory_order_relaxed);
    barA_.store((float)barBeats_, std::memory_order_relaxed);
    if (frames > 0) {
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

void AutoPan::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id == A_Restart) restartReq_.store(true, std::memory_order_release);
    else if (id == A_ResetMeters)
        resetReq_.store(true, std::memory_order_release);
    wake();
}

int32_t AutoPan::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    const int64_t cyc = cycleA_.load(std::memory_order_relaxed);
    const double fr = fracA_.load(std::memory_order_relaxed);
    const int64_t base = cyc - (((cyc % 2) + 2) % 2); // the window starts on an even cycle
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeakL] = outPkLA_.load(std::memory_order_relaxed);
    t[S_OutPeakR] = outPkRA_.load(std::memory_order_relaxed);
    t[S_GainL] = gainLA_.load(std::memory_order_relaxed);
    t[S_GainR] = gainRA_.load(std::memory_order_relaxed);
    t[S_Pan] = panOf(t[S_GainL], t[S_GainR]);
    t[S_WinPhase] = (float)((double)(cyc - base) + fr);
    t[S_RateHz] = rateA_.load(std::memory_order_relaxed);
    t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    t[S_Playing] = (float)playingA_.load(std::memory_order_relaxed);
    t[S_Locked] = (float)lockedA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    for (int k = 0; k < 4; ++k) t[S_Sh0 + k] = hashBip(base - 1 + k);
    t[S_DivBeats] = (float)kDivBeats[divIndex()];
    t[S_BarBeats] = barA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_Signal] = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
    t[S_Floor] = 1.0f - std::clamp(get(Amount), 0.0f, 1.0f) * std::clamp(get(Mix), 0.0f, 1.0f);
    t[S_Cycles] = (float)cyc;

    // The pan's swing over the two-cycle window (the S&H window uses its own steps).
    const int wave = waveIndex();
    const float amount = get(Amount), mix = get(Mix), shape = get(Shape);
    const double off = std::clamp((double)get(Phase), 0.0, 1.0);
    const float invTanhKk = shape > 1.0e-3f ? 1.0f / fast_math::tanhPrecise(1.0f + shape * 12.0f) : 1.0f;
    float mn = 1.0f, mx = -1.0f;
    for (int s = 0; s < 128; ++s) {
        const double p = s / 64.0;
        const int64_t k = (int64_t)std::floor(p);
        const double f = p - (double)k, f2 = f + off;
        const float l = lfoAt(wave, shape, base + k, f, invTanhKk);
        const float r = f2 >= 1.0 ? lfoAt(wave, shape, base + k + 1, f2 - 1.0, invTanhKk)
                                  : lfoAt(wave, shape, base + k, f2, invTanhKk);
        const float gl = 1.0f - mix * amount * 0.5f * (1.0f - l), gr = 1.0f - mix * amount * 0.5f * (1.0f - r);
        const float pn = panOf(gl, gr);
        mn = std::min(mn, pn);
        mx = std::max(mx, pn);
    }
    t[S_PanMin] = mn;
    t[S_PanMax] = mx;

    int32_t n = 0;
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    return n;
}

const char* AutoPan::paramName(int32_t i) const {
    switch (i) {
    case Rate: return "Rate";
    case Amount: return "Amount";
    case Waveform: return "Waveform";
    case Shape: return "Shape";
    case Phase: return "Phase";
    case Mix: return "Mix";
    case Sync: return "Sync";
    case Division: return "Division";
    default: return "";
    }
}

float AutoPan::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void AutoPan::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

std::string AutoPan::deviceText(int32_t id) const {
    static const char* waves[5] = {"Sine", "Triangle", "Saw", "Square", "S&H"};
    char b[900];
    const double deg = std::clamp(get(Phase), 0.0f, 1.0f) * 360.0;
    if (id == 0) {
        std::string s = waves[waveIndex()];
        if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
        else
            std::snprintf(b, sizeof b, " - %.2f Hz", expMap(get(Rate), 0.01, 40.0));
        s += b;
        std::snprintf(b, sizeof b, " - %s %.0f %% - phase %.0f deg (%s) - amount %.0f %% - mix %.0f %%",
            waveIndex() == 4 ? "glide" : "shape", get(Shape) * 100.0f, deg, modeWord(deg), get(Amount) * 100.0f,
            get(Mix) * 100.0f);
        s += b;
        return s;
    }
    if (id == 1) {
        float sc[kTele];
        scopeRead(sc, kTele);
        auto lv = [](float v) {
            char t[24];
            if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf");
            else
                std::snprintf(t, sizeof t, "%.1f", v);
            return std::string(t);
        };
        auto gdb = [](float g) {
            char t[24];
            if (g < 0.001f) std::snprintf(t, sizeof t, "-inf");
            else
                std::snprintf(t, sizeof t, "%.1f", 20.0f * std::log10(g));
            return std::string(t);
        };
        std::snprintf(b, sizeof b,
            "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - gain L %s dB, R %s dB - pan %+.0f %% "
            "(swing %+.0f .. %+.0f %%) - floor %s dB - in %s dBFS - out L %s, R %s dBFS - %.1f BPM, bar %.2g beats - "
            "transport %s - CPU %.2f %%",
            sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
            sc[S_Locked] > 0.5f ? " locked to the song"
            : get(Sync) >= 0.5f ? " free-running at the synced rate"
                                : "",
            sc[S_WinPhase], gdb(sc[S_GainL]).c_str(), gdb(sc[S_GainR]).c_str(), sc[S_Pan] * 100.0f,
            sc[S_PanMin] * 100.0f, sc[S_PanMax] * 100.0f, gdb(sc[S_Floor]).c_str(), lv(sc[S_InPeak]).c_str(),
            lv(sc[S_OutPeakL]).c_str(), lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats],
            sc[S_Playing] > 0.5f ? "playing" : "stopped", sc[S_Cpu] * 100.0f);
        return b;
    }
    if (id == 2) {
        std::string divs;
        for (int i = 0; i < kDivs; ++i) {
            std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
            divs += b;
        }
        return "All params are normalized 0..1. Rate: the free LFO rate, exponential 0.01 Hz (0) .. 40 Hz (1) "
               "(0.40 = 0.28 Hz, 0.60 = 1.45 Hz, 0.70 = 3.3 Hz, 0.80 = 7.6 Hz). Amount: the depth, 0..1 - each channel "
               "dips "
               "to 1 - Amount at the LFO's low point. Waveform: 0 Sine, 0.25 Triangle, 0.5 Saw, 0.75 Square, 1 S&H "
               "(random steps, one per cycle; the right channel plays the same steps Phase later). Shape: sharpens "
               "Sine / "
               "Triangle / Saw toward a square (0 = pure); on S&H it is the glide between steps (0 = hard steps, 1 = a "
               "glide over the whole cycle); Square ignores it. Phase: the right LFO's offset from the left, 0..1 = "
               "0..360 deg - 0 = tremolo (both together), 0.5 = 180 deg auto-pan, 0.25 = 90 deg. Mix: 0 = dry .. 1 = "
               "full effect. Sync: >= 0.5 takes the rate from the tempo (Division) and locks the LFO to the song "
               "position while the transport plays, restarting on the bar (a 2/1 or 4/1 cycle on 2 or 4 bars). "
               "Division (with Sync): " +
               divs + ".";
    }
    return {};
}

float AutoPan::hashBip(int64_t k) {
    uint32_t x = (uint32_t)(uint64_t)k * 0x9E3779B9u ^ 0x53A9C1u;
    x ^= x >> 16;
    x *= 0x7FEB352Du;
    x ^= x >> 15;
    x *= 0x846CA68Bu;
    x ^= x >> 16;
    return (float)(x >> 8) / 8388608.0f - 1.0f;
}

float AutoPan::lfoAt(int wave, float shape, int64_t k, double f, float invTanhKk) {
    double v;
    switch (wave) {
    case 1: v = 4.0 * std::fabs(f - 0.5) - 1.0; break; // triangle
    case 2: v = 2.0 * f - 1.0; break;                  // saw
    case 3: return f < 0.5 ? 1.0f : -1.0f;             // square
    case 4: {                                          // sample & hold (+ glide)
        const float a = hashBip(k - 1), bb = hashBip(k);
        double t = shape > 1.0e-3f ? std::min(1.0, f / shape) : 1.0;
        t = t * t * (3.0 - 2.0 * t);
        return (float)(a + (bb - a) * t);
    }
    // Sine: fast_math::sin on phase01 — the LFO purely scales gain (pan law),
    // never steers a read pointer, so the <0.0007 error stays downstream.
    default: v = fast_math::sin((float)f); break; // sine
    }
    if (shape > 1.0e-3f) {
        const double kk = 1.0 + shape * 12.0;
        const float sharp = fast_math::tanhPrecise((float)(kk * v)) * invTanhKk;
        v = v * (1.0 - shape) + (double)sharp * shape;
    }
    return (float)v;
}

} // namespace nota
