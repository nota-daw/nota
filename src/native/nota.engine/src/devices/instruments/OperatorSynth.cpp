// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Operator implementation. The only behavioural deltas are the auto-sleep flag and the
// block-decimated spectrum ring (every 4th sample — scope-only, PCM-transparent).

#include "OperatorSynth.h"

namespace nota {

OperatorSynth::OperatorSynth() {
    set(Algo, 0.0f);
    set(Feedback, 0.0f);
    // A gentle electric-piano-ish default: D is the carrier, C modulates it 1:1.
    auto op = [&](int o, float coarse, float lvl, float wave, float a, float d, float s, float r) {
        const int base = ACoarse + o * 8;
        set((Param)(base + F_Coarse), coarse);
        set((Param)(base + F_Fine), 0.5f);
        set((Param)(base + F_Level), lvl);
        set((Param)(base + F_Wave), wave);
        set((Param)(base + F_A), a);
        set((Param)(base + F_D), d);
        set((Param)(base + F_S), s);
        set((Param)(base + F_R), r);
    };
    // coarse 0.0667 ≈ ratio 1 (index 1 of 16); levels: D carrier, C modulator.
    op(0, 0.0667f, 0.0f, 0.0f, 0.0f, 0.4f, 0.0f, 0.3f);   // A (silent)
    op(1, 0.0667f, 0.0f, 0.0f, 0.0f, 0.4f, 0.0f, 0.3f);   // B (silent)
    op(2, 0.0667f, 0.45f, 0.0f, 0.0f, 0.45f, 0.0f, 0.3f); // C modulator
    op(3, 0.0667f, 1.0f, 0.0f, 0.0f, 0.5f, 0.6f, 0.35f);  // D carrier
    set(FilterType, 0.0f);
    set(FilterFreq, 0.85f);
    set(FilterReso, 0.1f);
    set(Volume, 0.8f);
    set(FmDepth, 0.5f); // ×1 — identical to the pre-macro fixed depth
    set(Glide, 0.0f);
    set(VelToFm, 0.0f);
    set(KeyLevel, 0.0f);
    set(Mono, 0.0f);
    set(Bend, 0.5f);              // centred
    set(BendRange, 1.0f / 11.0f); // ±2 semitones
    set(ModWheel, 0.0f);
    set(FilKeyTrk, 0.0f);
    set(VelToLevel, 1.0f); // velocity scales the voice, as it always did
}

// The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
void OperatorSynth::controller(int32_t ctl, float value) {
    if (ctl == kCtlModWheel) pn_[ModWheel].store(std::clamp(value, 0.0f, 1.0f), std::memory_order_relaxed);
    Instrument::controller(ctl, value);
    wake();
}

void OperatorSynth::setSampleRate(double sr) {
    sampleRate_ = sr > 0 ? sr : 44100.0;
    std::memset(ring_, 0, sizeof(ring_));
    specF0_.store(0.0f, std::memory_order_relaxed);
    exprCoef_ = exprSmoothCoef(sampleRate_);
    wake();
}

void OperatorSynth::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        gexpr_.set(dim, value);
        return;
    }
    float add[kExprDims];
    gexpr_.offsets(wheelRange(), add);
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage[0] != Stage::Release && v.stage[0] != Stage::Off)
            v.ex.set(dim, value, add);
}

void OperatorSynth::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

std::string OperatorSynth::pluginParamId(int32_t i) const {
    static const char* ids[] = {"algo", "feedback", "acoarse", "afine", "alevel", "awave", "aatk", "adec", "asus",
        "arel", "bcoarse", "bfine", "blevel", "bwave", "batk", "bdec", "bsus", "brel", "ccoarse", "cfine", "clevel",
        "cwave", "catk", "cdec", "csus", "crel", "dcoarse", "dfine", "dlevel", "dwave", "datk", "ddec", "dsus", "drel",
        "filtype", "filfreq", "filreso", "volume", "fmdepth", "glide", "veltofm", "keylevel", "mono", "bend",
        "bendrange", "modwheel", "filkeytrk", "veltolevel"};
    return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
}

