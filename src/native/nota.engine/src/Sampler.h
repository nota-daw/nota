// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Sampler (kind 1) — classic sampler-style. Plays one sample pitched by
// MIDI note relative to a root, over a Start/End window, with a loop (off / forward /
// ping-pong, forward loops can run in reverse) and a loop crossfade, reverse, transpose+
// detune, per-voice amp ADSR and a TPT state-variable filter with optional key-tracking,
// a Poly/Mono/Choke voice mode, plus volume/pan. All the continuous controls run through
// the Instrument plugin-param interface (normalized 0..1) so they automate/persist like
// the Nota Synth. `setSample` + kind 1 stay unchanged for Drum/Instrument Rack
// compatibility; the defaults reproduce the old one-shot behavior. Params are APPEND-ONLY.
//
// The 2026-09 redesign appends: sample Gain (±24 dB before the envelope), Glide (a slide
// from the last note; in Mono it is legato — the voice keeps playing and slides, and a
// released note slides back to the one still held), pitch Keytrack (how far the key moves
// the pitch: 100 % chromatic, 0 % every key plays the root), Env → Cutoff (on/off + a
// bipolar ±6 octave depth from the amp envelope) and an Output level after the voices.
// Telemetry (scopeRead) gives the editor the loudest voice's envelope and cutoff.

#pragma once

#include "Instrument.h"
#include "SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <memory>

namespace nota {

class Sampler final : public Instrument {
public:
    enum Param { Volume = 0, Pan, Transpose, Detune, Start, End, Reverse, LoopMode,
                 LoopStart, LoopEnd, Attack, Decay, Sustain, Release, FilterType, Cutoff, Resonance,
                 VoiceMode, LoopXfade, FilterKeyTrack, VelAmount,
                 Gain, Glide, PitchTrack, EnvCutoff, EnvAmount, Output, kNumParams };

    Sampler() {
        pn_[Volume].store(1.0f);   pn_[Pan].store(0.5f);
        pn_[Transpose].store(0.5f); pn_[Detune].store(0.5f);   // 0 st / 0 cents
        pn_[Start].store(0.0f);    pn_[End].store(1.0f);
        pn_[Reverse].store(0.0f);  pn_[LoopMode].store(0.0f);
        pn_[LoopStart].store(0.0f); pn_[LoopEnd].store(1.0f);
        pn_[Attack].store(0.0f);   pn_[Decay].store(0.3f);
        pn_[Sustain].store(1.0f);  pn_[Release].store(0.06f);
        pn_[FilterType].store(0.0f); pn_[Cutoff].store(1.0f); pn_[Resonance].store(0.0f);
        pn_[VoiceMode].store(0.0f);  pn_[LoopXfade].store(0.1f); pn_[FilterKeyTrack].store(0.0f);
        pn_[VelAmount].store(1.0f);
        pn_[Gain].store(0.5f);       pn_[Glide].store(0.0f);      pn_[PitchTrack].store(1.0f);   // 0 dB · no glide · chromatic
        pn_[EnvCutoff].store(0.0f);  pn_[EnvAmount].store(0.75f); pn_[Output].store(1.0f);       // env → cutoff off (+3 oct when on)
    }

    // ---- value maps (mirrored by SamplerModel.cs) -------------------------------------
    static double gainDb(float v)       { return (std::clamp(v, 0.0f, 1.0f) - 0.5) * 48.0; }           // ±24 dB
    static double glideSeconds(float v) { return v <= 0.001f ? 0.0 : 0.001 * std::pow(2000.0, std::clamp(v, 0.0f, 1.0f)); }  // 1 ms … 2 s
    static double envOctaves(float v)   { return (std::clamp(v, 0.0f, 1.0f) - 0.5) * 12.0; }           // ±6 oct

    // ---- telemetry --------------------------------------------------------------------
    // [0] sounding voices · [1] play position 0..1 of the loudest voice (−1 silent) ·
    // [2] its envelope level 0..1 · [3] its stage (0 A · 1 D · 2 S · 3 R, −1 none) ·
    // [4] its note (fractional while gliding, −1 none) · [5] its cutoff in Hz after key
    // tracking and the envelope (−1 filter off / silent) · [6] output peak since the last
    // read, linear · [7] notes held (Mono stack depth).
    static constexpr int kTele = 8;
    int32_t scopeRead(float* out, int32_t maxN) const override {
        if (!out || maxN <= 0) return 0;
        const int n = std::min<int>(maxN, kTele);
        for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
        if (n > 6) tele_[6].store(0.0f, std::memory_order_relaxed);   // peak-hold: a read clears it
        return n;
    }

