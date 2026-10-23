// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Volt implementation. Voice-based
// auto-sleep; LFO + osc sines via fast_math::sin, macro-rate + control-rate
// cutoff via fast_math::exp2f. Vibrato, pitch, tuning, filter-coefficient and
// envelope paths stay std-exact (fast-sin there cost -56 dB — reverted).

#include "VoltSynth.h"

#include "dsp/math/FastMath.h"

namespace nota {

VoltSynth::VoltSynth() {
    // Classic init: two detuned saws → Fil1 low-pass with filter-env motion, a
    // little unison, snappy amp env. Osc2 sits on Fil1 too; Fil2 idle by default.
    set(Osc1Wave, 0.0f);
    set(Osc1Octave, 0.5f);
    set(Osc1Semi, 0.5f);
    set(Osc1Detune, 0.5f);
    set(Osc1Level, 0.85f);
    set(Osc1Route, 0.0f);
    set(Osc2Wave, 0.0f);
    set(Osc2Octave, 0.5f);
    set(Osc2Semi, 0.5f);
    set(Osc2Detune, 0.55f);
    set(Osc2Level, 0.70f);
    set(Osc2Route, 0.0f);
    set(NoiseLevel, 0.0f);
    set(NoiseColor, 0.6f);
    set(NoiseRoute, 0.0f);
    set(Fil1Type, 0.0f);
    set(Fil1Freq, 0.55f);
    set(Fil1Reso, 0.18f);
    set(Fil1EnvAmt, 0.66f);
    set(Fil1LfoAmt, 0.5f);
    set(Fil1KeyAmt, 0.3f);
    set(Fil1ToF2, 0.0f);
    set(Fil2Type, 0.0f);
    set(Fil2Freq, 0.7f);
    set(Fil2Reso, 0.15f);
    set(Fil2EnvAmt, 0.5f);
    set(Fil2LfoAmt, 0.5f);
    set(Fil2KeyAmt, 0.3f);
    set(FAttack, 0.02f);
    set(FDecay, 0.4f);
    set(FSustain, 0.25f);
    set(FRelease, 0.3f);
    set(Amp1Pan, 0.5f);
    set(Amp1Level, 0.85f);
    set(Amp2Pan, 0.5f);
    set(Amp2Level, 0.0f);
    set(Attack, 0.02f);
    set(Decay, 0.35f);
    set(Sustain, 0.8f);
    set(Release, 0.3f);
    set(Lfo1Rate, 0.42f);
    set(Lfo2Rate, 0.3f);
    set(Volume, 0.8f);
    set(VibRate, 0.4f);
    set(VibAmt, 0.0f);
    set(Unison, 0.25f);
    set(Glide, 0.0f);
    set(VelAmp, 0.4f);
    set(VelFilter, 0.3f);
    // Appended defaults: neutral so old projects (49-float state) sound unchanged.
    set(Osc1Phase, 0.0f);
    set(Osc2Phase, 0.0f); // 0 = free running
    set(Lfo1Depth, 0.5f);
    set(Lfo1Shape, 0.0f);
    set(Lfo1Sync, 0.0f);
    set(Lfo1Fade, 0.0f);
    set(Lfo2Depth, 0.5f);
    set(Lfo2Shape, 0.0f);
    set(Lfo2Sync, 0.0f);
    set(Lfo2Fade, 0.0f);
    set(Fil1Slope, 0.0f);
    set(Fil2Slope, 0.0f); // 0 = 12 dB
    set(ModWheel, 0.0f);
    set(MonoMode, 0.0f);
    set(OutPan, 0.5f);
    for (int s = 0; s < kSources; ++s)
        for (int d = 0; d < kDests; ++d) set((Param)matrixIdx(s, d), 0.5f); // bipolar zero
    for (int m = 0; m < kMacros; ++m) {
        set((Param)macroIdx(m, 0), 0.0f);
        set((Param)macroIdx(m, 1), 0.0f);
        set((Param)macroIdx(m, 2), 0.5f);
    }
    set(Bend, 0.5f);              // centred
    set(BendRange, 1.0f / 11.0f); // ±2 semitones
    set(VibWheel, 0.0f);          // vibrato at its own depth, not the wheel's
    set(Osc1Shape, 0.0f);
    set(Osc2Shape, 0.0f); // the wave chip as it is
    set(Osc1Pw, 0.5f);
    set(Osc2Pw, 0.5f); // a plain square
    set(FilDrive, 0.0f);
    set(Drive, 0.0f);     // clean
    set(UniVoices, 0.0f); // 1: one detuned partner (the Unison knob)
    set(UniSpread, 0.35f);
    set(Legato, 1.0f); // Mono slides, as Volt's mono always did
    set(WheelLfo, 0.0f);
}

void VoltSynth::controller(int32_t ctl, float value) {
    if (ctl == kCtlModWheel) pn_[ModWheel].store(std::clamp(value, 0.0f, 1.0f), std::memory_order_relaxed);
    Instrument::controller(ctl, value);
    wake();
}

void VoltSynth::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        gexpr_.set(dim, value);
        return;
    }
    float add[kExprDims];
    gexpr_.offsets(static_cast<float>(wheelRange()), add);
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.aStage != Stage::Release && v.aStage != Stage::Off)
            v.ex.set(dim, value, add);
}

