// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RecordingOps — capture abort/stop/finish, lane materialisation, loop takes, peaks, and recording self-tests.

#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
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

// whatever the input or mix thread already queued is drained and dropped with the rest.
void RecordingOps::abortAudioRecording() {
    if (!rt_.audioRecording_) return;
    rt_.audioRecording_ = false;
    rt_.internalRecordSource_.store(0, std::memory_order_relaxed);
    rt_.hwRecordTap_.store(false, std::memory_order_relaxed);
    releaseAudioInputIfIdle();
    drainInputQueue();
    rt_.captureLanes_.clear();
    updateCaptureLayout(); // the take's picks may no longer be needed
    captureWritePos_ = 0;
    captureLoop_ = false;
    captureWrapped_ = false;
    audioRecordTrackId_ = 0;
    rt_.retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
}

// Where the take's loop / linear capture stood when it stopped (shared by every lane).
struct TakeGeometry {
    bool loop = false, wrapped = false;
    size_t origin = 0, reached = 0, loopFrames = 0;
    double loopStartBeat = 0.0;
};

void RecordingOps::landCaptureSoFar() {
    TakeGeometry g;
    g.loop = captureLoop_;
    g.wrapped = captureWrapped_;
    g.origin = captureOrigin_;
    g.reached = captureWritePos_;
    g.loopFrames = captureLoopFrames_;
    g.loopStartBeat = captureLoopStartBeat_;
    if (g.loop) finishLoopPass(g.reached);
    rt_.beginUndoGroup();
    for (auto& l : rt_.captureLanes_) {
        CaptureLane landed;
        std::swap(landed, l);                  // the lane's frames and passes land; its set-up records on
        l.scratch = std::move(landed.scratch); // this drain's frames not yet written stay with it
        l.trackId = landed.trackId;
        l.first = landed.first;
        l.width = landed.width;
        l.channels = landed.channels;
        l.scene = landed.scene;
        l.lenBeats = landed.lenBeats;
        l.ownLen = landed.ownLen;
        l.fresh = landed.fresh;
        materialiseCaptureLane(landed, g);
    }
    rt_.endUndoGroup();
    captureLoop_ = false;
    captureWrapped_ = false;
    captureWritePos_ = 0;
    captureOrigin_ = 0;
    capturePassFrom_ = 0;
}

void RecordingOps::stopAudioRecording() {
    if (!rt_.audioRecording_) return;
    rt_.audioRecording_ = false;
    rt_.internalRecordSource_.store(0, std::memory_order_relaxed); // stop the mixGraph tap
    rt_.hwRecordTap_.store(false, std::memory_order_relaxed);      // stop the capture tap
    releaseAudioInputIfIdle();                                     // keep it open if monitoring
    drainInputQueue(); // pull whatever the input (or mix) thread pushed before it stopped

    TakeGeometry g;
    g.loop = captureLoop_;
    g.wrapped = captureWrapped_;
    g.origin = captureOrigin_;
    g.reached = captureWritePos_;
    g.loopFrames = captureLoopFrames_;
    g.loopStartBeat = captureLoopStartBeat_;
    if (g.loop) finishLoopPass(g.reached); // the open pass of every lane, up to where recording stopped

    std::vector<CaptureLane> lanes = std::move(rt_.captureLanes_);
    rt_.captureLanes_.clear();
    audioRecordTrackId_ = 0;
    captureWritePos_ = 0;
    captureLoop_ = false;
    updateCaptureLayout(); // the take's picks may no longer be needed

    // Every lane lands in one undo step (specs/086).
    rt_.beginUndoGroup();
    for (auto& l : lanes) materialiseCaptureLane(l, g);
    rt_.endUndoGroup();
    captureTempoMap_.reset();
    rt_.retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
    retryKeepFrom_ = -1;
}

