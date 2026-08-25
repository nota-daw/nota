// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Strata (mockup 3n) — a multi-layer overdub looper (device kind 15). Every
// pass is its own named layer with waveform, level and mute, instead of one summed
// buffer. The four transport actions (Record / Overdub / Play / Stop) and the
// editing actions (Undo / Clear) arrive through Device::deviceAction; the state
// machine applies them quantized to the loop's own bar grid. The first Record pass
// defines the loop length; later passes each fill one fresh layer. Feedback decays
// the layers already laid down (teal), input gain and speed/reverse are the audio
// path (brass). The whole layer stack — settings persist as normal params, the
// recorded PCM persists through getState/setState. Allocation-free after
// setSampleRate: eight fixed layer buffers sized to the max loop.
//
// Threading: deviceAction() runs on the message thread and only pokes atomics
// (command register, per-layer gain/mute); the audio thread owns pos/mode and
// applies commands at the next quantize boundary. Buffers are written only by the
// audio thread; the message thread reads them for persistence between takes.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

namespace nota {

class Strata : public Device {
public:
    enum { Feedback = 0, InputGain, Speed, Quantize, CountIn, SetTempo, Reverse, kNumParams };
    enum { M_Empty = 0, M_Rec, M_Play, M_Over, M_Stop };          // transport mode
    enum { LS_Play = 0, LS_Mute, LS_Rec };                        // per-layer state (UI)
    enum { C_Record = 0, C_Overdub, C_Play, C_Stop, C_Undo, C_Clear };  // deviceAction ids
    enum { A_Cmd = 0, A_LayerMute = 6, A_LayerGain = 7 };         // deviceAction id space

    static constexpr int   kMaxLayers = 8;
    static constexpr double kMaxLoopSec = 16.0;

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        const double nsr = sr > 0 ? sr : 44100.0;
        const int64_t nmax = (int64_t)std::llround(nsr * kMaxLoopSec);
        // Only (re)allocate when the rate actually changes — the engine may re-prepare
        // the graph between renders, and reassigning here would wipe recorded layers.
        if (nsr == sr_ && nmax == maxLoop_ && !buf_[0].empty()) return;
        sr_ = nsr; maxLoop_ = nmax;
        for (int k = 0; k < kMaxLayers; ++k) buf_[k].assign((size_t)maxLoop_ * 2, 0.0f);
    }

    void setTransportInfo(const TransportInfo& t) override {
        if (t.bpm > 1.0) { bpm_ = t.bpm; tsNum_ = t.tsNum > 0 ? t.tsNum : 4; }
    }
    void setTransport(double /*beatStart*/, double spb, bool /*playing*/) override {
        if (spb > 1.0) { bpm_ = 60.0 * sr_ / spb; }   // samples/beat -> BPM
    }