std::string VoltSynth::pluginParamId(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    if (i < MatrixBase) {
        static const char* ids[] = {"osc1wave", "osc1octave", "osc1semi", "osc1detune", "osc1level", "osc1route",
            "osc2wave", "osc2octave", "osc2semi", "osc2detune", "osc2level", "osc2route", "noise", "noisecolor",
            "noiseroute", "fil1type", "fil1freq", "fil1reso", "fil1env", "fil1lfo", "fil1key", "fil1tof2", "fil2type",
            "fil2freq", "fil2reso", "fil2env", "fil2lfo", "fil2key", "fattack", "fdecay", "fsustain", "frelease",
            "amp1pan", "amp1level", "amp2pan", "amp2level", "attack", "decay", "sustain", "release", "lfo1rate",
            "lfo2rate", "volume", "vibrate", "vibamt", "unison", "glide", "velamp", "velfilter", "osc1phase",
            "osc2phase", "lfo1depth", "lfo1shape", "lfo1sync", "lfo1fade", "lfo2depth", "lfo2shape", "lfo2sync",
            "lfo2fade", "fil1slope", "fil2slope", "modwheel", "mono", "outpan"};
        return std::string(ids[i]);
    }
    if (i < MacroBase) {
        int j = i - MatrixBase;
        return "mtx" + std::to_string(j / kDests) + "_" + std::to_string(j % kDests);
    }
    if (i >= ExtraBase) {
        static const char* ex[] = {"bend", "bendrange", "vibwheel", "osc1shape", "osc2shape", "osc1pw", "osc2pw",
            "fildrive", "drive", "univoices", "unispread", "legato", "wheellfo"};
        return std::string(ex[i - ExtraBase]);
    }
    int j = i - MacroBase, m = j / 3, f = j % 3;
    return "mac" + std::to_string(m) + (f == 0 ? "val" : f == 1 ? "dest" : "amt");
}

std::string VoltSynth::pluginParamName(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    if (i < MatrixBase) {
        static const char* nm[] = {"Osc1 Wave", "Osc1 Octave", "Osc1 Semi", "Osc1 Detune", "Osc1 Level", "Osc1 →Filter",
            "Osc2 Wave", "Osc2 Octave", "Osc2 Semi", "Osc2 Detune", "Osc2 Level", "Osc2 →Filter", "Noise",
            "Noise Color", "Noise →Filter", "Filter 1 Type", "Filter 1 Freq", "Filter 1 Reso", "Filter 1 Env",
            "Filter 1 LFO", "Filter 1 Key", "To Filter 2", "Filter 2 Type", "Filter 2 Freq", "Filter 2 Reso",
            "Filter 2 Env", "Filter 2 LFO", "Filter 2 Key", "Filter Attack", "Filter Decay", "Filter Sustain",
            "Filter Release", "Amp1 Pan", "Amp1 Level", "Amp2 Pan", "Amp2 Level", "Attack", "Decay", "Sustain",
            "Release", "LFO1 Rate", "LFO2 Rate", "Volume", "Vibrato Rate", "Vibrato", "Unison", "Glide", "Vel→Amp",
            "Vel→Filter", "Osc1 Phase", "Osc2 Phase", "LFO1 Depth", "LFO1 Shape", "LFO1 Sync", "LFO1 Fade",
            "LFO2 Depth", "LFO2 Shape", "LFO2 Sync", "LFO2 Fade", "Filter 1 Slope", "Filter 2 Slope", "Mod Wheel",
            "Mono Mode", "Out Pan"};
        return std::string(nm[i]);
    }
    static const char* srcNm[] = {"Amp Env", "Flt Env", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl"};
    static const char* dstNm[] = {"Pitch", "Osc2 Pitch", "Cutoff", "Reso", "Level", "Pan"};
    if (i < MacroBase) {
        int j = i - MatrixBase;
        return std::string(srcNm[j / kDests]) + " → " + dstNm[j % kDests];
    }
    if (i >= ExtraBase) {
        static const char* exNm[] = {"Pitch Bend", "Bend Range", "Vibrato → Wheel", "Osc1 Shape", "Osc2 Shape",
            "Osc1 Pulse Width", "Osc2 Pulse Width", "Filter Drive", "Drive", "Unison Voices", "Unison Spread", "Legato",
            "Wheel → LFO"};
        return std::string(exNm[i - ExtraBase]);
    }
    int j = i - MacroBase, m = j / 3, f = j % 3;
    return "Macro " + std::to_string(m + 1) + (f == 0 ? "" : f == 1 ? " Dest" : " Amount");
}

int32_t VoltSynth::pluginParamIndexOfId(const std::string& id) const {
    for (int32_t i = 0; i < kNumParams; ++i)
        if (pluginParamId(i) == id) return i;
    return -1;
}

std::vector<uint8_t> VoltSynth::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void VoltSynth::setState(const uint8_t* data, int32_t size) {
    if (!data) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + i * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
}

std::shared_ptr<Instrument> VoltSynth::clone() const {
    auto s = std::make_shared<VoltSynth>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(sampleRate_);
    return s;
}

void VoltSynth::noteOn(int32_t pitch, float velocity) {
    wake();
    const bool mono = get(MonoMode) > 0.5f;
    const bool glideOn = pn_[Glide].load(std::memory_order_relaxed) > 0.001f;

    const bool hadHeld = heldCount_ > 0; // another key already down → an overlap
    pushHeld(pitch);
    if (mono) {
        Voice* v = &voices_[0];
        // Legato slides only over a key that is still held, on a voice still sounding; a
        // note struck on a releasing voice is a fresh attack (before, it faded out).
        const bool sounding = v->active && v->aStage != Stage::Release && v->aStage != Stage::Off;
        const bool legato = get(Legato) >= 0.5f && hadHeld && sounding;
        v->pitch = pitch;
        v->ktOct = (pitch - 60) / 12.0;
        v->freqTarget = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        resetExpr(*v);
        if (!legato) {
            v->freqCur = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freqTarget;
            startVoice(*v, pitch, velocity);
        } else if (!glideOn)
            v->freqCur = v->freqTarget; // legato, no glide: jump pitch, keep envelopes
        v->active = true;
        lastFreq_ = v->freqTarget;
        return;
    }

    Voice* v = findFreeVoice();
    v->pitch = pitch;
    v->freqTarget = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
    resetExpr(*v);
    v->freqCur = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freqTarget;
    startVoice(*v, pitch, velocity);
    v->active = true;
    lastFreq_ = v->freqTarget;
}

void VoltSynth::noteOff(int32_t pitch) {
    wake();
    const bool wasHeld = popHeld(pitch);
    if (get(MonoMode) > 0.5f && wasHeld) {
        Voice& v = voices_[0];
        if (heldCount_ == 0) {
            if (v.active && v.aStage != Stage::Release) {
                v.aStage = Stage::Release;
                v.fStage = Stage::Release;
            }
            return;
        }
        // Back to the newest key still down — no new attack, it's one phrase.
        const int32_t top = held_[heldCount_ - 1];
        if (top != v.pitch) {
            v.pitch = top;
            v.ktOct = (top - 60) / 12.0;
            v.freqTarget = 440.0 * std::pow(2.0, (top - 69) / 12.0);
            lastFreq_ = v.freqTarget;
        }
        return;
    }
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.aStage != Stage::Release) {
            v.aStage = Stage::Release;
            v.fStage = Stage::Release;
        }
}

