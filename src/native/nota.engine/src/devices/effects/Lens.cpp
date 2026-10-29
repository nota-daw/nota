// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Lens implementation.

#include "Lens.h"

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace nota {

Lens::Lens() {
    p_[View].store(0.0f);
    p_[Freeze].store(0.0f);
    p_[Source].store(0.0f);
    p_[MidSide].store(0.0f);
    p_[FftSize].store(0.667f); // 4096
    p_[Window].store(0.0f);    // Hann
    p_[Average].store(0.2f);   // ~4 frames
    p_[Smooth].store(0.35f);
    p_[Decay].store(0.45f); // ~0.8 s
    p_[Tilt].store(0.0f);
    p_[Floor].store(0.3f); // −96 dB
    p_[PeakHold].store(1.0f);
    p_[PeakTime].store(0.45f);    // ~4 s
    p_[ScaleTop].store(0.5f);     // −6 dB
    p_[ScaleRange].store(0.667f); // 90 dB
    p_[TimeDiv].store(0.667f);    // ~2 ms/div
    p_[VoltDiv].store(0.7f);      // ~0.5/div
    p_[TrigMode].store(0.0f);     // Auto
    p_[TrigEdge].store(0.0f);     // rising
    p_[TrigLevel].store(0.59f);   // +0.18
    p_[Holdoff].store(0.06f);     // 12 ms
    p_[Persist].store(1.0f);
    p_[PersistTime].store(0.58f); // ~420 ms
    p_[Traces].store(0.286f);     // 3
    p_[Bright].store(0.66f);
    p_[Cursors].store(0.0f);
    p_[CursorA].store(0.25f);
    p_[CursorB].store(0.375f);
    p_[CursorSnap].store(0.0f);
    p_[WfSpeed].store(0.5f);   // 12 s
    p_[WfGain].store(0.625f);  // +6 dB
    p_[WfFloor].store(0.375f); // −90 dB
    p_[WfContrast].store(0.7f);
    p_[WfOverlap].store(0.667f); // 75 %
    p_[LogFreq].store(1.0f);
    p_[NoteGrid].store(0.0f);
    p_[Rate].store(0.4f); // 30 fps
    p_[Sync].store(0.0f);
    p_[Span].store(4.0f / 6.0f); // 1 bar
    p_[KeyShow].store(1.0f);
    p_[KeyGain].store(0.5f); // 0 dB
    p_[Layout].store(0.0f);  // Overlay
    setSampleRate(44100.0, 0);
}

void Lens::setSampleRate(double sr, int32_t maxBlock) {
    std::lock_guard<std::mutex> lock(mx_);
    sr_ = sr > 0 ? sr : 44100.0;
    ringL_.assign(kRing, 0.0f);
    ringR_.assign(kRing, 0.0f);
    ringKL_.assign(kRing, 0.0f);
    ringKR_.assign(kRing, 0.0f);
    ringW_.store(0, std::memory_order_relaxed);
    bins_.assign(kBins, Bin{});
    binW_.store(0, std::memory_order_relaxed);
    binLen_ = std::max(16, (int)std::lround(sr_ / 1500.0));
    binN_ = 0;
    pend_.assign((size_t)std::max(maxBlock, 8192) * 2, 0.0f);
    pendN_ = 0;
    keyTrace_.assign(kTrace, 0.0f);
    sync_.assign(kSyncHead + kSync * kSyncCol, 0.0f);
    curve_.assign(kCurve, -200.0f);
    peak_.assign(kCurve, -200.0f);
    peakAge_.assign(kCurve, 0.0f);
    avg_.assign(kMaxFft / 2, -200.0f);
    binDb_.assign(kMaxFft / 2, -200.0f);
    trace_.assign(kTrace, 0.0f);
    rawWin_.assign(kMaxWin, 0.0f);
    rawLen_ = 0;
    wfRow_.assign(kWfBins, 0.0f);
    fftN_ = 0;
    lastAnalyzeS_ = -1.0;
    trigPos_ = 0;
    trigOk_ = false;
    armed_ = true;
    singleDone_ = false;
    kInit();
    lufsBlock_ = std::max(64, (int)std::lround(sr_ * 0.008)); // 8 ms sub-blocks
    lufsRing_.assign(kLufsBlocks, 0.0);
    lufsIdx_ = 0;
    lufsAcc_ = 0.0;
    lufsN_ = 0;
}

// Analyzer: the signal passes through untouched.
void Lens::capture(const float* main, const float* key, int32_t keyFrames, int32_t frames, double beat0, double spb,
    bool playing, bool hist) {
    const bool ms = msOn();
    const int src = srcIdx();
    auto sel = [ms, src](float l, float r) {
        if (ms) return src == 2 ? 0.5f * (l - r) : 0.5f * (l + r);
        return src == 1 ? l : src == 2 ? r : 0.5f * (l + r);
    };
    const uint8_t flags = (uint8_t)((playing && spb > 0.0 ? kBinPlaying : 0) | (key ? kBinKey : 0) | chanBits(ms, src));
    uint32_t w = ringW_.load(std::memory_order_relaxed);
    for (int32_t i = 0; i < frames; ++i) {
        const float l = main[i * 2], r = main[i * 2 + 1];
        const bool haveKey = key && i < keyFrames;
        const float kl = haveKey ? key[i * 2] : 0.0f, kr = haveKey ? key[i * 2 + 1] : 0.0f;
        ringL_[w & (kRing - 1)] = l;
        ringR_[w & (kRing - 1)] = r;
        ringKL_[w & (kRing - 1)] = kl;
        ringKR_[w & (kRing - 1)] = kr;
        ++w;
        // The beat-stamped min/max history.
        if (!hist) continue;
        if (binN_ == 0) {
            acc_.beat = spb > 0.0 ? beat0 + (playing ? i / spb : 0.0) : beat0;
            acc_.spb = spb;
            acc_.sample = w - 1;
            acc_.flags = flags;
            acc_.mLo = acc_.kLo = 1e9f;
            acc_.mHi = acc_.kHi = -1e9f;
        }
        const float a = sel(l, r), k = sel(kl, kr);
        acc_.mLo = std::min(acc_.mLo, a);
        acc_.mHi = std::max(acc_.mHi, a);
        acc_.kLo = std::min(acc_.kLo, k);
        acc_.kHi = std::max(acc_.kHi, k);
        if (++binN_ >= binLen_) {
            const uint32_t bw = binW_.load(std::memory_order_relaxed);
            bins_[bw & (kBins - 1)] = acc_;
            binW_.store(bw + 1, std::memory_order_release);
            binN_ = 0;
        }
    }
    ringW_.store(w, std::memory_order_release);
}

void Lens::process(float* buf, int32_t frames) {
    const bool frozen = get(Freeze) >= 0.5f;
    // The key (specs/050): the engine hands it over before each process(), one block late.
    const float* key = scBuf_ && scFrames_ > 0 ? scBuf_ : nullptr;
    const int32_t keyN = scFrames_;
    scBuf_ = nullptr;
    const bool keyed = scTrackId_.load(std::memory_order_relaxed) >= 0;
    const bool sync = get(Sync) >= 0.5f;
    // A key routed from now: no earlier key in its ring. Sync turned on: a fresh bin.
    if (keyed && !keyedPrev_) {
        std::fill(ringKL_.begin(), ringKL_.end(), 0.0f);
        std::fill(ringKR_.begin(), ringKR_.end(), 0.0f);
    }
    if (sync && !syncPrev_) binN_ = 0;
    keyedPrev_ = keyed;
    syncPrev_ = sync;
    // The plain scope (no key, no Sync) only fills the main ring, in the loop below.
    const bool lean = !frozen && !keyed && !sync;
    const bool delay = keyed && (size_t)frames * 2 <= pend_.size();
    if (frozen || lean)
        pendN_ = 0;
    else if (delay) {
        // The bus holds the source's previous block: as long as the held one, whatever this one is.
        if (pendN_ > 0) capture(pend_.data(), key, key ? pendN_ : 0, pendN_, pendBeat_, pendSpb_, pendPlaying_, sync);
        std::copy(buf, buf + (size_t)frames * 2, pend_.begin());
        pendN_ = frames;
        pendBeat_ = beat0_;
        pendSpb_ = spb_;
        pendPlaying_ = playing_;
    } else {
        pendN_ = 0;
        capture(buf, key, keyN, frames, beat0_, spb_, playing_, sync);
    }

    double corrLR = 0, corrLL = 0, corrRR = 0;
    uint32_t w = ringW_.load(std::memory_order_relaxed);
    for (int32_t i = 0; i < frames; ++i) {
        const float l = buf[i * 2], r = buf[i * 2 + 1];
        if (lean) {
            ringL_[w & (kRing - 1)] = l;
            ringR_[w & (kRing - 1)] = r;
            ++w;
        }
        corrLR += (double)l * r;
        corrLL += (double)l * l;
        corrRR += (double)r * r;
        // K-weighted loudness (BS.1770): shelf then high-pass, per channel.
        const float kl = kFilter(l, ks_[0]), kr = kFilter(r, ks_[1]);
        lufsAcc_ += (double)kl * kl + (double)kr * kr;
        if (++lufsN_ >= lufsBlock_) {
            lufsRing_[lufsIdx_] = lufsAcc_ / (lufsN_ * 2);
            lufsIdx_ = (lufsIdx_ + 1) % kLufsBlocks;
            lufsAcc_ = 0.0;
            lufsN_ = 0;
            double sum = 0;
            for (double e : lufsRing_) sum += e;
            const double msq = sum / kLufsBlocks;
            lufs_.store(msq > 1e-12 ? (float)(-0.691 + 10.0 * std::log10(msq)) : -120.0f, std::memory_order_relaxed);
        }
    }
    if (lean) ringW_.store(w, std::memory_order_release);
    if (corrLL > 1e-12 && corrRR > 1e-12) {
        const float c = (float)(corrLR / std::sqrt(corrLL * corrRR));
        const float prev = corr_.load(std::memory_order_relaxed);
        corr_.store(prev + 0.2f * (c - prev), std::memory_order_relaxed);
    }
    const uint32_t seq = frameSeq_.fetch_add(1, std::memory_order_relaxed) + 1;
    if (key) keySeen_.store(seq, std::memory_order_relaxed);
}

int32_t Lens::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples < kScope) return 0;
    std::lock_guard<std::mutex> lock(mx_);
    ensureAnalyzed();
    out[M_PeakHz] = mPeakHz_;
    out[M_PeakDb] = mPeakDb_;
    out[M_RmsDb] = mRmsDb_;
    out[M_LevelDb] = mLevelDb_;
    out[M_CrestDb] = mLevelDb_ - mRmsDb_;
    out[M_Corr] = corr_.load(std::memory_order_relaxed);
    out[M_Lufs] = lufs_.load(std::memory_order_relaxed);
    out[M_TrigOk] = trigOk_ ? 1.0f : 0.0f;
    out[M_PeriodMs] = mPeriodMs_;
    out[M_FreqHz] = mPeriodMs_ > 1e-6f ? 1000.0f / mPeriodMs_ : 0.0f;
    out[M_Vpp] = mVpp_;
    out[M_Vrms] = mVrms_;
    out[M_WindowMs] = (float)(windowSamples() * 1000.0 / sr_);
    out[M_BinHz] = fftN_ > 0 ? (float)(sr_ / fftN_) : 0.0f;
    out[M_FftN] = (float)fftN_;
    out[M_FftMs] = fftN_ > 0 ? (float)(fftN_ * 1000.0 / sr_) : 0.0f;
    out[M_CursorAv] = traceAt(get(CursorA));
    out[M_CursorBv] = traceAt(get(CursorB));
    out[M_SampleRate] = (float)sr_;
    out[M_Held] = (get(Freeze) >= 0.5f || (trigModeIdx() == 2 && singleDone_)) ? 1.0f : 0.0f;
    out[M_KeyLive] = keyLive() ? 1.0f : 0.0f;
    out[M_Instance] = (float)instance_;
    return kScope;
}

