// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Aurora — the built-in wavetable synth (kind 5), reworked to the almanac's card.
// Each voice runs TWO wavetable oscillators (independent bank / position / warp / level /
// pitch) plus a sub oscillator, a unison stack (voices / detune / stereo spread), two
// state-variable filters with per-source routing (F1 / F2 / Both / Dry) in parallel or
// series, two envelopes (Env 1 = amp, Env 2 = free), two tempo-syncable LFOs, a generic
// 8×7 modulation matrix, EIGHT macros over twelve destinations, two performance wheels
// (pitch bend with a selectable range, mod wheel) and a built-in FX chain — drive
// (Tube / Tape / Fold, with a tone tilt), chorus (1× / 2× / 4×) and reverb
// (Room / Hall / Plate, with size) — each block switchable on its own. Four spectral
// banks (Analog / Pulse / Formant / Chroma); five warp modes (Off / Sync / Bend / PWM /
// Fold). All params ride the plugin-param interface → automation / persist / clone.
//
// Allocation-free after construction. The 512 KB wavetable set is built ONCE
// per process and shared by every instance — it is read-only data, and rebuilding it per
// voice-bank cost every new track (and every clone) eight million sines.
// Order of params is the persisted state layout — APPEND ONLY.
//
// DSP notes: pitch/phase-critical math stays std::-exact — the sub-oscillator sine, the
// wavetable position interpolation (readMorph), the table build (sharedTables), the LFO
// sine (the matrix can route it to pitch or a resonant cutoff), the chorus tap LFO and
// the drive Fold shaper (all measured over budget — see the .cpp) define the spectrum
// or its timing. Kept FastMath: the Tube drive via fast_math::tanhPrecise with a hoisted
// inverse drive gain (driveInvNorm, once per block), the warp Fold shaper and the unison
// pan gains via fast_math::sin/cos. Sleep: release + chorus/reverb tails ring past
// note-off (hasTail), auto-sleep after a silent hangover (engine skips render while
// idle; noteOn/noteOff/setParam wake).

#pragma once

#include "devices/Instrument.h"
#include "engine/NoteExpression.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace nota {

class WavetableSynth final : public Instrument {
  public:
    static constexpr int kSources = 8; // Env1, Env2, LFO1, LFO2, Vel, Key, ModWhl, Rand
    static constexpr int kDests = 7;   // Pitch, Osc2 Pitch, Position, Cutoff, Reso, Level, Pan
    static constexpr int kMacros = 8;
    // Macros reach further than the matrix: the seven voice-bus destinations above sit at
    // indices 0, 2, 4, 6, 7, 9 and 11 of this twelve-entry list, exactly where a
    // seven-entry normalized value rounds to — so a patch saved against the old list still
    // points at the same destination. The other five act on the block snapshot instead.
    //   0 Pitch · 1 Warp · 2 Osc 2 Pitch · 3 Osc 2 Level · 4 Position · 5 Sub Level
    //   6 Cutoff · 7 Reso · 8 Unison · 9 Level · 10 Drive · 11 Pan
    static constexpr int kMacroDests = 12;

    enum Param {
        // ---- original 11 (v1): Osc1 table/pos/warp, Env1 ADSR, Fil1 cutoff/reso, gain ----
        Table = 0,
        Position,
        Warp,
        Unison,
        Attack,
        Decay,
        Sustain,
        Release,
        Cutoff,
        Resonance,
        Gain,
        // ---- appended (v2, mockup 2j) ----
        Osc1WarpMode,
        Osc1Level,
        Osc1Oct,
        Osc1Semi,
        Osc1Detune,
        Osc1On, // 11..16
        Osc2Table,
        Osc2Position,
        Osc2Warp,
        Osc2WarpMode,
        Osc2Level,
        Osc2Oct,
        Osc2Semi,
        Osc2Detune,
        Osc2On, // 17..25
        SubLevel,
        SubOct,
        SubWave, // 26..28
        UniDetune,
        UniSpread, // 29,30
        Fil1Type,
        Fil1Slope,
        Fil1Env,
        Fil1Lfo,
        Fil1Key, // 31..35
        Fil2Type,
        Fil2Freq,
        Fil2Reso,
        Fil2Slope,
        Fil2Env,
        Fil2Lfo,
        Fil2Key, // 36..42
        RouteOsc1,
        RouteOsc2,
        RouteSub,
        FilSeries, // 43..46 (route 0..3 = F1/F2/Both/Dry)
        Env2Attack,
        Env2Decay,
        Env2Sustain,
        Env2Release, // 47..50
        Lfo1Rate,
        Lfo1Depth,
        Lfo1Shape,
        Lfo1Sync, // 51..54
        Lfo2Rate,
        Lfo2Depth,
        Lfo2Shape, // 55..57
        FxDrive,
        FxChorus,
        FxChorusRate,
        FxReverb, // 58..61
        ModWheel,
        MonoMode,                                   // 62,63
        MatrixBase,                                 // 64 (56 entries)
        MacroBase = MatrixBase + kSources * kDests, // 120 (24 entries)
        // ---- appended (v3, almanac rework): wheels, output pan, LFO 2 sync, FX blocks ----
        ExtraBase = MacroBase + kMacros * 3, // 144
        Bend = ExtraBase,
        BendRange,
        OutPan,
        Lfo2Sync, // 144..147
        FxDriveOn,
        FxDriveMode,
        FxTone, // 148..150
        FxChorusOn,
        FxChorusVoices, // 151,152
        FxReverbOn,
        FxReverbMode,
        FxReverbSize, // 153..155
        kNumParams    // 156
    };
    static int matrixIdx(int s, int d) { return MatrixBase + s * kDests + d; }
    static int macroIdx(int m, int f) { return MacroBase + m * 3 + f; }