void VoltSynth::render(float* out, int32_t frames) {
    // ---- macros first: six of the twelve destinations move the block snapshot
    //      below (levels, Filter 2, LFO rates), the other six ride the voice bus ----
    double macroDst[kDests] = {0, 0, 0, 0, 0, 0};
    double macroBlk[6] = {0, 0, 0, 0, 0, 0}; // osc1 lvl · osc2 lvl · noise · fil2 · lfo1 rate · lfo2 rate
    for (int m = 0; m < kMacros; ++m) {
        const double x = get((Param)macroIdx(m, 0)) * (get((Param)macroIdx(m, 2)) - 0.5) * 2.0;
        if (std::fabs(x) <= 1e-4) continue;
        switch (macroDestOf(get((Param)macroIdx(m, 1)))) {
        case 0: macroDst[0] += x; break;  // Pitch
        case 2: macroDst[1] += x; break;  // Osc 2 Pitch
        case 4: macroDst[2] += x; break;  // Cutoff
        case 7: macroDst[3] += x; break;  // Reso
        case 9: macroDst[4] += x; break;  // Level
        case 11: macroDst[5] += x; break; // Pan
        case 1: macroBlk[0] += x; break;  // Osc 1 Level
        case 3: macroBlk[1] += x; break;  // Osc 2 Level
        case 5: macroBlk[2] += x; break;  // Noise Level
        case 6: macroBlk[3] += x; break;  // Filter 2 cutoff
        case 8: macroBlk[4] += x; break;  // LFO 1 rate
        default: macroBlk[5] += x; break; // LFO 2 rate
        }
    }

    // ---- per-block param snapshot ----
    const int w1 = waveOf(Osc1Wave), w2 = waveOf(Osc2Wave);
    const double mult1 = pitchMult(Osc1Octave, Osc1Semi, Osc1Detune);
    const double mult2 = pitchMult(Osc2Octave, Osc2Semi, Osc2Detune);
    const float lvl1 = (float)std::clamp(get(Osc1Level) + macroBlk[0], 0.0, 2.0);
    const float lvl2 = (float)std::clamp(get(Osc2Level) + macroBlk[1], 0.0, 2.0);
    const double r1 = get(Osc1Route), r2 = get(Osc2Route), rn = get(NoiseRoute);
    const float noiseLvl = (float)std::clamp(get(NoiseLevel) + macroBlk[2], 0.0, 2.0);
    const double nColor = std::clamp((double)get(NoiseColor), 0.0, 1.0);
    const double nCoef = 0.02 + 0.9 * nColor; // one-pole toward white as color rises

    const int t1 = filtTypeOf(Fil1Type), t2 = filtTypeOf(Fil2Type);
    const bool slope1 = get(Fil1Slope) > 0.5f, slope2 = get(Fil2Slope) > 0.5f; // true = 24 dB (cascade)
    const double f1base = expMap(get(Fil1Freq), 20.0, 18000.0);
    const double f2base = expMap(std::clamp(get(Fil2Freq) + macroBlk[3], 0.0, 1.0), 20.0, 18000.0);
    const double reso1 = get(Fil1Reso), reso2 = get(Fil2Reso);
    const double f1Env = bip(Fil1EnvAmt), f1Lfo = bip(Fil1LfoAmt), f1Key = get(Fil1KeyAmt), toF2 = get(Fil1ToF2);
    const double f2Env = bip(Fil2EnvAmt), f2Lfo = bip(Fil2LfoAmt), f2Key = get(Fil2KeyAmt);

    const float fA = rateOf(FAttack, 0.001, 2.0), fD = rateOf(FDecay, 0.002, 4.0), fR = rateOf(FRelease, 0.002, 5.0);
    const float fS = get(FSustain);
    const float aA = rateOf(Attack, 0.001, 2.0), aD = rateOf(Decay, 0.002, 4.0), aR = rateOf(Release, 0.002, 5.0);
    const float aS = get(Sustain);

    const float p1 = bipF(Amp1Pan), p2 = bipF(Amp2Pan);
    const float p1L = std::cos((p1 + 1.0f) * 0.25f * (float)kPi), p1R = std::sin((p1 + 1.0f) * 0.25f * (float)kPi);
    const float p2L = std::cos((p2 + 1.0f) * 0.25f * (float)kPi), p2R = std::sin((p2 + 1.0f) * 0.25f * (float)kPi);
    const float l1 = get(Amp1Level), l2 = get(Amp2Level);

    // LFOs: shape + depth + optional tempo sync.
    const int lfo1shape = shapeOf(Lfo1Shape), lfo2shape = shapeOf(Lfo2Shape);
    // A macro on an LFO's rate scales its increment, so it works synced as well as free.
    const double lfo1Inc = lfoInc(Lfo1Rate, Lfo1Sync) * fast_math::exp2f((float)(macroBlk[4] * 2.0));
    const double lfo2Inc = lfoInc(Lfo2Rate, Lfo2Sync) * fast_math::exp2f((float)(macroBlk[5] * 2.0));
    const float lfo1Depth = get(Lfo1Depth), lfo2Depth = get(Lfo2Depth);
    const float fade1Inc = fadeInc(Lfo1Fade), fade2Inc = fadeInc(Lfo2Fade);

    const float modWheel = get(ModWheel);
    const double vibInc = expMap(get(VibRate), 0.1, 12.0) / sampleRate_;
    // "By the wheel": the vibrato depth the patch dials becomes the wheel's ceiling.
    const double vibCents = get(VibAmt) * 50.0 * (get(VibWheel) > 0.5f ? (double)modWheel : 1.0);
    const double uni = get(Unison);
    const double wheelLfo = get(WheelLfo) * modWheel * 2.0; // the mod wheel opens the LFOs onto the cutoffs
    const double uniDet = std::exp2(uni * 25.0 / 1200.0);   // side-osc detune (up to ~25 cents)
    const float uniLvl = (float)(uni * 0.9);
    // v6: per-oscillator shape morph and pulse width, the two drives, the unison stack.
    const double shp1 = get(Osc1Shape), shp2 = get(Osc2Shape);
    const double pw1 = pwOf(Osc1Pw), pw2 = pwOf(Osc2Pw);
    const double fDrv = 1.0 + get(FilDrive) * 11.0; // 1..12, before both filters
    const bool fDrive = fDrv > 1.001;
    const double fDrvNorm = fDrive ? 1.0 / std::tanh(fDrv) : 1.0;
    const double oDrv = 1.0 + get(Drive) * 7.0; // 1..8, on the output
    const bool oDrive = oDrv > 1.001;
    const double oDrvNorm = oDrive ? 1.0 / std::tanh(oDrv) : 1.0;
    const int nUni = uniCount();
    const double spread = get(UniSpread);
    const bool stereo = nUni > 1 && spread > 0.001; // copies panned apart → a filter per side
    double uDet[kMaxStack], uL[kMaxStack], uR[kMaxStack];
    for (int u = 0; u < nUni; ++u) {
        // The stack spreads across ±(Unison × 50) cents; a centred copy stays at unity gain.
        const double t = nUni == 1 ? 0.0 : 2.0 * u / (nUni - 1) - 1.0;
        uDet[u] = std::exp2(uni * 50.0 * t / 1200.0);
        const double p = t * spread;
        uL[u] = std::sqrt(1.0 - p);
        uR[u] = std::sqrt(1.0 + p);
    }
    const double uNorm = 1.0 / std::sqrt((double)nUni);
    const double glide = get(Glide);
    const double glideCoef = glide > 0.001 ? (1.0 - std::exp(-1.0 / (expMap(glide, 0.005, 0.6) * sampleRate_))) : 1.0;
    const float velAmp = get(VelAmp), velFilt = get(VelFilter);
    const float volume = get(Volume);
    const float outPan = bipF(OutPan);
    const float opL = std::cos((outPan + 1.0f) * 0.25f * (float)kPi),
                opR = std::sin((outPan + 1.0f) * 0.25f * (float)kPi);

    // Pitch-bend wheel, in the range the patch declares (1..12 semitones).
    const double bendMul = std::exp2((get(Bend) - 0.5) * 2.0 * wheelRange() / 12.0);
    float exAdd[kExprDims];
    gexpr_.offsets(static_cast<float>(wheelRange()), exAdd);

    // ---- modulation matrix: compact list of active (src,dst,amount) ----
    int amS[kSources * kDests], amD[kSources * kDests], amCount = 0;
    double amA[kSources * kDests];
    for (int s = 0; s < kSources; ++s)
        for (int d = 0; d < kDests; ++d) {
            double a = (get((Param)matrixIdx(s, d)) - 0.5) * 2.0;
            if (std::fabs(a) > 1e-4) {
                amS[amCount] = s;
                amD[amCount] = d;
                amA[amCount] = a;
                ++amCount;
            }
        }
    const bool pitchMod =
        amActive(amD, amCount, 0) || amActive(amD, amCount, 1) || macroDst[0] != 0.0 || macroDst[1] != 0.0;
    const bool panMod = amActive(amD, amCount, 5) || macroDst[5] != 0.0;
    const bool lvlMod = amActive(amD, amCount, 4) || macroDst[4] != 0.0;

    const double invSr = 1.0 / sampleRate_;

    for (int32_t i = 0; i < frames; ++i) {
        if (lfoPhase1_ >= 1.0) {
            lfoPhase1_ -= 1.0;
            lfoSh1_ = noise() * 2.0f - 1.0f;
        }
        if (lfoPhase2_ >= 1.0) {
            lfoPhase2_ -= 1.0;
            lfoSh2_ = noise() * 2.0f - 1.0f;
        }
        const double lfo1 = lfoValue(lfo1shape, lfoPhase1_, lfoSh1_);
        const double lfo2 = lfoValue(lfo2shape, lfoPhase2_, lfoSh2_);
        lfoPhase1_ += lfo1Inc;
        lfoPhase2_ += lfo2Inc;
        // Vibrato kept std-exact (reverted: fast-sin cost -56 dB).
        const double vibv = std::sin(kTwoPi * vibPhase_);
        vibPhase_ += vibInc;
        if (vibPhase_ >= 1.0) vibPhase_ -= 1.0;
        const double vibMult = vibCents > 0.0 ? std::exp2(vibCents * vibv / 1200.0) : 1.0;

        // Shared noise source (one-pole "color" toward white).
        const float white = noise() * 2.0f - 1.0f;
        nlp_ += (float)nCoef * (white - nlp_);
        const float nz = (float)(nlp_ * (1.0 - nColor) + white * nColor) * noiseLvl;

        float outL = 0.0f, outR = 0.0f;
        for (auto& v : voices_) {
            if (!v.active) continue;
            advanceEnv(v.aEnv, v.aStage, aA, aD, aS, aR);
            if (v.aStage == Stage::Off) {
                v.active = false;
                continue;
            }
            advanceEnv(v.fEnv, v.fStage, fA, fD, fS, fR);
            if (v.fade1 < 1.0f) v.fade1 = std::min(1.0f, v.fade1 + fade1Inc);
            if (v.fade2 < 1.0f) v.fade2 = std::min(1.0f, v.fade2 + fade2Inc);

            // Modulation sources → destinations for this voice/sample.
            double dst[kDests] = {macroDst[0], macroDst[1], macroDst[2], macroDst[3], macroDst[4], macroDst[5]};
            if (amCount) {
                const double src[kSources] = {v.aEnv, v.fEnv, lfo1 * lfo1Depth * v.fade1, lfo2 * lfo2Depth * v.fade2,
                    v.vel, std::clamp(v.ktOct / 4.0, -1.0, 1.0), modWheel};
                for (int k = 0; k < amCount; ++k) dst[amD[k]] += src[amS[k]] * amA[k];
            }

            if (glideCoef < 1.0) v.freqCur += (v.freqTarget - v.freqCur) * glideCoef;
            else
                v.freqCur = v.freqTarget;
            v.ex.tick(exAdd, exprCoef_);
            if (v.ex.bend() != v.exBendAt) {
                v.exBendAt = v.ex.bend();
                v.exBendMul = std::exp2(v.exBendAt / 12.0);
            }
            double base = v.freqCur * vibMult * bendMul * v.exBendMul * invSr;
            double m1 = mult1, m2 = mult2;
            if (pitchMod) {
                const double pm = std::exp2(dst[0] * 2.0); // ±24 semitones at full
                m1 *= pm;
                m2 *= pm * std::exp2(dst[1] * 2.0);
            }

            const double i1 = base * m1, i2 = base * m2;
            double s1, s2, s1r = 0.0, s2r = 0.0; // left (or mono) and right
            if (nUni == 1) {
                // One detuned partner per oscillator, scaled by the Unison knob.
                const double i1u = i1 * uniDet, i2u = i2 * uniDet;
                s1 = (osc(w1, shp1, pw1, v.ph1, i1) + osc(w1, shp1, pw1, v.ph1u, i1u) * uniLvl);
                s2 = (osc(w2, shp2, pw2, v.ph2, i2) + osc(w2, shp2, pw2, v.ph2u, i2u) * uniLvl);
                s1 *= lvl1 / (1.0 + uniLvl);
                s2 *= lvl2 / (1.0 + uniLvl);
                v.ph1 += i1;
                if (v.ph1 >= 1.0) v.ph1 -= 1.0;
                v.ph1u += i1u;
                if (v.ph1u >= 1.0) v.ph1u -= 1.0;
                v.ph2 += i2;
                if (v.ph2 >= 1.0) v.ph2 -= 1.0;
                v.ph2u += i2u;
                if (v.ph2u >= 1.0) v.ph2u -= 1.0;
            } else {
                // A stack of nUni copies per oscillator, detuned across ±cents, panned by Spread.
                double aL = 0.0, aR = 0.0, bL = 0.0, bR = 0.0;
                for (int u = 0; u < nUni; ++u) {
                    const double d1 = i1 * uDet[u], d2 = i2 * uDet[u];
                    const double x1 = osc(w1, shp1, pw1, v.st1[u], d1);
                    const double x2 = osc(w2, shp2, pw2, v.st2[u], d2);
                    v.st1[u] += d1;
                    if (v.st1[u] >= 1.0) v.st1[u] -= 1.0;
                    v.st2[u] += d2;
                    if (v.st2[u] >= 1.0) v.st2[u] -= 1.0;
                    aL += x1 * uL[u];
                    aR += x1 * uR[u];
                    bL += x2 * uL[u];
                    bR += x2 * uR[u];
                }
                s1 = aL * uNorm * lvl1;
                s1r = aR * uNorm * lvl1;
                s2 = bL * uNorm * lvl2;
                s2r = bR * uNorm * lvl2;
            }

            double in1 = s1 * (1.0 - r1) + s2 * (1.0 - r2) + nz * (1.0 - rn);
            double in2 = s1 * r1 + s2 * r2 + nz * rn;
            double in1r = 0.0, in2r = 0.0;
            if (stereo) {
                in1r = s1r * (1.0 - r1) + s2r * (1.0 - r2) + nz * (1.0 - rn);
                in2r = s1r * r1 + s2r * r2 + nz * rn;
            }
            if (fDrive) {
                in1 = std::tanh(in1 * fDrv) * fDrvNorm;
                in2 = std::tanh(in2 * fDrv) * fDrvNorm;
                if (stereo) {
                    in1r = std::tanh(in1r * fDrv) * fDrvNorm;
                    in2r = std::tanh(in2r * fDrv) * fDrvNorm;
                }
            }

            // Control-rate filter coefficients (cutoff = base × 2^mod, incl. matrix).
            if (v.modCount-- <= 0) {
                v.modCount = 15;
                const double velF = 1.0f - velFilt + velFilt * v.vel;
                const double cutMod = dst[2] * 4.0                                  // matrix Cutoff (±4 oct)
                                      + v.ex.slide() * 2.0 + v.ex.pressure() * 2.0; // MPE
                double o1 = f1Env * 4.0 * v.fEnv * velF + f1Key * v.ktOct + (f1Lfo * 2.0 + wheelLfo) * lfo1 + cutMod;
                double o2 = f2Env * 4.0 * v.fEnv * velF + f2Key * v.ktOct + (f2Lfo * 2.0 + wheelLfo) * lfo2 + cutMod;
                const double k1 = resoToK(reso1 + dst[3]);
                const double k2 = resoToK(reso2 + dst[3]);
                v.k1 = k1;
                v.k2 = k2;
                setSvf(v.f1a1, v.f1a2, v.f1a3,
                    std::clamp(f1base * fast_math::exp2f((float)o1), 20.0, sampleRate_ * 0.49), k1);
                setSvf(v.f2a1, v.f2a2, v.f2a3,
                    std::clamp(f2base * fast_math::exp2f((float)o2), 20.0, sampleRate_ * 0.49), k2);
            }
            double o1 = svf(v.s1, v.f1a1, v.f1a2, v.f1a3, v.k1, in1, t1);
            if (slope1) o1 = svf(v.s1b, v.f1a1, v.f1a2, v.f1a3, v.k1, o1, t1);
            in2 += o1 * toF2;
            double o2 = svf(v.s2, v.f2a1, v.f2a2, v.f2a3, v.k2, in2, t2);
            if (slope2) o2 = svf(v.s2b, v.f2a1, v.f2a2, v.f2a3, v.k2, o2, t2);
            double o1r = o1, o2r = o2;
            if (stereo) {
                o1r = svf(v.s1R, v.f1a1, v.f1a2, v.f1a3, v.k1, in1r, t1);
                if (slope1) o1r = svf(v.s1bR, v.f1a1, v.f1a2, v.f1a3, v.k1, o1r, t1);
                in2r += o1r * toF2;
                o2r = svf(v.s2R, v.f2a1, v.f2a2, v.f2a3, v.k2, in2r, t2);
                if (slope2) o2r = svf(v.s2bR, v.f2a1, v.f2a2, v.f2a3, v.k2, o2r, t2);
            }

            float ampG = v.aEnv * (1.0f - velAmp + velAmp * v.vel) * (1.0f + 0.5f * v.ex.pressure());
            if (lvlMod) ampG *= (float)std::clamp(1.0 + dst[4], 0.0, 4.0);
            const float a1 = (float)o1 * l1 * ampG;
            const float a2 = (float)o2 * l2 * ampG;
            float vL = a1 * p1L + a2 * p2L;
            float vR = stereo ? (float)o1r * l1 * ampG * p1R + (float)o2r * l2 * ampG * p2R : a1 * p1R + a2 * p2R;
            if (panMod) {
                const float pm = (float)std::clamp(dst[5], -1.0, 1.0); // linear balance atop static pan
                vL *= pm <= 0.0f ? 1.0f : 1.0f - pm;
                vR *= pm >= 0.0f ? 1.0f : 1.0f + pm;
            }
            outL += vL;
            outR += vR;
        }
        if (oDrive) {
            // Bass's output saturation, measured against a centred voice (amp pan −3 dB).
            outL = (float)(std::tanh(outL * kSqrt2 * oDrv) * oDrvNorm / kSqrt2);
            outR = (float)(std::tanh(outR * kSqrt2 * oDrv) * oDrvNorm / kSqrt2);
        }
        const float mL = outL * volume * 0.5f, mR = outR * volume * 0.5f;
        out[i * 2] += mL * opL * kSqrt2;
        out[i * 2 + 1] += mR * opR * kSqrt2;
    }
    activeVoices_.store(countActive(), std::memory_order_relaxed);
    // Sleep: with no sounding voice the loop above accumulated exact zeros.
    idle_ = (countActive() == 0);
}

