// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The articulation scheduler (specs/085, see ArticulationSchedule.h). Times here are absolute
// transport samples; a block covers [blockStart, blockStart + frames). The note rules are the ones
// gatherInstrumentNotes uses for the notes themselves, so a switch and its note agree on whether
// the note plays at all: a note at or past the clip end doesn't, one before the clip start sounds
// from the clip start, and a note's end is cut at the clip end.
//
// A clip "placement" is a clip at its place on this clock: an arrangement clip at its position, or
// one loop pass of a Session slot. The plug-in's selected articulation is one state for the whole
// track, so whether a latching point sends its switch is decided across every placement: it sends
// when the point that triggers just before it (by trigger time, then placement, then order in the
// clip) selects something else. That makes overlapping clips, per-articulation offsets that
// reorder the triggers, and a chase after a seek all agree with straight playback.

#include "ArticulationSchedule.h"
#include "TempoMap.h"

#include "MidiClip.h"

#include <algorithm>
#include <climits>
#include <cmath>
#include <limits>

namespace nota {

namespace {

constexpr double kInf = std::numeric_limits<double>::infinity();

struct Writer {
    ArtEv* out;
    int cap;
    int n = 0;
    int dropped = 0;
    int seq = 0;
    bool put(int32_t off, ArtEvKind kind, int32_t channel, int32_t d1, int32_t d2) {
        if (n >= cap) {
            ++dropped;
            return false;
        }
        out[n++] = {off, seq++, kind, static_cast<uint8_t>(channel), static_cast<uint8_t>(d1), static_cast<uint8_t>(d2)};
        return true;
    }
};

// Advance only [from, count): old entries advance before new key-ons are scheduled, then newly
// queued entries advance at block end. Due releases get buffer space before new actions. If a
// release cannot fit, keep it due at offset 0 of the next block instead of losing the held key.
void advanceReleases(Writer& w, ArticulationReleaseState& state, int32_t frames, int from = 0) {
    int kept = from;
    for (int i = from; i < state.count; ++i) {
        auto key = state.pending[i];
        if (key.remaining < frames) {
            if (w.put(std::max(0, key.remaining), ArtEvKind::KeyOff, key.channel, key.pitch, 0)) continue;
            key.remaining = 0;
        } else key.remaining -= frames;
        state.pending[kept++] = key;
    }
    state.count = kept;
}

int beginReleases(Writer& w, const ArticulationBlock& b) {
    if (!b.releases) return 0;
    advanceReleases(w, *b.releases, b.frames);
    return b.releases->count;
}

struct Window {
    double start, end; // [start, end)
    bool contains(double s) const { return s >= start && s < end; }
    int32_t offsetOf(double s) const { return static_cast<int32_t>(s - start); }
};

// An entry's actions, in map order, at `at` (absolute samples), if that falls in the block.
void sendActions(Writer& w, const ArticulationTable& table, const ArticulationEntry& e, double at, const Window& win,
    ArticulationReleaseState* releases = nullptr, double hold = 0.0) {
    if (!win.contains(at)) return;
    const int32_t off = win.offsetOf(at);
    const ArticulationAction* a = table.actionsOf(e);
    for (int32_t i = 0; i < e.actionCount; ++i) {
        const ArticulationAction& x = a[i];
        switch (x.kind) {
        case ArticulationActionKind::Keyswitch:
            if (releases && releases->count >= kMaxBlockArt) {
                ++w.dropped; // don't press a key whose release cannot be retained
                break;
            }
            if (w.put(off, ArtEvKind::KeyOn, x.channel, x.data1, x.data2) && releases)
                releases->pending[releases->count++] = {off + static_cast<int32_t>(std::lround(hold)),
                    static_cast<uint8_t>(x.channel), static_cast<uint8_t>(x.data1)};
            break;
        case ArticulationActionKind::ControlChange: w.put(off, ArtEvKind::Control, x.channel, x.data1, x.data2); break;
        case ArticulationActionKind::ProgramChange: w.put(off, ArtEvKind::Program, x.channel, x.data1, 0); break;
        case ArticulationActionKind::BankSelectMsb: w.put(off, ArtEvKind::Control, x.channel, 0, x.data2); break;
        case ArticulationActionKind::BankSelectLsb: w.put(off, ArtEvKind::Control, x.channel, 32, x.data2); break;
        }
    }
}

// Let go of the entry's keyswitch notes at `at`, if that falls in the block.
void releaseKeys(Writer& w, const ArticulationTable& table, const ArticulationEntry& e, double at, const Window& win) {
    if (!win.contains(at)) return;
    const int32_t off = win.offsetOf(at);
    const ArticulationAction* a = table.actionsOf(e);
    for (int32_t i = 0; i < e.actionCount; ++i)
        if (a[i].kind == ArticulationActionKind::Keyswitch) w.put(off, ArtEvKind::KeyOff, a[i].channel, a[i].data1, 0);
}

bool hasKey(const ArticulationTable& table, const ArticulationEntry& e, int32_t channel, int32_t pitch) {
    const ArticulationAction* a = table.actionsOf(e);
    // Playback notes currently use channel 1; channel 0 means that same note channel.
    // A default-channel action and an explicit channel-1 action hold the same physical key.
    for (int32_t i = 0; i < e.actionCount; ++i)
        if (a[i].kind == ArticulationActionKind::Keyswitch && std::max(1, a[i].channel) == std::max(1, channel) &&
            a[i].data1 == pitch) return true;
    return false;
}

bool hasKeys(const ArticulationTable& table, const ArticulationEntry& e) {
    const ArticulationAction* a = table.actionsOf(e);
    for (int32_t i = 0; i < e.actionCount; ++i)
        if (a[i].kind == ArticulationActionKind::Keyswitch) return true;
    return false;
}

// A clip at its place on this block's clock.
struct Placement {
    const MidiClip* clip;
    double start;       // samples
    double lengthBeats; // notes at or past it don't play
    long order;         // clip index or loop pass, for a total order of triggers
    bool trimEnd = true; // arrangement trims tails; Session notes may outlast a loop pass
};

// Everything a block's decisions share.
struct Ctx {
    const ArticulationTable& table;
    const ArticulationBlock& b;
    double msToS, trackShift, hold;
    double lo, hi; // the smallest and largest (offset − lead) over the table, in samples
};

Ctx makeCtx(const ArticulationTable& table, const ArticulationBlock& b) {
    Ctx c{table, b, b.sampleRate / 1000.0, 0.0, 0.0, kInf, -kInf};
    c.trackShift = b.trackOffsetMs * c.msToS;
    c.hold = kKeyswitchHoldMs * c.msToS;
    for (const ArticulationEntry& e : table.entries()) {
        const double d = (e.offsetMs - e.leadMs) * c.msToS;
        c.lo = std::min(c.lo, d);
        c.hi = std::max(c.hi, d);
    }
    if (c.lo > c.hi) c.lo = c.hi = 0.0; // an empty table
    return c;
}

// Where a note at clip-local `beat` sounds before its articulation's own offset.
double placementTime(const Ctx& c, const Placement& p, double beat) {
    return c.b.tempoMap ? c.b.tempoMap->secondsAt(p.clip->startBeat + beat) * c.b.sampleRate : p.start + beat * c.b.spb;
}
double noteTime(const Ctx& c, const Placement& p, double beat) { return placementTime(c, p, std::max(0.0, beat)) + c.trackShift; }

double trigOf(const Ctx& c, const Placement& p, const ArticulationChange& x, const ArticulationEntry& e) {
    return noteTime(c, p, x.beat) + (e.offsetMs - e.leadMs) * c.msToS;
}

struct Key {
    double trig;
    long order;
    int32_t idx;
};

bool before(const Key& a, const Key& k) {
    if (a.trig != k.trig) return a.trig < k.trig;
    if (a.order != k.order) return a.order < k.order;
    return a.idx < k.idx;
}

// Index of the first timeline point whose note time can reach `t` (samples) in placement `p`.
size_t firstFrom(const Ctx& c, const Placement& p, const std::vector<ArticulationChange>& ch, double t) {
    const double beat = c.b.tempoMap ? c.b.tempoMap->beatAt((t - c.trackShift) / c.b.sampleRate) - p.clip->startBeat : (t - p.start - c.trackShift) / c.b.spb;
    if (beat <= 0.0) return 0; // notes before the clip start sound from it
    return static_cast<size_t>(std::lower_bound(ch.begin(), ch.end(), beat,
                                   [](const ArticulationChange& x, double v) { return x.beat < v; }) -
                               ch.begin());
}

// The latching articulation in force just before key `k`: the entry of the point that triggers
// last before it, over every placement `all` gives; 0 when nothing has triggered yet.
template <class All> int32_t inForceBefore(const Ctx& c, const All& all, const Key& k) {
    Key best{-kInf, LONG_MIN, INT32_MIN};
    int32_t id = 0;
    bool found = false;
    all([&](const Placement& p) {
        const ArticulationTimeline* tl = p.clip->articulations.get();
        if (!tl) return;
        const auto& ch = tl->changes;
        // A point triggers no later than its note time + hi and no earlier than + lo: only the
        // ones whose note time is at most k − lo can be before k; scanning back, stop once even
        // + hi can't beat the best found.
        size_t i = firstFrom(c, p, ch, k.trig - c.lo + 1.0);
        while (i < ch.size() && noteTime(c, p, ch[i].beat) + c.lo <= k.trig) ++i;
        while (i-- > 0) {
            const ArticulationChange& x = ch[i];
            if (found && noteTime(c, p, x.beat) + c.hi < best.trig) break;
            if (x.beat >= p.lengthBeats) continue;
            const ArticulationEntry* e = c.table.find(x.entryId);
            if (!e) continue;
            const Key kx{trigOf(c, p, x, *e), p.order, static_cast<int32_t>(i)};
            if (!before(kx, k)) continue;
            if (!found || before(best, kx)) {
                best = kx;
                id = x.entryId;
                found = true;
            }
        }
    });
    return id;
}

// True when a momentary note other than (`self` in `selfOrder`) holds keyswitch (channel, pitch)
// down across `at`: pressed at or before it, released after it.
template <class All>
bool keyHeldAcross(const Ctx& c, const All& all, int32_t channel, int32_t pitch, double at, const Note* self, long selfOrder) {
    bool held = false;
    all([&](const Placement& p) {
        if (held) return;
        const double end = placementTime(c, p, p.lengthBeats);
        for (const Note& note : p.clip->notes) {
            if (&note == self && p.order == selfOrder) continue;
            if (note.startBeat >= p.lengthBeats || note.startBeat + note.lengthBeats <= 0.0) continue;
            const ArticulationEntry* e = c.table.resolve(note.articulation);
            if (!e || e->trigger != ArticulationTrigger::Momentary || !hasKey(c.table, *e, channel, pitch)) continue;
            const double shift = c.trackShift + e->offsetMs * c.msToS;
            const double on = placementTime(c, p, std::max(0.0, note.startBeat)) + shift - e->leadMs * c.msToS;
            const double noteEnd = placementTime(c, p, note.startBeat + note.lengthBeats);
            const double off = (p.trimEnd ? std::min(end, noteEnd) : noteEnd) + shift;
            if (on <= at && off > at) {
                held = true;
                return;
            }
        }
    });
    return held;
}

// The block's events for the placements `emit` gives, deciding against every placement `all`
// gives (a superset: for a slot, the pass before too). `chase` re-sends what is in force at the
// block's start; `covered` says whether any placement is still playing there.
template <class Emit, class All> void scheduleAll(Writer& w, const Ctx& c, const Window& win, const Emit& emit, const All& all) {
    const ArticulationBlock& b = c.b;
    bool landingSent = false; // a switch already lands on the block's first sample
    bool covered = false;

    // ---- latching: each point sends when it changes what is in force ----
    emit([&](const Placement& p) {
        const double end = placementTime(c, p, p.lengthBeats);
        if (win.start < end) covered = true;
        const ArticulationTimeline* tl = p.clip->articulations.get();
        if (!tl) return;
        const auto& ch = tl->changes;
        // Points that can act in this block: triggered in it, or released (hold later) in it.
        for (size_t i = firstFrom(c, p, ch, win.start - c.hold - c.hi); i < ch.size(); ++i) {
            const ArticulationChange& x = ch[i];
            if (x.beat >= p.lengthBeats) break; // sorted: the rest are past the clip end too
            if (noteTime(c, p, x.beat) + c.lo >= win.end) break;
            const ArticulationEntry* e = c.table.find(x.entryId);
            if (!e) continue;
            const double trig = trigOf(c, p, x, *e);
            if (trig + c.hold < win.start || trig >= win.end) continue;
            if (inForceBefore(c, all, Key{trig, p.order, static_cast<int32_t>(i)}) == x.entryId) continue; // already selected
            if (win.contains(trig) && trig < win.start + 1.0) landingSent = true;
            sendActions(w, c.table, *e, trig, win, b.releases, c.hold);
            if (!b.releases) releaseKeys(w, c.table, *e, trig + c.hold, win);
        }
        // A loop seam just passed: a key pressed before it and due for release after it (its
        // release would have come past the loop end) lets go now. Those points sit at the seam,
        // not in this block.
        if (b.seamOffs) {
            for (size_t i = firstFrom(c, p, ch, b.seamEnd - c.hold - c.hi); i < ch.size(); ++i) {
                const ArticulationChange& x = ch[i];
                if (x.beat >= p.lengthBeats || noteTime(c, p, x.beat) + c.lo >= b.seamEnd) break;
                const ArticulationEntry* e = c.table.find(x.entryId);
                if (!e) continue;
                const double trig = trigOf(c, p, x, *e);
                if (trig >= b.seamEnd || trig + c.hold < b.seamEnd) continue;
                if (inForceBefore(c, all, Key{trig, p.order, static_cast<int32_t>(i)}) == x.entryId) continue; // never sent
                releaseKeys(w, c.table, *e, win.start, win);
            }
        }
    });

    // ---- chase: retain its origin so a 20 ms release can land in a later block ----
    const double chaseStart = b.chase ? win.start : b.chaseStart;
    if (chaseStart >= 0.0 && ((b.chase && covered && !landingSent) || (!b.releases && win.contains(chaseStart + c.hold)))) {
        const int32_t id = inForceBefore(c, all, Key{chaseStart, LONG_MIN, INT32_MIN});
        // A natural switch at the landing sample supplies its own release; the old articulation
        // was never chased. Repeated points selecting the same ID are suppressed, so still chase.
        const int32_t landed = inForceBefore(c, all, Key{chaseStart + 1.0, LONG_MIN, INT32_MIN});
        if (id == landed)
            if (const ArticulationEntry* e = c.table.find(id)) {
                if (b.chase) sendActions(w, c.table, *e, chaseStart, win, b.releases, c.hold);
                if (!b.releases) releaseKeys(w, c.table, *e, chaseStart + c.hold, win);
            }
    }

    // ---- momentary: per note, held while the note sounds ----
    emit([&](const Placement& p) {
        const double end = placementTime(c, p, p.lengthBeats);
        for (const Note& note : p.clip->notes) {
            if (note.startBeat >= p.lengthBeats || note.startBeat + note.lengthBeats <= 0.0) continue;
            const ArticulationEntry* e = c.table.resolve(note.articulation);
            if (!e || e->trigger != ArticulationTrigger::Momentary) continue;
            const double shift = c.trackShift + e->offsetMs * c.msToS;
            const double onS = placementTime(c, p, std::max(0.0, note.startBeat)) + shift;
            const double noteEnd = placementTime(c, p, note.startBeat + note.lengthBeats);
            const double offS = (p.trimEnd ? std::min(end, noteEnd) : noteEnd) + shift;
            const double trig = onS - e->leadMs * c.msToS;
            sendActions(w, c.table, *e, trig, win);
            if (hasKeys(c.table, *e)) {
                // Each key lets go with its note — unless another note still holds the same key.
                if (win.contains(offS)) {
                    const ArticulationAction* a = c.table.actionsOf(*e);
                    for (int32_t k = 0; k < e->actionCount; ++k)
                        if (a[k].kind == ArticulationActionKind::Keyswitch &&
                            !keyHeldAcross(c, all, a[k].channel, a[k].data1, offS, &note, p.order))
                            w.put(win.offsetOf(offS), ArtEvKind::KeyOff, a[k].channel, a[k].data1, 0);
                }
                if (b.seamOffs && trig < b.seamEnd && offS >= b.seamEnd) releaseKeys(w, c.table, *e, win.start, win);
            }
            // A note already held where playback lands is chased by the note scheduler; its switch goes with it.
            if (b.chase && trig < win.start && offS > win.start) sendActions(w, c.table, *e, win.start, win);
        }
    });
}

int finish(Writer& w, ArtEv* out, int* dropped, const ArticulationBlock* b = nullptr, int carried = 0) {
    if (b && b->releases) advanceReleases(w, *b->releases, b->frames, carried);
    std::sort(out, out + w.n, [](const ArtEv& a, const ArtEv& c) {
        if (a.off != c.off) return a.off < c.off;
        const bool ra = a.kind == ArtEvKind::KeyOff, rc = c.kind == ArtEvKind::KeyOff;
        if (ra != rc) return ra; // releases before presses at the same sample
        return a.seq < c.seq;
    });
    if (dropped) *dropped = w.dropped;
    return w.n;
}

} // namespace

double articulationOffsetMs(const ArticulationTable* table, int32_t noteArticulation) {
    if (!table) return 0.0;
    const ArticulationEntry* e = table->resolve(noteArticulation);
    return e ? e->offsetMs : 0.0;
}

int scheduleArticulations(const std::vector<MidiClip>& clips, const ArticulationTable& table, const ArticulationBlock& b,
    ArtEv* out, int cap, int* dropped) {
    Writer w{out, cap};
    if (b.frames <= 0 || b.spb <= 0.0 || b.sampleRate <= 0.0) {
        if (dropped) *dropped = 0;
        return 0;
    }
    const Window win{b.blockStart, b.blockStart + b.frames};
    const Ctx c = makeCtx(table, b);
    const int carried = beginReleases(w, b);
    // Every active clip of the track, in its order.
    auto active = [&](auto&& f) {
        long i = 0;
        for (const MidiClip& clip : clips) {
            if (clip.active) f(Placement{&clip, b.tempoMap ? b.tempoMap->secondsAt(clip.startBeat) * b.sampleRate : clip.startBeat * b.spb, clip.lengthBeats, i});
            ++i;
        }
    };
    scheduleAll(w, c, win, active, active);
    return finish(w, out, dropped, &b, carried);
}

int scheduleSlotArticulations(const MidiClip& slotClip, double loopBeats, bool loop, const ArticulationTable& table,
    const ArticulationBlock& b, ArtEv* out, int cap, int* dropped) {
    Writer w{out, cap};
    if (b.frames <= 0 || b.spb <= 0.0 || b.sampleRate <= 0.0 || loopBeats <= 0.0) {
        if (dropped) *dropped = 0;
        return 0;
    }
    const Window win{b.blockStart, b.blockStart + b.frames};
    const Ctx c = makeCtx(table, b);
    const int carried = beginReleases(w, b);
    // The passes whose events can fall in this block: a pass's events lie between its start plus
    // the earliest trigger and its end plus the latest note shift and the hold — on a short loop
    // with a large offset, several passes away. No neighboring-pass limit: even a very short
    // loop must retain all passes whose events reach this block.
    const double loopS = loopBeats * b.spb;
    double maxShift = c.trackShift;
    for (const ArticulationEntry& e : table.entries()) maxShift = std::max(maxShift, c.trackShift + e.offsetMs * c.msToS);
    double tail = 0.0;
    for (const Note& note : slotClip.notes)
        if (note.startBeat >= 0.0 && note.startBeat < loopBeats)
            tail = std::max(tail, (note.startBeat + note.lengthBeats - loopBeats) * b.spb);
    const double late = std::max(0.0, maxShift) + std::max(c.hold, tail);
    const double early = c.trackShift + c.lo; // the earliest trigger relative to its note's grid time
    long lo = std::max(0L, static_cast<long>(std::floor((win.start - late) / loopS)) - 1);
    long hi = static_cast<long>(std::floor((win.end - std::min(0.0, early)) / loopS));
    if (!loop) hi = std::min(hi, 0L); // one-shot: a single pass
    if (hi < lo) {
        return finish(w, out, dropped, &b, carried);
    }
    auto passes = [&](long from) {
        return [&slotClip, loopS, loopBeats, from, hi](auto&& f) {
            for (long k = from; k <= hi; ++k) f(Placement{&slotClip, k * loopS, loopBeats, k, false});
        };
    };
    // Decisions look one pass further back, so a pass's first point knows what the pass before left in force.
    scheduleAll(w, c, win, passes(lo), passes(std::max(0L, lo - 1)));
    return finish(w, out, dropped, &b, carried);
}

int scheduleArticulationReleases(ArticulationReleaseState& releases, int32_t frames, ArtEv* out, int cap) {
    Writer w{out, cap};
    if (frames <= 0) return 0;
    advanceReleases(w, releases, frames);
    return finish(w, out, nullptr);
}

} // namespace nota
