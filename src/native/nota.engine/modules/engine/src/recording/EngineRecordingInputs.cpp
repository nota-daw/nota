// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RecordingOps — track arming, capture channels/layout, live input monitoring, and sinc resampler.

#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "model/EngineTypes.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "recording/CaptureRing.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

// --- live MIDI, arming, recording ------------------------------------------
void RecordingOps::setTrackArmed(int32_t trackId, bool armed) {
    if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
    auto t = rt_.findTrackAuthoring(trackId);
    const bool wasArmed = t && t->armed();
    if (t) t->setArmed(armed);
    if (t && t->type() == TrackType::Audio && armed != wasArmed) {
        rt_.devices().recomputeRouting(); // an armed track meters its source track's tap (#352)
        updateMonitorInput();             // ... or the hardware input: open / close the capture device
    }
    if (armed) return;
    // A key still held from live input loses its note-off once the track stops taking live
    // notes (#366): the audio thread releases the live notes this track is sounding. The key
    // itself stays held, for other armed tracks and for a take started while it's down.
    if (wasArmed) {
        std::lock_guard<std::mutex> lk(remoteMidiMx_);
        rt_.remoteMidi_.push(MidiEvent{false, -1, 0.0f, trackId, kLiveReleaseExpr});
    }
    // Disarming a track that's currently recording finalizes its take (the audio clip is
    // materialised / MIDI capture stops), so toggling its REC button stops the recording.
    const bool inAudioTake = rt_.audioRecording_ && captureLaneOf(trackId) != nullptr;
    // An Arrangement MIDI take lane (a Session take records into its slot, ended by its stop).
    const int32_t midiLane =
        rt_.recording_.load(std::memory_order_relaxed) && rt_.recordSessionScene_ < 0 ? midiLaneOf(trackId) : -1;
    const bool endsTake = inAudioTake || midiLane >= 0;
    if (inAudioTake) {
        // The last lane ends the audio take; any other lane, the first included, leaves with
        // what it recorded so far and the rest keep going (specs/086): finishCaptureLane hands
        // the first lane's role (the Session slot, the retry) to the next.
        if (rt_.captureLanes_.size() == 1) stopAudioRecording();
        else
            finishCaptureLane(trackId);
    }
    if (midiLane >= 0 && openMidiLaneCount() > 1) {
        // Other instrument tracks record on (specs/086): this lane folds what it has, ends its
        // controllers still in flight here (as flushCtlTake does for the last lane), and closes.
        foldRecordedTake();
        rt_.ctlStopAt_ = rt_.transport_.uiPositionBeats();
        foldMidiLane(rt_.midiLanes_[static_cast<size_t>(midiLane)], midiLane, {}, {}, true, 0, false);
        closeMidiLane(midiLane);
    } else if (midiLane >= 0) {
        flushCtlTake(); // the take's controllers still in flight (specs/020)
        closeMidiLane(midiLane);
        recordTrackId_ = -1;
        recordTrackRt_.store(-1, std::memory_order_relaxed);
        recordClipIndex_ = -1;
        rt_.recording_.store(false, std::memory_order_relaxed);
        // The last armed track left: the take ends here, and so does the performance (#971).
        if (rt_.sessionPass_.active && !isRecording())
            rt_.session().finishSessionPass(rt_.transport_.uiPositionBeats());
    }
    // Its take is kept (specs/078): with nothing else recording the attempt ends; otherwise a
    // retry goes on covering the capture still running, without this track.
    // A Session lane that leaves while other slots record is kept the same way (specs/086).
    if (endsTake && rt_.takeAttempt_.active && (!rt_.takeAttempt_.session || (inAudioTake && rt_.audioRecording_))) {
        if (!isRecording()) closeTakeAttempt();
        else
            keepTakeTrack(trackId);
    }
}
void RecordingOps::setTrackRecordInput(int32_t trackId, int32_t source) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    t->setRecordInputSource(source);
    if (t->monitor() || t->armed()) {
        rt_.devices().recomputeRouting();
        updateMonitorInput();
    } // monitored / metered source moved
}
int32_t RecordingOps::trackRecordInput(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->recordInputSource() : 0;
}
void RecordingOps::setTrackInputChannels(int32_t trackId, int32_t channels) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return;
    t->setInputChannels(channels);
    updateCaptureLayout();
}
int32_t RecordingOps::trackInputChannels(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->inputChannels() : Track::kInputStereo;
}
bool RecordingOps::setTrackInput(int32_t trackId, int32_t first, int32_t width) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio || !t->setInput(first, width)) return false;
    updateCaptureLayout(); // an armed / monitoring track's columns moved
    return true;
}
int32_t RecordingOps::trackInput(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return -1;
    return t->inputFirst() | (t->inputWidth() << 8);
}
bool RecordingOps::trackInputAvailable(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    return t->inputFirst() + t->inputWidth() <= inputChannelCount();
}

