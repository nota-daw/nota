// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ClipOps — clip & block/range copy/cut/paste/duplicate and automation-follows-clips.

#include "automation/AutomationOps.h"
#include "clips/ClipHelpers.h"
#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "core/Transport.h"
#include "model/Graph.h"
#include "model/MidiClip.h"
#include "model/Track.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
#include <utility>
#include <vector>

namespace nota::detail {

using namespace nota::detail::clip_helpers;

// --- clip clipboard (copy/cut/paste, incl. cross-track) ---------------------

// Pull the points of every automation lane that fall in [start,end] out into a
// per-lane snippet, with beats rebased to be relative to `start`. Empty lanes are
// dropped so paste only touches targets the clip actually carried.
std::vector<TrackLane> ClipOps::captureClipAutomation(const Track& t, double start, double end) const {
    constexpr double eps = 1e-6;
    std::vector<TrackLane> out;
    for (const auto& lane : t.automation) {
        TrackLane snip;
        snip.target = lane.target;
        snip.deviceIndex = lane.deviceIndex;
        snip.paramIndex = lane.paramIndex;
        snip.paramId = lane.paramId;
        snip.chainUid = lane.chainUid; // a lane into a rack (specs/044)
        snip.slot = lane.slot;
        for (const auto& p : lane.points)
            if (p.beat >= start - eps && p.beat <= end + eps) {
                AutomationPoint q = p;
                q.beat = p.beat - start; // clip-relative
                snip.points.push_back(q);
            }
        if (!snip.points.empty()) out.push_back(std::move(snip));
    }
    return out;
}

// Drop every automation point in [start,end] across all of `t`'s lanes (Cut, and the
// pre-clear before a paste re-lands its points).
void ClipOps::removeAutomationInRange(Track& t, double start, double end) {
    constexpr double eps = 1e-6;
    bool anyToRemove = false;
    for (const auto& lane : t.automation) {
        for (const auto& p : lane.points)
            if (p.beat >= start - eps && p.beat <= end + eps) {
                anyToRemove = true;
                break;
            }
        if (anyToRemove) break;
    }
    if (!anyToRemove) return;
    for (auto& lane : t.automation.write()) {
        auto& pts = lane.points;
        pts.erase(std::remove_if(pts.begin(), pts.end(),
                      [&](const AutomationPoint& p) { return p.beat >= start - eps && p.beat <= end + eps; }),
            pts.end());
    }
}

bool ClipOps::tracksDeviceLayoutMatch(const Track& a, const Track& b) {
    const int ka = a.instrument ? a.instrument->kind() : -100;
    const int kb = b.instrument ? b.instrument->kind() : -100;
    if (ka != kb) return false;
    if (a.devices.size() != b.devices.size()) return false;
    for (size_t i = 0; i < a.devices.size(); ++i)
        if (a.devices[i]->builtinKind() != b.devices[i]->builtinKind()) return false;
    return true;
}

// Re-land captured automation onto `nt`, placing the snippet's relative beats at
// placedStart..placedStart+len. Each matching lane's target span is cleared first so
// the pasted envelope replaces (not overlaps) whatever was there. Blocks separate
// clearing from insertion so one landing cannot erase another's copied points. Device/plugin/MIDI
// lanes only apply when sameTrack — their indices are positional, so a cross-track
// paste would otherwise bind them to the wrong device.
void ClipOps::applyClipAutomation(Track& nt, int32_t trackId, const std::vector<TrackLane>& snips, double placedStart,
    double len, bool sameTrack, ClipAutomationPass pass) const {
    const double s = placedStart, e = placedStart + len;
    for (const auto& snip : snips) {
        if (!sameTrack && !isMixTarget(snip.target)) continue;
        // Find the matching lane on nt, or create one bound to the same target.
        int32_t laneIdx = -1;
        {
            const auto& autos = nt.automation.read();
            for (int32_t li = 0; li < static_cast<int32_t>(autos.size()); ++li) {
                const auto& l = autos[static_cast<size_t>(li)];
                if (l.target != snip.target || l.chainUid != snip.chainUid || l.slot != snip.slot) continue;
                bool match = true;
                switch (snip.target) {
                case AutomationTarget::DeviceParam:
                case AutomationTarget::MidiDeviceParam:
                    match = l.deviceIndex == snip.deviceIndex && l.paramIndex == snip.paramIndex;
                    break;
                case AutomationTarget::PluginParam:
                    match = l.deviceIndex == snip.deviceIndex && l.paramId == snip.paramId;
                    break;
                case AutomationTarget::Send: match = l.deviceIndex == snip.deviceIndex; break; // one lane per bus
                default: break; // Volume / Pan: single lane
                }
                if (match) {
                    laneIdx = li;
                    break;
                }
            }
        }
        if (laneIdx < 0 && pass == ClipAutomationPass::Clear) continue;
        TrackLane* lane = nullptr;
        auto& autos = nt.automation.write();
        if (laneIdx >= 0) {
            lane = &autos[static_cast<size_t>(laneIdx)];
        } else {
            TrackLane nl;
            nl.target = snip.target;
            nl.deviceIndex = snip.deviceIndex;
            nl.paramId = snip.paramId;
            nl.chainUid = snip.chainUid;
            nl.slot = snip.slot;
            nl.paramIndex = snip.target == AutomationTarget::PluginParam && !snip.nested()
                                ? rt_.automation().pluginParamIndexOfId(trackId, snip.deviceIndex, snip.paramId)
                                : snip.paramIndex;
            autos.push_back(std::move(nl));
            lane = &autos.back();
        }
        auto& pts = lane->points;
        constexpr double eps = 1e-6;
        if (pass != ClipAutomationPass::Insert)
            pts.erase(std::remove_if(pts.begin(), pts.end(),
                          [&](const AutomationPoint& p) { return p.beat >= s - eps && p.beat <= e + eps; }),
                pts.end());
        if (pass == ClipAutomationPass::Clear) continue;
        for (const auto& p : snip.points) {
            AutomationPoint q = p;
            q.beat = placedStart + p.beat;
            pts.push_back(q);
        }
        std::sort(
            pts.begin(), pts.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    }
}

bool ClipOps::copyClip(int32_t trackId, int32_t clipIndex) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    double start = 0.0, len = 0.0;
    if (t->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return false;
        clipboardMidi_ = t->midiClips[static_cast<size_t>(clipIndex)];
        clipboardKind_ = 1;
        start = clipboardMidi_.startBeat;
        len = clipboardMidi_.lengthBeats;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return false;
        clipboardAudio_ = t->clips[static_cast<size_t>(clipIndex)];
        clipboardKind_ = 0;
        start = clipboardAudio_.startBeat;
        len = audioDisplayLenBeats(clipboardAudio_, spb, devSR, rt_.tempoMap());
    }
    clipboardAuto_ = captureClipAutomation(*t, start, start + len);
    clipboardAutoTrack_ = trackId;
    return true;
}

bool ClipOps::cutClip(int32_t trackId, int32_t clipIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    auto nt = rt_.cloneTrack(*old);
    double start = 0.0, len = 0.0;
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        clipboardMidi_ = nt->midiClips[static_cast<size_t>(clipIndex)];
        clipboardKind_ = 1;
        start = clipboardMidi_.startBeat;
        len = clipboardMidi_.lengthBeats;
        auto& mc = nt->midiClips.write();
        mc.erase(mc.begin() + clipIndex);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        clipboardAudio_ = nt->clips[static_cast<size_t>(clipIndex)];
        clipboardKind_ = 0;
        start = clipboardAudio_.startBeat;
        len = audioDisplayLenBeats(clipboardAudio_, spb, devSR, rt_.tempoMap());
        auto& ac = nt->clips.write();
        ac.erase(ac.begin() + clipIndex);
    }
    clipboardAuto_ = captureClipAutomation(*nt, start, start + len);
    clipboardAutoTrack_ = trackId;
    removeAutomationInRange(*nt, start, start + len); // Cut carries the automation away
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t ClipOps::clipboardClipKind() const {
    return clipboardKind_;
}

int32_t ClipOps::pasteClip(int32_t sourceTrackId, double atBeat) {
    if (clipboardKind_ < 0) return -1;
    auto old = rt_.findTrackAuthoring(sourceTrackId);
    if (!old) return -1;
    const bool destInstrument = old->type() == TrackType::Instrument;
    if (clipboardKind_ == 1 && !destInstrument) return -1;                 // MIDI clip → instrument track
    if (clipboardKind_ == 0 && old->type() != TrackType::Audio) return -1; // audio clip → audio track
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    double start = std::max(0.0, atBeat);
    auto nt = rt_.cloneTrack(*old);
    const bool sameTrack = sourceTrackId == clipboardAutoTrack_;
    if (clipboardKind_ == 1) {
        MidiClip m = clipboardMidi_;
        m.startBeat = placeMidiNonOverlap(*nt, start, m.lengthBeats);
        nt->midiClips.write().push_back(m);
        applyClipAutomation(*nt, sourceTrackId, clipboardAuto_, m.startBeat, m.lengthBeats, sameTrack);
        rt_.republishWithTrack(sourceTrackId, nt);
        return static_cast<int32_t>(nt->midiClips.size()) - 1;
    }
    AudioClip a = clipboardAudio_;
    a.startBeat = placeAudioNonOverlap(*nt, a, start, spb, devSR, rt_.tempoMap());
    const double len = audioDisplayLenBeats(a, spb, devSR, rt_.tempoMap());
    if (a.warpEnabled) configureClipWarp(a, spb, devSR);
    nt->clips.write().push_back(a);
    applyClipAutomation(*nt, sourceTrackId, clipboardAuto_, a.startBeat, len, sameTrack);
    rt_.republishWithTrack(sourceTrackId, nt);
    return static_cast<int32_t>(nt->clips.size()) - 1;
}

// --- block clip clipboard (multi-selection copy/cut/paste + duplicate) ------

// Pull the selected clips (+ their automation) out into BlockClip entries with beats
// rebased to the block start. blockLen returns the block's total span (max end - min start).
std::vector<BlockClip> ClipOps::captureBlock(
    const std::vector<std::pair<int32_t, int32_t>>& sel, double& blockLen) const {
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    std::vector<BlockClip> items;
    double minS = std::numeric_limits<double>::max(), maxE = -std::numeric_limits<double>::max();
    for (const auto& [tid, ci] : sel) {
        auto t = rt_.findTrackAuthoring(tid);
        if (!t) continue;
        BlockClip b;
        b.trackId = tid;
        double s, e;
        if (t->type() == TrackType::Instrument) {
            if (ci < 0 || ci >= static_cast<int32_t>(t->midiClips.size())) continue;
            b.kind = 1;
            b.midi = t->midiClips[static_cast<size_t>(ci)];
            s = b.midi.startBeat;
            b.len = b.midi.lengthBeats;
        } else {
            if (ci < 0 || ci >= static_cast<int32_t>(t->clips.size())) continue;
            b.kind = 0;
            b.audio = t->clips[static_cast<size_t>(ci)];
            s = b.audio.startBeat;
            b.len = audioDisplayLenBeats(b.audio, spb, devSR, rt_.tempoMap());
        }
        e = s + b.len;
        b.relStart = s; // absolute for now; rebased below
        b.autom = captureClipAutomation(*t, s, e);
        minS = std::min(minS, s);
        maxE = std::max(maxE, e);
        items.push_back(std::move(b));
    }
    if (items.empty()) {
        blockLen = 0.0;
        return items;
    }
    blockLen = maxE - minS;
    for (auto& b : items) b.relStart -= minS; // rebase to the block start
    return items;
}

// Re-land a captured block at placedStart onto the same tracks, one shared non-overlap
// shift for the whole block (so relative geometry is preserved), publishing every touched
// track in a single undo step. Records the placed clips in lastPlaced_.
void ClipOps::placeBlock(const std::vector<BlockClip>& items, double placedStart, bool overwrite) {
    lastPlaced_.clear();
    if (items.empty() || !rt_.authoring_) return;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    placedStart = std::max(0.0, placedStart);

    // One clone per touched track (multiple clips on a track accumulate into it). Clones
    // start from the current authoring track so the shared shift sees existing clips.
    std::map<int32_t, std::shared_ptr<Track>> clones;
    for (const auto& b : items) {
        if (clones.count(b.trackId)) continue;
        auto old = rt_.findTrackAuthoring(b.trackId);
        if (old) clones[b.trackId] = rt_.cloneTrack(*old);
    }

    // Find the minimal shared shift d >= 0 so no clip in the block overlaps an existing
    // clip on its track. All clips move together by d, so relative positions never change.
    // In overwrite mode the block lands exactly at placedStart and carves what it covers
    // instead (all carves first, so they never cut a copy placed by an earlier item).
    double d = 0.0;
    bool moved = !overwrite;
    auto lengthAt = [&](const BlockClip& b, double start) {
        return b.kind == 1 ? b.len : audioDisplayLenBeats(b.audio, spb, devSR, rt_.tempoMap(), start);
    };
    if (overwrite) {
        for (const auto& b : items) {
            auto it = clones.find(b.trackId);
            if (it == clones.end()) continue;
            const double s = placedStart + b.relStart, e = s + lengthAt(b, s);
            if (b.kind == 1) midiOverwriteRange(it->second->midiClips.write(), s, e);
            else
                audioOverwriteRange(it->second->clips.write(), s, e, spb, devSR, rt_.tempoMap());
        }
    }
    while (moved) {
        moved = false;
        for (const auto& b : items) {
            auto it = clones.find(b.trackId);
            if (it == clones.end()) continue;
            const Track& t = *it->second;
            const double s = placedStart + b.relStart + d, e = s + lengthAt(b, s);
            if (b.kind == 1) {
                for (const auto& c : t.midiClips) {
                    const double cs = c.startBeat, ce = cs + c.lengthBeats;
                    if (s < ce && e > cs) {
                        const double need = ce - (placedStart + b.relStart);
                        if (need > d + 1e-9) {
                            d = need;
                            moved = true;
                        }
                    }
                }
            } else {
                for (const auto& c : t.clips) {
                    const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap());
                    if (s < ce && e > cs) {
                        const double need = ce - (placedStart + b.relStart);
                        if (need > d + 1e-9) {
                            d = need;
                            moved = true;
                        }
                    }
                }
            }
        }
    }

