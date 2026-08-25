// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — tracks, mixer strips, send/return buses, arrangement clips (geometry + move/trim/split/duplicate/delete), MIDI clips & notes, and audio-clip/sample persistence.

#include "Engine.h"
#include "AudioFile.h"
#include "Compressor.h"
#include "CoreAudioBackend.h"
#include "Delay.h"
#include "Eq.h"
#include "PluginHostBridge.h"
#include "Reverb.h"
#include "RackCore.h"
#include "Sampler.h"
#include "GrainSynth.h"
#include "RhythmMachine.h"
#include "Synth.h"
#include "TempoDetect.h"
#include "Utility.h"
#include "WarpStream.h"

#include <algorithm>
#include <cstring>
#include <chrono>
#include <cmath>
#include <limits>
#include <map>

namespace nota {

// --- audio persistence (M7-6b) ---------------------------------------------
std::shared_ptr<SampleBuffer> Engine::findSampleById(int64_t sampleId) const {
    if (sampleId <= 0) return nullptr;
    for (auto& t : authoring_->tracks) {
        for (auto& c : t->clips)
            if (c.sample && c.sample->id == sampleId) return c.sample;
        for (auto& sl : t->sessionSlots)
            if (sl.audio.sample && sl.audio.sample->id == sampleId) return sl.audio.sample;
        if (auto* smp = dynamic_cast<Sampler*>(t->instrument.get()))
            if (smp->sample() && smp->sample()->id == sampleId) return smp->sample();
        if (auto* gr = dynamic_cast<GrainSynth*>(t->instrument.get()))
            if (gr->sample() && gr->sample()->id == sampleId) return gr->sample();
        // Nota Rhythm per-voice one-shots.
        if (auto* rh = dynamic_cast<RhythmMachine*>(t->instrument.get()))
            for (int v = 0; v < RhythmMachine::kVoices; ++v)
                if (auto b = rh->voiceSampleBuf(v)) if (b->id == sampleId) return b;
        // Samplers nested in a rack's chains (Drum Rack pads / Instrument Rack chains).
        if (auto* rack = dynamic_cast<RackCore*>(t->instrument.get()))
            for (int32_t c = 0; c < rack->chainCount(); ++c)
                if (auto* smp = dynamic_cast<Sampler*>(rack->chainInstrument(c)))
                    if (smp->sample() && smp->sample()->id == sampleId) return smp->sample();
    }
    return nullptr;
}

bool Engine::audioClipInfo(int32_t trackId, int32_t clipIndex, NotaAudioClipInfo* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return false;
    const AudioClip& c = t->clips[clipIndex];
    out->start_beat = c.startBeat;
    out->source_offset_frames = c.sourceOffsetFrames;
    out->length_frames = c.lengthFrames;
    out->gain = c.gain;
    out->sample_id = c.sample ? c.sample->id : 0;
    out->pitch_semitones = c.pitchSemitones;
    out->warp_enabled = c.warpEnabled ? 1 : 0;
    out->warp_mode = static_cast<int32_t>(c.warpMode);
    out->warp_beats = c.warpBeats;
    out->warp_play_start = c.warpPlayStart;
    out->warp_play_end = c.warpPlayEnd;
    return true;
}

bool Engine::setClipGain(int32_t trackId, int32_t clipIndex, float gain) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    auto nt = cloneTrack(*old);
    nt->clips[clipIndex].gain = gain < 0.0f ? 0.0f : gain;
    republishWithTrack(trackId, nt);
    return true;
}

// clip deactivate (key 0): an inactive clip stays on the timeline but plays no
// audio / emits no MIDI. Works on both audio and instrument tracks.
bool Engine::setClipActive(int32_t trackId, int32_t clipIndex, bool active) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        nt->midiClips[clipIndex].active = active;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        nt->clips[clipIndex].active = active;
    }
    republishWithTrack(trackId, nt);
    return true;
}

namespace {
constexpr int32_t kWarpBuildChunk = 16384;   // matches WarpStream::kMaxBlock (its out scratch cap)

// Played-window length in device frames for a warp clip (0 = not a buildable warp clip).
int64_t warpCacheFrames(const AudioClip& c, double spb, double devSR) {
    if (!c.warpEnabled || !c.sample || devSR <= 0.0 || spb <= 0.0
        || c.sample->sourceSampleRate <= 0.0 || c.warpMarkers.size() < 2) return 0;
    return std::llround(c.warpPlayLen() * spb);
}

// Build a warped clip's offline stretch cache, or clear it when off.
// Runs the streaming stretcher ONCE over the whole played window on the authoring
// thread and captures the device-rate output, so the audio thread just copies from
// the buffer (renderAudioClipsRaw) with no realtime stretch and no per-clip-start
// priming spike. Called only on discrete edits / tempo / SR changes (never per
// block); the in-use cache retires with the old snapshot, so it's race-free.
// For a big project this is heavy (stretches every clip's played window), so the load
// path defers it to Engine::warpBuildStep — see Engine::configureClipWarp.
void buildWarpCache(AudioClip& c, double spb, double devSR) {
    if (c.warpMarkers.size() >= 2) c.warpBeats = c.warpMarkers.back().beat;
    const int64_t frames = warpCacheFrames(c, spb, devSR);
    if (frames <= 0) { c.warpCache = nullptr; return; }

    auto cache = std::make_shared<WarpCache>();
    cache->frames = frames;
    cache->spb = spb;
    cache->devSR = devSR;
    cache->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);

    ClipWarpStream st;
    st.configure(c.sample->channels, c.warpMode, c.pitchSemitones, c.sample->sourceSampleRate, devSR);
    // Render the played window [warpPlayStart, warpPlayEndEff] in contiguous chunks: the
    // stream primes once on the first chunk, then runs seamlessly (drift-free feed) to the
    // end — exactly the audio the old realtime path produced, minus the RT cost. Gain is 1
    // here (applied at read time), so gain changes don't invalidate the cache.
    const double warpOffset = c.warpPlayStart * spb;
    for (int64_t off = 0; off < frames; off += kWarpBuildChunk) {
        const int32_t n = static_cast<int32_t>(std::min<int64_t>(kWarpBuildChunk, frames - off));
        st.render(cache->samples.data() + off * 2, n, warpOffset + static_cast<double>(off),
                  c.warpMarkers, c.warpBeats, *c.sample, spb, devSR, 1.0f);
    }
    c.warpCache = std::move(cache);
}

// Length-neutral warp seed: keep the clip's current musical length so toggling
// warp doesn't move audio; tempo changes then stretch it. Two end markers.
void seedNeutralWarp(AudioClip& c, double spb, double devSR) {
    c.warpBeats = static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / spb;
    c.warpMarkers = { {c.sourceOffsetFrames, 0.0},
                      {c.sourceOffsetFrames + static_cast<double>(c.effectiveLength()), c.warpBeats} };
    c.warpPlayStart = 0.0; c.warpPlayEnd = 0.0;   // fresh warp → play the whole material
}

// Auto-warp seed: estimate the source tempo, round the clip's true musical length
// to the beat grid, and seed two end markers so the whole clip stretches to that
// many beats at the project tempo (i.e. conforms to the current BPM). Returns the
// detected BPM, or 0 when detection fails (leaving markers untouched by caller).
double seedAutoWarp(AudioClip& c, double spb, double devSR) {
    const double srcSR = c.sample->sourceSampleRate;
    const int64_t srcLen = c.effectiveLength();
    const double bpm = detectTempo(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen, srcSR);
    if (bpm <= 0.0 || srcLen <= 0) return 0.0;
    const double durationSec = static_cast<double>(srcLen) / srcSR;
    const double beatsRaw = durationSec * bpm / 60.0;
    double beats = std::llround(beatsRaw);
    if (beats < 1.0) beats = 1.0;
    c.warpBeats = beats;
    c.warpMarkers = { {c.sourceOffsetFrames, 0.0},
                      {c.sourceOffsetFrames + static_cast<double>(srcLen), beats} };
    c.warpPlayStart = 0.0; c.warpPlayEnd = 0.0;   // fresh warp → play the whole material
    return bpm;
}

// Keep the trim window valid after warpBeats changes (marker/length edits). Preserves
// the window (clamped); an explicit end past the new length snaps back to full.
void clampWarpPlay(AudioClip& c) {
    if (c.warpPlayStart < 0.0 || c.warpPlayStart >= c.warpBeats) c.warpPlayStart = 0.0;
    if (c.warpPlayEnd > c.warpBeats || c.warpPlayEnd <= c.warpPlayStart) c.warpPlayEnd = 0.0;   // 0 = full
}
} // namespace

// Build (or clear) a clip's warp cache — unless the load path has asked to defer it.
// During project load `deferWarpBuild_` is set so Apply's many warp edits stay cheap;
// the caches are then filled incrementally by warpBuildStep with a progress bar, so a
// project with hundreds of warped clips opens at once instead of freezing for ~a minute.
void Engine::configureClipWarp(AudioClip& c, double spb, double devSR) {
    if (deferWarpBuild_) { c.warpCache = nullptr; return; }   // filled later by warpBuildStep
    buildWarpCache(c, spb, devSR);
}

// --- incremental warp-cache build (managed-driven, with a progress bar) --------------
// The load path defers cache building (above); these drive it afterwards in bounded
// chunks on the authoring thread so the UI stays responsive and a progress bar advances
// smoothly. State is a cursor over the ONE clip currently building (its partial buffer +
// stretcher), plus a scan for the next un-built warp clip when idle.

// The authoring clip the build cursor currently targets, if it's still valid to build
// (exists, still warped, still un-cached, same played length). Null when the cursor is
// idle or a mid-build edit changed the clip out from under us (→ abandon + rescan).
const AudioClip* Engine::warpBuildTargetClip() const {
    if (!wb_.building) return nullptr;
    auto t = findTrackAuthoring(wb_.trackId);
    if (!t || t->type() != TrackType::Audio) return nullptr;
    if (wb_.clipIndex < 0 || wb_.clipIndex >= static_cast<int32_t>(t->clips.size())) return nullptr;
    const AudioClip& c = t->clips[wb_.clipIndex];
    if (!c.warpEnabled || c.warpCache) return nullptr;
    if (!wb_.cache || warpCacheFrames(c, transport_.samplesPerBeat(), transport_.sampleRate()) != wb_.cache->frames)
        return nullptr;
    return &c;
}

// Total device frames of warped audio still needing a cache (progress denominator).
int64_t Engine::warpBuildRemainingFrames() const {
    if (!authoring_) return 0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    int64_t sum = 0;
    for (auto& t : authoring_->tracks) {
        if (t->type() != TrackType::Audio) continue;
        for (auto& c : t->clips)
            if (c.warpEnabled && !c.warpCache) sum += warpCacheFrames(c, spb, devSR);
    }
    // The in-progress clip is still un-cached above, so its full length was counted —
    // subtract what's already rendered so the bar reflects real progress.
    if (warpBuildTargetClip()) sum -= wb_.offset;
    return std::max<int64_t>(0, sum);
}

