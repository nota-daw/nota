// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Synth implementation. Adds voice-based
// auto-sleep (isIdle/hasTail/wake). DSP verbatim — bit-identical null.

#include "Synth.h"

namespace nota {

Synth::Synth() {
    // Musical defaults (also the "Init" patch the editor's double-click restores).
    pn_[Wave].store(0.0f); // Saw
    pn_[Attack].store(0.02f);
    pn_[Decay].store(0.25f);
    pn_[Sustain].store(0.7f);
    pn_[Release].store(0.20f);
    pn_[Cutoff].store(0.62f);
    pn_[Resonance].store(0.12f);
    pn_[Gain].store(0.80f);
    pn_[FilType].store(1.0f / 3.0f); // LP
    pn_[FilEnv].store(0.5f);         // bipolar centre = no envelope on the cutoff
    pn_[PulseWidth].store(0.5f);     // 50 % — a plain square
    pn_[Detune].store(0.5f);         // bipolar centre = 0 cents
    pn_[Octave].store(0.5f);         // bipolar centre = 0 octaves
    pn_[Unison].store(0.0f);         // 1 voice
    pn_[Spread].store(0.35f);
    pn_[Glide].store(0.0f);
    pn_[VelAmp].store(1.0f);    // full velocity tracking — the pre-2026 behaviour
    pn_[Pan].store(0.5f);       // centre
    pn_[VoiceMode].store(0.0f); // Poly 16
    pn_[View].store(1.0f);      // a new Synth opens as the mini (S) card
}

void Synth::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        gexpr_.set(dim, value);
        return;
    }
    float add[kExprDims];
    gexpr_.offsets(kWheelRange, add);
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage != Stage::Release) v.ex.set(dim, value, add);
}

std::string Synth::pluginParamId(int32_t i) const {
    static const char* ids[] = {"wave", "attack", "decay", "sustain", "release", "cutoff", "resonance", "gain",
        "filtype", "filenv", "pulsewidth", "detune", "octave", "unison", "spread", "glide", "velamp", "pan",
        "voicemode", "view"};
    return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
}

std::string Synth::pluginParamName(int32_t i) const {
    static const char* nm[] = {"Wave", "Attack", "Decay", "Sustain", "Release", "Cutoff", "Resonance", "Gain",
        "Filter Type", "Env →Cutoff", "Pulse Width", "Detune", "Octave", "Unison", "Spread", "Glide", "Vel →Vol", "Pan",
        "Voice Mode", "View"};
    return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
}

int32_t Synth::pluginParamIndexOfId(const std::string& id) const {
    for (int32_t i = 0; i < kNumParams; ++i)
        if (pluginParamId(i) == id) return i;
    return -1;
}

std::vector<uint8_t> Synth::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void Synth::setState(const uint8_t* data, int32_t size) {
    if (!data || size <= 0) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + i * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
}

std::shared_ptr<Instrument> Synth::clone() const {
    auto s = std::make_shared<Synth>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(sampleRate_);
    return s;
}

void Synth::noteOn(int32_t pitch, float velocity) {
    wake();
    const int mode = voiceMode();
    if (mode != 0) {                         // Mono / Legato share one voice
        const bool hadHeld = heldCount_ > 0; // another key still down → true overlap
        pushHeld(pitch);
        Voice& v = voices_[0];
        const bool releasing = v.stage == Stage::Release;
        // Legato holds the envelope only while another key is down; a note struck while
        // the voice is merely releasing must retrigger or it never actually sounds.
        const bool legato = mode == 2 && hadHeld && v.active && !releasing;
        const double target = freqOf(pitch);
        if (!v.active || releasing || glideSeconds() <= 0.0) v.freq = target;
        v.target = target;
        v.pitch = pitch;
        v.velocity = velocity;
        resetExpr(v);
        if (!legato) startEnvelope(v, !v.active || releasing);
        v.active = true;
        return;
    }
    pushHeld(pitch);
    Voice& v = *findFreeVoice();
    v.pitch = pitch;
    v.velocity = velocity;
    v.target = freqOf(pitch);
    v.freq = v.target; // no portamento in poly
    resetExpr(v);
    startEnvelope(v, true);
    v.active = true;
}

void Synth::noteOff(int32_t pitch) {
    wake();
    popHeld(pitch);
    if (voiceMode() != 0) {
        Voice& v = voices_[0];
        if (heldCount_ == 0) {
            v.stage = Stage::Release;
            return;
        }
        const int32_t top = held_[heldCount_ - 1]; // fall back to the key still down
        v.pitch = top;
        v.target = freqOf(top);
        if (glideSeconds() <= 0.0) v.freq = v.target;
        return;
    }
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage != Stage::Release) v.stage = Stage::Release;
}