// A track disarmed mid-take: drain, then land its lane alone (as a stop would) and drop it.
void RecordingOps::finishCaptureLane(int32_t trackId) {
    drainInputQueue();
    for (size_t k = 0; k < rt_.captureLanes_.size(); ++k) {
        if (rt_.captureLanes_[k].trackId != trackId) continue;
        CaptureLane lane = std::move(rt_.captureLanes_[k]);
        rt_.captureLanes_.erase(rt_.captureLanes_.begin() + static_cast<std::ptrdiff_t>(k));
        if (k == 0 && !rt_.captureLanes_.empty()) { // the take goes on carried by the next lane
            audioRecordTrackId_ = rt_.captureLanes_[0].trackId;
            if (rt_.recordSessionAudio_) {
                rt_.recordSessionTrackId_ = rt_.captureLanes_[0].trackId;
                rt_.recordSessionScene_ = rt_.captureLanes_[0].scene;
                rt_.takeAttempt_.sessionTrackId = rt_.recordSessionTrackId_;
                rt_.takeAttempt_.sessionScene = rt_.recordSessionScene_;
            }
        }
        const bool session = rt_.recordSessionAudio_;
        TakeGeometry g;
        g.loop = captureLoop_;
        g.wrapped = captureWrapped_;
        g.origin = captureOrigin_;
        g.reached = captureWritePos_;
        g.loopFrames = captureLoopFrames_;
        g.loopStartBeat = captureLoopStartBeat_;
        if (g.loop && captureWritePos_ > capturePassFrom_) { // its open pass, as finishLoopPass would
            const size_t ch = static_cast<size_t>(lane.channels);
            LoopPass p;
            p.channels = lane.channels;
            p.buf.assign(captureLoopFrames_ * ch, 0.0f);
            std::copy(lane.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch),
                lane.buf.begin() + static_cast<std::ptrdiff_t>(captureWritePos_ * ch),
                p.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch));
            p.from = capturePassFrom_;
            p.to = captureWritePos_;
            lane.passes.push_back(std::move(p));
        }
        materialiseCaptureLane(lane, g);
        rt_.recordSessionAudio_ = session; // the other lanes still record into their slots
        break;
    }
    updateCaptureLayout();
}

// A Session slot launched on another armed audio track while a Session audio take runs: it
// joins the take as a lane of its own from now (the take's clock and correction), so several
// slots record together and each stops on its own (specs/086).
bool RecordingOps::addSessionAudioLane(int32_t trackId, int32_t scene) {
    if (!rt_.audioRecording_ || !rt_.recordSessionAudio_ ||
        rt_.internalRecordSource_.load(std::memory_order_relaxed) != 0)
        return false;
    if (captureLaneOf(trackId)) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio || t->recordInputSource() != 0) return false;
    const int32_t inputs = inputChannelCount();
    if (inputs > 0 && t->inputFirst() + t->inputWidth() > inputs) return false;
    for (int32_t c = t->inputFirst(); c < t->inputFirst() + t->inputWidth() && c < kMaxInputChannels; ++c)
        if (inputDisabled_[static_cast<size_t>(c)]) return false;
    drainInputQueue(); // the cursor up to now: the lane starts here
    CaptureLane lane;
    lane.trackId = trackId;
    lane.first = t->inputFirst();
    lane.width = t->inputWidth();
    lane.channels = lane.width;
    lane.scene = scene;
    lane.offset = captureWritePos_;
    { // its own slot's length (#503), counted from now: it records from here
        const SessionSlot* sl = scene < static_cast<int32_t>(t->sessionSlots.size())
                                    ? &t->sessionSlots[static_cast<size_t>(scene)]
                                    : nullptr;
        lane.lenBeats = rt_.session().resolveTakeBeats(sl && !sl->hasClip ? sl : nullptr);
        lane.ownLen = rt_.session().ownTakeLength(sl);
        lane.fresh = !sl || !sl->hasClip; // its landing fires the slot's rule (#481)
        lane.startBeat = rt_.transport_.uiPositionBeats();
    }
    rt_.captureLanes_.push_back(std::move(lane));
    noteTakeTrack(trackId);
    if (rt_.takeAttempt_.active) rt_.takeAttempt_.sessionSlots.emplace_back(trackId, scene); // a retry relaunches it
    updateCaptureLayout(); // its input joins the capture
    return true;
}

