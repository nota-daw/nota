// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — tracks, mixer strips, send/return buses, arrangement clips (geometry + move/trim/split/duplicate/delete), MIDI clips & notes, and audio-clip/sample persistence.

#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "tracks/TrackOps.h"
#include "core/EngineRuntime.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/Graph.h"
#include "core/TempoMap.h"

#include <algorithm>
#include <cstdint>
#include <cstddef>
#include <atomic>
#include <cstring>
#include <cmath>
#include <limits>
#include <map>
#include <memory>
#include <set>
#include <vector>
#include <utility>

namespace nota::detail {

int32_t TrackOps::addAudioTrack() {
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto t = std::make_shared<Track>(id, TrackType::Audio);
    // A new track records input 1 alone, as most microphones are mono (#340); stereo (1/2) is
    // one pick away. Older projects keep the stereo pair they were made with.
    t->setInputChannels(Track::kInputMono1);
    g->tracks.push_back(std::move(t));
    rt_.publish(g);
    return id;
}

void TrackOps::setTrackVolume(int32_t id, float v) {
    if (auto t = rt_.findTrackAuthoring(id)) t->setVolume(v);
}
void TrackOps::setTrackPan(int32_t id, float p) {
    if (auto t = rt_.findTrackAuthoring(id)) t->setPan(p);
}
void TrackOps::setTrackMute(int32_t id, bool m) {
    if (auto t = rt_.findTrackAuthoring(id)) t->setMute(m);
}
void TrackOps::setTrackSolo(int32_t id, bool s) {
    if (auto t = rt_.findTrackAuthoring(id)) t->setSolo(s);
}
bool TrackOps::setTrackCue(int32_t id, bool on) {
    auto t = rt_.findTrackAuthoring(id);
    if (!t) return false;
    t->setCue(on);
    return true;
}
int32_t TrackOps::trackCue(int32_t id) const {
    auto t = rt_.findTrackAuthoring(id);
    return t ? (t->cue() ? 1 : 0) : -1;
}
int32_t TrackOps::trackCount() const {
    return static_cast<int32_t>(rt_.authoring_->tracks.size());
}

// --- send/return buses (M6-1) ----------------------------------------------

int32_t TrackOps::returnTrackCount() const {
    int32_t n = 0;
    for (auto& t : rt_.authoring_->tracks)
        if (t->type() == TrackType::Return) ++n;
    return n;
}

int32_t TrackOps::addReturnTrack() {
    const int32_t idx = returnTrackCount();
    if (idx >= kMaxReturns) return -1; // bus slots are fixed
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto rt = std::make_shared<Track>(id, TrackType::Return);
    rt->setReturnIndex(idx);
    g->tracks.push_back(rt);
    rt_.publish(g);
    return id;
}

void TrackOps::setTrackSend(int32_t id, int32_t bus, float level) {
    if (auto t = rt_.findTrackAuthoring(id)) t->setSend(bus, level);
}
float TrackOps::trackSend(int32_t id, int32_t bus) const {
    auto t = rt_.findTrackAuthoring(id);
    return t ? t->send(bus) : 0.0f;
}
int32_t TrackOps::trackReturnIndex(int32_t id) const {
    auto t = rt_.findTrackAuthoring(id);
    return t ? t->returnIndex() : -1;
}

// Peak-pick a warped clip's source envelope over a beat range [beat0, beat1] via the
bool TrackOps::trackInfo(int32_t index, TrackInfo* out) const {
    if (!out || index < 0 || index >= static_cast<int32_t>(rt_.authoring_->tracks.size())) return false;
    const Track& t = *rt_.authoring_->tracks[static_cast<size_t>(index)];
    const bool inst = t.type() == TrackType::Instrument;
    const bool ret = t.type() == TrackType::Return;
    const bool grp = t.type() == TrackType::Group;
    out->id = t.id();
    out->type = grp ? 3 : ret ? 2 : (inst ? 1 : 0); // 0=audio,1=instrument,2=return,3=group
    out->clipCount =
        (ret || grp) ? 0 : (inst ? static_cast<int32_t>(t.midiClips.size()) : static_cast<int32_t>(t.clips.size()));
    out->muted = t.mute() ? 1 : 0;
    out->soloed = t.solo() ? 1 : 0;
    out->armed = t.armed() ? 1 : 0;
    out->volume = t.volume();
    out->pan = t.pan();
    out->groupId = t.groupId();
    return true;
}

bool TrackOps::clipInfo(int32_t trackId, int32_t clipIndex, ClipInfo* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    if (t->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return false;
        const MidiClip& c = t->midiClips[static_cast<size_t>(clipIndex)];
        out->startBeat = c.startBeat;
        out->lengthBeats = c.lengthBeats;
        out->kind = 1;
        out->active = c.active ? 1 : 0;
        return true;
    }
    if (!audioClipRef(*t, clipIndex)) return false;
    const AudioClip& c = (*audioClipRef(*t, clipIndex));
    out->startBeat = c.startBeat;
    out->kind = 0;
    out->active = c.active ? 1 : 0;
    // Warped clips hold a fixed musical length = the trimmed play window; unwarped
    // length depends on tempo.
    if (c.warpEnabled && c.warpBeats > 0.0) {
        out->lengthBeats = c.warpPlayLen();
        return true;
    }
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    if (c.sample && spb > 0.0 && devSR > 0.0 && c.sample->sourceSampleRate > 0.0) {
        const double devFrames =
            static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / c.pitchRatio();
        out->lengthBeats = rt_.tempoMap()
                               ? rt_.secondsToBeat(rt_.beatToSeconds(c.startBeat) + devFrames / devSR) - c.startBeat
                               : devFrames / spb;
    } else {
        out->lengthBeats = 0.0;
    }
    return true;
}

// --- clip editing (M4-2) ---------------------------------------------------
bool TrackOps::deleteClip(int32_t trackId, int32_t clipIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        auto& mc = nt->midiClips.write();
        mc.erase(mc.begin() + clipIndex);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        auto& ac = nt->clips.write();
        ac.erase(ac.begin() + clipIndex);
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// Fully independent copy of a track: cloned instrument/MIDI-FX/insert devices (own DSP
// state), copied clips/automation with fresh warp stretchers, and the UI metadata.
// withContent = false leaves out what it plays: clips, Session slots and automation.
std::shared_ptr<Track> TrackOps::deepCloneTrack(const Track& src, int32_t newId, bool withContent) {
    auto nt = std::make_shared<Track>(newId, src.type());
    nt->setVolume(src.volume());
    nt->setPan(src.pan());
    nt->setMute(src.mute());
    nt->setSolo(src.solo());
    nt->setArmed(src.armed());
    nt->setCue(src.cue());
    nt->setReturnIndex(src.returnIndex());
    nt->setGroupId(src.groupId()); // keep membership on duplicate (paste resets to top-level)
    nt->setRecordInputSource(src.recordInputSource());
    nt->setInput(src.inputFirst(), src.inputWidth());
    nt->setMonitor(src.monitor());
    nt->setMidiFromTrackId(src.midiFromTrackId());
    nt->setMidiInput(src.midiInputSource(), src.midiInputChannel());
    nt->midiInputUid = src.midiInputUid;
    nt->setMidiOffsetMs(src.midiOffsetMs());
    nt->articulationTable = src.articulationTable; // the map goes with the instrument, content or not
    nt->articulationMapJson = src.articulationMapJson;
    for (int b = 0; b < kMaxReturns; ++b) nt->setSend(b, src.send(b));
    if (withContent) {
        nt->clips = src.clips; // sample buffers are shared_ptr (immutable) — fine
        nt->takes = src.takes;
        nt->midiClips = src.midiClips;
        nt->sessionSlots = src.sessionSlots;
        nt->automation = src.automation;
    }
    nt->name = src.name;
    nt->colorIndex = src.colorIndex;
    nt->pianoFold = src.pianoFold;
    nt->editLink = src.editLink;
    nt->instrument = rt_.devices().cloneInstrument(src);
    if (nt->instrument && src.instrument) nt->instrument->setBypassed(src.instrument->bypassed());
    for (auto& m : src.midiEffects)
        if (auto nm = rt_.devices().cloneMidiDevice(*m)) nt->midiEffects.push_back(nm);
    for (auto& d : src.devices)
        if (auto nd = rt_.devices().cloneDevice(*d)) nt->devices.push_back(nd);
    // Warped clips must not share a stretcher across tracks — rebuild per clip.
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    bool hasWarped = false;
    for (const auto& c : nt->clips)
        if (c.warpEnabled && c.sample) {
            hasWarped = true;
            break;
        }
    if (hasWarped) {
        for (auto& c : nt->clips.write())
            if (c.warpEnabled && c.sample) rt_.clips().configureClipWarp(c, spb, devSR);
    }
    return nt;
}

int32_t TrackOps::duplicateTrack(int32_t trackId) {
    auto src = rt_.findTrackAuthoring(trackId);
    if (!src) return -1;
    const int32_t newId = rt_.nextTrackId_++;
    auto nt = deepCloneTrack(*src, newId);

    // Insert the copy right after the source (new snapshot; checkpoints undo).
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size() + 1);
    for (auto& t : rt_.authoring_->tracks) {
        g->tracks.push_back(t);
        if (t->id() == trackId) g->tracks.push_back(nt);
    }
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return newId;
}

int32_t TrackOps::addTrackLike(int32_t trackId) {
    if (trackId == kMasterTrackId) return -1;
    auto src = rt_.findTrackAuthoring(trackId);
    if (!src || src->type() == TrackType::Return || src->type() == TrackType::Group) return -1;
    const int32_t newId = rt_.nextTrackId_++;
    auto nt = deepCloneTrack(*src, newId, /*withContent=*/false); // publish sizes the Session slots, all empty
    // The source's clips move onto this track, so it must sound the same: refuse it when the
    // instrument or any device didn't copy (a plugin that won't load a second instance).
    if (static_cast<bool>(src->instrument) != static_cast<bool>(nt->instrument) ||
        src->midiEffects.size() != nt->midiEffects.size() || src->devices.size() != nt->devices.size())
        return -1;
    nt->setGroupId(-1);
    nt->setArmed(false);
    nt->name.clear();
    nt->colorIndex = -1;

    // After the last regular track. Returns aren't always trailing in the graph (a track
    // added later is appended after them), so place it by the last regular track, not
    // in front of the first return.
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size() + 1);
    std::ptrdiff_t lastRegular = -1;
    for (std::size_t i = 0; i < rt_.authoring_->tracks.size(); ++i)
        if (rt_.authoring_->tracks[i]->type() != TrackType::Return) lastRegular = static_cast<std::ptrdiff_t>(i);
    if (lastRegular < 0) g->tracks.push_back(nt);
    for (std::size_t i = 0; i < rt_.authoring_->tracks.size(); ++i) {
        g->tracks.push_back(rt_.authoring_->tracks[i]);
        if (static_cast<std::ptrdiff_t>(i) == lastRegular) g->tracks.push_back(nt);
    }
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return newId;
}