// --- capture device channels (specs/086) ---------------------------------------------
// While the device is open its own answer; otherwise what it said when it was last open
// (or probed), until the input device changes.
int32_t RecordingOps::inputChannelCount() const {
    if (inputTestChannels_ > 0) return inputTestChannels_;
    if (rt_.input_ && rt_.input_->isRunning()) return rt_.input_->channelCount();
    return static_cast<int32_t>(inputChannelNames_.size());
}
std::string RecordingOps::inputChannelName(int32_t index) const {
    if (inputTestChannels_ > 0)
        return index >= 0 && index < inputTestChannels_ ? "Test " + std::to_string(index + 1) : std::string{};
    if (rt_.input_ && rt_.input_->isRunning()) return rt_.input_->channelName(index);
    return index >= 0 && index < static_cast<int32_t>(inputChannelNames_.size())
               ? inputChannelNames_[static_cast<size_t>(index)]
               : std::string{};
}
void RecordingOps::rememberInputChannels() {
    inputChannelNames_.clear();
    if (!rt_.input_ || !rt_.input_->isRunning()) return;
    for (int32_t c = 0, n = rt_.input_->channelCount(); c < n; ++c)
        inputChannelNames_.push_back(rt_.input_->channelName(c));
}
int32_t RecordingOps::probeAudioInput() {
    if (inputTestChannels_ > 0) return inputTestChannels_;
    if (rt_.input_ && rt_.input_->isRunning()) return rt_.input_->channelCount();
    if (inputProbed_) return static_cast<int32_t>(inputChannelNames_.size()); // once per device
    inputProbed_ = true;
    if (!ensureAudioInput()) return 0;
    const int32_t n = inputChannelCount();
    releaseAudioInputIfIdle(); // nothing records or monitors: close it again
    return n;
}
void RecordingOps::setDisabledInputs(const int32_t* channels, int32_t count) {
    inputDisabled_.fill(false);
    for (int32_t i = 0; channels && i < count; ++i)
        if (channels[i] >= 0 && channels[i] < kMaxInputChannels)
            inputDisabled_[static_cast<size_t>(channels[i])] = true;
}
void RecordingOps::forgetInputChannels() {
    inputChannelNames_.clear();
    inputProbed_ = false;
}
void RecordingOps::setTestInputDevice(int32_t channels) {
    inputTestChannels_ = std::clamp(channels, 0, kMaxInputChannels);
    inputTestMode_ = inputTestChannels_ > 0 || inputTestMode_;
}

