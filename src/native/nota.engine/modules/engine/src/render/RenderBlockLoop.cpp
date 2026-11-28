// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "session/SessionOps.h"
#include "model/Graph.h"
#include "model/EngineTypes.h"
#include "core/Transport.h"
#include "render/RenderHelpers.h"
#include "render/RenderGraphExecutor.h"
#include "render/RenderRoutingPlan.h"
#include "core/TempoMap.h"
#include "model/TimelineIndex.h"
#include "core/EnginePrelude.h"
#include "session/SessionPlayer.h"
#include "session/SessionPass.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace nota::detail {

// Master pan over one chunk (specs/099): gains ramp linearly from the last chunk's, so a
// pan change never steps; centre with nothing to ramp is skipped (bit-exact pass-through).
// After a seek, a start or a loop wrap (an export starts with one) masterPanSnap first sets
// the gains for where the playhead landed: there is nothing continuous to ramp from.
void EngineRuntime::masterPanSnap(float pan, PanLaw law) {
    const render_detail::PanGains g = masterPanGains(pan, law);
    masterPanG_[0] = g.ll, masterPanG_[1] = g.lr, masterPanG_[2] = g.rl, masterPanG_[3] = g.rr;
}

void EngineRuntime::masterPanStage(float* out, int32_t n, float pan, PanLaw law) {
    const render_detail::PanGains to = masterPanGains(pan, law);
    float* f = masterPanG_;
    const bool same = f[0] == to.ll && f[1] == to.lr && f[2] == to.rl && f[3] == to.rr;
    if (same && to.ll == 1.0f && to.lr == 0.0f && to.rl == 0.0f && to.rr == 1.0f) return;
    const float inv = n > 0 ? 1.0f / static_cast<float>(n) : 1.0f;
    for (int32_t i = 0; i < n; ++i) {
        const float x = same ? 1.0f : static_cast<float>(i + 1) * inv;
        const float ll = f[0] + (to.ll - f[0]) * x, lr = f[1] + (to.lr - f[1]) * x;
        const float rl = f[2] + (to.rl - f[2]) * x, rr = f[3] + (to.rr - f[3]) * x;
        const float inL = out[i * 2], inR = out[i * 2 + 1];
        out[i * 2] = ll * inL + lr * inR;
        out[i * 2 + 1] = rl * inL + rr * inR;
    }
    f[0] = to.ll, f[1] = to.lr, f[2] = to.rl, f[3] = to.rr;
}

void EngineRuntime::render(float* out, float* cue, int32_t numFrames) {
    // Counted before the suspend check (both seq_cst, paired with start()): once start() has
    // suspended rendering and seen the count at zero, no render can be inside the graph.
    rendersInFlight_.fetch_add(1, std::memory_order_seq_cst);
    struct Leave {
        std::atomic<int32_t>& n;
        ~Leave() { n.fetch_sub(1, std::memory_order_release); }
    } leave{rendersInFlight_};
    // start() is still re-preparing the nodes on the control thread: output silence.
    if (renderSuspended_.load(std::memory_order_seq_cst)) {
        std::fill(out, out + static_cast<size_t>(numFrames) * 2, 0.0f);
        if (cue) std::fill(cue, cue + static_cast<size_t>(numFrames) * 2, 0.0f);
        return;
    }
    ScopedNoDenormals noDenormals;
    const auto t0 = std::chrono::steady_clock::now();
    deviceBlock_ = true; // a deadline, whatever the gap since the last block
    phaseNs_ = 0.0;
    processBlock(out, cue, numFrames);
    const auto t1 = std::chrono::steady_clock::now();
    // The transport's rate, as mixGraph sizes by (a device-block render in tests has no device).
    if (phaseNs_ > 0.0 && numFrames > 0 && transport_.sampleRate() > 0.0)
        adjustPoolSizing(static_cast<double>(std::chrono::duration_cast<std::chrono::nanoseconds>(t1 - t0).count()),
            1e9 * numFrames / transport_.sampleRate());
    // DSP load = render time / block budget, smoothed (Phase 11). Live-device
    // path only; renderOffline stays untimed.
    const double sr = io_.sampleRate();
    if (sr > 0.0 && numFrames > 0) {
        const double elapsed = std::chrono::duration<double>(t1 - t0).count();
        const double load = elapsed / (numFrames / sr);
        const float prev = cpuLoad_.load(std::memory_order_relaxed);
        cpuLoad_.store(static_cast<float>(prev * 0.9 + load * 0.1), std::memory_order_relaxed);
    }
}

