// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Physical implementation. Pitch-critical math (bank coefficients, retune, resonator loops, mallet/noise
// exciters) stays std::-exact. Silent voices auto-sleep via isIdle() after a
// hangover — ringing tails fully decay before sleep (isIdle requires no active
// voice and a silent block).

#include "PhysicalSynth.h"

namespace nota {

PhysicalSynth::PhysicalSynth() {
    // A bright marimba by default: a medium mallet into a Marimba resonator.
    set(MalletVol, 0.85f);
    set(MalletStiff, 0.45f);
    set(MalletNoise, 0.1f);
    set(MalletColor, 0.4f);
    set(NoiseVol, 0.0f);
    set(NoiseEnvAmt, 0.5f);
    set(NoiseType, 0.0f);
    set(NoiseFreq, 0.5f);
    set(NoiseRes, 0.2f);
    set(NoiseA, 0.0f);
    set(NoiseD, 0.3f);
    set(NoiseS, 0.0f);
    set(NoiseR, 0.25f);
    set(R1Type, 0.2f);
    set(R1Decay, 0.5f);
    set(R1Material, 0.5f);
    set(R1Bright, 0.6f);
    set(R1Inharm, 0.0f);
    set(R1Ratio, 0.5f);
    set(R1Hit, 0.25f);
    set(R1Tune, 0.5f);
    set(R2On, 0.0f);
    set(R2Type, 0.0f);
    set(R2Decay, 0.5f);
    set(R2Material, 0.5f);
    set(R2Bright, 0.5f);
    set(R2Inharm, 0.0f);
    set(R2Ratio, 0.5f);
    set(R2Hit, 0.3f);
    set(R2Tune, 0.5f);
    set(Structure, 1.0f); // 1+2 parallel
    set(Tune, 0.5f);
    set(Fine, 0.5f);
    set(NoteOff, 0.3f);
    set(Pan, 0.5f);
    set(Volume, 0.8f);
    // Poly, and an even balance — both banks at full level, the pre-v2 "1 + 2" sum, so a
    // project saved before these two params existed sounds the same.
    set(Mono, 0.0f);
    set(ResMix, 0.5f);
}

void PhysicalSynth::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        gexpr_.set(dim, value);
        return;
    }
    float add[kExprDims];
    gexpr_.offsets(kWheelRange, add);
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && !v.released && !v.choke) v.ex.set(dim, value, add);
}

std::string PhysicalSynth::pluginParamId(int32_t i) const {
    static const char* ids[] = {"malletvol", "malletstiff", "malletnoise", "malletcolor", "noisevol", "noiseenv",
        "noisetype", "noisefreq", "noisereso", "noisea", "noised", "noises", "noiser", "r1type", "r1decay",
        "r1material", "r1bright", "r1inharm", "r1ratio", "r1hit", "r1tune", "r2on", "r2type", "r2decay", "r2material",
        "r2bright", "r2inharm", "r2ratio", "r2hit", "r2tune", "structure", "tune", "fine", "noteoff", "pan", "volume",
        "mono", "resmix"};
    return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
}

std::string PhysicalSynth::pluginParamName(int32_t i) const {
    static const char* nm[] = {"Mallet Volume", "Mallet Stiffness", "Mallet Noise", "Mallet Color", "Noise Volume",
        "Noise Env", "Noise Type", "Noise Freq", "Noise Reso", "Noise Attack", "Noise Decay", "Noise Sustain",
        "Noise Release", "Res1 Type", "Res1 Decay", "Res1 Material", "Res1 Bright", "Res1 Inharm", "Res1 Ratio",
        "Res1 Hit", "Res1 Tune", "Res2 On", "Res2 Type", "Res2 Decay", "Res2 Material", "Res2 Bright", "Res2 Inharm",
        "Res2 Ratio", "Res2 Hit", "Res2 Tune", "Res Structure", "Tune", "Fine", "Note Off", "Pan", "Volume", "Mono",
        "Res Mix"};
    return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
}

std::vector<uint8_t> PhysicalSynth::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void PhysicalSynth::setState(const uint8_t* data, int32_t size) {
    if (!data) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + i * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    wake();
}

std::shared_ptr<Instrument> PhysicalSynth::clone() const {
    auto s = std::make_shared<PhysicalSynth>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(sampleRate_);
    return s;
}