// The device channels the engine carries: every channel picked by an audio track that is armed
// or monitors the hardware input, plus the hardware take in progress. Publishes only on change.
bool RecordingOps::syncCaptureLayout() {
    bool wantMonitorOrArmed = false;
    if (rt_.authoring_)
        for (const auto& t : rt_.authoring_->tracks)
            if (t->type() == TrackType::Audio && (t->monitor() || t->armed()) && t->recordInputSource() == 0) {
                wantMonitorOrArmed = true;
                break;
            }
    const bool hasLanes = rt_.audioRecording_ && rt_.internalRecordSource_.load(std::memory_order_relaxed) == 0 &&
                          !rt_.captureLanes_.empty();
    if (!wantMonitorOrArmed && !hasLanes && testLayoutChannels_ <= 0 && layoutMsg_.count == 0)
        return wantMonitorOrArmed;

    std::array<bool, kMaxInputChannels> want{};
    auto addPick = [&](int32_t first, int32_t width) {
        for (int32_t c = first; c < first + width; ++c)
            if (c >= 0 && c < kMaxInputChannels) want[static_cast<size_t>(c)] = true;
    };
    if (wantMonitorOrArmed)
        for (const auto& t : rt_.authoring_->tracks)
            if (t->type() == TrackType::Audio && (t->monitor() || t->armed()) && t->recordInputSource() == 0)
                addPick(t->inputFirst(), t->inputWidth());
    if (hasLanes)
        for (const auto& l : rt_.captureLanes_) addPick(l.first, l.width); // every lane of the take
    addPick(0, testLayoutChannels_);                                       // tests feeding a pretend device
    CaptureLayoutSnapshot next;
    for (int32_t c = 0; c < kMaxInputChannels; ++c)
        if (want[static_cast<size_t>(c)]) {
            next.columnOf[static_cast<size_t>(c)] = static_cast<int8_t>(next.count);
            next.channel[static_cast<size_t>(next.count++)] = static_cast<int8_t>(c);
        }
    if (next.count == layoutMsg_.count && next.columnOf == layoutMsg_.columnOf) return wantMonitorOrArmed;
    next.generation = layoutMsg_.generation + 1;
    layoutMsg_ = next;
    layoutHistory_[next.generation % kLayoutHistory] = next;
    captureLayout_.publish(next);
    return wantMonitorOrArmed;
}
void RecordingOps::updateCaptureLayout() {
    syncCaptureLayout();
}
const CaptureLayoutSnapshot* RecordingOps::layoutFor(uint32_t generation) const {
    const auto& h = layoutHistory_[generation % kLayoutHistory];
    return h.generation == generation ? &h : nullptr;
}

// --- live input monitoring ---------------------------------------------------
void RecordingOps::setTrackMonitor(int32_t trackId, bool on) {
    if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return;
    t->setMonitor(on);
    rt_.devices().recomputeRouting(); // claim / release the source track's route tap
    updateMonitorInput();             // open / close the capture device for a hardware source
}
bool RecordingOps::trackMonitor(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->monitor();
}

// Open the capture device once; recording and monitoring share it, each gated by its own
// tap flag, so starting a take while monitoring doesn't reopen (or drop) the stream.
bool RecordingOps::ensureAudioInput() {
    if (inputTestMode_) return true;
    const bool ok = openAudioInput();
    // The monitor / input-meter tap waits for an open device (#987): when monitoring or an
    // armed track wanted the input but it didn't open then (a macOS permission prompt still
    // pending, the interface not ready), updateMonitorInput doesn't retry — so the next open
    // that succeeds (a take starting) switches the tap on, and the meter and monitoring work.
    if (ok && rt_.hwMonitorWanted_) rt_.hwMonitorTap_.store(true, std::memory_order_relaxed);
    return ok;
}

bool RecordingOps::openAudioInput() {
    if (rt_.input_ && rt_.input_->isRunning()) return true;
    if (!rt_.input_) rt_.input_ = rt_.io().backend_ ? rt_.io().backend_->createCoupledInputStream() : nullptr;
    if (!rt_.input_) rt_.input_ = io::audio::createAudioInputStream(rt_.io().activeSystem_); // standalone input stream
    // In the ASIO → WASAPI fallback the output runs on the system default; so does capture.
    // The saved WASAPI input stays as it is for when WASAPI is chosen.
    const bool asioFallback = io::audio::asioSupported() && rt_.io().config_.audioSystem == AudioSystem::Asio &&
                              rt_.io().activeSystem_ != AudioSystem::Asio;
    const uint32_t inputDev = io::audio::resolveAudioDeviceId(
        asioFallback ? std::string{} : rt_.io().config_.inputDeviceUid, /*inputScope=*/true);
    // The input thread only touches the lock-free rings (RT-safe).
    // At the engine's rate where the device can run it (#563); live monitoring converts the rest,
    // with a window and kernel made ready here, off the audio thread.
    prepareMonitorResampler();
    const double engineRate = rt_.transport_.sampleRate();
    const bool ok = rt_.input_->start(
        [this](const float* s, int32_t n, int32_t channels) { onInputBlock(s, n, channels); }, inputDev, engineRate);
    if (ok) {
        rememberInputChannels();
        monitorInRate_.store(rt_.input_->sampleRate(), std::memory_order_relaxed);
    }
    return ok;
}

