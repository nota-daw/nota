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
#include <set>
#include <type_traits>

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
    if (!audioClipRef(*t, clipIndex)) return false;
    const AudioClip& c = (*audioClipRef(*t, clipIndex));
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
    out->reversed = c.reversed ? 1 : 0;
    return true;
}

bool Engine::setClipGain(int32_t trackId, int32_t clipIndex, float gain) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = cloneTrack(*old);
    (*audioClipRef(*nt, clipIndex)).gain = gain < 0.0f ? 0.0f : gain;
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::clipAdsr(int32_t trackId, int32_t clipIndex, NotaClipAdsr* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    if (!audioClipRef(*t, clipIndex)) return false;
    const ClipAdsr& a = (*audioClipRef(*t, clipIndex)).adsr;
    out->attack_beats = a.attack;
    out->decay_beats = a.decay;
    out->release_beats = a.release;
    out->sustain = a.sustain;
    out->reserved = 0;
    return true;
}

// ADSR is plain clip data (no cache to rebuild), so this is as cheap as a gain change.
bool Engine::setClipAdsr(int32_t trackId, int32_t clipIndex, const NotaClipAdsr& in) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto finite = [](double v) { return std::isfinite(v) ? std::max(0.0, v) : 0.0; };
    ClipAdsr a;
    a.attack = finite(in.attack_beats);
    a.decay = finite(in.decay_beats);
    a.release = finite(in.release_beats);
    a.sustain = std::isfinite(in.sustain) ? std::clamp(in.sustain, 0.0f, 1.0f) : 1.0f;
    auto nt = cloneTrack(*old);
    (*audioClipRef(*nt, clipIndex)).adsr = a;
    syncSessionAudioSlot(*nt, clipIndex);
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
double seedAutoWarp(AudioClip& c, double spb, double devSR, double knownBpm = 0.0) {
    const double srcSR = c.sample->sourceSampleRate;
    const int64_t srcLen = c.effectiveLength();
    const double bpm = knownBpm > 0.0
        ? knownBpm
        : detectTempo(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen, srcSR);
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
    const AudioClip* cp = audioClipRef(*t, wb_.clipIndex);   // an arrangement clip or a session slot's
    if (!cp) return nullptr;
    const AudioClip& c = *cp;
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
        for (auto& sl : t->sessionSlots)
            if (sl.hasClip && sl.audio.warpEnabled && !sl.audio.warpCache) sum += warpCacheFrames(sl.audio, spb, devSR);
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
                for (size_t sc = 0; !target && sc < t->sessionSlots.size(); ++sc) {
                    const SessionSlot& sl = t->sessionSlots[sc];
                    if (sl.hasClip && sl.audio.warpEnabled && !sl.audio.warpCache && warpCacheFrames(sl.audio, spb, devSR) > 0) {
                        target = &sl.audio; tid = t->id(); ci = sessionClipIndex(static_cast<int32_t>(sc));
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
            if (old && audioClipRef(*old, wb_.clipIndex)) {
                auto nt = cloneTrack(*old);
                audioClipRef(*nt, wb_.clipIndex)->warpCache = std::const_pointer_cast<const WarpCache>(wb_.cache);
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
    g->scenes = authoring_->scenes;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        bool warped = false;
        if (t->type() == TrackType::Audio) {
            for (auto& c : t->clips) if (c.warpEnabled && c.sample) { warped = true; break; }
            for (auto& sl : t->sessionSlots) if (sl.hasClip && sl.audio.warpEnabled && sl.audio.sample) { warped = true; break; }
        }
        if (!warped) { g->tracks.push_back(t); continue; }
        auto nt = cloneTrack(*t);
        for (auto& c : nt->clips) if (c.warpEnabled && c.sample) configureClipWarp(c, spb, devSR);
        for (auto& sl : nt->sessionSlots)   // session audio slots warp like clips (S-06)
            if (sl.hasClip && sl.audio.warpEnabled && sl.audio.sample) configureClipWarp(sl.audio, spb, devSR);
        g->tracks.push_back(std::move(nt));
        any = true;
    }
    if (any) publishRaw(std::move(g));
}

bool Engine::setClipPitch(int32_t trackId, int32_t clipIndex, float semitones) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = cloneTrack(*old);
    (*audioClipRef(*nt, clipIndex)).pitchSemitones = semitones;
    configureClipWarp((*audioClipRef(*nt, clipIndex)), transport_.samplesPerBeat(), transport_.sampleRate()); // pitch is baked into the warp
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

// Reverse an audio clip (non-destructive): the played region is read back-to-front.
// Nothing is re-rendered — the source and the warp cache stay in file order and the
// renderer mirrors its read index — so this is as cheap as a gain change.
bool Engine::setClipReverse(int32_t trackId, int32_t clipIndex, bool reversed) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = cloneTrack(*old);
    (*audioClipRef(*nt, clipIndex)).reversed = reversed;
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setClipWarp(int32_t trackId, int32_t clipIndex, bool enabled, int32_t mode) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
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
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

double Engine::autoWarpClip(int32_t trackId, int32_t clipIndex, double knownBpm) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return 0.0;
    if (!audioClipRef(*old, clipIndex)) return 0.0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;
    const double bpm = seedAutoWarp(c, spb, devSR, knownBpm);
    if (bpm <= 0.0) return 0.0;   // detection failed: leave the clip unchanged
    c.warpEnabled = true;
    configureClipWarp(c, spb, devSR);
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return bpm;
}

double Engine::beatWarpClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return 0.0;
    if (!audioClipRef(*old, clipIndex)) return 0.0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
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
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return bpm;
}

