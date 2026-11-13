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

#include "dsp/core/RtAttributes.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

// Per-voice, per-envelope state beside the level and the stage the instrument already keeps.
struct EnvShapeState {
    // Progress through the current stage, 0..1. A straight stage leaves it at −1 and it is
    // worked out from the level when asked (shapedEnvProgress), not on every sample.
    float p = 0.0f;
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
    bool straight = true; // every k = 0: stepEnv takes the plain step (decided once per block)
};

inline EnvBends envBends(const EnvShapeParams& s) {
    constexpr float kExpBend = 0.6f; // how far Exp bends each stage toward the RC curve
    const float e = s.exp >= 0.5f ? kExpBend : 0.0f;
    auto k = [e](float n) { return std::clamp((n - 0.5f) * 2.0f + e, -1.0f, 1.0f); };
    EnvBends b{e, k(s.decayCurve), k(s.releaseCurve)};
    b.straight = b.a == 0.0f && b.d == 0.0f && b.r == 0.0f;
    return b;
}

inline float envCurve(float p, float k) {
    if (k == 0.0f) return p;
    const float e = 1.0f + 4.0f * std::fabs(k);
    return k > 0.0f ? 1.0f - std::pow(1.0f - p, e) : std::pow(p, e);
}

// The progress through a stage the straight step left at −1, from the level `env`.
template <class Stage> inline float shapedEnvProgress(Stage st, const EnvShapeState& sh, float env, float sus) {
    if (sh.p >= 0.0f) return sh.p;
    auto frac = [](float done, float span) { return span > 1e-6f ? std::clamp(done / span, 0.0f, 1.0f) : 1.0f; };
    switch (st) {
    case Stage::Attack: return frac(env - sh.from, 1.0f - sh.from);
    case Stage::Decay: return frac(sh.from - env, sh.from - sus);
    case Stage::Release: return frac(sh.from - env, sh.from);
    default: return 0.0f;
    }
}

