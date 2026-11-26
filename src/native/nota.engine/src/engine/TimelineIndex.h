// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TimelineIndex — immutable per-track interval index built on graph publication.
// Replaces per-block linear scans of all arrangement audio clips, MIDI clips, and MIDI notes
// with O(log N + k) interval pruning while emitting candidate indices in exact ascending
// source order (allocation-free on the audio thread, AR-6).

#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <limits>
#include <memory>
#include <vector>

namespace nota {

class Track;

class TimelineIndex {
  public:
    static constexpr int32_t kChunkSize = 32;
    static constexpr int32_t kMaxFastStackHits = 1024;
    // Upper tempo clamp for conservative unwarped audio duration -> beat bounds under TempoMap.
    static constexpr double kMaxConservTempoBpm = 300.0;

    struct NoteSpan {
        double startBeat = 0.0;      // clamped pickup start (>= 0.0, clip-local)
        double endBeat = 0.0;        // min(clip.lengthBeats, rawStart + lengthBeats)
        int32_t originalIndex = 0;   // index inside MidiClip::notes
    };

    struct MidiClipEntry {
        int32_t originalIndex = 0;   // index inside Track::midiClips
        double startBeat = 0.0;
        double endBeat = 0.0;        // startBeat + lengthBeats
        bool active = true;
        bool hasVolumeEnvelope = false;
        bool hasControllers = false;

        std::vector<NoteSpan> notesByStart;
        // Flat segment-tree max(endBeat) over kChunkSize blocks of notesByStart.
        std::vector<double> noteChunkMaxEnd;
        size_t noteChunkCount = 0;
        size_t noteTreeOffset = 0; // base 1-indexed array stored cleanly in vector size 2*pow2
        double notesMinRawStartBeat = 0.0;
        double notesMaxEndBeat = 0.0;
    };

    struct AudioClipEntry {
        int32_t originalIndex = 0;   // index inside Track::clips
        double startBeat = 0.0;
        bool warped = false;
        double warpLenBeats = 0.0;
        double unwarpedSec = 0.0;    // effectiveLength() / (sourceSampleRate * pitchRatio())
    };

    struct AudioTreeNode {
        double maxStartBeat = 0.0;
        double maxUnwarpedSec = 0.0;
        double maxWarpedEndBeat = -std::numeric_limits<double>::infinity();
    };

    static std::shared_ptr<const TimelineIndex> buildForTrack(const Track& track);

    bool empty() const noexcept { return midiClipsByStart_.empty() && audioClipsByStart_.empty(); }
    size_t midiClipCount() const noexcept { return midiClipsByStart_.size(); }
    size_t audioClipCount() const noexcept { return audioClipsByStart_.size(); }
    bool hasAnyMidiVolumeEnvelope() const noexcept { return anyMidiVolumeEnvelope_; }
    double minArtOffsetMs() const noexcept { return minArtOffsetMs_; }
    double maxArtOffsetMs() const noexcept { return maxArtOffsetMs_; }
    bool matchesTrack(const Track& track) const noexcept;

    // Visit original Track::midiClips indices (sorted ascending) that could intersect
    // [winStartBeat, winEndBeat] or the optional seam beat (>= 0).
    template <class Fn>
    void forEachMidiClipInWindow(double winStartBeat, double winEndBeat, double seamBeatOrNeg, Fn&& fn) const {
        const size_t n = midiClipsByStart_.size();
        if (n == 0) return;
        const bool hasSeam = seamBeatOrNeg >= 0.0;
        if (n <= 16) {
            for (const auto& c : midiClipsByStart_) {
                if (c.startBeat > winEndBeat && (!hasSeam || c.startBeat > seamBeatOrNeg)) break;
                if ((c.endBeat >= winStartBeat && c.startBeat <= winEndBeat) ||
                    (hasSeam && c.startBeat <= seamBeatOrNeg && c.endBeat >= seamBeatOrNeg)) {
                    fn(c.originalIndex);
                }
            }
            return;
        }
        std::array<int32_t, kMaxFastStackHits> hits;
        int32_t hitCount = 0;
        bool overflow = false;
        auto pushHit = [&](int32_t origIdx) {
            if (hitCount < kMaxFastStackHits) hits[hitCount++] = origIdx;
            else overflow = true;
        };
        const double maxQueryEnd = hasSeam ? std::max(winEndBeat, seamBeatOrNeg) : winEndBeat;
        const size_t limit = midiClipUpperBound(maxQueryEnd);
        if (limit == 0) return;

        collectMidiClipCandidates(limit, winStartBeat, winEndBeat, seamBeatOrNeg, pushHit);
        if (overflow) {
            // Fallback preserves strict ascending clip index without heap alloc.
            for (size_t i = 0; i < midiClipsByOriginal_.size(); ++i) {
                const MidiClipEntry& c = midiClipsByStart_[midiClipsByOriginal_[i]];
                if ((c.endBeat >= winStartBeat && c.startBeat <= winEndBeat) ||
                    (hasSeam && c.startBeat <= seamBeatOrNeg && c.endBeat >= seamBeatOrNeg)) {
                    fn(c.originalIndex);
                }
            }
            return;
        }
        std::sort(hits.begin(), hits.begin() + hitCount);
        int32_t prev = -1;
        for (int32_t i = 0; i < hitCount; ++i) {
            if (hits[i] != prev) {
                prev = hits[i];
                fn(prev);
            }
        }
    }

