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
// Header-only; allocation-free after construction. The 512 KB wavetable set is built ONCE
// per process and shared by every instance — it is read-only data, and rebuilding it per
// voice-bank cost every new track (and every clone) eight million sines.
// Order of params is the persisted state layout — APPEND ONLY.

#pragma once

#include "Instrument.h"

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
    static constexpr int kSources = 8;   // Env1, Env2, LFO1, LFO2, Vel, Key, ModWhl, Rand
    static constexpr int kDests   = 7;   // Pitch, Osc2 Pitch, Position, Cutoff, Reso, Level, Pan
    static constexpr int kMacros  = 8;
    // Macros reach further than the matrix: the seven voice-bus destinations above sit at
    // indices 0, 2, 4, 6, 7, 9 and 11 of this twelve-entry list, exactly where a
    // seven-entry normalized value rounds to — so a patch saved against the old list still
    // points at the same destination. The other five act on the block snapshot instead.
    //   0 Pitch · 1 Warp · 2 Osc 2 Pitch · 3 Osc 2 Level · 4 Position · 5 Sub Level
    //   6 Cutoff · 7 Reso · 8 Unison · 9 Level · 10 Drive · 11 Pan
    static constexpr int kMacroDests = 12;

    enum Param {
        // ---- original 11 (v1): Osc1 table/pos/warp, Env1 ADSR, Fil1 cutoff/reso, gain ----
        Table = 0, Position, Warp, Unison, Attack, Decay, Sustain, Release, Cutoff, Resonance, Gain,
        // ---- appended (v2, mockup 2j) ----
        Osc1WarpMode, Osc1Level, Osc1Oct, Osc1Semi, Osc1Detune, Osc1On,        // 11..16
        Osc2Table, Osc2Position, Osc2Warp, Osc2WarpMode, Osc2Level, Osc2Oct, Osc2Semi, Osc2Detune, Osc2On,  // 17..25
        SubLevel, SubOct, SubWave,                                            // 26..28
        UniDetune, UniSpread,                                                 // 29,30
        Fil1Type, Fil1Slope, Fil1Env, Fil1Lfo, Fil1Key,                       // 31..35
        Fil2Type, Fil2Freq, Fil2Reso, Fil2Slope, Fil2Env, Fil2Lfo, Fil2Key,   // 36..42
        RouteOsc1, RouteOsc2, RouteSub, FilSeries,                            // 43..46 (route 0..3 = F1/F2/Both/Dry)
        Env2Attack, Env2Decay, Env2Sustain, Env2Release,                      // 47..50
        Lfo1Rate, Lfo1Depth, Lfo1Shape, Lfo1Sync,                             // 51..54
        Lfo2Rate, Lfo2Depth, Lfo2Shape,                                       // 55..57
        FxDrive, FxChorus, FxChorusRate, FxReverb,                            // 58..61
        ModWheel, MonoMode,                                                   // 62,63
        MatrixBase,                                                           // 64 (56 entries)
        MacroBase = MatrixBase + kSources * kDests,                           // 120 (24 entries)
        // ---- appended (v3, almanac rework): wheels, output pan, LFO 2 sync, FX blocks ----
        ExtraBase = MacroBase + kMacros * 3,                                  // 144
        Bend = ExtraBase, BendRange, OutPan, Lfo2Sync,                        // 144..147
        FxDriveOn, FxDriveMode, FxTone,                                       // 148..150
        FxChorusOn, FxChorusVoices,                                           // 151,152
        FxReverbOn, FxReverbMode, FxReverbSize,                               // 153..155
        kNumParams                                                            // 156
    };
    static int matrixIdx(int s, int d) { return MatrixBase + s * kDests + d; }
    static int macroIdx(int m, int f) { return MacroBase + m * 3 + f; }

    WavetableSynth() {
        set(Table, 0.0f); set(Position, 0.0f); set(Warp, 0.5f); set(Unison, 0.30f);
        set(Attack, 0.01f); set(Decay, 0.30f); set(Sustain, 0.75f); set(Release, 0.25f);
        set(Cutoff, 0.70f); set(Resonance, 0.10f); set(Gain, 0.80f);
        set(Osc1WarpMode, 0.0f); set(Osc1Level, 0.85f); set(Osc1Oct, 0.5f); set(Osc1Semi, 0.5f); set(Osc1Detune, 0.5f); set(Osc1On, 1.0f);
        set(Osc2Table, 0.33f); set(Osc2Position, 0.0f); set(Osc2Warp, 0.5f); set(Osc2WarpMode, 0.0f); set(Osc2Level, 0.0f); set(Osc2Oct, 0.5f); set(Osc2Semi, 0.5f); set(Osc2Detune, 0.55f); set(Osc2On, 0.0f);
        set(SubLevel, 0.0f); set(SubOct, 0.5f); set(SubWave, 0.0f);
        set(UniDetune, 0.3f); set(UniSpread, 0.6f);
        set(Fil1Type, 0.0f); set(Fil1Slope, 0.0f); set(Fil1Env, 0.5f); set(Fil1Lfo, 0.5f); set(Fil1Key, 0.3f);
        set(Fil2Type, 0.0f); set(Fil2Freq, 0.7f); set(Fil2Reso, 0.1f); set(Fil2Slope, 0.0f); set(Fil2Env, 0.5f); set(Fil2Lfo, 0.5f); set(Fil2Key, 0.3f);
        set(RouteOsc1, 0.0f); set(RouteOsc2, 0.0f); set(RouteSub, 0.0f); set(FilSeries, 0.0f);
        set(Env2Attack, 0.02f); set(Env2Decay, 0.4f); set(Env2Sustain, 0.3f); set(Env2Release, 0.3f);
        set(Lfo1Rate, 0.4f); set(Lfo1Depth, 0.5f); set(Lfo1Shape, 0.0f); set(Lfo1Sync, 0.0f);
        set(Lfo2Rate, 0.3f); set(Lfo2Depth, 0.5f); set(Lfo2Shape, 0.0f);
        set(FxDrive, 0.0f); set(FxChorus, 0.0f); set(FxChorusRate, 0.3f); set(FxReverb, 0.0f);
        set(ModWheel, 0.0f); set(MonoMode, 0.0f);
        for (int s = 0; s < kSources; ++s) for (int d = 0; d < kDests; ++d) set((Param)matrixIdx(s, d), 0.5f);
        for (int m = 0; m < kMacros; ++m) { set((Param)macroIdx(m, 0), 0.0f); set((Param)macroIdx(m, 1), 0.0f); set((Param)macroIdx(m, 2), 0.5f); }
        set(Bend, 0.5f); set(BendRange, 1.0f / 11.0f);   // centred, ±2 semitones
        set(OutPan, 0.5f); set(Lfo2Sync, 0.0f);
        // The three FX blocks start switched ON with their amount at zero, so a patch saved
        // before they had switches sounds exactly as it did.
        set(FxDriveOn, 1.0f); set(FxDriveMode, 0.0f); set(FxTone, 0.5f);
        set(FxChorusOn, 1.0f); set(FxChorusVoices, 0.5f);
        set(FxReverbOn, 1.0f); set(FxReverbMode, 0.0f); set(FxReverbSize, 0.5f);
        tables_ = sharedTables();
        chorusBuf_.assign(4096, 0.0f);
        for (auto& c : revC_) c.buf.assign(1, 0.0f);
    }

    int32_t kind() const override { return 5; }
    const char* displayName() const override { return "Nota Aurora"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        chorusBuf_.assign((size_t)(sampleRate_ * 0.05) + 2, 0.0f); chorusW_ = 0;
        static const int ct[kRevCombs] = {1116, 1188, 1277, 1356};
        for (int c = 0; c < kRevCombs; ++c) { revC_[c].buf.assign((size_t)(ct[c] * sampleRate_ / 44100.0) + 1, 0.0f); revC_[c].idx = 0; revC_[c].store = 0; }
        static const int at[kRevAll] = {556, 441};
        for (int a = 0; a < kRevAll; ++a) { revA_[a].buf.assign((size_t)(at[a] * sampleRate_ / 44100.0) + 1, 0.0f); revA_[a].idx = 0; }
        toneL_ = toneR_ = 0.0f;
    }
    void setTransport(double, double samplesPerBeat, bool) override { if (samplesPerBeat > 0.0) spb_ = samplesPerBeat; }
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // ---- parameters --------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i < MatrixBase) {
            static const char* ids[] = {
                "table", "position", "warp", "unison", "attack", "decay", "sustain", "release", "cutoff", "resonance", "gain",
                "osc1warpmode", "osc1level", "osc1oct", "osc1semi", "osc1detune", "osc1on",
                "osc2table", "osc2position", "osc2warp", "osc2warpmode", "osc2level", "osc2oct", "osc2semi", "osc2detune", "osc2on",
                "sublevel", "suboct", "subwave", "unidetune", "unispread",
                "fil1type", "fil1slope", "fil1env", "fil1lfo", "fil1key",
                "fil2type", "fil2freq", "fil2reso", "fil2slope", "fil2env", "fil2lfo", "fil2key",
                "routeosc1", "routeosc2", "routesub", "filseries",
                "env2attack", "env2decay", "env2sustain", "env2release",
                "lfo1rate", "lfo1depth", "lfo1shape", "lfo1sync", "lfo2rate", "lfo2depth", "lfo2shape",
                "fxdrive", "fxchorus", "fxchorusrate", "fxreverb", "modwheel", "mono" };
            return std::string(ids[i]);
        }
        if (i < MacroBase) { int j = i - MatrixBase; return "mtx" + std::to_string(j / kDests) + "_" + std::to_string(j % kDests); }
        if (i >= ExtraBase) {
            static const char* ex[] = { "bend", "bendrange", "outpan", "lfo2sync",
                                        "fxdriveon", "fxdrivemode", "fxtone",
                                        "fxchoruson", "fxchorusvoices",
                                        "fxreverbon", "fxreverbmode", "fxreverbsize" };
            return std::string(ex[i - ExtraBase]);
        }
        int j = i - MacroBase, m = j / 3, f = j % 3;
        return "mac" + std::to_string(m) + (f == 0 ? "val" : f == 1 ? "dest" : "amt");
    }
    std::string pluginParamName(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i < MatrixBase) {
            static const char* nm[] = {
                "Osc1 Table", "Osc1 Position", "Osc1 Warp", "Unison", "Attack", "Decay", "Sustain", "Release", "Cutoff", "Resonance", "Gain",
                "Osc1 Warp Mode", "Osc1 Level", "Osc1 Octave", "Osc1 Semi", "Osc1 Detune", "Osc1 On",
                "Osc2 Table", "Osc2 Position", "Osc2 Warp", "Osc2 Warp Mode", "Osc2 Level", "Osc2 Octave", "Osc2 Semi", "Osc2 Detune", "Osc2 On",
                "Sub Level", "Sub Octave", "Sub Wave", "Unison Detune", "Unison Spread",
                "Filter 1 Type", "Filter 1 Slope", "Filter 1 Env", "Filter 1 LFO", "Filter 1 Key",
                "Filter 2 Type", "Filter 2 Freq", "Filter 2 Reso", "Filter 2 Slope", "Filter 2 Env", "Filter 2 LFO", "Filter 2 Key",
                "Route Osc1", "Route Osc2", "Route Sub", "Filter Series",
                "Env2 Attack", "Env2 Decay", "Env2 Sustain", "Env2 Release",
                "LFO1 Rate", "LFO1 Depth", "LFO1 Shape", "LFO1 Sync", "LFO2 Rate", "LFO2 Depth", "LFO2 Shape",
                "Drive", "Chorus", "Chorus Rate", "Reverb", "Mod Wheel", "Mono Mode" };
            return std::string(nm[i]);
        }
        static const char* sn[] = { "Env 1", "Env 2", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl", "Random" };
        static const char* dn[] = { "Pitch", "Osc2 Pitch", "Position", "Cutoff", "Reso", "Level", "Pan" };
        if (i < MacroBase) { int j = i - MatrixBase; return std::string(sn[j / kDests]) + " → " + dn[j % kDests]; }
        if (i >= ExtraBase) {
            static const char* exNm[] = { "Pitch Bend", "Bend Range", "Out Pan", "LFO2 Sync",
                                          "Drive On", "Drive Mode", "Drive Tone",
                                          "Chorus On", "Chorus Voices",
                                          "Reverb On", "Reverb Mode", "Reverb Size" };
            return std::string(exNm[i - ExtraBase]);
        }
        int j = i - MacroBase, m = j / 3, f = j % 3;
        return "Macro " + std::to_string(m + 1) + (f == 0 ? "" : f == 1 ? " Dest" : " Amount");
    }
    float pluginParamGet(int32_t i) const override { return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  pluginParamSet(int32_t i, float v) override { if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    // The id → index table is the same for every instance, so build it once: a preset
    // apply looks up every one of its parameters, and the linear scan used to rebuild
    // each id's string on the way past it.
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        static const std::unordered_map<std::string, int32_t> map = [this] {
            std::unordered_map<std::string, int32_t> m;
            m.reserve(kNumParams * 2);
            for (int32_t i = 0; i < kNumParams; ++i) m.emplace(pluginParamId(i), i);
            return m;
        }();
        auto it = map.find(id);
        return it == map.end() ? -1 : it->second;
    }

    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) { float v = pn_[i].load(std::memory_order_relaxed); std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float)); }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data) return;
        const int n = std::min<int>(kNumParams, size / (int)sizeof(float));
        for (int i = 0; i < n; ++i) { float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float)); if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<WavetableSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    void noteOn(int32_t pitch, float velocity) override {
        const bool mono = get(MonoMode) > 0.5f;
        Voice* v = mono ? &voices_[0] : findFreeVoice();
        const bool legato = mono && v->active && v->stage != Stage::Off;
        v->active = true; v->pitch = pitch;
        v->freq = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        v->velocity = std::clamp(velocity, 0.0f, 1.0f);
        v->ktOct = (pitch - 60) / 12.0;
        rng_ = rng_ * 1664525u + 1013904223u; v->rand = ((rng_ >> 8) / 16777216.0) * 2.0 - 1.0;
        if (!legato) {
            v->env = 0.0f; v->stage = Stage::Attack;
            v->env2 = 0.0f; v->stage2 = Stage::Attack;
            v->ic1L[0] = v->ic2L[0] = v->ic1R[0] = v->ic2R[0] = 0.0;
            v->ic1L[1] = v->ic2L[1] = v->ic1R[1] = v->ic2R[1] = 0.0;
            for (int u = 0; u < kUnison; ++u) { rng_ = rng_ * 1664525u + 1013904223u; v->ph1[u] = (rng_ >> 8) / 16777216.0; rng_ = rng_ * 1664525u + 1013904223u; v->ph2[u] = (rng_ >> 8) / 16777216.0; }
            v->subPh = 0.0;
        }
    }
    void noteOff(int32_t pitch) override {
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.stage != Stage::Release) { v.stage = Stage::Release; v.stage2 = Stage::Release; }
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        // ---- macros first: seven of the twelve destinations ride the per-voice bus
        //      below, the other five move this block's snapshot ----
        double macroDst[kDests] = {0, 0, 0, 0, 0, 0, 0};
        double macroBlk[5] = {0, 0, 0, 0, 0};   // warp · osc2 level · sub level · unison · drive
        for (int m = 0; m < kMacros; ++m) {
            const double x = get((Param)macroIdx(m, 0)) * (get((Param)macroIdx(m, 2)) - 0.5) * 2.0;
            if (std::fabs(x) <= 1e-4) continue;
            switch (macroDestOf(get((Param)macroIdx(m, 1)))) {
                case 0:  macroDst[0] += x; break;   // Pitch
                case 2:  macroDst[1] += x; break;   // Osc 2 Pitch
                case 4:  macroDst[2] += x; break;   // Position
                case 6:  macroDst[3] += x; break;   // Cutoff
                case 7:  macroDst[4] += x; break;   // Reso
                case 9:  macroDst[5] += x; break;   // Level
                case 11: macroDst[6] += x; break;   // Pan
                case 1:  macroBlk[0] += x; break;   // Warp
                case 3:  macroBlk[1] += x; break;   // Osc 2 Level
                case 5:  macroBlk[2] += x; break;   // Sub Level
                case 8:  macroBlk[3] += x; break;   // Unison detune
                default: macroBlk[4] += x; break;   // Drive
            }
        }

        // ---- block snapshot ----
        const int b1 = bankOf(Table), b2 = bankOf(Osc2Table);
        const int wm1 = warpModeOf(Osc1WarpMode), wm2 = warpModeOf(Osc2WarpMode);
        const float basePos1 = get(Position), basePos2 = get(Osc2Position);
        const double warp1 = std::clamp(get(Warp) + macroBlk[0], 0.0, 1.0);
        const double warp2 = std::clamp(get(Osc2Warp) + macroBlk[0], 0.0, 1.0);
        const float lvl1 = get(Osc1On) > 0.5f ? get(Osc1Level) : 0.0f;
        const float lvl2 = get(Osc2On) > 0.5f ? (float)std::clamp(get(Osc2Level) + macroBlk[1], 0.0, 2.0) : 0.0f;
        const float subLvl = (float)std::clamp(get(SubLevel) + macroBlk[2], 0.0, 2.0);
        const int subWave = (int)std::lround(get(SubWave) * 2.0f);
        const double m1 = pitchMul(Osc1Oct, Osc1Semi, Osc1Detune), m2 = pitchMul(Osc2Oct, Osc2Semi, Osc2Detune);
        const double subMul = std::exp2(std::lround((get(SubOct) - 0.5f) * 4.0f) - 1.0);   // default one octave down
        // Unison: the count, the detune in cents and the stereo spread are three separate
        // dials — the detune no longer rides the count, so the readout says what you hear.
        const int uniN = std::clamp(1 + (int)std::lround(get(Unison) * 6.0f), 1, kUnison);
        const double uniDet = std::clamp(get(UniDetune) + macroBlk[3], 0.0, 1.0) * 50.0;   // ± cents
        const float uniSpread = get(UniSpread);

        const float atk = rateOf(Attack, 0.001, 2.0), dec = rateOf(Decay, 0.002, 3.0), rel = rateOf(Release, 0.002, 4.0), sus = get(Sustain);
        const float atk2 = rateOf(Env2Attack, 0.001, 2.0), dec2 = rateOf(Env2Decay, 0.002, 3.0), rel2 = rateOf(Env2Release, 0.002, 4.0), sus2 = get(Env2Sustain);

        const int t1type = filtType(Fil1Type), t2type = filtType(Fil2Type);
        const bool slope1 = get(Fil1Slope) > 0.5f, slope2 = get(Fil2Slope) > 0.5f;
        const double f1base = expMap(get(Cutoff), 20.0, 18000.0), f2base = expMap(get(Fil2Freq), 20.0, 18000.0);
        const double reso1 = get(Resonance), reso2 = get(Fil2Reso);
        const double f1Env = bip(Fil1Env), f1Lfo = bip(Fil1Lfo), f1Key = get(Fil1Key);
        const double f2Env = bip(Fil2Env), f2Lfo = bip(Fil2Lfo), f2Key = get(Fil2Key);
        const int r1 = routeOf(RouteOsc1), r2 = routeOf(RouteOsc2), rs = routeOf(RouteSub);
        const bool series = get(FilSeries) > 0.5f;

        const int ls1 = shapeOf(Lfo1Shape), ls2 = shapeOf(Lfo2Shape);
        const double lfo1Inc = lfoInc(Lfo1Rate, Lfo1Sync), lfo2Inc = lfoInc(Lfo2Rate, Lfo2Sync);
        const float lfo1Depth = get(Lfo1Depth), lfo2Depth = get(Lfo2Depth);
        const float gain = get(Gain), modWheel = get(ModWheel);

        // Pitch-bend wheel, in the range the patch declares (1..12 semitones).
        const double bendMul = std::exp2((get(Bend) - 0.5) * 2.0 * std::round(1.0 + get(BendRange) * 11.0) / 12.0);
        // Output pan, equal power.
        const float outPan = (get(OutPan) - 0.5f) * 2.0f;
        const float opL = std::cos((outPan + 1.0f) * 0.25f * (float)kPi);
        const float opR = std::sin((outPan + 1.0f) * 0.25f * (float)kPi);

        // FX chain snapshot: each block has a switch, a character and its amounts.
        const bool driveOn = get(FxDriveOn) > 0.5f, chorusOn = get(FxChorusOn) > 0.5f, reverbOn = get(FxReverbOn) > 0.5f;
        const float drive = (float)std::clamp(get(FxDrive) + macroBlk[4], 0.0, 1.0);
        const int driveMode = std::clamp((int)std::lround(get(FxDriveMode) * 2.0f), 0, 2);
        // Tone is a tilt: below the middle a one-pole darkens, above it lifts the top.
        const float toneCoef = (float)std::clamp((get(FxTone) - 0.5) * 2.0, -1.0, 1.0);
        const float chAmt = get(FxChorus);
        const int chVoices = 1 << std::clamp((int)std::lround(get(FxChorusVoices) * 2.0f), 0, 2);   // 1 / 2 / 4
        const float rvAmt = get(FxReverb);
        const int revMode = std::clamp((int)std::lround(get(FxReverbMode) * 2.0f), 0, 2);
        // Room is short and dry-ish, Hall long and dark, Plate long and bright.
        static const float fbBase[3] = {0.76f, 0.86f, 0.83f}, dampBase[3] = {0.45f, 0.72f, 0.18f};
        const float revFb = std::clamp(fbBase[revMode] + (get(FxReverbSize) - 0.5f) * 0.16f, 0.5f, 0.96f);
        const float revDamp = dampBase[revMode];

        // Matrix active list.
        int amS[kSources * kDests], amD[kSources * kDests], amN = 0; double amA[kSources * kDests];
        for (int s = 0; s < kSources; ++s) for (int d = 0; d < kDests; ++d) { double a = (get((Param)matrixIdx(s, d)) - 0.5) * 2.0; if (std::fabs(a) > 1e-4) { amS[amN] = s; amD[amN] = d; amA[amN] = a; ++amN; } }

        const double invSr = 1.0 / sampleRate_;

        for (int32_t i = 0; i < frames; ++i) {
            if (lfoPhase1_ >= 1.0) { lfoPhase1_ -= 1.0; lfoSh1_ = frand() * 2.0f - 1.0f; }
            if (lfoPhase2_ >= 1.0) { lfoPhase2_ -= 1.0; lfoSh2_ = frand() * 2.0f - 1.0f; }
            const double lfo1 = lfoValue(ls1, lfoPhase1_, lfoSh1_) * lfo1Depth;
            const double lfo2 = lfoValue(ls2, lfoPhase2_, lfoSh2_) * lfo2Depth;
            lfoPhase1_ += lfo1Inc; lfoPhase2_ += lfo2Inc;

            float mixL = 0.0f, mixR = 0.0f; int live = 0;
            for (auto& v : voices_) {
                if (!v.active) continue;
                advEnv(v.env, v.stage, atk, dec, sus, rel); if (v.stage == Stage::Off) { v.active = false; continue; }
                advEnv(v.env2, v.stage2, atk2, dec2, sus2, rel2);
                ++live;

                double dst[kDests] = { macroDst[0], macroDst[1], macroDst[2], macroDst[3], macroDst[4], macroDst[5], macroDst[6] };
                if (amN) {
                    const double src[kSources] = { v.env, v.env2, lfo1, lfo2, v.velocity, std::clamp(v.ktOct / 4.0, -1.0, 1.0), modWheel, v.rand };
                    for (int k = 0; k < amN; ++k) dst[amD[k]] += src[amS[k]] * amA[k];
                }
                const double pMul = std::exp2(dst[0] * 2.0);
                const float pos1 = std::clamp(basePos1 + (float)dst[2], 0.0f, 1.0f);
                const float pos2 = std::clamp(basePos2 + (float)dst[2], 0.0f, 1.0f);
                const double base = v.freq * invSr * pMul * bendMul;

                // --- oscillators (unison stack on Osc1/Osc2, mono sub) ---
                // A spread unison stack is stereo: the mid goes through the filters, the
                // side rides around them, so spread widens without doubling filter state.
                float o1 = 0.0f, o2 = 0.0f, side = 0.0f;
                if (lvl1 > 1e-5f) { float sd; o1 = oscStack(v.ph1, base * m1, b1, pos1, wm1, warp1, uniN, uniDet, uniSpread, sd); side += sd * lvl1; }
                if (lvl2 > 1e-5f) { float sd; o2 = oscStack(v.ph2, base * m2 * std::exp2(dst[1] * 2.0), b2, pos2, wm2, warp2, uniN, uniDet, uniSpread, sd); side += sd * lvl2; }
                float sub = 0.0f;
                if (subLvl > 1e-5f) { sub = subOsc(subWave, v.subPh); v.subPh += base * m1 * subMul; if (v.subPh >= 1.0) v.subPh -= 1.0; }
                o1 *= lvl1; o2 *= lvl2; sub *= subLvl;

                // --- source → filter routing ---
                double in1 = 0, in2 = 0, dryS = 0;
                routeAdd(r1, o1, in1, in2, dryS); routeAdd(r2, o2, in1, in2, dryS); routeAdd(rs, sub, in1, in2, dryS);

                // --- filters (control-rate coeff update) ---
                if (v.modCount-- <= 0) {
                    v.modCount = 15;
                    const double cutMod = dst[3] * 4.0, resoMod = dst[4];
                    double c1 = f1Env * 4.0 * v.env2 + f1Key * v.ktOct + f1Lfo * 2.0 * lfo1 + cutMod;
                    double c2 = f2Env * 4.0 * v.env2 + f2Key * v.ktOct + f2Lfo * 2.0 * lfo2 + cutMod;
                    setSvf(v.f[0], std::clamp(f1base * std::exp2(c1), 20.0, sampleRate_ * 0.49), resoToK(reso1 + resoMod));
                    setSvf(v.f[1], std::clamp(f2base * std::exp2(c2), 20.0, sampleRate_ * 0.49), resoToK(reso2 + resoMod));
                }
                double wet;
                if (series) { double a = svf(v.f[0], v.ic1L[0], v.ic2L[0], in1 + in2, t1type); if (slope1) a = svf(v.f[0], v.ic1R[0], v.ic2R[0], a, t1type); double b = svf(v.f[1], v.ic1L[1], v.ic2L[1], a, t2type); if (slope2) b = svf(v.f[1], v.ic1R[1], v.ic2R[1], b, t2type); wet = b; }
                else { double a = svf(v.f[0], v.ic1L[0], v.ic2L[0], in1, t1type); if (slope1) a = svf(v.f[0], v.ic1R[0], v.ic2R[0], a, t1type); double b = svf(v.f[1], v.ic1L[1], v.ic2L[1], in2, t2type); if (slope2) b = svf(v.f[1], v.ic1R[1], v.ic2R[1], b, t2type); wet = a + b; }
                double s = wet + dryS;

                float amp = v.env * v.velocity * (1.0f + (float)dst[5]);
                float sigL = (float)(s + side) * amp, sigR = (float)(s - side) * amp;
                const float pan = (float)std::clamp(dst[6], -1.0, 1.0);
                mixL += sigL * (pan <= 0 ? 1.0f : 1.0f - pan);
                mixR += sigR * (pan >= 0 ? 1.0f : 1.0f + pan);
            }
            activeVoices_.store(live, std::memory_order_relaxed);

            // --- global FX: drive → chorus → reverb, each on its own switch ---
            float l = mixL * 0.4f, r = mixR * 0.4f;
            if (driveOn && drive > 1e-4f) { l = driveSample(driveMode, l, drive); r = driveSample(driveMode, r, drive); }
            if (driveOn && toneCoef != 0.0f) { l = tone(toneL_, l, toneCoef); r = tone(toneR_, r, toneCoef); }
            if (chorusOn && chAmt > 1e-4f) { float cl, cr; chorus(l, r, chAmt, chVoices, cl, cr); l = cl; r = cr; }
            if (reverbOn && rvAmt > 1e-4f) { float rl, rr; reverb(l, r, revFb, revDamp, rl, rr); l += rl * rvAmt; r += rr * rvAmt; }

            out[i * 2]     += (l * opL) * gain;
            out[i * 2 + 1] += (r * opR) * gain;
        }
    }