// Do up to ~maxFrames of warp-cache building; returns frames still remaining (0 = done).
// Installs each finished clip via an atomic republish so playback picks it up mid-build.
int64_t Engine::warpBuildStep(int32_t maxFrames) {
    if (!authoring_) return 0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return 0;
    if (maxFrames < kWarpBuildChunk) maxFrames = kWarpBuildChunk;

    int32_t budget = maxFrames;
    while (budget > 0) {
        // Idle: find the next warp clip without a cache and start building it.
        if (!wb_.building) {
            int32_t tid = -1, ci = -1; const AudioClip* target = nullptr;
            for (auto& t : authoring_->tracks) {
                if (t->type() != TrackType::Audio) continue;
                for (size_t i = 0; i < t->clips.size(); ++i) {
                    const AudioClip& c = t->clips[i];
                    if (c.warpEnabled && !c.warpCache && warpCacheFrames(c, spb, devSR) > 0) {
                        target = &c; tid = t->id(); ci = static_cast<int32_t>(i); break;
                    }
                }
                if (target) break;
            }
            if (!target) break;   // nothing left to build
            const int64_t frames = warpCacheFrames(*target, spb, devSR);
            wb_.cache = std::make_shared<WarpCache>();
            wb_.cache->frames = frames; wb_.cache->spb = spb; wb_.cache->devSR = devSR;
            wb_.cache->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);
            wb_.stream = std::make_unique<ClipWarpStream>();
            wb_.stream->configure(target->sample->channels, target->warpMode, target->pitchSemitones,
                                  target->sample->sourceSampleRate, devSR);
            wb_.trackId = tid; wb_.clipIndex = ci; wb_.offset = 0; wb_.building = true;
        }

        // Re-fetch + revalidate the target (an edit between steps may have changed it).
        const AudioClip* clip = warpBuildTargetClip();
        if (!clip) { wb_ = {}; continue; }   // abandon this clip, rescan on the next loop

        // Render another chunk of the in-progress clip.
        const double warpOffset = clip->warpPlayStart * spb;
        const int32_t n = static_cast<int32_t>(
            std::min<int64_t>({ static_cast<int64_t>(kWarpBuildChunk),
                                static_cast<int64_t>(budget),
                                wb_.cache->frames - wb_.offset }));
        if (n > 0) {
            wb_.stream->render(wb_.cache->samples.data() + wb_.offset * 2, n,
                               warpOffset + static_cast<double>(wb_.offset),
                               clip->warpMarkers, clip->warpBeats, *clip->sample,
                               spb, devSR, 1.0f);
            wb_.offset += n; budget -= n;
        }

        // Finished this clip: install its cache via an atomic republish, then continue.
        if (wb_.offset >= wb_.cache->frames) {
            auto old = findTrackAuthoring(wb_.trackId);
            if (old && old->type() == TrackType::Audio
                && wb_.clipIndex < static_cast<int32_t>(old->clips.size())) {
                auto nt = cloneTrack(*old);
                nt->clips[wb_.clipIndex].warpCache = std::const_pointer_cast<const WarpCache>(wb_.cache);
                republishWithTrackRaw(wb_.trackId, nt);   // no undo entry — a cache isn't an edit
            }
            wb_ = {};   // clear the cursor (also frees the stretcher)
        }
    }
    return warpBuildRemainingFrames();
}

void Engine::setDeferWarpBuild(bool defer) { deferWarpBuild_ = defer; }

// Rebuild every warped clip's offline stretch cache for the given tempo + device rate.
// The stretched audio depends on both (spb sets the played length; the device rate sets
// the window sizes + src≠device pitch compensation), so a tempo or SR change must re-run
// the build — else warped clips play at the wrong length/pitch. Atomic republish (no
// undo): audio-thread safe. `spb` is passed explicitly so a tempo change can rebuild
// with the NEW value before the transport command that carries it has drained.
void Engine::reconfigureAllWarpStreams() {
    reconfigureAllWarpStreams(transport_.samplesPerBeat(), transport_.sampleRate());
}
void Engine::reconfigureAllWarpStreams(double spb, double devSR) {
    if (!authoring_) return;
    if (devSR <= 0.0 || spb <= 0.0) return;
    bool any = false;
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        bool warped = false;
        if (t->type() == TrackType::Audio)
            for (auto& c : t->clips) if (c.warpEnabled && c.sample) { warped = true; break; }
        if (!warped) { g->tracks.push_back(t); continue; }
        auto nt = cloneTrack(*t);
        for (auto& c : nt->clips) if (c.warpEnabled && c.sample) configureClipWarp(c, spb, devSR);
        g->tracks.push_back(std::move(nt));
        any = true;
    }
    if (any) publishRaw(std::move(g));
}

bool Engine::setClipPitch(int32_t trackId, int32_t clipIndex, float semitones) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    auto nt = cloneTrack(*old);
    nt->clips[clipIndex].pitchSemitones = semitones;
    configureClipWarp(nt->clips[clipIndex], transport_.samplesPerBeat(), transport_.sampleRate()); // pitch is baked into the warp
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setClipWarp(int32_t trackId, int32_t clipIndex, bool enabled, int32_t mode) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    // On enable, auto-detect the source tempo and snap the clip's length to the
    // beat grid so it conforms to the project BPM. If detection fails, fall back
    // to a length-neutral seed (toggling warp then leaves audio where it is).
    if (enabled && !c.warpEnabled && c.sample && spb > 0.0 && devSR > 0.0 && c.sample->sourceSampleRate > 0.0) {
        if (seedAutoWarp(c, spb, devSR) <= 0.0) seedNeutralWarp(c, spb, devSR);
    }
    c.warpEnabled = enabled;
    if (mode >= 0) c.warpMode = static_cast<WarpMode>(mode);
    configureClipWarp(c, spb, devSR);
    republishWithTrack(trackId, nt);
    return true;
}

double Engine::autoWarpClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return 0.0;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return 0.0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;
    const double bpm = seedAutoWarp(c, spb, devSR);
    if (bpm <= 0.0) return 0.0;   // detection failed: leave the clip unchanged
    c.warpEnabled = true;
    configureClipWarp(c, spb, devSR);
    republishWithTrack(trackId, nt);
    return bpm;
}

double Engine::beatWarpClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return 0.0;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return 0.0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;

    // Establish the beat grid from the detected tempo (rounded musical length).
    const int64_t srcLen = c.effectiveLength();
    const double srcSR = c.sample->sourceSampleRate;
    const double bpm = detectTempo(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen, srcSR);
    if (bpm <= 0.0 || srcLen <= 0) return 0.0;
    double beats = std::max(1.0, static_cast<double>(std::llround(srcLen / srcSR * bpm / 60.0)));
    const double srcPerBeat = static_cast<double>(srcLen) / beats;  // uniform source frames per beat

    // Detect transients and pin a marker at each — snapping its beat to the 1/16 grid —
    // so every hit locks to the grid ("Beats" warp). Segments between markers
    // stretch to fit, correcting drift a uniform stretch can't.
    double trans[256];
    const int32_t nt2 = detectTransients(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen,
                                         srcSR, trans, 256);
    std::vector<WarpMarker> markers;
    markers.push_back({ c.sourceOffsetFrames, 0.0 });
    const double grid = 0.25;  // 1/16 note in 4/4
    double prevBeat = 0.0;
    for (int32_t i = 0; i < nt2; ++i) {
        const double beatRaw = (trans[i] - c.sourceOffsetFrames) / srcPerBeat;
        const double snapped = std::round(beatRaw / grid) * grid;
        if (snapped <= prevBeat + 1e-6 || snapped >= beats - 1e-6) continue;  // keep beats strictly rising, inside (0,beats)
        markers.push_back({ trans[i], snapped });
        prevBeat = snapped;
    }
    markers.push_back({ c.sourceOffsetFrames + static_cast<double>(srcLen), beats });

    c.warpEnabled = true;
    c.warpMode = WarpMode::Beats;
    c.warpBeats = beats;
    c.warpMarkers = std::move(markers);
    c.warpPlayStart = 0.0; c.warpPlayEnd = 0.0;   // fresh warp → play the whole material
    configureClipWarp(c, spb, devSR);
    republishWithTrack(trackId, nt);
    return bpm;
}

bool Engine::setClipWarpLength(int32_t trackId, int32_t clipIndex, double beats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    if (beats < 0.25) beats = 0.25;
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    c.warpBeats = beats;
    // The end marker owns the total length; keep interior markers within it.
    if (!c.warpMarkers.empty()) {
        c.warpMarkers.back().beat = beats;
        for (auto& m : c.warpMarkers) if (m.beat > beats) m.beat = beats;
    }
    clampWarpPlay(c);   // keep the trim window valid for the new length
    configureClipWarp(c, transport_.samplesPerBeat(), transport_.sampleRate());
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setClipWarpMarkers(int32_t trackId, int32_t clipIndex,
                                const double* srcFrames, const double* beats, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    if (count < 2 || !srcFrames || !beats) return false;
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    c.warpMarkers.clear();
    c.warpMarkers.reserve(count);
    for (int32_t i = 0; i < count; ++i) c.warpMarkers.push_back({ srcFrames[i], beats[i] });
    std::sort(c.warpMarkers.begin(), c.warpMarkers.end(),
              [](const WarpMarker& a, const WarpMarker& b) { return a.srcFrame < b.srcFrame; });
    c.warpBeats = c.warpMarkers.back().beat;
    clampWarpPlay(c);   // preserve the trim window across marker edits (clamped to new length)
    configureClipWarp(c, transport_.samplesPerBeat(), transport_.sampleRate());
    republishWithTrack(trackId, nt);
    return true;
}

// Trim a warped clip's played window (clip Start/End over the warp). Markers
// and warpBeats are untouched; only the [playStart, playEnd] beat window moves. No-op
// on unwarped clips (their region is set via setClipSourceRegion).
bool Engine::setClipWarpTrim(int32_t trackId, int32_t clipIndex, double playStart, double playEnd) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    const AudioClip& oc = old->clips[clipIndex];
    if (!oc.warpEnabled || oc.warpBeats <= 0.0) return false;
    constexpr double kMinBeat = 0.05;
    double ps = std::clamp(playStart, 0.0, std::max(0.0, oc.warpBeats - kMinBeat));
    double pe = std::clamp(playEnd, ps + kMinBeat, oc.warpBeats);
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    c.warpPlayStart = ps;
    c.warpPlayEnd = pe;
    configureClipWarp(c, transport_.samplesPerBeat(), transport_.sampleRate()); // window is baked into the cache
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::clipWarpMarkers(int32_t trackId, int32_t clipIndex,
                                double* outSrc, double* outBeat, int32_t maxCount) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const auto& m = t->clips[clipIndex].warpMarkers;
    const int32_t n = std::min<int32_t>(maxCount, static_cast<int32_t>(m.size()));
    for (int32_t i = 0; i < n; ++i) { if (outSrc) outSrc[i] = m[i].srcFrame; if (outBeat) outBeat[i] = m[i].beat; }
    return static_cast<int32_t>(m.size());
}

int32_t Engine::addAudioClipEx(int32_t trackId, const std::string& path, double startBeat,
                               double sourceOffsetFrames, int64_t lengthFrames, float gain) {
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return -1;
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    auto nt = cloneTrack(*old);
    AudioClip clip;
    clip.sample = sample;
    clip.startBeat = startBeat;
    clip.sourceOffsetFrames = sourceOffsetFrames;
    clip.lengthFrames = lengthFrames;
    clip.gain = gain;
    nt->clips.push_back(clip);
    const int32_t idx = static_cast<int32_t>(nt->clips.size()) - 1;
    republishWithTrack(trackId, nt);
    return idx;
}

bool Engine::sampleInfo(int64_t sampleId, NotaSampleInfo* out) const {
    if (!out) return false;
    auto s = findSampleById(sampleId);
    if (!s) return false;
    out->channels = s->channels;
    out->frames = s->frames;
    out->sample_rate = s->sourceSampleRate;
    return true;
}

int64_t Engine::sampleRead(int64_t sampleId, float* out, int64_t cap) const {
    auto s = findSampleById(sampleId);
    if (!s) return 0;
    const int64_t total = static_cast<int64_t>(s->samples.size());
    if (out && cap > 0) {
        const int64_t n = cap < total ? cap : total;
        std::copy_n(s->samples.data(), n, out);
    }
    return total;
}

