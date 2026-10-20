// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Grain implementation. Downstream Hann/Tukey grain amplitude windows run on fast_math::cos (scheduling,
// read positions, dry-head fades and interpolation stay std::-exact). Silent clouds
// auto-sleep via isIdle() after a hangover.

#include "GrainSynth.h"

#include "dsp/math/FastMath.h"

namespace nota {

GrainSynth::GrainSynth() {
    set(Position, 0.3f);
    set(Scan, 0.5f);
    set(Spray, 0.15f);
    set(GrainSize, 0.42f);
    set(Density, 0.6f);
    set(Coarse, 0.5f);
    set(Fine, 0.5f);
    set(Spread, 0.4f);
    set(PosRand, 0.12f);
    set(PitchRand, 0.0f);
    set(PanRand, 0.3f);
    set(ScanMode, 0.5f); // Freeze
    set(FilterType, 0.0f);
    set(FilterFreq, 0.9f);
    set(FilterReso, 0.1f);
    set(Attack, 0.06f);
    set(Decay, 0.3f);
    set(Sustain, 0.85f);
    set(Release, 0.4f);
    set(Volume, 0.8f);
    set(Pan, 0.5f);
    set(GrainShape, 0.0f);
    set(DryWet, 1.0f);
    for (auto& p : posPub_) p.store(-1.0f, std::memory_order_relaxed);
}

void GrainSynth::setSampleRate(double sr) {
    sampleRate_ = sr > 0 ? sr : 44100.0;
    if (!sample_ || sample_->empty()) sample_ = makeDefaultSample(sampleRate_);
    wake();
}

int32_t GrainSynth::playPositions(float* out, int32_t maxN) const {
    int c = 0;
    for (int i = 0; i < kVoices && c < maxN; ++i) {
        float p = posPub_[i].load(std::memory_order_relaxed);
        if (p >= 0.0f) out[c++] = p;
    }
    return c;
}

int32_t GrainSynth::scopeRead(float* out, int32_t maxN) const {
    if (!out || maxN < 2) return 0;
    const int n = std::min<int>((int)scope_[1].load(std::memory_order_relaxed), (maxN - 2) / 3);
    out[0] = scope_[0].load(std::memory_order_relaxed);
    out[1] = (float)n;
    for (int i = 0; i < n * 3; ++i) out[2 + i] = scope_[2 + i].load(std::memory_order_relaxed);
    return 2 + n * 3;
}

std::string GrainSynth::pluginParamId(int32_t i) const {
    static const char* ids[] = {"position", "scan", "spray", "grainsize", "density", "coarse", "fine", "spread",
        "posrand", "pitchrand", "panrand", "scanmode", "filtype", "filfreq", "filreso", "attack", "decay", "sustain",
        "release", "volume", "pan", "grainshape", "drywet"};
    return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
}

std::string GrainSynth::pluginParamName(int32_t i) const {
    static const char* nm[] = {"Position", "Scan", "Spray", "Grain Size", "Density", "Coarse", "Fine", "Spread",
        "Pos Rand", "Pitch Rand", "Pan Rand", "Scan Mode", "Filter Type", "Filter Freq", "Filter Reso", "Attack",
        "Decay", "Sustain", "Release", "Volume", "Pan", "Grain Shape", "Dry/Wet"};
    return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
}

std::vector<uint8_t> GrainSynth::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void GrainSynth::setState(const uint8_t* data, int32_t size) {
    if (!data) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + i * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    wake();
}

std::shared_ptr<Instrument> GrainSynth::clone() const {
    auto s = std::make_shared<GrainSynth>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->sample_ = sample_;
    s->setRoot(rootNote());
    s->setSampleRate(sampleRate_);
    return s;
}

void GrainSynth::noteOn(int32_t pitch, float velocity) {
    wake();
    Voice* v = findFreeVoice();
    v->active = true;
    v->pitch = pitch;
    v->vel = std::clamp(velocity, 0.0f, 1.0f);
    v->seq = ++seq_;
    v->env = 0.0f;
    v->stage = Stage::Attack;
    v->grainClock = 0.0;
    v->scanOff = 0.0;
    v->s1L = v->s2L = v->s1R = v->s2R = 0.0;
    for (auto& g : v->grains) g.active = false;
    const int64_t frames = sample_ ? sample_->frames : 0;
    v->readPos = basePos01(pitch, scanModeNow()) * frames;
    v->dryPos = v->readPos;
}

void GrainSynth::noteOff(int32_t pitch) {
    wake();
    Voice* oldest = nullptr;
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage != Stage::Release && (!oldest || v.seq < oldest->seq)) oldest = &v;
    if (oldest) oldest->stage = Stage::Release;
}

