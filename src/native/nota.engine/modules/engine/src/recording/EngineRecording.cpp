// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RecordingOps — arrangement/loop take startup, capture loop layout, input queue drain, and recording status.

#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "model/EngineTypes.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "recording/CaptureRing.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

bool RecordingOps::isRecording() const {
    return rt_.recording_.load(std::memory_order_relaxed) || rt_.audioRecording_;
}
int32_t RecordingOps::recordStartStatus() const {
    return rt_.recordStartStatus_;
}
bool RecordingOps::isAudioRecording() const {
    return rt_.audioRecording_;
}
int32_t RecordingOps::audioRecordTrackId() const {
    return rt_.audioRecording_ ? audioRecordTrackId_ : 0;
}
double RecordingOps::audioRecordStartBeat() const {
    return rt_.audioRecordStartBeat_;
}
double RecordingOps::takeTransportBeats() const {
    return std::isnan(takeBeatOverride_) ? rt_.transport_.uiPositionBeats() : takeBeatOverride_;
}
bool RecordingOps::takeLoopBypassed() const {
    return std::isnan(takeBeatOverride_) && rt_.transport_.uiPastLoopEnd();
}

// and retryCountIn counts in again right away (the stop it queued hasn't landed yet).
bool RecordingOps::startArrangementTake(bool wasPlaying, double playhead, bool retryCountIn) {
    // Record every armed track together: every armed audio track captures audio — each its
    // own hardware input, in one take (specs/086), or the first one another track's output —
    // and the first armed instrument track captures MIDI. Both run at once, so arming an
    // instrument + audio tracks and hitting REC records them all.
    std::vector<int32_t> audioIds;
    std::vector<std::shared_ptr<Track>> instTs; // every armed instrument track records MIDI (specs/086)
    std::vector<int32_t> instSkipped;
    for (auto& t : rt_.authoring_->tracks) {
        if (!t->armed()) continue;
        if (t->type() == TrackType::Audio) audioIds.push_back(t->id());
        else if (t->type() == TrackType::Instrument) {
            if (static_cast<int32_t>(instTs.size()) < kMaxMidiTakeLanes) instTs.push_back(t);
            else
                instSkipped.push_back(t->id());
        }
    }
    const bool haveAudio = !audioIds.empty();
    const bool haveMidi = !instTs.empty();
    // Nothing armed but the Session is playing (or about to): record the performance alone
    // (#971, clarification Q2). With the Session silent, the caller auto-arms as before.
    const bool performanceOnly = !haveAudio && !haveMidi && rt_.session().sessionPlayingOrQueued();
    if (!haveAudio && !haveMidi && !performanceOnly) {
        rt_.recording_.store(false, std::memory_order_relaxed);
        rt_.recordStartStatus_ = 1;
        return false;
    }
    // Every Arrangement take also records the Session performance (#971). Before anything is
    // written, so the pass's history starts here.
    rt_.session().beginSessionPass(performanceOnly);

    // A take started from stop gets the count-in (if one is set); the capture taps stay
    // gated until it runs out, so the clicks-only pre-roll isn't recorded.
    rt_.recordCountIn_ = !wasPlaying && !rt_.recordSessionAudio_ && rt_.countInBars() > 0;
    // Remember what the take may write before it writes anything (the MIDI clip below).
    openTakeAttempt(false);
    rt_.takeAttempt_.countedIn = rt_.recordCountIn_;
    for (int32_t id : audioIds) noteTakeTrack(id);
    for (auto& t : instTs) noteTakeTrack(t->id());
    takeStartOverride_ = playhead;
    takePlayingOverride_ = wasPlaying ? 1 : 0;
    bool audioOk = true;
    recordSkipped_.clear();
    if (haveAudio) audioOk = startAudioRecordingLanes(audioIds);
    if (!haveMidi) publishMidiLanes({}); // no MIDI lanes left over from an earlier take
    takeStartOverride_ = std::numeric_limits<double>::quiet_NaN();
    takePlayingOverride_ = -1;

    for (int32_t id : instSkipped) recordSkipped_.emplace_back(id, kSkipTooMany);
    if (haveMidi) {
        // Each track overdubs into a clip that already spans the playhead, else opens one
        // there; the new clips are one undo step (specs/086).
        std::vector<int32_t> ids, clips;
        rt_.beginUndoGroup();
        for (auto& instT : instTs) {
            int32_t idx = -1;
            for (int32_t i = 0; i < static_cast<int32_t>(instT->midiClips.size()); ++i) {
                const MidiClip& c = instT->midiClips[static_cast<size_t>(i)];
                if (playhead >= c.startBeat && playhead < c.startBeat + c.lengthBeats) {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) {
                auto nt = rt_.cloneTrack(*instT);
                MidiClip c;
                c.startBeat = playhead;
                c.lengthBeats = 4.0;
                nt->midiClips.write().push_back(c);
                idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
                EngineRuntime::TakePushScope takePush(rt_); // the take's own clip (specs/078)
                rt_.republishWithTrack(instT->id(), nt);
            }
            ids.push_back(instT->id());
            clips.push_back(idx);
        }
        rt_.endUndoGroup();
        publishMidiLanes(ids);
        for (size_t k = 0; k < clips.size(); ++k) rt_.midiLanes_[k].clipIndex = clips[k];
        recordTrackId_ = ids[0];
        recordClipIndex_ = clips[0];
        // A fresh take for loop record replace: nothing recorded, nothing replaced yet (the
        // audio thread also zeroes replaceUpTo_ when it sees the take start).
        takeNotes_.clear();
        appliedReplaceUpTo_ = 0;
        rt_.replaceUpTo_.store(0, std::memory_order_relaxed);
        rt_.recording_.store(true, std::memory_order_relaxed);
    }

    if (performanceOnly) rt_.recording_.store(true, std::memory_order_relaxed);
    // Unified transport/rewind bookkeeping (the sub-calls above may have set per-call
    // values; make them consistent for the combined take).
    if (!rt_.recordSessionAudio_) {
        recordReturnBeat_ = playhead;
        recordStartedTransport_ = !wasPlaying;
    }
    if (!wasPlaying) { // roll so the take advances (idempotent if already rolling)
        if (rt_.recordCountIn_ && retryCountIn) {
            rt_.arrangementActive_.store(true, std::memory_order_relaxed);
            rt_.startCountIn(rt_.countInBars());
        } else if (rt_.recordCountIn_)
            rt_.transportPlayCountIn();
        else
            rt_.transportPlay();
    }
    rt_.recordCountIn_ = false;
    rt_.recordStartStatus_ = (haveAudio && !audioOk && !haveMidi) ? 2 : performanceOnly ? 3 : 0;
    if (!isRecording()) { // nothing started: nothing to retry, no performance to write
        rt_.session().abandonSessionPass();
        closeTakeAttempt();
    }
    return isRecording();
}

double RecordingOps::takePlayhead() const {
    return std::isnan(takeStartOverride_) ? rt_.transport_.uiPositionBeats() : takeStartOverride_;
}

bool RecordingOps::takeTransportPlaying() const {
    return takePlayingOverride_ < 0 ? rt_.transport_.uiIsPlaying() : takePlayingOverride_ != 0;
}

// Decide the capture layout at take start: loop punch if the transport loop is on
// (record start point mapped into the loop), else a linearly growing buffer.
void RecordingOps::beginCaptureLayout() {
    captureTempoMap_ = rt_.authoring_ ? rt_.authoring_->tempoMap : nullptr;
    captureLoop_ = false;
    captureWrapped_ = false;
    captureWritePos_ = 0;
    captureOrigin_ = 0;
    capturePassFrom_ = 0;
    for (auto& l : rt_.captureLanes_) {
        l.buf.clear();
        l.passes.clear();
    }
    captureLinearUnderLoop_ = false;
    if (!rt_.recordSessionAudio_ && rt_.loopWrapsFrom(rt_.audioRecordStartBeat_))
        activateLoopPunch(rt_.audioRecordStartBeat_);
}

// Switch capture into loop punch: a fixed loop-region buffer per lane whose shared cursor
// starts at the loop-local position of atBeat and wraps at the loop length. Called at record
// start (loop already on) or the first drain after loop is enabled mid-take (discarding the
// linear pre-roll — the point of enabling the loop is to (re)record the loop region).
void RecordingOps::activateLoopPunch(double atBeat) {
    captureTempoMap_ = rt_.authoring_ ? rt_.authoring_->tempoMap : nullptr;
    const double devSR = rt_.transport_.sampleRate();
    const double fpb = rt_.transport_.samplesPerBeat() * (devSR > 0.0 ? audioRecordSampleRate_ / devSR : 1.0);
    const double loopLen = rt_.loopEnd_ - rt_.loopStart_;
    if (loopLen <= 0.0 || fpb <= 0.0) return;
    captureLoopFrames_ = static_cast<size_t>(std::llround(
        captureTempoMap_ ? (captureTempoMap_->secondsAt(rt_.loopEnd_) - captureTempoMap_->secondsAt(rt_.loopStart_)) *
                               audioRecordSampleRate_
                         : loopLen * fpb));
    if (captureLoopFrames_ == 0) return;
    double local = atBeat - rt_.loopStart_;
    local -= std::floor(local / loopLen) * loopLen; // wrap into [0, loopLen)
    if (local < 0.0) local = 0.0;
    captureLoopStartBeat_ = rt_.loopStart_;
    captureOrigin_ =
        static_cast<size_t>(std::llround(captureTempoMap_ ? (captureTempoMap_->secondsAt(rt_.loopStart_ + local) -
                                                                captureTempoMap_->secondsAt(rt_.loopStart_)) *
                                                                audioRecordSampleRate_
                                                          : local * fpb)) %
        captureLoopFrames_;
    captureWritePos_ = captureOrigin_;
    captureWrapped_ = false;
    for (auto& l : rt_.captureLanes_) {
        l.buf.assign(captureLoopFrames_ * static_cast<size_t>(l.channels), 0.0f); // fresh loop-region buffer, zeroed
        l.passes.clear();
    }
    captureLoop_ = true;
    capturePassFrom_ = captureOrigin_;
}

// The pass that started at capturePassFrom_ ends at `to`: keep a copy per lane as a future
// take (specs/067) before the next pass overwrites the loop buffer. Message thread (the drain).
void RecordingOps::finishLoopPass(size_t to) {
    if (to <= capturePassFrom_ || captureLoopFrames_ == 0) return;
    for (auto& l : rt_.captureLanes_) {
        const size_t ch = static_cast<size_t>(l.channels);
        LoopPass p;
        p.channels = l.channels;
        p.buf.assign(captureLoopFrames_ * ch, 0.0f);
        std::copy(l.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch),
            l.buf.begin() + static_cast<std::ptrdiff_t>(to * ch),
            p.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch));
        p.from = capturePassFrom_;
        p.to = to;
        l.passes.push_back(std::move(p));
    }
}