bool Engine::samplerInfo(int32_t trackId, NotaSamplerInfo* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    auto* smp = dynamic_cast<Sampler*>(t->instrument.get());
    if (!smp) return false;
    out->sample_id = smp->sample() ? smp->sample()->id : 0;
    out->root_note = smp->rootNote();
    out->loop = smp->loopEnabled() ? 1 : 0;
    return true;
}

bool Engine::grainInfo(int32_t trackId, NotaSamplerInfo* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    auto* gr = dynamic_cast<GrainSynth*>(t->instrument.get());
    if (!gr) return false;
    out->sample_id = gr->sample() ? gr->sample()->id : 0;
    out->root_note = gr->rootNote();
    out->loop = 0;
    return true;
}

// Nota Rhythm Phase 2: per-voice sample id + source (0 Synth, 1 Sample).
bool Engine::rhythmVoiceInfo(int32_t trackId, int32_t voice, NotaSamplerInfo* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    auto* rh = t ? dynamic_cast<RhythmMachine*>(t->instrument.get()) : nullptr;
    if (!rh) return false;
    out->sample_id = rh->voiceSampleId(voice);
    out->root_note = 60; out->loop = 0;
    return true;
}
int32_t Engine::rhythmVoiceSource(int32_t trackId, int32_t voice) const {
    auto t = findTrackAuthoring(trackId);
    auto* rh = t ? dynamic_cast<RhythmMachine*>(t->instrument.get()) : nullptr;
    return rh ? rh->voiceSource(voice) : 0;
}

int32_t Engine::grainPlayPositions(int32_t trackId, float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
    auto* gr = t ? dynamic_cast<GrainSynth*>(t->instrument.get()) : nullptr;
    return gr ? gr->playPositions(out, maxN) : 0;
}

int32_t Engine::instrumentVoiceCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->activeVoiceCount() : -1;
}

int32_t Engine::instrumentHeldNotes(int32_t trackId, int32_t* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->heldNotes(out, maxN) : 0;
}

int32_t Engine::instrumentScope(int32_t trackId, float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->scopeRead(out, maxN) : 0;
}

// --- structural edits: audio (M1) ------------------------------------------
int32_t Engine::addAudioTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    g->tracks.push_back(std::make_shared<Track>(id, TrackType::Audio));
    publish(g);
    return id;
}

int32_t Engine::addAudioClip(int32_t trackId, const std::string& path, double startBeat) {
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return -1;
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;

    auto nt = cloneTrack(*old);
    AudioClip clip; clip.sample = sample; clip.startBeat = startBeat;
    nt->clips.push_back(clip);
    const int32_t clipIndex = static_cast<int32_t>(nt->clips.size()) - 1;
    republishWithTrack(trackId, nt);
    return clipIndex;
}

void Engine::setTrackVolume(int32_t id, float v) { if (auto t = findTrackAuthoring(id)) t->setVolume(v); }
void Engine::setTrackPan(int32_t id, float p)    { if (auto t = findTrackAuthoring(id)) t->setPan(p); }
void Engine::setTrackMute(int32_t id, bool m)    { if (auto t = findTrackAuthoring(id)) t->setMute(m); }
void Engine::setTrackSolo(int32_t id, bool s)    { if (auto t = findTrackAuthoring(id)) t->setSolo(s); }
int32_t Engine::trackCount() const { return static_cast<int32_t>(authoring_->tracks.size()); }

// --- send/return buses (M6-1) ----------------------------------------------

int32_t Engine::returnTrackCount() const {
    int32_t n = 0;
    for (auto& t : authoring_->tracks) if (t->type() == TrackType::Return) ++n;
    return n;
}

int32_t Engine::addReturnTrack() {
    const int32_t idx = returnTrackCount();
    if (idx >= kMaxReturns) return -1;          // bus slots are fixed
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto rt = std::make_shared<Track>(id, TrackType::Return);
    rt->setReturnIndex(idx);
    g->tracks.push_back(rt);
    publish(g);
    return id;
}

void Engine::setTrackSend(int32_t id, int32_t bus, float level) {
    if (auto t = findTrackAuthoring(id)) t->setSend(bus, level);
}
float Engine::trackSend(int32_t id, int32_t bus) const {
    auto t = findTrackAuthoring(id);
    return t ? t->send(bus) : 0.0f;
}
int32_t Engine::trackReturnIndex(int32_t id) const {
    auto t = findTrackAuthoring(id);
    return t ? t->returnIndex() : -1;
}

// Peak-pick a warped clip's source envelope over a beat range [beat0, beat1] via the
// marker map (time-stretch preserves the amplitude envelope). Shared by the played
// window (getClipPeaks) and the full-material view (getClipWarpFullPeaks).
static int32_t warpPeaksRange(const AudioClip& clip, float* out, int32_t maxPoints,
                              double beat0, double beat1) {
    if (!out || maxPoints <= 0 || !clip.sample) return 0;
    const auto& m = clip.warpMarkers;
    if (m.size() < 2) return 0;
    const SampleBuffer& sb = *clip.sample;
    const int32_t buckets = std::min<int32_t>(maxPoints, 2048);
    const double span = beat1 - beat0;
    if (!(span > 0.0)) return 0;
    auto srcAtBeat = [&](double beat) -> double {
        if (beat <= m.front().beat) return m.front().srcFrame;
        if (beat >= m.back().beat)  return m.back().srcFrame;
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
        float mn = 1.0f, mx = -1.0f;
        for (int64_t f = s0; f <= s1; ++f) {
            float l, r; sb.readStereo(f, l, r);
            const float mid = 0.5f * (l + r);
            mn = std::min(mn, mid); mx = std::max(mx, mid);
        }
        if (mn > mx) { mn = mx = 0.0f; }
        out[b * 2] = mn; out[b * 2 + 1] = mx;
    }
    return buckets;
}

int32_t Engine::getClipPeaks(int32_t trackId, int32_t clipIndex,
                             float* outMinMax, int32_t maxPoints) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const AudioClip& clip = t->clips[clipIndex];
    if (!clip.sample) return 0;
    const SampleBuffer& sb = *clip.sample;

    // Warped clips: the waveform is drawn in the beat domain (0..warpBeats). With the
    // realtime streaming warp there is no stretched cache, so derive the envelope from
    // the SOURCE via the marker map (beat→source) — time-stretch preserves the
    // amplitude envelope, so peak-picking the mapped source window matches the warped
    // timeline (and dragged markers shift it correctly). Unwarped: straight source.
    const auto& m = clip.warpMarkers;
    const bool warped = clip.warpEnabled && m.size() >= 2 && clip.warpBeats > 0.0;

    if (warped)   // peaks over the PLAYED window (what the arrangement shows/plays)
        return warpPeaksRange(clip, outMinMax, maxPoints, clip.warpPlayStart, clip.warpPlayEndEff());

    const int64_t base = static_cast<int64_t>(clip.sourceOffsetFrames);
    const int64_t total = clip.effectiveLength();
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    for (int32_t b = 0; b < buckets; ++b) {
        int64_t begin = base + b * per;
        int64_t end   = begin + per;
        float mn = 1.0f, mx = -1.0f;
        for (int64_t f = begin; f < end; ++f) {
            float l, r; sb.readStereo(f, l, r);
            const float mid = 0.5f * (l + r);
            mn = std::min(mn, mid); mx = std::max(mx, mid);
        }
        outMinMax[b * 2] = mn; outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

// Peaks over the WHOLE sample (frames 0..sample->frames), regardless of the clip's
// trimmed region — the clip editor draws the full file so the Start/End brackets can
// reveal audio that was trimmed off. Unwarped only (warped uses getClipPeaks).
int32_t Engine::getClipSourcePeaks(int32_t trackId, int32_t clipIndex,
                                   float* outMinMax, int32_t maxPoints) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const AudioClip& clip = t->clips[clipIndex];
    if (!clip.sample) return 0;
    const SampleBuffer& sb = *clip.sample;
    const int64_t total = sb.frames;
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    for (int32_t b = 0; b < buckets; ++b) {
        int64_t begin = b * per;
        int64_t end   = begin + per;
        float mn = 1.0f, mx = -1.0f;
        for (int64_t f = begin; f < end; ++f) {
            float l, r; sb.readStereo(f, l, r);
            const float mid = 0.5f * (l + r);
            mn = std::min(mn, mid); mx = std::max(mx, mid);
        }
        if (mn > mx) { mn = mx = 0.0f; }
        outMinMax[b * 2] = mn; outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

// Peaks over the FULL warped material [0..warpBeats], ignoring the trim window — the
// clip editor draws the whole warp so the Start/End brackets can reveal trimmed beats.
int32_t Engine::getClipWarpFullPeaks(int32_t trackId, int32_t clipIndex,
                                     float* outMinMax, int32_t maxPoints) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const AudioClip& clip = t->clips[clipIndex];
    if (!(clip.warpEnabled && clip.warpBeats > 0.0)) return 0;
    return warpPeaksRange(clip, outMinMax, maxPoints, 0.0, clip.warpBeats);
}

// --- arrangement geometry (M4) ---------------------------------------------

bool Engine::trackInfo(int32_t index, NotaTrackInfo* out) const {
    if (!out || index < 0 || index >= static_cast<int32_t>(authoring_->tracks.size())) return false;
    const Track& t = *authoring_->tracks[index];
    const bool inst = t.type() == TrackType::Instrument;
    const bool ret  = t.type() == TrackType::Return;
    const bool grp  = t.type() == TrackType::Group;
    out->id = t.id();
    out->type = grp ? 3 : ret ? 2 : (inst ? 1 : 0);   // 0=audio,1=instrument,2=return,3=group
    out->clip_count = (ret || grp) ? 0
                          : (inst ? static_cast<int32_t>(t.midiClips.size())
                                  : static_cast<int32_t>(t.clips.size()));
    out->muted = t.mute() ? 1 : 0;
    out->soloed = t.solo() ? 1 : 0;
    out->armed = t.armed() ? 1 : 0;
    out->volume = t.volume();
    out->pan = t.pan();
    out->group_id = t.groupId();
    return true;
}

bool Engine::clipInfo(int32_t trackId, int32_t clipIndex, NotaClipInfo* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (t->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return false;
        const MidiClip& c = t->midiClips[clipIndex];
        out->start_beat = c.startBeat;
        out->length_beats = c.lengthBeats;
        out->kind = 1;
        out->active = c.active ? 1 : 0;
        return true;
    }
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return false;
    const AudioClip& c = t->clips[clipIndex];
    out->start_beat = c.startBeat;
    out->kind = 0;
    out->active = c.active ? 1 : 0;
    // Warped clips hold a fixed musical length = the trimmed play window; unwarped
    // length depends on tempo.
    if (c.warpEnabled && c.warpBeats > 0.0) { out->length_beats = c.warpPlayLen(); return true; }
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    if (c.sample && spb > 0.0 && devSR > 0.0 && c.sample->sourceSampleRate > 0.0) {
        const double devFrames = static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / c.pitchRatio();
        out->length_beats = devFrames / spb;
    } else {
        out->length_beats = 0.0;
    }
    return true;
}

// --- clip editing (M4-2) ---------------------------------------------------
namespace {
// Audio isn't warped: convert between musical beats and *source* sample frames
// at the current tempo/sample-rate.
double audioBeatsToSrcFrames(const AudioClip& c, double beats, double spb, double devSR) {
    if (!c.sample || spb <= 0 || devSR <= 0 || c.sample->sourceSampleRate <= 0) return 0.0;
    return beats * spb * c.sample->sourceSampleRate / devSR * c.pitchRatio();
}
double audioLenBeats(const AudioClip& c, double spb, double devSR) {
    if (!c.sample || spb <= 0 || devSR <= 0 || c.sample->sourceSampleRate <= 0) return 0.0;
    const double devFrames = static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / c.pitchRatio();
    return devFrames / spb;
}
// Timeline span (beats) of an audio clip as shown/played: warp uses the play window,
// unwarped uses the source region.
double audioDisplayLenBeats(const AudioClip& c, double spb, double devSR) {
    if (c.warpEnabled && c.warpMarkers.size() >= 2 && c.warpBeats > 0.0) return c.warpPlayLen();
    return audioLenBeats(c, spb, devSR);
}
// Push `start` right until [start, start+len) overlaps no existing clip on the track.
double placeMidiNonOverlap(const Track& t, double start, double len) {
    bool moved = true;
    while (moved) {
        moved = false;
        for (const auto& c : t.midiClips) {
            const double cs = c.startBeat, ce = cs + c.lengthBeats;
            if (start < ce && start + len > cs) { start = ce; moved = true; }
        }
    }
    return start;
}
double placeAudioNonOverlap(const Track& t, double start, double len, double spb, double devSR) {
    bool moved = true;
    while (moved) {
        moved = false;
        for (const auto& c : t.clips) {
            const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
            if (start < ce && start + len > cs) { start = ce; moved = true; }
        }
    }
    return start;
}

// --- overwrite / comp: carve existing clips out of a timeline range ---------
// Replace-on-drop: a newly placed clip (recorded / dragged) replaces the region it covers.
// The parts of an existing clip outside the range survive as trimmed remainders.
MidiClip midiCarveLeft(const MidiClip& src, double endLocal) {   // keep [start, start+endLocal)
    MidiClip r; r.name = src.name; r.startBeat = src.startBeat; r.lengthBeats = endLocal;
    for (const auto& n : src.notes) if (n.startBeat < endLocal) r.notes.push_back(n);
    return r;
}
MidiClip midiCarveRight(const MidiClip& src, double startLocal) {   // keep [start+startLocal, end)
    MidiClip r; r.name = src.name; r.startBeat = src.startBeat + startLocal; r.lengthBeats = src.lengthBeats - startLocal;
    for (const auto& n : src.notes) if (n.startBeat >= startLocal) { Note m = n; m.startBeat -= startLocal; r.notes.push_back(m); }
    return r;
}
AudioClip audioCarveLeft(const AudioClip& src, double endLocal, double spb, double devSR) {
    AudioClip left = src;
    if (src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0) {
        left.warpPlayEnd = src.warpPlayStart + endLocal; buildWarpCache(left, spb, devSR);
    } else {
        left.lengthFrames = static_cast<int64_t>(audioBeatsToSrcFrames(src, endLocal, spb, devSR));
    }
    return left;
}
AudioClip audioCarveRight(const AudioClip& src, double startLocal, double spb, double devSR) {
    AudioClip right = src; right.startBeat = src.startBeat + startLocal;
    if (src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0) {
        right.warpPlayStart = src.warpPlayStart + startLocal; right.warpPlayEnd = src.warpPlayEndEff();
        buildWarpCache(right, spb, devSR);
    } else {
        const int64_t cut = static_cast<int64_t>(audioBeatsToSrcFrames(src, startLocal, spb, devSR));
        right.sourceOffsetFrames = src.sourceOffsetFrames + static_cast<double>(cut);
        right.lengthFrames = src.effectiveLength() - cut;
    }
    return right;
}
void midiOverwriteRange(std::vector<MidiClip>& clips, double ns, double ne) {
    std::vector<MidiClip> out; out.reserve(clips.size() + 2);
    constexpr double eps = 1e-6;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + c.lengthBeats;
        if (ce <= ns + eps || cs >= ne - eps) { out.push_back(c); continue; }   // no overlap
        if (ns > cs + eps) out.push_back(midiCarveLeft(c, ns - cs));             // left remainder
        if (ne < ce - eps) out.push_back(midiCarveRight(c, ne - cs));            // right remainder
    }
    clips.swap(out);
}
void audioOverwriteRange(std::vector<AudioClip>& clips, double ns, double ne, double spb, double devSR) {
    std::vector<AudioClip> out; out.reserve(clips.size() + 2);
    constexpr double eps = 1e-6;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
        if (ce <= ns + eps || cs >= ne - eps) { out.push_back(c); continue; }
        if (ns > cs + eps) out.push_back(audioCarveLeft(c, ns - cs, spb, devSR));
        if (ne < ce - eps) out.push_back(audioCarveRight(c, ne - cs, spb, devSR));
    }
    clips.swap(out);
}
// A clip clipped to the timeline range [ns,ne] (its intersection), timeline positions
// preserved — used to lift the covered slice for a range duplicate.
MidiClip midiSlice(const MidiClip& c, double ns, double ne) {
    const double cs = c.startBeat, ce = cs + c.lengthBeats;
    const double a = std::max(cs, ns), b = std::min(ce, ne);
    return midiCarveLeft(midiCarveRight(c, a - cs), b - a);
}
AudioClip audioSlice(const AudioClip& c, double ns, double ne, double spb, double devSR) {
    const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
    const double a = std::max(cs, ns), b = std::min(ce, ne);
    return audioCarveLeft(audioCarveRight(c, a - cs, spb, devSR), b - a, spb, devSR);
}
} // namespace

