// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Shutter implementation.
// Control-rate and envelope math kept std::-exact; silent stretches auto-sleep
// via isIdle() under the flight-time rule (see process()).

#include "Shutter.h"

#include "dsp/math/SimdAudio.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>

namespace nota {

Shutter::Shutter() {
    p_[Threshold].store(0.457f); // −38 dB
    p_[Return].store(0.125f);    // 3 dB
    p_[Attack].store(0.175f);    // ~0.05 ms
    p_[Hold].store(0.588f);      // ~15 ms
    p_[Release].store(0.63f);    // ~120 ms
    p_[Floor].store(0.0f);       // −∞ (mute)
    p_[Lookahead].store(0.5f);   // 1 ms
    p_[Flip].store(0.0f);        // Gate
    p_[DetHP].store(0.301f);     // ~80 Hz
    p_[DetLP].store(0.548f);     // ~2.5 kHz
    p_[Listen].store(0.0f);
    p_[Shape].store(0.5f); // Log — the original one-pole
    p_[Retrigger].store(0.0f);
    p_[DetFilter].store(1.0f); // the original always filtered the key
    p_[PeakHold].store(0.0f);
    p_[ExternalKey].store(1.0f); // a routed key is used (the original behaviour)
    p_[PatTarget].store(0.0f);   // Off — the gate sounds as before
    p_[PatRate].store(0.5f);     // 1 Bar
    p_[PatDepth].store(1.0f);
    p_[PatSync].store(1.0f);
    p_[PatSmooth].store(0.0f);
    p_[PatSwing].store(0.0f);
    p_[PatSteps].store(0.5f);  // 16
    p_[PatLevels].store(0.5f); // 8
    p_[PatRetrig].store(0.0f);
    p_[PatGated].store(0.0f);
    p_[PatComplexity].store(0.5f);
    p_[PatOut].store(1.0f);
    for (int k = 0; k < kNumSteps; ++k) p_[Step1 + k].store(kPushPattern[k % 16] / 8.0f); // "Push"
    for (auto& h : hist_) h.fill(0.0f);
    setSampleRate(44100.0, 0);
}

void Shutter::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    laMax_ = (int)std::lround(sr_ * 0.006) + 2; // up to 5 ms look-ahead
    laBuf_.assign((size_t)laMax_ * 2, 0.0f);
    laW_ = 0;
    resetState();
    quietSamples_ = 0;
    idle_ = false;
    srA_.store((float)sr_, std::memory_order_relaxed);
}

void Shutter::setTransportInfo(const TransportInfo& ti) {
    if (ti.bpm > 1.0) bpm_ = ti.bpm;
    if (ti.tsNum > 0) tsNum_ = ti.tsNum;
    if (ti.tsDenom > 0) tsDen_ = ti.tsDenom;
}

void Shutter::setTransport(double beatStart, double samplesPerBeat, bool playing) {
    tBeat_ = beatStart;
    tSpb_ = samplesPerBeat;
    tPlaying_ = playing;
    tFed_ = true;
}

void Shutter::deviceAction(int32_t id, int32_t iarg, float) {
    if (id == A_ResetMeters) resetMetersReq_.store(true, std::memory_order_relaxed);
    else if (id == A_Window)
        windowIdx_.store(std::clamp(iarg, 0, 2), std::memory_order_relaxed);
    wake();
}

void Shutter::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