// After a device callback that rendered leaves: what the callback spent outside them (its
// serial work), and — while the pool runs — its sizing (see mixGraph).
// The parallel phase has the buffer less the callback's serial work (serial tracks, groups,
// returns, the master): that is its time. The sizing grows by a quarter when callbacks keep
// coming in late with the phase over its time — the work grew while the pool ran — a share,
// smoothed over ~32 blocks, past kOverrunShare. A callback that made its deadline doesn't count,
// however its time split: a late wake-up or a preempted thread stretches the phase or the
// serial part for a block, and more threads only make that likelier. A phase that overran
// while the serial work alone made the callback late doesn't count either: helpers can't
// shorten that. The sizing never passes the pool's own over-reading task time (the old
// sizing), and eases back toward the single-thread cost while phases take under half their time.
void EngineRuntime::adjustPoolSizing(double totalNs, double periodNs) {
    const double serial = std::max(0.0, totalNs - phaseNs_);
    serialNs_ += (serial - serialNs_) / 16.0;
    if (!rtParallel_) return;
    const double room = std::max(0.25 * periodNs, periodNs - serialNs_);
    const bool overrun = totalNs > periodNs && phaseNs_ > room && serial < periodNs;
    overrunShare_ += ((overrun ? 1.0 : 0.0) - overrunShare_) / 32.0;
    if (overrunShare_ > kOverrunShare) {
        sizeWorkNs_ = std::min(std::max(sizeWorkNs_, leafWorkNs_) * 5 / 4, std::max(parWorkNs_, leafWorkNs_));
        overrunShare_ = 0.0;
    } else if (phaseNs_ < 0.5 * room) {
        const int64_t floorNs = std::min(leafWorkNs_, parWorkNs_);
        if (sizeWorkNs_ > floorNs) sizeWorkNs_ -= (sizeWorkNs_ - floorNs) / 64;
    } else if (!overrun && overrunShare_ < 0.5 * kOverrunShare) {
        const int64_t ceilNs = std::max(leafWorkNs_, parWorkNs_);
        if (sizeWorkNs_ > ceilNs) sizeWorkNs_ -= (sizeWorkNs_ - ceilNs) / 16;
    }
}

void EngineRuntime::renderOffline(float* out, int32_t frames, double sampleRate) {
    if (sampleRate > 0.0) transport_.setSampleRate(sampleRate);
    renderOffline(out, frames);
}

void EngineRuntime::renderOffline(float* out, int32_t frames) {
    // Offline rendering re-prepares and processes the graph on the calling thread — with the
    // device live that would race the audio thread. Callers stop the backend first; enforce it.
    if (io_.isRunning()) {
        std::fill(out, out + static_cast<size_t>(frames) * 2, 0.0f);
        return;
    }
    io_.stopPreview(); // audition must never bleed into a bounce (M7-4a)
    if (transport_.sampleRate() <= 0) transport_.setSampleRate(44100.0);
    const double sr = transport_.sampleRate();
    // Re-prepare every node for the render rate ONCE, only when it actually changed (an export
    // bounces in chunks: re-preparing every chunk would reset each instrument/device/plugin's
    // DSP state and click at each block seam). This also re-rates the master + racks + MIDI FX
    // and reconfigures warp streams (their pitch/window sizes are baked from the device rate —
    // a higher export rate would otherwise play warped clips too fast).
    if (sr != preparedSR_) {
        propagateSampleRate(sr);
        clips().reconfigureAllWarpStreams();
        devices().recomputePdc();
        preparedSR_ = sr;
    }
    const int32_t need = std::clamp(frames, 1, kMaxBlock);
    if (need > preparedBlockCap_) ensureBlockCapacity(need);
    deviceBlock_ = false;               // export/freeze: no deadline, every render thread
    processBlock(out, nullptr, frames); // no Cue in a bounce (specs/089 FR-016)
}