    int32_t kind() const override { return 1; }
    const char* displayName() const override { return "Nota Sampler"; }

    // --- sample (persist / Drum Rack; not a plugin-param) ------------------
    std::shared_ptr<SampleBuffer> sample() const { return sample_; }
    int32_t rootNote() const { return rootNote_.load(std::memory_order_relaxed); }
    void    setRoot(int32_t r) { rootNote_.store(std::clamp(r, 0, 127), std::memory_order_relaxed); }
    bool    loopEnabled() const { return loopModeOf() > 0; }
    void setSample(std::shared_ptr<SampleBuffer> s, int32_t rootNote, bool loop) {
        sample_ = std::move(s);
        rootNote_.store(std::clamp(rootNote, 0, 127), std::memory_order_relaxed);
        // The loop flag only corrects a disagreeing mode: on → forward (0.5) unless already
        // looping (ping-pong 1.0 stays), off → one-shot. A reload keeps the loop kind.
        if (loop != loopEnabled()) pn_[LoopMode].store(loop ? 0.5f : 0.0f, std::memory_order_relaxed);
    }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

    // Live playback position (0..1 of the sample) of the loudest active voice, or -1
    // if silent — for the UI's real-time cursor. Audio thread writes, UI reads.
    float playPosition() const { return playPos_.load(std::memory_order_relaxed); }