void VoltSynth::wake() {
    idle_ = false;
}

void VoltSynth::resetExpr(Voice& v) {
    float add[kExprDims];
    gexpr_.offsets(static_cast<float>(wheelRange()), add);
    v.ex.reset(add);
    v.exBendAt = 0.0f;
    v.exBendMul = 1.0;
}

void VoltSynth::startVoice(Voice& v, int32_t pitch, float velocity) {
    v.ktOct = (pitch - 60) / 12.0;
    v.ph1 = phaseStart(Osc1Phase);
    v.ph1u = wrap(v.ph1 + 0.25);
    v.ph2 = phaseStart(Osc2Phase);
    v.ph2u = wrap(v.ph2 + 0.25);
    v.vel = velocity;
    v.aEnv = 0.0f;
    v.aStage = Stage::Attack;
    v.fEnv = 0.0f;
    v.fStage = Stage::Attack;
    v.fade1 = get(Lfo1Fade) > 0.001f ? 0.0f : 1.0f;
    v.fade2 = get(Lfo2Fade) > 0.001f ? 0.0f : 1.0f;
    v.f1a1 = v.f1a2 = 0.0;
    v.f2a1 = v.f2a2 = 0.0;
    for (double& z : v.s1) z = 0.0;
    for (double& z : v.s2) z = 0.0;
    for (double& z : v.s1b) z = 0.0;
    for (double& z : v.s2b) z = 0.0;
    for (double& z : v.s1R) z = 0.0;
    for (double& z : v.s2R) z = 0.0;
    for (double& z : v.s1bR) z = 0.0;
    for (double& z : v.s2bR) z = 0.0;
    for (int u = 0; u < kMaxStack; ++u) {
        v.st1[u] = wrap(v.ph1 + u * 0.13);
        v.st2[u] = wrap(v.ph2 + u * 0.13);
    }
    v.modCount = 0;
}