bool TrackOps::removeTrack(int32_t trackId) {
    auto rt = rt_.findTrackAuthoring(trackId);
    if (!rt) return false;
    // Close any open plugin editor windows for this track before it leaves the live
    // graph — otherwise the native window lingers on screen after the track is gone.
    // (The track object itself survives in undo history, so the plugin isn't freed
    // yet; hiding the editor is the visible fix.)
    if (rt->instrument) rt->instrument->closeEditor();
    for (auto& d : rt->devices)
        if (d) d->closeEditor();
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks)
        if (t->id() != trackId) g->tracks.push_back(t);
    if (rt->type() == TrackType::Return) compactReturns(*g);
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return true;
}

bool TrackOps::moveTrack(int32_t trackId, int32_t toIndex) {
    auto rt = rt_.findTrackAuthoring(trackId);
    if (!rt || rt->type() == TrackType::Return) return false; // returns aren't reorderable here
    // Split into the movable (non-return) list + the returns, which keep their trailing slots.
    std::vector<std::shared_ptr<Track>> regular, returns;
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() == TrackType::Return) returns.push_back(t);
        else
            regular.push_back(t);
    }
    int32_t from = -1;
    for (int32_t i = 0; i < static_cast<int32_t>(regular.size()); ++i)
        if (regular[static_cast<size_t>(i)]->id() == trackId) {
            from = i;
            break;
        }
    if (from < 0) return false;
    int32_t to = std::clamp(toIndex, 0, static_cast<int32_t>(regular.size()) - 1);
    if (to == from) return true;
    auto tr = regular[static_cast<size_t>(from)];
    regular.erase(regular.begin() + from);
    regular.insert(regular.begin() + to, tr);
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(regular.size() + returns.size());
    for (auto& t : regular) g->tracks.push_back(t);
    for (auto& t : returns) g->tracks.push_back(t);
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return true;
}

