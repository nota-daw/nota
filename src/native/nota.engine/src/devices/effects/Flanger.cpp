// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Flanger implementation.
// Verbatim DSP, all std::-exact: the LFO steers the modulated delay reads inside the
// feedback loop. Silent combs auto-sleep via isIdle().

#include "Flanger.h"

#include "dsp/math/SimdAudio.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>

namespace nota {

Flanger::Flanger() {
    p_[Rate].store(0.384f);  // 0.20 Hz
    p_[Delay].store(0.735f); // 2.5 ms
    p_[Depth].store(0.80f);
    p_[Feedback].store((float)(0.5 + 0.70 / (2.0 * kFbMax))); // +70 %
    p_[Mix].store(0.50f);
    p_[Waveform].store(0.5f); // Triangle
    p_[Sync].store(0.0f);     // Free (Hz)
    p_[Division].store((float)kDefaultDiv / (kDivs - 1));
    p_[Stereo].store(0.5f); // 90°
    setSampleRate(44100.0, 0);
}

void Flanger::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    cycle_ = 0;
    frac_ = 0.0;
    clearLine();
    snap_ = true;
    resetMeters();
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
}

void Flanger::setTransport(double beatStart, double spb, bool playing) {
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

void Flanger::setTransportInfo(const TransportInfo& ti) {
    if (ti.tsNum > 0 && ti.tsDenom > 0) barBeats_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 0.5, 64.0);
}

void Flanger::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

