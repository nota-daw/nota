// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "TimelineIndex.h"
#include "ArticulationTable.h"
#include "Track.h"

namespace nota {

namespace {

size_t nextPow2(size_t v) {
    size_t p = 1;
    while (p < v) p <<= 1;
    return p;
}

} // namespace

size_t TimelineIndex::midiClipUpperBound(double maxStartBeat) const noexcept {
    auto it = std::upper_bound(midiClipsByStart_.begin(), midiClipsByStart_.end(), maxStartBeat,
        [](double b, const MidiClipEntry& c) { return b < c.startBeat; });
    return static_cast<size_t>(it - midiClipsByStart_.begin());
}

size_t TimelineIndex::noteUpperBound(const MidiClipEntry& clip, double maxStartBeat) const noexcept {
    auto it = std::upper_bound(clip.notesByStart.begin(), clip.notesByStart.end(), maxStartBeat,
        [](double b, const NoteSpan& n) { return b < n.startBeat; });
    return static_cast<size_t>(it - clip.notesByStart.begin());
}

size_t TimelineIndex::audioClipUpperBound(double maxStartBeat) const noexcept {
    auto it = std::upper_bound(audioClipsByStart_.begin(), audioClipsByStart_.end(), maxStartBeat,
        [](double b, const AudioClipEntry& c) { return b < c.startBeat; });
    return static_cast<size_t>(it - audioClipsByStart_.begin());
}

std::shared_ptr<const TimelineIndex> TimelineIndex::buildForTrack(const Track& track) {
    auto idx = std::make_shared<TimelineIndex>();

    // Collect articulation min/max shift bounds if present (track-wide immutable table).
    if (track.articulationTable) {
        double lo = 0.0, hi = 0.0;
        bool any = false;
        for (const ArticulationEntry& e : track.articulationTable->entries()) {
            const double off = e.offsetMs;
            if (!any) {
                lo = hi = off;
                any = true;
            } else {
                lo = std::min(lo, off);
                hi = std::max(hi, off);
            }
        }
        idx->minArtOffsetMs_ = lo;
        idx->maxArtOffsetMs_ = hi;
    }

    // 1. Index MIDI clips and their internal notes.
    const size_t nm = track.midiClips.size();
    if (nm > 0) {
        idx->midiClipsByStart_.reserve(nm);
        for (size_t ci = 0; ci < nm; ++ci) {
            const MidiClip& clip = track.midiClips[ci];
            MidiClipEntry entry;
            entry.originalIndex = static_cast<int32_t>(ci);
            entry.startBeat = clip.startBeat;
            entry.endBeat = clip.startBeat + clip.lengthBeats;
            entry.active = clip.active;
            entry.hasVolumeEnvelope = clip.active && !clip.volumeEnvelope.points.empty();
            entry.hasControllers = clip.active && !clip.controllers.empty();
            if (entry.hasVolumeEnvelope) idx->anyMidiVolumeEnvelope_ = true;

            const size_t nn = clip.notes.size();
            if (nn > 0) {
                entry.notesByStart.reserve(nn);
                bool first = true;
                for (size_t ni = 0; ni < nn; ++ni) {
                    const Note& note = clip.notes[ni];
                    if (note.startBeat >= clip.lengthBeats || note.startBeat + note.lengthBeats <= 0.0) continue;
                    const double startB = std::max(0.0, note.startBeat);
                    const double endB = std::min(clip.lengthBeats, note.startBeat + note.lengthBeats);
                    if (first) {
                        entry.notesMinRawStartBeat = note.startBeat;
                        entry.notesMaxEndBeat = endB;
                        first = false;
                    } else {
                        entry.notesMinRawStartBeat = std::min(entry.notesMinRawStartBeat, note.startBeat);
                        entry.notesMaxEndBeat = std::max(entry.notesMaxEndBeat, endB);
                    }
                    entry.notesByStart.push_back({startB, endB, static_cast<int32_t>(ni)});
                }
                std::sort(entry.notesByStart.begin(), entry.notesByStart.end(),
                    [](const NoteSpan& a, const NoteSpan& b) {
                        if (a.startBeat != b.startBeat) return a.startBeat < b.startBeat;
                        return a.originalIndex < b.originalIndex;
                    });
                const size_t sn = entry.notesByStart.size();
                if (sn > 48) {
                    const size_t chunks = (sn + static_cast<size_t>(kChunkSize) - 1) / static_cast<size_t>(kChunkSize);
                    const size_t base = nextPow2(chunks);
                    entry.noteChunkCount = chunks;
                    entry.noteTreeOffset = base;
                    entry.noteChunkMaxEnd.assign(base * 2, -std::numeric_limits<double>::infinity());
                    for (size_t k = 0; k < chunks; ++k) {
                        const size_t b = k * static_cast<size_t>(kChunkSize);
                        const size_t e = std::min(sn, b + static_cast<size_t>(kChunkSize));
                        double m = entry.notesByStart[b].endBeat;
                        for (size_t j = b + 1; j < e; ++j) m = std::max(m, entry.notesByStart[j].endBeat);
                        entry.noteChunkMaxEnd[base + k] = m;
                    }
                    for (size_t node = base - 1; node >= 1; --node) {
                        entry.noteChunkMaxEnd[node] =
                            std::max(entry.noteChunkMaxEnd[node * 2], entry.noteChunkMaxEnd[node * 2 + 1]);
                    }
                }
            }
            idx->midiClipsByStart_.push_back(std::move(entry));
        }

        std::sort(idx->midiClipsByStart_.begin(), idx->midiClipsByStart_.end(),
            [](const MidiClipEntry& a, const MidiClipEntry& b) {
                if (a.startBeat != b.startBeat) return a.startBeat < b.startBeat;
                return a.originalIndex < b.originalIndex;
            });
        idx->midiClipsByOriginal_.resize(nm, 0);
        for (size_t i = 0; i < nm; ++i)
            idx->midiClipsByOriginal_[static_cast<size_t>(idx->midiClipsByStart_[i].originalIndex)] = i;

        if (nm > 16) {
            const size_t chunks = (nm + static_cast<size_t>(kChunkSize) - 1) / static_cast<size_t>(kChunkSize);
            const size_t base = nextPow2(chunks);
            idx->midiClipTreeBase_ = base;
            idx->midiClipTree_.assign(base * 2, -std::numeric_limits<double>::infinity());
            for (size_t k = 0; k < chunks; ++k) {
                const size_t b = k * static_cast<size_t>(kChunkSize);
                const size_t e = std::min(nm, b + static_cast<size_t>(kChunkSize));
                double m = idx->midiClipsByStart_[b].endBeat;
                for (size_t j = b + 1; j < e; ++j) m = std::max(m, idx->midiClipsByStart_[j].endBeat);
                idx->midiClipTree_[base + k] = m;
            }
            for (size_t node = base - 1; node >= 1; --node) {
                idx->midiClipTree_[node] = std::max(idx->midiClipTree_[node * 2], idx->midiClipTree_[node * 2 + 1]);
            }
        }
    }

    // 2. Index active Audio clips.
    const size_t na = track.clips.size();
    if (na > 0) {
        idx->audioClipsByStart_.reserve(na);
        for (size_t ci = 0; ci < na; ++ci) {
            const AudioClip& clip = track.clips[ci];
            if (!clip.active) continue;
            AudioClipEntry entry;
            entry.originalIndex = static_cast<int32_t>(ci);
            entry.startBeat = clip.startBeat;
            entry.warped = clip.warpEnabled;
            if (clip.warpEnabled) {
                entry.warpLenBeats = std::max(0.0, clip.warpPlayLen());
            } else if (clip.sample && clip.sample->sourceSampleRate > 0.0) {
                const int64_t len = clip.effectiveLength();
                const double ratio = clip.pitchRatio();
                entry.unwarpedSec = (len > 0 && ratio > 0.0)
                    ? static_cast<double>(len) / (clip.sample->sourceSampleRate * ratio)
                    : 0.0;
            }
            idx->audioClipsByStart_.push_back(entry);
        }
        const size_t act = idx->audioClipsByStart_.size();
        std::sort(idx->audioClipsByStart_.begin(), idx->audioClipsByStart_.end(),
            [](const AudioClipEntry& a, const AudioClipEntry& b) {
                if (a.startBeat != b.startBeat) return a.startBeat < b.startBeat;
                return a.originalIndex < b.originalIndex;
            });
        // Preserve compact ascending-original map for overflow fallback.
        idx->audioClipOrigOrder_.resize(act);
        for (size_t i = 0; i < act; ++i) idx->audioClipOrigOrder_[i] = i;
        std::sort(idx->audioClipOrigOrder_.begin(), idx->audioClipOrigOrder_.end(),
            [&](size_t a, size_t b) {
                return idx->audioClipsByStart_[a].originalIndex < idx->audioClipsByStart_[b].originalIndex;
            });

        if (act > 16) {
            const size_t chunks = (act + static_cast<size_t>(kChunkSize) - 1) / static_cast<size_t>(kChunkSize);
            const size_t base = nextPow2(chunks);
            idx->audioClipTreeBase_ = base;
            idx->audioClipTree_.resize(base * 2);
            for (size_t k = 0; k < chunks; ++k) {
                const size_t b = k * static_cast<size_t>(kChunkSize);
                const size_t e = std::min(act, b + static_cast<size_t>(kChunkSize));
                AudioTreeNode node;
                node.maxStartBeat = idx->audioClipsByStart_[e - 1].startBeat;
                for (size_t j = b; j < e; ++j) {
                    const AudioClipEntry& c = idx->audioClipsByStart_[j];
                    if (c.warped) {
                        node.maxWarpedEndBeat = std::max(node.maxWarpedEndBeat, c.startBeat + c.warpLenBeats);
                    } else {
                        node.maxUnwarpedSec = std::max(node.maxUnwarpedSec, c.unwarpedSec);
                    }
                }
                idx->audioClipTree_[base + k] = node;
            }
            for (size_t n = base - 1; n >= 1; --n) {
                const AudioTreeNode& L = idx->audioClipTree_[n * 2];
                const AudioTreeNode& R = idx->audioClipTree_[n * 2 + 1];
                AudioTreeNode parent;
                parent.maxStartBeat = std::max(L.maxStartBeat, R.maxStartBeat);
                parent.maxUnwarpedSec = std::max(L.maxUnwarpedSec, R.maxUnwarpedSec);
                parent.maxWarpedEndBeat = std::max(L.maxWarpedEndBeat, R.maxWarpedEndBeat);
                idx->audioClipTree_[n] = parent;
            }
        }
    }

    return idx;
}

} // namespace nota
