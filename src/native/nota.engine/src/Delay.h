// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in stereo delay ("Nota Delay" — almanac update) on the Chamber frame: independent
// L/R times (free ms or tempo-synced divisions 1/16…1/2 incl. triplet/dotted), Link L/R,
// feedback, ping-pong cross-feed, stereo spread, a tone shaper in the loop (low cut · high
// cut · allpass diffusion · tape saturation), tape-style WOW modulation (rate · depth),
// Freeze (infinite loop, input muted) and an output stage (dry level · dry/wet · width ·
// bass mono · wet only · output trim). Tempo comes from Device::setTransport.
//
// Changing the delay time either crossfades to the new tap (Fade on Change — no pitch
// artefact) or glides to it at a limited rate (tape repitch). The diffuser sits in the loop
// and lengthens it by a fixed group delay; Latency Comp pulls the read pointer back by that
// much so the echo lands exactly on the set time.
//
// All params are normalized 0..1 (denormalized in process()); persist/clone/automation
// are generic through the base Device. Param order is the persisted layout — APPEND ONLY.
// The ring buffers are allocated on the message thread in setSampleRate, so process()
// never allocates. A core Device (JUCE-free).

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Delay : public Device {
public:
    enum {
        SyncMode = 0, TimeL, TimeR, DivL, DivR, LinkLR, Feedback, Spread,
        PingPong, WowRate, WowDepth, Freeze, DryWet, Output,
        // ---- appended for the almanac update — keep the order, append only ----
        DryLevel, Diffuse, LowCut, HighCut, TapeMode, FadeChange,
        Width, BassMono, WetOnly, LatencyComp, kNumParams
    };
    // Scope telemetry layout (read by the card).
    enum {
        S_WetPk = 0, S_OutL, S_OutR, S_Cpu, S_SampleRate, S_Bpm,
        S_DiffuseSmp, S_Frozen, S_MsL, S_MsR, S_Latency, kScope
    };
    // deviceAction ids.
    enum { A_ClearLoop = 0 };

    static constexpr float kMaxTimeMs = 2000.0f;
    static constexpr int kNumDiv = 8;
    // Division lengths in beats (quarter = 1): 1/16, 1/8T, 1/8, 1/8·, 1/4T, 1/4, 1/4·, 1/2.
    static constexpr double kDivBeats[kNumDiv] = {0.25, 1.0 / 3.0, 0.5, 0.75, 2.0 / 3.0, 1.0, 1.5, 2.0};

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        size_ = static_cast<int>(sr_ * (kMaxTimeMs / 1000.0)) + static_cast<int>(sr_ * 0.12) + 4;
        bufL_.assign(size_, 0.0f); bufR_.assign(size_, 0.0f);
        write_ = 0;
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
        srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    }
    void setTransport(double, double samplesPerBeat, bool) override {
        if (samplesPerBeat > 0.0) spb_ = samplesPerBeat;
    }

    void process(float* buf, int32_t frames) override {
        if (size_ <= 4 || frames <= 0) return;
        const auto t0 = std::chrono::steady_clock::now();
        if (clear_.exchange(false, std::memory_order_relaxed)) wipe();

        const bool sync   = on(SyncMode) && spb_ > 0.0;
        const bool link   = on(LinkLR);
        const bool ping   = on(PingPong);
        const bool freeze = on(Freeze);
        const bool tape   = on(TapeMode);
        const bool fade   = on(FadeChange);
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
        const double apLen = static_cast<double>(ap_[0].n + ap_[1].n);   // loop group delay
        if (on(LatencyComp)) { tgtL -= apLen; tgtR -= apLen; }
        const double lo = 1.0, hi = static_cast<double>(size_) - 3.0;
        tgtL = std::clamp(tgtL, lo, hi); tgtR = std::clamp(tgtR, lo, hi);
        if (curL_ <= 0.0) { curL_ = tgtL; curR_ = tgtR; oldL_ = tgtL; oldR_ = tgtR; }

        if (fade) {
            // Crossfade to the new tap; a fade already running finishes first, so dragging
            // a time knob does not restart it every block.
            if (fadePos_ >= fadeLen_ && (std::fabs(tgtL - curL_) > 0.5 || std::fabs(tgtR - curR_) > 0.5)) {
                oldL_ = curL_; oldR_ = curR_; curL_ = tgtL; curR_ = tgtR; fadePos_ = 0;
            }
        } else {
            fadePos_ = fadeLen_;   // repitch: one tap, glided below
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
        const float tapeC = onePole(8000.0);                       // tape head loss
        const float drive = tape ? 1.0f + 2.0f * clamp01(Feedback) : 0.0f;
        const float apG = static_cast<float>(0.62 * diffuse);
        const float monoC = onePole(std::max(20.0, expMap(clamp01(BassMono), 30.0, 500.0)));
        const bool monoOn = clamp01(BassMono) > 0.001f;

        float wetPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;

        for (int32_t i = 0; i < frames; ++i) {
            if (!fade) {
                curL_ += std::clamp(tgtL - curL_, -glide, glide);
                curR_ += std::clamp(tgtR - curR_, -glide, glide);
            } else if (fadePos_ < fadeLen_) {
                ++fadePos_;
            }
            const float lfo = std::sin(kTwoPi * wowPhase_);
            wowPhase_ += wowInc; if (wowPhase_ >= 1.0) wowPhase_ -= 1.0;
            const double mod = static_cast<double>(lfo * wowDepth);

            float rdL, rdR;
            if (fadePos_ < fadeLen_) {
                const float t = static_cast<float>(fadePos_) / static_cast<float>(fadeLen_);
                const float a = 1.0f - t;
                rdL = read(bufL_, oldL_ + mod) * a + read(bufL_, curL_ + mod) * t;
                rdR = read(bufR_, oldR_ - mod) * a + read(bufR_, curR_ - mod) * t;
            } else {
                rdL = read(bufL_, curL_ + mod);
                rdR = read(bufR_, curR_ - mod);
            }

            const float l = buf[i * 2], r = buf[i * 2 + 1];
            float wl = ping ? rdR : rdL, wr = ping ? rdL : rdR;
            float inpL = l * inGain + wl * fb;
            float inpR = r * inGain + wr * fb;
            if (!freeze) {
                // The loop's tone: cuts, tape head loss + saturation. Frozen, the loop is
                // left untouched so it holds instead of slowly dulling.
                if (hpOn) { hpL_ += hpC * (inpL - hpL_); inpL -= hpL_; hpR_ += hpC * (inpR - hpR_); inpR -= hpR_; }
                if (lpOn) { lpL_ += lpC * (inpL - lpL_); inpL = lpL_; lpR_ += lpC * (inpR - lpR_); inpR = lpR_; }
                if (tape) {
                    tapeL_ += tapeC * (inpL - tapeL_); inpL = std::tanh(tapeL_ * drive) / std::tanh(drive);
                    tapeR_ += tapeC * (inpR - tapeR_); inpR = std::tanh(tapeR_ * drive) / std::tanh(drive);
                }
            }
            inpL = ap_[1].tick(ap_[0].tick(inpL, apG), apG);
            inpR = ap_[3].tick(ap_[2].tick(inpR, apG), apG);
            bufL_[write_] = inpL; bufR_[write_] = inpR;
            if (++write_ >= size_) write_ = 0;

            // ---- output stage: width, bass mono, mix, trim ----
            float outWetL = rdL, outWetR = rdR;
            if (width != 1.0f || monoOn) {
                float mid = (outWetL + outWetR) * 0.5f, side = (outWetL - outWetR) * 0.5f * width;
                if (monoOn) { monoL_ += monoC * (side - monoL_); side -= monoL_; }
                outWetL = mid + side; outWetR = mid - side;
            }
            const float oL = (dryG * l + wetG * outWetL) * outG;
            const float oR = (dryG * r + wetG * outWetR) * outG;
            buf[i * 2] = oL; buf[i * 2 + 1] = oR;

            wetPk = std::max(wetPk, std::fabs(outWetL * wetG));
            outPkL = std::max(outPkL, std::fabs(oL)); outPkR = std::max(outPkR, std::fabs(oR));
        }

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

    // ---- identity / params -------------------------------------------------------
    const char* displayName() const override { return "Nota Delay"; }
    int32_t     builtinKind() const override { return 3; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Sync", "Time L", "Time R", "Div L", "Div R", "Link L/R", "Feedback",
            "Spread", "Ping-Pong", "Wow Rate", "Wow Depth", "Freeze", "Dry/Wet", "Output",
            "Dry Level", "Diffuse", "Low Cut", "High Cut", "Tape Mode", "Fade on Change",
            "Width", "Bass Mono", "Wet Only", "Latency Comp" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Nothing is added to the dry path — the wet is the effect itself.
    int32_t latencySamples() const override { return 0; }

    void deviceAction(int32_t id, int32_t, float) override {
        if (id == A_ClearLoop) clear_.store(true, std::memory_order_relaxed);
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
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

    // id 0 — a one-line summary of what the delay is doing now (the card's status strip,
    // and get_device_text over MCP).
    std::string deviceText(int32_t id) const override {
        if (id != 0) return {};
        char b[256];
        const bool sync = getParam(SyncMode) >= 0.5f && spb_ > 0.0;
        const bool link = getParam(LinkLR) >= 0.5f;
        const float msl = msL_.load(std::memory_order_relaxed), msr = msR_.load(std::memory_order_relaxed);
        if (getParam(Freeze) >= 0.5f) {
            std::snprintf(b, sizeof b, "Freeze: loop held, input muted - %.0f ms / %.0f ms - mix %.0f %%",
                          msl, msr, getParam(DryWet) * 100.0f);
        } else if (sync) {
            const int dl = divIndex(DivL), dr = link ? dl : divIndex(DivR);
            static const char* nm[kNumDiv] = { "1/16", "1/8T", "1/8", "1/8.", "1/4T", "1/4", "1/4.", "1/2" };
            std::snprintf(b, sizeof b, "Sync %s / %s%s - feedback %.0f %% - mix %.0f %%",
                          nm[dl], nm[dr], getParam(PingPong) >= 0.5f ? " - ping-pong" : "",
                          getParam(Feedback) * 100.0f, getParam(DryWet) * 100.0f);
        } else {
            std::snprintf(b, sizeof b, "Free %.0f ms / %.0f ms%s - feedback %.0f %% - mix %.0f %%",
                          msl, msr, getParam(PingPong) >= 0.5f ? " - ping-pong" : "",
                          getParam(Feedback) * 100.0f, getParam(DryWet) * 100.0f);
        }
        return std::string(b);
    }

    Delay() {
        set(SyncMode, 1.0f); set(TimeL, 0.35f); set(TimeR, 0.35f); set(DivL, 2.0f / 7.0f); set(DivR, 2.0f / 7.0f);
        set(LinkLR, 0.0f); set(Feedback, 0.38f); set(Spread, 0.0f); set(PingPong, 0.0f);
        set(WowRate, 0.3f); set(WowDepth, 0.15f); set(Freeze, 0.0f); set(DryWet, 0.3f); set(Output, 0.5f);
        set(DryLevel, 0.70711f); set(Diffuse, 0.0f); set(LowCut, 0.0f); set(HighCut, 1.0f);
        set(TapeMode, 0.0f); set(FadeChange, 1.0f); set(Width, 0.5f); set(BassMono, 0.0f);
        set(WetOnly, 0.0f); set(LatencyComp, 1.0f);
    }

private:
    static constexpr double kTwoPi = 6.283185307179586;

    // A Schroeder allpass: lossless, so a frozen loop neither grows nor decays.
    struct AllPass {
        std::vector<float> z; int n = 0, w = 0;
        void resize(int len) { n = std::max(1, len); z.assign(n, 0.0f); w = 0; }
        void wipe() { std::fill(z.begin(), z.end(), 0.0f); }
        float tick(float x, float g) {
            const float d = z[w];
            const float v = x + g * d;
            z[w] = v; if (++w >= n) w = 0;
            return d - g * v;
        }
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool on(int i) const { return get(i) >= 0.5f; }
    float clamp01(int i) const { return std::clamp(get(i), 0.0f, 1.0f); }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    // Dry trim: 0 dB at 1/√2, up to +6 dB at the top, silent at 0.
    static float dryGain(float v) { return 2.0f * v * v; }
    static float decayPeak(float held, float now) { return now > held ? now : held * 0.82f; }
    float onePole(double hz) const { return static_cast<float>(1.0 - std::exp(-kTwoPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_)); }

    int divIndex(int p) const { return std::clamp(static_cast<int>(std::lround(std::clamp(getParam(p), 0.0f, 1.0f) * (kNumDiv - 1))), 0, kNumDiv - 1); }

    double delaySamples(int timeParam, int divParam, bool sync) const {
        if (sync) return kDivBeats[divIndex(divParam)] * spb_;
        const double ms = std::clamp(static_cast<double>(clamp01(timeParam)) * kMaxTimeMs, 1.0, static_cast<double>(kMaxTimeMs));
        return ms * 0.001 * sr_;
    }
    float read(const std::vector<float>& b, double delay) const {
        double rp = write_ - std::clamp(delay, 1.0, static_cast<double>(size_) - 3.0);
        while (rp < 0) rp += size_;
        const int i0 = static_cast<int>(rp); const float frac = static_cast<float>(rp - i0);
        int i1 = i0 + 1; if (i1 >= size_) i1 -= size_;
        return b[i0] + (b[i1] - b[i0]) * frac;
    }
    void wipe() {
        std::fill(bufL_.begin(), bufL_.end(), 0.0f);
        std::fill(bufR_.begin(), bufR_.end(), 0.0f);
        for (auto& a : ap_) a.wipe();
        hpL_ = hpR_ = lpL_ = lpR_ = tapeL_ = tapeR_ = monoL_ = monoR_ = 0.0f;
    }

    double sr_ = 44100.0, spb_ = 0.0, wowPhase_ = 0.0;
    double curL_ = 0.0, curR_ = 0.0, oldL_ = 0.0, oldR_ = 0.0;
    int fadeLen_ = 1024, fadePos_ = 1024;
    int size_ = 0, write_ = 0;
    std::vector<float> bufL_, bufR_;
    AllPass ap_[4];
    float hpL_ = 0, hpR_ = 0, lpL_ = 0, lpR_ = 0, tapeL_ = 0, tapeR_ = 0, monoL_ = 0, monoR_ = 0;
    double cpuS_ = 0.0;
    std::atomic<bool> clear_{false};
    std::atomic<float> mWet_{0}, mOutL_{0}, mOutR_{0}, cpuA_{0}, srA_{44100.0f}, bpmA_{0}, diffA_{0}, msL_{0}, msR_{0};
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
