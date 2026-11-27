// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — lifecycle, audio/MIDI device config, preview, tone/transport setters, immutable-graph publish/clone/undo/reset, post-fader meters. Definitions of the rest of the class live in the sibling Engine_*.cpp slices.

#include "core/EnginePrelude.h"
#include "core/AudioThreadPool.h"
#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"
#include "render/HybridBufferController.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/Graph.h"
#include "model/EngineTypes.h"
#include "model/ArticulationTable.h"
#include "render/RenderRoutingPlan.h"
#include "model/TimelineIndex.h"
#include "nota/dsp/rt/ScratchArena.h"
#include "nota/devices/Register.h"
#include "nota/io/audio/AudioConfigStore.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "nota/io/midi/MidiDriverRegistry.h"
#include "nota/media/Warp.h" // complete ClipWarpStream for wb_.stream (unique_ptr) destruction

#include <algorithm>
#include <atomic>
#include <cmath>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota::detail {

EngineRuntime::EngineRuntime() : authoring_(std::make_shared<Graph>()) {
    authoring_->masterTrack = std::make_shared<Track>(kMasterTrackId, TrackType::Audio);
    devices::registerBuiltinDevices();
    scratch_.assign(kMaxBlock * 2, 0.0f);
    mixTmp_.assign(kMaxBlock * 2, 0.0f);
    monitorHwBuf_.assign(static_cast<size_t>(kMaxBlock) * kMaxInputChannels, 0.0f); // every carried column (specs/086)
    clipEnvScratch_.assign(kMaxBlock * 2, 0.0f);
    for (auto& b : returnBus_) b.assign(kMaxBlock * 2, 0.0f); // M6-1 send buses
    for (auto& b : groupBus_) b.assign(kMaxBlock * 2, 0.0f);  // group submix buses
    for (auto& b : routeBus_) b.assign(kMaxBlock * 2, 0.0f);  // Phase B route buses (double-buffered)
    for (auto& b : routeBusNext_) b.assign(kMaxBlock * 2, 0.0f);
    for (auto& b : routeBusPre_) b.assign(kMaxBlock * 2, 0.0f); // Phase D pre-FX tap
    for (auto& b : routeBusPreNext_) b.assign(kMaxBlock * 2, 0.0f);
    for (auto& s : routeSlotTrackId_) s.store(-1, std::memory_order_relaxed);
    for (auto& s : routeSlotPart_) s.store(0, std::memory_order_relaxed);
    silentKey_.assign(kMaxBlock * 2, 0.0f); // the key of a part that is gone (specs/050)
    for (auto& b : trackTapRing_) b.assign(static_cast<size_t>(kTrackTapRingFrames) * 2, 0.0f); // #563
    for (auto& s : trackTapRingTrack_) s = -1;
    ensureRenderScratch(authoring_->tracks.size());
    graphHist_.publish(authoring_);
    hybrid_ = std::make_unique<HybridBufferController>(*this);
    hybrid_->onGraphChanged(authoring_);
    racks().installPluginFactory(); // lets rack blobs rebuild hosted-plugin children on load
}

EngineRuntime::~EngineRuntime() {
    clips().cancelWarpJob(); // a background warp build must not outlive the engine
    if (hybrid_) hybrid_->setStopped();
    syncStreamingPlaybackState(false);
    io_.stop();
    if (input_) input_->stop(); // monitoring can keep the capture device open until teardown
}

void EngineRuntime::propagateSampleRate(double sr) {
    if (sr <= 0.0) sr = 44100.0;
    auto rate = [&](Track& t) {
        if (t.instrument) t.instrument->setSampleRate(sr);
        for (auto& d : t.devices)
            if (d) d->setSampleRate(sr, kMaxBlock);
        for (auto& m : t.midiEffects)
            if (m) m->setSampleRate(sr, kMaxBlock);
    };
    for (auto& t : authoring_->tracks)
        if (t) rate(*t);
    if (authoring_->masterTrack) rate(*authoring_->masterTrack);
    if (authoring_) ensureRenderScratch(authoring_->tracks.size());
    preparedSR_ = sr; // the offline render re-prepares only when this changes
}