std::string OperatorSynth::pluginParamName(int32_t i) const {
    static const char* nm[] = {"Algorithm", "Feedback", "A Coarse", "A Fine", "A Level", "A Wave", "A Attack",
        "A Decay", "A Sustain", "A Release", "B Coarse", "B Fine", "B Level", "B Wave", "B Attack", "B Decay",
        "B Sustain", "B Release", "C Coarse", "C Fine", "C Level", "C Wave", "C Attack", "C Decay", "C Sustain",
        "C Release", "D Coarse", "D Fine", "D Level", "D Wave", "D Attack", "D Decay", "D Sustain", "D Release",
        "Filter Type", "Filter Freq", "Filter Reso", "Volume", "FM Depth", "Glide", "Vel to FM", "Key to Level", "Mono",
        "Pitch Bend", "Bend Range", "Mod Wheel", "Filter Key Track", "Vel to Level"};
    return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
}

void OperatorSynth::pluginParamSet(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

int32_t OperatorSynth::pluginParamIndexOfId(const std::string& id) const {
    for (int32_t i = 0; i < kNumParams; ++i)
        if (pluginParamId(i) == id) return i;
    return -1;
}

std::vector<uint8_t> OperatorSynth::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void OperatorSynth::setState(const uint8_t* data, int32_t size) {
    if (!data) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + i * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
}

std::shared_ptr<Instrument> OperatorSynth::clone() const {
    auto s = std::make_shared<OperatorSynth>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(sampleRate_);
    return s;
}

void OperatorSynth::noteOn(int32_t pitch, float velocity) {
    const bool mono = get(Mono) >= 0.5f;
    const bool glideOn = get(Glide) > 0.0f;
    Voice* v = mono ? &voices_[0] : findFreeVoice();
    // Mono + glide + a still-sounding voice → legato: keep phases/envelopes, just retune.
    const bool legato = mono && glideOn && v->active && v->stage[0] != Stage::Off;
    v->active = true;
    v->pitch = pitch;
    v->vel = std::clamp(velocity, 0.0f, 1.0f);
    v->freq = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
    {
        float add[kExprDims];
        gexpr_.offsets(wheelRange(), add);
        v->ex.reset(add);
        v->exBendAt = 0.0f;
        v->exBendMul = 1.0;
    }
    v->curFreq = legato ? v->curFreq : (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freq;
    lastFreq_ = v->freq;
    if (!legato) {
        v->fbLast = 0.0;
        v->s1 = v->s2 = 0.0;
        for (int o = 0; o < 4; ++o) {
            v->phase[o] = 0.0;
            v->env[o] = 0.0f;
            v->stage[o] = Stage::Attack;
        }
    }
    wake();
}

void OperatorSynth::noteOff(int32_t pitch) {
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage[0] != Stage::Release)
            for (int o = 0; o < 4; ++o) v.stage[o] = Stage::Release;
    wake();
}

void OperatorSynth::allNotesOff() {
    gexpr_.reset();
    for (auto& v : voices_) v.active = false;
    wake();
}

void OperatorSynth::render(float* out, int32_t frames) {
    const int algo = std::clamp((int)std::lround(get(Algo) * (kNumAlgos - 1.0f)), 0, kNumAlgos - 1);
    const int8_t* tgt = kAlgo[algo];
    const double fb = get(Feedback) * 0.9;
    // Mod wheel is the player's hand on the modulation index: fully up doubles it.
    const double fmMul = get(FmDepth) * 2.0 * (1.0 + get(ModWheel)); // 0.5, wheel down ⇒ ×1
    const double velToFm = get(VelToFm);
    const double keyLevel = get(KeyLevel);
    const double velToLevel = get(VelToLevel);
    // Pitch-bend wheel, in the range the patch declares (1..12 semitones).
    const double bendSemis = (get(Bend) - 0.5) * 2.0 * wheelRange();
    float exAdd[kExprDims];
    gexpr_.offsets(wheelRange(), exAdd);
    const double bendMul = std::exp2(bendSemis / 12.0);
    const double keyTrk = get(FilKeyTrk);
    double ratio[4], fineMul[4], level[4], atk[4], dec[4], sus[4], rel[4];
    int wave[4];
    for (int o = 0; o < 4; ++o) {
        const int base = ACoarse + o * 8;
        ratio[o] = kRatio[std::clamp((int)std::lround(get((Param)(base + F_Coarse)) * 15.0f), 0, 15)];
        fineMul[o] = std::exp2(((double)get((Param)(base + F_Fine)) - 0.5) * 0.08); // ±~0.7 semitone
        level[o] = get((Param)(base + F_Level));
        wave[o] = std::clamp((int)std::lround(get((Param)(base + F_Wave)) * 3.0f), 0, 3);
        atk[o] = rateOf((Param)(base + F_A), 0.001, 4.0);
        dec[o] = rateOf((Param)(base + F_D), 0.002, 6.0);
        sus[o] = get((Param)(base + F_S));
        rel[o] = rateOf((Param)(base + F_R), 0.002, 8.0);
    }
    const int ftype = std::clamp((int)std::lround(get(FilterType) * 2.0f), 0, 2);
    const double fbase = expMap(get(FilterFreq), 60.0, 18000.0);
    const double fk = std::clamp(2.0 - 1.9 * get(FilterReso), 0.05, 2.0);
    const double volume = get(Volume);
    const double invSr = 1.0 / sampleRate_;
    // Glide coefficient: one-pole approach toward the target frequency.
    const double glideT = get(Glide) > 0.0f ? expMap(get(Glide), 0.005, 1.2) : 0.0;
    const double gCoef = glideT > 0.0 ? (1.0 - std::exp(-1.0 / (glideT * sampleRate_))) : 1.0;

    // Filter coeffs, recomputed per block. With key tracking the cutoff follows the
    // note, so each voice gets its own pair; without it they all land on the same one.
    double fgV[kVoices], fa1V[kVoices];
    for (int vi = 0; vi < kVoices; ++vi) {
        const double cut = keyTrk > 0.0 ? fbase * std::exp2(keyTrk * (voices_[vi].pitch - 60) / 12.0) : fbase;
        fgV[vi] = std::tan(kPi * std::clamp(cut, 20.0, sampleRate_ * 0.49) * invSr);
        fa1V[vi] = 1.0 / (1.0 + fgV[vi] * (fgV[vi] + fk));
    }

    double f0 = 0.0; // frequency of the freshest active voice → spectrum base + label
    float peak = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        float mono = 0.0f;
        for (int vi = 0; vi < kVoices; ++vi) {
            Voice& v = voices_[vi];
            if (!v.active) continue;
            const double fg = fgV[vi], fa1 = fa1V[vi];
            // Advance glide toward the target pitch.
            v.curFreq += (v.freq - v.curFreq) * gCoef;
            v.ex.tick(exAdd, exprCoef_);
            if (v.ex.bend() != v.exBendAt) {
                v.exBendAt = v.ex.bend();
                v.exBendMul = std::exp2(v.exBendAt / 12.0);
            }
            const double pitchHz = v.curFreq * bendMul * v.exBendMul;
            // Velocity → FM depth and keyboard → modulator level (timbre controls).
            const double velFac = 1.0 - velToFm * (1.0 - (double)v.vel);
            const double keyFac = keyLevel > 0.0 ? std::exp2(-keyLevel * (v.pitch - 60) / 24.0) : 1.0;
            const double exFm = std::exp2((double)v.ex.slide()) * (1.0 + v.ex.pressure());
            const double modScale = kFmIndex * fmMul * velFac * keyFac * exFm;
            double opOut[4];
            double modIn[4] = {0, 0, 0, 0};
            double carrier = 0.0;
            int nc = 0;
            bool anyOn = false;
            for (int o = 0; o < 4; ++o) {
                advanceEnv(v.env[o], v.stage[o], (float)atk[o], (float)dec[o], (float)sus[o], (float)rel[o]);
                if (v.stage[o] != Stage::Off) anyOn = true;
                double pmod = modIn[o];
                if (o == 0 && fb > 0.0) pmod += fb * v.fbLast;
                const double s = waveform(wave[o], v.phase[o] + pmod) * level[o] * v.env[o];
                opOut[o] = s;
                v.phase[o] += pitchHz * ratio[o] * fineMul[o] * invSr;
                if (v.phase[o] >= 1.0) v.phase[o] -= std::floor(v.phase[o]);
                const int t = tgt[o];
                if (t < 4) modIn[t] += s * modScale;
                else {
                    carrier += s;
                    ++nc;
                }
            }
            v.fbLast = opOut[0];
            if (!anyOn) {
                v.active = false;
                continue;
            }
            if (f0 <= 0.0) f0 = pitchHz;
            double y = nc > 0 ? carrier / std::sqrt((double)nc) : 0.0;
            // Subtractive filter (TPT SVF).
            const double v3 = y - v.s2;
            const double v1 = fa1 * v.s1 + fa1 * fg * v3;
            const double v2 = v.s2 + fg * v1;
            v.s1 = 2.0 * v1 - v.s1;
            v.s2 = 2.0 * v2 - v.s2;
            double fout = ftype == 1 ? (y - fk * v1 - v2) : ftype == 2 ? v1 : v2; // HP / BP / LP
            mono += (float)(fout * (1.0 - velToLevel * (1.0 - (double)v.vel)) * (1.0 + 0.5 * v.ex.pressure()));
        }
        // Spectrum-analysis tap (pre-master timbre signal), block-decimated: the ring
        // feeds only the message-thread scope, so sampling every 4th frame is
        // transparent to the PCM output.
        if ((i & 3) == 0) {
            ring_[ringW_ & (kRing - 1)] = mono;
            ringW_.fetch_add(1, std::memory_order_relaxed);
        }
        const float o = mono * (float)volume * 0.4f;
        peak = std::max(peak, std::fabs(o));
        out[i * 2] += o;
        out[i * 2 + 1] += o;
    }
    if (f0 > 0.0) specF0_.store((float)f0, std::memory_order_relaxed);

    // Auto-sleep flag only (the engine skips render while idle): no voice sounding
    // and this block silent. noteOn/noteOff/setParam wake.
    int live = 0;
    for (auto& v : voices_)
        if (v.active) ++live;
    if (live == 0 && peak <= 1.0e-5f) {
        quietSamples_ += (uint64_t)frames;
        if (quietSamples_ > kIdleSamples) idle_ = true;
    } else {
        quietSamples_ = 0;
        idle_ = false;
    }
}

