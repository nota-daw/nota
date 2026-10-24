// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Transport / musical clock (AR-9). The single source of time. Owned and
// mutated by the audio thread; UI-visible state is mirrored into atomics that
// the UI polls.

#pragma once

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <limits>

#include "TransportInfo.h"

namespace nota {

enum class TempoCurve : int32_t {
    Linear = 0,
    Square = 1,
    Triangle = 2,
    Smooth = 3
};

struct TempoPoint {
    double beat = 0.0;
    double bpm = 120.0;
    double secondsAtBeat = 0.0;
    int32_t curve = static_cast<int32_t>(TempoCurve::Linear);
};

class Transport {
public:
    static constexpr int32_t kMaxTempoPoints = 4096;

    Transport() {
        tempoPoints_[0] = TempoPoint{};
    }

    void setSampleRate(double sr) {
        if (!(sr > 0.0) || !std::isfinite(sr)) return;
        const double elapsed = sampleRate_ > 0.0 ? playheadSamples_ / sampleRate_ : 0.0;
        sampleRate_ = sr;
        sampleRateSnapshot_.store(sr, std::memory_order_relaxed);
        playheadSamples_ = elapsed * sampleRate_;
        publish();
    }

    double sampleRate() const { return sampleRateSnapshot_.load(std::memory_order_relaxed); }
    double uiBpm() const { return uiBpm_.load(std::memory_order_relaxed); }

    static bool prepareTempoMap(TempoPoint* points, int32_t count) {
        if (!points || count < 1 || count > kMaxTempoPoints) return false;
        if (std::abs(points[0].beat) > 1e-9) return false;
        points[0].beat = 0.0;
        points[0].secondsAtBeat = 0.0;
        for (int32_t i = 0; i < count; ++i) {
            if (!std::isfinite(points[i].beat) || !std::isfinite(points[i].bpm)
                || points[i].beat < 0.0 || !(points[i].bpm >= 1.0 && points[i].bpm <= 999.0)
                || points[i].curve < 0 || points[i].curve > 3) return false;
            if (i > 0 && !(points[i].beat > points[i - 1].beat)) return false;
            if (i > 0)
                points[i].secondsAtBeat = points[i - 1].secondsAtBeat
                    + integrateSegment(points[i - 1], points[i], 1.0);
        }
        return true;
    }

    void play()  { playing_ = true;  publish(); }

    void stop()  {
        playing_ = false;
        playheadSamples_ = samplesAtBeat(startAnchorBeat_);
        publish();
    }

    void setBpm(double bpm) {
        if (!(bpm >= 1.0 && bpm <= 999.0) || !std::isfinite(bpm)) return;
        const double beat = positionBeats();
        const double anchorBeat = startAnchorBeat_;
        tempoScale_ = 1.0;
        tempoPoints_[0] = TempoPoint{0.0, bpm, 0.0, static_cast<int32_t>(TempoCurve::Linear)};
        tempoPointCount_ = 1;
        bpm_ = bpm;
        startAnchorBeat_ = anchorBeat;
        playheadSamples_ = samplesAtBeat(beat);
        publish();
    }

    void setTempoMap(const TempoPoint* points, int32_t count) {
        if (!points || count < 1 || count > kMaxTempoPoints || std::abs(points[0].beat) > 1e-9) return;
        const double beat = positionBeats();
        const double anchorBeat = startAnchorBeat_;
        tempoScale_ = 1.0;
        for (int32_t i = 0; i < count; ++i) tempoPoints_[static_cast<size_t>(i)] = points[i];
        tempoPointCount_ = count;
        bpm_ = tempoPoints_[0].bpm;
        startAnchorBeat_ = anchorBeat;
        playheadSamples_ = samplesAtBeat(beat);
        publish();
    }

    double bpm() const { return bpmAtBeat(positionBeats()); }

    void setModulatedBpm(double bpm) {
        if (!std::isfinite(bpm)) return;
        bpm = std::clamp(bpm, 1.0, 999.0);
        const double beat = positionBeats();
        const double anchorBeat = startAnchorBeat_;
        const double baseBpm = bpmAtBeat(beat) / tempoScale_;
        if (!(baseBpm > 0.0) || !std::isfinite(baseBpm)) return;
        tempoScale_ = std::clamp(bpm / baseBpm, 1.0 / 999.0, 999.0);
        startAnchorBeat_ = anchorBeat;
        playheadSamples_ = samplesAtBeat(beat);
        publish();
    }
    void setTimeSignature(int num, int denom) { if (num > 0 && denom > 0) { tsNum_ = num; tsDenom_ = denom; } }
    void setMetronome(bool on) { metronome_ = on; }
    void setLoop(bool enabled, double startBeat, double endBeat) {
        loopEnabled_ = enabled && endBeat > startBeat;
        loopStartBeat_ = std::max(0.0, startBeat);
        loopEndBeat_ = std::max(loopStartBeat_, endBeat);
    }
    void seekBeats(double beat) {
        if (!std::isfinite(beat)) return;
        beat = std::max(0.0, beat);
        playheadSamples_ = samplesAtBeat(beat);
        startAnchorBeat_ = beat;
        publish();
    }