// Connect hardware MIDI controllers (honouring the user's disabled set). Each source has
// its own MPE decoder (channel state is per controller); notes and expression go into the
// live queue, CC + note-on also to MIDI learn — unless a control surface claims the message
// (specs/051), which sends it to the surface queue instead.
std::shared_ptr<Track> EngineRuntime::findTrackAuthoring(int32_t id) const {
    if (id == kMasterTrackId) return authoring_->masterTrack; // master effect chain
    for (auto& t : authoring_->tracks)
        if (t->id() == id) return t;
    return nullptr;
}

std::shared_ptr<Track> EngineRuntime::cloneTrack(const Track& t) const {
    // Reuse playback state at construction: allocating a fresh player here only to replace it
    // later wastes its fixed release storage on every clip edit and undo.
    // The same goes for the held live notes (#366, #504), shared by every version of the track.
    auto nt = std::make_shared<Track>(t.id(), t.type(), t.sessionPlayer, t.liveHeld);
    nt->setVolume(t.volume());
    nt->setPan(t.pan());
    nt->setMute(t.mute());
    nt->setSolo(t.solo());
    nt->setArmed(t.armed());
    nt->setCue(t.cue());                                         // Cue (specs/089)
    nt->setReturnIndex(t.returnIndex());                         // M6-1
    nt->setGroupId(t.groupId());                                 // group membership
    nt->setRecordInputSource(t.recordInputSource());             // internal record routing
    nt->setInput(t.inputFirst(), t.inputWidth());                // hardware input pick
    nt->setMonitor(t.monitor());                                 // live input monitoring
    nt->setMidiFromTrackId(t.midiFromTrackId());                 // MIDI routing
    nt->setMidiInput(t.midiInputSource(), t.midiInputChannel()); // MIDI input filter (specs/086)
    nt->midiInputUid = t.midiInputUid;
    nt->setMidiOffsetMs(t.midiOffsetMs());       // MIDI playback offset
    nt->articulationTable = t.articulationTable; // articulation map (shared, immutable)
    nt->articulationMapJson = t.articulationMapJson;
    for (int b = 0; b < kMaxReturns; ++b) nt->setSend(b, t.send(b)); // M6-1: send levels
    nt->clips = t.clips;
    nt->takes = t.takes; // take lanes (specs/067) — CoW slice
    nt->midiClips = t.midiClips;
    nt->timelineIndex = t.timelineIndex; // reused when clips/midiClips/articulationTable tokens match
    nt->instrument = t.instrument;       // shared — voice state persists
    nt->midiEffects = t.midiEffects;     // shared — arp held/phase state persists
    nt->devices = t.devices;             // shared — DSP state persists (M3)
    nt->pdc = t.pdc;                     // shared — delay-line state persists (M3-7)
    nt->sessionSlots = t.sessionSlots;   // Session slots (M5) — CoW slice
    nt->automation = t.automation;       // automation lanes (M9) — CoW slice
    nt->modulators = t.modulators;       // CV modulation sources — CoW slice
    nt->cvLinks = t.cvLinks;             // CV modulation edges — CoW slice
    nt->ccRoutesDirty = true;            // Arp CC routes (specs/044): rebuilt when published
    nt->nextModId = t.nextModId;
    nt->setFrozen(t.frozen()); // M7: freeze state + buffer (shared by pointer)
    nt->frozenBuf = t.frozenBuf;
    nt->frozenSpb = t.frozenSpb;
    nt->frozenTiming = t.frozenTiming;
    nt->name = t.name; // UI metadata
    nt->colorIndex = t.colorIndex;
    nt->pianoFold = t.pianoFold;
    nt->editLink = t.editLink;
    return nt;
}

// Whether every clip of `t` (arrangement and Session) has the articulation timeline its notes
// and its track's table call for — O(1) per clip on a track without a map. Message thread.
bool EngineRuntime::articulationsCurrent(const Track& t) {
    if (!t.articulationTable) { // the common case, inline: no map means no timelines
        for (const auto& c : t.midiClips)
            if (c.articulations) return false;
        for (const auto& s : t.sessionSlots)
            if (s.midi.articulations) return false;
        return true;
    }
    for (const auto& c : t.midiClips)
        if (!articulationTimelineCurrent(c, t.articulationTable)) return false;
    for (const auto& s : t.sessionSlots)
        if (!articulationTimelineCurrent(s.midi, t.articulationTable)) return false;
    return true;
}

