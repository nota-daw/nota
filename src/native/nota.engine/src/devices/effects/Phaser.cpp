// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Phaser implementation.
// Verbatim DSP, all std::-exact: the LFO steers the all-pass corner inside the solved
// feedback loop. Silent chains auto-sleep via isIdle().

#include "Phaser.h"

#include "dsp/math/SimdAudio.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <string>

namespace nota {

Phaser::Phaser() {
    p_[Rate].store(0.5f);                                     // 0.40 Hz
    p_[Center].store(0.602f);                                 // 800 Hz
    p_[Depth].store(0.70f);                                   // ±2.1 oct
    p_[Feedback].store((float)(0.5 + 0.40 / (2.0 * kFbMax))); // +40 %
    p_[Mix].store(0.50f);
    p_[Waveform].store(0.0f); // Sine
    p_[Sync].store(0.0f);     // Free (Hz)
    p_[Division].store((float)kDefaultDiv / (kDivs - 1));
    p_[Stereo].store(0.5f);  // 90°
    p_[Stages].store(0.25f); // 4 stages
    setSampleRate(44100.0, 0);
}

void Phaser::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    cycle_ = 0;
    frac_ = 0.0;
    clearStages();
    snap_ = true;
    resetMeters();
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
}

void Phaser::setTransport(double beatStart, double spb, bool playing) {
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

void Phaser::setTransportInfo(const TransportInfo& ti) {
    if (ti.tsNum > 0 && ti.tsDenom > 0) barBeats_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 0.5, 64.0);
}

void Phaser::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

void Phaser::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (restartReq_.exchange(false, std::memory_order_acquire)) {
        cycle_ = 0;
        frac_ = 0.0;
    }
    if (clearReq_.exchange(false, std::memory_order_acquire)) clearStages();
    if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

    const int wave = waveIndex();
    const double lc = std::log2(centerHz());
    const double depth = std::clamp((double)get(Depth), 0.0, 1.0);
    const double fb = feedback();
    const double mix = std::clamp((double)get(Mix), 0.0, 1.0);
    const double dryT = std::min(1.0, 2.0 * (1.0 - mix)), wetT = std::min(1.0, 2.0 * mix);
    const double phOff = stereoDeg() / 360.0;
    const int stT = stageCount();
    const bool sync = get(Sync) >= 0.5f;
    const double div = kDivBeats[divIndex()];
    const double spb = spb_ > 0.0 ? spb_ : sr_ * 0.5; // 120 BPM until the engine says otherwise
    const double rateHz = sync ? sr_ / (spb * div) : rateFreeHz();
    const double inc = rateHz / sr_;
    const bool locked = sync && playing_ && spb_ > 0.0;
    const double span = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
    const double sm = 1.0 - std::exp(-1.0 / (0.015 * sr_)); // ~15 ms parameter glide
    const double fade = 1.0 / (0.003 * sr_);                // ~3 ms each way for a stage change
    const double fcHi = std::log2(0.45 * sr_), fcLo = std::log2(10.0);

    const double wetSignT = fb < 0.0 ? -wetT : wetT;
    if (snap_) {
        lcS_ = lc;
        depthS_ = depth;
        fbS_ = fb;
        dryS_ = dryT;
        wetS_ = wetSignT;
        stages_ = stT;
        stGain_ = 1.0;
        snap_ = false;
    }

    // Idle skip: silent input on a settled chain leaves the buffer untouched. The LFO
    // phase keeps advancing so the wake-up sweep has no timing jump. The chain holds
    // no delay line, so there is no feed-through flight time beyond the hangover —
    // but a whistling loop still blocks accrual via feedPk_ below.
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

    float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f, feedPk = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        if (locked) {
            const double beat = beatStart_ + (double)i / spb_;
            const double spans = std::floor(beat / span);
            const double c = (beat - spans * span) / div;
            const double k = std::floor(c);
            cycle_ = (int64_t)spans * 4096 + (int64_t)k;
            frac_ = c - k;
        }
        lcS_ += sm * (lc - lcS_);
        depthS_ += sm * (depth - depthS_);
        fbS_ += sm * (fb - fbS_);
        dryS_ += sm * (dryT - dryS_);
        wetS_ += sm * (wetSignT - wetS_);

        // A stage-count change: fade the wet path out, switch on silence, fade back in.
        if (stT != stages_) {
            stGain_ -= fade;
            if (stGain_ <= 0.0) {
                stGain_ = 0.0;
                for (int s = std::min(stages_, stT); s < kMaxStages; ++s) sL_[s] = sR_[s] = 0.0;
                stages_ = stT;
            }
        } else if (stGain_ < 1.0)
            stGain_ = std::min(1.0, stGain_ + fade);

        if ((ctl_++ & 3) == 0) {
            const double swing = kDepthOct * depthS_;
            fcL_ = std::exp2(std::clamp(lcS_ + swing * lfo(wave, frac_), fcLo, fcHi));
            fcR_ = std::exp2(std::clamp(lcS_ + swing * lfo(wave, frac_ + phOff), fcLo, fcHi));
            aL_ = coef(fcL_);
            aR_ = coef(fcR_);
        }

        const float xL = buf[i * 2], xR = buf[i * 2 + 1];
        inPk = std::max(inPk, std::max(std::fabs(xL), std::fabs(xR)));
        const double yL = chain(sL_, aL_, xL, fbS_);
        const double yR = chain(sR_, aR_, xR, fbS_);
        // Only a silent-input block can count towards sleep, so only it measures the
        // chain (a loud block resets the count anyway). Loop-invariant: unswitched.
        if (inSilent) feedPk = std::max(feedPk, (float)std::max(std::fabs(yL), std::fabs(yR)));

        const double wg = wetS_ * stGain_;
        const double norm = 1.0 / std::max(dryS_ + std::fabs(wetS_), 1e-3);
        const float oL = (float)((dryS_ * xL + wg * yL) * norm);
        const float oR = (float)((dryS_ * xR + wg * yR) * norm);
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

    // Chain-output peak (decays slowly so a whistling loop blocks accrual); the chain
    // holds no delay line, so no flight time beyond the hangover.
    feedPk_ = feedPk > feedPk_ ? feedPk : feedPk_ * 0.90f;
    if (inSilent && outPkL <= 1.0e-5f && outPkR <= 1.0e-5f && feedPk_ <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > kIdleSamples) idle_ = true;

    if (frames > 0) {
        const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
        inPkM_ = std::max(inPk, inPkM_ * fall);
        outPkLM_ = std::max(outPkL, outPkLM_ * fall);
        outPkRM_ = std::max(outPkR, outPkRM_ * fall);
        fcLA_.store((float)fcL_, std::memory_order_relaxed);
        fcRA_.store((float)fcR_, std::memory_order_relaxed);
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

void Phaser::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id == A_Restart) restartReq_.store(true, std::memory_order_release);
    else if (id == A_Clear)
        clearReq_.store(true, std::memory_order_release);
    else if (id == A_ResetMeters)
        resetReq_.store(true, std::memory_order_release);
    wake();
}