int32_t OperatorSynth::scopeRead(float* outv, int32_t maxN) const {
    if (!outv || maxN <= 0) return 0;
    const double f0 = specF0_.load(std::memory_order_relaxed);
    const int N = std::min(maxN, kSpecBins);
    if (f0 <= 0.0) {
        for (int k = 0; k < N; ++k) outv[k] = 0.0f;
        return N;
    }
    const double sr = sampleRate_;
    const uint32_t head = ringW_.load(std::memory_order_relaxed);
    double mag[kSpecBins];
    double mx = 1e-9;
    for (int k = 0; k < N; ++k) {
        const double freq = (k + 1) * f0;
        if (freq > sr * 0.45) {
            mag[k] = 0.0;
            continue;
        }
        const double w = 2.0 * kPi * freq / sr;
        const double cw = 2.0 * std::cos(w);
        double s0 = 0.0, s1 = 0.0, s2 = 0.0;
        for (int n = 0; n < kRing; ++n) {
            const double x = ring_[(head + n) & (kRing - 1)];
            s0 = x + cw * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        const double re = s1 - s2 * std::cos(w), im = s2 * std::sin(w);
        mag[k] = std::sqrt(re * re + im * im);
        if (mag[k] > mx) mx = mag[k];
    }
    const double inv = 1.0 / mx;
    for (int k = 0; k < N; ++k) outv[k] = (float)std::clamp(mag[k] * inv, 0.0, 1.0);
    return N;
}

double OperatorSynth::waveform(int w, double ph) {
    switch (w) {
    case 1: {
        double f = ph - std::floor(ph);
        return 4.0 * std::fabs(f - 0.5) - 1.0;
    } // tri
    case 2: {
        double f = ph - std::floor(ph);
        return 2.0 * f - 1.0;
    } // saw
    case 3: {
        double f = ph - std::floor(ph);
        return f < 0.5 ? 1.0 : -1.0;
    } // square
    default: return std::sin(kTwoPi * ph); // sine (pitch-defining: stays std::-exact)
    }
}

void OperatorSynth::advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
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

OperatorSynth::Voice* OperatorSynth::findFreeVoice() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0];
    for (auto& v : voices_)
        if (v.env[3] < q->env[3]) q = &v; // steal by carrier-D env
    return q;
}

} // namespace nota
