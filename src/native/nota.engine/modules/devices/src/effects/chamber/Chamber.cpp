// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Chamber.h"
#include "ChamberKernel.h"
#include "common/DeviceMath.h"
#include "effects/chamber/ChamberIrSynth.h"

#include <mutex>
#include <memory>
#include <cstring>
#include <cstdio>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <chrono>
#include <atomic>
#include <algorithm>
#include <signalsmith-linear/fft.h>
#include <vector>
#include <utility>
#include <thread>
#include <string>

namespace nota {

// ============================================================================
// Chamber device
// ============================================================================

Chamber::Chamber() {
    static const float def[kNumParams] = {
        0.5f, 0.35f, 1.0f, 1.0f, 0.70710678f, 0.0f, // blend · dry/wet · conv · algo · dry level · routing
        0.0f, 0.0f, 1.0f, 0.0f, 0.5f, 0.2f, 0.0f, 0.0f,
        1.0f, // IR · start · decay · attack · size · predelay · sync · reverse · true stereo
        0.0f, 0.588f, 0.5f, 0.7f, 0.613f, 0.2f, 0.0f, 0.6f,
        0.25f, // mode · decay · size · diffusion · damping · predelay · sync · low · high
        0.0f, 0.0f, 0.0f, 0.41f, 0.3f, 0.5f, 1.0f,
        1.0f,                         // freeze · freeze in · vintage · rate · depth · shimmer · pitch · feedback
        0.0f, 0.5f, 0.5f, 1.0f, 0.5f, // EQ low cut · low gain · high shelf · high cut · position
        0.0f, 0.548f, 0.6666667f, 0.5f, 0.0f, 0.5f, 0.0f,
        1.0f, // duck · release · output · width · bass mono · quality · wet only · zero latency
        0.0f  // view: L (#531)
    };
    for (int i = 0; i < kNumParams; ++i) p_[i].store(def[i], std::memory_order_relaxed);
}

Chamber::~Chamber() {
    stopWorker();
    { std::unique_ptr<chamber::ConvKernel> unclaimed(ready_.exchange(nullptr, std::memory_order_acq_rel)); }
    cur_.reset();
    fade_.reset();
    ramp_.reset();
    xfOld_.reset();
    for (auto& k : spill_) k.reset();
    drainRetired();
}

void Chamber::setSampleRate(double sr, int32_t /*maxBlock*/) {
    const double nsr = sr > 0 ? sr : 44100.0;
    if (prepared_ && nsr == sr_) return; // re-prepare at the same rate keeps tails + kernels
    sr_ = nsr;
    srA_.store(nsr, std::memory_order_relaxed);
    algo_.prepare(sr_);
    for (auto* d : {&convPre_[0], &convPre_[1], &algoPre_[0], &algoPre_[1]}) d->init(static_cast<int>(2.6 * sr_) + 8);
    convPreCur_ = algoPreCur_ = -1.0f;
    for (auto& b : eq_) b.clear();
    duckEnv_ = 0.0f;
    duckGain_ = 1.0f;
    msLp1_[0] = msLp1_[1] = 0.0f;
    // Not concurrent with process(): install a kernel for the current params directly.
    { std::unique_ptr<chamber::ConvKernel> unclaimed(ready_.exchange(nullptr, std::memory_order_acq_rel)); }
    cur_.reset();
    fade_.reset();
    ramp_.reset();
    xfOld_.reset();
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
    prepared_ = true;
    rebuild(true);
    startWorker();
}

void Chamber::setTransport(double /*beatStart*/, double spb, bool /*playing*/) {
    if (spb > 1.0) spb_ = spb;
}

void Chamber::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

float Chamber::shimRatio() const {
    static const float r[3] = {0.5f, 1.4983071f, 2.0f}; // −12 · +7 · +12 semitones
    return r[std::clamp(static_cast<int>(std::lround(get(ShimmerPitch) * 2.0f)), 0, 2)];
}

float Chamber::predelaySamples(int timeParam, int syncParam) const {
    static const double beats[kSyncDivs] = {
        1.0 / 16, 1.0 / 12, 1.0 / 8, 1.0 / 6, 1.0 / 4, 1.0 / 3, 3.0 / 8, 1.0 / 2, 2.0 / 3, 3.0 / 4, 1.0, 1.5, 2.0};
    const float v = get(timeParam);
    double s;
    if (get(syncParam) >= 0.5f)
        s = beats[std::clamp(static_cast<int>(std::lround(v * (kSyncDivs - 1))), 0, kSyncDivs - 1)] * spb_;
    else
        s = 0.5 * v * v * sr_; // 0..500 ms, quadratic
    return static_cast<float>(std::clamp(s, 0.0, 2.5 * sr_));
}

void Chamber::updateEq() {
    const float v[4] = {get(EqLowCut), get(EqLowGain), get(EqHighShelf), get(EqHighCut)};
    if (v[0] == eqV_[0] && v[1] == eqV_[1] && v[2] == eqV_[2] && v[3] == eqV_[3] && eqSr_ == sr_) return;
    const bool wasOn[4] = {eqOn_[0], eqOn_[1], eqOn_[2], eqOn_[3]};
    const double lc = chamber::expMap(v[0], 20.0, 2000.0), hc = chamber::expMap(v[3], 1000.0, 20000.0);
    const double lg = (v[1] - 0.5) * 36.0, hg = (v[2] - 0.5) * 36.0;
    eqOn_[0] = v[0] > 0.001f;
    eqOn_[1] = std::fabs(lg) > 0.05;
    eqOn_[2] = std::fabs(hg) > 0.05;
    eqOn_[3] = v[3] < 0.999f && hc < sr_ * 0.45;
    eq_[0].hp(lc, sr_);
    eq_[1].lowShelf(std::max(250.0, lc * 2.0), lg, sr_);
    eq_[2].highShelf(std::min(4000.0, hc * 0.5), hg, sr_);
    eq_[3].lp(std::min(hc, sr_ * 0.45), sr_);
    for (int i = 0; i < 4; ++i)
        if (eqOn_[i] && !wasOn[i]) eq_[i].clear();
    for (int i = 0; i < 4; ++i) eqV_[i] = v[i];
    eqSr_ = sr_;
}

void Chamber::runEq(float* l, float* r, int n) {
    for (int s = 0; s < 4; ++s) {
        if (!eqOn_[s]) continue;
        auto& b = eq_[s];
        for (int i = 0; i < n; ++i) {
            l[i] = b.run(0, l[i]);
            r[i] = b.run(1, r[i]);
        }
    }
}

void Chamber::install(std::unique_ptr<chamber::ConvKernel> k) {
    if (k->sampleRate != sr_) {
        retire(std::move(k));
        return;
    } // built for a rate we left
    if (!cur_ || !convActive_) { // nothing sounding: just swap
        retire(std::move(cur_));
        cur_ = std::move(k);
        retire(std::move(fade_));
        retire(std::move(ramp_));
        return;
    }
    if (k->compatible(*cur_)) { // same layout: morph the whole tail
        k->transplant(*cur_);
        xfOld_ = std::move(cur_);
        cur_ = std::move(k);
        xfPos_ = 0;
        xfWarm_ = cur_->warmup();
        return;
    }
    // Structural change (zero latency / true stereo / length class): the old kernel rings
    // out what it already heard while the new one starts clean; a third ramps away.
    retire(std::move(ramp_));
    ramp_ = std::move(fade_);
    rampPos_ = 0;
    fade_ = std::move(cur_);
    cur_ = std::move(k);
}

void Chamber::clearConv() {
    if (cur_) cur_->clear();
    retire(std::move(fade_));
    retire(std::move(ramp_));
    retire(std::move(xfOld_));
}

void Chamber::retire(std::unique_ptr<chamber::ConvKernel> k) {
    if (!k) return;
    const uint32_t h = rHead_.load(std::memory_order_relaxed);
    if (h - rTail_.load(std::memory_order_acquire) < kRing) {
        ring_[h % kRing] = std::move(k);
        rHead_.store(h + 1, std::memory_order_release);
        return;
    }
    for (auto& s : spill_)
        if (!s) {
            s = std::move(k);
            return;
        }
    // Ring and spill both full: the worker hasn't run for 40 retirements. Unreachable in
    // practice (it drains every 10 ms); dropping without freeing beats freeing on the audio thread.
    (void)k.release();
}

void Chamber::flushSpill() {
    for (auto& s : spill_) {
        if (!s) continue;
        const uint32_t h = rHead_.load(std::memory_order_relaxed);
        if (h - rTail_.load(std::memory_order_acquire) >= kRing) return;
        ring_[h % kRing] = std::move(s);
        rHead_.store(h + 1, std::memory_order_release);
    }
}

void Chamber::process(float* buf, int32_t frames) {
    if (!prepared_ || frames <= 0) return;
    const auto cpuTimer = blockTimer(frames, sr_);
    flushSpill();
    if (!xfOld_)
        if (auto* raw = ready_.exchange(nullptr, std::memory_order_acq_rel))
            install(std::unique_ptr<chamber::ConvKernel>(raw));

    // Idle skip: silent input on settled tails leaves the buffer untouched. A
    // frozen loop never sleeps (infinite tail). Kernel hand-off above still
    // runs so IR re-shapes land while sleeping. Sleep engages only after the
    // full feed-through flight time (pre-delays + kernel blocks + FDN lines),
    // tracked below — output silence alone proves nothing while lines are full.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    if (inSilent && get(Freeze) < 0.5f && idle_) return;

    const double sr = sr_;
    // ---- block parameters ----
    const float blend = get(Blend);
    const float mix = get(DryWet);
    const bool convOn = get(ConvOn) >= 0.5f, algoOn = get(AlgoOn) >= 0.5f;
    const float dryLvl = 2.0f * get(DryLevel) * get(DryLevel);
    const bool serial = get(Routing) >= 0.5f;
    const bool freeze = get(Freeze) >= 0.5f, freezeIn = get(FreezeIn) >= 0.5f;
    const bool wetOnly = get(WetOnly) >= 0.5f;
    const int eqPos = std::clamp(static_cast<int>(std::lround(get(EqPosition) * 2.0f)), 0, 2);
    const float outGain = std::pow(10.0f, (-24.0f + 36.0f * get(Output)) / 20.0f);
    const float width = get(Width) * 2.0f;
    const float monoV = get(BassMono);
    const float monoC = monoV > 0.001f ? chamber::onePole(chamber::expMap(monoV, 30.0, 500.0), sr) : 0.0f;
    const float duckDb = get(DuckAmount) * 24.0f;
    const float duckRel =
        static_cast<float>(std::exp(-1.0 / (chamber::expMap(get(DuckRelease), 20.0, 2000.0) * 0.001 * sr)));
    const float duckAtk = static_cast<float>(std::exp(-1.0 / (0.003 * sr)));
    const float gc = std::sqrt(1.0f - blend), ga = std::sqrt(blend);
    const float wetMix = wetOnly ? 1.0f : std::sin(mix * static_cast<float>(chamber::kPi) * 0.5f);
    const float dryMix = wetOnly ? 0.0f : std::cos(mix * static_cast<float>(chamber::kPi) * 0.5f) * dryLvl;
    const float sm = static_cast<float>(1.0 - std::exp(-1.0 / (0.015 * sr)));
    const float preGlide = static_cast<float>(1.0 - std::exp(-1.0 / (0.03 * sr)));
    const int lat = cur_ ? cur_->latency : 0;
    const float convPreT = std::max(0.0f, predelaySamples(ConvPredelay, ConvSync) - static_cast<float>(lat));
    const float algoPreT = predelaySamples(AlgoPredelay, AlgoSync);
    if (convPreCur_ < 0.0f) convPreCur_ = convPreT;
    if (algoPreCur_ < 0.0f) algoPreCur_ = algoPreT;
    updateEq();

    const chamber::Algo::Params ap = algoParams();

    // Engines on/off ramp their contribution; a fully-off engine stops and forgets its tail.
    if (convOn && !convActive_) {
        convActive_ = true;
        clearConv();
    }
    if (algoOn && !algoActive_) {
        algoActive_ = true;
        algo_.reset();
    }

    float wetPk = 0.0f, outPk[2] = {0.0f, 0.0f}, inPk = 0.0f, feedPk = 0.0f;
    for (int32_t off = 0; off < frames; off += kChunk) {
        const int n = std::min<int>(kChunk, frames - off);
        float* b = buf + static_cast<size_t>(off) * 2;
        // dry + ducking detector + the (freeze-gated) engine feed
        for (int i = 0; i < n; ++i) {
            const float l = b[i * 2], r = b[i * 2 + 1];
            dL_[i] = l;
            dR_[i] = r;
            const float a = std::max(std::fabs(l), std::fabs(r));
            inPk = std::max(inPk, a);
            duckEnv_ = a > duckEnv_ ? a + duckAtk * (duckEnv_ - a) : a + duckRel * (duckEnv_ - a);
            feedG_ += sm * (((freeze && !freezeIn) ? 0.0f : 1.0f) - feedG_);
            fL_[i] = l * feedG_;
            fR_[i] = r * feedG_;
            feedPk = std::max(feedPk, std::max(std::fabs(fL_[i]), std::fabs(fR_[i])));
        }
        if (eqPos == 0) runEq(fL_, fR_, n);

        // ---- convolution ----
        std::fill(cL_, cL_ + n, 0.0f);
        std::fill(cR_, cR_ + n, 0.0f);
        if (convActive_) {
            for (int i = 0; i < n; ++i) {
                convPreCur_ += preGlide * (convPreT - convPreCur_);
                convPre_[0].push(fL_[i]);
                convPre_[1].push(fR_[i]);
                tL_[i] = convPre_[0].lin(convPreCur_);
                tR_[i] = convPre_[1].lin(convPreCur_);
            }
            if (xfOld_) { // re-shaped IR: both kernels hear the input; hold the old until
                          // the new one's output is exact, then an equal-power crossfade
                std::fill(xL_, xL_ + n, 0.0f);
                std::fill(xR_, xR_ + n, 0.0f);
                xfOld_->process(tL_, tR_, xL_, xR_, n);
                if (cur_) cur_->process(tL_, tR_, cL_, cR_, n);
                for (int i = 0; i < n; ++i) {
                    const float a = std::clamp(static_cast<float>(xfPos_++ - xfWarm_) / kXfLen, 0.0f, 1.0f);
                    const float gn = std::sin(a * 1.5707963f), go = std::cos(a * 1.5707963f);
                    cL_[i] = cL_[i] * gn + xL_[i] * go;
                    cR_[i] = cR_[i] * gn + xR_[i] * go;
                }
                if (xfPos_ >= xfWarm_ + kXfLen) retire(std::move(xfOld_));
            } else if (cur_)
                cur_->process(tL_, tR_, cL_, cR_, n);
            if (fade_) {
                fade_->process(nullptr, nullptr, cL_, cR_, n);
                if (fade_->silent()) retire(std::move(fade_));
            }
            if (ramp_) {
                std::fill(tL_, tL_ + n, 0.0f);
                std::fill(tR_, tR_ + n, 0.0f);
                ramp_->process(nullptr, nullptr, tL_, tR_, n);
                for (int i = 0; i < n; ++i) {
                    const float g = std::max(0.0f, 1.0f - static_cast<float>(rampPos_++) / kRampLen);
                    cL_[i] += tL_[i] * g;
                    cR_[i] += tR_[i] * g;
                }
                if (rampPos_ >= kRampLen) retire(std::move(ramp_));
            }
        }

        // ---- algorithm ----
        std::fill(aL_, aL_ + n, 0.0f);
        std::fill(aR_, aR_ + n, 0.0f);
        if (algoActive_) {
            const bool fromConv = serial && convActive_;
            for (int i = 0; i < n; ++i) {
                algoPreCur_ += preGlide * (algoPreT - algoPreCur_);
                algoPre_[0].push(fromConv ? cL_[i] : fL_[i]);
                algoPre_[1].push(fromConv ? cR_[i] : fR_[i]);
                tL_[i] = algoPre_[0].lin(algoPreCur_);
                tR_[i] = algoPre_[1].lin(algoPreCur_);
            }
            // Idle gate: silent input + a tail below −140 dB → skip the tank until sound returns.
            float inPkA = 0.0f;
            for (int i = 0; i < n; ++i) inPkA = std::max(inPkA, std::max(std::fabs(tL_[i]), std::fabs(tR_[i])));
            if (inPkA > 0.0f || freeze || algoQuiet_ < kQuietSamples) {
                algo_.process(tL_, tR_, aL_, aR_, n, ap);
                float outPkA = 0.0f;
                for (int i = 0; i < n; ++i) outPkA = std::max(outPkA, std::max(std::fabs(aL_[i]), std::fabs(aR_[i])));
                algoQuiet_ = (inPkA == 0.0f && outPkA < 1e-7f) ? algoQuiet_ + n : 0;
                if (algoQuiet_ >= kQuietSamples) algo_.reset(); // drop sub-audible residue
            }
        }

        // ---- wet: blend → tail EQ → width / bass mono → ducking ----
        for (int i = 0; i < n; ++i) {
            convG_ += sm * ((convOn ? gc : 0.0f) - convG_);
            algoG_ += sm * ((algoOn ? ga : 0.0f) - algoG_);
            wL_[i] = cL_[i] * convG_ + aL_[i] * algoG_;
            wR_[i] = cR_[i] * convG_ + aR_[i] * algoG_;
        }
        if (eqPos == 1) runEq(wL_, wR_, n);
        for (int i = 0; i < n; ++i) {
            const float mid = 0.5f * (wL_[i] + wR_[i]);
            widthS_ += sm * (width - widthS_);
            float side = 0.5f * (wL_[i] - wR_[i]) * widthS_;
            if (monoC > 0.0f) {
                msLp1_[0] += monoC * (side - msLp1_[0]);
                msLp1_[1] += monoC * (msLp1_[0] - msLp1_[1]);
                side -= msLp1_[1];
            }
            float target = 1.0f;
            if (duckDb > 0.01f) { // wet dips by up to Duck Amount as the input rises −42 → −12 dBFS
                const float envDb = fast_math::linToDb(std::max(1e-6f, duckEnv_));
                target = fast_math::dbToLin(-duckDb * std::clamp((envDb + 42.0f) / 30.0f, 0.0f, 1.0f));
            }
            duckGain_ += sm * (target - duckGain_);
            wetS_ += sm * (wetMix - wetS_);
            dryS_ += sm * (dryMix - dryS_);
            outS_ += sm * (outGain - outS_);
            const float wl = chamber::softClip((mid + side) * duckGain_) * wetS_;
            const float wr = chamber::softClip((mid - side) * duckGain_) * wetS_;
            wetPk = std::max(wetPk, std::max(std::fabs(wl), std::fabs(wr)));
            wL_[i] = wl + dL_[i] * dryS_;
            wR_[i] = wr + dR_[i] * dryS_;
        }
        if (eqPos == 2) runEq(wL_, wR_, n);
        for (int i = 0; i < n; ++i) {
            const float l = wL_[i] * outS_, r = wR_[i] * outS_;
            b[i * 2] = l;
            b[i * 2 + 1] = r;
            outPk[0] = std::max(outPk[0], std::fabs(l));
            outPk[1] = std::max(outPk[1], std::fabs(r));
        }
    }

    // Engines that finished ramping out stop (and drop their tails).
    if (!convOn && convActive_ && convG_ < 1e-4f) {
        convActive_ = false;
        convG_ = 0.0f;
        clearConv();
    }
    if (!algoOn && algoActive_ && algoG_ < 1e-4f) {
        algoActive_ = false;
        algoG_ = 0.0f;
        algo_.reset();
    }

    // Engine-feed peak (decays slowly so a ringing tail blocks accrual); sleep
    // additionally waits out the feed-through flight time (pre-delays, kernel
    // blocks, FDN lines) so late energy arriving after a quiet gap is never cut.
    feedPk_ = feedPk > feedPk_ ? feedPk : feedPk_ * 0.90f;
    const double flight =
        std::max(0.0, static_cast<double>(std::max(convPreCur_, algoPreCur_))) + bigBlock(sr_) + 0.5 * sr_;
    if (inSilent && !freeze && std::max(outPk[0], outPk[1]) <= 1.0e-5f && feedPk_ <= 1.0e-5f)
        quietSamples_ += static_cast<uint64_t>(frames);
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > static_cast<uint64_t>(flight) + kIdleSamples) idle_ = true;