int32_t PhysicalSynth::scopeRead(float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    float buf[kScopeLen];
    buf[0] = static_cast<float>(activeVoices_.load(std::memory_order_relaxed));
    buf[1] = get(Mono) >= 0.5f ? 1.0f : 0.0f;
    buf[2] = lastF0_.load(std::memory_order_relaxed);
    buf[3] = outPeak_.load(std::memory_order_relaxed);
    for (int b = 0; b < 2; ++b) {
        const Param base = b == 0 ? R1Type : R2Type;
        const Param tune = b == 0 ? R1Tune : R2Tune;
        const double tr = std::exp2((get(tune) - 0.5) * 4.0);
        const int type = typeOf(base);
        float* row = buf + kScopeHead + b * 3 * kModes;
        for (int m = 0; m < kModes; ++m) {
            const Mode md = modeOf(type, m, get(Param(base + 1)), get(Param(base + 2)), get(Param(base + 3)),
                get(Param(base + 4)), get(Param(base + 5)), get(Param(base + 6)));
            row[m] = static_cast<float>(md.ratio * tr);
            row[kModes + m] = static_cast<float>(md.amp);
            row[2 * kModes + m] = static_cast<float>(md.tau);
        }
    }
    const int n = std::min<int>(maxN, kScopeLen);
    std::memcpy(out, buf, n * sizeof(float));
    return n;
}

void PhysicalSynth::noteOn(int32_t pitch, float velocity) {
    wake();
    // Mono: whatever is sounding is choked (a short fade, so the cut doesn't click)
    // and the new strike takes a fresh voice — a struck body can't glide.
    if (get(Mono) >= 0.5f)
        for (auto& o : voices_)
            if (o.active) o.choke = true;
    Voice* v = findFreeVoice();
    const double tuneSemis = (get(Tune) - 0.5f) * 48.0 + (get(Fine) - 0.5f) * 2.0; // ±24 st + ±1 st fine
    const double f0 = 440.0 * std::pow(2.0, (pitch - 69 + tuneSemis) / 12.0);
    lastF0_.store(static_cast<float>(f0), std::memory_order_relaxed);
    v->active = true;
    v->released = false;
    v->choke = false;
    v->pitch = pitch;
    v->vel = std::clamp(velocity, 0.0f, 1.0f);
    v->peak = v->vel;
    v->relEnv = 1.0f;
    {
        float add[kExprDims];
        gexpr_.offsets(kWheelRange, add);
        v->ex.reset(add);
    }
    v->exBend = 0.0f;
    v->exSlide = 0.0f;
    v->bowLp = 0.0f;
    v->exAge = 0;

    // Mallet strike: contact time from Stiffness (soft = long/dark, hard = short/bright).
    const double contact = expMap(1.0 - get(MalletStiff), 0.0004, 0.010) * sampleRate_; // samples
    v->mEnvDec = static_cast<float>(std::exp(-1.0 / std::max(1.0, contact)));
    v->mCut = std::clamp(0.05f + 0.9f * get(MalletStiff), 0.02f, 0.999f); // lowpass: hard = brighter
    v->mEnv = 1.0f;
    v->mLp = 0.0f;
    v->mFirst = true;
    v->mActive = get(MalletVol) > 0.0001f;

    // Noise burst envelope reset.
    v->nEnv = 0.0f;
    v->nStage = get(NoiseVol) > 0.0001f ? Stage::Attack : Stage::Off;
    v->nIc1 = v->nIc2 = 0.0;

    buildBank(v->r1, f0 * std::exp2((get(R1Tune) - 0.5) * 4.0), typeOf(R1Type), get(R1Decay), get(R1Material),
        get(R1Bright), get(R1Inharm), get(R1Ratio), get(R1Hit));
    if (get(R2On) >= 0.5f)
        buildBank(v->r2, f0 * std::exp2((get(R2Tune) - 0.5) * 4.0), typeOf(R2Type), get(R2Decay), get(R2Material),
            get(R2Bright), get(R2Inharm), get(R2Ratio), get(R2Hit));
}

void PhysicalSynth::noteOff(int32_t pitch) {
    wake();
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && !v.released) v.released = true;
}

void PhysicalSynth::allNotesOff() {
    wake();
    gexpr_.reset();
    for (auto& v : voices_) v.active = false;
}

