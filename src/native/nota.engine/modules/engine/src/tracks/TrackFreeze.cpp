// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TrackOps — freeze capture, unfreeze, and serialized frozen-audio state (M7).

#include "tracks/TrackOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "core/TempoMap.h"
#include "core/Transport.h"
#include "model/Graph.h"
#include "model/Track.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <utility>
#include <vector>

namespace nota::detail {

// --- freeze (M7) -----------------------------------------------------------
// Frozen-audio blob layout: [double frozenSpb][int64 frameCount] then frameCount
// interleaved-stereo float samples. Small, self-describing; version-free (any change
// bumps the whole project format).
namespace {
constexpr int kFreezeHeaderBytes = static_cast<int>(sizeof(double) + sizeof(int64_t));
// Negative spb distinguishes a mapped freeze from legacy PCM. FTM2 (specs/099) stores
// {beat, bpm, incomingBpm, curve}; FTM1 (#568 builds) stored the old step/linear/Bezier
// points and is read back approximately (Bezier segments as linear).
constexpr uint32_t kFreezeMapMagicV1 = 0x314d5446; // FTM1
constexpr uint32_t kFreezeMapMagic = 0x324d5446;   // FTM2
constexpr int kFreezeMapHeaderBytes = 2 * sizeof(uint32_t) + 2 * sizeof(double);
constexpr int kFreezePointBytesV1 = 8 * sizeof(double);
constexpr int kFreezePointBytes = 4 * sizeof(double); // explicit fields, never native struct padding
} // namespace

int64_t TrackOps::beginFreeze(int32_t trackId, double lengthBeats, double tailSec, double startBeat) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0;
    if (t->type() != TrackType::Instrument && t->type() != TrackType::Audio) return 0;
    const double sr = rt_.transport_.sampleRate() > 0.0 ? rt_.transport_.sampleRate() : 44100.0;
    const double spb = rt_.transport_.samplesPerBeat();
    if (spb <= 0.0 || !std::isfinite(lengthBeats) || lengthBeats <= 0.0 || !std::isfinite(startBeat) || startBeat < 0 ||
        !std::isfinite(tailSec))
        return 0;
    // tailSec lets reverb/delay tails ring past the last clip (0 for an exact-range bounce).
    const double seconds = rt_.beatToSeconds(startBeat + lengthBeats) - rt_.beatToSeconds(startBeat);
    if (!std::isfinite(seconds) || seconds <= 0 ||
        seconds + std::max(0.0, tailSec) >
            static_cast<double>(std::numeric_limits<int64_t>::max() / (2 * sizeof(float))) / sr)
        return 0;
    int64_t frames = static_cast<int64_t>(std::ceil(seconds * sr)) + static_cast<int64_t>(std::max(0.0, tailSec) * sr);
    if (frames <= 0) return 0;
    rt_.freezeCaptureBuf_.assign(static_cast<size_t>(frames) * 2, 0.0f);
    rt_.freezeCapFrames_ = frames;
    rt_.freezeCursor_ = 0;
    rt_.freezeCaptureSpb_ = spb;
    rt_.freezeCaptureTiming_.reset();
    if (rt_.tempoMap() || startBeat != 0.0)
        rt_.freezeCaptureTiming_ =
            std::make_shared<const Track::FrozenTiming>(Track::FrozenTiming{rt_.authoring_->tempoMap, sr, startBeat});
    rt_.freezeCaptureTrackId_.store(trackId, std::memory_order_relaxed);
    return frames;
}

void TrackOps::endFreeze(int32_t trackId) {
    rt_.freezeCaptureTrackId_.store(-1, std::memory_order_relaxed);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || rt_.freezeCaptureBuf_.empty()) {
        cancelFreeze();
        return;
    }
    auto buf = std::make_shared<std::vector<float>>(std::move(rt_.freezeCaptureBuf_));
    rt_.freezeCaptureBuf_ = std::vector<float>(); // reset the moved-from vector
    auto nt = rt_.cloneTrack(*old);
    nt->frozenBuf = buf; // shared_ptr<vector> → shared_ptr<const vector>
    nt->frozenSpb = rt_.freezeCaptureSpb_;
    nt->frozenTiming = std::move(rt_.freezeCaptureTiming_);
    nt->setFrozen(true);
    rt_.republishWithTrackRaw(trackId, std::move(nt)); // a view toggle, not an undo step
    rt_.freezeCapFrames_ = 0;
    rt_.freezeCursor_ = 0;
}

void TrackOps::cancelFreeze() {
    rt_.freezeCaptureTrackId_.store(-1, std::memory_order_relaxed);
    rt_.freezeCaptureBuf_ = std::vector<float>();
    rt_.freezeCapFrames_ = 0;
    rt_.freezeCursor_ = 0;
    rt_.freezeCaptureTiming_.reset();
}

int64_t TrackOps::takeFreezeCapture(float* out, int64_t capFrames) {
    const int64_t n = (out && capFrames > 0) ? std::min(capFrames, rt_.freezeCursor_) : 0;
    if (n > 0) std::memcpy(out, rt_.freezeCaptureBuf_.data(), static_cast<size_t>(n) * 2 * sizeof(float));
    cancelFreeze(); // disarm + free the capture; the track itself is untouched
    return n;
}

void TrackOps::unfreeze(int32_t trackId) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !old->frozen()) return;
    auto nt = rt_.cloneTrack(*old);
    nt->setFrozen(false);
    nt->frozenBuf.reset();
    nt->frozenSpb = 0.0;
    nt->frozenTiming.reset();
    rt_.republishWithTrackRaw(trackId, std::move(nt));
}