const CaptureLane* RecordingOps::captureLaneOf(int32_t trackId) const {
    for (const auto& l : rt_.captureLanes_)
        if (l.trackId == trackId) return &l;
    return nullptr;
}

double RecordingOps::audioRecordLengthBeats(int32_t trackId) const {
    if (!rt_.audioRecording_) return 0.0;
    const CaptureLane* l =
        trackId > 0 ? captureLaneOf(trackId) : (rt_.captureLanes_.empty() ? nullptr : &rt_.captureLanes_[0]);
    if (!l) return 0.0;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    const double sampleSR = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : devSR;
    if (spb <= 0.0 || sampleSR <= 0.0) return 0.0;
    const double frames = static_cast<double>(l->buf.size() / static_cast<size_t>(l->channels));
    if (captureTempoMap_ && l->scene < 0) {
        const double origin = captureLoop_ ? captureLoopStartBeat_ : rt_.audioRecordStartBeat_;
        return captureTempoMap_->beatAt(captureTempoMap_->secondsAt(origin) + frames / sampleSR) - origin;
    }
    return frames * devSR / sampleSR / spb;
}

void RecordingOps::drainInputQueue() {
    if (rt_.audioRecording_ && !rt_.recordSessionAudio_) {
        const double at = takeTransportBeats();
        // (A seek past the loop end during a loop take is handled where the frames are
        // written below: it splits this drain's frames at the seek, #981.)
        if (!captureLoop_ && rt_.loopEnabled_ && !rt_.loopWrapsFrom(at)) captureLinearUnderLoop_ = true;
        // Loop enabled after recording began → switch to loop punch from here, dropping the
        // straight pre-roll. A straight take that played on past the loop and is seeked back
        // into it keeps what it recorded instead: that lands first (#981).
        if (!captureLoop_ && rt_.loopWrapsFrom(at)) {
            if (captureLinearUnderLoop_ && captureWritePos_ > 0) landCaptureSoFar();
            captureLinearUnderLoop_ = false;
            activateLoopPunch(at);
        }
    }

    for (auto& l : rt_.captureLanes_) l.scratch.clear();
    auto clearScratch = [&] {
        for (auto& l : rt_.captureLanes_) l.scratch.clear();
    };
    // Hardware takes: blocks of layout columns, scattered into every lane — its pick is one
    // column (stored mono) or two (specs/086). A block of a layout that no longer carries a
    // lane's pick — or one too old to look up — keeps its length there as silence.
    CaptureBlock b;
    while (rt_.recordRing_.next(b)) {
        if (b.flags & CaptureBlock::kMarker) { // the producer's first frames of a new capture epoch follow
            if (awaitCaptureMarker_) clearScratch();
            awaitCaptureMarker_ = false;
            continue;
        }
        if (awaitCaptureMarker_) { // written for the take a retry threw away (specs/078)
            rt_.recordRing_.skip(b.frames);
            continue;
        }
        const CaptureLayoutSnapshot* L = layoutFor(b.generation);
        const size_t w = static_cast<size_t>(std::max(b.width, 1));
        captureBlock_.resize(static_cast<size_t>(b.frames) * w);
        const int32_t got = rt_.recordRing_.read(captureBlock_.data(), b.frames);
        for (auto& l : rt_.captureLanes_) {
            const int32_t c0 = L ? L->column(l.first) : -1;
            const int32_t c1 = l.width == 2 ? (L ? L->column(l.first + 1) : -1) : -1;
            const bool has0 = c0 >= 0 && c0 < b.width, has1 = c1 >= 0 && c1 < b.width;
            for (int32_t i = 0; i < got; ++i) {
                const float* row = captureBlock_.data() + static_cast<size_t>(i) * w;
                l.scratch.push_back(has0 ? row[c0] : 0.0f);
                if (l.channels == 2) l.scratch.push_back(has1 ? row[c1] : 0.0f);
            }
        }
    }
    // Internal resampling takes (one lane, stereo): frames from the audio thread.
    InputFrame f;
    while (rt_.internalQueue_.pop(f)) {
        if (isCaptureMarker(f)) {
            if (awaitCaptureMarker_) clearScratch();
            awaitCaptureMarker_ = false;
            continue;
        }
        if (awaitCaptureMarker_ || rt_.captureLanes_.empty()) continue;
        rt_.captureLanes_[0].scratch.push_back(f.l);
        rt_.captureLanes_[0].scratch.push_back(f.r);
    }
    if (awaitCaptureMarker_) rt_.inputDroppedFrames_.store(0, std::memory_order_relaxed);
    if (rt_.captureLanes_.empty()) return;
    // Frames the producer had to drop while this drain was stalled (ring full).
    // They are the newest samples — the ones that couldn't fit after the popped batch —
    // so append an equal run of silence *after* the drained frames. That keeps the take's
    // frame count equal to the elapsed transport time: a dropout becomes a bounded local
    // gap instead of shifting (and progressively desyncing) everything recorded after it.
    if (int64_t dropped = rt_.inputDroppedFrames_.exchange(0, std::memory_order_relaxed); dropped > 0)
        for (auto& l : rt_.captureLanes_)
            l.scratch.insert(l.scratch.end(), static_cast<size_t>(dropped) * static_cast<size_t>(l.channels), 0.0f);
    size_t frames = rt_.captureLanes_[0].scratch.size() / static_cast<size_t>(rt_.captureLanes_[0].channels);
    if (frames == 0) return;

    if (captureLoop_ && captureLoopFrames_ > 0) {
        // A seek past the loop end during a loop take (#981): the transport plays on from the
        // seek, so the take must too. This drain's frames were captured up to now; the ones
        // after the seek (the time the transport has played on since) start the straight take
        // at the seek, the rest still belong to the loop — so nothing lands later than it sounded.
        size_t toLoop = frames;
        double from = 0.0, now = 0.0, fpb = 0.0;
        const bool split = takeLoopBypassed();
        if (split) {
            from = rt_.transport_.uiPastLoopFromBeats();
            now = takeTransportBeats();
            const double devSR = rt_.transport_.sampleRate();
            fpb = rt_.transport_.samplesPerBeat() * (devSR > 0.0 ? audioRecordSampleRate_ / devSR : 1.0);
            const double after = std::max(0.0, (now - from) * fpb);
            toLoop = frames - std::min(frames, static_cast<size_t>(std::llround(after)));
        }
        // Loop punch: write into the loop-region buffers, the shared cursor wrapping at the
        // loop length so each pass overwrites the previous in place (frame-count clock → clean seams).
        for (size_t i = 0; i < toLoop; ++i) {
            for (auto& l : rt_.captureLanes_) {
                const size_t ch = static_cast<size_t>(l.channels);
                for (size_t c = 0; c < ch; ++c) l.buf[captureWritePos_ * ch + c] = l.scratch[i * ch + c];
            }
            if (++captureWritePos_ >= captureLoopFrames_) {
                finishLoopPass(captureLoopFrames_);
                captureWritePos_ = 0;
                captureWrapped_ = true;
                capturePassFrom_ = 0;
            }
        }
        if (!split) return;
        landCaptureSoFar(); // the loop's passes land; every lane now records straight
        // The straight take starts where its first frame sounded: the seek, or later if the
        // queue holds less than the time played on since.
        rt_.audioRecordStartBeat_ = fpb > 0.0 ? std::max(from, now - static_cast<double>(frames - toLoop) / fpb) : from;
        captureLinearUnderLoop_ = true;
        for (auto& l : rt_.captureLanes_) {
            const size_t ch = static_cast<size_t>(l.channels);
            l.scratch.erase(l.scratch.begin(),
                l.scratch.begin() + static_cast<std::ptrdiff_t>(std::min(l.scratch.size(), toLoop * ch)));
        }
        frames -= toLoop;
        if (frames == 0) return;
    }

    // Linear append: every lane grows by exactly what was captured, reserved in 30 s steps so
    // a long multi-track take doesn't reallocate on every drain.
    const size_t base = captureWritePos_;
    const size_t chunk = static_cast<size_t>(std::max(audioRecordSampleRate_, 8000.0) * 30.0);
    for (auto& l : rt_.captureLanes_) {
        const size_t ch = static_cast<size_t>(l.channels);
        const size_t at = base > l.offset ? base - l.offset : 0; // a lane that joined later starts at its own 0
        const size_t need = (at + frames) * ch;
        if (l.buf.capacity() < need) l.buf.reserve(need + chunk * ch);
        if (l.buf.size() < need) l.buf.resize(need, 0.0f);
        std::copy_n(l.scratch.data(), std::min(l.scratch.size(), frames * ch), l.buf.data() + at * ch);
    }
    captureWritePos_ = base + frames;
    // A retried Session audio take (specs/078) keeps what follows its launch boundary: note the
    // capture frame where it was crossed now, at the tempo it was crossed at.
    if (rt_.recordSessionAudio_ && !std::isnan(rt_.retryStartBeat_) && retryKeepFrom_ < 0) {
        const double pos = rt_.transport_.uiPositionBeats();
        if (pos >= rt_.retryStartBeat_) {
            const double devSR = rt_.transport_.sampleRate();
            const double fpb = rt_.transport_.samplesPerBeat() * (devSR > 0.0 ? audioRecordSampleRate_ / devSR : 1.0);
            const int64_t past = static_cast<int64_t>(std::llround((pos - rt_.retryStartBeat_) * fpb));
            retryKeepFrom_ = std::max<int64_t>(0, static_cast<int64_t>(captureWritePos_) - past);
        }
    }
}