    // ---- meters + CPU ----
    const float dec = static_cast<float>(std::exp(-frames / (0.3 * sr)));
    auto hold = [&](std::atomic<float>& a, float v) {
        a.store(std::max(v, a.load(std::memory_order_relaxed) * dec), std::memory_order_relaxed);
    };
    hold(mWet_, wetPk);
    hold(mOutL_, outPk[0]);
    hold(mOutR_, outPk[1]);
    hold(mIn_, inPk);
    mDuck_.store(-fast_math::linToDb(std::max(1e-6f, duckGain_)), std::memory_order_relaxed);
    latA_.store(cur_ ? cur_->latency : 0, std::memory_order_relaxed);
    kernA_.store((cur_ ? 1 : 0) + (fade_ ? 1 : 0) + (ramp_ ? 1 : 0) + (xfOld_ ? 1 : 0), std::memory_order_relaxed);
    kernSecA_.store(cur_ ? cur_->seconds : 0.0f, std::memory_order_relaxed);
    bpmA_.store(static_cast<float>(60.0 * sr / spb_), std::memory_order_relaxed);
}

const char* Chamber::paramName(int32_t i) const {
    static const char* nm[kNumParams] = {"Blend", "Dry/Wet", "Conv On", "Algo On", "Dry Level", "Routing", "IR",
        "IR Start", "IR Decay", "IR Attack", "IR Size", "Conv Predelay", "Conv Sync", "IR Reverse", "IR True Stereo",
        "Algo Mode", "Algo Decay", "Algo Size", "Algo Diffusion", "Algo Damping", "Algo Predelay", "Algo Sync",
        "Algo Low Decay", "Algo High Decay", "Freeze", "Freeze In", "Algo Vintage", "Mod Rate", "Mod Depth",
        "Shimmer Amount", "Shimmer Pitch", "Shimmer Feedback", "EQ Low Cut", "EQ Low Gain", "EQ High Shelf",
        "EQ High Cut", "EQ Position", "Duck Amount", "Duck Release", "Output", "Width", "Bass Mono", "Quality",
        "Wet Only", "Zero Latency", "View"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float Chamber::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Chamber::setParam(int32_t i, float v) {
    if (i < 0 || i >= kNumParams) return;
    p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

int32_t Chamber::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples < kScope) return 0;
    std::shared_ptr<const chamber::SourceIr> src;
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        src = shownSrc_;
    }
    out[S_WetL] = mWet_.load(std::memory_order_relaxed);
    out[S_WetR] = out[S_WetL];
    out[S_OutL] = mOutL_.load(std::memory_order_relaxed);
    out[S_OutR] = mOutR_.load(std::memory_order_relaxed);
    out[S_DuckDb] = mDuck_.load(std::memory_order_relaxed);
    out[S_Cpu] = readScopeCpuLoad();
    out[S_Latency] = static_cast<float>(latA_.load(std::memory_order_relaxed));
    out[S_SampleRate] = static_cast<float>(srA_.load(std::memory_order_relaxed));
    out[S_Building] =
        (pending_.load(std::memory_order_relaxed) || ready_.load(std::memory_order_relaxed)) ? 1.0f : 0.0f;
    out[S_IrSeconds] = src ? src->seconds() : 0.0f;
    out[S_IrRate] = src ? static_cast<float>(src->sampleRate) : 0.0f;
    out[S_IrChannels] = src ? static_cast<float>(src->channels) : 0.0f;
    out[S_KernelSeconds] = kernSecA_.load(std::memory_order_relaxed);
    out[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
    out[S_Frozen] = get(Freeze) >= 0.5f ? 1.0f : 0.0f;
    out[S_Block] = static_cast<float>(bigBlock(srA_.load(std::memory_order_relaxed)));
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        out[S_UserIr] = userIr_ ? 1.0f : 0.0f;
    }
    out[S_InPeak] = mIn_.load(std::memory_order_relaxed);
    out[S_Kernels] = static_cast<float>(kernA_.load(std::memory_order_relaxed));
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        out[S_ResultSeconds] = convEnvSec_;
        out[S_PreviewSeconds] = algoEnvSec_;
    }
    out[S_PreviewGen] = static_cast<float>(previewGen_.load(std::memory_order_relaxed) % 1000000u);
    return kScope;
}

