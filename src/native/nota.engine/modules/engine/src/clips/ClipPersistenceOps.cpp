// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ClipOps — audio/sample persistence, waveform peaks, track/clip UI metadata, MIDI notes, MPE store, and articulation maps.

#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "session/SessionOps.h"
#include "model/ArticulationTable.h"
#include "model/Graph.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/devices/racks/RackCore.h" // IWYU pragma: keep
#include "nota/dsp/capabilities/IMosaicInstrument.h"
#include "nota/dsp/capabilities/IRhythmInstrument.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "nota/engine/Types.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

// --- audio persistence (M7-6b) ---------------------------------------------
std::shared_ptr<SampleBuffer> ClipOps::findSampleById(int64_t sampleId) const {
    if (sampleId <= 0) return nullptr;
    for (auto& t : rt_.authoring_->tracks) {
        for (auto& c : t->clips)
            if (c.sample && c.sample->id == sampleId) return c.sample;
        for (auto& k : t->takes)
            if (k.sample && k.sample->id == sampleId) return k.sample;
        for (auto& sl : t->sessionSlots)
            if (sl.audio.sample && sl.audio.sample->id == sampleId) return sl.audio.sample;
        if (auto* smp = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(t->instrument.get()))
            if (smp->sample() && smp->sample()->id == sampleId) return smp->sample();
        // Nota Mosaic's zones (the editor's waveform; never bundled — they are referenced).
        if (auto* mo = dynamic_cast<const dsp::capabilities::IMosaicInstrument*>(t->instrument.get()))
            for (const auto& b : mo->loadedSamples())
                if (b->id == sampleId) return b;
        // Nota Rhythm per-voice one-shots.
        if (auto* rh = dynamic_cast<const dsp::capabilities::IRhythmInstrument*>(t->instrument.get()))
            for (int v = 0; v < rh->rhythmVoiceCount(); ++v)
                if (auto b = rh->voiceSampleBuf(v))
                    if (b->id == sampleId) return b;
        // Samplers and Mosaics in a rack's chains (Drum Rack pads / Instrument Rack chains), and
        // in the pads of a Drum Rack nested in a chain (specs/041).
        if (auto b = chainSampleById(dynamic_cast<RackCore*>(t->instrument.get()), sampleId, 0)) return b;
    }
    return nullptr;
}

std::shared_ptr<SampleBuffer> ClipOps::chainSampleById(const RackCore* rack, int64_t sampleId, int depth) {
    if (!rack || depth > 1) return nullptr;
    for (int32_t c = 0; c < rack->chainCount(); ++c) {
        Instrument* inst = rack->chainInstrument(c);
        if (auto* smp = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(inst))
            if (smp->sample() && smp->sample()->id == sampleId) return smp->sample();
        if (auto* mo = dynamic_cast<const dsp::capabilities::IMosaicInstrument*>(inst))
            for (const auto& b : mo->loadedSamples())
                if (b->id == sampleId) return b;
        if (auto b = chainSampleById(dynamic_cast<RackCore*>(inst), sampleId, depth + 1)) return b;
    }
    return nullptr;
}

bool ClipOps::audioClipInfo(int32_t trackId, int32_t clipIndex, AudioClipInfo* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    if (!audioClipRef(*t, clipIndex)) return false;
    const AudioClip& c = (*audioClipRef(*t, clipIndex));
    out->startBeat = c.startBeat;
    out->sourceOffsetFrames = c.sourceOffsetFrames;
    out->lengthFrames = c.lengthFrames;
    out->gain = c.gain;
    out->sampleId = c.sample ? c.sample->id : 0;
    out->pitchSemitones = c.pitchSemitones;
    out->warpEnabled = c.warpEnabled ? 1 : 0;
    out->warpMode = static_cast<int32_t>(c.warpMode);
    out->warpBeats = c.warpBeats;
    out->warpPlayStart = c.warpPlayStart;
    out->warpPlayEnd = c.warpPlayEnd;
    out->reversed = c.reversed ? 1 : 0;
    return true;
}