// --- track groups (submix buses) -------------------------------------------
int32_t TrackOps::addGroupTrack() {
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    g->tracks.push_back(std::make_shared<Track>(id, TrackType::Group));
    rt_.publish(g);
    return id;
}

int32_t TrackOps::createGroup(const int32_t* ids, int32_t n) {
    if (!ids || n <= 0) return -1;
    std::vector<int32_t> members;
    int32_t parent = -1;
    for (int32_t i = 0; i < n; ++i) {
        auto t = rt_.findTrackAuthoring(ids[i]);
        if (!t || t->type() == TrackType::Return) continue;
        if (members.empty()) parent = t->groupId(); // nest the group under the members' common parent
        if (std::find(members.begin(), members.end(), ids[i]) == members.end()) members.push_back(ids[i]);
    }
    if (members.empty()) return -1;
    const int32_t gid = rt_.nextTrackId_++;
    auto grp = std::make_shared<Track>(gid, TrackType::Group);
    grp->setGroupId(parent);
    auto isMember = [&](int32_t id) { return std::find(members.begin(), members.end(), id) != members.end(); };

    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size() + 1);
    bool placed = false;
    for (auto& t : rt_.authoring_->tracks) {
        if (isMember(t->id())) {
            if (!placed) {
                g->tracks.push_back(grp);
                placed = true;
            } // group sits at the topmost member
            auto nt = rt_.cloneTrack(*t);
            nt->setGroupId(gid);
            g->tracks.push_back(nt);
        } else {
            g->tracks.push_back(t);
        }
    }
    if (!placed) g->tracks.push_back(grp);
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return gid;
}