// Input thread: copy the layout's columns of this device block into the take and monitor
// rings. No allocation, no lock (AR-6): the layout is read by seqlock, the rings are SPSC.
void RecordingOps::onInputBlock(const float* s, int32_t n, int32_t channels) {
    if (n > inputBlockFrames_.load(std::memory_order_relaxed)) inputBlockFrames_.store(n, std::memory_order_relaxed);
    captureLayout_.read(layoutInput_); // a write in progress: keep the copy we had
    const CaptureLayoutSnapshot& L = layoutInput_;
    if (L.count <= 0) return;
    // The gate is shut while a count-in clicks: the take starts when the transport rolls.
    const uint32_t capEp = rt_.captureEpoch_.load(std::memory_order_acquire); // before the gate (specs/078)
    if (rt_.hwRecordTap_.load(std::memory_order_relaxed) && rt_.captureGate_.load(std::memory_order_acquire)) {
        rt_.markCaptureEpoch(hwCaptureEpoch_, capEp, /*hardware=*/true);
        if (!rt_.recordRing_.pushGather(L.generation, s, channels, n, L.channel.data(), L.count))
            rt_.inputDroppedFrames_.fetch_add(n, std::memory_order_relaxed);
    }
    if (rt_.hwMonitorTap_.load(std::memory_order_relaxed))
        monitorRing_.pushGather(
            L.generation, s, channels, n, L.channel.data(), L.count); // full: the audio thread trims
}

void RecordingOps::releaseAudioInputIfIdle() {
    if (rt_.hwRecordTap_.load(std::memory_order_relaxed) || rt_.hwMonitorTap_.load(std::memory_order_relaxed)) return;
    if (rt_.input_) rt_.input_->stop();
}

// Keep the capture device open exactly while some audio track monitors the hardware
// input, or is armed on it and meters it (#352). Only acts on a change, so a failed open
// isn't retried on every edit.
void RecordingOps::updateMonitorInput() {
    const bool want = syncCaptureLayout(); // the channels armed / monitoring tracks pick
    if (want == rt_.hwMonitorWanted_) return;
    rt_.hwMonitorWanted_ = want;
    if (want) {
        if (ensureAudioInput()) rt_.hwMonitorTap_.store(true, std::memory_order_relaxed);
    } else {
        rt_.hwMonitorTap_.store(false, std::memory_order_relaxed);
        releaseAudioInputIfIdle();
    }
}

// Audio thread: pop this segment's hardware input into monitorHwBuf_ (layoutAudio_ columns).
// The capture and output devices run on separate clocks, so keep a cushion of about one
// capture block queued (re-primed after an underrun) and trim any excess so latency can't
// build up. A block written under another layout ends the segment early; the next segment
// adopts that layout (a short gap, as a device reopen gives). A capture device at another
// rate than the engine's goes through the resampler instead (#563).
void RecordingOps::pullMonitorInput(int32_t frames) {
    const int32_t width0 = std::max(rt_.layoutAudio_.count, 0);
    std::fill_n(rt_.monitorHwBuf_.data(), static_cast<size_t>(frames) * static_cast<size_t>(width0), 0.0f);
    if (!rt_.hwMonitorTap_.load(std::memory_order_relaxed)) {
        monitorPrimed_ = false;
        monitorConverting_ = false;
        return;
    }
    // ASIO duplex pushes a whole driver block before rendering it (one clock), so nothing has
    // to be primed (R8). But render may split that block into segments (a loop boundary): the
    // rest of the current block is not a backlog — only input older than one block is.
    const bool sharedClock = rt_.monitorSharedClock_.load(std::memory_order_relaxed);
    const double inRate = monitorInRate_.load(std::memory_order_relaxed);
    const double outRate = rt_.transport_.sampleRate();
    const bool convert = !sharedClock && inRate > 0.0 && outRate > 0.0 && std::fabs(inRate - outRate) > 0.5 &&
                         monitorSinc_.load(std::memory_order_acquire) != nullptr;
    if (convert != monitorConverting_) { // a device at another rate opened (or closed): start over
        monitorConverting_ = convert;
        monitorPrimed_ = false;
        monitorSrcRatio_ = 0.0; // re-sized on the next converted segment
    }
    if (convert) {
        resampleMonitorInput(frames, inRate / outRate);
        return;
    }
    const size_t block = static_cast<size_t>(std::clamp(inputBlockFrames_.load(std::memory_order_relaxed), 64, 4096));
    const size_t n = static_cast<size_t>(frames);
    const size_t cushion = sharedClock ? 0 : block;
    const size_t keep = sharedClock ? std::max(block, n) : n + cushion;
    size_t avail = monitorRing_.framesQueued();
    if (avail > keep + cushion + 256) { // drifted / stalled: drop the stale backlog
        skipMonitorFrames(avail - keep);
        avail = keep;
    }
    if (!monitorPrimed_) {
        if (avail < n + cushion) return; // still filling the cushion: silence
        monitorPrimed_ = true;
    }
    bool adopted = false, layoutBreak = false;
    const size_t got = readMonitorRing(rt_.monitorHwBuf_.data(), n, adopted, layoutBreak);
    if (adopted) { // the newer layout's columns: the rest of the segment is silent in them
        const size_t w = static_cast<size_t>(std::max(rt_.layoutAudio_.count, 0));
        std::fill(rt_.monitorHwBuf_.data() + got * w, rt_.monitorHwBuf_.data() + n * w, 0.0f);
    }
    if (layoutBreak) return;             // this segment's columns follow the old layout: finish it silent
    if (got < n) monitorPrimed_ = false; // underrun: rebuild the cushion
}

