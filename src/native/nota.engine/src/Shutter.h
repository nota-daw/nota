// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Shutter (device kind 19, mockup "Nota Shutter") — a noise gate / ducker.
// The "shutter" opens when the detector crosses the Threshold and closes when it falls back
// past the Return (hysteresis, stops chatter); Attack / Hold / Release shape each opening,
// with a Shape (Linear ramps, Log = the classic one-pole, Snap = stays open then shuts hard),
// Floor sets how far the closed signal drops (−∞ = mute, higher = a range), Lookahead lets
// the gate open just before a transient, Flip turns the gate into a ducker and Retrigger
// makes every hit fire one attack → hold → release envelope (trigger mode). The detector keys
// off this track or an external sidechain (External Key), through its own 12 dB/oct band-pass
// (Det Filter) with a Listen monitor, as a fast peak follower or with Peak Hold.
//
//   key ─▶ [HP ─▶ LP] ─▶ peak follower ─▶ threshold / return / hold ─▶ envelope (shape)
//   in  ─▶ look-ahead delay ─────────────────────────────────────────▶ × gain ─▶ out
//
// A core Device (JUCE-free), header-only, allocation-free after setSampleRate. Params are
// normalized 0..1, denormalized in process(); APPEND ONLY (0..10 are the original layout, the
// appended ones default to the old sound). Persistence / automation / clone flow generically.
// Telemetry (scopeRead): kTele live values, then three kHist-column histories over the
// window (input peak dB, gate gain 0..1, detector dB), oldest first.
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset the meters (peak GR, triggers, history), 1 = window (iarg 0 250 ms,
// 1 1 s, 2 4 s).

#pragma once

#include "Device.h"
#include "TransportInfo.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Shutter : public Device {
public:
    enum {
        Threshold = 0,  // open level (0..1 → −70..0 dB)
        Return,         // hysteresis below threshold to close (0..1 → 0..24 dB)
        Attack,         // open time (exp 0.01..100 ms)
        Hold,           // minimum open time after the detector drops (exp 0.1..500 ms)
        Release,        // close time (exp 1..2000 ms)
        Floor,          // closed level (0..1 → −70..0 dB; 0 = full mute / −∞) — the "range"
        Lookahead,      // 0/1/5 ms (discrete)
        Flip,           // 0 = Gate, 1 = Duck (invert)
        DetHP,          // detector high-pass (exp 20..2000 Hz)
        DetLP,          // detector low-pass (exp 200..20000 Hz)
        Listen,         // >=0.5 monitor the detector (band-passed key) instead of the gated audio
        // ---- appended (redesign) ----
        Shape,          // 0 Linear / 0.5 Log (one-pole, the old sound) / 1 Snap
        Retrigger,      // >=0.5 trigger mode: each hit fires one attack → hold → release
        DetFilter,      // >=0.5 the detector band-pass is in (off = full-band key)
        PeakHold,       // >=0.5 the detector holds peaks (~40 ms fall instead of 3 ms)
        ExternalKey,    // >=0.5 key off the sidechain source when one is routed
        kNumParams
    };

    // Telemetry slots (scopeRead). 0..4 keep the original layout.
    enum {
        S_InDb = 0,     // input level (pre-gate), dBFS
        S_GateGain,     // gate gain now 0..1
        S_GrDb,         // gain reduction now, dB (>= 0)
        S_DetDb,        // detector level (filtered key), dBFS
        S_Open,         // 1 = the detector holds the gate open
        S_State,        // 0 closed, 1 attack, 2 open, 3 hold, 4 release
        S_OutDb,        // output level, dBFS
        S_OpenRatio,    // share of the window the gate was open, 0..1
        S_TrigPerBar,   // openings in the last bar
        S_Triggers,     // openings since the last reset
        S_SampleRate,
        S_Cpu,          // share of real time spent in process()
        S_Latency,      // look-ahead, samples
        S_ExtKey,       // 1 = an external key is driving the detector
        S_KeyDb,        // key level before the detector filter, dBFS
        S_ThrDb,        // threshold, dBFS
        S_RetDb,        // close level (threshold − return), dBFS
        S_FloorDb,      // floor, dB (−120 = −∞)
        S_PeakGrDb,     // largest reduction since the last reset, dB
        S_Bpm,
        S_WindowSec,    // history window, seconds
        S_HistN,        // columns per history
        S_Envelope,     // envelope phase 0..1 (1 = fully engaged)
        kTele = 24
    };
    static constexpr int kHist = 128;
    static constexpr int kHistAt = kTele;                  // input peak dB · gate gain · detector dB
    static constexpr int kScope = kTele + 3 * kHist;

    Shutter() {
        p_[Threshold].store(0.457f);  // −38 dB
        p_[Return].store(0.125f);     // 3 dB
        p_[Attack].store(0.175f);     // ~0.05 ms
        p_[Hold].store(0.588f);       // ~15 ms
        p_[Release].store(0.63f);     // ~120 ms
        p_[Floor].store(0.0f);        // −∞ (mute)
        p_[Lookahead].store(0.5f);    // 1 ms
        p_[Flip].store(0.0f);         // Gate
        p_[DetHP].store(0.301f);      // ~80 Hz
        p_[DetLP].store(0.548f);      // ~2.5 kHz
        p_[Listen].store(0.0f);
        p_[Shape].store(0.5f);        // Log — the original one-pole
        p_[Retrigger].store(0.0f);
        p_[DetFilter].store(1.0f);    // the original always filtered the key
        p_[PeakHold].store(0.0f);
        p_[ExternalKey].store(1.0f);  // a routed key is used (the original behaviour)
        for (auto& h : hist_) h.fill(0.0f);
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        laMax_ = (int)std::lround(sr_ * 0.006) + 2;   // up to 5 ms look-ahead
        laBuf_.assign((size_t)laMax_ * 2, 0.0f); laW_ = 0;
        resetState();
        srA_.store((float)sr_, std::memory_order_relaxed);
    }

    // Sidechain: external key source (Compressor / Ceiling plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    void setTransportInfo(const TransportInfo& ti) override {
        if (ti.bpm > 1.0) bpm_ = ti.bpm;
        if (ti.tsNum > 0) tsNum_ = ti.tsNum;
        if (ti.tsDenom > 0) tsDen_ = ti.tsDenom;
    }

    // GR (dB, >=0) for the shell titlebar meter, like the Compressor.
    float gainReductionDb() const override { return grDb_.load(std::memory_order_relaxed); }

    int32_t latencySamples() const override { return laSamples(); }

    void deviceAction(int32_t id, int32_t iarg, float) override {
        if (id == A_ResetMeters) resetMetersReq_.store(true, std::memory_order_relaxed);
        else if (id == A_Window) windowIdx_.store(std::clamp(iarg, 0, 2), std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetMetersReq_.exchange(false, std::memory_order_relaxed)) {
            peakGr_ = 0.0f; trigTotal_ = 0; trigN_ = 0;
            for (auto& h : hist_) h.fill(0.0f);
            for (int k = 0; k < kHist; ++k) hist_[0][(size_t)k] = -120.0f, hist_[1][(size_t)k] = 1.0f, hist_[2][(size_t)k] = -120.0f;
        }

        const float thrDb = -70.0f + get(Threshold) * 70.0f;
        const float thrLin = dbToLin(thrDb);
        const float retLin = dbToLin(thrDb - get(Return) * 24.0f);
        const float atkMs = (float)expMap(get(Attack), 0.01, 100.0);
        const float holdMs = (float)expMap(get(Hold), 0.1, 500.0);
        const float relMs = (float)expMap(get(Release), 1.0, 2000.0);
        const float floorLin = get(Floor) <= 0.001f ? 0.0f : dbToLin(-70.0f + get(Floor) * 70.0f);
        const bool flip = get(Flip) >= 0.5f;
        const bool listen = get(Listen) >= 0.5f;
        const bool retrig = get(Retrigger) >= 0.5f;
        const bool filt = get(DetFilter) >= 0.5f;
        const int shape = shapeIdx();
        const double detHpHz = expMap(get(DetHP), 20.0, 2000.0);
        const double detLpHz = expMap(get(DetLP), 200.0, 20000.0);

        // Envelope steps (coef = the one-pole pole, x + c·(y − x)): Log = one-pole, Linear / Snap = a straight ramp over the time.
        const float gAtk = coef(atkMs), gRel = coef(relMs);
        const float rAtk = (float)(1.0 / std::max(1.0, atkMs * 0.001 * sr_));
        const float rRel = (float)(1.0 / std::max(1.0, relMs * 0.001 * sr_));
        const float dAtk = coef(0.05f), dRel = coef(get(PeakHold) >= 0.5f ? 40.0f : 3.0f);   // detector follower
        const float mRel = coef(80.0f);                                                   // level meters
        const int holdSamps = std::max(0, (int)std::lround(holdMs * 0.001 * sr_));
        const Svf hpF = svf(detHpHz), lpF = svf(detLpHz);
        const int la = laSamples();

        const bool useExt = get(ExternalKey) >= 0.5f;
        const float* sc = (useExt && scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);

        static constexpr double kWin[3] = { 0.25, 1.0, 4.0 };
        const double winSec = kWin[std::clamp(windowIdx_.load(std::memory_order_relaxed), 0, 2)];
        const int colLen = std::max(1, (int)std::lround(winSec * sr_ / kHist));
        const float ratioC = coef((float)(winSec * 1000.0));

        float grMax = 0.0f;

        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];

            // Detector source: external key if routed and enabled, else this track.
            const float dl = sc ? sc[i * 2] * scGain : l;
            const float dr = sc ? sc[i * 2 + 1] * scGain : r;
            const float dmono = 0.5f * (dl + dr);
            const float akey = std::max(std::fabs(dl), std::fabs(dr));
            keyEnv_ = akey > keyEnv_ ? akey : akey + mRel * (keyEnv_ - akey);

            // Detector band-pass (12 dB/oct HP then LP), then a fast peak follower.
            float key = dmono;
            if (filt) { key = hpF.hp(key, hp_); key = lpF.lp(key, lp_); }
            const float det = std::fabs(key);
            detEnv_ = det > detEnv_ ? det + dAtk * (detEnv_ - det) : det + dRel * (detEnv_ - det);

            // Input meter envelope (pre-gate).
            const float ain = std::max(std::fabs(l), std::fabs(r));
            inEnv_ = ain > inEnv_ ? ain : ain + mRel * (inEnv_ - ain);

            // Hysteresis + hold state machine. Retrigger = trigger mode: a crossing (re-armed by
            // falling under the return) fires one hold, whatever the key does after.
            const bool above = detEnv_ > thrLin, below = detEnv_ < retLin;
            if (!retrig) {
                if (above) { if (!open_) onTrigger(); open_ = true; holdCtr_ = holdSamps; }
                else if (below) { if (holdCtr_ > 0) --holdCtr_; else open_ = false; }
                armed_ = true;
            } else {
                if (above && armed_) { onTrigger(); open_ = true; holdCtr_ = holdSamps; armed_ = false; }
                if (below) armed_ = true;
                if (open_) { if (holdCtr_ > 0) --holdCtr_; else open_ = false; }
            }
            ++clock_;

            // Envelope phase: 1 = engaged (gate open / duck down). Attack moves it up, release down.
            const float tgt = open_ ? 1.0f : 0.0f;
            if (shape == 1) env_ = tgt + (tgt > env_ ? gAtk : gRel) * (env_ - tgt);
            else env_ = tgt > env_ ? std::min(tgt, env_ + rAtk) : std::max(tgt, env_ - rRel);
            const float f = shape == 2 ? 1.0f - (1.0f - env_) * (1.0f - env_) * (1.0f - env_) : env_;
            g_ = flip ? 1.0f - (1.0f - floorLin) * f : floorLin + (1.0f - floorLin) * f;

            // Look-ahead: apply the (anticipating) gain to the delayed audio.
            laBuf_[(size_t)laW_ * 2] = l; laBuf_[(size_t)laW_ * 2 + 1] = r;
            int rp = laW_ - la; if (rp < 0) rp += laMax_;
            const float ol = laBuf_[(size_t)rp * 2], or_ = laBuf_[(size_t)rp * 2 + 1];
            if (++laW_ >= laMax_) laW_ = 0;

            float yl, yr;
            if (listen) { yl = yr = key; }
            else { yl = ol * g_; yr = or_ * g_; }
            buf[i * 2] = yl; buf[i * 2 + 1] = yr;
            const float aout = std::max(std::fabs(yl), std::fabs(yr));
            outEnv_ = aout > outEnv_ ? aout : aout + mRel * (outEnv_ - aout);

            const float gr = g_ < 0.999f ? -20.0f * std::log10(std::max(1e-5f, g_)) : 0.0f;
            if (gr > grMax) grMax = gr;
            openRatio_ = (env_ > 0.5f ? 1.0f : 0.0f) + ratioC * (openRatio_ - (env_ > 0.5f ? 1.0f : 0.0f));

            // History column: input peak, the lowest gain, the detector peak.
            colIn_ = std::max(colIn_, ain); colGate_ = std::min(colGate_, g_); colDet_ = std::max(colDet_, detEnv_);
            if (++colN_ >= colLen) {
                const int hw = histW_.load(std::memory_order_relaxed);
                hist_[0][(size_t)hw] = db(colIn_);
                hist_[1][(size_t)hw] = colGate_;
                hist_[2][(size_t)hw] = db(colDet_);
                histW_.store((hw + 1) % kHist, std::memory_order_release);
                colIn_ = 0.0f; colGate_ = 1.0f; colDet_ = 0.0f; colN_ = 0;
            }
        }

        // Openings in the last bar.
        const double barSamples = (double)tsNum_ * (4.0 / (double)tsDen_) * 60.0 / bpm_ * sr_;
        int inBar = 0;
        for (int k = 0; k < trigN_; ++k) if ((double)(clock_ - trigAt_[(size_t)k]) <= barSamples) ++inBar;

        int state;
        if (open_) state = env_ < 0.99f ? 1 : retrig ? 3 : detEnv_ >= retLin ? 2 : 3;
        else state = env_ > 0.01f ? 4 : 0;
        if (grMax > peakGr_) peakGr_ = grMax;

        // Publish meters.
        inDb_.store(db(inEnv_), std::memory_order_relaxed);
        outDb_.store(db(outEnv_), std::memory_order_relaxed);
        keyDb_.store(db(keyEnv_), std::memory_order_relaxed);
        gateGain_.store(g_, std::memory_order_relaxed);
        grDb_.store(grMax, std::memory_order_relaxed);
        peakGrA_.store(peakGr_, std::memory_order_relaxed);
        detDb_.store(db(detEnv_), std::memory_order_relaxed);
        openF_.store(open_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        stateA_.store((float)state, std::memory_order_relaxed);
        envA_.store(env_, std::memory_order_relaxed);
        ratioA_.store(openRatio_, std::memory_order_relaxed);
        trigBarA_.store((float)inBar, std::memory_order_relaxed);
        trigTotA_.store((float)trigTotal_, std::memory_order_relaxed);
        extA_.store(sc ? 1.0f : 0.0f, std::memory_order_relaxed);
        bpmA_.store((float)bpm_, std::memory_order_relaxed);
        winA_.store((float)winSec, std::memory_order_relaxed);
        scBuf_ = nullptr;
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    // Telemetry block, then the three histories (oldest first). Writes as much of that layout
    // as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        const float thrDb = -70.0f + get(Threshold) * 70.0f;
        t[S_InDb] = inDb_.load(std::memory_order_relaxed);
        t[S_GateGain] = gateGain_.load(std::memory_order_relaxed);
        t[S_GrDb] = grDb_.load(std::memory_order_relaxed);
        t[S_DetDb] = detDb_.load(std::memory_order_relaxed);
        t[S_Open] = openF_.load(std::memory_order_relaxed);
        t[S_State] = stateA_.load(std::memory_order_relaxed);
        t[S_OutDb] = outDb_.load(std::memory_order_relaxed);
        t[S_OpenRatio] = ratioA_.load(std::memory_order_relaxed);
        t[S_TrigPerBar] = trigBarA_.load(std::memory_order_relaxed);
        t[S_Triggers] = trigTotA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency] = (float)laSamples();
        t[S_ExtKey] = extA_.load(std::memory_order_relaxed);
        t[S_KeyDb] = keyDb_.load(std::memory_order_relaxed);
        t[S_ThrDb] = thrDb;
        t[S_RetDb] = thrDb - get(Return) * 24.0f;
        t[S_FloorDb] = get(Floor) <= 0.001f ? -120.0f : -70.0f + get(Floor) * 70.0f;
        t[S_PeakGrDb] = peakGrA_.load(std::memory_order_relaxed);
        t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
        t[S_WindowSec] = winA_.load(std::memory_order_relaxed);
        t[S_HistN] = (float)kHist;
        t[S_Envelope] = envA_.load(std::memory_order_relaxed);

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        const int hw = histW_.load(std::memory_order_acquire);
        for (int s = 0; s < 3; ++s)
            for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = hist_[(size_t)s][(size_t)((hw + k) % kHist)];
        return n;
    }

    const char* displayName() const override { return "Nota Shutter"; }
    int32_t     builtinKind() const override { return 19; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = { "Threshold", "Return", "Attack", "Hold", "Release", "Floor",
                                              "Lookahead", "Flip", "Det HP", "Det LP", "Listen",
                                              "Shape", "Retrigger", "Det Filter", "Peak Hold", "External Key" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        char b[768];
        static const char* shapes[3] = { "linear", "log", "snap" };
        static const char* states[5] = { "closed", "attack", "open", "hold", "release" };
        const float thr = -70.0f + get(Threshold) * 70.0f;
        if (id == 0) {
            const bool ext = get(ExternalKey) >= 0.5f && sidechainSourceTrackId() >= 0;
            std::string s = get(Flip) >= 0.5f ? "Duck" : "Gate";
            std::snprintf(b, sizeof b, " - threshold %.1f dB - return %.1f dB - %s %s / %s / %s - %s",
                          thr, get(Return) * 24.0f, shapes[shapeIdx()], msText(expMap(get(Attack), 0.01, 100.0)).c_str(),
                          msText(expMap(get(Hold), 0.1, 500.0)).c_str(), msText(expMap(get(Release), 1.0, 2000.0)).c_str(),
                          get(Floor) <= 0.001f ? "floor -inf" : ("floor " + dbText(-70.0f + get(Floor) * 70.0f)).c_str());
            s += b;
            std::snprintf(b, sizeof b, " - look %d ms", kLookMs[lookIdx()]);
            s += b;
            if (get(Retrigger) >= 0.5f) s += " - retrigger";
            s += ext ? " - external key" : " - internal key";
            if (get(DetFilter) >= 0.5f)
                s += " - key filter " + hzText(expMap(get(DetHP), 20.0, 2000.0)) + "..." + hzText(expMap(get(DetLP), 200.0, 20000.0));
            else s += " - key full-band";
            if (get(PeakHold) >= 0.5f) s += " - peak hold";
            if (get(Listen) >= 0.5f) s += " - LISTEN (key monitor)";
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            const int st = std::clamp((int)std::lround(sc[S_State]), 0, 4);
            std::snprintf(b, sizeof b,
                          "%s - gain %.0f %% - GR %.1f dB (peak %.1f dB) - in %.1f dB - out %.1f dB - detector %.1f dB vs threshold %.1f dB - "
                          "open %.0f %% of %.2g s - %.0f openings in the last bar (%.0f since reset) - %s key %.1f dB - latency %.0f smp",
                          states[st], sc[S_GateGain] * 100.0f, sc[S_GrDb], sc[S_PeakGrDb], sc[S_InDb], sc[S_OutDb], sc[S_DetDb], thr,
                          sc[S_OpenRatio] * 100.0f, sc[S_WindowSec], sc[S_TrigPerBar], sc[S_Triggers],
                          sc[S_ExtKey] > 0.5f ? "external" : "internal", sc[S_KeyDb], sc[S_Latency]);
            return b;
        }
        if (id == 2) {
            return "All params 0..1. Threshold: -70 + 70*v dBFS (0.457 = -38 dB). Return: 24*v dB below the threshold, where the gate "
                   "closes again (hysteresis). Attack 0.01*10000^v ms (0.175 = 0.05 ms, 0.5 = 1 ms). Hold 0.1*5000^v ms (0.588 = 15 ms). "
                   "Release 1*2000^v ms (0.63 = 120 ms). Floor (range): 0 = -inf (mute), else -70 + 70*v dB — the level when closed, "
                   "or how deep Duck goes. Lookahead: 0 = 0 ms, 0.5 = 1 ms, 1 = 5 ms (adds latency). Flip: 0 Gate, 1 Duck. "
                   "Det HP 20*100^v Hz (0.301 = 80 Hz). Det LP 200*100^v Hz (0.548 = 2.5 kHz). Listen: hear the filtered key. "
                   "Shape: 0 Linear (straight ramps), 0.5 Log (one-pole, the classic), 1 Snap (stays open, then shuts hard). "
                   "Retrigger: trigger mode, each hit fires one attack-hold-release. Det Filter: the key band-pass is in. "
                   "Peak Hold: the detector holds peaks (steadier on low notes). External Key: use the routed sidechain source. "
                   "Toggles: >= 0.5 = on.";
        }
        return {};
    }

    enum { A_ResetMeters = 0, A_Window = 1 };

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kLookMs[3] = { 0, 1, 5 };

    // TPT state-variable filter (Q = 0.707) for the key band-pass.
    struct Svf {
        float g = 0.0f, k = 1.41421356f, a1 = 0.0f, a2 = 0.0f, a3 = 0.0f;
        struct St { float ic1 = 0.0f, ic2 = 0.0f; };
        float run(float x, St& s, bool high) const {
            const float v3 = x - s.ic2;
            const float v1 = a1 * s.ic1 + a2 * v3;
            const float v2 = s.ic2 + a2 * s.ic1 + a3 * v3;
            s.ic1 = 2.0f * v1 - s.ic1; s.ic2 = 2.0f * v2 - s.ic2;
            return high ? x - k * v1 - v2 : v2;
        }
        float hp(float x, St& s) const { return run(x, s, true); }
        float lp(float x, St& s) const { return run(x, s, false); }
    };
    Svf svf(double hz) const {
        Svf f;
        f.g = (float)std::tan(kPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_);
        f.a1 = 1.0f / (1.0f + f.g * (f.g + f.k)); f.a2 = f.g * f.a1; f.a3 = f.g * f.a2;
        return f;
    }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int shapeIdx() const { return std::clamp((int)std::lround(get(Shape) * 2.0f), 0, 2); }
    int lookIdx() const { return std::clamp((int)std::lround(get(Lookahead) * 2.0f), 0, 2); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float dbToLin(float dbv) { return std::pow(10.0f, dbv / 20.0f); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    float coef(float ms) const { return (float)std::exp(-1.0 / (std::max(0.01f, ms) * 0.001 * sr_)); }
    int laSamples() const { return std::clamp((int)std::lround(kLookMs[lookIdx()] * 0.001 * sr_), 0, std::max(0, laMax_ - 1)); }
    static std::string msText(double ms) {
        char b[32];
        if (ms >= 100.0) std::snprintf(b, sizeof b, "%.0f ms", ms);
        else if (ms >= 10.0) std::snprintf(b, sizeof b, "%.1f ms", ms);
        else std::snprintf(b, sizeof b, "%.2f ms", ms);
        return b;
    }
    static std::string hzText(double hz) {
        char b[32];
        if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.1f kHz", hz / 1000.0); else std::snprintf(b, sizeof b, "%.0f Hz", hz);
        return b;
    }
    static std::string dbText(float v) { char b[32]; std::snprintf(b, sizeof b, "%.1f dB", v); return b; }

    void onTrigger() {
        ++trigTotal_;
        if (trigN_ < kTrig) trigAt_[(size_t)trigN_++] = clock_;
        else { for (int k = 1; k < kTrig; ++k) trigAt_[(size_t)k - 1] = trigAt_[(size_t)k]; trigAt_[kTrig - 1] = clock_; }
    }

    void resetState() {
        std::fill(laBuf_.begin(), laBuf_.end(), 0.0f); laW_ = 0;
        detEnv_ = inEnv_ = outEnv_ = keyEnv_ = 0.0f; g_ = 1.0f; env_ = 0.0f;
        holdCtr_ = 0; open_ = false; armed_ = true;
        hp_ = {}; lp_ = {};
        openRatio_ = 0.0f; colIn_ = 0.0f; colGate_ = 1.0f; colDet_ = 0.0f; colN_ = 0;
        for (int k = 0; k < kHist; ++k) hist_[0][(size_t)k] = -120.0f, hist_[1][(size_t)k] = 1.0f, hist_[2][(size_t)k] = -120.0f;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};

    std::vector<float> laBuf_; int laMax_ = 0, laW_ = 0;
    float detEnv_ = 0.0f, inEnv_ = 0.0f, outEnv_ = 0.0f, keyEnv_ = 0.0f, g_ = 1.0f, env_ = 0.0f;
    Svf::St hp_, lp_;
    int holdCtr_ = 0; bool open_ = false, armed_ = true;
    float openRatio_ = 0.0f, peakGr_ = 0.0f;
    double bpm_ = 120.0; int tsNum_ = 4, tsDen_ = 4;
    static constexpr int kTrig = 64;
    std::array<int64_t, kTrig> trigAt_{}; int trigN_ = 0; int64_t clock_ = 0; int64_t trigTotal_ = 0;
    float colIn_ = 0.0f, colGate_ = 1.0f, colDet_ = 0.0f; int colN_ = 0;
    std::array<std::array<float, kHist>, 3> hist_{};
    std::atomic<int> histW_{0};
    double cpuS_ = 0.0;

    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;
    std::atomic<bool> resetMetersReq_{false};
    std::atomic<int> windowIdx_{1};

    std::atomic<float> inDb_{-120.0f}, outDb_{-120.0f}, keyDb_{-120.0f}, gateGain_{1.0f}, grDb_{0.0f}, peakGrA_{0.0f},
        detDb_{-120.0f}, openF_{0.0f}, stateA_{0.0f}, envA_{0.0f}, ratioA_{0.0f}, trigBarA_{0.0f}, trigTotA_{0.0f},
        extA_{0.0f}, bpmA_{120.0f}, winA_{1.0f}, srA_{44100.0f}, cpuA_{0.0f};
};

} // namespace nota