// Rebuild the stale timelines of `t`, which no render can see yet (a fresh clone). Message thread.
void EngineRuntime::settleArticulations(Track& t) {
    auto settle = [&](MidiClip& c) {
        if (articulationTimelineCurrent(c, t.articulationTable)) return;
        c.articulations = t.articulationTable ? buildArticulationTimeline(c, t.articulationTable) : nullptr;
    };
    for (auto& c : t.midiClips.write()) settle(c);
    for (auto& s : t.sessionSlots.write()) settle(s.midi);
}

// Message thread: make sure the published render scratch holds `tracks` tracks. Never
// grows the live one (the audio thread may be inside a block using it): builds a new one
// with headroom and publishes it. Runs inside publishRaw (before the live-graph swap, so a
// block that sees the new graph sees its scratch too) and propagateSampleRate.
void EngineRuntime::ensureRenderScratch(size_t tracks, int32_t minFrames) {
    const RenderScratch* cur = scratchHist_.live();
    const int32_t curFrames =
        cur && cur->frameCapacity > 0 ? cur->frameCapacity : dsp::StereoScratchArena<1>::kDefaultLiveFrames;
    const int32_t frameCap =
        std::clamp(std::max({curFrames, minFrames, dsp::StereoScratchArena<1>::kDefaultLiveFrames}),
            dsp::StereoScratchArena<1>::kDefaultLiveFrames, kMaxBlock);
    const size_t workerSlots = static_cast<size_t>(std::clamp(pool_ ? pool_->workerCount() + 1 : 1, 1, 65));
    if (cur && cur->tracks >= tracks && cur->frameCapacity >= frameCap && cur->workerPlanarCount >= workerSlots) return;
    const size_t cap = std::max<size_t>(8, (tracks + 7) / 8 * 8); // grow in steps of 8 tracks
    auto s = std::make_shared<RenderScratch>();
    s->tracks = cap;
    s->frameCapacity = frameCap;
    s->workerPlanarCount = workerSlots;
    s->leafWork.resize(cap);
    s->leafBuf.assign(cap * static_cast<size_t>(frameCap) * 4, 0.0f); // two halves (dry + post) per track
    s->groupScratch.assign(static_cast<size_t>(kMaxGroups) * static_cast<size_t>(frameCap) * 2, 0.0f);
    s->returnScratch.assign(static_cast<size_t>(kMaxReturns) * static_cast<size_t>(frameCap) * 2, 0.0f);
    s->workerPlanar.resize(workerSlots);
    for (auto& buf : s->workerPlanar) buf.ensureFrames(frameCap);
    s->blockMidi.assign(cap, std::vector<MidiEv>(1024));
    s->blockMidiN.assign(cap, 0);
    s->blockMidiStart.resize(cap);
    s->blockExpr.assign(cap, std::vector<ExprEv>(kMaxBlockExpr));
    s->blockExprN.assign(cap, 0);
    s->parIdx.assign(cap, 0);
    s->trackRouteSlot.assign(cap, -1);
    scratchHist_.publish(std::move(s));
}

void EngineRuntime::ensureBlockCapacity(int32_t frames) {
    const int32_t target = std::clamp(frames, dsp::StereoScratchArena<1>::kDefaultLiveFrames, kMaxBlock);
    if (target > preparedBlockCap_) preparedBlockCap_ = target;
    if (!authoring_) {
        ensureRenderScratch(0, preparedBlockCap_);
        return;
    }
    ensureRenderScratch(authoring_->tracks.size(), preparedBlockCap_);
    auto prepTrack = [cap = preparedBlockCap_](Track& t) {
        if (t.instrument) t.instrument->ensureBlockCapacity(cap);
        for (auto& d : t.devices)
            if (d) d->ensureBlockCapacity(cap);
    };
    for (auto& t : authoring_->tracks)
        if (t) prepTrack(*t);
    if (authoring_->masterTrack) prepTrack(*authoring_->masterTrack);
}

bool EngineRuntime::setRenderThreads(int n) {
    if (io_.isRunning()) return false; // the audio thread may be inside dispatch()
    pool_ = std::make_unique<AudioThreadPool>(n < 0 ? -1 : n);
    if (authoring_) ensureRenderScratch(authoring_->tracks.size(), preparedBlockCap_);
    return true;
}