    // Clear every landing before inserting any copied automation. Mapped audio
    // spans can overlap even when their beat-relative point offsets do not.
    for (const auto& b : items) {
        auto it = clones.find(b.trackId);
        if (it == clones.end()) continue;
        const double start = placedStart + b.relStart + d;
        applyClipAutomation(
            *it->second, b.trackId, b.autom, start, lengthAt(b, start), b.sameTrack, ClipAutomationPass::Clear);
    }

    // Insert the shifted copies + their automation, remembering each new clip's index.
    for (const auto& b : items) {
        auto it = clones.find(b.trackId);
        if (it == clones.end()) continue;
        Track& nt = *it->second;
        const double start = placedStart + b.relStart + d;
        int32_t newIdx;
        if (b.kind == 1) {
            MidiClip m = b.midi;
            m.startBeat = start;
            nt.midiClips.write().push_back(m);
            newIdx = static_cast<int32_t>(nt.midiClips.size()) - 1;
        } else {
            AudioClip a = b.audio;
            a.startBeat = start;
            if (a.warpEnabled) configureClipWarp(a, spb, devSR); // reuses the copied cache when it is current
            nt.clips.write().push_back(a);
            newIdx = static_cast<int32_t>(nt.clips.size()) - 1;
        }
        applyClipAutomation(nt, b.trackId, b.autom, start, lengthAt(b, start), b.sameTrack, ClipAutomationPass::Insert);
        lastPlaced_.emplace_back(b.trackId, newIdx);
    }

