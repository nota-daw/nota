// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "core/EngineRuntime.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/EngineTypes.h"
#include "model/ArticulationTable.h"
#include "render/RenderHelpers.h"
#include "model/ArticulationSchedule.h"
#include "render/RenderGraphExecutor.h"
#include "render/RenderRoutingPlan.h"
#include "core/TempoMap.h"
#include "model/TimelineIndex.h"
#include "core/EnginePrelude.h"
#include "nota/devices/racks/DrumRack.h"
#include "model/ParamResolve.h"
#include "session/SessionPlayer.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <vector>

namespace nota::detail {

void EngineRuntime::noteExprEvents(const Note& note, double onS, double offS, int32_t frames, double spb, ExprEv* out,
    int& n, int cap, const TempoMap* map, double sr, double sampleStart, double startBeat, double shift) {
    static constexpr int32_t kExprStep = 64; // ~1.3 ms at 48 kHz; the voices smooth between
    if (!note.expr || spb <= 0.0 || offS <= 0.0 || onS >= frames || offS <= onS) return;
    const int32_t first = static_cast<int32_t>(std::max(0.0, onS)); // the note-on's own offset
    const int32_t end = static_cast<int32_t>(std::min<double>(frames, offS));
    if (first >= frames) return;
    const NoteExpr& e = *note.expr;
    for (int32_t d = 0; d < kExprDims; ++d) {
        const auto& c = e.dim[d];
        if (c.empty()) continue;
        auto valueAt = [&](int32_t off) {
            return e.valueAt(d, map ? map->beatAt((sampleStart + off - shift) / sr) - startBeat : (off - onS) / spb);
        };
        if (n < cap) out[n++] = ExprEv{first, note.pitch, d, valueAt(first)};
        if (c.size() < 2) continue;
        // While the curve moves (one step past its last point, to land on it).
        const double moveFrom =
            (map ? map->secondsAt(startBeat + c.front().beat) * sr + shift - sampleStart : onS + c.front().beat * spb) -
            kExprStep;
        const double moveTo =
            (map ? map->secondsAt(startBeat + c.back().beat) * sr + shift - sampleStart : onS + c.back().beat * spb) +
            kExprStep;
        for (int32_t off = (first / kExprStep + 1) * kExprStep; off < end && n < cap; off += kExprStep)
            if (off >= moveFrom && off <= moveTo) out[n++] = ExprEv{off, note.pitch, d, valueAt(off)};
    }
}

void EngineRuntime::clipCtlEvents(const MidiClip& clip, double clipStartS, double clipEndS, double blockStart,
    int32_t frames, double spb, bool chase, ExprEv* out, int& n, int cap, const TempoMap* map, double sr,
    double sampleStart) {
    static constexpr int32_t kStep = 64; // as recorded MPE: the instrument smooths between
    if (clip.controllers.empty() || spb <= 0.0) return;
    const double b1 = blockStart + frames;
    const double from = std::max(blockStart, clipStartS), to = std::min(b1, clipEndS);
    if (to <= from) return;
    const int32_t first = static_cast<int32_t>(from - blockStart);
    const int32_t end = std::min(frames, static_cast<int32_t>(std::ceil(to - blockStart)));
    const bool entry =
        chase || (clipStartS >= blockStart - 0.5 && clipStartS < b1 - 0.5); // half-sample slack, as below
    auto beatAt = [&](double off) {
        return map ? map->beatAt((sampleStart + off) / sr) - clip.startBeat : (blockStart + off - clipStartS) / spb;
    };
    for (const auto& cp : clip.controllers) {
        if (!cp || cp->points.empty()) continue;
        const ControllerCurve& c = *cp;
        const int32_t dim = kCtlDimBase + c.ctl;
        const double firstBeat = c.points.front().beat;
        // Before a curve's first point it has no say: the controller keeps what it had.
        if (entry && beatAt(first) >= firstBeat - 1e-9 && n < cap)
            out[n++] = ExprEv{first, -1, dim, c.valueAt(beatAt(first))};
        // Recorded points sit on block starts (the capture is block-timed): place each on a whole
        // sample, with a hair of slack, so rounding never lets one fall between two blocks.
        const double minPtBeat = beatAt(first) - 1e-3;
        auto ptIt = std::lower_bound(c.points.begin(), c.points.end(), minPtBeat,
            [](const AutomationPoint& pt, double b) { return pt.beat < b; });
        for (; ptIt != c.points.end() && n < cap; ++ptIt) {
            const auto& p = *ptIt;
            const double rel = map ? map->secondsAt(clip.startBeat + p.beat) * sr - sampleStart
                                   : clipStartS + p.beat * spb - blockStart;
            const int64_t off = static_cast<int64_t>(std::floor(rel + 1e-6));
            if (off >= end || off >= frames) break;
            if (off >= first) out[n++] = ExprEv{static_cast<int32_t>(off), -1, dim, p.value};
        }
        if (!ctlIsSwitch(c.ctl) && c.movesIn(beatAt(first), beatAt(end)))
            for (int32_t off = (first / kStep + 1) * kStep; off < end && n < cap; off += kStep)
                if (beatAt(off) >= firstBeat) out[n++] = ExprEv{off, -1, dim, c.valueAt(beatAt(off))};
    }
}

void EngineRuntime::clipCtlEndResets(const MidiClip& clip, double clipStartS, double clipEndS, double blockStart,
    int32_t frames, ExprEv* out, int& n, int cap) {
    if (clip.controllers.empty() || clipEndS <= clipStartS || clipEndS < blockStart || clipEndS >= blockStart + frames)
        return;
    const double lenBeats = clip.lengthBeats;
    const int32_t off = static_cast<int32_t>(clipEndS - blockStart);
    for (const auto& cp : clip.controllers) {
        if (!cp || cp->points.empty() || n >= cap) continue;
        if (!ctlIsSwitch(cp->ctl) && cp->ctl != kCtlPitchWheel) continue;
        if (cp->points.front().beat > lenBeats || cp->valueAt(lenBeats) == kCtlRest) continue;
        out[n++] = ExprEv{off, -1, kCtlDimBase + cp->ctl, kCtlRest};
    }
}

// Runs the block's sorted note events through the track's MIDI FX chain (bypass =
// passthrough). Ping-pongs between evs and scratch (both size >= 1024); the final
// list is left in evs. beatStart is the block's beat position for the active clock.
int EngineRuntime::applyMidiEffects(
    Track& t, MidiEv* evs, int n, MidiEv* scratch, int32_t frames, double beatStart, double spb, bool playing) {
    MidiEv* a = evs;
    MidiEv* b = scratch;
    for (auto& md : t.midiEffects) {
        if (!md) continue;
        int outN = 0;
        md->process(a, n, b, outN, 1024, frames, beatStart, spb, playing);
        std::swap(a, b);
        n = outN;
    }
    if (a != evs)
        for (int i = 0; i < n; ++i) evs[i] = a[i];
    return n;
}

// Map/CC routing: a MIDI effect (the arpeggiator) drives a target audio-device param
// on the same track from its per-step CC lane. Runs each block after the MIDI chain,
// before the device chain processes, so the modulation applies this block.
// An Arp grouped into a rack (a MIDI Effect Rack or an Instrument Rack chain) keeps driving
// its target, and a target grouped into a rack is reached through its nested address
// (specs/044); both walks read published snapshots only.
// The route comes from the published snapshot (Track::ccRoutes), never from the Arp's own fields:
// a group or rack edit retargets those before the new device order is published.
static void applyCcRoute(
    Track& t, const MidiDevice& md, const std::vector<RenderRoutingPlan::CompiledCcRouteEntry>* compiled) {
    Device* d = nullptr;
    int32_t pi = -1;
    if (compiled) {
        for (const auto& entry : *compiled) {
            if (entry.arp != &md) continue;
            const auto& ep = entry.target;
            pi = ep.paramIndex;
            if (pi < 0) return;
            if (ep.nested) {
                if (ep.rack && ep.rack->liveGeneration() == ep.expectedRackGen) d = ep.device;
                else
                    d = resolveNested(t, ep.host, ep.chainUid, ep.slot).device;
            } else {
                d = ep.device;
            }
            break;
        }
    } else {
        const Track::CcRoute* rt = nullptr;
        for (auto& r : *t.ccRoutes)
            if (r.arp == &md) {
                rt = &r;
                break;
            }
        if (!rt) return;
        const int di = rt->dev;
        pi = rt->param;
        if (pi < 0) return;
        if (rt->uid) d = resolveNested(t, di, rt->uid, rt->slot).device;
        else if (di >= 0 && di < static_cast<int>(t.devices.size()))
            d = t.devices[static_cast<size_t>(di)].get();
    }
    if (!d || pi >= d->paramCount()) return;
    const float mn = d->paramMin(pi), mx = d->paramMax(pi);
    const float mod = std::clamp(md.ccValue() * md.ccDepth(), 0.0f, 1.0f);
    d->setLiveParam(pi, mn + (mx - mn) * mod);
}

void EngineRuntime::applyMidiCcRouting(Track& t, const Graph* g, size_t trackIdx) {
    if (!t.ccRoutes) return;
    const auto* compiled =
        (g && g->routingPlan && trackIdx != SIZE_MAX) ? g->routingPlan->ccRoutesForTrack(trackIdx) : nullptr;
    for (auto& md : t.midiEffects) {
        if (!md) continue;
        applyCcRoute(t, *md, compiled);
        if (auto* r = md->asRack()) r->forEachChildMidi([&](MidiDevice& c) { applyCcRoute(t, c, compiled); });
    }
    if (auto* r = t.instrument ? t.instrument->asRack() : nullptr)
        r->forEachChildMidi([&](MidiDevice& c) { applyCcRoute(t, c, compiled); });
}

bool EngineRuntime::isUnderGroup(const Graph& g, const Track& t, int32_t groupId) {
    if (groupId < 0) return false;
    for (int32_t a = t.groupId(), hops = 0; a >= 0 && hops < 64; ++hops) {
        if (a == groupId) return true;
        int32_t parent = -1;
        for (auto& o : g.tracks)
            if (o->id() == a) {
                parent = o->groupId();
                break;
            }
        a = parent;
    }
    return false;
}

void EngineRuntime::drainCommands(Graph* g) {
    Command c;
    while (commands_.pop(c)) {
        switch (c.type) {
        case CommandType::SetToneEnabled: toneEnabled_ = (c.i0 != 0); break;
        case CommandType::SetFrequency: frequency_ = c.f0; break;
        case CommandType::TransportPlay:
            if (c.i0 > 0 && !transport_.isPlaying()) { // count-in first (i0 = bars)
                countInActive_ = true;
                countInPos_ = 0.0;
                countInTotal_ = static_cast<double>(c.i0) * std::max(1, transport_.beatsPerBar());
                lastCountInBeat_ = -1;
                uiCountIn_.store(countInTotal_, std::memory_order_relaxed);
                // Shut the capture here too, in command order: a Stop queued just before (a
                // retried take, #521) reopened the gate startCountIn() had shut.
                captureGate_.store(false, std::memory_order_release);
            } else {
                endCountIn();
                transport_.play();
                launchCountInSlot(); // Play pressed during a Session take's count-in
            }
            break;
        case CommandType::TransportStop:
            endCountIn();
            transport_.stop();
            if (hybrid_) hybrid_->invalidate(false);
            break;
        case CommandType::SetBpm:
            transport_.setBpm(c.d0);
            if (hybrid_) hybrid_->invalidate(false);
            break;
        case CommandType::SetTimeSignature: transport_.setTimeSignature(c.i0, c.i1); break;
        case CommandType::SetLoop:
            transport_.setLoop(c.i0 != 0, c.d0, c.d1);
            if (hybrid_) hybrid_->invalidate(false);
            break;
        case CommandType::SetMetronome: transport_.setMetronome(c.i0 != 0); break;
        case CommandType::ReleaseNote:
            if (c.i1 >= 0 && c.i1 < 128) {
                int k = 0, free = -1;
                while (k < 16 && releaseTrack_[k] != c.i0) {
                    if (free < 0 && releaseTrack_[k] < 0) free = k;
                    ++k;
                }
                if (k == 16) k = free >= 0 ? free : 0; // a full table reuses the first entry
                if (releaseTrack_[k] != c.i0) {
                    releaseTrack_[k] = c.i0;
                    for (auto& w : releasePitches_[k]) w = 0;
                }
                releasePitches_[k][c.i1 >> 5] |= 1u << (c.i1 & 31);
            }
            break;
        case CommandType::TrackNotesOff:
            if (notesOffCount_ < 8) notesOffTracks_[notesOffCount_++] = c.i0;
            break;
        case CommandType::CancelSessionLaunch: applyLaunchCancel(g, c); break;
        case CommandType::Seek:
            seekedWhileRolling_ |= transport_.isPlaying(); // a jump mid-playback, not Stop→Seek→Play
            transport_.seekBeats(c.d0);
            seekedThisBlock_ = true;
            if (hybrid_) hybrid_->invalidate(false);
            break;
        case CommandType::TakeRestart: // specs/078: the blocks from here belong to the retried take
            if ((c.i1 & 4) != 0) {     // a take started normally: the current epoch, nothing waits
                rtTakeEpoch_ = static_cast<uint32_t>(c.i0);
                rtWaitScene_ = -1;
            } else if ((c.i1 & 2) != 0) { // a Session MIDI take: from when its slot relaunches
                rtPendingEpoch_ = static_cast<uint32_t>(c.i0);
                rtWaitScene_ = static_cast<int32_t>(c.d0);
            } else {
                rtTakeEpoch_ = static_cast<uint32_t>(c.i0);
                rtTakeRestart_ = true;
                rtWaitScene_ = -1;
            }
            if ((c.i1 & 1) != 0) captureGate_.store(true, std::memory_order_release);
            break;
        case CommandType::None: break;
        }
    }
}

// The live-recording preview (#56): copy the notes being held into the take (live input,
// then routed MIDI) behind the sequence counter — odd while writing, so a reader that sees an
// odd or changed value retries. Wait-free and fixed-size; an idle engine with an empty snapshot writes nothing.
void EngineRuntime::publishHeldSnapshot(double nowBeat) {
    const bool rec = recording_.load(std::memory_order_relaxed);
    if (!rec && heldCount_.load(std::memory_order_relaxed) == 0) return; // idle: already empty
    const uint32_t s = heldSeq_.load(std::memory_order_relaxed);
    heldSeq_.store(s + 1, std::memory_order_relaxed);
    std::atomic_thread_fence(std::memory_order_release);
    int32_t n = 0;
    if (rec) {
        auto put = [&](int32_t pitch, int32_t source, double start, float vel) {
            HeldSnapSlot& h = heldSnap_[n++];
            h.pitch.store(pitch, std::memory_order_relaxed);
            h.source.store(source, std::memory_order_relaxed);
            h.startBeat.store(start, std::memory_order_relaxed);
            h.velocity.store(vel, std::memory_order_relaxed);
        };
        const RtMidiLane& L = rtMidiLanes_[0]; // the preview shows the first lane's track
        for (int p = 0; p < 128; ++p)
            if (L.pendingActive[p]) put(p, 0, L.pendingStart[p], L.pendingVel[p]);
        for (int p = 0; p < 128; ++p)
            if (L.recRoutedActive[p]) put(p, 1, L.recRoutedStart[p], L.recRoutedVel[p]);
    }
    heldCount_.store(n, std::memory_order_relaxed);
    heldNow_.store(nowBeat, std::memory_order_relaxed);
    heldSeq_.store(s + 2, std::memory_order_release);
}

void EngineRuntime::drainLiveMidi(double blockStartBeat, bool playing) {
    liveCount_ = 0;
    liveExprCount_ = 0;
    liveReleaseCount_ = 0;
    const bool rec = recording_.load(std::memory_order_relaxed);

    // When recording into a Session slot, time notes against the slot's own loop
    // clock (localBeats holds this block's start position) instead of the global
    // transport, so overdubs land at the right spot in the loop. During a count-in the
    // transport holds still and notes are timed back from the take start (#56) — for a slot
    // take, back from the slot's start (#158).
    SessionPlayer* slot = recordSlotPlayer_.load(std::memory_order_acquire);
    const double slotLen = recordSlotLen_.load(std::memory_order_relaxed);
    const bool timed = playing || countInActive_; // the timeline has a position for an event
    const double nowBeat = slot ? (countInActive_     ? countInClockOffset()
                                      : slotLen > 0.0 ? std::fmod(slot->localBeats, slotLen)
                                                      : slot->localBeats)
                                : blockStartBeat + countInClockOffset();
    captureNow_ = nowBeat;

    // The take's MIDI lanes (specs/086): a track-addressed note (Nota Remote) records on its own
    // track's lane; ordinary live input on every lane whose MIDI input filter it passes.
    rtMidiLaneCount_ = std::clamp(midiLaneCount_.load(std::memory_order_acquire), 0, kMaxMidiTakeLanes);
    for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
        const int32_t tid = midiLaneTrack_[static_cast<size_t>(k)].load(std::memory_order_relaxed);
        RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
        if (L.trackId != tid) { // another track's lane now (or closed): start clean
            L.clear();
            L.trackId = tid;
            rtLaneWasLive_[k] = false;
        }
        rtLaneSrc_[k] = midiLaneSrc_[static_cast<size_t>(k)].load(std::memory_order_relaxed);
        rtLaneCh_[k] = midiLaneCh_[static_cast<size_t>(k)].load(std::memory_order_relaxed);
        // The lane's clock: the take's (the first slot, or the transport); a Session slot that
        // joined the take (specs/086) times on its own loop and records once it plays.
        rtLaneNow_[k] = nowBeat;
        rtLaneSlotLen_[k] = slotLen;
        rtLaneSlot_[k] = slot != nullptr;
        rtLaneLive_[k] = true;
        const SessionLaneSlots* ls = laneSlotsHist_.live();
        SessionPlayer* lp = ls ? ls->player[static_cast<size_t>(k)] : nullptr;
        if (k > 0 && lp && lp != slot) {
            const double len = ls->len[static_cast<size_t>(k)];
            rtLaneSlot_[k] = true;
            rtLaneSlotLen_[k] = len;
            rtLaneLive_[k] = lp->playing.load(std::memory_order_relaxed) == ls->scene[static_cast<size_t>(k)];
            rtLaneNow_[k] = len > 0.0 ? std::fmod(lp->localBeats, len) : lp->localBeats;
        }
        rtLaneJustLive_[k] = rec && rtLaneLive_[k] && !rtLaneWasLive_[k];
        rtLaneWasLive_[k] = rec && rtLaneLive_[k];
    }
    // A retried Session MIDI take begins when its slot has relaunched: until then what is
    // played carries the old epoch and stays out (specs/078).
    if (rtWaitScene_ >= 0 && slot && slot->pendingSlot() != rtWaitScene_ &&
        slot->playing.load(std::memory_order_relaxed) == rtWaitScene_) {
        rtTakeEpoch_ = rtPendingEpoch_;
        rtTakeRestart_ = true;
        rtWaitScene_ = -1;
    }
    // A retry (TakeRestart) starts a fresh take even when this thread never saw recording off.
    const bool restarted = rtTakeRestart_;
    rtTakeRestart_ = false;
    const bool takeStarted = rec && (!prevRecording_ || restarted);
    routedSeedPending_ = takeStarted && !slot; // routed notes held now join the take (captureRoutedNotes)
    // A retried take restarts from a playhead that jumped back: keys still held join it at its
    // start, not at a press beat from the take that was thrown away.
    routedSeedFromNow_ = restarted;
    if (rec != prevRecording_ || restarted)
        for (auto& L : rtMidiLanes_) L.clear();
    if (takeStarted) { // a new take: pass 0, the loop record mode as set now
        recordPass_ = 0;
        passTriggered_ = false;
        passReplace_ = loopRecordReplace_.load(std::memory_order_relaxed);
        replaceUpTo_.store(0, std::memory_order_release);
    }
    prevRecording_ = rec;
    // Every live event plays at offset 0 and the render sorts offs ahead of ons at one
    // offset, so a pitch's on and off landing in the same block (a quick tap, a finger
    // sliding over pads, a phone's notes arriving in one Wi-Fi burst) would play
    // off-then-on and hang the note. A pitch's second event — and anything past the
    // block's capacity — waits for the next block, with everything queued behind it.
    uint32_t seen[4] = {};
    MidiEvent e;
    for (;;) {
        if (liveCarried_) {
            e = liveCarry_;
            liveCarried_ = false;
        } else if (!liveMidi_.pop(e) && !remoteMidi_.pop(e))
            break;
        if (e.expr >= 0) { // expression: keep the latest per (track, pitch, dim), never carried
            int k = 0;
            while (k < liveExprCount_ &&
                   (liveExpr_[k].pitch != e.pitch || liveExpr_[k].expr != e.expr || liveExpr_[k].trackId != e.trackId ||
                       liveExpr_[k].source != e.source || liveExpr_[k].channel != e.channel)) // per input (specs/086)
                ++k;
            if (k < liveExprCount_) liveExpr_[k].velocity = e.velocity;
            else if (liveExprCount_ < kMaxLiveExpr)
                liveExpr_[liveExprCount_++] = e;
            continue;
        }
        if (e.expr == kLiveReleaseExpr) { // a track disarmed: release, the keys stay down
            if (liveReleaseCount_ >= kMaxLiveRelease) {
                liveCarry_ = e;
                liveCarried_ = true;
                break;
            }
            liveRelease_[liveReleaseCount_++] = e.trackId;
            continue;
        }
        const bool valid = e.pitch >= 0 && e.pitch < 128;
        const uint32_t bit = valid ? 1u << (e.pitch & 31) : 0u;
        if (liveCount_ >= kMaxLive || (valid && (seen[e.pitch >> 5] & bit))) {
            liveCarry_ = e;
            liveCarried_ = true;
            break;
        }
        if (valid) seen[e.pitch >> 5] |= bit;
        liveEvents_[liveCount_++] = e;
        if (valid) { // keys down, recording or not (seeds a take started while they're held)
            liveDown_[e.pitch] = e.on;
            if (e.on) {
                livePressBeat_[e.pitch] = timed ? nowBeat : std::numeric_limits<double>::quiet_NaN();
                livePressVel_[e.pitch] = e.velocity;
                livePressTrack_[e.pitch] = e.trackId;
                livePressSource_[e.pitch] = e.source;
                livePressChannel_[e.pitch] = e.channel;
            }
        }
        if (rec && timed && valid)
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
                // A note the lane holds ends on its off from the input that played it, even if the
                // lane's input changed since.
                if (!laneTakes(k, e) && (e.on || !L.pendingActive[e.pitch] || L.pendingSource[e.pitch] != e.source ||
                                            L.pendingChannel[e.pitch] != e.channel))
                    continue;
                if (e.on) {
                    if (!rtLaneLive_[k]) continue; // its slot hasn't started yet
                    L.pendingActive[e.pitch] = true;
                    L.pendingStart[e.pitch] = rtLaneNow_[k];
                    L.pendingVel[e.pitch] = e.velocity;
                    L.pendingSource[e.pitch] = e.source;
                    L.pendingChannel[e.pitch] = e.channel;
                    if (!rtLaneSlot_[k]) triggerPassReplace();
                } else if (L.pendingActive[e.pitch]) {
                    double len = rtLaneNow_[k] - L.pendingStart[e.pitch];
                    if (rtLaneSlot_[k] && len < 0.0) len += rtLaneSlotLen_[k]; // wrapped past the loop end
                    RecordedNote rn{e.pitch, L.pendingStart[e.pitch], len, L.pendingVel[e.pitch], recordPass_};
                    rn.lane = k;
                    pushRecorded(rn);
                    L.pendingActive[e.pitch] = false;
                }
            }
    }
    // Keys still down once this block's events are in join a take that starts here, from
    // where they started: their press beat if the transport was rolling (or counting in),
    // else the take's first moment — the count-in's first beat, or the record start. A slot
    // take starts them at the slot's current position. Seeding after the drain keeps out a
    // key whose release was already queued when Record was pressed.
    // A slot that joined the take picks up the keys held when it starts recording, too.
    bool laneStarted = false;
    for (int32_t k = 0; k < rtMidiLaneCount_; ++k) laneStarted |= rtLaneJustLive_[k];
    if (takeStarted || laneStarted)
        for (int p = 0; p < 128; ++p) {
            if (!liveDown_[p]) continue;
            MidiEvent held{true, p, livePressVel_[p], livePressTrack_[p]};
            held.source = livePressSource_[p];
            held.channel = livePressChannel_[p];
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
                if (L.pendingActive[p] || !rtLaneLive_[k] || !(takeStarted || rtLaneJustLive_[k]) ||
                    !laneTakes(k, held))
                    continue;
                L.pendingActive[p] = true;
                L.pendingStart[p] =
                    (rtLaneSlot_[k] || restarted || std::isnan(livePressBeat_[p])) ? rtLaneNow_[k] : livePressBeat_[p];
                L.pendingVel[p] = livePressVel_[p];
                L.pendingSource[p] = held.source;
                L.pendingChannel[p] = held.channel;
            }
        }
    if (rec && timed && liveExprCount_ > 0) captureLiveExpr(); // on each lane's clock

    // Recorded controllers (specs/020): the block's coalesced instrument-wide controllers that
    // reach the take, on the capture clock. A take starts with every controller already away
    // from rest (a pedal held down when Record is pressed), at the record start.
    // Each lane from the inputs its track listens to: a pedal held on one keyboard doesn't seed
    // the take of a track that plays from another (specs/086).
    if (rec && takeStarted) {
        for (int32_t c = 0; c < kCtlCount; ++c) {
            if (ctlIsReserved(c) || liveCtlTouch_[c].load(std::memory_order_relaxed) == 0) continue; // never moved
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                if (rtMidiLanes_[static_cast<size_t>(k)].trackId < 0 || !rtLaneLive_[k]) continue;
                const float v = laneStartCtl(k, c);
                if (v == kCtlRest) continue;
                RecordedCtl x{c, rtLaneSlot_[k] ? rtLaneNow_[k] : blockStartBeat, v, recordPass_};
                x.lane = k;
                pushRecordedCtl(x);
            }
        }
    }
    if (rec && timed)
        for (int i = 0; i < liveExprCount_; ++i) {
            const MidiEvent& ex = liveExpr_[static_cast<size_t>(i)];
            if (ex.expr < kCtlDimBase) continue;
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                if (!rtLaneLive_[k] || !laneTakes(k, ex)) continue;
                RecordedCtl x{ex.expr - kCtlDimBase, rtLaneNow_[k], ex.velocity, recordPass_};
                x.lane = k;
                pushRecordedCtl(x);
            }
        }
}

