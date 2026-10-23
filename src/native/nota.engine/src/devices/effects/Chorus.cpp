// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chorus implementation.
// Verbatim DSP, all std::-exact: the LFO steers the modulated delay reads and the
// warmth tanh sits inside the feedback loop. Silent loops auto-sleep via isIdle().

#include "Chorus.h"

#include "dsp/math/SimdAudio.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>

namespace nota {

Chorus::Chorus() {
    p_[Rate].store(0.6157f); // 0.80 Hz
    p_[Mode].store(0.0f);    // Classic
    p_[Sync].store(0.0f);    // Free (Hz)
    p_[Division].store((float)kDefaultDiv / (kDivs - 1));
    p_[Offset].store(1.0f); // 180°
    p_[Hpf].store(0.0f);    // off
    p_[Amount].store(0.40f);
    p_[Feedback].store(0.5f); // 0
    p_[Width].store(0.5f);    // 100 %
    p_[Warmth].store(0.0f);
    p_[Mix].store(0.50f);
    setSampleRate(44100.0, 0);
}

void Chorus::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    cycle_ = 0;
    frac_ = 0.0;
    clearLines();
    snap_ = true;
    resetMeters();
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
}

void Chorus::setTransport(double beatStart, double spb, bool playing) {
    beatStart_ = beatStart;
    spb_ = spb > 0 ? spb : 0.0;
    playing_ = playing;
}

void Chorus::setTransportInfo(const TransportInfo& ti) {
    if (ti.tsNum > 0 && ti.tsDenom > 0) barBeats_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 0.5, 64.0);
}

void Chorus::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