// A clip note sounds until the render reaches its note-off. An edit that takes a sounding note
// away — its clip deleted or cut, the note deleted, moved or shortened, an undo, a tool preview
// put back — leaves that note-off nowhere to come from, and the voice held on until Stop
// (#649). While playing, each instrument track an edit replaced is compared with the one
// before it: a note of its arrangement clips around the playhead that the edit took away has
// its pitch released on the audio thread (ReleaseNote, which spares a key held live), unless
// the track still holds that pitch now. The window is wide because the UI's playhead trails
// the render's; releasing a pitch that had already stopped is harmless, and notes the edit
// left as they were release nothing.
void EngineRuntime::releaseVanishedClipNotes(const Graph& before, const Graph& after) {
    const double pos = transport_.uiPositionBeats();
    constexpr double kWindow = 0.5; // beats: what may be sounding around the UI's playhead
    constexpr double kAhead = 0.05; // beats: what the render may already have started past it
    struct Span {
        int32_t pitch;
        double on, off;
    };
    // The notes a track's clips sound within the window around the playhead, in absolute beats.
    const auto around = [&](const Track& t, std::vector<Span>& out) {
        for (const MidiClip& clip : t.midiClips) {
            const double end = clip.startBeat + clip.lengthBeats;
            if (clip.startBeat > pos + kWindow || end < pos - kWindow) continue;
            for (const Note& n : clip.notes) {
                if (n.pitch < 0 || n.pitch >= 128 || n.startBeat >= clip.lengthBeats) continue;
                const double on = clip.startBeat + std::max(0.0, n.startBeat);
                const double off = std::min(end, clip.startBeat + n.startBeat + n.lengthBeats);
                if (off > on && on <= pos + kWindow && off >= pos - kWindow) out.push_back({n.pitch, on, off});
            }
        }
    };
    std::vector<Span> was, is;
    for (size_t i = 0; i < before.tracks.size(); ++i) {
        const auto& old = before.tracks[i];
        if (!old || old->type() != TrackType::Instrument || old->midiClips.empty()) continue;
        const Track* now = nullptr;
        bool same = false;
        if (i < after.tracks.size() && after.tracks[i] && after.tracks[i]->id() == old->id()) {
            now = after.tracks[i].get();
            same = (after.tracks[i] == old);
        } else {
            for (const auto& t : after.tracks)
                if (t && t->id() == old->id()) {
                    now = t.get();
                    same = (t == old);
                    break;
                }
        }
        if (same || !now || old->midiClips.sharesStorageWith(now->midiClips))
            continue; // untouched, or gone with its instrument
        was.clear();
        around(*old, was);
        if (was.empty()) continue;
        is.clear();
        around(*now, is);
        // Only a note the edit took away counts: one still there as it was (a rename, an edit
        // elsewhere) releases nothing. A vanished note's pitch is let go unless the edited
        // track holds that pitch right now — a later note of the pitch doesn't keep it ringing.
        uint32_t gone[4] = {};
        for (const Span& w : was) {
            bool kept = false;
            for (const Span& n : is)
                if (n.pitch == w.pitch && std::fabs(n.on - w.on) < 1e-6 && std::fabs(n.off - w.off) < 1e-6) {
                    kept = true;
                    break;
                }
            if (!kept) gone[w.pitch >> 5] |= 1u << (w.pitch & 31);
        }
        for (const Span& n : is)
            if (n.on <= pos + kAhead && n.off >= pos) gone[n.pitch >> 5] &= ~(1u << (n.pitch & 31));
        for (int p = 0; p < 128; ++p) {
            if (!((gone[p >> 5] >> (p & 31)) & 1u)) continue;
            Command c;
            c.type = CommandType::ReleaseNote;
            c.i0 = old->id();
            c.i1 = p;
            commands_.push(c);
        }
    }
}

namespace {

bool graphNeedsRoutingPlan(const Graph& g) noexcept {
    if (g.tracks.size() > 4) return true;
    if (g.masterTrack && (!g.masterTrack->automation.empty() || !g.masterTrack->cvLinks.empty() ||
                             (g.masterTrack->ccRoutes && !g.masterTrack->ccRoutes->empty()))) {
        return true;
    }
    for (const auto& t : g.tracks) {
        if (!t) continue;
        if (t->type() == TrackType::Group || t->type() == TrackType::Return) return true;
        if (!t->automation.empty() || !t->modulators.empty() || !t->cvLinks.empty()) return true;
        if (t->ccRoutes && !t->ccRoutes->empty()) return true;
        if (t->midiFromTrackId() >= 0) return true;
        if (t->instrument && (t->instrument->acceptsSidechain() || t->instrument->asRack() != nullptr)) return true;
        for (const auto& d : t->devices)
            if (d && (d->acceptsSidechain() || d->asRack() != nullptr)) return true;
    }
    return false;
}

} // namespace

