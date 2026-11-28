// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ClipOps — arrangement overwrite/carve bridges, paste, move, trim, resize, source region, split, and single-clip duplicate.

#include "clips/ClipHelpers.h"
#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "session/SessionOps.h"
#include "core/Transport.h"
#include "model/Graph.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/engine/Types.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <map>
#include <memory>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

using namespace nota::detail::clip_helpers;

void ClipOps::overwriteAudioClipsInRange(std::vector<AudioClip>& clips, double ns, double ne, double spb) {
    // The caller measured ns/ne with `spb`. Comp and a loop take use the authoring tempo;
    // a recording that just stopped uses the same clock as the clip it is about to place.
    audioOverwriteRange(clips, ns, ne, spb, rt_.transport_.sampleRate(), rt_.tempoMap());
}

void ClipOps::overwriteMidiClipsInRange(std::vector<MidiClip>& clips, double ns, double ne) {
    midiOverwriteRange(clips, ns, ne);
}

AudioClip ClipOps::sliceAudioClip(const AudioClip& c, double ns, double ne) const {
    return audioSlice(c, ns, ne, rt_.authoringSamplesPerBeat(), rt_.transport_.sampleRate(), rt_.tempoMap());
}

int32_t ClipOps::pasteAudioFrames(
    int32_t trackId, const float* interleaved, int64_t frames, double startBeat, const std::string& name) {
    if (!interleaved || frames <= 0) return -1;
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return -1;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return -1;
    startBeat = std::max(0.0, startBeat);

    auto sb = std::make_shared<SampleBuffer>();
    sb->channels = 2;
    sb->frames = frames;
    sb->sourceSampleRate = devSR; // the capture ran at the device rate
    sb->samples.assign(interleaved, interleaved + static_cast<size_t>(frames) * 2);

    auto nt = rt_.cloneTrack(*old);
    const double endBeat = rt_.tempoMap()
                               ? rt_.secondsToBeat(rt_.beatToSeconds(startBeat) + static_cast<double>(frames) / devSR)
                               : startBeat + static_cast<double>(frames) / spb;
    audioOverwriteRange(nt->clips.write(), startBeat, endBeat, spb, devSR, rt_.tempoMap());
    AudioClip clip;
    clip.sample = std::move(sb);
    clip.startBeat = startBeat;
    clip.lengthFrames = frames;
    clip.name = name;
    nt->clips.write().push_back(std::move(clip));
    const int32_t idx = static_cast<int32_t>(nt->clips.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    lastPlaced_ = {{trackId, idx}};
    return idx;
}

bool ClipOps::moveClip(int32_t trackId, int32_t clipIndex, double newStartBeat) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    lastMoveKeptDeviceAuto_ = false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    auto nt = rt_.cloneTrack(*old);
    // The dragged clip overwrites whatever it lands on: pull it out, carve its new range
    // from the remaining clips, then drop it back on top.
    double oldStart = 0, sourceLen = 0, len = 0; // separate lift and landing spans
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        auto& mc = nt->midiClips.write();
        MidiClip moved = mc[static_cast<size_t>(clipIndex)];
        oldStart = moved.startBeat;
        sourceLen = len = moved.lengthBeats;
        moved.startBeat = newStartBeat;
        mc.erase(mc.begin() + clipIndex);
        midiOverwriteRange(mc, newStartBeat, newStartBeat + moved.lengthBeats);
        mc.push_back(moved);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        auto& ac = nt->clips.write();
        AudioClip moved = ac[static_cast<size_t>(clipIndex)];
        oldStart = moved.startBeat;
        sourceLen = audioDisplayLenBeats(moved, spb, devSR, rt_.tempoMap());
        moved.startBeat = newStartBeat;
        ac.erase(ac.begin() + clipIndex);
        len = audioDisplayLenBeats(moved, spb, devSR, rt_.tempoMap());
        audioOverwriteRange(ac, newStartBeat, newStartBeat + len, spb, devSR, rt_.tempoMap());
        ac.push_back(moved);
    }
    // Automation follows the clip within its span (req 8.3.1), unless envelopes are locked.
    if (!automationLock_ && sourceLen > 1e-6 && std::abs(newStartBeat - oldStart) > 1e-9) {
        auto snips = captureClipAutomation(*nt, oldStart, oldStart + sourceLen);
        removeAutomationInRange(*nt, oldStart, oldStart + sourceLen);
        applyClipAutomation(*nt, trackId, snips, newStartBeat, len, /*sameTrack*/ true);
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool ClipOps::moveClipToTrack(int32_t srcTrackId, int32_t clipIndex, int32_t sourceTrackId, double newStartBeat) {
    if (srcTrackId == sourceTrackId) return moveClip(srcTrackId, clipIndex, newStartBeat);
    auto src = rt_.findTrackAuthoring(srcTrackId);
    auto dst = rt_.findTrackAuthoring(sourceTrackId);
    if (!src || !dst) return false;
    // Same-type only: instrument→instrument (MIDI) or audio→audio; never a return.
    if (src->type() != dst->type() || src->type() == TrackType::Return) return false;
    if (newStartBeat < 0) newStartBeat = 0;

    lastMoveKeptDeviceAuto_ = false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    auto nsrc = rt_.cloneTrack(*src);
    auto ndst = rt_.cloneTrack(*dst);
    double oldStart = 0, sourceLen = 0, len = 0; // separate lift and landing spans
    // The moved clip overwrites what it lands on at the destination.
    if (src->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nsrc->midiClips.size())) return false;
        auto& smc = nsrc->midiClips.write();
        MidiClip clip = smc[static_cast<size_t>(clipIndex)]; // copy before erasing
        oldStart = clip.startBeat;
        sourceLen = len = clip.lengthBeats;
        clip.startBeat = newStartBeat;
        smc.erase(smc.begin() + clipIndex);
        auto& dmc = ndst->midiClips.write();
        midiOverwriteRange(dmc, newStartBeat, newStartBeat + clip.lengthBeats);
        dmc.push_back(clip);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nsrc->clips.size())) return false;
        auto& sac = nsrc->clips.write();
        AudioClip clip = sac[static_cast<size_t>(clipIndex)]; // copy before erasing
        oldStart = clip.startBeat;
        sourceLen = audioDisplayLenBeats(clip, spb, devSR, rt_.tempoMap());
        clip.startBeat = newStartBeat;
        sac.erase(sac.begin() + clipIndex);
        len = audioDisplayLenBeats(clip, spb, devSR, rt_.tempoMap());
        auto& dac = ndst->clips.write();
        audioOverwriteRange(dac, newStartBeat, newStartBeat + len, spb, devSR, rt_.tempoMap());
        dac.push_back(clip);
    }

    // Automation follow across tracks (req 8.3.1/8.3.4). Identical device layout → everything
    // follows, exactly like a same-track move. Otherwise only the mix lanes (volume, pan, sends)
    // move and device/plugin automation stays on the source (its indices are positional); we
    // flag that so the UI can tell the user.
    if (!automationLock_ && sourceLen > 1e-6) {
        auto snips = captureClipAutomation(*nsrc, oldStart, oldStart + sourceLen);
        if (tracksDeviceLayoutMatch(*nsrc, *ndst)) {
            removeAutomationInRange(*nsrc, oldStart, oldStart + sourceLen);
            applyClipAutomation(*ndst, sourceTrackId, snips, newStartBeat, len, /*sameTrack*/ true);
        } else {
            for (const auto& s : snips)
                if (!isMixTarget(s.target)) {
                    lastMoveKeptDeviceAuto_ = true;
                    break;
                }
            // Clear only the mix lanes from the source span; leave device/plugin points in place.
            constexpr double eps = 1e-6;
            bool anyMix = false;
            for (const auto& lane : nsrc->automation)
                if (isMixTarget(lane.target) && !lane.points.empty()) {
                    anyMix = true;
                    break;
                }
            if (anyMix) {
                for (auto& lane : nsrc->automation.write()) {
                    if (!isMixTarget(lane.target)) continue;
                    auto& pts = lane.points;
                    pts.erase(std::remove_if(pts.begin(), pts.end(),
                                  [&](const AutomationPoint& p) {
                                      return p.beat >= oldStart - eps && p.beat <= oldStart + sourceLen + eps;
                                  }),
                        pts.end());
                }
            }
            applyClipAutomation(*ndst, sourceTrackId, snips, newStartBeat, len, /*sameTrack*/ false);
        }
    }

    // Atomic two-track swap in a single publish (one undo checkpoint).
    rt_.pushUndo();
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->id() == srcTrackId) g->tracks.push_back(nsrc);
        else if (t->id() == sourceTrackId)
            g->tracks.push_back(ndst);
        else
            g->tracks.push_back(t);
    }
    rt_.publishRaw(std::move(g));
    return true;
}