// Audio thread: read up to `want` frames of the monitor ring, in layoutAudio_'s columns, into
// `dst`. A block written under a newer layout is adopted when nothing was read yet (`adopted`);
// after some frames it ends the read (`layoutBreak`), the caller's frames keep the old columns.
size_t RecordingOps::readMonitorRing(float* dst, size_t want, bool& adopted, bool& layoutBreak) {
    size_t i = 0;
    while (i < want) {
        if (monitorRing_.remaining() == 0) {
            CaptureBlock b;
            if (!monitorRing_.next(b)) break;
            if (b.flags & CaptureBlock::kMarker) continue;
            monitorBlockGen_ = b.generation;
        }
        if (monitorBlockGen_ != rt_.layoutAudio_.generation) {
            if (i > 0) {
                layoutBreak = true;
                return i;
            }
            CaptureLayoutSnapshot L;
            if (captureLayout_.read(L) && L.generation == monitorBlockGen_) {
                rt_.layoutAudio_ = L;
                adopted = true;
            }
        }
        if (monitorBlockGen_ != rt_.layoutAudio_.generation || monitorRing_.width() != rt_.layoutAudio_.count) {
            monitorRing_.skip(monitorRing_.remaining()); // a layout already replaced: drop it
            continue;
        }
        const size_t w = static_cast<size_t>(rt_.layoutAudio_.count);
        i += static_cast<size_t>(monitorRing_.read(dst + i * w, static_cast<int32_t>(want - i)));
    }
    return i;
}

// The monitor resampler's kernel (#563): sinc(x) under a Kaiser window (beta 7, ~70 dB),
// x in [0, kSincZeros], kSincTable points per zero crossing. Built on first use, once per
// process (thread-safe static init); control thread only.
const float* RecordingOps::monitorSincTable() {
    static const std::vector<float> table = [] {
        constexpr double kBeta = 7.0, kPi = 3.14159265358979323846;
        auto i0 = [](double x) { // modified Bessel function of the first kind, order 0
            double sum = 1.0, term = 1.0;
            for (int k = 1; k < 64 && term > 1e-12 * sum; ++k) {
                term *= (x / (2.0 * k)) * (x / (2.0 * k));
                sum += term;
            }
            return sum;
        };
        const int32_t n = kSincZeros * kSincTable;
        std::vector<float> t(static_cast<size_t>(n) + 2, 0.0f);
        for (int32_t k = 0; k <= n; ++k) {
            const double x = static_cast<double>(k) / kSincTable;
            const double r = x / kSincZeros;
            const double sinc = k == 0 ? 1.0 : std::sin(kPi * x) / (kPi * x);
            t[static_cast<size_t>(k)] =
                static_cast<float>(sinc * i0(kBeta * std::sqrt(std::max(0.0, 1.0 - r * r))) / i0(kBeta));
        }
        return t;
    }();
    return table.data();
}

