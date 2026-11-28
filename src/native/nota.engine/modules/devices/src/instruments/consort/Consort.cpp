// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Consort implementation. Denormal guard
// unified onto dsp::ScopedNoDenormals, fast math via Pentad.h's unified wrappers
// (both bit-identical); adds auto-sleep over voices + delay flight time
// (isIdle/hasTail/wake; a running sequencer blocks sleep).

#include "Consort.h"
#include <vector>
#include <string>
#include <memory>
#include <cstring>
#include <cstdlib>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <climits>
#include <atomic>
#include <algorithm>
#include "nota/dsp/Instrument.h"
#include "instruments/pentad/Pentad.h"
#include "common/DeviceMath.h"

namespace nota {

Consort::Consort() {
    // "Init": four saws at 8' (a hair apart), PARA, stereo LP/LP a half-octave apart,
    // a moderate filter envelope, organ-ish amp envelope; delay dry, sequencer off.
    for (int i = 0; i < kNumParams; ++i) pn_[i].store(0.0f, std::memory_order_relaxed);
    set(Tune, 0.5f);
    set(Bend, 0.5f);
    set(ModWheel, 0.0f);
    set(Aftertouch, 0.0f);
    set(BendRange, 1.0f / 11.0f);
    set(Volume, 0.6f);
    set(Oversample, 0.5f);
    set(VoiceMode, 1.0f);
    set(TruePoly, 0.0f);
    set(MultiTrig, 1.0f);
    set(Unison, 1.0f);
    set(Drift, 0.25f);
    set(Glide, 0.0f);
    set(GlideType, 1.0f);
    set(GlideGated, 0.0f);
    set(VelVca, 0.0f);
    set(AtCut, 0.0f);
    set(O1Oct, 0.5f);
    set(O1Wave, 1.0f / 3.0f);
    set(O2Oct, 0.5f);
    set(O2Freq, 0.5f + 0.05f / 14.0f);
    set(O2Wave, 1.0f / 3.0f);
    set(O2Sync, 0.0f);
    set(O3Oct, 0.5f);
    set(O3Freq, 0.5f - 0.04f / 14.0f);
    set(O3Wave, 1.0f / 3.0f);
    set(O4Oct, 0.5f);
    set(O4Freq, 0.5f + 0.08f / 14.0f);
    set(O4Wave, 1.0f / 3.0f);
    set(O4Sync, 0.0f);
    set(Pw, 0.4f);
    set(Mix1, 0.75f);
    set(Mix2, 0.75f);
    set(Mix3, 0.75f);
    set(Mix4, 0.75f);
    set(MixNoise, 0.0f);
    set(MixExt, 0.0f);
    set(NoiseColor, 0.0f);
    set(MixDrive, 0.0f);
    set(FiltMode, 0.5f);
    set(Cutoff, 0.55f);
    set(Reso, 0.15f);
    set(Spacing, 0.5f + 0.5f / 6.0f);
    set(FEnvAmt, 0.66f);
    set(KbdTrk, 0.5f);
    set(BassComp, 0.0f);
    set(FAttack, 0.0f);
    set(FDecay, 0.45f);
    set(FSustain, 0.4f);
    set(FRelease, 0.4f);
    set(AAttack, 0.0f);
    set(ADecay, 0.45f);
    set(ASustain, 0.85f);
    set(ARelease, 0.35f);
    set(FDCurve, 0.5f);
    set(FRCurve, 0.5f);
    set(FEnvExp, 1.0f);
    set(ADCurve, 0.5f);
    set(ARCurve, 0.5f);
    set(AEnvExp, 1.0f); // straight + Exp: the RC generator, as before
    set(LfoWave, 0.0f);
    set(LfoRate, 0.5f);
    set(LfoSync, 0.0f);
    set(LfoPitch, 0.5f);
    set(LfoCut, 0.0f);
    set(LfoPwm, 0.0f);
    set(LfoDest, 0.0f);
    set(DlyTime, 0.62f);
    set(DlySpacing, 0.5f + 0.13f);
    set(DlyFb, 0.35f);
    set(DlyMix, 0.0f);
    set(DlyPing, 1.0f);
    set(DlyDigital, 0.0f);
    set(SeqMode, 0.0f);
    set(SeqRate, 0.6f);
    set(SeqSwing, 0.0f);
    set(SeqOrder, 0.0f);
    set(SeqLatch, 0.0f);
    set(SeqLength, 1.0f);
    set(SeqRatchet, 0.5f);
    set(ArpOct, 0.0f);
    static const int pat[kSteps] = {0, 0, 12, 0, 7, 0, 12, 3, 0, 0, 12, 0, 7, 5, 10, 12};
    for (int s = 0; s < kSteps; ++s) {
        set(StepType0 + s, 0.0f);
        set(StepPitch0 + s, 0.5f + static_cast<float>(pat[s]) / 48.0f);
    }
    set(Atten1, 0.5f);
    set(Atten2, 0.5f);
    for (int c = 0; c < kCables; ++c) {
        set(Cable0 + c * 3, 0.0f);
        set(Cable0 + c * 3 + 1, 0.0f);
        set(Cable0 + c * 3 + 2, 0.75f);
    }

    initDecimators();
    dlyN_ = kDlyLen;
    dlyBuf_.reset(
        static_cast<float*>(std::calloc(static_cast<size_t>(kDlyLen) * 4, sizeof(float)))); // L, R, gain L, gain R
    resetAll();
}

void Consort::controller(int32_t ctl, float value) {
    if (ctl == kCtlModWheel) pn_[ModWheel].store(std::clamp(value, 0.0f, 1.0f), std::memory_order_relaxed);
    Instrument::controller(ctl, value);
    wake();
}

int32_t Consort::scopeRead(float* out, int32_t maxN) const {
    const int n = std::min<int>(maxN, kScopeN);
    for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
    if (n > ScCpu) out[ScCpu] = readScopeCpuLoad();
    return n;
}

int32_t Consort::heldNotes(int32_t* out, int32_t maxN) const {
    const int n = std::min<int>(keyN_, maxN);
    for (int i = 0; i < n; ++i) out[i] = keys_[i];
    return n;
}

const std::vector<std::string>& Consort::idTable() {
    static const std::vector<std::string> t = [] {
        std::vector<std::string> v = {"tune", "bend", "modwheel", "aftertouch", "bendrange", "volume", "oversample",
            "voicemode", "truepoly", "multitrig", "unison", "drift", "glide", "glidetype", "glidegated", "velvca",
            "atcut", "o1oct", "o1wave", "o2oct", "o2freq", "o2wave", "o2sync", "o3oct", "o3freq", "o3wave", "o4oct",
            "o4freq", "o4wave", "o4sync", "pw", "mix1", "mix2", "mix3", "mix4", "mixnoise", "mixext", "noisecolor",
            "mixdrive", "filtmode", "cutoff", "reso", "spacing", "fenvamt", "kbdtrk", "basscomp", "fattack", "fdecay",
            "fsustain", "frelease", "aattack", "adecay", "asustain", "arelease", "lfowave", "lforate", "lfosync",
            "lfopitch", "lfocut", "lfopwm", "lfodest", "dlytime", "dlyspacing", "dlyfb", "dlymix", "dlyping",
            "dlydigital", "seqmode", "seqrate", "seqswing", "seqorder", "seqlatch", "seqlen", "seqratchet", "arpoct"};
        for (int s = 0; s < kSteps; ++s) v.push_back("st" + std::to_string(s + 1));
        for (int s = 0; s < kSteps; ++s) v.push_back("sp" + std::to_string(s + 1));
        v.push_back("atten1");
        v.push_back("atten2");
        for (int c = 0; c < kCables; ++c) {
            const std::string n = std::to_string(c + 1);
            v.push_back("c" + n + "src");
            v.push_back("c" + n + "dst");
            v.push_back("c" + n + "amt");
        }
        for (const char* id : {"fdcurve", "frcurve", "fenvexp", "adcurve", "arcurve", "aenvexp"}) v.push_back(id);
        return v;
    }();
    return t;
}

const std::vector<std::string>& Consort::nameTable() {
    static const std::vector<std::string> t = [] {
        std::vector<std::string> v = {"Master Tune", "Control Pitch-Bend", "Control Mod-Wheel", "Control Aftertouch",
            "Control Bend-Range", "Output Volume", "Output Oversampling", "Voice Mode", "Voice True-Poly",
            "Voice Multi-Trig", "Voice Unison-Detune", "Voice Drift", "Voice Glide-Time", "Voice Glide-Type",
            "Voice Glide-Gated", "Output Velocity-VCA", "Output Aftertouch-Cutoff", "Osc 1 Octave", "Osc 1 Wave",
            "Osc 2 Octave", "Osc 2 Frequency", "Osc 2 Wave", "Osc 2 Sync", "Osc 3 Octave", "Osc 3 Frequency",
            "Osc 3 Wave", "Osc 4 Octave", "Osc 4 Frequency", "Osc 4 Wave", "Osc 4 Sync", "Osc Pulse-Width",
            "Mixer Osc-1", "Mixer Osc-2", "Mixer Osc-3", "Mixer Osc-4", "Mixer Noise", "Mixer Ext", "Mixer Noise-Color",
            "Mixer Drive", "Filter Mode", "Filter Cutoff", "Filter Resonance", "Filter Spacing", "Filter Env-Amount",
            "Filter Key-Track", "Filter Bass-Comp", "Filter-Env Attack", "Filter-Env Decay", "Filter-Env Sustain",
            "Filter-Env Release", "Amp-Env Attack", "Amp-Env Decay", "Amp-Env Sustain", "Amp-Env Release", "LFO Wave",
            "LFO Rate", "LFO Tempo-Sync", "LFO Pitch", "LFO Cutoff", "LFO PWM", "LFO Destination", "Delay Time",
            "Delay Spacing", "Delay Feedback", "Delay Mix", "Delay Ping-Pong", "Delay Digital", "Seq Mode", "Seq Rate",
            "Seq Swing", "Seq Order", "Seq Latch", "Seq Length", "Seq Ratchet", "Seq Arp-Octaves"};
        auto two = [](int i) { return (i < 10 ? "0" : "") + std::to_string(i); };
        for (int s = 0; s < kSteps; ++s) v.push_back("Seq-Type Step-" + two(s + 1));
        for (int s = 0; s < kSteps; ++s) v.push_back("Seq-Pitch Step-" + two(s + 1));
        v.push_back("Patch Atten-1");
        v.push_back("Patch Atten-2");
        for (int c = 0; c < kCables; ++c) {
            const std::string n = two(c + 1);
            v.push_back("Patch-Source Cable-" + n);
            v.push_back("Patch-Dest Cable-" + n);
            v.push_back("Patch-Depth Cable-" + n);
        }
        for (const char* nm : {"Filter Env Decay Curve", "Filter Env Release Curve", "Filter Env Exp",
                 "Amp Env Decay Curve", "Amp Env Release Curve", "Amp Env Exp"})
            v.push_back(nm);
        return v;
    }();
    return t;
}

int32_t Consort::pluginParamIndexOfId(const std::string& id) const {
    const auto& t = idTable();
    for (int32_t i = 0; i < kNumParams; ++i)
        if (t[static_cast<size_t>(i)] == id) return i;
    return -1;
}

std::vector<uint8_t> Consort::getState() const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        const float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + static_cast<size_t>(i) * sizeof(float), &v, sizeof(float));
    }
    return b;
}

