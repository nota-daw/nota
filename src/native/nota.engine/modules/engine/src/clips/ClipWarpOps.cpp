// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — tracks, mixer strips, send/return buses, arrangement clips (geometry + move/trim/split/duplicate/delete), MIDI clips & notes, and audio-clip/sample persistence.

#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "session/SessionOps.h"
#include "model/Track.h"
#include "model/Graph.h"
#include "core/TempoMap.h"
#include "nota/media/Warp.h"

#include <algorithm>
#include <cstdint>
#include <atomic>
#include <cstring>
#include <cmath>
#include <memory>
#include <thread>
#include <vector>
#include <utility>

namespace nota::detail {

ClipOps::~ClipOps() = default;

namespace {
constexpr int32_t kWarpBuildChunk = 16384; // matches WarpStream::kMaxBlock (its out scratch cap)

// Played-window length in device frames for a warp clip (0 = not a buildable warp clip).
int64_t warpCacheFrames(const AudioClip& c, double spb, double devSR) {
    if (!c.warpEnabled || !c.sample || devSR <= 0.0 || spb <= 0.0 || c.sample->sourceSampleRate <= 0.0 ||
        c.warpMarkers.size() < 2)
        return 0;
    return std::llround(c.warpPlayLen() * spb);
}

// What a cache built for c right now would be stretched from (see WarpInputs).
WarpInputs warpInputsOf(const AudioClip& c, double spb, double devSR) {
    WarpInputs in;
    in.sample = c.sample.get();
    in.markers = c.warpMarkers;
    in.warpBeats = c.warpMarkers.size() >= 2 ? c.warpMarkers.back().beat : c.warpBeats;
    in.playStart = c.warpPlayStart;
    in.playEnd = c.warpPlayEndEff();
    in.mode = c.warpMode;
    in.pitch = c.pitchSemitones;
    in.spb = spb;
    in.devSR = devSR;
    return in;
}

bool tempoWarpPlan(const AudioClip& c, const std::shared_ptr<const TempoMap>& map, double devSR,
    WarpInputs& renderInputs, int64_t& frames) {
    if (!map || !c.warpEnabled || !c.sample || c.warpMarkers.size() < 2 || devSR <= 0) return false;
    const double from = c.warpPlayStart, to = c.warpPlayEndEff();
    const double origin = map->secondsAt(c.startBeat);
    const double length = to - from;
    std::vector<double> beats{0, length};
    for (const auto& p : c.warpMarkers)
        if (p.beat > from && p.beat < to) beats.push_back(c.reversed ? to - p.beat : p.beat - from);
    for (const auto& n : map->nodes) {
        const double b = n.beat - c.startBeat;
        if (b > 0 && b < length) beats.push_back(b);
    }
    std::sort(beats.begin(), beats.end());
    beats.erase(std::unique(beats.begin(), beats.end()), beats.end());
    renderInputs = warpInputsOf(c, devSR, devSR);
    renderInputs.markers.clear();
    for (double local : beats) {
        const double b = c.reversed ? to - local : from + local;
        auto hi = std::upper_bound(c.warpMarkers.begin(), c.warpMarkers.end(), b,
            [](double beat, const WarpMarker& p) { return beat < p.beat; });
        const auto& p = hi == c.warpMarkers.begin() ? c.warpMarkers.front() : *(hi - 1);
        const auto& q = hi == c.warpMarkers.end() ? c.warpMarkers.back() : *hi;
        const double src =
            p.beat == q.beat ? p.srcFrame : p.srcFrame + (q.srcFrame - p.srcFrame) * (b - p.beat) / (q.beat - p.beat);
        // The virtual source advances forwards while its reader walks PCM backwards.
        // Reverse musical positions before stretching, rather than mirroring elapsed time.
        renderInputs.markers.push_back({c.reversed ? static_cast<double>(c.sample->frames) - src : src,
            map->secondsAt(c.startBeat + local) - origin});
    }
    renderInputs.warpBeats = renderInputs.markers.back().beat;
    renderInputs.playStart = 0;
    renderInputs.playEnd = renderInputs.warpBeats;
    renderInputs.spb = renderInputs.devSR = devSR;
    frames = std::llround(renderInputs.warpBeats * devSR);
    return frames > 0;
}

// Stretch `frames` device frames of the played window described by `in` into `out`
// (interleaved stereo). The stream primes once on the first chunk, then runs seamlessly
// (drift-free feed) to the end. Gain is 1 (applied at read time). Stops early — returning
// false — when `cancel` is raised; `done` (optional) tracks progress for a worker build.
bool renderWarpWindow(const WarpInputs& in, const SampleBuffer& sample, float* out, int64_t frames,
    const std::atomic<bool>* cancel = nullptr, std::atomic<int64_t>* done = nullptr, bool reverseSource = false) {
    ClipWarpStream st;
    st.configure(sample.channels, in.mode, in.pitch, sample.sourceSampleRate, in.devSR, reverseSource);
    const double warpOffset = in.playStart * in.spb;
    for (int64_t off = 0; off < frames; off += kWarpBuildChunk) {
        if (cancel && cancel->load(std::memory_order_relaxed)) return false;
        const int32_t n = static_cast<int32_t>(std::min<int64_t>(kWarpBuildChunk, frames - off));
        st.render(out + off * 2, n, warpOffset + static_cast<double>(off), in.markers, in.warpBeats, sample, in.spb,
            in.devSR, 1.0f);
        if (done) done->store(off + n, std::memory_order_relaxed);
    }
    return true;
}

// Build a warped clip's offline stretch cache, or clear it when off.
// Runs the streaming stretcher ONCE over the whole played window on the authoring
// thread and captures the device-rate output, so the audio thread just copies from
// the buffer (renderAudioClipsRaw) with no realtime stretch and no per-clip-start
// priming spike. Called only on discrete edits / tempo / SR changes (never per
// block); the in-use cache retires with the old snapshot, so it's race-free.
// For a big project this is heavy (stretches every clip's played window), so the load
// path defers it to EngineRuntime::warpBuildStep — see EngineRuntime::configureClipWarp.
// Make the clip's cache current without stretching, when its current cache already holds
// the audio (#299): the same inputs → keep it; the same stretch over a window that contains
// the clip's new played window (a split, a trim, a range carve) → a slice sharing its samples.
// The slice is the very audio the wider window played there, so a cut is seamless. False when
// the cache can't serve (a tempo / pitch / mode / marker change, or a wider window).
bool reuseWarpCache(AudioClip& c, double spb, double devSR) {
    if (!c.warpCache) return false;
    const WarpCache& old = *c.warpCache;
    // Compare borrowed identity first. A split only needs an owned marker copy for
    // its final slice, not temporary vectors for both equality checks.
    const auto& in = old.inputs;
    const double warpBeats = c.warpMarkers.size() >= 2 ? c.warpMarkers.back().beat : c.warpBeats;
    const double from = c.warpPlayStart, to = c.warpPlayEndEff();
    if (!(in.sample == c.sample.get() && in.markers == c.warpMarkers && in.warpBeats == warpBeats &&
            in.mode == c.warpMode && in.pitch == c.pitchSemitones && in.spb == spb && in.devSR == devSR))
        return false;
    if (in.playStart == from && in.playEnd == to) return true;
    constexpr double eps = 1e-9;
    if (from < in.playStart - eps || to > in.playEnd + eps) return false;
    // The offset is taken from the cache that owns the samples, not added up slice by
    // slice, so repeated splits don't each round and drift off the original audio.
    std::shared_ptr<const WarpCache> root = old.base ? old.base : c.warpCache;
    // Both ends are rounded on the owner's frame grid, so neighbouring slices meet exactly.
    const int64_t offset = std::clamp<int64_t>(std::llround((from - root->inputs.playStart) * spb), 0, root->frames);
    const int64_t end = std::clamp<int64_t>(std::llround((to - root->inputs.playStart) * spb), offset, root->frames);
    const int64_t frames = end - offset;
    if (frames <= 0) return false;
    auto slice = std::make_shared<WarpCache>();
    slice->frames = frames;
    slice->spb = old.spb;
    slice->devSR = old.devSR;
    slice->inputs = warpInputsOf(c, spb, devSR);
    slice->base = std::move(root);
    slice->baseOffset = offset;
    c.warpCache = std::move(slice);
    return true;
}

// Length-neutral warp seed: keep the clip's current musical length so toggling
// warp doesn't move audio; tempo changes then stretch it. Two end markers.
// Auto-warp seed: estimate the source tempo, round the clip's true musical length
// to the beat grid, and seed two end markers so the whole clip stretches to that
// many beats at the project tempo (i.e. conforms to the current BPM). Returns the
// detected BPM, or 0 when detection fails (leaving markers untouched by caller).
double seedAutoWarp(AudioClip& c, [[maybe_unused]] double spb, [[maybe_unused]] double devSR, double knownBpm = 0.0) {
    const double srcSR = c.sample->sourceSampleRate;
    const int64_t srcLen = c.effectiveLength();
    const double bpm =
        knownBpm > 0.0 ? knownBpm : detectTempo(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen, srcSR);
    if (bpm <= 0.0 || srcLen <= 0) return 0.0;
    const double durationSec = static_cast<double>(srcLen) / srcSR;
    const double beatsRaw = durationSec * bpm / 60.0;
    double beats = static_cast<double>(std::llround(beatsRaw));
    if (beats < 1.0) beats = 1.0;
    c.warpBeats = beats;
    c.warpMarkers = {{c.sourceOffsetFrames, 0.0}, {c.sourceOffsetFrames + static_cast<double>(srcLen), beats}};
    c.warpPlayStart = 0.0;
    c.warpPlayEnd = 0.0; // fresh warp → play the whole material
    return bpm;
}

// Remember which unwarped region a warp-off clip's markers belong to (see AudioClip).
void stampWarpOffRegion(AudioClip& c) {
    c.warpOffRegionOffset = c.sourceOffsetFrames;
    c.warpOffRegionLength = c.effectiveLength();
}

// A warp-off clip's markers can come back on re-enable only while the region is the one
// they were stamped with — a split or trim made with warp off has moved it since.
bool warpMarkersRestorable(const AudioClip& c) {
    return c.warpMarkers.size() >= 2 && c.warpBeats > 0.0 && c.warpOffRegionLength >= 0 &&
           c.warpOffRegionOffset == c.sourceOffsetFrames && c.warpOffRegionLength == c.effectiveLength();
}

// Keep the trim window valid after warpBeats changes (marker/length edits). Preserves
// the window (clamped); an explicit end past the new length snaps back to full.
void clampWarpPlay(AudioClip& c) {
    if (c.warpPlayStart < 0.0 || c.warpPlayStart >= c.warpBeats) c.warpPlayStart = 0.0;
    if (c.warpPlayEnd > c.warpBeats || c.warpPlayEnd <= c.warpPlayStart) c.warpPlayEnd = 0.0; // 0 = full
}
} // namespace

void buildWarpCache(AudioClip& c, double spb, double devSR) {
    if (c.warpMarkers.size() >= 2) c.warpBeats = c.warpMarkers.back().beat;
    const int64_t frames = warpCacheFrames(c, spb, devSR);
    if (frames <= 0) {
        c.warpCache = nullptr;
        return;
    }
    if (reuseWarpCache(c, spb, devSR)) return;

    auto cache = std::make_shared<WarpCache>();
    cache->frames = frames;
    cache->spb = spb;
    cache->devSR = devSR;
    cache->inputs = warpInputsOf(c, spb, devSR);
    cache->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);
    // The played window [warpPlayStart, warpPlayEndEff] — exactly the audio the old realtime
    // path produced, minus the RT cost. Gain changes don't invalidate the cache.
    renderWarpWindow(cache->inputs, *c.sample, cache->samples.data(), frames);
    c.warpCache = std::move(cache);
}