// Peak envelopes for the card: layer 0 / 1 = the source IR left (LL) / right (RR) over its
// full length; layer 2 = the processed IR as convolved (S_ResultSeconds long); layer 3 = the
// algorithm's rendered impulse response (S_PreviewSeconds long). All normalised to 1.
int32_t Chamber::layerWave(int32_t layer, float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    if (layer == 2 || layer == 3) {
        std::lock_guard<std::mutex> lk(srcMu_);
        const auto& e = layer == 2 ? convEnv_ : algoEnv_;
        if (e.empty()) return 0;
        const size_t n = e.size();
        for (int32_t k = 0; k < maxSamples; ++k) {
            const size_t a = n * static_cast<size_t>(k) / static_cast<size_t>(maxSamples);
            const size_t z = std::max(a + 1, n * static_cast<size_t>(k + 1) / static_cast<size_t>(maxSamples));
            float pk = 0.0f;
            for (size_t i = a; i < z && i < n; ++i) pk = std::max(pk, e[i]);
            out[k] = pk;
        }
        return maxSamples;
    }
    std::shared_ptr<const chamber::SourceIr> src;
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        src = shownSrc_;
    }
    if (!src || src->frames <= 0) return 0;
    const int nc = src->channels;
    const int c = layer <= 0 ? 0 : (nc >= 4 ? 3 : nc >= 2 ? 1 : 0);
    const int64_t F = src->frames;
    float peak = 1e-9f;
    for (int64_t i = 0; i < F; ++i) peak = std::max(peak, std::fabs(src->data[static_cast<size_t>(i * nc + c)]));
    for (int32_t k = 0; k < maxSamples; ++k) {
        const int64_t a = F * k / maxSamples, z = std::max(a + 1, F * (k + 1) / maxSamples);
        float pk = 0.0f;
        for (int64_t i = a; i < z && i < F; ++i)
            pk = std::max(pk, std::fabs(src->data[static_cast<size_t>(i * nc + c)]));
        out[k] = pk / peak;
    }
    return maxSamples;
}