void Consort::setState(const uint8_t* data, int32_t size) {
    if (!data) return;
    const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + static_cast<size_t>(i) * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    wake();
}

std::shared_ptr<Instrument> Consort::clone() const {
    auto s = std::make_shared<Consort>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(pendingRate_.load(std::memory_order_relaxed));
    return s;
}

double Consort::seqRateBeats(float v) {
    static const double b[kSeqRates] = {1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125}; // 1/4 1/8 1/8T 1/16 1/16T 1/32
    return b[std::clamp(static_cast<int>(std::lround(v * (kSeqRates - 1))), 0, kSeqRates - 1)];
}

double Consort::syncBeatsOf(float v) { // LFO cycle length in beats (quarter notes)
    static const double b[kSyncDivs] = {
        32., 16., 8., 4., 2., 1.5, 1., 0.75, 2. / 3., 0.5, 1. / 3., 0.25, 1. / 6., 0.125};
    return b[std::clamp(static_cast<int>(std::lround(v * (kSyncDivs - 1))), 0, kSyncDivs - 1)];
}

void Consort::setTransport(double beatStart, double samplesPerBeat, bool playing) {
    const double pos = beatStart * samplesPerBeat;
    const bool jumped = playing && tPlaying_ && std::fabs(pos - (tPos_ + tFrames_)) > 2.0;
    if (jumped) resetAll();
    else if (playing && !tPlaying_) {
        bool any = false;
        for (int v = 0; v < kMaxVoices; ++v) any |= voices_[v].active;
        if (!any && !seqRun_) resetAll();
    }
    tBeat_ = beatStart;
    tSpb_ = samplesPerBeat;
    tFresh_ = true;
    tPos_ = pos;
    tFrames_ = 0.0;
    if (playing != tPlaying_) nextEvBeat_ = -1e300; // re-grid the sequencer on the new clock
    tPlaying_ = playing;
}