// Does this block's lane `lane` record `e`? A track-addressed event only on its own track's
// lane; live input through the lane track's MIDI input filter (specs/086).
bool EngineRuntime::laneTakes(int32_t lane, const MidiEvent& e) const {
    const int32_t tid = rtMidiLanes_[static_cast<size_t>(lane)].trackId;
    if (tid < 0) return false;
    if (e.trackId >= 0) return e.trackId == tid;
    const int32_t s = rtLaneSrc_[lane], c = rtLaneCh_[lane];
    return (s == Track::kMidiAllInputs || (e.source >= 0 && s == e.source)) &&
           (c == 0 || e.source < 0 || c == e.channel + 1);
}

float EngineRuntime::laneStartCtl(int32_t lane, int32_t ctl) const {
    const int32_t tid = rtMidiLanes_[static_cast<size_t>(lane)].trackId;
    const int32_t s = rtLaneSrc_[lane], c = rtLaneCh_[lane];
    uint32_t best = 0;
    float v = kCtlRest;
    auto take = [&](const LiveCtlIn& in) {
        const uint32_t st = in.stamp.load(std::memory_order_relaxed);
        if (st <= best) return;
        best = st;
        v = in.value.load(std::memory_order_relaxed);
    };
    if (liveCtlTrack_[ctl].channel.load(std::memory_order_relaxed) == tid) take(liveCtlTrack_[ctl]);
    for (int32_t i = 0; i < kCtlInputs; ++i) {
        const int32_t src = i - 1; // as laneTakes: no device passes the all-inputs filter only
        const LiveCtlIn& in = liveCtlIn_[i][ctl];
        if (in.stamp.load(std::memory_order_relaxed) <= best) continue;
        if (s != Track::kMidiAllInputs && (src < 0 || s != src)) continue;
        if (c != 0 && src >= 0 && c != in.channel.load(std::memory_order_relaxed) + 1) continue;
        take(in);
    }
    return v;
}