void Synth::render(float* out, int32_t frames) {
    // Snapshot params once per block (denormalise to musical units).
    const int wave = static_cast<int>(std::lround(pn_[Wave].load(std::memory_order_relaxed) * 3.0f));
    const float atkRate = static_cast<float>(1.0 / (secOf(Attack, 0.001, 2.0) * sampleRate_));
    const float decRate = static_cast<float>(1.0 / (secOf(Decay, 0.002, 2.0) * sampleRate_));
    const float relRate = static_cast<float>(1.0 / (secOf(Release, 0.002, 3.0) * sampleRate_));
    const float sustain = pn_[Sustain].load(std::memory_order_relaxed);
    const float gain = pn_[Gain].load(std::memory_order_relaxed);
    const float velAmp = pn_[VelAmp].load(std::memory_order_relaxed);
    const int filType = filterType();
    const float filEnv = (pn_[FilEnv].load(std::memory_order_relaxed) - 0.5f) * 2.0f; // −1..+1
    const double pw = 0.05 + pn_[PulseWidth].load(std::memory_order_relaxed) * 0.90;
    const double reso = pn_[Resonance].load(std::memory_order_relaxed);
    const double baseCut = expMap(pn_[Cutoff].load(std::memory_order_relaxed), 20.0, 18000.0);
    const int uni = unisonCount();
    const double spread = pn_[Spread].load(std::memory_order_relaxed);
    const double cents = (pn_[Detune].load(std::memory_order_relaxed) - 0.5) * 100.0; // ±50 c
    const double tune = std::pow(2.0, octaveShift());
    const double glideC = glideCoef();

    // Unison detune ratios and pan gains — the same for every voice this block.
    double uDet[kMaxUnison], uL[kMaxUnison], uR[kMaxUnison];
    for (int u = 0; u < uni; ++u) {
        // One voice: `cents` is a plain fine tune. Several: they spread across ±cents.
        const double t = (uni == 1) ? 1.0 : (2.0 * u / (uni - 1) - 1.0);
        uDet[u] = std::pow(2.0, cents * t / 1200.0);
        const double p = (uni == 1) ? 0.0 : t * spread; // −1..+1
        uL[u] = std::sqrt(0.5 * (1.0 - p));
        uR[u] = std::sqrt(0.5 * (1.0 + p));
    }
    const double uNorm = 1.0 / std::sqrt(static_cast<double>(uni));

    // Static pan (equal power) and the block's amplitude scale.
    const double pan = (pn_[Pan].load(std::memory_order_relaxed) - 0.5) * 2.0;
    const double panL = std::sqrt(0.5 * (1.0 - pan)) * kSqrt2;
    const double panR = std::sqrt(0.5 * (1.0 + pan)) * kSqrt2;
    float exAdd[kExprDims];
    gexpr_.offsets(kWheelRange, exAdd);

    for (auto& v : voices_) {
        if (!v.active) continue;
        double coefEnv = -1.0; // env value the coefficients were built for
        double coefExpr = 0.0; // expression octaves they were built for
        double a1 = 0, a2 = 0, a3 = 0, k = 0;
        int coefAge = 0;
        double bendMul = 1.0;
        float bendAt = 0.0f;
        for (int32_t i = 0; i < frames; ++i) {
            v.ex.tick(exAdd, exprCoef_);
            if (v.ex.bend() != bendAt) {
                bendAt = v.ex.bend();
                bendMul = std::exp2(bendAt / 12.0);
            }
            switch (v.stage) {
            case Stage::Attack:
                v.env += atkRate;
                if (v.env >= 1.0f) {
                    v.env = 1.0f;
                    v.stage = Stage::Decay;
                }
                break;
            case Stage::Decay:
                v.env -= decRate;
                if (v.env <= sustain) {
                    v.env = sustain;
                    v.stage = Stage::Sustain;
                }
                break;
            case Stage::Sustain: break;
            case Stage::Release:
                v.env -= relRate;
                if (v.env <= 0.0f) {
                    v.env = 0.0f;
                    v.active = false;
                }
                break;
            }

            // Glide: a one-pole slide of the playing frequency towards the note's.
            if (glideC > 0.0 && v.freq != v.target) {
                v.freq += (v.target - v.freq) * glideC;
                if (std::fabs(v.target - v.freq) < 1e-4) v.freq = v.target;
            } else if (glideC <= 0.0) {
                v.freq = v.target;
            }

            // Oscillator stack: `uni` detuned copies, panned across the stereo field.
            double sl = 0.0, sr = 0.0;
            const double base = v.freq * tune * bendMul / sampleRate_;
            for (int u = 0; u < uni; ++u) {
                const double s = oscSample(wave, v.phase[u], pw);
                v.phase[u] += base * uDet[u];
                if (v.phase[u] >= 1.0) v.phase[u] -= 1.0;
                sl += s * uL[u];
                sr += s * uR[u];
            }
            sl *= uNorm;
            sr *= uNorm;

            if (filType > 0) {
                // Rebuild the coefficients when the envelope or the note's expression has
                // moved the cutoff far enough to matter (every 16 samples at most) — tan()
                // is not cheap.
                const double exOcts = v.ex.slide() * 2.0 + v.ex.pressure() * 2.0;
                const bool moving = filEnv != 0.0f || std::fabs(exOcts - coefExpr) > 0.01;
                if (coefEnv < 0.0 || (moving && --coefAge <= 0)) {
                    const double octs = filEnv * v.env * 4.0 + exOcts; // ±4 octaves + MPE
                    const double fc = std::clamp(baseCut * std::pow(2.0, octs), 20.0, 20000.0);
                    const double g = std::tan(kPi * std::min(fc, sampleRate_ * 0.49) / sampleRate_);
                    k = 2.0 - 1.94 * reso; // 2 = no res
                    a1 = 1.0 / (1.0 + g * (g + k));
                    a2 = g * a1;
                    a3 = g * a2;
                    coefEnv = v.env;
                    coefExpr = exOcts;
                    coefAge = 16;
                }
                sl = svf(v.fl, sl, a1, a2, a3, k, filType);
                sr = svf(v.fr, sr, a1, a2, a3, k, filType);
            }

            const double amp = ((1.0f - velAmp) + velAmp * v.velocity) * (1.0 + 0.5 * v.ex.pressure());
            const double e = v.env * amp * gain * 0.25;
            out[i * 2] += static_cast<float>(sl * e * panL);
            out[i * 2 + 1] += static_cast<float>(sr * e * panR);
            if (!v.active) break;
        }
    }
    // Sleep: no sounding voice means the loop above wrote nothing.
    idle_ = true;
    for (const auto& v : voices_)
        if (v.active) {
            idle_ = false;
            break;
        }
}