void EngineRuntime::publishRaw(std::shared_ptr<Graph> g, bool restored) {
    if (g && !restored &&
        (!authoring_ || g->masterTrack != authoring_->masterTrack || g->tempoMap != authoring_->tempoMap))
        deriveTempoMap(*g); // specs/099: the master Tempo lane is the tempo
    // Fade flags are derived, not stored. A track this graph shares with the one still
    // playing is the audio thread's — leave it. Any other one an edit just cloned is
    // settled in place. A restored track (undo, redo, take retry) was published before and
    // a render may still be walking it, so it is cloned when its fades would change.
    // Every take id comes from nextTakeId_; until one does (or a load restores a kept join),
    // no clip can join, and this runs on every edit, undo and redo (perf-gate edit-warp-split).
    if (g && authoring_ && (nextTakeId_ > 0 || joinedEdgesRestored_)) {
        auto shared = [&](const std::shared_ptr<Track>& t) {
            for (const auto& old : authoring_->tracks)
                if (old == t) return true;
            return authoring_->masterTrack && authoring_->masterTrack == t;
        };
        auto settle = [&](std::shared_ptr<Track>& t) {
            if (!t || !recorder().mayHaveTakeJoins(*t) || shared(t)) return;
            if (!restored) {
                recorder().settleTakeJoins(*t);
            } else if (!recorder().takeJoinsSettled(*t)) {
                t = cloneTrack(*t);
                recorder().settleTakeJoins(*t);
            }
        };
        for (auto& t : g->tracks) settle(t);
        settle(g->masterTrack);
    }
    // Ensure every track has a session slot per scene (M5). Only grows freshly
    // added tracks (already-sized tracks are a no-op), so no race with audio.
    for (auto& t : g->tracks)
        if (static_cast<int32_t>(t->sessionSlots.size()) < g->sceneCount)
            t->sessionSlots.write().resize(static_cast<size_t>(g->sceneCount));
    // Articulation timelines (specs/085) follow each clip's notes and its track's table. The same
    // rule as the take joins above: a track the playing graph shares is the audio thread's and
    // already current; one an edit just cloned is settled in place; a restored one (undo, redo,
    // take retry) may still be walked by a render, so it is cloned first if anything would change.
    if (g) {
        auto playing = [&](size_t idx, const std::shared_ptr<Track>& t) {
            if (!authoring_) return false;
            if (idx < authoring_->tracks.size() && authoring_->tracks[idx] == t) return true;
            for (const auto& old : authoring_->tracks)
                if (old == t) return true;
            return false;
        };
        for (size_t idx = 0; idx < g->tracks.size(); ++idx) {
            auto& t = g->tracks[idx];
            if (!t || (t->type() != TrackType::Instrument && !t->articulationTable && t->midiClips.empty()) ||
                playing(idx, t) || articulationsCurrent(*t)) {
                continue; // O(1) track/shared check first
            }
            if (restored) {
                t = cloneTrack(*t);
                g->routingPlan.reset();
            }
            settleArticulations(*t);
        }
    }
    // A mapped stretch is prepared before publication, never in render. Reuse it
    // across unrelated edits; clone only the tracks whose cache identity changed.
    if (g && g->tempoMap)
        for (auto& t : g->tracks) {
            bool copied = false;
            for (size_t i = 0; i < t->clips.size(); ++i) {
                const auto& c = t->clips[i];
                if (!c.warpEnabled || !c.sample) continue;
                const auto* cache = c.tempoWarpCache();
                if (cache && c.tempoWarp->map == g->tempoMap && cache->devSR == transport_.sampleRate() &&
                    c.tempoWarp->startBeat == c.startBeat && c.tempoWarp->reversed == c.reversed &&
                    cache->inputs.sample == c.sample.get() && cache->inputs.markers == c.warpMarkers &&
                    cache->inputs.playStart == c.warpPlayStart && cache->inputs.playEnd == c.warpPlayEndEff() &&
                    cache->inputs.pitch == c.pitchSemitones && cache->inputs.mode == c.warpMode)
                    continue;
                if (!copied) {
                    t = cloneTrack(*t);
                    g->routingPlan.reset();
                    copied = true;
                }
                clips().configureTempoWarp(t->clips.write()[i], g->tempoMap, transport_.sampleRate());
            }
        }
    if (g && authoring_ && transport_.uiIsPlaying()) releaseVanishedClipNotes(*authoring_, *g);
    if (g && authoring_) devices().giveUpVanishedPluginLoads(*authoring_, *g);
    if (g) {
        const dsp::TelemetryMode tmode = telemetryMode();
        for (size_t i = 0; i < g->tracks.size(); ++i) {
            const auto& t = g->tracks[i];
            if (!t) continue;
            if (authoring_ && i < authoring_->tracks.size() && authoring_->tracks[i] == t) continue;
            if (t->instrument && t->instrument->telemetryMode() != tmode) t->instrument->setTelemetryMode(tmode);
            for (auto& d : t->devices)
                if (d && d->telemetryMode() != tmode) d->setTelemetryMode(tmode);
        }
        if (g->masterTrack && (!authoring_ || authoring_->masterTrack != g->masterTrack)) {
            if (g->masterTrack->instrument && g->masterTrack->instrument->telemetryMode() != tmode)
                g->masterTrack->instrument->setTelemetryMode(tmode);
            for (auto& d : g->masterTrack->devices)
                if (d && d->telemetryMode() != tmode) d->setTelemetryMode(tmode);
        }
    }
    auto prevGraph = authoring_;
    authoring_ = g;
    if (g) {
        ensureRenderScratch(g->tracks.size(), preparedBlockCap_);
        if (preparedBlockCap_ > dsp::StereoScratchArena<1>::kDefaultLiveFrames) ensureBlockCapacity(preparedBlockCap_);
    }
    // Fresh track objects only: the audio thread never sees them change. Before any CC target
    // exists their routes are complete as they are (empty): marking them so keeps an undo to
    // such a snapshot from rebuilding them from targets set later.
    if (g && ccDestsSet_) racks().syncCcRoutes(*g);
    else if (g) {
        for (auto& t : g->tracks) t->ccRoutesDirty = false;
        if (g->masterTrack) g->masterTrack->ccRoutesDirty = false;
    }
    if (g) {
        for (size_t i = 0; i < g->tracks.size(); ++i) {
            auto& t = g->tracks[i];
            if (!t) continue;
            const bool wantsIndex = (t->clips.size() > 16 || t->midiClips.size() > 16);
            if (prevGraph && i < prevGraph->tracks.size() && prevGraph->tracks[i] == t &&
                (t->timelineIndex || !wantsIndex))
                continue; // shared with previously published graph: already indexed and sealed
            if (!wantsIndex) {
                t->timelineIndex.reset();
            } else {
                if (t->timelineIndex && !t->timelineIndex->matchesTrack(*t)) t->timelineIndex.reset();
                if (!t->timelineIndex) t->timelineIndex = TimelineIndex::buildForTrack(*t);
            }
            t->sealContent();
        }
        if (g->masterTrack && (!prevGraph || prevGraph->masterTrack != g->masterTrack)) g->masterTrack->sealContent();
        if (!graphNeedsRoutingPlan(*g)) {
            g->routingPlan.reset();
        } else if (!restored || !g->routingPlan) {
            g->routingPlan = RenderRoutingPlan::build(*g);
        }
        bool anyAuto = !g->masterVolume.points.empty() || (g->masterTrack && !g->masterTrack->automation.empty());
        bool anyMod = false;
        bool anyCv = false;
        for (const auto& t : g->tracks) {
            if (!t) continue;
            anyAuto |= !t->automation.empty();
            anyMod |= !t->modulators.empty();
            anyCv |= !t->cvLinks.empty();
        }
        g->hasAutomation = anyAuto;
        g->hasModulators = anyMod;
        g->hasCvLinks = anyCv;
    }
    graphHist_.publish(g); // frees the graphs no render can still be walking
    if (hybrid_ && hybrid_->mode() != HybridBufferMode::Off) hybrid_->onGraphChanged(g);
    busState_.requestInvalidate();
    devices().recomputeRouting();    // Phase B: refresh sidechain source-track → route-slot table
    recorder().updateMonitorInput(); // a monitoring track may have come or gone (undo, delete, load)
}