void seedNeutralWarp(AudioClip& c, double spb, double devSR) {
    c.warpBeats = static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / spb;
    c.warpMarkers = {
        {c.sourceOffsetFrames, 0.0}, {c.sourceOffsetFrames + static_cast<double>(c.effectiveLength()), c.warpBeats}};
    c.warpPlayStart = 0.0;
    c.warpPlayEnd = 0.0; // fresh warp → play the whole material
}

// Build (or clear) a clip's warp cache — unless the caller has asked to defer it.
// During project load `deferWarpBuild_` is set so Apply's many warp edits stay cheap;
// the caches are then filled by warpBuildStep / warpBuildPoll with a progress bar, so a
// project with hundreds of warped clips opens at once instead of freezing for ~a minute.
// Editor edits defer too: the clip keeps its now-stale cache (played clipped to the new
// length) until the background build replaces it, instead of the UI freezing on a long file.
void ClipOps::configureClipWarp(AudioClip& c, double spb, double devSR) {
    if (c.tempoWarp) c.tempoWarp.reset();
    if (!c.warpEnabled) {
        c.warpCache = nullptr;
        return;
    }
    if (c.warpMarkers.size() >= 2) c.warpBeats = c.warpMarkers.back().beat;
    if (warpCacheFrames(c, spb, devSR) > 0 && reuseWarpCache(c, spb, devSR)) return; // free, so even when deferred
    if (deferWarpBuild_) return; // stale (or no) cache until warpBuildStep / warpBuildPoll
    buildWarpCache(c, spb, devSR);
}