    // ---- commands from the UI (message thread) --------------------------------
    void deviceAction(int32_t id, int32_t iarg, float farg) override {
        if (id == A_LayerMute) { if (iarg >= 0 && iarg < kMaxLayers) muted_[iarg].store(farg > 0.5f); return; }
        if (id == A_LayerGain) { if (iarg >= 0 && iarg < kMaxLayers) gainDb_[iarg].store(farg); return; }
        pending_.store(id, std::memory_order_release);   // C_* — audio thread applies at boundary
    }

    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0 || layer < 0 || layer >= layerCount_.load()) return 0;
        int64_t len = loopLen_.load(); if (len <= 0) return 0;
        const float* b = buf_[layer].data();
        int32_t n = maxSamples;
        for (int32_t i = 0; i < n; ++i) {
            int64_t a = len * i / n, z = len * (i + 1) / n; if (z <= a) z = a + 1;
            float pk = 0.0f;
            for (int64_t s = a; s < z && s < len; ++s) pk = std::max(pk, std::fabs(b[s * 2]) + std::fabs(b[s * 2 + 1]));
            out[i] = pk * 0.5f;
        }
        return n;
    }

    void process(float* buf, int32_t frames) override {
        const double sr = sr_;
        const float  fb      = std::clamp(p_[Feedback].load() * 0.01f, 0.0f, 1.0f);
        const float  inGain  = std::pow(10.0f, p_[InputGain].load() / 20.0f);
        const float  speed   = std::clamp(p_[Speed].load(), 0.25f, 4.0f);
        const int    quant   = std::clamp((int)std::lround(p_[Quantize].load()), 0, 2);
        const bool   reverse = p_[Reverse].load() > 0.5f;

        const int64_t len = loopLen_.load();
        const int     lc  = layerCount_.load();
        const double  barSampT = bpm_ > 1.0 ? (60.0 / bpm_) * sr * tsNum_ : 0.0;   // transport bar
        const double  barLen = (len > 0 && loopBars_ > 0) ? (double)len / loopBars_ : barSampT;
        const double  beatLen = barLen > 0 ? barLen / std::max(1, tsNum_) : 0.0;

        // Per-block feedback-decayed playback gains (older layers sink under newer).
        float eg[kMaxLayers];
        for (int k = 0; k < kMaxLayers; ++k)
            eg[k] = std::pow(10.0f, gainDb_[k].load() / 20.0f) * std::pow(fb, (float)std::max(0, lc - 1 - k));

        int mode = mode_.load();
        int recL = recLayer_.load();

        for (int32_t i = 0; i < frames; ++i) {
            const float inL = buf[i * 2] * inGain, inR = buf[i * 2 + 1] * inGain;
            const bool recording = (mode == M_Rec || mode == M_Over) && recL >= 0;
            const double prevPos = pos_;

            // Apply a queued command at the right boundary.
            int cmd = pending_.load(std::memory_order_acquire);
            if (cmd >= 0) {
                bool atLoopStart = false, atBar = false, atBeat = false;
                if (len > 0) {
                    // Boundaries derived from the loop's own grid (computed after advance below);
                    // approximate here using prevPos vs a one-sample lookahead.
                    double np = prevPos + (recording ? 1.0 : (reverse ? -speed : speed));
                    double nn = np; if (nn >= len) nn -= len; if (nn < 0) nn += len;
                    atLoopStart = (nn < prevPos) || (nn == 0.0);
                    if (barLen > 0)  atBar  = atLoopStart || std::floor(prevPos / barLen)  != std::floor(nn / barLen);
                    if (beatLen > 0) atBeat = atLoopStart || std::floor(prevPos / beatLen) != std::floor(nn / beatLen);
                } else if (mode == M_Rec && barSampT > 0) {
                    double nr = recCount_ + 1.0;
                    atBar = std::floor(recCount_ / barSampT) != std::floor(nr / barSampT);
                }
                bool immediate = (cmd == C_Undo || cmd == C_Clear) ||
                                 (mode == M_Empty) || (len <= 0 && mode != M_Rec);
                bool go = immediate || quant == 0 ||
                          (cmd == C_Record ? atLoopStart : (quant == 1 ? atBar : atBeat));
                if (go) { applyCommand(cmd, len); pending_.store(-1, std::memory_order_release);
                          mode = mode_.load(); recL = recLayer_.load(); }
            }

            // Record the (gained) input into the active layer at the integer position.
            if ((mode == M_Rec || mode == M_Over) && recL >= 0) {
                int64_t w = (int64_t)pos_;
                if (mode == M_Rec && loopLen_.load() <= 0) w = (int64_t)recCount_;   // first pass grows
                if (w >= 0 && w < maxLoop_) { buf_[recL][w * 2] = inL; buf_[recL][w * 2 + 1] = inR; }
            }

            // Sum the playing layers (skip the one being recorded — it's monitored live).
            float wetL = 0.0f, wetR = 0.0f;
            if (mode != M_Stop && len > 0) {
                int64_t p0 = (int64_t)pos_; double fr = pos_ - p0;
                int64_t p1 = p0 + 1; if (p1 >= len) p1 -= len;
                for (int k = 0; k < lc; ++k) {
                    if (k == recL || muted_[k].load()) continue;
                    const float* b = buf_[k].data();
                    float sL = b[p0 * 2] + fr * (b[p1 * 2] - b[p0 * 2]);
                    float sR = b[p0 * 2 + 1] + fr * (b[p1 * 2 + 1] - b[p0 * 2 + 1]);
                    wetL += sL * eg[k]; wetR += sR * eg[k];
                }
            }

            buf[i * 2] = inL + wetL; buf[i * 2 + 1] = inR + wetR;

            // Advance the loop / recording clock.
            if (mode == M_Rec && loopLen_.load() <= 0) {
                recCount_ += 1.0; pos_ = recCount_;
                if (recCount_ >= maxLoop_ - 1) { finalizeFirstLoop(); mode = M_Play; mode_.store(M_Play); recL = -1; recLayer_.store(-1); }
            } else if (len > 0) {
                pos_ += recording ? 1.0 : (reverse ? -speed : speed);
                if (pos_ >= len) pos_ -= len; if (pos_ < 0) pos_ += len;
                if (recording) {
                    recCount_ += 1.0;
                    if (recCount_ >= len) {   // one full pass captured -> finalize this layer
                        state_[recL].store(LS_Play); recLayer_.store(-1); recL = -1;
                        mode_.store(M_Play); mode = M_Play;
                    }
                }
            }
        }
        mode_.store(mode);
    }

    // ---- built-in device identity --------------------------------------------
    const char* displayName() const override { return "Nota Strata"; }
    int32_t     builtinKind() const override { return 15; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Feedback", "InputGain", "Speed", "Quantize", "CountIn", "SetTempo", "Reverse" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) { case InputGain: return -24.0f; case Speed: return 0.25f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        switch (i) { case Feedback: return 100.0f; case InputGain: return 24.0f; case Speed: return 4.0f;
            case Quantize: return 2.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load() : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v); }

    // Scope pack: [mode, loopBars, posPhase, layerCount, recLayer, recProgress,
    // then per layer k: state, gainDb, muted]. Read lock-free by the UI.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < 6) return 0;
        int lc = layerCount_.load(); int64_t len = loopLen_.load();
        out[0] = (float)mode_.load();
        out[1] = (float)loopBars_;
        out[2] = len > 0 ? (float)(pos_ / len) : 0.0f;
        out[3] = (float)lc;
        out[4] = (float)recLayer_.load();
        out[5] = len > 0 ? (float)std::min(1.0, recCount_ / len) : 0.0f;
        int n = 6;
        for (int k = 0; k < kMaxLayers && n + 3 <= maxSamples; ++k) {
            out[n++] = (float)state_[k].load();
            out[n++] = gainDb_[k].load();
            out[n++] = muted_[k].load() ? 1.0f : 0.0f;
        }
        return n;
    }

    // ---- persistence: settings are params; the recorded layers are the blob -----
    std::vector<uint8_t> getState() const override {
        int lc = layerCount_.load(); int64_t len = loopLen_.load();
        std::vector<uint8_t> s;
        auto pI = [&](int32_t v) { for (int b = 0; b < 4; ++b) s.push_back((uint8_t)(v >> (8 * b))); };
        auto pF = [&](float f) { int32_t v; std::memcpy(&v, &f, 4); pI(v); };
        pI(1);                    // version
        pI((int32_t)std::llround(sr_));
        pI((int32_t)len);
        pI(loopBars_);
        pI(lc);
        for (int k = 0; k < lc; ++k) { pF(gainDb_[k].load()); pI(muted_[k].load() ? 1 : 0); pI(state_[k].load()); }
        for (int k = 0; k < lc; ++k) {
            const float* b = buf_[k].data();
            for (int64_t i = 0; i < len * 2; ++i) pF(b[i]);
        }
        return s;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size < 20) return;
        int off = 0;
        auto gI = [&]() { int32_t v = 0; for (int b = 0; b < 4; ++b) v |= (int32_t)data[off++] << (8 * b); return v; };
        auto gF = [&]() { int32_t v = gI(); float f; std::memcpy(&f, &v, 4); return f; };
        int ver = gI(); (void)ver;
        gI();                                   // stored sample rate (ignored; buffers are at sr_)
        int64_t len = gI();
        int bars = gI();
        int lc = gI();
        len = std::clamp<int64_t>(len, 0, maxLoop_);
        lc  = std::clamp(lc, 0, kMaxLayers);
        float g[kMaxLayers]; int mu[kMaxLayers], st[kMaxLayers];
        for (int k = 0; k < lc; ++k) { if (off + 12 > size) { lc = k; break; } g[k] = gF(); mu[k] = gI(); st[k] = gI(); }
        for (int k = 0; k < lc; ++k) {
            float* b = buf_[k].data();
            for (int64_t i = 0; i < len * 2; ++i) b[i] = (off + 4 <= size) ? gF() : 0.0f;
            gainDb_[k].store(g[k]); muted_[k].store(mu[k] != 0); state_[k].store(st[k]);
        }
        loopLen_.store(len); loopBars_ = std::max(1, bars); layerCount_.store(lc);
        recLayer_.store(-1); recCount_ = 0; pos_ = 0;
        mode_.store(lc > 0 ? M_Stop : M_Empty);
    }

    Strata() {
        p_[Feedback].store(100.0f); p_[InputGain].store(0.0f); p_[Speed].store(1.0f);
        p_[Quantize].store(1.0f); p_[CountIn].store(0.0f); p_[SetTempo].store(0.0f); p_[Reverse].store(0.0f);
        for (int k = 0; k < kMaxLayers; ++k) { gainDb_[k].store(0.0f); muted_[k].store(false); state_[k].store(LS_Play); }
        setSampleRate(44100.0, 0);
    }

