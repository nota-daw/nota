// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Modulation.h"

namespace nota {

int32_t Modulator::readScope(float* out, int32_t cap) const {
    const int32_t w = scopeWrite.load(std::memory_order_relaxed);
    const int32_t n = cap < kScopeLen ? cap : kScopeLen;
    for (int32_t i = 0; i < n; ++i) out[i] = scopeBuf[(w + i) & (kScopeLen - 1)];
    return n;
}

Modulator& Modulator::operator=(const Modulator& o) {
    id = o.id;
    kind = o.kind;
    waveform.store(o.waveform.load(std::memory_order_relaxed), std::memory_order_relaxed);
    tempoSync.store(o.tempoSync.load(std::memory_order_relaxed), std::memory_order_relaxed);
    rateHz.store(o.rateHz.load(std::memory_order_relaxed), std::memory_order_relaxed);
    rateSyncBeats.store(o.rateSyncBeats.load(std::memory_order_relaxed), std::memory_order_relaxed);
    depth.store(o.depth.load(std::memory_order_relaxed), std::memory_order_relaxed);
    phase01.store(o.phase01.load(std::memory_order_relaxed), std::memory_order_relaxed);
    attackMs.store(o.attackMs.load(std::memory_order_relaxed), std::memory_order_relaxed);
    releaseMs.store(o.releaseMs.load(std::memory_order_relaxed), std::memory_order_relaxed);
    decayMs.store(o.decayMs.load(std::memory_order_relaxed), std::memory_order_relaxed);
    sustain.store(o.sustain.load(std::memory_order_relaxed), std::memory_order_relaxed);
    envValue.store(o.envValue.load(std::memory_order_relaxed), std::memory_order_relaxed);
    adsrStage.store(o.adsrStage.load(std::memory_order_relaxed), std::memory_order_relaxed);
    prevGate.store(o.prevGate.load(std::memory_order_relaxed), std::memory_order_relaxed);
    inputA.store(o.inputA.load(std::memory_order_relaxed), std::memory_order_relaxed);
    inputB.store(o.inputB.load(std::memory_order_relaxed), std::memory_order_relaxed);
    mathOffset.store(o.mathOffset.load(std::memory_order_relaxed), std::memory_order_relaxed);
    outValue.store(o.outValue.load(std::memory_order_relaxed), std::memory_order_relaxed);
    for (int i = 0; i < kScopeLen; ++i) scopeBuf[i] = o.scopeBuf[i];
    scopeWrite.store(o.scopeWrite.load(std::memory_order_relaxed), std::memory_order_relaxed);
    return *this;
}

float Modulator::get(ModField f) const {
    switch (f) {
    case ModField::Waveform: return (float)waveform.load(std::memory_order_relaxed);
    case ModField::TempoSync: return (float)tempoSync.load(std::memory_order_relaxed);
    case ModField::RateHz: return rateHz.load(std::memory_order_relaxed);
    case ModField::RateSyncBeats: return rateSyncBeats.load(std::memory_order_relaxed);
    case ModField::Depth: return depth.load(std::memory_order_relaxed);
    case ModField::Phase: return phase01.load(std::memory_order_relaxed);
    case ModField::Attack: return attackMs.load(std::memory_order_relaxed);
    case ModField::Release: return releaseMs.load(std::memory_order_relaxed);
    case ModField::Decay: return decayMs.load(std::memory_order_relaxed);
    case ModField::Sustain: return sustain.load(std::memory_order_relaxed);
    case ModField::InputA: return (float)inputA.load(std::memory_order_relaxed);
    case ModField::InputB: return (float)inputB.load(std::memory_order_relaxed);
    case ModField::MathOffset: return mathOffset.load(std::memory_order_relaxed);
    }
    return 0.0f;
}

void Modulator::set(ModField f, float v) {
    switch (f) {
    case ModField::Waveform: waveform.store((int32_t)std::lround(v), std::memory_order_relaxed); break;
    case ModField::TempoSync: tempoSync.store(v > 0.5f ? 1 : 0, std::memory_order_relaxed); break;
    case ModField::RateHz: rateHz.store(v, std::memory_order_relaxed); break;
    case ModField::RateSyncBeats: rateSyncBeats.store(v, std::memory_order_relaxed); break;
    case ModField::Depth: // a Math node's gain runs 0..2; every other depth 0..1 (#92)
        depth.store(std::clamp(v, 0.0f, kind == ModulatorKind::Math ? 2.0f : 1.0f), std::memory_order_relaxed);
        break;
    case ModField::Phase: phase01.store(v, std::memory_order_relaxed); break;
    case ModField::Attack: attackMs.store(std::max(0.1f, v), std::memory_order_relaxed); break;
    case ModField::Release: releaseMs.store(std::max(1.0f, v), std::memory_order_relaxed); break;
    case ModField::Decay: decayMs.store(std::max(1.0f, v), std::memory_order_relaxed); break;
    case ModField::Sustain: sustain.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); break;
    case ModField::InputA: inputA.store((int32_t)std::lround(v), std::memory_order_relaxed); break;
    case ModField::InputB: inputB.store((int32_t)std::lround(v), std::memory_order_relaxed); break;
    case ModField::MathOffset: mathOffset.store(v, std::memory_order_relaxed); break;
    }
}