std::string Chamber::deviceText(int32_t id) const {
    if (id == T_IrList) {
        std::string s;
        for (int i = 0; i < chamber::kNumBuiltinIrs; ++i) {
            const auto& sp = chamber::irSpec(i);
            char len[32];
            std::snprintf(len, sizeof(len), "%.1f", static_cast<double>(sp.length));
            s += sp.name;
            s += '\t';
            s += sp.category;
            s += '\t';
            s += len;
            s += '\n';
        }
        return s;
    }
    std::lock_guard<std::mutex> lk(srcMu_);
    if (id == T_UserName) return userIr_ ? userIr_->name : std::string{};
    const int ir = irIndex();
    if (id == T_IrName) {
        if (ir < chamber::kNumBuiltinIrs) return chamber::irSpec(ir).name;
        return userIr_ ? userIr_->name : std::string("User IR — none loaded");
    }
    if (id == T_IrCategory) return ir < chamber::kNumBuiltinIrs ? chamber::irSpec(ir).category : "USER";
    return {};
}

bool Chamber::loadFile(const std::string& path) {
    auto sb = decodeAudioFile(path);
    if (!sb || sb->empty()) return false;
    const int inCh = sb->channels;
    const int ch = inCh >= 4 ? 4 : inCh >= 2 ? 2 : 1;
    float peak = 0.0f;
    for (float v : sb->samples) peak = std::max(peak, std::fabs(v));
    if (peak <= 1e-9f) return false;
    int64_t first = 0; // skip leading silence (below −60 dB of the peak)
    while (first < sb->frames) {
        bool hit = false;
        for (int c = 0; c < inCh; ++c)
            if (std::fabs(sb->samples[static_cast<size_t>(first * inCh + c)]) > peak * 1e-3f) {
                hit = true;
                break;
            }
        if (hit) break;
        ++first;
    }
    first = std::max<int64_t>(0, first - 8);
    const double srcSr = sb->sourceSampleRate > 0 ? sb->sourceSampleRate : 44100.0;
    const int64_t frames = std::min<int64_t>(sb->frames - first, static_cast<int64_t>(kMaxUserSec * srcSr));
    if (frames < 8) return false;
    auto ir = std::make_shared<chamber::SourceIr>();
    ir->channels = ch;
    ir->frames = frames;
    ir->sampleRate = srcSr;
    ir->category = "USER";
    std::string nm = path;
    if (auto sl = nm.find_last_of("/\\"); sl != std::string::npos) nm = nm.substr(sl + 1);
    if (auto dot = nm.find_last_of('.'); dot != std::string::npos && dot > 0) nm = nm.substr(0, dot);
    ir->name = nm;
    ir->data.resize(static_cast<size_t>(frames) * static_cast<size_t>(ch));
    for (int64_t i = 0; i < frames; ++i)
        for (int c = 0; c < ch; ++c)
            ir->data[static_cast<size_t>(i * ch + c)] = sb->samples[static_cast<size_t>((first + i) * inCh + c)];
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        userIr_ = std::move(ir);
    }
    userGen_.fetch_add(1, std::memory_order_relaxed);
    p_[IrSelect].store(1.0f, std::memory_order_relaxed);
    wake();
    if (prepared_) rebuild(false);
    return true;
}

