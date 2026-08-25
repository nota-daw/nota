// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Rhythm — the built-in drum machine (instrument kind 12). A self-contained 16-step
// groovebox: eight drum voices, each a compact synth engine (electronic drum generation —
// analog/FM kick, noise snare, metal hats…), programmed on an internal 16-step sequencer
// that plays synced to the DAW transport. Incoming MIDI notes also trigger the mapped voice
// live (finger-drumming). Phase 1 is synth-only; per-voice samples are a later phase.
//
//   Voice engines (retriggered, monophonic): Kick (sine + pitch-drop + click), Snare (tone
//   sines + noise snap), Clap (noise bursts + tail), Rim (band-passed noise click), Closed/
//   Open Hat (HP noise — closed chokes open, 808-style), Tom (sine + pitch env), Perc (ring).
//
//   Sequencer: setTransport() feeds the beat clock; a 1/16 step = 0.25 beat, 16 steps = 1 bar.
//   Swing delays odd 16ths; Humanize jitters timing + velocity. Four pattern banks (A–D).
//
// The 7 per-voice knobs (Tune/Decay/Punch/Tone/Drive/Level/Pan) + 4 globals ride the plugin-
// param interface (normalized 0..1) → automation / persist / clone for free. The step
// patterns are structural state (getState/setState blob + the action() UI channel), not
// params. Header-only, allocation-free after construction. Name & DSP are Nota's own.

#pragma once