void Engine::overwriteAudioClipsInRange(std::vector<AudioClip>& clips, double ns, double ne) {
    audioOverwriteRange(clips, ns, ne, transport_.samplesPerBeat(), transport_.sampleRate());
}

bool Engine::moveClip(int32_t trackId, int32_t clipIndex, double newStartBeat) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    lastMoveKeptDeviceAuto_ = false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    // The dragged clip overwrites whatever it lands on: pull it out, carve its new range
    // from the remaining clips, then drop it back on top.
    double oldStart = 0, len = 0;   // for automation follow
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        MidiClip moved = nt->midiClips[clipIndex]; oldStart = moved.startBeat; len = moved.lengthBeats;
        moved.startBeat = newStartBeat;
        nt->midiClips.erase(nt->midiClips.begin() + clipIndex);
        midiOverwriteRange(nt->midiClips, newStartBeat, newStartBeat + moved.lengthBeats);
        nt->midiClips.push_back(moved);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        AudioClip moved = nt->clips[clipIndex]; oldStart = moved.startBeat;
        moved.startBeat = newStartBeat;
        nt->clips.erase(nt->clips.begin() + clipIndex);
        len = audioDisplayLenBeats(moved, spb, devSR);
        audioOverwriteRange(nt->clips, newStartBeat, newStartBeat + len, spb, devSR);
        nt->clips.push_back(moved);
    }
    // Automation follows the clip within its span (req 8.3.1), unless envelopes are locked.
    if (!automationLock_ && len > 1e-6 && std::abs(newStartBeat - oldStart) > 1e-9) {
        auto snips = captureClipAutomation(*nt, oldStart, oldStart + len);
        removeAutomationInRange(*nt, oldStart, oldStart + len);
        applyClipAutomation(*nt, trackId, snips, newStartBeat, len, /*sameTrack*/ true);
    }
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::moveClipToTrack(int32_t srcTrackId, int32_t clipIndex, int32_t sourceTrackId, double newStartBeat) {
    if (srcTrackId == sourceTrackId) return moveClip(srcTrackId, clipIndex, newStartBeat);
    auto src = findTrackAuthoring(srcTrackId);
    auto dst = findTrackAuthoring(sourceTrackId);
    if (!src || !dst) return false;
    // Same-type only: instrument→instrument (MIDI) or audio→audio; never a return.
    if (src->type() != dst->type() || src->type() == TrackType::Return) return false;
    if (newStartBeat < 0) newStartBeat = 0;

    lastMoveKeptDeviceAuto_ = false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    auto nsrc = cloneTrack(*src);
    auto ndst = cloneTrack(*dst);
    double oldStart = 0, len = 0;   // for automation follow
    // The moved clip overwrites what it lands on at the destination.
    if (src->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nsrc->midiClips.size())) return false;
        MidiClip clip = nsrc->midiClips[clipIndex];   // copy before erasing
        oldStart = clip.startBeat; len = clip.lengthBeats;
        clip.startBeat = newStartBeat;
        nsrc->midiClips.erase(nsrc->midiClips.begin() + clipIndex);
        midiOverwriteRange(ndst->midiClips, newStartBeat, newStartBeat + clip.lengthBeats);
        ndst->midiClips.push_back(clip);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nsrc->clips.size())) return false;
        AudioClip clip = nsrc->clips[clipIndex];      // copy before erasing
        oldStart = clip.startBeat;
        clip.startBeat = newStartBeat;
        nsrc->clips.erase(nsrc->clips.begin() + clipIndex);
        len = audioDisplayLenBeats(clip, spb, devSR);
        audioOverwriteRange(ndst->clips, newStartBeat, newStartBeat + len, spb, devSR);
        ndst->clips.push_back(clip);
    }

    // Automation follow across tracks (req 8.3.1/8.3.4). Identical device layout → everything
    // follows, exactly like a same-track move. Otherwise only the common params (Volume/Pan)
    // move and device/plugin automation stays on the source (its indices are positional); we
    // flag that so the UI can tell the user.
    if (!automationLock_ && len > 1e-6) {
        auto snips = captureClipAutomation(*nsrc, oldStart, oldStart + len);
        if (tracksDeviceLayoutMatch(*nsrc, *ndst)) {
            removeAutomationInRange(*nsrc, oldStart, oldStart + len);
            applyClipAutomation(*ndst, sourceTrackId, snips, newStartBeat, len, /*sameTrack*/ true);
        } else {
            for (const auto& s : snips)
                if (s.target != AutomationTarget::Volume && s.target != AutomationTarget::Pan) { lastMoveKeptDeviceAuto_ = true; break; }
            // Clear only Volume/Pan from the source span; leave device/plugin points in place.
            constexpr double eps = 1e-6;
            for (auto& lane : nsrc->automation) {
                if (lane.target != AutomationTarget::Volume && lane.target != AutomationTarget::Pan) continue;
                auto& pts = lane.points;
                pts.erase(std::remove_if(pts.begin(), pts.end(),
                            [&](const AutomationPoint& p){ return p.beat >= oldStart - eps && p.beat <= oldStart + len + eps; }),
                          pts.end());
            }
            applyClipAutomation(*ndst, sourceTrackId, snips, newStartBeat, len, /*sameTrack*/ false);
        }
    }

    // Atomic two-track swap in a single publish (one undo checkpoint).
    pushUndo();
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        if (t->id() == srcTrackId)       g->tracks.push_back(nsrc);
        else if (t->id() == sourceTrackId) g->tracks.push_back(ndst);
        else                             g->tracks.push_back(t);
    }
    publishRaw(std::move(g));
    return true;
}

// --- time-range clip ops (arrangement time-selection) -----------------------
namespace {
bool rangeTouchesTrack(const Track& t, double s, double e, double spb, double devSR) {
    constexpr double eps = 1e-6;
    if (t.type() == TrackType::Instrument) {
        for (const auto& c : t.midiClips) { double cs = c.startBeat, ce = cs + c.lengthBeats; if (ce > s + eps && cs < e - eps) return true; }
    } else {
        for (const auto& c : t.clips) { double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR); if (ce > s + eps && cs < e - eps) return true; }
    }
    return false;
}
} // namespace

bool Engine::deleteClipsInRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (end <= start + 1e-9 || trackIds.empty()) return false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    auto listed = [&](int32_t id){ return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    bool any = false;
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        if (t->type() != TrackType::Return && listed(t->id()) && rangeTouchesTrack(*t, start, end, spb, devSR)) {
            auto nt = cloneTrack(*t);
            if (nt->type() == TrackType::Instrument) midiOverwriteRange(nt->midiClips, start, end);
            else audioOverwriteRange(nt->clips, start, end, spb, devSR);
            g->tracks.push_back(nt);
            any = true;
        } else g->tracks.push_back(t);
    }
    if (!any) return false;
    pushUndo();
    publishRaw(std::move(g));
    return true;
}

