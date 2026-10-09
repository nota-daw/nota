// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Per-note expression (MPE) for the built-in instruments. Three dimensions ride on every
// note, the MPE set: pitch bend (semitones), pressure (0..1, channel/poly aftertouch) and
// slide (0..1, CC74 "timbre", 0.5 = neutral). The engine delivers them through
// Instrument::noteExpression — keyed by pitch, or pitch −1 for the whole instrument (an
// ordinary keyboard's pitch wheel / aftertouch, an MPE master channel).
//
// A voice keeps a VoiceExpr: block-rate targets, smoothed per sample (values arrive once
// per block, so stepping them would zipper). The instrument-wide values are folded into
// the same smoother as an offset, so a voice reads one effective value per dimension.
// Values set right after the note-on (an MPE controller's initial bend/pressure/slide)
// land without a glide.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota {

enum ExprDim : int32_t { ExprBend = 0, ExprPressure = 1, ExprSlide = 2, kExprDims = 3 };

// The sustain pedal (CC64, 0 = up, 1 = down) rides the same channel, instrument-wide only
// (pitch −1) and outside the per-note set above, so a VoiceExpr never sees it. Instruments
// that don't model a damper ignore it (GlobalExpr::set drops unknown dims).
inline constexpr int32_t ExprSustain = 3;
inline constexpr int32_t kExprWireDims = 4;   // every dim the engine accepts

inline constexpr float kExprNeutral[kExprDims] = { 0.0f, 0.0f, 0.5f };

// One-pole smoothing coefficient for a ~3 ms time constant.
inline float exprSmoothCoef(double sampleRate) {
    return static_cast<float>(1.0 - std::exp(-1.0 / (0.003 * (sampleRate > 0.0 ? sampleRate : 44100.0))));
}

// The instrument-wide expression (pitch −1). The wheel is normalized −1..+1; each synth
// scales it by its own bend range (2 semitones unless the patch has a range of its own).
struct GlobalExpr {
    float wheel = 0.0f, pressure = 0.0f, slide = 0.5f;

    void set(int32_t dim, float v) {
        if (dim == ExprBend)          wheel = std::clamp(v, -1.0f, 1.0f);
        else if (dim == ExprPressure) pressure = std::clamp(v, 0.0f, 1.0f);
        else if (dim == ExprSlide)    slide = std::clamp(v, 0.0f, 1.0f);
    }
    void reset() { wheel = 0.0f; pressure = 0.0f; slide = 0.5f; }

    // The offsets a voice adds to its own values this block.
    void offsets(float bendRangeSemis, float out[kExprDims]) const {
        out[ExprBend] = wheel * bendRangeSemis;
        out[ExprPressure] = pressure;
        out[ExprSlide] = slide - 0.5f;
    }
};

struct VoiceExpr {
    float tgt[kExprDims] = { 0.0f, 0.0f, 0.5f };   // this note's own values
    float cur[kExprDims] = { 0.0f, 0.0f, 0.5f };   // smoothed effective values (own + global)
    bool  fresh = true;                            // no sample rendered since the note-on

    // A note-on: back to neutral, and the next values snap rather than glide.
    void reset(const float add[kExprDims]) {
        for (int d = 0; d < kExprDims; ++d) { tgt[d] = kExprNeutral[d]; cur[d] = kExprNeutral[d] + add[d]; }
        fresh = true;
    }
    void set(int32_t dim, float v, const float add[kExprDims]) {
        if (dim < 0 || dim >= kExprDims) return;
        tgt[dim] = v;
        if (fresh) cur[dim] = v + add[dim];
    }
    // One sample of smoothing towards own + global.
    void tick(const float add[kExprDims], float coef) {
        fresh = false;
        for (int d = 0; d < kExprDims; ++d) cur[d] += (tgt[d] + add[d] - cur[d]) * coef;
    }

    float bend() const { return cur[ExprBend]; }                                   // semitones
    float pressure() const { return std::clamp(cur[ExprPressure], 0.0f, 1.0f); }   // 0..1
    float slide() const { return std::clamp(cur[ExprSlide], 0.0f, 1.0f) * 2.0f - 1.0f; }  // −1..+1
};

} // namespace nota
