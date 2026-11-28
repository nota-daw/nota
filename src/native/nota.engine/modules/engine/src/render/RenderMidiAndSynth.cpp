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

// Append a track's own note events (clips while playing + live input when armed/auditioned).
int EngineRuntime::gatherInstrumentNotes(
    Track& t, MidiEv* evs, int n, int32_t frames, double blockStart, double spb, bool playing, ExprEv* xev, int* xn) {
    if (playing) {
        const double tsr = transport_.sampleRate() > 0.0 ? transport_.sampleRate() : 44100.0;
        const double msToS = tsr / 1000.0;
        const double trackShiftS = t.midiOffsetMs() * msToS;
        const ArticulationTable* artTable = t.articulationTable.get();
        const bool mapped = transport_.hasTempoMap();
        const double seamEnd = mapped ? blockStart + seamEndSamples_ - transport_.playheadSamples() : seamEndSamples_;
        const TempoMap* tmap = transport_.tempoMap();
        const double tplay = transport_.playheadSamples();
        const bool unshifted = !artTable && trackShiftS == 0.0;
        const TimelineIndex* idx = (spb > 0.0) ? t.timelineIndex.get() : nullptr;

        auto gatherMode = [&]<bool Shifted, bool Mapped>() {
            auto appendNote = [&](const MidiClip& clip, double clipStart, double clipEnd, bool clipActive, bool hasVel,
                                  const Note& note) {
                double shiftS = 0.0;
                if constexpr (Shifted)
                    shiftS = artTable ? trackShiftS + articulationOffsetMs(artTable, note.articulation) * msToS
                                      : trackShiftS;
                if (note.startBeat >= clip.lengthBeats || note.startBeat + note.lengthBeats <= 0.0) return;
                auto at = [&](double clipBeat) {
                    if constexpr (Mapped) return renderSampleAtBeat(clip.startBeat + clipBeat, blockStart, spb);
                    else
                        return clipStart + clipBeat * spb;
                };
                const double nominalOn = at(std::max(0.0, note.startBeat));
                double nominalOff = at(note.startBeat + note.lengthBeats);
                if (nominalOff > clipEnd) nominalOff = clipEnd;
                const double onS = Shifted ? nominalOn + shiftS : nominalOn;
                const double offS = Shifted ? nominalOff + shiftS : nominalOff;
                float vel = note.velocity;
                if (hasVel) vel *= std::clamp(clip.velocityEnvelope.valueAt(note.startBeat), 0.0f, 1.0f);
                if (clipActive && onS >= blockStart && onS < blockStart + frames && n < 1024)
                    evs[n++] = {static_cast<int32_t>(onS - blockStart), true, note.pitch, vel,
                        static_cast<float>((offS - onS) / spb)};
                if (seamOffs_ && onS < seamEnd && offS >= seamEnd && n < 1024)
                    evs[n++] = {0, false, note.pitch, 0.0f, 0.0f};
                if (chaseNotes_ && clipActive && onS < blockStart && offS > blockStart && n < 1024)
                    evs[n++] = {0, true, note.pitch, vel, 0.0f};
                if (offS >= blockStart && offS < blockStart + frames && n < 1024)
                    evs[n++] = {static_cast<int32_t>(offS - blockStart), false, note.pitch, 0.0f, 0.0f};
                if (xev && note.expr && clipActive)
                    noteExprEvents(note, at(note.startBeat) + shiftS - blockStart, offS - blockStart, frames, spb, xev,
                        *xn, kMaxBlockExpr, tmap, tsr, tplay, clip.startBeat + note.startBeat, shiftS);
            };

            if (idx && !seamOffs_) {
                const double minShiftMs = t.midiOffsetMs() + idx->minArtOffsetMs();
                const double maxShiftMs = t.midiOffsetMs() + idx->maxArtOffsetMs();
                const double maxBps = Mapped ? (TimelineIndex::kMaxConservTempoBpm / 60.0) : (tsr / spb);
                const double maxBeatsPerMs = std::max(maxBps, tsr / spb) / 1000.0;
                const double padBefore = std::max(0.0, maxShiftMs) * maxBeatsPerMs + 0.05;
                const double padAfter = std::max(0.0, -minShiftMs) * maxBeatsPerMs + 0.05;
                const double b0 = (Mapped && tmap) ? tmap->beatAt(tplay / tsr) : blockStart / spb;
                const double b1 = (Mapped && tmap) ? tmap->beatAt((tplay + frames) / tsr) : (blockStart + frames) / spb;
                const double rawStart = std::min(b0, b1);
                const double rawEnd = std::max(b0, b1);
                const double qStart = rawStart - padBefore;
                const double qEnd = rawEnd + padAfter;

                if (xev) {
                    idx->forEachMidiClipInWindow(rawStart - 0.05, rawEnd + 0.05, -1.0, [&](int32_t ci) {
                        const MidiClip& clip = t.midiClips[static_cast<size_t>(ci)];
                        if (clip.active && !clip.controllers.empty())
                            clipCtlEndResets(clip, renderSampleAtBeat(clip.startBeat, blockStart, spb),
                                renderSampleAtBeat(clip.startBeat + clip.lengthBeats, blockStart, spb), blockStart,
                                frames, xev, *xn, kMaxBlockExpr);
                    });
                }
                idx->forEachMidiClipInWindow(qStart, qEnd, -1.0, [&](int32_t ci) {
                    const MidiClip& clip = t.midiClips[static_cast<size_t>(ci)];
                    const bool clipActive = clip.active;
                    const double clipStart = renderSampleAtBeat(clip.startBeat, blockStart, spb);
                    const double clipEnd = renderSampleAtBeat(clip.startBeat + clip.lengthBeats, blockStart, spb);
                    const bool hasVel = !clip.velocityEnvelope.points.empty();
                    idx->forEachNoteInClip(
                        ci, qStart - clip.startBeat, qEnd - clip.startBeat, -1.0, [&](int32_t noteIdx) {
                            appendNote(
                                clip, clipStart, clipEnd, clipActive, hasVel, clip.notes[static_cast<size_t>(noteIdx)]);
                        });
                    if (xev && clipActive && !clip.controllers.empty())
                        clipCtlEvents(clip, clipStart, clipEnd, blockStart, frames, spb, chaseNotes_, xev, *xn,
                            kMaxBlockExpr, tmap, tsr, tplay);
                });
            } else {
                for (const MidiClip& clip : t.midiClips.read()) {
                    const bool clipActive = clip.active;
                    const double clipStart = renderSampleAtBeat(clip.startBeat, blockStart, spb);
                    const double clipEnd = renderSampleAtBeat(clip.startBeat + clip.lengthBeats, blockStart, spb);
                    if (xev && clipActive && !clip.controllers.empty())
                        clipCtlEndResets(clip, clipStart, clipEnd, blockStart, frames, xev, *xn, kMaxBlockExpr);
                    const bool hasVel = !clip.velocityEnvelope.points.empty();
                    for (const Note& note : clip.notes) appendNote(clip, clipStart, clipEnd, clipActive, hasVel, note);
                    if (xev && clipActive && !clip.controllers.empty())
                        clipCtlEvents(clip, clipStart, clipEnd, blockStart, frames, spb, chaseNotes_, xev, *xn,
                            kMaxBlockExpr, tmap, tsr, tplay);
                }
            }
        };
        if (mapped) {
            if (unshifted) gatherMode.template operator()<false, true>();
            else
                gatherMode.template operator()<true, true>();
        } else if (unshifted) {
            gatherMode.template operator()<false, false>();
        } else {
            gatherMode.template operator()<true, false>();
        }
    }
    // Notes a loop record replace removed from this track's take: release them (once), unless
    // the player is holding that key live.
    for (int k = 0; k < 16; ++k) {
        if (releaseTrack_[k] != t.id()) continue;
        for (int p = 0; p < 128 && n < 1024; ++p)
            if ((releasePitches_[k][p >> 5] >> (p & 31)) & 1u && !liveDown_[p]) evs[n++] = {0, false, p, 0.0f, 0.0f};
        for (auto& w : releasePitches_[k]) w = 0;
        releaseTrack_[k] = -1;
    }
    // Live notes reach a track when it's armed, or when it's the audition target
    // (so clicking a rack/drum pad plays it without arming/recording).
    // A track-addressed event (Nota Remote) plays only its own track.
    const bool takesUntargeted = t.armed() || t.id() == auditionTrackId_.load(std::memory_order_relaxed);
    return liveNotesFor(t, takesUntargeted, true, takesUntargeted, evs, n);
}