bool TrackOps::trackFrozen(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->frozen() && t->frozenBuf != nullptr;
}

int32_t TrackOps::freezeGetState(int32_t trackId, uint8_t* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->frozen() || !t->frozenBuf) return 0;
    const std::vector<float>& buf = *t->frozenBuf;
    const int64_t frames = static_cast<int64_t>(buf.size() / 2);
    const auto* timing = t->frozenTiming.get();
    const uint32_t count = timing && timing->map ? static_cast<uint32_t>(timing->map->points.size()) : 0;
    const int64_t header = kFreezeHeaderBytes + (timing ? kFreezeMapHeaderBytes + count * kFreezePointBytes : 0);
    const int64_t size = header + frames * 2 * static_cast<int64_t>(sizeof(float));
    if (size > std::numeric_limits<int32_t>::max()) return 0; // 2 GB cap
    if (out && cap >= size) {
        const double spb = timing ? -t->frozenSpb : t->frozenSpb;
        std::memcpy(out, &spb, sizeof(double));
        std::memcpy(out + sizeof(double), &frames, sizeof(int64_t));
        if (timing) {
            uint8_t* cursor = out + kFreezeHeaderBytes;
            auto write = [&](const auto& value) {
                std::memcpy(cursor, &value, sizeof(value));
                cursor += sizeof(value);
            };
            write(kFreezeMapMagic);
            write(count);
            write(timing->sampleRate);
            write(timing->startBeat);
            if (timing->map)
                for (const auto& p : timing->map->points) {
                    const double values[] = {p.beat, p.bpm, p.incomingBpm, static_cast<double>(p.curve)};
                    std::memcpy(cursor, values, sizeof(values));
                    cursor += sizeof(values);
                }
        }
        std::memcpy(out + header, buf.data(), static_cast<size_t>(frames) * 2 * sizeof(float));
    }
    return static_cast<int32_t>(size);
}

void TrackOps::freezeSetState(int32_t trackId, const uint8_t* data, int32_t size) {
    if (!data || size < kFreezeHeaderBytes) return;
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return;
    double spb = 0.0;
    int64_t frames = 0;
    std::memcpy(&spb, data, sizeof(double));
    std::memcpy(&frames, data + sizeof(double), sizeof(int64_t));
    if (frames <= 0 || !std::isfinite(spb) || spb == 0.0) return;
    int64_t header = kFreezeHeaderBytes;
    std::shared_ptr<const Track::FrozenTiming> timing;
    if (spb < 0) {
        if (size - header < kFreezeMapHeaderBytes) return;
        const uint8_t* cursor = data + header;
        auto read = [&](auto& value) {
            std::memcpy(&value, cursor, sizeof(value));
            cursor += sizeof(value);
        };
        uint32_t magic = 0, count = 0;
        double sr = 0, start = 0;
        read(magic);
        read(count);
        read(sr);
        read(start);
        const bool v1 = magic == kFreezeMapMagicV1;
        if ((magic != kFreezeMapMagic && !v1) || count > TempoMap::kMaxPoints || !std::isfinite(sr) || sr <= 0 ||
            !std::isfinite(start) || start < 0)
            return;
        header += kFreezeMapHeaderBytes + static_cast<int64_t>(count) * (v1 ? kFreezePointBytesV1 : kFreezePointBytes);
        if (header > size || frames > (size - header) / (2 * static_cast<int64_t>(sizeof(float)))) return;
        std::vector<TempoMap::Point> points;
        points.reserve(count);
        double prevBpm = 0;
        int prevShape = 1;
        for (uint32_t i = 0; i < count; ++i) {
            if (v1) { // beat, bpm, shape, x1, y1, x2, y2, incoming_bpm
                double v[8];
                std::memcpy(v, cursor, sizeof(v));
                cursor += sizeof(v);
                if (!std::isfinite(v[2]) || v[2] < 0 || v[2] > 2) return;
                const double bpm = TempoMap::clampBpm(v[1]);
                const double incoming = i == 0           ? bpm
                                        : prevShape == 0 ? prevBpm
                                                         : TempoMap::clampBpm(v[7] > 0 ? v[7] : v[1]);
                points.push_back({v[0], bpm, incoming, 0.0f});
                prevBpm = bpm;
                prevShape = static_cast<int>(v[2]);
            } else {
                double v[4];
                std::memcpy(v, cursor, sizeof(v));
                cursor += sizeof(v);
                points.push_back({v[0], v[1], v[2], static_cast<float>(v[3])});
            }
        }
        if (!points.empty() && !TempoMap::valid(points)) return;
        auto map = points.empty() ? nullptr : std::make_shared<const TempoMap>(std::move(points));
        timing = std::make_shared<const Track::FrozenTiming>(Track::FrozenTiming{std::move(map), sr, start});
        spb = -spb;
    }
    if (frames > (size - header) / (2 * static_cast<int64_t>(sizeof(float)))) return;
    auto buf = std::make_shared<std::vector<float>>(static_cast<size_t>(frames) * 2);
    std::memcpy(buf->data(), data + header, static_cast<size_t>(frames) * 2 * sizeof(float));
    auto nt = rt_.cloneTrack(*old);
    nt->frozenBuf = buf;
    nt->frozenSpb = spb;
    nt->frozenTiming = std::move(timing);
    nt->setFrozen(true);
    rt_.republishWithTrackRaw(trackId, std::move(nt));
}

} // namespace nota::detail