// One lane of a stopped take into its track: loop passes as take lanes (specs/067), a Session
// slot (M5-4), or a clip where recording began (latency-corrected), carving what it lands on.
void RecordingOps::materialiseCaptureLane(CaptureLane& lane, const TakeGeometry& g) {
    const int32_t trackId = lane.trackId;
    const size_t ch = static_cast<size_t>(lane.channels);
    auto old = trackId > 0 ? rt_.findTrackAuthoring(trackId) : nullptr;
    if (!old || lane.buf.empty()) return;

    // Two or more passes keep every pass as a take (specs/067). A session take never loops here.
    if (g.loop && lane.passes.size() >= 2 && lane.scene < 0) {
        const double sr = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : rt_.transport_.sampleRate();
        auto nt = rt_.cloneTrack(*old);
        materialiseLoopTakes(*nt, lane.passes, g.loopFrames, g.loopStartBeat, sr);
        rt_.republishWithTrack(trackId, nt);
        return;
    }

    // A Session audio retry (specs/078) captured from the retry on; the take starts on the launch
    // quantum after it, so what came before that boundary goes.
    // The boundary frame was noted as the capture crossed it (drainInputQueue); stopped before
    // that, nothing of the retried take is kept.
    // A slot lane (lane.scene), not recordSessionAudio_: the first slot landed clears that flag,
    // and every slot of a multi-slot retry needs the same trim.
    if (lane.scene >= 0 && !std::isnan(rt_.retryStartBeat_)) {
        const size_t have = lane.buf.size() / ch;
        const size_t keep = retryKeepFrom_ < 0 ? have + lane.offset : static_cast<size_t>(retryKeepFrom_);
        const size_t drop = std::min(have, keep > lane.offset ? keep - lane.offset : 0); // in the lane's frames
        lane.buf.erase(lane.buf.begin(), lane.buf.begin() + static_cast<std::ptrdiff_t>(drop * ch));
    }
    if (lane.buf.empty()) { // stopped before the retried take's boundary: nothing to keep
        rt_.recordSessionAudio_ = false;
        return;
    }

    auto sample = std::make_shared<SampleBuffer>();
    sample->channels = lane.channels;
    sample->frames = static_cast<int64_t>(lane.buf.size() / ch);
    sample->sourceSampleRate = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : rt_.transport_.sampleRate();
    lane.buf.shrink_to_fit();
    sample->samples = std::move(lane.buf);

    auto nt = rt_.cloneTrack(*old);

    // Session-slot take (M5-4): store into the slot with a loop length derived
    // from the captured duration at the current tempo.
    if (lane.scene >= 0) {
        const int32_t scene = lane.scene;
        rt_.recordSessionAudio_ = false;
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        if (scene < static_cast<int32_t>(slots.size())) {
            SessionSlot& s = slots[static_cast<size_t>(scene)];
            s.hasClip = true;
            s.audio = AudioClip{};
            s.audio.sample = sample;
            const double sr = rt_.transport_.sampleRate();
            const double spb = rt_.transport_.samplesPerBeat();
            const double deviceFrames =
                static_cast<double>(sample->frames) * (sr > 0 ? sr / sample->sourceSampleRate : 1.0);
            s.lengthBeats = spb > 0 ? deviceFrames / spb : 4.0;
            if (s.lengthBeats <= 0.0) s.lengthBeats = 4.0;
            // Fixed-length capture keeps its length; an open-ended take is cut to whole quanta.
            // A take of the slot's own length stopped by hand before it is cut the same way (#503).
            if (lane.lenBeats <= 0.0) s.lengthBeats = rt_.session().roundedTakeBeats(s.lengthBeats);
            else if (lane.ownLen && !rt_.takeLengthReached_)
                s.lengthBeats = std::min(lane.lenBeats, rt_.session().roundedTakeBeats(s.lengthBeats));
            else
                s.lengthBeats = lane.lenBeats;
        }
        rt_.republishWithTrack(trackId, nt);
        if (lane.fresh) rt_.session().noteTakeLanded(trackId, scene); // a playable clip now: its rule may fire (#481)
        return;
    }

    // Place the take and carve whatever it lands on (overwrite/comp). Loop punch: a
    // completed pass fills the loop region [loopStart, loopEnd]; a partial pass is the
    // recorded sub-range. Linear: sits where recording began, spanning the captured length.
    // setBpm stores the new tempo before the transport applies it. Playback will measure
    // this clip in that tempo, so the beat range it occupies — and the carve — must too.
    const double spb = rt_.authoringSamplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    const double sampleSR = sample->sourceSampleRate > 0 ? sample->sourceSampleRate : devSR;
    const double beatsPerFrame = (spb > 0.0 && sampleSR > 0.0) ? devSR / sampleSR / spb : 0.0;
    auto beatAtFrame = [&](double origin, double frame) {
        return captureTempoMap_ ? captureTempoMap_->beatAt(captureTempoMap_->secondsAt(origin) + frame / sampleSR)
                                : origin + frame * beatsPerFrame;
    };

    AudioClip clip;
    clip.sample = sample;
    double ns = 0.0, ne = 0.0;
    if (g.loop && g.loopFrames > 0) {
        if (g.wrapped) { // at least one full pass → the whole loop
            clip.startBeat = g.loopStartBeat;
            ns = g.loopStartBeat;
            ne = beatAtFrame(g.loopStartBeat, static_cast<double>(g.loopFrames));
        } else if (g.reached > g.origin) { // partial pass → just what was recorded
            clip.sourceOffsetFrames = static_cast<double>(g.origin);
            clip.lengthFrames = static_cast<int64_t>(g.reached - g.origin);
            clip.startBeat = beatAtFrame(g.loopStartBeat, static_cast<double>(g.origin));
            ns = clip.startBeat;
            ne = beatAtFrame(g.loopStartBeat, static_cast<double>(g.reached));
        } else {
            return; // loop armed but nothing captured
        }
    } else {
        // Linear take: move it earlier by the device latency so it lines up with what was
        // playing when it was performed. (Loop and session takes keep their grid as before.)
        double trimDevice = 0.0;
        clip.startBeat =
            latencyCompensatedStart(rt_.audioRecordStartBeat_, audioRecordLatencyFrames_, spb, &trimDevice);
        if (captureTempoMap_) {
            const double seconds =
                captureTempoMap_->secondsAt(rt_.audioRecordStartBeat_) - audioRecordLatencyFrames_ / devSR;
            clip.startBeat = captureTempoMap_->beatAt(std::max(0.0, seconds));
            trimDevice = std::max(0.0, -seconds * devSR);
        }
        double kept = static_cast<double>(sample->frames);
        if (trimDevice > 0.0 && devSR > 0.0) {
            const double trim = std::min(kept, trimDevice * sampleSR / devSR);
            clip.sourceOffsetFrames = trim;
            kept -= trim;
            clip.lengthFrames = static_cast<int64_t>(kept);
        }
        ns = clip.startBeat;
        ne = beatAtFrame(clip.startBeat, kept);
    }
    if (ne <= ns) return;
    auto& clipsW = nt->clips.write();
    rt_.clips().overwriteAudioClipsInRange(clipsW, ns, ne, spb);
    clipsW.push_back(clip);
    rt_.republishWithTrack(trackId, nt);
}