void Consort::noteOn(int32_t pitch, float velocity) {
    wake();
    syncStructure();
    const float vel = std::clamp(velocity, 0.0f, 1.0f);
    pushKey(pitch);
    if (seqMode() != 0) {
        seqKeyOn(pitch, vel);
        return;
    }
    playOn(pitch, vel);
}

void Consort::noteOff(int32_t pitch) {
    wake();
    syncStructure();
    removeKey(pitch);
    if (seqMode() != 0) {
        seqKeyOff(pitch);
        return;
    }
    playOff(pitch);
}

void Consort::render(float* out, int32_t frames) {
    bool skip = false;
    const uint32_t gateEpoch = gate_.beginBlock(&skip);
    const double pr = pendingRate_.load(std::memory_order_relaxed);
    if (skip && pr == sampleRate_) return;
    dsp::ScopedNoDenormals noDenormals;
    const auto cpuTimer = blockTimer(frames, pr, 0.05);
    if (pr != sampleRate_) { // voices keep playing; only rate-derived state restarts
        sampleRate_ = pr;
        fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
        for (int c = 0; c < 2; ++c) {
            dnSteep_[c].clear_buffers();
            dnMid_[c].clear_buffers();
        }
        smoothInit_ = false;
        setupDelayRate();
    }
    for (int32_t done = 0; done < frames;) {
        const int n = std::min<int>(kChunk, frames - done);
        renderChunk(out + done * 2, n); // advances tFrames_
        done += n;
    }
    // Sleep: no sounding voice plus a silent block (voices + delay tap) means
    // the tail has settled; sleep additionally waits out the delay line's
    // flight time so an in-flight echo is never cut (cf. Delay's loop logic).
    // A running sequencer (latched) blocks sleep — render advances its clock.
    const double flight = std::max(dlyTimeCur_[0] > 0.0f ? static_cast<double>(dlyTimeCur_[0]) : 0.0,
        dlyTimeCur_[1] > 0.0f ? static_cast<double>(dlyTimeCur_[1]) : 0.0);
    gate_.setHangoverSamples(static_cast<uint64_t>(std::max(0.0, flight)) + kIdleSamples);
    gate_.observe(seqRun_ || activeCount_ > 0 || chunkOutPeak_ > 1.0e-5f, frames, gateEpoch);
    chunkOutPeak_ = 0.0f;
    publishTelemetry();
}