bool TrackOps::ungroup(int32_t groupId) {
    auto grp = rt_.findTrackAuthoring(groupId);
    if (!grp || grp->type() != TrackType::Group) return false;
    for (auto& d : grp->devices)
        if (d) d->closeEditor();           // its plugin windows shouldn't linger
    const int32_t parent = grp->groupId(); // children reparent up one level
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->id() == groupId) continue; // drop the group track
        if (t->groupId() == groupId) {
            auto nt = rt_.cloneTrack(*t);
            nt->setGroupId(parent);
            g->tracks.push_back(nt);
        } else
            g->tracks.push_back(t);
    }
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return true;
}

bool TrackOps::setTrackGroup(int32_t trackId, int32_t groupId) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() == TrackType::Return || trackId == groupId) return false;
    if (t->groupId() == groupId) return true; // already there
    if (groupId >= 0) {
        auto grp = rt_.findTrackAuthoring(groupId);
        if (!grp || grp->type() != TrackType::Group) return false;
        // Reject cycles: groupId must not live inside trackId's own subtree.
        for (int32_t a = groupId; a >= 0;) {
            if (a == trackId) return false;
            auto at = rt_.findTrackAuthoring(a);
            a = at ? at->groupId() : -1;
        }
    }
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& tt : rt_.authoring_->tracks) {
        if (tt->id() == trackId) {
            auto nt = rt_.cloneTrack(*tt);
            nt->setGroupId(groupId);
            g->tracks.push_back(nt);
        } else
            g->tracks.push_back(tt);
    }
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return true;
}

// --- multi-track sets (header multi-selection) -----------------------------
std::vector<std::shared_ptr<Track>> TrackOps::expandTrackSet(const int32_t* ids, int32_t n) const {
    std::vector<std::shared_ptr<Track>> out;
    if (!ids || n <= 0) return out;
    std::set<int32_t> want(ids, ids + n);
    // A track belongs when it or any ancestor group is in the set.
    auto inSet = [&](const Track& t) {
        if (want.count(t.id())) return true;
        for (int32_t a = t.groupId(), hops = 0; a >= 0 && hops < 64; ++hops) {
            if (want.count(a)) return true;
            auto p = rt_.findTrackAuthoring(a);
            a = p ? p->groupId() : -1;
        }
        return false;
    };
    for (auto& t : rt_.authoring_->tracks)
        if (inSet(*t)) out.push_back(t);
    return out;
}