bool ClipOps::moveClipBlock(const std::vector<ClipMoveReq>& moves) {
    lastPlaced_.clear();
    lastMoveKeptDeviceAuto_ = false;
    if (moves.empty() || !rt_.authoring_) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();

    // Validate everything up front (all-or-nothing) and clone each touched track once.
    std::map<int32_t, std::shared_ptr<Track>> clones;
    std::set<std::pair<int32_t, int32_t>> seen;
    for (const auto& m : moves) {
        auto src = rt_.findTrackAuthoring(m.srcTrackId);
        auto dst = rt_.findTrackAuthoring(m.dstTrackId);
        if (!src || !dst || src->type() != dst->type() || src->type() == TrackType::Return) return false;
        if (m.srcTrackId == kMasterTrackId || m.dstTrackId == kMasterTrackId) return false;
        const size_t n = src->type() == TrackType::Instrument ? src->midiClips.size() : src->clips.size();
        if (m.clipIndex < 0 || m.clipIndex >= static_cast<int32_t>(n)) return false;
        if (!seen.emplace(m.srcTrackId, m.clipIndex).second) return false; // same clip twice
        if (!clones.count(m.srcTrackId)) clones[m.srcTrackId] = rt_.cloneTrack(*src);
        if (!clones.count(m.dstTrackId)) clones[m.dstTrackId] = rt_.cloneTrack(*dst);
    }

    // Snapshot each moving clip (+ the automation in its span) from the pre-move tracks.
    struct Item {
        const ClipMoveReq* req;
        bool midi;
        MidiClip mc;
        AudioClip ac;
        double oldStart, sourceLen, len, newStart;
        bool sameTrack, layoutMatch;
        std::vector<TrackLane> snips;
    };
    std::vector<Item> items;
    items.reserve(moves.size());
    for (const auto& m : moves) {
        const Track& src = *clones[m.srcTrackId];
        Item it{&m, src.type() == TrackType::Instrument, {}, {}, 0, 0, 0, std::max(0.0, m.newStartBeat),
            m.srcTrackId == m.dstTrackId, true, {}};
        if (it.midi) {
            it.mc = src.midiClips[static_cast<size_t>(m.clipIndex)];
            it.oldStart = it.mc.startBeat;
            it.sourceLen = it.len = it.mc.lengthBeats;
            it.mc.startBeat = it.newStart;
        } else {
            it.ac = src.clips[static_cast<size_t>(m.clipIndex)];
            it.oldStart = it.ac.startBeat;
            it.sourceLen = audioDisplayLenBeats(it.ac, spb, devSR, rt_.tempoMap());
            it.len = audioDisplayLenBeats(it.ac, spb, devSR, rt_.tempoMap(), it.newStart);
            it.ac.startBeat = it.newStart;
        }
        it.layoutMatch = it.sameTrack || tracksDeviceLayoutMatch(src, *clones[m.dstTrackId]);
        const bool follow =
            !automationLock_ && it.sourceLen > 1e-6 && (!it.sameTrack || std::abs(it.newStart - it.oldStart) > 1e-9);
        if (follow) it.snips = captureClipAutomation(src, it.oldStart, it.oldStart + it.sourceLen);
        items.push_back(std::move(it));
    }

    // Rebuild each touched track's clip list: clips staying on the track keep their slot (new
    // start), clips leaving are dropped, and every stationary clip is carved by all incoming
    // ranges. Cross-track arrivals are appended afterwards.
    std::map<int32_t, std::vector<std::pair<double, double>>> incoming; // dst track → landing ranges
    for (const auto& it : items) incoming[it.req->dstTrackId].emplace_back(it.newStart, it.newStart + it.len);
    std::map<std::pair<int32_t, int32_t>, size_t> itemOf; // (src track, index) → item
    for (size_t i = 0; i < items.size(); ++i) itemOf[{items[i].req->srcTrackId, items[i].req->clipIndex}] = i;
    std::vector<int32_t> newIndex(items.size(), -1);

    for (auto& [tid, nt] : clones) {
        const auto& ranges = incoming[tid];
        auto rebuild = [&](auto& clips, auto carve) {
            std::remove_reference_t<decltype(clips)> out;
            out.reserve(clips.size() + 2 * ranges.size());
            for (int32_t ci = 0; ci < static_cast<int32_t>(clips.size()); ++ci) {
                auto f = itemOf.find({tid, ci});
                if (f != itemOf.end()) {
                    auto& it = items[f->second];
                    if (!it.sameTrack) continue; // leaves this track
                    newIndex[f->second] = static_cast<int32_t>(out.size());
                    if constexpr (std::is_same_v<std::decay_t<decltype(clips[0])>, MidiClip>) out.push_back(it.mc);
                    else
                        out.push_back(it.ac);
                    continue;
                }
                std::remove_reference_t<decltype(clips)> pieces{clips[static_cast<size_t>(ci)]};
                for (const auto& [s, e] : ranges) carve(pieces, s, e);
                for (auto& p : pieces) out.push_back(std::move(p));
            }
            clips.swap(out);
        };
        if (nt->type() == TrackType::Instrument)
            rebuild(nt->midiClips.write(),
                [](std::vector<MidiClip>& v, double s, double e) { midiOverwriteRange(v, s, e); });
        else
            rebuild(nt->clips.write(), [&](std::vector<AudioClip>& v, double s, double e) {
                audioOverwriteRange(v, s, e, spb, devSR, rt_.tempoMap());
            });
    }
    for (size_t i = 0; i < items.size(); ++i) {
        auto& it = items[i];
        if (it.sameTrack) continue;
        Track& dst = *clones[it.req->dstTrackId];
        if (it.midi) {
            dst.midiClips.write().push_back(it.mc);
            newIndex[i] = static_cast<int32_t>(dst.midiClips.size()) - 1;
        } else {
            dst.clips.write().push_back(it.ac);
            newIndex[i] = static_cast<int32_t>(dst.clips.size()) - 1;
        }
    }

    // Automation follows (req 8.3.1/8.3.4): lift every source span first, then re-land, so
    // one clip's landing never gets erased by another's lift. Cross-track moves between
    // differing device layouts carry only Volume/Pan; device/plugin points stay behind.
    constexpr double eps = 1e-6;
    for (auto& it : items) {
        if (it.snips.empty()) continue;
        Track& src = *clones[it.req->srcTrackId];
        if (it.layoutMatch) {
            removeAutomationInRange(src, it.oldStart, it.oldStart + it.sourceLen);
            continue;
        }
        for (const auto& s : it.snips)
            if (!isMixTarget(s.target)) {
                lastMoveKeptDeviceAuto_ = true;
                break;
            }
        bool anyMix = false;
        for (const auto& lane : src.automation)
            if (isMixTarget(lane.target) && !lane.points.empty()) {
                anyMix = true;
                break;
            }
        if (anyMix) {
            for (auto& lane : src.automation.write()) {
                if (!isMixTarget(lane.target)) continue;
                auto& pts = lane.points;
                pts.erase(std::remove_if(pts.begin(), pts.end(),
                              [&](const AutomationPoint& p) {
                                  return p.beat >= it.oldStart - eps && p.beat <= it.oldStart + it.sourceLen + eps;
                              }),
                    pts.end());
            }
        }
    }
    for (auto& it : items)
        if (!it.snips.empty())
            applyClipAutomation(*clones[it.req->dstTrackId], it.req->dstTrackId, it.snips, it.newStart, it.len,
                it.layoutMatch, ClipAutomationPass::Clear);
    for (auto& it : items)
        if (!it.snips.empty())
            applyClipAutomation(*clones[it.req->dstTrackId], it.req->dstTrackId, it.snips, it.newStart, it.len,
                it.layoutMatch, ClipAutomationPass::Insert);

    for (size_t i = 0; i < items.size(); ++i) lastPlaced_.emplace_back(items[i].req->dstTrackId, newIndex[i]);

    // Publish every touched track at once — a single undo checkpoint for the whole group.
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

bool ClipOps::trimClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    if (newLengthBeats < 0.25) newLengthBeats = 0.25;
    auto nt = rt_.cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        MidiClip& c = nt->midiClips.write()[static_cast<size_t>(clipIndex)];
        const double delta = newStartBeat - c.startBeat; // shift notes to stay put
        for (auto& n : c.notes) n.startBeat -= delta;
        c.shiftControllers(-delta); // and controllers (a point left of the new start still sets the value there)
        c.startBeat = newStartBeat;
        c.lengthBeats = newLengthBeats;
    } else {
        if (!audioClipRef(*old, clipIndex)) return false;
        AudioClip& c = (*audioClipWrite(*nt, clipIndex));
        const double spb = rt_.transport_.samplesPerBeat();
        const double devSR = rt_.transport_.sampleRate();
        const double delta = newStartBeat - c.startBeat;
        const double want = audioBeatsToSrcFrames(
            c, newLengthBeats, spb, devSR, clipIndex >= 0 ? rt_.tempoMap() : nullptr, newStartBeat);
        if (c.reversed) {
            // Mirrored: a reversed clip's audible head is the region's END, so the left
            // edge moves that end in and the length is taken backwards from it.
            const double headEnd = audioReverseHead(c, delta, spb, devSR, clipIndex >= 0 ? rt_.tempoMap() : nullptr);
            const double off = std::max(0.0, headEnd - want);
            c.startBeat = newStartBeat;
            c.sourceOffsetFrames = off;
            c.lengthFrames = static_cast<int64_t>(std::max(0.0, headEnd - off));
        } else {
            double newOffset = c.sourceOffsetFrames +
                               audioBeatsToSrcFrames(c, delta, spb, devSR, clipIndex >= 0 ? rt_.tempoMap() : nullptr);
            if (newOffset < 0) newOffset = 0;
            c.startBeat = newStartBeat;
            c.sourceOffsetFrames = newOffset;
            c.lengthFrames = static_cast<int64_t>(want);
        }
    }
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool ClipOps::resizeAudioClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    if (newLengthBeats < 0.25) newLengthBeats = 0.25;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));

    if (c.warpEnabled) {
        // Warped: TRIM the played window — do NOT stretch (stretch is the
        // markers / BPM chip). Markers, warpBeats and the stream are untouched; we only
        // move the [warpPlayStart, warpPlayEnd] window and the timeline start. Dragging
        // the left edge shifts playStart (and startBeat); the right edge moves playEnd.
        constexpr double kMinBeat = 0.05;
        const double d = newStartBeat - c.startBeat;
        double ps, pe;
        if (c.reversed) { // the window's END is the audible head, so the edges swap roles
            pe = std::clamp(c.warpPlayEndEff() - d, kMinBeat, c.warpBeats);
            ps = std::clamp(pe - newLengthBeats, 0.0, pe - kMinBeat);
        } else {
            ps = std::clamp(c.warpPlayStart + d, 0.0, std::max(0.0, c.warpBeats - kMinBeat));
            pe = std::clamp(ps + newLengthBeats, ps + kMinBeat, c.warpBeats);
        }
        c.startBeat = newStartBeat;
        c.warpPlayStart = ps;
        c.warpPlayEnd = pe;
        configureClipWarp(c, spb, devSR); // the played window is baked into the cache
        rt_.session().syncSessionAudioSlot(*nt, clipIndex);
        rt_.republishWithTrack(trackId, nt);
        return true;
    }

    // Unwarped: trim within the source; if the drag asks for more than the source
    // can provide, auto-enable warp and stretch the current content to fit (this is
    // what lets a short one-shot be dragged out to a whole bar).
    const double delta = newStartBeat - c.startBeat;
    const int64_t wantFrames = static_cast<int64_t>(
        audioBeatsToSrcFrames(c, newLengthBeats, spb, devSR, clipIndex >= 0 ? rt_.tempoMap() : nullptr, newStartBeat));
    // A reversed clip reads the region backwards, so the source a drag can reveal sits
    // BEFORE its audible head (the region's end) rather than after the offset: the head is
    // the anchor and the region grows leftwards from it.
    double newOffset;
    int64_t srcAvail;
    if (c.reversed) {
        const double head = audioReverseHead(c, delta, spb, devSR, clipIndex >= 0 ? rt_.tempoMap() : nullptr);
        srcAvail = static_cast<int64_t>(head);
        newOffset = head - static_cast<double>(std::min<int64_t>(wantFrames, srcAvail));
    } else {
        newOffset = c.sourceOffsetFrames +
                    audioBeatsToSrcFrames(c, delta, spb, devSR, clipIndex >= 0 ? rt_.tempoMap() : nullptr);
        if (newOffset < 0) newOffset = 0;
        srcAvail = c.sample ? (c.sample->frames - static_cast<int64_t>(newOffset)) : 0;
    }
    if (newOffset < 0) newOffset = 0;
    c.startBeat = newStartBeat;
    c.sourceOffsetFrames = newOffset;
    if (!c.sample || wantFrames <= srcAvail) {
        c.lengthFrames = wantFrames; // plain trim (reveal/hide source)
    } else {
        // Stretch the current content (from the new offset to end of source) to the
        // requested musical length; two end markers = uniform stretch you can refine.
        c.lengthFrames = srcAvail;
        c.warpEnabled = true;
        c.warpBeats = newLengthBeats;
        c.warpMarkers = {{static_cast<double>(newOffset), 0.0},
            {static_cast<double>(newOffset) + static_cast<double>(c.effectiveLength()), newLengthBeats}};
        configureClipWarp(c, spb, devSR);
    }
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// Set the clip's source region directly (clip Start/End markers): which part
// of the sample plays. The timeline position (startBeat) is unchanged — only the read
// offset and length move. Unwarped audio clips only (warped regions are governed by
// warp markers). Frame-precise; clamped to the sample bounds with a small minimum.
bool ClipOps::setClipSourceRegion(int32_t trackId, int32_t clipIndex, double offsetFrames, int64_t lengthFrames) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    const AudioClip& oc = (*audioClipRef(*old, clipIndex));
    if (!oc.sample || oc.warpEnabled) return false; // warped: region is marker-driven
    const int64_t total = oc.sample->frames;
    if (total <= 0) return false;
    constexpr int64_t kMinFrames = 256;
    double off = offsetFrames;
    if (off < 0) off = 0;
    if (off > static_cast<double>(total - kMinFrames)) off = static_cast<double>(total - kMinFrames);
    if (off < 0) off = 0;
    int64_t len = lengthFrames;
    const int64_t avail = total - static_cast<int64_t>(off);
    len = std::clamp<int64_t>(len, std::min<int64_t>(kMinFrames, avail), avail);
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    c.sourceOffsetFrames = off;
    c.lengthFrames = len;
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t ClipOps::splitClip(int32_t trackId, int32_t clipIndex, double atBeat) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    auto nt = rt_.cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return -1;
        const MidiClip& src = old->midiClips[static_cast<size_t>(clipIndex)];
        const double local = atBeat - src.startBeat;
        if (local <= 0 || local >= src.lengthBeats) return -1;
        // The overwrite's carves, so the halves keep the clip's name, and its deactivation
        // and envelopes come along (#467).
        MidiClip left = midiCarveLeft(src, local);
        MidiClip right = midiCarveRight(src, local);
        if (midiCarriesState(src)) {
            carryMidiPiece(left, src, 0.0, local);
            carryMidiPiece(right, src, local, src.lengthBeats);
        }
        auto& mc = nt->midiClips.write();
        mc[static_cast<size_t>(clipIndex)] = std::move(left);
        mc.insert(mc.begin() + clipIndex + 1, std::move(right));
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return -1;
        const double spb = rt_.transport_.samplesPerBeat();
        const double devSR = rt_.transport_.sampleRate();
        // The old graph owns an immutable source throughout the split. Reading it
        // avoids copying its markers, envelopes and cache ownership a third time.
        const AudioClip& src = old->clips[static_cast<size_t>(clipIndex)];
        const double display = audioDisplayLenBeats(src, spb, devSR, rt_.tempoMap());
        const double local = atBeat - src.startBeat;
        if (local <= 0.0 || local >= display) return -1;
        // The same carves the range ops use: they cut in played time, so warped halves
        // split over the beat window (not lengthFrames, which warp ignores) and a reversed
        // clip's halves keep the audio each one sounded. Each warped half also gets its own
        // stretch cache, since the stretcher's seek state is per-clip.
        AudioClip left = audioCarveLeft(src, local, spb, devSR, rt_.tempoMap());
        AudioClip right = audioCarveRight(src, local, spb, devSR, rt_.tempoMap());
        if (audioCarriesState(src)) { // each half plays its own part of the envelopes (#467)
            carryAudioPiece(left, src, 0.0, local);
            carryAudioPiece(right, src, local, display);
        }
        if (splitFades_) {
            const double fade = splitFadeBeats();
            raiseSplitEdge(left, false, true, fade, local);
            raiseSplitEdge(right, true, false, fade, display - local);
        }
        auto& ac = nt->clips.write();
        ac[static_cast<size_t>(clipIndex)] = std::move(left);
        ac.insert(ac.begin() + clipIndex + 1, std::move(right));
    }
    rt_.republishWithTrack(trackId, nt);
    return clipIndex + 1;
}