void EngineRuntime::endCountIn() {
    countInActive_ = false;
    uiCountIn_.store(0.0, std::memory_order_relaxed);
    captureGate_.store(true, std::memory_order_relaxed);
}

// A Session take counted in (#158): its slot starts as the transport rolls, on that sample,
// rather than on the next launch quantum. No-op when no take is waiting.
void EngineRuntime::launchCountInSlot() {
    if (const int32_t sc = countInLaunchScene_.exchange(-1, std::memory_order_acq_rel); sc >= 0)
        if (auto* sp = recordSlotPlayer_.load(std::memory_order_acquire)) sp->requestLaunch(sc, 0.0);
}

void EngineRuntime::applyLaunchCancel(Graph* g, const Command& c) {
    if (!g) return;
    const uint64_t target =
        static_cast<uint64_t>(static_cast<uint32_t>(c.i0)) | (static_cast<uint64_t>(static_cast<uint32_t>(c.i1)) << 32);
    // A whole-scene cancel spares a scene launch that has started on any track (clips may have their
    // own quanta), so a scene never ends up half launched; the whole batch is drained before any
    // track applies its request, so every target sees the same answer. A slot cancel (d1 = 0) is the
    // performer's choice for that slot and withdraws it either way.
    if (const int32_t gen = SessionPlayer::genOf(target); gen > 0 && c.d1 != 0.0)
        for (auto& t : g->tracks)
            if (t->sessionPlayer && t->sessionPlayer->appliedGen.load(std::memory_order_relaxed) == gen) return;
    const int32_t trackId = static_cast<int32_t>(c.d0);
    for (auto& t : g->tracks)
        if (t->id() == trackId) {
            if (t->sessionPlayer) t->sessionPlayer->withdraw(target);
            return;
        }
}

static inline bool sessionActiveOf(const Track& t) {
    return t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0;
}

// The transport loop just wrapped (audio thread, #29). MIDI FX keep their pattern and pending
// offs across the seam instead of being reset. A take recording into the Arrangement splits each
// note held across the seam: the part played so far ends at the loop end, and the note carries
// on from the loop start (a key pressed just before the seam — a head under 1/64 beat — moves
// to the loop start whole). A Session slot take times notes on its own loop clock: untouched.
void EngineRuntime::onLoopWrap(Graph* g, double loopStartBeat, double loopEndBeat) {
    const double jump = loopStartBeat - loopEndBeat;
    if (rtPassSerial_ != 0) // #971: what plays ends at the loop end and goes on from its start
        if (const uint32_t ps = passSerial_.load(std::memory_order_acquire); ps == rtPassSerial_)
            session().pushPassEvent({LaunchEvent::Seam, 0, -1, loopEndBeat, loopStartBeat, 1.0f, ps});
    if (g)
        for (auto& tptr : g->tracks)
            if (tptr && tptr->instrument && !sessionActiveOf(*tptr)) {
                for (auto& md : tptr->midiEffects)
                    if (md) md->timeJump(jump);
                tptr->instrument->midiTimeJump(jump); // MIDI effects in a rack's chains (#329)
            }

    if (!recording_.load(std::memory_order_relaxed) || recordSlotPlayer_.load(std::memory_order_acquire)) return;
    constexpr double kMinHead = 1.0 / 64.0;
    auto split = [&](int32_t lane, int32_t pitch, double& start, float vel) {
        const double head = loopEndBeat - start;
        RecordedNote rn{pitch, start, head, vel, recordPass_};
        rn.lane = lane;
        if (head >= kMinHead) pushRecorded(rn);
        start = loopStartBeat;
    };
    for (int32_t k = 0; k < rtMidiLaneCount_; ++k) { // every lane of the take (specs/086)
        RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
        for (int p = 0; p < 128; ++p)
            if (L.pendingActive[p]) split(k, p, L.pendingStart[p], L.pendingVel[p]);
        for (int p = 0; p < 128; ++p)
            if (L.recRoutedActive[p]) split(k, p, L.recRoutedStart[p], L.recRoutedVel[p]);
        // Controllers being replaced over a range stop at the pass end (specs/020).
        RecordedCtl x{-1, loopEndBeat, 0.0f, recordPass_};
        x.lane = k;
        pushRecordedCtl(x);
    }
    // A new pass of the take: the continuations above belong to it (they don't trigger a
    // replace — only a fresh note does), and it takes the loop record mode set right now.
    ++recordPass_;
    passTriggered_ = false;
    passReplace_ = loopRecordReplace_.load(std::memory_order_relaxed);
}