void Synth::wake() {
    idle_ = false;
}

double Synth::oscSample(int wave, double phase, double pw) {
    switch (wave) {
    case 1: return phase < pw ? 1.0 : -1.0;            // square / pulse
    case 2: return 4.0 * std::fabs(phase - 0.5) - 1.0; // triangle
    case 3: return std::sin(2.0 * kPi * phase);        // sine
    default: return 2.0 * phase - 1.0;                 // saw
    }
}

double Synth::svf(Svf& s, double in, double a1, double a2, double a3, double k, int type) {
    const double v3 = in - s.ic2;
    const double v1 = a1 * s.ic1 + a2 * v3;
    const double v2 = s.ic2 + a2 * s.ic1 + a3 * v3;
    s.ic1 = 2.0 * v1 - s.ic1;
    s.ic2 = 2.0 * v2 - s.ic2;
    switch (type) {
    case 2: return in - k * v1 - v2; // high-pass
    case 3: return v1;               // band-pass
    default: return v2;              // low-pass
    }
}

int Synth::unisonCount() const {
    static const int n[] = {1, 2, 4, 7};
    return n[std::clamp(static_cast<int>(std::lround(pn_[Unison].load(std::memory_order_relaxed) * 3.0f)), 0, 3)];
}

double Synth::glideSeconds() const {
    const double g = pn_[Glide].load(std::memory_order_relaxed);
    return g <= 1e-4 ? 0.0 : g * g * 2.0;
}

double Synth::glideCoef() const {
    const double s = glideSeconds();
    return s <= 0.0 ? 0.0 : std::clamp(1.0 - std::exp(-1.0 / (s * 0.25 * sampleRate_)), 0.0, 1.0);
}

void Synth::startEnvelope(Voice& v, bool fresh) {
    v.stage = Stage::Attack;
    if (!fresh) return;
    v.env = 0.0f;
    for (int u = 0; u < kMaxUnison; ++u) v.phase[u] = u * 0.13; // spread the stack's phases
    v.fl = {};
    v.fr = {};
}

Synth::Voice* Synth::findFreeVoice() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0];
    for (auto& v : voices_)
        if (v.env < q->env) q = &v; // steal the quietest
    return q;
}

void Synth::pushHeld(int32_t pitch) {
    popHeld(pitch);
    if (heldCount_ < kHeldStack) held_[heldCount_++] = pitch;
}

void Synth::popHeld(int32_t pitch) {
    int w = 0;
    for (int r = 0; r < heldCount_; ++r)
        if (held_[r] != pitch) held_[w++] = held_[r];
    heldCount_ = w;
}

} // namespace nota