// Control thread: the resampler's window (this engine's) and kernel (the process's), the first
// time an input opens. Never resized after, so the audio thread can't see it move; it converts
// only once monitorSinc_ is set, which is published last.
void RecordingOps::prepareMonitorResampler() {
    if (monitorSinc_.load(std::memory_order_acquire)) return;
    monitorSrc_.assign(static_cast<size_t>(kMonitorSrcFrames) * kMaxInputChannels, 0.0f);
    monitorSinc_.store(monitorSincTable(), std::memory_order_release);
}

void RecordingOps::resetMonitorResampler(double ratio) {
    // Cut at the lower Nyquist: an input faster than the engine is low-passed at the engine's
    // Nyquist before it is decimated (#563 review: nothing above it may fold back as a false
    // tone); a slower one keeps its whole band and loses the images above it. Wider by `ratio`
    // when decimating, as many taps each side as that takes to reach kSincZeros crossings.
    monitorSrcRatio_ = ratio;
    monitorSrcCut_ =
        std::clamp(ratio > 1.0 ? 1.0 / ratio : 1.0, static_cast<double>(kSincZeros) / kMonitorSrcHalfMax, 1.0);
    monitorSrcHalf_ = std::min(kMonitorSrcHalfMax, static_cast<int32_t>(std::ceil(kSincZeros / monitorSrcCut_)));
    monitorSrcFrames_ = monitorSrcHalf_; // a half-length of silence before the first input frame
    monitorSrcPos_ = static_cast<double>(monitorSrcHalf_);
    monitorFillAvg_ = -1.0; // unset: the first primed segment starts it
    monitorSrcGen_ = rt_.layoutAudio_.generation;
    const size_t w = static_cast<size_t>(std::max(rt_.layoutAudio_.count, 0));
    std::fill_n(monitorSrc_.data(), static_cast<size_t>(monitorSrcHalf_) * w, 0.0f);
}