bool Engine::setClipWarpLength(int32_t trackId, int32_t clipIndex, double beats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    if (beats < 0.25) beats = 0.25;
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
    c.warpBeats = beats;
    // The end marker owns the total length; keep interior markers within it.
    if (!c.warpMarkers.empty()) {
        c.warpMarkers.back().beat = beats;
        for (auto& m : c.warpMarkers) if (m.beat > beats) m.beat = beats;
    }
    clampWarpPlay(c);   // keep the trim window valid for the new length
    configureClipWarp(c, transport_.samplesPerBeat(), transport_.sampleRate());
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setClipWarpMarkers(int32_t trackId, int32_t clipIndex,
                                const double* srcFrames, const double* beats, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    if (count < 2 || !srcFrames || !beats) return false;
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
    c.warpMarkers.clear();
    c.warpMarkers.reserve(count);
    for (int32_t i = 0; i < count; ++i) c.warpMarkers.push_back({ srcFrames[i], beats[i] });
    std::sort(c.warpMarkers.begin(), c.warpMarkers.end(),
              [](const WarpMarker& a, const WarpMarker& b) { return a.srcFrame < b.srcFrame; });
    c.warpBeats = c.warpMarkers.back().beat;
    clampWarpPlay(c);   // preserve the trim window across marker edits (clamped to new length)
    configureClipWarp(c, transport_.samplesPerBeat(), transport_.sampleRate());
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

// Trim a warped clip's played window (clip Start/End over the warp). Markers
// and warpBeats are untouched; only the [playStart, playEnd] beat window moves. No-op
// on unwarped clips (their region is set via setClipSourceRegion).
bool Engine::setClipWarpTrim(int32_t trackId, int32_t clipIndex, double playStart, double playEnd) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    const AudioClip& oc = (*audioClipRef(*old, clipIndex));
    if (!oc.warpEnabled || oc.warpBeats <= 0.0) return false;
    constexpr double kMinBeat = 0.05;
    double ps = std::clamp(playStart, 0.0, std::max(0.0, oc.warpBeats - kMinBeat));
    double pe = std::clamp(playEnd, ps + kMinBeat, oc.warpBeats);
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
    c.warpPlayStart = ps;
    c.warpPlayEnd = pe;
    configureClipWarp(c, transport_.samplesPerBeat(), transport_.sampleRate()); // window is baked into the cache
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::clipWarpMarkers(int32_t trackId, int32_t clipIndex,
                                double* outSrc, double* outBeat, int32_t maxCount) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (!audioClipRef(*t, clipIndex)) return 0;
    const auto& m = (*audioClipRef(*t, clipIndex)).warpMarkers;
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

// Nota Rhythm voice FX. The machine swaps its chains as snapshots itself (like a rack), so
// these edit the live instrument in place — a clone would drop the running voices.
static RhythmMachine* rhythmOf(const std::shared_ptr<Track>& t) {
    return t ? dynamic_cast<RhythmMachine*>(t->instrument.get()) : nullptr;
}
int32_t Engine::rhythmVoiceDeviceCount(int32_t trackId, int32_t voice) const {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->voiceDeviceCount(voice) : 0;
}
int32_t Engine::rhythmAddVoiceDevice(int32_t trackId, int32_t voice, int32_t kind) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->addVoiceDevice(voice, kind) : -1;
}
bool Engine::rhythmRemoveVoiceDevice(int32_t trackId, int32_t voice, int32_t dev) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh && rh->removeVoiceDevice(voice, dev);
}
bool Engine::rhythmMoveVoiceDevice(int32_t trackId, int32_t voice, int32_t from, int32_t to) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh && rh->moveVoiceDevice(voice, from, to);
}
Device* Engine::rhythmVoiceDevice(int32_t trackId, int32_t voice, int32_t dev) const {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->voiceDevice(voice, dev) : nullptr;
}
std::string Engine::rhythmKitName(int32_t trackId) const {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->kitName() : std::string{};
}
void Engine::setRhythmKitName(int32_t trackId, const std::string& name) {
    if (auto* rh = rhythmOf(findTrackAuthoring(trackId))) rh->setKitName(name);
}
std::string Engine::rhythmMacroName(int32_t trackId, int32_t macro) const {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->macroName(macro) : std::string{};
}
void Engine::setRhythmMacroName(int32_t trackId, int32_t macro, const std::string& name) {
    if (auto* rh = rhythmOf(findTrackAuthoring(trackId))) rh->setMacroName(macro, name);
}
int32_t Engine::rhythmAddMacroMapping(int32_t trackId, int32_t macro, int32_t voice, int32_t device, int32_t param, float lo, float hi) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->addMacroMapping(macro, voice, device, param, lo, hi) : -1;
}
int32_t Engine::rhythmMacroMappingCount(int32_t trackId) const {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh ? rh->macroMappingCount() : 0;
}
bool Engine::rhythmMacroMappingInfo(int32_t trackId, int32_t index, int32_t& macro, int32_t& voice, int32_t& device,
                                    int32_t& param, float& lo, float& hi, int32_t& curve) const {
    auto* rh = rhythmOf(findTrackAuthoring(trackId));
    RhythmMachine::MacroMap m;
    if (!rh || !rh->macroMapping(index, m)) return false;
    macro = m.macro; voice = m.voice; device = m.device; param = m.param; lo = m.lo; hi = m.hi; curve = m.curve;
    return true;
}
bool Engine::rhythmRemoveMacroMapping(int32_t trackId, int32_t index) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh && rh->removeMacroMapping(index);
}
bool Engine::rhythmSetMacroMappingRange(int32_t trackId, int32_t index, float lo, float hi) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh && rh->setMacroMappingRange(index, lo, hi);
}
bool Engine::rhythmSetMacroMappingCurve(int32_t trackId, int32_t index, int32_t curve) {
    auto* rh = rhythmOf(findTrackAuthoring(trackId)); return rh && rh->setMacroMappingCurve(index, curve);
}
void Engine::rhythmClearMacros(int32_t trackId) {
    if (auto* rh = rhythmOf(findTrackAuthoring(trackId))) rh->clearMacros();
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
    return addAudioClipBuffer(trackId, decodeAudioFile(path), startBeat);
}

int32_t Engine::addAudioClipBuffer(int32_t trackId, std::shared_ptr<SampleBuffer> sample, double startBeat) {
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
        float mn, mx;
        sb.peakRange(s0, s1 + 1, mn, mx);
        out[b * 2] = mn; out[b * 2 + 1] = mx;
    }
    return buckets;
}

// Flip a min/max peak array end-to-end (each bucket's own min/max is unchanged) so a
// reversed clip draws the waveform it actually plays.
static void reversePeaks(float* peaks, int32_t count) {
    for (int32_t a = 0, b = count - 1; a < b; ++a, --b) {
        std::swap(peaks[a * 2], peaks[b * 2]);
        std::swap(peaks[a * 2 + 1], peaks[b * 2 + 1]);
    }
}