bool ClipOps::setClipGain(int32_t trackId, int32_t clipIndex, float gain) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = rt_.cloneTrack(*old);
    (*audioClipWrite(*nt, clipIndex)).gain = gain < 0.0f ? 0.0f : gain;
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool ClipOps::clipAdsr(int32_t trackId, int32_t clipIndex, engine::ClipAdsr* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    if (!audioClipRef(*t, clipIndex)) return false;
    const ClipAdsr& a = (*audioClipRef(*t, clipIndex)).adsr;
    out->attackBeats = a.attack;
    out->decayBeats = a.decay;
    out->releaseBeats = a.release;
    out->sustain = a.sustain;
    return true;
}

// ADSR is plain clip data (no cache to rebuild), so this is as cheap as a gain change.
bool ClipOps::setClipAdsr(int32_t trackId, int32_t clipIndex, const engine::ClipAdsr& in) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto finite = [](double v) { return std::isfinite(v) ? std::max(0.0, v) : 0.0; };
    ClipAdsr a;
    a.attack = finite(in.attackBeats);
    a.decay = finite(in.decayBeats);
    a.release = finite(in.releaseBeats);
    a.sustain = std::isfinite(in.sustain) ? std::clamp(in.sustain, 0.0f, 1.0f) : 1.0f;
    auto nt = rt_.cloneTrack(*old);
    (*audioClipWrite(*nt, clipIndex)).adsr = a;
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// clip deactivate (key 0): an inactive clip stays on the timeline but plays no
// audio / emits no MIDI. Works on both audio and instrument tracks.
bool ClipOps::setClipActive(int32_t trackId, int32_t clipIndex, bool active) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        nt->midiClips.write()[static_cast<size_t>(clipIndex)].active = active;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        nt->clips.write()[static_cast<size_t>(clipIndex)].active = active;
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t ClipOps::addAudioClipEx(int32_t trackId, const std::string& path, double startBeat, double sourceOffsetFrames,
    int64_t lengthFrames, float gain) {
    auto sample = decodeAudioFile(path, rt_.decodeOptions());
    if (!sample || sample->empty()) return -1;
    if (sample->source) sample->source->primeAround(static_cast<int64_t>(std::max(0.0, sourceOffsetFrames)), false);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    auto nt = rt_.cloneTrack(*old);
    AudioClip clip;
    clip.sample = sample;
    clip.startBeat = startBeat;
    clip.sourceOffsetFrames = sourceOffsetFrames;
    clip.lengthFrames = lengthFrames;
    clip.gain = gain;
    nt->clips.write().push_back(clip);
    const int32_t idx = static_cast<int32_t>(nt->clips.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return idx;
}

int32_t ClipOps::addAudioClip(int32_t trackId, const std::string& path, double startBeat) {
    return addAudioClipBuffer(trackId, decodeAudioFile(path, rt_.decodeOptions()), startBeat);
}

int32_t ClipOps::addAudioClipBuffer(int32_t trackId, std::shared_ptr<SampleBuffer> sample, double startBeat) {
    if (!sample || sample->empty()) return -1;
    if (sample->source) sample->source->primeAround(0, false);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;

    auto nt = rt_.cloneTrack(*old);
    AudioClip clip;
    clip.sample = sample;
    clip.startBeat = startBeat;
    nt->clips.write().push_back(clip);
    const int32_t clipIndex = static_cast<int32_t>(nt->clips.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return clipIndex;
}

// marker map (time-stretch preserves the amplitude envelope). Shared by the played
// window (getClipPeaks) and the full-material view (getClipWarpFullPeaks).
static int32_t warpPeaksRange(const AudioClip& clip, float* out, float* outRms, int32_t maxPoints, double beat0,
    double beat1, int32_t cap = 2048) {
    if (!out || maxPoints <= 0 || !clip.sample) return 0;
    const auto& m = clip.warpMarkers;
    if (m.size() < 2) return 0;
    const SampleBuffer& sb = *clip.sample;
    const int32_t buckets = std::min<int32_t>(maxPoints, cap);
    const double span = beat1 - beat0;
    if (!(span > 0.0)) return 0;
    auto srcAtBeat = [&](double beat) -> double {
        if (beat <= m.front().beat) return m.front().srcFrame;
        if (beat >= m.back().beat) return m.back().srcFrame;
        for (size_t i = 0; i + 1 < m.size(); ++i)
            if (beat < m[i + 1].beat) {
                const double bs = m[i + 1].beat - m[i].beat;
                if (bs <= 0.0) return m[i].srcFrame;
                return m[i].srcFrame + (m[i + 1].srcFrame - m[i].srcFrame) * (beat - m[i].beat) / bs;
            }
        return m.back().srcFrame;
    };
    for (int32_t b = 0; b < buckets; ++b) {
        int64_t s0 = static_cast<int64_t>(srcAtBeat(beat0 + span * b / buckets));
        int64_t s1 = static_cast<int64_t>(srcAtBeat(beat0 + span * (b + 1) / buckets));
        if (s1 < s0) std::swap(s0, s1);
        float mn, mx;
        sb.peakRange(s0, s1 + 1, mn, mx);
        out[b * 2] = mn;
        out[b * 2 + 1] = mx;
        if (outRms) outRms[b] = sb.rmsRange(s0, s1 + 1);
    }
    return buckets;
}

// Flip a min/max peak array end-to-end (each bucket's own min/max is unchanged) so a
// reversed clip draws the waveform it actually plays.
static void reversePeaks(float* peaks, float* rms, int32_t count) {
    for (int32_t a = 0, b = count - 1; a < b; ++a, --b) {
        std::swap(peaks[a * 2], peaks[b * 2]);
        std::swap(peaks[a * 2 + 1], peaks[b * 2 + 1]);
        if (rms) std::swap(rms[a], rms[b]);
    }
}

int32_t ClipOps::getClipPeaks(
    int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints, float* outRms) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& clip = (*audioClipRef(*t, clipIndex));
    if (!clip.sample) return 0;
    const SampleBuffer& sb = *clip.sample;

    // Warped clips: the waveform is drawn in the beat domain (0..warpBeats). With the
    // realtime streaming warp there is no stretched cache, so derive the envelope from
    // the SOURCE via the marker map (beat→source) — time-stretch preserves the
    // amplitude envelope, so peak-picking the mapped source window matches the warped
    // timeline (and dragged markers shift it correctly). Unwarped: straight source.
    const auto& m = clip.warpMarkers;
    const bool warped = clip.warpEnabled && m.size() >= 2 && clip.warpBeats > 0.0;

    if (warped) { // peaks over the PLAYED window (what the arrangement shows/plays)
        const int32_t n = warpPeaksRange(clip, outMinMax, outRms, maxPoints, clip.warpPlayStart, clip.warpPlayEndEff());
        if (clip.reversed) reversePeaks(outMinMax, outRms, n);
        return n;
    }

    const int64_t base = static_cast<int64_t>(clip.sourceOffsetFrames);
    const int64_t total = clip.effectiveLength();
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    for (int32_t b = 0; b < buckets; ++b) {
        const int64_t begin = base + b * per;
        float mn, mx;
        sb.peakRange(begin, begin + per, mn, mx);
        outMinMax[b * 2] = mn;
        outMinMax[b * 2 + 1] = mx;
        if (outRms) outRms[b] = sb.rmsRange(begin, begin + per);
    }
    if (clip.reversed) reversePeaks(outMinMax, outRms, buckets);
    return buckets;
}

// Peaks over the WHOLE sample (frames 0..sample->frames), regardless of the clip's
// trimmed region — the clip editor draws the full file so the Start/End brackets can
// reveal audio that was trimmed off. Unwarped only (warped uses getClipPeaks). Always in
// FILE order: the editor draws this view together with source-frame brackets, so it
// mirrors the two as one when the clip is reversed rather than flipping peaks here.
int32_t ClipOps::getClipSourcePeaks(
    int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints, float* outRms) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& clip = (*audioClipRef(*t, clipIndex));
    if (!clip.sample) return 0;
    const SampleBuffer& sb = *clip.sample;
    const int64_t total = sb.frames;
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    for (int32_t b = 0; b < buckets; ++b) {
        float mn, mx;
        sb.peakRange(b * per, (b + 1) * per, mn, mx);
        outMinMax[b * 2] = mn;
        outMinMax[b * 2 + 1] = mx;
        if (outRms) outRms[b] = sb.rmsRange(b * per, (b + 1) * per);
    }
    return buckets;
}