NOTA_ALWAYS_INLINE void VoltSynth::advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
    switch (st) {
    case Stage::Attack:
        env += atk;
        if (env >= 1.0f) {
            env = 1.0f;
            st = Stage::Decay;
        }
        break;
    case Stage::Decay:
        env -= dec;
        if (env <= sus) {
            env = sus;
            st = Stage::Sustain;
        }
        break;
    case Stage::Sustain: break;
    case Stage::Release:
        env -= rel;
        if (env <= 0.0f) {
            env = 0.0f;
            st = Stage::Off;
        }
        break;
    case Stage::Off: break;
    }
}

void VoltSynth::setSvf(double& a1, double& a2, double& a3, double fc, double k) const {
    const double g = std::tan(kPi * fc / sampleRate_);
    a1 = 1.0 / (1.0 + g * (g + k));
    a2 = g * a1;
    a3 = g * a2;
}

NOTA_ALWAYS_INLINE double VoltSynth::svf(double* st, double a1, double a2, double a3, double k, double in, int type) {
    const double v3 = in - st[1];
    const double v1 = a1 * st[0] + a2 * v3;
    const double v2 = st[1] + a2 * st[0] + a3 * v3;
    st[0] = 2.0 * v1 - st[0];
    st[1] = 2.0 * v2 - st[1];
    switch (type) {
    case 1: return in - k * v1 - v2; // high-pass
    case 2: return v1;               // band-pass
    case 3: return in - k * v1;      // notch
    default: return v2;              // low-pass
    }
}

