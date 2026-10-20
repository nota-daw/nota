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
// Sample/grain read heads stay std::-exact — the custom
// linear-interpolated reader is kept verbatim (no shared-primitive migration).
// Silent voices auto-sleep via isIdle(); release tails fully decay before sleep.

#pragma once

#include "devices/Instrument.h"
#include "media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <memory>

namespace nota {

class Sampler final : public Instrument {
  public:
    enum Param {
        Volume = 0,
        Pan,
        Transpose,
        Detune,
        Start,
        End,
        Reverse,
        LoopMode,
        LoopStart,
        LoopEnd,
        Attack,
        Decay,
        Sustain,
        Release,
        FilterType,
        Cutoff,
        Resonance,
        VoiceMode,
        LoopXfade,
        FilterKeyTrack,
        VelAmount,
        Gain,
        Glide,
        PitchTrack,
        EnvCutoff,
        EnvAmount,
        Output,
        kNumParams
    };

    Sampler();

    // ---- value maps (mirrored by SamplerModel.cs) -------------------------------------
    static double gainDb(float v) { return (std::clamp(v, 0.0f, 1.0f) - 0.5) * 48.0; } // ±24 dB
    static double glideSeconds(float v) {
        return v <= 0.001f ? 0.0 : 0.001 * std::pow(2000.0, std::clamp(v, 0.0f, 1.0f));
    } // 1 ms … 2 s
    static double envOctaves(float v) { return (std::clamp(v, 0.0f, 1.0f) - 0.5) * 12.0; } // ±6 oct

    // ---- telemetry --------------------------------------------------------------------
    // [0] sounding voices · [1] play position 0..1 of the loudest voice (−1 silent) ·
    // [2] its envelope level 0..1 · [3] its stage (0 A · 1 D · 2 S · 3 R, −1 none) ·
    // [4] its note (fractional while gliding, −1 none) · [5] its cutoff in Hz after key
    // tracking and the envelope (−1 filter off / silent) · [6] output peak since the last
    // read, linear · [7] notes held (Mono stack depth).
    static constexpr int kTele = 8;
    int32_t scopeRead(float* out, int32_t maxN) const override;

    int32_t kind() const override { return 1; }
    const char* displayName() const override { return "Nota Sampler"; }

    // --- sample (persist / Drum Rack; not a plugin-param) ------------------
    std::shared_ptr<SampleBuffer> sample() const { return sample_; }
    int32_t rootNote() const { return rootNote_.load(std::memory_order_relaxed); }
    void setRoot(int32_t r) { rootNote_.store(std::clamp(r, 0, 127), std::memory_order_relaxed); }
    bool loopEnabled() const { return loopModeOf() > 0; }
    void setSample(std::shared_ptr<SampleBuffer> s, int32_t rootNote, bool loop);

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        wake();
    }

    // Live playback position (0..1 of the sample) of the loudest active voice, or -1
    // if silent — for the UI's real-time cursor. Audio thread writes, UI reads.
    float playPosition() const { return playPos_.load(std::memory_order_relaxed); }

    // Sounding voices, for the shell's voice meter (message-thread read of a snapshot).
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // --- plugin-params (automatable) --------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        wake();
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i)
            if (pluginParamId(i) == id) return i;
        return -1;
    }

    std::shared_ptr<Instrument> clone() const override;

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

    // Sleep contract: one-shots end, release tails decay; a looping voice stays active
    // until note-off, so sleep (no active voice + own block silent, after a hangover)
    // never cuts a tail. noteOn/noteOff/pluginParamSet/setSample wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quietSamples_ = 0;
    }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool active = false;
        int32_t pitch = 0, dir = 1;
        double pos = 0.0, note = 0.0, cutoff = 20000.0;
        float velocity = 0.0f, env = 0.0f;
        double ic1L = 0, ic2L = 0, ic1R = 0, ic2R = 0;
        Stage stage = Stage::Attack;
    };
    Voice* findFreeVoice();
    // Linear-interpolated stereo read at a fractional sample position. Kept verbatim:
    // read-head math is position-critical (an off-by-one here cost −19 dB before).
    void readInterp(double pos, float& l, float& r) const;
    int loopModeOf() const {
        return std::clamp(static_cast<int>(std::lround(pn_[LoopMode].load(std::memory_order_relaxed) * 2.0f)), 0, 2);
    }
    int voiceModeOf() const {
        return std::clamp(static_cast<int>(std::lround(pn_[VoiceMode].load(std::memory_order_relaxed) * 2.0f)), 0, 2);
    }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    double secOf(Param p, double lo, double hi) const { return expMap(pn_[p].load(std::memory_order_relaxed), lo, hi); }
    static float svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type);

    void publishIdle();
    // Mono note stack (most recent last) for legato glide back to a still-held key.
    void pushHeld(int32_t p);
    void popHeld(int32_t p);

    static constexpr int kCtl = 16; // control-rate block (glide, pitch, filter coefficients)
    static constexpr int kHeld = 16;
    int32_t held_[kHeld] = {};
    int heldN_ = 0;
    int32_t lastNote_ = -1;
    mutable std::atomic<float> tele_[kTele] = {};
    static constexpr int kVoices = 16;
    Voice voices_[kVoices];
    std::shared_ptr<SampleBuffer> sample_;
    std::atomic<int32_t> rootNote_{60};
    double sampleRate_ = 44100.0;
    std::atomic<float> playPos_{-1.0f};
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];

    // Auto-sleep hangover.
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
};

} // namespace nota
