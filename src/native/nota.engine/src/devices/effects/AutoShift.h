// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Auto Shift (device kind 10) — a real-time vocal pitch corrector. Stages:
//   1. DETECT — an NSDF pitch tracker (McLeod: normalized square difference, first peak
//               above 90% of the global max, parabolic refinement) over the mono sum, run
//               every hop. The autocorrelation goes through an FFT, so the window can grow
//               to cover low voices; the lag search is limited to the Det Low … Det High
//               note range and Det Sens sets the voicing threshold. Unvoiced frames with a
//               high zero-crossing rate are flagged as sibilants.
//   2. TARGET — the smoothed pitch (Human blends in a slow note centre, so vibrato is kept
//               around the corrected note) is pulled toward the nearest note of the key's
//               scale, a Custom 12-note set, or — Key Source MIDI — the notes another
//               track is playing (routed through the sidechain source: Note / Scale mode,
//               Latch, Oct Lock, Glide within the MIDI Bend range). Amount, Range, Shift
//               and Fine finish the target; Speed glides the retune.
//   3. SHIFT  — a pitch-synchronous overlap-add (TD-PSOLA) shifter: two-period Hann grains
//               taken a whole period apart and laid out at the new period. Each grain is
//               resampled by the formant factor, so Formant (preserve 0..1) keeps the
//               vocal tract where it was and Formant Shift moves it on its own. At unity
//               grains land where they were taken → transparent. Skip Sibilants passes
//               sibilants unshifted. The shifted path follows the dry level (a slow level
//               match, ±6 dB). The dry path is delayed by the same fixed latency (reported
//               for PDC), so Mix never combs.
// Learn: a decaying, duration-weighted histogram of the sung pitch classes (about 16 bars)
// is matched against Krumhansl key profiles; Key Source Auto follows the best key, and
// deviceAction 1 runs a one-shot Learn that commits it.
//
// scopeRead layout: kTele telemetry floats (S_*), then the 12-bin pitch-class histogram
// (max = 1), then three kHist histories (oldest first): detected pitch, output pitch and
// the MIDI target (MIDI note numbers, 0 = none). gainReductionDb() keeps publishing the
// detected pitch / 127 (the original LIVE feed).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset the analysis (histogram + history), 1 = Learn (iarg 1 start,
// 0 stop and commit, 2 cancel).
// Allocation-free after setSampleRate. Params
// normalized 0..1 → persist / clone / automation flow generically through the base Device.
// APPEND ONLY. Pitch detection, grain scheduling and formant math stay verbatim std::
// (no FastMath — 1 ulp steering a read head, threshold, grain position or loop diverges
// to block scale); the grain Hann window keeps std::cos for the same reason. Custom ring
// buffers are kept verbatim (no shared-primitive migration). Silent stretches auto-sleep
// via isIdle() under the flight-time rule (see process()).

#pragma once

#include "devices/Device.h"
#include "devices/MidiDevice.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>

namespace nota {

class AutoShift : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY. 0..9 are the
    // original params (Formant = how much of the formant envelope is preserved 0..1).
    enum {
        Key = 0,
        Scale,
        Amount,
        Speed,
        Shift,
        Mix,
        Range,
        Formant,
        KeySource,
        Follow,
        Human,
        Fine,
        FormantShift,
        DetLow,
        DetHigh,
        DetSens,
        SkipSibilants,
        CustomScale,
        NoteC,
        NoteCs,
        NoteD,
        NoteDs,
        NoteE,
        NoteF,
        NoteFs,
        NoteG,
        NoteGs,
        NoteA,
        NoteAs,
        NoteB,
        MidiMode,
        MidiLatch,
        MidiOctLock,
        MidiGlide,
        MidiBendRange,
        MidiBend,
        kNumParams
    };