    double samplesPerBeat() const { return samplesPerBeatAtBeat(positionBeats()); }
    double samplesPerBeatAtBeat(double beat) const {
        const double bpm = bpmAtBeat(beat);
        return sampleRate_ * 60.0 / bpm;
    }
    double positionBeats() const { return beatAtSample(playheadSamples_); }
    double beatAtSample(double sample) const {
        if (!(sample > 0.0) || !(sampleRate_ > 0.0)) return 0.0;
        const double seconds = sample / sampleRate_ * tempoScale_;
        int32_t idx = pointIndexAtSeconds(seconds);
        const TempoPoint& p = tempoPoints_[static_cast<size_t>(idx)];
        if (idx + 1 >= tempoPointCount_)
            return p.beat + (seconds - p.secondsAtBeat) * p.bpm / 60.0;
        double lo = p.beat;
        double hi = tempoPoints_[static_cast<size_t>(idx + 1)].beat;
        for (int i = 0; i < 28; ++i) {
            const double mid = (lo + hi) * 0.5;
            if (secondsAtBeat(mid) < seconds) lo = mid;
            else hi = mid;
        }
        return (lo + hi) * 0.5;
    }
    double samplesAtBeat(double beat) const {
        return sampleRate_ * secondsAtBeat(std::max(0.0, beat)) / tempoScale_;
    }
    double playheadSamples() const { return playheadSamples_; }
    bool   isPlaying() const { return playing_; }
    bool   metronomeEnabled() const { return metronome_; }
    int    beatsPerBar() const { return tsNum_; }

    void advanceBy(int32_t frames) {
        if (playing_) playheadSamples_ += frames;
        publish();
    }

    void wrapToLoopStart() { playheadSamples_ = samplesAtBeat(loopStartBeat_); publish(); }

    bool   isLooping() const { return loopEnabled_; }
    double loopStartSamples() const { return samplesAtBeat(loopStartBeat_); }
    double loopEndSamples() const { return samplesAtBeat(loopEndBeat_); }
    double loopStartBeat() const { return loopStartBeat_; }
    double loopEndBeat() const { return loopEndBeat_; }
    double samplesUntilNextTempoPoint() const {
        const int32_t idx = pointIndexAtBeat(positionBeats());
        if (idx + 1 >= tempoPointCount_) return std::numeric_limits<double>::infinity();
        return std::max(0.0, samplesAtBeat(tempoPoints_[static_cast<size_t>(idx + 1)].beat) - playheadSamples_);
    }
    bool   hasTempoMap() const { return tempoPointCount_ > 1; }
    int    timeSigDenom() const { return tsDenom_; }

    TransportInfo transportInfo(double blockStartSamples) const {
        const double beat = beatAtSample(blockStartSamples);
        TransportInfo ti;
        ti.bpm = bpmAtBeat(beat);
        ti.tsNum = tsNum_;
        ti.tsDenom = tsDenom_;
        ti.ppqPosition = beat;
        ti.timeInSamples = static_cast<int64_t>(blockStartSamples);
        ti.timeInSeconds = sampleRate_ > 0.0 ? blockStartSamples / sampleRate_ : 0.0;
        ti.isPlaying = playing_;
        ti.isLooping = loopEnabled_;
        ti.ppqLoopStart = loopStartBeat_;
        ti.ppqLoopEnd = loopEndBeat_;
        return ti;
    }

    double uiPositionBeats() const { return uiBeats_.load(std::memory_order_relaxed); }
    bool   uiIsPlaying() const { return uiPlaying_.load(std::memory_order_relaxed); }

private:
    static double shapedT(int32_t curve, double t) {
        t = std::clamp(t, 0.0, 1.0);
        switch (curve) {
            case static_cast<int32_t>(TempoCurve::Square): return 0.0;
            case static_cast<int32_t>(TempoCurve::Triangle):
                return t < 0.5 ? 2.0 * t * t : 1.0 - 2.0 * (1.0 - t) * (1.0 - t);
            case static_cast<int32_t>(TempoCurve::Smooth): return t * t * (3.0 - 2.0 * t);
            default: return t;
        }
    }