void Consort::wake() {
    gate_.wake();
}

void Consort::setOscTarget(Voice& V, int k, int note, bool glide) {
    const float tg = static_cast<float>(note);
    const float T = glideSecOf(get(Glide));
    if (!glide || !V.hasPitch[k] || T <= 0.0f) {
        V.pitch[k] = V.tgt[k] = tg;
        V.rate[k] = 0.0f;
        V.hasPitch[k] = true;
        return;
    }
    V.tgt[k] = tg;
    const int type = std::clamp(static_cast<int>(std::lround(get(GlideType) * 2.0f)), 0, 2);
    const double spT = T * sampleRate_;
    if (type == 0) V.rate[k] = static_cast<float>(12.0 / spT); // LCR: T per octave
    else if (type == 1)
        V.rate[k] = static_cast<float>(std::fabs(tg - V.pitch[k]) / spT); // LCT: always T
    else
        V.rate[k] = -1.0f; // EXP
}

void Consort::wake(Voice& V) {
    V.active = true;
    V.fresh = true;
    const float duty = dutyOf(get(Pw));
    for (int k = 0; k < kOsc; ++k) {
        V.osc[k].ph += V.rng.uni();
        V.osc[k].ph -= std::floor(V.osc[k].ph);
        V.osc[k].hi = V.osc[k].ph < duty;
        V.osc[k].ring.clear();
        V.oPrev[k] = 0.0f;
    }
    V.fa.clear();
    V.fb.clear();
    V.sat.reset();
    V.fPrev = V.vPrev = 0.0f;
    V.uIn[0] = V.uIn[1] = V.uIn[2] = 0.0f;
    V.gIn1 = V.gIn2 = 0.0f;
    V.g1s = V.g2s = false;
}