// Cut (not delete) every listed track's clips at both `start` and `end`, so the covered
// slice becomes standalone clip(s). Any clip crossing a boundary is replaced by its
// consecutive slices; clips clear of both boundaries are untouched. One undo step.
bool Engine::splitClipsAtRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (end <= start + 1e-9 || trackIds.empty()) return false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    constexpr double eps = 1e-6;
    auto listed = [&](int32_t id){ return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    bool any = false;
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        if (t->type() == TrackType::Return || !listed(t->id()) || !rangeTouchesTrack(*t, start, end, spb, devSR))
        { g->tracks.push_back(t); continue; }
        auto nt = cloneTrack(*t);
        bool split = false;
        if (nt->type() == TrackType::Instrument) {
            std::vector<MidiClip> out; out.reserve(nt->midiClips.size() + 2);
            for (const auto& c : nt->midiClips) {
                const double cs = c.startBeat, ce = cs + c.lengthBeats;
                int nc = 0; double cuts[2];
                if (start > cs + eps && start < ce - eps) cuts[nc++] = start;
                if (end   > cs + eps && end   < ce - eps) cuts[nc++] = end;
                if (nc == 0) { out.push_back(c); continue; }
                double a = cs;
                for (int i = 0; i < nc; ++i) { out.push_back(midiSlice(c, a, cuts[i])); a = cuts[i]; }
                out.push_back(midiSlice(c, a, ce));
                split = true;
            }
            if (split) nt->midiClips.swap(out);
        } else {
            std::vector<AudioClip> out; out.reserve(nt->clips.size() + 2);
            for (const auto& c : nt->clips) {
                const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
                int nc = 0; double cuts[2];
                if (start > cs + eps && start < ce - eps) cuts[nc++] = start;
                if (end   > cs + eps && end   < ce - eps) cuts[nc++] = end;
                if (nc == 0) { out.push_back(c); continue; }
                double a = cs;
                for (int i = 0; i < nc; ++i) { out.push_back(audioSlice(c, a, cuts[i], spb, devSR)); a = cuts[i]; }
                out.push_back(audioSlice(c, a, ce, spb, devSR));
                split = true;
            }
            if (split) nt->clips.swap(out);
        }
        if (split) { g->tracks.push_back(nt); any = true; } else g->tracks.push_back(t);
    }
    if (!any) return false;
    pushUndo();
    publishRaw(std::move(g));
    return true;
}

double Engine::duplicateRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (end <= start + 1e-9 || trackIds.empty()) return -1.0;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    const double len = end - start;
    constexpr double eps = 1e-6;
    auto listed = [&](int32_t id){ return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    bool any = false;
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        if (t->type() == TrackType::Return || !listed(t->id()) || !rangeTouchesTrack(*t, start, end, spb, devSR))
        { g->tracks.push_back(t); continue; }
        auto nt = cloneTrack(*t);
        if (nt->type() == TrackType::Instrument) {
            std::vector<MidiClip> slices;
            for (auto& c : nt->midiClips) { double cs = c.startBeat, ce = cs + c.lengthBeats;
                if (ce > start + eps && cs < end - eps) { auto s = midiSlice(c, start, end); s.startBeat += len; slices.push_back(std::move(s)); } }
            midiOverwriteRange(nt->midiClips, end, end + len);   // the copy overwrites what it lands on (2.8)
            for (auto& s : slices) nt->midiClips.push_back(std::move(s));
        } else {
            std::vector<AudioClip> slices;
            for (auto& c : nt->clips) { double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
                if (ce > start + eps && cs < end - eps) { auto s = audioSlice(c, start, end, spb, devSR); s.startBeat += len; slices.push_back(std::move(s)); } }
            audioOverwriteRange(nt->clips, end, end + len, spb, devSR);
            for (auto& s : slices) nt->clips.push_back(std::move(s));
        }
        g->tracks.push_back(nt);
        any = true;
    }
    if (!any) return -1.0;
    pushUndo();
    publishRaw(std::move(g));
    return len;
}

bool Engine::trimClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    if (newLengthBeats < 0.25) newLengthBeats = 0.25;
    auto nt = cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        MidiClip& c = nt->midiClips[clipIndex];
        const double delta = newStartBeat - c.startBeat;  // shift notes to stay put
        for (auto& n : c.notes) n.startBeat -= delta;
        c.startBeat = newStartBeat;
        c.lengthBeats = newLengthBeats;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        AudioClip& c = nt->clips[clipIndex];
        const double spb = transport_.samplesPerBeat();
        const double devSR = transport_.sampleRate();
        const double delta = newStartBeat - c.startBeat;
        double newOffset = c.sourceOffsetFrames + audioBeatsToSrcFrames(c, delta, spb, devSR);
        if (newOffset < 0) newOffset = 0;
        c.startBeat = newStartBeat;
        c.sourceOffsetFrames = newOffset;
        c.lengthFrames = static_cast<int64_t>(audioBeatsToSrcFrames(c, newLengthBeats, spb, devSR));
    }
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::resizeAudioClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    if (newLengthBeats < 0.25) newLengthBeats = 0.25;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];

    if (c.warpEnabled) {
        // Warped: TRIM the played window — do NOT stretch (stretch is the
        // markers / BPM chip). Markers, warpBeats and the stream are untouched; we only
        // move the [warpPlayStart, warpPlayEnd] window and the timeline start. Dragging
        // the left edge shifts playStart (and startBeat); the right edge moves playEnd.
        constexpr double kMinBeat = 0.05;
        const double d = newStartBeat - c.startBeat;
        double ps = std::clamp(c.warpPlayStart + d, 0.0, std::max(0.0, c.warpBeats - kMinBeat));
        double pe = std::clamp(ps + newLengthBeats, ps + kMinBeat, c.warpBeats);
        c.startBeat = newStartBeat;
        c.warpPlayStart = ps;
        c.warpPlayEnd = pe;
        configureClipWarp(c, spb, devSR);   // the played window is baked into the cache
        republishWithTrack(trackId, nt);
        return true;
    }

    // Unwarped: trim within the source; if the drag asks for more than the source
    // can provide, auto-enable warp and stretch the current content to fit (this is
    // what lets a short one-shot be dragged out to a whole bar).
    const double delta = newStartBeat - c.startBeat;
    double newOffset = c.sourceOffsetFrames + audioBeatsToSrcFrames(c, delta, spb, devSR);
    if (newOffset < 0) newOffset = 0;
    c.startBeat = newStartBeat;
    c.sourceOffsetFrames = newOffset;
    const int64_t wantFrames = static_cast<int64_t>(audioBeatsToSrcFrames(c, newLengthBeats, spb, devSR));
    const int64_t srcAvail = c.sample ? (c.sample->frames - static_cast<int64_t>(newOffset)) : 0;
    if (!c.sample || wantFrames <= srcAvail) {
        c.lengthFrames = wantFrames;   // plain trim (reveal/hide source)
    } else {
        // Stretch the current content (from the new offset to end of source) to the
        // requested musical length; two end markers = uniform stretch you can refine.
        c.lengthFrames = srcAvail;
        c.warpEnabled = true;
        c.warpBeats = newLengthBeats;
        c.warpMarkers = { {static_cast<double>(newOffset), 0.0},
                          {static_cast<double>(newOffset) + static_cast<double>(c.effectiveLength()), newLengthBeats} };
        configureClipWarp(c, spb, devSR);
    }
    republishWithTrack(trackId, nt);
    return true;
}

// Set the clip's source region directly (clip Start/End markers): which part
// of the sample plays. The timeline position (startBeat) is unchanged — only the read
// offset and length move. Unwarped audio clips only (warped regions are governed by
// warp markers). Frame-precise; clamped to the sample bounds with a small minimum.
bool Engine::setClipSourceRegion(int32_t trackId, int32_t clipIndex,
                                 double offsetFrames, int64_t lengthFrames) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    const AudioClip& oc = old->clips[clipIndex];
    if (!oc.sample || oc.warpEnabled) return false;   // warped: region is marker-driven
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
    auto nt = cloneTrack(*old);
    AudioClip& c = nt->clips[clipIndex];
    c.sourceOffsetFrames = off;
    c.lengthFrames = len;
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::splitClip(int32_t trackId, int32_t clipIndex, double atBeat) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    auto nt = cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return -1;
        MidiClip src = nt->midiClips[clipIndex];
        const double local = atBeat - src.startBeat;
        if (local <= 0 || local >= src.lengthBeats) return -1;
        MidiClip left, right;
        left.startBeat = src.startBeat; left.lengthBeats = local;
        right.startBeat = atBeat;       right.lengthBeats = src.lengthBeats - local;
        for (const auto& n : src.notes) {
            if (n.startBeat < local) left.notes.push_back(n);
            else { Note m = n; m.startBeat -= local; right.notes.push_back(m); }
        }
        nt->midiClips[clipIndex] = left;
        nt->midiClips.insert(nt->midiClips.begin() + clipIndex + 1, right);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return -1;
        const double spb = transport_.samplesPerBeat();
        const double devSR = transport_.sampleRate();
        AudioClip src = nt->clips[clipIndex];
        const double local = atBeat - src.startBeat;
        const bool warped = src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0;
        if (warped) {
            // Warped clips play the beat window [warpPlayStart, warpPlayEndEff()] — split
            // in that domain (lengthFrames/sourceOffsetFrames are ignored when warped, so
            // editing them left both halves showing/playing the whole material).
            const double playLen = src.warpPlayLen();
            if (local <= 0.0 || local >= playLen) return -1;
            const double splitPlay = src.warpPlayStart + local;   // warp-material beat of the cut
            AudioClip left = src, right = src;
            left.warpPlayEnd = splitPlay;                         // concrete end (> start)
            right.startBeat = atBeat;
            right.warpPlayStart = splitPlay;
            right.warpPlayEnd = src.warpPlayEndEff();
            // Each half needs its own streaming stretcher (seek state is per-clip).
            configureClipWarp(left, spb, devSR);
            configureClipWarp(right, spb, devSR);
            nt->clips[clipIndex] = left;
            nt->clips.insert(nt->clips.begin() + clipIndex + 1, right);
        } else {
            const double lenBeats = audioLenBeats(src, spb, devSR);
            if (local <= 0 || local >= lenBeats) return -1;
            const int64_t splitSrc = static_cast<int64_t>(audioBeatsToSrcFrames(src, local, spb, devSR));
            const int64_t total = src.effectiveLength();
            AudioClip left = src, right = src;
            left.lengthFrames = splitSrc;
            right.startBeat = atBeat;
            right.sourceOffsetFrames = src.sourceOffsetFrames + static_cast<double>(splitSrc);
            right.lengthFrames = total - splitSrc;
            nt->clips[clipIndex] = left;
            nt->clips.insert(nt->clips.begin() + clipIndex + 1, right);
        }
    }
    republishWithTrack(trackId, nt);
    return clipIndex + 1;
}

int32_t Engine::duplicateClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return -1;
        MidiClip d = nt->midiClips[clipIndex];
        const double srcStart = d.startBeat, len = d.lengthBeats;
        auto snips = captureClipAutomation(*nt, srcStart, srcStart + len);
        // Place right after the source, then shift past any further clip so it never overlaps.
        d.startBeat = placeMidiNonOverlap(*nt, d.startBeat + d.lengthBeats, d.lengthBeats);
        nt->midiClips.insert(nt->midiClips.begin() + clipIndex + 1, d);
        applyClipAutomation(*nt, trackId, snips, d.startBeat, len, /*sameTrack*/ true);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return -1;
        AudioClip d = nt->clips[clipIndex];
        const double len = audioDisplayLenBeats(d, spb, devSR);
        const double srcStart = d.startBeat;
        auto snips = captureClipAutomation(*nt, srcStart, srcStart + len);
        d.startBeat = placeAudioNonOverlap(*nt, d.startBeat + len, len, spb, devSR);
        if (d.warpEnabled) configureClipWarp(d, spb, devSR);   // the copy needs its own stretcher
        nt->clips.insert(nt->clips.begin() + clipIndex + 1, d);
        applyClipAutomation(*nt, trackId, snips, d.startBeat, len, /*sameTrack*/ true);
    }
    republishWithTrack(trackId, nt);
    return clipIndex + 1;
}