void ClipOps::configureTempoWarp(AudioClip& c, const std::shared_ptr<const TempoMap>& map, double devSR, bool force) {
    if (!map || !c.warpEnabled || !c.sample || c.warpMarkers.size() < 2 || devSR <= 0) return;
    const auto inputs = warpInputsOf(c, devSR, devSR);
    if (c.tempoWarpCache() && c.tempoWarp->map == map && c.tempoWarp->startBeat == c.startBeat &&
        c.tempoWarp->reversed == c.reversed && c.tempoWarpCache()->inputs == inputs)
        return;
    if (deferWarpBuild_ && !force) return;
    WarpInputs renderInputs;
    int64_t frames = 0;
    if (!tempoWarpPlan(c, map, devSR, renderInputs, frames)) return;
    auto cache = std::make_shared<WarpCache>();
    cache->frames = frames;
    cache->devSR = cache->spb = devSR;
    cache->inputs = inputs;
    cache->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);
    renderWarpWindow(renderInputs, *c.sample, cache->samples.data(), frames, nullptr, nullptr, c.reversed);
    c.tempoWarp = std::make_shared<const AudioClip::TempoWarp>(
        AudioClip::TempoWarp{std::move(cache), map, c.startBeat, c.reversed});
}

bool ClipOps::warpNeedsBuild(const AudioClip& c, bool arrangement) const {
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    if (arrangement && rt_.authoring_ && rt_.authoring_->tempoMap) {
        WarpInputs renderInputs;
        int64_t frames = 0;
        if (!tempoWarpPlan(c, rt_.authoring_->tempoMap, devSR, renderInputs, frames)) return false;
        return !c.tempoWarpCache() || c.tempoWarp->map != rt_.authoring_->tempoMap ||
               c.tempoWarp->startBeat != c.startBeat || c.tempoWarp->reversed != c.reversed ||
               !(c.tempoWarpCache()->inputs == warpInputsOf(c, devSR, devSR));
    }
    if (warpCacheFrames(c, spb, devSR) <= 0) return false;
    return !c.warpCache || !(c.warpCache->inputs == warpInputsOf(c, spb, devSR));
}