#include "Instrument.h"
#include "SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class RhythmMachine final : public Instrument {
public:
    static constexpr int kVoices = 8;
    static constexpr int kSteps = 16;
    static constexpr int kBanks = 4;
    static constexpr int kPV = 7;                       // params per voice
    static constexpr int kNumParams = kVoices * kPV + 4; // + Swing, Humanize, Accent, Volume

    enum VP { Tune = 0, Decay, Punch, Tone, Drive, Level, Pan };
    enum GP { Swing = kVoices * kPV, Humanize, Accent, Volume };
    enum Scope { S_Step = 0, S_Flash0, kScopeN = S_Flash0 + kVoices };

    // action() ids (UI editing channel).
    enum Act { A_ToggleStep = 0, A_SetVel, A_ToggleAccent, A_SelectBank, A_ClearBank, A_SelectVoice, A_SetSource };
    enum Source { Synth = 0, Sample = 1 };

    RhythmMachine() {
        // Sensible 808-ish defaults per voice.
        for (int v = 0; v < kVoices; ++v) {
            setP(v, Tune, 0.35f); setP(v, Decay, 0.5f); setP(v, Punch, 0.5f);
            setP(v, Tone, 0.5f); setP(v, Drive, 0.15f); setP(v, Level, 0.8f); setP(v, Pan, 0.5f);
        }
        // Per-voice character tweaks.
        setP(0, Tune, 0.22f); setP(0, Decay, 0.55f);   // Kick — low, long
        setP(4, Decay, 0.14f);                          // Closed hat — short
        setP(5, Decay, 0.5f);                           // Open hat — long
        pn_[Swing].store(0.0f); pn_[Humanize].store(0.0f); pn_[Accent].store(0.7f); pn_[Volume].store(0.8f);
        clearAll();
        // A minimal four-on-the-floor so a fresh instance makes a sound.
        for (int s = 0; s < 16; s += 4) { on_[0][0][s] = 1; vel_[0][0][s] = 200; }
        on_[0][4][2] = on_[0][4][6] = on_[0][4][10] = on_[0][4][14] = 1;   // closed hat off-beats
        vel_[0][4][2] = vel_[0][4][6] = vel_[0][4][10] = vel_[0][4][14] = 150;
    }

    int32_t kind() const override { return 12; }
    const char* displayName() const override { return "Nota Rhythm"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

    void setTransport(double beatStart, double spb, bool playing) override {
        if (spb > 0.0) spb_ = spb;
        beatStart_ = beatStart; playing_ = playing;
    }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i >= kVoices * kPV) { static const char* g[] = {"swing","humanize","accent","volume"}; return g[i - kVoices * kPV]; }
        static const char* pid[] = {"tune","decay","punch","tone","drive","level","pan"};
        return "v" + std::to_string(i / kPV) + "_" + pid[i % kPV];
    }
    std::string pluginParamName(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i >= kVoices * kPV) { static const char* g[] = {"Swing","Humanize","Accent","Volume"}; return g[i - kVoices * kPV]; }
        static const char* pn[] = {"Tune","Decay","Punch","Tone","Drive","Level","Pan"};
        return std::string(kVoiceNames[i / kPV]) + " " + pn[i % kPV];
    }
    float pluginParamGet(int32_t i) const override { return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f; }
    void pluginParamSet(int32_t i, float v) override { if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- UI editing channel ----------------------------------------------
    void action(int32_t id, int32_t iarg, float farg) override {
        switch (id) {
            case A_ToggleStep: { int v = iarg / kSteps, s = iarg % kSteps; if (valid(v, s)) { uint8_t& o = on_[bank_][v][s]; o = o ? 0 : 1; if (o && vel_[bank_][v][s] == 0) vel_[bank_][v][s] = 180; } break; }
            case A_SetVel:     { int v = iarg / kSteps, s = iarg % kSteps; if (valid(v, s)) vel_[bank_][v][s] = (uint8_t)std::clamp((int)std::lround(farg * 255.0f), 0, 255); break; }
            case A_ToggleAccent: { int v = iarg / kSteps, s = iarg % kSteps; if (valid(v, s)) acc_[bank_][v][s] = acc_[bank_][v][s] ? 0 : 1; break; }
            case A_SelectBank:  if (iarg >= 0 && iarg < kBanks) bank_ = (uint8_t)iarg; break;
            case A_ClearBank:   { int b = (iarg >= 0 && iarg < kBanks) ? iarg : bank_; for (int v = 0; v < kVoices; ++v) for (int s = 0; s < kSteps; ++s) { on_[b][v][s] = 0; acc_[b][v][s] = 0; } break; }
            case A_SelectVoice: if (iarg >= 0 && iarg < kVoices) selVoice_ = (uint8_t)iarg; break;
            case A_SetSource:   if (iarg >= 0 && iarg < kVoices) src_[iarg] = (farg > 0.5f && smp_[iarg]) ? (uint8_t)Sample : (uint8_t)Synth; break;
            default: break;
        }
    }

    // ---- per-voice samples (Phase 2) — message thread; the engine clones before load ------
    void setVoiceSample(int v, std::shared_ptr<SampleBuffer> b) {
        if (v < 0 || v >= kVoices) return;
        smp_[v] = std::move(b);
        src_[v] = smp_[v] ? (uint8_t)Sample : (uint8_t)Synth;
    }
    int32_t voiceSource(int v) const { return (v >= 0 && v < kVoices) ? src_[v] : 0; }
    int64_t voiceSampleId(int v) const { return (v >= 0 && v < kVoices && smp_[v]) ? smp_[v]->id : 0; }
    std::shared_ptr<SampleBuffer> voiceSampleBuf(int v) const { return (v >= 0 && v < kVoices) ? smp_[v] : nullptr; }

    // ---- project state ----------------------------------------------------
    // Layout: [u32 magic]['kNumParams' floats]['bank','sel',2 reserved]['on,vel,acc' per bank/voice/step].
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b;
        b.reserve(8 + kNumParams * 4 + kBanks * kVoices * kSteps * 3);
        auto putU32 = [&](uint32_t v) { for (int i = 0; i < 4; ++i) b.push_back((uint8_t)(v >> (i * 8))); };
        putU32(kMagic);
        for (int i = 0; i < kNumParams; ++i) { float v = pn_[i].load(std::memory_order_relaxed); uint8_t t[4]; std::memcpy(t, &v, 4); b.insert(b.end(), t, t + 4); }
        b.push_back(bank_); b.push_back(selVoice_); b.push_back(0); b.push_back(0);
        for (int bk = 0; bk < kBanks; ++bk) for (int v = 0; v < kVoices; ++v) for (int s = 0; s < kSteps; ++s) { b.push_back(on_[bk][v][s]); b.push_back(vel_[bk][v][s]); b.push_back(acc_[bk][v][s]); }
        for (int v = 0; v < kVoices; ++v) b.push_back(src_[v]);   // per-voice source (buffers persist separately)
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size < 4) return;
        int off = 0;
        uint32_t magic = 0; for (int i = 0; i < 4; ++i) magic |= (uint32_t)data[i] << (i * 8);
        off = 4;
        if (magic != kMagic) return;   // unknown — keep defaults
        for (int i = 0; i < kNumParams && off + 4 <= size; ++i, off += 4) { float v; std::memcpy(&v, data + off, 4); if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
        if (off + 2 <= size) { bank_ = (uint8_t)std::min<int>(data[off], kBanks - 1); selVoice_ = (uint8_t)std::min<int>(data[off + 1], kVoices - 1); off += 4; }
        for (int bk = 0; bk < kBanks; ++bk) for (int v = 0; v < kVoices; ++v) for (int s = 0; s < kSteps; ++s) {
            if (off + 3 > size) return;
            on_[bk][v][s] = data[off]; vel_[bk][v][s] = data[off + 1]; acc_[bk][v][s] = data[off + 2]; off += 3;
        }
        for (int v = 0; v < kVoices; ++v) if (off < size) src_[v] = data[off++];   // source flags (buffers reloaded by the project)
    }
    std::shared_ptr<Instrument> clone() const override {
        auto r = std::make_shared<RhythmMachine>();
        for (int i = 0; i < kNumParams; ++i) r->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        std::memcpy(r->on_, on_, sizeof(on_)); std::memcpy(r->vel_, vel_, sizeof(vel_)); std::memcpy(r->acc_, acc_, sizeof(acc_));
        r->bank_ = bank_; r->selVoice_ = selVoice_;
        for (int v = 0; v < kVoices; ++v) { r->smp_[v] = smp_[v]; r->src_[v] = src_[v]; }   // share sample buffers (immutable)
        r->setSampleRate(sampleRate_);
        return r;
    }

    int32_t scopeRead(float* out, int32_t maxN) const override {
        const int n = std::min(maxN, (int)kScopeN);
        for (int i = 0; i < n; ++i) out[i] = scope_[i].load(std::memory_order_relaxed);
        return n;
    }

    // ---- note events (live finger-drumming) -------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        for (int v = 0; v < kVoices; ++v) if (kMidiMap[v] == pitch) { trigger(v, velocity); return; }
    }
    void noteOff(int32_t) override {}
    void allNotesOff() override { for (auto& vc : voices_) vc.active = false; }

    void render(float* out, int32_t frames) override {
        const float swing = get(Swing), human = get(Humanize);
        const float accentG = 1.0f + get(Accent) * 1.0f;   // up to +6 dB-ish
        const float vol = get(Volume) * 1.2f;
        const double invSr = 1.0 / sampleRate_;
        const double stepBeats = 0.25;   // 1/16

        int flash[kVoices] = {0};
        int lastStep = curStep_;
        for (int32_t i = 0; i < frames; ++i) {
            // --- sequencer clock ---
            if (playing_ && spb_ > 0.0) {
                double beat = beatStart_ + (double)i * invSr * (sampleRate_ / spb_) * spb_ * invSr; // = beatStart_ + i/spb_
                beat = beatStart_ + (double)i / spb_;
                double pos = beat / stepBeats;              // in 16th-steps
                long absStep = (long)std::floor(pos);
                int barStep = (int)(((absStep % kSteps) + kSteps) % kSteps);
                double frac = pos - std::floor(pos);        // 0..1 within the step
                double trigFrac = (barStep & 1) ? (double)swing * 0.5 : 0.0;   // swing delays odd 16ths
                if (absStep != lastAbsStep_) { lastAbsStep_ = absStep; stepArmed_ = true; }
                if (stepArmed_ && frac >= trigFrac) {
                    stepArmed_ = false; curStep_ = barStep;
                    for (int v = 0; v < kVoices; ++v) if (on_[bank_][v][barStep]) {
                        float vv = vel_[bank_][v][barStep] / 255.0f;
                        if (acc_[bank_][v][barStep]) vv = std::min(1.0f, vv * accentG);
                        if (human > 0.0f) vv = std::clamp(vv + (noiseF() * 2.0f - 1.0f) * human * 0.25f, 0.0f, 1.0f);
                        trigger(v, vv);
                        flash[v] = 1;
                    }
                }
            } else {
                curStep_ = -1;
            }

            // --- synth voices ---
            float l = 0.0f, r = 0.0f;
            for (int v = 0; v < kVoices; ++v) {
                if (!voices_[v].active) continue;
                float s = voiceSample(v);
                if (!std::isfinite(s)) { s = 0.0f; voices_[v].active = false; }
                const float pan = std::clamp(get2(v, Pan) * 2.0f - 1.0f, -1.0f, 1.0f);
                const float gl = (pan <= 0.0f ? 1.0f : 1.0f - pan);
                const float gr = (pan >= 0.0f ? 1.0f : 1.0f + pan);
                l += s * gl; r += s * gr;
            }
            out[i * 2]     += l * vol;
            out[i * 2 + 1] += r * vol;
        }

        // publish telemetry
        scope_[S_Step].store((float)(playing_ ? curStep_ : -1), std::memory_order_relaxed);
        for (int v = 0; v < kVoices; ++v) {
            float f = scope_[S_Flash0 + v].load(std::memory_order_relaxed) * 0.6f;
            if (flash[v]) f = 1.0f;
            scope_[S_Flash0 + v].store(f, std::memory_order_relaxed);
        }
        (void)lastStep;
    }