// Audio thread (#563): `frames` output frames of a capture device running `ratio` times the
// engine's rate. A windowed-sinc read of the input at a step of `ratio`, nudged by the average
// queued input so the two clocks can't drift apart: the queue settles on one capture block of
// cushion instead of being trimmed or re-primed (a click each time).
void RecordingOps::resampleMonitorInput(int32_t frames, double ratio) {
    const size_t block = static_cast<size_t>(std::clamp(inputBlockFrames_.load(std::memory_order_relaxed), 64, 4096));
    float* out = rt_.monitorHwBuf_.data();
    // The next block's layout, adopted before the window is sized in its columns (the direct
    // path adopts it inside the read; here the held frames must all share one layout).
    if (monitorRing_.remaining() == 0) {
        CaptureBlock b;
        while (monitorRing_.next(b))
            if (!(b.flags & CaptureBlock::kMarker)) {
                monitorBlockGen_ = b.generation;
                break;
            }
    }
    if (monitorRing_.remaining() > 0 && monitorBlockGen_ != rt_.layoutAudio_.generation) {
        CaptureLayoutSnapshot L;
        if (captureLayout_.read(L) && L.generation == monitorBlockGen_) {
            rt_.layoutAudio_ = L;
            // The caller cleared the old layout's columns; a wider one reaches past them, into
            // what an earlier segment left there (#563 review). Silence in the new columns.
            std::fill_n(out, static_cast<size_t>(frames) * static_cast<size_t>(std::max(L.count, 0)), 0.0f);
        }
    }
    if (monitorSrcGen_ != rt_.layoutAudio_.generation || monitorSrcRatio_ != ratio) {
        monitorPrimed_ = false;
        resetMonitorResampler(ratio);
    }
    const int32_t half = monitorSrcHalf_;
    const double held = static_cast<double>(monitorSrcFrames_) - monitorSrcPos_; // read, not yet played
    const double need = std::ceil(frames * ratio) + half + 1.0; // this segment, plus the kernel's reach
    // A cushion of one capture block over what a segment reads, as on the direct path. The
    // queue sampled here swings by up to a block as capture blocks land, so it is only called
    // a stall well past that.
    const double blk = static_cast<double>(block);
    const double target = need + blk;
    double queued = static_cast<double>(monitorRing_.framesQueued()) + held;
    if (queued > need + 4.0 * blk + 256.0) { // stalled: drop the stale backlog
        const double drop = std::min(queued - target, static_cast<double>(monitorRing_.framesQueued()));
        skipMonitorFrames(static_cast<size_t>(drop));
        queued -= drop;
        monitorFillAvg_ = queued;
    }
    if (!monitorPrimed_) {
        if (queued < target) return; // still filling the cushion: silence
        monitorPrimed_ = true;
        monitorFillAvg_ = queued;
    }
    // Drift control: the average queue (over ~0.5 s) is steered back when it leaves a band one
    // capture block wide around the cushion's middle. The sampled queue swings
    // on its own — slowly, when the two block rates are close — and a controller chasing that
    // swing would bend the pitch for nothing. Past the band it corrects within seconds, by at
    // most 0.2 % of speed (real clocks drift by ~0.01 %).
    const double outRate = std::max(1.0, rt_.transport_.sampleRate());
    const double a = std::min(1.0, frames / (0.5 * outRate));
    monitorFillAvg_ += (queued - monitorFillAvg_) * a;
    const double err = monitorFillAvg_ - (need + 1.5 * blk);
    const double band = 0.5 * blk;
    const double over = err > band ? err - band : err < -band ? err + band : 0.0;
    const double adj = std::clamp(over / (2.0 * ratio * outRate), -0.002, 0.002);
    const double step = ratio * (1.0 + adj);

    const size_t w = static_cast<size_t>(std::max(rt_.layoutAudio_.count, 0));
    if (w == 0) return;
    float* src = monitorSrc_.data();
    const float* table = monitorSinc_.load(std::memory_order_relaxed);
    const double cut = monitorSrcCut_;
    const double tableScale = cut * kSincTable; // table points per input frame of distance
    const double tableEnd = static_cast<double>(kSincZeros) * kSincTable;
    float acc[kMaxInputChannels];
    int32_t i = 0;
    bool dry = false; // the ring held less than the window wanted
    while (i < frames) {
        // Fill the window as far as the rest of the segment reads (or as it holds).
        const double lastPos = monitorSrcPos_ + (frames - 1 - i) * step;
        const int32_t want = std::min(kMonitorSrcFrames, static_cast<int32_t>(lastPos) + half + 1);
        if (want > monitorSrcFrames_) {
            bool adopted = false, layoutBreak = false;
            const size_t got = readMonitorRing(src + static_cast<size_t>(monitorSrcFrames_) * w,
                static_cast<size_t>(want - monitorSrcFrames_), adopted, layoutBreak);
            if (got < static_cast<size_t>(want - monitorSrcFrames_)) dry = true;
            if (adopted) { // frames of another layout: start over in it next segment
                monitorPrimed_ = false;
                resetMonitorResampler(ratio);
                std::fill_n(
                    out, static_cast<size_t>(frames) * static_cast<size_t>(std::max(rt_.layoutAudio_.count, 0)), 0.0f);
                return;
            }
            monitorSrcFrames_ += static_cast<int32_t>(got);
        }
        // Play every output frame whose taps are all held: input frames k0-half+1 .. k0+half.
        for (; i < frames; ++i) {
            const int32_t k0 = static_cast<int32_t>(monitorSrcPos_);
            if (k0 + half >= monitorSrcFrames_) break;
            const double frac = monitorSrcPos_ - k0;
            std::fill_n(acc, w, 0.0f);
            float norm = 0.0f;
            for (int32_t k = k0 - half + 1; k <= k0 + half; ++k) {
                const double t = std::fabs(static_cast<double>(k - k0) - frac) * tableScale;
                if (t >= tableEnd) continue;
                const int32_t ti = static_cast<int32_t>(t);
                const float tf = static_cast<float>(t - ti);
                const float h = table[ti] + (table[ti + 1] - table[ti]) * tf;
                norm += h;
                const float* x = src + static_cast<size_t>(k) * w;
                for (size_t c = 0; c < w; ++c) acc[c] += h * x[c];
            }
            const float g = norm != 0.0f ? 1.0f / norm : 0.0f; // unity gain at DC, whatever the phase
            float* o = out + static_cast<size_t>(i) * w;
            for (size_t c = 0; c < w; ++c) o[c] = acc[c] * g;
            monitorSrcPos_ += step;
        }
        // Keep the kernel's history before the read position; slide the rest down.
        const int32_t drop = std::min(static_cast<int32_t>(monitorSrcPos_) - half + 1, monitorSrcFrames_ - half);
        if (drop > 0) {
            std::memmove(src, src + static_cast<size_t>(drop) * w,
                static_cast<size_t>(monitorSrcFrames_ - drop) * w * sizeof(float));
            monitorSrcFrames_ -= drop;
            monitorSrcPos_ -= drop;
        }
        if (i < frames && dry) { // the ring ran dry: rebuild the cushion
            monitorPrimed_ = false;
            return;
        }
    }
}