const AudioClip* ClipOps::nextWarpBuildTarget(int32_t& trackId, int32_t& clipIndex) const {
    if (!rt_.authoring_) return nullptr;
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() != TrackType::Audio) continue;
        for (size_t i = 0; i < t->clips.size(); ++i)
            if (warpNeedsBuild(t->clips[i])) {
                trackId = t->id();
                clipIndex = static_cast<int32_t>(i);
                return &t->clips[i];
            }
        for (size_t sc = 0; sc < t->sessionSlots.size(); ++sc) {
            const SessionSlot& sl = t->sessionSlots[sc];
            if (sl.hasClip && warpNeedsBuild(sl.audio, false)) {
                trackId = t->id();
                clipIndex = sessionClipIndex(static_cast<int32_t>(sc));
                return &sl.audio;
            }
        }
    }
    return nullptr;
}

// --- incremental warp-cache build (managed-driven, with a progress bar) --------------
// The load path defers cache building (above); these drive it afterwards in bounded
// chunks on the authoring thread so the UI stays responsive and a progress bar advances
// smoothly. State is a cursor over the ONE clip currently building (its partial buffer +
// stretcher), plus a scan for the next un-built warp clip when idle.

// The authoring clip the build cursor currently targets, if it's still valid to build
// (exists, still warped, still un-cached, same played length). Null when the cursor is
// idle or a mid-build edit changed the clip out from under us (→ abandon + rescan).
const AudioClip* ClipOps::warpBuildTargetClip() const {
    if (!wb_.building) return nullptr;
    auto t = rt_.findTrackAuthoring(wb_.trackId);
    if (!t || t->type() != TrackType::Audio) return nullptr;
    const AudioClip* cp = audioClipRef(*t, wb_.clipIndex); // an arrangement clip or a session slot's
    if (!cp) return nullptr;
    const AudioClip& c = *cp;
    if (!wb_.cache || !warpNeedsBuild(c, wb_.clipIndex >= 0)) return nullptr;
    if (wb_.tempoMap) {
        if (!rt_.authoring_ || rt_.authoring_->tempoMap != wb_.tempoMap || c.startBeat != wb_.tempoStartBeat ||
            c.reversed != wb_.reverseSource ||
            !(warpInputsOf(c, rt_.transport_.sampleRate(), rt_.transport_.sampleRate()) == wb_.cache->inputs))
            return nullptr;
    } else if (!(warpInputsOf(c, rt_.transport_.samplesPerBeat(), rt_.transport_.sampleRate()) == wb_.cache->inputs))
        return nullptr;
    return &c;
}

// Total device frames of warped audio still needing a cache (progress denominator).
int64_t ClipOps::warpBuildRemainingFrames() const {
    if (!rt_.authoring_) return 0;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    int64_t sum = 0;
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() != TrackType::Audio) continue;
        for (auto& c : t->clips)
            if (warpNeedsBuild(c)) {
                WarpInputs renderInputs;
                int64_t frames = 0;
                sum +=
                    rt_.authoring_->tempoMap && tempoWarpPlan(c, rt_.authoring_->tempoMap, devSR, renderInputs, frames)
                        ? frames
                        : warpCacheFrames(c, spb, devSR);
            }
        for (auto& sl : t->sessionSlots)
            if (sl.hasClip && warpNeedsBuild(sl.audio, false)) sum += warpCacheFrames(sl.audio, spb, devSR);
    }
    // The in-progress clip is still un-cached above, so its full length was counted —
    // subtract what's already rendered so the bar reflects real progress.
    if (warpBuildTargetClip()) sum -= wb_.offset;
    if (wj_ && !wj_->cancel.load(std::memory_order_relaxed)) sum -= wj_->done.load(std::memory_order_relaxed);
    return std::max<int64_t>(0, sum);
}