int EngineRuntime::liveNotesFor(const Track& t, bool untargeted, bool targeted, bool stillTakes, MidiEv* evs, int n) {
    Track::LiveHeld* held = t.liveHeld.get();
    for (int i = 0; i < liveCount_ && n < 1024; ++i) {
        const MidiEvent& le = liveEvents_[i];
        // A note it is holding still ends after its MIDI input changed (specs/086): the off
        // comes from the old input, which the new filter would turn away. Only that input's
        // off, though: the same key released on another keyboard isn't this note's.
        const bool heldOff = !le.on && held && le.pitch >= 0 && le.pitch < 128 &&
                             ((held->bits[le.pitch >> 6] >> (le.pitch & 63)) & 1u) &&
                             held->source[le.pitch] == le.source && held->channel[le.pitch] == le.channel;
        if (le.trackId >= 0 ? !targeted || le.trackId != t.id() : !untargeted || (!passesMidiInput(t, le) && !heldOff))
            continue;
        evs[n++] = {0, le.on, le.pitch, le.velocity, 0.0f};
        if (!held || le.trackId >= 0 || le.pitch < 0 || le.pitch >= 128) continue;
        const uint64_t bit = 1ull << (le.pitch & 63);
        if (le.on) {
            held->bits[le.pitch >> 6] |= bit;
            held->source[le.pitch] = le.source;
            held->channel[le.pitch] = le.channel;
        } else {
            held->bits[le.pitch >> 6] &= ~bit;
        }
    }
    // Disarmed with keys down (#366): release the live notes it was given, unless it takes
    // live input again (re-armed, or the audition target).
    if (held && held->release) {
        held->release = false;
        if (!stillTakes) {
            for (int h = 0; h < 2; ++h) {
                uint64_t bits = held->bits[h];
                held->bits[h] = 0;
                while (bits && n < 1024) {
                    evs[n++] = {0, false, h * 64 + std::countr_zero(bits), 0.0f, 0.0f};
                    bits &= bits - 1;
                }
            }
        }
    }
    return n;
}