// State blob: the user IR (if any). Always non-empty so a restore re-syncs the kernel.
std::vector<uint8_t> Chamber::getState() const {
    std::shared_ptr<const chamber::SourceIr> u;
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        u = userIr_;
    }
    std::vector<uint8_t> s;
    auto pU = [&](uint32_t v) {
        for (int b = 0; b < 4; ++b) s.push_back(static_cast<uint8_t>(v >> (8 * b)));
    };
    auto pF = [&](float f) {
        uint32_t v;
        std::memcpy(&v, &f, 4);
        pU(v);
    };
    pU(kMagic);
    pU(1u);
    pU(u ? 1u : 0u);
    if (u) {
        pU(static_cast<uint32_t>(u->name.size()));
        s.insert(s.end(), u->name.begin(), u->name.end());
        pF(static_cast<float>(u->sampleRate));
        pU(static_cast<uint32_t>(u->channels));
        pU(static_cast<uint32_t>(u->frames));
        s.reserve(s.size() + u->data.size() * 4);
        for (float v : u->data) pF(v);
    }
    return s;
}

void Chamber::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 12) return;
    size_t off = 0;
    const size_t n = static_cast<size_t>(size);
    auto gU = [&](uint32_t& v) {
        if (off + 4 > n) return false;
        v = 0;
        for (int b = 0; b < 4; ++b) v |= static_cast<uint32_t>(data[off++]) << (8 * b);
        return true;
    };
    auto gF = [&](float& f) {
        uint32_t v;
        if (!gU(v)) return false;
        std::memcpy(&f, &v, 4);
        return true;
    };
    uint32_t magic = 0, ver = 0, has = 0;
    if (!gU(magic) || magic != kMagic || !gU(ver) || !gU(has)) return;
    std::shared_ptr<chamber::SourceIr> ir;
    if (has) {
        uint32_t nl = 0, ch = 0, frames = 0;
        float sr = 0.0f;
        if (!gU(nl) || off + nl > n) return;
        std::string name(reinterpret_cast<const char*>(data + off), nl);
        off += nl;
        if (!gF(sr) || !gU(ch) || !gU(frames)) return;
        if (ch < 1 || ch > 4 || sr <= 0.0f || frames == 0 || off + static_cast<size_t>(frames) * ch * 4 > n) return;
        ir = std::make_shared<chamber::SourceIr>();
        ir->name = name;
        ir->category = "USER";
        ir->sampleRate = sr;
        ir->channels = static_cast<int>(ch);
        ir->frames = frames;
        ir->data.resize(static_cast<size_t>(frames) * ch);
        for (auto& v : ir->data) gF(v);
    }
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        userIr_ = std::move(ir);
    }
    userGen_.fetch_add(1, std::memory_order_relaxed);
    wake();
    if (prepared_) rebuild(false); // synchronous: the restored params + IR are live on return
}