private:
    void finalizeFirstLoop() {
        int64_t len = (int64_t)std::llround(recCount_);
        if (len < 1) len = 1;
        double barSamp = bpm_ > 1.0 ? (60.0 / bpm_) * sr_ * tsNum_ : (double)len;
        loopBars_ = std::max(1, (int)std::llround(len / std::max(1.0, barSamp)));
        loopLen_.store(std::min(len, maxLoop_));
        state_[0].store(LS_Play);
    }

    // Runs on the audio thread only.
    void applyCommand(int cmd, int64_t len) {
        int mode = mode_.load(), lc = layerCount_.load();
        switch (cmd) {
        case C_Record:
        case C_Overdub:
            if (mode == M_Empty || lc == 0) {                 // first pass defines the loop
                startFresh(); mode_.store(M_Rec); recLayer_.store(0); layerCount_.store(1);
                state_[0].store(LS_Rec); recCount_ = 0; pos_ = 0;
            } else if (lc < kMaxLayers && recLayer_.load() < 0) {  // stack a new layer
                int k = lc; clearLayer(k, len); recLayer_.store(k); layerCount_.store(k + 1);
                state_[k].store(LS_Rec); recCount_ = 0; mode_.store(M_Over);
            }
            break;
        case C_Play:
            if (mode == M_Rec && loopLen_.load() <= 0) finalizeFirstLoop();
            stopRecording(); mode_.store(M_Play);
            break;
        case C_Stop:
            if (mode == M_Rec && loopLen_.load() <= 0) finalizeFirstLoop();
            stopRecording(); mode_.store(M_Stop); pos_ = 0;
            break;
        case C_Undo: {
            int r = recLayer_.load();
            if (r >= 0) { layerCount_.store(r); recLayer_.store(-1); mode_.store(loopLen_.load() > 0 ? M_Play : M_Empty); }
            else if (lc > 0) layerCount_.store(lc - 1);
            if (layerCount_.load() <= 0) { mode_.store(M_Empty); loopLen_.store(0); pos_ = 0; }
            break;
        }
        case C_Clear:
            layerCount_.store(0); recLayer_.store(-1); loopLen_.store(0);
            mode_.store(M_Empty); pos_ = 0; recCount_ = 0;
            break;
        }
    }
    void startFresh() { clearLayer(0, maxLoop_); }
    void stopRecording() {
        int r = recLayer_.load();
        if (r >= 0) { state_[r].store(LS_Play); recLayer_.store(-1); }
    }
    void clearLayer(int k, int64_t len) {
        if (k < 0 || k >= kMaxLayers) return;
        int64_t n = std::min(len > 0 ? len : maxLoop_, maxLoop_) * 2;
        std::memset(buf_[k].data(), 0, (size_t)n * sizeof(float));
        gainDb_[k].store(0.0f); muted_[k].store(false);
    }

    double sr_ = 44100.0, bpm_ = 120.0;
    int tsNum_ = 4;
    int64_t maxLoop_ = 0;
    std::vector<float> buf_[kMaxLayers];

    // Shared state (audio thread owns pos_/recCount_; atomics cross to the UI/message thread).
    std::atomic<int> mode_{M_Empty}, layerCount_{0}, recLayer_{-1};
    std::atomic<int64_t> loopLen_{0};
    int loopBars_ = 1;
    double pos_ = 0.0, recCount_ = 0.0;
    std::atomic<int> pending_{-1};
    std::atomic<float> gainDb_[kMaxLayers];
    std::atomic<bool>  muted_[kMaxLayers];
    std::atomic<int>   state_[kMaxLayers];
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