    // Publish all touched tracks at once — a single undo checkpoint for the whole block.
    rt_.pushUndo();
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        auto it = clones.find(t->id());
        g->tracks.push_back(it != clones.end() ? it->second : t);
    }
    rt_.publishRaw(std::move(g));
}

bool ClipOps::copyClipBlock(const std::vector<std::pair<int32_t, int32_t>>& sel) {
    double len = 0.0;
    auto items = captureBlock(sel, len);
    if (items.empty()) return false;
    clipboardBlock_ = std::move(items);
    clipboardBlockLen_ = len;
    clipboardBlockOverwrite_ = false;
    clipboardBlockAnchor_ = -1;
    return true;
}

bool ClipOps::copyRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (end <= start + 1e-9 || trackIds.empty() || !rt_.authoring_) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    constexpr double eps = 1e-6;
    auto listed = [&](int32_t id) { return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    std::vector<BlockClip> items;
    int32_t anchor = -1; // the range's top row
    for (const auto& t : rt_.authoring_->tracks) {
        if (t->type() == TrackType::Return || !listed(t->id())) continue;
        if (anchor < 0) anchor = t->id();
        auto add = [&](BlockClip b, double s) {
            b.trackId = t->id();
            b.relStart = s - start;
            b.autom = captureClipAutomation(*t, s, s + b.len);
            items.push_back(std::move(b));
        };
        if (t->type() == TrackType::Instrument) {
            for (const auto& c : t->midiClips) {
                const double cs = c.startBeat, ce = cs + c.lengthBeats;
                if (ce <= start + eps || cs >= end - eps) continue;
                BlockClip b;
                b.kind = 1;
                b.midi = midiSlice(c, start, end);
                b.len = b.midi.lengthBeats;
                const double s = b.midi.startBeat;
                add(std::move(b), s);
            }
        } else if (t->type() == TrackType::Audio) {
            for (const auto& c : t->clips) {
                const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap());
                if (ce <= start + eps || cs >= end - eps) continue;
                BlockClip b;
                b.kind = 0;
                b.audio = audioSlice(c, start, end, spb, devSR, rt_.tempoMap());
                b.len = audioDisplayLenBeats(b.audio, spb, devSR, rt_.tempoMap());
                const double s = b.audio.startBeat;
                add(std::move(b), s);
            }
        }
    }
    if (items.empty()) return false;
    clipboardBlock_ = std::move(items);
    clipboardBlockLen_ = end - start;
    clipboardBlockOverwrite_ = true;
    clipboardBlockAnchor_ = anchor;
    return true;
}