bool Chamber::Sig::operator==(const Sig& o) const {
    return ir == o.ir && start == o.start && end == o.end && attack == o.attack && size == o.size && rev == o.rev &&
           ts == o.ts && zl == o.zl && q == o.q && sr == o.sr && gen == o.gen;
}

chamber::Algo::Params Chamber::algoParams() const {
    chamber::Algo::Params ap;
    ap.mode = std::clamp(static_cast<int>(std::lround(get(AlgoMode) * 3.0f)), 0, 3);
    ap.decay = static_cast<float>(chamber::expMap(get(AlgoDecay), 0.2, 20.0));
    ap.size = static_cast<float>(chamber::expMap(get(AlgoSize), 0.4, 2.5));
    ap.diffuse = get(AlgoDiffusion);
    ap.damp = static_cast<float>(chamber::expMap(get(AlgoDamping), 500.0, 18000.0));
    ap.lowMul = static_cast<float>(chamber::expMap(get(AlgoLowDecay), 0.25, 4.0));
    ap.highMul = static_cast<float>(chamber::expMap(get(AlgoHighDecay), 0.25, 4.0));
    ap.rate = static_cast<float>(chamber::expMap(get(ModRate), 0.05, 8.0));
    ap.depth = get(ModDepth);
    ap.freeze = get(Freeze) >= 0.5f;
    ap.vintage = get(AlgoVintage) >= 0.5f;
    ap.cubic = qualityIndex() == 2;
    ap.shimAmt = get(ShimmerAmount);
    ap.shimRatio = shimRatio();
    ap.shimFb = get(ShimmerFeedback) >= 0.5f;
    return ap;
}

