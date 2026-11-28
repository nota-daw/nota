// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session performance → Arrangement (#971, specs/109): the engine side of a pass. An Arrangement
// take starts a pass; poll() folds the audio thread's launch reports into played spans
// (SessionPass); when the take stops, every span is written to its track as ordinary Arrangement
// clips, replacing what the track had there and nothing else, so the Arrangement afterwards plays
// what was heard. Everything a pass writes is one undo step. Message thread.

#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/Graph.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "session/SessionPlayer.h"
#include "session/SessionOps.h"
#include "session/SessionPass.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstddef>
#include <set>
#include <tuple>
#include <vector>
#include <utility>

namespace nota::detail {

double SessionOps::launchQuant() const {
    return rt_.launchQuant_.load(std::memory_order_relaxed);
}

// Audio thread, after each rolling render segment while a take runs: what each Session player
// applied in it (a launch or stop, at its beat and slot position), a one-shot that ran out in it
// (at the beat its content ended), or — the first segment of a pass or of its retry — what already
// plays. No allocation: a fixed-size push.
NOTA_NOINLINE void SessionOps::reportPassEvents(Graph* g, double segBeat, double segBeats) {
    const uint32_t ps = rt_.passSerial_.load(std::memory_order_acquire);
    if (ps == 0) return;
    const bool opening = rt_.rtPassSerial_ != ps;
    rt_.rtPassSerial_ = ps;
    rt_.rtPassEndBeat_ = segBeat + segBeats;
    for (auto& tp : g->tracks) {
        SessionPlayer* sp = tp->sessionPlayer.get();
        if (!sp) continue;
        const bool applied = sp->applyCount != sp->reportedCount;
        sp->reportedCount = sp->applyCount;
        const int32_t now = sp->playing.load(std::memory_order_relaxed);
        const int32_t was = sp->reportedPlaying;
        sp->reportedPlaying = now;
        if (opening) {
            // Read back from the end of the segment (a monitored track's clock stands still: 0).
            if (now >= 0)
                pushPassEvent({LaunchEvent::Launch, tp->id(), now, segBeat, std::max(0.0, sp->localBeats - segBeats),
                    sp->gain, ps});
        } else if (applied) {
            // A quantized launch is written on its grid beat, the slot's beat 0 (or the Legato
            // carry) there.
            pushPassEvent({LaunchEvent::Launch, tp->id(), now, sp->appliedBeat, sp->appliedPos, sp->gain, ps});
        } else if (now < 0 && was >= 0) {
            // A one-shot ran out: its clock passed the slot's end during this segment.
            double end = segBeat + segBeats;
            if (was < static_cast<int32_t>(tp->sessionSlots.size())) {
                const SessionSlot& s = tp->sessionSlots[static_cast<size_t>(was)];
                const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
                end = std::max(segBeat, end - std::max(0.0, sp->localBeats - L));
            }
            pushPassEvent({LaunchEvent::Launch, tp->id(), -1, end, 0.0, 1.0f, ps});
        }
    }
}

bool SessionOps::sessionPlayingOrQueued() const {
    for (const auto& t : rt_.authoring_->tracks)
        if (const auto& sp = t->sessionPlayer)
            if (sp->playing.load(std::memory_order_relaxed) >= 0 || sp->pendingSlot() >= 0) return true;
    return false;
}

int32_t SessionOps::sessionPassSpans(PassSpan* out, int32_t cap) const {
    if (!rt_.sessionPass_.active) return 0;
    const double now = rt_.transport_.uiPositionBeats();
    int32_t n = 0;
    for (const auto& s : rt_.sessionPass_.spans) {
        // An armed audio track's input take wins over its slot (writePassTrack): not shown.
        const auto t = rt_.findTrackAuthoring(s.trackId);
        if (t && t->type() == TrackType::Audio && passRecordsInput(*t)) continue;
        if (out && n < cap) {
            PassSpan& o = out[n];
            o.trackId = s.trackId;
            o.slot = s.slot;
            o.startBeat = s.start;
            // A loop wrap not drained yet can put the playhead before the span: hold it at its start.
            o.endBeat = s.open() ? std::max(s.start, now) : s.end;
            o.slotPos = s.slotPos;
            o.open = s.open() ? 1 : 0;
            o.warped = 0;
            if (t && s.slot >= 0 && s.slot < static_cast<int32_t>(t->sessionSlots.size())) {
                const AudioClip& a = t->sessionSlots[static_cast<size_t>(s.slot)].audio;
                o.warped = a.sample && a.warpEnabled && a.warpBeats > 0.0 ? 1 : 0;
            }
        }
        ++n;
    }
    return n;
}

void SessionOps::beginSessionPass(bool performanceOnly) {
    LaunchEvent e;
    while (launchEvents_.pop(e)) {} // an earlier pass's late reports
    rt_.sessionPass_.reset();
    rt_.sessionPass_.active = true;
    rt_.sessionPass_.performanceOnly = performanceOnly;
    rt_.sessionPass_.tempoBefore = rt_.authoringBpm_;
    if (++passSerialNext_ == 0) ++passSerialNext_; // 0 means "no pass"
    rt_.sessionPass_.serial = passSerialNext_;
    passBase_ = rt_.authoring_->tracks;
    passGraph_ = rt_.authoring_;
    passArmed_.clear();
    for (const auto& t : passBase_)
        if (t && t->armed()) passArmed_.push_back(t->id());
    rt_.passUndoBase_ = rt_.undoStack_.size();
    passDropped_.store(0, std::memory_order_relaxed);
    rt_.passSerial_.store(rt_.sessionPass_.serial, std::memory_order_release);
}

void SessionOps::drainSessionPass() {
    LaunchEvent e;
    while (launchEvents_.pop(e))
        if (rt_.sessionPass_.active && e.serial == rt_.sessionPass_.serial) rt_.sessionPass_.onEvent(e);
    rt_.sessionPass_.dropped = passDropped_.load(std::memory_order_relaxed);
}

void SessionOps::abandonSessionPass() {
    rt_.passSerial_.store(0, std::memory_order_release);
    LaunchEvent e;
    while (launchEvents_.pop(e)) {}
    rt_.sessionPass_.reset();
    passBase_.clear();
    passGraph_.reset();
    passArmed_.clear();
}

bool SessionOps::finishSessionPass(double stopBeat) {
    if (!rt_.sessionPass_.active) return false;
    drainSessionPass(); // everything the audio thread reported for this pass
    rt_.passSerial_.store(0, std::memory_order_release);
    rt_.sessionPass_.closeAll(stopBeat);
    bool wrote = false;
    if (!rt_.sessionPass_.empty()) {
        // One checkpoint before the writes; the writes themselves are raw publishes, and the
        // pass's history (its take, its automation, these clips) collapses into one step below.
        // When nothing in the pass pushed a step yet, the checkpoint is the project as the pass
        // found it: an overdub into an existing clip grows it without a step of its own.
        rt_.passUndoBase_ = std::min(rt_.passUndoBase_, rt_.undoStack_.size()); // history cleared mid-pass
        const bool fromStart = rt_.undoStack_.size() == rt_.passUndoBase_ && passGraph_ && rt_.undoGroupDepth_ == 0;
        rt_.pushUndo();
        if (fromStart && !rt_.undoStack_.empty()) rt_.undoStack_.back().graph = passGraph_;
        std::vector<int32_t> order;
        for (const auto& s : rt_.sessionPass_.spans)
            if (std::find(order.begin(), order.end(), s.trackId) == order.end()) order.push_back(s.trackId);
        for (int32_t id : order) {
            std::vector<const PlayedSpan*> mine;
            for (const auto& s : rt_.sessionPass_.spans)
                if (s.trackId == id) mine.push_back(&s);
            writePassTrack(id, mine);
        }
        writePassTempo();
        wrote = true;
    }
    if (wrote && rt_.undoGroupDepth_ == 0 && rt_.undoStack_.size() > rt_.passUndoBase_ + 1) {
        rt_.undoGroupBase_ = rt_.passUndoBase_;
        rt_.undoGroupDepth_ = 1;
        rt_.endUndoGroup();
    }
    rt_.sessionPass_.reset();
    passBase_.clear();
    passGraph_.reset();
    passArmed_.clear();
    return wrote;
}

namespace {
// A note's or controller point's identity for telling the take's from what the track had before
// the pass.
using NoteKey = std::tuple<int32_t, int64_t, int64_t, int32_t>;
int64_t quant(double b) {
    return static_cast<int64_t>(std::llround(b * 1e6));
}
NoteKey keyOf(const Note& n, double clipStart) {
    return {n.pitch, quant(clipStart + n.startBeat), quant(n.lengthBeats),
        static_cast<int32_t>(std::lround(n.velocity * 1e4f))};
}
NoteKey keyOf(int32_t ctl, const AutomationPoint& p, double clipStart) {
    return {ctl, quant(clipStart + p.beat), static_cast<int32_t>(std::lround(p.value * 1e4f)), 0};
}
} // namespace

bool SessionOps::passRecordsInput(const Track& t) const {
    if (rt_.sessionPass_.performanceOnly) return false;
    return t.armed() || std::find(passArmed_.begin(), passArmed_.end(), t.id()) != passArmed_.end();
}

void SessionOps::writePassTrack(int32_t trackId, const std::vector<const PlayedSpan*>& spans) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || (old->type() != TrackType::Instrument && old->type() != TrackType::Audio)) return;
    // A track armed during the pass recorded its input (disarming it on the way saved its take).
    const bool takeRecorded = passRecordsInput(*old);
    // An armed audio track records its input over what it plays: its take wins there, as an
    // audio take replaces material today (and with monitoring on its slot was silent anyway).
    if (old->type() == TrackType::Audio && takeRecorded) return;
    auto nt = rt_.cloneTrack(*old);

    // An armed instrument track's take: the notes and controller moves it holds now that it did
    // not hold when the pass began. Those played over a span join the span's written clip
    // (clarification Q3).
    std::vector<Note> takeNotes;                               // startBeat = timeline beat
    std::vector<std::pair<int32_t, AutomationPoint>> takeCtls; // ctl, point at its timeline beat
    if (old->type() == TrackType::Instrument && takeRecorded) {
        std::multiset<NoteKey> before, beforeCtl;
        for (const auto& b : passBase_)
            if (b && b->id() == trackId)
                for (const auto& c : b->midiClips) {
                    for (const auto& n : c.notes) before.insert(keyOf(n, c.startBeat));
                    for (const auto& cv : c.controllers)
                        if (cv)
                            for (const auto& p : cv->points) beforeCtl.insert(keyOf(cv->ctl, p, c.startBeat));
                }
        for (const auto& c : nt->midiClips) {
            for (const auto& n : c.notes) {
                auto it = before.find(keyOf(n, c.startBeat));
                if (it != before.end()) {
                    before.erase(it);
                    continue;
                }
                Note t = n;
                t.startBeat = c.startBeat + n.startBeat;
                takeNotes.push_back(std::move(t));
            }
            for (const auto& cv : c.controllers) {
                if (!cv) continue;
                for (const auto& p : cv->points) {
                    auto it = beforeCtl.find(keyOf(cv->ctl, p, c.startBeat));
                    if (it != beforeCtl.end()) {
                        beforeCtl.erase(it);
                        continue;
                    }
                    AutomationPoint q = p;
                    q.beat = c.startBeat + p.beat;
                    takeCtls.push_back({cv->ctl, q});
                }
            }
        }
    }

    const double spb = rt_.authoringSamplesPerBeat();
    bool changed = false;
    for (const PlayedSpan* sp : spans) {
        if (sp->slot < 0 || sp->slot >= static_cast<int32_t>(nt->sessionSlots.size())) continue;
        const SessionSlot& s = nt->sessionSlots[static_cast<size_t>(sp->slot)];
        if (!s.hasClip) continue;
        const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
        const double a = sp->start, b = sp->end;
        if (nt->type() == TrackType::Instrument) {
            MidiClip w = unrollSlotMidi(s.midi, L, s.loop, *sp);
            w.name = s.name;
            for (Note& n : w.notes) // a held note's re-based expression
                if (n.expr && n.exprId == 0) n.exprId = rt_.clips().storeNoteExpr(n.expr);
            for (const Note& tn : takeNotes)
                if (tn.startBeat >= a - 1e-9 && tn.startBeat < b - 1e-9) {
                    Note n = tn;
                    n.startBeat = tn.startBeat - a;
                    n.lengthBeats = std::min(tn.lengthBeats, b - tn.startBeat);
                    w.notes.push_back(std::move(n));
                }
            std::stable_sort(
                w.notes.begin(), w.notes.end(), [](const Note& x, const Note& y) { return x.startBeat < y.startBeat; });
            // The take's controller moves over the span: from its first move on, a moved
            // controller follows the take (as a recorded move replaces from there), the slot's
            // curve before that.
            std::vector<int32_t> moved;
            for (const auto& [ctl, p] : takeCtls)
                if (p.beat >= a - 1e-9 && p.beat < b - 1e-9 &&
                    std::find(moved.begin(), moved.end(), ctl) == moved.end())
                    moved.push_back(ctl);
            for (int32_t ctl : moved) {
                std::vector<AutomationPoint> take;
                for (const auto& [c, p] : takeCtls)
                    if (c == ctl && p.beat >= a - 1e-9 && p.beat < b - 1e-9) {
                        AutomationPoint q = p;
                        q.beat = p.beat - a;
                        take.push_back(q);
                    }
                std::stable_sort(
                    take.begin(), take.end(), [](const auto& x, const auto& y) { return x.beat < y.beat; });
                std::vector<AutomationPoint> pts;
                if (const ControllerCurve* slotCurve = w.controller(ctl))
                    for (const auto& p : slotCurve->points)
                        if (p.beat < take.front().beat - 1e-9) pts.push_back(p);
                pts.insert(pts.end(), take.begin(), take.end());
                w.setController(ctl, std::move(pts));
            }
            auto& midiW = nt->midiClips.write();
            rt_.clips().overwriteMidiClipsInRange(midiW, a, b);
            midiW.push_back(std::move(w));
        } else {
            if (!s.audio.sample) continue;
            auto& clipsW = nt->clips.write();
            rt_.clips().overwriteAudioClipsInRange(clipsW, a, b, spb);
            for (double r : slotRepetitions(L, s.loop, *sp)) {
                AudioClip c = s.audio;
                c.startBeat = r;
                c.name = s.name;
                c.active = true;
                c.gain *= sp->gain;
                clipsW.push_back(rt_.clips().sliceAudioClip(c, std::max(r, a), std::min(r + L, b)));
            }
        }
        changed = true;
    }
    if (!changed) return;
    {
        auto& mc = nt->midiClips.write();
        std::stable_sort(
            mc.begin(), mc.end(), [](const MidiClip& x, const MidiClip& y) { return x.startBeat < y.startBeat; });
    }
    {
        auto& ac = nt->clips.write();
        std::stable_sort(
            ac.begin(), ac.end(), [](const AudioClip& x, const AudioClip& y) { return x.startBeat < y.startBeat; });
    }
    rt_.republishWithTrackRaw(trackId, nt);
}