// Replace mode: the first fresh note of a pass after the first removes the take's earlier
// passes. Published once per pass; collectRecorded reads it before draining recorded_, so every
// earlier-pass note queued before it is in hand.
void EngineRuntime::triggerPassReplace() {
    if (!passReplace_ || passTriggered_ || recordPass_ <= 0) return;
    passTriggered_ = true;
    replaceUpTo_.store(recordPass_, std::memory_order_release);
}

// The master section when the master is more than a wire (specs/099): devices, lanes and the
// pan stage, per chunk. Out of line on purpose: inlined into processBlock it took the
// registers of the output loop below it (the perf gate's GCC build spilled the clamp bound
// there, two loads a sample).
#if defined(__GNUC__) || defined(__clang__)
[[gnu::noinline]]
#elif defined(_MSC_VER)
__declspec(noinline)
#endif
void EngineRuntime::processMasterChain(Graph& g, float* out, int32_t numFrames, double spb, int32_t nAnchors) {
    const SegAnchor* const anchors = segAnchors_;
    Track& master = *g.masterTrack;
    const bool mappedMaster = transport_.hasTempoMap();
    const bool lanes = !master.automation.empty();
    int32_t ai = 0;
    for (int32_t off = 0; off < numFrames;) {
        while (ai + 1 < nAnchors && anchors[ai + 1].off <= off) ++ai;
        const SegAnchor& an = anchors[ai];
        const int32_t pieceEnd = ai + 1 < nAnchors ? anchors[ai + 1].off : numFrames;
        const int32_t n = std::min({numFrames - off, kMaxBlock, pieceEnd - off});
        const bool snap = an.jump && off == an.off; // first chunk after a seek / start / wrap
        const double s0 = an.samples + (an.rolling ? off - an.off : 0);
        const double s1 = s0 + (an.rolling ? n : 0);
        double beat = 0.0, endBeat = 0.0, chunkSpb = spb;
        float pan = std::clamp(master.pan(), -1.0f, 1.0f), startPan = pan;
        if (spb > 0.0) {
            if (mappedMaster) {
                beat = transport_.beatAtSamples(s0);
                endBeat = transport_.beatAtSamples(s1);
                chunkSpb = endBeat > beat ? n / (endBeat - beat) : spb;
            } else { // fixed tempo: the exact spb, so synced devices never see rounding as a tempo change
                beat = s0 / spb;
                endBeat = s1 / spb;
            }
            if (lanes) {
                automation().applyMasterLanes(master, beat);
                // The pan stage ramps to the lane's value at the chunk's end, so a drawn
                // pan sweep is followed exactly, not one chunk late.
                if (const TrackLane* pl =
                        g.routingPlan ? g.routingPlan->masterPanLane() : automation().masterPanLane(master)) {
                    startPan = std::clamp(pl->valueAt(beat), -1.0f, 1.0f);
                    pan = std::clamp(pl->valueAt(endBeat), -1.0f, 1.0f);
                }
            }
        }
        for (auto& d : master.devices) {
            if (!d || d->bypassed()) continue;
            if (spb > 0.0) d->setTransport(beat, chunkSpb, an.rolling);
            d->process(out + off * 2, n);
        }
        if (snap) masterPanSnap(startPan, g.panLaw); // from where the playhead landed
        masterPanStage(out + off * 2, n, pan, g.panLaw);
        off += n;
    }
}