// Peak envelope of `chans` over kEnvBins, normalised to its maximum.
std::vector<float> Chamber::envelope(const std::vector<std::vector<float>>& chans, const std::vector<int>& use) {
    std::vector<float> env(kEnvBins, 0.0f);
    if (chans.empty() || chans[0].empty()) return env;
    const size_t n = chans[0].size();
    float peak = 1e-12f;
    for (int k = 0; k < kEnvBins; ++k) {
        const size_t a = n * static_cast<size_t>(k) / kEnvBins,
                     z = std::max(a + 1, n * static_cast<size_t>(k + 1) / kEnvBins);
        float pk = 0.0f;
        for (int c : use) {
            const auto& v = chans[static_cast<size_t>(c)];
            for (size_t i = a; i < z && i < n; ++i) pk = std::max(pk, std::fabs(v[i]));
        }
        env[static_cast<size_t>(k)] = pk;
        peak = std::max(peak, pk);
    }
    for (auto& v : env) v /= peak;
    return env;
}

std::vector<int> Chamber::previewSignature() const {
    static const int ids[] = {AlgoMode, AlgoDecay, AlgoSize, AlgoDiffusion, AlgoDamping, AlgoLowDecay, AlgoHighDecay,
        Freeze, AlgoVintage, ModRate, ModDepth, ShimmerAmount, ShimmerPitch, ShimmerFeedback};
    std::vector<int> v;
    for (int id : ids) v.push_back(static_cast<int>(std::lround(get(id) * 2000.0f)));
    return v;
}

// Render the algorithm's impulse response offline (a private tank at 16 kHz) and keep its
// envelope — the card's decay graph draws the real echogram, not a model of it.
void Chamber::renderAlgoPreview() {
    const auto p = algoParams();
    if (!preview_) {
        preview_ = std::make_unique<chamber::Algo>();
        preview_->prepare(kPreviewSr);
    }
    preview_->restart();
    const double rt = p.freeze ? 3.0 : p.decay * std::max(1.0f, std::max(p.lowMul, p.highMul));
    const double secs = std::clamp(rt * 1.15 + 0.1, 0.4, 12.0);
    const size_t frames = static_cast<size_t>(secs * kPreviewSr);
    std::vector<std::vector<float>> out(2, std::vector<float>(frames, 0.0f));
    float inL[256] = {}, inR[256] = {};
    for (size_t off = 0; off < frames; off += 256) {
        const int n = static_cast<int>(std::min<size_t>(256, frames - off));
        inL[0] = inR[0] = off == 0 ? 1.0f : 0.0f;
        preview_->process(inL, inR, &out[0][off], &out[1][off], n, p);
    }
    auto env = envelope(out, {0, 1});
    std::lock_guard<std::mutex> lk(srcMu_);
    algoEnv_ = std::move(env);
    algoEnvSec_ = static_cast<float>(secs);
    previewGen_.fetch_add(1, std::memory_order_relaxed);
}

Chamber::Sig Chamber::signature() const {
    auto q = [](float v) { return static_cast<int>(std::lround(v * 4000.0f)); };
    Sig s;
    s.ir = irIndex();
    s.start = q(get(IrStart));
    s.end = q(get(IrDecay));
    s.attack = q(get(IrAttack));
    s.size = q(get(IrSize));
    s.rev = get(IrReverse) >= 0.5f;
    s.ts = get(IrTrueStereo) >= 0.5f;
    s.zl = get(ZeroLatency) >= 0.5f;
    s.q = qualityIndex();
    s.sr = static_cast<int>(std::lround(srA_.load(std::memory_order_relaxed)));
    s.gen = userGen_.load(std::memory_order_relaxed);
    return s;
}