    // Visit original MidiClip::notes indices (sorted ascending) within indexed clip slot.
    // Query window MUST already be in clip-local beats expanded symmetrically for offsets.
    template <class Fn>
    void forEachNoteInClip(int32_t originalClipIndex, double clipLocalStartBeat, double clipLocalEndBeat,
                           double clipLocalSeamBeatOrNeg, Fn&& fn) const {
        if (originalClipIndex < 0 || static_cast<size_t>(originalClipIndex) >= midiClipsByOriginal_.size()) return;
        const MidiClipEntry& clip = midiClipsByStart_[midiClipsByOriginal_[static_cast<size_t>(originalClipIndex)]];
        const size_t n = clip.notesByStart.size();
        if (n == 0) return;

        const bool hasSeam = clipLocalSeamBeatOrNeg >= 0.0;
        const double maxQueryEnd = hasSeam ? std::max(clipLocalEndBeat, clipLocalSeamBeatOrNeg) : clipLocalEndBeat;
        if (n <= 48) {
            // Fast small-clip path preserves strict original note order if sorted or emits cleanly.
            // Wait: notesByStart is sorted by startBeat, so sort small hits to keep originalIndex order.
            std::array<int32_t, 64> smallHits;
            int32_t cnt = 0;
            for (const auto& note : clip.notesByStart) {
                if (note.startBeat > maxQueryEnd) break;
                if ((note.endBeat >= clipLocalStartBeat && note.startBeat <= clipLocalEndBeat) ||
                    (hasSeam && note.startBeat <= clipLocalSeamBeatOrNeg && note.endBeat >= clipLocalSeamBeatOrNeg)) {
                    smallHits[cnt++] = note.originalIndex;
                }
            }
            if (cnt == 0) return;
            std::sort(smallHits.begin(), smallHits.begin() + cnt);
            for (int32_t i = 0; i < cnt; ++i) fn(smallHits[i]);
            return;
        }

        const size_t limit = noteUpperBound(clip, maxQueryEnd);
        if (limit == 0) return;

        std::array<int32_t, kMaxFastStackHits> hits;
        int32_t hitCount = 0;
        bool overflow = false;
        auto pushHit = [&](int32_t origIdx) {
            if (hitCount < kMaxFastStackHits) hits[hitCount++] = origIdx;
            else overflow = true;
        };
        collectNoteCandidates(clip, limit, clipLocalStartBeat, clipLocalEndBeat, clipLocalSeamBeatOrNeg, pushHit);
        if (overflow) {
            // Exceeded 1024 active overlapping notes in a single block: fallback scans linear original count.
            for (int32_t idx = 0; idx < static_cast<int32_t>(n); ++idx) fn(idx);
            return;
        }
        std::sort(hits.begin(), hits.begin() + hitCount);
        int32_t prev = -1;
        for (int32_t i = 0; i < hitCount; ++i) {
            if (hits[i] != prev) {
                prev = hits[i];
                fn(prev);
            }
        }
    }

    // Visit original Track::midiClips indices (sorted ascending) carrying volumeEnvelope overlapping window.
    template <class Fn>
    void forEachMidiEnvelopeClipInWindow(double winStartBeat, double winEndBeat, Fn&& fn) const {
        if (!anyMidiVolumeEnvelope_) return;
        forEachMidiClipInWindow(winStartBeat, winEndBeat, -1.0, [&](int32_t clipIdx) {
            if (midiClipsByStart_[midiClipsByOriginal_[static_cast<size_t>(clipIdx)]].hasVolumeEnvelope)
                fn(clipIdx);
        });
    }

