// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Take lanes (specs/067): the passes of an Arrangement audio loop recording, kept on the track
// so they can be comped. Takes are graph data, so every edit here is a message-thread clone +
// republish (AR-5) and undo comes from the snapshot history. The comp is the track's ordinary
// clips; a section made from a take carries the take's id, which only the lanes' highlight and
// flatten read — the audio thread plays clips as always.

#include "Engine.h"
#include "media/AudioFile.h"

#include <algorithm>
#include <cmath>

namespace nota {

namespace {

const AudioTake* findTake(const Track& t, int32_t takeId) {
    for (const auto& k : t.takes)
        if (k.id == takeId) return &k;
    return nullptr;
}

} // namespace

double Engine::beatsPerSourceFrame(double sampleRate) const {
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return 0.0;
    const double sr = sampleRate > 0.0 ? sampleRate : devSR;
    return devSR / sr / spb;
}

int32_t Engine::takeCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->takes.size()) : 0;
}

bool Engine::takeInfo(int32_t trackId, int32_t index, NotaTakeInfo* out) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !out || index < 0 || index >= static_cast<int32_t>(t->takes.size())) return false;
    const AudioTake& k = t->takes[index];
    const double bpf = k.sample ? beatsPerSourceFrame(k.sample->sourceSampleRate) : 0.0;
    *out = NotaTakeInfo{};
    out->id = k.id;
    out->number = k.number;
    out->group = k.group;
    out->origin_beat = k.originBeat;
    out->start_beat = k.originBeat + k.sourceOffsetFrames * bpf;
    out->length_beats = static_cast<double>(k.lengthFrames) * bpf;
    out->source_offset_frames = k.sourceOffsetFrames;
    out->length_frames = k.lengthFrames;
    out->sample_id = k.sample ? k.sample->id : 0;
    return true;
}

int32_t Engine::takePeaks(int32_t trackId, int32_t index, float* outMinMax, int32_t maxPoints) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !outMinMax || maxPoints <= 0 || index < 0 || index >= static_cast<int32_t>(t->takes.size())) return 0;
    const AudioTake& k = t->takes[index];
    if (!k.sample || k.lengthFrames <= 0) return 0;
    const int64_t base = static_cast<int64_t>(k.sourceOffsetFrames);
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, k.lengthFrames));
    const int64_t per = k.lengthFrames / buckets;
    for (int32_t b = 0; b < buckets; ++b) {
        const int64_t begin = base + b * per;
        float mn, mx;
        k.sample->peakRange(begin, begin + per, mn, mx);
        outMinMax[b * 2] = mn;
        outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

bool Engine::compTake(int32_t trackId, int32_t takeId, double startBeat, double endBeat) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    const AudioTake* k = findTake(*old, takeId);
    if (!k || !k->sample) return false;
    const double bpf = beatsPerSourceFrame(k->sample->sourceSampleRate);
    if (bpf <= 0.0) return false;
    // Whole frames of the take, clamped to what the pass recorded: sections from one
    // recording then meet frame to frame, so a comp has no gaps or overlaps at its seams.
    const double lo = k->sourceOffsetFrames, hi = k->sourceOffsetFrames + static_cast<double>(k->lengthFrames);
    const double f0 = std::clamp(std::round((std::min(startBeat, endBeat) - k->originBeat) / bpf), lo, hi);
    const double f1 = std::clamp(std::round((std::max(startBeat, endBeat) - k->originBeat) / bpf), lo, hi);
    if (f1 - f0 < 1.0) return false;

    auto nt = cloneTrack(*old);
    const double ns = k->originBeat + f0 * bpf, ne = k->originBeat + f1 * bpf;
    overwriteAudioClipsInRange(nt->clips, ns, ne);
    AudioClip clip;
    clip.sample = k->sample;
    clip.startBeat = ns;
    clip.sourceOffsetFrames = f0;
    clip.lengthFrames = static_cast<int64_t>(f1 - f0);
    clip.takeId = k->id;
    nt->clips.push_back(std::move(clip));
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::deleteTake(int32_t trackId, int32_t takeId) {
    auto old = findTrackAuthoring(trackId);
    if (!old || !findTake(*old, takeId)) return false;
    auto nt = cloneTrack(*old);
    nt->takes.erase(std::remove_if(nt->takes.begin(), nt->takes.end(), [&](const AudioTake& k) { return k.id == takeId; }),
                    nt->takes.end());
    for (auto& c : nt->clips)
        if (c.takeId == takeId) c.takeId = 0; // keeps playing, no longer a section of a take
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::flattenTakes(int32_t trackId) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->takes.empty()) return false;
    auto nt = cloneTrack(*old);

    std::vector<int32_t> groups;
    for (const auto& k : nt->takes)
        if (std::find(groups.begin(), groups.end(), k.group) == groups.end()) groups.push_back(k.group);

    for (int32_t g : groups) {
        const AudioTake* first = nullptr;
        for (const auto& k : nt->takes)
            if (k.group == g && k.sample) {
                first = &k;
                break;
            }
        auto inGroup = [&](int32_t takeId) {
            for (const auto& k : nt->takes)
                if (k.id == takeId) return k.group == g;
            return false;
        };
        if (!first) continue;
        const SampleBuffer& ref = *first->sample;
        const double origin = first->originBeat;
        const double bpf = beatsPerSourceFrame(ref.sourceSampleRate);
        const int64_t frames = ref.frames;
        const int32_t ch = ref.channels;

        // A section merges when it is still exactly where its take put it and plays its
        // frames as they are; anything else was edited into a clip of its own and stays one.
        auto plain = [&](const AudioClip& c) {
            if (!c.sample || c.sample->frames != frames || c.sample->channels != ch ||
                c.sample->sourceSampleRate != ref.sourceSampleRate)
                return false;
            if (!c.active || c.warpEnabled || c.reversed || c.pitchSemitones != 0.0f) return false;
            if (!c.volumeEnvelope.points.empty() || !c.panEnvelope.points.empty() || !c.adsr.isIdentity()) return false;
            const double off = std::round(c.sourceOffsetFrames);
            const int64_t len = c.effectiveLength();
            if (off < 0.0 || len <= 0 || static_cast<int64_t>(off) + len > frames) return false;
            return bpf > 0.0 && std::abs(c.startBeat - (origin + off * bpf)) < 1e-6;
        };

        std::vector<float> buf;
        int64_t lo = frames, hi = 0;
        std::vector<AudioClip> kept;
        kept.reserve(nt->clips.size());
        for (auto& c : nt->clips) {
            if (c.takeId == 0 || !inGroup(c.takeId)) {
                kept.push_back(std::move(c));
                continue;
            }
            if (!plain(c)) {
                c.takeId = 0;
                kept.push_back(std::move(c));
                continue;
            }
            if (buf.empty()) buf.assign(static_cast<size_t>(frames) * ch, 0.0f);
            const int64_t off = static_cast<int64_t>(std::round(c.sourceOffsetFrames));
            const int64_t len = c.effectiveLength();
            const float* src = c.sample->samples.data() + off * ch;
            float* dst = buf.data() + off * ch;
            for (int64_t i = 0; i < len * ch; ++i) dst[i] = src[i] * c.gain;
            lo = std::min(lo, off);
            hi = std::max(hi, off + len);
        }
        nt->clips = std::move(kept);
        if (hi > lo) {
            auto sb = std::make_shared<SampleBuffer>();
            sb->channels = ch;
            sb->frames = frames;
            sb->sourceSampleRate = ref.sourceSampleRate;
            sb->samples = std::move(buf);
            AudioClip clip;
            clip.sample = std::move(sb);
            clip.startBeat = origin + static_cast<double>(lo) * bpf;
            clip.sourceOffsetFrames = static_cast<double>(lo);
            clip.lengthFrames = hi - lo;
            nt->clips.push_back(std::move(clip));
        }
        nt->takes.erase(std::remove_if(nt->takes.begin(), nt->takes.end(), [&](const AudioTake& k) { return k.group == g; }),
                        nt->takes.end());
    }
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::clipTakeId(int32_t trackId, int32_t clipIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    return t->clips[clipIndex].takeId;
}

int32_t Engine::restoreTake(int32_t trackId, const std::string& path, int32_t number, int32_t group, double originBeat,
                            double sourceOffsetFrames, int64_t lengthFrames) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return -1;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return -1;
    auto nt = cloneTrack(*old);
    AudioTake k;
    k.sample = std::move(sample);
    k.id = ++nextTakeId_;
    k.number = number;
    k.group = group;
    nextTakeGroup_ = std::max(nextTakeGroup_, group);
    k.originBeat = originBeat;
    k.sourceOffsetFrames = std::clamp(sourceOffsetFrames, 0.0, static_cast<double>(k.sample->frames));
    k.lengthFrames = std::clamp<int64_t>(lengthFrames, 0, k.sample->frames - static_cast<int64_t>(k.sourceOffsetFrames));
    nt->takes.push_back(std::move(k));
    republishWithTrackRaw(trackId, nt);
    return nextTakeId_;
}