int32_t Engine::getClipPeaks(int32_t trackId, int32_t clipIndex,
                             float* outMinMax, int32_t maxPoints) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
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
        const int32_t n = warpPeaksRange(clip, outMinMax, maxPoints, clip.warpPlayStart, clip.warpPlayEndEff());
        if (clip.reversed) reversePeaks(outMinMax, n);
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
        outMinMax[b * 2] = mn; outMinMax[b * 2 + 1] = mx;
    }
    if (clip.reversed) reversePeaks(outMinMax, buckets);
    return buckets;
}

// Peaks over the WHOLE sample (frames 0..sample->frames), regardless of the clip's
// trimmed region — the clip editor draws the full file so the Start/End brackets can
// reveal audio that was trimmed off. Unwarped only (warped uses getClipPeaks). Always in
// FILE order: the editor draws this view together with source-frame brackets, so it
// mirrors the two as one when the clip is reversed rather than flipping peaks here.
int32_t Engine::getClipSourcePeaks(int32_t trackId, int32_t clipIndex,
                                   float* outMinMax, int32_t maxPoints) const {
    if (!outMinMax || maxPoints <= 0) return 0;
    auto t = findTrackAuthoring(trackId);
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
        outMinMax[b * 2] = mn; outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

// Peaks over the FULL warped material [0..warpBeats], ignoring the trim window — the
// clip editor draws the whole warp so the Start/End brackets can reveal trimmed beats.
// In marker (file) order — the editor mirrors this view and its markers together when the
// clip is reversed, so flipping the peaks alone here would desync them.
int32_t Engine::getClipWarpFullPeaks(int32_t trackId, int32_t clipIndex,
                                     float* outMinMax, int32_t maxPoints) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& clip = (*audioClipRef(*t, clipIndex));
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
    if (!audioClipRef(*t, clipIndex)) return false;
    const AudioClip& c = (*audioClipRef(*t, clipIndex));
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
// Source frame just past a REVERSED clip's audible head (its region end) after the left
// edge moved by `deltaBeats`. Trims measure a reversed clip backwards from here.
double audioReverseHead(const AudioClip& c, double deltaBeats, double spb, double devSR) {
    double head = c.sourceOffsetFrames + static_cast<double>(c.effectiveLength())
                - audioBeatsToSrcFrames(c, deltaBeats, spb, devSR);
    if (c.sample) head = std::min(head, static_cast<double>(c.sample->frames));
    return std::max(0.0, head);
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
// Both carves work in PLAYED time, so a reversed clip mirrors: its audible head is the
// region's end, so keeping the first beats keeps the region's tail and vice versa.
AudioClip audioCarveLeft(const AudioClip& src, double endLocal, double spb, double devSR) {
    AudioClip left = src;
    left.adsr.release = 0.0;   // the cut is a new, hard end: the tail's release stays with the right piece
    if (src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0) {
        if (src.reversed) {
            left.warpPlayStart = std::max(0.0, src.warpPlayEndEff() - endLocal);
            left.warpPlayEnd = src.warpPlayEndEff();
        } else {
            left.warpPlayEnd = src.warpPlayStart + endLocal;
        }
        buildWarpCache(left, spb, devSR);
    } else {
        const int64_t keep = static_cast<int64_t>(audioBeatsToSrcFrames(src, endLocal, spb, devSR));
        if (src.reversed)
            left.sourceOffsetFrames = src.sourceOffsetFrames + static_cast<double>(src.effectiveLength() - keep);
        left.lengthFrames = keep;
    }
    return left;
}
AudioClip audioCarveRight(const AudioClip& src, double startLocal, double spb, double devSR) {
    AudioClip right = src; right.startBeat = src.startBeat + startLocal;
    right.adsr.attack = 0.0; right.adsr.decay = 0.0;   // the head's attack/decay stay with the left piece
    if (src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0) {
        if (src.reversed) {
            right.warpPlayStart = src.warpPlayStart;
            right.warpPlayEnd = std::max(src.warpPlayStart, src.warpPlayEndEff() - startLocal);
        } else {
            right.warpPlayStart = src.warpPlayStart + startLocal; right.warpPlayEnd = src.warpPlayEndEff();
        }
        buildWarpCache(right, spb, devSR);
    } else {
        const int64_t cut = static_cast<int64_t>(audioBeatsToSrcFrames(src, startLocal, spb, devSR));
        if (!src.reversed)   // reversed drops the region's tail instead, so the offset stays
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

int32_t Engine::pasteAudioFrames(int32_t trackId, const float* interleaved, int64_t frames, double startBeat,
                                 const std::string& name) {
    if (!interleaved || frames <= 0) return -1;
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return -1;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return -1;
    startBeat = std::max(0.0, startBeat);

    auto sb = std::make_shared<SampleBuffer>();
    sb->channels = 2;
    sb->frames = frames;
    sb->sourceSampleRate = devSR;   // the capture ran at the device rate
    sb->samples.assign(interleaved, interleaved + static_cast<size_t>(frames) * 2);

    auto nt = cloneTrack(*old);
    audioOverwriteRange(nt->clips, startBeat, startBeat + static_cast<double>(frames) / spb, spb, devSR);
    AudioClip clip;
    clip.sample = std::move(sb);
    clip.startBeat = startBeat;
    clip.lengthFrames = frames;
    clip.name = name;
    nt->clips.push_back(std::move(clip));
    const int32_t idx = static_cast<int32_t>(nt->clips.size()) - 1;
    republishWithTrack(trackId, nt);
    lastPlaced_ = { { trackId, idx } };
    return idx;
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
    g->scenes = authoring_->scenes;
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

bool Engine::moveClipBlock(const std::vector<ClipMoveReq>& moves) {
    lastPlaced_.clear();
    lastMoveKeptDeviceAuto_ = false;
    if (moves.empty() || !authoring_) return false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();

    // Validate everything up front (all-or-nothing) and clone each touched track once.
    std::map<int32_t, std::shared_ptr<Track>> clones;
    std::set<std::pair<int32_t,int32_t>> seen;
    for (const auto& m : moves) {
        auto src = findTrackAuthoring(m.srcTrackId);
        auto dst = findTrackAuthoring(m.dstTrackId);
        if (!src || !dst || src->type() != dst->type() || src->type() == TrackType::Return) return false;
        if (m.srcTrackId == kMasterTrackId || m.dstTrackId == kMasterTrackId) return false;
        const size_t n = src->type() == TrackType::Instrument ? src->midiClips.size() : src->clips.size();
        if (m.clipIndex < 0 || m.clipIndex >= static_cast<int32_t>(n)) return false;
        if (!seen.emplace(m.srcTrackId, m.clipIndex).second) return false;   // same clip twice
        if (!clones.count(m.srcTrackId)) clones[m.srcTrackId] = cloneTrack(*src);
        if (!clones.count(m.dstTrackId)) clones[m.dstTrackId] = cloneTrack(*dst);
    }

    // Snapshot each moving clip (+ the automation in its span) from the pre-move tracks.
    struct Item { const ClipMoveReq* req; bool midi; MidiClip mc; AudioClip ac; double oldStart, len, newStart;
                  bool sameTrack, layoutMatch; std::vector<AutomationLane> snips; };
    std::vector<Item> items; items.reserve(moves.size());
    for (const auto& m : moves) {
        const Track& src = *clones[m.srcTrackId];
        Item it{ &m, src.type() == TrackType::Instrument, {}, {}, 0, 0, std::max(0.0, m.newStartBeat),
                 m.srcTrackId == m.dstTrackId, true, {} };
        if (it.midi) { it.mc = src.midiClips[m.clipIndex]; it.oldStart = it.mc.startBeat; it.len = it.mc.lengthBeats; it.mc.startBeat = it.newStart; }
        else         { it.ac = src.clips[m.clipIndex]; it.oldStart = it.ac.startBeat; it.len = audioDisplayLenBeats(it.ac, spb, devSR); it.ac.startBeat = it.newStart; }
        it.layoutMatch = it.sameTrack || tracksDeviceLayoutMatch(src, *clones[m.dstTrackId]);
        const bool follow = !automationLock_ && it.len > 1e-6 && (!it.sameTrack || std::abs(it.newStart - it.oldStart) > 1e-9);
        if (follow) it.snips = captureClipAutomation(src, it.oldStart, it.oldStart + it.len);
        items.push_back(std::move(it));
    }

    // Rebuild each touched track's clip list: clips staying on the track keep their slot (new
    // start), clips leaving are dropped, and every stationary clip is carved by all incoming
    // ranges. Cross-track arrivals are appended afterwards.
    std::map<int32_t, std::vector<std::pair<double,double>>> incoming;   // dst track → landing ranges
    for (const auto& it : items) incoming[it.req->dstTrackId].emplace_back(it.newStart, it.newStart + it.len);
    std::map<std::pair<int32_t,int32_t>, size_t> itemOf;                  // (src track, index) → item
    for (size_t i = 0; i < items.size(); ++i) itemOf[{ items[i].req->srcTrackId, items[i].req->clipIndex }] = i;
    std::vector<int32_t> newIndex(items.size(), -1);

    for (auto& [tid, nt] : clones) {
        const auto& ranges = incoming[tid];
        auto rebuild = [&](auto& clips, auto carve) {
            std::remove_reference_t<decltype(clips)> out; out.reserve(clips.size() + 2 * ranges.size());
            for (int32_t ci = 0; ci < static_cast<int32_t>(clips.size()); ++ci) {
                auto f = itemOf.find({ tid, ci });
                if (f != itemOf.end()) {
                    auto& it = items[f->second];
                    if (!it.sameTrack) continue;   // leaves this track
                    newIndex[f->second] = static_cast<int32_t>(out.size());
                    if constexpr (std::is_same_v<std::decay_t<decltype(clips[0])>, MidiClip>) out.push_back(it.mc);
                    else out.push_back(it.ac);
                    continue;
                }
                std::remove_reference_t<decltype(clips)> pieces{ clips[ci] };
                for (const auto& [s, e] : ranges) carve(pieces, s, e);
                for (auto& p : pieces) out.push_back(std::move(p));
            }
            clips.swap(out);
        };
        if (nt->type() == TrackType::Instrument)
            rebuild(nt->midiClips, [](std::vector<MidiClip>& v, double s, double e){ midiOverwriteRange(v, s, e); });
        else
            rebuild(nt->clips, [&](std::vector<AudioClip>& v, double s, double e){ audioOverwriteRange(v, s, e, spb, devSR); });
    }
    for (size_t i = 0; i < items.size(); ++i) {
        auto& it = items[i];
        if (it.sameTrack) continue;
        Track& dst = *clones[it.req->dstTrackId];
        if (it.midi) { dst.midiClips.push_back(it.mc); newIndex[i] = static_cast<int32_t>(dst.midiClips.size()) - 1; }
        else         { dst.clips.push_back(it.ac);     newIndex[i] = static_cast<int32_t>(dst.clips.size()) - 1; }
    }

    // Automation follows (req 8.3.1/8.3.4): lift every source span first, then re-land, so
    // one clip's landing never gets erased by another's lift. Cross-track moves between
    // differing device layouts carry only Volume/Pan; device/plugin points stay behind.
    constexpr double eps = 1e-6;
    for (auto& it : items) {
        if (it.snips.empty()) continue;
        Track& src = *clones[it.req->srcTrackId];
        if (it.layoutMatch) { removeAutomationInRange(src, it.oldStart, it.oldStart + it.len); continue; }
        for (const auto& s : it.snips)
            if (s.target != AutomationTarget::Volume && s.target != AutomationTarget::Pan) { lastMoveKeptDeviceAuto_ = true; break; }
        for (auto& lane : src.automation) {
            if (lane.target != AutomationTarget::Volume && lane.target != AutomationTarget::Pan) continue;
            auto& pts = lane.points;
            pts.erase(std::remove_if(pts.begin(), pts.end(),
                        [&](const AutomationPoint& p){ return p.beat >= it.oldStart - eps && p.beat <= it.oldStart + it.len + eps; }),
                      pts.end());
        }
    }
    for (auto& it : items)
        if (!it.snips.empty())
            applyClipAutomation(*clones[it.req->dstTrackId], it.req->dstTrackId, it.snips, it.newStart, it.len, it.layoutMatch);

    for (size_t i = 0; i < items.size(); ++i) lastPlaced_.emplace_back(items[i].req->dstTrackId, newIndex[i]);

    // Publish every touched track at once — a single undo checkpoint for the whole group.
    pushUndo();
    auto g = std::make_shared<Graph>();
    g->sceneCount   = authoring_->sceneCount;
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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

// --- consolidate (⌘J): one clip per track over a range ------------------------
namespace {
// One source clip's audible slice [a,b) of a consolidate range (timeline beats), with the
// clip's start and its envelope (null/empty = unity) for stitching.
struct EnvSpan { double a, b, clipStart; const AutomationLane* lane; };

// Stitch per-clip envelopes into one lane in consolidated-clip-local beats: each span keeps
// its clip's curve, clips without one and the gaps between clips hold unity, and a boundary
// between two clips is a step (two points on the same beat). Empty when no span carries an
// envelope, so the result keeps the no-envelope fast path.
AutomationLane stitchEnvelopes(const std::vector<EnvSpan>& spans, double start, double end) {
    AutomationLane out;
    if (std::none_of(spans.begin(), spans.end(), [](const EnvSpan& s) { return s.lane && !s.lane->points.empty(); }))
        return out;
    auto add = [&](double beat, float v, float curve = 0.0f) { out.points.push_back({beat - start, v, curve}); };
    double cursor = start;
    for (const auto& s : spans) {
        const double a = std::max(s.a, cursor), b = s.b;
        if (b <= a) continue;
        if (a > cursor) { add(cursor, 1.0f); add(a, 1.0f); }
        const bool has = s.lane && !s.lane->points.empty();
        add(a, has ? s.lane->valueAt(a - s.clipStart) : 1.0f);
        if (has)
            for (const auto& p : s.lane->points) {
                const double x = s.clipStart + p.beat;
                if (x > a && x < b) add(x, p.value, p.curve);
            }
        add(b, has ? s.lane->valueAt(b - s.clipStart) : 1.0f);
        cursor = b;
    }
    if (cursor < end) { add(cursor, 1.0f); add(end, 1.0f); }
    return out;
}

// The covered MIDI clips merged into one clip over [s,e): exactly the notes that play —
// a note must start inside its clip's window and the range, and its tail stops at its clip's
// end (or the range end), as playback cuts it. Velocity envelopes are baked into the notes.
MidiClip consolidateMidiClips(const std::vector<MidiClip>& clips, double s, double e) {
    constexpr double eps = 1e-6;
    std::vector<const MidiClip*> src;
    for (const auto& c : clips)
        if (c.startBeat + c.lengthBeats > s + eps && c.startBeat < e - eps) src.push_back(&c);
    std::sort(src.begin(), src.end(), [](const MidiClip* x, const MidiClip* y) { return x->startBeat < y->startBeat; });

    MidiClip out;
    out.startBeat = s;
    out.lengthBeats = e - s;
    for (const MidiClip* c : src) if (!c->name.empty()) { out.name = c->name; break; }
    std::vector<EnvSpan> spans;
    for (const MidiClip* c : src) {
        if (!c->active) continue;   // a deactivated clip is silent
        const double cs = c->startBeat, ce = std::min(cs + c->lengthBeats, e);
        spans.push_back({std::max(cs, s), ce, cs, &c->volumeEnvelope});
        const bool hasVel = !c->velocityEnvelope.points.empty();
        for (const Note& n : c->notes) {
            if (n.startBeat < 0.0 || n.startBeat >= c->lengthBeats) continue;
            const double on = cs + n.startBeat;
            if (on < s - eps || on >= e - eps) continue;
            Note m = n;
            m.startBeat = std::max(0.0, on - s);
            m.lengthBeats = std::min(on + n.lengthBeats, ce) - on;
            if (m.lengthBeats <= 0.0) continue;
            if (hasVel) m.velocity = n.velocity * std::clamp(c->velocityEnvelope.valueAt(n.startBeat), 0.0f, 1.0f);
            out.notes.push_back(m);
        }
    }
    out.volumeEnvelope = stitchEnvelopes(spans, s, e);
    return out;
}
} // namespace

// Bounce what the covered clips play over [s,e) — the same renderer the audio thread uses,
// so gain, varispeed, warp, edge fades and clip envelopes are all baked — into a new
// device-rate stereo sample, placed as one clip at `s`. If any audible source was warped the
// result is warped too (neutral markers, that clip's mode) so it keeps following tempo; an
// all-unwarped range stays an unwarped, sample-exact copy.
AudioClip Engine::consolidateAudioClips(const std::vector<AudioClip>& clips, double s, double e) {
    constexpr double eps = 1e-6;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    std::vector<AudioClip> src;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR);
        if (ce <= s + eps || cs >= e - eps) continue;
        src.push_back(c);
        // A deferred (project-load) warp cache isn't built yet; the renderer would skip it.
        if (src.back().warpEnabled && !src.back().warpCache) buildWarpCache(src.back(), spb, devSR);
    }
    std::sort(src.begin(), src.end(), [](const AudioClip& x, const AudioClip& y) { return x.startBeat < y.startBeat; });

    const int64_t frames = std::max<int64_t>(1, std::llround((e - s) * spb));
    auto sb = std::make_shared<SampleBuffer>();
    sb->channels = 2;
    sb->frames = frames;
    sb->sourceSampleRate = devSR;
    sb->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);
    constexpr int64_t kBlock = 4096;
    for (int64_t off = 0; off < frames; off += kBlock) {
        const int32_t n = static_cast<int32_t>(std::min(kBlock, frames - off));
        renderAudioClipsRaw(src, sb->samples.data() + off * 2, n, s * spb + static_cast<double>(off), spb);
    }

    AudioClip out;
    out.sample = std::move(sb);
    out.startBeat = s;
    out.lengthFrames = frames;
    for (const auto& c : src) if (!c.name.empty()) { out.name = c.name; break; }
    const auto warped = std::find_if(src.begin(), src.end(), [](const AudioClip& c) { return c.active && c.warpEnabled; });
    if (warped != src.end()) {
        out.warpEnabled = true;
        out.warpMode = warped->warpMode;
        seedNeutralWarp(out, spb, devSR);
        configureClipWarp(out, spb, devSR);
    }
    return out;
}

bool Engine::consolidateRange(const std::vector<int32_t>& trackIds, double start, double end) {
    start = std::max(0.0, start);
    if (end <= start + 1e-9 || trackIds.empty()) return false;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return false;
    auto listed = [&](int32_t id){ return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    std::vector<std::pair<int32_t,int32_t>> placed;
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->scenes = authoring_->scenes;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        const bool clipTrack = t->type() == TrackType::Instrument || t->type() == TrackType::Audio;
        if (!clipTrack || !listed(t->id()) || !rangeTouchesTrack(*t, start, end, spb, devSR))
        { g->tracks.push_back(t); continue; }
        auto nt = cloneTrack(*t);
        if (nt->type() == TrackType::Instrument) {
            MidiClip merged = consolidateMidiClips(nt->midiClips, start, end);
            midiOverwriteRange(nt->midiClips, start, end);
            nt->midiClips.push_back(std::move(merged));
            placed.emplace_back(nt->id(), static_cast<int32_t>(nt->midiClips.size()) - 1);
        } else {
            AudioClip bounced = consolidateAudioClips(nt->clips, start, end);
            audioOverwriteRange(nt->clips, start, end, spb, devSR);
            nt->clips.push_back(std::move(bounced));
            placed.emplace_back(nt->id(), static_cast<int32_t>(nt->clips.size()) - 1);
        }
        g->tracks.push_back(nt);
    }
    if (placed.empty()) return false;
    pushUndo();
    publishRaw(std::move(g));
    lastPlaced_ = std::move(placed);
    return true;
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
        if (!audioClipRef(*nt, clipIndex)) return false;
        AudioClip& c = (*audioClipRef(*nt, clipIndex));
        const double spb = transport_.samplesPerBeat();
        const double devSR = transport_.sampleRate();
        const double delta = newStartBeat - c.startBeat;
        const double want = audioBeatsToSrcFrames(c, newLengthBeats, spb, devSR);
        if (c.reversed) {
            // Mirrored: a reversed clip's audible head is the region's END, so the left
            // edge moves that end in and the length is taken backwards from it.
            const double headEnd = audioReverseHead(c, delta, spb, devSR);
            const double off = std::max(0.0, headEnd - want);
            c.startBeat = newStartBeat;
            c.sourceOffsetFrames = off;
            c.lengthFrames = static_cast<int64_t>(std::max(0.0, headEnd - off));
        } else {
            double newOffset = c.sourceOffsetFrames + audioBeatsToSrcFrames(c, delta, spb, devSR);
            if (newOffset < 0) newOffset = 0;
            c.startBeat = newStartBeat;
            c.sourceOffsetFrames = newOffset;
            c.lengthFrames = static_cast<int64_t>(want);
        }
    }
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::resizeAudioClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    if (newStartBeat < 0) newStartBeat = 0;
    if (newLengthBeats < 0.25) newLengthBeats = 0.25;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    AudioClip& c = (*audioClipRef(*nt, clipIndex));

    if (c.warpEnabled) {
        // Warped: TRIM the played window — do NOT stretch (stretch is the
        // markers / BPM chip). Markers, warpBeats and the stream are untouched; we only
        // move the [warpPlayStart, warpPlayEnd] window and the timeline start. Dragging
        // the left edge shifts playStart (and startBeat); the right edge moves playEnd.
        constexpr double kMinBeat = 0.05;
        const double d = newStartBeat - c.startBeat;
        double ps, pe;
        if (c.reversed) {   // the window's END is the audible head, so the edges swap roles
            pe = std::clamp(c.warpPlayEndEff() - d, kMinBeat, c.warpBeats);
            ps = std::clamp(pe - newLengthBeats, 0.0, pe - kMinBeat);
        } else {
            ps = std::clamp(c.warpPlayStart + d, 0.0, std::max(0.0, c.warpBeats - kMinBeat));
            pe = std::clamp(ps + newLengthBeats, ps + kMinBeat, c.warpBeats);
        }
        c.startBeat = newStartBeat;
        c.warpPlayStart = ps;
        c.warpPlayEnd = pe;
        configureClipWarp(c, spb, devSR);   // the played window is baked into the cache
        syncSessionAudioSlot(*nt, clipIndex);
        republishWithTrack(trackId, nt);
        return true;
    }

    // Unwarped: trim within the source; if the drag asks for more than the source
    // can provide, auto-enable warp and stretch the current content to fit (this is
    // what lets a short one-shot be dragged out to a whole bar).
    const double delta = newStartBeat - c.startBeat;
    const int64_t wantFrames = static_cast<int64_t>(audioBeatsToSrcFrames(c, newLengthBeats, spb, devSR));
    // A reversed clip reads the region backwards, so the source a drag can reveal sits
    // BEFORE its audible head (the region's end) rather than after the offset: the head is
    // the anchor and the region grows leftwards from it.
    double newOffset;
    int64_t srcAvail;
    if (c.reversed) {
        const double head = audioReverseHead(c, delta, spb, devSR);
        srcAvail = static_cast<int64_t>(head);
        newOffset = head - static_cast<double>(std::min<int64_t>(wantFrames, srcAvail));
    } else {
        newOffset = c.sourceOffsetFrames + audioBeatsToSrcFrames(c, delta, spb, devSR);
        if (newOffset < 0) newOffset = 0;
        srcAvail = c.sample ? (c.sample->frames - static_cast<int64_t>(newOffset)) : 0;
    }
    if (newOffset < 0) newOffset = 0;
    c.startBeat = newStartBeat;
    c.sourceOffsetFrames = newOffset;
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
    syncSessionAudioSlot(*nt, clipIndex);
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
    if (!audioClipRef(*old, clipIndex)) return false;
    const AudioClip& oc = (*audioClipRef(*old, clipIndex));
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
    AudioClip& c = (*audioClipRef(*nt, clipIndex));
    c.sourceOffsetFrames = off;
    c.lengthFrames = len;
    syncSessionAudioSlot(*nt, clipIndex);
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
        const AudioClip src = nt->clips[clipIndex];
        const double local = atBeat - src.startBeat;
        if (local <= 0.0 || local >= audioDisplayLenBeats(src, spb, devSR)) return -1;
        // The same carves the range ops use: they cut in played time, so warped halves
        // split over the beat window (not lengthFrames, which warp ignores) and a reversed
        // clip's halves keep the audio each one sounded. Each warped half also gets its own
        // stretch cache, since the stretcher's seek state is per-clip.
        nt->clips[clipIndex] = audioCarveLeft(src, local, spb, devSR);
        nt->clips.insert(nt->clips.begin() + clipIndex + 1, audioCarveRight(src, local, spb, devSR));
    }
    republishWithTrack(trackId, nt);
    return clipIndex + 1;
}

int32_t Engine::duplicateClip(int32_t trackId, int32_t clipIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const double spb = transport_.samplesPerBeat(), devSR = transport_.sampleRate();
    auto nt = cloneTrack(*old);
    int32_t newIdx;
    if (nt->type() == TrackType::Instrument) {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->midiClips.size())) return -1;
        MidiClip d = nt->midiClips[clipIndex];
        const double srcStart = d.startBeat, len = d.lengthBeats;
        auto snips = captureClipAutomation(*nt, srcStart, srcStart + len);
        // Land right after the source, overwriting whatever sits there (like a drag).
        d.startBeat = srcStart + len;
        midiOverwriteRange(nt->midiClips, d.startBeat, d.startBeat + len);
        nt->midiClips.push_back(d);
        newIdx = static_cast<int32_t>(nt->midiClips.size()) - 1;
        applyClipAutomation(*nt, trackId, snips, d.startBeat, len, /*sameTrack*/ true);
    } else {
        if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(nt->clips.size())) return -1;
        AudioClip d = nt->clips[clipIndex];
        const double len = audioDisplayLenBeats(d, spb, devSR);
        const double srcStart = d.startBeat;
        auto snips = captureClipAutomation(*nt, srcStart, srcStart + len);
        d.startBeat = srcStart + len;
        audioOverwriteRange(nt->clips, d.startBeat, d.startBeat + len, spb, devSR);
        if (d.warpEnabled) configureClipWarp(d, spb, devSR);   // the copy needs its own stretcher
        nt->clips.push_back(d);
        newIdx = static_cast<int32_t>(nt->clips.size()) - 1;
        applyClipAutomation(*nt, trackId, snips, d.startBeat, len, /*sameTrack*/ true);
    }
    republishWithTrack(trackId, nt);
    return newIdx;
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
void Engine::placeBlock(const std::vector<BlockClip>& items, double placedStart, bool overwrite) {
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
    // In overwrite mode the block lands exactly at placedStart and carves what it covers
    // instead (all carves first, so they never cut a copy placed by an earlier item).
    double d = 0.0;
    bool moved = !overwrite;
    if (overwrite) {
        for (const auto& b : items) {
            auto it = clones.find(b.trackId);
            if (it == clones.end()) continue;
            const double s = placedStart + b.relStart, e = s + b.len;
            if (b.kind == 1) midiOverwriteRange(it->second->midiClips, s, e);
            else audioOverwriteRange(it->second->clips, s, e, spb, devSR);
        }
    }
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    // sourceTrackId >= 0 remaps the whole block onto that track (see remapBlock).
    std::vector<BlockClip> items = sourceTrackId >= 0 ? remapBlock(clipboardBlock_, sourceTrackId) : clipboardBlock_;
    if (items.empty()) return 0;
    placeBlock(items, atBeat);
    return static_cast<int32_t>(lastPlaced_.size());
}