    // Visit active original Track::clips indices (sorted ascending) that could intersect [winStartBeat, winEndBeat].
    // `maxBeatsPerSec` must conservatively bound `sr / spb` (fixed tempo) or max TempoMap BPS (mapped).
    template <class Fn>
    void forEachAudioClipInWindow(double winStartBeat, double winEndBeat, double maxBeatsPerSec, Fn&& fn) const {
        const size_t n = audioClipsByStart_.size();
        if (n == 0) return;
        const double bps = std::max(0.0, maxBeatsPerSec);
        constexpr double kPadBeats = 0.05; // rounding / split-edge slack
        const double qStart = winStartBeat - kPadBeats;
        const double qEnd = winEndBeat + kPadBeats;

        if (n <= 16) {
            for (const auto& c : audioClipsByStart_) {
                if (c.startBeat > qEnd) break;
                const double endB = c.warped ? (c.startBeat + c.warpLenBeats) : (c.startBeat + c.unwarpedSec * bps);
                if (endB >= qStart) fn(c.originalIndex);
            }
            return;
        }

        const size_t limit = audioClipUpperBound(qEnd);
        if (limit == 0) return;

        std::array<int32_t, kMaxFastStackHits> hits;
        int32_t hitCount = 0;
        bool overflow = false;
        auto pushHit = [&](int32_t origIdx) {
            if (hitCount < kMaxFastStackHits) hits[hitCount++] = origIdx;
            else overflow = true;
        };
        collectAudioCandidates(limit, qStart, qEnd, bps, pushHit);
        if (overflow) {
            const size_t totalOrig = audioClipOrigOrder_.size();
            for (size_t i = 0; i < totalOrig; ++i) {
                const AudioClipEntry& c = audioClipsByStart_[audioClipOrigOrder_[i]];
                if (c.startBeat <= qEnd) {
                    const double endB = c.warped ? (c.startBeat + c.warpLenBeats) : (c.startBeat + c.unwarpedSec * bps);
                    if (endB >= qStart) fn(c.originalIndex);
                }
            }
            return;
        }
        std::sort(hits.begin(), hits.begin() + hitCount);
        for (int32_t i = 0; i < hitCount; ++i) fn(hits[i]);
    }

  private:
    size_t midiClipUpperBound(double maxStartBeat) const noexcept;
    size_t noteUpperBound(const MidiClipEntry& clip, double maxStartBeat) const noexcept;
    size_t audioClipUpperBound(double maxStartBeat) const noexcept;

    template <class PushFn>
    void collectMidiClipCandidates(size_t limit, double wStart, double wEnd, double seamBeat, PushFn&& push) const;
    template <class PushFn>
    void collectNoteCandidates(const MidiClipEntry& clip, size_t limit, double wStart, double wEnd, double seamBeat,
                               PushFn&& push) const;
    template <class PushFn>
    void collectAudioCandidates(size_t limit, double qStart, double qEnd, double bps, PushFn&& push) const;

    std::vector<MidiClipEntry> midiClipsByStart_;
    std::vector<size_t> midiClipsByOriginal_;
    std::vector<double> midiClipTree_;
    size_t midiClipTreeBase_ = 0;
    bool anyMidiVolumeEnvelope_ = false;
    double minArtOffsetMs_ = 0.0;
    double maxArtOffsetMs_ = 0.0;

    std::vector<AudioClipEntry> audioClipsByStart_;
    std::vector<size_t> audioClipOrigOrder_; // maps compact sorted list if needed
    std::vector<AudioTreeNode> audioClipTree_;
    size_t audioClipTreeBase_ = 0;