    // Telemetry block (scopeRead, first kTele floats).
    enum {
        S_DetMidi = 0, // smoothed detected pitch (MIDI, fractional), 0 = unvoiced
        S_TargetMidi,  // target note (MIDI, may glide), 0 = none
        S_OutMidi,     // output pitch now (detected + the applied shift), 0 = unvoiced
        S_CorrCents,   // correction being applied now, cents (excl. Shift / Fine)
        S_Conf,        // detector clarity 0..1
        S_Hz,          // detected frequency
        S_Voiced,      // 1 = pitched
        S_Sibilant,    // 1 = an unvoiced, noisy frame (sibilant)
        S_SampleRate,
        S_Cpu,        // share of real time spent in process()
        S_Latency,    // samples
        S_MidiNote,   // MIDI target note (before octave / glide), -1 = none
        S_MidiVel,    // its velocity 0..127
        S_MidiHeld,   // notes held on the source now
        S_MidiLive,   // 1 = a MIDI source fed this device in the last block
        S_Learning,   // 1 = a one-shot Learn is running
        S_LearnKey,   // best key 0..11 (-1 = not enough singing yet)
        S_LearnScale, // its scale: 1 Major, 2 Minor
        S_LearnMatch, // its profile correlation 0..1
        S_Learn2Key,  // runner-up
        S_Learn2Scale,
        S_Learn2Match,
        S_InScale,     // share of the sung notes that lie in the active scale 0..1
        S_AnalysisSec, // seconds of voiced singing the histogram holds (effective)
        S_WindowSec,   // history span, seconds
        S_HistN,       // points per history
        S_Mask,        // active 12-bit pitch-class mask (absolute, C = bit 0)
        S_Key,         // active key 0..11
        S_Ratio,       // pitch ratio now
        S_InDb,        // input level (detector window RMS), dBFS
        S_Bars,        // analysis length, bars
        S_MidiMask,    // pitch classes held on the MIDI source (absolute)
        kTele = 32
    };
    static constexpr int kHist = 384;
    static constexpr int kScope = kTele + 12 + 3 * kHist;
    enum { A_ResetAnalysis = 0, A_Learn = 1 };

    AutoShift();
    void setSampleRate(double sr, int32_t maxBlock) override;
    int32_t latencySamples() const override { return lat_; }