// Do up to ~maxFrames of warp-cache building; returns frames still remaining (0 = done).
// Installs each finished clip via an atomic republish so playback picks it up mid-build.
int64_t ClipOps::warpBuildStep(int32_t maxFrames) {
    if (!rt_.authoring_) return 0;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return 0;
    if (maxFrames < kWarpBuildChunk) maxFrames = kWarpBuildChunk;
    cancelWarpJob(); // the synchronous path owns the build from here

    int32_t budget = maxFrames;
    while (budget > 0) {
        // Idle: find the next warp clip needing a cache and start building it.
        if (!wb_.building) {
            int32_t tid = -1, ci = -1;
            const AudioClip* target = nextWarpBuildTarget(tid, ci);
            if (!target) break; // nothing left to build
            int64_t frames = 0;
            wb_.tempoMap = ci >= 0 ? rt_.authoring_->tempoMap : nullptr; // Session slots: ordinary cache
            wb_.tempoStartBeat = wb_.tempoMap ? target->startBeat : -1.0;
            wb_.reverseSource = wb_.tempoMap && target->reversed;
            if (wb_.tempoMap) tempoWarpPlan(*target, wb_.tempoMap, devSR, wb_.renderInputs, frames);
            else {
                frames = warpCacheFrames(*target, spb, devSR);
                wb_.renderInputs = warpInputsOf(*target, spb, devSR);
            }
            wb_.cache = std::make_shared<WarpCache>();
            wb_.cache->frames = frames;
            wb_.cache->spb = wb_.tempoMap ? devSR : spb;
            wb_.cache->devSR = devSR;
            wb_.cache->inputs = warpInputsOf(*target, wb_.tempoMap ? devSR : spb, devSR);
            wb_.cache->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);
            wb_.stream = std::make_unique<ClipWarpStream>();
            wb_.stream->configure(target->sample->channels, target->warpMode, target->pitchSemitones,
                target->sample->sourceSampleRate, devSR, wb_.reverseSource);
            wb_.trackId = tid;
            wb_.clipIndex = ci;
            wb_.offset = 0;
            wb_.building = true;
        }

        // Re-fetch + revalidate the target (an edit between steps may have changed it).
        const AudioClip* clip = warpBuildTargetClip();
        if (!clip) {
            wb_ = {};
            continue;
        } // abandon this clip, rescan on the next loop

        // Render another chunk of the in-progress clip.
        const double warpOffset = wb_.renderInputs.playStart * wb_.renderInputs.spb;
        const int32_t n = static_cast<int32_t>(std::min<int64_t>(
            {static_cast<int64_t>(kWarpBuildChunk), static_cast<int64_t>(budget), wb_.cache->frames - wb_.offset}));
        if (n > 0) {
            wb_.stream->render(wb_.cache->samples.data() + wb_.offset * 2, n,
                warpOffset + static_cast<double>(wb_.offset), wb_.renderInputs.markers, wb_.renderInputs.warpBeats,
                *clip->sample, wb_.renderInputs.spb, devSR, 1.0f);
            wb_.offset += n;
            budget -= n;
        }

        // Finished this clip: install its cache via an atomic republish, then continue.
        if (wb_.offset >= wb_.cache->frames) {
            auto old = rt_.findTrackAuthoring(wb_.trackId);
            if (old && audioClipRef(*old, wb_.clipIndex)) {
                auto nt = rt_.cloneTrack(*old);
                auto* built = audioClipWrite(*nt, wb_.clipIndex);
                if (wb_.tempoMap) {
                    built->tempoWarp = std::make_shared<const AudioClip::TempoWarp>(
                        AudioClip::TempoWarp{std::const_pointer_cast<const WarpCache>(wb_.cache), wb_.tempoMap,
                            wb_.tempoStartBeat, wb_.reverseSource});
                } else
                    built->warpCache = std::const_pointer_cast<const WarpCache>(wb_.cache);
                rt_.republishWithTrackRaw(wb_.trackId, nt); // no undo entry — a cache isn't an edit
            }
            wb_ = {}; // clear the cursor (also frees the stretcher)
        }
    }
    return warpBuildRemainingFrames();
}

void ClipOps::setDeferWarpBuild(bool defer) {
    deferWarpBuild_ = defer;
}

void ClipOps::cancelWarpJob() {
    if (!wj_) return;
    wj_->cancel.store(true, std::memory_order_relaxed);
    if (wj_->thread.joinable()) wj_->thread.join();
    wj_.reset();
}