// Deleting a return leaves a hole in the bus slots, but the renderer only runs buses
// 0..count-1 and addReturnTrack hands out slot `count` — so the survivors move down to
// 0..n-1 (in track order) and every send — level and lane — follows its bus; sends to a
// deleted bus go.
// Changed tracks are cloned, never edited in place: the old graph is the undo snapshot.
void TrackOps::compactReturns(Graph& g) const {
    int32_t remap[kMaxReturns];
    std::fill(std::begin(remap), std::end(remap), -1);
    int32_t n = 0;
    for (auto& t : g.tracks) {
        if (t->type() != TrackType::Return) continue;
        const int32_t old = t->returnIndex();
        if (old >= 0 && old < kMaxReturns) remap[old] = n++;
    }
    for (auto& t : g.tracks) {
        if (t->type() == TrackType::Return) {
            const int32_t old = t->returnIndex();
            const int32_t idx = (old >= 0 && old < kMaxReturns) ? remap[old] : -1;
            if (idx < 0 || idx == old) continue;
            auto nt = rt_.cloneTrack(*t);
            nt->setReturnIndex(idx);
            t = std::move(nt);
            continue;
        }
        float sends[kMaxReturns] = {};
        for (int32_t b = 0; b < kMaxReturns; ++b)
            if (remap[b] >= 0) sends[remap[b]] = t->send(b);
        bool changed = false;
        for (int32_t b = 0; b < kMaxReturns; ++b)
            if (sends[b] != t->send(b)) changed = true;
        // Send lanes (specs/072) follow their bus too; a lane to a deleted bus goes.
        auto busOf = [&](const TrackLane& l) {
            return l.deviceIndex >= 0 && l.deviceIndex < kMaxReturns ? remap[l.deviceIndex] : -1;
        };
        for (const auto& l : t->automation)
            if (l.target == AutomationTarget::Send && !l.nested() && busOf(l) != l.deviceIndex) changed = true;
        if (!changed) continue;
        auto nt = rt_.cloneTrack(*t);
        for (int32_t b = 0; b < kMaxReturns; ++b) nt->setSend(b, sends[b]);
        bool anySendLane = false;
        for (const auto& l : nt->automation)
            if (l.target == AutomationTarget::Send && !l.nested()) {
                anySendLane = true;
                break;
            }
        if (anySendLane) {
            auto& lanes = nt->automation.write();
            for (auto& l : lanes)
                if (l.target == AutomationTarget::Send && !l.nested()) l.deviceIndex = busOf(l);
            lanes.erase(std::remove_if(lanes.begin(), lanes.end(),
                            [](const TrackLane& l) { return l.target == AutomationTarget::Send && l.deviceIndex < 0; }),
                lanes.end());
        }
        t = std::move(nt);
    }
}

std::vector<int32_t> TrackOps::insertTrackClones(
    const std::vector<std::shared_ptr<Track>>& src, int32_t afterId, int32_t rootParent, bool keepParent) {
    std::map<int32_t, int32_t> remap; // source id -> copy id
    for (auto& s : src)
        if (s->type() != TrackType::Return) remap[s->id()] = rt_.nextTrackId_++;
    std::vector<std::shared_ptr<Track>> clones;
    std::vector<int32_t> newIds;
    for (auto& s : src) {
        if (s->type() == TrackType::Return) continue; // return-bus identity can't be duplicated
        auto nt = deepCloneTrack(*s, remap[s->id()]);
        auto it = remap.find(s->groupId());
        nt->setGroupId(it != remap.end() ? it->second : keepParent ? s->groupId() : rootParent);
        clones.push_back(nt);
        newIds.push_back(nt->id());
    }
    if (clones.empty()) return newIds;

    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size() + clones.size());
    for (auto& t : rt_.authoring_->tracks) g->tracks.push_back(t);
    int32_t insertAt = -1;
    for (int32_t i = 0; i < static_cast<int32_t>(g->tracks.size()); ++i)
        if (g->tracks[static_cast<size_t>(i)]->id() == afterId &&
            g->tracks[static_cast<size_t>(i)]->type() != TrackType::Return) {
            insertAt = i + 1;
            break;
        }
    if (insertAt < 0) {
        // Append after the last non-return track so the copies sit with the regular tracks.
        insertAt = static_cast<int32_t>(g->tracks.size());
        for (int32_t i = 0; i < static_cast<int32_t>(g->tracks.size()); ++i)
            if (g->tracks[static_cast<size_t>(i)]->type() == TrackType::Return) {
                insertAt = i;
                break;
            }
    }
    g->tracks.insert(g->tracks.begin() + insertAt, clones.begin(), clones.end());
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return newIds;
}