bool RecordingOps::startAudioRecording(int32_t trackId) {
    return startAudioRecordingLanes(std::vector<int32_t>{trackId});
}

// One audio take on every listed track (specs/086). The first track decides the kind: an
// internal source (another track, a return, the master) records that one track as before; a
// hardware input records every listed track on the hardware input, each its own pick, sharing
// the take's start, clock and latency correction. Tracks that can't join are reported in
// recordSkipped_.
bool RecordingOps::startAudioRecordingLanes(const std::vector<int32_t>& trackIds) {
    recordSkipped_.clear();
    std::vector<std::shared_ptr<Track>> tracks;
    for (int32_t id : trackIds)
        if (auto t = rt_.findTrackAuthoring(id); t && t->type() == TrackType::Audio) tracks.push_back(std::move(t));
    if (tracks.empty()) return false;
    rt_.captureLanes_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;
    rt_.inputDroppedFrames_.store(0, std::memory_order_relaxed); // fresh drop accounting per take
    awaitCaptureMarker_ = retryingTake_;                         // a retried take keeps nothing from before its marker
    retryKeepFrom_ = -1;
    rt_.audioRecordStartBeat_ = takePlayhead();

    // Internal resampling: capture another track / send / master output instead of the
    // hardware input. The audio thread feeds the ring from mixGraph; no device is opened.
    const int32_t src = tracks[0]->recordInputSource();
    if (src != 0) {
        for (size_t k = 1; k < tracks.size(); ++k) recordSkipped_.emplace_back(tracks[k]->id(), kSkipMixedSources);
        CaptureLane lane;
        lane.trackId = tracks[0]->id();
        lane.scene = rt_.recordSessionAudio_ ? rt_.recordSessionScene_ : -1;
        lane.lenBeats = rt_.recordSessionAudio_ ? rt_.recordSessionLen_ : 0.0; // #503
        lane.ownLen = rt_.recordSessionAudio_ && rt_.recordSessionOwnLen_;
        rt_.captureLanes_.push_back(std::move(lane));
        audioRecordTrackId_ = rt_.captureLanes_[0].trackId;
        audioRecordLatencyFrames_ = 0; // internal resampling has no device round trip (specs/049, R7)
        rt_.internalRecordSource_.store(src, std::memory_order_relaxed);
        audioRecordSampleRate_ = rt_.transport_.sampleRate();
        beginCaptureLayout();
        rt_.audioRecording_ = true;
        if (!rt_.recordSessionAudio_) {
            recordReturnBeat_ = rt_.audioRecordStartBeat_;
            recordStartedTransport_ = !takeTransportPlaying();
        }
        if (!takeTransportPlaying() && !rt_.recordCountIn_) rt_.transportPlay();
        return true;
    }

    // The device may already be open for monitoring: flush anything a previous take's
    // tap left behind, then start feeding the capture ring.
    if (!ensureAudioInput()) {
        audioRecordTrackId_ = 0;
        return false;
    }
    const int32_t inputs = inputChannelCount();
    for (auto& t : tracks) {
        if (t->recordInputSource() != 0) {
            recordSkipped_.emplace_back(t->id(), kSkipMixedSources);
            continue;
        }
        if (inputs > 0 && t->inputFirst() + t->inputWidth() > inputs) { // the device lacks its input
            recordSkipped_.emplace_back(t->id(), kSkipInputUnavailable);
            continue;
        }
        bool off = false; // switched off on the Audio I/O page
        for (int32_t c = t->inputFirst(); c < t->inputFirst() + t->inputWidth() && c < kMaxInputChannels; ++c)
            off = off || inputDisabled_[static_cast<size_t>(c)];
        if (off) {
            recordSkipped_.emplace_back(t->id(), kSkipSwitchedOff);
            continue;
        }
        CaptureLane lane;
        lane.trackId = t->id();
        lane.first = t->inputFirst();
        lane.width = t->inputWidth();
        lane.channels = lane.width; // a mono pick is stored mono
        lane.scene = rt_.recordSessionAudio_ ? rt_.recordSessionScene_ : -1;
        lane.lenBeats = rt_.recordSessionAudio_ ? rt_.recordSessionLen_ : 0.0; // #503
        lane.ownLen = rt_.recordSessionAudio_ && rt_.recordSessionOwnLen_;
        rt_.captureLanes_.push_back(std::move(lane));
    }
    if (rt_.captureLanes_.empty()) {
        audioRecordTrackId_ = 0;
        return false;
    }
    audioRecordTrackId_ = rt_.captureLanes_[0].trackId;
    // A hardware take is late by the device's round trip (in + out); 0 on backends that don't
    // report it (specs/049, research R7). Every lane gets the same correction (specs/086).
    audioRecordLatencyFrames_ = rt_.io().audioInputLatencyFrames() + rt_.io().audioOutputLatencyFrames();
    rt_.recordRing_.clear();
    {
        InputFrame stale;
        while (rt_.internalQueue_.pop(stale)) {}
    }
    rt_.audioRecording_ = true; // the layout below carries this take's picks
    updateCaptureLayout();
    rt_.audioRecording_ = false;
    rt_.hwRecordTap_.store(true, std::memory_order_relaxed);
    audioRecordSampleRate_ =
        rt_.input_ && rt_.input_->sampleRate() > 0 ? rt_.input_->sampleRate() : rt_.transport_.sampleRate();
    beginCaptureLayout();
    rt_.audioRecording_ = true;
    // Arrangement takes rewind to the take start on stop; session takes manage
    // their own transport via launchSlot, so don't arm the rewind for them.
    if (!rt_.recordSessionAudio_) {
        recordReturnBeat_ = rt_.audioRecordStartBeat_;
        recordStartedTransport_ = !takeTransportPlaying();
    }
    if (!takeTransportPlaying() && !rt_.recordCountIn_) rt_.transportPlay(); // else setRecording counts in
    return true;
}

// Stop capturing and throw the capture away (a discarded take, specs/078): the taps go quiet,

} // namespace nota::detail
