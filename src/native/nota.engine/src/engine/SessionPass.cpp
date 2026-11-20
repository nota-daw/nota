// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "SessionPass.h"

#include "MidiController.h"

#include <algorithm>
#include <cmath>

namespace nota {

namespace {
constexpr double kEps = 1e-9;
} // namespace

void SessionPass::close(PlayedSpan& s, double beat) { s.end = std::max(beat, s.start); }

void SessionPass::prune() {
    spans.erase(std::remove_if(spans.begin(), spans.end(),
                    [](const PlayedSpan& s) { return !s.open() && s.end - s.start <= kEps; }),
        spans.end());
}

void SessionPass::onEvent(const LaunchEvent& e) {
    switch (e.kind) {
    case LaunchEvent::Launch: {
        for (auto it = spans.rbegin(); it != spans.rend(); ++it)
            if (it->trackId == e.trackId && it->open()) {
                close(*it, e.beat);
                break;
            }
        if (e.slot >= 0) {
            PlayedSpan s;
            s.trackId = e.trackId;
            s.slot = e.slot;
            s.start = e.beat;
            s.slotPos = e.slotPos;
            s.gain = e.gain;
            spans.push_back(s);
        }
        break;
    }
    case LaunchEvent::Seam: {
        // The transport loop wrapped: what plays ends at the loop end and goes on from the loop
        // start, the slot's own clock running on (the Session player keeps its position).
        const size_t n = spans.size();
        for (size_t i = 0; i < n; ++i) {
            if (!spans[i].open()) continue;
            PlayedSpan next = spans[i];
            close(spans[i], e.beat);
            next.slotPos = spans[i].slotPos + (e.beat - spans[i].start);
            next.start = e.slotPos;
            spans.push_back(next);
        }
        break;
    }
    case LaunchEvent::Stop:
        closeAll(e.beat);
        stopped = true;
        return;
    }
    prune();
}

void SessionPass::closeAll(double beat) {
    for (auto& s : spans)
        if (s.open()) close(s, beat);
    prune();
}

std::vector<double> slotRepetitions(double L, bool loop, const PlayedSpan& span) {
    std::vector<double> out;
    if (!(L > kEps) || span.open() || span.end - span.start <= kEps) return out;
    const double origin = span.start - span.slotPos; // where the slot's beat 0 lies on the timeline
    if (!loop) {
        if (span.slotPos < L - kEps) out.push_back(origin);
        return out;
    }
    for (long k = static_cast<long>(std::floor(span.slotPos / L + kEps)); origin + k * L < span.end - kEps; ++k)
        out.push_back(origin + k * L);
    return out;
}

namespace {
// Points of `src` (beats local to one repetition, [0, L]) laid over every repetition and cut to the
// span, as clip-local beats of the written clip. Each repetition starts with the value at its
// start and ends with the value at its end (a hair before the next one's start, so the reset to
// the next repetition's first value is a step, as the player restarts the curve), so a ramp up to
// the loop end stays a ramp and a partial repetition keeps its shape up to where it was cut.
template <class ValueAt>
std::vector<AutomationPoint> tilePoints(const std::vector<AutomationPoint>& src, ValueAt valueAt, double L,
    const std::vector<double>& reps, const PlayedSpan& span) {
    constexpr double kNudge = 1e-6; // as settleJoins: under a sample at any tempo
    std::vector<AutomationPoint> out;
    const double len = span.end - span.start;
    for (double r : reps) {
        const double from = std::max(r, span.start), to = std::min(r + L, span.end);
        if (to - from <= kEps) continue;
        const bool last = to >= span.end - kEps;
        const double tailBeat = last ? len : to - span.start - kNudge;
        AutomationPoint head;
        head.beat = from - span.start;
        head.value = valueAt(from - r);
        out.push_back(head);
        for (const auto& p : src) {
            if (p.beat < 0.0 || p.beat > L) continue;
            const double rel = r + p.beat - span.start;
            if (rel <= head.beat + kEps || rel >= tailBeat - kEps) continue;
            AutomationPoint q = p;
            q.beat = rel;
            out.push_back(q);
        }
        if (tailBeat > head.beat + kEps) {
            AutomationPoint tail;
            tail.beat = tailBeat;
            tail.value = valueAt(to - r);
            out.push_back(tail);
        }
    }
    std::stable_sort(out.begin(), out.end(), [](const auto& a, const auto& b) { return a.beat < b.beat; });
    return out;
}

// A note's expression from `off` beats into it on, re-based so the note now starts there.
std::shared_ptr<const NoteExpr> exprFrom(const NoteExpr& e, double off) {
    auto r = std::make_shared<NoteExpr>();
    for (int32_t d = 0; d < kExprDims; ++d) {
        const auto& src = e.dim[d];
        if (src.empty()) continue;
        r->dim[d].push_back({0.0f, e.valueAt(d, off)});
        for (const auto& p : src)
            if (p.beat > off + kEps) r->dim[d].push_back({static_cast<float>(p.beat - off), p.value});
    }
    return r->empty() ? nullptr : r;
}
} // namespace

MidiClip unrollSlotMidi(const MidiClip& slot, double L, bool loop, const PlayedSpan& span) {
    MidiClip c;
    c.name = slot.name;
    c.startBeat = span.start;
    c.lengthBeats = span.open() ? 0.0 : std::max(0.0, span.end - span.start);
    c.active = true;
    const double len = c.lengthBeats;
    const auto reps = slotRepetitions(L, loop, span);
    // A note of the repetition before the first one written can still sound at the span start
    // (Record pressed, or a loop seam crossed, while it holds).
    std::vector<double> noteReps = reps;
    if (loop && !reps.empty()) noteReps.insert(noteReps.begin(), reps.front() - L);
    for (double r : noteReps)
        for (const Note& n : slot.notes) {
            if (n.startBeat < 0.0 || n.startBeat >= L) continue;
            if (r < span.start - span.slotPos - kEps) continue; // before the slot started playing
            const double on = r + n.startBeat;
            if (on >= span.end - kEps || on + n.lengthBeats <= span.start + kEps) continue;
            Note w = n;
            if (on < span.start - kEps) { // held over the span start: the part still to sound
                const double cut = span.start - on;
                w.startBeat = 0.0;
                w.lengthBeats = std::min(n.lengthBeats - cut, len);
                w.exprId = 0; // the writer stores the re-based expression
                w.expr = n.expr ? exprFrom(*n.expr, cut) : nullptr;
            } else {
                w.startBeat = std::max(0.0, on - span.start);
                w.lengthBeats = std::min(n.lengthBeats, len - w.startBeat);
            }
            w.velocity = std::clamp(n.velocity * span.gain, 0.0f, 1.0f);
            c.notes.push_back(std::move(w));
        }
    std::stable_sort(c.notes.begin(), c.notes.end(), [](const Note& a, const Note& b) { return a.startBeat < b.startBeat; });
    for (const auto& cv : slot.controllers) {
        if (!cv || cv->points.empty()) continue;
        auto pts = tilePoints(cv->points, [&](double b) { return cv->valueAt(b); }, L, reps, span);
        c.setController(cv->ctl, std::move(pts));
    }
    // The slot's velocity and volume envelopes are not copied: Session playback does not apply
    // them, and the written clip plays what was heard.
    return c;
}

int32_t sessionPassSelfTest() {
    auto near = [](double a, double b) { return std::fabs(a - b) < 1e-9; };
    {   // launch, switch, stop, zero-length drop
        SessionPass p;
        p.onEvent({LaunchEvent::Launch, 1, 0, 0.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Launch, 2, 3, 4.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Launch, 1, 1, 8.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Launch, 2, -1, 12.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Launch, 2, 2, 12.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Launch, 2, -1, 12.0, 0.0, 1.0f, 1}); // started and stopped at once: dropped
        p.closeAll(16.0);
        if (p.spans.size() != 3) return 1;
        if (p.spans[0].trackId != 1 || !near(p.spans[0].end, 8.0)) return 2;
        if (p.spans[1].trackId != 2 || p.spans[1].slot != 3 || !near(p.spans[1].start, 4.0) || !near(p.spans[1].end, 12.0)) return 3;
        if (p.spans[2].slot != 1 || !near(p.spans[2].end, 16.0)) return 4;
    }
    {   // a loop seam carries the slot position on
        SessionPass p;
        p.onEvent({LaunchEvent::Launch, 1, 0, 2.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Seam, 0, -1, 8.0, 0.0, 1.0f, 1});
        p.onEvent({LaunchEvent::Stop, 0, -1, 3.0, 0.0, 1.0f, 1});
        if (p.spans.size() != 2) return 5;
        if (!near(p.spans[0].end, 8.0) || !near(p.spans[1].start, 0.0) || !near(p.spans[1].slotPos, 6.0) || !near(p.spans[1].end, 3.0))
            return 6;
    }
    {   // unroll: a 2-beat slot with a note on 0 and 1, played from position 1 for 4 beats
        MidiClip s;
        Note a; a.pitch = 60; a.startBeat = 0.0; a.lengthBeats = 0.5; a.velocity = 0.5f;
        Note b = a; b.pitch = 62; b.startBeat = 1.0; b.lengthBeats = 3.0;
        s.notes = {a, b};
        PlayedSpan sp; sp.start = 10.0; sp.end = 14.0; sp.slotPos = 1.0; sp.gain = 2.0f;
        MidiClip c = unrollSlotMidi(s, 2.0, true, sp);
        // repetitions start at 9, 11, 13: notes at 10 (62), 11 (60), 12 (62), 13 (60) -> clip-local 0,1,2,3
        if (c.notes.size() != 4 || !near(c.lengthBeats, 4.0)) return 7;
        if (c.notes[0].pitch != 62 || !near(c.notes[0].lengthBeats, 3.0)) return 8;
        if (c.notes[2].pitch != 62 || !near(c.notes[2].lengthBeats, 2.0)) return 9; // cut at the span end
        if (!near(c.notes[1].velocity, 1.0)) return 10;                            // gain, clamped
        // one-shot: only the first pass, and nothing if the position is past it
        if (unrollSlotMidi(s, 2.0, false, sp).notes.size() != 1) return 11;
        PlayedSpan late = sp; late.slotPos = 3.0;
        if (!unrollSlotMidi(s, 2.0, false, late).notes.empty()) return 12;
    }
    {   // a note still sounding at the span start keeps its remaining part
        MidiClip s;
        Note a; a.pitch = 55; a.startBeat = 0.0; a.lengthBeats = 3.0; a.velocity = 0.5f;
        s.notes = {a};
        PlayedSpan sp; sp.start = 10.0; sp.end = 14.0; sp.slotPos = 1.0;
        MidiClip c = unrollSlotMidi(s, 4.0, true, sp);
        if (c.notes.empty() || c.notes[0].pitch != 55 || !near(c.notes[0].startBeat, 0.0) || !near(c.notes[0].lengthBeats, 2.0))
            return 13;
        if (c.notes.size() != 2 || !near(c.notes[1].startBeat, 3.0)) return 14; // the next repetition's
    }
    {   // a controller ramp up to the loop end stays a ramp, and resets at each repetition
        MidiClip s;
        s.setController(kCtlModWheel, {{0.0, 0.0f, 0.0f}, {2.0, 1.0f, 0.0f}});
        PlayedSpan sp; sp.start = 0.0; sp.end = 3.0;
        MidiClip c = unrollSlotMidi(s, 2.0, true, sp);
        const ControllerCurve* m = c.controller(kCtlModWheel);
        if (!m) return 15;
        if (std::fabs(m->valueAt(1.0) - 0.5f) > 1e-3 || m->valueAt(1.999) < 0.99f) return 16;
        if (std::fabs(m->valueAt(2.0)) > 1e-3 || std::fabs(m->valueAt(2.5) - 0.25f) > 1e-3) return 17; // reset, then the partial pass
        if (std::fabs(m->valueAt(3.0) - 0.5f) > 1e-3) return 18;
    }
    return 0;
}

} // namespace nota