bool EngineRuntime::takesLiveEvent(const Track& t, const MidiEvent& e) const {
    if (e.trackId >= 0) return e.trackId == t.id();
    return (t.armed() && passesMidiInput(t, e)) || t.id() == auditionTrackId_.load(std::memory_order_relaxed);
}

// An untargeted live event reaches an armed track only through its MIDI input filter
// (specs/086); the audition target (a pad clicked in a device) takes everything.
bool EngineRuntime::passesMidiInput(const Track& t, const MidiEvent& e) const {
    return t.id() == auditionTrackId_.load(std::memory_order_relaxed) || t.acceptsMidiFrom(e.source, e.channel);
}

// The note-offs of the notes MIDI In started on a track and no longer ends (#504), appended to
// evs at the block start; the note-ons of a new route at the same offset sort after them.
int EngineRuntime::releaseRouted(Track::LiveHeld& held, MidiEv* evs, int n) {
    for (int h = 0; h < 2; ++h) {
        uint64_t& bits = held.routedBits[h];
        while (bits && n < 1024) { // a full buffer keeps the rest for the next block
            evs[n++] = {0, false, h * 64 + std::countr_zero(bits), 0.0f, 0.0f};
            bits &= bits - 1;
        }
    }
    return n;
}

// Live expression bypasses the MIDI FX chain (it is keyed by the pitch the player holds) and
// reaches an instrument the way live notes do: through its own track, or through the track
// it takes its MIDI from.
void EngineRuntime::applyLiveExpression(Graph* g, const Track& t, Instrument& inst) const {
    const Track* src = nullptr;
    const int32_t srcId = g ? t.midiFromTrackId() : -1;
    if (g && srcId >= 0) {
        if (const RenderRoutingPlan* plan = g->routingPlan.get()) {
            const int32_t j = plan->findTrackIndex(srcId);
            if (j >= 0 && static_cast<size_t>(j) < g->tracks.size()) {
                const Track* p = g->tracks[static_cast<size_t>(j)].get();
                if (p && p != &t && p->type() == TrackType::Instrument && p->id() == srcId) src = p;
            }
        } else {
            for (const auto& p : g->tracks)
                if (p && p.get() != &t && p->type() == TrackType::Instrument && p->id() == srcId) {
                    src = p.get();
                    break;
                }
        }
    }
    for (int i = 0; i < liveExprCount_; ++i) {
        const MidiEvent& e = liveExpr_[i];
        if (takesLiveEvent(t, e) || (src && takesLiveEvent(*src, e))) deliverExpr(inst, e.pitch, e.expr, e.velocity);
    }
}