bool ClipOps::cutRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (!copyRange(trackIds, start, end)) return false;
    deleteClipsInRange(trackIds, start, end, /*withAutomation=*/true);
    return true;
}

bool ClipOps::cutClipBlock(const std::vector<std::pair<int32_t, int32_t>>& sel) {
    if (!copyClipBlock(sel)) return false;
    // Remove the source clips (highest index first per track) + the automation in their
    // spans, publishing every touched track in one undo step.
    std::map<int32_t, std::shared_ptr<Track>> clones;
    std::map<int32_t, std::vector<int32_t>> byTrack;
    for (const auto& [tid, ci] : sel) byTrack[tid].push_back(ci);
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    for (auto& [tid, idxs] : byTrack) {
        auto old = rt_.findTrackAuthoring(tid);
        if (!old) continue;
        auto nt = rt_.cloneTrack(*old);
        std::sort(idxs.begin(), idxs.end(), std::greater<int32_t>());
        for (int32_t ci : idxs) {
            if (nt->type() == TrackType::Instrument) {
                if (ci < 0 || ci >= static_cast<int32_t>(nt->midiClips.size())) continue;
                const MidiClip m = nt->midiClips[static_cast<size_t>(ci)];
                removeAutomationInRange(*nt, m.startBeat, m.startBeat + m.lengthBeats);
                auto& mc = nt->midiClips.write();
                mc.erase(mc.begin() + ci);
            } else {
                if (ci < 0 || ci >= static_cast<int32_t>(nt->clips.size())) continue;
                const AudioClip a = nt->clips[static_cast<size_t>(ci)];
                removeAutomationInRange(
                    *nt, a.startBeat, a.startBeat + audioDisplayLenBeats(a, spb, devSR, rt_.tempoMap()));
                auto& ac = nt->clips.write();
                ac.erase(ac.begin() + ci);
            }
        }
        clones[tid] = nt;
    }
    if (clones.empty()) return true;
    rt_.pushUndo();
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        auto it = clones.find(t->id());
        g->tracks.push_back(it != clones.end() ? it->second : t);
    }
    rt_.publishRaw(std::move(g));
    return true;
}