void RecordingOps::skipMonitorFrames(size_t frames) {
    while (frames > 0) {
        if (monitorRing_.remaining() == 0) {
            CaptureBlock b;
            if (!monitorRing_.next(b)) return;
            if (!(b.flags & CaptureBlock::kMarker)) monitorBlockGen_ = b.generation;
            continue;
        }
        const int32_t k = static_cast<int32_t>(std::min<size_t>(frames, static_cast<size_t>(monitorRing_.remaining())));
        monitorRing_.skip(k);
        frames -= static_cast<size_t>(k);
    }
}

void RecordingOps::setMonitorInputForTest(double sampleRate, int32_t blockFrames) {
    inputTestMode_ = true; // the pushed frames are the input: never open (or feed from) a real device
    prepareMonitorResampler();
    monitorInRate_.store(sampleRate > 0.0 ? sampleRate : 0.0, std::memory_order_relaxed);
    if (blockFrames > 0) inputBlockFrames_.store(blockFrames, std::memory_order_relaxed);
}

void RecordingOps::pushMonitorFramesForTest(const float* interleavedStereo, int32_t frames) {
    inputTestMode_ = true;
    testLayoutChannels_ = std::max(testLayoutChannels_, 2);
    updateCaptureLayout();
    rt_.hwMonitorTap_.store(true, std::memory_order_relaxed);
    monitorRing_.pushGather(
        layoutMsg_.generation, interleavedStereo, 2, frames, layoutMsg_.channel.data(), layoutMsg_.count);
}
bool RecordingOps::monitorSharedClockSelfTest() {
    constexpr int32_t kBlock = 512, kFirst = 10;
    const bool sharedWas = rt_.monitorSharedClock_.load();
    const int32_t blockWas = inputBlockFrames_.load();
    const bool tapWas = rt_.hwMonitorTap_.load();
    monitorRing_.clear();
    testLayoutChannels_ = std::max(testLayoutChannels_, 2);
    updateCaptureLayout();
    rt_.monitorSharedClock_.store(true);
    inputBlockFrames_.store(kBlock);
    rt_.hwMonitorTap_.store(true);
    monitorPrimed_ = false;
    std::vector<float> sig(static_cast<size_t>(kBlock) * 2);
    for (int32_t i = 0; i < kBlock; ++i) {
        sig[static_cast<size_t>(i) * 2] = static_cast<float>(i);
        sig[static_cast<size_t>(i) * 2 + 1] = -static_cast<float>(i);
    }
    monitorRing_.pushGather(layoutMsg_.generation, sig.data(), 2, kBlock, layoutMsg_.channel.data(), layoutMsg_.count);
    bool ok = true;
    pullMonitorInput(kFirst);
    const size_t w = static_cast<size_t>(rt_.layoutAudio_.count);
    const int32_t cL = rt_.layoutAudio_.column(0), cR = rt_.layoutAudio_.column(1);
    ok = ok && cL >= 0 && cR >= 0;
    for (int32_t i = 0; ok && i < kFirst; ++i)
        ok = rt_.monitorHwBuf_[static_cast<size_t>(i) * w + static_cast<size_t>(cL)] == static_cast<float>(i);
    pullMonitorInput(kBlock - kFirst);
    for (int32_t i = 0; ok && i < kBlock - kFirst; ++i)
        ok =
            rt_.monitorHwBuf_[static_cast<size_t>(i) * w + static_cast<size_t>(cL)] == static_cast<float>(kFirst + i) &&
            rt_.monitorHwBuf_[static_cast<size_t>(i) * w + static_cast<size_t>(cR)] == -static_cast<float>(kFirst + i);
    monitorRing_.clear();
    rt_.monitorSharedClock_.store(sharedWas);
    inputBlockFrames_.store(blockWas);
    rt_.hwMonitorTap_.store(tapWas);
    monitorPrimed_ = false;
    return ok;
}

} // namespace nota::detail