void EngineRuntime::beginUndoGroup() {
    if (undoGroupDepth_++ == 0) undoGroupBase_ = undoStack_.size();
}

void EngineRuntime::endUndoGroup() {
    if (undoGroupDepth_ <= 0 || --undoGroupDepth_ > 0) return;
    // Collapse the group into its first entry: the pre-group graph (the first snapshot any
    // entry of the group holds — a mix-only entry holds none) plus all mix changes merged.
    if (undoStack_.size() > undoGroupBase_ + 1) {
        auto first = undoStack_.begin() + static_cast<std::ptrdiff_t>(undoGroupBase_);
        for (auto it = first + 1; it != undoStack_.end(); ++it) {
            if (!first->graph) {
                first->graph = it->graph;
                first->take = it->take; // a take's checkpoint stays the take's (specs/078)
            }
            mergeMix(first->mix, it->mix);
        }
        undoStack_.erase(first + 1, undoStack_.end());
    }
}

void EngineRuntime::publish(std::shared_ptr<Graph> g) {
    pushUndo();
    publishRaw(std::move(g));
}

void EngineRuntime::republishWithTrackRaw(int32_t trackId, std::shared_ptr<Track> nt) {
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    if (trackId == kMasterTrackId) g->masterTrack = nt; // master chain edit
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) g->tracks.push_back(t->id() == trackId ? nt : t);
    publishRaw(std::move(g));
}