// --- clip clipboard (copy/cut/paste, incl. cross-track) ---------------------

// Pull the points of every automation lane that fall in [start,end] out into a
// per-lane snippet, with beats rebased to be relative to `start`. Empty lanes are
// dropped so paste only touches targets the clip actually carried.
std::vector<AutomationLane> Engine::captureClipAutomation(const Track& t, double start, double end) const {
    constexpr double eps = 1e-6;
    std::vector<AutomationLane> out;
    for (const auto& lane : t.automation) {
        AutomationLane snip;
        snip.target      = lane.target;
        snip.deviceIndex = lane.deviceIndex;
        snip.paramIndex  = lane.paramIndex;
        snip.paramId     = lane.paramId;
        for (const auto& p : lane.points)
            if (p.beat >= start - eps && p.beat <= end + eps) {
                AutomationPoint q = p;
                q.beat = p.beat - start;   // clip-relative
                snip.points.push_back(q);
            }
        if (!snip.points.empty()) out.push_back(std::move(snip));
    }
    return out;
}

// Drop every automation point in [start,end] across all of `t`'s lanes (Cut, and the
// pre-clear before a paste re-lands its points).
void Engine::removeAutomationInRange(Track& t, double start, double end) {
    constexpr double eps = 1e-6;
    for (auto& lane : t.automation) {
        auto& pts = lane.points;
        pts.erase(std::remove_if(pts.begin(), pts.end(),
                    [&](const AutomationPoint& p){ return p.beat >= start - eps && p.beat <= end + eps; }),
                  pts.end());
    }
}

bool Engine::tracksDeviceLayoutMatch(const Track& a, const Track& b) {
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
// the pasted envelope replaces (not overlaps) whatever was there. Device/plugin/MIDI
// lanes only apply when sameTrack — their indices are positional, so a cross-track
// paste would otherwise bind them to the wrong device.
void Engine::applyClipAutomation(Track& nt, int32_t trackId, const std::vector<AutomationLane>& snips,
                                 double placedStart, double len, bool sameTrack) const {
    const double s = placedStart, e = placedStart + len;
    for (const auto& snip : snips) {
        if (!sameTrack && snip.target != AutomationTarget::Volume && snip.target != AutomationTarget::Pan)
            continue;
        // Find the matching lane on nt, or create one bound to the same target.
        AutomationLane* lane = nullptr;
        for (auto& l : nt.automation) {
            if (l.target != snip.target) continue;
            bool match = true;
            switch (snip.target) {
                case AutomationTarget::DeviceParam:
                case AutomationTarget::MidiDeviceParam:
                    match = l.deviceIndex == snip.deviceIndex && l.paramIndex == snip.paramIndex; break;
                case AutomationTarget::PluginParam:
                    match = l.deviceIndex == snip.deviceIndex && l.paramId == snip.paramId; break;
                default: break;   // Volume / Pan: single lane
            }
            if (match) { lane = &l; break; }
        }
        if (!lane) {
            AutomationLane nl;
            nl.target      = snip.target;
            nl.deviceIndex = snip.deviceIndex;
            nl.paramId     = snip.paramId;
            nl.paramIndex  = snip.target == AutomationTarget::PluginParam
                               ? pluginParamIndexOfId(trackId, snip.deviceIndex, snip.paramId)
                               : snip.paramIndex;
            nt.automation.push_back(std::move(nl));
            lane = &nt.automation.back();
        }
        auto& pts = lane->points;
        constexpr double eps = 1e-6;
        pts.erase(std::remove_if(pts.begin(), pts.end(),
                    [&](const AutomationPoint& p){ return p.beat >= s - eps && p.beat <= e + eps; }),
                  pts.end());
        for (const auto& p : snip.points) {
            AutomationPoint q = p;
            q.beat = placedStart + p.beat;
            pts.push_back(q);
        }
        std::sort(pts.begin(), pts.end(),
                  [](const AutomationPoint& a, const AutomationPoint& b){ return a.beat < b.beat; });
    }
}

bool Engine::copyClip(int32_t trackId, int32_t clipIndex) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    double start = 0.0, len = 0.0;
    if (t->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return false;
        clipboardMidi_ = t->midiClips[clipIndex];
        clipboardKind_ = 1;
        start = clipboardMidi_.startBeat; len = clipboardMidi_.lengthBeats;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return false;
        clipboardAudio_ = t->clips[clipIndex];
        clipboardAudio_.warpCache = nullptr;   // paste rebuilds the cache
        clipboardKind_ = 0;
        start = clipboardAudio_.startBeat; len = audioDisplayLenBeats(clipboardAudio_, spb, devSR);
    }
    clipboardAuto_ = captureClipAutomation(*t, start, start + len);
    clipboardAutoTrack_ = trackId;
    return true;
}

bool Engine::cutClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    double start = 0.0, len = 0.0;
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        clipboardMidi_ = nt->midiClips[clipIndex];
        clipboardKind_ = 1;
        start = clipboardMidi_.startBeat; len = clipboardMidi_.lengthBeats;
        nt->midiClips.erase(nt->midiClips.begin() + clipIndex);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        clipboardAudio_ = nt->clips[clipIndex];
        clipboardAudio_.warpCache = nullptr;
        clipboardKind_ = 0;
        start = clipboardAudio_.startBeat; len = audioDisplayLenBeats(clipboardAudio_, spb, devSR);
        nt->clips.erase(nt->clips.begin() + clipIndex);
    }
    clipboardAuto_ = captureClipAutomation(*nt, start, start + len);
    clipboardAutoTrack_ = trackId;
    removeAutomationInRange(*nt, start, start + len);   // Cut carries the automation away
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::clipboardClipKind() const { return clipboardKind_; }

int32_t Engine::pasteClip(int32_t sourceTrackId, double atBeat) {
    if (clipboardKind_ < 0) return -1;
    auto old = findTrackAuthoring(sourceTrackId);
    if (!old) return -1;
    const bool destInstrument = old->type() == TrackType::Instrument;
    if (clipboardKind_ == 1 && !destInstrument) return -1;               // MIDI clip → instrument track
    if (clipboardKind_ == 0 && old->type() != TrackType::Audio) return -1; // audio clip → audio track
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    double start = std::max(0.0, atBeat);
    auto nt = cloneTrack(*old);
    const bool sameTrack = sourceTrackId == clipboardAutoTrack_;
    if (clipboardKind_ == 1) {
        MidiClip m = clipboardMidi_;
        m.startBeat = placeMidiNonOverlap(*nt, start, m.lengthBeats);
        nt->midiClips.push_back(m);
        applyClipAutomation(*nt, sourceTrackId, clipboardAuto_, m.startBeat, m.lengthBeats, sameTrack);
        republishWithTrack(sourceTrackId, nt);
        return static_cast<int32_t>(nt->midiClips.size()) - 1;
    }
    AudioClip a = clipboardAudio_;
    const double len = audioDisplayLenBeats(a, spb, devSR);
    a.startBeat = placeAudioNonOverlap(*nt, start, len, spb, devSR);
    if (a.warpEnabled) configureClipWarp(a, spb, devSR);
    nt->clips.push_back(a);
    applyClipAutomation(*nt, sourceTrackId, clipboardAuto_, a.startBeat, len, sameTrack);
    republishWithTrack(sourceTrackId, nt);
    return static_cast<int32_t>(nt->clips.size()) - 1;
}

// --- block clip clipboard (multi-selection copy/cut/paste + duplicate) ------

// Pull the selected clips (+ their automation) out into BlockClip entries with beats
// rebased to the block start. blockLen returns the block's total span (max end - min start).
std::vector<Engine::BlockClip> Engine::captureBlock(
        const std::vector<std::pair<int32_t,int32_t>>& sel, double& blockLen) const {
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    std::vector<BlockClip> items;
    double minS = std::numeric_limits<double>::max(), maxE = -std::numeric_limits<double>::max();
    for (const auto& [tid, ci] : sel) {
        auto t = findTrackAuthoring(tid);
        if (!t) continue;
        BlockClip b; b.trackId = tid;
        double s, e;
        if (t->type() == TrackType::Instrument) {
            if (ci < 0 || ci >= static_cast<int32_t>(t->midiClips.size())) continue;
            b.kind = 1; b.midi = t->midiClips[ci];
            s = b.midi.startBeat; b.len = b.midi.lengthBeats;
        } else {
            if (ci < 0 || ci >= static_cast<int32_t>(t->clips.size())) continue;
            b.kind = 0; b.audio = t->clips[ci]; b.audio.warpCache = nullptr;  // copy rebuilds the cache
            s = b.audio.startBeat; b.len = audioDisplayLenBeats(b.audio, spb, devSR);
        }
        e = s + b.len;
        b.relStart = s;                                   // absolute for now; rebased below
        b.autom = captureClipAutomation(*t, s, e);
        minS = std::min(minS, s); maxE = std::max(maxE, e);
        items.push_back(std::move(b));
    }
    if (items.empty()) { blockLen = 0.0; return items; }
    blockLen = maxE - minS;
    for (auto& b : items) b.relStart -= minS;             // rebase to the block start
    return items;
}

// Re-land a captured block at placedStart onto the same tracks, one shared non-overlap
// shift for the whole block (so relative geometry is preserved), publishing every touched
// track in a single undo step. Records the placed clips in lastPlaced_.
void Engine::placeBlock(const std::vector<BlockClip>& items, double placedStart) {
    lastPlaced_.clear();
    if (items.empty() || !authoring_) return;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    placedStart = std::max(0.0, placedStart);

    // One clone per touched track (multiple clips on a track accumulate into it). Clones
    // start from the current authoring track so the shared shift sees existing clips.
    std::map<int32_t, std::shared_ptr<Track>> clones;
    for (const auto& b : items) {
        if (clones.count(b.trackId)) continue;
        auto old = findTrackAuthoring(b.trackId);
        if (old) clones[b.trackId] = cloneTrack(*old);
    }

    // Find the minimal shared shift d >= 0 so no clip in the block overlaps an existing
    // clip on its track. All clips move together by d, so relative positions never change.
    double d = 0.0;
    bool moved = true;
    while (moved) {
        moved = false;
        for (const auto& b : items) {
            auto it = clones.find(b.trackId);
            if (it == clones.end()) continue;
            const Track& t = *it->second;
            const double s = placedStart + b.relStart + d, e = s + b.len;
            if (b.kind == 1) {
                for (const auto& c : t.midiClips) {
                    const double cs = c.startBeat, ce = cs + c.lengthBeats;
                    if (s < ce && e > cs) {
                        const double need = ce - (placedStart + b.relStart);
                        if (need > d + 1e-9) { d = need; moved = true; }
                    }
                }
            } else {
                for (const auto& c : t.clips) {
                    const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
                    if (s < ce && e > cs) {
                        const double need = ce - (placedStart + b.relStart);
                        if (need > d + 1e-9) { d = need; moved = true; }
                    }
                }
            }
        }
    }

    // Insert the shifted copies + their automation, remembering each new clip's index.
    for (const auto& b : items) {
        auto it = clones.find(b.trackId);
        if (it == clones.end()) continue;
        Track& nt = *it->second;
        const double start = placedStart + b.relStart + d;
        int32_t newIdx;
        if (b.kind == 1) {
            MidiClip m = b.midi; m.startBeat = start;
            nt.midiClips.push_back(m);
            newIdx = static_cast<int32_t>(nt.midiClips.size()) - 1;
        } else {
            AudioClip a = b.audio; a.startBeat = start;
            if (a.warpEnabled) configureClipWarp(a, spb, devSR);   // the copy needs its own stretcher
            nt.clips.push_back(a);
            newIdx = static_cast<int32_t>(nt.clips.size()) - 1;
        }
        applyClipAutomation(nt, b.trackId, b.autom, start, b.len, b.sameTrack);
        lastPlaced_.emplace_back(b.trackId, newIdx);
    }

    // Publish all touched tracks at once — a single undo checkpoint for the whole block.
    pushUndo();
    auto g = std::make_shared<Graph>();
    g->sceneCount   = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack  = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        auto it = clones.find(t->id());
        g->tracks.push_back(it != clones.end() ? it->second : t);
    }
    publishRaw(std::move(g));
}