    // Sounding voices, for the shell's voice meter (message-thread read of a snapshot).
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // --- plugin-params (automatable) --------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = { "volume", "pan", "transpose", "detune", "start", "end", "reverse", "loopmode",
                                     "loopstart", "loopend", "attack", "decay", "sustain", "release", "filtertype", "cutoff", "resonance",
                                     "voicemode", "loopxfade", "keytrack", "velamount",
                                     "gain", "glide", "pitchtrack", "envcutoff", "envamount", "output" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        // Display names group by their head in the automation menu (Sample › Start,
        // Filter › Cutoff …); the ids are what persists, so names may change.
        static const char* nm[] = { "Out Volume", "Out Pan", "Pitch Transpose", "Pitch Detune", "Sample Start", "Sample End",
                                    "Sample Reverse", "Loop Mode", "Loop Start", "Loop End",
                                    "Env Attack", "Env Decay", "Env Sustain", "Env Release",
                                    "Filter Type", "Filter Cutoff", "Filter Resonance",
                                    "Voice Mode", "Loop Crossfade", "Filter Keytrack", "Voice Velocity",
                                    "Sample Gain", "Voice Glide", "Pitch Keytrack", "Filter Env On", "Filter Env Amount", "Out Level" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override { return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  pluginParamSet(int32_t i, float v) override { if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<Sampler>();
        s->sample_ = sample_; s->rootNote_.store(rootNote_.load(std::memory_order_relaxed), std::memory_order_relaxed);
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    void noteOn(int32_t pitch, float velocity) override {
        if (!sample_ || sample_->empty()) return;
        const int vm = voiceModeOf();
        const bool glide = pn_[Glide].load(std::memory_order_relaxed) > 0.001f;
        const double from = lastNote_ >= 0 ? static_cast<double>(lastNote_) : static_cast<double>(pitch);
        lastNote_ = pitch;
        if (vm == 1) {
            pushHeld(pitch);
            if (glide) {   // legato: the sounding voice keeps its place and envelope and slides
                for (auto& v : voices_)
                    if (v.active && v.stage != Stage::Release) { v.pitch = pitch; v.velocity = velocity; return; }
            }
            for (auto& v : voices_) if (v.active && v.stage != Stage::Release) v.stage = Stage::Release;  // Mono: release others
        } else if (vm == 2) {
            for (auto& v : voices_) v.active = false;                                                     // Choke: hard cut
        }
        Voice* v = findFreeVoice();
        const double startF = std::clamp(pn_[Start].load(std::memory_order_relaxed), 0.0f, 1.0f) * sample_->frames;
        const double endF   = std::clamp(pn_[End].load(std::memory_order_relaxed), 0.0f, 1.0f) * sample_->frames;
        const bool rev = pn_[Reverse].load(std::memory_order_relaxed) > 0.5f;
        v->active = true; v->stage = Stage::Attack; v->env = 0.0f;
        v->pitch = pitch; v->velocity = velocity; v->dir = rev ? -1 : 1;
        v->note = glide ? from : static_cast<double>(pitch);
        v->pos = rev ? std::max(startF, endF - 1.0) : startF;
        v->ic1L = v->ic2L = v->ic1R = v->ic2R = 0.0;
    }
    void noteOff(int32_t pitch) override {
        if (voiceModeOf() == 1) {
            const bool top = heldN_ > 0 && held_[heldN_ - 1] == pitch;
            popHeld(pitch);
            // Legato glide back to the note still held underneath.
            if (top && heldN_ > 0 && pn_[Glide].load(std::memory_order_relaxed) > 0.001f) {
                const int32_t back = held_[heldN_ - 1];
                for (auto& v : voices_)
                    if (v.active && v.stage != Stage::Release && v.pitch == pitch) { v.pitch = back; lastNote_ = back; return; }
            }
        }
        for (auto& v : voices_) if (v.active && v.pitch == pitch && v.stage != Stage::Release) v.stage = Stage::Release;
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; heldN_ = 0; }

    void render(float* out, int32_t frames) override {
        if (!sample_ || sample_->empty()) { publishIdle(); return; }
        const double N = static_cast<double>(sample_->frames);
        double startF = std::clamp(pn_[Start].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        double endF   = std::clamp(pn_[End].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        if (endF <= startF + 1) endF = std::min(N, startF + 1);
        double loopA = std::clamp(pn_[LoopStart].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        double loopB = std::clamp(pn_[LoopEnd].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        loopA = std::clamp(loopA, startF, endF); loopB = std::clamp(loopB, startF, endF);
        if (loopB <= loopA + 1) loopB = std::min(endF, loopA + 1);
        const int loopMode = loopModeOf();
        const double loopLen = loopB - loopA;
        // Loop crossfade length (samples), capped to half the loop — forward loop only.
        double xf = pn_[LoopXfade].load(std::memory_order_relaxed) * 0.2 * sampleRate_;
        xf = std::clamp(xf, 0.0, loopLen * 0.5);

        const int32_t root = rootNote_.load(std::memory_order_relaxed);
        const double pitchOff = (pn_[Transpose].load(std::memory_order_relaxed) - 0.5) * 48.0
                              + (pn_[Detune].load(std::memory_order_relaxed) - 0.5) * 1.0;   // ±50 cents
        const double pTrack = pn_[PitchTrack].load(std::memory_order_relaxed);
        const double srcRatio = sample_->sourceSampleRate / sampleRate_;
        const float gain = pn_[Volume].load(std::memory_order_relaxed)
                         * static_cast<float>(std::pow(10.0, gainDb(pn_[Gain].load(std::memory_order_relaxed)) / 20.0))
                         * pn_[Output].load(std::memory_order_relaxed);
        const double pan = pn_[Pan].load(std::memory_order_relaxed) * 2.0 - 1.0;
        const float gl = static_cast<float>(std::cos((pan + 1.0) * kPi / 4.0));
        const float gr = static_cast<float>(std::sin((pan + 1.0) * kPi / 4.0));

        const float atkRate = static_cast<float>(1.0 / (secOf(Attack, 0.0005, 4.0) * sampleRate_));
        const float decRate = static_cast<float>(1.0 / (secOf(Decay, 0.002, 6.0) * sampleRate_));
        const float relRate = static_cast<float>(1.0 / (secOf(Release, 0.002, 6.0) * sampleRate_));
        const float sustain = pn_[Sustain].load(std::memory_order_relaxed);
        const float velAmt  = pn_[VelAmount].load(std::memory_order_relaxed);   // 1 = full velocity→volume, 0 = flat

        // Glide: a one-pole slide of the voice's note toward its key (time ≈ to 63 %).
        const double glideSec = glideSeconds(pn_[Glide].load(std::memory_order_relaxed));
        const double glideK = glideSec > 0.0 ? 1.0 - std::exp(-static_cast<double>(kCtl) / (glideSec * sampleRate_)) : 1.0;

        // Filter (TPT SVF). Cutoff can key-track the note relative to the root and follow
        // the amp envelope; coefficients refresh every kCtl samples.
        const int fType = std::clamp(static_cast<int>(std::lround(pn_[FilterType].load(std::memory_order_relaxed) * 3.0f)), 0, 3);
        const double baseCut = expMap(pn_[Cutoff].load(std::memory_order_relaxed), 20.0, 20000.0);
        const double kTrack = pn_[FilterKeyTrack].load(std::memory_order_relaxed);
        const double k  = 2.0 - 1.9 * pn_[Resonance].load(std::memory_order_relaxed);
        const bool envOn = pn_[EnvCutoff].load(std::memory_order_relaxed) > 0.5f;
        const double envOct = envOn ? envOctaves(pn_[EnvAmount].load(std::memory_order_relaxed)) : 0.0;

        int nActive = 0;
        float peak = 0.0f;
        for (auto& v : voices_) {
            if (!v.active) continue;
            ++nActive;
            double inc = 0.0, a1 = 0.0, a2 = 0.0, a3 = 0.0;
            for (int32_t i = 0; i < frames; ++i) {
                if ((i % kCtl) == 0) {   // control rate: glide, pitch, filter coefficients
                    v.note += (v.pitch - v.note) * glideK;
                    if (std::abs(v.pitch - v.note) < 1e-4) v.note = v.pitch;
                    inc = std::pow(2.0, ((v.note - root) * pTrack + pitchOff) / 12.0) * srcRatio;
                    if (fType > 0) {
                        double fc = baseCut;
                        if (kTrack > 0.0) fc *= std::pow(2.0, kTrack * (v.note - root) / 12.0);
                        if (envOn) fc *= std::pow(2.0, envOct * v.env);
                        v.cutoff = std::clamp(fc, 20.0, sampleRate_ * 0.49);
                        const double g = std::tan(kPi * v.cutoff / sampleRate_);
                        a1 = 1.0 / (1.0 + g * (g + k)); a2 = g * a1; a3 = g * a2;
                    }
                }
                // envelope
                switch (v.stage) {
                    case Stage::Attack:  v.env += atkRate; if (v.env >= 1.0f) { v.env = 1.0f; v.stage = Stage::Decay; } break;
                    case Stage::Decay:   v.env -= decRate; if (v.env <= sustain) { v.env = sustain; v.stage = Stage::Sustain; } break;
                    case Stage::Sustain: break;
                    case Stage::Release: v.env -= relRate; if (v.env <= 0.0f) { v.env = 0.0f; v.active = false; } break;
                }
                if (!v.active) break;

                float ls, rs; readInterp(v.pos, ls, rs);
                // Forward-loop crossfade: as we approach loopB, blend in the loop head.
                if (loopMode == 1 && xf > 1.0 && v.dir > 0 && v.pos > loopB - xf) {
                    const double t = (v.pos - (loopB - xf)) / xf;   // 0..1 across the seam
                    float hl, hr; readInterp(v.pos - loopLen, hl, hr);
                    ls = static_cast<float>(ls * (1.0 - t) + hl * t);
                    rs = static_cast<float>(rs * (1.0 - t) + hr * t);
                }

                if (fType > 0) { ls = svf(v.ic1L, v.ic2L, ls, a1, a2, a3, k, fType); rs = svf(v.ic1R, v.ic2R, rs, a1, a2, a3, k, fType); }

                const float amp = v.env * (velAmt * v.velocity + (1.0f - velAmt)) * gain;
                out[i * 2]     += ls * amp * gl;
                out[i * 2 + 1] += rs * amp * gr;

                // advance + loop / end handling
                v.pos += inc * v.dir;
                if (loopMode == 1) {                 // forward loop (either direction)
                    if (v.dir > 0) { if (v.pos >= loopB) v.pos -= loopLen; }
                    else           { if (v.pos <= loopA) v.pos += loopLen; }
                } else if (loopMode == 2) {          // ping-pong
                    if (v.pos >= loopB) { v.pos = loopB - (v.pos - loopB); v.dir = -1; }
                    else if (v.pos <= loopA) { v.pos = loopA + (loopA - v.pos); v.dir = 1; }
                } else {                             // one-shot (respects reverse)
                    if (v.pos >= endF || v.pos < startF) { v.active = false; break; }
                }
            }
        }
        for (int32_t i = 0; i < frames * 2; ++i) peak = std::max(peak, std::abs(out[i]));
        // Publish the loudest active voice + the voice count for the UI.
        const Voice* best = nullptr;
        for (auto& v : voices_) if (v.active && (!best || v.env > best->env)) best = &v;
        playPos_.store(best ? static_cast<float>(best->pos / N) : -1.0f, std::memory_order_relaxed);
        activeVoices_.store(nActive, std::memory_order_relaxed);
        tele_[0].store(static_cast<float>(nActive), std::memory_order_relaxed);
        tele_[1].store(best ? static_cast<float>(best->pos / N) : -1.0f, std::memory_order_relaxed);
        tele_[2].store(best ? best->env : 0.0f, std::memory_order_relaxed);
        tele_[3].store(best ? static_cast<float>(static_cast<int>(best->stage)) : -1.0f, std::memory_order_relaxed);
        tele_[4].store(best ? static_cast<float>(best->note) : -1.0f, std::memory_order_relaxed);
        tele_[5].store(best && fType > 0 ? static_cast<float>(best->cutoff) : -1.0f, std::memory_order_relaxed);
        if (peak > tele_[6].load(std::memory_order_relaxed)) tele_[6].store(peak, std::memory_order_relaxed);
        tele_[7].store(static_cast<float>(heldN_), std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool    active = false;
        int32_t pitch = 0, dir = 1;
        double  pos = 0.0, note = 0.0, cutoff = 20000.0;
        float   velocity = 0.0f, env = 0.0f;
        double  ic1L = 0, ic2L = 0, ic1R = 0, ic2R = 0;
        Stage   stage = Stage::Attack;
    };
    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.env < q->env) q = &v;
        return q;
    }
    // Linear-interpolated stereo read at a fractional sample position.
    void readInterp(double pos, float& l, float& r) const {
        const int64_t i0 = static_cast<int64_t>(pos);
        float l0, r0, l1, r1;
        sample_->readStereo(std::clamp<int64_t>(i0, 0, sample_->frames - 1), l0, r0);
        sample_->readStereo(std::clamp<int64_t>(i0 + 1, 0, sample_->frames - 1), l1, r1);
        const double frac = pos - i0;
        l = static_cast<float>(l0 + (l1 - l0) * frac);
        r = static_cast<float>(r0 + (r1 - r0) * frac);
    }
    int loopModeOf() const { return std::clamp(static_cast<int>(std::lround(pn_[LoopMode].load(std::memory_order_relaxed) * 2.0f)), 0, 2); }
    int voiceModeOf() const { return std::clamp(static_cast<int>(std::lround(pn_[VoiceMode].load(std::memory_order_relaxed) * 2.0f)), 0, 2); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    double secOf(Param p, double lo, double hi) const { return expMap(pn_[p].load(std::memory_order_relaxed), lo, hi); }
    static float svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type) {
        const double x = xf;
        const double v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = 2.0 * v1 - ic1; ic2 = 2.0 * v2 - ic2;
        if (type == 1) return static_cast<float>(v2);                 // low-pass
        if (type == 2) return static_cast<float>(x - k * v1 - v2);    // high-pass
        return static_cast<float>(v1);                                // band-pass
    }

    void publishIdle() {
        playPos_.store(-1.0f, std::memory_order_relaxed);
        activeVoices_.store(0, std::memory_order_relaxed);
        tele_[0].store(0.0f, std::memory_order_relaxed); tele_[1].store(-1.0f, std::memory_order_relaxed);
        tele_[2].store(0.0f, std::memory_order_relaxed); tele_[3].store(-1.0f, std::memory_order_relaxed);
        tele_[4].store(-1.0f, std::memory_order_relaxed); tele_[5].store(-1.0f, std::memory_order_relaxed);
    }
    // Mono note stack (most recent last) for legato glide back to a still-held key.
    void pushHeld(int32_t p) {
        popHeld(p);
        if (heldN_ == kHeld) { std::memmove(held_, held_ + 1, sizeof(int32_t) * (kHeld - 1)); --heldN_; }
        held_[heldN_++] = p;
    }
    void popHeld(int32_t p) {
        for (int i = 0; i < heldN_; ++i)
            if (held_[i] == p) { std::memmove(held_ + i, held_ + i + 1, sizeof(int32_t) * (heldN_ - i - 1)); --heldN_; return; }
    }

    static constexpr int kCtl = 16;    // control-rate block (glide, pitch, filter coefficients)
    static constexpr int kHeld = 16;
    int32_t held_[kHeld] = {};
    int     heldN_ = 0;
    int32_t lastNote_ = -1;
    mutable std::atomic<float> tele_[kTele] = {};
    static constexpr int kVoices = 16;
    Voice  voices_[kVoices];
    std::shared_ptr<SampleBuffer> sample_;
    std::atomic<int32_t> rootNote_{60};
    double  sampleRate_ = 44100.0;
    std::atomic<float> playPos_{-1.0f};
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