void Chorus::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (restartReq_.exchange(false, std::memory_order_acquire)) {
        cycle_ = 0;
        frac_ = 0.0;
    }
    if (clearReq_.exchange(false, std::memory_order_acquire)) clearLines();
    if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

    const int modeT = modeIndex();
    const double amount = std::clamp((double)get(Amount), 0.0, 1.0);
    const double fb = feedback();
    const double offT = offsetDeg() / 360.0;
    const double width = widthPct() / 100.0;
    const double warmth = std::clamp((double)get(Warmth), 0.0, 1.0);
    const bool sync = get(Sync) >= 0.5f;
    const double div = kDivBeats[divIndex()];
    const double spb = spb_ > 0.0 ? spb_ : sr_ * 0.5; // 120 BPM until the engine says otherwise
    const double rateHz = sync ? sr_ / (spb * div) : rateFreeHz();
    const double inc = rateHz / sr_;
    const bool locked = sync && playing_ && spb_ > 0.0;
    const double span = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
    const double sm = 1.0 - std::exp(-1.0 / (0.015 * sr_)); // ~15 ms parameter glide
    const double fadeStep = 1.0 / (0.008 * sr_);            // ~8 ms mode fade
    const double msToS = sr_ * 0.001;
    const double maxD = (double)(kBuf - 4);
    const double hpHz = hpfHz();
    const bool hpOn = hpHz > 0.0;

    if (snap_) {
        mode_ = modeT;
        amountS_ = amount;
        fbS_ = fb;
        offS_ = offT;
        widthS_ = width;
        warmS_ = warmth;
        const MixW mw = mixWeights(modeT);
        dryS_ = mw.dry;
        wetS_ = mw.wet;
        fade_ = 1.0;
        hpS_ = hpOn ? hpHz : 0.0;
        snap_ = false;
    }

    // Idle skip: silent input on a settled loop leaves the buffer untouched. The LFO
    // phase keeps advancing so the wake-up voices have no timing jump. Sleep engages
    // only after the full feed-through flight time (a voice can arrive long after the
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

    float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f, pushPk = 0.0f;
    double tau[3] = {0.0, 0.0, 0.0};
    for (int32_t i = 0; i < frames; ++i) {
        if (locked) {
            const double beat = beatStart_ + (double)i / spb_;
            const double spans = std::floor(beat / span);
            const double c = (beat - spans * span) / div;
            const double k = std::floor(c);
            cycle_ = (int64_t)spans * 4096 + (int64_t)k;
            frac_ = c - k;
        }
        // A Mode change: fade the wet out, switch while it is silent, fade back in.
        if (modeT != mode_) {
            fade_ -= fadeStep;
            if (fade_ <= 0.0) {
                fade_ = 0.0;
                mode_ = modeT;
            }
        } else if (fade_ < 1.0) {
            fade_ = std::min(1.0, fade_ + fadeStep);
        }
        const MixW mw = mixWeights(mode_);
        amountS_ += sm * (amount - amountS_);
        fbS_ += sm * (fb - fbS_);
        offS_ += sm * (offT - offS_);
        widthS_ += sm * (width - widthS_);
        warmS_ += sm * (warmth - warmS_);
        dryS_ += sm * (mw.dry - dryS_);
        wetS_ += sm * (mw.wet - wetS_);

        // Control-rate coefficients: the HPF (TPT SVF, Q 0.707) and the warmth low-pass.
        if ((i & 15) == 0) {
            if (hpOn) {
                hpS_ = hpS_ > 0.0 ? hpS_ + 16.0 * sm * (hpHz - hpS_) : hpHz;
                const double g = std::tan(kPi * std::min(hpS_, sr_ * 0.45) / sr_);
                hpG_ = g;
                hpK_ = 1.4142135623730951;
                hpA1_ = 1.0 / (1.0 + g * (g + hpK_));
            } else {
                hpS_ = 0.0;
            }
            const double lpHz = 20000.0 * std::pow(0.125, warmS_); // 20 kHz … 2.5 kHz
            lpA_ = 1.0 - std::exp(-2.0 * kPi * std::min(lpHz, sr_ * 0.45) / sr_);
        }

        const double base = kBaseMs[mode_], dep = kDepMs[mode_] * amountS_;
        const int nv = mode_ == 1 ? 3 : 2;
        for (int v = 0; v < nv; ++v) tau[v] = base + dep * std::sin(2.0 * kPi * (frac_ + voicePhase(mode_, v, offS_)));
        if (nv == 2) tau[2] = 0.0;

        const float xL = buf[i * 2], xR = buf[i * 2 + 1];
        inPk = std::max(inPk, std::max(std::fabs(xL), std::fabs(xR)));

        // Voices → wet L / R.
        double wL, wR;
        const double d0 = std::clamp(tau[0] * msToS, 2.0, maxD), d1 = std::clamp(tau[1] * msToS, 2.0, maxD);
        if (nv == 3) {
            const double d2 = std::clamp(tau[2] * msToS, 2.0, maxD);
            const double v1 = readLine(lineL_, d0);
            const double v2 = 0.5 * (readLine(lineL_, d1) + readLine(lineR_, d1));
            const double v3 = readLine(lineR_, d2);
            wL = (v1 + kCentre * v2) * kEnsNorm;
            wR = (v3 + kCentre * v2) * kEnsNorm;
        } else {
            wL = readLine(lineL_, d0);
            wR = readLine(lineR_, d1);
        }

        // The line input: HPF'd dry + feedback, through the warmth stage.
        double hL = xL, hR = xR;
        if (hpOn) {
            hL = hpTick(hpL_, xL);
            hR = hpTick(hpR_, xR);
        }
        const float pushL = sanitize((float)warm(lpL_, hL + fbS_ * wL));
        const float pushR = sanitize((float)warm(lpR_, hR + fbS_ * wR));
        lineL_[w_] = pushL;
        lineR_[w_] = pushR;
        pushPk = std::max(pushPk, std::max(std::fabs(pushL), std::fabs(pushR)));
        w_ = (w_ + 1) & kMask;

        // Width on the wet (M/S).
        const double m = 0.5 * (wL + wR), s = 0.5 * (wL - wR) * widthS_;
        const double wet = wetS_ * fade_;
        const double norm = 1.0 / std::max(dryS_ + wetS_, 1e-3);
        const float oL = (float)((dryS_ * xL + wet * (m + s)) * norm);
        const float oR = (float)((dryS_ * xR + wet * (m - s)) * norm);
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

    // Loop-feed peak (decays slowly so a ringing tail blocks accrual); sleep
    // additionally waits out the voices' flight time so a late voice arriving after
    // a quiet gap is never cut: output silence alone proves nothing while the lines
    // are full.
    feedPk_ = pushPk > feedPk_ ? pushPk : feedPk_ * 0.90f;
    const double flight = 0.018 * sr_; // Ensemble 12 ms + 6 ms, the longest modulated line
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
        const double dep = kDepMs[mode_] * amountS_;
        for (int v = 0; v < 3; ++v) {
            tauA_[v].store((float)tau[v], std::memory_order_relaxed);
            const bool live = v < (mode_ == 1 ? 3 : 2);
            centsA_[v].store(live ? (float)centsAt(dep, rateHz, frac_ + voicePhase(mode_, v, offS_)) : 0.0f,
                std::memory_order_relaxed);
        }
        modeA_.store(mode_, std::memory_order_relaxed);
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

void Chorus::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id == A_Restart) restartReq_.store(true, std::memory_order_release);
    else if (id == A_Clear)
        clearReq_.store(true, std::memory_order_release);
    else if (id == A_ResetMeters)
        resetReq_.store(true, std::memory_order_release);
    wake();
}