// Peaks over the FULL warped material [0..warpBeats], ignoring the trim window — the
// clip editor draws the whole warp so the Start/End brackets can reveal trimmed beats.
// In marker (file) order — the editor mirrors this view and its markers together when the
// clip is reversed, so flipping the peaks alone here would desync them.
int32_t ClipOps::getClipWarpFullPeaks(
    int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints, float* outRms) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& clip = (*audioClipRef(*t, clipIndex));
    if (!(clip.warpEnabled && clip.warpBeats > 0.0)) return 0;
    return warpPeaksRange(clip, outMinMax, outRms, maxPoints, 0.0, clip.warpBeats);
}

// The clip editor's zoomed-in detail: peaks over just the visible slice, so zooming
// re-resolves the waveform instead of magnifying the whole-clip overview into blocks.
constexpr int32_t kRangePeaksCap = 8192;

int32_t ClipOps::getClipWarpPeaksRange(int32_t trackId, int32_t clipIndex, double beat0, double beat1, float* outMinMax,
    int32_t maxPoints, float* outRms) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& clip = (*audioClipRef(*t, clipIndex));
    if (!(clip.warpEnabled && clip.warpBeats > 0.0)) return 0;
    beat0 = std::clamp(beat0, 0.0, clip.warpBeats);
    beat1 = std::clamp(beat1, 0.0, clip.warpBeats);
    return warpPeaksRange(clip, outMinMax, outRms, maxPoints, beat0, beat1, kRangePeaksCap);
}

