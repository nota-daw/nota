// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "core/EngineRuntime.h"
#include "session/SessionOps.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/EngineTypes.h"
#include "model/ArticulationTable.h"
#include "render/RenderHelpers.h"
#include "model/ArticulationSchedule.h"
#include "render/RenderGraphExecutor.h"
#include "render/RenderRoutingPlan.h"
#include "model/TimelineIndex.h"
#include "core/EnginePrelude.h"
#include "nota/devices/racks/RackCore.h"       // IWYU pragma: keep
#include "nota/devices/racks/RackInstrument.h" // IWYU pragma: keep
#include "session/SessionPlayer.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <vector>
#include <utility>
#include <memory>

namespace nota::detail {

void EngineRuntime::renderSessionSlotRaw(Track& t, float* dst, int32_t frames, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    Instrument* inst = t.instrument.get();
    auto& sp = *t.sessionPlayer;
    const int32_t slot = sp.playing.load(std::memory_order_relaxed);
    if (!inst || slot < 0 || slot >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[static_cast<size_t>(slot)];
    if (!s.hasClip) return;

    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    const double p = sp.localBeats;
    const double db = frames / spb;

    MidiEv evs[1024];
    int n = 0;
    ExprEv xev[kMaxBlockExpr];
    int xn = 0;
    const long i0 = static_cast<long>(std::floor(p / L));
    const float velGain = sp.gain;
    // The track's MIDI offset and each note's articulation offset (specs/085, T049), as in the
    // arrangement: the notes stay where they are, they sound shifted. A later shift can bring a note
    // of the previous pass into this block, so that pass is read too — only when something is late,
    // so a slot with no offset plays exactly as before.
    const double msToS = transport_.sampleRate() / 1000.0;
    const ArticulationTable* artTable = t.articulationTable.get();
    const double trackMs = t.midiOffsetMs();
    for (const Note& note : s.midi.notes) {
        if (note.startBeat < 0.0 || note.startBeat >= L) continue;
        const double shiftS =
            artTable ? (trackMs + articulationOffsetMs(artTable, note.articulation)) * msToS : trackMs * msToS;
        const double shiftBeats = shiftS / spb;
        // This note's on, off, or expression can reach the block from any pass whose shifted
        // lifetime overlaps it. Derive both bounds per note: offsets and long tails can span
        // arbitrarily many short loops, and the block itself may cross several loops.
        const long itLo =
            std::max(0L, static_cast<long>(std::floor((p - shiftBeats - note.startBeat - note.lengthBeats) / L)));
        long itHi = static_cast<long>(std::floor((p + db - shiftBeats - note.startBeat) / L));
        if (!s.loop) itHi = std::min(itHi, 0L);
        for (long it = itLo; it <= itHi; ++it) {
            const double onAbs = static_cast<double>(it) * L + note.startBeat;
            const double offAbs = onAbs + note.lengthBeats;
            // Unshifted: the block test in beats, as always — the next block starts at exactly p + db,
            // so a note on the boundary falls in one block, never both or neither. Shifted: in samples.
            const bool onIn = shiftS == 0.0 ? onAbs >= p && onAbs < p + db
                                            : (onAbs - p) * spb + shiftS >= 0.0 && (onAbs - p) * spb + shiftS < frames;
            const bool offIn = shiftS == 0.0
                                   ? offAbs >= p && offAbs < p + db
                                   : (offAbs - p) * spb + shiftS >= 0.0 && (offAbs - p) * spb + shiftS < frames;
            if (onIn && n < 1024)
                evs[n++] = {static_cast<int32_t>((onAbs - p) * spb + shiftS), true, note.pitch, note.velocity * velGain,
                    static_cast<float>(note.lengthBeats)};
            if (offIn && n < 1024)
                evs[n++] = {static_cast<int32_t>((offAbs - p) * spb + shiftS), false, note.pitch, 0.0f, 0.0f};
            if (note.expr)
                noteExprEvents(
                    note, (onAbs - p) * spb + shiftS, (offAbs - p) * spb + shiftS, frames, spb, xev, xn, kMaxBlockExpr);
        }
    }
    // The slot clip's controllers, per loop iteration: a release at each iteration's end, then
    // its values from the start of the next (specs/020).
    if (!s.midi.controllers.empty())
        for (long it = std::max(0L, i0 - 1); it <= i0 + 1; ++it) {
            if (!s.loop && it > 0) break;
            const double cs = (static_cast<double>(it) * L - p) * spb;
            const double ce = (static_cast<double>(it + 1) * L - p) * spb;
            clipCtlEndResets(s.midi, cs, ce, 0.0, frames, xev, xn, kMaxBlockExpr);
        }
    if (!s.midi.controllers.empty())
        for (long it = std::max(0L, i0 - 1); it <= i0 + 1; ++it) {
            if (!s.loop && it > 0) break;
            const double cs = (static_cast<double>(it) * L - p) * spb;
            const double ce = (static_cast<double>(it + 1) * L - p) * spb;
            clipCtlEvents(s.midi, cs, ce, 0.0, frames, spb, false, xev, xn, kMaxBlockExpr);
        }
    std::stable_sort(xev, xev + xn, [](const ExprEv& a, const ExprEv& b) { return a.off < b.off; });

    // Live monitoring while overdub-recording into this slot (M5-4).
    bool monitoring = recordSlotPlayer_.load(std::memory_order_acquire) == &sp;
    if (!monitoring && recording_.load(std::memory_order_relaxed)) // a slot that joined the take (specs/086)
        if (const SessionLaneSlots* ls = laneSlotsHist_.live())
            for (SessionPlayer* lp : ls->player) monitoring = monitoring || lp == &sp;
    // A phone plays only its own track.
    n = liveNotesFor(t, monitoring, monitoring, monitoring && t.armed(), evs, n);

    std::sort(evs, evs + n, midiEvLess);

    // MIDI FX chain (arpeggiator etc.) on the slot's own loop clock.
    if (!t.midiEffects.empty()) {
        MidiEv scratch[1024];
        n = applyMidiEffects(t, evs, n, scratch, frames, p, spb, true);
        applyMidiCcRouting(t);
        std::sort(evs, evs + n, midiEvLess);
    } else if (t.instrument && t.instrument->asRack())
        applyMidiCcRouting(t); // Arps grouped into the Instrument Rack's chains (specs/044)

    // The monitored live expression, after the offset-0 notes (as in renderInstrumentRaw).
    // The slot's articulation MIDI (specs/085, T042), on the slot's own loop clock, past its MIDI FX.
    ArtEv aev[kMaxBlockArt];
    int an = 0;
    if (artTable && spb > 0.0 && inst->takesArticulations()) {
        ArticulationBlock ab;
        ab.frames = frames;
        ab.blockStart = p * spb;
        ab.spb = spb;
        ab.sampleRate = transport_.sampleRate();
        ab.trackOffsetMs = t.midiOffsetMs();
        // The first block after a launch (playedBeats resets at launch): what is in force where the
        // slot starts — its start, or the outgoing position for a legato launch — goes out, so a
        // switch whose lead falls before the launch isn't lost.
        ab.chase = sp.playedBeats.load(std::memory_order_relaxed) == 0.0;
        ab.releases = &sp.articulationReleases;
        ab.seamOffs = false;
        ab.seamEnd = 0.0;
        an = scheduleSlotArticulations(s.midi, L, s.loop, *artTable, ab, aev, kMaxBlockArt);
    } else if (sp.articulationReleases.count > 0 && inst->takesArticulations()) {
        an = scheduleArticulationReleases(sp.articulationReleases, frames, aev, kMaxBlockArt);
    }
    playEvents(*inst, dst, frames, evs, n, xev, xn, aev, an, p, spb, true, [&] { // the slot's own loop clock
        if (!monitoring) return;
        for (int i = 0; i < liveExprCount_; ++i)
            if ((liveExpr_[i].trackId < 0 && passesMidiInput(t, liveExpr_[i])) || liveExpr_[i].trackId == t.id())
                deliverExpr(*inst, liveExpr_[i].pitch, liveExpr_[i].expr, liveExpr_[i].velocity);
    });
    sp.localBeats = p + db;
}

// Renders an audio track's playing Session slot (pre-fader): the slot's clip — the same
// clip model as the arrangement (region, warp, pitch, reverse, envelopes) — looped over the
// slot's length on the slot's own clock (S-06). A block that crosses the loop end renders
// in two pieces; a one-shot slot stops at the end.
void EngineRuntime::renderSessionAudioSlotRaw(Track& t, float* dst, int32_t frames, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    auto& sp = *t.sessionPlayer;
    const int32_t slot = sp.playing.load(std::memory_order_relaxed);
    if (slot < 0 || slot >= static_cast<int32_t>(t.sessionSlots.size()) || spb <= 0.0) return;
    const SessionSlot& s = t.sessionSlots[static_cast<size_t>(slot)];
    if (!s.hasClip || !s.audio.sample) return;

    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    double p = sp.localBeats;
    int32_t done = 0;
    while (done < frames) {
        if (!s.loop && p >= L) break;
        double local = std::fmod(p, L);
        if (local < 0.0) local += L;
        const double toEnd = (L - local) * spb;
        int32_t seg = static_cast<int32_t>(std::min<double>(frames - done, std::ceil(toEnd - 1e-9)));
        if (seg <= 0) seg = 1;
        mixAudioClip(s.audio, dst + done * 2, seg, local * spb, spb, sp.gain, transport_);
        done += seg;
        p += seg / spb;
    }
    sp.localBeats += frames / spb;
}

// Renders a track's dry audio clips (pre-fader) into `dst` (zeroed here). Also drives
// Consolidate's offline bounce (consolidateRange) over a detached clip list.
void EngineRuntime::finishCue(float* cue, int32_t numFrames) {
    const float gain = cueGain_.load(std::memory_order_relaxed);
    float pkL = 0.0f, pkR = 0.0f;
    double sqL = 0.0, sqR = 0.0;
    for (int32_t i = 0; i < numFrames; ++i) {
        float l = cue[i * 2] * gain;
        float r = cue[i * 2 + 1] * gain;
        if (!std::isfinite(l)) l = 0.0f;
        if (!std::isfinite(r)) r = 0.0f;
        l = std::clamp(l, -1.0f, 1.0f);
        r = std::clamp(r, -1.0f, 1.0f);
        cue[i * 2] = l;
        cue[i * 2 + 1] = r;
        pkL = std::max(pkL, std::fabs(l));
        pkR = std::max(pkR, std::fabs(r));
        sqL += l * static_cast<double>(l);
        sqR += r * static_cast<double>(r);
    }
    cuePeakL_.store(pkL, std::memory_order_relaxed);
    cuePeakR_.store(pkR, std::memory_order_relaxed);
    cueRmsL_.store(render_detail::rmsFromSquareSum(sqL, numFrames), std::memory_order_relaxed);
    cueRmsR_.store(render_detail::rmsFromSquareSum(sqR, numFrames), std::memory_order_relaxed);
}

// Fill `dst` with what a monitoring audio track hears: the hardware input pulled for this
// segment, or its source track's previous-block post-fader route tap. Guards against the
// obvious feedback loops (itself, master, or a group it sits inside) by staying silent.
void EngineRuntime::renderMonitorInput(Graph* g, const Track& t, float* dst, int32_t frames) {
    const int32_t src = t.recordInputSource();
    if (src == 0) {
        // The track's own column(s) of this segment's capture (specs/086); a mono pick sounds
        // in the middle (#340), a channel the layout doesn't carry is silent.
        const size_t w = static_cast<size_t>(std::max(layoutAudio_.count, 0));
        const int32_t cL = layoutAudio_.column(t.inputFirst());
        const int32_t cR = t.inputWidth() == 2 ? layoutAudio_.column(t.inputFirst() + 1) : cL;
        const float* in = monitorHwBuf_.data();
        for (int32_t i = 0; i < frames; ++i) {
            const size_t row = static_cast<size_t>(i) * w;
            dst[i * 2] = cL >= 0 ? in[row + static_cast<size_t>(cL)] : 0.0f;
            dst[i * 2 + 1] = cR >= 0 ? in[row + static_cast<size_t>(cR)] : 0.0f;
        }
        return;
    }
    std::fill_n(dst, frames * 2, 0.0f);
    if (src < 0 || src == t.id()) return;
    for (int32_t a = t.groupId(), hops = 0; a >= 0 && hops < 64; ++hops) { // ancestor group?
        if (a == src) return;
        int32_t parent = -1;
        for (auto& o : g->tracks)
            if (o->id() == a) {
                parent = o->groupId();
                break;
            }
        a = parent;
    }
    // The source's post-fader tap, `monitorLag_` frames behind: a continuous stream (#563).
    const int32_t slot = routeSlotForTrack(src);
    if (slot < 0 || trackTapRingTrack_[static_cast<size_t>(slot)] != src) return; // routed this segment: silent once
    const float* ring = trackTapRing_[static_cast<size_t>(slot)].data();
    constexpr int64_t mask = kTrackTapRingFrames - 1;
    const int64_t from = trackTapRingPos_ - monitorLag_; // before the first lag: the ring's zeros
    for (int32_t i = 0; i < frames; ++i) {
        const size_t k = static_cast<size_t>((from + i) & mask) * 2;
        dst[i * 2] = ring[k];
        dst[i * 2 + 1] = ring[k + 1];
    }
    // A lag that just changed crossfades from the old read position, so the change neither
    // skips nor repeats frames audibly. The old position stays inside what has been written
    // (see advanceMonitorLag), so both reads are real signal.
    if (monitorFadePos_ >= monitorFadeLen_) return;
    const int64_t old = trackTapRingPos_ - monitorLagFrom_;
    const int32_t n = std::min(frames, monitorFadeLen_ - monitorFadePos_);
    for (int32_t i = 0; i < n; ++i) {
        const float w = static_cast<float>(monitorFadePos_ + i) / static_cast<float>(monitorFadeLen_);
        const size_t k = static_cast<size_t>((old + i) & mask) * 2;
        dst[i * 2] = w * dst[i * 2] + (1.0f - w) * ring[k];
        dst[i * 2 + 1] = w * dst[i * 2 + 1] + (1.0f - w) * ring[k + 1];
    }
}

// The monitoring lag for this segment (#563): never shorter than the segment, so a read never
// reaches frames not yet written. It grows at once and shrinks to the longest segment of the
// last second, so an export's long chunks or a larger buffer don't keep their latency for good.
void EngineRuntime::advanceMonitorLag(int32_t frames) {
    const int32_t was = monitorLag_;
    int32_t lag = std::max(was, frames);
    if (frames > monitorLagPeak_) monitorLagPeak_ = frames;
    monitorLagWindow_ += frames;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    if (static_cast<double>(monitorLagWindow_) >= sr) {
        if (monitorLagPeak_ < lag) lag = monitorLagPeak_;
        monitorLagPeak_ = 0;
        monitorLagWindow_ = 0;
    }
    if (lag == was) return;
    monitorLag_ = lag;
    // Crossfade from the old read position (~10 ms). Shrinking, the old position is further
    // back: written. Growing, it runs into unwritten frames `was` frames into this segment, so
    // the fade ends there. From nothing (the first segment) there is nothing to fade from.
    const int32_t fade = static_cast<int32_t>(0.010 * sr);
    monitorLagFrom_ = was;
    monitorFadePos_ = 0;
    monitorFadeLen_ = lag < was ? fade : std::min(fade, was);
}

// Append this segment's post-fader taps to the monitoring rings (#563). A slot that now mirrors
// another source starts from silence rather than the old source's frames.
void EngineRuntime::publishTrackTapRings(int32_t frames) {
    const int32_t n = routeSlotCount_.load(std::memory_order_acquire);
    const int32_t limit = std::max(n, trackTapRingHighWater_);
    if (limit > 0) {
        constexpr int64_t mask = kTrackTapRingFrames - 1;
        int32_t newHigh = 0;
        for (int32_t s = 0; s < limit; ++s) {
            const int32_t id = s < n ? routeSlotTrackId_[s].load(std::memory_order_relaxed) : -1;
            if (id < 0) {
                trackTapRingTrack_[s] = -1;
                continue;
            }
            const uint64_t part = routeSlotPart_[s].load(std::memory_order_relaxed);
            if (part != 0) { // monitoring hears whole tracks only
                trackTapRingTrack_[s] = -1;
                continue;
            }
            newHigh = s + 1;
            float* ring = trackTapRing_[static_cast<size_t>(s)].data();
            if (trackTapRingTrack_[static_cast<size_t>(s)] != id) {
                std::fill_n(ring, static_cast<size_t>(kTrackTapRingFrames) * 2, 0.0f);
                trackTapRingTrack_[static_cast<size_t>(s)] = id;
            }
            const float* tap = routeBusNext_[static_cast<size_t>(s)].data();
            for (int32_t i = 0; i < frames; ++i) {
                const size_t k = static_cast<size_t>((trackTapRingPos_ + i) & mask) * 2;
                ring[k] = tap[i * 2];
                ring[k + 1] = tap[i * 2 + 1];
            }
        }
        trackTapRingHighWater_ = newHigh;
    }
    trackTapRingPos_ += frames;
    if (monitorFadePos_ < monitorFadeLen_) monitorFadePos_ = std::min(monitorFadeLen_, monitorFadePos_ + frames);
}

// Per-leaf DSP for Pass 1 : render the track's dry stereo (pre-fader,
// pre-device) into dst. Per-track state only (plus read-only shared) —
// bit-identical in pool tasks and inline. Session clocks (shared followRng_)
// advance in the serial pre-pass, never here.
bool EngineRuntime::renderLeafDry(Track& t, float* dst, const LeafWork& w, Graph* g, int32_t frames, double blockStart,
    double spb, bool playing, const TransportInfo& ti, [[maybe_unused]] bool arrangementActive, size_t trackIdx) {
    // 1) Render the track's dry stereo (pre-fader).
    switch (w.path) {
    case LeafPath::Frozen: fillFrozen(t, dst, frames, blockStart, spb); return true;
    case LeafPath::Monitor: renderMonitorInput(g, t, dst, frames); return true;
    case LeafPath::SessionInst:
        feedInstrumentKeys(g, t, frames); // a Session clip plays the same instrument: its keys too
        feedPartTaps(t);                  // a Session clip plays the same pads: their keys too (specs/050)
        renderSessionSlotRaw(t, dst, frames, spb);
        if (t.instrument) t.instrument->setPartTaps(nullptr, 0);
        session().sessionPostRender(t, frames / spb);
        return true;
    case LeafPath::SessionAudio:
        renderSessionAudioSlotRaw(t, dst, frames, spb);
        session().sessionPostRender(t, frames / spb);
        return true;
    case LeafPath::Instrument: {
        if (t.instrument) {
            t.instrument->setTransportInfo(ti); // hosted-plugin sync
            feedInstrumentKeys(g, t, frames);
        }
        feedPartTaps(t);
        const bool wroteAudio = renderInstrumentRaw(g, t, dst, frames, blockStart, spb, playing, trackIdx);
        if (t.instrument) t.instrument->setPartTaps(nullptr, 0);
        return wroteAudio;
    }
    case LeafPath::AudioClips: renderTrackAudioClipsRaw(t, dst, frames, blockStart, spb); return true;
    case LeafPath::Tail: std::fill_n(dst, frames * 2, 0.0f); return false; // the chain alone rings on
    case LeafPath::Skip: return false;                                     // unreachable (pre-pass filters Skip out)
    }
    return true;
}

// Parts of a track's instrument that keys listen to (specs/050): hand it where each one's Pre /
// Post goes this block (it copies the table; a block may render in pieces). The Next buses are
// written by this track alone, so the parallel leaf render stays race-free. Audio thread.
void EngineRuntime::feedPartTaps(const Track& t) {
    if (!t.instrument) return;
    Instrument::PartTap taps[kMaxRoutes];
    int32_t nTaps = 0;
    for (int32_t s = 0, n = routeSlotCount_.load(std::memory_order_acquire); s < n; ++s) {
        if (routeSlotTrackId_[s].load(std::memory_order_relaxed) != t.id()) continue;
        const uint64_t part = routeSlotPart_[s].load(std::memory_order_relaxed);
        if (part != 0)
            taps[nTaps++] = {
                part, routeBusPreNext_[static_cast<size_t>(s)].data(), routeBusNext_[static_cast<size_t>(s)].data()};
    }
    t.instrument->setPartTaps(taps, nTaps);
}

// The key buffer for a consumer: its route slot's tap, nullptr for none — or, for a part key
// whose part is gone (specs/050), silence: a detector handed nullptr keys off its own input,
// and the key is shown as missing, not as Internal. This block's tap once the source has
// written it (routeFresh_, #676), else the previous block's.
const float* EngineRuntime::keyBuffer(int32_t src, uint64_t part, bool pre) const {
    const int32_t s = routeSlotFor(src, part);
    if (s >= 0) {
        if (routeFresh_[static_cast<size_t>(s)])
            return pre ? routeBusPreNext_[static_cast<size_t>(s)].data() : routeBusNext_[static_cast<size_t>(s)].data();
        return pre ? routeBusPre_[static_cast<size_t>(s)].data() : routeBus_[static_cast<size_t>(s)].data();
    }
    return part != 0 ? silentKey_.data() : nullptr;
}

// Hand a device its sidechain key for this block (the source track's signal, pre- or post-FX
// tap: this block's when the source rendered first, see keyBuffer) or its MIDI key; a rack
// passes keys on to its chain children, each with its own source (specs/044). Audio thread.
void EngineRuntime::feedDeviceKey(Graph* g, Device& d, int32_t ownTrackId, int32_t frames) {
    const int32_t src = d.sidechainSourceTrackId();
    if (src >= 0) {
        d.setSidechain(keyBuffer(src, d.sidechainPart(), d.sidechainTapPre() != 0), frames);
        const int32_t s = routeSlotFor(src, d.sidechainPart());
        d.setSidechainLate(s >= 0 && !routeFresh_[s]);
        if (d.wantsMidiKey()) feedMidiKey(g, d, src);
    } else if (d.wantsOwnMidiKey())
        feedMidiKey(g, d, ownTrackId); // no key track: this track's notes
    if (auto* r = d.asRack()) feedRackChildKeys(g, *r, ownTrackId, frames);
}

// React (Nota Flux): hand a listening instrument its source track's post-fader signal (see
// keyBuffer) just before it renders; and the keyed devices and instruments inside an Instrument
// Rack's chains theirs (specs/044). Audio thread.
void EngineRuntime::feedInstrumentKeys(Graph* g, Track& t, int32_t frames) {
    if (!t.instrument) return;
    const int32_t iSc = t.instrument->sidechainSourceTrackId();
    if (iSc >= 0) t.instrument->setSidechain(keyBuffer(iSc, t.instrument->sidechainPart(), false), frames);
    if (auto* r = t.instrument->asRack()) feedRackChildKeys(g, *r, t.id(), frames);
}

void EngineRuntime::feedRackChildKeys(Graph* g, const RackCore& r, int32_t ownTrackId, int32_t frames) {
    if (!r.hasSidechainChild()) return;
    r.forEachChildDevice([&](Device& c) {
        if (!c.bypassed() && !c.asRack()) feedDeviceKey(g, c, ownTrackId, frames);
    });
    r.forEachChildInstrument([&](Instrument& i) {
        const int32_t src = i.sidechainSourceTrackId();
        if (src < 0) return;
        i.setSidechain(keyBuffer(src, i.sidechainPart(), false), frames);
    });
}

// Step 2/2b for Pass 1: delegated cleanly to RenderGraphExecutor::processDeviceChain
// (contiguous planar FX stretch optimization + PDC).
void EngineRuntime::processLeafDevices(Track& t, float* buf, const LeafWork& w, Graph* g, int32_t frames,
    double blockStart, double spb, bool playing, const TransportInfo& ti) {
    RenderGraphExecutor::processDeviceChain(
        *this, g, t, buf, pool_ ? pool_->workerCount() : 0, frames, blockStart, spb, playing, ti, w.frozenActive);
}

void EngineRuntime::renderClick(float* out, float* cue, int32_t i, int clickLen, double sr, int32_t dest, float gain) {
    if (clickRemaining_ <= 0) return;
    const float env = static_cast<float>(clickRemaining_) / static_cast<float>(clickLen);
    const float s = static_cast<float>(std::sin(clickPhase_)) * env * kMetroGain * gain;
    if (dest != 1) {
        out[i * 2] += s;
        out[i * 2 + 1] += s;
    }
    if (dest != 0 && cue) {
        cue[i * 2] += s;
        cue[i * 2 + 1] += s;
    }
    clickPhase_ += render_detail::kTwoPi * clickFreq_ / sr;
    if (clickPhase_ >= render_detail::kTwoPi) clickPhase_ -= render_detail::kTwoPi;
    --clickRemaining_;
}

void EngineRuntime::renderMetronome(float* out, int32_t numFrames, double blockStartSamples) {
    if (!transport_.metronomeEnabled()) return;
    const double sr = transport_.sampleRate();
    const int beatsPerBar = transport_.beatsPerBar();
    const int clickLen = static_cast<int>(0.03 * sr);
    const int32_t dest = clickDest_.load(std::memory_order_relaxed);
    const float gain = clickGain_.load(std::memory_order_relaxed);

    for (int32_t i = 0; i < numFrames; ++i) {
        const double p = blockStartSamples + i;
        if (p >= 0.0) {
            const int64_t beat =
                static_cast<int64_t>(transport_.beatAtSamples(p) * transport_.timeSigDenom() / 4.0 + 1e-10);
            if (beat != lastBeatEmitted_) {
                lastBeatEmitted_ = beat;
                clickRemaining_ = clickLen;
                clickPhase_ = 0.0;
                clickFreq_ = (beat % beatsPerBar == 0) ? 1500.0 : 1000.0;
            }
        }
        renderClick(out, cueSeg_, i, clickLen, sr, dest, gain);
    }
}

// Count-in clicks: same voice as the metronome, accent on each bar's first beat. Sounds
// regardless of the metronome switch; the transport is stopped meanwhile.
bool EngineRuntime::renderCountIn(float* out, int32_t numFrames, double spb) {
    const double sr = transport_.sampleRate();
    const int beatsPerBar = std::max(1, transport_.beatsPerBar());
    const int clickLen = static_cast<int>(0.03 * sr);
    // countInPos_ is expressed in the signature's beat unit so 6/8 clicks six
    // eighth notes while recorded pickup positions remain quarter-note beats.
    const double inc = spb > 0.0 ? transport_.timeSigDenom() / (4.0 * spb) : 0.0;
    const int32_t dest = clickDest_.load(std::memory_order_relaxed);
    const float gain = clickGain_.load(std::memory_order_relaxed);

    for (int32_t i = 0; i < numFrames; ++i) {
        if (countInPos_ < countInTotal_) {
            const int64_t beat = static_cast<int64_t>(countInPos_ + 1e-9);
            if (beat != lastCountInBeat_) {
                lastCountInBeat_ = beat;
                clickRemaining_ = clickLen;
                clickPhase_ = 0.0;
                clickFreq_ = (beat % beatsPerBar == 0) ? 1500.0 : 1000.0;
            }
        }
        renderClick(out, cueSeg_, i, clickLen, sr, dest, gain);
        countInPos_ += inc;
    }
    uiCountIn_.store(std::max(0.0, countInTotal_ - countInPos_), std::memory_order_relaxed);
    return inc <= 0.0 || countInPos_ >= countInTotal_ - 0.5 * inc;
}

void EngineRuntime::renderTone(float* out, int32_t numFrames) {
    const double sr = transport_.sampleRate();
    const float target = toneEnabled_ ? 1.0f : 0.0f;
    if (sr <= 0.0 || (target == 0.0f && toneLevel_ == 0.0f)) {
        tonePhase_ = 0.0;
        return;
    }
    const double inc = render_detail::kTwoPi * static_cast<double>(frequency_) / sr;
    const float step = static_cast<float>(1.0 / (sr * 0.01)); // ~10 ms fade in/out
    for (int32_t i = 0; i < numFrames; ++i) {
        toneLevel_ = target > toneLevel_ ? std::min(target, toneLevel_ + step) : std::max(target, toneLevel_ - step);
        const float s = static_cast<float>(std::sin(tonePhase_)) * kToneGain * toneLevel_;
        out[i * 2] += s;
        out[i * 2 + 1] += s;
        tonePhase_ += inc;
        if (tonePhase_ >= render_detail::kTwoPi) tonePhase_ -= render_detail::kTwoPi;
    }
}

} // namespace nota::detail