    const void* clipsToken_ = nullptr;
    const void* midiClipsToken_ = nullptr;
    const void* artTableToken_ = nullptr;
};

// ---- inline template tree walkers (allocation-free RT queries) ----

template <class PushFn>
void TimelineIndex::collectMidiClipCandidates(
    size_t limit, double wStart, double wEnd, double seamBeat, PushFn&& push) const {
    const bool hasSeam = seamBeat >= 0.0;
    if (midiClipTreeBase_ == 0) {
        for (size_t i = 0; i < limit; ++i) {
            const auto& c = midiClipsByStart_[i];
            if ((c.endBeat >= wStart && c.startBeat <= wEnd) ||
                (hasSeam && c.startBeat <= seamBeat && c.endBeat >= seamBeat))
                push(c.originalIndex);
        }
        return;
    }
    const size_t maxChunk = (limit - 1) / static_cast<size_t>(kChunkSize);
    struct Frame { size_t node, l, r; };
    std::array<Frame, 64> stack{};
    int sp = 0;
    stack[sp++] = {1, 0, midiClipTreeBase_ - 1};
    while (sp > 0) {
        const Frame f = stack[--sp];
        if (f.l > maxChunk) continue;
        const double maxEnd = midiClipTree_[f.node];
        if (maxEnd < wStart && (!hasSeam || maxEnd < seamBeat)) continue;
        if (f.l == f.r) {
            const size_t begin = f.l * static_cast<size_t>(kChunkSize);
            const size_t end = std::min(limit, begin + static_cast<size_t>(kChunkSize));
            for (size_t i = begin; i < end; ++i) {
                const auto& c = midiClipsByStart_[i];
                if ((c.endBeat >= wStart && c.startBeat <= wEnd) ||
                    (hasSeam && c.startBeat <= seamBeat && c.endBeat >= seamBeat))
                    push(c.originalIndex);
            }
            continue;
        }
        const size_t mid = (f.l + f.r) >> 1;
        if (mid + 1 <= maxChunk) stack[sp++] = {f.node * 2 + 1, mid + 1, f.r};
        stack[sp++] = {f.node * 2, f.l, mid};
    }
}

template <class PushFn>
void TimelineIndex::collectNoteCandidates(
    const MidiClipEntry& clip, size_t limit, double wStart, double wEnd, double seamBeat, PushFn&& push) const {
    const bool hasSeam = seamBeat >= 0.0;
    if (clip.noteChunkCount == 0 || clip.noteChunkMaxEnd.empty()) {
        for (size_t i = 0; i < limit; ++i) {
            const auto& note = clip.notesByStart[i];
            if ((note.endBeat >= wStart && note.startBeat <= wEnd) ||
                (hasSeam && note.startBeat <= seamBeat && note.endBeat >= seamBeat))
                push(note.originalIndex);
        }
        return;
    }
    const size_t base = clip.noteTreeOffset;
    const size_t maxChunk = (limit - 1) / static_cast<size_t>(kChunkSize);
    struct Frame { size_t node, l, r; };
    std::array<Frame, 64> stack{};
    int sp = 0;
    stack[sp++] = {1, 0, base - 1};
    while (sp > 0) {
        const Frame f = stack[--sp];
        if (f.l > maxChunk) continue;
        const double maxEnd = clip.noteChunkMaxEnd[f.node];
        if (maxEnd < wStart && (!hasSeam || maxEnd < seamBeat)) continue;
        if (f.l == f.r) {
            const size_t begin = f.l * static_cast<size_t>(kChunkSize);
            const size_t end = std::min(limit, begin + static_cast<size_t>(kChunkSize));
            for (size_t i = begin; i < end; ++i) {
                const auto& note = clip.notesByStart[i];
                if ((note.endBeat >= wStart && note.startBeat <= wEnd) ||
                    (hasSeam && note.startBeat <= seamBeat && note.endBeat >= seamBeat))
                    push(note.originalIndex);
            }
            continue;
        }
        const size_t mid = (f.l + f.r) >> 1;
        if (mid + 1 <= maxChunk) stack[sp++] = {f.node * 2 + 1, mid + 1, f.r};
        stack[sp++] = {f.node * 2, f.l, mid};
    }
}

template <class PushFn>
void TimelineIndex::collectAudioCandidates(
    size_t limit, double qStart, double qEnd, double bps, PushFn&& push) const {
    if (audioClipTreeBase_ == 0) {
        for (size_t i = 0; i < limit; ++i) {
            const auto& c = audioClipsByStart_[i];
            const double endB = c.warped ? (c.startBeat + c.warpLenBeats) : (c.startBeat + c.unwarpedSec * bps);
            if (endB >= qStart && c.startBeat <= qEnd) push(c.originalIndex);
        }
        return;
    }
    const size_t maxChunk = (limit - 1) / static_cast<size_t>(kChunkSize);
    struct Frame { size_t node, l, r; };
    std::array<Frame, 64> stack{};
    int sp = 0;
    stack[sp++] = {1, 0, audioClipTreeBase_ - 1};
    while (sp > 0) {
        const Frame f = stack[--sp];
        if (f.l > maxChunk) continue;
        const AudioTreeNode& info = audioClipTree_[f.node];
        const double conservativeMaxEnd = std::max(info.maxWarpedEndBeat, info.maxStartBeat + info.maxUnwarpedSec * bps);
        if (conservativeMaxEnd < qStart) continue;
        if (f.l == f.r) {
            const size_t begin = f.l * static_cast<size_t>(kChunkSize);
            const size_t end = std::min(limit, begin + static_cast<size_t>(kChunkSize));
            for (size_t i = begin; i < end; ++i) {
                const auto& c = audioClipsByStart_[i];
                const double endB = c.warped ? (c.startBeat + c.warpLenBeats) : (c.startBeat + c.unwarpedSec * bps);
                if (endB >= qStart && c.startBeat <= qEnd) push(c.originalIndex);
            }
            continue;
        }
        const size_t mid = (f.l + f.r) >> 1;
        if (mid + 1 <= maxChunk) stack[sp++] = {f.node * 2 + 1, mid + 1, f.r};
        stack[sp++] = {f.node * 2, f.l, mid};
    }
}

} // namespace nota