void Flanger::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (restartReq_.exchange(false, std::memory_order_acquire)) {
        cycle_ = 0;
        frac_ = 0.0;
    }
    if (clearReq_.exchange(false, std::memory_order_acquire)) clearLine();
    if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

    const int wave = waveIndex();
    const double base = baseMs();
    const double depth = std::clamp((double)get(Depth), 0.0, 1.0);
    const double fb = feedback();
    const double mix = std::clamp((double)get(Mix), 0.0, 1.0);
    const double dryT = std::min(1.0, 2.0 * (1.0 - mix)), wetT = std::min(1.0, 2.0 * mix);
    const double phOff = stereoDeg() / 360.0;
    const bool sync = get(Sync) >= 0.5f;
    const double div = kDivBeats[divIndex()];
    const double spb = spb_ > 0.0 ? spb_ : sr_ * 0.5; // 120 BPM until the engine says otherwise
    const double rateHz = sync ? sr_ / (spb * div) : rateFreeHz();
    const double inc = rateHz / sr_;
    const bool locked = sync && playing_ && spb_ > 0.0;
    const double span = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
    const double sm = 1.0 - std::exp(-1.0 / (0.015 * sr_)); // ~15 ms parameter glide
    const double msToS = sr_ * 0.001;
    const double maxD = (double)(kBuf - 4);

    const double wetSignT = fb < 0.0 ? -wetT : wetT;
    if (snap_) {
        baseS_ = base;
        depthS_ = depth;
        fbS_ = fb;
        dryS_ = dryT;
        wetS_ = wetSignT;
        snap_ = false;
    }

    // Idle skip: silent input on a settled comb leaves the buffer untouched. The LFO
    // phase keeps advancing so the wake-up sweep has no timing jump. Sleep engages
    // only after the full feed-through flight time (an echo can arrive long after the
    // input and the output both read silent), tracked below via feedPk_/quietSamples_.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    if (inSilent && idle_) {
        frac_ += inc * (double)frames;
        if (frac_ >= 1.0) {
            const double wr = std::floor(frac_);
            frac_ -= wr;
            cycle_ += (int64_t)wr;
        }
        return;
    }

    float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;
    double tauL = 0.0, tauR = 0.0;
    const int w0 = w_;
    for (int32_t i = 0; i < frames; ++i) {
        if (locked) {
            const double beat = beatStart_ + (double)i / spb_;
            const double spans = std::floor(beat / span);
            const double c = (beat - spans * span) / div;
            const double k = std::floor(c);
            cycle_ = (int64_t)spans * 4096 + (int64_t)k;
            frac_ = c - k;
        }
        baseS_ += sm * (base - baseS_);
        depthS_ += sm * (depth - depthS_);
        fbS_ += sm * (fb - fbS_);
        dryS_ += sm * (dryT - dryS_);
        wetS_ += sm * (wetSignT - wetS_);

        const double lfoL = lfo(wave, frac_);
        const double lfoR = lfo(wave, frac_ + phOff);
        tauL = baseS_ * (1.0 + kDepthScale * depthS_ * lfoL);
        tauR = baseS_ * (1.0 + kDepthScale * depthS_ * lfoR);
        const double dL = std::clamp(tauL * msToS, 2.0, maxD);
        const double dR = std::clamp(tauR * msToS, 2.0, maxD);

        const float xL = buf[i * 2], xR = buf[i * 2 + 1];
        inPk = std::max(inPk, std::max(std::fabs(xL), std::fabs(xR)));
        const float yL = readLine(lineL_, dL), yR = readLine(lineR_, dR);
        const float pushL = sanitize(xL + (float)fbS_ * yL);
        const float pushR = sanitize(xR + (float)fbS_ * yR);
        lineL_[w_] = pushL;
        lineR_[w_] = pushR;
        w_ = (w_ + 1) & kMask;

        const double norm = 1.0 / std::max(dryS_ + std::fabs(wetS_), 1e-3);
        const float oL = (float)((dryS_ * xL + wetS_ * yL) * norm);
        const float oR = (float)((dryS_ * xR + wetS_ * yR) * norm);
        buf[i * 2] = oL;
        buf[i * 2 + 1] = oR;
        outPkL = std::max(outPkL, std::fabs(oL));
        outPkR = std::max(outPkR, std::fabs(oR));

        if (!locked) {
            frac_ += inc;
            if (frac_ >= 1.0) {
                const double wr = std::floor(frac_);
                frac_ -= wr;
                cycle_ += (int64_t)wr;
            }
        }
    }

    // Loop-feed peak (decays slowly so a ringing comb blocks accrual); sleep
    // additionally waits out the sweep's flight time so an in-flight echo is never
    // cut: output silence alone proves nothing while the line is full. The block's
    // writes are the last `frames` slots before w_, read back in one pass (at most two
    // runs around the wrap) rather than tracked per sample.
    float pushPk = 0.0f;
    for (int at = w0, left = std::min<int32_t>(frames, kBuf); left > 0;) {
        const int run = std::min(left, kBuf - at);
        pushPk = std::max({pushPk, simd_audio::peakAbs(lineL_ + at, run), simd_audio::peakAbs(lineR_ + at, run)});
        left -= run;
        at = (at + run) & kMask;
    }
    feedPk_ = pushPk > feedPk_ ? pushPk : feedPk_ * 0.90f;
    const double flight = 0.008 * (1.0 + kDepthScale) * sr_; // 8 ms · 1.92, the longest modulated line
    if (inSilent && outPkL <= 1.0e-5f && outPkR <= 1.0e-5f && feedPk_ <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > (uint64_t)flight + kIdleSamples) idle_ = true;

    if (frames > 0) {
        const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
        inPkM_ = std::max(inPk, inPkM_ * fall);
        outPkLM_ = std::max(outPkL, outPkLM_ * fall);
        outPkRM_ = std::max(outPkR, outPkRM_ * fall);
        tauLA_.store((float)tauL, std::memory_order_relaxed);
        tauRA_.store((float)tauR, std::memory_order_relaxed);
    }
    inPkA_.store(db(inPkM_), std::memory_order_relaxed);
    outPkLA_.store(db(outPkLM_), std::memory_order_relaxed);
    outPkRA_.store(db(outPkRM_), std::memory_order_relaxed);
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

void Flanger::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id == A_Restart) restartReq_.store(true, std::memory_order_release);
    else if (id == A_Clear)
        clearReq_.store(true, std::memory_order_release);
    else if (id == A_ResetMeters)
        resetReq_.store(true, std::memory_order_release);
    wake();
}