int32_t ClipOps::getClipSourcePeaksRange(int32_t trackId, int32_t clipIndex, double frame0, double frame1,
    float* outMinMax, int32_t maxPoints, float* outRms) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& clip = (*audioClipRef(*t, clipIndex));
    if (!clip.sample) return 0;
    const SampleBuffer& sb = *clip.sample;
    const double total = static_cast<double>(sb.frames);
    frame0 = std::clamp(frame0, 0.0, total);
    frame1 = std::clamp(frame1, 0.0, total);
    if (!(frame1 > frame0)) return 0;
    const int32_t buckets = std::min(maxPoints, kRangePeaksCap);
    const double per = (frame1 - frame0) / buckets;
    for (int32_t b = 0; b < buckets; ++b) {
        const int64_t f0 = static_cast<int64_t>(std::floor(frame0 + per * b));
        const int64_t f1 = std::max<int64_t>(f0 + 1, static_cast<int64_t>(std::floor(frame0 + per * (b + 1))));
        float mn, mx;
        sb.peakRange(f0, f1, mn, mx);
        outMinMax[b * 2] = mn;
        outMinMax[b * 2 + 1] = mx;
        if (outRms) outRms[b] = sb.rmsRange(f0, f1);
    }
    return buckets;
}

// --- clip / track names + track colour (UI metadata) ------------------------
namespace {
int32_t copyStr(const std::string& s, char* out, int32_t cap) {
    if (out && cap > 0) {
        const int32_t n = std::min<int32_t>(static_cast<int32_t>(s.size()), cap - 1);
        std::memcpy(out, s.data(), static_cast<size_t>(n));
        out[n] = '\0';
    }
    return static_cast<int32_t>(s.size());
}
} // namespace

bool ClipOps::setClipName(int32_t trackId, int32_t clipIndex, const std::string& name) {
    // A Session slot (SessionClip index, MIDI or audio): its name is the slot's.
    if (const int32_t scene = sessionSceneOfClip(clipIndex); scene >= 0)
        return rt_.session().setSessionClipName(trackId, scene, name);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        nt->midiClips.write()[static_cast<size_t>(clipIndex)].name = name;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        nt->clips.write()[static_cast<size_t>(clipIndex)].name = name;
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}
int32_t ClipOps::clipName(int32_t trackId, int32_t clipIndex, char* out, int32_t cap) const {
    if (const int32_t scene = sessionSceneOfClip(clipIndex); scene >= 0) {
        std::string n;
        return rt_.session().sessionClipName(trackId, scene, n) ? copyStr(n, out, cap)
                                                                : copyStr(std::string(), out, cap);
    }
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0;
    if (t->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
        return copyStr(t->midiClips[static_cast<size_t>(clipIndex)].name, out, cap);
    }
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    return copyStr(t->clips[static_cast<size_t>(clipIndex)].name, out, cap);
}
bool ClipOps::setTrackName(int32_t trackId, const std::string& name) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    nt->name = name;
    rt_.republishWithTrack(trackId, nt);
    return true;
}
int32_t ClipOps::trackName(int32_t trackId, char* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? copyStr(t->name, out, cap) : 0;
}
bool ClipOps::setTrackColorIndex(int32_t trackId, int32_t colorIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    nt->colorIndex = colorIndex;
    rt_.republishWithTrack(trackId, nt);
    return true;
}
int32_t ClipOps::trackColorIndex(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->colorIndex : -1;
}
bool ClipOps::setTrackPianoFold(int32_t trackId, bool on) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    if (old->pianoFold == on) return true;
    auto nt = rt_.cloneTrack(*old);
    nt->pianoFold = on;
    rt_.republishWithTrackRaw(trackId, nt); // a view change, not an edit: no undo step
    return true;
}
int32_t ClipOps::trackPianoFold(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? (t->pianoFold ? 1 : 0) : -1;
}
bool ClipOps::setTrackEditLink(int32_t trackId, bool on) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Group) return false;
    if (old->editLink == on) return true; // no change, no undo step
    auto nt = rt_.cloneTrack(*old);
    nt->editLink = on;
    rt_.republishWithTrack(trackId, nt); // changes what edits do: an undo step (unlike pianoFold)
    return true;
}
int32_t ClipOps::trackEditLink(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? (t->editLink ? 1 : 0) : -1;
}