void Consort::paraOn(int p, float vel) {
    const bool others = pHeldN_ > 0;
    listPush(pHeld_, pHeldN_, p);
    Voice& V = voices_[0];
    V.vel = vel;
    V.note = p;
    V.order = ++orderCtr_;
    focus_ = 0;
    assignSlots(others);
    V.gate = true;
    if (!others || get(MultiTrig) > 0.5f) trigger(V);
    if (!V.active) wake(V);
}

void Consort::paraOff(int p) {
    if (!listRemove(pHeld_, pHeldN_, p)) return;
    Voice& V = voices_[0];
    if (pHeldN_ == 0) {
        release(V);
        return;
    } // slots keep their pitch for the release tail
    V.note = pHeld_[pHeldN_ - 1];
    assignSlots(true);
}

void Consort::assignSlots(bool legato) {
    Voice& V = voices_[0];
    const int S = slotsOf(structMode());
    const int nw = std::min(S, pHeldN_);
    int want[4] = {-1, -1, -1, -1};
    for (int j = 0; j < nw; ++j) want[j] = pHeld_[pHeldN_ - nw + j];
    int na[4] = {-1, -1, -1, -1};
    bool used[4] = {false, false, false, false};
    for (int s = 0; s < S; ++s) {
        if (slotDbl_[s] || slotNote_[s] < 0) continue;
        for (int j = 0; j < nw; ++j)
            if (!used[j] && want[j] == slotNote_[s]) {
                na[s] = want[j];
                used[j] = true;
                break;
            }
    }
    for (int j = 0; j < nw; ++j) {
        if (used[j]) continue;
        for (int t = 0; t < S; ++t) {
            const int s = (rr_ + t) % S;
            if (na[s] < 0) {
                na[s] = want[j];
                used[j] = true;
                rr_ = (s + 1) % S;
                break;
            }
        }
    }
    bool dbl[4] = {false, false, false, false};
    if (get(Unison) > 0.5f && nw > 0) {
        int k = 0;
        for (int s = 0; s < S; ++s)
            if (na[s] < 0) {
                na[s] = want[nw - 1 - (k % nw)];
                dbl[s] = true;
                ++k;
            }
    }
    for (int s = 0; s < 4; ++s) {
        slotNote_[s] = s < S ? na[s] : -1;
        slotDbl_[s] = s < S && dbl[s];
    }
    const bool gl = glideAllowed(legato);
    for (int k = 0; k < kOsc; ++k) {
        const int note = na[slotOfOsc(k, S)];
        if (note >= 0) {
            setOscTarget(V, k, note, gl);
            V.gT[k] = 1.0f;
        } else
            V.gT[k] = 0.0f;
    }
}