int32_t Lens::layerWave(int32_t layer, float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    std::lock_guard<std::mutex> lock(mx_);
    ensureAnalyzed();
    switch (layer) {
    case L_Spectrum: return copyOut(curve_.data(), kCurve, out, maxSamples);
    case L_Peak: return copyOut(peak_.data(), kCurve, out, maxSamples);
    case L_Trace: return copyOut(trace_.data(), kTrace, out, maxSamples);
    case L_Raw: return copyOut(rawWin_.data(), rawLen_, out, maxSamples);
    case L_Waterfall: return copyOut(wfRow_.data(), kWfBins, out, maxSamples);
    case L_KeyTrace: return copyOut(keyTrace_.data(), kTrace, out, maxSamples);
    case L_Sync: return copyOut(sync_.data(), kSyncHead + kSync * kSyncCol, out, maxSamples);
    default: return 0;
    }
}

void Lens::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    std::lock_guard<std::mutex> lock(mx_);
    switch (id) {
    case A_Rearm:
        armed_ = true;
        singleDone_ = false;
        break;
    case A_ResetPeak:
        for (int i = 0; i < kCurve; ++i) {
            peak_[i] = -200.0f;
            peakAge_[i] = 0.0f;
        }
        break;
    case A_ResetTrigger:
        trigPos_ = 0;
        trigOk_ = false;
        break;
    default: break;
    }
}