int32_t ClipOps::addMidiClip(int32_t trackId, double startBeat, double lengthBeats) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return -1;
    auto nt = rt_.cloneTrack(*old);
    MidiClip c;
    c.startBeat = startBeat;
    c.lengthBeats = lengthBeats;
    nt->midiClips.write().push_back(c);
    const int32_t idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return idx;
}

bool ClipOps::setClipNotes(int32_t trackId, int32_t clipIndex, const NoteData* notes, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    auto nt = rt_.cloneTrack(*old);
    MidiClip& clip = nt->midiClips.write()[static_cast<size_t>(clipIndex)];
    clip.notes.clear();
    clip.notes.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) { clip.notes.push_back(noteFromData(notes[i])); }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool ClipOps::setClipNotesLive(int32_t trackId, int32_t clipIndex, const NoteData* notes, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    auto nt = rt_.cloneTrack(*old);
    MidiClip& clip = nt->midiClips.write()[static_cast<size_t>(clipIndex)];
    clip.notes.clear();
    clip.notes.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) { clip.notes.push_back(noteFromData(notes[i])); }
    rt_.republishWithTrackRaw(trackId, nt); // no undo checkpoint — the UI seeds one per gesture
    return true;
}

int32_t ClipOps::getClipNotes(int32_t trackId, int32_t clipIndex, NoteData* out, int32_t maxNotes) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    const MidiClip& clip = t->midiClips[static_cast<size_t>(clipIndex)];
    const int32_t n = std::min<int32_t>(maxNotes, static_cast<int32_t>(clip.notes.size()));
    for (int32_t i = 0; i < n; ++i) noteToData(clip.notes[static_cast<size_t>(i)], out[i]);
    return n;
}

// ---- recorded MPE store ----------------------------------------------------------

int32_t ClipOps::storeNoteExpr(std::shared_ptr<const NoteExpr> e) {
    if (!e || e->empty()) return 0;
    std::lock_guard<std::mutex> lk(exprMx_);
    const int32_t id = nextExprId_++;
    exprStore_.emplace(id, std::move(e));
    return id;
}

int32_t ClipOps::createNoteExpr(const ExprPointData* points, int32_t count) {
    if (!points || count <= 0) return 0;
    auto e = std::make_shared<NoteExpr>();
    for (int32_t i = 0; i < count; ++i) {
        const auto& p = points[i];
        if (p.dim < 0 || p.dim >= kExprDims || !std::isfinite(p.beat) || !std::isfinite(p.value)) continue;
        const float v = p.dim == ExprBend ? std::clamp(p.value, -96.0f, 96.0f) : std::clamp(p.value, 0.0f, 1.0f);
        e->dim[p.dim].push_back({std::max(0.0f, p.beat), v});
    }
    for (auto& d : e->dim)
        std::stable_sort(d.begin(), d.end(), [](const ExprPoint& a, const ExprPoint& b) { return a.beat < b.beat; });
    return storeNoteExpr(std::move(e));
}

int32_t ClipOps::noteExprPoints(int32_t id, ExprPointData* out, int32_t maxPoints) const {
    std::shared_ptr<const NoteExpr> e;
    {
        std::lock_guard<std::mutex> lk(exprMx_);
        auto it = exprStore_.find(id);
        if (it == exprStore_.end()) return 0;
        e = it->second;
    }
    int32_t n = 0;
    for (int32_t d = 0; d < kExprDims; ++d)
        for (const auto& p : e->dim[d]) {
            if (out && n < maxPoints) out[n] = ExprPointData{d, p.beat, p.value};
            ++n;
        }
    return out ? std::min(n, maxPoints) : n;
}