bool Engine::copyClipBlock(const std::vector<std::pair<int32_t,int32_t>>& sel) {
    double len = 0.0;
    auto items = captureBlock(sel, len);
    if (items.empty()) return false;
    clipboardBlock_ = std::move(items);
    clipboardBlockLen_ = len;
    return true;
}

bool Engine::cutClipBlock(const std::vector<std::pair<int32_t,int32_t>>& sel) {
    if (!copyClipBlock(sel)) return false;
    // Remove the source clips (highest index first per track) + the automation in their
    // spans, publishing every touched track in one undo step.
    std::map<int32_t, std::shared_ptr<Track>> clones;
    std::map<int32_t, std::vector<int32_t>> byTrack;
    for (const auto& [tid, ci] : sel) byTrack[tid].push_back(ci);
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    for (auto& [tid, idxs] : byTrack) {
        auto old = findTrackAuthoring(tid);
        if (!old) continue;
        auto nt = cloneTrack(*old);
        std::sort(idxs.begin(), idxs.end(), std::greater<int32_t>());
        for (int32_t ci : idxs) {
            if (nt->type() == TrackType::Instrument) {
                if (ci < 0 || ci >= static_cast<int32_t>(nt->midiClips.size())) continue;
                const auto& m = nt->midiClips[ci];
                removeAutomationInRange(*nt, m.startBeat, m.startBeat + m.lengthBeats);
                nt->midiClips.erase(nt->midiClips.begin() + ci);
            } else {
                if (ci < 0 || ci >= static_cast<int32_t>(nt->clips.size())) continue;
                const auto& a = nt->clips[ci];
                removeAutomationInRange(*nt, a.startBeat, a.startBeat + audioDisplayLenBeats(a, spb, devSR));
                nt->clips.erase(nt->clips.begin() + ci);
            }
        }
        clones[tid] = nt;
    }
    if (clones.empty()) return true;
    pushUndo();
    auto g = std::make_shared<Graph>();
    g->sceneCount   = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack  = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        auto it = clones.find(t->id());
        g->tracks.push_back(it != clones.end() ? it->second : t);
    }
    publishRaw(std::move(g));
    return true;
}

int32_t Engine::pasteClipBlock(double atBeat, int32_t sourceTrackId) {
    if (clipboardBlock_.empty() || !authoring_) return 0;
    std::vector<BlockClip> items = clipboardBlock_;
    // sourceTrackId >= 0 remaps the whole block by the track-index delta from its top track to
    // the destination (so a single clip lands on that track; a multi-track block shifts
    // together). Clips that fall off the track list or hit a type-mismatched track are
    // skipped; a remapped clip's device/plugin automation is dropped (indices are positional).
    if (sourceTrackId >= 0) {
        auto idxOf = [&](int32_t tid) -> int {
            for (size_t i = 0; i < authoring_->tracks.size(); ++i)
                if (authoring_->tracks[i]->id() == tid) return static_cast<int>(i);
            return -1;
        };
        const int destIdx = idxOf(sourceTrackId);
        int topIdx = std::numeric_limits<int>::max();
        for (const auto& b : items) { int ix = idxOf(b.trackId); if (ix >= 0) topIdx = std::min(topIdx, ix); }
        if (destIdx < 0 || topIdx == std::numeric_limits<int>::max()) return 0;
        const int delta = destIdx - topIdx;
        std::vector<BlockClip> remapped;
        for (const auto& b : items) {
            const int ix = idxOf(b.trackId);
            if (ix < 0) continue;
            const int ti = ix + delta;
            if (ti < 0 || ti >= static_cast<int>(authoring_->tracks.size())) continue;
            const auto& tt = authoring_->tracks[ti];
            const bool typeOk = (b.kind == 1) ? (tt->type() == TrackType::Instrument)
                                              : (tt->type() == TrackType::Audio);
            if (!typeOk) continue;
            BlockClip nb = b;
            nb.sameTrack = tt->id() == b.trackId;
            nb.trackId   = tt->id();
            remapped.push_back(std::move(nb));
        }
        items = std::move(remapped);
        if (items.empty()) return 0;
    }
    placeBlock(items, atBeat);
    return static_cast<int32_t>(lastPlaced_.size());
}

double Engine::duplicateClipBlock(const std::vector<std::pair<int32_t,int32_t>>& sel) {
    double len = 0.0;
    auto items = captureBlock(sel, len);
    if (items.empty()) return -1.0;
    // captureBlock rebases relStart to the block start; recover the block's absolute start
    // (the min clip start across the selection) so we can place the copy right after it.
    double absStart = std::numeric_limits<double>::max();
    for (const auto& [tid, ci] : sel) {
        auto t = findTrackAuthoring(tid);
        if (!t) continue;
        if (t->type() == TrackType::Instrument) {
            if (ci >= 0 && ci < static_cast<int32_t>(t->midiClips.size()))
                absStart = std::min(absStart, t->midiClips[ci].startBeat);
        } else {
            if (ci >= 0 && ci < static_cast<int32_t>(t->clips.size()))
                absStart = std::min(absStart, t->clips[ci].startBeat);
        }
    }
    if (absStart == std::numeric_limits<double>::max()) return -1.0;
    placeBlock(items, absStart + len);
    return len;
}

// --- clip / track names + track colour (UI metadata) ------------------------
namespace {
int32_t copyStr(const std::string& s, char* out, int32_t cap) {
    if (out && cap > 0) {
        const int32_t n = std::min<int32_t>(static_cast<int32_t>(s.size()), cap - 1);
        std::memcpy(out, s.data(), n);
        out[n] = '\0';
    }
    return static_cast<int32_t>(s.size());
}
} // namespace

bool Engine::setClipName(int32_t trackId, int32_t clipIndex, const std::string& name) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        nt->midiClips[clipIndex].name = name;
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        nt->clips[clipIndex].name = name;
    }
    republishWithTrack(trackId, nt);
    return true;
}
int32_t Engine::clipName(int32_t trackId, int32_t clipIndex, char* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    if (t->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
        return copyStr(t->midiClips[clipIndex].name, out, cap);
    }
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    return copyStr(t->clips[clipIndex].name, out, cap);
}
bool Engine::setTrackName(int32_t trackId, const std::string& name) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    nt->name = name;
    republishWithTrack(trackId, nt);
    return true;
}
int32_t Engine::trackName(int32_t trackId, char* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    return t ? copyStr(t->name, out, cap) : 0;
}
bool Engine::setTrackColorIndex(int32_t trackId, int32_t colorIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    nt->colorIndex = colorIndex;
    republishWithTrack(trackId, nt);
    return true;
}
int32_t Engine::trackColorIndex(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->colorIndex : -1;
}

bool Engine::deleteClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return false;
        nt->midiClips.erase(nt->midiClips.begin() + clipIndex);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return false;
        nt->clips.erase(nt->clips.begin() + clipIndex);
    }
    republishWithTrack(trackId, nt);
    return true;
}

// Fully independent copy of a track: cloned instrument/MIDI-FX/insert devices (own DSP
// state), copied clips/automation with fresh warp stretchers, and the UI metadata.
std::shared_ptr<Track> Engine::deepCloneTrack(const Track& src, int32_t newId) {
    auto nt = std::make_shared<Track>(newId, src.type());
    nt->setVolume(src.volume()); nt->setPan(src.pan());
    nt->setMute(src.mute());     nt->setSolo(src.solo()); nt->setArmed(src.armed());
    nt->setReturnIndex(src.returnIndex());
    nt->setGroupId(src.groupId());   // keep membership on duplicate (paste resets to top-level)
    nt->setRecordInputSource(src.recordInputSource());
    nt->setMidiFromTrackId(src.midiFromTrackId());
    for (int b = 0; b < kMaxReturns; ++b) nt->setSend(b, src.send(b));
    nt->clips        = src.clips;          // sample buffers are shared_ptr (immutable) — fine
    nt->midiClips    = src.midiClips;
    nt->sessionSlots = src.sessionSlots;
    nt->automation   = src.automation;
    nt->name         = src.name;
    nt->colorIndex   = src.colorIndex;
    nt->instrument   = cloneInstrument(src);
    for (auto& m : src.midiEffects) if (auto nm = cloneMidiDevice(*m)) nt->midiEffects.push_back(nm);
    for (auto& d : src.devices) if (auto nd = cloneDevice(*d)) nt->devices.push_back(nd);
    // Warped clips must not share a stretcher across tracks — rebuild per clip.
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    for (auto& c : nt->clips) if (c.warpEnabled && c.sample) configureClipWarp(c, spb, devSR);
    return nt;
}

int32_t Engine::duplicateTrack(int32_t trackId) {
    auto src = findTrackAuthoring(trackId);
    if (!src) return -1;
    const int32_t newId = nextTrackId_++;
    auto nt = deepCloneTrack(*src, newId);

    // Insert the copy right after the source (new snapshot; checkpoints undo).
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size() + 1);
    for (auto& t : authoring_->tracks) {
        g->tracks.push_back(t);
        if (t->id() == trackId) g->tracks.push_back(nt);
    }
    publish(std::move(g));
    recomputePdc();
    return newId;
}

bool Engine::removeTrack(int32_t trackId) {
    auto rt = findTrackAuthoring(trackId);
    if (!rt) return false;
    // Close any open plugin editor windows for this track before it leaves the live
    // graph — otherwise the native window lingers on screen after the track is gone.
    // (The track object itself survives in undo history, so the plugin isn't freed
    // yet; hiding the editor is the visible fix.)
    if (rt->instrument) rt->instrument->closeEditor();
    for (auto& d : rt->devices) if (d) d->closeEditor();
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks)
        if (t->id() != trackId) g->tracks.push_back(t);
    publish(std::move(g));
    recomputePdc();
    return true;
}

bool Engine::moveTrack(int32_t trackId, int32_t toIndex) {
    auto rt = findTrackAuthoring(trackId);
    if (!rt || rt->type() == TrackType::Return) return false;   // returns aren't reorderable here
    // Split into the movable (non-return) list + the returns, which keep their trailing slots.
    std::vector<std::shared_ptr<Track>> regular, returns;
    for (auto& t : authoring_->tracks) {
        if (t->type() == TrackType::Return) returns.push_back(t);
        else regular.push_back(t);
    }
    int32_t from = -1;
    for (int32_t i = 0; i < static_cast<int32_t>(regular.size()); ++i)
        if (regular[i]->id() == trackId) { from = i; break; }
    if (from < 0) return false;
    int32_t to = std::clamp(toIndex, 0, static_cast<int32_t>(regular.size()) - 1);
    if (to == from) return true;
    auto tr = regular[from];
    regular.erase(regular.begin() + from);
    regular.insert(regular.begin() + to, tr);
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(regular.size() + returns.size());
    for (auto& t : regular) g->tracks.push_back(t);
    for (auto& t : returns) g->tracks.push_back(t);
    publish(std::move(g));
    recomputePdc();
    return true;
}