void EngineRuntime::republishWithTrack(int32_t trackId, std::shared_ptr<Track> nt) {
    pushUndo();
    republishWithTrackRaw(trackId, std::move(nt));
}

void EngineRuntime::reset() {
    // Stop playback/recording first so the audio thread reads nothing stale.
    transportStop();
    clips().resetWarpCursor(); // abandon any in-flight warp-cache build (its clip pointer is about to dangle)
    seekBeats(0.0);
    recording_.store(false, std::memory_order_relaxed);
    session().abandonSessionPass(); // #971: the old project's performance is not written into the new one
    if (audioRecording_) recorder().stopAudioRecording();
    io_.stopPreview();
    // Drop automation write/override state: its lane indices belong to the old project.
    activeWrites_.clear();
    overrides_.clear();
    autoRecord_.store(false, std::memory_order_relaxed);
    // Publish an empty graph (no undo checkpoint — a load is a fresh baseline)
    // and reset the id counter and history. A fresh master effect chain comes with it.
    auto empty = std::make_shared<Graph>();
    empty->masterTrack = std::make_shared<Track>(kMasterTrackId, TrackType::Audio);
    publishRaw(std::move(empty));
    panLawSnap_.store(true, std::memory_order_release); // a new project takes its law at once
    nextTrackId_ = 1;
    clearUndoHistory();
}

void EngineRuntime::clearUndoHistory() {
    // The stacks hold old authoring graphs only — never the live one — and every published
    // graph also stays in graphHist_ until the audio thread has moved past it, so dropping
    // them here frees nothing the audio thread can still be reading.
    undoStack_.clear();
    redoStack_.clear();
    devices().compactNoteStates(true); // nothing refers to the saved states any more
    mixGestures_.clear();              // a gesture can't outlive the history it would land in
    touchGestures_.clear();
    undoGroupDepth_ = 0;
    undoGroupBase_ = 0;
}

bool EngineRuntime::trackMeter(int32_t trackId, float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    peakL = t->meterPeakL();
    peakR = t->meterPeakR();
    rmsL = t->meterRmsL();
    rmsR = t->meterRmsR();
    return true;
}

int32_t EngineRuntime::readTrackMeters(TrackMeterEntry* out, int32_t capacity) const {
    if (!authoring_) return 0;
    const auto& tracks = authoring_->tracks;
    const int32_t total = static_cast<int32_t>(tracks.size());
    if (out && capacity > 0) {
        const int32_t n = std::min(total, capacity);
        for (int32_t i = 0; i < n; ++i) {
            const auto& t = tracks[static_cast<size_t>(i)];
            if (!t) {
                out[i] = {};
                out[i].trackId = -1;
                continue;
            }
            out[i].trackId = t->id();
            out[i].meter.peakL = t->meterPeakL();
            out[i].meter.peakR = t->meterPeakR();
            out[i].meter.rmsL = t->meterRmsL();
            out[i].meter.rmsR = t->meterRmsR();
        }
    }
    return total;
}

void EngineRuntime::applyTelemetryMode(const Graph& g, dsp::TelemetryMode mode) {
    auto applyTrack = [mode](Track& t) {
        if (t.instrument) t.instrument->setTelemetryMode(mode);
        for (auto& d : t.devices)
            if (d) d->setTelemetryMode(mode);
    };
    for (const auto& t : g.tracks)
        if (t) applyTrack(*t);
    if (g.masterTrack) applyTrack(*g.masterTrack);
}