void PhysicalSynth::render(float* out, int32_t frames) {
    if (idle_) return;
    const float malletVol = get(MalletVol);
    const float malletNoise = get(MalletNoise);
    const float malletColor = get(MalletColor);
    const float noiseVol = get(NoiseVol);
    const float noiseEnvAmt = bip(NoiseEnvAmt);
    const int noiseType = std::clamp((int)std::lround(get(NoiseType) * 2.0f), 0, 2);
    const double noiseBase = expMap(get(NoiseFreq), 60.0, 12000.0);
    const double noiseK = std::clamp(2.0 - 1.9 * get(NoiseRes), 0.05, 2.0);
    const float nA = rate(NoiseA, 0.001, 2.0), nD = rate(NoiseD, 0.002, 4.0), nR = rate(NoiseR, 0.002, 5.0);
    const float nS = get(NoiseS);
    const bool r2on = get(R2On) >= 0.5f;
    const bool serial = get(Structure) < 0.5f; // 0 = 1>2 (serial), 1 = 1+2 (parallel)
    // Res Mix: an equal-gain balance — both banks at full level in the middle (the plain
    // sum), one fading out toward either end. In 1>2 bank 2 carries a make-up gain.
    const float mix = get(ResMix);
    const float g1 = std::min(1.0f, 2.0f * (1.0f - mix)), g2 = std::min(1.0f, 2.0f * mix);
    const float chokeCoef = static_cast<float>(std::exp(-1.0 / (0.004 * sampleRate_)));
    float peak = 0.0f;
    const float relCoef = static_cast<float>(std::exp(-1.0 / (expMap(1.0 - get(NoteOff), 0.006, 4.0) * sampleRate_)));
    const float volume = get(Volume);
    const float pan = (get(Pan) - 0.5f) * 2.0f;
    const float gl = std::cos((pan + 1.0f) * 0.25f * (float)kPi);
    const float gr = std::sin((pan + 1.0f) * 0.25f * (float)kPi);
    float exAdd[kExprDims];
    gexpr_.offsets(kWheelRange, exAdd);

    for (int32_t i = 0; i < frames; ++i) {
        float mono = 0.0f;
        for (auto& v : voices_) {
            if (!v.active) continue;

            // --- expression: retune / re-damp the banks at control rate ----------
            v.ex.tick(exAdd, exprCoef_);
            if (--v.exAge <= 0) {
                v.exAge = 32;
                const float b = v.ex.bend(), sl = v.ex.slide();
                if (std::fabs(b - v.exBend) > 0.002f || std::fabs(sl - v.exSlide) > 0.004f) {
                    v.exBend = b;
                    v.exSlide = sl;
                    const double mul = std::exp2(b / 12.0), ring = std::exp2(-(double)sl); // R^ring: ring < 1 = longer
                    retune(v.r1, mul, ring);
                    if (r2on) retune(v.r2, mul, ring);
                }
            }

            // --- exciter: mallet strike + filtered noise burst -----------
            float exc = 0.0f;
            if (v.mActive) {
                float raw = (v.mFirst ? 1.0f : 0.0f) + malletNoise * noise();
                v.mFirst = false;
                v.mLp += v.mCut * (raw - v.mLp);
                float m = v.mLp + malletColor * (raw - v.mLp); // Color: dark(lp) .. bright(raw)
                exc += m * v.mEnv * malletVol;
                v.mEnv *= v.mEnvDec;
                if (v.mEnv < 1.0e-4f) v.mActive = false;
            }
            if (v.nStage != Stage::Off) {
                advanceEnv(v.nEnv, v.nStage, nA, nD, nS, nR);
                const double cut =
                    std::clamp(noiseBase * std::exp2(noiseEnvAmt * 3.0 * v.nEnv), 20.0, sampleRate_ * 0.49);
                const float nf = svf(v.nIc1, v.nIc2, cut, noiseK, noise(), noiseType);
                exc += nf * v.nEnv * noiseVol;
            }

            // Bowing: pressure feeds coloured noise in, normalised by the fundamental's
            // ring time so a long-ringing body doesn't build up into a roar.
            const float pr = v.ex.pressure();
            if (pr > 1e-4f) {
                v.bowLp += 0.15f * (noise() - v.bowLp);
                exc += v.bowLp * pr * pr * v.r1.bowNorm * kBowGain;
            }

            // --- resonator banks -----------------------------------------
            float o1 = bank(v.r1, exc);
            float wet;
            if (r2on) {
                float o2 = serial ? bankSerial(v.r2, o1) : bank(v.r2, exc);
                wet = g1 * o1 + g2 * (serial ? kSerialMakeup : 1.0f) * o2;
            } else {
                wet = o1;
            }
            if (v.choke) {
                wet *= v.relEnv;
                v.relEnv *= chokeCoef;
                if (v.relEnv < 1.0e-4f) {
                    v.active = false;
                    continue;
                }
            } else if (v.released) {
                wet *= v.relEnv;
                v.relEnv *= relCoef;
            }

            const float s = wet * v.vel * (1.0f + 0.5f * pr);
            mono += s;
            v.peak = std::max(v.peak * 0.99997f, std::fabs(s));
            if (v.peak < 8.0e-5f && !v.mActive && v.nStage == Stage::Off && (v.released || pr <= 1e-4f))
                v.active = false; // rang out (a held, pressed note can still be bowed)
        }
        const float o = mono * volume * 0.4f;
        out[i * 2] += o * gl;
        out[i * 2 + 1] += o * gr;
        peak = std::max(peak, std::fabs(o));
    }
    int n = 0;
    for (const auto& v : voices_)
        if (v.active && !v.choke) ++n;
    activeVoices_.store(n, std::memory_order_relaxed);
    // Peak hold for the telemetry: ~300 ms fall.
    const float fall = static_cast<float>(std::exp(-static_cast<double>(frames) / (0.3 * sampleRate_)));
    outPeak_.store(std::max(peak, outPeak_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);

    // Auto-sleep: nothing sounding and the block silent. Ringing tails keep voices
    // active (see above), so sleep only engages after the full decay.
    if (n == 0 && peak <= 1.0e-5f) {
        quietSamples_ += static_cast<uint64_t>(frames);
        if (quietSamples_ > kIdleSamples) idle_ = true;
    } else {
        quietSamples_ = 0;
        idle_ = false;
    }
}

void PhysicalSynth::retune(Bank& b, double mul, double ring) const {
    const double wMax = kPi * 0.98;
    for (int m = 0; m < kModes; ++m) {
        if (b.amp[m] == 0.0) continue;
        const double w = b.w[m] * mul;
        const double R = std::pow(b.R[m], ring);
        b.a1[m] = w < wMax ? 2.0 * R * std::cos(w) : 0.0;
        b.a2[m] = w < wMax ? R * R : 0.0;
        b.bs[m] = b.amp[m] * (1.0 - R) * 2.0 * std::sin(std::min(w, wMax));
    }
}

void PhysicalSynth::advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
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
            st = (sus <= 1.0e-4f ? Stage::Off : Stage::Sustain);
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

float PhysicalSynth::svf(double& ic1, double& ic2, double fc, double k, double in, int type) const {
    const double g = std::tan(kPi * fc / sampleRate_);
    const double a1 = 1.0 / (1.0 + g * (g + k));
    const double v3 = in - ic2;
    const double v1 = a1 * ic1 + a1 * g * v3;
    const double v2 = ic2 + g * v1;
    ic1 = 2.0 * v1 - ic1;
    ic2 = 2.0 * v2 - ic2;
    switch (type) {
    case 1: return (float)v1;
    case 2: return (float)(in - k * v1 - v2);
    default: return (float)v2;
    }
}

float PhysicalSynth::bank(Bank& b, float x) {
    double acc = 0.0;
    for (int m = 0; m < kModes; ++m) {
        const double y = b.b0[m] * x + b.a1[m] * b.y1[m] - b.a2[m] * b.y2[m];
        b.y2[m] = b.y1[m];
        b.y1[m] = y;
        acc += y;
    }
    return static_cast<float>(acc);
}

float PhysicalSynth::bankSerial(Bank& b, float x) {
    double acc = 0.0;
    for (int m = 0; m < kModes; ++m) {
        const double y = b.bs[m] * x + b.a1[m] * b.y1[m] - b.a2[m] * b.y2[m];
        b.y2[m] = b.y1[m];
        b.y1[m] = y;
        acc += y;
    }
    return static_cast<float>(acc);
}

double PhysicalSynth::ratioOf(int type, int m) {
    // Free-free beam (bar): (β_m/β_0)², β ≈ (m+0.5)π with exact low modes.
    static const double beam[kModes] = {1.0, 2.7565, 5.4039, 8.9330, 13.3443, 18.6379, 24.8137, 31.8718, 39.8122,
        48.6349, 58.3399, 68.9272, 80.3968, 92.7487, 105.983, 120.099};
    // Tuned marimba bar (2nd partial ≈ 2 octaves).
    static const double marimba[kModes] = {
        1.0, 3.984, 9.531, 17.65, 28.10, 41.0, 56.4, 74.3, 94.7, 117.6, 143.0, 170.9, 201.3, 234.2, 269.6, 307.5};
    // Ideal circular membrane (drum) Bessel ratios.
    static const double membrane[kModes] = {1.0, 1.5933, 2.1355, 2.2954, 2.6531, 2.9173, 3.1555, 3.5001, 3.5985, 3.6521,
        3.8452, 4.0602, 4.1057, 4.2323, 4.6018, 4.8321};
    switch (type) {
    case 0: return beam[m];
    case 1: return marimba[m];
    case 2: return static_cast<double>(m + 1); // String: harmonic
    case 3: return membrane[m];
    case 4: return std::sqrt(static_cast<double>(m + 1)); // Plate: dense metallic (√ series)
    default: return static_cast<double>(2 * m + 1);       // Pipe: odd harmonics
    }
}

PhysicalSynth::Mode PhysicalSynth::modeOf(
    int type, int m, float decay, float material, float bright, float inharm, float ratioP, float hit) {
    const double baseDecay = expMap(decay, 0.04, 18.0);  // ring time (s)
    const double matRoll = expMap(material, 1.0, 0.45);  // high-mode decay scaling (metal .. damped)
    const double brightRoll = expMap(bright, 0.55, 1.0); // high-mode amplitude tilt
    const double ratioExp = 0.5 + ratioP;                // partial-spacing exponent (0.5 .. 1.5)
    const double stretch = inharm * 0.04;                // inharmonic stretch per mode
    Mode md;
    md.ratio = std::pow(ratioOf(type, m) * (1.0 + stretch * m), ratioExp);
    md.tau = std::max(0.01, baseDecay * std::pow(matRoll, m));
    // Amplitude: bright tilt × strike-position comb (mode excited ∝ |sin|).
    md.amp = std::pow(brightRoll, m) * (0.3 + 0.7 * std::fabs(std::sin((m + 1) * kPi * (0.02 + 0.96 * hit))));
    return md;
}

void PhysicalSynth::buildBank(Bank& b, double f0, int type, float decay, float material, float bright, float inharm,
    float ratioP, float hit) const {
    const double nyq = sampleRate_ * 0.49;
    b.bowNorm = 0.0f;
    for (int m = 0; m < kModes; ++m) {
        const Mode md = modeOf(type, m, decay, material, bright, inharm, ratioP, hit);
        const double f = f0 * md.ratio;
        if (f >= nyq || f <= 0.0) {
            b.a1[m] = b.a2[m] = b.b0[m] = b.bs[m] = b.amp[m] = 0.0;
            b.y1[m] = b.y2[m] = 0.0;
            continue;
        }
        const double R = std::exp(-1.0 / (md.tau * sampleRate_));
        const double w = kTwoPi * f / sampleRate_;
        b.a1[m] = 2.0 * R * std::cos(w);
        b.a2[m] = R * R;
        // The sin(w) input gain normalises the resonator so its struck peak ≈ amp,
        // independent of the (near-1) decay radius.
        b.b0[m] = md.amp * std::sin(w);
        // Peak gain on resonance ≈ b / ((1 − R)·2 sin w) → normalise to amp.
        b.bs[m] = md.amp * (1.0 - R) * 2.0 * std::sin(w);
        b.y1[m] = b.y2[m] = 0.0;
        b.R[m] = R;
        b.w[m] = w;
        b.amp[m] = md.amp;
        // Noise through a mode rings up by ~1/√(4(1−R)); the fundamental sets the scale.
        if (m == 0) b.bowNorm = static_cast<float>(2.0 * std::sqrt(1.0 - R));
    }
}

PhysicalSynth::Voice* PhysicalSynth::findFreeVoice() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0];
    for (auto& v : voices_)
        if (v.peak < q->peak) q = &v; // steal the quietest
    return q;
}

} // namespace nota
