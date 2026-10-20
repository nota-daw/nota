// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RhythmVoiceDsp — the drum-voice synthesis engine behind Nota Rhythm (kind 12),
// split out of RhythmMachine.h so the sequencer/bus/FX owner stays lean. It owns the
// eight monophonic voices (synth engines + one-shot sample playback) and their noise
// generator; the parent feeds per-block parameter snapshots and sample references.
//
//   Voice engines (retriggered, monophonic): Kick (sine + pitch-drop + click), Snare (tone
//   sines + noise snap), Clap (noise bursts + tail), Rim (band-passed noise click), Closed/
//   Open Hat (HP noise — closed chokes open, 808-style), Tom (sine + pitch env), Perc (ring).
//
// The synthesis math is verbatim: oscillator phases,
// playback rates, envelopes and filter states stay std::-exact — only the downstream
// drive/saturation stages run on fast_math::tanhPrecise. The single noise stream
// (rng_) is shared with the parent's Humanize jitter through noiseF(), so the exact
// call order in the parent's render loop preserves the deterministic sequence.

#pragma once

#include "media/SampleBuffer.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

struct RhythmVoiceDsp {
    static constexpr int kVoices = 8;

    struct Voice {
        bool active = false;
        float vel = 0.0f;
        double ph = 0.0, ph2 = 0.0;
        float env = 0.0f, env2 = 0.0f; // amp + secondary (noise/tail) env
        float pitchEnv = 0.0f;
        float lp = 0.0f, lp2 = 0.0f, hp = 0.0f, hpPrev = 0.0f; // one-pole filter states (lp2: a sample's right channel)
        float bp = 0.0f, bp2 = 0.0f;                           // band-pass (SVF) states
        int burst = 0;
        float burstT = 0.0f; // clap multi-burst
        double spos = 0.0;
        bool splay = false; // sample-voice playback
        double sBeg = 0.0, sEnd = 0.0;
        bool srev = false; // the played region, direction
    };

    // Per-block parameter snapshot for one voice (copied from the parent's params;
    // floats cross by value, so the synthesis sees bit-identical inputs).
    struct Params {
        float tune = 0.35f, decay = 0.5f, punch = 0.5f, tone = 0.5f, drive = 0.15f, level = 0.8f;
        double sampleRate = 44100.0;
    };

    void trigger(
        int v, float velocity, bool isSample, const SampleBuffer* sb, float start01, float length01, bool reverse);

    // One stereo frame from a sample-voice one-shot (a stereo file keeps its image): pitch
    // (Tune) → playback rate, amp env (Decay), Tone → LP, Drive → tanh, Level. Stops at the
    // end of the region.
    void sampleVoice(int v, float& outL, float& outR, const Params& p, const SampleBuffer& b);

    void voiceStereo(int v, float& l, float& r, const Params& p, const SampleBuffer* sb);

    void allOff() {
        for (auto& vc : voices) vc.active = false;
    }

    float noiseF() {
        rng_ ^= rng_ << 13;
        rng_ ^= rng_ >> 17;
        rng_ ^= rng_ << 5;
        return (rng_ & 0xFFFFFF) / (float)0x1000000;
    }
    static float expMap(float v, float lo, float hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0f, 1.0f)); }
    // per-sample exponential decay coefficient for a given time constant (seconds to ~-60 dB).
    static float decayCoef(float seconds, double sr) {
        return std::exp(-6.9077f / (std::max(1e-4f, seconds) * (float)sr));
    }

    Voice voices[kVoices];
    uint32_t rng_ = 0x1234567u;

  private:
    static constexpr double kTwoPi = 6.283185307179586;

    // One mono sample for voice v, advancing its state. Params denormalized inline.
    float voiceSample(int v, const Params& p);
};

} // namespace nota