int32_t EngineRuntime::midiLaneOfRt(int32_t trackId) const {
    if (!recording_.load(std::memory_order_relaxed)) return -1;
    const int32_t n = std::clamp(midiLaneCount_.load(std::memory_order_acquire), 0, kMaxMidiTakeLanes);
    for (int32_t k = 0; k < n; ++k)
        if (midiLaneTrack_[static_cast<size_t>(k)].load(std::memory_order_relaxed) == trackId) return k;
    return -1;
}

// Recorded MPE: the block's (coalesced) live expression for a note being recorded becomes a
// point on that note's curve, timed from its start. Notes still held keep growing theirs; the
// message thread pairs them with the RecordedNote at note-off (poll). Audio thread.
void EngineRuntime::captureLiveExpr() {
    for (int i = 0; i < liveExprCount_; ++i) {
        const MidiEvent& e = liveExpr_[i];
        if (e.pitch < 0 || e.pitch > 127 || e.expr < 0 || e.expr >= kExprDims)
            continue; // instrument-wide: not per note
        for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
            const RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
            if (!L.pendingActive[e.pitch] || !laneTakes(k, e)) continue;
            double off = rtLaneNow_[k] - L.pendingStart[e.pitch];
            if (rtLaneSlot_[k] && rtLaneSlotLen_[k] > 0.0 && off < 0.0)
                off += rtLaneSlotLen_[k]; // wrapped past the loop end
            RecordedExpr x{
                e.pitch, L.pendingStart[e.pitch], e.expr, static_cast<float>(std::max(0.0, off)), e.velocity};
            x.lane = k;
            pushRecordedExpr(x);
        }
    }
}