int32_t Phaser::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    const int64_t cyc = cycleA_.load(std::memory_order_relaxed);
    const double fr = fracA_.load(std::memory_order_relaxed);
    const int64_t base = cyc - (((cyc % 2) + 2) % 2); // the window starts on an even cycle
    const double sr = srA_.load(std::memory_order_relaxed);
    const double ch = centerHz(), dep = std::clamp((double)get(Depth), 0.0, 1.0);
    const double fb = feedback();
    const int n = stageCount();
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeakL] = outPkLA_.load(std::memory_order_relaxed);
    t[S_OutPeakR] = outPkRA_.load(std::memory_order_relaxed);
    t[S_FcL] = fcLA_.load(std::memory_order_relaxed);
    t[S_FcR] = fcRA_.load(std::memory_order_relaxed);
    if (t[S_FcL] <= 0.0f) t[S_FcL] = t[S_FcR] = (float)ch; // before the first block
    t[S_WinPhase] = (float)((double)(cyc - base) + fr);
    t[S_RateHz] = rateA_.load(std::memory_order_relaxed);
    t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    t[S_Playing] = (float)playingA_.load(std::memory_order_relaxed);
    t[S_Locked] = (float)lockedA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = (float)sr;
    t[S_CenterHz] = (float)ch;
    t[S_FcMin] = (float)(ch * std::exp2(-kDepthOct * dep));
    t[S_FcMax] = (float)(ch * std::exp2(kDepthOct * dep));
    t[S_NotchHz] = (float)firstNotchHz(t[S_FcL], n, fb, sr);
    t[S_NotchMinHz] = (float)firstNotchHz(t[S_FcMin], n, fb, sr);
    t[S_NotchMaxHz] = (float)firstNotchHz(t[S_FcMax], n, fb, sr);
    const double mix = std::clamp((double)get(Mix), 0.0, 1.0);
    const double dry = std::min(1.0, 2.0 * (1.0 - mix)), wet = std::min(1.0, 2.0 * mix);
    t[S_NullDb] = (float)(20.0 * std::log10(std::max(mag(dry, wet, fb, fb < 0.0 ? 2.0 * kPi : kPi), 1e-4)));
    t[S_PeakDb] = (float)(20.0 * std::log10(std::max(mag(dry, wet, fb, fb >= 0.0 ? 0.0 : kPi), 1e-4)));
    t[S_DivBeats] = (float)kDivBeats[divIndex()];
    t[S_BarBeats] = barA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_Signal] = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
    t[S_Cycles] = (float)cyc;
    t[S_Stages] = (float)n;
    t[S_Notches] = (float)notchCount(n, fb);
    int32_t k = 0;
    for (int i = 0; i < kTele && k < maxSamples; ++i) out[k++] = t[i];
    return k;
}