// --- track groups (submix buses) -------------------------------------------
int32_t Engine::addGroupTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    g->tracks.push_back(std::make_shared<Track>(id, TrackType::Group));
    publish(g);
    return id;
}

int32_t Engine::createGroup(const int32_t* ids, int32_t n) {
    if (!ids || n <= 0) return -1;
    std::vector<int32_t> members;
    int32_t parent = -1;
    for (int32_t i = 0; i < n; ++i) {
        auto t = findTrackAuthoring(ids[i]);
        if (!t || t->type() == TrackType::Return) continue;
        if (members.empty()) parent = t->groupId();   // nest the group under the members' common parent
        if (std::find(members.begin(), members.end(), ids[i]) == members.end()) members.push_back(ids[i]);
    }
    if (members.empty()) return -1;
    const int32_t gid = nextTrackId_++;
    auto grp = std::make_shared<Track>(gid, TrackType::Group);
    grp->setGroupId(parent);
    auto isMember = [&](int32_t id) { return std::find(members.begin(), members.end(), id) != members.end(); };

    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size() + 1);
    bool placed = false;
    for (auto& t : authoring_->tracks) {
        if (isMember(t->id())) {
            if (!placed) { g->tracks.push_back(grp); placed = true; }   // group sits at the topmost member
            auto nt = cloneTrack(*t); nt->setGroupId(gid); g->tracks.push_back(nt);
        } else {
            g->tracks.push_back(t);
        }
    }
    if (!placed) g->tracks.push_back(grp);
    publish(std::move(g));
    recomputePdc();
    return gid;
}

bool Engine::ungroup(int32_t groupId) {
    auto grp = findTrackAuthoring(groupId);
    if (!grp || grp->type() != TrackType::Group) return false;
    for (auto& d : grp->devices) if (d) d->closeEditor();   // its plugin windows shouldn't linger
    const int32_t parent = grp->groupId();                  // children reparent up one level
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        if (t->id() == groupId) continue;                   // drop the group track
        if (t->groupId() == groupId) { auto nt = cloneTrack(*t); nt->setGroupId(parent); g->tracks.push_back(nt); }
        else g->tracks.push_back(t);
    }
    publish(std::move(g));
    recomputePdc();
    return true;
}

bool Engine::setTrackGroup(int32_t trackId, int32_t groupId) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() == TrackType::Return || trackId == groupId) return false;
    if (t->groupId() == groupId) return true;   // already there
    if (groupId >= 0) {
        auto grp = findTrackAuthoring(groupId);
        if (!grp || grp->type() != TrackType::Group) return false;
        // Reject cycles: groupId must not live inside trackId's own subtree.
        for (int32_t a = groupId; a >= 0; ) {
            if (a == trackId) return false;
            auto at = findTrackAuthoring(a);
            a = at ? at->groupId() : -1;
        }
    }
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& tt : authoring_->tracks) {
        if (tt->id() == trackId) { auto nt = cloneTrack(*tt); nt->setGroupId(groupId); g->tracks.push_back(nt); }
        else g->tracks.push_back(tt);
    }
    publish(std::move(g));
    recomputePdc();
    return true;
}

// --- track clipboard (copy/cut/paste) --------------------------------------
bool Engine::copyTrack(int32_t trackId) {
    auto src = findTrackAuthoring(trackId);
    if (!src) return false;
    trackClipboard_ = deepCloneTrack(*src, 0);   // id is a placeholder; paste assigns a real one
    return true;
}
bool Engine::hasTrackClipboard() const { return trackClipboard_ != nullptr; }
int32_t Engine::pasteTrack() {
    if (!trackClipboard_) return -1;
    if (trackClipboard_->type() == TrackType::Return) return -1;  // return-bus identity can't be duplicated
    const int32_t newId = nextTrackId_++;
    auto nt = deepCloneTrack(*trackClipboard_, newId);            // re-clone so repeat pastes stay independent
    nt->setGroupId(-1);                                           // paste lands top-level (its group may not exist)
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size() + 1);
    for (auto& t : authoring_->tracks) g->tracks.push_back(t);
    // Append after the last non-return track so it sits with the regular tracks.
    int32_t insertAt = static_cast<int32_t>(g->tracks.size());
    for (int32_t i = 0; i < static_cast<int32_t>(g->tracks.size()); ++i)
        if (g->tracks[i]->type() == TrackType::Return) { insertAt = i; break; }
    g->tracks.insert(g->tracks.begin() + insertAt, nt);
    publish(std::move(g));
    recomputePdc();
    return newId;
}


int32_t Engine::addMidiClip(int32_t trackId, double startBeat, double lengthBeats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return -1;
    auto nt = cloneTrack(*old);
    MidiClip c; c.startBeat = startBeat; c.lengthBeats = lengthBeats;
    nt->midiClips.push_back(c);
    const int32_t idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
    republishWithTrack(trackId, nt);
    return idx;
}

bool Engine::setClipNotes(int32_t trackId, int32_t clipIndex, const NotaNoteData* notes, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    auto nt = cloneTrack(*old);
    MidiClip& clip = nt->midiClips[clipIndex];
    clip.notes.clear();
    clip.notes.reserve(count);
    for (int32_t i = 0; i < count; ++i) {
        Note n; n.pitch = notes[i].pitch; n.startBeat = notes[i].start_beat;
        n.lengthBeats = notes[i].length_beats; n.velocity = notes[i].velocity;
        clip.notes.push_back(n);
    }
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setClipNotesLive(int32_t trackId, int32_t clipIndex, const NotaNoteData* notes, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    auto nt = cloneTrack(*old);
    MidiClip& clip = nt->midiClips[clipIndex];
    clip.notes.clear();
    clip.notes.reserve(count);
    for (int32_t i = 0; i < count; ++i) {
        Note n; n.pitch = notes[i].pitch; n.startBeat = notes[i].start_beat;
        n.lengthBeats = notes[i].length_beats; n.velocity = notes[i].velocity;
        clip.notes.push_back(n);
    }
    republishWithTrackRaw(trackId, nt);   // no undo checkpoint — the UI seeds one per gesture
    return true;
}

int32_t Engine::getClipNotes(int32_t trackId, int32_t clipIndex, NotaNoteData* out, int32_t maxNotes) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    const MidiClip& clip = t->midiClips[clipIndex];
    const int32_t n = std::min<int32_t>(maxNotes, static_cast<int32_t>(clip.notes.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].pitch = clip.notes[i].pitch;
        out[i].start_beat = clip.notes[i].startBeat;
        out[i].length_beats = clip.notes[i].lengthBeats;
        out[i].velocity = clip.notes[i].velocity;
    }
    return n;
}

int32_t Engine::clipNoteCount(int32_t trackId, int32_t clipIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    return static_cast<int32_t>(t->midiClips[clipIndex].notes.size());
}

// --- freeze (M7) -----------------------------------------------------------
// Frozen-audio blob layout: [double frozenSpb][int64 frameCount] then frameCount
// interleaved-stereo float samples. Small, self-describing; version-free (any change
// bumps the whole project format).
namespace { constexpr int kFreezeHeaderBytes = static_cast<int>(sizeof(double) + sizeof(int64_t)); }

int64_t Engine::beginFreeze(int32_t trackId, double lengthBeats) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    if (t->type() != TrackType::Instrument && t->type() != TrackType::Audio) return 0;
    const double sr  = transport_.sampleRate() > 0.0 ? transport_.sampleRate() : 44100.0;
    const double spb = transport_.samplesPerBeat();
    if (spb <= 0.0 || lengthBeats <= 0.0) return 0;
    const double tailSec = 2.0;   // let reverb/delay tails ring past the last clip
    int64_t frames = static_cast<int64_t>(std::ceil(lengthBeats * spb)) + static_cast<int64_t>(tailSec * sr);
    if (frames <= 0) return 0;
    freezeCaptureBuf_.assign(static_cast<size_t>(frames) * 2, 0.0f);
    freezeCapFrames_   = frames;
    freezeCursor_      = 0;
    freezeCaptureSpb_  = spb;
    freezeCaptureTrackId_.store(trackId, std::memory_order_relaxed);
    return frames;
}

void Engine::endFreeze(int32_t trackId) {
    freezeCaptureTrackId_.store(-1, std::memory_order_relaxed);
    auto old = findTrackAuthoring(trackId);
    if (!old || freezeCaptureBuf_.empty()) { cancelFreeze(); return; }
    auto buf = std::make_shared<std::vector<float>>(std::move(freezeCaptureBuf_));
    freezeCaptureBuf_ = std::vector<float>();   // reset the moved-from vector
    auto nt = cloneTrack(*old);
    nt->frozenBuf = buf;                          // shared_ptr<vector> → shared_ptr<const vector>
    nt->frozenSpb = freezeCaptureSpb_;
    nt->setFrozen(true);
    republishWithTrackRaw(trackId, std::move(nt)); // a view toggle, not an undo step
    freezeCapFrames_ = 0; freezeCursor_ = 0;
}

void Engine::cancelFreeze() {
    freezeCaptureTrackId_.store(-1, std::memory_order_relaxed);
    freezeCaptureBuf_ = std::vector<float>();
    freezeCapFrames_ = 0; freezeCursor_ = 0;
}

void Engine::unfreeze(int32_t trackId) {
    auto old = findTrackAuthoring(trackId);
    if (!old || !old->frozen()) return;
    auto nt = cloneTrack(*old);
    nt->setFrozen(false);
    nt->frozenBuf.reset();
    nt->frozenSpb = 0.0;
    republishWithTrackRaw(trackId, std::move(nt));
}

bool Engine::trackFrozen(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->frozen() && t->frozenBuf != nullptr;
}

int32_t Engine::freezeGetState(int32_t trackId, uint8_t* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->frozen() || !t->frozenBuf) return 0;
    const std::vector<float>& buf = *t->frozenBuf;
    const int64_t frames = static_cast<int64_t>(buf.size() / 2);
    const int64_t size = kFreezeHeaderBytes + frames * 2 * static_cast<int64_t>(sizeof(float));
    if (size > std::numeric_limits<int32_t>::max()) return 0;   // 2 GB cap
    if (out && cap >= size) {
        const double spb = t->frozenSpb;
        std::memcpy(out, &spb, sizeof(double));
        std::memcpy(out + sizeof(double), &frames, sizeof(int64_t));
        std::memcpy(out + kFreezeHeaderBytes, buf.data(), static_cast<size_t>(frames) * 2 * sizeof(float));
    }
    return static_cast<int32_t>(size);
}

void Engine::freezeSetState(int32_t trackId, const uint8_t* data, int32_t size) {
    if (!data || size < kFreezeHeaderBytes) return;
    auto old = findTrackAuthoring(trackId);
    if (!old) return;
    double spb = 0.0; int64_t frames = 0;
    std::memcpy(&spb, data, sizeof(double));
    std::memcpy(&frames, data + sizeof(double), sizeof(int64_t));
    if (frames <= 0 || spb <= 0.0) return;
    const int64_t need = kFreezeHeaderBytes + frames * 2 * static_cast<int64_t>(sizeof(float));
    if (size < need) return;
    auto buf = std::make_shared<std::vector<float>>(static_cast<size_t>(frames) * 2);
    std::memcpy(buf->data(), data + kFreezeHeaderBytes, static_cast<size_t>(frames) * 2 * sizeof(float));
    auto nt = cloneTrack(*old);
    nt->frozenBuf = buf;
    nt->frozenSpb = spb;
    nt->setFrozen(true);
    republishWithTrackRaw(trackId, std::move(nt));
}

} // namespace nota
