// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A shaped ADSR step shared by the subtractive built-ins (Volt, Aurora, Operator, Bass,
// Physical) — nota-daw/nota#531. Each stage runs on a 0..1 progress p, so a stage can bend:
//
//   curve(p, k) = p                     k = 0  (straight — the old envelopes, sample-exact)
//               = 1 − (1 − p)^(1 + 4k)  k > 0  (fast start, slow finish: the analog RC look)
//               = p^(1 + 4|k|)          k < 0  (slow start, fast finish)
//
// A stage keeps its duration whatever the bend: attack climbs from where the voice was,
// decay falls from where it peaked to the sustain level, release falls from where the key
// was let go, each at the stage's rate per sample, exactly as the straight envelope did.
// With every k = 0 the step is the old `env ±= rate` code byte for byte, so projects and
// presets made before the curves existed render the same.
//
// Real-time safe: no allocation, no locks; std::pow only while a stage is bent.

#pragma once

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

// Per-voice, per-envelope state beside the level and the stage the instrument already keeps.
struct EnvShapeState {
    float p = 0.0f;    // progress through the current stage, 0..1 (telemetry reads it too)
    float from = 0.0f; // the level the stage started from
    int last = -1;     // the stage seen on the previous step (-1: re-arm on the next step)
};

// The three shape params of one envelope, as the param table stores them (normalized).
struct EnvShapeParams {
    float decayCurve = 0.5f;   // 0.5 = straight
    float releaseCurve = 0.5f; // 0.5 = straight
    float exp = 0.0f;          // ≥ 0.5: Exp — every stage gets the analog bend on top
};

// The bends k (−1..1) of attack, decay and release for a params set.
struct EnvBends {
    float a = 0.0f, d = 0.0f, r = 0.0f;
};

inline EnvBends envBends(const EnvShapeParams& s) {
    constexpr float kExpBend = 0.6f; // how far Exp bends each stage toward the RC curve
    const float e = s.exp >= 0.5f ? kExpBend : 0.0f;
    auto k = [e](float n) { return std::clamp((n - 0.5f) * 2.0f + e, -1.0f, 1.0f); };
    return {e, k(s.decayCurve), k(s.releaseCurve)};
}

inline float envCurve(float p, float k) {
    if (k == 0.0f) return p;
    const float e = 1.0f + 4.0f * std::fabs(k);
    return k > 0.0f ? 1.0f - std::pow(1.0f - p, e) : std::pow(p, e);
}

// One sample of the ADSR. `Stage` is the instrument's own enum class with the members
// Attack, Decay, Sustain, Release and Off.
template <class Stage>
inline void advanceShapedEnv(float& env, Stage& st, EnvShapeState& sh, float atk, float dec, float sus, float rel,
    const EnvBends& k) {
    const int s = static_cast<int>(st);
    if (s != sh.last) {
        sh.last = s;
        sh.p = 0.0f;
        sh.from = env;
    }
    switch (st) {
    case Stage::Attack: {
        const float span = 1.0f - sh.from;
        if (k.a == 0.0f) {
            env += atk;
            if (env >= 1.0f) {
                env = 1.0f;
                st = Stage::Decay;
            }
            sh.p = span > 1e-6f ? std::clamp((env - sh.from) / span, 0.0f, 1.0f) : 1.0f;
        } else if (span <= 1e-6f) {
            env = 1.0f;
            st = Stage::Decay;
        } else {
            sh.p += atk / span;
            if (sh.p >= 1.0f) {
                env = 1.0f;
                st = Stage::Decay;
            } else {
                env = sh.from + span * envCurve(sh.p, k.a);
            }
        }
        break;
    }
    case Stage::Decay: {
        const float span = sh.from - sus;
        if (k.d == 0.0f) {
            env -= dec;
            if (env <= sus) {
                env = sus;
                st = Stage::Sustain;
            }
            sh.p = span > 1e-6f ? std::clamp((sh.from - env) / span, 0.0f, 1.0f) : 1.0f;
        } else if (span <= 1e-6f) {
            env = sus;
            st = Stage::Sustain;
        } else {
            sh.p += dec / span;
            if (sh.p >= 1.0f) {
                env = sus;
                st = Stage::Sustain;
            } else {
                env = sh.from - span * envCurve(sh.p, k.d);
            }
        }
        break;
    }
    case Stage::Sustain: break;
    case Stage::Release: {
        const float span = sh.from;
        if (k.r == 0.0f) {
            env -= rel;
            if (env <= 0.0f) {
                env = 0.0f;
                st = Stage::Off;
            }
            sh.p = span > 1e-6f ? std::clamp((sh.from - env) / span, 0.0f, 1.0f) : 1.0f;
        } else if (span <= 1e-6f) {
            env = 0.0f;
            st = Stage::Off;
        } else {
            sh.p += rel / span;
            if (sh.p >= 1.0f) {
                env = 0.0f;
                st = Stage::Off;
            } else {
                env = span * (1.0f - envCurve(sh.p, k.r));
            }
        }
        break;
    }
    case Stage::Off: break;
    }
}