// Background warp build. Each call (from the UI's poll loop): if the worker finished,
// install its cache — only if the clip still has the inputs it was built from, else drop
// it — then start the next clip needing a build on a fresh worker. A clip edited while its
// build runs cancels that build early. Returns frames still remaining (0 = all built).
int64_t ClipOps::warpBuildPoll() {
    if (!rt_.authoring_) return 0;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return 0;

    if (wj_) {
        const AudioClip* c = nullptr;
        if (auto t = rt_.findTrackAuthoring(wj_->trackId); t && t->type() == TrackType::Audio)
            c = audioClipRef(*t, wj_->clipIndex);
        const bool valid =
            c && warpNeedsBuild(*c, wj_->clipIndex >= 0) &&
            (wj_->tempoMap
                    ? rt_.authoring_->tempoMap == wj_->tempoMap && c->startBeat == wj_->tempoStartBeat &&
                          c->reversed == wj_->reverseSource && warpInputsOf(*c, devSR, devSR) == wj_->cache->inputs
                    : warpInputsOf(*c, spb, devSR) == wj_->cache->inputs);
        if (!valid) wj_->cancel.store(true, std::memory_order_relaxed);
        if (valid && !wj_->finished.load(std::memory_order_acquire)) return warpBuildRemainingFrames();
        if (wj_->thread.joinable()) wj_->thread.join();
        if (valid && !wj_->cancel.load(std::memory_order_relaxed)) {
            auto nt = rt_.cloneTrack(*rt_.findTrackAuthoring(wj_->trackId));
            auto* built = audioClipWrite(*nt, wj_->clipIndex);
            if (wj_->tempoMap) {
                built->tempoWarp = std::make_shared<const AudioClip::TempoWarp>(
                    AudioClip::TempoWarp{std::const_pointer_cast<const WarpCache>(wj_->cache), wj_->tempoMap,
                        wj_->tempoStartBeat, wj_->reverseSource});
            } else
                built->warpCache = std::const_pointer_cast<const WarpCache>(wj_->cache);
            rt_.republishWithTrackRaw(wj_->trackId, nt); // no undo entry — a cache isn't an edit
        }
        wj_.reset();
    }

    int32_t tid = -1, ci = -1;
    const AudioClip* target = nextWarpBuildTarget(tid, ci);
    if (!target) return 0;
    auto job = std::make_unique<WarpJob>();
    job->trackId = tid;
    job->clipIndex = ci;
    job->sample = target->sample;                                 // keeps the buffer alive for the worker
    job->tempoMap = ci >= 0 ? rt_.authoring_->tempoMap : nullptr; // Session slots: ordinary cache
    job->tempoStartBeat = job->tempoMap ? target->startBeat : -1.0;
    job->reverseSource = job->tempoMap && target->reversed;
    job->cache = std::make_shared<WarpCache>();
    if (job->tempoMap) tempoWarpPlan(*target, job->tempoMap, devSR, job->renderInputs, job->cache->frames);
    else {
        job->cache->frames = warpCacheFrames(*target, spb, devSR);
        job->renderInputs = warpInputsOf(*target, spb, devSR);
    }
    job->cache->spb = job->tempoMap ? devSR : spb;
    job->cache->devSR = devSR;
    job->cache->inputs = warpInputsOf(*target, job->tempoMap ? devSR : spb, devSR);
    WarpJob* j = job.get();
    job->thread = std::thread([j] {
        WarpCache& wc = *j->cache;
        wc.samples.assign(static_cast<size_t>(wc.frames) * 2, 0.0f);
        renderWarpWindow(
            j->renderInputs, *j->sample, wc.samples.data(), wc.frames, &j->cancel, &j->done, j->reverseSource);
        j->finished.store(true, std::memory_order_release);
    });
    wj_ = std::move(job);
    return warpBuildRemainingFrames();
}