Note ClipOps::noteFromData(const NoteData& d) const {
    Note n;
    n.pitch = d.pitch;
    n.startBeat = d.startBeat;
    n.lengthBeats = d.lengthBeats;
    n.velocity = d.velocity;
    n.articulation = d.articulationId > 0 ? d.articulationId : 0;
    if (d.exprId > 0) {
        std::lock_guard<std::mutex> lk(exprMx_);
        auto it = exprStore_.find(d.exprId);
        if (it != exprStore_.end()) {
            n.exprId = d.exprId;
            n.expr = it->second;
        }
    }
    return n;
}

void ClipOps::noteToData(const Note& n, NoteData& d) {
    d.pitch = n.pitch;
    d.startBeat = n.startBeat;
    d.lengthBeats = n.lengthBeats;
    d.velocity = n.velocity;
    d.exprId = n.expr ? n.exprId : 0;
    d.articulationId = n.articulation;
}

// --- articulation maps (specs/085) ---------------------------------------------------------

namespace {
constexpr int32_t kMaxArticulationJson = 16 * 1024 * 1024; // the map file's own limit
}

bool ClipOps::setTrackArticulationMap(int32_t trackId, int32_t defaultId, const ArticulationEntryData* entries,
    int32_t entryCount, const ArticulationActionData* actions, int32_t actionCount, const char* json, int32_t jsonLen) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    if (jsonLen < 0 || jsonLen > kMaxArticulationJson || (jsonLen > 0 && !json)) return false;
    // Sizes checked before anything is allocated for them; the rest is ArticulationTable::build's.
    if (entryCount < 0 || actionCount < 0 || entryCount > kArticulationMaxEntries ||
        actionCount > kArticulationMaxActions)
        return false;
    if ((entryCount > 0 && !entries) || (actionCount > 0 && !actions)) return false;
    std::vector<ArticulationEntry> e(static_cast<size_t>(entryCount));
    for (int32_t i = 0; i < entryCount; ++i) {
        const ArticulationEntryData& s = entries[i];
        e[static_cast<size_t>(i)] = {
            s.id, static_cast<ArticulationTrigger>(s.triggerMode), s.offsetMs, s.leadMs, s.actionStart, s.actionCount};
    }
    std::vector<ArticulationAction> a(static_cast<size_t>(actionCount));
    for (int32_t i = 0; i < actionCount; ++i) {
        const ArticulationActionData& s = actions[i];
        a[static_cast<size_t>(i)] = {static_cast<ArticulationActionKind>(s.kind), s.channel, s.data1, s.data2};
    }
    auto table = ArticulationTable::build(defaultId, e.data(), entryCount, a.data(), actionCount);
    if (!table) return false;
    auto nt = rt_.cloneTrack(*old);
    nt->articulationTable = std::move(table);
    nt->articulationMapJson = std::make_shared<const std::string>(json ? json : "", static_cast<size_t>(jsonLen));
    rt_.republishWithTrack(trackId, nt); // the clips' timelines are rebuilt as it publishes
    return true;
}

bool ClipOps::clearTrackArticulationMap(int32_t trackId) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    if (!old->articulationTable && !old->articulationMapJson) return true; // nothing to undo
    auto nt = rt_.cloneTrack(*old);
    nt->articulationTable = nullptr;
    nt->articulationMapJson = nullptr;
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t ClipOps::trackArticulationMapJson(int32_t trackId, char* out, int32_t maxLen) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->articulationMapJson) return 0;
    const std::string& s = *t->articulationMapJson;
    const auto n = static_cast<int32_t>(s.size());
    if (out && maxLen >= n) std::memcpy(out, s.data(), s.size());
    return n;
}

std::shared_ptr<const ArticulationTable> ClipOps::trackArticulationTable(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->articulationTable : nullptr;
}

int32_t ClipOps::clipNoteCount(int32_t trackId, int32_t clipIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    return static_cast<int32_t>(t->midiClips[static_cast<size_t>(clipIndex)].notes.size());
}

} // namespace nota::detail