int32_t Flanger::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    const int64_t cyc = cycleA_.load(std::memory_order_relaxed);
    const double fr = fracA_.load(std::memory_order_relaxed);
    const int64_t base = cyc - (((cyc % 2) + 2) % 2); // the window starts on an even cycle
    const double bms = baseMs(), dep = std::clamp((double)get(Depth), 0.0, 1.0);
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeakL] = outPkLA_.load(std::memory_order_relaxed);
    t[S_OutPeakR] = outPkRA_.load(std::memory_order_relaxed);
    t[S_TauL] = tauLA_.load(std::memory_order_relaxed);
    t[S_TauR] = tauRA_.load(std::memory_order_relaxed);
    if (t[S_TauL] <= 0.0f) t[S_TauL] = t[S_TauR] = (float)bms; // before the first block
    t[S_WinPhase] = (float)((double)(cyc - base) + fr);
    t[S_RateHz] = rateA_.load(std::memory_order_relaxed);
    t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    t[S_Playing] = (float)playingA_.load(std::memory_order_relaxed);
    t[S_Locked] = (float)lockedA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_BaseMs] = (float)bms;
    t[S_TauMin] = (float)(bms * (1.0 - kDepthScale * dep));
    t[S_TauMax] = (float)(bms * (1.0 + kDepthScale * dep));
    const Comb cb = comb();
    t[S_NotchHz] = (float)(cb.notchTheta / (2.0 * kPi) / (std::max(1e-6f, t[S_TauL]) * 0.001));
    t[S_NotchMinHz] = (float)(cb.notchTheta / (2.0 * kPi) / (std::max(1e-6f, t[S_TauMax]) * 0.001));
    t[S_NotchMaxHz] = (float)(cb.notchTheta / (2.0 * kPi) / (std::max(1e-6f, t[S_TauMin]) * 0.001));
    t[S_NullDb] = (float)cb.nullDb;
    t[S_PeakDb] = (float)cb.peakDb;
    t[S_DivBeats] = (float)kDivBeats[divIndex()];
    t[S_BarBeats] = barA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_Signal] = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
    t[S_Cycles] = (float)cyc;
    int32_t n = 0;
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    return n;
}

const char* Flanger::paramName(int32_t i) const {
    switch (i) {
    case Rate: return "Rate";
    case Delay: return "Delay";
    case Depth: return "Depth";
    case Feedback: return "Feedback";
    case Mix: return "Mix";
    case Waveform: return "Waveform";
    case Sync: return "Sync";
    case Division: return "Division";
    case Stereo: return "Stereo";
    default: return "";
    }
}

float Flanger::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Flanger::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

std::string Flanger::deviceText(int32_t id) const {
    static const char* waves[3] = {"Sine", "Triangle", "Saw"};
    char b[1400];
    if (id == 0) {
        std::string s = waves[waveIndex()];
        if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
        else
            std::snprintf(b, sizeof b, " - %.2f Hz", rateFreeHz());
        s += b;
        std::snprintf(b, sizeof b,
            " - stereo %.0f deg - delay %.2f ms - depth %.0f %% - feedback %+.0f %% (%s) - mix %.0f %%", stereoDeg(),
            baseMs(), get(Depth) * 100.0f, feedback() * 100.0, modeWord(), get(Mix) * 100.0f);
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
        std::snprintf(b, sizeof b,
            "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - delay L %.2f ms, R %.2f ms "
            "(sweep %.2f .. %.2f ms) - first notch %.0f Hz (sweeps %.0f .. %.0f Hz), %.1f dB deep - comb "
            "peaks %+.1f dB - "
            "in %s dBFS - out L %s, R %s dBFS - %.1f BPM, bar %.2g beats - transport %s - CPU %.2f %%",
            sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
            sc[S_Locked] > 0.5f ? " locked to the song"
            : get(Sync) >= 0.5f ? " free-running at the synced rate"
                                : "",
            sc[S_WinPhase], sc[S_TauL], sc[S_TauR], sc[S_TauMin], sc[S_TauMax], sc[S_NotchHz], sc[S_NotchMinHz],
            sc[S_NotchMaxHz], sc[S_NullDb], sc[S_PeakDb], lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeakL]).c_str(),
            lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats], sc[S_Playing] > 0.5f ? "playing" : "stopped",
            sc[S_Cpu] * 100.0f);
        return b;
    }
    if (id == 2) {
        std::string divs;
        for (int i = 0; i < kDivs; ++i) {
            std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
            divs += b;
        }
        return "All params are normalized 0..1. Rate: the free LFO rate, exponential 0.02 Hz (0) .. 8 Hz (1) "
               "(0.269 = 0.1 Hz, 0.384 = 0.2 Hz, 0.537 = 0.5 Hz, 0.653 = 1 Hz, 0.769 = 2 Hz, 0.884 = 4 Hz). Delay: the "
               "centre of the sweep, exponential 0.1 ms (0) .. 8 ms (1) (0.263 = 0.32 ms, 0.409 = 0.6 ms, 0.525 = 1 "
               "ms, "
               "0.567 = 1.2 ms, 0.683 = 2 ms, 0.735 = 2.5 ms, 0.776 = 3 ms, 0.841 = 4 ms, 0.892 = 5 ms). Depth: how "
               "far "
               "the delay swings, 0..1 - tau = Delay * (1 +/- 0.92 * Depth). Feedback: bipolar, (v - 0.5) * 1.9 = -95 "
               "% .. "
               "+95 % (0.5 = none, 0.868 = +70 %, 0.132 = -70 %); positive sharpens the peaks at multiples of 1/delay "
               "(jet), negative also flips the wet polarity so the peaks move to the odd multiples of 1/(2 delay) "
               "(hollow); past 85 % the comb rings. Mix: 0 = dry, 0.5 = dry and wet equal (the "
               "deepest notches), 1 = wet only (a pitch vibrato). Waveform: 0 Sine, 0.5 Triangle, 1 Saw. Stereo: the "
               "right "
               "LFO's offset from the left, 0..1 = 0..180 deg (0 = mono sweep, 0.5 = 90 deg, 1 = opposite). Sync: >= "
               "0.5 "
               "takes the rate from the tempo (Division) and locks the LFO to the song position while the transport "
               "plays, restarting on the bar. Division (with Sync): " +
               divs + ".";
    }
    return {};
}