private:
    static constexpr double kPi = 3.14159265358979323846, kTwoPi = 6.283185307179586, kHalfPi = kPi * 0.5;
    static constexpr int kBanks = 4, kFrames = 16, kTableSize = 2048, kUnison = 7, kVoices = 16;
    static constexpr int kRevCombs = 4, kRevAll = 2;

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct SvfCoef { double a1 = 0, a2 = 0, a3 = 0, k = 1; };
    struct Voice {
        bool active = false; int32_t pitch = 0; double freq = 0, ktOct = 0, rand = 0;
        double ph1[kUnison] = {}, ph2[kUnison] = {}, subPh = 0;
        float velocity = 0, env = 0, env2 = 0;
        Stage stage = Stage::Attack, stage2 = Stage::Attack;
        SvfCoef f[2];
        double ic1L[2] = {}, ic2L[2] = {}, ic1R[2] = {}, ic2R[2] = {};   // [0]=stage1, [1]=2nd slope stage per filter
        int32_t modCount = 0;
    };

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
    double pitchMul(int oct, int semi, int det) const { int o = (int)std::lround((get(oct) - 0.5) * 6); int s = (int)std::lround((get(semi) - 0.5) * 24); double c = (get(det) - 0.5) * 100; return std::exp2(o + s / 12.0 + c / 1200.0); }

    static void routeAdd(int route, double s, double& in1, double& in2, double& dry) {
        switch (route) { case 0: in1 += s; break; case 1: in2 += s; break; case 2: in1 += s; in2 += s; break; default: dry += s; }
    }
    static void advEnv(float& e, Stage& st, float a, float d, float s, float r) {
        switch (st) { case Stage::Attack: e += a; if (e >= 1) { e = 1; st = Stage::Decay; } break;
            case Stage::Decay: e -= d; if (e <= s) { e = s; st = Stage::Sustain; } break;
            case Stage::Sustain: break; case Stage::Release: e -= r; if (e <= 0) { e = 0; st = Stage::Off; } break; case Stage::Off: break; }
    }

    void setSvf(SvfCoef& c, double fc, double k) const { double g = std::tan(kPi * fc / sampleRate_); c.a1 = 1.0 / (1.0 + g * (g + k)); c.a2 = g * c.a1; c.a3 = g * c.a2; c.k = k; }
    static double svf(const SvfCoef& c, double& ic1, double& ic2, double in, int type) {
        double v3 = in - ic2, v1 = c.a1 * ic1 + c.a2 * v3, v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
        ic1 = 2 * v1 - ic1; ic2 = 2 * v2 - ic2;
        switch (type) { case 1: return in - c.k * v1 - v2; case 2: return v1; case 3: return in - c.k * v1; case 4: return in + (c.k > 0 ? v1 * (2.0 - c.k) : v1); default: return v2; }
    }

    // Warp mode applied to the read phase (Off/Sync/Bend/PWM/Fold — Fold post-processes output).
    static double warpRead(int mode, double phase, double amt) {
        switch (mode) {
            case 1: { double p = phase * (1.0 + amt * 3.0); return p - std::floor(p); }                  // Sync
            case 2: { double d = std::clamp(0.5 + (amt - 0.5) * 0.9, 0.05, 0.95); return phase < d ? 0.5 * phase / d : 0.5 + 0.5 * (phase - d) / (1.0 - d); }  // Bend
            case 3: { double w = 0.05 + amt * 0.9; return phase < w ? 0.5 * phase / w : 0.5 + 0.5 * (phase - w) / (1.0 - w); }  // PWM-ish
            default: return phase;                                                                        // Off / Fold (no phase change)
        }
    }
    static float foldOut(float s, double amt) { float g = 1.0f + (float)amt * 3.0f; return std::sin((float)kHalfPi * g * s); }   // smooth wavefolder

    // One oscillator: a unison stack of wavetable reads, detuned in cents and spread
    // across the stereo field with equal power. Returns the mid (which the filters see)
    // and, through `sideOut`, the half-difference the spread opens up.
    float oscStack(double* ph, double inc, int bank, float pos, int mode, double warpAmt,
                   int uniN, double detCents, float spread, float& sideOut) {
        const float posF = pos * (kFrames - 1); const int f0 = std::clamp((int)posF, 0, kFrames - 1), f1 = std::min(f0 + 1, kFrames - 1); const float ff = posF - f0;
        const float* t0 = frame(bank, f0); const float* t1 = frame(bank, f1);
        float accL = 0.0f, accR = 0.0f;
        // √2 undoes the equal-power law's 0.707 at centre, so a spread of 0 is bit-for-bit
        // the mono stack this used to be.
        const float norm = 1.41421356f / std::sqrt((float)uniN);
        for (int u = 0; u < uniN; ++u) {
            const double d = (uniN > 1) ? ((double)u / (uniN - 1) - 0.5) * 2.0 : 0.0;
            const double det = std::exp2(d * detCents / 1200.0);
            double rp = warpRead(mode, ph[u], warpAmt);
            float s = readMorph(t0, t1, ff, rp);
            if (mode == 4) s = foldOut(s, warpAmt);
            const float pan = (float)d * spread;
            accL += s * std::cos((pan + 1.0f) * 0.25f * (float)kPi);
            accR += s * std::sin((pan + 1.0f) * 0.25f * (float)kPi);
            ph[u] += inc * det; if (ph[u] >= 1.0) ph[u] -= 1.0;
        }
        accL *= norm; accR *= norm;
        sideOut = (accL - accR) * 0.5f;
        return (accL + accR) * 0.5f;
    }
    static float subOsc(int wave, double ph) {
        switch (wave) { case 1: return ph < 0.5 ? 1.0f : -1.0f; case 2: return (float)(4.0 * std::fabs(ph - 0.5) - 1.0); default: return (float)std::sin(kTwoPi * ph); }
    }

    const float* frame(int bank, int f) const { return tables_ + (size_t)((bank * kFrames) + f) * kTableSize; }
    static float readMorph(const float* t0, const float* t1, float ff, double phase) {
        const double x = phase * kTableSize; int i0 = (int)x & (kTableSize - 1); int i1 = (i0 + 1) & (kTableSize - 1);
        const float xf = (float)(x - std::floor(x)); const float a = t0[i0] + (t0[i1] - t0[i0]) * xf; const float b = t1[i0] + (t1[i1] - t1[i0]) * xf; return a + (b - a) * ff;
    }
    static float harmAmp(int bank, float t, int n) {
        switch (bank) {
            case 0: return n == 1 ? 1.0f : t * (1.0f / n);
            case 1: return (n % 2 == 1) ? (n == 1 ? 1.0f : t * (1.0f / n)) : 0.0f;
            case 2: { float peak = 1.0f + t * 16.0f, w = 2.0f + t * 3.0f, x = (n - peak) / w; return std::exp(-x * x) + (n == 1 ? 0.15f : 0.0f); }
            default: return (1.0f / n) * std::fabs(std::cos((float)kPi * n * (0.25f + 0.75f * t)));
        }
    }
    // The four banks × sixteen frames are the same read-only 512 KB for every instance and
    // every clone, so they are built once, on first use, and handed out as a pointer.
    static const float* sharedTables() {
        static const std::vector<float> t = [] {
            std::vector<float> tab((size_t)kBanks * kFrames * kTableSize, 0.0f);
            const int nMax = std::min(kTableSize / 2, 64);
            for (int b = 0; b < kBanks; ++b) for (int f = 0; f < kFrames; ++f) {
                const float tt = (float)f / (kFrames - 1); float* p = &tab[((b * kFrames) + f) * kTableSize]; float peak = 1e-6f;
                // The recipe is fixed per (bank, frame): evaluate it once, not per sample.
                float amp[65];
                for (int n = 1; n <= nMax; ++n) amp[n] = harmAmp(b, tt, n);
                for (int j = 0; j < kTableSize; ++j) {
                    const double ph = (double)j / kTableSize; double acc = 0;
                    for (int n = 1; n <= nMax; ++n) if (amp[n] > 1e-5f) acc += amp[n] * std::sin(2.0 * kPi * n * ph);
                    p[j] = (float)acc; peak = std::max(peak, std::fabs(p[j]));
                }
                const float inv = 1.0f / peak; for (int j = 0; j < kTableSize; ++j) p[j] *= inv;
            }
            return tab;
        }();
        return t.data();
    }

    static double lfoValue(int shape, double ph, float sh) { switch (shape) { case 1: return ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph; case 2: return ph < 0.5 ? 1 : -1; case 3: return sh; default: return std::sin(kTwoPi * ph); } }
    double lfoInc(int rate, int sync) const { int d = std::clamp((int)std::lround(get(sync) * 7.0f), 0, 7); if (d > 0 && spb_ > 0.0) { static const double beats[8] = {0, 4, 2, 1, 0.5, 0.25, 0.125, 0.0625}; return 1.0 / (beats[d] * spb_); } return expMap(get(rate), 0.05, 20.0) / sampleRate_; }

    // FX · drive. Tube is the soft symmetric tanh this always had; Tape adds the second
    // harmonic a tape stage leans on; Fold turns the curve back on itself past the top.
    static float driveSample(int mode, float x, float amt) {
        const float d = 1.0f + amt * 8.0f;
        switch (mode) {
            // Tape: a rational curve that bends earlier and saturates more slowly than tanh,
            // with a second-harmonic term for the asymmetry a tape stage has.
            case 1: { float u = x * d + 0.35f * x * x * d; float y = u / (1.0f + std::fabs(u)); return y / (1.0f - 1.0f / (d * 0.7f + 2.0f)); }
            case 2: { float y = std::sin((float)kHalfPi * std::clamp(x * d, -3.0f, 3.0f)); return y * 0.8f; }     // Fold
            default: return std::tanh(x * d) / std::tanh(d * 0.7f + 1.0f);                                        // Tube
        }
    }
    // A tilt around the middle: negative darkens (one-pole low-pass), positive lifts the
    // top (the signal plus what the low-pass throws away).
    static float tone(float& z, float x, float coef) {
        const float a = 0.12f + 0.55f * (1.0f - std::fabs(coef));
        z += (x - z) * a;
        return coef < 0 ? z + (x - z) * (1.0f + coef) : x + (x - z) * coef * 1.4f;
    }

    // FX · a stereo chorus of 1, 2 or 4 modulated taps, and a compact Freeverb whose
    // feedback and damping the mode + size choose.
    void chorus(float l, float r, float amt, int voices, float& outL, float& outR) {
        const int sz = (int)chorusBuf_.size(); if (sz < 4) { outL = l; outR = r; return; }
        chorusBuf_[chorusW_] = (l + r) * 0.5f;
        const double rate = expMap(get(FxChorusRate), 0.1, 6.0); chorusPh_ += rate / sampleRate_; if (chorusPh_ >= 1.0) chorusPh_ -= 1.0;
        auto tap = [&](double phase) { double d = (0.008 + 0.006 * std::sin(kTwoPi * (phase - std::floor(phase)))) * sampleRate_; double rp = chorusW_ - d; while (rp < 0) rp += sz; int i0 = (int)rp; int i1 = (i0 + 1) % sz; float f = (float)(rp - i0); return chorusBuf_[i0] + (chorusBuf_[i1] - chorusBuf_[i0]) * f; };
        float a = 0.0f, b = 0.0f;
        // Taps sit evenly around the LFO cycle; odd ones go left, even ones right.
        for (int k = 0; k < voices; ++k) { float t = tap(chorusPh_ + (double)k / voices); if (k % 2 == 0) a += t; else b += t; }
        const float na = 1.0f / std::max(1, (voices + 1) / 2), nb = voices > 1 ? 1.0f / (voices / 2) : 1.0f;
        a *= na; b = voices > 1 ? b * nb : a;
        if (++chorusW_ >= sz) chorusW_ = 0;
        outL = l * (1 - amt * 0.5f) + a * amt; outR = r * (1 - amt * 0.5f) + b * amt;
    }
    void reverb(float l, float r, float fb, float damp, float& outL, float& outR) {
        const float in = (l + r) * 0.015f; float o = 0.0f;
        for (auto& c : revC_) { float out = c.buf[c.idx]; c.store = out * (1.0f - damp) + c.store * damp; c.buf[c.idx] = in + c.store * fb; if (++c.idx >= (int)c.buf.size()) c.idx = 0; o += out; }
        for (auto& a : revA_) { float bo = a.buf[a.idx]; float out = -o + bo; a.buf[a.idx] = o + bo * 0.5f; if (++a.idx >= (int)a.buf.size()) a.idx = 0; o = out; }
        outL = o; outR = o;
    }

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(int p, double lo, double hi) const { return (float)(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float frand() { rng_ = rng_ * 1664525u + 1013904223u; return (rng_ >> 8) / 16777216.0f; }
    Voice* findFreeVoice() { for (auto& v : voices_) if (!v.active) return &v; Voice* q = &voices_[0]; for (auto& v : voices_) if (v.env < q->env) q = &v; return q; }

    struct Comb { std::vector<float> buf; int idx = 0; float store = 0; };
    struct Allp { std::vector<float> buf; int idx = 0; };

    Voice voices_[kVoices];
    double sampleRate_ = 44100.0, spb_ = 0.0;
    double lfoPhase1_ = 0, lfoPhase2_ = 0, chorusPh_ = 0;
    float lfoSh1_ = 0, lfoSh2_ = 0;
    uint32_t rng_ = 0x51ed270bu;
    const float* tables_ = nullptr;   // shared, read-only (sharedTables)
    std::vector<float> chorusBuf_; int chorusW_ = 0;
    float toneL_ = 0, toneR_ = 0;
    Comb revC_[kRevCombs]; Allp revA_[kRevAll];
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