void Consort::polyOn(int p, float vel) {
    const bool others = pHeldN_ > 0;
    listPush(pHeld_, pHeldN_, p);
    const bool glide = glideAllowed(others);
    int v = -1;
    for (int k = 0; k < kMaxVoices; ++k) {
        const Voice& V = voices_[k];
        if (V.active && V.gate && V.note == p && V.pendNote < 0) {
            v = k;
            break;
        }
    }
    bool fade = false;
    if (v < 0) v = allocate(fade);
    Voice& V = voices_[v];
    if (fade) {
        V.pendNote = p;
        V.pendVel = vel;
        V.pendGlide = glide;
        V.fade = fadeLen_;
        V.gate = false;
        return;
    }
    startPoly(v, p, vel, glide);
}

void Consort::startPoly(int v, int p, float vel, bool glide) {
    Voice& V = voices_[v];
    V.note = p;
    V.vel = vel;
    V.gate = true;
    V.order = ++orderCtr_;
    for (int k = 0; k < kOsc; ++k) {
        setOscTarget(V, k, p, glide);
        V.gT[k] = 1.0f;
        V.g[k] = 1.0f;
    }
    trigger(V);
    focus_ = v;
    if (!V.active) wake(V);
}

void Consort::polyOff(int p) {
    listRemove(pHeld_, pHeldN_, p);
    for (int v = 0; v < kMaxVoices; ++v) {
        Voice& V = voices_[v];
        if (V.gate && V.note == p) release(V);
        if (V.pendNote == p) V.pendNote = -1;
    }
}

int Consort::allocate(bool& fade) {
    fade = false;
    int best = -1;
    uint64_t bo = ~0ull;
    for (int v = 0; v < kMaxVoices; ++v)
        if (!voices_[v].active && voices_[v].pendNote < 0 && voices_[v].order < bo) {
            bo = voices_[v].order;
            best = v;
        }
    if (best >= 0) return best;
    for (int v = 0; v < kMaxVoices; ++v)
        if (!voices_[v].gate && voices_[v].pendNote < 0 && voices_[v].order < bo) {
            bo = voices_[v].order;
            best = v;
        }
    if (best >= 0) return best;
    for (int v = 0; v < kMaxVoices; ++v)
        if (voices_[v].pendNote < 0 && voices_[v].order < bo) {
            bo = voices_[v].order;
            best = v;
        }
    if (best < 0) best = 0;
    fade = true;
    return best;
}

void Consort::seqKeyOn(int p, float vel) {
    const bool latch = get(SeqLatch) > 0.5f;
    if (latch && physN_ == 0) latN_ = 0; // a fresh chord replaces the latched one
    listPush(phys_, physN_, p);
    if (latch) listPush(lat_, latN_, p);
    seqVel_ = vel;
    seqBase_ = p;
    if (!seqRun_ && poolN() > 0) seqStart();
}

void Consort::seqKeyOff(int p) {
    listRemove(phys_, physN_, p);
    if (get(SeqLatch) > 0.5f) return;
    if (physN_ == 0) {
        seqStop();
        return;
    }
    seqBase_ = phys_[physN_ - 1];
}

void Consort::seqStart() {
    seqRun_ = true;
    curN_ = LLONG_MIN;
    arpPos_ = -1;
    seqIntBeat_ = 0.0;
    nextEvBeat_ = -1e300;
    curType_ = 3;
    curSub_ = 0;
}

void Consort::seqStop() {
    if (seqNote_ >= 0) {
        playOff(seqNote_);
        seqNote_ = -1;
    }
    seqRun_ = false;
    curN_ = LLONG_MIN;
    curIdx_ = -1;
    seqGateV_ = 0.0f;
}

int Consort::stepIndexOf(long long n) const {
    const int len = seqLengthOf(get(SeqLength));
    const int ord = std::clamp(static_cast<int>(std::lround(get(SeqOrder) * 2.0f)), 0, 2);
    const long long m = ((n % len) + len) % len;
    if (seqMode() == 2 || ord == 0) return static_cast<int>(m); // ARP: order walks the chord
    if (ord == 1) return len - 1 - static_cast<int>(m);
    return static_cast<int>(
        pentad::hash32(static_cast<uint32_t>(n) * 2654435761U ^ 0x51ED27U) % static_cast<uint32_t>(len));
}

