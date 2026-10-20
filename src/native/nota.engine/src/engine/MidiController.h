// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Instrument-wide MIDI controllers (specs/020): the sustain pedal, mod wheel, any CC, the
// pitch wheel and channel pressure, addressed by one id (`ctl`, NOTA_CTL_* in nota_engine.h):
// 0..127 the CC, 128 the pitch wheel, 129 channel pressure. Values are normalised — a CC
// 0..1 (data / 127), the wheel −1..+1, pressure 0..1.
//
// A MIDI clip keeps one breakpoint curve per controller it uses (ControllerCurve), recorded
// with the notes or drawn in the piano roll. Curves are immutable once built and shared by
// pointer, so cloning a track (every recording poll, every undo checkpoint) copies none of
// their points. Switch controllers (CC64–69: sustain, portamento, sostenuto, soft, legato,
// hold 2) hold their value until the next point; the rest are linear between points.
//
// Inside the engine a controller travels on the expression wire (ExprEv / MidiEvent) with
// pitch −1 and dim kCtlDimBase + ctl, and reaches the instrument through
// Instrument::controller.

#pragma once

#include "Automation.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

inline constexpr int32_t kCtlPitchWheel = 128;
inline constexpr int32_t kCtlChannelPressure = 129;
inline constexpr int32_t kCtlCount = 130;
inline constexpr int32_t kCtlSustain = 64;
inline constexpr int32_t kCtlModWheel = 1;
// Controllers ride the expression wire above every per-note / instrument-wide dim.
inline constexpr int32_t kCtlDimBase = 16;

// Data entry / RPN / NRPN (their meaning comes from the messages around them) and the channel
// mode messages: never recorded, drawn or automated.
inline bool ctlIsReserved(int32_t ctl) {
    if (ctl < 0 || ctl >= kCtlCount) return true;
    return ctl == 6 || ctl == 38 || (ctl >= 96 && ctl <= 101) || (ctl >= 120 && ctl <= 127);
}
inline bool ctlIsSwitch(int32_t ctl) {
    return ctl >= 64 && ctl <= 69;
}
inline float ctlMin(int32_t ctl) {
    return ctl == kCtlPitchWheel ? -1.0f : 0.0f;
}
inline float ctlClamp(int32_t ctl, float v) {
    return std::clamp(v, ctlMin(ctl), 1.0f);
}
// Where a controller rests: pedal up, wheel centred, nothing pressed.
inline constexpr float kCtlRest = 0.0f;

struct ControllerCurve {
    int32_t ctl = kCtlSustain;
    std::vector<AutomationPoint> points; // sorted by beat (clip-local, may be < 0), one per beat

    // Held before the first and after the last point; a switch takes the last point at or
    // before `beat`, the rest interpolate with the automation curve shaping.
    float valueAt(double beat) const;
    // Whether the curve moves anywhere in [b0, b1) (a linear stretch between differing points).
    bool movesIn(double b0, double b1) const;
};

// Sort by beat and keep the last point given for any one beat.
void normalizeCtlPoints(std::vector<AutomationPoint>& p);

// A switch keeps only its changes.
inline std::vector<AutomationPoint> dedupeSwitch(const std::vector<AutomationPoint>& p) {
    std::vector<AutomationPoint> out;
    for (const auto& x : p)
        if (out.empty() || out.back().value != x.value) out.push_back(x);
    return out;
}

// Ramer–Douglas–Peucker over breakpoints (first and last always kept): the fewest points that
// stay within `tol` of a block-rate recording — the shape the gesture had.
std::vector<AutomationPoint> thinCtlCurve(const std::vector<AutomationPoint>& p, float tol);

// Recording tolerances: about one 7-bit step for a CC, ~1% of the wheel's travel — the capture
// is block-timed, so a finer tolerance would only keep the block grid's jitter.
inline float ctlThinTolerance(int32_t ctl) {
    return ctl == kCtlPitchWheel ? 0.012f : 0.008f;
}
// A replaced range meets the curve around it in a step: the old value holds up to a hair before
// the range (and the range's last value up to a hair before the old curve takes over again).
inline constexpr double kCtlJunction = 1.0 / 1024.0; // beats

// The curve cut to [from, to) and shifted by -from, with the value at each edge pinned so the
// piece sounds the same (split / slice / slot hand-off). Before the curve's first point nothing
// is pinned: there the controller keeps what it had.
std::vector<AutomationPoint> sliceCtlPoints(const ControllerCurve& c, double from, double to);

} // namespace nota