double VoltSynth::lfoValue(int shape, double ph, float shHold) {
    switch (shape) {
    case 1: return ph < 0.5 ? (4.0 * ph - 1.0) : (3.0 - 4.0 * ph); // triangle
    case 2: return ph < 0.5 ? 1.0 : -1.0;                          // square
    case 3: return shHold;                                         // sample & hold
    default: return fast_math::sin((float)ph);                     // sine (LFO: modulation source)
    }
}

double VoltSynth::lfoInc(Param rate, Param sync) const {
    int div = std::clamp((int)std::lround(get(sync) * 7.0f), 0, 7); // 0 = free
    if (div > 0 && samplesPerBeat_ > 0.0) {
        static const double beats[8] = {0, 4, 2, 1, 0.5, 0.25, 0.125, 0.0625};
        return 1.0 / (beats[div] * samplesPerBeat_);
    }
    return expMap(get(rate), 0.05, 20.0) / sampleRate_;
}

float VoltSynth::fadeInc(Param p) const {
    double t = get(p);
    if (t <= 0.001) return 1.0f;
    return (float)(1.0 / (expMap(t, 0.01, 5.0) * sampleRate_));
}

NOTA_ALWAYS_INLINE double VoltSynth::osc(int wave, double shape, double pw, double ph, double dt) {
    if (shape <= 0.0) return oscBlep(wave, ph, dt, pw);
    static constexpr int kPos[4] = {2, 3, 1, 0};  // chip → morph position
    static constexpr int kWave[4] = {3, 2, 0, 1}; // morph position → chip
    const double s = std::min(3.0, kPos[wave] + shape);
    const int a = std::min(2, (int)s);
    const double f = s - a;
    return oscBlep(kWave[a], ph, dt, pw) * (1.0 - f) + oscBlep(kWave[a + 1], ph, dt, pw) * f;
}