// Every pass becomes a take (one new group, numbers continuing on the track), and the comp is
// what the in-place loop buffer would have held: each frame from the newest pass that recorded
// it. The loop region is carved as a wrapped loop take always was.
void RecordingOps::materialiseLoopTakes(
    Track& nt, std::vector<LoopPass>& passes, size_t loopFrames, double loopStartBeat, double sampleRate) {
    const double bpf = rt_.beatsPerSourceFrame(sampleRate);
    auto beatAtFrame = [&](double frame) {
        return captureTempoMap_
                   ? captureTempoMap_->beatAt(captureTempoMap_->secondsAt(loopStartBeat) + frame / sampleRate)
                   : loopStartBeat + frame * bpf;
    };
    const int32_t group = ++rt_.nextTakeGroup_;
    int32_t number = 0;
    for (const auto& k : nt.takes) number = std::max(number, k.number);

    std::vector<AudioTake> takes;
    takes.reserve(passes.size());
    for (auto& p : passes) {
        auto sb = std::make_shared<SampleBuffer>();
        sb->channels = p.channels;
        sb->frames = static_cast<int64_t>(loopFrames);
        sb->sourceSampleRate = sampleRate;
        sb->samples = std::move(p.buf);
        AudioTake k;
        k.sample = std::move(sb);
        k.id = ++rt_.nextTakeId_;
        k.number = ++number;
        k.group = group;
        k.originBeat = loopStartBeat;
        k.sourceOffsetFrames = static_cast<double>(p.from);
        k.lengthFrames = static_cast<int64_t>(p.to - p.from);
        k.beatsPerFrame = bpf;
        k.tempoMap = captureTempoMap_;
        takes.push_back(std::move(k));
    }

    auto& clipsW = nt.clips.write();
    rt_.clips().overwriteAudioClipsInRange(
        clipsW, loopStartBeat, beatAtFrame(static_cast<double>(loopFrames)), rt_.authoringSamplesPerBeat());
    std::vector<std::pair<size_t, size_t>> open{{0, loopFrames}}; // loop frames no newer pass covers yet
    for (size_t i = passes.size(); i-- > 0 && !open.empty();) {
        const size_t from = passes[i].from, to = passes[i].to;
        std::vector<std::pair<size_t, size_t>> rest;
        for (auto [a, b] : open) {
            const size_t s = std::max(a, from), e = std::min(b, to);
            if (s >= e) {
                rest.emplace_back(a, b);
                continue;
            }
            AudioClip clip;
            clip.sample = takes[i].sample;
            clip.startBeat = beatAtFrame(static_cast<double>(s));
            clip.sourceOffsetFrames = static_cast<double>(s);
            clip.lengthFrames = static_cast<int64_t>(e - s);
            clip.takeId = takes[i].id;
            clipsW.push_back(std::move(clip));
            if (a < s) rest.emplace_back(a, s);
            if (e < b) rest.emplace_back(e, b);
        }
        open = std::move(rest);
    }
    auto& takesW = nt.takes.write();
    for (auto& k : takes) takesW.push_back(std::move(k));
}