// Remap a block by the track-index delta from its top track to the destination (so a single
// clip lands on that track; a multi-track block shifts together). Clips that fall off the
// track list or hit a type-mismatched track are skipped; a remapped clip's device/plugin
// automation is dropped (indices are positional).
std::vector<Engine::BlockClip> Engine::remapBlock(const std::vector<BlockClip>& items, int32_t destTrackId) const {
    auto idxOf = [&](int32_t tid) -> int {
        for (size_t i = 0; i < authoring_->tracks.size(); ++i)
            if (authoring_->tracks[i]->id() == tid) return static_cast<int>(i);
        return -1;
    };
    const int destIdx = idxOf(destTrackId);
    int topIdx = std::numeric_limits<int>::max();
    for (const auto& b : items) { int ix = idxOf(b.trackId); if (ix >= 0) topIdx = std::min(topIdx, ix); }
    if (destIdx < 0 || topIdx == std::numeric_limits<int>::max()) return {};
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
    return remapped;
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
    placeBlock(items, absStart + len, /*overwrite*/ true);   // right after itself, like a drag
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
    // A Session slot (SessionClip index, MIDI or audio): its name is the slot's.
    if (const int32_t scene = sessionSceneOfClip(clipIndex); scene >= 0) return setSessionClipName(trackId, scene, name);
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
    if (const int32_t scene = sessionSceneOfClip(clipIndex); scene >= 0) {
        std::string n;
        return sessionClipName(trackId, scene, n) ? copyStr(n, out, cap) : copyStr(std::string(), out, cap);
    }
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
    nt->setMonitor(src.monitor());
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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
    g->scenes = authoring_->scenes;
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

// --- multi-track sets (header multi-selection) -----------------------------
std::vector<std::shared_ptr<Track>> Engine::expandTrackSet(const int32_t* ids, int32_t n) const {
    std::vector<std::shared_ptr<Track>> out;
    if (!ids || n <= 0) return out;
    std::set<int32_t> want(ids, ids + n);
    // A track belongs when it or any ancestor group is in the set.
    auto inSet = [&](const Track& t) {
        if (want.count(t.id())) return true;
        for (int32_t a = t.groupId(), hops = 0; a >= 0 && hops < 64; ++hops) {
            if (want.count(a)) return true;
            auto p = findTrackAuthoring(a);
            a = p ? p->groupId() : -1;
        }
        return false;
    };
    for (auto& t : authoring_->tracks) if (inSet(*t)) out.push_back(t);
    return out;
}

std::vector<int32_t> Engine::insertTrackClones(const std::vector<std::shared_ptr<Track>>& src,
                                               int32_t afterId, int32_t rootParent, bool keepParent) {
    std::map<int32_t, int32_t> remap;   // source id -> copy id
    for (auto& s : src) if (s->type() != TrackType::Return) remap[s->id()] = nextTrackId_++;
    std::vector<std::shared_ptr<Track>> clones;
    std::vector<int32_t> newIds;
    for (auto& s : src) {
        if (s->type() == TrackType::Return) continue;   // return-bus identity can't be duplicated
        auto nt = deepCloneTrack(*s, remap[s->id()]);
        auto it = remap.find(s->groupId());
        nt->setGroupId(it != remap.end() ? it->second : keepParent ? s->groupId() : rootParent);
        clones.push_back(nt);
        newIds.push_back(nt->id());
    }
    if (clones.empty()) return newIds;

    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->scenes = authoring_->scenes;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size() + clones.size());
    for (auto& t : authoring_->tracks) g->tracks.push_back(t);
    int32_t insertAt = -1;
    for (int32_t i = 0; i < static_cast<int32_t>(g->tracks.size()); ++i)
        if (g->tracks[i]->id() == afterId && g->tracks[i]->type() != TrackType::Return) { insertAt = i + 1; break; }
    if (insertAt < 0) {
        // Append after the last non-return track so the copies sit with the regular tracks.
        insertAt = static_cast<int32_t>(g->tracks.size());
        for (int32_t i = 0; i < static_cast<int32_t>(g->tracks.size()); ++i)
            if (g->tracks[i]->type() == TrackType::Return) { insertAt = i; break; }
    }
    g->tracks.insert(g->tracks.begin() + insertAt, clones.begin(), clones.end());
    publish(std::move(g));
    recomputePdc();
    return newIds;
}

