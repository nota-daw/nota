// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EnvShape — shaped ADSR and RC envelope helpers shared by subtractive synths.

#pragma once

#include <nota/dsp/rt/RtAttributes.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

struct EnvShapeState {
    float p = 0.0f;
    float from = 0.0f;
    int last = -1;
};

struct EnvShapeParams {
    float decayCurve = 0.5f;
    float releaseCurve = 0.5f;
    float exp = 0.0f;
};

struct EnvBends {
    float a = 0.0f, d = 0.0f, r = 0.0f;
    bool straight = true;
};

inline EnvBends envBends(const EnvShapeParams& s) {
    constexpr float kExpBend = 0.6f;
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

template <class Stage>
inline void advanceShapedEnv(
    float& env, Stage& st, EnvShapeState& sh, float atk, float dec, float sus, float rel, const EnvBends& k) {
    const int s = static_cast<int>(st);
    if (s != sh.last) {
        sh.last = s;
        sh.p = 0.0f;
        sh.from = env;
    }
    if (sh.p < 0.0f) {
        const bool bent = st == Stage::Attack    ? k.a != 0.0f
                          : st == Stage::Decay   ? k.d != 0.0f
                          : st == Stage::Release ? k.r != 0.0f
                                                 : false;
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

template <class Stage>
NOTA_ALWAYS_INLINE void stepEnv(
    float& env, Stage& st, EnvShapeState& sh, float atk, float dec, float sus, float rel, const EnvBends& k) {
    if (k.straight) {
        advanceStraightEnv(env, st, atk, dec, sus, rel);
        sh.p = -1.0f;
    } else {
        advanceShapedEnv(env, st, sh, atk, dec, sus, rel, k);
    }
}

constexpr int kEnvTeleFloats = 2;

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

struct RcEnvCoefs {
    float aC = 1, aTgt = 1.3f, dC = 1, rC = 1, offFloor = 3e-5f;
    float atkRate = 1, decRate = 1, relRate = 1;
};

inline bool rcEnvNative(const EnvShapeParams& p) {
    return p.exp >= 0.5f && p.decayCurve == 0.5f && p.releaseCurve == 0.5f;
}

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

inline void advanceRcEnv(
    float& env, int& st, EnvShapeState& sh, float sus, const RcEnvCoefs& c, const EnvShapeParams& p, bool native) {
    enum class S { Attack, Decay, Sustain, Release, Off };
    if (native) {
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
            if (sh.last == 1 && std::fabs(env - sus) < 1e-3f) sh.last = 2;
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
    if (st == 2 && sh.last != 2 && env <= sus + 1e-3f) sh.last = 2;
    S s = st == 1 ? S::Attack : st == 2 ? (sh.last == 2 ? S::Sustain : S::Decay) : st == 3 ? S::Release : S::Off;
    advanceShapedEnv(env, s, sh, c.atkRate, c.decRate, sus, c.relRate, envBends(p));
    if (s == S::Sustain) {
        sh.last = 2;
        env += (sus - env) * c.dC;
    }
    st = s == S::Attack ? 1 : (s == S::Decay || s == S::Sustain) ? 2 : s == S::Release ? 3 : 0;
}

inline void advanceRcEnv(
    float& env, int& st, EnvShapeState& sh, float sus, const RcEnvCoefs& c, const EnvShapeParams& p) {
    advanceRcEnv(env, st, sh, sus, c, p, rcEnvNative(p));
}

inline void publishRcEnvTele(std::atomic<float>* out, bool sounding, int st, EnvShapeState* sh, float env, float sus) {
    int code = 4;
    if (sounding && sh && st != 0) {
        code = st == 1 ? 0 : st == 2 ? (sh->last == 2 || std::fabs(env - sus) < 1e-3f ? 2 : 1) : 3;
        if (code != sh->last) {
            if (!(code == 2 && sh->last == 1)) sh->from = env;
            sh->last = code;
            sh->p = -1.0f;
        }
    }
    out[0].store(static_cast<float>(code), std::memory_order_relaxed);
    out[1].store(code == 4 ? 0.0f : rcEnvProgress(env, *sh, sus), std::memory_order_relaxed);
}

} // namespace nota

namespace nota::devices {
using nota::advanceRcEnv;
using nota::advanceRcStep;
using nota::advanceShapedEnv;
using nota::advanceStraightEnv;
using nota::EnvBends;
using nota::envBends;
using nota::envCurve;
using nota::EnvShapeParams;
using nota::EnvShapeState;
using nota::kEnvTeleFloats;
using nota::publishEnvTele;
using nota::publishRcEnvTele;
using nota::RcEnvCoefs;
using nota::rcEnvNative;
using nota::rcEnvProgress;
using nota::readEnvTele;
using nota::shapedEnvProgress;
using nota::stepEnv;
} // namespace nota::devices
