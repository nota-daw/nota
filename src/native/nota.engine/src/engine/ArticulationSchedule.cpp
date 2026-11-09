// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The articulation scheduler (specs/085, see ArticulationSchedule.h). Times here are absolute
// transport samples; a block covers [blockStart, blockStart + frames). The note rules are the ones
// gatherInstrumentNotes uses for the notes themselves, so a switch and its note agree on whether
// the note plays at all: a note at or past the clip end doesn't, one before the clip start sounds
// from the clip start, and a note's end is cut at the clip end.

#include "ArticulationSchedule.h"

#include "MidiClip.h"

#include <algorithm>
#include <cmath>

namespace nota {

namespace {

struct Writer {
    ArtEv* out;
    int cap;
    int n = 0;
    int dropped = 0;
    int seq = 0;
    void put(int32_t off, ArtEvKind kind, int32_t channel, int32_t d1, int32_t d2) {
        if (n >= cap) {
            ++dropped;
            return;
        }
        out[n++] = {off, seq++, kind, static_cast<uint8_t>(channel), static_cast<uint8_t>(d1), static_cast<uint8_t>(d2)};
    }
};

struct Window {
    double start, end; // [start, end)
    bool contains(double s) const { return s >= start && s < end; }
    int32_t offsetOf(double s) const { return static_cast<int32_t>(s - start); }
};

// An entry's actions, in map order, at `at` (absolute samples), if that falls in the block.
void sendActions(Writer& w, const ArticulationTable& table, const ArticulationEntry& e, double at, const Window& win) {
    if (!win.contains(at)) return;
    const int32_t off = win.offsetOf(at);
    const ArticulationAction* a = table.actionsOf(e);
    for (int32_t i = 0; i < e.actionCount; ++i) {
        const ArticulationAction& x = a[i];
        switch (x.kind) {
        case ArticulationActionKind::Keyswitch: w.put(off, ArtEvKind::KeyOn, x.channel, x.data1, x.data2); break;
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

bool hasKeys(const ArticulationTable& table, const ArticulationEntry& e) {
    const ArticulationAction* a = table.actionsOf(e);
    for (int32_t i = 0; i < e.actionCount; ++i)
        if (a[i].kind == ArticulationActionKind::Keyswitch) return true;
    return false;
}

} // namespace

double articulationOffsetMs(const ArticulationTable* table, int32_t noteArticulation) {
    if (!table) return 0.0;
    const ArticulationEntry* e = table->resolve(noteArticulation);
    return e ? e->offsetMs : 0.0;
}

namespace {

// One pass of one clip: the clip's notes and timeline placed at `clipStart` (samples) and played
// for `lengthBeats` — the arrangement's own position, or one loop pass of a Session slot.
void scheduleClip(Writer& w, const MidiClip& clip, const ArticulationTable& table, const ArticulationBlock& b,
    const Window& win, double clipStart, double lengthBeats) {
    const double msToS = b.sampleRate / 1000.0;
    const double trackShift = b.trackOffsetMs * msToS;
    const double hold = kKeyswitchHoldMs * msToS;
    const double clipEnd = clipStart + lengthBeats * b.spb;
    {
        // ---- latching: the clip's change points ----
        if (const ArticulationTimeline* tl = clip.articulations.get()) {
            auto trigOf = [&](const ArticulationChange& c, const ArticulationEntry& e) {
                const double eff = clipStart + std::max(0.0, c.beat) * b.spb + trackShift + e.offsetMs * msToS;
                return eff - e.leadMs * msToS;
            };
            // Chase first, so a change due in this block still wins: the state in force where
            // playback lands (the last change already past), unless the clip is over or a change
            // lands right on the block's first sample anyway.
            if (b.chase && win.start < clipEnd) {
                const ArticulationEntry* chased = nullptr;
                bool landsOnStart = false;
                for (const ArticulationChange& c : tl->changes) {
                    if (c.beat >= lengthBeats) break;
                    const ArticulationEntry* e = table.find(c.entryId);
                    if (!e) continue;
                    const double trig = trigOf(c, *e);
                    if (trig < win.start) chased = e;
                    else if (trig < win.start + 1.0) landsOnStart = true;
                }
                if (chased && !landsOnStart) {
                    sendActions(w, table, *chased, win.start, win);
                    releaseKeys(w, table, *chased, win.start + hold, win);
                }
            }
            for (const ArticulationChange& c : tl->changes) {
                if (c.beat >= lengthBeats) break; // sorted: the rest are past the clip end too
                const ArticulationEntry* e = table.find(c.entryId);
                if (!e) continue;
                const double trig = trigOf(c, *e);
                sendActions(w, table, *e, trig, win);
                releaseKeys(w, table, *e, trig + hold, win);
                // A key pressed before a loop seam and due for release after it: let go now.
                if (b.seamOffs && trig < b.seamEnd && trig + hold >= b.seamEnd) releaseKeys(w, table, *e, win.start, win);
            }
        }

        // ---- momentary: per note, held while the note sounds ----
        for (const Note& note : clip.notes) {
            if (note.startBeat >= lengthBeats || note.startBeat + note.lengthBeats <= 0.0) continue;
            const ArticulationEntry* e = table.resolve(note.articulation);
            if (!e || e->trigger != ArticulationTrigger::Momentary) continue;
            const double shift = trackShift + e->offsetMs * msToS;
            const double onS = clipStart + std::max(0.0, note.startBeat) * b.spb + shift;
            const double offS = std::min(clipEnd, clipStart + (note.startBeat + note.lengthBeats) * b.spb) + shift;
            const double trig = onS - e->leadMs * msToS;
            sendActions(w, table, *e, trig, win);
            if (hasKeys(table, *e)) {
                releaseKeys(w, table, *e, offS, win);
                if (b.seamOffs && trig < b.seamEnd && offS >= b.seamEnd) releaseKeys(w, table, *e, win.start, win);
            }
            // A note already held where playback lands is chased by the note scheduler; its switch goes with it.
            if (b.chase && trig < win.start && offS > win.start) sendActions(w, table, *e, win.start, win);
        }
    }
}

int finish(Writer& w, ArtEv* out, int* dropped) {
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

int scheduleArticulations(const std::vector<MidiClip>& clips, const ArticulationTable& table, const ArticulationBlock& b,
    ArtEv* out, int cap, int* dropped) {
    Writer w{out, cap};
    if (b.frames <= 0 || b.spb <= 0.0 || b.sampleRate <= 0.0) return 0;
    const Window win{b.blockStart, b.blockStart + b.frames};
    for (const MidiClip& clip : clips)
        if (clip.active) scheduleClip(w, clip, table, b, win, clip.startBeat * b.spb, clip.lengthBeats);
    return finish(w, out, dropped);
}

int scheduleSlotArticulations(const MidiClip& slotClip, double loopBeats, bool loop, const ArticulationTable& table,
    const ArticulationBlock& b, ArtEv* out, int cap, int* dropped) {
    Writer w{out, cap};
    if (b.frames <= 0 || b.spb <= 0.0 || b.sampleRate <= 0.0 || loopBeats <= 0.0) return 0;
    const Window win{b.blockStart, b.blockStart + b.frames};
    // The pass playing now, the one before (a release or a held note running into this block) and
    // the next (a negative offset or a lead reaching back across the loop end into this block).
    const long now = static_cast<long>(std::floor(b.blockStart / (loopBeats * b.spb)));
    for (long it = std::max(0L, now - 1); it <= now + 1; ++it) {
        if (!loop && it > 0) break; // one-shot: a single pass
        scheduleClip(w, slotClip, table, b, win, it * loopBeats * b.spb, loopBeats);
    }
    return finish(w, out, dropped);
}

} // namespace nota