// Print routed-in MIDI (a source's post-FX note events) into the record track's take, pairing
// note-on/off across blocks into RecordedNotes at absolute beats. Audio thread; recorded_ has a
// single producer (this + drainLiveMidi both run in processBlock).
// Keeps the routed notes sounding into an armed destination (on/off pairs from its source's
// post-FX MIDI), timed on the capture clock, for seeding a take started while they're held.
void EngineRuntime::trackRoutedDown(
    int32_t ownerId, const MidiEv* evs, int n, double blockStart, double spb, bool timed) {
    if (routedOwner_ != ownerId) {
        for (int p = 0; p < 128; ++p) routedDown_[p] = false;
        routedOwner_ = ownerId;
    }
    for (int k = 0; k < n; ++k) {
        const int p = evs[k].pitch;
        if (p < 0 || p >= 128) continue;
        routedDown_[p] = evs[k].on;
        if (evs[k].on) {
            routedPressBeat_[p] = timed && spb > 0.0 ? (blockStart + evs[k].off) / spb + countInClockOffset()
                                                     : std::numeric_limits<double>::quiet_NaN();
            routedPressVel_[p] = evs[k].vel;
        }
    }
}

void EngineRuntime::captureRoutedNotes(
    int32_t lane, const MidiEv* evs, int n, double blockStart, double spb, double clockOffset) {
    RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(lane)];
    for (int k = 0; k < n; ++k) {
        const int p = evs[k].pitch;
        if (p < 0 || p >= 128) continue;
        const double beat = (blockStart + evs[k].off) / spb + clockOffset; // count-in: before the take start
        if (evs[k].on) {
            L.recRoutedActive[p] = true;
            L.recRoutedStart[p] = beat;
            L.recRoutedVel[p] = evs[k].vel;
            if (!recordSlotPlayer_.load(std::memory_order_relaxed)) triggerPassReplace();
        } else if (L.recRoutedActive[p]) {
            RecordedNote rn{
                p, L.recRoutedStart[p], std::max(0.0, beat - L.recRoutedStart[p]), L.recRoutedVel[p], recordPass_};
            rn.lane = lane;
            pushRecorded(rn);
            L.recRoutedActive[p] = false;
        }
    }
    // A take that starts this block takes in the routed notes still sounding after its events:
    // from their press beat if the transport was rolling (or counting in), else from now.
    if (routedSeedPending_) {
        routedSeedPending_ = false;
        const double now = blockStart / spb + clockOffset;
        if (routedOwner_ == L.trackId)
            for (int p = 0; p < 128; ++p) {
                if (!routedDown_[p] || L.recRoutedActive[p]) continue;
                L.recRoutedActive[p] = true;
                L.recRoutedStart[p] =
                    (routedSeedFromNow_ || std::isnan(routedPressBeat_[p])) ? now : routedPressBeat_[p];
                L.recRoutedVel[p] = routedPressVel_[p];
            }
    }
}

} // namespace nota::detail