double Flanger::combMag(double dry, double wet, double fb, double th) {
    const double c = std::cos(th), s = std::sin(th);
    return combMagSigned(dry, fb < 0.0 ? -wet : wet, fb, c, s);
}

double Flanger::combMagSigned(double dry, double wet, double fb, double c, double s) {
    const double nr = wet * c, ni = -wet * s, dr = 1.0 - fb * c, di = fb * s, den = dr * dr + di * di;
    const double re = dry + (nr * dr + ni * di) / den, im = (ni * dr - nr * di) / den;
    return std::sqrt(re * re + im * im) / std::max(dry + std::fabs(wet), 0.001);
}

Flanger::Comb Flanger::comb() const {
    const double mix = std::clamp((double)get(Mix), 0.0, 1.0), fb = feedback();
    const double dry = std::min(1.0, 2.0 * (1.0 - mix)), wet = std::min(1.0, 2.0 * mix);
    double best = kPi, bm = 1e9;
    for (int i = 1; i <= 180; ++i) {
        const double th = i / 180.0 * 2.0 * kPi, m = combMag(dry, wet, fb, th);
        if (m < bm) {
            bm = m;
            best = th;
        }
    }
    const double pk = combMag(dry, wet, fb, fb >= 0.0 ? 0.0 : kPi);
    return {best, 20.0 * std::log10(std::max(bm, 1e-4)), 20.0 * std::log10(std::max(pk, 1e-4))};
}

const char* Flanger::modeWord() const {
    const double fb = feedback();
    return std::fabs(fb) >= 0.85 ? "resonant" : fb <= -0.15 ? "negative" : fb >= 0.15 ? "positive" : "through";
}

double Flanger::lfo(int wave, double p) {
    const double f = p - std::floor(p);
    if (wave == 0) return std::sin(2.0 * kPi * f);
    if (wave == 1) return f < 0.25 ? 4.0 * f : f < 0.75 ? 2.0 - 4.0 * f : 4.0 * f - 4.0;
    const double g = f + 0.5;
    return 2.0 * (g - std::floor(g)) - 1.0;
}

inline float Flanger::readLine(const float* line, double d) const {
    const double r = (double)w_ - d;
    const double fl = std::floor(r);
    const float t = (float)(r - fl);
    const int i0 = (int)((int64_t)fl & kMask);
    const float ym1 = line[(i0 - 1) & kMask], y0 = line[i0], y1 = line[(i0 + 1) & kMask], y2 = line[(i0 + 2) & kMask];
    const float c1 = 0.5f * (y1 - ym1);
    const float c2 = ym1 - 2.5f * y0 + 2.0f * y1 - 0.5f * y2;
    const float c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);
    return ((c3 * t + c2) * t + c1) * t + y0;
}

void Flanger::clearLine() {
    std::memset(lineL_, 0, sizeof lineL_);
    std::memset(lineR_, 0, sizeof lineR_);
    w_ = 0;
}

} // namespace nota