const char* Phaser::paramName(int32_t i) const {
    switch (i) {
    case Rate: return "Rate";
    case Center: return "Center";
    case Depth: return "Depth";
    case Feedback: return "Feedback";
    case Mix: return "Mix";
    case Waveform: return "Waveform";
    case Sync: return "Sync";
    case Division: return "Division";
    case Stereo: return "Stereo";
    case Stages: return "Stages";
    default: return "";
    }
}

float Phaser::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Phaser::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

std::string Phaser::deviceText(int32_t id) const {
    static const char* waves[3] = {"Sine", "Triangle", "Saw"};
    char b[1600];
    if (id == 0) {
        const int n = stageCount();
        std::string s;
        std::snprintf(b, sizeof b, "%d stages (%d notches) - %s", n, notchCount(n, feedback()), waves[waveIndex()]);
        s += b;
        if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
        else
            std::snprintf(b, sizeof b, " - %.2f Hz", rateFreeHz());
        s += b;
        std::snprintf(b, sizeof b,
            " - stereo %.0f deg - center %.0f Hz - depth +/-%.1f oct - feedback %+.0f %% (%s) - mix %.0f %%",
            stereoDeg(), centerHz(), kDepthOct * get(Depth), feedback() * 100.0, modeWord(), get(Mix) * 100.0f);
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
            "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - fc L %.0f Hz, R %.0f Hz "
            "(sweep %.0f .. %.0f Hz) - %d stages, %d notches - first notch %.0f Hz (sweeps %.0f .. %.0f Hz), "
            "%.1f dB deep - peaks %+.1f dB - in %s dBFS - out L %s, R %s dBFS - %.1f BPM, bar %.2g beats - "
            "transport %s - CPU %.2f %%",
            sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
            sc[S_Locked] > 0.5f ? " locked to the song"
            : get(Sync) >= 0.5f ? " free-running at the synced rate"
                                : "",
            sc[S_WinPhase], sc[S_FcL], sc[S_FcR], sc[S_FcMin], sc[S_FcMax], (int)sc[S_Stages], (int)sc[S_Notches],
            sc[S_NotchHz], sc[S_NotchMinHz], sc[S_NotchMaxHz], sc[S_NullDb], sc[S_PeakDb], lv(sc[S_InPeak]).c_str(),
            lv(sc[S_OutPeakL]).c_str(), lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats],
            sc[S_Playing] > 0.5f ? "playing" : "stopped", sc[S_Cpu] * 100.0f);
        return b;
    }
    if (id == 2) {
        std::string divs, sts;
        for (int i = 0; i < kDivs; ++i) {
            std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
            divs += b;
        }
        for (int i = 0; i < kStageOpts; ++i) {
            std::snprintf(b, sizeof b, "%s%.2f = %d", i ? ", " : "", (double)i / (kStageOpts - 1), kStageCounts[i]);
            sts += b;
        }
        return "All params are normalized 0..1. Rate: the free LFO rate, exponential 0.02 Hz (0) .. 8 Hz (1) "
               "(0.269 = 0.1 Hz, 0.384 = 0.2 Hz, 0.5 = 0.4 Hz, 0.537 = 0.5 Hz, 0.653 = 1 Hz, 0.769 = 2 Hz, 0.884 = 4 "
               "Hz). "
               "Center: the middle of the sweep, exponential 50 Hz (0) .. 5 kHz (1) (0.151 = 100 Hz, 0.301 = 200 Hz, "
               "0.5 = 500 Hz, 0.602 = 800 Hz, 0.65 = 1 kHz, 0.801 = 2 kHz, 1 = 5 kHz). Depth: how far the LFO swings "
               "the "
               "corner, 0..1 = 0 .. +/-3 octaves - fc = Center * 2^(3 * Depth * lfo). Feedback: bipolar, (v - 0.5) * "
               "1.9 = "
               "-95 % .. +95 % (0.5 = none, 0.711 = +40 %, 0.184 = -60 %); positive sharpens the peaks between the "
               "notches, "
               "negative also flips the wet polarity so the notches move and widen (one fewer); past 85 % the phaser "
               "whistles. Mix: 0 = dry, 0.5 = dry and wet equal (the deepest notches), 1 = wet only (phase shift, "
               "almost no "
               "notches). Waveform: 0 Sine, 0.5 Triangle, 1 Saw. Stereo: the right LFO's offset from the left, 0..1 = "
               "0..180 deg (0 = mono sweep, 0.5 = 90 deg, 1 = opposite). Stages (all-pass stages; notches = stages / "
               "2): " +
               sts +
               ". Sync: >= 0.5 takes the rate from the tempo (Division) and locks the LFO to the song position "
               "while the transport plays, restarting on the bar. Division (with Sync): " +
               divs + ".";
    }
    return {};
}