void Shutter::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (resetMetersReq_.exchange(false, std::memory_order_relaxed)) {
        peakGr_ = 0.0f;
        trigTotal_ = 0;
        trigN_ = 0;
        for (auto& h : hist_) h.fill(0.0f);
        for (int k = 0; k < kHist; ++k)
            hist_[0][(size_t)k] = -120.0f, hist_[1][(size_t)k] = 1.0f, hist_[2][(size_t)k] = -120.0f;
    }

    const bool useExt = get(ExternalKey) >= 0.5f;
    const float* sc = (useExt && scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    const bool keySilent = sc == nullptr || simd_audio::isSilent(sc, frames * 2);

    // Idle skip under the flight-time rule: silent input AND silent key on a
    // settled gate leave the buffer untouched. Correct output would be the
    // flushed look-ahead line (zeros — sleep engages only after the line plus
    // the detector/meter hangover) times the settled gain, i.e. silence; the
    // silent input already in the buffer is equally silent (diff ≤ 1e-5).
    // The pattern phase and trigger clock keep advancing so wake-up timing does
    // not jump; envelopes decay in closed form so the wake-up gain matches.
    if (inSilent && keySilent && idle_) {
        const int patT = patTarget();
        const bool pSync = get(PatSync) >= 0.5f;
        const int rateI = patRateIdx();
        const double spb = (tFed_ && tSpb_ > 1.0) ? tSpb_ : sr_ * 60.0 / bpm_;
        const double barBeats = (double)tsNum_ * 4.0 / (double)tsDen_;
        static constexpr double kSyncBeats[5] = {1.0, 2.0, 0.0, 0.0, 0.0};
        static constexpr double kBars[5] = {0.0, 0.0, 1.0, 2.0, 4.0};
        static constexpr double kFreeHz[5] = {4.0, 2.0, 1.0, 0.5, 0.25};
        const double patBeats = rateI < 2 ? kSyncBeats[rateI] : kBars[rateI] * barBeats;
        const double phInc = pSync ? 1.0 / std::max(1.0, patBeats * spb) : kFreeHz[rateI] / sr_;
        const bool retrigOn = get(PatRetrig) >= 0.5f;
        const int la = laSamples();
        const bool lock = pSync && !retrigOn && tFed_ && tPlaying_ && patBeats > 0.0 && patT > 0;
        const double lockBeat0 = tBeat_ - (double)la / spb;
        if (patT > 0) {
            int mi = 0;
            if (retrigOn && midiEvs_)
                for (; mi < midiN_ && midiEvs_[mi].off <= frames; ++mi)
                    if (midiEvs_[mi].on) patPhase_ = 0.0;
            if (lock) {
                const double b = lockBeat0 + (double)(frames - 1) / spb;
                patPhase_ = b / patBeats - std::floor(b / patBeats);
            } else {
                patPhase_ += phInc * (double)frames;
                if (patPhase_ >= 1.0) patPhase_ -= std::floor(patPhase_);
            }
        }
        clock_ += frames;
        // Envelopes keep decaying in closed form (all ~0 here; keeps wake-up exact).
        const float mRel = coef(80.0f);
        const float dRel = coef(get(PeakHold) >= 0.5f ? 40.0f : 3.0f);
        const float mDec = (float)std::pow(mRel, frames);
        const float dDec = (float)std::pow(dRel, frames);
        detEnv_ *= dDec;
        inEnv_ *= mDec;
        outEnv_ *= mDec;
        keyEnv_ *= mDec;
        const int shape = shapeIdx();
        const float gRel = coef((float)expMap(get(Release), 1.0, 2000.0));
        const float rRel = (float)(1.0 / std::max(1.0, (float)expMap(get(Release), 1.0, 2000.0) * 0.001 * sr_));
        if (shape == 1) env_ = env_ * (float)std::pow(gRel, frames);
        else
            env_ = std::max(0.0f, env_ - rRel * (float)frames);
        const float floorLin = get(Floor) <= 0.001f ? 0.0f : dbToLin(-70.0f + get(Floor) * 70.0f);
        const float f = shape == 2 ? 1.0f - (1.0f - env_) * (1.0f - env_) * (1.0f - env_) : env_;
        g_ = get(Flip) >= 0.5f ? 1.0f - (1.0f - floorLin) * f : floorLin + (1.0f - floorLin) * f;
        openRatio_ *= mDec;
        // History columns advance on zeros (peaks of silence, settled gain).
        static constexpr double kWin[3] = {0.25, 1.0, 4.0};
        const double winSec = kWin[std::clamp(windowIdx_.load(std::memory_order_relaxed), 0, 2)];
        const int colLen = std::max(1, (int)std::lround(winSec * sr_ / kHist));
        colGate_ = std::min(colGate_, g_);
        colN_ += frames;
        while (colN_ >= colLen) {
            const int hw = histW_.load(std::memory_order_relaxed);
            hist_[0][(size_t)hw] = -120.0f;
            hist_[1][(size_t)hw] = colGate_;
            hist_[2][(size_t)hw] = -120.0f;
            histW_.store((hw + 1) % kHist, std::memory_order_release);
            colIn_ = 0.0f;
            colGate_ = 1.0f;
            colDet_ = 0.0f;
            colN_ -= colLen;
        }
        inDb_.store(db(inEnv_), std::memory_order_relaxed);
        outDb_.store(db(outEnv_), std::memory_order_relaxed);
        keyDb_.store(db(keyEnv_), std::memory_order_relaxed);
        gateGain_.store(g_, std::memory_order_relaxed);
        grDb_.store(0.0f, std::memory_order_relaxed);
        detDb_.store(db(detEnv_), std::memory_order_relaxed);
        openF_.store(0.0f, std::memory_order_relaxed);
        envA_.store(env_, std::memory_order_relaxed);
        ratioA_.store(openRatio_, std::memory_order_relaxed);
        patPhaseA_.store((float)patPhase_, std::memory_order_relaxed);
        scBuf_ = nullptr;
        midiEvs_ = nullptr;
        midiN_ = 0;
        tFed_ = false;
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
        return;
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
    const float dAtk = coef(0.05f), dRel = coef(get(PeakHold) >= 0.5f ? 40.0f : 3.0f); // detector follower
    const float mRel = coef(80.0f);                                                    // level meters
    const int holdSamps = std::max(0, (int)std::lround(holdMs * 0.001 * sr_));
    const Svf hpF = svf(detHpHz), lpF = svf(detLpHz);
    const int la = laSamples();

    const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);

    static constexpr double kWin[3] = {0.25, 1.0, 4.0};
    const double winSec = kWin[std::clamp(windowIdx_.load(std::memory_order_relaxed), 0, 2)];
    const int colLen = std::max(1, (int)std::lround(winSec * sr_ / kHist));
    const float ratioC = coef((float)(winSec * 1000.0));

    // ---- Pattern ----
    const int patT = patTarget();
    const int nSteps = patSteps();
    const int nLevels = patLevels();
    const bool pSync = get(PatSync) >= 0.5f;
    const int rateI = patRateIdx();
    const double spb = (tFed_ && tSpb_ > 1.0) ? tSpb_ : sr_ * 60.0 / bpm_;
    const double barBeats = (double)tsNum_ * 4.0 / (double)tsDen_;
    static constexpr double kSyncBeats[5] = {1.0, 2.0, 0.0, 0.0, 0.0}; // 1/4, 1/2 · then bars
    static constexpr double kBars[5] = {0.0, 0.0, 1.0, 2.0, 4.0};
    static constexpr double kFreeHz[5] = {4.0, 2.0, 1.0, 0.5, 0.25};
    const double patBeats = rateI < 2 ? kSyncBeats[rateI] : kBars[rateI] * barBeats;
    const double phInc = pSync ? 1.0 / std::max(1.0, patBeats * spb) : kFreeHz[rateI] / sr_;
    const bool retrigOn = get(PatRetrig) >= 0.5f;
    // Locked to the song: the phase of the output sample (look-ahead delays the audio).
    const bool lock = pSync && !retrigOn && tFed_ && tPlaying_ && patBeats > 0.0;
    const double lockBeat0 = tBeat_ - (double)la / spb;
    const float depth = get(PatDepth);
    const float smooth = get(PatSmooth) * 0.5f;       // ramp, in steps
    const float swing = get(PatSwing) * 0.75f * 0.5f; // off-step delay, in steps
    const bool gated = get(PatGated) >= 0.5f;
    float stepV[kNumSteps];
    for (int k = 0; k < nSteps; ++k) stepV[k] = std::round(std::clamp(get(Step1 + k), 0.0f, 1.0f) * nLevels) / nLevels;
    const float patSmC = coef(1.0f); // ~1 ms de-click on the pattern gain
    int mi = 0;
    float patVal = patVal_;

    float grMax = 0.0f;
    float blkOutPk = 0.0f;

    for (int32_t i = 0; i < frames; ++i) {
        // Pattern phase: song-locked, or free-running (restarted by a MIDI note-on).
        if (patT > 0) {
            if (retrigOn && midiEvs_)
                for (; mi < midiN_ && midiEvs_[mi].off <= i; ++mi)
                    if (midiEvs_[mi].on) patPhase_ = 0.0;
            if (lock) {
                const double b = lockBeat0 + (double)i / spb;
                patPhase_ = b / patBeats - std::floor(b / patBeats);
            }
            const float v = patternAt(stepV, nSteps, (float)(patPhase_ * nSteps), smooth, swing);
            patVal = v + patSmC * (patVal - v);
            if (!lock) {
                patPhase_ += phInc;
                if (patPhase_ >= 1.0) patPhase_ -= std::floor(patPhase_);
            }
        }
        const float patGain = 1.0f - depth * (1.0f - patVal);

        const float l = buf[i * 2], r = buf[i * 2 + 1];

        // Detector source: external key if routed and enabled, else this track.
        const float dl = sc ? sc[i * 2] * scGain : l;
        const float dr = sc ? sc[i * 2 + 1] * scGain : r;
        const float dmono = 0.5f * (dl + dr);
        const float akey = std::max(std::fabs(dl), std::fabs(dr));
        keyEnv_ = akey > keyEnv_ ? akey : akey + mRel * (keyEnv_ - akey);

        // Detector band-pass (12 dB/oct HP then LP), then a fast peak follower.
        float key = dmono;
        if (filt) {
            key = hpF.hp(key, hp_);
            key = lpF.lp(key, lp_);
        }
        const float det = std::fabs(key);
        detEnv_ = det > detEnv_ ? det + dAtk * (detEnv_ - det) : det + dRel * (detEnv_ - det);

        // Input meter envelope (pre-gate).
        const float ain = std::max(std::fabs(l), std::fabs(r));
        inEnv_ = ain > inEnv_ ? ain : ain + mRel * (inEnv_ - ain);

        // Hysteresis + hold state machine. Retrigger = trigger mode: a crossing (re-armed by
        // falling under the return) fires one hold, whatever the key does after.
        const bool above = detEnv_ > thrLin, below = detEnv_ < retLin;
        if (!retrig) {
            if (above) {
                if (!open_) onTrigger();
                open_ = true;
                holdCtr_ = holdSamps;
            } else if (below) {
                if (holdCtr_ > 0) --holdCtr_;
                else
                    open_ = false;
            }
            armed_ = true;
        } else {
            if (above && armed_) {
                onTrigger();
                open_ = true;
                holdCtr_ = holdSamps;
                armed_ = false;
            }
            if (below) armed_ = true;
            if (open_) {
                if (holdCtr_ > 0) --holdCtr_;
                else
                    open_ = false;
            }
        }
        ++clock_;

        // Envelope phase: 1 = engaged (gate open / duck down). Attack moves it up, release down.
        const float tgt = open_ ? 1.0f : 0.0f;
        if (shape == 1) env_ = tgt + (tgt > env_ ? gAtk : gRel) * (env_ - tgt);
        else
            env_ = tgt > env_ ? std::min(tgt, env_ + rAtk) : std::max(tgt, env_ - rRel);
        const float f = shape == 2 ? 1.0f - (1.0f - env_) * (1.0f - env_) * (1.0f - env_) : env_;
        g_ = flip ? 1.0f - (1.0f - floorLin) * f : floorLin + (1.0f - floorLin) * f;
        if (patT == 1) g_ = gated ? g_ * patGain : patGain; // Volume: the pattern is the gate (or rides it)

        // Look-ahead: apply the (anticipating) gain to the delayed audio.
        laBuf_[(size_t)laW_ * 2] = l;
        laBuf_[(size_t)laW_ * 2 + 1] = r;
        int rp = laW_ - la;
        if (rp < 0) rp += laMax_;
        const float ol = laBuf_[(size_t)rp * 2], or_ = laBuf_[(size_t)rp * 2 + 1];
        if (++laW_ >= laMax_) laW_ = 0;

        float yl, yr;
        if (listen) {
            yl = yr = key;
        } else {
            yl = ol * g_;
            yr = or_ * g_;
        }
        buf[i * 2] = yl;
        buf[i * 2 + 1] = yr;
        const float aout = std::max(std::fabs(yl), std::fabs(yr));
        outEnv_ = aout > outEnv_ ? aout : aout + mRel * (outEnv_ - aout);
        blkOutPk = std::max(blkOutPk, aout);

        const float gr = g_ < 0.999f ? -20.0f * std::log10(std::max(1e-5f, g_)) : 0.0f;
        if (gr > grMax) grMax = gr;
        openRatio_ = (env_ > 0.5f ? 1.0f : 0.0f) + ratioC * (openRatio_ - (env_ > 0.5f ? 1.0f : 0.0f));

        // History column: input peak, the lowest gain, the detector peak.
        colIn_ = std::max(colIn_, ain);
        colGate_ = std::min(colGate_, g_);
        colDet_ = std::max(colDet_, detEnv_);
        if (++colN_ >= colLen) {
            const int hw = histW_.load(std::memory_order_relaxed);
            hist_[0][(size_t)hw] = db(colIn_);
            hist_[1][(size_t)hw] = colGate_;
            hist_[2][(size_t)hw] = db(colDet_);
            histW_.store((hw + 1) % kHist, std::memory_order_release);
            colIn_ = 0.0f;
            colGate_ = 1.0f;
            colDet_ = 0.0f;
            colN_ = 0;
        }
    }

    // Flight-time hangover: only after the look-ahead line has flushed and the
    // gate/detector have settled with input, key, and output all quiet.
    if (inSilent && keySilent && blkOutPk <= 1.0e-5f && !open_ && env_ <= 1.0e-4f && detEnv_ <= retLin)
        quietSamples_ += (uint64_t)frames;
    else
        quietSamples_ = 0;
    idle_ = quietSamples_ > kIdleSamples + (uint64_t)std::max(0, laMax_);

    // Openings in the last bar.
    const double barSamples = (double)tsNum_ * (4.0 / (double)tsDen_) * 60.0 / bpm_ * sr_;
    int inBar = 0;
    for (int k = 0; k < trigN_; ++k)
        if ((double)(clock_ - trigAt_[(size_t)k]) <= barSamples) ++inBar;

    int state;
    if (open_) state = env_ < 0.99f ? 1 : retrig ? 3 : detEnv_ >= retLin ? 2 : 3;
    else
        state = env_ > 0.01f ? 4 : 0;
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
    midiEvs_ = nullptr;
    midiN_ = 0;
    tFed_ = false;

    patVal_ = patVal;
    const float patOut = patT > 0 ? 1.0f - depth * (1.0f - patVal) : 1.0f;
    p_[PatOut].store(patOut, std::memory_order_relaxed);
    patPhaseA_.store((float)patPhase_, std::memory_order_relaxed);
    patGainA_.store(patT == 1 ? patOut : 1.0f, std::memory_order_relaxed);
    patValA_.store(patOut, std::memory_order_relaxed);
    patBeatsA_.store(pSync ? (float)patBeats : 0.0f, std::memory_order_relaxed);
    if (frames > 0) {
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

// Telemetry block, then the three histories (oldest first). Writes as much of that layout
// as fits in maxSamples. Lock-free; torn reads are fine.
int32_t Shutter::scopeRead(float* out, int32_t maxSamples) const {
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
    float pt[kPat] = {};
    const float ph = patPhaseA_.load(std::memory_order_relaxed);
    pt[P_Phase] = ph;
    pt[P_Step] = (float)std::min(patSteps() - 1, (int)(ph * patSteps()));
    pt[P_Gain] = patGainA_.load(std::memory_order_relaxed);
    pt[P_Value] = patValA_.load(std::memory_order_relaxed);
    pt[P_Target] = (float)patTarget();
    pt[P_Steps] = (float)patSteps();
    pt[P_Beats] = patBeatsA_.load(std::memory_order_relaxed);
    for (int k = 0; k < kPat && n < maxSamples; ++k) out[n++] = pt[k];
    return n;
}

const char* Shutter::paramName(int32_t i) const {
    static const char* nm[Step1] = {"Threshold", "Return", "Attack", "Hold", "Release", "Floor", "Lookahead", "Flip",
        "Det HP", "Det LP", "Listen", "Shape", "Retrigger", "Det Filter", "Peak Hold", "External Key", "Pattern Target",
        "Pattern Rate", "Pattern Depth", "Pattern Sync", "Pattern Smooth", "Pattern Swing", "Pattern Steps",
        "Pattern Levels", "Pattern Retrig", "Pattern Gated", "Pattern Complexity", "Pattern Out"};
    static const auto steps = [] {
        std::array<std::string, kNumSteps> a;
        for (int k = 0; k < kNumSteps; ++k) a[(size_t)k] = "Step " + std::to_string(k + 1);
        return a;
    }();
    if (i >= 0 && i < Step1) return nm[i];
    if (i >= Step1 && i < kNumParams) return steps[(size_t)(i - Step1)].c_str();
    return "";
}

float Shutter::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Shutter::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

// 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
std::string Shutter::deviceText(int32_t id) const {
    char b[768];
    static const char* shapes[3] = {"linear", "log", "snap"};
    static const char* states[5] = {"closed", "attack", "open", "hold", "release"};
    const float thr = -70.0f + get(Threshold) * 70.0f;
    if (id == 0) {
        const bool ext = get(ExternalKey) >= 0.5f && sidechainSourceTrackId() >= 0;
        std::string s = get(Flip) >= 0.5f ? "Duck" : "Gate";
        std::snprintf(b, sizeof b, " - threshold %.1f dB - return %.1f dB - %s %s / %s / %s - %s", thr,
            get(Return) * 24.0f, shapes[shapeIdx()], msText(expMap(get(Attack), 0.01, 100.0)).c_str(),
            msText(expMap(get(Hold), 0.1, 500.0)).c_str(), msText(expMap(get(Release), 1.0, 2000.0)).c_str(),
            get(Floor) <= 0.001f ? "floor -inf" : ("floor " + dbText(-70.0f + get(Floor) * 70.0f)).c_str());
        s += b;
        std::snprintf(b, sizeof b, " - look %d ms", kLookMs[lookIdx()]);
        s += b;
        if (get(Retrigger) >= 0.5f) s += " - retrigger";
        s += ext ? " - external key" : " - internal key";
        if (get(DetFilter) >= 0.5f)
            s += " - key filter " + hzText(expMap(get(DetHP), 20.0, 2000.0)) + "..." +
                 hzText(expMap(get(DetLP), 200.0, 20000.0));
        else
            s += " - key full-band";
        if (get(PeakHold) >= 0.5f) s += " - peak hold";
        if (get(Listen) >= 0.5f) s += " - LISTEN (key monitor)";
        if (patTarget() > 0) {
            static const char* syncN[5] = {"1/4", "1/2", "1 bar", "2 bars", "4 bars"};
            static const char* freeN[5] = {"4 Hz", "2 Hz", "1 Hz", "0.5 Hz", "0.25 Hz"};
            std::snprintf(b, sizeof b,
                " - PATTERN %s %s, %d steps x %d levels, depth %.0f %%, smooth %.0f %%, swing %.0f %%%s%s",
                patTarget() == 1 ? (get(PatGated) >= 0.5f ? "volume (gated)" : "volume") : "map (Pattern Out)",
                get(PatSync) >= 0.5f ? syncN[patRateIdx()] : freeN[patRateIdx()], patSteps(), patLevels(),
                get(PatDepth) * 100.0f, get(PatSmooth) * 100.0f, get(PatSwing) * 75.0f,
                get(PatRetrig) >= 0.5f ? ", MIDI retrig" : "", "");
            s += b;
            s += " [";
            for (int k = 0; k < patSteps(); ++k) {
                const float v = std::round(get(Step1 + k) * patLevels()) / patLevels();
                s += v <= 0.0f ? '.' : v >= 1.0f ? '#' : v >= 0.5f ? '+' : '-';
            }
            s += "]";
        }
        return s;
    }
    if (id == 1) {
        float sc[kTele];
        scopeRead(sc, kTele);
        const int st = std::clamp((int)std::lround(sc[S_State]), 0, 4);
        float pt[kScope];
        const int pn = scopeRead(pt, kScope);
        std::string pat;
        if (patTarget() > 0 && pn >= kScope) {
            char pb[160];
            std::snprintf(pb, sizeof pb, " - pattern step %d/%d, phase %.2f, value %.0f %%",
                (int)pt[kPatAt + P_Step] + 1, patSteps(), pt[kPatAt + P_Phase], pt[kPatAt + P_Value] * 100.0f);
            pat = pb;
        }
        std::snprintf(b, sizeof b,
            "%s - gain %.0f %% - GR %.1f dB (peak %.1f dB) - in %.1f dB - out %.1f dB - detector %.1f dB vs "
            "threshold %.1f dB - "
            "open %.0f %% of %.2g s - %.0f openings in the last bar (%.0f since reset) - %s key %.1f dB - "
            "latency %.0f smp",
            states[st], sc[S_GateGain] * 100.0f, sc[S_GrDb], sc[S_PeakGrDb], sc[S_InDb], sc[S_OutDb], sc[S_DetDb], thr,
            sc[S_OpenRatio] * 100.0f, sc[S_WindowSec], sc[S_TrigPerBar], sc[S_Triggers],
            sc[S_ExtKey] > 0.5f ? "external" : "internal", sc[S_KeyDb], sc[S_Latency]);
        return b + pat;
    }
    if (id == 2) {
        return "All params 0..1. Threshold: -70 + 70*v dBFS (0.457 = -38 dB). Return: 24*v dB below the threshold, "
               "where the gate "
               "closes again (hysteresis). Attack 0.01*10000^v ms (0.175 = 0.05 ms, 0.5 = 1 ms). Hold 0.1*5000^v ms "
               "(0.588 = 15 ms). "
               "Release 1*2000^v ms (0.63 = 120 ms). Floor (range): 0 = -inf (mute), else -70 + 70*v dB — the level "
               "when closed, "
               "or how deep Duck goes. Lookahead: 0 = 0 ms, 0.5 = 1 ms, 1 = 5 ms (adds latency). Flip: 0 Gate, 1 Duck. "
               "Det HP 20*100^v Hz (0.301 = 80 Hz). Det LP 200*100^v Hz (0.548 = 2.5 kHz). Listen: hear the filtered "
               "key. "
               "Shape: 0 Linear (straight ramps), 0.5 Log (one-pole, the classic), 1 Snap (stays open, then shuts "
               "hard). "
               "Retrigger: trigger mode, each hit fires one attack-hold-release. Det Filter: the key band-pass is in. "
               "Peak Hold: the detector holds peaks (steadier on low notes). External Key: use the routed sidechain "
               "source. "
               "Toggles: >= 0.5 = on. Pattern Target: 0 Off, 0.5 Volume (a rhythmic gate replacing the threshold gate, "
               "or "
               "riding it when Pattern Gated is on), 1 Map (audio untouched; Pattern Out carries the value as a CV "
               "source). "
               "Pattern Rate: 5 steps — Sync 1/4, 1/2, 1 bar, 2 bars, 4 bars (the whole pattern); Free 4, 2, 1, 0.5, "
               "0.25 Hz. "
               "Pattern Depth: gain = 1 - depth*(1 - step). Pattern Smooth: ramp into a step, up to half a step. "
               "Pattern Swing: "
               "0..75 % (delays every second step by up to 3/8 step). Pattern Steps: 0/0.5/1 = 8/16/32. Pattern "
               "Levels: "
               "0/0.5/1 = 4/8/16 (steps snap to them). Pattern Retrig: a MIDI note-on restarts it. Step 1..32: 0..1 "
               "each.";
    }
    return {};
}

float Shutter::Svf::run(float x, St& s, bool high) const {
    const float v3 = x - s.ic2;
    const float v1 = a1 * s.ic1 + a2 * v3;
    const float v2 = s.ic2 + a2 * s.ic1 + a3 * v3;
    s.ic1 = 2.0f * v1 - s.ic1;
    s.ic2 = 2.0f * v2 - s.ic2;
    return high ? x - k * v1 - v2 : v2;
}

Shutter::Svf Shutter::svf(double hz) const {
    Svf f;
    f.g = (float)std::tan(kPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_);
    f.a1 = 1.0f / (1.0f + f.g * (f.g + f.k));
    f.a2 = f.g * f.a1;
    f.a3 = f.g * f.a2;
    return f;
}

float Shutter::patternAt(const float* v, int n, float pos, float smooth, float swing) {
    int i = std::clamp((int)pos, 0, n - 1);
    float e = pos - (float)i; // time since step i's grid line
    if ((i & 1) && e < swing) {
        i -= 1;
        e += 1.0f;
    } // still the even step before (swung)
    else if (i & 1)
        e -= swing;
    const float cur = v[i];
    if (smooth <= 1e-4f || e >= smooth) return cur;
    const float prev = v[(i + n - 1) % n];
    return prev + (cur - prev) * (e / smooth);
}

std::string Shutter::msText(double ms) {
    char b[32];
    if (ms >= 100.0) std::snprintf(b, sizeof b, "%.0f ms", ms);
    else if (ms >= 10.0)
        std::snprintf(b, sizeof b, "%.1f ms", ms);
    else
        std::snprintf(b, sizeof b, "%.2f ms", ms);
    return b;
}

std::string Shutter::hzText(double hz) {
    char b[32];
    if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.1f kHz", hz / 1000.0);
    else
        std::snprintf(b, sizeof b, "%.0f Hz", hz);
    return b;
}

std::string Shutter::dbText(float v) {
    char b[32];
    std::snprintf(b, sizeof b, "%.1f dB", v);
    return b;
}

void Shutter::onTrigger() {
    ++trigTotal_;
    if (trigN_ < kTrig) trigAt_[(size_t)trigN_++] = clock_;
    else {
        for (int k = 1; k < kTrig; ++k) trigAt_[(size_t)k - 1] = trigAt_[(size_t)k];
        trigAt_[kTrig - 1] = clock_;
    }
}

void Shutter::resetState() {
    std::fill(laBuf_.begin(), laBuf_.end(), 0.0f);
    laW_ = 0;
    detEnv_ = inEnv_ = outEnv_ = keyEnv_ = 0.0f;
    g_ = 1.0f;
    env_ = 0.0f;
    holdCtr_ = 0;
    open_ = false;
    armed_ = true;
    hp_ = {};
    lp_ = {};
    openRatio_ = 0.0f;
    colIn_ = 0.0f;
    colGate_ = 1.0f;
    colDet_ = 0.0f;
    colN_ = 0;
    for (int k = 0; k < kHist; ++k)
        hist_[0][(size_t)k] = -120.0f, hist_[1][(size_t)k] = 1.0f, hist_[2][(size_t)k] = -120.0f;
    patPhase_ = 0.0;
    patVal_ = 1.0f;
}

} // namespace nota