int32_t ClipOps::pasteClipBlock(double atBeat, int32_t sourceTrackId) {
    if (clipboardBlock_.empty() || !rt_.authoring_) return 0;
    // sourceTrackId >= 0 remaps the whole block onto that track (see remapBlock).
    std::vector<BlockClip> items =
        sourceTrackId >= 0 ? remapBlock(clipboardBlock_, sourceTrackId, clipboardBlockAnchor_) : clipboardBlock_;
    if (items.empty()) return 0;
    placeBlock(items, atBeat, clipboardBlockOverwrite_);
    return static_cast<int32_t>(lastPlaced_.size());
}

// Remap a block by the track-index delta from its top track to the destination (so a single
// clip lands on that track; a multi-track block shifts together). Clips that fall off the
// track list or hit a type-mismatched track are skipped; a remapped clip's device/plugin
// automation is dropped (indices are positional).
std::vector<BlockClip> ClipOps::remapBlock(
    const std::vector<BlockClip>& items, int32_t destTrackId, int32_t anchorTrackId) const {
    auto idxOf = [&](int32_t tid) -> int {
        for (size_t i = 0; i < rt_.authoring_->tracks.size(); ++i)
            if (rt_.authoring_->tracks[i]->id() == tid) return static_cast<int>(i);
        return -1;
    };
    const int destIdx = idxOf(destTrackId);
    int topIdx = std::numeric_limits<int>::max();
    for (const auto& b : items) {
        int ix = idxOf(b.trackId);
        if (ix >= 0) topIdx = std::min(topIdx, ix);
    }
    if (anchorTrackId >= 0 && idxOf(anchorTrackId) >= 0) topIdx = idxOf(anchorTrackId);
    if (destIdx < 0 || topIdx == std::numeric_limits<int>::max()) return {};
    const int delta = destIdx - topIdx;
    std::vector<BlockClip> remapped;
    for (const auto& b : items) {
        const int ix = idxOf(b.trackId);
        if (ix < 0) continue;
        const int ti = ix + delta;
        if (ti < 0 || ti >= static_cast<int>(rt_.authoring_->tracks.size())) continue;
        const auto& tt = rt_.authoring_->tracks[static_cast<size_t>(ti)];
        const bool typeOk = (b.kind == 1) ? (tt->type() == TrackType::Instrument) : (tt->type() == TrackType::Audio);
        if (!typeOk) continue;
        BlockClip nb = b;
        nb.sameTrack = tt->id() == b.trackId;
        nb.trackId = tt->id();
        remapped.push_back(std::move(nb));
    }
    return remapped;
}

double ClipOps::duplicateClipBlock(const std::vector<std::pair<int32_t, int32_t>>& sel) {
    double len = 0.0;
    auto items = captureBlock(sel, len);
    if (items.empty()) return -1.0;
    // captureBlock rebases relStart to the block start; recover the block's absolute start
    // (the min clip start across the selection) so we can place the copy right after it.
    double absStart = std::numeric_limits<double>::max();
    for (const auto& [tid, ci] : sel) {
        auto t = rt_.findTrackAuthoring(tid);
        if (!t) continue;
        if (t->type() == TrackType::Instrument) {
            if (ci >= 0 && ci < static_cast<int32_t>(t->midiClips.size()))
                absStart = std::min(absStart, t->midiClips[static_cast<size_t>(ci)].startBeat);
        } else {
            if (ci >= 0 && ci < static_cast<int32_t>(t->clips.size()))
                absStart = std::min(absStart, t->clips[static_cast<size_t>(ci)].startBeat);
        }
    }
    if (absStart == std::numeric_limits<double>::max()) return -1.0;
    placeBlock(items, absStart + len, /*overwrite*/ true); // right after itself, like a drag
    return len;
}

} // namespace nota::detail