void EngineRuntime::processBlock(float* out, float* cue, int32_t numFrames) {
    // Every audio-thread read of recordSlotPlayer_ happens inside this block (count-in launch,
    // live MIDI, capture), so this scope keeps the player it points at alive.
    const SnapshotHistory<SessionPlayer>::RenderScope recordSlotScope(recordSlotHist_);
    const SnapshotHistory<SessionLaneSlots>::RenderScope laneSlotScope(laneSlotsHist_); // specs/086
    const SnapshotHistory<Graph>::RenderScope graphScope(graphHist_);
    Graph* g = graphHist_.live();
    const bool tempoChanged = transport_.attachTempoMap(g ? g->tempoMap.get() : nullptr);
    struct TempoScope {
        Transport& t;
        ~TempoScope() { t.detachTempoMap(); }
    } tempoScope{transport_};
    if (tempoChanged && transport_.isPlaying()) seekedThisBlock_ = seekedWhileRolling_ = true;
    drainCommands(g);
    for (int32_t i = 0; i < numFrames * 2; ++i) out[i] = 0.0f;
    if (cue) std::fill(cue, cue + static_cast<size_t>(numFrames) * 2, 0.0f);

    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    bool playing = transport_.isPlaying(); // flips to true mid-block when a count-in runs out

    drainLiveMidi(transport_.positionBeats(), playing);

    // The graph, then this block's scratch: publishRaw publishes the scratch first, so it
    // always fits the graph seen here. Both held until their scopes end the render epoch.
    const SnapshotHistory<RenderScratch>::RenderScope scratchScope(scratchHist_);
    rs_ = scratchHist_.live();

    auto flushArrangementVoices = [&] {
        if (g)
            for (auto& tptr : g->tracks)
                if (tptr->instrument && !sessionActiveOf(*tptr)) {
                    tptr->liveHeld->midiKey = {};
                    tptr->instrument->allNotesOff();
                    if (tptr->liveHeld) tptr->liveHeld->forgetRouted(); // #504
                    releaseHeldControllers(*tptr->instrument);          // a clip's pedal / wheel (specs/020)
                    for (auto& md : tptr->midiEffects)
                        if (md) md->reset(); // flush arp held/pending
                }
    };
    // Flush stuck instrument voices on a play→stop edge (pause/stop), or on a seek while
    // rolling: otherwise a held note keeps ringing forever since its note-off never arrives.
    const bool seeked = seekedThisBlock_, seekedRolling = seekedWhileRolling_;
    seekedThisBlock_ = seekedWhileRolling_ = false;
    if ((!playing && renderWasPlaying_) || (playing && renderWasPlaying_ && seekedRolling)) flushArrangementVoices();
    // #971: the transport stopped under a running pass — what played ends here; a new roll
    // re-reports what plays then.
    if (!playing && renderWasPlaying_ && rtPassSerial_ != 0) {
        if (const uint32_t ps = passSerial_.load(std::memory_order_acquire); ps == rtPassSerial_)
            session().pushPassEvent({LaunchEvent::Stop, 0, -1, rtPassEndBeat_, 0.0, 1.0f, ps});
        rtPassSerial_ = 0;
    }
    // A device moved between tracks (specs/043): the moved instrument's held notes, or the
    // voices an arp drove on the track it left, stop here, on the thread that owns them.
    if (g)
        for (int32_t k = 0; k < notesOffCount_; ++k)
            for (auto& tptr : g->tracks)
                if (tptr->id() == notesOffTracks_[k]) {
                    tptr->liveHeld->midiKey = {};
                    if (tptr->instrument) {
                        tptr->instrument->allNotesOff();
                        releaseHeldControllers(*tptr->instrument);
                        if (tptr->liveHeld) tptr->liveHeld->forgetRouted(); // #504
                    }
                    for (auto& md : tptr->midiEffects)
                        if (md) md->reset();
                }
    notesOffCount_ = 0;
    // Tracks disarmed with keys down (#366): they release on this render.
    if (g)
        for (int k = 0; k < liveReleaseCount_; ++k)
            for (auto& tptr : g->tracks)
                if (tptr->id() == liveRelease_[k] && tptr->liveHeld) tptr->liveHeld->release = true;
    // Note chase: when playback starts, the playhead jumps (seek) or the loop wraps, clip
    // notes that began before the playhead but are still held get re-triggered at the
    // jump point — otherwise a note you start in the middle of stays silent.
    bool chase = playing && (!renderWasPlaying_ || seeked);
    if (chase) {
        const double b = transport_.positionBeats() * transport_.timeSigDenom() / 4.0;
        const auto whole = static_cast<int64_t>(std::floor(b));
        lastBeatEmitted_ = b - static_cast<double>(whole) < 1e-8 ? whole - 1 : whole;
        clickRemaining_ = 0;
    }
    renderWasPlaying_ = playing;

    // Split the block at loop boundaries so the loop is sample-accurate: nothing
    // past the loop end sounds, and the playhead lands exactly on loopStart so a
    // note at the loop start (beat 0) retriggers every cycle.
    int32_t done = 0;
    int32_t loopSeams[16];
    int32_t nLoopSeams = 0; // block offsets where the loop wrapped
    double presentationStart = transport_.playheadSamples();
    int32_t presentationFrames = 0, presentationLead = 0;
    // Where each segment starts on the timeline (specs/099): the master section runs after this
    // loop and reads its lanes / tempo from these, so a loop wrap or a count-in ending inside the
    // block puts each master chunk at the beat that actually sounded there. `jump`: the playhead
    // got there by a seek, a start or a wrap (the master pan snaps instead of ramping).
    // (a member, not a local: a compiler that zero-fills locals would clear it every block)
    SegAnchor* const anchors = segAnchors_;
    int32_t nAnchors = 0;
    while (done < numFrames) {
        transport_.rejoinLoop(); // a clock change put a played-on playhead back in the loop
        if (playing && transport_.wrapsAtLoopEnd() &&
            transport_.playheadSamples() >= transport_.loopEndSamples() - 0.5) {
            // No voice flush here: clip notes crossing the loop end get note-offs in the next
            // segment (seamOffs_), so live-held keys keep sounding and tails ring out (#29).
            seamEndSamples_ = transport_.playheadSamples();
            transport_.wrapToLoopStart();
            if (hybrid_) hybrid_->invalidate(false);
            if (spb > 0.0)
                onLoopWrap(g, transport_.beatAtSamples(transport_.loopStartSamples()),
                    transport_.beatAtSamples(seamEndSamples_));
            lastBeatEmitted_ =
                static_cast<int64_t>(std::ceil(transport_.positionBeats() * transport_.timeSigDenom() / 4.0 - 1e-8)) -
                1;
            seamOffs_ = true;
            chase = true;
            if (nLoopSeams < 16) loopSeams[nLoopSeams++] = done; // declick this seam below
        }
        int32_t seg = std::min(numFrames - done, kMaxBlock);
        if (playing && transport_.isLooping()) {
            const double toEnd = transport_.loopEndSamples() - transport_.playheadSamples();
            if (toEnd > 0.5 && toEnd < seg) seg = std::max(1, static_cast<int32_t>(std::lround(toEnd)));
        }
        // End the segment where the count-in runs out, so the transport rolls on that sample.
        if (countInActive_ && spb > 0.0) {
            const double left = (countInTotal_ - countInPos_) * spb * 4.0 / std::max(1, transport_.timeSigDenom());
            if (left < seg) seg = std::max(1, static_cast<int32_t>(std::lround(left)));
        }
        const double segStart = transport_.playheadSamples();
        double localSpb = spb, musicalStart = segStart;
        if (transport_.hasTempoMap()) {
            const double beatStart = transport_.positionBeats();
            if (playing) {
                const auto& points = g->tempoMap->points;
                auto next = std::upper_bound(
                    points.begin(), points.end(), beatStart + 1e-8, [](double b, const auto& p) { return b < p.beat; });
                if (next != points.end()) {
                    const double until = transport_.samplesAtBeat(next->beat) - segStart;
                    if (until > .5 && until < seg) seg = std::max(1, static_cast<int32_t>(std::ceil(until)));
                }
                const double beatEnd = transport_.beatAtSamples(segStart + seg);
                if (beatEnd > beatStart) localSpb = seg / (beatEnd - beatStart);
            }
            musicalStart = beatStart * localSpb;
        }
        if (playing) {
            if (presentationFrames == 0) {
                presentationStart = segStart;
                presentationLead = done;
            }
            presentationFrames += seg;
        }
        if (nAnchors < kMaxSegAnchors && (nAnchors == 0 || chase || playing != anchors[nAnchors - 1].rolling))
            anchors[nAnchors++] = {done, segStart, playing, chase};
        chaseNotes_ = chase;
        if (chase) articulationChaseStart_ = transport_.hasTempoMap() ? segStart : musicalStart;
        cueSeg_ = cue ? cue + done * 2 : nullptr; // this segment of the Cue bus (specs/089)
        if (g && sr > 0.0) mixGraph(g, out + done * 2, seg, musicalStart, segStart, playing, localSpb);
        // #971: a running take reports the Session launches this segment applied — outside the
        // track loop, so the render path pays one load when no take runs.
        if (g && playing && localSpb > 0.0 && passSerial_.load(std::memory_order_relaxed) != 0)
            session().reportPassEvents(g, musicalStart / localSpb, seg / localSpb);
        chaseNotes_ = chase = seamOffs_ = false;
        bool countInDone = false;
        if (playing) renderMetronome(out + done * 2, seg, segStart);
        else if (countInActive_)
            countInDone = renderCountIn(out + done * 2, seg, spb);
        transport_.advanceBy(seg);
        done += seg;
        if (countInDone) {
            transport_.play();
            endCountIn();
            launchCountInSlot();
            // Let the metronome click the beat we launch on (not when starting mid-beat).
            const double b = transport_.positionBeats() * transport_.timeSigDenom() / 4.0;
            const int64_t fb = static_cast<int64_t>(std::floor(b));
            lastBeatEmitted_ = (b - static_cast<double>(fb) < 1e-6) ? fb - 1 : fb;
            playing = chase = renderWasPlaying_ = true;
        }
    }

    // Declick each loop seam: the playhead jumps and sounding voices are cut at the
    // wrap, so the mix has a hard discontinuity there. Fade the mix down over the last
    // ~1.5 ms before the seam and up over the first ~1.5 ms after it — a tiny notch that
    // removes the click for instruments and audio alike, every cycle.
    if (nLoopSeams > 0) {
        const int32_t kD = std::max(8, std::min(96, static_cast<int32_t>(sr * 0.0015)));
        for (int32_t si = 0; si < nLoopSeams; ++si) {
            const int32_t s = loopSeams[si];
            const int32_t kb = std::min(kD, s), ka = std::min(kD, numFrames - s);
            for (int32_t m = 0; m < kb; ++m) { // fade out into the seam
                const double gN =
                    0.5 - 0.5 * std::cos(render_detail::kTwoPi * 0.5 * static_cast<double>(kb - m) / (kb + 1));
                out[static_cast<size_t>((s - kb + m) * 2)] *= static_cast<float>(gN);
                out[static_cast<size_t>((s - kb + m) * 2 + 1)] *= static_cast<float>(gN);
            }
            for (int32_t m = 0; m < ka; ++m) { // fade in out of the seam
                const double gN =
                    0.5 - 0.5 * std::cos(render_detail::kTwoPi * 0.5 * static_cast<double>(m + 1) / (ka + 1));
                out[static_cast<size_t>((s + m) * 2)] *= static_cast<float>(gN);
                out[static_cast<size_t>((s + m) * 2 + 1)] *= static_cast<float>(gN);
            }
        }
    }

    publishHeldSnapshot(captureNow_); // live + routed capture for this block done (#56)

    renderTone(out, numFrames);
    io_.renderPreview(out, numFrames); // audition voice (M7-4a), pre-master

    // Master effect chain (post-mix, pre-fader) and master pan (specs/099): run the whole mix
    // through the master track's devices, then the pan stage, in kMaxBlock chunks so each
    // device sees at most a full block — and the master's own lanes (Pan, device params) are
    // read per chunk, as track lanes are per segment, however long the call (export).
    // Fast path: no devices, no lanes, centred and nothing ramping — the master is a wire, as it
    // was before the pan stage, and the per-block cost stays what it was (perf gate).
    const bool masterWire =
        !g || !g->masterTrack ||
        (g->masterTrack->devices.empty() && g->masterTrack->automation.empty() && g->masterTrack->pan() == 0.0f &&
            masterPanG_[0] == 1.0f && masterPanG_[1] == 0.0f && masterPanG_[2] == 0.0f && masterPanG_[3] == 1.0f);
    if (!masterWire) processMasterChain(*g, out, numFrames, spb, nAnchors);

    const float mv = masterVolume_.load(std::memory_order_relaxed);
    float mpkL = 0.0f, mpkR = 0.0f;
    double msqL = 0.0, msqR = 0.0, msLR = 0.0; // msLR: Stereo Map (specs/008)
    for (int32_t i = 0; i < numFrames; ++i) {
        // Master safety net: a NaN slips straight through std::clamp (every compare
        // with NaN is false, so clamp returns it unchanged) and hits the speakers as
        // full-scale noise. Squash any non-finite sample to silence *before* clamping
        // so a single bad value from a device/feedback path can't blast the output.
        float l = out[i * 2] * mv;
        float r = out[i * 2 + 1] * mv;
        if (!std::isfinite(l)) l = 0.0f;
        if (!std::isfinite(r)) r = 0.0f;
        l = std::clamp(l, -1.0f, 1.0f);
        r = std::clamp(r, -1.0f, 1.0f);
        out[i * 2] = l;
        out[i * 2 + 1] = r;
        const float al = std::fabs(l), ar = std::fabs(r);
        if (al > mpkL) mpkL = al;
        if (ar > mpkR) mpkR = ar;
        msqL += l * static_cast<double>(l);
        msqR += r * static_cast<double>(r);
        msLR += l * static_cast<double>(r);
    }
    render_detail::stereoFold(masterStereoL_, masterStereoR_, masterStereoLR_, msqL, msqR, msLR, numFrames,
        cachedStereoBlockStep(numFrames, transport_.sampleRate()));
    masterPeakL_.store(mpkL, std::memory_order_relaxed);
    masterPeakR_.store(mpkR, std::memory_order_relaxed);
    transport_.publishPresentation(presentationStart, presentationFrames, presentationLead,
        deviceBlock_ && io_.backend_ ? std::max(numFrames, io_.backend_->bufferFrames()) : numFrames);
    masterRmsL_.store(render_detail::rmsFromSquareSum(msqL, numFrames), std::memory_order_relaxed);
    masterRmsR_.store(render_detail::rmsFromSquareSum(msqR, numFrames), std::memory_order_relaxed);
    cueSeg_ = nullptr;
    if (cue) finishCue(cue, numFrames);
}

// The Cue bus after every tap and click (specs/089): the Cue level, the same NaN / clip
// safety net as Main, and the Cue meter. Never through the master chain or master fader.
void EngineRuntime::mixGraph(
    Graph* g, float* out, int32_t frames, double blockStart, double elapsedStart, bool playing, double spb) {
    RenderGraphExecutor::mixGraph(*this, g, out, frames, blockStart, elapsedStart, playing, spb);
}

// The click goes to Main, Cue or both (specs/089), at kMetroGain × the click level. A click
// to Cue skips the master chain and fader; without a Cue buffer (offline) it writes nothing.

} // namespace nota::detail