// Scene tempos heard during the pass, as the master Tempo lane: the tempo before the pass from
// the start, then an instant change at every scene's beat. Only when the project had no Tempo lane
// (with one, the scene tempo does not apply and the lane already plays what was heard).
void SessionOps::writePassTempo() {
    if (rt_.sessionPass_.tempoSteps.empty() || rt_.tempoMap()) return;
    const int32_t lane =
        rt_.automation().addAutomationLane(kMasterTrackId, static_cast<int32_t>(AutomationTarget::Tempo), -1, -1);
    if (lane < 0) return;
    std::vector<AutomationPointData> pts;
    double prev = rt_.sessionPass_.tempoBefore > 0.0 ? rt_.sessionPass_.tempoBefore : rt_.authoringBpm_;
    pts.push_back({0.0, static_cast<float>(prev), 0.0f});
    for (const auto& [beat, bpm] : rt_.sessionPass_.tempoSteps) {
        if (beat > 1e-9) pts.push_back({beat, static_cast<float>(prev), 0.0f});
        else
            pts.clear();
        pts.push_back({std::max(0.0, beat), static_cast<float>(bpm), 0.0f});
        prev = bpm;
    }
    rt_.automation().setAutomationPoints(kMasterTrackId, lane, pts.data(), static_cast<int32_t>(pts.size()));
}

} // namespace nota::detail