int Consort::arpNext() {
    int srt[128];
    const int n = poolN();
    if (n <= 0) return seqBase_;
    std::memcpy(srt, poolArr(), sizeof(int) * static_cast<size_t>(n));
    std::sort(srt, srt + n);
    const int oct = arpOctOf(get(ArpOct));
    const int cnt = n * oct;
    const int ord = std::clamp(static_cast<int>(std::lround(get(SeqOrder) * 2.0f)), 0, 2);
    int j;
    if (ord == 2)
        j = static_cast<int>(
            pentad::hash32(static_cast<uint32_t>(++arpCtr_) * 0x9E3779B9U ^ 0xA11CE5U) % static_cast<uint32_t>(cnt));
    else {
        arpPos_ = (arpPos_ + 1) % cnt;
        j = ord == 1 ? cnt - 1 - arpPos_ : arpPos_;
    }
    seqPitchV_ = static_cast<float>(12 * (j / n) + srt[j % n] - srt[0]) / 60.0f;
    return srt[j % n] + 12 * (j / n);
}

int Consort::seqPitchFor(int idx) {
    if (seqMode() == 2) return std::clamp(arpNext(), 0, 127);
    const int sp = stepPitch(idx);
    seqPitchV_ = static_cast<float>(sp) / 60.0f;
    return std::clamp(seqBase_ + sp, 0, 127);
}

void Consort::seqService() {
    if (!seqRun_) return;
    const double b = beatNow();
    for (int guard = 0; guard < 8 && b + 1e-9 >= nextEvBeat_; ++guard) seqProcess(b);
}

void Consort::seqProcess(double b) {
    constexpr double eps = 1e-9;
    const double rb = seqRateBeats(get(SeqRate));
    const double sw = swingOf(get(SeqSwing));
    const double pair = std::floor((b + eps) / (2.0 * rb));
    const double p = b - pair * 2.0 * rb;
    const bool odd = p + eps >= 2.0 * rb * sw;
    const long long n = static_cast<long long>(pair) * 2 + (odd ? 1 : 0);
    const double sb = pair * 2.0 * rb + (odd ? 2.0 * rb * sw : 0.0);
    const double sl = odd ? 2.0 * rb * (1.0 - sw) : 2.0 * rb * sw;
    const int R = ratchetOf(get(SeqRatchet));
    if (n != curN_) {
        curN_ = n;
        const int idx = stepIndexOf(n);
        const int nextT = stepType(stepIndexOf(n + 1));
        curIdx_ = idx;
        clk_ = std::max(1, static_cast<int>(0.005 * sampleRate_));
        const int t = stepType(idx);
        const double gateEnd = nextT == 2 ? 1e300 : sb + 0.6 * sl;
        if (t == 3) {
            seqNoteOff();
            curType_ = 3;
        } else if (t == 2) {
            if (seqNote_ < 0) curType_ = 3;
            else {
                curType_ = 2;
                offBeat_ = gateEnd;
            }
        } else if (t == 1) {
            seqNoteOn(seqPitchFor(idx));
            curType_ = 1;
            curSub_ = 0;
            curPitch_ = seqNote_;
            offBeat_ = sb + 0.5 * sl / R;
        } else {
            seqNoteOn(seqPitchFor(idx));
            curType_ = 0;
            offBeat_ = gateEnd;
        }
    } else if (curType_ == 1) {
        const int k = static_cast<int>(std::floor((b - sb + eps) / (sl / R)));
        if (k != curSub_ && k < R) {
            curSub_ = k;
            clk_ = std::max(1, static_cast<int>(0.005 * sampleRate_));
            seqNoteOn(curPitch_);
            offBeat_ = sb + (k + 0.5) * sl / R;
        }
    }
    if (seqNote_ >= 0 && b + eps >= offBeat_) seqNoteOff();
    double nx = sb + sl;
    if (curType_ == 1) {
        const double sub = sb + (curSub_ + 1) * sl / R;
        if (sub < nx - eps) nx = sub;
    }
    if (seqNote_ >= 0 && offBeat_ < nx) nx = offBeat_;
    nextEvBeat_ = std::max(nx, b + 1e-7);
}