int32_t Chorus::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    const int64_t cyc = cycleA_.load(std::memory_order_relaxed);
    const double fr = fracA_.load(std::memory_order_relaxed);
    const int64_t base = cyc - (((cyc % 2) + 2) % 2); // the window starts on an even cycle
    const int md = modeIndex();
    const double bms = kBaseMs[md], dep = depMs();
    const float rate = rateA_.load(std::memory_order_relaxed);
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeakL] = outPkLA_.load(std::memory_order_relaxed);
    t[S_OutPeakR] = outPkRA_.load(std::memory_order_relaxed);
    const bool fresh = modeA_.load(std::memory_order_relaxed) == md && tauA_[0].load(std::memory_order_relaxed) > 0.0f;
    for (int v = 0; v < 3; ++v) {
        const bool live = v < voices();
        t[S_Tau1 + v] = !live ? 0.0f : fresh ? tauA_[v].load(std::memory_order_relaxed) : (float)bms;
        t[S_Cents1 + v] = !live || !fresh ? 0.0f : centsA_[v].load(std::memory_order_relaxed);
    }
    t[S_WinPhase] = (float)((double)(cyc - base) + fr);
    t[S_RateHz] = rate;
    t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    t[S_Playing] = (float)playingA_.load(std::memory_order_relaxed);
    t[S_Locked] = (float)lockedA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_BaseMs] = (float)bms;
    t[S_TauMin] = (float)(bms - dep);
    t[S_TauMax] = (float)(bms + dep);
    t[S_MaxCents] = (float)maxCents(dep, rate);
    t[S_Voices] = (float)voices();
    t[S_Mode] = (float)md;
    t[S_HpHz] = (float)hpfHz();
    t[S_WidthPct] = (float)widthPct();
    t[S_DivBeats] = (float)kDivBeats[divIndex()];
    t[S_BarBeats] = barA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_Signal] = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
    t[S_Cycles] = (float)cyc;
    int32_t n = 0;
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    return n;
}

const char* Chorus::paramName(int32_t i) const {
    switch (i) {
    case Rate: return "Rate";
    case Mode: return "Mode";
    case Sync: return "Sync";
    case Division: return "Division";
    case Offset: return "Offset";
    case Hpf: return "HPF";
    case Amount: return "Amount";
    case Feedback: return "Feedback";
    case Width: return "Width";
    case Warmth: return "Warmth";
    case Mix: return "Mix";
    default: return "";
    }
}

float Chorus::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Chorus::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