// A track's own MIDI *output* for this block: its clips + live input, drum-rack swing, then
// its own MIDI FX chain. This is what feeds the track's instrument and, via routing, any
// destination it forwards to — so the FX (arp etc.) run exactly once per block.
int EngineRuntime::computeInstrumentMidi(Track& t, MidiEv* evs, int32_t frames, double blockStart, double spb,
    bool playing, bool arrangementActive, ExprEv* xev, int* xn, const Graph* g, size_t trackIdx) {
    // Arrangement clips gather only when the Arrangement is active; live input (armed/audition)
    // is independent, so a session-only jam still lets you play instruments live.
    if (xn) *xn = 0;
    int n = gatherInstrumentNotes(t, evs, 0, frames, blockStart, spb, playing && arrangementActive, xev, xn);
    std::sort(evs, evs + n, midiEvLess);

    // Drum Rack swing + humanize: nudge note-on sample offsets. Off-beat 1/16 hits
    // are pushed late by swing; humanize adds a small random jitter. The instrument
    // already receives these at sample accuracy (segmented render below), so shaping
    // the offsets here is all it takes. Off-events keep their time (drum one-shots
    // ignore note length). Delays that would spill past the block clamp to its end.
    if (auto* dr = t.instrument ? t.instrument->asDrumRack() : nullptr) {
        const float sw = dr->swing(), hz = dr->humanize();
        if ((sw > 0.0f || hz > 0.0f) && spb > 0.0) {
            static thread_local uint32_t rng = 0x9E3779B9u; // tracks may run on the render pool
            const double sixteenth = spb * 0.25;            // samples per 1/16 note
            // Hits on one step move together: one jitter per moment, not per note. Jittered
            // apart, an open and a closed hat on one step (one choke group) landed in a random
            // order and the open one was cut on random passes (#644).
            int32_t stepAt = -1;
            double stepJitter = 0.0;
            for (int k = 0; k < n; ++k) {
                if (!evs[k].on) continue;
                double delay = 0.0;
                if (sw > 0.0f) {
                    const long idx = std::lround((blockStart + evs[k].off) / sixteenth);
                    if (idx & 1L) delay += static_cast<double>(sw) * sixteenth * 0.6; // up to 60% of a 1/16
                }
                if (hz > 0.0f) {
                    if (evs[k].off != stepAt) {
                        stepAt = evs[k].off;
                        rng = rng * 1664525u + 1013904223u;
                        stepJitter = ((rng >> 9) & 0xFFFF) / 65535.0; // 0..1
                    }
                    delay += stepJitter * static_cast<double>(hz) * sixteenth * 0.35;
                }
                evs[k].off = std::clamp(evs[k].off + static_cast<int32_t>(std::llround(delay)), 0, frames - 1);
            }
            std::sort(evs, evs + n, midiEvLess);
        }
    }

    // MIDI FX chain (arpeggiator etc.): transform the note events before the
    // instrument. Driven by the transport beat when playing, else the device's own
    // clock. The chain may consume/generate events, so re-sort afterwards.
    if (!t.midiEffects.empty()) {
        MidiEv scratch[1024];
        n = applyMidiEffects(t, evs, n, scratch, frames, spb > 0.0 ? blockStart / spb : 0.0, spb, playing);
        applyMidiCcRouting(t, g, trackIdx);
        std::sort(evs, evs + n, midiEvLess);
    } else if (t.instrument && t.instrument->asRack())
        applyMidiCcRouting(t, g, trackIdx); // Arps grouped into the Instrument Rack's chains (specs/044)
    t.updateMidiCv(evs, n);                 // MIDI→CV modulators read the post-FX note stream
    return n;
}