bool Engine::setClipTakeId(int32_t trackId, int32_t clipIndex, int32_t takeId) {
    auto old = findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    c.takeId = takeId;
    // A reopened comp section decodes its own copy of the take's file: share the take's buffer
    // instead, as a freshly recorded comp does.
    if (const AudioTake* k = findTake(*nt, takeId); k && k->sample && c.sample && c.sample != k->sample &&
                                                     c.sample->frames == k->sample->frames &&
                                                     c.sample->channels == k->sample->channels &&
                                                     c.sample->sourceSampleRate == k->sample->sourceSampleRate)
        c.sample = k->sample;
    republishWithTrackRaw(trackId, nt);
    return true;
}

bool Engine::takeRecordTest(int32_t trackId, const float* interleavedStereo, int32_t frames, double startBeat) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio || !interleavedStereo || frames <= 0 || audioRecording_) return false;
    const size_t before = t->takes.size();
    audioCaptureBuf_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;
    captureChannels_ = Track::kInputStereo;
    inputDroppedFrames_.store(0, std::memory_order_relaxed);
    audioRecordTrackId_ = trackId;
    audioRecordStartBeat_ = startBeat;
    audioRecordSampleRate_ = transport_.sampleRate();
    audioRecordLatencyFrames_ = 0;
    beginCaptureLayout();
    audioRecording_ = true;
    constexpr int32_t kChunk = 1024; // well inside the capture ring, drained like poll() would
    for (int32_t at = 0; at < frames; at += kChunk) {
        pushInputFramesForTest(interleavedStereo + static_cast<size_t>(at) * 2, std::min(kChunk, frames - at));
        drainInputQueue();
    }
    stopAudioRecording();
    auto nt = findTrackAuthoring(trackId);
    return nt && (nt->takes.size() > before || !nt->clips.empty());
}

} // namespace nota
