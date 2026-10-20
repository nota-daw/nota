// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RhythmVoiceDsp implementation. Verbatim synthesis from the header-only
// RhythmMachine, re-homed: oscillator/noise/envelope/filter math stays
// std::-exact, only the downstream drive/saturation stages use
// fast_math::tanhPrecise.

#include "RhythmVoiceDsp.h"

#include "dsp/math/FastMath.h"

namespace nota {

void RhythmVoiceDsp::trigger(
    int v, float velocity, bool isSample, const SampleBuffer* sb, float start01, float length01, bool reverse) {
    Voice& vc = voices[v];
    vc.active = true;
    vc.vel = std::clamp(velocity, 0.0f, 1.0f);
    vc.ph = 0.0;
    vc.ph2 = 0.0;
    vc.env = 1.0f;
    vc.env2 = 1.0f;
    vc.pitchEnv = 1.0f;
    vc.burst = 0;
    vc.burstT = 0.0f;
    vc.splay = (isSample && sb && !sb->empty());
    vc.spos = 0.0;
    vc.lp = 0.0f;
    vc.lp2 = 0.0f;
    if (vc.splay) {
        // The played region: Start .. Start + Length of the file (at least ~1 ms); Reverse
        // plays it back to front.
        const double n = (double)sb->frames;
        const double st = std::clamp((double)start01, 0.0, 1.0) * n;
        const double en = std::clamp(st + (double)length01 * n, std::min(n, st + 48.0), n);
        vc.sBeg = st;
        vc.sEnd = en;
        vc.srev = reverse;
        vc.spos = vc.srev ? std::max(st, en - 1.0) : st;
    }
    if (v == 4) voices[5].active = false; // closed hat chokes open hat (808)
}

void RhythmVoiceDsp::sampleVoice(int v, float& outL, float& outR, const Params& p, const SampleBuffer& b) {
    Voice& vc = voices[v];
    const double sampleRate = p.sampleRate;
    float l, r;
    b.readStereo((int64_t)vc.spos, l, r);
    const double rate =
        (b.sourceSampleRate > 0 ? b.sourceSampleRate / sampleRate : 1.0) * std::exp2((p.tune - 0.5) * 2.0);
    // A 64-frame fade into the region's far edge, so a Length cut never clicks.
    const double left = vc.srev ? vc.spos - vc.sBeg : vc.sEnd - vc.spos;
    if (left < 64.0 * rate) {
        const float f = (float)std::max(0.0, left / (64.0 * rate));
        l *= f;
        r *= f;
    }
    vc.spos += vc.srev ? -rate : rate;
    // Tone as a gentle one-pole low-pass (0 = dark, 1 = open).
    const float lpc = 0.05f + p.tone * 0.95f;
    vc.lp += lpc * (l - vc.lp);
    vc.lp2 += lpc * (r - vc.lp2);
    l = vc.lp;
    r = vc.lp2;
    if (p.drive > 0.001f) {
        const float g = 1.0f + p.drive * 3.0f;
        l = fast_math::tanhPrecise(l * g);
        r = fast_math::tanhPrecise(r * g);
    }
    vc.env *= decayCoef(expMap(p.decay, 0.05f, 2.0f), sampleRate);
    if ((vc.srev ? vc.spos < vc.sBeg : vc.spos >= vc.sEnd) || vc.env < 1e-4f) {
        vc.active = false;
        vc.splay = false;
    }
    const float k = vc.env * vc.vel * p.level;
    outL = l * k;
    outR = r * k;
}

void RhythmVoiceDsp::voiceStereo(int v, float& l, float& r, const Params& p, const SampleBuffer* sb) {
    if (voices[v].splay) {
        sampleVoice(v, l, r, p, *sb);
        return;
    }
    l = r = voiceSample(v, p);
}

float RhythmVoiceDsp::voiceSample(int v, const Params& p) {
    Voice& vc = voices[v];
    const float tune = p.tune, decay = p.decay, punch = p.punch;
    const float tone = p.tone, drive = p.drive, level = p.level;
    const double sampleRate = p.sampleRate;
    const double invSr = 1.0 / sampleRate;
    float out = 0.0f;

    switch (v) {
    case 0:
    case 6: { // Kick / Tom — sine with a pitch drop + click
        const float base = (v == 0) ? expMap(tune, 30.0f, 120.0f) : expMap(tune, 80.0f, 300.0f);
        const float pdrop = (v == 0) ? 3.0f : 2.0f;
        float f = base * (1.0f + vc.pitchEnv * pdrop);
        vc.ph += (double)f * invSr;
        if (vc.ph >= 1.0) vc.ph -= 1.0;
        float s = (float)std::sin(kTwoPi * vc.ph);
        float click = (vc.env > 0.6f ? (noiseF() * 2.0f - 1.0f) : 0.0f) * punch * 0.6f;
        s = fast_math::tanhPrecise((s + click) * (1.0f + drive * 4.0f));
        vc.pitchEnv *= decayCoef(0.03f + 0.05f * tune, sampleRate);
        vc.env *= decayCoef(expMap(decay, 0.06f, 1.2f), sampleRate);
        out = s * vc.env * vc.vel;
        break;
    }
    case 1: { // Snare — two tone sines + noise snap
        float f1 = expMap(tune, 140.0f, 330.0f);
        vc.ph += (double)f1 * invSr;
        if (vc.ph >= 1.0) vc.ph -= 1.0;
        vc.ph2 += (double)(f1 * 1.6f) * invSr;
        if (vc.ph2 >= 1.0) vc.ph2 -= 1.0;
        float body = 0.5f * ((float)std::sin(kTwoPi * vc.ph) + (float)std::sin(kTwoPi * vc.ph2));
        float n = noiseF() * 2.0f - 1.0f;
        vc.hp = 0.85f * (vc.hp + n - vc.hpPrev);
        vc.hpPrev = n;                                   // high-passed noise
        float mix = (1.0f - tone) * body + tone * vc.hp; // tone = body↔noise
        mix = fast_math::tanhPrecise(mix * (1.0f + drive * 3.0f));
        vc.env *= decayCoef(expMap(decay, 0.05f, 0.5f) * (0.4f + 0.6f * (1.0f - punch)), sampleRate);
        vc.env2 *= decayCoef(0.02f, sampleRate);
        out = (mix * vc.env + n * vc.env2 * punch * 0.5f) * vc.vel;
        break;
    }
    case 2: { // Clap — three noise bursts + a short tail
        vc.burstT -= (float)invSr;
        if (vc.burst < 3 && vc.burstT <= 0.0f) {
            vc.env = 1.0f;
            vc.burst++;
            vc.burstT = 0.012f;
        }
        float n = noiseF() * 2.0f - 1.0f;
        vc.bp += (float)(expMap(tune, 700.0f, 1800.0f) * invSr) * (n - vc.bp); // colour
        float s = fast_math::tanhPrecise(vc.bp * (1.0f + drive * 3.0f));
        vc.env *= decayCoef((vc.burst < 3) ? 0.01f : expMap(decay, 0.05f, 0.4f), sampleRate);
        out = s * vc.env * vc.vel * 2.0f;
        break;
    }
    case 3: { // Rim — band-passed noise click
        float n = noiseF() * 2.0f - 1.0f + (vc.env > 0.7f ? 1.0f : 0.0f);
        float g = std::tan((float)(3.14159265f * expMap(tune, 900.0f, 2600.0f) * invSr));
        float a1 = 1.0f / (1.0f + g * (g + 0.6f));
        float hp = (n - (0.6f + g) * vc.bp - vc.bp2) * a1;
        float bpv = g * hp + vc.bp;
        vc.bp = g * hp + bpv;
        float lp = g * bpv + vc.bp2;
        vc.bp2 = g * bpv + lp;
        vc.env *= decayCoef(expMap(decay, 0.02f, 0.12f), sampleRate);
        out = fast_math::tanhPrecise(bpv * (1.5f + drive * 3.0f)) * vc.env * vc.vel;
        break;
    }
    case 4:
    case 5: { // Closed / Open Hat — high-passed metallic noise
        // a cluster of square oscillators → metallic; then high-pass.
        float metal = 0.0f;
        static const float rat[6] = {1.0f, 1.34f, 1.61f, 1.93f, 2.44f, 2.77f};
        float baseF = expMap(tune, 320.0f, 900.0f);
        vc.ph += (double)(baseF)*invSr;
        if (vc.ph >= 1.0) vc.ph -= 1.0;
        for (int k = 0; k < 6; ++k) metal += ((std::fmod(vc.ph * rat[k], 1.0) < 0.5) ? 1.0f : -1.0f);
        metal *= 1.0f / 6.0f;
        float n = noiseF() * 2.0f - 1.0f;
        float sig = (1.0f - tone) * metal + tone * n;
        vc.hp = 0.93f * (vc.hp + sig - vc.hpPrev);
        vc.hpPrev = sig; // bright HP
        float dcy = (v == 4) ? expMap(decay, 0.02f, 0.16f) : expMap(decay, 0.08f, 0.7f);
        vc.env *= decayCoef(dcy, sampleRate);
        out = fast_math::tanhPrecise(vc.hp * (1.0f + drive * 2.0f)) * vc.env * vc.vel * 0.7f;
        break;
    }
    default: { // 7 Perc — ring/FM metallic
        float fc = expMap(tune, 200.0f, 1200.0f);
        vc.ph += (double)fc * invSr;
        if (vc.ph >= 1.0) vc.ph -= 1.0;
        vc.ph2 += (double)(fc * (1.0f + tone * 2.5f)) * invSr;
        if (vc.ph2 >= 1.0) vc.ph2 -= 1.0;
        float s = (float)std::sin(kTwoPi * vc.ph) * (float)std::sin(kTwoPi * vc.ph2); // ring mod
        s = fast_math::tanhPrecise(
            s * (1.0f + drive * 3.0f) + punch * (noiseF() * 2.0f - 1.0f) * (vc.env > 0.7f ? 0.5f : 0.0f));
        vc.env *= decayCoef(expMap(decay, 0.05f, 0.6f), sampleRate);
        out = s * vc.env * vc.vel;
        break;
    }
    }
    if (vc.env < 1e-4f && vc.env2 < 1e-4f) vc.active = false;
    return out * level;
}

} // namespace nota