// MIDI pre-pass: compute each non-session instrument track's post-FX MIDI output into its
// per-position buffer (rs_->blockMidi). Runs once per block so each track's MIDI FX advance once;
// the render pass then feeds instruments from these buffers (own + routed sources').
void EngineRuntime::computeBlockMidi(
    Graph* g, int32_t frames, double blockStart, double spb, bool playing, bool arrangementActive) {
    const size_t nt = g ? g->tracks.size() : 0;
    // AR-6: never allocate here — publishRaw pre-sizes the vectors (and
    // every inner buffer) for each graph swap before it goes live. The guard
    // below is unreachable in practice; it neutralizes counts so no stale
    // MIDI can replay instead of growing.
    const size_t have = std::min({rs_->blockMidi.size(), rs_->blockMidiN.size(), rs_->blockMidiStart.size(),
        rs_->blockExpr.size(), rs_->blockExprN.size()});
    if (have < nt) {
        for (size_t i = 0; i < have; ++i) {
            rs_->blockMidiN[i] = 0;
            rs_->blockMidiStart[i] = {};
            rs_->blockExprN[i] = 0;
        }
        return;
    }
    const bool keepHeld = heldMidiKeysRead(); // a Nota Color somewhere reads held notes
    const bool hybridActive = hybrid_ && hybrid_->beginRtActive();
    for (size_t i = 0; i < nt; ++i) {
        rs_->blockMidiN[i] = 0;
        rs_->blockExprN[i] = 0;
        Track& t = *g->tracks[i];
        if (hybridActive && hybrid_->isRtBlockWorkerServed(i, t.id())) continue;
        if (t.type() != TrackType::Instrument || !t.instrument) {
            if (t.liveHeld && t.liveHeld->midiKey.mask) t.liveHeld->midiKey = {};
            if (rs_->blockMidiStart[i].mask) rs_->blockMidiStart[i] = {};
            continue;
        }
        auto& held = t.liveHeld->midiKey;
        const bool timeline = playing && arrangementActive;
        if (t.liveHeld->midiKeyArrangement && !timeline) held = {};
        t.liveHeld->midiKeyArrangement = timeline;
        // Session-active tracks feed via renderSessionSlotRaw (their own clock), so skip here.
        const bool sessionActive = t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0;
        if (sessionActive) {
            if (held.mask) held = {};
            if (rs_->blockMidiStart[i].mask) rs_->blockMidiStart[i] = {};
            continue;
        }
        auto& buf = rs_->blockMidi[i];
        if (buf.size() < 1024) {
            rs_->blockMidiStart[i] = {};
            continue;
        } // unreachable: pre-sized
        auto& xbuf = rs_->blockExpr[i];
        if (xbuf.size() < static_cast<size_t>(kMaxBlockExpr)) {
            rs_->blockMidiStart[i] = {};
            continue;
        }
        rs_->blockMidiN[i] = computeInstrumentMidi(
            t, buf.data(), frames, blockStart, spb, playing, arrangementActive, xbuf.data(), &rs_->blockExprN[i], g, i);
        // Frozen source audio is a recording and does not expose its former MIDI. With no device
        // reading held notes (keepHeld) no books are kept. A zero mask means every count is zero,
        // so the 260-byte clears run only when something was held.
        if (!keepHeld || (t.frozen() && t.frozenBuf)) {
            if (held.mask) held = {};
            if (rs_->blockMidiStart[i].mask) rs_->blockMidiStart[i] = {};
        } else {
            if (held.mask || rs_->blockMidiStart[i].mask) rs_->blockMidiStart[i] = held;
            for (int k = 0; k < rs_->blockMidiN[i]; ++k) held.apply(buf[static_cast<size_t>(k)]);
        }
    }
}