    // The MIDI source rides on the sidechain source slot: pick an instrument track and the
    // engine hands this device that track's notes each block (setMidiKey).
    int32_t sidechainSourceTrackId() const override { return srcTrack_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { srcTrack_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float*, int32_t) override {}
    bool acceptsSidechain() const override { return true; }
    bool wantsMidiKey() const override { return true; }
    void setMidiKey(const MidiEv* evs, int32_t n) override {
        midiEvs_ = evs;
        midiN_ = n;
        midiFed_ = true;
    }

    void setTransportInfo(const TransportInfo& ti) override;

    // Instantaneous smoothed detected pitch (MIDI/127), 0 = unvoiced — the original LIVE feed.
    float gainReductionDb() const override;

    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: grain/analysis tails ring past the input — the engine-visible
    // tail. Settles after the hangover (see process()).
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Telemetry, the pitch-class histogram, then the three histories (oldest first).
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Auto Shift"; }
    int32_t builtinKind() const override { return 10; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kBuf = 16384; // audio / output ring (power of two)
    static constexpr int64_t kMask = kBuf - 1;
    static constexpr int kWinMax = 4096; // largest analysis window
    static constexpr int kFftMax = 2 * kWinMax;
    static constexpr int kRec = 64;                 // per-hop decisions, timestamped
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    // Longest flight past the input: grains land up to kBuf/2 ahead of the read head,
    // the analysis window spans up to kWinMax, the dry path lags by lat_ (≤ kBuf/4).
    static constexpr uint64_t kMaxFlight = kBuf / 2 + kWinMax;
    // Chromatic / Major / Minor / Pentatonic Major / Pentatonic Minor (bit i = i semitones above the key).
    static constexpr uint16_t kScaleMask[5] = {0x0FFF, 0x0AB5, 0x05AD, 0x0295, 0x04A9};
    // Krumhansl–Kessler key profiles.
    static constexpr double kMajProf[12] = {6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88};
    static constexpr double kMinProf[12] = {6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17};

    struct Rec {
        int64_t at = 0;
        double semi = 0.0;
        double period = 200.0;
        bool sib = false;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    int keyIdx() const { return std::clamp((int)std::lround(get(Key) * 11.0f), 0, 11); }
    int scaleIdx() const { return std::clamp((int)std::lround(get(Scale) * 4.0f), 0, 4); }
    int keySrc() const {
        const float v = get(KeySource);
        return v < 0.25f ? 0 : v < 0.75f ? 1 : 2;
    }
    int detLowMidi() const { return 24 + (int)std::lround(get(DetLow) * 72.0f); }
    int detHighMidi() const { return 24 + (int)std::lround(get(DetHigh) * 72.0f); }
    static uint16_t rot(uint16_t m, int key) {
        key = ((key % 12) + 12) % 12;
        return (uint16_t)(((m << key) | (m >> (12 - key))) & 0x0FFF);
    }
    uint16_t scaleMask() const;
    static std::string noteText(int m);
    static std::string stText(double st);

    // Nearest note whose pitch class is in `mask` (absolute); ties go to the closer side.
    static double snapMask(double m, uint16_t mask);

    void resetAnalysis();
    void resetState();

    // The decision in force for input time `t`: the latest record stamped at or before it.
    const Rec& recAt(int64_t t) const;

    // --- MIDI target ------------------------------------------------------------
    void takeMidi();
    void dropOrder(int p);

    // --- FFT (radix-2, in place) ----------------------------------------------------
    void fft(int n, bool inverse);

    // --- detection + target (every hop) -----------------------------------------------
    void detect();

    uint16_t heldMask() const;

    static double profileCorr(const std::array<double, 12>& h, const double* prof, int key);

    void analyseKey(int src);
    void commitLearn();

    // --- TD-PSOLA grain ------------------------------------------------------------------
    // Synthesis mark s (input time), period P, pitch ratio r, formant factor fr. The analysis
    // mark is the one a whole number of periods from the last, nearest s, whose grain is
    // fully written; `snap` takes it at s (sibilants).
    void grain(double s, double P, double r, double fr, bool snap, int64_t to);
    // grain() minus the accumulator writes: advances la_/lastP_ identically. The writes
    // are exact +0.0 no-ops while asleep (ring_ holds only zeros), so the idle skip runs
    // this instead and stays bit-close on wake.
    void grainSilent(double s, double P, double r, double fr, bool snap);

    double sr_ = 44100.0;
    int lat_ = 706, hop_ = 256, baseWin_ = 1024;
    std::atomic<float> p_[kNumParams] = {};
    std::array<std::array<float, kBuf>, 2> ring_{};
    std::array<std::array<float, kBuf>, 2> acc_{};
    std::array<float, kBuf> det_{};
    std::array<float, kFftMax> re_{}, im_{};
    std::array<float, kFftMax / 2> twC_{}, twS_{};
    std::array<double, kWinMax + 1> pe_{};
    std::array<float, kWinMax> nsdf_{};
    std::array<Rec, kRec> rec_{};
    int64_t recW_ = 0;
    int64_t n_ = 0;
    int hopCnt_ = 0;
    double pg_ = 220.0, ns_ = 0.0, la_ = 0.0, lastP_ = 0.0, curSemi_ = 0.0;
    double eDry_ = 0.0, eWet_ = 0.0, gAuto_ = 1.0, lvlA_ = 0.0003, lvlB_ = 0.0005; // level match
    double sm_ = 60.0, center_ = 60.0, midiT_ = -1.0;
    bool have_ = false;

    // MIDI source
    std::atomic<int32_t> srcTrack_{-1};
    const MidiEv* midiEvs_ = nullptr;
    int32_t midiN_ = 0;
    bool midiFed_ = false;
    std::array<uint8_t, 128> held_{};
    std::array<int, 16> order_{};
    int orderN_ = 0, lastNote_ = -1, lastVel_ = 0;
    uint16_t latchMask_ = 0;

    // key analysis
    std::array<double, 12> pcw_{};
    double totalW_ = 0.0;
    int hopsSinceKey_ = 0;
    struct Best {
        int key = -1, scale = 1;
        double corr = 0.0;
    } best_;
    bool learning_ = false;
    double bpm_ = 120.0;
    int tsNum_ = 4;

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak grain-path push (blocks sleep while grains ring)

    // UI feeds
    std::array<std::atomic<float>, kTele> tele_{};
    std::array<std::atomic<float>, 12> pcPub_{};
    std::array<std::array<float, kHist>, 3> hist_{};
    std::atomic<int> histW_{0};
    std::atomic<float> pitchPub_{0.0f}, srA_{44100.0f}, cpuA_{0.0f}, ratioA_{1.0f}, midiLiveA_{0.0f};
    std::atomic<bool> resetReq_{false};
    std::atomic<int> learnReq_{0};
    double cpuS_ = 0.0;
};

} // namespace nota