private:
    static constexpr uint32_t kMagic = 0x31485452;   // "RTH1"
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr const char* kVoiceNames[kVoices] = { "Kick", "Snare", "Clap", "Rim", "Closed Hat", "Open Hat", "Tom", "Perc" };
    static constexpr int kMidiMap[kVoices] = { 36, 38, 39, 37, 42, 46, 45, 41 };

    struct Voice {
        bool active = false;
        float vel = 0.0f;
        double ph = 0.0, ph2 = 0.0;
        float env = 0.0f, env2 = 0.0f;     // amp + secondary (noise/tail) env
        float pitchEnv = 0.0f;
        float lp = 0.0f, hp = 0.0f, hpPrev = 0.0f;   // one-pole filter states
        float bp = 0.0f, bp2 = 0.0f;                 // band-pass (SVF) states
        int   burst = 0; float burstT = 0.0f;        // clap multi-burst
        double spos = 0.0; bool splay = false;       // sample-voice playback
    };

    // --- helpers -----------------------------------------------------------
    void  setP(int v, int p, float val) { pn_[v * kPV + p].store(val, std::memory_order_relaxed); }
    float get(int i) const { return pn_[i].load(std::memory_order_relaxed); }
    float get2(int v, int p) const { return pn_[v * kPV + p].load(std::memory_order_relaxed); }
    static bool valid(int v, int s) { return v >= 0 && v < kVoices && s >= 0 && s < kSteps; }
    void clearAll() { std::memset(on_, 0, sizeof(on_)); std::memset(vel_, 0, sizeof(vel_)); std::memset(acc_, 0, sizeof(acc_)); }
    float noiseF() { rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5; return (rng_ & 0xFFFFFF) / (float)0x1000000; }
    static float expMap(float v, float lo, float hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0f, 1.0f)); }
    // per-sample exponential decay coefficient for a given time constant (seconds to ~-60 dB).
    float decayCoef(float seconds) const { return std::exp(-6.9077f / (std::max(1e-4f, seconds) * (float)sampleRate_)); }

    void trigger(int v, float velocity) {
        Voice& vc = voices_[v];
        vc.active = true; vc.vel = std::clamp(velocity, 0.0f, 1.0f);
        vc.ph = 0.0; vc.ph2 = 0.0; vc.env = 1.0f; vc.env2 = 1.0f; vc.pitchEnv = 1.0f;
        vc.burst = 0; vc.burstT = 0.0f;
        vc.splay = (src_[v] == Sample && smp_[v] && !smp_[v]->empty());
        vc.spos = 0.0; vc.lp = 0.0f;
        if (v == 4) voices_[5].active = false;   // closed hat chokes open hat (808)
    }

    // One mono sample from a sample-voice one-shot: pitch (Tune) → playback rate, amp env
    // (Decay), Tone → LP, Drive → tanh, Level. Stops at the end of the buffer.
    float sampleVoice(int v) {
        Voice& vc = voices_[v];
        auto& b = *smp_[v];
        const float tune = get2(v, Tune), decay = get2(v, Decay), tone = get2(v, Tone), drive = get2(v, Drive);
        float l, r; b.readStereo((int64_t)vc.spos, l, r);
        float s = 0.5f * (l + r);
        const double rate = (b.sourceSampleRate > 0 ? b.sourceSampleRate / sampleRate_ : 1.0) * std::exp2((tune - 0.5) * 2.0);
        vc.spos += rate;
        // Tone as a gentle one-pole low-pass (0 = dark, 1 = open).
        const float lpc = 0.05f + tone * 0.95f;
        vc.lp += lpc * (s - vc.lp); s = vc.lp;
        if (drive > 0.001f) s = std::tanh(s * (1.0f + drive * 3.0f));
        vc.env *= decayCoef(expMap(decay, 0.05f, 2.0f));
        if (vc.spos >= (double)b.frames || (vc.env < 1e-4f)) { vc.active = false; vc.splay = false; }
        return s * vc.env * vc.vel * get2(v, Level);
    }

    // One mono sample for voice v, advancing its state. Params denormalized inline.
    float voiceSample(int v) {
        Voice& vc = voices_[v];
        if (vc.splay) return sampleVoice(v);   // sample-source voice (Phase 2)
        const float tune = get2(v, Tune), decay = get2(v, Decay), punch = get2(v, Punch);
        const float tone = get2(v, Tone), drive = get2(v, Drive), level = get2(v, Level);
        const double invSr = 1.0 / sampleRate_;
        float out = 0.0f;

        switch (v) {
            case 0: case 6: {   // Kick / Tom — sine with a pitch drop + click
                const float base = (v == 0) ? expMap(tune, 30.0f, 120.0f) : expMap(tune, 80.0f, 300.0f);
                const float pdrop = (v == 0) ? 3.0f : 2.0f;
                float f = base * (1.0f + vc.pitchEnv * pdrop);
                vc.ph += (double)f * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                float s = (float)std::sin(kTwoPi * vc.ph);
                float click = (vc.env > 0.6f ? (noiseF() * 2.0f - 1.0f) : 0.0f) * punch * 0.6f;
                s = std::tanh((s + click) * (1.0f + drive * 4.0f));
                vc.pitchEnv *= decayCoef(0.03f + 0.05f * tune);
                vc.env *= decayCoef(expMap(decay, 0.06f, 1.2f));
                out = s * vc.env * vc.vel;
                break;
            }
            case 1: {   // Snare — two tone sines + noise snap
                float f1 = expMap(tune, 140.0f, 330.0f);
                vc.ph += (double)f1 * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                vc.ph2 += (double)(f1 * 1.6f) * invSr; if (vc.ph2 >= 1.0) vc.ph2 -= 1.0;
                float body = 0.5f * ((float)std::sin(kTwoPi * vc.ph) + (float)std::sin(kTwoPi * vc.ph2));
                float n = noiseF() * 2.0f - 1.0f;
                vc.hp = 0.85f * (vc.hp + n - vc.hpPrev); vc.hpPrev = n;   // high-passed noise
                float mix = (1.0f - tone) * body + tone * vc.hp;         // tone = body↔noise
                mix = std::tanh(mix * (1.0f + drive * 3.0f));
                vc.env *= decayCoef(expMap(decay, 0.05f, 0.5f) * (0.4f + 0.6f * (1.0f - punch)));
                vc.env2 *= decayCoef(0.02f);
                out = (mix * vc.env + n * vc.env2 * punch * 0.5f) * vc.vel;
                break;
            }
            case 2: {   // Clap — three noise bursts + a short tail
                vc.burstT -= (float)invSr;
                if (vc.burst < 3 && vc.burstT <= 0.0f) { vc.env = 1.0f; vc.burst++; vc.burstT = 0.012f; }
                float n = noiseF() * 2.0f - 1.0f;
                vc.bp += (float)(expMap(tune, 700.0f, 1800.0f) * invSr) * (n - vc.bp);   // colour
                float s = std::tanh(vc.bp * (1.0f + drive * 3.0f));
                vc.env *= decayCoef((vc.burst < 3) ? 0.01f : expMap(decay, 0.05f, 0.4f));
                out = s * vc.env * vc.vel * 2.0f;
                break;
            }
            case 3: {   // Rim — band-passed noise click
                float n = noiseF() * 2.0f - 1.0f + (vc.env > 0.7f ? 1.0f : 0.0f);
                float g = std::tan((float)(3.14159265f * expMap(tune, 900.0f, 2600.0f) * invSr));
                float a1 = 1.0f / (1.0f + g * (g + 0.6f));
                float hp = (n - (0.6f + g) * vc.bp - vc.bp2) * a1;
                float bpv = g * hp + vc.bp; vc.bp = g * hp + bpv;
                float lp = g * bpv + vc.bp2; vc.bp2 = g * bpv + lp;
                vc.env *= decayCoef(expMap(decay, 0.02f, 0.12f));
                out = std::tanh(bpv * (1.5f + drive * 3.0f)) * vc.env * vc.vel;
                break;
            }
            case 4: case 5: {   // Closed / Open Hat — high-passed metallic noise
                // a cluster of square oscillators → metallic; then high-pass.
                float metal = 0.0f;
                static const float rat[6] = { 1.0f, 1.34f, 1.61f, 1.93f, 2.44f, 2.77f };
                float baseF = expMap(tune, 320.0f, 900.0f);
                vc.ph += (double)(baseF) * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                for (int k = 0; k < 6; ++k) metal += ((std::fmod(vc.ph * rat[k], 1.0) < 0.5) ? 1.0f : -1.0f);
                metal *= 1.0f / 6.0f;
                float n = noiseF() * 2.0f - 1.0f;
                float sig = (1.0f - tone) * metal + tone * n;
                vc.hp = 0.93f * (vc.hp + sig - vc.hpPrev); vc.hpPrev = sig;   // bright HP
                float dcy = (v == 4) ? expMap(decay, 0.02f, 0.16f) : expMap(decay, 0.08f, 0.7f);
                vc.env *= decayCoef(dcy);
                out = std::tanh(vc.hp * (1.0f + drive * 2.0f)) * vc.env * vc.vel * 0.7f;
                break;
            }
            default: {   // 7 Perc — ring/FM metallic
                float fc = expMap(tune, 200.0f, 1200.0f);
                vc.ph += (double)fc * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                vc.ph2 += (double)(fc * (1.0f + tone * 2.5f)) * invSr; if (vc.ph2 >= 1.0) vc.ph2 -= 1.0;
                float s = (float)std::sin(kTwoPi * vc.ph) * (float)std::sin(kTwoPi * vc.ph2);   // ring mod
                s = std::tanh(s * (1.0f + drive * 3.0f) + punch * (noiseF() * 2.0f - 1.0f) * (vc.env > 0.7f ? 0.5f : 0.0f));
                vc.env *= decayCoef(expMap(decay, 0.05f, 0.6f));
                out = s * vc.env * vc.vel;
                break;
            }
        }
        if (vc.env < 1e-4f && vc.env2 < 1e-4f) vc.active = false;
        return out * level;
    }

    double sampleRate_ = 44100.0;
    double spb_ = 22050.0, beatStart_ = 0.0;
    bool   playing_ = false;
    long   lastAbsStep_ = -999999;
    bool   stepArmed_ = false;
    int    curStep_ = -1;
    uint32_t rng_ = 0x1234567u;

    Voice voices_[kVoices];
    uint8_t on_[kBanks][kVoices][kSteps];
    uint8_t vel_[kBanks][kVoices][kSteps];
    uint8_t acc_[kBanks][kVoices][kSteps];
    uint8_t bank_ = 0, selVoice_ = 0;
    std::shared_ptr<SampleBuffer> smp_[kVoices];   // per-voice sample (Phase 2); null = synth
    uint8_t src_[kVoices] = {0};                    // per-voice source: 0 Synth, 1 Sample

    std::atomic<float> scope_[kScopeN];
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