int32_t TrackOps::duplicateTracks(const int32_t* ids, int32_t n, int32_t* outIds, int32_t cap) {
    auto set = expandTrackSet(ids, n);
    set.erase(std::remove_if(set.begin(), set.end(),
                  [](const std::shared_ptr<Track>& t) { return t->type() == TrackType::Return; }),
        set.end());
    if (set.empty()) return -1;
    auto newIds = insertTrackClones(set, set.back()->id(), -1, /*keepParent*/ true);
    const int32_t count = static_cast<int32_t>(newIds.size());
    if (outIds)
        for (int32_t i = 0; i < count && i < cap; ++i) outIds[i] = newIds[static_cast<size_t>(i)];
    return count > 0 ? count : -1;
}

bool TrackOps::removeTracks(const int32_t* ids, int32_t n) {
    auto set = expandTrackSet(ids, n);
    if (set.empty()) return false;
    std::set<int32_t> drop;
    bool anyReturn = false;
    for (auto& t : set) {
        anyReturn |= t->type() == TrackType::Return;
        // Plugin windows shouldn't outlive the track (it survives in undo history, so only hide).
        if (t->instrument) t->instrument->closeEditor();
        for (auto& d : t->devices)
            if (d) d->closeEditor();
        drop.insert(t->id());
    }
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks)
        if (!drop.count(t->id())) g->tracks.push_back(t);
    if (anyReturn) compactReturns(*g);
    rt_.publish(std::move(g));
    rt_.devices().recomputePdc();
    return true;
}

// --- track clipboard (copy/cut/paste) --------------------------------------
bool TrackOps::copyTrack(int32_t trackId) {
    return copyTracks(&trackId, 1);
}

bool TrackOps::copyTracks(const int32_t* ids, int32_t n) {
    auto set = expandTrackSet(ids, n);
    if (set.empty()) return false;
    std::vector<std::shared_ptr<Track>> clip;
    clip.reserve(set.size());
    for (auto& t : set) clip.push_back(deepCloneTrack(*t, t->id())); // source id kept for re-linking
    trackClipboard_ = std::move(clip);
    return true;
}

bool TrackOps::hasTrackClipboard() const {
    return !trackClipboard_.empty();
}

int32_t TrackOps::pasteTrack() {
    std::vector<int32_t> ids(trackClipboard_.size());
    int32_t n = pasteTracks(-1, ids.data(), static_cast<int32_t>(ids.size()));
    return n > 0 ? ids[0] : -1;
}

int32_t TrackOps::pasteTracks(int32_t afterTrackId, int32_t* outIds, int32_t cap) {
    if (trackClipboard_.empty()) return -1;
    // Land next to the anchor, in its group; without one, top-level (the source groups may be gone).
    int32_t parent = -1;
    auto anchor = rt_.findTrackAuthoring(afterTrackId);
    if (anchor && anchor->type() != TrackType::Return && anchor->id() != kMasterTrackId) parent = anchor->groupId();
    else
        afterTrackId = -1;
    auto newIds = insertTrackClones(trackClipboard_, afterTrackId, parent, /*keepParent*/ false);
    const int32_t count = static_cast<int32_t>(newIds.size());
    if (outIds)
        for (int32_t i = 0; i < count && i < cap; ++i) outIds[i] = newIds[static_cast<size_t>(i)];
    return count > 0 ? count : -1;
}

} // namespace nota::detail
