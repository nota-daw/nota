// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pentad implementation. pentad::exp2f,
// tanhRatio and tanPrewarp unify onto dsp::FastMath (bit-identical); adds
// voice-based auto-sleep with a quiet hangover (isIdle/hasTail/wake).

#include "Pentad.h"

namespace nota {

Pentad::Pentad() {
    // A plain two-saw brass-ish init: Osc A + B saws (B a few cents sharp), filter half
    // open with a moderate envelope and half keyboard tracking, organ-like amp envelope.
    set(Tune, 0.5f);
    set(Bend, 0.5f);
    set(ModWheel, 0.0f);
    set(Aftertouch, 0.0f);
    set(BendRange, 1.0f / 11.0f);
    set(Glide, 0.0f);
    set(GlideMode, 0.0f);
    set(VoiceMode, 0.0f);
    set(Polyphony, 0.0f);
    set(Alloc, 0.0f);
    set(Unison, 0.0f);
    set(UniDetune, 0.3f);
    set(ReleaseOn, 1.0f);
    set(Drift, 0.25f);
    set(Seed, 0.5f);
    set(Volume, 0.7f);
    set(Spread, 0.3f);
    set(VelAmp, 0.0f);
    set(VelFilt, 0.0f);
    set(AtCutoff, 0.0f);
    set(AtLfo, 0.0f);
    set(NoiseFloor, 1.0f);
    set(LowComp, 0.0f);
    set(Oversample, 0.0f);
    set(OaOct, 2.0f / 3.0f);
    set(OaSemi, 0.5f);
    set(OaFine, 0.5f);
    set(OaSaw, 1.0f);
    set(OaPulse, 0.0f);
    set(OaPw, 0.5f);
    set(OaSync, 0.0f);
    set(ObOct, 2.0f / 3.0f);
    set(ObSemi, 0.5f);
    set(ObFine, 0.54f);
    set(ObSaw, 1.0f);
    set(ObTri, 0.0f);
    set(ObPulse, 0.0f);
    set(ObPw, 0.5f);
    set(ObLoFreq, 0.0f);
    set(ObKbd, 1.0f);
    set(MixA, 0.8f);
    set(MixAOn, 1.0f);
    set(MixB, 0.6f);
    set(MixBOn, 1.0f);
    set(MixNoise, 0.0f);
    set(MixNoiseOn, 1.0f);
    set(NoiseColor, 0.0f);
    set(Cutoff, 0.55f);
    set(Reso, 0.1f);
    set(FEnvAmt, 0.35f);
    set(KeyTrk, 0.5f);
    set(FAttack, 0.0f);
    set(FDecay, 0.45f);
    set(FSustain, 0.3f);
    set(FRelease, 0.4f);
    set(AAttack, 0.0f);
    set(ADecay, 0.45f);
    set(ASustain, 0.85f);
    set(ARelease, 0.4f);
    set(LfoRate, 0.55f);
    set(LfoSaw, 0.0f);
    set(LfoTri, 1.0f);
    set(LfoSquare, 0.0f);
    set(LfoAmt, 0.0f);
    set(LfoSync, 0.0f);
    set(WmMix, 0.0f);
    set(WmFreqA, 1.0f);
    set(WmFreqB, 1.0f);
    set(WmPwA, 0.0f);
    set(WmPwB, 0.0f);
    set(WmFilter, 0.0f);
    set(PmEnv, 0.0f);
    set(PmOscB, 0.0f);
    set(PmFreqA, 0.0f);
    set(PmPwA, 0.0f);
    set(PmFilter, 0.0f);
    initDecimators();
    resetAll();
}

void Pentad::controller(int32_t ctl, float value) {
    if (ctl == kCtlModWheel) pn_[ModWheel].store(std::clamp(value, 0.0f, 1.0f), std::memory_order_relaxed);
    Instrument::controller(ctl, value);
    wake();
}

int32_t Pentad::scopeRead(float* out, int32_t maxN) const {
    const int n = std::min<int>(maxN, kScopeN);
    for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
    return n;
}

int32_t Pentad::heldNotes(int32_t* out, int32_t maxN) const {
    const int n = std::min<int>(heldN_, maxN);
    for (int i = 0; i < n; ++i) out[i] = held_[i];
    return n;
}

const char* Pentad::paramId(int i) {
    static const char* ids[kNumParams] = {"tune", "bend", "modwheel", "aftertouch", "bendrange", "glide", "glidemode",
        "voicemode", "polyphony", "alloc", "unison", "unidetune", "releaseon", "drift", "seed", "volume", "spread",
        "velamp", "velfilt", "atcutoff", "atlfo", "noisefloor", "lowcomp", "oversample", "oaoct", "oasemi", "oafine",
        "oasaw", "oapulse", "oapw", "oasync", "oboct", "obsemi", "obfine", "obsaw", "obtri", "obpulse", "obpw",
        "oblofreq", "obkbd", "mixa", "mixaon", "mixb", "mixbon", "mixnoise", "mixnoiseon", "noisecolor", "cutoff",
        "reso", "fenvamt", "keytrk", "fattack", "fdecay", "fsustain", "frelease", "aattack", "adecay", "asustain",
        "arelease", "lforate", "lfosaw", "lfotri", "lfosquare", "lfoamt", "lfosync", "wmmix", "wmfreqa", "wmfreqb",
        "wmpwa", "wmpwb", "wmfilter", "pmenv", "pmoscb", "pmfreqa", "pmpwa", "pmfilter"};
    return (i >= 0 && i < kNumParams) ? ids[i] : "";
}

const char* Pentad::paramName(int i) {
    static const char* nm[kNumParams] = {"Master Tune", "Pitch Bend", "Mod Wheel", "Aftertouch Amount", "Bend Range",
        "Glide Time", "Glide Mode", "Voice Mode", "Voice Count", "Voice Allocation", "Unison Stack", "Unison Detune",
        "Release Switch", "Vintage Drift", "Vintage Seed", "Output Volume", "Output Spread", "Velocity Amp",
        "Velocity Filter", "Aftertouch Cutoff", "Aftertouch LFO", "Analog Noise-Floor", "Filter Bass-Comp",
        "Quality Oversampling", "Osc A Octave", "Osc A Semitone", "Osc A Fine", "Osc A Saw", "Osc A Pulse",
        "Osc A Pulse-Width", "Osc A Sync", "Osc B Octave", "Osc B Semitone", "Osc B Fine", "Osc B Saw",
        "Osc B Triangle", "Osc B Pulse", "Osc B Pulse-Width", "Osc B Lo-Freq", "Osc B Keyboard", "Mixer Osc-A Level",
        "Mixer Osc-A On", "Mixer Osc-B Level", "Mixer Osc-B On", "Mixer Noise Level", "Mixer Noise On",
        "Mixer Noise Color", "Filter Cutoff", "Filter Resonance", "Filter Env-Amount", "Filter Key-Track",
        "Filter-Env Attack", "Filter-Env Decay", "Filter-Env Sustain", "Filter-Env Release", "Amp-Env Attack",
        "Amp-Env Decay", "Amp-Env Sustain", "Amp-Env Release", "LFO Rate", "LFO Saw", "LFO Triangle", "LFO Square",
        "LFO Initial-Amount", "LFO Tempo-Sync", "Wheel-Mod Source-Mix", "Wheel-Mod Freq-A", "Wheel-Mod Freq-B",
        "Wheel-Mod PW-A", "Wheel-Mod PW-B", "Wheel-Mod Filter", "Poly-Mod Filter-Env", "Poly-Mod Osc-B",
        "Poly-Mod Freq-A", "Poly-Mod PW-A", "Poly-Mod Filter"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

int32_t Pentad::pluginParamIndexOfId(const std::string& id) const {
    for (int32_t i = 0; i < kNumParams; ++i)
        if (id == paramId(i)) return i;
    return -1;
}

std::vector<uint8_t> Pentad::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        const float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void Pentad::setState(const uint8_t* data, int32_t size) {
    if (!data) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + i * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
}

std::shared_ptr<Instrument> Pentad::clone() const {
    auto s = std::make_shared<Pentad>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(pendingRate_.load(std::memory_order_relaxed));
    return s;
}

float Pentad::syncBeatsOf(float v) { // cycle length in beats (quarter notes)
    static const float b[kSyncDivs] = {
        32.f, 16.f, 8.f, 4.f, 2.f, 1.5f, 1.f, 0.75f, 2.f / 3.f, 0.5f, 1.f / 3.f, 0.25f, 1.f / 6.f, 0.125f};
    return b[std::clamp(static_cast<int>(std::lround(v * (kSyncDivs - 1))), 0, kSyncDivs - 1)];
}

void Pentad::setTransport(double beatStart, double samplesPerBeat, bool playing) {
    const double pos = beatStart * samplesPerBeat;
    const bool jumped = playing && tPlaying_ && std::fabs(pos - (tPos_ + tFrames_)) > 2.0;
    if (jumped) resetAll();
    else if (playing && !tPlaying_) {
        bool any = false;
        for (int v = 0; v < kMaxVoices; ++v) any |= voices_[v].active;
        if (!any) resetAll();
    }
    tBeat_ = beatStart;
    tSpb_ = samplesPerBeat;
    tFresh_ = true;
    tPos_ = pos;
    tFrames_ = 0.0;
    tPlaying_ = playing;
}

void Pentad::noteOn(int32_t pitch, float velocity) {
    wake();
    syncStructure();
    const int mode = voiceMode();
    const bool othersHeld = heldN_ > 0;
    pushHeld(pitch);
    const float vel = std::clamp(velocity, 0.0f, 1.0f);
    const int N = polyphonyOf(get(Polyphony));
    const float glideT = glideSecOf(get(Glide));
    if (mode == 0) {
        const int gm = std::clamp(static_cast<int>(std::lround(get(GlideMode) * 2.0f)), 0, 2);
        const bool glide = glideT > 0.0f && (gm == 1 || (gm == 2 && othersHeld));
        const int S = std::min(unisonOf(get(Unison)), N);
        for (int s = 0; s < S; ++s) {
            int v = -1;
            for (int k = 0; k < N; ++k) { // the same key again → retrigger its voice
                const Voice& V = voices_[k];
                if (V.active && V.note == pitch && V.stackIdx == s && V.stackN == S && V.pendNote < 0) {
                    v = k;
                    break;
                }
            }
            bool fade = false;
            if (v < 0) v = allocate(N, fade);
            if (fade) {
                Voice& V = voices_[v];
                V.pendNote = pitch;
                V.pendVel = vel;
                V.pendStack = s;
                V.pendStackN = S;
                V.pendGlide = glide;
                for (int d = 0; d < kExprDims; ++d) V.pendExpr[d] = kExprNeutral[d];
                V.fade = fadeLen_;
            } else {
                startVoice(v, pitch, vel, s, S, glide);
            }
        }
    } else {
        const int S = (mode == 1) ? N : 1;
        const int low = lowestHeld();
        if (!othersHeld) {
            for (int s = 0; s < S; ++s) startVoice(s, low, vel, s, S, glideT > 0.0f);
        } else {
            for (int s = 0; s < S; ++s) {
                Voice& V = voices_[s];
                V.note = low;
                V.pitchTgt = static_cast<float>(low);
                if (glideT <= 0.0f) V.pitch = V.pitchTgt;
            }
        }
    }
}

void Pentad::noteOff(int32_t pitch) {
    wake();
    syncStructure();
    removeHeld(pitch);
    if (voiceMode() == 0) {
        for (int v = 0; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            if (V.gate && V.note == pitch) release(V);
            if (V.pendNote == pitch) V.pendNote = -1; // stolen-voice note ended before it started
        }
        return;
    }
    const int S = voiceMode() == 1 ? polyphonyOf(get(Polyphony)) : 1;
    if (heldN_ == 0) {
        for (int s = 0; s < S; ++s)
            if (voices_[s].gate) release(voices_[s]);
        return;
    }
    const int low = lowestHeld();
    const bool glide = glideSecOf(get(Glide)) > 0.0f;
    for (int s = 0; s < S; ++s) {
        Voice& V = voices_[s];
        if (!V.active) continue;
        V.note = low;
        V.pitchTgt = static_cast<float>(low);
        if (!glide) V.pitch = V.pitchTgt;
    }
}

void Pentad::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        gexpr_.set(dim, value);
        return;
    }
    if (dim < 0 || dim >= kExprDims) return;
    for (auto& V : voices_) {
        if (V.gate && V.note == pitch && V.pendNote < 0) V.ex.set(dim, value, kNoExprAdd);
        if (V.pendNote == pitch) V.pendExpr[dim] = value; // a stolen voice still fading out
    }
}

void Pentad::render(float* out, int32_t frames) {
    dsp::ScopedNoDenormals ftz;
    const auto t0 = std::chrono::steady_clock::now();
    const double pr = pendingRate_.load(std::memory_order_relaxed);
    if (pr != sampleRate_) { // voices keep playing; only rate-derived state restarts
        sampleRate_ = pr;
        fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
        for (int c = 0; c < 2; ++c) {
            dnSteep_[c].clear_buffers();
            dnMid_[c].clear_buffers();
        }
        smoothInit_ = false;
    }
    for (int32_t done = 0; done < frames;) {
        const int n = std::min<int>(kChunk, frames - done);
        renderChunk(out + done * 2, n);
        done += n;
    }
    // Sleep: no sounding voice plus a silent block means the tail (voices and
    // the HIIR decimator drain) has settled; the hangover covers the drain.
    if (activeCount_ == 0 && chunkOutPeak_ <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > kIdleHangover) idle_ = true;
    chunkOutPeak_ = 0.0f;
    tFrames_ += frames;
    const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    const double avail = frames / sampleRate_;
    if (avail > 0.0) cpu_ += 0.05 * (el / avail - cpu_);
    publishTelemetry(frames);
}

void Pentad::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

int Pentad::allocate(int N, bool& fade) {
    fade = false;
    if (get(Alloc) < 0.5f) { // round-robin: next voice in rotation that isn't held
        for (int k = 0; k < N; ++k) {
            const int v = (rr_ + k) % N;
            if (!voices_[v].gate && voices_[v].pendNote < 0) {
                rr_ = (v + 1) % N;
                return v;
            }
        }
        const int v = rr_ % N;
        rr_ = (v + 1) % N; // all held: reassign in rotation
        return v;
    }
    int best = -1;
    uint64_t bo = ~0ull; // oldest steal: idle (least recently used) …
    for (int v = 0; v < N; ++v)
        if (!voices_[v].active && voices_[v].pendNote < 0 && voices_[v].order < bo) {
            bo = voices_[v].order;
            best = v;
        }
    if (best >= 0) return best;
    for (int v = 0; v < N; ++v)
        if (!voices_[v].gate && voices_[v].pendNote < 0 && voices_[v].order < bo) {
            bo = voices_[v].order;
            best = v;
        }
    if (best >= 0) return best; // … then the oldest releasing voice, then fade-steal
    for (int v = 0; v < N; ++v)
        if (voices_[v].pendNote < 0 && voices_[v].order < bo) {
            bo = voices_[v].order;
            best = v;
        }
    if (best < 0) best = 0;
    fade = true;
    return best;
}

void Pentad::startVoice(int v, int note, float vel, int s, int S, bool glide) {
    Voice& V = voices_[v];
    V.note = note;
    V.vel = vel;
    V.gate = true;
    V.stackIdx = s;
    V.stackN = S;
    V.order = ++orderCtr_;
    V.ex.reset(kNoExprAdd);
    V.pitchTgt = static_cast<float>(note);
    if (!V.hasPitch || !glide) V.pitch = V.pitchTgt;
    V.hasPitch = true;
    V.fst = 1;
    V.ast = 1; // retrigger from the current level (no restart at zero)
    if (!V.active) wake(V);
}

void Pentad::wake(Voice& V) {
    V.active = true;
    V.fresh = true;
    V.A.ph += V.rng.uni();
    V.A.ph -= std::floor(V.A.ph);
    V.B.ph += V.rng.uni();
    V.B.ph -= std::floor(V.B.ph);
    V.A.hi = V.A.ph < get(OaPw);
    V.B.hi = V.B.ph < get(ObPw);
    V.A.ring.clear();
    V.B.ring.clear();
    V.sat.reset();
    for (int j = 0; j < 4; ++j) {
        V.s[j] = 0.0f;
        V.df[j] = 0.0f;
    }
    V.y4 = 0.0f;
}

void Pentad::computeStatics(int v) {
    pentad::Rng r;
    r.seed(pentad::hash32(seedCache_ * 2654435761U + pentad::hash32(static_cast<uint32_t>(v) + 17U)));
    Voice& V = voices_[v];
    V.oA = r.bi();
    V.oB = r.bi();
    V.oCut = r.bi();
    V.oEnvF = r.bi();
    V.oEnvA = r.bi();
    V.oPw = r.bi();
}

void Pentad::resetAll() {
    seedCache_ = seedValue();
    for (int v = 0; v < kMaxVoices; ++v) {
        Voice& V = voices_[v];
        V = Voice{};
        V.rng.seed(pentad::hash32(seedCache_ ^ pentad::hash32(static_cast<uint32_t>(v) * 0x9E3779B9U + 1U)));
        V.A.ph = V.rng.uni();
        V.B.ph = V.rng.uni();
        V.wCount = 1 + static_cast<int>(V.rng.next() % 4096u);
        computeStatics(v);
    }
    heldN_ = 0;
    rr_ = 0;
    orderCtr_ = 0;
    gexpr_.reset();
    lfoPh_ = 0.0;
    tFresh_ = true;
    noiseRng_.seed(pentad::hash32(seedCache_ ^ 0xA5A5A5A5U));
    wmRng_.seed(pentad::hash32(seedCache_ ^ 0x5A5A5A5AU));
    pk0_ = pk1_ = pk2_ = 0.0f;
    for (int c = 0; c < 2; ++c) {
        dnSteep_[c].clear_buffers();
        dnMid_[c].clear_buffers();
    }
    smoothInit_ = false;
    lastMode_ = voiceMode();
    lastPoly_ = polyphonyOf(get(Polyphony));
    fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
    peakL_ = peakR_ = mixPeak_ = 0.0f;
}

void Pentad::initDecimators() {
    static const double steep[12] = {0.017347915108876406, 0.067150480426919179, 0.14330738338179819,
        0.23745131944299824, 0.34085550201503761, 0.44601111310335906, 0.54753112652956148, 0.6423859124721446,
        0.72968928615804163, 0.81029959388029904, 0.88644514917318362, 0.96150605146543733};
    static const double mid[5] = {
        0.029113887601773612, 0.11638402872809682, 0.26337786480329456, 0.47885453461538624, 0.78984065611473109};
    for (int c = 0; c < 2; ++c) {
        dnSteep_[c].set_coefs(steep);
        dnMid_[c].set_coefs(mid);
    }
}

void Pentad::syncStructure() {
    const int mode = voiceMode();
    if (mode != lastMode_) {
        for (auto& V : voices_)
            if (V.gate) release(V);
        heldN_ = 0;
        lastMode_ = mode;
    }
    const int N = polyphonyOf(get(Polyphony));
    if (N != lastPoly_) {
        for (int v = N; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            if (V.active && V.fade == 0) {
                V.pendNote = -1;
                V.fade = fadeLen_;
                V.gate = false;
            }
        }
        rr_ = 0;
        lastPoly_ = N;
    }
    const uint32_t sd = seedValue();
    if (sd != seedCache_) {
        seedCache_ = sd;
        for (int v = 0; v < kMaxVoices; ++v) computeStatics(v);
    }
}

void Pentad::renderChunk(float* out, int n) {
    float P[kNumParams];
    for (int i = 0; i < kNumParams; ++i) P[i] = pn_[i].load(std::memory_order_relaxed);
    const double sr = sampleRate_;

    syncStructure();

    // Oversampling factor: ×2 / ×4 at ≤ 48 kHz, halved at 88.2/96, ×1 at 176.4/192.
    int os = P[Oversample] > 0.5f ? 4 : 2;
    if (sr >= 176000.0) os = 1;
    else if (sr >= 88000.0)
        os = std::max(1, os / 2);
    if (os != osCur_) {
        for (int c = 0; c < 2; ++c) {
            dnSteep_[c].clear_buffers();
            dnMid_[c].clear_buffers();
        }
        osCur_ = os;
    }
    const double fsOs = sr * os;

    // ---- global per-sample controls (smoothed ~20 ms) ----
    const float sc = 1.0f - static_cast<float>(std::exp(-1.0 / (0.02 * sr)));
    const float bendRange = static_cast<float>(bendRangeOf(P[BendRange]));
    const float tgt[kSm] = {
        (P[Tune] - 0.5f) * 2.0f + ((P[Bend] - 0.5f) * 2.0f + gexpr_.wheel) * bendRange, // SmPitch (semis)
        P[ModWheel], std::min(1.0f, P[Aftertouch] + gexpr_.pressure),
        std::log2(20.0f) + std::clamp(P[Cutoff], 0.0f, 1.0f) * 9.96578428f // SmCut (log2 Hz)
            + (gexpr_.slide - 0.5f) * 4.0f,
        4.6f * P[Reso], P[MixAOn] > 0.5f ? P[MixA] : 0.0f, P[MixBOn] > 0.5f ? P[MixB] : 0.0f,
        P[MixNoiseOn] > 0.5f ? P[MixNoise] : 0.0f, P[FEnvAmt] * 9.0f, P[PmEnv], P[PmOscB], P[OaPw], P[ObPw],
        volumeGainOf(P[Volume]) * 0.35f, P[KeyTrk], P[LfoAmt], P[WmMix], P[UniDetune] * 0.5f};
    if (!smoothInit_) {
        for (int k = 0; k < kSm; ++k) sm_[k] = tgt[k];
        smoothInit_ = true;
    }

    // LFO
    const int lfoN = (P[LfoSaw] > 0.5f) + (P[LfoTri] > 0.5f) + (P[LfoSquare] > 0.5f);
    const bool lfoSync = P[LfoSync] > 0.5f && tSpb_ > 0.0;
    double lfoInc = lfoHzOf(P[LfoRate]) / sr;
    if (lfoSync) {
        const double beats = syncBeatsOf(P[LfoRate]);
        lfoInc = 1.0 / (beats * tSpb_);
        if (tFresh_ && tPlaying_) {
            const double c = tBeat_ / beats;
            lfoPh_ = c - std::floor(c);
        }
    }
    tFresh_ = false;
    const bool wmA = P[WmFreqA] > 0.5f, wmB = P[WmFreqB] > 0.5f, wmPA = P[WmPwA] > 0.5f, wmPB = P[WmPwB] > 0.5f,
               wmF = P[WmFilter] > 0.5f;
    const float atCut = P[AtCutoff] * 4.0f, atLfo = P[AtLfo];
    exprAtCut_ = atCut > 0.0f ? atCut : 2.0f; // per-note pressure → cutoff, octaves
    exprCoef_ = exprSmoothCoef(sr);

    for (int i = 0; i < n; ++i) {
        for (int k = 0; k < kSm; ++k) sm_[k] += (tgt[k] - sm_[k]) * sc;
        float lfo = 0.0f;
        if (lfoN > 0) {
            const float ph = static_cast<float>(lfoPh_);
            if (P[LfoSaw] > 0.5f) lfo += 2.0f * ph - 1.0f;
            if (P[LfoTri] > 0.5f) lfo += ph < 0.5f ? 4.0f * ph - 1.0f : 3.0f - 4.0f * ph;
            if (P[LfoSquare] > 0.5f) lfo += ph < 0.5f ? 1.0f : -1.0f;
            lfo /= static_cast<float>(lfoN);
        }
        lfoPh_ += lfoInc;
        if (lfoPh_ >= 1.0) lfoPh_ -= std::floor(lfoPh_);
        const float wmNoise = wmRng_.bi();
        const float src = lfo + (wmNoise - lfo) * sm_[SmWmMix];
        const float amt = std::clamp(sm_[SmLfoAmt] + sm_[SmMod] + atLfo * sm_[SmAt], 0.0f, 1.0f);
        const float semis = src * amt * amt * 12.0f;
        gPitchA_[i] = sm_[SmPitch] + (wmA ? semis : 0.0f);
        gPitchB_[i] = sm_[SmPitch] + (wmB ? semis : 0.0f);
        gPwA_[i] = sm_[SmPwA] + (wmPA ? src * amt * 0.45f : 0.0f);
        gPwB_[i] = sm_[SmPwB] + (wmPB ? src * amt * 0.45f : 0.0f);
        gCut_[i] = sm_[SmCut] + (wmF ? src * amt * 4.0f : 0.0f) + atCut * sm_[SmAt];
        gK_[i] = sm_[SmK];
        gMixA_[i] = sm_[SmMixA];
        gMixB_[i] = sm_[SmMixB];
        gMixN_[i] = sm_[SmMixN];
        gFenv_[i] = sm_[SmFenv];
        gPmE_[i] = sm_[SmPmEnv];
        gPmB_[i] = sm_[SmPmOscB];
        gVol_[i] = sm_[SmVol];
        gKey_[i] = sm_[SmKey];
        gDet_[i] = sm_[SmDet];
    }
    // Shared noise source at the oversampled rate (white, or Kellet "economy" pink).
    const bool pink = P[NoiseColor] > 0.5f;
    for (int k = 0, m = n * os; k < m; ++k) {
        const float w = noiseRng_.bi();
        if (pink) {
            pk0_ = 0.99765f * pk0_ + w * 0.0990460f;
            pk1_ = 0.96300f * pk1_ + w * 0.2965164f;
            pk2_ = 0.57000f * pk2_ + w * 1.0526913f;
            gNoise_[k] = (pk0_ + pk1_ + pk2_ + w * 0.1848f) * 0.3f;
        } else
            gNoise_[k] = w;
    }

    // ---- voices ----
    const int m = n * os;
    std::fill(osL_, osL_ + m, 0.0f);
    std::fill(osR_, osR_ + m, 0.0f);
    VoiceCtl c{};
    c.os = os;
    c.invOs = 1.0f / static_cast<float>(os);
    c.invFsOs = 1.0 / fsOs;
    c.fsOs = fsOs;
    c.sr = sr;
    c.drift = P[Drift];
    c.oaOff = 12.0f * octaveOf(P[OaOct]) + semiOf(P[OaSemi]) + fineCentsOf(P[OaFine]) * 0.01f;
    c.obOff = 12.0f * octaveOf(P[ObOct]) + semiOf(P[ObSemi]) + fineCentsOf(P[ObFine]) * 0.01f;
    c.sawA = P[OaSaw] > 0.5f ? 1.0f : 0.0f;
    c.pulA = P[OaPulse] > 0.5f ? 1.0f : 0.0f;
    c.sync = P[OaSync] > 0.5f;
    c.sawB = P[ObSaw] > 0.5f ? 1.0f : 0.0f;
    c.triB = P[ObTri] > 0.5f ? 1.0f : 0.0f;
    c.pulB = P[ObPulse] > 0.5f ? 1.0f : 0.0f;
    c.kbdB = P[ObKbd] > 0.5f;
    c.loB = P[ObLoFreq] > 0.5f;
    c.pmFreqA = P[PmFreqA] > 0.5f;
    c.pmPwA = P[PmPwA] > 0.5f;
    c.pmFilt = P[PmFilter] > 0.5f;
    c.velAmp = P[VelAmp];
    c.velFilt = P[VelFilt];
    c.comp = P[LowComp] > 0.5f ? 1.0f : 0.0f;
    c.noiseFloor = P[NoiseFloor] > 0.5f;
    const bool relOn = P[ReleaseOn] > 0.5f;
    c.tFA = attackSecOf(P[FAttack]) / 1.4663f;
    c.tFD = decaySecOf(P[FDecay]) * 0.25f;
    c.tFR = relOn ? decaySecOf(P[FRelease]) * 0.25f : 0.001f;
    c.tAA = attackSecOf(P[AAttack]) / 1.4663f;
    c.tAD = decaySecOf(P[ADecay]) * 0.25f;
    c.tAR = relOn ? decaySecOf(P[ARelease]) * 0.25f : 0.001f;
    c.fS = P[FSustain];
    c.aS = P[ASustain];
    const float gT = glideSecOf(P[Glide]);
    c.glideC = gT > 0.0f ? 1.0f - static_cast<float>(std::exp(-1.0 / (gT / 3.0 * sr))) : 1.0f;
    c.walkC = 1.0f - static_cast<float>(std::exp(-1.0 / (0.8 * sr)));
    c.spread = P[Spread];
    c.invFade = 1.0f / static_cast<float>(fadeLen_);
    c.cutMax = static_cast<float>(std::min(fsOs * 0.40, 30000.0));
    mixPeakChunk_ = 0.0f;

    int active = 0;
    for (int v = 0; v < kMaxVoices; ++v) {
        Voice& V = voices_[v];
        if (!V.active) {
            V.level = 0.0f;
            continue;
        }
        // Pan: unison stacks fan out across the field, poly voices on a golden-ratio spread.
        float pos = V.stackN > 1 ? (2.0f * V.stackIdx / static_cast<float>(V.stackN - 1) - 1.0f)
                                 : (2.0f * static_cast<float>(std::fmod(0.5 + v * 0.61803398875, 1.0)) - 1.0f);
        const float ang = (std::clamp(c.spread * pos, -1.0f, 1.0f) + 1.0f) * 0.25f * static_cast<float>(kPi);
        V.panL = std::cos(ang) * 1.41421356f;
        V.panR = std::sin(ang) * 1.41421356f;
        renderVoice(V, n, c);
        if (V.active) ++active;
    }

    // ---- decimate the oversampled stereo sum and add into the output ----
    if (os == 1) {
        for (int i = 0; i < n; ++i) {
            dsL_[i] = osL_[i];
            dsR_[i] = osR_[i];
        }
    } else if (os == 2) {
        dnSteep_[0].process_block(dsL_, osL_, n);
        dnSteep_[1].process_block(dsR_, osR_, n);
    } else {
        dnMid_[0].process_block(tmpL_, osL_, n * 2);
        dnMid_[1].process_block(tmpR_, osR_, n * 2);
        dnSteep_[0].process_block(dsL_, tmpL_, n);
        dnSteep_[1].process_block(dsR_, tmpR_, n);
    }
    float pl = 0.0f, prr = 0.0f;
    for (int i = 0; i < n; ++i) {
        float l = dsL_[i], r = dsR_[i];
        if (!std::isfinite(l) || !std::isfinite(r)) { l = r = 0.0f; }
        out[i * 2] += l;
        out[i * 2 + 1] += r;
        pl = std::max(pl, std::fabs(l));
        prr = std::max(prr, std::fabs(r));
    }
    const float dec = static_cast<float>(std::exp(-n / (0.3 * sr)));
    peakL_ = std::max(pl, peakL_ * dec);
    peakR_ = std::max(prr, peakR_ * dec);
    mixPeak_ = std::max(mixPeakChunk_, mixPeak_ * dec);
    chunkOutPeak_ = std::max(chunkOutPeak_, std::max(pl, prr));
    activeCount_ = active;
}

void Pentad::renderVoice(Voice& V, int n, const VoiceCtl& c) {
    using namespace pentad;
    const double sr = c.sr;
    // Per-voice envelope coefficients with the vintage time spread.
    const float ef = 1.0f + c.drift * 0.2f * V.oEnvF, ea = 1.0f + c.drift * 0.2f * V.oEnvA;
    const float fA = coefOf(c.tFA * ef, sr), fD = coefOf(c.tFD * ef, sr), fR = coefOf(c.tFR * ef, sr);
    const float aA = coefOf(c.tAA * ea, sr), aD = coefOf(c.tAD * ea, sr), aR = coefOf(c.tAR * ea, sr);
    // Unison stacks stay fat but don't scale linearly with the voice count.
    const float stackGain = V.stackN > 1 ? std::pow(static_cast<float>(V.stackN), -0.4f) : 1.0f;
    const float velA = (1.0f - c.velAmp * (1.0f - V.vel)) * stackGain;
    const float velF = 1.0f - c.velFilt * (1.0f - V.vel);
    const int os = c.os;
    const float kPiF = static_cast<float>(kPi);
    const bool audioCut = c.pmFilt && c.sawB + c.triB + c.pulB > 0.0f;
    float* L = osL_;
    float* R = osR_;
    float mixPk = mixPeakChunk_;

    for (int i = 0; i < n; ++i) {
        // -- stolen-voice fade → hand the voice to its pending note --
        float fadeG = 1.0f;
        if (V.fade > 0) {
            --V.fade;
            fadeG = static_cast<float>(V.fade) * c.invFade;
            if (V.fade == 0) {
                if (V.pendNote >= 0) {
                    V.fenv = V.aenv = 0.0f;
                    const int pn = V.pendNote;
                    V.pendNote = -1;
                    startVoice(static_cast<int>(&V - voices_), pn, V.pendVel, V.pendStack, V.pendStackN, V.pendGlide);
                    for (int d = 0; d < kExprDims; ++d) V.ex.set(d, V.pendExpr[d], kNoExprAdd);
                } else {
                    V.active = false;
                    V.gate = false;
                    V.fst = V.ast = 0;
                    V.fenv = V.aenv = 0.0f;
                    V.level = 0.0f;
                    return;
                }
            }
        }
        // -- glide (exponential, in semitones) --
        V.pitch += (V.pitchTgt - V.pitch) * c.glideC;
        // -- envelopes (RC curves; attack aims past 1 like the analog charge) --
        switch (V.fst) {
        case 1:
            V.fenv += (1.3f - V.fenv) * fA;
            if (V.fenv >= 1.0f) {
                V.fenv = 1.0f;
                V.fst = 2;
            }
            break;
        case 2: V.fenv += (c.fS - V.fenv) * fD; break;
        case 3:
            V.fenv -= V.fenv * fR;
            if (V.fenv < 1e-6f) {
                V.fenv = 0.0f;
                V.fst = 0;
            }
            break;
        default: break;
        }
        switch (V.ast) {
        case 1:
            V.aenv += (1.3f - V.aenv) * aA;
            if (V.aenv >= 1.0f) {
                V.aenv = 1.0f;
                V.ast = 2;
            }
            break;
        case 2: V.aenv += (c.aS - V.aenv) * aD; break;
        case 3:
            V.aenv -= V.aenv * aR;
            if (V.aenv < 3e-5f) {
                V.aenv = 0.0f;
                V.ast = 0;
            }
            break; // ≈ -90 dB
        default: break;
        }
        if (V.ast == 0 && V.fade == 0) { // asleep: stop computing this voice
            V.active = false;
            V.gate = false;
            V.fst = 0;
            V.level = 0.0f;
            mixPeakChunk_ = mixPk;
            return;
        }
        // -- slow random walk (vintage drift) --
        if (--V.wCount <= 0) {
            V.tA = V.rng.bi();
            V.tB = V.rng.bi();
            V.tC = V.rng.bi();
            V.wCount = static_cast<int>(sr * (0.4 + 1.6 * V.rng.uni())) + 1;
        }
        V.wA += (V.tA - V.wA) * c.walkC;
        V.wB += (V.tB - V.wB) * c.walkC;
        V.wC += (V.tC - V.wC) * c.walkC;

        // -- base-rate control targets --
        V.ex.tick(kNoExprAdd, exprCoef_);
        const float key = V.pitch + V.ex.bend();
        const float det =
            V.stackN > 1 ? gDet_[i] * (2.0f * V.stackIdx / static_cast<float>(V.stackN - 1) - 1.0f) : 0.0f;
        const float fe = V.fenv;
        const float pmE = gPmE_[i] * gPmE_[i];
        float noteA = key + det + gPitchA_[i] + c.oaOff + c.drift * (0.15f * V.oA + 0.08f * V.wA);
        if (c.pmFreqA) noteA += pmE * 60.0f * fe;
        float noteB = (c.kbdB ? key + det : 60.0f) + gPitchB_[i] + c.obOff + c.drift * (0.15f * V.oB + 0.08f * V.wB);
        if (c.loB) noteB -= 96.0f;
        const double dtA = std::min(440.0 * exp2f((noteA - 69.0f) * (1.0f / 12.0f)) * c.invFsOs, 0.45);
        const double dtB = std::min(440.0 * exp2f((noteB - 69.0f) * (1.0f / 12.0f)) * c.invFsOs, 0.45);
        float cut = gCut_[i] + gFenv_[i] * fe * velF + gKey_[i] * (key - 60.0f) * (1.0f / 12.0f) +
                    c.drift * (0.3f * V.oCut + 0.1f * V.wC);
        if (c.pmFilt) cut += gPmE_[i] * 6.0f * fe;
        cut += V.ex.slide() * 2.0f + V.ex.pressure() * exprAtCut_;
        const float fc = std::clamp(exp2f(cut), 8.0f, c.cutMax);
        const float g = tanPrewarp(kPiF * fc * static_cast<float>(c.invFsOs));
        const float pwA = gPwA_[i] + (c.pmPwA ? gPmE_[i] * 0.5f * fe : 0.0f) + c.drift * 0.02f * V.oPw;
        const float pwB = gPwB_[i];
        const float amp = (V.aenv * velA + 3e-5f) * fadeG * gVol_[i] * (1.0f + 0.5f * V.ex.pressure());
        const float pmB = gPmB_[i] * gPmB_[i];
        const float fmOct = c.pmFreqA ? pmB * 4.0f : 0.0f;
        const float pwmB = c.pmPwA ? gPmB_[i] * 0.5f : 0.0f;
        const float cutB = c.pmFilt ? pmB * 4.0f : 0.0f;
        if (V.fresh) {
            V.pDtA = dtA;
            V.pDtB = dtB;
            V.pG = g;
            V.pCut = cut;
            V.pPwA = pwA;
            V.pPwB = pwB;
            V.pAmp = amp;
            V.fresh = false;
        }
        const float k = gK_[i];
        const float mA = gMixA_[i], mB = gMixB_[i], mN = gMixN_[i];
        const float xin0 = 1.0f + c.comp * k;
        const float* nz = gNoise_ + i * os;
        float* lo = L + i * os;
        float* ro = R + i * os;

        for (int s = 0; s < os; ++s) {
            const float t = static_cast<float>(s + 1) * c.invOs;
            // --- Osc B (master) ---
            const double dB = V.pDtB + (dtB - V.pDtB) * t;
            const float duB = std::clamp(V.pPwB + (pwB - V.pPwB) * t, 0.0f, 1.0f);
            const float gzB = c.pulB * pulseZone(duB);
            const double pb0 = V.B.ph;
            const float wrapB = advance(V.B, dB, 0.0f, dB, duB, c.sawB, gzB);
            const float pb = static_cast<float>(V.B.ph);
            if (c.triB != 0.0f) { // triangle slope corners (BLAMP)
                const float sl = 8.0f * static_cast<float>(dB) * c.triB;
                const float inv = static_cast<float>(1.0 / dB);
                if (wrapB >= 0.0f) {
                    if (pb0 < 0.5) V.B.ring.blamp((0.5f + pb) * inv, -sl);
                    V.B.ring.blamp(wrapB, sl);
                    if (pb >= 0.5f) V.B.ring.blamp((pb - 0.5f) * inv, -sl);
                } else if (pb0 < 0.5 && pb >= 0.5f)
                    V.B.ring.blamp((pb - 0.5f) * inv, -sl);
            }
            float nb = c.sawB * (2.0f * pb - 1.0f);
            if (c.triB != 0.0f) nb += pb < 0.5f ? 4.0f * pb - 1.0f : 3.0f - 4.0f * pb;
            nb += gzB * ((V.B.hi ? 1.0f : -1.0f) - (2.0f * duB - 1.0f));
            V.B.ring.naive(nb);
            const float bOut = V.B.ring.pop();

            // --- Osc A (slave; Poly-Mod FM / PWM from B at audio rate) ---
            double dA = V.pDtA + (dtA - V.pDtA) * t;
            if (fmOct != 0.0f) dA = std::min(dA * exp2f(fmOct * bOut), 0.45);
            const float duA = std::clamp(V.pPwA + (pwA - V.pPwA) * t + pwmB * bOut, 0.0f, 1.0f);
            const float gzA = c.pulA * pulseZone(duA);
            if (c.sync && wrapB >= 0.0f) {
                advance(V.A, dA * (1.0 - wrapB), wrapB, dA, duA, c.sawA, gzA);
                const float pr = static_cast<float>(V.A.ph);
                V.A.ring.blep(wrapB, -2.0f * pr * c.sawA);                   // saw snaps back to -1
                if (!V.A.hi && duA > 0.0f) V.A.ring.blep(wrapB, 2.0f * gzA); // pulse goes high
                V.A.hi = duA > 0.0f;
                V.A.ph = 0.0;
                advance(V.A, dA * wrapB, 0.0f, dA, duA, c.sawA, gzA);
            } else {
                advance(V.A, dA, 0.0f, dA, duA, c.sawA, gzA);
            }
            const float pa = static_cast<float>(V.A.ph);
            V.A.ring.naive(c.sawA * (2.0f * pa - 1.0f) + gzA * ((V.A.hi ? 1.0f : -1.0f) - (2.0f * duA - 1.0f)));
            const float aOut = V.A.ring.pop();

            // --- mixer (soft saturation into the filter) ---
            const float mix = mA * aOut + mB * bOut + mN * nz[s];
            mixPk = std::max(mixPk, std::fabs(mix));
            const float x = V.sat.process(mix * 0.6) * (1.0f / 0.6f);

            // --- 4-pole OTA low-pass (ZDF, per-sample linearised tanh) ---
            float gg;
            if (audioCut) {
                const float cc = V.pCut + (cut - V.pCut) * t + cutB * bOut;
                gg = tanPrewarp(kPiF * std::clamp(exp2f(cc), 8.0f, c.cutMax) * static_cast<float>(c.invFsOs));
            } else
                gg = V.pG + (g - V.pG) * t;
            float G[4], S[4];
            for (int j = 0; j < 4; ++j) {
                const float a = gg * tanhRatio(kStageDrive * V.df[j]);
                const float inv = 1.0f / (1.0f + a);
                G[j] = a * inv;
                S[j] = V.s[j] * inv;
            }
            const float Gt = G[0] * G[1] * G[2] * G[3];
            const float St = G[3] * (G[2] * (G[1] * S[0] + S[1]) + S[2]) + S[3];
            const float xin = x * xin0;
            // The resonance loop saturates (it bounds the self-oscillation amplitude
            // without dragging its pitch off the cutoff the way stage clipping would).
            const float kf = k * tanhRatio(kLoopDrive * V.y4);
            float y3 = (Gt * xin + St) / (1.0f + kf * Gt);
            const float u = xin - kf * y3;
            const float y0 = G[0] * u + S[0], y1 = G[1] * y0 + S[1], y2 = G[2] * y1 + S[2];
            y3 = G[3] * y2 + S[3];
            V.df[0] = u - y0;
            V.df[1] = y0 - y1;
            V.df[2] = y1 - y2;
            V.df[3] = y2 - y3;
            V.s[0] = 2.0f * y0 - V.s[0];
            V.s[1] = 2.0f * y1 - V.s[1];
            V.s[2] = 2.0f * y2 - V.s[2];
            V.s[3] = 2.0f * y3 - V.s[3];
            V.y4 = y3;

            // --- VCA (gentle curvature + leakage) and noise floor ---
            float o = std::clamp(y3, -6.0f, 6.0f) * (V.pAmp + (amp - V.pAmp) * t);
            o -= 0.008f * o * o * o;
            if (c.noiseFloor) o += 1e-5f * V.rng.bi();
            lo[s] += o * V.panL;
            ro[s] += o * V.panR;
        }
        V.pDtA = dtA;
        V.pDtB = dtB;
        V.pG = g;
        V.pCut = cut;
        V.pPwA = pwA;
        V.pPwB = pwB;
        V.pAmp = amp;

        // Extreme settings must never poison the voice: reset a non-finite filter.
        if (!std::isfinite(V.s[0] + V.s[1] + V.s[2] + V.s[3])) {
            for (int j = 0; j < 4; ++j) {
                V.s[j] = 0.0f;
                V.df[j] = 0.0f;
            }
            V.y4 = 0.0f;
        }
    }
    V.level = V.aenv * velA * (V.fade > 0 ? V.fade * c.invFade : 1.0f);
    mixPeakChunk_ = mixPk;
}

void Pentad::publishTelemetry(int32_t /*frames*/) {
    tele_[ScPoly].store(static_cast<float>(lastPoly_), std::memory_order_relaxed);
    tele_[ScActive].store(static_cast<float>(activeCount_), std::memory_order_relaxed);
    tele_[ScPeakL].store(peakL_, std::memory_order_relaxed);
    tele_[ScPeakR].store(peakR_, std::memory_order_relaxed);
    tele_[ScMixPeak].store(mixPeak_, std::memory_order_relaxed);
    tele_[ScCpu].store(static_cast<float>(cpu_), std::memory_order_relaxed);
    tele_[ScOs].store(static_cast<float>(osCur_), std::memory_order_relaxed);
    tele_[ScRate].store(static_cast<float>(sampleRate_), std::memory_order_relaxed);
    for (int v = 0; v < kMaxVoices; ++v)
        tele_[ScVoices + v].store(voices_[v].active ? voices_[v].level : 0.0f, std::memory_order_relaxed);
}

} // namespace nota