double VoltSynth::oscBlep(int wave, double ph, double dt, double pw) {
    switch (wave) {
    case 1: { // square, pulse width pw
        double s = ph < pw ? 1.0 : -1.0;
        s += polyBlep(ph, dt);
        double p2 = ph + (1.0 - pw);
        if (p2 >= 1.0) p2 -= 1.0;
        return s - polyBlep(p2, dt);
    }
    case 2: return 4.0 * std::fabs(ph - 0.5) - 1.0;      // triangle
    case 3: return fast_math::sin((float)ph);            // sine: fast-sin kept (-78 dB null)
    default: return (2.0 * ph - 1.0) - polyBlep(ph, dt); // saw
    }
}

double VoltSynth::polyBlep(double t, double dt) {
    if (dt <= 0.0) return 0.0;
    if (t < dt) {
        double x = t / dt;
        return x + x - x * x - 1.0;
    }
    if (t > 1.0 - dt) {
        double x = (t - 1.0) / dt;
        return x * x + x + x + 1.0;
    }
    return 0.0;
}

float VoltSynth::noise() {
    rng_ ^= rng_ << 13;
    rng_ ^= rng_ >> 17;
    rng_ ^= rng_ << 5;
    return (rng_ & 0xFFFFFF) / static_cast<float>(0x1000000);
}

double VoltSynth::pitchMult(Param oct, Param semi, Param det) const {
    int o = (int)std::lround((get(oct) - 0.5f) * 6.0f);
    int s = (int)std::lround((get(semi) - 0.5f) * 24.0f);
    double c = (get(det) - 0.5f) * 100.0;
    return std::exp2(o + s / 12.0 + c / 1200.0);
}

void VoltSynth::pushHeld(int32_t pitch) {
    popHeld(pitch);
    if (heldCount_ == 16) {
        std::memmove(held_, held_ + 1, 15 * sizeof(int32_t));
        --heldCount_;
    }
    held_[heldCount_++] = pitch;
}

bool VoltSynth::popHeld(int32_t pitch) {
    for (int k = heldCount_ - 1; k >= 0; --k)
        if (held_[k] == pitch) {
            std::memmove(held_ + k, held_ + k + 1, (heldCount_ - 1 - k) * sizeof(int32_t));
            --heldCount_;
            return true;
        }
    return false;
}

VoltSynth::Voice* VoltSynth::findFreeVoice() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0];
    for (auto& v : voices_)
        if (v.aEnv < q->aEnv) q = &v;
    return q;
}

} // namespace nota