double RecordingOps::latencyCompensatedStart(
    double startBeat, int32_t latencyFrames, double samplesPerBeat, double* trimFrames) {
    if (trimFrames) *trimFrames = 0.0;
    if (latencyFrames <= 0 || samplesPerBeat <= 0.0) return startBeat;
    const double shift = latencyFrames / samplesPerBeat;
    if (startBeat >= shift) return startBeat - shift;
    if (trimFrames) *trimFrames = (shift - std::max(0.0, startBeat)) * samplesPerBeat;
    return 0.0;
}

int32_t RecordingOps::audioRecordPeaks(float* outMinMax, int32_t maxPoints, int32_t trackId) const {
    if (!outMinMax || maxPoints <= 0 || !rt_.audioRecording_) return 0;
    const CaptureLane* l =
        trackId > 0 ? captureLaneOf(trackId) : (rt_.captureLanes_.empty() ? nullptr : &rt_.captureLanes_[0]);
    if (!l) return 0;
    const size_t ch = static_cast<size_t>(l->channels);
    const int64_t total = static_cast<int64_t>(l->buf.size() / ch); // frames
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    // Live preview redraws ~30x/s over a growing buffer, so bound the scan: sample
    // at most ~64 frames per bucket. The materialised clip uses full-res peaks.
    const int64_t step = std::max<int64_t>(1, per / 64);
    for (int32_t b = 0; b < buckets; ++b) {
        const int64_t begin = b * per;
        const int64_t end = begin + per;
        float mn = 1.0f, mx = -1.0f;
        for (int64_t f = begin; f < end; f += step) {
            const float* fr = l->buf.data() + static_cast<size_t>(f) * ch;
            const float m = ch == 2 ? 0.5f * (fr[0] + fr[1]) : fr[0];
            mn = std::min(mn, m);
            mx = std::max(mx, m);
        }
        outMinMax[b * 2] = mn;
        outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

int32_t RecordingOps::audioRecordingTracks(int32_t* out, int32_t cap) const {
    if (!rt_.audioRecording_) return 0;
    const int32_t n = static_cast<int32_t>(rt_.captureLanes_.size());
    for (int32_t i = 0; out && i < n && i < cap; ++i) out[i] = rt_.captureLanes_[static_cast<size_t>(i)].trackId;
    return n;
}

int32_t RecordingOps::recordSkipped(int32_t* trackIds, int32_t* reasons, int32_t cap) const {
    const int32_t n = static_cast<int32_t>(recordSkipped_.size());
    for (int32_t i = 0; i < n && i < cap; ++i) {
        if (trackIds) trackIds[i] = recordSkipped_[static_cast<size_t>(i)].first;
        if (reasons) reasons[i] = recordSkipped_[static_cast<size_t>(i)].second;
    }
    return n;
}

void RecordingOps::pushInputFramesForTest(const float* interleavedStereo, int32_t frames) {
    pushInputFramesForTestN(interleavedStereo, frames, 2);
}

void RecordingOps::feedInputForTest(const float* interleaved, int32_t frames, int32_t channels) {
    constexpr int32_t kChunk = 1024; // well inside the ring, drained like poll() would
    for (int32_t at = 0; at < frames; at += kChunk) {
        pushInputFramesForTestN(interleaved + static_cast<size_t>(at) * static_cast<size_t>(channels),
            std::min(kChunk, frames - at), channels);
        if (rt_.audioRecording_) drainInputQueue();
    }
}

void RecordingOps::pushInputFramesForTestN(const float* interleaved, int32_t frames, int32_t channels) {
    if (testLayoutChannels_ < channels) {
        testLayoutChannels_ = std::min(channels, kMaxInputChannels);
        updateCaptureLayout();
    }
    // As the input thread does: the first push after a retry marks the new capture epoch.
    rt_.markCaptureEpoch(hwCaptureEpoch_, rt_.captureEpoch_.load(std::memory_order_acquire), /*hardware=*/true);
    if (!rt_.recordRing_.pushGather(
            layoutMsg_.generation, interleaved, channels, frames, layoutMsg_.channel.data(), layoutMsg_.count))
        rt_.inputDroppedFrames_.fetch_add(frames, std::memory_order_relaxed);
}

// Tests: one lane on `trackId` with this pick, the cursor at the start (no device).
void RecordingOps::beginTestLane(int32_t trackId, int32_t first, int32_t width) {
    captureTempoMap_ = rt_.authoring_ ? rt_.authoring_->tempoMap : nullptr;
    rt_.captureLanes_.clear();
    CaptureLane lane;
    lane.trackId = trackId;
    lane.first = first;
    lane.width = width;
    lane.channels = width;
    rt_.captureLanes_.push_back(std::move(lane));
    captureWritePos_ = 0;
    captureLoop_ = false;
    audioRecordTrackId_ = trackId;
}

bool RecordingOps::audioRecordSelfTest() {
    const int32_t tid = rt_.tracks().addAudioTrack();
    beginTestLane(tid, 0, 2);
    rt_.audioRecordStartBeat_ = 0.0;
    audioRecordSampleRate_ = 44100.0;
    audioRecordLatencyFrames_ = 0;
    rt_.audioRecording_ = true; // simulate a take without opening a device

    constexpr int32_t kFrames = 4410; // 0.1 s
    std::vector<float> sig(static_cast<size_t>(kFrames) * 2);
    for (int32_t i = 0; i < kFrames; ++i) {
        const float v = 0.5f * static_cast<float>(std::sin(2.0 * 3.14159265358979 * 440.0 * i / 44100.0));
        sig[static_cast<size_t>(i) * 2] = v;
        sig[static_cast<size_t>(i) * 2 + 1] = v;
    }
    pushInputFramesForTest(sig.data(), kFrames);
    drainInputQueue();    // ring -> buffer (mimics poll during recording)
    stopAudioRecording(); // materialise the clip

    auto t = rt_.findTrackAuthoring(tid);
    if (!t || t->clips.empty() || !t->clips.back().sample) return false;
    const SampleBuffer& sb = *t->clips.back().sample;
    double sum = 0.0;
    for (float s : sb.samples) sum += static_cast<double>(s) * s;
    const double rms = sb.samples.empty() ? 0.0 : std::sqrt(sum / static_cast<double>(sb.samples.size()));
    if (sb.frames < kFrames - 4 || rms <= 0.01) return false;
    for (double start : {0.0, 5.0}) {
        auto mappedStorage = std::make_unique<EngineRuntime>();
        EngineRuntime& mapped = *mappedStorage;
        mapped.transport_.setSampleRate(48000);
        mapped.transport_.setBpm(120);
        { // 120 BPM stepping to 60 at beat 4, on the master Tempo lane (specs/099)
            const int32_t lane = mapped.automation().addAutomationLane(
                kMasterTrackId, static_cast<int32_t>(AutomationTarget::Tempo), -1, -1);
            const AutomationPointData tempo[] = {{0, 120, 0}, {4, 120, 0}, {4, 60, 0}};
            if (lane < 0 || !mapped.automation().setAutomationPoints(kMasterTrackId, lane, tempo, 3)) return false;
        }
        const int32_t track = mapped.tracks().addAudioTrack();
        mapped.recorder().beginTestLane(track, 0, 2);
        mapped.audioRecordStartBeat_ = start;
        mapped.recorder().audioRecordSampleRate_ = 48000;
        mapped.recorder().audioRecordLatencyFrames_ = 0;
        mapped.audioRecording_ = true;
        mapped.loopEnabled_ = true;
        mapped.loopStart_ = 0;
        mapped.loopEnd_ = 8;
        mapped.recorder().beginCaptureLayout();
        if (mapped.recorder().captureLoopFrames_ != 6 * 48000 ||
            mapped.recorder().captureOrigin_ != (start == 0 ? 0 : 3 * 48000))
            return false;
        const int frames = (start == 0 ? 7 : 2) * 48000;
        std::vector<float> input(static_cast<size_t>(frames) * 2, .25f);
        mapped.recorder().feedInputForTest(input.data(), frames, 2);
        mapped.recorder().stopAudioRecording();
        auto recorded = mapped.findTrackAuthoring(track);
        if (!recorded || recorded->clips.empty()) return false;
        double end = 0;
        for (size_t i = 0; i < recorded->clips.size(); ++i) {
            ClipInfo info{};
            if (!mapped.tracks().clipInfo(track, static_cast<int32_t>(i), &info)) return false;
            end = std::max(end, info.startBeat + info.lengthBeats);
        }
        if (std::abs(end - (start == 0 ? 8 : 7)) > 1e-6) return false;
        if (start != 0 && std::abs(recorded->clips[0].startBeat - start) > 1e-6) return false;
        if (start == 0) {
            TakeInfo pass{};
            if (mapped.recorder().takeCount(track) != 2 || !mapped.recorder().takeInfo(track, 0, &pass) ||
                std::abs(pass.lengthBeats - 8) > 1e-6)
                return false;
            if (!mapped.recorder().compTake(track, pass.id, 5, 6)) return false;
            bool correct = false;
            for (const auto& c : mapped.findTrackAuthoring(track)->clips)
                if (std::abs(c.startBeat - 5) < 1e-6 && std::abs(c.sourceOffsetFrames - 3 * 48000) < 1) correct = true;
            if (!correct) return false;
        }
    }
    return true;
}

bool RecordingOps::sessionAudioRecordSelfTest() {
    const int32_t tid = rt_.tracks().addAudioTrack();
    const int32_t scene = 0;

    // Begin a session audio take without opening a device (mimic the input ring).
    beginTestLane(tid, 0, 2);
    rt_.captureLanes_[0].scene = scene; // a Session lane lands in its slot (specs/086)
    rt_.recordSessionTrackId_ = tid;
    rt_.recordSessionScene_ = scene;
    rt_.recordSessionAudio_ = true;
    audioRecordTrackId_ = tid;
    rt_.audioRecordStartBeat_ = 0.0;
    audioRecordSampleRate_ = 44100.0;
    audioRecordLatencyFrames_ = 0;
    rt_.audioRecording_ = true;

    constexpr int32_t kFrames = 4410; // 0.1 s
    std::vector<float> sig(static_cast<size_t>(kFrames) * 2);
    for (int32_t i = 0; i < kFrames; ++i) {
        const float v = 0.5f * static_cast<float>(std::sin(2.0 * 3.14159265358979 * 440.0 * i / 44100.0));
        sig[static_cast<size_t>(i) * 2] = v;
        sig[static_cast<size_t>(i) * 2 + 1] = v;
    }
    pushInputFramesForTest(sig.data(), kFrames);
    drainInputQueue();
    rt_.session().stopSessionRecord(); // -> stopAudioRecording materialises into slot.audio, then launches

    auto t = rt_.findTrackAuthoring(tid);
    if (!t || scene >= static_cast<int32_t>(t->sessionSlots.size())) return false;
    const SessionSlot& s = t->sessionSlots[scene];
    if (!s.hasClip || !s.audio.sample || s.lengthBeats <= 0.0) return false;

    // Play the slot back through the looped renderer and confirm it carries signal.
    if (rt_.transport_.sampleRate() <= 0) rt_.transport_.setSampleRate(44100.0);
    t->sessionPlayer->playing.store(scene, std::memory_order_relaxed);
    t->sessionPlayer->localBeats = 0.0;
    const double spb = rt_.transport_.samplesPerBeat();
    std::vector<float> buf(2048 * 2, 0.0f);
    rt_.renderSessionAudioSlotRaw(*t, buf.data(), 2048, spb);
    t->sessionPlayer->playing.store(-1, std::memory_order_relaxed);

    double sum = 0.0;
    for (float v : buf) sum += static_cast<double>(v) * v;
    const double rms = std::sqrt(sum / static_cast<double>(buf.size()));
    return rms > 0.01;
}

} // namespace nota::detail