// Text reports, for MCP (get_device_text) and the smoke test.
std::string Lens::deviceText(int32_t id) const {
    std::lock_guard<std::mutex> lock(mx_);
    ensureAnalyzed();
    char b[256];
    switch (id) {
    case 0: { // one-line summary
        std::snprintf(b, sizeof b,
            "%s · %s · peak %.1f Hz %.1f dB · rms %.1f dB · crest %.1f dB · LUFS %.1f · corr %+.2f", viewName(),
            sourceName(), mPeakHz_, mPeakDb_, mRmsDb_, mLevelDb_ - mRmsDb_, lufs_.load(std::memory_order_relaxed),
            corr_.load(std::memory_order_relaxed));
        return b;
    }
    case 1: { // third-octave band table
        std::string s;
        for (int i = 0; i < kBands; i++) {
            std::snprintf(b, sizeof b, "%.0f Hz\t%.1f dB\n", kBandHz[i], bandDb(kBandHz[i]));
            s += b;
        }
        return s;
    }
    case 2: { // scope measurements
        const double winMs = windowSamples() * 1000.0 / sr_;
        std::snprintf(b, sizeof b, "trigger %s · window %.2f ms · period %.3f ms (%.1f Hz) · Vpp %.3f · Vrms %.3f",
            trigOk_ ? "locked" : "free-run", winMs, mPeriodMs_, mPeriodMs_ > 1e-6f ? 1000.0f / mPeriodMs_ : 0.0f, mVpp_,
            mVrms_);
        std::string s = b;
        // Tempo sync and the key (specs/050).
        static const char* spans[7] = {"1/4 beat", "1/2 beat", "1 beat", "2 beats", "1 bar", "2 bars", "4 bars"};
        std::snprintf(b, sizeof b, " · sync %s (%s, %s) · key %s%s, %+.1f dB, %s", get(Sync) >= 0.5f ? "on" : "off",
            spans[step(get(Span), 7)], sync_[S_Mode] > 0.5f ? "on the beat grid" : "free",
            scTrackId_.load(std::memory_order_relaxed) >= 0 ? (keyLive() ? "arriving" : "routed, silent") : "none",
            get(KeyShow) >= 0.5f ? "" : " (hidden)", keyGainDb(get(KeyGain)), get(Layout) >= 0.5f ? "stacked" : "overlay");
        return s + b;
    }
    case 3: { // strongest spectral peaks, with note names
        std::string s;
        for (auto& pk : topPeaks(8)) {
            std::snprintf(b, sizeof b, "%.1f Hz\t%.1f dB\t%s\n", pk.hz, pk.db, noteName(pk.hz).c_str());
            s += b;
        }
        return s.empty() ? "(no peaks above the floor)" : s;
    }
    case 4: { // cursor measurements
        const double winSec = windowSamples() / sr_;
        const double a = get(CursorA) * winSec, bb = get(CursorB) * winSec;
        const double dt = std::fabs(bb - a);
        std::snprintf(b, sizeof b, "A %.3f ms · B %.3f ms · dt %.3f ms (%.1f Hz) · dV %.3f", a * 1000, bb * 1000,
            dt * 1000, dt > 1e-9 ? 1.0 / dt : 0.0, traceAt(get(CursorB)) - traceAt(get(CursorA)));
        return b;
    }
    default: return {};
    }
}