std::unique_ptr<chamber::ConvKernel> Chamber::makeKernel(const Sig& s) {
    using chamber::ConvKernel;
    const double sr = static_cast<double>(s.sr);
    std::shared_ptr<const chamber::SourceIr> src;
    if (s.ir < chamber::kNumBuiltinIrs) src = chamber::builtinIr(s.ir);
    else {
        std::lock_guard<std::mutex> lk(srcMu_);
        src = userIr_;
    }
    {
        std::lock_guard<std::mutex> lk(srcMu_);
        shownSrc_ = src;
    }
    std::vector<int> chans;
    std::vector<ConvKernel::Path> paths;
    int nIn = 2;
    bool mono = false;
    const int nc = src ? src->channels : 0;
    if (nc == 1) {
        chans = {0};
        paths = {{0, 0, 0}, {1, 1, 0}};
    } else if (nc == 2) {
        chans = {0, 1};
        if (s.ts) {
            nIn = 1;
            mono = true;
            paths = {{0, 0, 0}, {0, 1, 1}};
        } else
            paths = {{0, 0, 0}, {1, 1, 1}};
    } else if (nc >= 4) {
        if (s.ts) {
            chans = {0, 1, 2, 3};
            paths = {{0, 0, 0}, {0, 1, 1}, {1, 0, 2}, {1, 1, 3}};
        } else {
            chans = {0, 3};
            paths = {{0, 0, 0}, {1, 1, 1}};
        }
    }
    chamber::Shape sh;
    sh.start = static_cast<float>(s.start) / 4000.0f;
    sh.end = static_cast<float>(s.end) / 4000.0f;
    sh.attackMs = 500.0f * (static_cast<float>(s.attack) / 4000.0f) * (static_cast<float>(s.attack) / 4000.0f);
    sh.size = static_cast<float>(chamber::expMap(s.size / 4000.0, 0.5, 2.0));
    sh.reverse = s.rev != 0;
    static const double maxSec[3] = {3.0, 6.0, 10.0};
    std::vector<std::vector<float>> h;
    if (src && nc > 0) h = chamber::shapeIr(*src, chans, sh, sr, maxSec[std::clamp(s.q, 0, 2)]);
    { // the processed response, as the card's RESULT lane (direct paths only)
        std::vector<int> use;
        if (!h.empty()) {
            use.push_back(0);
            if (h.size() > 1) use.push_back(h.size() == 4 ? 3 : 1);
        }
        auto env = envelope(h, use);
        std::lock_guard<std::mutex> lk(srcMu_);
        convEnv_ = h.empty() ? std::vector<float>{} : std::move(env);
        convEnvSec_ = h.empty() ? 0.0f : static_cast<float>(static_cast<double>(h[0].size()) / sr);
        previewGen_.fetch_add(1, std::memory_order_relaxed);
    }
    return ConvKernel::build(h, paths, nIn, mono, s.zl != 0, bigBlock(sr), sr);
}

// Build a kernel for the current params when they changed. `direct` installs it as the
// current kernel (only while process() can't run); otherwise it's handed to the audio thread.
bool Chamber::rebuild(bool direct) {
    std::lock_guard<std::mutex> lk(buildMu_);
    const Sig s = signature();
    if (!direct && built_ && s == builtSig_) {
        pending_.store(false, std::memory_order_relaxed);
        return false;
    }
    pending_.store(true, std::memory_order_relaxed);
    auto k = makeKernel(s);
    builtSig_ = s;
    built_ = true;
    if (direct) {
        cur_ = std::move(k);
    } else {
        std::unique_ptr<chamber::ConvKernel> unclaimed(ready_.exchange(k.release(), std::memory_order_acq_rel));
    }
    pending_.store(false, std::memory_order_relaxed);
    return true;
}

void Chamber::startWorker() {
    if (worker_.joinable()) return;
    stop_.store(false);
    worker_ = std::thread([this] { workerLoop(); });
}

void Chamber::stopWorker() {
    if (!worker_.joinable()) return;
    {
        std::lock_guard<std::mutex> lk(wMu_);
        stop_.store(true);
    }
    wCv_.notify_all();
    worker_.join();
}

void Chamber::workerLoop() {
    using clock = std::chrono::steady_clock;
    auto last = clock::now() - std::chrono::seconds(1), lastPreview = last;
    while (!stop_.load()) {
        {
            std::unique_lock<std::mutex> lk(wMu_);
            wCv_.wait_for(lk, std::chrono::milliseconds(10), [this] { return stop_.load(); });
        }
        if (stop_.load()) break;
        drainRetired();
        if (clock::now() - lastPreview >= std::chrono::milliseconds(40)) {
            auto ps = previewSignature();
            if (ps != previewSig_) {
                previewSig_ = std::move(ps);
                renderAlgoPreview();
                lastPreview = clock::now();
            }
        }
        if (clock::now() - last < std::chrono::milliseconds(25)) continue; // coalesce drags
        bool changed;
        {
            std::lock_guard<std::mutex> lk(buildMu_);
            changed = !built_ || signature() != builtSig_;
        }
        if (changed && rebuild(false)) last = clock::now();
    }
}

void Chamber::drainRetired() {
    uint32_t t = rTail_.load(std::memory_order_relaxed);
    const uint32_t h = rHead_.load(std::memory_order_acquire);
    while (t != h) {
        ring_[t % kRing].reset();
        ++t;
    }
    rTail_.store(t, std::memory_order_release);
}

} // namespace nota