// Rebuild every warped clip's offline stretch cache for the given tempo + device rate.
// The stretched audio depends on both (spb sets the played length; the device rate sets
// the window sizes + src≠device pitch compensation), so a tempo or SR change must re-run
// the build — else warped clips play at the wrong length/pitch. Atomic republish (no
// undo): audio-thread safe. `spb` is passed explicitly so a tempo change can rebuild
// with the NEW value before the transport command that carries it has drained.
void ClipOps::reconfigureAllWarpStreams() {
    reconfigureAllWarpStreams(rt_.transport_.samplesPerBeat(), rt_.transport_.sampleRate());
}
void ClipOps::reconfigureAllWarpStreams(double spb, double devSR) {
    if (!rt_.authoring_) return;
    if (devSR <= 0.0 || spb <= 0.0) return;
    bool any = false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        bool warped = false;
        if (t->type() == TrackType::Audio) {
            for (auto& c : t->clips)
                if (c.warpEnabled && c.sample) {
                    warped = true;
                    break;
                }
            for (auto& sl : t->sessionSlots)
                if (sl.hasClip && sl.audio.warpEnabled && sl.audio.sample) {
                    warped = true;
                    break;
                }
        }
        if (!warped) {
            g->tracks.push_back(t);
            continue;
        }
        auto nt = rt_.cloneTrack(*t);
        bool hasWarpedClip = false;
        for (const auto& c : nt->clips)
            if (c.warpEnabled && c.sample) {
                hasWarpedClip = true;
                break;
            }
        if (hasWarpedClip) {
            for (auto& c : nt->clips.write())
                if (c.warpEnabled && c.sample) configureClipWarp(c, spb, devSR);
        }
        bool hasWarpedSlot = false;
        for (const auto& sl : nt->sessionSlots)
            if (sl.hasClip && sl.audio.warpEnabled && sl.audio.sample) {
                hasWarpedSlot = true;
                break;
            }
        if (hasWarpedSlot) {
            for (auto& sl : nt->sessionSlots.write()) // session audio slots warp like clips (S-06)
                if (sl.hasClip && sl.audio.warpEnabled && sl.audio.sample) configureClipWarp(sl.audio, spb, devSR);
        }
        g->tracks.push_back(std::move(nt));
        any = true;
    }
    if (any) rt_.publishRaw(std::move(g));
}

bool ClipOps::setClipPitch(int32_t trackId, int32_t clipIndex, float semitones) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    c.pitchSemitones = semitones;
    configureClipWarp(c, rt_.transport_.samplesPerBeat(),
        rt_.transport_.sampleRate()); // pitch is baked into the warp
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// Reverse an audio clip (non-destructive): the played region is read back-to-front.
// Ordinary caches mirror their read index; mapped caches rebuild with direction in
// their source mapping, so reversed content still lands on the same musical grid.
bool ClipOps::setClipReverse(int32_t trackId, int32_t clipIndex, bool reversed) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = rt_.cloneTrack(*old);
    (*audioClipWrite(*nt, clipIndex)).reversed = reversed;
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool ClipOps::setClipWarp(int32_t trackId, int32_t clipIndex, bool enabled, int32_t mode) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    // Off is a bypass (#310): the markers stay on the clip, stamped with the region they
    // belong to, and turning warp back on restores them as they were. Only a first enable
    // (or one after a split / trim moved the region) seeds: auto-detect the source tempo
    // and snap the clip's length to the beat grid so it conforms to the project BPM; if
    // detection fails, a length-neutral seed (toggling warp then leaves audio where it is).
    if (!enabled && c.warpEnabled) stampWarpOffRegion(c);
    if (enabled && !c.warpEnabled && !warpMarkersRestorable(c) && c.sample && spb > 0.0 && devSR > 0.0 &&
        c.sample->sourceSampleRate > 0.0) {
        if (seedAutoWarp(c, spb, devSR) <= 0.0) seedNeutralWarp(c, spb, devSR);
    }
    c.warpEnabled = enabled;
    if (mode >= 0) c.warpMode = static_cast<WarpMode>(mode);
    configureClipWarp(c, spb, devSR);
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

double ClipOps::autoWarpClip(int32_t trackId, int32_t clipIndex, double knownBpm) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return 0.0;
    if (!audioClipRef(*old, clipIndex)) return 0.0;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;
    const double bpm = seedAutoWarp(c, spb, devSR, knownBpm);
    if (bpm <= 0.0) return 0.0; // detection failed: leave the clip unchanged
    c.warpEnabled = true;
    configureClipWarp(c, spb, devSR);
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return bpm;
}

double ClipOps::beatWarpClip(int32_t trackId, int32_t clipIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return 0.0;
    if (!audioClipRef(*old, clipIndex)) return 0.0;
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;

    // Establish the beat grid from the detected tempo (rounded musical length).
    const int64_t srcLen = c.effectiveLength();
    const double srcSR = c.sample->sourceSampleRate;
    const double bpm = detectTempo(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen, srcSR);
    if (bpm <= 0.0 || srcLen <= 0) return 0.0;
    double beats = std::max(1.0, static_cast<double>(std::llround(static_cast<double>(srcLen) / srcSR * bpm / 60.0)));
    const double srcPerBeat = static_cast<double>(srcLen) / beats; // uniform source frames per beat

    // Detect transients and pin a marker at each — snapping its beat to the 1/16 grid —
    // so every hit locks to the grid ("Beats" warp). Segments between markers
    // stretch to fit, correcting drift a uniform stretch can't.
    double trans[256];
    const int32_t nt2 =
        detectTransients(*c.sample, static_cast<int64_t>(c.sourceOffsetFrames), srcLen, srcSR, trans, 256);
    std::vector<WarpMarker> markers;
    markers.push_back({c.sourceOffsetFrames, 0.0});
    const double grid = 0.25; // 1/16 note in 4/4
    double prevBeat = 0.0;
    for (int32_t i = 0; i < nt2; ++i) {
        const double beatRaw = (trans[i] - c.sourceOffsetFrames) / srcPerBeat;
        const double snapped = std::round(beatRaw / grid) * grid;
        if (snapped <= prevBeat + 1e-6 || snapped >= beats - 1e-6)
            continue; // keep beats strictly rising, inside (0,beats)
        markers.push_back({trans[i], snapped});
        prevBeat = snapped;
    }
    markers.push_back({c.sourceOffsetFrames + static_cast<double>(srcLen), beats});

    c.warpEnabled = true;
    c.warpMode = WarpMode::Beats;
    c.warpBeats = beats;
    c.warpMarkers = std::move(markers);
    c.warpPlayStart = 0.0;
    c.warpPlayEnd = 0.0; // fresh warp → play the whole material
    configureClipWarp(c, spb, devSR);
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return bpm;
}

