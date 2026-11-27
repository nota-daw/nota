// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "core/EngineRuntime.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/EngineTypes.h"
#include "model/ArticulationTable.h"
#include "core/Transport.h"
#include "render/RenderHelpers.h"
#include "model/ArticulationSchedule.h"
#include "render/RenderGraphExecutor.h"
#include "render/RenderRoutingPlan.h"
#include "core/TempoMap.h"
#include "model/TimelineIndex.h"
#include "core/EnginePrelude.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace nota::detail {

void EngineRuntime::renderAudioClipsRaw(
    const std::vector<AudioClip>& clips, float* dst, int32_t frames, double blockStart, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    if (transport_.hasTempoMap()) {
        const double elapsed = transport_.playheadSamples();
        for (const AudioClip& clip : clips) mixAudioClip<true>(clip, dst, frames, elapsed, spb, 1.0f, transport_);
    } else
        for (const AudioClip& clip : clips) mixAudioClip(clip, dst, frames, blockStart, spb, 1.0f, transport_);
}

void EngineRuntime::renderTrackAudioClipsRaw(
    const Track& t, float* dst, int32_t frames, double blockStart, double spb) {
    if (!t.timelineIndex || spb <= 0.0) {
        renderAudioClipsRaw(t.clips.read(), dst, frames, blockStart, spb);
        return;
    }
    std::fill_n(dst, static_cast<size_t>(frames) * 2, 0.0f);
    const bool mapped = transport_.hasTempoMap();
    const TempoMap* tmap = transport_.tempoMap();
    const double sr = transport_.sampleRate() > 0.0 ? transport_.sampleRate() : 44100.0;
    const double elapsed = transport_.playheadSamples();
    const double b0 = (mapped && tmap) ? tmap->beatAt(elapsed / sr) : blockStart / spb;
    const double b1 = (mapped && tmap) ? tmap->beatAt((elapsed + frames) / sr) : (blockStart + frames) / spb;
    const double winStart = std::min(b0, b1);
    const double winEnd = std::max(b0, b1);
    const double maxBps = mapped ? (TimelineIndex::kMaxConservTempoBpm / 60.0) : (sr / spb);
    t.timelineIndex->forEachAudioClipInWindow(winStart, winEnd, maxBps, [&](int32_t ci) {
        const AudioClip& clip = t.clips[static_cast<size_t>(ci)];
        if (mapped) mixAudioClip<true>(clip, dst, frames, elapsed, spb, 1.0f, transport_);
        else
            mixAudioClip(clip, dst, frames, blockStart, spb, 1.0f, transport_);
    });
}

void EngineRuntime::renderTrackAudioClipsUnmapped(
    const Track& t, float* dst, int32_t frames, double blockStart, double spb, double sr) {
    std::fill_n(dst, static_cast<size_t>(frames) * 2, 0.0f);
    Transport clock;
    clock.setSampleRate(sr > 0.0 ? sr : 44100.0);
    if (spb > 0.0) clock.setBpm((clock.sampleRate() * 60.0) / spb);
    if (!t.timelineIndex || spb <= 0.0) {
        for (const AudioClip& clip : t.clips.read())
            mixAudioClip<false>(clip, dst, frames, blockStart, spb, 1.0f, clock, /*forceSyncFill=*/true);
        return;
    }
    const double b0 = blockStart / spb;
    const double b1 = (blockStart + frames) / spb;
    const double maxBps = clock.sampleRate() / spb;
    t.timelineIndex->forEachAudioClipInWindow(std::min(b0, b1), std::max(b0, b1), maxBps, [&](int32_t ci) {
        mixAudioClip<false>(t.clips[static_cast<size_t>(ci)], dst, frames, blockStart, spb, 1.0f, clock, true);
    });
}