std::string Chorus::deviceText(int32_t id) const {
    char b[1400];
    const int md = modeIndex();
    if (id == 0) {
        std::string s = kModeNames[md];
        if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
        else
            std::snprintf(b, sizeof b, " - %.2f Hz", rateFreeHz());
        s += b;
        if (md == 1) s += " - 3 voices 120 deg apart";
        else {
            std::snprintf(b, sizeof b, " - offset %.0f deg", offsetDeg());
            s += b;
        }
        const double hp = hpfHz();
        if (hp > 0.0) {
            std::snprintf(b, sizeof b, " - HPF %.0f Hz", hp);
            s += b;
        } else
            s += " - HPF off";
        std::snprintf(b, sizeof b, " - amount %.0f %% - feedback %+.0f %% - width %.0f %% - warmth %.0f %%",
            get(Amount) * 100.0f, feedback() * 100.0, widthPct(), get(Warmth) * 100.0f);
        s += b;
        if (md == 2) s += " - mix wet (vibrato)";
        else {
            std::snprintf(b, sizeof b, " - mix %.0f %%", get(Mix) * 100.0f);
            s += b;
        }
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
        std::string s;
        std::snprintf(b, sizeof b,
            "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - %d voices:", sc[S_RateHz],
            1000.0 / std::max(1e-3f, sc[S_RateHz]),
            sc[S_Locked] > 0.5f ? " locked to the song"
            : get(Sync) >= 0.5f ? " free-running at the synced rate"
                                : "",
            sc[S_WinPhase], (int)sc[S_Voices]);
        s += b;
        for (int v = 0; v < (int)sc[S_Voices]; ++v) {
            std::snprintf(b, sizeof b, " %s %.2f ms %+.1f ct%s", voiceName(md, v), sc[S_Tau1 + v], sc[S_Cents1 + v],
                v + 1 < (int)sc[S_Voices] ? "," : "");
            s += b;
        }
        std::snprintf(b, sizeof b,
            " - sweep %.2f .. %.2f ms - detune up to +/-%.1f ct - in %s dBFS - out L %s, R %s dBFS - "
            "%.1f BPM, bar %.2g beats - transport %s - CPU %.2f %%",
            sc[S_TauMin], sc[S_TauMax], sc[S_MaxCents], lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeakL]).c_str(),
            lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats], sc[S_Playing] > 0.5f ? "playing" : "stopped",
            sc[S_Cpu] * 100.0f);
        s += b;
        return s;
    }
    if (id == 2) {
        std::string divs;
        for (int i = 0; i < kDivs; ++i) {
            std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
            divs += b;
        }
        return "All params are normalized 0..1. Mode: 0 Classic (2 voices L / R, tau 8 ms +/- 5 ms * Amount), 0.5 "
               "Ensemble "
               "(3 voices 120 deg apart, left / centre / right, tau 12 +/- 6 ms), 1 Vibrato (the L / R pair on 3 +/- "
               "2.5 ms, "
               "wet only - Mix is ignored). Rate: the free LFO rate, exponential 0.02 Hz (0) .. 8 Hz (1) (0.269 = 0.1 "
               "Hz, "
               "0.384 = 0.2 Hz, 0.537 = 0.5 Hz, 0.616 = 0.8 Hz, 0.653 = 1 Hz, 0.769 = 2 Hz, 0.884 = 4 Hz, 0.938 = 5.5 "
               "Hz). "
               "Offset: the right voice's LFO behind the left, 0..1 = 0..180 deg (Classic / Vibrato). HPF: the wet "
               "path's "
               "high-pass, 0 = off, else exponential 20 Hz .. 2 kHz (0.15 = 40 Hz, 0.35 = 100 Hz, 0.5 = 200 Hz, 0.65 = "
               "400 Hz). "
               "Amount: how far the delay swings, 0..1 of the mode's range. Feedback: bipolar, (v - 0.5) * 1.8 = -90 % "
               ".. +90 % "
               "(0.5 = none); past 60 % it rings like a flanger. Width: the wet's stereo width, 0..1 = 0..200 % (0.5 = "
               "100 %; "
               "0 = mono; above 100 % lifts the side up to +6 dB). Warmth: 0 = clean, 1 = a dark, saturated "
               "bucket-brigade "
               "line (low-pass 20 kHz .. 2.5 kHz). Mix: 0 = dry, 0.5 = dry and wet equal, 1 = wet only. Sync: >= 0.5 "
               "takes "
               "the rate from the tempo (Division) and locks the LFO to the song position while the transport plays, "
               "restarting on the bar. Division (with Sync): " +
               divs + ".";
    }
    return {};
}

double Chorus::centsAt(double depMs, double rateHz, double p) {
    const double dt = depMs * 0.001 * 2.0 * kPi * rateHz * std::cos(2.0 * kPi * p);
    return 1200.0 * std::log2(std::max(1.0 - dt, 0.01));
}

Chorus::MixW Chorus::mixWeights(int mode) const {
    if (mode == 2) return {0.0, 1.0}; // Vibrato: wet only
    const double mix = std::clamp((double)get(Mix), 0.0, 1.0);
    return {std::min(1.0, 2.0 * (1.0 - mix)), std::min(1.0, 2.0 * mix)};
}

double Chorus::voicePhase(int mode, int v, double off) {
    if (mode == 1) return v / 3.0;
    return v == 0 ? 0.0 : off;
}

const char* Chorus::voiceName(int mode, int v) {
    if (mode == 1) return v == 0 ? "V1" : v == 1 ? "V2" : "V3";
    return v == 0 ? "L" : "R";
}

double Chorus::hpTick(Svf& f, double x) {
    const double hp = (x - (hpK_ + hpG_) * f.s1 - f.s2) * hpA1_;
    const double v1 = hpG_ * hp, bp = v1 + f.s1;
    f.s1 = bp + v1;
    const double v2 = hpG_ * bp, lp = v2 + f.s2;
    f.s2 = lp + v2;
    return hp;
}

double Chorus::warm(double& z, double x) const {
    if (warmS_ < 1e-4) {
        z = x;
        return x;
    }
    z += lpA_ * (x - z);
    const double k = 1.0 + 2.5 * warmS_;
    return z + warmS_ * (std::tanh(k * z) / k - z);
}

inline float Chorus::readLine(const float* line, double d) const {
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

void Chorus::clearLines() {
    std::memset(lineL_, 0, sizeof lineL_);
    std::memset(lineR_, 0, sizeof lineR_);
    w_ = 0;
    hpL_ = hpR_ = Svf{};
    lpL_ = lpR_ = 0.0;
}

} // namespace nota