int32_t Engine::duplicateTracks(const int32_t* ids, int32_t n, int32_t* outIds, int32_t cap) {
    auto set = expandTrackSet(ids, n);
    set.erase(std::remove_if(set.begin(), set.end(),
                             [](const std::shared_ptr<Track>& t) { return t->type() == TrackType::Return; }),
              set.end());
    if (set.empty()) return -1;
    auto newIds = insertTrackClones(set, set.back()->id(), -1, /*keepParent*/ true);
    const int32_t count = static_cast<int32_t>(newIds.size());
    if (outIds) for (int32_t i = 0; i < count && i < cap; ++i) outIds[i] = newIds[i];
    return count > 0 ? count : -1;
}

bool Engine::removeTracks(const int32_t* ids, int32_t n) {
    auto set = expandTrackSet(ids, n);
    if (set.empty()) return false;
    std::set<int32_t> drop;
    for (auto& t : set) {
        // Plugin windows shouldn't outlive the track (it survives in undo history, so only hide).
        if (t->instrument) t->instrument->closeEditor();
        for (auto& d : t->devices) if (d) d->closeEditor();
        drop.insert(t->id());
    }
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;
    g->scenes = authoring_->scenes;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks)
        if (!drop.count(t->id())) g->tracks.push_back(t);
    publish(std::move(g));
    recomputePdc();
    return true;
}

