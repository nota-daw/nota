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
// recorded PCM persists through getState/setState.
// Allocation-free after setSampleRate. Verbatim math throughout (control path —
// no FastMath substitution). Silent stretches auto-sleep via isIdle(): only once
// no layer can still sound (stopped / empty / no layers) and nothing is being
// recorded; a sounding loop never sleeps.
//
// Threading: deviceAction() runs on the message thread and only pokes atomics
// (command register, per-layer gain/mute); the audio thread owns pos/mode and
// applies commands at the next quantize boundary. Buffers are written only by the
// audio thread; the message thread reads them for persistence between takes.

#pragma once

#include "devices/Device.h"

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
    enum { M_Empty = 0, M_Rec, M_Play, M_Over, M_Stop };               // transport mode
    enum { LS_Play = 0, LS_Mute, LS_Rec };                             // per-layer state (UI)
    enum { C_Record = 0, C_Overdub, C_Play, C_Stop, C_Undo, C_Clear }; // deviceAction ids
    enum { A_Cmd = 0, A_LayerMute = 6, A_LayerGain = 7 };              // deviceAction id space

    static constexpr int kMaxLayers = 8;
    static constexpr double kMaxLoopSec = 16.0;

    Strata();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransportInfo(const TransportInfo& t) override;
    void setTransport(double beatStart, double spb, bool playing) override;

    // ---- commands from the UI (message thread) --------------------------------
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: the loop outlives silent input, so the tail is real. Settles to
    // idle only once no layer can still sound and nothing is being recorded.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // ---- built-in device identity --------------------------------------------
    const char* displayName() const override;
    int32_t builtinKind() const override;
    int32_t paramCount() const override;
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // ---- scope + persistence ---------------------------------------------------
    // Scope pack: [mode, loopBars, posPhase, layerCount, recLayer, recProgress,
    // then per layer k: state, gainDb, muted]. Read lock-free by the UI.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    // Settings are params; the recorded layers are the blob.
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;

  private:
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    void finalizeFirstLoop();
    // Runs on the audio thread only.
    void applyCommand(int cmd, int64_t len);
    void startFresh();
    void stopRecording();
    void clearLayer(int k, int64_t len);

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
    std::atomic<bool> muted_[kMaxLayers];
    std::atomic<int> state_[kMaxLayers];
    std::atomic<float> p_[kNumParams] = {};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