// Telemetry for the editor's envelope graphs: two floats per envelope — the stage
// (0 A · 1 D · 2 S · 3 R · 4 none sounding) and the progress through it.
constexpr int kEnvTeleFloats = 2;

template <class Stage>
inline void publishEnvTele(std::atomic<float>* out, bool sounding, Stage st, const EnvShapeState& sh) {
    const bool on = sounding && st != Stage::Off;
    out[0].store(on ? static_cast<float>(static_cast<int>(st)) : 4.0f, std::memory_order_relaxed);
    out[1].store(on ? sh.p : 0.0f, std::memory_order_relaxed);
}

inline int32_t readEnvTele(const std::atomic<float>* in, int n, float* out, int32_t maxN) {
    const int32_t k = std::min<int32_t>(maxN, n);
    for (int32_t i = 0; i < k; ++i) out[i] = in[i].load(std::memory_order_relaxed);
    return k < 0 ? 0 : k;
}

// ---- RC envelopes (Consort, Twin) ---------------------------------------------------
// An analog-style generator: the attack charges toward a target above 1, decay and release
// fall exponentially. With Exp on and both curves straight (their defaults on these
// instruments) the step is that generator, unchanged; any other shape runs the shaped step
// above at the segments' nominal lengths. `st` is the instrument's own code:
// 0 idle · 1 attack · 2 decay / sustain · 3 release.
struct RcEnvCoefs {
    float aC = 1, aTgt = 1.3f, dC = 1, rC = 1, offFloor = 3e-5f; // the RC generator
    float atkRate = 1, decRate = 1, relRate = 1;                 // per-sample rates of the shaped step
};

inline bool rcEnvNative(const EnvShapeParams& p) {
    return p.exp >= 0.5f && p.decayCurve == 0.5f && p.releaseCurve == 0.5f;
}

inline void advanceRcEnv(float& env, int& st, EnvShapeState& sh, float sus, const RcEnvCoefs& c, const EnvShapeParams& p) {
    enum class S { Attack, Decay, Sustain, Release, Off };
    if (rcEnvNative(p)) {
        const int code = st == 1 ? 0 : st == 2 ? (sh.last == 2 ? 2 : 1) : st == 3 ? 3 : 4;
        if (code != sh.last) {
            sh.last = code;
            sh.p = 0.0f;
            sh.from = env;
        }
        switch (st) {
        case 1:
            env += (c.aTgt - env) * c.aC;
            if (env >= 1.0f) {
                env = 1.0f;
                st = 2;
            }
            sh.p = sh.from < 1.0f ? std::clamp((env - sh.from) / (1.0f - sh.from), 0.0f, 1.0f) : 1.0f;
            break;
        case 2:
            env += (sus - env) * c.dC;
            sh.p = sh.from - sus > 1e-6f ? std::clamp((sh.from - env) / (sh.from - sus), 0.0f, 1.0f) : 1.0f;
            if (sh.last == 1 && std::fabs(env - sus) < 1e-3f) sh.last = 2; // the graph's sustain
            break;
        case 3:
            env -= env * c.rC;
            if (env < c.offFloor) {
                env = 0.0f;
                st = 0;
            }
            sh.p = sh.from > 1e-6f ? std::clamp((sh.from - env) / sh.from, 0.0f, 1.0f) : 1.0f;
            break;
        default: break;
        }
        return;
    }
    S s = st == 1 ? S::Attack : st == 2 ? (sh.last == 2 ? S::Sustain : S::Decay) : st == 3 ? S::Release : S::Off;
    advanceShapedEnv(env, s, sh, c.atkRate, c.decRate, sus, c.relRate, envBends(p));
    if (s == S::Sustain) sh.last = 2;
    st = s == S::Attack ? 1 : (s == S::Decay || s == S::Sustain) ? 2 : s == S::Release ? 3 : 0;
}

// The telemetry stage of an RC envelope (EnvShape.h's code: 0 A · 1 D · 2 S · 3 R · 4 none).
inline void publishRcEnvTele(std::atomic<float>* out, bool sounding, int st, const EnvShapeState& sh) {
    const int code = !sounding || st == 0 ? 4 : st == 1 ? 0 : st == 2 ? (sh.last == 2 ? 2 : 1) : 3;
    out[0].store(static_cast<float>(code), std::memory_order_relaxed);
    out[1].store(code == 4 ? 0.0f : sh.p, std::memory_order_relaxed);
}

} // namespace nota