    WavetableSynth();

    int32_t kind() const override { return 5; }
    const char* displayName() const override { return "Nota Aurora"; }

    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override;

    // MPE: bend → pitch (a keyboard's wheel uses the patch's Bend Range), pressure → level +
    // both cutoffs, slide → both oscillators' wavetable position (±half the table).
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void setTransport(double, double samplesPerBeat, bool) override;
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // Sleep contract: release + chorus/reverb tails ring past note-off (the engine-visible
    // tail). Settles after the hangover; noteOn/noteOff/setParam wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // ---- parameters --------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override;
    // The id → index table is the same for every instance, so build it once: a preset
    // apply looks up every one of its parameters, and the linear scan used to rebuild
    // each id's string on the way past it.
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846, kTwoPi = 6.283185307179586, kHalfPi = kPi * 0.5;
    static constexpr int kBanks = 4, kFrames = 16, kTableSize = 2048, kUnison = 7, kVoices = 16;
    static constexpr int kRevCombs = 4, kRevAll = 2;

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct SvfCoef {
        double a1 = 0, a2 = 0, a3 = 0, k = 1;
    };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        double freq = 0, ktOct = 0, rand = 0;
        double ph1[kUnison] = {}, ph2[kUnison] = {}, subPh = 0;
        float velocity = 0, env = 0, env2 = 0;
        Stage stage = Stage::Attack, stage2 = Stage::Attack;
        SvfCoef f[2];
        double ic1L[2] = {}, ic2L[2] = {}, ic1R[2] = {},
               ic2R[2] = {}; // L = 1st stage, R = 2nd slope stage; [i] = filter i
        double sc1L[2] = {}, sc2L[2] = {}, sc1R[2] = {}, sc2R[2] = {}; // the same filters' state for the unison side
        void clearSide() {
            for (int i = 0; i < 2; ++i) sc1L[i] = sc2L[i] = sc1R[i] = sc2R[i] = 0.0;
        }
        int32_t modCount = 0;
        VoiceExpr ex; // MPE bend / pressure / slide
        float exBendAt = 0.0f;
        double exBendMul = 1.0;
    };

    float wheelRange() const { return static_cast<float>(std::round(1.0 + get(BendRange) * 11.0)); } // semitones
    void set(int i, float v) { pn_[i].store(v, std::memory_order_relaxed); }
    float get(int i) const { return pn_[i].load(std::memory_order_relaxed); }
    double bip(int i) const { return (get(i) - 0.5) * 2.0; }
    int bankOf(int p) const { return std::clamp((int)std::lround(get(p) * (kBanks - 1)), 0, kBanks - 1); }
    int warpModeOf(int p) const { return std::clamp((int)std::lround(get(p) * 4.0f), 0, 4); }
    int filtType(int p) const { return std::clamp((int)std::lround(get(p) * 4.0f), 0, 4); }
    int shapeOf(int p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    int routeOf(int p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    static int macroDestOf(float v) { return std::clamp((int)std::lround(v * (kMacroDests - 1)), 0, kMacroDests - 1); }
    static double resoToK(double r) { return std::clamp(2.0 - 1.94 * std::clamp(r, 0.0, 1.0), 0.06, 2.0); }
    double pitchMul(int oct, int semi, int det) const {
        int o = (int)std::lround((get(oct) - 0.5) * 6);
        int s = (int)std::lround((get(semi) - 0.5) * 24);
        double c = (get(det) - 0.5) * 100;
        return std::exp2(o + s / 12.0 + c / 1200.0);
    }

    static void routeAdd(int route, double s, double& in1, double& in2, double& dry) {
        switch (route) {
        case 0: in1 += s; break;
        case 1: in2 += s; break;
        case 2:
            in1 += s;
            in2 += s;
            break;
        default: dry += s;
        }
    }
    static void advEnv(float& e, Stage& st, float a, float d, float s, float r);

    void setSvf(SvfCoef& c, double fc, double k) const;
    static double svf(const SvfCoef& c, double& ic1, double& ic2, double in, int type);

    // Filter 1 and 2 on one signal, in parallel or series, through one set of integrator
    // state (a = first stage, b = the second slope stage; [i] = filter i).
    static double filterPair(const SvfCoef* f, double* ic1a, double* ic2a, double* ic1b, double* ic2b, double in1,
        double in2, bool series, int t1, int t2, bool slope1, bool slope2);

    // Warp mode applied to the read phase (Off/Sync/Bend/PWM/Fold — Fold post-processes output).
    static double warpRead(int mode, double phase, double amt);
    static float foldOut(float s, double amt);

    // One oscillator: a unison stack of wavetable reads, detuned in cents and spread
    // across the stereo field with equal power. Returns the mid (which the filters see)
    // and, through `sideOut`, the half-difference the spread opens up.
    float oscStack(double* ph, double inc, int bank, float pos, int mode, double warpAmt, int uniN, double detCents,
        float spread, float& sideOut);
    // Sub oscillator: the sine DEFINES pitch and stays std::-exact.
    static float subOsc(int wave, double ph) {
        switch (wave) {
        case 1: return ph < 0.5 ? 1.0f : -1.0f;
        case 2: return (float)(4.0 * std::fabs(ph - 0.5) - 1.0);
        default: return (float)std::sin(kTwoPi * ph);
        }
    }

    const float* frame(int bank, int f) const { return tables_ + (size_t)((bank * kFrames) + f) * kTableSize; }
    // Wavetable position interpolation — pitch-critical, stays std::-exact.
    static float readMorph(const float* t0, const float* t1, float ff, double phase) {
        const double x = phase * kTableSize;
        int i0 = (int)x & (kTableSize - 1);
        int i1 = (i0 + 1) & (kTableSize - 1);
        const float xf = (float)(x - std::floor(x));
        const float a = t0[i0] + (t0[i1] - t0[i0]) * xf;
        const float b = t1[i0] + (t1[i1] - t1[i0]) * xf;
        return a + (b - a) * ff;
    }
    static float harmAmp(int bank, float t, int n);
    // The four banks × sixteen frames are the same read-only 512 KB for every instance and
    // every clone, so they are built once, on first use, and handed out as a pointer.
    // The build defines the spectrum: stays std::-exact.
    static const float* sharedTables();

    static double lfoValue(int shape, double ph, float sh);
    double lfoInc(int rate, int sync) const {
        int d = std::clamp((int)std::lround(get(sync) * 7.0f), 0, 7);
        if (d > 0 && spb_ > 0.0) {
            static const double beats[8] = {0, 4, 2, 1, 0.5, 0.25, 0.125, 0.0625};
            return 1.0 / (beats[d] * spb_);
        }
        return expMap(get(rate), 0.05, 20.0) / sampleRate_;
    }

    // FX · drive. Tube is the soft symmetric tanh this always had; Tape adds the second
    // harmonic a tape stage leans on; Fold turns the curve back on itself past the top.
    // invNorm is the hoisted 1/tanh(drive*0.7+1) (Tube only; ignored by Tape/Fold).
    static float driveSample(int mode, float x, float amt, float invNorm);
    // A tilt around the middle: negative darkens (one-pole low-pass), positive lifts the
    // top (the signal plus what the low-pass throws away).
    static float tone(float& z, float x, float coef);

    // FX · a stereo chorus of 1, 2 or 4 modulated taps, and a compact Freeverb whose
    // feedback and damping the mode + size choose.
    void chorus(float l, float r, float amt, int voices, float& outL, float& outR);
    void reverb(float l, float r, float fb, float damp, float& outL, float& outR);

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(int p, double lo, double hi) const { return (float)(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float frand() {
        rng_ = rng_ * 1664525u + 1013904223u;
        return (rng_ >> 8) / 16777216.0f;
    }
    Voice* findFreeVoice() {
        for (auto& v : voices_)
            if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_)
            if (v.env < q->env) q = &v;
        return q;
    }

    struct Comb {
        std::vector<float> buf;
        int idx = 0;
        float store = 0;
    };
    struct Allp {
        std::vector<float> buf;
        int idx = 0;
    };

    Voice voices_[kVoices];
    double sampleRate_ = 44100.0, spb_ = 0.0;
    float exprCoef_ = exprSmoothCoef(44100.0);
    GlobalExpr gexpr_; // pitch wheel / channel pressure (pitch −1)
    double lfoPhase1_ = 0, lfoPhase2_ = 0, chorusPh_ = 0;
    float lfoSh1_ = 0, lfoSh2_ = 0;
    uint32_t rng_ = 0x51ed270bu;
    const float* tables_ = nullptr; // shared, read-only (sharedTables)
    std::vector<float> chorusBuf_;
    int chorusW_ = 0;
    float toneL_ = 0, toneR_ = 0;
    Comb revC_[kRevCombs];
    Allp revA_[kRevAll];
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];

    // Auto-sleep hangover (flag only — the engine skips render while idle).
    static constexpr uint64_t kIdleSamples = 16384;
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
