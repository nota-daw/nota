// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/EngineTypes.h"
#include "model/ArticulationSchedule.h"
#include "render/RenderGraphExecutor.h"
#include "core/EnginePrelude.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <vector>
#include <utility>
#include <memory>

namespace nota::detail {

void EngineRuntime::poll() {
    if (audioRecording_) recorder().drainInputQueue(); // keep the capture buffer flowing while recording
    if (sessionPass_.active) {                         // #971: Session launches heard in the pass
        session().drainSessionPass();
        // The transport stopped under it (an MCP / API stop; the app ends the take first): the
        // pass ends there and is written. A performance-only pass is the whole take: it ends too.
        if (sessionPass_.stopped) {
            if (sessionPass_.performanceOnly) {
                recorder().setRecording(false); // as Record off: writes the pass, closes the take
            } else {
                recorder().foldRecordedTake();
                session().finishSessionPass(transport_.uiPositionBeats());
            }
        }
    }
    session().serviceSessionFollow(); // scene follow + fixed-length session capture

    // Automation write/record (M9-C). Message thread: a transport stop ends any
    // latched gesture, and active gestures are sampled at the live playhead.
    {
        const bool playing = transport_.uiIsPlaying();
        const bool rec = autoRecord_.load(std::memory_order_relaxed);
        if (!playing && wasPlaying_) automation().finishAllWrites(); // stop ends latched writes
        wasPlaying_ = playing;

        // Plugin-GUI gestures (M9-C): grabbing a knob in the plugin's own editor
        // begins/ends a write for that param, so it records (or overrides) just like
        // our own faders — a mouse gesture, so never latched. Instruments/devices are
        // stable across snapshots, so a copied track list stays valid while
        // begin/end republish.
        {
            auto snapshot = authoring_->tracks;
            for (auto& tptr : snapshot) {
                const int32_t tid = tptr->id();
                if (auto& inst = tptr->instrument) {
                    int gb = inst->takePluginGestureBegin();
                    if (gb >= 0)
                        automation().beginAutomationWrite(
                            tid, 3, -1, -1, automation().pluginParamId(tid, -1, gb).c_str(), false);
                    int ge = inst->takePluginGestureEnd();
                    if (ge >= 0)
                        automation().endAutomationWrite(
                            tid, 3, -1, -1, automation().pluginParamId(tid, -1, ge).c_str());
                }
                for (int32_t di = 0; di < static_cast<int32_t>(tptr->devices.size()); ++di) {
                    auto& d = tptr->devices[static_cast<size_t>(di)];
                    if (!d) continue;
                    int gb = d->takePluginGestureBegin();
                    if (gb >= 0)
                        automation().beginAutomationWrite(
                            tid, 3, di, -1, automation().pluginParamId(tid, di, gb).c_str(), false);
                    int ge = d->takePluginGestureEnd();
                    if (ge >= 0)
                        automation().endAutomationWrite(
                            tid, 3, di, -1, automation().pluginParamId(tid, di, ge).c_str());
                }
            }
        }

        if (playing && rec && !activeWrites_.empty())
            automation().sampleAutomationWritesAt(transport_.uiPositionBeats());
    }

    recorder().foldRecordedTake();
}

void RecordingOps::foldRecordedTake() {
    // Loop record replace (#29): read the audio thread's mark before draining, so every note of
    // an earlier pass queued ahead of it is in this batch.
    const int32_t replaceUpTo = rt_.replaceUpTo_.load(std::memory_order_acquire);
    RecordedNote rn;
    std::vector<RecordedNote> batch;
    while (rt_.recorded_.pop(rn))
        if (rn.epoch == takeEpoch_) batch.push_back(rn); // a discarded take's late notes go (specs/078)
    // Expression after the notes: a note's points were all queued before its note-off was.
    collectRecordedExpr();
    {
        RecordedCtl x;
        while (rt_.recordedCtl_.pop(x))
            if (x.epoch == takeEpoch_) rt_.ctlBatch_.push_back(x);
    } // controllers (specs/020)
    const bool ctlWork = !rt_.ctlBatch_.empty() || rt_.ctlStopPending_;
    const bool replacePending = replaceUpTo > appliedReplaceUpTo_ && rt_.recordSessionScene_ < 0;
    if (batch.empty() && !replacePending && !ctlWork) return;

    // Session-slot recording (M5-4): notes are already loop-local; each lane appends into its
    // slot (several slots record at once, specs/086).
    if (!rt_.midiLanes_.empty() && rt_.midiLanes_[0].scene >= 0) {
        if (batch.empty() && !ctlWork) return;
        std::vector<RecordedCtl> ctls = std::move(rt_.ctlBatch_);
        rt_.ctlBatch_.clear();
        const bool stop = rt_.ctlStopPending_;
        rt_.ctlStopPending_ = false;
        for (size_t k = 0; k < rt_.midiLanes_.size(); ++k)
            foldSessionLane(rt_.midiLanes_[k], static_cast<int32_t>(k), batch, ctls, stop);
        return;
    }

    // Arrangement: every MIDI take lane folds its own notes and controllers into its clip
    // (specs/086); the loop record replace covers the whole take.
    std::vector<RecordedCtl> ctls = std::move(rt_.ctlBatch_);
    rt_.ctlBatch_.clear();
    const bool stop = rt_.ctlStopPending_;
    rt_.ctlStopPending_ = false;
    for (size_t k = 0; k < rt_.midiLanes_.size(); ++k)
        foldMidiLane(rt_.midiLanes_[k], static_cast<int32_t>(k), batch, ctls, stop, replaceUpTo, replacePending);
    if (replacePending) appliedReplaceUpTo_ = replaceUpTo;
}

// A Session lane's share of a poll: its loop-local notes and controllers into its slot.
bool RecordingOps::foldSessionLane(MidiTakeLane& l, int32_t lane, const std::vector<RecordedNote>& batch,
    const std::vector<RecordedCtl>& ctls, bool stop) {
    bool mine = stop;
    for (const auto& r : batch) mine = mine || r.lane == lane;
    for (const auto& x : ctls) mine = mine || x.lane == lane;
    auto st = l.trackId >= 0 && l.scene >= 0 && mine ? rt_.findTrackAuthoring(l.trackId) : nullptr;
    if (!st || l.scene >= static_cast<int32_t>(st->sessionSlots.size())) {
        for (const auto& r : batch)
            if (r.lane == lane) {
                Note gone;
                attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
            }
        return false;
    }
    std::swap(rt_.ctlFold_, l.ctlFold);
    std::swap(rt_.takeCtls_, l.takeCtls);
    struct SwapBack {
        RecordingOps& e;
        MidiTakeLane& l;
        ~SwapBack() {
            std::swap(e.rt_.ctlFold_, l.ctlFold);
            std::swap(e.rt_.takeCtls_, l.takeCtls);
        }
    } swapBack{*this, l};
    for (const auto& x : ctls)
        if (x.lane == lane) rt_.ctlBatch_.push_back(x);
    rt_.ctlStopPending_ = stop;
    auto nt = rt_.cloneTrack(*st);
    SessionSlot& s = nt->sessionSlots.write()[static_cast<size_t>(l.scene)];
    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    for (auto& r : batch) {
        if (r.lane != lane) continue;
        Note n;
        n.pitch = r.pitch;
        n.startBeat = std::fmod(r.startBeat < 0 ? 0 : r.startBeat, L);
        n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1;
        // Played on the count-in (#158): a slot has nothing before its start, so a note
        // released before the take began is dropped and one held into it starts there.
        if (r.startBeat < 0.0) {
            const double end = r.startBeat + r.lengthBeats;
            if (end <= 1e-9) {
                Note gone;
                attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
                continue;
            }
            n.lengthBeats = end;
        }
        n.velocity = r.velocity;
        attachRecordedExpr(n, exprKey(lane, r.pitch), r.startBeat);
        s.midi.notes.push_back(n);
    }
    foldRecordedCtl(s.midi, L);
    rt_.ctlStopPending_ = false;
    rt_.republishWithTrackRaw(l.trackId, nt); // grows the live take, not a new undo step
    return true;
}

// One lane's share of a poll: its notes, its controllers (and a pending stop) into its clip.
// The fold state of the lane is swapped into takeNotes_ / ctlFold_ / takeCtls_ for the work.
bool RecordingOps::foldMidiLane(MidiTakeLane& l, int32_t lane, const std::vector<RecordedNote>& batch,
    const std::vector<RecordedCtl>& ctls, bool stop, int32_t replaceUpTo, bool replacePending) {
    bool mine = stop || replacePending;
    for (const auto& r : batch) mine = mine || r.lane == lane;
    for (const auto& x : ctls) mine = mine || x.lane == lane;
    auto t = l.trackId >= 0 && mine ? rt_.findTrackAuthoring(l.trackId) : nullptr;
    if (!t || l.clipIndex < 0 || l.clipIndex >= static_cast<int32_t>(t->midiClips.size())) {
        for (const auto& r : batch) // its curves go with its notes
            if (r.lane == lane) {
                Note gone;
                attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
            }
        return false;
    }
    std::swap(takeNotes_, l.takeNotes);
    std::swap(rt_.ctlFold_, l.ctlFold);
    std::swap(rt_.takeCtls_, l.takeCtls);
    struct SwapBack {
        RecordingOps& e;
        MidiTakeLane& l;
        ~SwapBack() {
            std::swap(e.takeNotes_, l.takeNotes);
            std::swap(e.rt_.ctlFold_, l.ctlFold);
            std::swap(e.rt_.takeCtls_, l.takeCtls);
        }
    } swapBack{*this, l};
    for (const auto& x : ctls)
        if (x.lane == lane) rt_.ctlBatch_.push_back(x);
    rt_.ctlStopPending_ = stop;

    auto nt = rt_.cloneTrack(*t);
    MidiClip& clip = nt->midiClips.write()[static_cast<size_t>(l.clipIndex)];
    for (auto& r : batch) {
        if (r.lane != lane) continue;
        if (r.pass < replaceUpTo) { // its pass was already replaced: drop it (and its curves)
            Note gone;
            attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
            continue;
        }
        // A note that began before the take's clip (played on the count-in, or a key held
        // when Record was pressed, #56) moves the clip's left edge back to it, keeping every
        // note where it is. Clips can't start before 1.1, so a note older than that keeps
        // its head before the clip's start (it sounds from the clip start).
        if (r.startBeat < clip.startBeat) {
            const double newStart = std::max(0.0, r.startBeat);
            const double delta = clip.startBeat - newStart;
            if (delta > 0.0) {
                for (auto& o : clip.notes) o.startBeat += delta;
                clip.lengthBeats += delta;
                clip.startBeat = newStart;
            }
        }
        Note n;
        n.pitch = r.pitch;
        n.startBeat = r.startBeat - clip.startBeat;
        n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1;
        n.velocity = r.velocity;
        attachRecordedExpr(n, exprKey(lane, r.pitch), r.startBeat);
        clip.notes.push_back(n);
        takeNotes_.push_back({r.pitch, r.startBeat, r.pass});
        // Grow the clip so freshly recorded notes fall inside it (gating in
        // renderInstrumentRaw drops notes past lengthBeats).
        const double end = n.startBeat + n.lengthBeats;
        if (end > clip.lengthBeats) clip.lengthBeats = end;
    }
    foldRecordedCtl(clip, 0.0);
    // Replace: remove the notes this take recorded on earlier passes — matched by pitch and
    // absolute start, so notes the clip held before the take (or the user drew) stay.
    if (replacePending) {
        auto& notes = clip.notes;
        for (size_t i = 0; i < notes.size();) {
            const double abs = clip.startBeat + notes[i].startBeat;
            auto it = std::find_if(takeNotes_.begin(), takeNotes_.end(), [&](const TakeNote& k) {
                return k.pass < replaceUpTo && k.pitch == notes[i].pitch && std::fabs(k.absStart - abs) < 1e-6;
            });
            if (it == takeNotes_.end()) {
                ++i;
                continue;
            }
            takeNotes_.erase(it);
            // It may be sounding right now (an earlier pass playing back): release its voice.
            Command c;
            c.type = CommandType::ReleaseNote;
            c.i0 = l.trackId;
            c.i1 = notes[i].pitch;
            rt_.commands_.push(c);
            notes.erase(notes.begin() + static_cast<std::ptrdiff_t>(i));
        }
        takeNotes_.erase(std::remove_if(takeNotes_.begin(), takeNotes_.end(),
                             [&](const TakeNote& k) { return k.pass < replaceUpTo; }),
            takeNotes_.end());
        // ...and the controller points those passes recorded (the whole pass is replaced).
        for (const TakeCtl& k : rt_.takeCtls_) {
            if (k.pass >= replaceUpTo) continue;
            const ControllerCurve* cur = clip.controller(k.ctl);
            if (!cur) continue;
            std::vector<AutomationPoint> pts;
            const double lb = k.absBeat - clip.startBeat;
            for (const auto& p : cur->points)
                if (std::fabs(p.beat - lb) > 1e-6) pts.push_back(p);
            clip.setController(k.ctl, std::move(pts));
        }
        rt_.takeCtls_.erase(std::remove_if(rt_.takeCtls_.begin(), rt_.takeCtls_.end(),
                                [&](const TakeCtl& k) { return k.pass < replaceUpTo; }),
            rt_.takeCtls_.end());
    }
    rt_.republishWithTrackRaw(l.trackId, nt); // grows the live take, not a new undo step
    return true;
}

void RecordingOps::collectRecordedExpr() {
    RecordedExpr x;
    while (rt_.recordedExpr_.pop(x)) {
        if (x.epoch != takeEpoch_) continue; // a discarded take's (specs/078)
        auto& curve = pendingExpr_[{exprKey(x.lane, x.pitch), x.startBeat}].dim[x.dim];  // per lane (specs/086)
        if (!curve.empty() && x.beat <= curve.back().beat) curve.back().value = x.value; // same instant
        else
            curve.push_back({x.beat, x.value});
    }
}

// A finished recorded note takes the curve gathered for it, thinned to the points that shape
// it (a block-rate stream is far denser than the gesture needs) — or nothing when every
// dimension stayed neutral (an MPE controller announces a note's resting state too).
void RecordingOps::attachRecordedExpr(Note& n, int32_t pitch, double startBeat) {
    auto it = pendingExpr_.find({pitch, startBeat});
    if (it == pendingExpr_.end()) return;
    auto e = std::make_shared<NoteExpr>();
    static constexpr float kTol[kExprDims] = {0.03f, 0.01f, 0.01f}; // semitones / units
    for (int32_t d = 0; d < kExprDims; ++d) {
        const auto& src = it->second.dim[d];
        bool moved = false;
        for (const auto& p : src)
            if (std::fabs(p.value - kExprNeutral[d]) > kTol[d]) {
                moved = true;
                break;
            }
        if (moved) e->dim[d] = thinExprCurve(src, kTol[d]);
    }
    pendingExpr_.erase(it);
    if (e->empty()) return;
    n.expr = e;
    n.exprId = rt_.clips().storeNoteExpr(e);
}

// --- recorded controllers (specs/020) -------------------------------------------
// The audio thread queues every controller sample of the take (RecordedCtl); each poll folds the
// new ones into the take's clip. A controller a pass moves is replaced over the range it touched
// — from its first point in the pass up to the pass end (the loop end, or the record stop) — so
// two passes of the mod wheel never interleave; the controllers a pass didn't move stay, and so
// do the notes (a pedal-only pass re-records only the pedal). The run since the last poll is
// thinned on its own, anchored on the range's last committed point, so a long take stays a
// handful of points per gesture as it grows.

void RecordingOps::resetCtlFold() {
    for (auto& f : rt_.ctlFold_) f = CtlFold{};
    rt_.ctlBatch_.clear();
    rt_.takeCtls_.clear();
    for (auto& l : rt_.midiLanes_) {
        for (auto& f : l.ctlFold) f = CtlFold{};
        l.takeCtls.clear();
    }
    rt_.ctlStopPending_ = false;
    RecordedCtl x;
    while (rt_.recordedCtl_.pop(x)) {}
}

bool RecordingOps::foldRecordedCtl(MidiClip& clip, double slotLen) {
    const bool slot = slotLen > 0.0;
    bool changed = false;
    for (const RecordedCtl& x : rt_.ctlBatch_) {
        if (x.ctl < 0) { // a loop pass ended: its ranges run to the loop end
            for (int32_t c = 0; c < kCtlCount; ++c) {
                CtlFold& f = rt_.ctlFold_[static_cast<size_t>(c)];
                if (!f.open || f.pass != x.pass) continue;
                commitCtlRun(clip, c, slotLen, true);
                closeCtlRange(clip, c, x.beat - clip.startBeat, slotLen);
                f.open = false;
                changed = true;
            }
            continue;
        }
        if (x.ctl >= kCtlCount || ctlIsReserved(x.ctl)) continue;
        CtlFold& f = rt_.ctlFold_[static_cast<size_t>(x.ctl)];
        // A Session take wraps at the slot's end: the range closes there and a new one opens.
        const bool wrapped = slot && f.open && x.beat < f.lastAbs - slotLen * 0.5;
        if (f.open && (f.pass != x.pass || wrapped)) {
            commitCtlRun(clip, x.ctl, slotLen, true);
            closeCtlRange(clip, x.ctl, slot ? slotLen : f.lastAbs - clip.startBeat, slotLen);
            f.open = false;
            changed = true;
        }
        if (!f.open) {
            f.open = true;
            f.pass = x.pass;
            f.startAbs = x.beat;
            f.hasCommitted = false;
            f.before = clip.controllerPtr(x.ctl);
        }
        f.lastAbs = x.beat;
        f.run.push_back(x);
    }
    rt_.ctlBatch_.clear();
    for (int32_t c = 0; c < kCtlCount; ++c)
        if (!rt_.ctlFold_[static_cast<size_t>(c)].run.empty()) {
            commitCtlRun(clip, c, slotLen, false);
            changed = true;
        }
    if (rt_.ctlStopPending_) { // the take stopped: the open ranges end at the stop
        rt_.ctlStopPending_ = false;
        for (int32_t c = 0; c < kCtlCount; ++c) {
            CtlFold& f = rt_.ctlFold_[static_cast<size_t>(c)];
            if (!f.open) continue;
            commitCtlRun(clip, c, slotLen, true); // the points held back for thinning
            closeCtlRange(clip, c,
                slot ? std::fmod(std::max(0.0, f.lastAbs), slotLen) : rt_.ctlStopAt_ - clip.startBeat, slotLen);
            f.open = false;
            changed = true;
        }
    }
    return changed;
}

void RecordingOps::commitCtlRun(MidiClip& clip, int32_t ctl, double slotLen, bool final) {
    CtlFold& f = rt_.ctlFold_[static_cast<size_t>(ctl)];
    if (f.run.empty()) return;
    const bool slot = slotLen > 0.0;
    // A controller moved before the take's clip (on the count-in) moves its left edge back to
    // it, like a pickup note does; never before 1.1.
    if (!slot) {
        double minAbs = f.run.front().beat;
        for (const auto& x : f.run) minAbs = std::min(minAbs, x.beat);
        if (minAbs < clip.startBeat) {
            const double newStart = std::max(0.0, minAbs);
            const double delta = clip.startBeat - newStart;
            if (delta > 0.0) {
                for (auto& o : clip.notes) o.startBeat += delta;
                clip.shiftControllers(delta);
                clip.lengthBeats += delta;
                clip.startBeat = newStart;
            }
        }
    }
    auto local = [&](double abs) { return slot ? std::fmod(std::max(0.0, abs), slotLen) : abs - clip.startBeat; };

    // The run on the take clock, after the range's last committed point (the thinning anchor).
    std::vector<AutomationPoint> pts;
    if (f.hasCommitted) pts.push_back(f.committed);
    for (const auto& x : f.run) {
        const AutomationPoint p{x.beat, ctlClamp(ctl, x.value), 0.0f};
        if (!pts.empty() && std::fabs(pts.back().beat - p.beat) < 1e-9) pts.back().value = p.value;
        else
            pts.push_back(p);
    }
    f.run.clear();
    const double loAbs = f.hasCommitted ? f.committed.beat : f.startAbs;
    const double hiAbs = pts.back().beat;
    std::vector<AutomationPoint> kept = ctlIsSwitch(ctl) ? dedupeSwitch(pts) : thinCtlCurve(pts, ctlThinTolerance(ctl));
    if (f.hasCommitted && !kept.empty() && std::fabs(kept.front().beat - f.committed.beat) < 1e-9)
        kept.erase(kept.begin());
    // A moving curve keeps its last segment back: the points after the last-but-one kept point
    // wait for the next poll, where the thinning may find them on one longer line.
    // (A long straight or steady stretch is committed at 512 points, so the tail stays small.)
    if (!final && !ctlIsSwitch(ctl) && pts.size() <= 512) {
        const double holdFrom = kept.size() >= 2
                                    ? kept[kept.size() - 2].beat
                                    : (f.hasCommitted ? f.committed.beat : -std::numeric_limits<double>::infinity());
        if (kept.size() >= 2) kept.pop_back();
        else
            kept.clear();
        for (const auto& p : pts)
            if (p.beat > holdFrom + 1e-12 || (kept.empty() && !f.hasCommitted))
                f.run.push_back(RecordedCtl{ctl, p.beat, p.value, f.pass});
        if (kept.empty()) return; // nothing settled yet; the range keeps growing
    }

    // Replace what the clip held over the range so far, then add the run.
    const double loL = local(loAbs), hiL = local(hiAbs);
    std::vector<AutomationPoint> out;
    if (const ControllerCurve* cur = clip.controller(ctl))
        for (const auto& p : cur->points) {
            const bool inRange = f.hasCommitted ? (p.beat > loL + 1e-9 && p.beat <= hiL + 1e-9)
                                                : (p.beat >= loL - 1e-9 && p.beat <= hiL + 1e-9);
            if (!inRange) out.push_back(p);
        }
    // The range's first commit steps out of the old curve: its value holds to just before.
    if (!f.hasCommitted && f.before && !f.before->points.empty() &&
        f.before->points.front().beat < loL - kCtlJunction) {
        const double j = loL - kCtlJunction;
        const bool junction = !kept.empty() && f.before->valueAt(j) != kept.front().value;
        // The last point before the range now ends its segment early: shorten its curve so the
        // stretch before the range keeps its values (#591).
        for (auto it = out.rbegin(); it != out.rend(); ++it)
            if (it->beat < loL - 1e-9) {
                it->curve = ctlPieceCurve(f.before->points, it->beat, junction ? j : loL);
                break;
            }
        if (junction) out.push_back({j, f.before->valueAt(j), 0.0f});
    }
    for (const auto& p : kept) {
        out.push_back({local(p.beat), p.value, 0.0f});
        rt_.takeCtls_.push_back({ctl, p.beat, f.pass});
    }
    if (!kept.empty()) {
        f.committed = kept.back();
        f.hasCommitted = true;
    }
    clip.setController(ctl, std::move(out));
    // Grow the clip so the controller's last move falls inside it (a pedal released late).
    if (!slot && hiL > clip.lengthBeats) clip.lengthBeats = hiL;
}

void RecordingOps::flushCtlTake() {
    {
        RecordedCtl x;
        while (rt_.recordedCtl_.pop(x))
            if (x.epoch == takeEpoch_) rt_.ctlBatch_.push_back(x);
    }
    rt_.ctlStopPending_ = true;
    rt_.ctlStopAt_ = rt_.transport_.uiPositionBeats();
    if (!rt_.midiLanes_.empty() && rt_.midiLanes_[0].scene >= 0) { // every Session slot of the take (specs/086)
        std::vector<RecordedCtl> ctls = std::move(rt_.ctlBatch_);
        rt_.ctlBatch_.clear();
        rt_.ctlStopPending_ = false;
        for (size_t k = 0; k < rt_.midiLanes_.size(); ++k)
            foldSessionLane(rt_.midiLanes_[k], static_cast<int32_t>(k), {}, ctls, true);
    } else if (!rt_.midiLanes_.empty()) { // every MIDI take lane (specs/086)
        std::vector<RecordedCtl> ctls = std::move(rt_.ctlBatch_);
        rt_.ctlBatch_.clear();
        rt_.ctlStopPending_ = false;
        for (size_t k = 0; k < rt_.midiLanes_.size(); ++k)
            foldMidiLane(rt_.midiLanes_[k], static_cast<int32_t>(k), {}, ctls, true, 0, false);
    }
    resetCtlFold();
}

// End ctl's range at `atLocal` (clip-local): what the clip held between the range's last point
// and there is replaced too; where the old curve goes on past it, a junction point at `atLocal`
// hands back to it with the value it had there.
void RecordingOps::closeCtlRange(MidiClip& clip, int32_t ctl, double atLocal, double slotLen) {
    CtlFold& f = rt_.ctlFold_[static_cast<size_t>(ctl)];
    const ControllerCurve* cur = clip.controller(ctl);
    if (!cur || !f.hasCommitted) return;
    const double lastL =
        slotLen > 0.0 ? std::fmod(std::max(0.0, f.committed.beat), slotLen) : f.committed.beat - clip.startBeat;
    if (atLocal <= lastL + 1e-9) return;
    // The value the curve had there before this range replaced anything (rest when it had none).
    const float old = f.before && !f.before->points.empty() ? f.before->valueAt(atLocal) : kCtlRest;
    std::vector<AutomationPoint> out;
    bool after = false, at = false;
    for (const auto& p : cur->points) {
        if (p.beat > lastL + 1e-9 && p.beat < atLocal - 1e-9) continue;
        if (p.beat >= atLocal - 1e-9) after = true;
        if (std::fabs(p.beat - atLocal) < 1e-9) at = true;
        out.push_back(p);
    }
    if (after && !at) {
        // Hold the range's last value up to the junction, then hand back to the old curve.
        if (!ctlIsSwitch(ctl) && atLocal - kCtlJunction > lastL + 1e-9 && old != f.committed.value)
            out.push_back({atLocal - kCtlJunction, f.committed.value, 0.0f});
        // ...and from there on the old curve plays its own shape (#591).
        const float tail = f.before && !ctlIsSwitch(ctl)
                               ? ctlPieceCurve(f.before->points, atLocal, std::numeric_limits<double>::infinity())
                               : 0.0f;
        out.push_back({atLocal, old, tail});
    }
    clip.setController(ctl, std::move(out));
    f.before.reset();
}

// --- audio thread ----------------------------------------------------------

} // namespace nota::detail