void GrainSynth::allNotesOff() {
    wake();
    for (auto& v : voices_) v.active = false;
}

void GrainSynth::render(float* out, int32_t frames) {
    auto sb = sample_;
    if (!sb || sb->empty()) return;
    if (idle_) return;
    const int64_t slen = sb->frames;
    const double srcRatio = (sb->sourceSampleRate > 0 ? sb->sourceSampleRate : sampleRate_) / sampleRate_;

    const int mode = scanModeNow();
    const double scanRate = (mode == 0) ? ((double)get(Scan) - 0.5) * 2.0 * 4.0 * srcRatio : 0.0;
    const double sprayFr = (double)get(Spray) * 0.25 * slen; // up to 25% of the file
    const double grainLen = std::max(64.0, expMap(get(GrainSize), 0.004, 0.4) * sampleRate_);
    const double overlap = 1.0 + (double)get(Density) * 7.0;
    const double interval = std::max(16.0, grainLen / overlap);
    const double coarse = ((double)get(Coarse) - 0.5) * 48.0;
    const double fine = ((double)get(Fine) - 0.5) * 2.0;
    const double spread = get(Spread);
    const double posRandFr = (double)get(PosRand) * 0.25 * slen;
    const double pitchRand = (double)get(PitchRand) * 12.0;
    const int root = rootNote();
    const double panRand = get(PanRand);
    const int ftype = std::clamp((int)std::lround(get(FilterType) * 2.0f), 0, 2);
    const double fbase = expMap(get(FilterFreq), 60.0, 18000.0);
    const double fk = std::clamp(2.0 - 1.9 * get(FilterReso), 0.05, 2.0);
    const int gshape = std::clamp((int)std::lround(get(GrainShape) * 3.0f), 0, 3);
    const float aA = rateOf(Attack, 0.001, 3.0), aD = rateOf(Decay, 0.002, 4.0), aR = rateOf(Release, 0.003, 6.0);
    const float aS = get(Sustain);
    const double volume = get(Volume);
    const double panv = (get(Pan) - 0.5) * 2.0;
    const double gpL = std::cos((panv + 1.0) * 0.25 * kPi), gpR = std::sin((panv + 1.0) * 0.25 * kPi);
    const double fg = std::tan(kPi * std::min(fbase, sampleRate_ * 0.49) / sampleRate_);
    const double fa1 = 1.0 / (1.0 + fg * (fg + fk));
    const double wet = get(DryWet);
    const bool doWet = wet > 1e-4, doDry = wet < 1.0 - 1e-4;
    const float wetGain = (float)wet, dryGain = (float)((1.0 - wet) * kDryTrim);
    const double dryEnd = (double)std::max<int64_t>(1, slen - 1);
    const double dryFade = std::max(1.0, 0.004 * (sb->sourceSampleRate > 0 ? sb->sourceSampleRate : sampleRate_));

    // Per-voice, per-block: where the read position sits (live), how fast the dry head runs.
    for (auto& v : voices_) {
        if (!v.active) continue;
        v.base = basePos01(v.pitch, mode) * slen;
        if (mode != 0) v.scanOff = 0.0;
        const double semis = (mode == 2 ? 0.0 : (v.pitch - root)) + coarse + fine;
        v.dryRate = std::pow(2.0, semis / 12.0) * srcRatio;
    }

    float ownPeak = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        float mixL = 0.0f, mixR = 0.0f;
        for (auto& v : voices_) {
            if (!v.active) continue;
            advanceEnv(v.env, v.stage, aA, aD, aS, aR);
            if (v.stage == Stage::Off) {
                v.active = false;
                continue;
            }

            // The read position: the live base, plus the moving offset in Scan mode
            // (wrapping within the file).
            if (mode == 0) {
                v.scanOff += scanRate;
                if (v.scanOff >= slen) v.scanOff -= slen;
                else if (v.scanOff < 0)
                    v.scanOff += slen;
            }
            v.readPos = v.base + v.scanOff;
            if (v.readPos >= slen) v.readPos -= slen;

            // The dry head: the sample itself from the read position at the note's
            // pitch, wrapping at the end with both ends faded so the wrap doesn't click.
            float dl = 0.0f, dr = 0.0f;
            if (doDry) {
                const int64_t d0 = (int64_t)v.dryPos;
                const double df = v.dryPos - d0;
                float l0, r0, l1, r1;
                sb->readStereo(d0, l0, r0);
                sb->readStereo(std::min<int64_t>(d0 + 1, slen - 1), l1, r1);
                const float fade = (float)std::min({1.0, v.dryPos / dryFade, (dryEnd - v.dryPos) / dryFade});
                dl = (l0 + (l1 - l0) * (float)df) * fade;
                dr = (r0 + (r1 - r0) * (float)df) * fade;
                v.dryPos += v.dryRate;
                if (v.dryPos >= dryEnd) v.dryPos = std::fmod(v.dryPos, dryEnd);
            }

            // Spawn grains at the density interval.
            v.grainClock += 1.0;
            if (doWet && v.grainClock >= interval) {
                v.grainClock -= interval;
                Grain* g = freeGrain(v);
                if (g) {
                    double pos = v.readPos + (sprayFr + posRandFr) * (rnd() * 2.0 - 1.0);
                    pos = std::clamp(pos, 0.0, (double)std::max<int64_t>(0, slen - 2));
                    const double semis =
                        (mode == 2 ? 0.0 : (v.pitch - root)) + coarse + fine + pitchRand * (rnd() * 2.0 - 1.0);
                    g->pos = pos;
                    g->age = 0.0;
                    g->len = grainLen;
                    g->rate = std::pow(2.0, semis / 12.0) * srcRatio;
                    double pan = 0.5 + (spread * 0.5 + panRand * 0.5) * (rnd() * 2.0 - 1.0);
                    pan = std::clamp(pan, 0.0, 1.0);
                    g->gL = (float)std::cos(pan * 0.5 * kPi);
                    g->gR = (float)std::sin(pan * 0.5 * kPi);
                    g->pan = (float)pan;
                    g->active = true;
                }
            }

            // Render active grains.
            float gl = 0.0f, gr = 0.0f;
            for (auto& g : v.grains) {
                if (!g.active) continue;
                const double idx = g.pos + g.age * g.rate;
                if (idx < 0.0 || idx >= slen - 1) {
                    g.active = false;
                    continue;
                }
                const int64_t i0 = (int64_t)idx;
                const double fr = idx - i0;
                float l0, r0, l1, r1;
                sb->readStereo(i0, l0, r0);
                sb->readStereo(i0 + 1, l1, r1);
                const float w = grainWindow(gshape, g.age / g.len);
                gl += (l0 + (l1 - l0) * (float)fr) * w * g.gL;
                gr += (r0 + (r1 - r0) * (float)fr) * w * g.gR;
                g.age += 1.0;
                if (g.age >= g.len) g.active = false;
            }
            if (!doWet) v.grainClock = std::min(v.grainClock, interval);
            const float norm = (float)(1.0 / std::sqrt(overlap)) * wetGain;
            double yL = gl * norm + dl * dryGain, yR = gr * norm + dr * dryGain;
            // Subtractive TPT filter per channel.
            yL = svf(v.s1L, v.s2L, yL, fg, fa1, fk, ftype);
            yR = svf(v.s1R, v.s2R, yR, fg, fa1, fk, ftype);
            const float e = v.env * v.vel;
            mixL += (float)yL * e;
            mixR += (float)yR * e;
        }
        out[i * 2] += (float)((mixL * gpL) * volume * 2.4);
        out[i * 2 + 1] += (float)((mixR * gpR) * volume * 2.4);
        ownPeak = std::max(ownPeak, std::max(std::fabs(mixL), std::fabs(mixR)));
    }
    // Publish each voice's read position (0..1) for the UI playheads, and the cloud.
    const double inv = slen > 0 ? 1.0 / slen : 0.0;
    int nv = 0, ng = 0;
    for (int vi = 0; vi < kVoices; ++vi) {
        const Voice& v = voices_[vi];
        posPub_[vi].store(v.active ? (float)(v.readPos * inv) : -1.0f, std::memory_order_relaxed);
        if (!v.active) continue;
        ++nv;
        for (const auto& g : v.grains) {
            if (!g.active || ng >= kVoices * kGrains) continue;
            scope_[2 + ng * 3].store((float)((g.pos + g.age * g.rate) * inv), std::memory_order_relaxed);
            scope_[3 + ng * 3].store(g.pan, std::memory_order_relaxed);
            scope_[4 + ng * 3].store(grainWindow(gshape, g.age / g.len) * v.env, std::memory_order_relaxed);
            ++ng;
        }
    }
    scope_[0].store((float)nv, std::memory_order_relaxed);
    scope_[1].store((float)ng, std::memory_order_relaxed);
    activeVoices_.store(nv, std::memory_order_relaxed);

    // Auto-sleep: envelope-off voices deactivate above, so no active voice plus a
    // silent block means every grain and tail has fully decayed.
    if (nv == 0 && ownPeak <= 1.0e-5f) {
        quietSamples_ += static_cast<uint64_t>(frames);
        if (quietSamples_ > kIdleSamples) idle_ = true;
    } else {
        quietSamples_ = 0;
        idle_ = false;
    }
}