void EngineRuntime::renderAnticipativeInstrumentBlock(Graph* g, Track& t, size_t trackIdx, float* dst, int32_t frames,
    double blockStart, double spb, double sr, MidiEv* midiScratch, ExprEv* exprScratch, ArtEv* artScratch) {
    std::fill_n(dst, static_cast<size_t>(frames) * 2, 0.0f);
    Instrument* inst = t.instrument.get();
    if (!inst || spb <= 0.0) return;
    inst->setTransport(blockStart / spb, spb, true);

    int n = 0;
    int xn = 0;
    const double tsr = sr > 0.0 ? sr : 44100.0;
    const double msToS = tsr / 1000.0;
    const double trackShiftS = t.midiOffsetMs() * msToS;
    const ArticulationTable* artTable = t.articulationTable.get();
    const TimelineIndex* idx = t.timelineIndex.get();

    auto processClip = [&](const MidiClip& clip, int32_t clipIdx, bool indexedNotes, double clipLocalStartB,
                           double clipLocalEndB) {
        const bool clipActive = clip.active;
        const double clipStart = clip.startBeat * spb;
        const double clipEnd = (clip.startBeat + clip.lengthBeats) * spb;
        const bool hasVel = !clip.velocityEnvelope.points.empty();
        auto handleNote = [&](const Note& note) {
            if (note.startBeat >= clip.lengthBeats || note.startBeat + note.lengthBeats <= 0.0) return;
            const double shiftS =
                artTable ? trackShiftS + articulationOffsetMs(artTable, note.articulation) * msToS : trackShiftS;
            const double onS = clipStart + std::max(0.0, note.startBeat) * spb + shiftS;
            const double offS = std::min(clipEnd, clipStart + (note.startBeat + note.lengthBeats) * spb) + shiftS;
            float vel = note.velocity;
            if (hasVel) vel *= std::clamp(clip.velocityEnvelope.valueAt(note.startBeat), 0.0f, 1.0f);
            if (clipActive && onS >= blockStart && onS < blockStart + frames && n < 1024)
                midiScratch[n++] = {static_cast<int32_t>(onS - blockStart), true, note.pitch, vel,
                    static_cast<float>((offS - onS) / spb)};
            if (offS >= blockStart && offS < blockStart + frames && n < 1024)
                midiScratch[n++] = {static_cast<int32_t>(offS - blockStart), false, note.pitch, 0.0f};
            if (note.expr && clipActive)
                noteExprEvents(note, clipStart + note.startBeat * spb + shiftS - blockStart, offS - blockStart, frames,
                    spb, exprScratch, xn, kMaxBlockExpr);
        };
        if (indexedNotes && idx) {
            idx->forEachNoteInClip(clipIdx, clipLocalStartB, clipLocalEndB, -1.0,
                [&](int32_t noteIdx) { handleNote(clip.notes[static_cast<size_t>(noteIdx)]); });
        } else {
            for (const Note& note : clip.notes) handleNote(note);
        }
        if (clipActive)
            clipCtlEvents(clip, clipStart, clipEnd, blockStart, frames, spb, false, exprScratch, xn, kMaxBlockExpr);
    };

    if (idx) {
        const double minShiftMs = t.midiOffsetMs() + idx->minArtOffsetMs();
        const double maxShiftMs = t.midiOffsetMs() + idx->maxArtOffsetMs();
        const double maxBeatsPerMs = (tsr / spb) / 1000.0;
        const double padBefore = std::max(0.0, maxShiftMs) * maxBeatsPerMs + 0.05;
        const double padAfter = std::max(0.0, -minShiftMs) * maxBeatsPerMs + 0.05;
        const double rawStart = blockStart / spb;
        const double rawEnd = (blockStart + frames) / spb;
        idx->forEachMidiClipInWindow(rawStart - 0.05, rawEnd + 0.05, -1.0, [&](int32_t ci) {
            const MidiClip& clip = t.midiClips[static_cast<size_t>(ci)];
            if (clip.active)
                clipCtlEndResets(clip, clip.startBeat * spb, (clip.startBeat + clip.lengthBeats) * spb, blockStart,
                    frames, exprScratch, xn, kMaxBlockExpr);
        });
        idx->forEachMidiClipInWindow(rawStart - padBefore, rawEnd + padAfter, -1.0, [&](int32_t ci) {
            const MidiClip& clip = t.midiClips[static_cast<size_t>(ci)];
            processClip(clip, ci, true, (rawStart - padBefore) - clip.startBeat, (rawEnd + padAfter) - clip.startBeat);
        });
    } else {
        for (const MidiClip& clip : t.midiClips)
            if (clip.active)
                clipCtlEndResets(clip, clip.startBeat * spb, (clip.startBeat + clip.lengthBeats) * spb, blockStart,
                    frames, exprScratch, xn, kMaxBlockExpr);
        for (size_t ci = 0; ci < t.midiClips.size(); ++ci)
            processClip(t.midiClips[ci], static_cast<int32_t>(ci), false, 0.0, 0.0);
    }

    std::sort(midiScratch, midiScratch + n, midiEvLess);
    if (!t.midiEffects.empty()) {
        MidiEv fxScratch[1024];
        n = applyMidiEffects(t, midiScratch, n, fxScratch, frames, blockStart / spb, spb, true);
        applyMidiCcRouting(t, g, trackIdx);
        std::sort(midiScratch, midiScratch + n, midiEvLess);
    } else if (t.instrument && t.instrument->asRack()) {
        applyMidiCcRouting(t, g, trackIdx);
    }
    std::stable_sort(exprScratch, exprScratch + xn, [](const ExprEv& a, const ExprEv& b) { return a.off < b.off; });

    int an = 0;
    if (artTable && inst->takesArticulations()) {
        ArticulationBlock ab;
        ab.frames = frames;
        ab.blockStart = blockStart;
        ab.spb = spb;
        ab.sampleRate = tsr;
        ab.trackOffsetMs = t.midiOffsetMs();
        an = scheduleArticulations(t.midiClips.read(), *artTable, ab, artScratch, kMaxBlockArt);
    }
    playEvents(*inst, dst, frames, midiScratch, n, exprScratch, xn, artScratch, an, blockStart / spb, spb, true, [] {});

    auto applyEnv = [&](const MidiClip& clip) {
        if (!clip.active || clip.volumeEnvelope.points.empty()) return;
        const double cs = clip.startBeat * spb;
        const double ce = (clip.startBeat + clip.lengthBeats) * spb;
        for (int32_t i = 0; i < frames; ++i) {
            const double p = blockStart + i;
            if (p < cs || p >= ce) continue;
            const float gEnv = std::clamp(clip.volumeEnvelope.valueAt((p - cs) / spb), 0.0f, 1.0f);
            dst[i * 2] *= gEnv;
            dst[i * 2 + 1] *= gEnv;
        }
    };
    if (idx && idx->hasAnyMidiVolumeEnvelope()) {
        const double b0 = blockStart / spb;
        const double b1 = (blockStart + frames) / spb;
        idx->forEachMidiEnvelopeClipInWindow(
            b0 - 0.05, b1 + 0.05, [&](int32_t ci) { applyEnv(t.midiClips[static_cast<size_t>(ci)]); });
    } else {
        for (const MidiClip& clip : t.midiClips) applyEnv(clip);
    }
}