// Read the source's MIDI pre-pass results while rendering the consumer. A keyed
// device receives events and the notes that were held before their first offset.
void EngineRuntime::feedMidiKey(Graph* g, Device& d, int32_t srcTrackId) {
    if (!g) return;
    const size_t nt = std::min(g->tracks.size(), rs_->blockMidiN.size());
    auto feedAt = [&](size_t j) {
        const auto& source = *g->tracks[j];
        const bool playable = source.type() == TrackType::Instrument && !(source.frozen() && source.frozenBuf);
        d.setMidiKeyBlock({srcTrackId, rs_->blockMidi[j].data(), rs_->blockMidiN[j],
            playable ? &rs_->blockMidiStart[j] : nullptr, playable ? source.liveHeld->midiKey.mask : 0});
    };
    if (const RenderRoutingPlan* plan = g->routingPlan.get()) {
        const int32_t j = plan->findTrackIndex(srcTrackId);
        if (j >= 0 && static_cast<size_t>(j) < nt && g->tracks[static_cast<size_t>(j)] &&
            g->tracks[static_cast<size_t>(j)]->id() == srcTrackId) {
            feedAt(static_cast<size_t>(j));
            return;
        }
        d.setMidiKeyBlock({srcTrackId});
        return;
    }
    for (size_t j = 0; j < nt; ++j)
        if (g->tracks[j] && g->tracks[j]->id() == srcTrackId) {
            feedAt(j);
            return;
        }
    d.setMidiKeyBlock({srcTrackId});
}