bool ClipOps::setClipWarpLength(int32_t trackId, int32_t clipIndex, double beats) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    if (beats < 0.25) beats = 0.25;
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    c.warpBeats = beats;
    // The end marker owns the total length; keep interior markers within it.
    if (!c.warpMarkers.empty()) {
        c.warpMarkers.back().beat = beats;
        for (auto& m : c.warpMarkers)
            if (m.beat > beats) m.beat = beats;
    }
    clampWarpPlay(c); // keep the trim window valid for the new length
    configureClipWarp(c, rt_.transport_.samplesPerBeat(), rt_.transport_.sampleRate());
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool ClipOps::setClipWarpMarkers(
    int32_t trackId, int32_t clipIndex, const double* srcFrames, const double* beats, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    if (count < 2 || !srcFrames || !beats) return false;
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    c.warpMarkers.clear();
    c.warpMarkers.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) c.warpMarkers.push_back({srcFrames[i], beats[i]});
    std::sort(c.warpMarkers.begin(), c.warpMarkers.end(),
        [](const WarpMarker& a, const WarpMarker& b) { return a.srcFrame < b.srcFrame; });
    c.warpBeats = c.warpMarkers.back().beat;
    clampWarpPlay(c);                          // preserve the trim window across marker edits (clamped to new length)
    if (!c.warpEnabled) stampWarpOffRegion(c); // markers kept for a later re-enable (a project load)
    configureClipWarp(c, rt_.transport_.samplesPerBeat(), rt_.transport_.sampleRate());
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// Trim a warped clip's played window (clip Start/End over the warp). Markers
// and warpBeats are untouched; only the [playStart, playEnd] beat window moves. No-op
// on unwarped clips (their region is set via setClipSourceRegion).
bool ClipOps::setClipWarpTrim(int32_t trackId, int32_t clipIndex, double playStart, double playEnd) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    const AudioClip& oc = (*audioClipRef(*old, clipIndex));
    // A warp-off clip still takes the window of the markers it keeps (a project load).
    if ((!oc.warpEnabled && !warpMarkersRestorable(oc)) || oc.warpBeats <= 0.0) return false;
    constexpr double kMinBeat = 0.05;
    double ps = std::clamp(playStart, 0.0, std::max(0.0, oc.warpBeats - kMinBeat));
    double pe = std::clamp(playEnd, ps + kMinBeat, oc.warpBeats);
    auto nt = rt_.cloneTrack(*old);
    AudioClip& c = (*audioClipWrite(*nt, clipIndex));
    c.warpPlayStart = ps;
    c.warpPlayEnd = pe;
    configureClipWarp(
        c, rt_.transport_.samplesPerBeat(), rt_.transport_.sampleRate()); // window is baked into the cache
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t ClipOps::clipWarpMarkers(
    int32_t trackId, int32_t clipIndex, double* outSrc, double* outBeat, int32_t maxCount) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (!audioClipRef(*t, clipIndex)) return 0;
    const AudioClip& c = (*audioClipRef(*t, clipIndex));
    // A warp-off clip split or trimmed since has markers for a region it no longer
    // plays: they'd be re-seeded on re-enable, so they aren't reported (nor saved).
    if (!c.warpEnabled && !warpMarkersRestorable(c)) return 0;
    const auto& m = c.warpMarkers;
    const int32_t n = std::min<int32_t>(maxCount, static_cast<int32_t>(m.size()));
    for (int32_t i = 0; i < n; ++i) {
        if (outSrc) outSrc[i] = m[static_cast<size_t>(i)].srcFrame;
        if (outBeat) outBeat[i] = m[static_cast<size_t>(i)].beat;
    }
    return static_cast<int32_t>(m.size());
}

} // namespace nota::detail