int Consort::seqSamplesToNext() const {
    if (!seqRun_) return INT_MAX;
    const double d = (nextEvBeat_ - beatNow()) * spb();
    if (d <= 0.0) return 0;
    return d > 1e9 ? INT_MAX : static_cast<int>(std::ceil(d - 1e-6));
}

void Consort::syncStructure() {
    const int st = structMode();
    if (st != lastStruct_) {
        for (auto& V : voices_)
            if (V.gate || V.pendNote >= 0) {
                release(V);
                V.pendNote = -1;
            }
        pHeldN_ = 0;
        rr_ = 0;
        for (int s = 0; s < 4; ++s) {
            slotNote_[s] = -1;
            slotDbl_[s] = false;
        }
        if (seqNote_ >= 0) seqNote_ = -1;
        lastStruct_ = st;
    }
    const int sm = seqMode();
    if (sm != lastSeq_) {
        seqStop();
        physN_ = latN_ = 0;
        for (auto& V : voices_)
            if (V.gate) release(V);
        pHeldN_ = 0;
        lastSeq_ = sm;
    }
    if (get(SeqLatch) < 0.5f && latN_ > 0) {
        latN_ = 0;
        if (seqRun_ && physN_ == 0) seqStop();
    }
}

void Consort::resetVoices() {
    for (int v = 0; v < kMaxVoices; ++v) {
        Voice& V = voices_[v];
        const pentad::Rng rng = V.rng;
        const float oOff[kOsc] = {V.oOff[0], V.oOff[1], V.oOff[2], V.oOff[3]};
        const float oCut = V.oCut;
        V = Voice{};
        V.rng = rng;
        V.oCut = oCut;
        for (int k = 0; k < kOsc; ++k) {
            V.oOff[k] = oOff[k];
            V.osc[k].ph = V.rng.uni();
        }
        V.wCount = 1 + static_cast<int>(V.rng.next() % 4096u);
    }
    pHeldN_ = 0;
    keyN_ = 0;
    rr_ = 0;
    physN_ = latN_ = 0;
    for (int s = 0; s < 4; ++s) {
        slotNote_[s] = -1;
        slotDbl_[s] = false;
    }
    seqRun_ = false;
    seqNote_ = -1;
    curN_ = LLONG_MIN;
    curIdx_ = -1;
    seqGateV_ = 0.0f;
    seqPitchV_ = 0.0f;
    clk_ = 0;
    lastStruct_ = structMode();
    lastSeq_ = seqMode();
    fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
}

void Consort::resetAll() {
    for (int v = 0; v < kMaxVoices; ++v) {
        Voice& V = voices_[v];
        V.rng.seed(pentad::hash32(0xC0A5E7u ^ pentad::hash32(static_cast<uint32_t>(v) * 0x9E3779B9U + 1U)));
        for (int k = 0; k < kOsc; ++k) V.oOff[k] = V.rng.bi();
        V.oCut = V.rng.bi();
    }
    resetVoices();
    orderCtr_ = 0;
    focus_ = 0;
    arpCtr_ = 0;
    lfoPh_ = 0.0;
    lfoRnd0_ = lfoRnd1_ = 0.0f;
    tFresh_ = true;
    noiseRng_.seed(0xA5A5A5A5U);
    lfoRng_.seed(0x5A5A5A5AU);
    dlyRng_.seed(0x3C3C3C3CU);
    pk0_ = pk1_ = pk2_ = 0.0f;
    for (int c = 0; c < 2; ++c) {
        dnSteep_[c].clear_buffers();
        dnMid_[c].clear_buffers();
    }
    smoothInit_ = false;
    peakL_ = peakR_ = 0.0f;
    clearDelay();
}

} // namespace nota