double GrainSynth::svf(double& s1, double& s2, double in, double g, double a1, double k, int type) {
    const double v3 = in - s2;
    const double v1 = a1 * s1 + a1 * g * v3;
    const double v2 = s2 + g * v1;
    s1 = 2.0 * v1 - s1;
    s2 = 2.0 * v2 - s2;
    return type == 1 ? (in - k * v1 - v2) : type == 2 ? v1 : v2; // HP / BP / LP
}

void GrainSynth::advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
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

float GrainSynth::grainWindow(int shape, double x) {
    switch (shape) {
    case 1: {
        const double d = (x - 0.5) * 4.0;
        return (float)std::exp(-d * d);
    }
    case 2: {
        const double e = 0.25;
        if (x < e) return (float)(0.5 - 0.5 * fast_math::cos((float)(x / (2.0 * e))));
        if (x > 1 - e) return (float)(0.5 - 0.5 * fast_math::cos((float)((1 - x) / (2.0 * e))));
        return 1.0f;
    }
    case 3: return (float)(1.0 - std::fabs(2.0 * x - 1.0));
    default: return (float)(0.5 - 0.5 * fast_math::cos((float)x));
    }
}

GrainSynth::Voice* GrainSynth::findFreeVoice() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0];
    for (auto& v : voices_)
        if (v.env < q->env) q = &v;
    return q;
}

std::shared_ptr<SampleBuffer> GrainSynth::makeDefaultSample(double sr) {
    auto s = std::make_shared<SampleBuffer>();
    const int64_t n = (int64_t)(1.6 * sr);
    s->channels = 1;
    s->frames = n;
    s->sourceSampleRate = sr;
    s->samples.resize(n);
    const double f0 = 110.0;
    for (int64_t i = 0; i < n; ++i) {
        const double t = (double)i / sr;
        double v = 0.0;
        for (int k = 1; k <= 6; ++k) {
            const double amp = 1.0 / k;
            const double det = 1.0 + 0.002 * std::sin(2.0 * kPi * 0.3 * t * k); // slow drift
            v += amp * std::sin(2.0 * kPi * f0 * k * det * t);
        }
        const double env = 0.5 - 0.5 * std::cos(2.0 * kPi * std::min(1.0, i / (0.05 * sr))); // fade-in
        s->samples[i] = (float)(v * 0.16 * (i < 0.05 * sr ? env : 1.0));
    }
    return s;
}

} // namespace nota
