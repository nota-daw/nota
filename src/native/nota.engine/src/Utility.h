// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in Utility (device kind 4) — reworked to the "show the thing you can't
// hear" mockup (design 3j). A stereo utility: output gain, L/R balance, mid/side
// stereo width, channel mode (Stereo/Left/Right/Swap), mono-below (bass mono up
// to a cutoff), mute and per-channel phase invert. It also packs live telemetry
// through scopeRead — IN/OUT peaks, an inter-channel correlation coefficient and
// a decimated ring of output L/R pairs for a vectorscope — so width and phase,
// whose effect is hard to hear on headphones, become visible.
//
// A core Device (JUCE-free); params are atomic / lock-free. Allocation-free after
// construction.

#pragma once

#include "Device.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class Utility : public Device {
public:
    enum { Gain = 0, Balance, Width, ChannelMode, MonoFreq, MonoBelow, Mute, InvertL, InvertR, kNumParams };

    // Packed scope telemetry (scopeRead): IN/OUT linear peaks (L/R), the output
    // correlation (-1..+1), then kScopePairs decimated output (L,R) pairs.
    enum { S_InL = 0, S_InR, S_OutL, S_OutR, S_Corr, kMeters };
    static constexpr int kScopePairs = 48;
    static constexpr int kScope = kMeters + kScopePairs * 2;

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        decim_ = std::max(1, (int)std::lround(sr_ / 6000.0));   // ~48 pts over ~8 ms
        lpSide_ = 0.0f;
    }

    void process(float* buf, int32_t frames) override {
        const int   mode   = std::clamp((int)std::lround(p_[ChannelMode].load(std::memory_order_relaxed)), 0, 3);
        const bool  mute   = p_[Mute].load(std::memory_order_relaxed) >= 0.5f;
        const bool  invL   = p_[InvertL].load(std::memory_order_relaxed) >= 0.5f;
        const bool  invR   = p_[InvertR].load(std::memory_order_relaxed) >= 0.5f;
        const float widthF = std::clamp(p_[Width].load(std::memory_order_relaxed) / 100.0f, 0.0f, 4.0f);
        const float bal    = std::clamp(p_[Balance].load(std::memory_order_relaxed), -1.0f, 1.0f);
        const float gain   = mute ? 0.0f : std::pow(10.0f, p_[Gain].load(std::memory_order_relaxed) / 20.0f);
        const bool  monoOn = p_[MonoBelow].load(std::memory_order_relaxed) >= 0.5f;
        const float monoHz = std::clamp(p_[MonoFreq].load(std::memory_order_relaxed), 20.0f, 2000.0f);
        const float lpCoef = 1.0f - std::exp(-2.0f * kPi * monoHz / (float)sr_);
        // Constant-power-ish linear balance: attenuate the opposite side.
        const float gL = bal <= 0.0f ? 1.0f : 1.0f - bal;
        const float gR = bal >= 0.0f ? 1.0f : 1.0f + bal;

        float inL = 0, inR = 0, outL = 0, outR = 0;
        double sLL = 0, sRR = 0, sLR = 0;
        int w = ringW_;

        for (int32_t i = 0; i < frames; ++i) {
            float l = buf[i * 2], r = buf[i * 2 + 1];
            inL = std::max(inL, std::fabs(l));
            inR = std::max(inR, std::fabs(r));

            switch (mode) {                       // channel mode
                case 1: r = l; break;             // Left  → both
                case 2: l = r; break;             // Right → both
                case 3: { float t = l; l = r; r = t; break; }  // Swap
                default: break;                   // Stereo
            }

            float mid = 0.5f * (l + r), side = 0.5f * (l - r);
            if (monoOn) { lpSide_ += lpCoef * (side - lpSide_); side -= lpSide_; }  // bass → mono
            side *= widthF;
            l = mid + side; r = mid - side;
            l *= gL; r *= gR;                      // balance
            if (invL) l = -l;
            if (invR) r = -r;
            l *= gain; r *= gain;                  // output gain / mute
            buf[i * 2] = l; buf[i * 2 + 1] = r;

            outL = std::max(outL, std::fabs(l));
            outR = std::max(outR, std::fabs(r));
            sLL += (double)l * l; sRR += (double)r * r; sLR += (double)l * r;

            if (--decimCnt_ <= 0) {                // vectorscope capture (decimated)
                decimCnt_ = decim_;
                ring_[w * 2] = l; ring_[w * 2 + 1] = r;
                w = (w + 1) % kScopePairs;
            }
        }
        ringW_ = w; ringWA_.store(w, std::memory_order_release);

        // Correlation of the output; hold the last value while silent.
        if (sLL > 1e-9 && sRR > 1e-9) {
            float c = std::clamp((float)(sLR / std::sqrt(sLL * sRR)), -1.0f, 1.0f);
            corr_.store(corr_.load(std::memory_order_relaxed) * 0.8f + c * 0.2f, std::memory_order_relaxed);
        }
        inL_.store(inL, std::memory_order_relaxed);   inR_.store(inR, std::memory_order_relaxed);
        outL_.store(outL, std::memory_order_relaxed); outR_.store(outR, std::memory_order_relaxed);
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        out[S_InL]  = inL_.load(std::memory_order_relaxed);
        out[S_InR]  = inR_.load(std::memory_order_relaxed);
        out[S_OutL] = outL_.load(std::memory_order_relaxed);
        out[S_OutR] = outR_.load(std::memory_order_relaxed);
        out[S_Corr] = corr_.load(std::memory_order_relaxed);
        int w = ringWA_.load(std::memory_order_acquire);     // oldest sample
        for (int k = 0; k < kScopePairs; ++k) {
            int idx = (w + k) % kScopePairs;
            out[kMeters + k * 2]     = ring_[idx * 2];
            out[kMeters + k * 2 + 1] = ring_[idx * 2 + 1];
        }
        return kScope;
    }

    const char* displayName() const override { return "Nota Utility"; }
    int32_t     builtinKind() const override { return 4; }
    int32_t     paramCount() const override { return kNumParams; }

    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Gain", "Balance", "Width", "Channel Mode",
                                    "Mono Freq", "Mono Below", "Mute", "Invert L", "Invert R" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) { case Gain: return -24.0f; case Balance: return -1.0f; case MonoFreq: return 20.0f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        switch (i) {
            case Gain: return 24.0f; case Balance: return 1.0f; case Width: return 400.0f;
            case ChannelMode: return 3.0f; case MonoFreq: return 2000.0f; default: return 1.0f;
        }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    Utility() {
        p_[Gain].store(0.0f);        // 0 dB
        p_[Balance].store(0.0f);     // centre
        p_[Width].store(100.0f);     // 100 %
        p_[ChannelMode].store(0.0f); // Stereo
        p_[MonoFreq].store(120.0f);  // 120 Hz
        p_[MonoBelow].store(0.0f);   // off
        p_[Mute].store(0.0f);
        p_[InvertL].store(0.0f);
        p_[InvertR].store(0.0f);
    }

private:
    static constexpr float kPi = 3.14159265358979323846f;

    std::atomic<float> p_[kNumParams] = {};
    double sr_ = 44100.0;
    float  lpSide_ = 0.0f;   // mono-below one-pole state (audio thread)
    int    decim_ = 8, decimCnt_ = 0;

    // Scope telemetry (audio thread writes, UI reads lock-free; torn reads OK).
    std::atomic<float> inL_{0}, inR_{0}, outL_{0}, outR_{0}, corr_{1.0f};
    std::array<float, kScopePairs * 2> ring_{};
    int ringW_ = 0;                 // audio-thread write cursor
    std::atomic<int> ringWA_{0};    // published cursor for the UI
};

} // namespace nota