// One sample of a straight ADSR: the old `env ±= rate` step, and nothing else. It leaves the
// shape state alone — a few instructions per envelope per sample add up on a voice of four
// operators — so the graphs note the stage once per block instead (publishEnvTele). Callers
// set the state's progress to −1 after it (as stepEnv does), so a bend that comes back later
// in the stage starts from the level instead of an old progress.
template <class Stage>
NOTA_ALWAYS_INLINE void advanceStraightEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
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
    case Stage::Release:
        env -= rel;
        if (env <= 0.0f) {
            env = 0.0f;
            st = Stage::Off;
        }
        break;
    default: break;
    }
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
    // A stage stepped straight until now (its progress left at −1, by the straight branches
    // below or by advanceStraightEnv's callers) and bent from here on starts its bend from the
    // level it has reached: the level carries on without a jump, whatever the old progress was.
    if (sh.p < 0.0f) {
        const bool bent = st == Stage::Attack ? k.a != 0.0f : st == Stage::Decay ? k.d != 0.0f : st == Stage::Release ? k.r != 0.0f : false;
        if (bent) {
            sh.from = env;
            sh.p = 0.0f;
        }
    }
    switch (st) {
    case Stage::Attack: {
        const float span = 1.0f - sh.from;
        if (k.a == 0.0f) {
            env += atk;
            sh.p = -1.0f;
            if (env >= 1.0f) {
                env = 1.0f;
                st = Stage::Decay;
                sh.p = 1.0f;
            }
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
            sh.p = -1.0f;
            if (env <= sus) {
                env = sus;
                st = Stage::Sustain;
                sh.p = 1.0f;
            }
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
            sh.p = -1.0f;
            if (env <= 0.0f) {
                env = 0.0f;
                st = Stage::Off;
                sh.p = 1.0f;
            }
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

// One sample: the plain step while every stage is straight (the default), else the shaped
// one. A stage bent while a note plays starts its bend from the level it has reached (the
// straight step marks the progress unknown, so the shaped one never reuses an old one).
template <class Stage>
NOTA_ALWAYS_INLINE void stepEnv(float& env, Stage& st, EnvShapeState& sh, float atk, float dec, float sus, float rel, const EnvBends& k) {
    if (k.straight) {
        advanceStraightEnv(env, st, atk, dec, sus, rel);
        sh.p = -1.0f; // its progress is the level's now: a bend that comes back starts from here
    } else {
        advanceShapedEnv(env, st, sh, atk, dec, sus, rel, k);
    }
}

// Telemetry for the editor's envelope graphs: two floats per envelope — the stage
// (0 A · 1 D · 2 S · 3 R · 4 none sounding) and the progress through it.
constexpr int kEnvTeleFloats = 2;

// `sh` is the shown envelope's shape state (null: none sounding), `env` its level and `sus`
// its sustain. A straight envelope steps without its shape state, so a stage it entered is
// noted here, once per block: the graph's progress starts at most one block late.
template <class Stage>
inline void publishEnvTele(std::atomic<float>* out, bool sounding, Stage st, EnvShapeState* sh, float env, float sus) {
    const bool on = sounding && sh && st != Stage::Off;
    float p = 0.0f;
    if (on) {
        const int s = static_cast<int>(st);
        if (s != sh->last) {
            sh->last = s;
            sh->p = -1.0f;
            sh->from = env;
        }
        p = shapedEnvProgress(st, *sh, env, sus);
    }
    out[0].store(on ? static_cast<float>(static_cast<int>(st)) : 4.0f, std::memory_order_relaxed);
    out[1].store(p, std::memory_order_relaxed);
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

// The progress through an RC envelope's stage. The generator's own step leaves sh.p at −1
// (it doesn't need it), and the progress is worked out here when the graphs or the shaped
// step ask, instead of on every sample.
inline float rcEnvProgress(float env, const EnvShapeState& sh, float sus) {
    if (sh.p >= 0.0f) return sh.p;
    switch (sh.last) {
    case 0: return sh.from < 1.0f ? std::clamp((env - sh.from) / (1.0f - sh.from), 0.0f, 1.0f) : 1.0f;
    case 1:
    case 2: return sh.from - sus > 1e-6f ? std::clamp((sh.from - env) / (sh.from - sus), 0.0f, 1.0f) : 1.0f;
    case 3: return sh.from > 1e-6f ? std::clamp((sh.from - env) / sh.from, 0.0f, 1.0f) : 1.0f;
    default: return 0.0f;
    }
}

// One sample of the RC generator alone (st: 0 idle · 1 attack · 2 decay / sustain · 3 release),
// small enough to inline into a voice loop. Callers that take it while the shape is native
// (rcEnvNative, decided once per block) set the state's progress to −1 after it: the graphs
// note the stage once per block (publishRcEnvTele), and a shape that comes back starts from
// the level.
NOTA_ALWAYS_INLINE void advanceRcStep(float& env, int& st, float sus, const RcEnvCoefs& c) {
    switch (st) {
    case 1:
        env += (c.aTgt - env) * c.aC;
        if (env >= 1.0f) {
            env = 1.0f;
            st = 2;
        }
        break;
    case 2: env += (sus - env) * c.dC; break;
    case 3:
        env -= env * c.rC;
        if (env < c.offFloor) {
            env = 0.0f;
            st = 0;
        }
        break;
    default: break;
    }
}

// `native` is rcEnvNative(p), which callers can work out once per block.
inline void advanceRcEnv(float& env, int& st, EnvShapeState& sh, float sus, const RcEnvCoefs& c, const EnvShapeParams& p,
    bool native) {
    enum class S { Attack, Decay, Sustain, Release, Off };
    if (native) {
        // A new stage starts from where the level is (stage codes as the graphs read them).
        auto enter = [&](int code) {
            sh.last = code;
            sh.from = env;
        };
        sh.p = -1.0f;
        switch (st) {
        case 1:
            if (sh.last != 0) enter(0);
            env += (c.aTgt - env) * c.aC;
            if (env >= 1.0f) {
                env = 1.0f;
                st = 2;
            }
            break;
        case 2:
            if (sh.last != 1 && sh.last != 2) enter(1);
            env += (sus - env) * c.dC;
            if (sh.last == 1 && std::fabs(env - sus) < 1e-3f) sh.last = 2; // the graph's sustain
            break;
        case 3:
            if (sh.last != 3) enter(3);
            env -= env * c.rC;
            if (env < c.offFloor) {
                env = 0.0f;
                st = 0;
            }
            break;
        default:
            if (sh.last != 4) enter(4);
            break;
        }
        return;
    }
    // The plain generator (advanceRcStep, Twin's inline step) doesn't note when a voice reaches
    // its sustain, and a Sustain raised above a held level isn't a decay at all: a voice in the
    // decay / sustain stage whose level is not above the sustain follows it as the sustain does,
    // instead of being taken for a decay that lands on the new level at once.
    if (st == 2 && sh.last != 2 && env <= sus + 1e-3f) sh.last = 2;
    S s = st == 1 ? S::Attack : st == 2 ? (sh.last == 2 ? S::Sustain : S::Decay) : st == 3 ? S::Release : S::Off;
    advanceShapedEnv(env, s, sh, c.atkRate, c.decRate, sus, c.relRate, envBends(p));
    if (s == S::Sustain) {
        sh.last = 2;
        env += (sus - env) * c.dC; // a held note follows the Sustain knob, as the generator does
    }
    st = s == S::Attack ? 1 : (s == S::Decay || s == S::Sustain) ? 2 : s == S::Release ? 3 : 0;
}

inline void advanceRcEnv(float& env, int& st, EnvShapeState& sh, float sus, const RcEnvCoefs& c, const EnvShapeParams& p) {
    advanceRcEnv(env, st, sh, sus, c, p, rcEnvNative(p));
}

// The telemetry stage of an RC envelope (EnvShape.h's code: 0 A · 1 D · 2 S · 3 R · 4 none)
// and its progress; `sh` is the shown envelope's shape state (null: none sounding), `env` and
// `sus` its level and sustain. advanceRcStep doesn't track its stage, so a stage it entered is
// noted here, once per block: the progress starts at most one block late.
inline void publishRcEnvTele(std::atomic<float>* out, bool sounding, int st, EnvShapeState* sh, float env, float sus) {
    int code = 4;
    if (sounding && sh && st != 0) {
        code = st == 1 ? 0 : st == 2 ? (sh->last == 2 || std::fabs(env - sus) < 1e-3f ? 2 : 1) : 3;
        if (code != sh->last) {
            if (!(code == 2 && sh->last == 1)) sh->from = env; // decay → sustain keeps the decay's start
            sh->last = code;
            sh->p = -1.0f;
        }
    }
    out[0].store(static_cast<float>(code), std::memory_order_relaxed);
    out[1].store(code == 4 ? 0.0f : rcEnvProgress(env, *sh, sus), std::memory_order_relaxed);
}

} // namespace nota