int32_t ClipOps::duplicateClip(int32_t trackId, int32_t clipIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    auto nt = rt_.cloneTrack(*old);
    int32_t newIdx;
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return -1;
        MidiClip d = nt->midiClips[static_cast<size_t>(clipIndex)];
        const double srcStart = d.startBeat, len = d.lengthBeats;
        auto snips = captureClipAutomation(*nt, srcStart, srcStart + len);
        // Land right after the source, overwriting whatever sits there (like a drag).
        d.startBeat = srcStart + len;
        {
            auto& mc = nt->midiClips.write();
            midiOverwriteRange(mc, d.startBeat, d.startBeat + len);
            mc.push_back(d);
        }
        newIdx = static_cast<int32_t>(nt->midiClips.size()) - 1;
        applyClipAutomation(*nt, trackId, snips, d.startBeat, len, /*sameTrack*/ true);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return -1;
        AudioClip d = nt->clips[static_cast<size_t>(clipIndex)];
        const double len = audioDisplayLenBeats(d, spb, devSR, rt_.tempoMap());
        const double srcStart = d.startBeat;
        auto snips = captureClipAutomation(*nt, srcStart, srcStart + len);
        d.startBeat = srcStart + len;
        {
            auto& ac = nt->clips.write();
            audioOverwriteRange(ac, d.startBeat, d.startBeat + audioDisplayLenBeats(d, spb, devSR, rt_.tempoMap()), spb,
                devSR, rt_.tempoMap());
            if (d.warpEnabled) configureClipWarp(d, spb, devSR); // shares the source's cache when it's current
            ac.push_back(d);
        }
        newIdx = static_cast<int32_t>(nt->clips.size()) - 1;
        applyClipAutomation(*nt, trackId, snips, d.startBeat, len, /*sameTrack*/ true);
    }
    rt_.republishWithTrack(trackId, nt);
    return newIdx;
}

} // namespace nota::detail