double Phaser::mag(double dry, double wet, double fb, double th) {
    const double w = fb < 0.0 ? -wet : wet, c = std::cos(th), s = std::sin(th);
    const double nr = w * c, ni = -w * s, dr = 1.0 - fb * c, di = fb * s, den = dr * dr + di * di;
    const double re = dry + (nr * dr + ni * di) / den, im = (ni * dr - nr * di) / den;
    return std::sqrt(re * re + im * im) / std::max(dry + wet, 0.001);
}

double Phaser::phaseLag(double f, double fc, int n, double sr) {
    const double nyq = 0.5 * sr;
    const double tf = std::tan(kPi * std::min(f, nyq * 0.9999) / sr),
                 tc = std::tan(kPi * std::min(fc, nyq * 0.9999) / sr);
    return n * 2.0 * std::atan(tf / std::max(tc, 1e-9));
}

double Phaser::freqAtLag(double th, double fc, int n, double sr) {
    const double tc = std::tan(kPi * std::min(fc, 0.49999 * sr) / sr);
    return sr / kPi * std::atan(tc * std::tan(th / (2.0 * n)));
}

double Phaser::firstNotchHz(double fc, int n, double fb, double sr) {
    return freqAtLag(fb < 0.0 ? 2.0 * kPi : kPi, fc, n, sr);
}

const char* Phaser::modeWord() const {
    const double fb = feedback();
    return std::fabs(fb) >= 0.85 ? "resonant" : fb <= -0.15 ? "negative" : fb >= 0.15 ? "positive" : "clean";
}

double Phaser::coef(double fc) const {
    const double t = std::tan(kPi * fc / sr_);
    return (t - 1.0) / (t + 1.0);
}

inline double Phaser::chain(double* s, double a, double x, double fb) const {
    const int n = stages_;
    double g = 1.0, S = 0.0;
    for (int k = 0; k < n; ++k) {
        S = a * S + s[k];
        g *= a;
    }
    const double y = (g * x + S) / (1.0 - fb * g);
    double v = x + fb * y;
    for (int k = 0; k < n; ++k) {
        const double o = a * v + s[k];
        s[k] = sanitize(v - a * o);
        v = o;
    }
    return v;
}

double Phaser::lfo(int wave, double p) {
    const double f = p - std::floor(p);
    if (wave == 0) return std::sin(2.0 * kPi * f);
    if (wave == 1) return f < 0.25 ? 4.0 * f : f < 0.75 ? 2.0 - 4.0 * f : 4.0 * f - 4.0;
    const double g = f + 0.5;
    return 2.0 * (g - std::floor(g)) - 1.0;
}

void Phaser::clearStages() {
    for (int k = 0; k < kMaxStages; ++k) sL_[k] = sR_[k] = 0.0;
}

} // namespace nota