void EngineRuntime::setTelemetryMode(dsp::TelemetryMode mode) {
    telemetryMode_.store(mode, std::memory_order_relaxed);
    if (authoring_) applyTelemetryMode(*authoring_, mode);
}

void EngineRuntime::masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    peakL = masterPeakL_.load(std::memory_order_relaxed);
    peakR = masterPeakR_.load(std::memory_order_relaxed);
    rmsL = masterRmsL_.load(std::memory_order_relaxed);
    rmsR = masterRmsR_.load(std::memory_order_relaxed);
}

void EngineRuntime::setStreamThresholdBytes(int64_t bytes) {
    streamThresholdBytes_.store(std::max<int64_t>(0, bytes), std::memory_order_release);
}

void EngineRuntime::setSampleLoadPolicy(SampleLoadPolicy policy) {
    sampleLoadPolicy_.store(static_cast<int32_t>(policy), std::memory_order_release);
}

bool EngineRuntime::isSampleStreaming(int64_t sampleId) const {
    auto s = clips().findSampleById(sampleId);
    return s && s->isStreaming();
}

void EngineRuntime::streamingStats(StreamingStats& out) const {
    out = StreamingStats{};
    out.streamThresholdBytes = streamThresholdBytes();
    if (!authoring_) return;

    std::vector<int64_t> seen;
    auto visit = [&](const std::shared_ptr<SampleBuffer>& sb) {
        if (!sb) return;
        for (int64_t id : seen)
            if (id == sb->id) return;
        seen.push_back(sb->id);
        const int64_t mem = sb->memoryBytes();
        out.residentSampleBytes += mem;
        if (sb->isStreaming()) {
            ++out.activeStreams;
            const StreamRuntimeStats rs = sb->source->runtimeStats();
            out.streamCacheBytes += rs.cacheBytes;
            out.pagesFaulted += rs.pagesFaulted;
            out.pagesServedAsync += rs.pagesServedAsync;
            out.pagesServedSync += rs.pagesServedSync;
            out.rtUnderruns += rs.rtUnderruns;
        }
    };

    for (const auto& t : authoring_->tracks) {
        if (!t) continue;
        for (const auto& c : t->clips) visit(c.sample);
        for (const auto& k : t->takes) visit(k.sample);
        for (const auto& sl : t->sessionSlots) visit(sl.audio.sample);
    }
}

void EngineRuntime::syncStreamingPlaybackState(bool playing, double beatHint) const {
    if (!authoring_) return;
    const double sr = transport_.sampleRate() > 0.0 ? transport_.sampleRate() : 44100.0;
    const double spb = authoringBpm_ > 0.0 ? (sr * 60.0 / authoringBpm_) : transport_.samplesPerBeat();
    for (const auto& t : authoring_->tracks) {
        if (!t) continue;
        for (const auto& c : t->clips) {
            if (!c.sample || !c.sample->source) continue;
            c.sample->source->setPlaybackActive(playing);
            if (beatHint >= 0.0 && spb > 0.0) {
                const double ratio = c.sample->sourceSampleRate / sr * c.pitchRatio();
                const int64_t len = c.effectiveLength();
                const double clipBeats = static_cast<double>(len) / std::max(1e-9, ratio * spb);
                if (beatHint >= c.startBeat - 1.0 && beatHint <= c.startBeat + clipBeats) {
                    const double relFrames = std::max(0.0, (beatHint - c.startBeat) * spb * ratio);
                    const int64_t targetFrame =
                        c.reversed ? static_cast<int64_t>(c.sourceOffsetFrames +
                                                          std::max(0.0, static_cast<double>(len) - 1.0 - relFrames))
                                   : static_cast<int64_t>(c.sourceOffsetFrames + relFrames);
                    c.sample->source->primeAround(targetFrame, c.reversed);
                }
            }
        }
        for (const auto& sl : t->sessionSlots) {
            if (sl.audio.sample && sl.audio.sample->source) sl.audio.sample->source->setPlaybackActive(playing);
        }
    }
}

void EngineRuntime::setHybridBufferingMode(HybridBufferMode mode) {
    if (hybrid_) {
        hybrid_->setMode(mode);
        if (mode == HybridBufferMode::Auto) hybrid_->onGraphChanged(authoring_);
    }
}

} // namespace nota::detail