bool EngineRuntime::renderInstrumentRaw(
    Graph* g, Track& t, float* dst, int32_t frames, double blockStart, double spb, bool playing, size_t trackIdx) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    Instrument* inst = t.instrument.get();
    if (!inst) return false;
    inst->setTransport(spb > 0.0 ? blockStart / spb : 0.0, spb, playing); // generative synths (Pendulum)

    MidiEv evs[1024];
    int n = 0;
    ExprEv xev[kMaxBlockExpr];
    int xn = 0;
    if (g) {
        const int32_t srcId = t.midiFromTrackId(); // "MIDI In": the track we receive from (-1 = none)
        // Recording the destination prints the routed MIDI into its take (arm the destination).
        const int32_t recLane = srcId >= 0 ? midiLaneOfRt(t.id()) : -1; // its MIDI take lane (specs/086)
        const bool captureRouted = recLane >= 0 && (playing || countInActive_) && spb > 0.0;
        // MIDI In changed since the last render (#504): the notes the old route started end
        // here, since their note-offs now go nowhere. A route switched away and back between
        // two renders changed nothing, so its notes end with their own note-offs.
        Track::LiveHeld* held = t.liveHeld.get();
        // A replaced instrument never heard the old one's notes: forget them, not release them.
        // Its uid tells a replacement from an edit: an edit that forks the object (a rack chain
        // renamed) keeps the uid and the voices, so its notes are still the route's to end.
        if (held && held->routedInstUid != inst->uid()) {
            held->forgetRouted();
            held->routedInstUid = inst->uid();
        }
        if (held && held->routedFrom != srcId) {
            n = releaseRouted(*held, evs, n);
            held->routedFrom = srcId;
        }
        const bool trackRouted = held && (srcId >= 0 || (held->routedBits[0] | held->routedBits[1]) != 0);
        const size_t nt = std::min(g->tracks.size(), rs_->blockMidiN.size());
        if (!trackRouted && trackIdx < nt && g->tracks[trackIdx] && g->tracks[trackIdx].get() == &t) {
            const int m = rs_->blockMidiN[trackIdx];
            const MidiEv* srcEvs = rs_->blockMidi[trackIdx].data();
            const int copyN = std::min(m, 1024 - n);
            if (copyN > 0) {
                std::memcpy(evs + n, srcEvs, static_cast<size_t>(copyN) * sizeof(MidiEv));
                n += copyN;
            }
            if (trackIdx < rs_->blockExprN.size()) {
                const int copyX = std::min(rs_->blockExprN[trackIdx], kMaxBlockExpr - xn);
                if (copyX > 0) {
                    std::memcpy(xev + xn, rs_->blockExpr[trackIdx].data(), static_cast<size_t>(copyX) * sizeof(ExprEv));
                    xn += copyX;
                }
            }
        } else {
            bool srcFound = false;
            uint64_t decided[2] = {0, 0};
            int32_t decidedAt[128];
            bool decidedSet[128];
            auto visitSource = [&](size_t j) {
                Track* p = g->tracks[j].get();
                if (!p) return;
                const bool self = (p == &t);
                const bool routed = !self && srcId >= 0 && p->type() == TrackType::Instrument && p->id() == srcId;
                if (!self && !routed) return;
                const int m = rs_->blockMidiN[j];
                const MidiEv* srcEvs = rs_->blockMidi[j].data();
                if (routed) srcFound = true;
                for (int k = 0; k < m && n < 1024; ++k) {
                    evs[n++] = srcEvs[k];
                    const int pitch = srcEvs[k].pitch;
                    if (!trackRouted || pitch < 0 || pitch >= 128) continue;
                    if (!routed && !srcEvs[k].on) continue; // an own note-off changes nothing
                    const uint64_t bit = 1ull << (pitch & 63);
                    if ((decided[pitch >> 6] & bit) && srcEvs[k].off < decidedAt[pitch]) continue;
                    decided[pitch >> 6] |= bit;
                    decidedAt[pitch] = srcEvs[k].off;
                    decidedSet[pitch] = routed && srcEvs[k].on;
                }
                if (j < rs_->blockExprN.size())
                    for (int k = 0; k < rs_->blockExprN[j] && xn < kMaxBlockExpr; ++k)
                        xev[xn++] = rs_->blockExpr[j][static_cast<size_t>(k)];
                if (routed && t.armed()) trackRoutedDown(t.id(), srcEvs, m, blockStart, spb, playing || countInActive_);
                if (routed && captureRouted) {
                    captureRoutedNotes(recLane, srcEvs, m, blockStart, spb, countInClockOffset());
                    if (j < rs_->blockExprN.size())
                        for (int k = 0; k < rs_->blockExprN[j]; ++k) {
                            const ExprEv& x = rs_->blockExpr[j][static_cast<size_t>(k)];
                            if (x.dim < kCtlDimBase) continue;
                            RecordedCtl rc{x.dim - kCtlDimBase, (blockStart + x.off) / spb + countInClockOffset(),
                                x.value, recordPass_};
                            rc.lane = recLane;
                            pushRecordedCtl(rc);
                        }
                }
            };
            if (const RenderRoutingPlan* plan = g->routingPlan.get()) {
                const int32_t selfIdx = (trackIdx < nt) ? static_cast<int32_t>(trackIdx) : plan->findTrackIndex(t.id());
                const int32_t routedIdx =
                    (srcId >= 0) ? ((selfIdx >= 0) ? plan->resolveMidiSourceIndex(static_cast<size_t>(selfIdx), srcId)
                                                   : plan->findTrackIndex(srcId))
                                 : -1;
                if (selfIdx >= 0 && static_cast<size_t>(selfIdx) < nt && routedIdx >= 0 &&
                    static_cast<size_t>(routedIdx) < nt && routedIdx != selfIdx) {
                    visitSource(static_cast<size_t>(std::min(selfIdx, routedIdx)));
                    visitSource(static_cast<size_t>(std::max(selfIdx, routedIdx)));
                } else {
                    if (selfIdx >= 0 && static_cast<size_t>(selfIdx) < nt) visitSource(static_cast<size_t>(selfIdx));
                    if (routedIdx >= 0 && static_cast<size_t>(routedIdx) < nt && routedIdx != selfIdx)
                        visitSource(static_cast<size_t>(routedIdx));
                }
            } else {
                for (size_t j = 0; j < nt; ++j) visitSource(j);
            }
            for (int h = 0; h < 2; ++h)
                for (uint64_t bits = decided[h]; bits; bits &= bits - 1) {
                    const int pitch = h * 64 + std::countr_zero(bits);
                    const uint64_t bit = 1ull << (pitch & 63);
                    if (decidedSet[pitch]) held->routedBits[h] |= bit;
                    else
                        held->routedBits[h] &= ~bit;
                }
            // MIDI In names a track that is gone (deleted): its notes end too (#504).
            if (held && srcId >= 0 && !srcFound) n = releaseRouted(*held, evs, n);
            // A switched-off instrument drops this block's events (playEvents), so nothing the route
            // started is sounding on it: forget those notes rather than release them later (#504).
            if (held && inst->bypassed()) held->forgetRouted();
        }
    } else {
        n = computeInstrumentMidi(t, evs, frames, blockStart, spb, playing,
            arrangementActive_.load(std::memory_order_relaxed), xev, &xn); // fallback: no graph
    }

    std::sort(evs, evs + n, midiEvLess);
    std::stable_sort(xev, xev + xn, [](const ExprEv& a, const ExprEv& b) { return a.off < b.off; });

    // Articulation MIDI (specs/085): the track's own arrangement clips, past its MIDI FX, only to an
    // instrument that takes it. A stack array like the notes' (AR-6); overflow is dropped, never grown.
    ArtEv aev[kMaxBlockArt];
    int an = 0;
    if (t.articulationTable && playing && spb > 0.0 && arrangementActive_.load(std::memory_order_relaxed) &&
        inst->takesArticulations()) {
        ArticulationBlock ab;
        ab.frames = frames;
        ab.blockStart = blockStart;
        ab.spb = spb;
        ab.sampleRate = transport_.sampleRate();
        ab.tempoMap = transport_.tempoMap();
        if (ab.tempoMap) ab.blockStart = transport_.playheadSamples();
        ab.trackOffsetMs = t.midiOffsetMs();
        ab.chase = chaseNotes_;
        ab.chaseStart = articulationChaseStart_;
        ab.seamOffs = seamOffs_;
        ab.seamEnd = seamEndSamples_;
        an = scheduleArticulations(t.midiClips.read(), *t.articulationTable, ab, aev, kMaxBlockArt);
    }
    const bool wroteAudio =
        playEvents(*inst, dst, frames, evs, n, xev, xn, aev, an, spb > 0.0 ? blockStart / spb : 0.0, spb, playing, [&] {
            if (liveExprCount_ > 0) applyLiveExpression(g, t, *inst);
        });
    if (!wroteAudio) return false;

    // MIDI clip volume envelope (M9 follow-up): scale the instrument output per
    // sample within each clip's time window (0..1 curve in clip-local beats).
    if (playing) {
        auto applyMidiClipEnv = [&](const MidiClip& clip) {
            if (!clip.active || clip.volumeEnvelope.points.empty()) return;
            const double clipStart = renderSampleAtBeat(clip.startBeat, blockStart, spb);
            const double clipEnd = renderSampleAtBeat(clip.startBeat + clip.lengthBeats, blockStart, spb);
            const bool mappedEnv = transport_.hasTempoMap();
            const double elapsed = transport_.playheadSamples();
            for (int32_t i = 0; i < frames; ++i) {
                const double p = blockStart + i;
                if (p < clipStart || p >= clipEnd) continue;
                const double beat =
                    mappedEnv ? transport_.beatAtSamples(elapsed + i) - clip.startBeat : (p - clipStart) / spb;
                const float envGain = std::clamp(clip.volumeEnvelope.valueAt(beat), 0.0f, 1.0f);
                dst[i * 2] *= envGain;
                dst[i * 2 + 1] *= envGain;
            }
        };
        if (t.timelineIndex && spb > 0.0) {
            if (t.timelineIndex->hasAnyMidiVolumeEnvelope()) {
                const bool mappedEnv = transport_.hasTempoMap();
                const TempoMap* tmapEnv = transport_.tempoMap();
                const double srEnv = transport_.sampleRate() > 0.0 ? transport_.sampleRate() : 44100.0;
                const double elapsed = transport_.playheadSamples();
                const double b0 = (mappedEnv && tmapEnv) ? tmapEnv->beatAt(elapsed / srEnv) : blockStart / spb;
                const double b1 =
                    (mappedEnv && tmapEnv) ? tmapEnv->beatAt((elapsed + frames) / srEnv) : (blockStart + frames) / spb;
                t.timelineIndex->forEachMidiEnvelopeClipInWindow(std::min(b0, b1) - 0.05, std::max(b0, b1) + 0.05,
                    [&](int32_t ci) { applyMidiClipEnv(t.midiClips[static_cast<size_t>(ci)]); });
            }
        } else {
            for (const MidiClip& clip : t.midiClips) applyMidiClipEnv(clip);
        }
    }
    return true;
}

// Renders the playing Session slot's MIDI (looped) through the instrument into
// `dst` (zeroed here), on its own loop clock. M5-2.

} // namespace nota::detail