    static double bpmOnSegment(const TempoPoint& a, const TempoPoint& b, double t) {
        return a.bpm + (b.bpm - a.bpm) * shapedT(a.curve, t);
    }

    static double integrateSegment(const TempoPoint& a, const TempoPoint& b, double tEnd) {
        tEnd = std::clamp(tEnd, 0.0, 1.0);
        const double beatLength = b.beat - a.beat;
        if (!(beatLength > 0.0) || tEnd <= 0.0) return 0.0;
        if (a.curve == static_cast<int32_t>(TempoCurve::Square))
            return beatLength * tEnd * 60.0 / a.bpm;
        if (a.curve == static_cast<int32_t>(TempoCurve::Linear)) {
            const double delta = b.bpm - a.bpm;
            const double vEnd = a.bpm + delta * tEnd;
            if (std::abs(delta) < 1e-12) return beatLength * tEnd * 60.0 / a.bpm;
            return beatLength * 60.0 * std::log(vEnd / a.bpm) / delta;
        }
        constexpr int n = 24;
        const double h = tEnd / n;
        double sum = 1.0 / a.bpm + 1.0 / bpmOnSegment(a, b, tEnd);
        for (int i = 1; i < n; ++i)
            sum += (i & 1 ? 4.0 : 2.0) / bpmOnSegment(a, b, h * i);
        return beatLength * 60.0 * h * sum / 3.0;
    }

    int32_t pointIndexAtBeat(double beat) const {
        int32_t lo = 0, hi = tempoPointCount_;
        while (lo < hi) {
            const int32_t mid = lo + (hi - lo) / 2;
            if (tempoPoints_[static_cast<size_t>(mid)].beat <= beat) lo = mid + 1;
            else hi = mid;
        }
        return std::max(0, lo - 1);
    }

    int32_t pointIndexAtSeconds(double seconds) const {
        int32_t lo = 0, hi = tempoPointCount_;
        while (lo < hi) {
            const int32_t mid = lo + (hi - lo) / 2;
            if (tempoPoints_[static_cast<size_t>(mid)].secondsAtBeat <= seconds) lo = mid + 1;
            else hi = mid;
        }
        return std::max(0, lo - 1);
    }

    double bpmAtBeat(double beat) const {
        const int32_t idx = pointIndexAtBeat(beat);
        const TempoPoint& a = tempoPoints_[static_cast<size_t>(idx)];
        double baseBpm = a.bpm;
        if (idx + 1 < tempoPointCount_ && beat > a.beat) {
            const TempoPoint& b = tempoPoints_[static_cast<size_t>(idx + 1)];
            const double t = (beat - a.beat) / (b.beat - a.beat);
            baseBpm = bpmOnSegment(a, b, t);
        }
        return std::clamp(baseBpm * tempoScale_, 1.0, 999.0);
    }

    double secondsAtBeat(double beat) const {
        if (!(beat > 0.0)) return 0.0;
        const int32_t idx = pointIndexAtBeat(beat);
        const TempoPoint& a = tempoPoints_[static_cast<size_t>(idx)];
        if (idx + 1 >= tempoPointCount_)
            return a.secondsAtBeat + (beat - a.beat) * 60.0 / a.bpm;
        const TempoPoint& b = tempoPoints_[static_cast<size_t>(idx + 1)];
        const double t = (beat - a.beat) / (b.beat - a.beat);
        return a.secondsAtBeat + integrateSegment(a, b, t);
    }

    void publish() {
        const double beat = positionBeats();
        uiBeats_.store(beat, std::memory_order_relaxed);
        uiBpm_.store(bpmAtBeat(beat), std::memory_order_relaxed);
        uiPlaying_.store(playing_, std::memory_order_relaxed);
    }

    double sampleRate_ = 44100.0;
    std::atomic<double> sampleRateSnapshot_{44100.0};
    double bpm_ = 120.0;
    double tempoScale_ = 1.0; // realtime modulation factor over the authored tempo map
    int    tsNum_ = 4, tsDenom_ = 4;
    bool   playing_ = false;
    double playheadSamples_ = 0.0;
    double startAnchorBeat_ = 0.0;
    bool   loopEnabled_ = false;
    double loopStartBeat_ = 0.0, loopEndBeat_ = 0.0;
    bool   metronome_ = false;
    std::array<TempoPoint, kMaxTempoPoints> tempoPoints_{};
    int32_t tempoPointCount_ = 1;

    std::atomic<double> uiBeats_{0.0};
    std::atomic<double> uiBpm_{120.0};
    std::atomic<bool>   uiPlaying_{false};
};

} // namespace nota