// Consolidation owns this clock; rendering must neither attach to nor advance the live one.
void EngineRuntime::renderAudioClipsAt(
    const std::vector<AudioClip>& clips, float* dst, int32_t frames, const Transport& clock) {
    std::fill_n(dst, frames * 2, 0.0f);
    for (const AudioClip& clip : clips) {
        if (clock.hasTempoMap())
            mixAudioClip<true>(clip, dst, frames, clock.playheadSamples(), clock.samplesPerBeat(), 1.0f, clock);
        else
            mixAudioClip(clip, dst, frames, clock.playheadSamples(), clock.samplesPerBeat(), 1.0f, clock);
    }
}

// One clip of renderAudioClipsRaw — shared with Session audio slots, which loop a clip on
// their own clock (renderSessionAudioSlotRaw).
template <bool mapped>
void EngineRuntime::mixAudioClip(const AudioClip& clip, float* dst, int32_t frames, double blockStart, double spb,
    float extraGain, const Transport& clock, bool forceSyncFill) {
    if (!clip.active) return; // deactivated clip (key 0): stays on the timeline but silent
    const double sr = clock.sampleRate();
    double startSamples;
    if constexpr (mapped) startSamples = clock.samplesAtBeat(clip.startBeat);
    else
        startSamples = clip.startBeat * spb;
    // Clip envelopes (M9 follow-up): 0..1 volume + -1..1 pan curves in clip-local
    // beats, applied per sample. Empty = fast path (no cost). Pan uses a balance
    // law (unity at centre) so it composes with the track's own pan stage.
    const bool hasVol = !clip.volumeEnvelope.points.empty();
    const bool hasPan = !clip.panEnvelope.points.empty();
    const bool hasAdsr = !clip.adsr.isIdentity();
    const bool hasEnv = hasVol || hasPan || hasAdsr;
    double lenBeats = 0.0; // played length (beats) for the ADSR release — set per path below
    auto applyEnv = [&](double p, float& l, float& r) {
        double b;
        if constexpr (mapped) b = clock.beatAtSamples(p) - clip.startBeat;
        else
            b = (p - startSamples) / spb;
        if (hasAdsr) {
            const float g = clip.adsr.gainAt(b, lenBeats);
            l *= g;
            r *= g;
        }
        if (hasVol) {
            const float g = std::clamp(clip.volumeEnvelope.valueAt(b), 0.0f, 1.0f);
            l *= g;
            r *= g;
        }
        if (hasPan) {
            const float pan = std::clamp(clip.panEnvelope.valueAt(b), -1.0f, 1.0f);
            if (pan > 0.0f) l *= (1.0f - pan);
            else if (pan < 0.0f)
                r *= (1.0f + pan);
        }
    };

    // Warped clips: copy straight from the offline stretch cache.
    // The cache holds the played window [warpPlayStart, warpPlayEndEff] pre-rendered
    // at the device rate, so there is no realtime stretch here — just a memcpy-cheap
    // read (no per-clip-start priming spike). The clip's on-timeline length is the
    // cache's frame count (both are warpPlayLen×spb once rebuilt for the tempo).
    const WarpCache* readyCache;
    if constexpr (mapped) readyCache = clip.tempoWarpCache();
    else
        readyCache = clip.warpCache.get();
    if (clip.warpEnabled && readyCache && readyCache->frames > 0) {
        const WarpCache& wc = *readyCache;
        bool reverseRead = clip.reversed;
        if constexpr (mapped) reverseRead = clip.reversed != clip.tempoWarp->reversed;
        // A stale cache (an edit's rebuild still running in the background) keeps playing,
        // clipped to the clip's new played length so it never spills past the clip's end.
        // Both edges sit on whole frames rounded from the timeline (not start + a rounded
        // length), so clips that meet — the halves of a split — leave no frame between them.
        const int64_t startF = std::llround(startSamples);
        double endAt;
        if constexpr (mapped) endAt = clock.samplesAtBeat(clip.startBeat + clip.warpPlayLen());
        else
            endAt = startSamples + clip.warpPlayLen() * spb;
        const int64_t endF = std::min<int64_t>(startF + wc.frames, std::llround(endAt));
        const double clipDeviceLen = static_cast<double>(endF - startF);
        if constexpr (mapped) lenBeats = clip.warpPlayLen();
        else
            lenBeats = clipDeviceLen / spb;
        const double ovStart = std::max(blockStart, static_cast<double>(startF));
        const double ovEnd = std::min(blockStart + frames, static_cast<double>(endF));
        if (ovEnd <= ovStart) return;
        const int32_t dstOff = static_cast<int32_t>(std::llround(ovStart - blockStart));
        const int32_t segLen = static_cast<int32_t>(std::llround(ovEnd - ovStart));
        if (segLen <= 0 || dstOff < 0 || dstOff + segLen > frames) return;
        const int64_t cacheBase = std::llround(ovStart) - startF; // 0-based into the cache
        const float* csamp = wc.data();
        const float g = clip.gain * extraGain;
        // A split fade is a few milliseconds of attack or release with the sustain left at
        // full level, so the body is unity. It is its own loop, with no envelope test and no
        // helper call: folding the test into the sample loop cost audio-warp-split +11%, and
        // a body loop that reloaded its pointers cost another +3.6%. The extra sample on
        // each edge covers a rounding step; a body sample it pulls in has sustain gain 1.
        // Every other clip takes the original loop below, unchanged.
        if (!mapped && hasAdsr && clip.adsr.sustain >= 1.0f && !hasVol && !hasPan) {
            int32_t head = 0, tail = segLen;
            if (clip.adsr.attack > 0.0) {
                const double n = startSamples + clip.adsr.attack * spb + 1.0 - ovStart; // j < n
                // Clamp in double before the narrowing cast. The span only covers this
                // block; a long attack must not overflow int32 on the way there.
                const double c = std::clamp(std::ceil(n), 0.0, static_cast<double>(segLen));
                head = static_cast<int32_t>(c);
            }
            if (clip.adsr.release > 0.0) {
                const double n = startSamples + clipDeviceLen - clip.adsr.release * spb - 1.0 - ovStart; // j > n
                const double c = std::clamp(std::floor(n) + 1.0, 0.0, static_cast<double>(segLen));
                tail = static_cast<int32_t>(c);
            }
            if (head > tail) head = tail = segLen; // edges overlap: the whole block fades
            for (int32_t j = 0; j < head; ++j) {
                const int64_t ci = reverseRead ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
            for (int32_t j = head; j < tail; ++j) {
                const int64_t ci = reverseRead ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                const float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
            for (int32_t j = tail; j < segLen; ++j) {
                const int64_t ci = reverseRead ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
        } else {
            for (int32_t j = 0; j < segLen; ++j) {
                // Ordinary caches mirror their read index. Mapped caches already hold
                // the requested direction, baked into the source mapping before stretch.
                const int64_t ci = reverseRead ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                if (hasEnv) applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
        }
        return;
    }
    // Warp enabled but the cache isn't ready yet (a rebuild is pending after an
    // edit/tempo change): stay silent rather than fall through and play the clip
    // unwarped (wrong length/pitch) for the brief window until the cache publishes.
    if (clip.warpEnabled) return;

    // Unwarped clips resample at their natural rate with varispeed transpose
    // folded into the read ratio.
    if (!clip.sample) return;
    const SampleBuffer& sb = *clip.sample;
    const double ratio = sb.sourceSampleRate / sr * clip.pitchRatio();
    const int64_t len = clip.effectiveLength();
    const double clipDeviceLen = static_cast<double>(len) / ratio;
    if constexpr (mapped) lenBeats = clock.beatAtSamples(startSamples + clipDeviceLen) - clip.startBeat;
    else
        lenBeats = clipDeviceLen / spb;

    const double blkLo = std::max(blockStart, startSamples);
    const double blkHi = std::min(blockStart + frames, startSamples + clipDeviceLen);
    if (!(blkHi > blkLo)) return;

    const bool allowSyncFill = forceSyncFill || !deviceBlock_;
    if (sb.isStreaming()) {
        auto posAt = [&](double p) {
            double s = clip.reversed
                           ? clip.sourceOffsetFrames + static_cast<double>(len - 1) - (p - startSamples) * ratio
                           : clip.sourceOffsetFrames + (p - startSamples) * ratio;
            return std::max(clip.sourceOffsetFrames, s);
        };
        const int64_t fA = static_cast<int64_t>(posAt(blkLo));
        const int64_t fB = static_cast<int64_t>(posAt(blkHi - 1.0)) + 1;
        sb.source->preparePlaybackWindow(std::min(fA, fB), std::max(fA, fB), clip.reversed, allowSyncFill);
    }

    int32_t slotHint = -1;
    for (int32_t i = 0; i < frames; ++i) {
        const double p = blockStart + i;
        if (p < startSamples || p >= startSamples + clipDeviceLen) continue;
        // Reverse walks the source region from its last frame back to its first;
        // the interpolation is unchanged (srcPos is still a real source position).
        double srcPos = clip.reversed
                            ? clip.sourceOffsetFrames + static_cast<double>(len - 1) - (p - startSamples) * ratio
                            : clip.sourceOffsetFrames + (p - startSamples) * ratio;
        if (srcPos < clip.sourceOffsetFrames) srcPos = clip.sourceOffsetFrames;
        const int64_t i0 = static_cast<int64_t>(srcPos);
        const double frac = srcPos - static_cast<double>(i0);
        float l0, r0, l1, r1;
        sb.readStereoFast(i0, slotHint, allowSyncFill, l0, r0);
        sb.readStereoFast(i0 + 1, slotHint, allowSyncFill, l1, r1);
        float l = static_cast<float>(l0 + (l1 - l0) * frac) * clip.gain * extraGain;
        float r = static_cast<float>(r0 + (r1 - r0) * frac) * clip.gain * extraGain;
        // Short edge fade (~3 ms) so a clip that starts/ends mid-waveform doesn't click.
        const float eg =
            static_cast<float>(audioClipEdgeGain(p - startSamples, clipDeviceLen, sr, clip.fadeHead, clip.fadeTail));
        l *= eg;
        r *= eg;
        applyEnv(p, l, r);
        dst[i * 2] += l;
        dst[i * 2 + 1] += r;
    }
}

// Freeze playback (M7): fill `dst` from the track's frozen buffer for this segment.
// The buffer is indexed by beat (frame = beat·frozenSpb), so it resolves correctly at
// any render sample rate (an export bounce at a different rate resamples for free).
void EngineRuntime::fillFrozen(Track& t, float* dst, int32_t frames, double blockStart, double spb) {
    const auto bufPtr = t.frozenBuf; // copy the shared_ptr (keeps it alive)
    if (!bufPtr || spb <= 0.0 || t.frozenSpb <= 0.0) {
        std::fill_n(dst, frames * 2, 0.0f);
        return;
    }
    const std::vector<float>& buf = *bufPtr;
    const int64_t n = static_cast<int64_t>(buf.size() / 2);
    const auto* timing = t.frozenTiming.get();
    const bool mappedClock = transport_.hasTempoMap();
    const double elapsed = transport_.playheadSamples();
    const double origin = timing && timing->map ? timing->map->secondsAt(timing->startBeat) * timing->sampleRate : 0.0;
    const bool sameMap = timing && timing->map && timing->map.get() == transport_.tempoMap();
    for (int32_t i = 0; i < frames; ++i) {
        const double beat = !sameMap && mappedClock ? transport_.beatAtSamples(elapsed + i) : (blockStart + i) / spb;
        const double srcF = timing && timing->map
                                ? sameMap ? (elapsed + i) * timing->sampleRate / transport_.sampleRate() - origin
                                          : timing->map->secondsAt(beat) * timing->sampleRate - origin
                                : (beat - (timing ? timing->startBeat : 0.0)) * t.frozenSpb;
        const int64_t i0 = static_cast<int64_t>(srcF);
        if (srcF < 0.0 || i0 >= n) {
            dst[i * 2] = 0.0f;
            dst[i * 2 + 1] = 0.0f;
            continue;
        }
        const int64_t i1 = (i0 + 1 < n) ? i0 + 1 : i0;
        const float fr = static_cast<float>(srcF - static_cast<double>(i0));
        dst[i * 2] = buf[static_cast<size_t>(i0 * 2)] +
                     (buf[static_cast<size_t>(i1 * 2)] - buf[static_cast<size_t>(i0 * 2)]) * fr;
        dst[i * 2 + 1] = buf[static_cast<size_t>(i0 * 2 + 1)] +
                         (buf[static_cast<size_t>(i1 * 2 + 1)] - buf[static_cast<size_t>(i0 * 2 + 1)]) * fr;
    }
}

// Is this track currently driven by a Session slot (which owns its own loop
// clock)? Such tracks are exempt from the transport-loop / stop voice flush.

} // namespace nota::detail
