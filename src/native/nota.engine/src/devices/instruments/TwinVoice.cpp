// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — the voice's per-sample path (see TwinVoice.h).

#include "devices/instruments/TwinVoice.h"

#include <algorithm>
#include <cmath>

namespace nota {
namespace twin {

namespace {

inline float triAt(float p) { return p < 0.5f ? 4.0f * p - 1.0f : 3.0f - 4.0f * p; }

// The triangle's slope corners crossed while the phase moved p0 → p1 (no reset), as BLAMPs.
inline void triCorners(pentad::Osc& o, double p0, float p1, float wrapD, double dt, float gTri) {
    if (gTri == 0.0f) return;
    const float sl = 8.0f * static_cast<float>(dt) * gTri;
    const float inv = static_cast<float>(1.0 / dt);
    if (wrapD >= 0.0f) {
        if (p0 < 0.5) o.ring.blamp((0.5f + p1) * inv, -sl);
        o.ring.blamp(wrapD, sl);
        if (p1 >= 0.5f) o.ring.blamp((p1 - 0.5f) * inv, -sl);
    } else if (p0 < 0.5 && p1 >= 0.5f)
        o.ring.blamp((p1 - 0.5f) * inv, -sl);
}

// One os-rate sample of a triangle / saw / pulse oscillator. syncD ≥ 0: the master wrapped
// syncD samples ago, so this one resets there. Returns this oscillator's own wrap distance
// (−1 = none) and writes its sample to `out`.
inline float oscStep(pentad::Osc& o, double dt, float duty, const WaveGains& w, float syncD, float& out) {
    using namespace pentad;
    const float gz = w.pul * pulseZone(duty);
    float wrap = -1.0f;
    if (syncD >= 0.0f) {
        const double p0 = o.ph;
        advance(o, dt * (1.0 - syncD), syncD, dt, duty, w.saw, gz);
        const float pr = static_cast<float>(o.ph);
        if (w.tri != 0.0f && p0 < 0.5 && pr >= 0.5f) // corner before the reset
            o.ring.blamp(syncD + (pr - 0.5f) * static_cast<float>(1.0 / dt), -8.0f * static_cast<float>(dt) * w.tri);
        o.ring.blep(syncD, -2.0f * pr * w.saw); // saw snaps back to −1
        if (!o.hi && duty > 0.0f) o.ring.blep(syncD, 2.0f * gz);
        o.hi = duty > 0.0f;
        if (w.tri != 0.0f) { // triangle: value jump to −1 and slope back to rising
            o.ring.blep(syncD, (-1.0f - triAt(pr)) * w.tri);
            if (pr >= 0.5f) o.ring.blamp(syncD, 8.0f * static_cast<float>(dt) * w.tri);
        }
        o.ph = 0.0;
        advance(o, dt * syncD, 0.0f, dt, duty, w.saw, gz);
        triCorners(o, 0.0, static_cast<float>(o.ph), -1.0f, dt, w.tri);
    } else {
        const double p0 = o.ph;
        wrap = advance(o, dt, 0.0f, dt, duty, w.saw, gz);
        triCorners(o, p0, static_cast<float>(o.ph), wrap, dt, w.tri);
    }
    const float p = static_cast<float>(o.ph);
    o.ring.naive(w.saw * (2.0f * p - 1.0f) + w.tri * triAt(p) + gz * ((o.hi ? 1.0f : -1.0f) - (2.0f * duty - 1.0f)));
    out = o.ring.pop();
    return wrap;
}

// Metal: a sine folder on the oscillator, first-order antiderivative anti-aliasing.
inline float fold(float x, float drive, double& x1, float& f1) {
    const float a = 1.5707963f * drive;
    const float F = (1.0f - std::cos(a * x)) / a;
    const double dx = x - x1;
    const float y = std::fabs(dx) > 1e-5 ? static_cast<float>((F - f1) / dx) : std::sin(a * 0.5f * (x + static_cast<float>(x1)));
    x1 = x;
    f1 = F;
    return y;
}

inline float softBound(float x) { return x / (1.0f + std::fabs(x)); }

} // namespace

void voiceControl(Voice& V, const PartCtl& c, double sr) {
    // The slow drift walk moves once per control interval.
    V.walkLeft -= kCtl;
    if (V.walkLeft <= 0) {
        V.walkTgt = V.rng.bi();
        V.walkLeft = static_cast<int>(sr * (0.4 + 1.6 * V.rng.uni()));
    }
    V.walk += (V.walkTgt - V.walk) * 0.02f;

    const float ef = 1.0f + c.drift * 0.2f * V.oEnv;
    V.fenv.setTimes(c.fA * ef, c.fD * ef, c.fS, c.fR * ef, c.fCurve, sr, false);
    V.aenv.setTimes(c.aA * ef, c.aD * ef, c.aS, c.aR * ef, c.aCurve, sr, false);
    V.menv.setTimes(c.mA, c.mD, c.mS, c.mR, 0.5f, sr, c.mLoop);
    V.glideC = c.glideSec > 0.0f ? static_cast<float>(1.0 - std::exp(-4.6 / (c.glideSec * sr))) : 1.0f;
    V.mm.setReso(c.mmRes);
    V.mm.setMode(c.mmMode);

    const bool snap = V.fresh;
    const float oct = 12.0f * static_cast<float>(c.octave);
    for (int k = 0; k < 2; ++k) {
        V.rPitch[k].to(c.semi[k] + oct + c.drift * (0.08f * V.oP[k] + 0.03f * V.walk), snap);
        V.rLvl[k].to(c.lvl[k], snap);
        V.rDuty[k].to(c.duty[k], snap);
        V.rWave[k].to(c.wave[k], snap);
        V.rMetal[k].to(c.metal[k], snap);
    }
    V.rMmCut.to(c.mmCut + c.drift * 0.15f * V.oCut, snap);
    V.rLdCut.to(c.ldCut + c.drift * 0.15f * V.oCut, snap);
    V.rNoise.to(c.noiseLvl, snap);
    const float stackGain = V.stackN > 1 ? std::pow(static_cast<float>(V.stackN), -0.4f) : 1.0f;
    V.rGain.to(c.gain * stackGain, snap);
    V.fresh = false;
}

int renderVoice(Voice& V, const PartCtl& c, int from, int n, int os, double sr, float modWheel, int fadeLen,
    const float* gLfo1, float* osL, float* osR) {
    const double fsOs = sr * os;
    const double invFsOs = 1.0 / fsOs;
    V.mm.setSampleRate(fsOs);
    V.ld.setSampleRate(fsOs);
    const float velA = 1.0f - c.aVel * (1.0f - V.vel);
    const float velF = 1.0f - c.fVel * (1.0f - V.vel);
    const float vib = (c.vibSemis * modWheel + c.vibExtra) * c.lfoAmt[0];
    const double lfoInc = c.lfoHz[0] / sr;
    const float noiseA = fast_math::exp2f(-c.noiseCol * 7.0f), noiseG = 1.0f + c.noiseCol * 2.5f;
    const float o2Key = c.o2lo ? -36.0f : 0.0f; // low-frequency Osc 2 drops off the keyboard
    const float invFade = fadeLen > 0 ? 1.0f / static_cast<float>(fadeLen) : 1.0f;

    for (int i = from; i < n; ++i) {
        // -- a stolen voice fades out, then the caller hands it the pending note --
        float fadeG = 1.0f;
        if (V.fade > 0) {
            --V.fade;
            fadeG = static_cast<float>(V.fade) * invFade;
            if (V.fade == 0) {
                if (V.pendNote < 0) {
                    V.active = V.gate = false;
                    V.aenv = V.fenv = V.menv = Env{};
                }
                return i;
            }
        }
        V.pitch += (V.pitchTgt - V.pitch) * V.glideC;
        V.ex.tick(c.exAdd, c.exCoef);
        const float fe = V.fenv.tick();
        const float ae = V.aenv.tick();
        V.menv.tick();
        if (V.aenv.st == Env::Idle && V.fade == 0) {
            V.active = V.gate = false;
            return i;
        }

        // -- LFO 1 → vibrato (fade-in per voice) --
        float lv = 0.0f;
        if (vib != 0.0f) {
            const float raw = c.lfoGlobal[0] ? gLfo1[i] : V.lfo[0].step(lfoInc, c.lfoShape[0]);
            lv = raw * V.lfoFadeLvl[0] * vib;
        } else if (!c.lfoGlobal[0])
            V.lfo[0].step(lfoInc, c.lfoShape[0]); // keep the phase running
        if (V.lfoFadeLvl[0] < 1.0f) V.lfoFadeLvl[0] = std::min(1.0f, V.lfoFadeLvl[0] + V.lfoFadeInc[0]);

        const float key = V.pitch + V.det + V.ex.bend();
        const float p1 = key + V.rPitch[0].next() + lv;
        const float p2 = key + o2Key + V.rPitch[1].next() + lv;
        const double dt1 = std::min(440.0 * fast_math::exp2f((p1 - 69.0f) * (1.0f / 12.0f)) * invFsOs, 0.45);
        const double dt2 = std::min(440.0 * fast_math::exp2f((p2 - 69.0f) * (1.0f / 12.0f)) * invFsOs, 0.45);
        const float kt = (key - 60.0f) * (1.0f / 12.0f);
        const float mmOct = V.rMmCut.next() + c.mmKey * kt + fe * c.mmEnv * velF;
        const float ldOct = V.rLdCut.next() + c.ldKey * kt + fe * c.ldEnv * velF;
        if (c.fmFilt == 0.0f) V.mm.setG(V.mm.prewarp(fast_math::exp2f(std::clamp(mmOct, 3.3f, 14.6f))));
        V.ld.setParams(fast_math::exp2f(std::clamp(ldOct, 3.3f, 14.6f)), c.ldRes);

        const float l1 = V.rLvl[0].next(), l2 = V.rLvl[1].next(), ln = V.rNoise.next();
        const float du1 = V.rDuty[0].next(), du2 = V.rDuty[1].next();
        const WaveGains w1 = waveGains(V.rWave[0].next()), w2 = waveGains(V.rWave[1].next());
        const float m1 = V.rMetal[0].next() * w1.tri, m2 = V.rMetal[1].next() * w2.tri;
        const float amp = ae * velA * V.rGain.next() * fadeG;
        const float a1 = l1 * (1.0f - c.bal[0]), b1 = l1 * c.bal[0];
        const float a2 = l2 * (1.0f - c.bal[1]), b2 = l2 * c.bal[1];
        const float an = ln * (1.0f - c.noiseBal), bn = ln * c.noiseBal;
        float* L = osL + i * os;
        float* R = osR + i * os;

        for (int s = 0; s < os; ++s) {
            double d1 = dt1;
            if (c.fm21 != 0.0f) d1 = std::min(d1 * fast_math::exp2f(c.fm21 * V.o2Out), 0.45);
            float y1, y2;
            const float wrap1 = oscStep(V.osc[0], d1, du1, w1, -1.0f, y1);
            oscStep(V.osc[1], dt2, du2, w2, c.sync ? wrap1 : -1.0f, y2);
            if (m1 > 0.0f) y1 += m1 * (fold(y1, 1.0f + 3.0f * m1, V.fx1[0], V.fy1[0]) - y1);
            else V.fx1[0] = y1;
            if (m2 > 0.0f) y2 += m2 * (fold(y2, 1.0f + 3.0f * m2, V.fx1[1], V.fy1[1]) - y2);
            else V.fx1[1] = y2;
            V.o2Out = y2;

            V.noiseRng ^= V.noiseRng << 13;
            V.noiseRng ^= V.noiseRng >> 17;
            V.noiseRng ^= V.noiseRng << 5;
            const float white = static_cast<float>(V.noiseRng >> 8) * (2.0f / 16777216.0f) - 1.0f;
            V.noiseLp += noiseA * (white - V.noiseLp);
            const float nz = V.noiseLp * noiseG;

            // Brute feedback, plus a −100 dB floor so a resonant filter can sing on its own.
            const float fbS = c.brute * 0.9f * softBound(1.5f * V.fb) + 1e-5f * white;
            const float mmIn = a1 * y1 + a2 * y2 + an * nz + fbS;
            const float ldIn = b1 * y1 + b2 * y2 + bn * nz + fbS;
            if (c.fmFilt != 0.0f)
                V.mm.setG(V.mm.prewarp(fast_math::exp2f(std::clamp(mmOct + c.fmFilt * y2, 3.3f, 14.6f))));
            const float mo = V.mm.process(mmIn);
            const float li = ldIn + (1.0f - c.route) * mo;
            const float lo = c.ld12 ? V.ld.process12(li) : V.ld.process(li);
            float out = lo + c.route * mo;
            V.fb = out;
            if (c.drive > 0.0f)
                out = V.sat.process(out * (1.0f + 6.0f * c.drive) * 0.6f) * (1.0f / 0.6f) / (1.0f + 1.5f * c.drive);
            const float o = out * amp;
            L[s] += o * V.panL;
            R[s] += o * V.panR;
        }
        // Extreme settings must never poison the voice.
        if (!std::isfinite(V.fb)) {
            V.mm.reset();
            V.ld.reset();
            V.sat.reset();
            V.fb = 0.0f;
        }
    }
    return n;
}

} // namespace twin
} // namespace nota