float Modulator::mathEval(float a, float b) const {
    const float gain = depth.load(std::memory_order_relaxed), off = mathOffset.load(std::memory_order_relaxed);
    switch (waveform.load(std::memory_order_relaxed)) {
    case 1: return a - b;          // Subtract
    case 2: return a * b;          // Multiply
    case 3: return -a;             // Invert
    case 4: return a * gain + off; // Scale (offset + gain)
    case 5: return std::min(a, b); // Min
    case 6: return std::max(a, b); // Max
    default: return a + b;         // Add
    }
}

void Modulator::updateEnv(float level, double dt) {
    const float target = std::clamp(level, 0.0f, 1.0f);
    const float cur = envValue.load(std::memory_order_relaxed);
    const float tc =
        (target > cur ? attackMs.load(std::memory_order_relaxed) : releaseMs.load(std::memory_order_relaxed)) * 0.001f;
    const float a = tc <= 1e-6f ? 1.0f : (float)(1.0 - std::exp(-dt / tc));
    envValue.store(cur + (target - cur) * a, std::memory_order_relaxed);
}

void Modulator::updateAdsr(bool gate, double dt) {
    int32_t stage = adsrStage.load(std::memory_order_relaxed);
    const bool pg = prevGate.load(std::memory_order_relaxed) != 0;
    if (gate && !pg) stage = 1; // note-on  → attack
    else if (!gate && pg)
        stage = 4; // note-off → release
    prevGate.store(gate ? 1 : 0, std::memory_order_relaxed);

    auto coeff = [dt](float ms) {
        const double tc = ms * 0.001;
        return tc <= 1e-6 ? 1.0f : (float)(1.0 - std::exp(-dt / tc));
    };
    float v = envValue.load(std::memory_order_relaxed);
    const float s = sustain.load(std::memory_order_relaxed);
    switch (stage) {
    case 1:
        v += (1.0f - v) * coeff(attackMs.load(std::memory_order_relaxed));
        if (v >= 0.99f) stage = 2;
        break; // attack → 1
    case 2:
        v += (s - v) * coeff(decayMs.load(std::memory_order_relaxed));
        if (std::fabs(v - s) < 1e-3f) stage = 3;
        break;            // decay → sustain
    case 3: v = s; break; // sustain
    case 4:
        v += (0.0f - v) * coeff(releaseMs.load(std::memory_order_relaxed));
        if (v < 1e-3f) {
            v = 0.0f;
            stage = 0;
        }
        break;      // release → 0
    default: break; // idle
    }
    envValue.store(v, std::memory_order_relaxed);
    adsrStage.store(stage, std::memory_order_relaxed);
}

float Modulator::eval(double beat, double timeSec) const {
    if (kind == ModulatorKind::Math || kind == ModulatorKind::Scope)
        return outValue.load(std::memory_order_relaxed); // precomputed each block (Scope = pass-through)
    if (kind == ModulatorKind::Macro) return depth.load(std::memory_order_relaxed); // manual knob value 0..1
    if (kind == ModulatorKind::EnvFollower || kind == ModulatorKind::MidiToCv || kind == ModulatorKind::Adsr)
        return envValue.load(std::memory_order_relaxed) * depth.load(std::memory_order_relaxed);
    double cyc;
    if (tempoSync.load(std::memory_order_relaxed)) {
        const float rb = rateSyncBeats.load(std::memory_order_relaxed);
        cyc = rb > 1e-6f ? beat / (double)rb : 0.0;
    } else {
        cyc = timeSec * (double)rateHz.load(std::memory_order_relaxed);
    }
    double p = cyc + (double)phase01.load(std::memory_order_relaxed);
    p -= std::floor(p); // [0,1)
    float w;
    switch (waveform.load(std::memory_order_relaxed)) {
    case 1: w = 1.0f - 4.0f * std::fabs((float)p - 0.5f); break; // triangle
    case 2: w = 2.0f * (float)p - 1.0f; break;                   // saw
    case 3: w = p < 0.5 ? 1.0f : -1.0f; break;                   // square
    case 4: {                                                    // random (sample & hold)
        uint32_t n = (uint32_t)(int64_t)std::floor(cyc);
        n ^= n >> 16;
        n *= 0x7feb352dU;
        n ^= n >> 15;
        n *= 0x846ca68bU;
        n ^= n >> 16;
        w = (float)((double)n / 4294967295.0) * 2.0f - 1.0f;
        break;
    }
    default: w = std::sin((float)p * 6.28318530718f); break; // sine
    }
    return w * depth.load(std::memory_order_relaxed);
}

CvLink& CvLink::operator=(const CvLink& o) {
    sourceKind = o.sourceKind;
    sourceModId = o.sourceModId;
    sourceDevice = o.sourceDevice;
    sourceParam = o.sourceParam;
    targetKind = o.targetKind;
    targetTrack = o.targetTrack;
    targetDevice = o.targetDevice;
    targetParam = o.targetParam;
    depth.store(o.depth.load(std::memory_order_relaxed), std::memory_order_relaxed);
    mode.store(o.mode.load(std::memory_order_relaxed), std::memory_order_relaxed);
    base.store(o.base.load(std::memory_order_relaxed), std::memory_order_relaxed);
    return *this;
}

} // namespace nota