// --- track clipboard (copy/cut/paste) --------------------------------------
bool Engine::copyTrack(int32_t trackId) { return copyTracks(&trackId, 1); }

bool Engine::copyTracks(const int32_t* ids, int32_t n) {
    auto set = expandTrackSet(ids, n);
    if (set.empty()) return false;
    std::vector<std::shared_ptr<Track>> clip;
    clip.reserve(set.size());
    for (auto& t : set) clip.push_back(deepCloneTrack(*t, t->id()));   // source id kept for re-linking
    trackClipboard_ = std::move(clip);
    return true;
}

bool Engine::hasTrackClipboard() const { return !trackClipboard_.empty(); }

int32_t Engine::pasteTrack() {
    std::vector<int32_t> ids(trackClipboard_.size());
    int32_t n = pasteTracks(-1, ids.data(), static_cast<int32_t>(ids.size()));
    return n > 0 ? ids[0] : -1;
}

int32_t Engine::pasteTracks(int32_t afterTrackId, int32_t* outIds, int32_t cap) {
    if (trackClipboard_.empty()) return -1;
    // Land next to the anchor, in its group; without one, top-level (the source groups may be gone).
    int32_t parent = -1;
    auto anchor = findTrackAuthoring(afterTrackId);
    if (anchor && anchor->type() != TrackType::Return && anchor->id() != kMasterTrackId)
        parent = anchor->groupId();
    else afterTrackId = -1;
    auto newIds = insertTrackClones(trackClipboard_, afterTrackId, parent, /*keepParent*/ false);
    const int32_t count = static_cast<int32_t>(newIds.size());
    if (outIds) for (int32_t i = 0; i < count && i < cap; ++i) outIds[i] = newIds[i];
    return count > 0 ? count : -1;
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

int64_t Engine::beginFreeze(int32_t trackId, double lengthBeats, double tailSec) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    if (t->type() != TrackType::Instrument && t->type() != TrackType::Audio) return 0;
    const double sr  = transport_.sampleRate() > 0.0 ? transport_.sampleRate() : 44100.0;
    const double spb = transport_.samplesPerBeat();
    if (spb <= 0.0 || lengthBeats <= 0.0) return 0;
    // tailSec lets reverb/delay tails ring past the last clip (0 for an exact-range bounce).
    int64_t frames = static_cast<int64_t>(std::ceil(lengthBeats * spb)) + static_cast<int64_t>(std::max(0.0, tailSec) * sr);
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

int64_t Engine::takeFreezeCapture(float* out, int64_t capFrames) {
    const int64_t n = (out && capFrames > 0) ? std::min(capFrames, freezeCursor_) : 0;
    if (n > 0) std::memcpy(out, freezeCaptureBuf_.data(), static_cast<size_t>(n) * 2 * sizeof(float));
    cancelFreeze();   // disarm + free the capture; the track itself is untouched
    return n;
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