const char* Lens::paramName(int32_t i) const {
    static const char* nm[] = {
        "View",
        "Freeze",
        "Source",
        "Mid/Side",
        "Spectrum FFT",
        "Spectrum Window",
        "Spectrum Average",
        "Spectrum Smooth",
        "Spectrum Decay",
        "Spectrum Tilt",
        "Spectrum Floor",
        "Spectrum Peak Hold",
        "Spectrum Peak Time",
        "Scale Top",
        "Scale Range",
        "Scope Time/Div",
        "Scope Volt/Div",
        "Scope Trigger",
        "Scope Edge",
        "Scope Level",
        "Scope Holdoff",
        "Scope Persist",
        "Scope Persist Time",
        "Scope Traces",
        "Scope Bright",
        "Cursor On",
        "Cursor A",
        "Cursor B",
        "Cursor Snap",
        "Waterfall Speed",
        "Waterfall Gain",
        "Waterfall Floor",
        "Waterfall Contrast",
        "Waterfall Overlap",
        "Display Log Freq",
        "Display Note Grid",
        "Display Rate",
        "Scope Sync",
        "Scope Span",
        "Scope Key",
        "Scope Key Gain",
        "Scope Layout",
    };
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float Lens::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Lens::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
}

void Lens::kInit() {
    { // stage 1: high shelf, +4 dB at 1681.97 Hz
        const double f0 = 1681.974450955533, Q = 0.7071752369554196, G = 3.999843853973347;
        const double K = std::tan(kPi * f0 / sr_), Vh = std::pow(10.0, G / 20.0);
        const double Vb = std::pow(Vh, 0.4996667741545416);
        const double a0 = 1 + K / Q + K * K;
        kb_[0] = {(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0,
            2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0};
    }
    { // stage 2: high pass at 38.14 Hz
        const double f0 = 38.13547087602444, Q = 0.5003270373238773;
        const double K = std::tan(kPi * f0 / sr_);
        const double a0 = 1 + K / Q + K * K;
        kb_[1] = {1.0 / a0, -2.0 / a0, 1.0 / a0, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0};
    }
    ks_[0] = ks_[1] = {};
}

float Lens::kFilter(float x, KState& st) const {
    double y = x;
    for (int s = 0; s < 2; ++s) {
        const Biquad& c = kb_[s];
        const double o = c.b0 * y + st.z1[s];
        st.z1[s] = c.b1 * y - c.a1 * o + st.z2[s];
        st.z2[s] = c.b2 * y - c.a2 * o;
        y = o;
    }
    return (float)y;
}

int Lens::windowSamples() const {
    return std::clamp((int)std::lround(timeDivSec(get(TimeDiv)) * 8.0 * sr_), 16, kMaxWin);
}

float Lens::traceAt(float pos) const {
    const double x = std::clamp((double)pos, 0.0, 1.0) * (kTrace - 1);
    const int i = std::min((int)x, kTrace - 2);
    const double f = x - i;
    return (float)(trace_[i] * (1 - f) + trace_[i + 1] * f);
}

// Called under mx_ from every read entry point; recomputes at most once per
// 1/Rate seconds and only when the audio thread has produced a new block.
void Lens::ensureAnalyzed() const {
    const double now = nowSeconds();
    const double period = 1.0 / std::clamp(10.0 + get(Rate) * 50.0, 10.0, 60.0);
    const uint32_t seq = frameSeq_.load(std::memory_order_relaxed);
    if (lastAnalyzeS_ >= 0 && now - lastAnalyzeS_ < period && seq == lastSeq_) return;
    const double dt = lastAnalyzeS_ < 0 ? period : std::min(now - lastAnalyzeS_, 0.5);
    lastAnalyzeS_ = now;
    lastSeq_ = seq;

    analyzeSpectrum(dt);
    analyzeScope();
    analyzeSync();
}

void Lens::analyzeSpectrum(double dt) const {
    const int n = fftSizeOf(get(FftSize));
    if (n != fftN_) {
        fft_.resize((size_t)n);
        freq_.assign((size_t)n / 2, {});
        time_.assign((size_t)n, 0.0f);
        win_.assign((size_t)n, 0.0f);
        std::fill(avg_.begin(), avg_.end(), -200.0f); // bins mean new frequencies now
        fftN_ = n;
        winKind_ = -1;
    }
    const int wk = step(get(Window), 3);
    if (wk != winKind_) {
        buildWindow(wk, n);
        winKind_ = wk;
    }

    const uint32_t head = ringW_.load(std::memory_order_relaxed);
    const uint32_t start = head - (uint32_t)n;
    const bool ms = msOn();
    const int src = srcIdx();
    double sumSq = 0;
    float peakLin = 0;
    for (int i = 0; i < n; ++i) {
        const float s = pick(start + (uint32_t)i, ms, src);
        sumSq += (double)s * s;
        peakLin = std::max(peakLin, std::fabs(s));
        time_[i] = s * win_[i];
    }
    mRmsDb_ = (float)db(std::sqrt(sumSq / n));
    mLevelDb_ = (float)db(peakLin);

    fft_.fft(time_.data(), freq_.data());
    const int bins = n / 2;
    const double ref = winSum_ * 0.5; // full-scale sine → 0 dBFS
    const double alpha = 1.0 / std::max(1.0, std::round(1.0 + get(Average) * 15.0));
    for (int k = 1; k < bins; ++k) {
        const double mag = std::abs(freq_[(size_t)k]);
        const double d = db(mag / std::max(1e-12, ref));
        float& a = avg_[(size_t)k];
        if (a < -190.0f) a = (float)d;
        else
            a = (float)(a + alpha * (d - a));
        binDb_[(size_t)k] = a;
    }
    binDb_[0] = binDb_[1];

    // Neighbour smoothing: a widening box over the log axis reads as the
    // classic "1/6 octave" smoothing without a second FFT.
    // Averaged in the POWER domain, not in dB: a dB-domain box drags a narrow peak
    // several dB down and the readout stops matching the signal.
    const int sm = (int)std::lround(get(Smooth) * 6.0);
    if (sm > 0) {
        smTmp_.resize((size_t)bins);
        for (int k = 0; k < bins; ++k) smTmp_[(size_t)k] = (float)std::pow(10.0, binDb_[(size_t)k] * 0.1);
        for (int k = 0; k < bins; ++k) {
            const int w = 1 + (int)(sm * (0.2 + 2.0 * k / (double)bins));
            const int j0 = std::max(0, k - w), j1 = std::min(bins - 1, k + w);
            double run = 0;
            for (int j = j0; j <= j1; ++j) run += smTmp_[(size_t)j];
            binDb_[(size_t)k] = (float)(10.0 * std::log10(std::max(1e-20, run / (j1 - j0 + 1))));
        }
    }

    // Map the bins onto the log-frequency curve, applying tilt and floor,
    // then apply the fall ballistic and the peak hold.
    const double fLo = 20.0, fHi = std::min(20000.0, sr_ * 0.45);
    const double tilt = get(Tilt) * 9.0;
    const double floorDb = -120.0 + get(Floor) * 80.0;
    const double fallDb = dt / std::max(0.01, expMap(get(Decay), 0.05, 8.0)) * 90.0;
    const double holdS = expMap(get(PeakTime), 0.5, 30.0);
    const bool peakOn = get(PeakHold) >= 0.5f;
    double best = -400, bestHz = 0;
    for (int i = 0; i < kCurve; ++i) {
        const double f = fLo * std::pow(fHi / fLo, i / (double)(kCurve - 1));
        const double f0 = i == 0 ? f : std::sqrt(f * fLo * std::pow(fHi / fLo, (i - 1) / (double)(kCurve - 1)));
        const double f1 =
            i == kCurve - 1 ? f : std::sqrt(f * fLo * std::pow(fHi / fLo, (i + 1) / (double)(kCurve - 1)));
        const double binHz = sr_ / n;
        const int k0 = std::clamp((int)std::floor(f0 / binHz), 1, bins - 1);
        const int k1 = std::clamp((int)std::ceil(f1 / binHz), 1, bins - 1);
        double v;
        if (k1 <= k0) {
            const double x = std::clamp(f / binHz, 1.0, bins - 1.0);
            const int ka = (int)x;
            const double fr = x - ka;
            v = binDb_[(size_t)ka] * (1 - fr) + binDb_[(size_t)std::min(ka + 1, bins - 1)] * fr;
        } else {
            v = -400;
            for (int k = k0; k <= k1; ++k) v = std::max(v, (double)binDb_[(size_t)k]);
        }
        v += tilt * std::log2(f / 1000.0);
        v = std::max(v, floorDb);
        if (v > best) {
            best = v;
            bestHz = f;
        }

        float& c = curve_[(size_t)i];
        c = v >= c ? (float)v : (float)std::max(v, c - fallDb);
        if (peakOn) {
            if (c >= peak_[(size_t)i]) {
                peak_[(size_t)i] = c;
                peakAge_[(size_t)i] = 0.0f;
            } else {
                peakAge_[(size_t)i] += (float)dt;
                if (peakAge_[(size_t)i] > holdS)
                    peak_[(size_t)i] = (float)std::max((double)c, peak_[(size_t)i] - fallDb * 0.5);
            }
        } else {
            peak_[(size_t)i] = c;
            peakAge_[(size_t)i] = 0.0f;
        }
    }
    mPeakHz_ = (float)bestHz;
    mPeakDb_ = (float)best;

    // Waterfall row: the same curve, resampled and normalized to 0..1.
    const double wfGain = -24.0 + get(WfGain) * 48.0;
    const double wfFloor = -120.0 + get(WfFloor) * 80.0;
    const double wfTop = std::max(wfFloor + 6.0, -6.0 + wfGain);
    const double contrast = 0.5 + get(WfContrast) * 1.5;
    for (int i = 0; i < kWfBins; ++i) {
        const double x = i * (kCurve - 1) / (double)(kWfBins - 1);
        const int a = (int)x;
        const double fr = x - a;
        const double v = curve_[(size_t)a] * (1 - fr) + curve_[(size_t)std::min(a + 1, kCurve - 1)] * fr + wfGain;
        wfRow_[(size_t)i] = (float)std::clamp(
            std::pow(std::clamp((v - wfFloor) / (wfTop - wfFloor), 0.0, 1.0), 1.0 / contrast), 0.0, 1.0);
    }
}

void Lens::buildWindow(int kind, int n) const {
    double sum = 0;
    for (int i = 0; i < n; ++i) {
        const double x = 2 * kPi * i / (n - 1);
        double w;
        switch (kind) {
        case 1: // Blackman-Harris
            w = 0.35875 - 0.48829 * std::cos(x) + 0.14128 * std::cos(2 * x) - 0.01168 * std::cos(3 * x);
            break;
        case 2: // Flat top
            w = 0.21557895 - 0.41663158 * std::cos(x) + 0.277263158 * std::cos(2 * x) - 0.083578947 * std::cos(3 * x) +
                0.006947368 * std::cos(4 * x);
            break;
        default: // Hann
            w = 0.5 - 0.5 * std::cos(x);
            break;
        }
        win_[(size_t)i] = (float)w;
        sum += w;
    }
    winSum_ = sum;
}

// Trigger search + trace capture. The trace is pinned to the trigger point
// (25 % pre-trigger, like the mockup's marker) so it stands still.
void Lens::analyzeScope() const {
    const int win = windowSamples();
    const int pre = win / 4;
    const uint32_t head = ringW_.load(std::memory_order_relaxed);
    const bool frozen = get(Freeze) >= 0.5f;
    const int mode = trigModeIdx();                   // 0 Auto · 1 Normal · 2 Single
    if (frozen || (mode == 2 && singleDone_)) return; // hold the captured trace

    const float level = (get(TrigLevel) - 0.5f) * 2.0f;
    const bool rising = get(TrigEdge) < 0.5f;
    const int holdoff = (int)std::lround(get(Holdoff) * 0.2 * sr_);
    const bool ms = msOn();
    const int src = srcIdx();

    // The newest sample index whose window is fully inside the ring.
    const uint32_t newest = head - (uint32_t)(win - pre) - 1;
    const uint32_t oldest = head - (uint32_t)(kRing - 8);
    const uint32_t limit = newest - (uint32_t)std::min(kRing - win - 8, 4 * win + 2048);

    bool found = false;
    uint32_t at = 0;
    for (uint32_t t = newest; (int32_t)(t - limit) > 0 && (int32_t)(t - oldest) > 0; --t) {
        if (holdoff > 0 && trigOk_ && (int32_t)(t - trigPos_) < holdoff && (int32_t)(t - trigPos_) > -holdoff) continue;
        const float a = pick(t - 1, ms, src), b = pick(t, ms, src);
        if (rising ? (a < level && b >= level) : (a > level && b <= level)) {
            found = true;
            at = t;
            break;
        }
    }

    if (!found) {
        if (mode == 0) {
            at = newest;
            trigOk_ = false;
        } // Auto: free-run
        else
            return; // Normal / Single: keep the last trace
    } else {
        trigOk_ = true;
        trigPos_ = at;
        if (mode == 2) {
            if (!armed_) return;
            armed_ = false;
            singleDone_ = true;
        }
    }

    // Raw window (for export) + the decimated trace, peak-preserving so a
    // transient survives a 400 ms window squeezed into 512 points.
    const uint32_t s0 = at - (uint32_t)pre;
    rawLen_ = std::min(win, kMaxWin);
    for (int i = 0; i < rawLen_; ++i) rawWin_[(size_t)i] = pick(s0 + (uint32_t)i, ms, src);

    double sumSq = 0;
    float lo = 1e9f, hi = -1e9f;
    for (int i = 0; i < kTrace; ++i) {
        const int a = (int)((int64_t)i * win / kTrace);
        const int b = std::max(a + 1, (int)((int64_t)(i + 1) * win / kTrace));
        float best = 0;
        for (int j = a; j < b && j < win; ++j) {
            const float v = rawWin_[(size_t)std::min(j, rawLen_ - 1)];
            if (std::fabs(v) > std::fabs(best)) best = v;
        }
        trace_[(size_t)i] = best;
        // The key over the same window, so the two line up sample for sample (specs/050).
        float kb = 0;
        for (int j = a; j < b && j < win; ++j) {
            const float v = pickKey(s0 + (uint32_t)j, ms, src);
            if (std::fabs(v) > std::fabs(kb)) kb = v;
        }
        keyTrace_[(size_t)i] = kb;
    }
    for (int i = 0; i < rawLen_; ++i) {
        const float v = rawWin_[(size_t)i];
        sumSq += (double)v * v;
        lo = std::min(lo, v);
        hi = std::max(hi, v);
    }
    mVpp_ = rawLen_ > 0 ? hi - lo : 0.0f;
    mVrms_ = rawLen_ > 0 ? (float)std::sqrt(sumSq / rawLen_) : 0.0f;
    mPeriodMs_ = (float)(measurePeriod() * 1000.0);
}

// Period by autocorrelation over the captured window (0 when nothing locks).
double Lens::measurePeriod() const {
    const int n = rawLen_;
    if (n < 32) return 0.0;
    const int maxLag = std::min(n / 2, (int)(sr_ / 20.0));
    const int minLag = std::max(2, (int)(sr_ / 20000.0));
    double e0 = 0;
    for (int i = 0; i < n; ++i) e0 += (double)rawWin_[(size_t)i] * rawWin_[(size_t)i];
    if (e0 < 1e-9) return 0.0;
    double bestV = 0;
    int bestLag = 0;
    bool rose = false;
    for (int lag = minLag; lag < maxLag; ++lag) {
        double s = 0;
        for (int i = 0; i + lag < n; ++i) s += (double)rawWin_[(size_t)i] * rawWin_[(size_t)(i + lag)];
        const double norm = s / e0;
        if (!rose) {
            if (norm < 0.2) rose = true;
            continue;
        } // skip the lag-0 lobe
        if (norm > bestV) {
            bestV = norm;
            bestLag = lag;
        }
    }
    return bestV > 0.35 && bestLag > 0 ? bestLag / sr_ : 0.0;
}

double Lens::bandDb(double hz) const {
    const double fLo = 20.0, fHi = std::min(20000.0, sr_ * 0.45);
    if (hz <= fLo) return curve_[0];
    if (hz >= fHi) return curve_[kCurve - 1];
    const double x = std::log(hz / fLo) / std::log(fHi / fLo) * (kCurve - 1);
    const int i = std::min((int)x, kCurve - 2);
    const double f = x - i;
    return curve_[(size_t)i] * (1 - f) + curve_[(size_t)(i + 1)] * f;
}

std::vector<Lens::Peak> Lens::topPeaks(int want) const {
    const double fLo = 20.0, fHi = std::min(20000.0, sr_ * 0.45);
    const double floorDb = -120.0 + get(Floor) * 80.0 + 3.0;
    std::vector<Peak> out;
    for (int i = 2; i < kCurve - 2; ++i) {
        const float v = curve_[(size_t)i];
        if (v <= floorDb) continue;
        if (v < curve_[(size_t)(i - 1)] || v < curve_[(size_t)(i + 1)]) continue;
        if (v < curve_[(size_t)(i - 2)] || v < curve_[(size_t)(i + 2)]) continue;
        out.push_back({fLo * std::pow(fHi / fLo, i / (double)(kCurve - 1)), v});
    }
    std::sort(out.begin(), out.end(), [](const Peak& a, const Peak& b) { return a.db > b.db; });
    if ((int)out.size() > want) out.resize((size_t)want);
    return out;
}

std::string Lens::noteName(double hz) {
    if (hz <= 0) return "—";
    const double midi = 69.0 + 12.0 * std::log2(hz / 440.0);
    const int n = (int)std::lround(midi);
    const int cents = (int)std::lround((midi - n) * 100.0);
    static const char* nm[12] = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
    char b[32];
    std::snprintf(b, sizeof b, "%s%d %+d ct", nm[((n % 12) + 12) % 12], n / 12 - 1, cents);
    return b;
}

// The synced frame (specs/050). With the transport playing, the window is the span on the
// beat grid that holds the playhead: columns left of the playhead are this pass, the rest the
// previous pass. The history is walked back only while it is one continuous run (playing, beats
// falling): a seek, a loop wrap or a stop ends it, so the pass restarts cleanly. Stopped, the
// window rolls: the last span of audio at the current tempo, newest at the right. Short spans
// read the raw rings, longer ones the min/max bins, so a transient keeps its height.
void Lens::analyzeSync() const {
    if (get(Freeze) >= 0.5f || get(Sync) < 0.5f) return; // hold the frame; Sync off nobody reads it
    float* h = sync_.data();
    float* col = h + kSyncHead;
    const float nan = std::nanf("");
    for (int i = 0; i < kSync * kSyncCol; ++i) col[i] = nan;
    const uint32_t head = binW_.load(std::memory_order_acquire);
    const double bpb = bpb_.load(std::memory_order_relaxed);
    const double S = spanBeats(get(Span), bpb);
    const bool keyOn = keyLive();
    h[S_BeatsPerBar] = (float)bpb;
    h[S_SpanBeats] = (float)S;
    h[S_KeyLive] = keyOn ? 1.0f : 0.0f;
    h[S_Mode] = 0.0f;
    h[S_Playhead] = 1.0f;
    h[S_Start] = 0.0f;
    h[S_Bpm] = 120.0f;
    if (head == 0) return;
    const uint32_t mask = kBins - 1;
    const Bin& nb = bins_[(head - 1) & mask];
    const double spb = nb.spb > 0.0 ? nb.spb : sr_ * 0.5; // 120 BPM when the engine gave none
    h[S_Bpm] = (float)(60.0 * sr_ / spb);
    const bool ms = msOn();
    const int src = srcIdx();
    const uint8_t chan = chanBits(ms, src);
    auto sameChan = [chan](const Bin& b) { return (b.flags & kBinChan) == chan; };
    const uint32_t ringHead = ringW_.load(std::memory_order_acquire);
    const uint32_t newestEnd = nb.sample + (uint32_t)binLen_; // one past the newest bin's last sample

    auto put = [&](int c, float mLo, float mHi, float kLo, float kHi, float age) {
        float* v = col + (size_t)c * kSyncCol;
        if (std::isnan(v[4])) {
            v[0] = mLo;
            v[1] = mHi;
            v[2] = kLo;
            v[3] = kHi;
            v[4] = age;
            return;
        }
        v[0] = std::min(v[0], mLo);
        v[1] = std::max(v[1], mHi);
        v[2] = std::min(v[2], kLo);
        v[3] = std::max(v[3], kHi);
    };
    // Samples [a, b) of the raw rings into column c (b > a), if they are still in the ring.
    auto putRaw = [&](int c, uint32_t a, uint32_t b, float age) {
        if ((int32_t)(ringHead - b) < 0 || ringHead - a > (uint32_t)(kRing - 16)) return false;
        float mLo = 1e9f, mHi = -1e9f, kLo = 1e9f, kHi = -1e9f;
        for (uint32_t t = a; t != b; ++t) {
            const float m = pick(t, ms, src), k = pickKey(t, ms, src);
            mLo = std::min(mLo, m);
            mHi = std::max(mHi, m);
            kLo = std::min(kLo, k);
            kHi = std::max(kHi, k);
        }
        put(c, mLo, mHi, kLo, kHi, age);
        return true;
    };
    const uint32_t depth = std::min<uint32_t>(head, kBins - 1);

    if (!(nb.flags & kBinPlaying)) {
        // Free: the last S beats at this tempo, rolling.
        const double span = S * spb;
        if (span + 64 < kRing) {
            const uint32_t n = std::max<uint32_t>(1, (uint32_t)std::lround(span));
            for (int c = 0; c < kSync; ++c) {
                const uint32_t a = newestEnd - n + (uint32_t)((uint64_t)n * c / kSync);
                const uint32_t b = std::max(a + 1, newestEnd - n + (uint32_t)((uint64_t)n * (c + 1) / kSync));
                putRaw(c, a, b, 0.0f);
            }
        } else {
            // Scaled by the span asked for: history it doesn't have yet stays empty on the left.
            const uint32_t want = std::max<uint32_t>(1, (uint32_t)(span / binLen_));
            const uint32_t nBins = std::min<uint32_t>(depth, want);
            for (uint32_t k = 0; k < nBins; ++k) {
                const Bin& b = bins_[(head - 1 - k) & mask];
                if (!sameChan(b)) break;
                const double x0 = 1.0 - (double)(k + 1) / want, x1 = 1.0 - (double)k / want;
                const int c0 = std::clamp((int)(x0 * kSync), 0, kSync - 1), c1 = std::clamp((int)std::ceil(x1 * kSync) - 1, c0, kSync - 1);
                for (int c = c0; c <= c1; ++c) put(c, b.mLo, b.mHi, b.kLo, b.kHi, 0.0f);
            }
        }
        return;
    }

    // Synced: B is where the newest audio ends, W0 the window's start on the grid.
    const double B = nb.beat + binLen_ / spb;
    const double W0 = std::floor(B / S + 1e-9) * S;
    const double play = std::clamp((B - W0) / S, 0.0, 1.0);
    h[S_Mode] = 1.0f;
    h[S_Playhead] = (float)play;
    h[S_Start] = (float)W0;

    // Bin k (newest first) still follows bin k-1 on the song's timeline: playing, and one bin's
    // worth of beats earlier. A loop wrap or a seek either way ends the run.
    auto follows = [&](const Bin& b, double later) {
        if (!(b.flags & kBinPlaying)) return false;
        const double step = binLen_ / (b.spb > 0.0 ? b.spb : spb), d = later - b.beat;
        return d > 0.0 && d < step * 1.5 + 1e-9;
    };

    // The continuous run back from now: where it starts (in beats).
    double validFrom = nb.beat, prev = nb.beat;
    for (uint32_t k = 1; k < depth; ++k) {
        const Bin& b = bins_[(head - 1 - k) & mask];
        if (!follows(b, prev)) break;
        prev = b.beat;
        validFrom = b.beat;
        if (b.beat < W0 - S) break;
    }

    if (2.0 * S * spb + 64 < kRing) {
        // Short span: each column's beats → samples on the newest bin's tempo.
        for (int pass = 0; pass < 2; ++pass) {
            const double base = W0 - pass * S;
            for (int c = 0; c < kSync; ++c) {
                const double x0 = (double)c / kSync, x1 = (double)(c + 1) / kSync;
                if (pass == 0 && x0 >= play) break;   // not played yet in this pass
                if (pass == 1 && x1 <= play) continue; // overwritten by this pass
                const double b0 = base + x0 * S, b1 = base + std::min(x1, pass == 0 ? play : 1.0) * S;
                if (b0 < validFrom) continue;
                // Half-open [s0, s1): a click on a column edge lands in one column only.
                const int64_t s0 = (int64_t)std::floor((b0 - nb.beat) * spb + 1e-6), s1 = (int64_t)std::floor((b1 - nb.beat) * spb + 1e-6);
                const uint32_t a = nb.sample + (uint32_t)s0, e = nb.sample + (uint32_t)std::max<int64_t>(s0 + 1, s1);
                putRaw(c, a, e, (float)pass);
            }
        }
        return;
    }

    // Long span: place each bin of the run by its own beat.
    for (uint32_t k = 0; k < depth; ++k) {
        const Bin& b = bins_[(head - 1 - k) & mask];
        if (!sameChan(b) || (k > 0 && !follows(b, prev))) break; // the raw path above reads any channel
        prev = b.beat;
        const double e = b.beat + binLen_ / (b.spb > 0.0 ? b.spb : spb);
        if (b.beat < W0 - S) break;
        // Columns [x0, x1) of a pass; a bin that straddles the window's start feeds both.
        auto place = [&](double x0, double x1, int pass) {
            if (x1 <= 0.0 || x0 >= 1.0) return;
            int c0 = std::clamp((int)(x0 * kSync), 0, kSync - 1);
            const int c1 = std::clamp((int)std::ceil(x1 * kSync) - 1, c0, kSync - 1);
            if (pass == 1) c0 = std::max(c0, (int)std::ceil(play * kSync)); // right of the sweep only
            for (int c = c0; c <= c1; ++c) put(c, b.mLo, b.mHi, b.kLo, b.kHi, (float)pass);
        };
        if (e > W0) place((b.beat - W0) / S, (e - W0) / S, 0);
        if (b.beat < W0) place((b.beat - (W0 - S)) / S, (e - (W0 - S)) / S, 1);
    }
}

} // namespace nota
