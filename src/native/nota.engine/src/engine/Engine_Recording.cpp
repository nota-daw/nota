// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — live MIDI input, track arming, and audio/MIDI recording (M2/M4-3): note on/off, record start/stop, input-queue drain, capture self-tests.

#include "Engine.h"
#include "audio_io/AudioBackendFactory.h"

#include <algorithm>
#include <bit>
#include <cmath>

namespace nota {

// --- live MIDI, arming, recording ------------------------------------------
void Engine::setTrackArmed(int32_t trackId, bool armed) {
    auto t = findTrackAuthoring(trackId);
    const bool wasArmed = t && t->armed();
    if (t) t->setArmed(armed);
    if (t && t->type() == TrackType::Audio && armed != wasArmed) {
        recomputeRouting();   // an armed track meters its source track's tap (#352)
        updateMonitorInput(); // ... or the hardware input: open / close the capture device
    }
    if (armed) return;
    // A key still held from live input loses its note-off once the track stops taking live
    // notes (#366): the audio thread releases the live notes this track is sounding. The key
    // itself stays held, for other armed tracks and for a take started while it's down.
    if (wasArmed) {
        std::lock_guard<std::mutex> lk(remoteMidiMx_);
        remoteMidi_.push(MidiEvent{false, -1, 0.0f, trackId, kLiveReleaseExpr});
    }
    // Disarming a track that's currently recording finalizes its take (the audio clip is
    // materialised / MIDI capture stops), so toggling its REC button stops the recording.
    const bool inAudioTake = audioRecording_ && captureLaneOf(trackId) != nullptr;
    // An Arrangement MIDI take lane (a Session take records into its slot, ended by its stop).
    const int32_t midiLane =
        recording_.load(std::memory_order_relaxed) && recordSessionScene_ < 0 ? midiLaneOf(trackId) : -1;
    const bool endsTake = inAudioTake || midiLane >= 0;
    if (inAudioTake) {
        // The last lane ends the audio take; any other lane, the first included, leaves with
        // what it recorded so far and the rest keep going (specs/086): finishCaptureLane hands
        // the first lane's role (the Session slot, the retry) to the next.
        if (captureLanes_.size() == 1) stopAudioRecording();
        else finishCaptureLane(trackId);
    }
    if (midiLane >= 0 && openMidiLaneCount() > 1) {
        // Other instrument tracks record on (specs/086): this lane folds what it has, ends its
        // controllers still in flight here (as flushCtlTake does for the last lane), and closes.
        foldRecordedTake();
        ctlStopAt_ = transport_.uiPositionBeats();
        foldMidiLane(midiLanes_[static_cast<size_t>(midiLane)], midiLane, {}, {}, true, 0, false);
        closeMidiLane(midiLane);
    } else if (midiLane >= 0) {
        flushCtlTake(); // the take's controllers still in flight (specs/020)
        closeMidiLane(midiLane);
        recordTrackId_ = -1;
        recordTrackRt_.store(-1, std::memory_order_relaxed);
        recordClipIndex_ = -1;
        recording_.store(false, std::memory_order_relaxed);
    }
    // Its take is kept (specs/078): with nothing else recording the attempt ends; otherwise a
    // retry goes on covering the capture still running, without this track.
    // A Session lane that leaves while other slots record is kept the same way (specs/086).
    if (endsTake && takeAttempt_.active && (!takeAttempt_.session || (inAudioTake && audioRecording_))) {
        if (!isRecording()) closeTakeAttempt();
        else keepTakeTrack(trackId);
    }
}
void Engine::setTrackRecordInput(int32_t trackId, int32_t source) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    t->setRecordInputSource(source);
    if (t->monitor() || t->armed()) {
        recomputeRouting();
        updateMonitorInput();
    } // monitored / metered source moved
}
int32_t Engine::trackRecordInput(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->recordInputSource() : 0;
}
void Engine::setTrackInputChannels(int32_t trackId, int32_t channels) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return;
    t->setInputChannels(channels);
    updateCaptureLayout();
}
int32_t Engine::trackInputChannels(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->inputChannels() : Track::kInputStereo;
}
bool Engine::setTrackInput(int32_t trackId, int32_t first, int32_t width) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio || !t->setInput(first, width)) return false;
    updateCaptureLayout(); // an armed / monitoring track's columns moved
    return true;
}
int32_t Engine::trackInput(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return -1;
    return t->inputFirst() | (t->inputWidth() << 8);
}
bool Engine::trackInputAvailable(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    return t->inputFirst() + t->inputWidth() <= inputChannelCount();
}

// --- capture device channels (specs/086) ---------------------------------------------
// While the device is open its own answer; otherwise what it said when it was last open
// (or probed), until the input device changes.
int32_t Engine::inputChannelCount() const {
    if (inputTestChannels_ > 0) return inputTestChannels_;
    if (input_ && input_->isRunning()) return input_->channelCount();
    return static_cast<int32_t>(inputChannelNames_.size());
}
std::string Engine::inputChannelName(int32_t index) const {
    if (inputTestChannels_ > 0)
        return index >= 0 && index < inputTestChannels_ ? "Test " + std::to_string(index + 1) : std::string{};
    if (input_ && input_->isRunning()) return input_->channelName(index);
    return index >= 0 && index < static_cast<int32_t>(inputChannelNames_.size()) ? inputChannelNames_[static_cast<size_t>(index)]
                                                                                  : std::string{};
}
void Engine::rememberInputChannels() {
    inputChannelNames_.clear();
    if (!input_ || !input_->isRunning()) return;
    for (int32_t c = 0, n = input_->channelCount(); c < n; ++c) inputChannelNames_.push_back(input_->channelName(c));
}
int32_t Engine::probeAudioInput() {
    if (inputTestChannels_ > 0) return inputTestChannels_;
    if (input_ && input_->isRunning()) return input_->channelCount();
    if (inputProbed_) return static_cast<int32_t>(inputChannelNames_.size()); // once per device
    inputProbed_ = true;
    if (!ensureAudioInput()) return 0;
    const int32_t n = inputChannelCount();
    releaseAudioInputIfIdle(); // nothing records or monitors: close it again
    return n;
}
void Engine::setDisabledInputs(const int32_t* channels, int32_t count) {
    inputDisabled_.fill(false);
    for (int32_t i = 0; channels && i < count; ++i)
        if (channels[i] >= 0 && channels[i] < kMaxInputChannels) inputDisabled_[static_cast<size_t>(channels[i])] = true;
}
void Engine::forgetInputChannels() {
    inputChannelNames_.clear();
    inputProbed_ = false;
}
void Engine::setTestInputDevice(int32_t channels) {
    inputTestChannels_ = std::clamp(channels, 0, kMaxInputChannels);
    inputTestMode_ = inputTestChannels_ > 0 || inputTestMode_;
}

// The device channels the engine carries: every channel picked by an audio track that is armed
// or monitors the hardware input, plus the hardware take in progress. Publishes only on change.
void Engine::updateCaptureLayout() {
    std::array<bool, kMaxInputChannels> want{};
    auto addPick = [&](int32_t first, int32_t width) {
        for (int32_t c = first; c < first + width; ++c)
            if (c >= 0 && c < kMaxInputChannels) want[static_cast<size_t>(c)] = true;
    };
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (t->type() == TrackType::Audio && (t->monitor() || t->armed()) && t->recordInputSource() == 0)
                addPick(t->inputFirst(), t->inputWidth());
    if (audioRecording_ && internalRecordSource_.load(std::memory_order_relaxed) == 0)
        for (const auto& l : captureLanes_) addPick(l.first, l.width); // every lane of the take
    addPick(0, testLayoutChannels_); // tests feeding a pretend device
    CaptureLayoutSnapshot next;
    for (int32_t c = 0; c < kMaxInputChannels; ++c)
        if (want[static_cast<size_t>(c)]) {
            next.columnOf[static_cast<size_t>(c)] = static_cast<int8_t>(next.count);
            next.channel[static_cast<size_t>(next.count++)] = static_cast<int8_t>(c);
        }
    if (next.count == layoutMsg_.count && next.columnOf == layoutMsg_.columnOf) return;
    next.generation = layoutMsg_.generation + 1;
    layoutMsg_ = next;
    layoutHistory_[next.generation % kLayoutHistory] = next;
    captureLayout_.publish(next);
}
const CaptureLayoutSnapshot* Engine::layoutFor(uint32_t generation) const {
    const auto& h = layoutHistory_[generation % kLayoutHistory];
    return h.generation == generation ? &h : nullptr;
}

// --- live input monitoring ---------------------------------------------------
void Engine::setTrackMonitor(int32_t trackId, bool on) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return;
    t->setMonitor(on);
    recomputeRouting();   // claim / release the source track's route tap
    updateMonitorInput(); // open / close the capture device for a hardware source
}
bool Engine::trackMonitor(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->monitor();
}

// Open the capture device once; recording and monitoring share it, each gated by its own
// tap flag, so starting a take while monitoring doesn't reopen (or drop) the stream.
bool Engine::ensureAudioInput() {
    if (inputTestMode_) return true;
    if (input_ && input_->isRunning()) return true;
    if (!input_) input_ = createAudioInput(activeSystem_); // ASIO: the running driver's inputs
    // In the ASIO → WASAPI fallback the output runs on the system default; so does capture.
    // The saved WASAPI input stays as it is for when WASAPI is chosen.
    const bool asioFallback = asioSupported() && config_.audioSystem == AudioSystem::Asio && activeSystem_ != AudioSystem::Asio;
    const uint32_t inputDev = resolveAudioDeviceId(asioFallback ? std::string{} : config_.inputDeviceUid, /*inputScope=*/true);
    // The input thread only touches the lock-free rings (RT-safe).
    const bool ok = input_->start([this](const float* s, int32_t n, int32_t channels) { onInputBlock(s, n, channels); },
        inputDev);
    if (ok) rememberInputChannels();
    return ok;
}

// Input thread: copy the layout's columns of this device block into the take and monitor
// rings. No allocation, no lock (AR-6): the layout is read by seqlock, the rings are SPSC.
void Engine::onInputBlock(const float* s, int32_t n, int32_t channels) {
    if (n > inputBlockFrames_.load(std::memory_order_relaxed)) inputBlockFrames_.store(n, std::memory_order_relaxed);
    captureLayout_.read(layoutInput_); // a write in progress: keep the copy we had
    const CaptureLayoutSnapshot& L = layoutInput_;
    if (L.count <= 0) return;
    // The gate is shut while a count-in clicks: the take starts when the transport rolls.
    const uint32_t capEp = captureEpoch_.load(std::memory_order_acquire); // before the gate (specs/078)
    if (hwRecordTap_.load(std::memory_order_relaxed) && captureGate_.load(std::memory_order_acquire)) {
        markCaptureEpoch(hwCaptureEpoch_, capEp, /*hardware=*/true);
        if (!recordRing_.pushGather(L.generation, s, channels, n, L.channel.data(), L.count))
            inputDroppedFrames_.fetch_add(n, std::memory_order_relaxed);
    }
    if (hwMonitorTap_.load(std::memory_order_relaxed))
        monitorRing_.pushGather(L.generation, s, channels, n, L.channel.data(), L.count); // full: the audio thread trims
}

void Engine::releaseAudioInputIfIdle() {
    if (hwRecordTap_.load(std::memory_order_relaxed) || hwMonitorTap_.load(std::memory_order_relaxed)) return;
    if (input_) input_->stop();
}

// Keep the capture device open exactly while some audio track monitors the hardware
// input, or is armed on it and meters it (#352). Only acts on a change, so a failed open
// isn't retried on every edit.
void Engine::updateMonitorInput() {
    updateCaptureLayout(); // the channels armed / monitoring tracks pick
    bool want = false;
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (t->type() == TrackType::Audio && (t->monitor() || t->armed()) && t->recordInputSource() == 0) {
                want = true;
                break;
            }
    if (want == hwMonitorWanted_) return;
    hwMonitorWanted_ = want;
    if (want) {
        if (ensureAudioInput()) hwMonitorTap_.store(true, std::memory_order_relaxed);
    } else {
        hwMonitorTap_.store(false, std::memory_order_relaxed);
        releaseAudioInputIfIdle();
    }
}

// Audio thread: pop this segment's hardware input into monitorHwBuf_ (layoutAudio_ columns).
// The capture and output devices run on separate clocks, so keep a cushion of about one
// capture block queued (re-primed after an underrun) and trim any excess so latency can't
// build up. A block written under another layout ends the segment early; the next segment
// adopts that layout (a short gap, as a device reopen gives).
void Engine::pullMonitorInput(int32_t frames) {
    const int32_t width0 = std::max(layoutAudio_.count, 0);
    std::fill_n(monitorHwBuf_.data(), static_cast<size_t>(frames) * static_cast<size_t>(width0), 0.0f);
    if (!hwMonitorTap_.load(std::memory_order_relaxed)) {
        monitorPrimed_ = false;
        return;
    }
    const size_t block = static_cast<size_t>(std::clamp(inputBlockFrames_.load(std::memory_order_relaxed), 64, 4096));
    const size_t n = static_cast<size_t>(frames);
    // ASIO duplex pushes a whole driver block before rendering it (one clock), so nothing has
    // to be primed (R8). But render may split that block into segments (a loop boundary): the
    // rest of the current block is not a backlog — only input older than one block is.
    const bool sharedClock = monitorSharedClock_.load(std::memory_order_relaxed);
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
    size_t i = 0;
    while (i < n) {
        if (monitorRing_.remaining() == 0) {
            CaptureBlock b;
            if (!monitorRing_.next(b)) break;
            if (b.flags & CaptureBlock::kMarker) continue;
            monitorBlockGen_ = b.generation;
        }
        if (monitorBlockGen_ != layoutAudio_.generation) {
            if (i > 0) return; // this segment's columns follow the old layout: finish it silent
            CaptureLayoutSnapshot L;
            if (captureLayout_.read(L) && L.generation == monitorBlockGen_) {
                layoutAudio_ = L;
                std::fill_n(monitorHwBuf_.data(), n * static_cast<size_t>(std::max(L.count, 0)), 0.0f);
            }
        }
        if (monitorBlockGen_ != layoutAudio_.generation || monitorRing_.width() != layoutAudio_.count) {
            monitorRing_.skip(monitorRing_.remaining()); // a layout already replaced: drop it
            continue;
        }
        const size_t w = static_cast<size_t>(layoutAudio_.count);
        i += static_cast<size_t>(monitorRing_.read(monitorHwBuf_.data() + i * w, static_cast<int32_t>(n - i)));
    }
    if (i < n) monitorPrimed_ = false; // underrun: rebuild the cushion
}

void Engine::skipMonitorFrames(size_t frames) {
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

void Engine::pushMonitorFramesForTest(const float* interleavedStereo, int32_t frames) {
    inputTestMode_ = true;
    testLayoutChannels_ = std::max(testLayoutChannels_, 2);
    updateCaptureLayout();
    hwMonitorTap_.store(true, std::memory_order_relaxed);
    monitorRing_.pushGather(layoutMsg_.generation, interleavedStereo, 2, frames, layoutMsg_.channel.data(), layoutMsg_.count);
}
bool Engine::monitorSharedClockSelfTest() {
    constexpr int32_t kBlock = 512, kFirst = 10;
    const bool sharedWas = monitorSharedClock_.load();
    const int32_t blockWas = inputBlockFrames_.load();
    const bool tapWas = hwMonitorTap_.load();
    monitorRing_.clear();
    testLayoutChannels_ = std::max(testLayoutChannels_, 2);
    updateCaptureLayout();
    monitorSharedClock_.store(true);
    inputBlockFrames_.store(kBlock);
    hwMonitorTap_.store(true);
    monitorPrimed_ = false;
    std::vector<float> sig(static_cast<size_t>(kBlock) * 2);
    for (int32_t i = 0; i < kBlock; ++i) {
        sig[static_cast<size_t>(i) * 2] = static_cast<float>(i);
        sig[static_cast<size_t>(i) * 2 + 1] = -static_cast<float>(i);
    }
    monitorRing_.pushGather(layoutMsg_.generation, sig.data(), 2, kBlock, layoutMsg_.channel.data(), layoutMsg_.count);
    bool ok = true;
    pullMonitorInput(kFirst);
    const size_t w = static_cast<size_t>(layoutAudio_.count);
    const int32_t cL = layoutAudio_.column(0), cR = layoutAudio_.column(1);
    ok = ok && cL >= 0 && cR >= 0;
    for (int32_t i = 0; ok && i < kFirst; ++i) ok = monitorHwBuf_[static_cast<size_t>(i) * w + static_cast<size_t>(cL)] == static_cast<float>(i);
    pullMonitorInput(kBlock - kFirst);
    for (int32_t i = 0; ok && i < kBlock - kFirst; ++i)
        ok = monitorHwBuf_[static_cast<size_t>(i) * w + static_cast<size_t>(cL)] == static_cast<float>(kFirst + i) &&
             monitorHwBuf_[static_cast<size_t>(i) * w + static_cast<size_t>(cR)] == -static_cast<float>(kFirst + i);
    monitorRing_.clear();
    monitorSharedClock_.store(sharedWas);
    inputBlockFrames_.store(blockWas);
    hwMonitorTap_.store(tapWas);
    monitorPrimed_ = false;
    return ok;
}

void Engine::setTrackMidiSource(int32_t trackId, int32_t sourceTrackId) {
    if (sourceTrackId == trackId) sourceTrackId = -1; // no self-routing
    if (auto t = findTrackAuthoring(trackId)) t->setMidiFromTrackId(sourceTrackId);
}
// One undo step (specs/085, T051), like every other edit of a track: a new track with the offset,
// published with the graph. The audio thread reads the new track's atomic; nothing is written to
// the track it may be rendering. An unchanged value is no step at all.
void Engine::setTrackMidiOffsetMs(int32_t trackId, double ms) {
    if (!std::isfinite(ms)) ms = 0.0;
    ms = std::clamp(ms, -2000.0, 2000.0);
    auto old = findTrackAuthoring(trackId);
    if (!old || old->midiOffsetMs() == ms) return;
    auto nt = cloneTrack(*old);
    nt->setMidiOffsetMs(ms);
    republishWithTrack(trackId, nt);
}
double Engine::trackMidiOffsetMs(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->midiOffsetMs() : 0.0;
}
int32_t Engine::trackMidiSource(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->midiFromTrackId() : -1;
}
// The MidiEvent of a live event from `source` (specs/086): hardware sources keep their index.
static MidiEvent liveEvent(bool on, int32_t pitch, float v, int32_t expr, int32_t source, int32_t channel) {
    MidiEvent e{on, pitch, v, -1, expr};
    e.source = static_cast<int8_t>(source >= 0 && source < 127 ? source : -1);
    e.channel = static_cast<int8_t>(std::clamp(channel, 0, 15));
    return e;
}

void Engine::noteOn(int32_t pitch, float velocity, int32_t source, int32_t channel) {
    {
        std::lock_guard<std::mutex> lk(liveMidiMx_);
        liveMidi_.push(liveEvent(true, pitch, velocity, -1, source, channel));
    }
    if (pitch >= 0 && pitch < 128) liveHeld_[pitch >> 5].fetch_or(1u << (pitch & 31), std::memory_order_relaxed);
}
void Engine::noteOff(int32_t pitch, float releaseVelocity, int32_t source, int32_t channel) {
    {
        std::lock_guard<std::mutex> lk(liveMidiMx_);
        liveMidi_.push(liveEvent(false, pitch, std::clamp(releaseVelocity, 0.0f, 1.0f), -1, source, channel));
    }
    if (pitch >= 0 && pitch < 128) liveHeld_[pitch >> 5].fetch_and(~(1u << (pitch & 31)), std::memory_order_relaxed);
}

// The instrument-wide dims (pitch −1) are controllers now: one path plays and records them.
static int32_t ctlOfGlobalDim(int32_t dim) {
    switch (dim) {
    case ExprBend: return kCtlPitchWheel;
    case ExprPressure: return kCtlChannelPressure;
    case ExprSlide: return 74;
    default: return kCtlSustain; // ExprSustain
    }
}

void Engine::noteExpression(int32_t pitch, int32_t dim, float value, int32_t source, int32_t channel) {
    if (pitch < -1 || pitch > 127 || dim < 0 || dim >= kExprWireDims || (dim >= kExprDims && pitch >= 0) ||
        !std::isfinite(value))
        return;
    if (pitch < 0) {
        controller(ctlOfGlobalDim(dim), value, source, channel);
        return;
    }
    std::lock_guard<std::mutex> lk(liveMidiMx_);
    liveMidi_.push(liveEvent(false, pitch, value, dim, source, channel));
}

void Engine::trackNoteExpression(int32_t trackId, int32_t pitch, int32_t dim, float value) {
    if (pitch < -1 || pitch > 127 || dim < 0 || dim >= kExprWireDims || (dim >= kExprDims && pitch >= 0) ||
        !std::isfinite(value))
        return;
    if (pitch < 0) {
        trackController(trackId, ctlOfGlobalDim(dim), value);
        return;
    }
    std::lock_guard<std::mutex> lk(remoteMidiMx_);
    remoteMidi_.push(MidiEvent{false, pitch, value, trackId, dim});
}

void Engine::controller(int32_t ctl, float value, int32_t source, int32_t channel) {
    if (ctlIsReserved(ctl) || !std::isfinite(value)) return;
    value = ctlClamp(ctl, value);
    noteLiveController(ctl, value, source, channel);
    std::lock_guard<std::mutex> lk(liveMidiMx_);
    liveMidi_.push(liveEvent(false, -1, value, kCtlDimBase + ctl, source, channel));
}

bool Engine::setTrackMidiInput(int32_t trackId, int32_t source, int32_t channel) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument) return false;
    if (source != Track::kMidiAllInputs && (source < 0 || source >= MidiInput::kMaxSources)) source = -1; // nothing
    channel = std::clamp(channel, 0, 16);
    if (source == Track::kMidiAllInputs) t->midiInputUid.clear();
    t->setMidiInput(source, channel);
    if (const int32_t k = midiLaneOf(trackId); k >= 0) { // recording now: the lane follows
        midiLaneSrc_[static_cast<size_t>(k)].store(source, std::memory_order_relaxed);
        midiLaneCh_[static_cast<size_t>(k)].store(channel, std::memory_order_relaxed);
    }
    return true;
}

bool Engine::setTrackMidiInputUid(int32_t trackId, const std::string& uid, int32_t channel) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument) return false;
    t->midiInputUid = uid;
    return setTrackMidiInput(trackId, uid.empty() ? Track::kMidiAllInputs : midiSourceOfUid(uid), channel);
}
std::string Engine::trackMidiInputUid(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->midiInputUid : std::string{};
}

// MIDI take lanes (specs/086): one per recording instrument track, published to the audio
// thread before the take's recording_ flag; a lane closes when its track stops recording.
void Engine::publishMidiLanes(const std::vector<int32_t>& trackIds) {
    const int32_t n = std::min(static_cast<int32_t>(trackIds.size()), kMaxMidiTakeLanes);
    midiLanes_.assign(static_cast<size_t>(n), MidiTakeLane{});
    for (int32_t k = 0; k < n; ++k) {
        const size_t i = static_cast<size_t>(k);
        midiLanes_[i].trackId = trackIds[i];
        auto t = findTrackAuthoring(trackIds[i]);
        midiLaneSrc_[i].store(t ? t->midiInputSource() : Track::kMidiAllInputs, std::memory_order_relaxed);
        midiLaneCh_[i].store(t ? t->midiInputChannel() : 0, std::memory_order_relaxed);
        midiLaneTrack_[i].store(trackIds[i], std::memory_order_relaxed);
    }
    midiLaneCount_.store(n, std::memory_order_release);
    publishSessionLaneSlots();
}

void Engine::closeMidiLane(int32_t lane) {
    if (lane < 0 || lane >= static_cast<int32_t>(midiLanes_.size())) return;
    midiLanes_[static_cast<size_t>(lane)].trackId = -1;
    midiLaneTrack_[static_cast<size_t>(lane)].store(-1, std::memory_order_relaxed);
    publishSessionLaneSlots();
}

// The Session lanes' slots, for the audio thread's per-lane clocks (specs/086); none for an
// Arrangement take. The table keeps its players alive while a render may read it.
void Engine::publishSessionLaneSlots() {
    std::shared_ptr<SessionLaneSlots> tab;
    for (size_t k = 0; k < midiLanes_.size() && k < 16; ++k) {
        const MidiTakeLane& l = midiLanes_[k];
        if (l.trackId < 0 || l.scene < 0 || !l.player) continue;
        if (!tab) tab = std::make_shared<SessionLaneSlots>();
        tab->player[k] = l.player.get();
        tab->scene[k] = l.scene;
        double len = 4.0;
        if (auto t = findTrackAuthoring(l.trackId); t && l.scene < static_cast<int32_t>(t->sessionSlots.size()))
            len = t->sessionSlots[static_cast<size_t>(l.scene)].lengthBeats;
        tab->len[k] = len;
        tab->keep.push_back(l.player);
    }
    laneSlotsHist_.publish(std::move(tab));
}

// A MIDI slot launched on another instrument track while a Session MIDI take runs (specs/086):
// it joins the take as a lane of its own — its own clip, input filter and loop clock — and
// records from when its slot starts playing (the launch quantum).
bool Engine::addSessionMidiLane(int32_t trackId, int32_t scene) {
    if (!recording_.load(std::memory_order_relaxed) || recordSessionAudio_ || recordSessionScene_ < 0 ||
        sessionCountIn_ || midiLanes_.empty() || midiLanes_[0].scene < 0)
        return false;
    if (midiLaneOf(trackId) >= 0 || static_cast<int32_t>(midiLanes_.size()) >= kMaxMidiTakeLanes) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument || scene < 0 || scene >= authoring_->sceneCount) return false;
    noteTakeTrack(trackId);
    const bool hasClip = scene < static_cast<int32_t>(t->sessionSlots.size()) && t->sessionSlots[scene].hasClip;
    const bool growing = !hasClip && sessionRecordLen_ <= 0.0;
    if (!hasClip) {
        TakePushScope takePush(*this); // the take's own clip (specs/078)
        addSessionMidiClip(trackId, scene, growing ? kGrowingTakeBeats : sessionRecordLen_);
        t = findTrackAuthoring(trackId);
        if (!t) return false;
    }
    MidiTakeLane l;
    l.trackId = trackId;
    l.scene = scene;
    l.growing = growing;
    l.player = t->sessionPlayer;
    const size_t k = midiLanes_.size();
    midiLanes_.push_back(std::move(l));
    midiLaneSrc_[k].store(t->midiInputSource(), std::memory_order_relaxed);
    midiLaneCh_[k].store(t->midiInputChannel(), std::memory_order_relaxed);
    midiLaneTrack_[k].store(trackId, std::memory_order_relaxed);
    publishSessionLaneSlots(); // its clock is there before the lane is
    midiLaneCount_.store(static_cast<int32_t>(k) + 1, std::memory_order_release);
    if (takeAttempt_.active) takeAttempt_.sessionSlots.emplace_back(trackId, scene); // a retry relaunches it
    launchSlot(trackId, scene); // plays + loops the slot from the launch quantum
    return true;
}

// A fresh MIDI take that ran open-ended: cut it to whole launch quanta (a bar when launching is
// unquantized) — no undo step of its own, so undo drops the whole take.
void Engine::cutGrowingLane(MidiTakeLane& l) {
    if (!l.growing || l.trackId < 0 || l.scene < 0) return;
    l.growing = false;
    double el = 0.0;
    if (l.player && l.player->playing.load(std::memory_order_relaxed) == l.scene)
        el = l.player->playedBeats.load(std::memory_order_relaxed);
    if (auto old = findTrackAuthoring(l.trackId); old && l.scene < static_cast<int32_t>(old->sessionSlots.size())) {
        auto nt = cloneTrack(*old);
        SessionSlot& sl = nt->sessionSlots[static_cast<size_t>(l.scene)];
        const double len = roundedTakeBeats(el);
        sl.lengthBeats = len;
        sl.midi.lengthBeats = len;
        republishWithTrackRaw(l.trackId, nt);
    }
}

// One slot of a several-slot Session MIDI take stops (specs/086): what it recorded goes in, its
// open controller ranges close, a fresh open-ended take is cut to quanta, and the slot plays
// on. The first slot leaving hands the take's clock and count to the next.
void Engine::finishSessionMidiLane(int32_t lane) {
    if (lane < 0 || lane >= static_cast<int32_t>(midiLanes_.size())) return;
    foldRecordedTake(); // everything queued so far, every lane
    MidiTakeLane& l = midiLanes_[static_cast<size_t>(lane)];
    foldSessionLane(l, lane, {}, {}, true);
    cutGrowingLane(l);
    const int32_t tid = l.trackId;
    closeMidiLane(lane);
    if (takeAttempt_.active) keepTakeTrack(tid); // landed: a retry leaves it be
    if (tid == recordSessionTrackId_) { // the take's first slot: the next one carries it
        for (auto& n : midiLanes_) {
            if (n.trackId < 0 || n.scene < 0) continue;
            recordSessionTrackId_ = n.trackId;
            recordSessionScene_ = n.scene;
            recordSessionGrowing_ = n.growing;
            if (auto t = findTrackAuthoring(n.trackId); t && n.scene < static_cast<int32_t>(t->sessionSlots.size()))
                recordSlotLen_.store(t->sessionSlots[static_cast<size_t>(n.scene)].lengthBeats, std::memory_order_relaxed);
            recordSlotHist_.publish(n.player);
            recordSlotPlayer_.store(n.player.get(), std::memory_order_release);
            takeAttempt_.sessionTrackId = n.trackId;
            takeAttempt_.sessionScene = n.scene;
            break;
        }
    }
}

int32_t Engine::midiLaneOf(int32_t trackId) const {
    for (size_t k = 0; k < midiLanes_.size(); ++k)
        if (midiLanes_[k].trackId == trackId) return static_cast<int32_t>(k);
    return -1;
}

int32_t Engine::openMidiLaneCount() const {
    int32_t n = 0;
    for (const auto& l : midiLanes_) n += l.trackId >= 0 ? 1 : 0;
    return n;
}
int32_t Engine::trackMidiInputSource(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->midiInputSource() : Track::kMidiAllInputs;
}
int32_t Engine::trackMidiInputChannel(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->midiInputChannel() : 0;
}

void Engine::trackController(int32_t trackId, int32_t ctl, float value) {
    if (ctlIsReserved(ctl) || !std::isfinite(value)) return;
    value = ctlClamp(ctl, value);
    noteLiveController(ctl, value, -1, 0, trackId);
    std::lock_guard<std::mutex> lk(remoteMidiMx_);
    remoteMidi_.push(MidiEvent{false, -1, value, trackId, kCtlDimBase + ctl});
}

void Engine::trackNoteOn(int32_t trackId, int32_t pitch, float velocity) {
    {
        std::lock_guard<std::mutex> lk(remoteMidiMx_);
        remoteMidi_.push(MidiEvent{true, pitch, velocity, trackId});
    }
    if (pitch >= 0 && pitch < 128) liveHeld_[pitch >> 5].fetch_or(1u << (pitch & 31), std::memory_order_relaxed);
}
void Engine::trackNoteOff(int32_t trackId, int32_t pitch, float releaseVelocity) {
    {
        std::lock_guard<std::mutex> lk(remoteMidiMx_);
        remoteMidi_.push(MidiEvent{false, pitch, std::clamp(releaseVelocity, 0.0f, 1.0f), trackId});
    }
    if (pitch >= 0 && pitch < 128) liveHeld_[pitch >> 5].fetch_and(~(1u << (pitch & 31)), std::memory_order_relaxed);
}

// Currently-pressed live-input pitches (keyboard + MIDI), oldest bit order. Lock-free.
int32_t Engine::liveHeldNotes(int32_t* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    int32_t n = 0;
    for (int w = 0; w < 4 && n < maxN; ++w) {
        uint32_t bits = liveHeld_[w].load(std::memory_order_relaxed);
        while (bits && n < maxN) {
            int b = std::countr_zero(bits); // portable count-trailing-zeros (C++20)
            out[n++] = w * 32 + b;
            bits &= bits - 1;
        }
    }
    return n;
}

// The live-recording preview (#56): the audio thread's held-note snapshot, retried while a
// block is writing it. Take geometry comes from the message-thread record state (this
// runs on the UI thread, which owns it). A read that keeps racing returns 0 — one
// frame without preview, never a wait.
int32_t Engine::recordHeldNotes(
    NotaRecordHeldNote* out, int32_t maxN, int32_t* trackId, int32_t* sessionScene, double* nowBeat) const {
    // An audio take into a Session slot records no MIDI: recordTrackId_ may still name the
    // last MIDI take's track (it isn't reset on stop), so it must not light up here.
    const bool rec = recording_.load(std::memory_order_relaxed) && !recordSessionAudio_;
    const bool slot = rec && recordSessionScene_ >= 0 && recordSessionTrackId_ > 0;
    const int32_t tid = !rec ? 0 : slot ? recordSessionTrackId_ : (recordTrackId_ > 0 ? recordTrackId_ : 0);
    if (trackId) *trackId = tid;
    if (sessionScene) *sessionScene = slot ? recordSessionScene_ : -1;
    if (nowBeat) *nowBeat = 0.0;
    if (tid <= 0) return 0;
    for (int attempt = 0; attempt < 4; ++attempt) {
        const uint32_t s0 = heldSeq_.load(std::memory_order_acquire);
        if (s0 & 1u) continue;
        const int32_t n = std::clamp(heldCount_.load(std::memory_order_relaxed), 0, kMaxHeldSnap);
        const double now = heldNow_.load(std::memory_order_relaxed);
        const int32_t m = (out && maxN > 0) ? std::min(n, maxN) : 0;
        for (int32_t i = 0; i < m; ++i) {
            out[i].pitch = heldSnap_[i].pitch.load(std::memory_order_relaxed);
            out[i].source = heldSnap_[i].source.load(std::memory_order_relaxed);
            out[i].start_beat = heldSnap_[i].startBeat.load(std::memory_order_relaxed);
            out[i].velocity = heldSnap_[i].velocity.load(std::memory_order_relaxed);
        }
        std::atomic_thread_fence(std::memory_order_acquire);
        if (heldSeq_.load(std::memory_order_relaxed) != s0) continue;
        if (nowBeat) *nowBeat = now;
        return n;
    }
    return 0;
}

void Engine::setRecording(bool on) {
    // The record button is also the automation-record switch (M9-C): while it is
    // engaged, moving any control writes that parameter's lane. This is set even
    // when no track is armed for a take — if the UI then pops the button back up
    // it calls us again with false, which clears it.
    setAutomationRecord(on);
    if (on) {
        collectRecordedExpr();
        pendingExpr_.clear();
        resetCtlFold();
    } // a new take starts with no stale curves
    if (!on) {
        // Controllers still being replaced over a range end where the take stops (poll folds it).
        if (recording_.load(std::memory_order_relaxed)) {
            ctlStopPending_ = true;
            ctlStopAt_ = transport_.uiPositionBeats();
        }
        recording_.store(false, std::memory_order_relaxed);
        stopAudioRecording(); // materialise if an audio take was rolling
        if (!takeAttempt_.session) closeTakeAttempt(); // the take is kept: nothing to put back
        // If the take started the transport, stop it and rewind to where the
        // take began so re-recording lands in the same place (no drift).
        if (recordStartedTransport_) {
            recordStartedTransport_ = false;
            transportStop();
            seekBeats(recordReturnBeat_);
        }
        return;
    }
    startArrangementTake(transport_.uiIsPlaying(), transport_.uiPositionBeats(), false);
}

// The take start behind setRecording(true). A retry (specs/078) passes where the take starts
// and whether the clock rolls — the transport mirror still shows the take it just dropped —
// and retryCountIn counts in again right away (the stop it queued hasn't landed yet).
bool Engine::startArrangementTake(bool wasPlaying, double playhead, bool retryCountIn) {
    // Record every armed track together: every armed audio track captures audio — each its
    // own hardware input, in one take (specs/086), or the first one another track's output —
    // and the first armed instrument track captures MIDI. Both run at once, so arming an
    // instrument + audio tracks and hitting REC records them all.
    std::vector<int32_t> audioIds;
    std::vector<std::shared_ptr<Track>> instTs; // every armed instrument track records MIDI (specs/086)
    std::vector<int32_t> instSkipped;
    for (auto& t : authoring_->tracks) {
        if (!t->armed()) continue;
        if (t->type() == TrackType::Audio) audioIds.push_back(t->id());
        else if (t->type() == TrackType::Instrument) {
            if (static_cast<int32_t>(instTs.size()) < kMaxMidiTakeLanes) instTs.push_back(t);
            else instSkipped.push_back(t->id());
        }
    }
    const bool haveAudio = !audioIds.empty();
    const bool haveMidi = !instTs.empty();
    if (!haveAudio && !haveMidi) {
        recording_.store(false, std::memory_order_relaxed);
        recordStartStatus_ = 1;
        return false;
    }

    // A take started from stop gets the count-in (if one is set); the capture taps stay
    // gated until it runs out, so the clicks-only pre-roll isn't recorded.
    recordCountIn_ = !wasPlaying && !recordSessionAudio_ && countInBars() > 0;
    // Remember what the take may write before it writes anything (the MIDI clip below).
    openTakeAttempt(false);
    takeAttempt_.countedIn = recordCountIn_;
    for (int32_t id : audioIds) noteTakeTrack(id);
    for (auto& t : instTs) noteTakeTrack(t->id());
    takeStartOverride_ = playhead;
    takePlayingOverride_ = wasPlaying ? 1 : 0;
    bool audioOk = true;
    recordSkipped_.clear();
    if (haveAudio) audioOk = startAudioRecordingLanes(audioIds);
    if (!haveMidi) publishMidiLanes({}); // no MIDI lanes left over from an earlier take
    takeStartOverride_ = std::numeric_limits<double>::quiet_NaN();
    takePlayingOverride_ = -1;

    for (int32_t id : instSkipped) recordSkipped_.emplace_back(id, kSkipTooMany);
    if (haveMidi) {
        // Each track overdubs into a clip that already spans the playhead, else opens one
        // there; the new clips are one undo step (specs/086).
        std::vector<int32_t> ids, clips;
        beginUndoGroup();
        for (auto& instT : instTs) {
            int32_t idx = -1;
            for (int32_t i = 0; i < static_cast<int32_t>(instT->midiClips.size()); ++i) {
                const MidiClip& c = instT->midiClips[i];
                if (playhead >= c.startBeat && playhead < c.startBeat + c.lengthBeats) {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) {
                auto nt = cloneTrack(*instT);
                MidiClip c;
                c.startBeat = playhead;
                c.lengthBeats = 4.0;
                nt->midiClips.push_back(c);
                idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
                TakePushScope takePush(*this); // the take's own clip (specs/078)
                republishWithTrack(instT->id(), nt);
            }
            ids.push_back(instT->id());
            clips.push_back(idx);
        }
        endUndoGroup();
        publishMidiLanes(ids);
        for (size_t k = 0; k < clips.size(); ++k) midiLanes_[k].clipIndex = clips[k];
        recordTrackId_ = ids[0];
        recordClipIndex_ = clips[0];
        // A fresh take for loop record replace: nothing recorded, nothing replaced yet (the
        // audio thread also zeroes replaceUpTo_ when it sees the take start).
        takeNotes_.clear();
        appliedReplaceUpTo_ = 0;
        replaceUpTo_.store(0, std::memory_order_relaxed);
        recording_.store(true, std::memory_order_relaxed);
    }

    // Unified transport/rewind bookkeeping (the sub-calls above may have set per-call
    // values; make them consistent for the combined take).
    if (!recordSessionAudio_) {
        recordReturnBeat_ = playhead;
        recordStartedTransport_ = !wasPlaying;
    }
    if (!wasPlaying) { // roll so the take advances (idempotent if already rolling)
        if (recordCountIn_ && retryCountIn) {
            arrangementActive_.store(true, std::memory_order_relaxed);
            startCountIn(countInBars());
        } else if (recordCountIn_)
            transportPlayCountIn();
        else
            transportPlay();
    }
    recordCountIn_ = false;
    recordStartStatus_ = (haveAudio && !audioOk && !haveMidi) ? 2 : 0;
    if (!isRecording()) closeTakeAttempt(); // nothing started: nothing to retry
    return isRecording();
}

double Engine::takePlayhead() const {
    return std::isnan(takeStartOverride_) ? transport_.uiPositionBeats() : takeStartOverride_;
}

bool Engine::takeTransportPlaying() const {
    return takePlayingOverride_ < 0 ? transport_.uiIsPlaying() : takePlayingOverride_ != 0;
}

// Decide the capture layout at take start: loop punch if the transport loop is on
// (record start point mapped into the loop), else a linearly growing buffer.
void Engine::beginCaptureLayout() {
    captureLoop_ = false;
    captureWrapped_ = false;
    captureWritePos_ = 0;
    captureOrigin_ = 0;
    capturePassFrom_ = 0;
    for (auto& l : captureLanes_) {
        l.buf.clear();
        l.passes.clear();
    }
    if (!recordSessionAudio_ && loopEnabled_ && loopEnd_ > loopStart_) activateLoopPunch(audioRecordStartBeat_);
}

// Switch capture into loop punch: a fixed loop-region buffer per lane whose shared cursor
// starts at the loop-local position of atBeat and wraps at the loop length. Called at record
// start (loop already on) or the first drain after loop is enabled mid-take (discarding the
// linear pre-roll — the point of enabling the loop is to (re)record the loop region).
void Engine::activateLoopPunch(double atBeat) {
    const double devSR = transport_.sampleRate();
    const double fpb = transport_.samplesPerBeat() * (devSR > 0.0 ? audioRecordSampleRate_ / devSR : 1.0);
    const double loopLen = loopEnd_ - loopStart_;
    if (loopLen <= 0.0 || fpb <= 0.0) return;
    captureLoopFrames_ = static_cast<size_t>(std::llround(loopLen * fpb));
    if (captureLoopFrames_ == 0) return;
    double local = atBeat - loopStart_;
    local -= std::floor(local / loopLen) * loopLen; // wrap into [0, loopLen)
    if (local < 0.0) local = 0.0;
    captureLoopStartBeat_ = loopStart_;
    captureOrigin_ = static_cast<size_t>(std::llround(local * fpb)) % captureLoopFrames_;
    captureWritePos_ = captureOrigin_;
    captureWrapped_ = false;
    for (auto& l : captureLanes_) {
        l.buf.assign(captureLoopFrames_ * static_cast<size_t>(l.channels), 0.0f); // fresh loop-region buffer, zeroed
        l.passes.clear();
    }
    captureLoop_ = true;
    capturePassFrom_ = captureOrigin_;
}

// The pass that started at capturePassFrom_ ends at `to`: keep a copy per lane as a future
// take (specs/067) before the next pass overwrites the loop buffer. Message thread (the drain).
void Engine::finishLoopPass(size_t to) {
    if (to <= capturePassFrom_ || captureLoopFrames_ == 0) return;
    for (auto& l : captureLanes_) {
        const size_t ch = static_cast<size_t>(l.channels);
        LoopPass p;
        p.channels = l.channels;
        p.buf.assign(captureLoopFrames_ * ch, 0.0f);
        std::copy(l.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch),
                  l.buf.begin() + static_cast<std::ptrdiff_t>(to * ch),
                  p.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch));
        p.from = capturePassFrom_;
        p.to = to;
        l.passes.push_back(std::move(p));
    }
}

const Engine::CaptureLane* Engine::captureLaneOf(int32_t trackId) const {
    for (const auto& l : captureLanes_)
        if (l.trackId == trackId) return &l;
    return nullptr;
}

double Engine::audioRecordLengthBeats(int32_t trackId) const {
    if (!audioRecording_) return 0.0;
    const CaptureLane* l = trackId > 0 ? captureLaneOf(trackId) : (captureLanes_.empty() ? nullptr : &captureLanes_[0]);
    if (!l) return 0.0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    const double sampleSR = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : devSR;
    if (spb <= 0.0 || sampleSR <= 0.0) return 0.0;
    const double frames = static_cast<double>(l->buf.size() / static_cast<size_t>(l->channels));
    return frames * devSR / sampleSR / spb;
}

void Engine::drainInputQueue() {
    // Loop enabled after recording began → switch to loop punch from here.
    if (audioRecording_ && !recordSessionAudio_ && !captureLoop_ && loopEnabled_ && loopEnd_ > loopStart_)
        activateLoopPunch(transport_.uiPositionBeats());

    for (auto& l : captureLanes_) l.scratch.clear();
    auto clearScratch = [&] {
        for (auto& l : captureLanes_) l.scratch.clear();
    };
    // Hardware takes: blocks of layout columns, scattered into every lane — its pick is one
    // column (stored mono) or two (specs/086). A block of a layout that no longer carries a
    // lane's pick — or one too old to look up — keeps its length there as silence.
    CaptureBlock b;
    while (recordRing_.next(b)) {
        if (b.flags & CaptureBlock::kMarker) { // the producer's first frames of a new capture epoch follow
            if (awaitCaptureMarker_) clearScratch();
            awaitCaptureMarker_ = false;
            continue;
        }
        if (awaitCaptureMarker_) { // written for the take a retry threw away (specs/078)
            recordRing_.skip(b.frames);
            continue;
        }
        const CaptureLayoutSnapshot* L = layoutFor(b.generation);
        const size_t w = static_cast<size_t>(std::max(b.width, 1));
        captureBlock_.resize(static_cast<size_t>(b.frames) * w);
        const int32_t got = recordRing_.read(captureBlock_.data(), b.frames);
        for (auto& l : captureLanes_) {
            const int32_t c0 = L ? L->column(l.first) : -1;
            const int32_t c1 = l.width == 2 ? (L ? L->column(l.first + 1) : -1) : -1;
            const bool has0 = c0 >= 0 && c0 < b.width, has1 = c1 >= 0 && c1 < b.width;
            for (int32_t i = 0; i < got; ++i) {
                const float* row = captureBlock_.data() + static_cast<size_t>(i) * w;
                l.scratch.push_back(has0 ? row[c0] : 0.0f);
                if (l.channels == 2) l.scratch.push_back(has1 ? row[c1] : 0.0f);
            }
        }
    }
    // Internal resampling takes (one lane, stereo): frames from the audio thread.
    InputFrame f;
    while (internalQueue_.pop(f)) {
        if (isCaptureMarker(f)) {
            if (awaitCaptureMarker_) clearScratch();
            awaitCaptureMarker_ = false;
            continue;
        }
        if (awaitCaptureMarker_ || captureLanes_.empty()) continue;
        captureLanes_[0].scratch.push_back(f.l);
        captureLanes_[0].scratch.push_back(f.r);
    }
    if (awaitCaptureMarker_) inputDroppedFrames_.store(0, std::memory_order_relaxed);
    if (captureLanes_.empty()) return;
    // Frames the producer had to drop while this drain was stalled (ring full).
    // They are the newest samples — the ones that couldn't fit after the popped batch —
    // so append an equal run of silence *after* the drained frames. That keeps the take's
    // frame count equal to the elapsed transport time: a dropout becomes a bounded local
    // gap instead of shifting (and progressively desyncing) everything recorded after it.
    if (int64_t dropped = inputDroppedFrames_.exchange(0, std::memory_order_relaxed); dropped > 0)
        for (auto& l : captureLanes_)
            l.scratch.insert(l.scratch.end(), static_cast<size_t>(dropped) * static_cast<size_t>(l.channels), 0.0f);
    const size_t frames = captureLanes_[0].scratch.size() / static_cast<size_t>(captureLanes_[0].channels);
    if (frames == 0) return;

    if (captureLoop_ && captureLoopFrames_ > 0) {
        // Loop punch: write into the loop-region buffers, the shared cursor wrapping at the
        // loop length so each pass overwrites the previous in place (frame-count clock → clean seams).
        for (size_t i = 0; i < frames; ++i) {
            for (auto& l : captureLanes_) {
                const size_t ch = static_cast<size_t>(l.channels);
                for (size_t c = 0; c < ch; ++c) l.buf[captureWritePos_ * ch + c] = l.scratch[i * ch + c];
            }
            if (++captureWritePos_ >= captureLoopFrames_) {
                finishLoopPass(captureLoopFrames_);
                captureWritePos_ = 0;
                captureWrapped_ = true;
                capturePassFrom_ = 0;
            }
        }
        return;
    }

    // Linear append: every lane grows by exactly what was captured, reserved in 30 s steps so
    // a long multi-track take doesn't reallocate on every drain.
    const size_t base = captureWritePos_;
    const size_t chunk = static_cast<size_t>(std::max(audioRecordSampleRate_, 8000.0) * 30.0);
    for (auto& l : captureLanes_) {
        const size_t ch = static_cast<size_t>(l.channels);
        const size_t at = base > l.offset ? base - l.offset : 0; // a lane that joined later starts at its own 0
        const size_t need = (at + frames) * ch;
        if (l.buf.capacity() < need) l.buf.reserve(need + chunk * ch);
        if (l.buf.size() < need) l.buf.resize(need, 0.0f);
        std::copy_n(l.scratch.data(), std::min(l.scratch.size(), frames * ch), l.buf.data() + at * ch);
    }
    captureWritePos_ = base + frames;
    // A retried Session audio take (specs/078) keeps what follows its launch boundary: note the
    // capture frame where it was crossed now, at the tempo it was crossed at.
    if (recordSessionAudio_ && !std::isnan(retryStartBeat_) && retryKeepFrom_ < 0) {
        const double pos = transport_.uiPositionBeats();
        if (pos >= retryStartBeat_) {
            const double devSR = transport_.sampleRate();
            const double fpb = transport_.samplesPerBeat() * (devSR > 0.0 ? audioRecordSampleRate_ / devSR : 1.0);
            const int64_t past = static_cast<int64_t>(std::llround((pos - retryStartBeat_) * fpb));
            retryKeepFrom_ = std::max<int64_t>(0, static_cast<int64_t>(captureWritePos_) - past);
        }
    }
}

bool Engine::startAudioRecording(int32_t trackId) {
    return startAudioRecordingLanes(std::vector<int32_t>{trackId});
}

// One audio take on every listed track (specs/086). The first track decides the kind: an
// internal source (another track, a return, the master) records that one track as before; a
// hardware input records every listed track on the hardware input, each its own pick, sharing
// the take's start, clock and latency correction. Tracks that can't join are reported in
// recordSkipped_.
bool Engine::startAudioRecordingLanes(const std::vector<int32_t>& trackIds) {
    recordSkipped_.clear();
    std::vector<std::shared_ptr<Track>> tracks;
    for (int32_t id : trackIds)
        if (auto t = findTrackAuthoring(id); t && t->type() == TrackType::Audio) tracks.push_back(std::move(t));
    if (tracks.empty()) return false;
    captureLanes_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;
    inputDroppedFrames_.store(0, std::memory_order_relaxed); // fresh drop accounting per take
    awaitCaptureMarker_ = retryingTake_; // a retried take keeps nothing from before its marker
    retryKeepFrom_ = -1;
    audioRecordStartBeat_ = takePlayhead();

    // Internal resampling: capture another track / send / master output instead of the
    // hardware input. The audio thread feeds the ring from mixGraph; no device is opened.
    const int32_t src = tracks[0]->recordInputSource();
    if (src != 0) {
        for (size_t k = 1; k < tracks.size(); ++k) recordSkipped_.emplace_back(tracks[k]->id(), kSkipMixedSources);
        CaptureLane lane;
        lane.trackId = tracks[0]->id();
        lane.scene = recordSessionAudio_ ? recordSessionScene_ : -1;
        captureLanes_.push_back(std::move(lane));
        audioRecordTrackId_ = captureLanes_[0].trackId;
        audioRecordLatencyFrames_ = 0; // internal resampling has no device round trip (specs/049, R7)
        internalRecordSource_.store(src, std::memory_order_relaxed);
        audioRecordSampleRate_ = transport_.sampleRate();
        beginCaptureLayout();
        audioRecording_ = true;
        if (!recordSessionAudio_) {
            recordReturnBeat_ = audioRecordStartBeat_;
            recordStartedTransport_ = !takeTransportPlaying();
        }
        if (!takeTransportPlaying() && !recordCountIn_) transportPlay();
        return true;
    }

    // The device may already be open for monitoring: flush anything a previous take's
    // tap left behind, then start feeding the capture ring.
    if (!ensureAudioInput()) {
        audioRecordTrackId_ = 0;
        return false;
    }
    const int32_t inputs = inputChannelCount();
    for (auto& t : tracks) {
        if (t->recordInputSource() != 0) {
            recordSkipped_.emplace_back(t->id(), kSkipMixedSources);
            continue;
        }
        if (inputs > 0 && t->inputFirst() + t->inputWidth() > inputs) { // the device lacks its input
            recordSkipped_.emplace_back(t->id(), kSkipInputUnavailable);
            continue;
        }
        bool off = false; // switched off on the Audio I/O page
        for (int32_t c = t->inputFirst(); c < t->inputFirst() + t->inputWidth() && c < kMaxInputChannels; ++c)
            off = off || inputDisabled_[static_cast<size_t>(c)];
        if (off) {
            recordSkipped_.emplace_back(t->id(), kSkipSwitchedOff);
            continue;
        }
        CaptureLane lane;
        lane.trackId = t->id();
        lane.first = t->inputFirst();
        lane.width = t->inputWidth();
        lane.channels = lane.width; // a mono pick is stored mono
        lane.scene = recordSessionAudio_ ? recordSessionScene_ : -1;
        captureLanes_.push_back(std::move(lane));
    }
    if (captureLanes_.empty()) {
        audioRecordTrackId_ = 0;
        return false;
    }
    audioRecordTrackId_ = captureLanes_[0].trackId;
    // A hardware take is late by the device's round trip (in + out); 0 on backends that don't
    // report it (specs/049, research R7). Every lane gets the same correction (specs/086).
    audioRecordLatencyFrames_ = audioInputLatencyFrames() + audioOutputLatencyFrames();
    recordRing_.clear();
    {
        InputFrame stale;
        while (internalQueue_.pop(stale)) {}
    }
    audioRecording_ = true; // the layout below carries this take's picks
    updateCaptureLayout();
    audioRecording_ = false;
    hwRecordTap_.store(true, std::memory_order_relaxed);
    audioRecordSampleRate_ = input_ && input_->sampleRate() > 0 ? input_->sampleRate() : transport_.sampleRate();
    beginCaptureLayout();
    audioRecording_ = true;
    // Arrangement takes rewind to the take start on stop; session takes manage
    // their own transport via launchSlot, so don't arm the rewind for them.
    if (!recordSessionAudio_) {
        recordReturnBeat_ = audioRecordStartBeat_;
        recordStartedTransport_ = !takeTransportPlaying();
    }
    if (!takeTransportPlaying() && !recordCountIn_) transportPlay(); // else setRecording counts in
    return true;
}

// Stop capturing and throw the capture away (a discarded take, specs/078): the taps go quiet,
// whatever the input or mix thread already queued is drained and dropped with the rest.
void Engine::abortAudioRecording() {
    if (!audioRecording_) return;
    audioRecording_ = false;
    internalRecordSource_.store(0, std::memory_order_relaxed);
    hwRecordTap_.store(false, std::memory_order_relaxed);
    releaseAudioInputIfIdle();
    drainInputQueue();
    captureLanes_.clear();
    updateCaptureLayout(); // the take's picks may no longer be needed
    captureWritePos_ = 0;
    captureLoop_ = false;
    captureWrapped_ = false;
    audioRecordTrackId_ = 0;
    retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
}

// Where the take's loop / linear capture stood when it stopped (shared by every lane).
struct Engine::TakeGeometry {
    bool loop = false, wrapped = false;
    size_t origin = 0, reached = 0, loopFrames = 0;
    double loopStartBeat = 0.0;
};

void Engine::stopAudioRecording() {
    if (!audioRecording_) return;
    audioRecording_ = false;
    internalRecordSource_.store(0, std::memory_order_relaxed); // stop the mixGraph tap
    hwRecordTap_.store(false, std::memory_order_relaxed);      // stop the capture tap
    releaseAudioInputIfIdle();                                 // keep it open if monitoring
    drainInputQueue(); // pull whatever the input (or mix) thread pushed before it stopped

    TakeGeometry g;
    g.loop = captureLoop_;
    g.wrapped = captureWrapped_;
    g.origin = captureOrigin_;
    g.reached = captureWritePos_;
    g.loopFrames = captureLoopFrames_;
    g.loopStartBeat = captureLoopStartBeat_;
    if (g.loop) finishLoopPass(g.reached); // the open pass of every lane, up to where recording stopped

    std::vector<CaptureLane> lanes = std::move(captureLanes_);
    captureLanes_.clear();
    audioRecordTrackId_ = 0;
    captureWritePos_ = 0;
    captureLoop_ = false;
    updateCaptureLayout(); // the take's picks may no longer be needed

    // Every lane lands in one undo step (specs/086).
    beginUndoGroup();
    for (auto& l : lanes) materialiseCaptureLane(l, g);
    endUndoGroup();
    retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
    retryKeepFrom_ = -1;
}

// A track disarmed mid-take: drain, then land its lane alone (as a stop would) and drop it.
void Engine::finishCaptureLane(int32_t trackId) {
    drainInputQueue();
    for (size_t k = 0; k < captureLanes_.size(); ++k) {
        if (captureLanes_[k].trackId != trackId) continue;
        CaptureLane lane = std::move(captureLanes_[k]);
        captureLanes_.erase(captureLanes_.begin() + static_cast<std::ptrdiff_t>(k));
        if (k == 0 && !captureLanes_.empty()) { // the take goes on carried by the next lane
            audioRecordTrackId_ = captureLanes_[0].trackId;
            if (recordSessionAudio_) {
                recordSessionTrackId_ = captureLanes_[0].trackId;
                recordSessionScene_ = captureLanes_[0].scene;
                takeAttempt_.sessionTrackId = recordSessionTrackId_;
                takeAttempt_.sessionScene = recordSessionScene_;
            }
        }
        const bool session = recordSessionAudio_;
        TakeGeometry g;
        g.loop = captureLoop_;
        g.wrapped = captureWrapped_;
        g.origin = captureOrigin_;
        g.reached = captureWritePos_;
        g.loopFrames = captureLoopFrames_;
        g.loopStartBeat = captureLoopStartBeat_;
        if (g.loop && captureWritePos_ > capturePassFrom_) { // its open pass, as finishLoopPass would
            const size_t ch = static_cast<size_t>(lane.channels);
            LoopPass p;
            p.channels = lane.channels;
            p.buf.assign(captureLoopFrames_ * ch, 0.0f);
            std::copy(lane.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch),
                      lane.buf.begin() + static_cast<std::ptrdiff_t>(captureWritePos_ * ch),
                      p.buf.begin() + static_cast<std::ptrdiff_t>(capturePassFrom_ * ch));
            p.from = capturePassFrom_;
            p.to = captureWritePos_;
            lane.passes.push_back(std::move(p));
        }
        materialiseCaptureLane(lane, g);
        recordSessionAudio_ = session; // the other lanes still record into their slots
        break;
    }
    updateCaptureLayout();
}

// A Session slot launched on another armed audio track while a Session audio take runs: it
// joins the take as a lane of its own from now (the take's clock and correction), so several
// slots record together and each stops on its own (specs/086).
bool Engine::addSessionAudioLane(int32_t trackId, int32_t scene) {
    if (!audioRecording_ || !recordSessionAudio_ || internalRecordSource_.load(std::memory_order_relaxed) != 0) return false;
    if (captureLaneOf(trackId)) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio || t->recordInputSource() != 0) return false;
    const int32_t inputs = inputChannelCount();
    if (inputs > 0 && t->inputFirst() + t->inputWidth() > inputs) return false;
    for (int32_t c = t->inputFirst(); c < t->inputFirst() + t->inputWidth() && c < kMaxInputChannels; ++c)
        if (inputDisabled_[static_cast<size_t>(c)]) return false;
    drainInputQueue(); // the cursor up to now: the lane starts here
    CaptureLane lane;
    lane.trackId = trackId;
    lane.first = t->inputFirst();
    lane.width = t->inputWidth();
    lane.channels = lane.width;
    lane.scene = scene;
    lane.offset = captureWritePos_;
    captureLanes_.push_back(std::move(lane));
    noteTakeTrack(trackId);
    if (takeAttempt_.active) takeAttempt_.sessionSlots.emplace_back(trackId, scene); // a retry relaunches it
    updateCaptureLayout(); // its input joins the capture
    return true;
}

// One lane of a stopped take into its track: loop passes as take lanes (specs/067), a Session
// slot (M5-4), or a clip where recording began (latency-corrected), carving what it lands on.
void Engine::materialiseCaptureLane(CaptureLane& lane, const TakeGeometry& g) {
    const int32_t trackId = lane.trackId;
    const size_t ch = static_cast<size_t>(lane.channels);
    auto old = trackId > 0 ? findTrackAuthoring(trackId) : nullptr;
    if (!old || lane.buf.empty()) return;

    // Two or more passes keep every pass as a take (specs/067). A session take never loops here.
    if (g.loop && lane.passes.size() >= 2 && lane.scene < 0) {
        const double sr = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : transport_.sampleRate();
        auto nt = cloneTrack(*old);
        materialiseLoopTakes(*nt, lane.passes, g.loopFrames, g.loopStartBeat, sr);
        republishWithTrack(trackId, nt);
        return;
    }

    // A Session audio retry (specs/078) captured from the retry on; the take starts on the launch
    // quantum after it, so what came before that boundary goes.
    // The boundary frame was noted as the capture crossed it (drainInputQueue); stopped before
    // that, nothing of the retried take is kept.
    // A slot lane (lane.scene), not recordSessionAudio_: the first slot landed clears that flag,
    // and every slot of a multi-slot retry needs the same trim.
    if (lane.scene >= 0 && !std::isnan(retryStartBeat_)) {
        const size_t have = lane.buf.size() / ch;
        const size_t keep = retryKeepFrom_ < 0 ? have + lane.offset : static_cast<size_t>(retryKeepFrom_);
        const size_t drop = std::min(have, keep > lane.offset ? keep - lane.offset : 0); // in the lane's frames
        lane.buf.erase(lane.buf.begin(), lane.buf.begin() + static_cast<std::ptrdiff_t>(drop * ch));
    }
    if (lane.buf.empty()) { // stopped before the retried take's boundary: nothing to keep
        recordSessionAudio_ = false;
        return;
    }

    auto sample = std::make_shared<SampleBuffer>();
    sample->channels = lane.channels;
    sample->frames = static_cast<int64_t>(lane.buf.size() / ch);
    sample->sourceSampleRate = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : transport_.sampleRate();
    lane.buf.shrink_to_fit();
    sample->samples = std::move(lane.buf);

    auto nt = cloneTrack(*old);

    // Session-slot take (M5-4): store into the slot with a loop length derived
    // from the captured duration at the current tempo.
    if (lane.scene >= 0) {
        const int32_t scene = lane.scene;
        recordSessionAudio_ = false;
        if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
            nt->sessionSlots.resize(authoring_->sceneCount);
        if (scene < static_cast<int32_t>(nt->sessionSlots.size())) {
            SessionSlot& s = nt->sessionSlots[scene];
            s.hasClip = true;
            s.audio = AudioClip{};
            s.audio.sample = sample;
            const double sr = transport_.sampleRate();
            const double spb = transport_.samplesPerBeat();
            const double deviceFrames = sample->frames * (sr > 0 ? sr / sample->sourceSampleRate : 1.0);
            s.lengthBeats = spb > 0 ? deviceFrames / spb : 4.0;
            if (s.lengthBeats <= 0.0) s.lengthBeats = 4.0;
            // Fixed-length capture keeps its length; an open-ended take is cut to whole quanta.
            s.lengthBeats = sessionRecordLen_ > 0.0 ? sessionRecordLen_ : roundedTakeBeats(s.lengthBeats);
        }
        republishWithTrack(trackId, nt);
        return;
    }

    // Place the take and carve whatever it lands on (overwrite/comp). Loop punch: a
    // completed pass fills the loop region [loopStart, loopEnd]; a partial pass is the
    // recorded sub-range. Linear: sits where recording began, spanning the captured length.
    // setBpm stores the new tempo before the transport applies it. Playback will measure
    // this clip in that tempo, so the beat range it occupies — and the carve — must too.
    const double spb = authoringSamplesPerBeat();
    const double devSR = transport_.sampleRate();
    const double sampleSR = sample->sourceSampleRate > 0 ? sample->sourceSampleRate : devSR;
    const double beatsPerFrame = (spb > 0.0 && sampleSR > 0.0) ? devSR / sampleSR / spb : 0.0;

    AudioClip clip;
    clip.sample = sample;
    double ns = 0.0, ne = 0.0;
    if (g.loop && g.loopFrames > 0) {
        if (g.wrapped) { // at least one full pass → the whole loop
            clip.startBeat = g.loopStartBeat;
            ns = g.loopStartBeat;
            ne = g.loopStartBeat + static_cast<double>(g.loopFrames) * beatsPerFrame;
        } else if (g.reached > g.origin) { // partial pass → just what was recorded
            clip.sourceOffsetFrames = static_cast<double>(g.origin);
            clip.lengthFrames = static_cast<int64_t>(g.reached - g.origin);
            clip.startBeat = g.loopStartBeat + static_cast<double>(g.origin) * beatsPerFrame;
            ns = clip.startBeat;
            ne = clip.startBeat + static_cast<double>(g.reached - g.origin) * beatsPerFrame;
        } else {
            return; // loop armed but nothing captured
        }
    } else {
        // Linear take: move it earlier by the device latency so it lines up with what was
        // playing when it was performed. (Loop and session takes keep their grid as before.)
        double trimDevice = 0.0;
        clip.startBeat = latencyCompensatedStart(audioRecordStartBeat_, audioRecordLatencyFrames_, spb, &trimDevice);
        double kept = static_cast<double>(sample->frames);
        if (trimDevice > 0.0 && devSR > 0.0) {
            const double trim = std::min(kept, trimDevice * sampleSR / devSR);
            clip.sourceOffsetFrames = trim;
            kept -= trim;
            clip.lengthFrames = static_cast<int64_t>(kept);
        }
        ns = clip.startBeat;
        ne = clip.startBeat + kept * beatsPerFrame;
    }
    if (ne <= ns) return;
    overwriteAudioClipsInRange(nt->clips, ns, ne, spb);
    nt->clips.push_back(clip);
    republishWithTrack(trackId, nt);
}

// Every pass becomes a take (one new group, numbers continuing on the track), and the comp is
// what the in-place loop buffer would have held: each frame from the newest pass that recorded
// it. The loop region is carved as a wrapped loop take always was.
void Engine::materialiseLoopTakes(Track& nt, std::vector<LoopPass>& passes, size_t loopFrames, double loopStartBeat,
                                  double sampleRate) {
    const double bpf = beatsPerSourceFrame(sampleRate);
    const int32_t group = ++nextTakeGroup_;
    int32_t number = 0;
    for (const auto& k : nt.takes) number = std::max(number, k.number);

    std::vector<AudioTake> takes;
    takes.reserve(passes.size());
    for (auto& p : passes) {
        auto sb = std::make_shared<SampleBuffer>();
        sb->channels = p.channels;
        sb->frames = static_cast<int64_t>(loopFrames);
        sb->sourceSampleRate = sampleRate;
        sb->samples = std::move(p.buf);
        AudioTake k;
        k.sample = std::move(sb);
        k.id = ++nextTakeId_;
        k.number = ++number;
        k.group = group;
        k.originBeat = loopStartBeat;
        k.sourceOffsetFrames = static_cast<double>(p.from);
        k.lengthFrames = static_cast<int64_t>(p.to - p.from);
        k.beatsPerFrame = bpf;
        takes.push_back(std::move(k));
    }

    overwriteAudioClipsInRange(nt.clips, loopStartBeat,
                               loopStartBeat + static_cast<double>(loopFrames) * bpf, authoringSamplesPerBeat());
    std::vector<std::pair<size_t, size_t>> open{{0, loopFrames}}; // loop frames no newer pass covers yet
    for (size_t i = passes.size(); i-- > 0 && !open.empty();) {
        const size_t from = passes[i].from, to = passes[i].to;
        std::vector<std::pair<size_t, size_t>> rest;
        for (auto [a, b] : open) {
            const size_t s = std::max(a, from), e = std::min(b, to);
            if (s >= e) {
                rest.emplace_back(a, b);
                continue;
            }
            AudioClip clip;
            clip.sample = takes[i].sample;
            clip.startBeat = loopStartBeat + static_cast<double>(s) * bpf;
            clip.sourceOffsetFrames = static_cast<double>(s);
            clip.lengthFrames = static_cast<int64_t>(e - s);
            clip.takeId = takes[i].id;
            nt.clips.push_back(std::move(clip));
            if (a < s) rest.emplace_back(a, s);
            if (e < b) rest.emplace_back(e, b);
        }
        open = std::move(rest);
    }
    for (auto& k : takes) nt.takes.push_back(std::move(k));
}

double Engine::latencyCompensatedStart(double startBeat, int32_t latencyFrames, double samplesPerBeat,
                                       double* trimFrames) {
    if (trimFrames) *trimFrames = 0.0;
    if (latencyFrames <= 0 || samplesPerBeat <= 0.0) return startBeat;
    const double shift = latencyFrames / samplesPerBeat;
    if (startBeat >= shift) return startBeat - shift;
    if (trimFrames) *trimFrames = (shift - std::max(0.0, startBeat)) * samplesPerBeat;
    return 0.0;
}

int32_t Engine::audioRecordPeaks(float* outMinMax, int32_t maxPoints, int32_t trackId) const {
    if (!outMinMax || maxPoints <= 0 || !audioRecording_) return 0;
    const CaptureLane* l = trackId > 0 ? captureLaneOf(trackId) : (captureLanes_.empty() ? nullptr : &captureLanes_[0]);
    if (!l) return 0;
    const size_t ch = static_cast<size_t>(l->channels);
    const int64_t total = static_cast<int64_t>(l->buf.size() / ch); // frames
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    // Live preview redraws ~30x/s over a growing buffer, so bound the scan: sample
    // at most ~64 frames per bucket. The materialised clip uses full-res peaks.
    const int64_t step = std::max<int64_t>(1, per / 64);
    for (int32_t b = 0; b < buckets; ++b) {
        const int64_t begin = b * per;
        const int64_t end = begin + per;
        float mn = 1.0f, mx = -1.0f;
        for (int64_t f = begin; f < end; f += step) {
            const float* fr = l->buf.data() + static_cast<size_t>(f) * ch;
            const float m = ch == 2 ? 0.5f * (fr[0] + fr[1]) : fr[0];
            mn = std::min(mn, m);
            mx = std::max(mx, m);
        }
        outMinMax[b * 2] = mn;
        outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

int32_t Engine::audioRecordingTracks(int32_t* out, int32_t cap) const {
    if (!audioRecording_) return 0;
    const int32_t n = static_cast<int32_t>(captureLanes_.size());
    for (int32_t i = 0; out && i < n && i < cap; ++i) out[i] = captureLanes_[static_cast<size_t>(i)].trackId;
    return n;
}

int32_t Engine::recordSkipped(int32_t* trackIds, int32_t* reasons, int32_t cap) const {
    const int32_t n = static_cast<int32_t>(recordSkipped_.size());
    for (int32_t i = 0; i < n && i < cap; ++i) {
        if (trackIds) trackIds[i] = recordSkipped_[static_cast<size_t>(i)].first;
        if (reasons) reasons[i] = recordSkipped_[static_cast<size_t>(i)].second;
    }
    return n;
}

void Engine::pushInputFramesForTest(const float* interleavedStereo, int32_t frames) {
    pushInputFramesForTestN(interleavedStereo, frames, 2);
}

void Engine::feedInputForTest(const float* interleaved, int32_t frames, int32_t channels) {
    constexpr int32_t kChunk = 1024; // well inside the ring, drained like poll() would
    for (int32_t at = 0; at < frames; at += kChunk) {
        pushInputFramesForTestN(interleaved + static_cast<size_t>(at) * static_cast<size_t>(channels),
            std::min(kChunk, frames - at), channels);
        if (audioRecording_) drainInputQueue();
    }
}

void Engine::pushInputFramesForTestN(const float* interleaved, int32_t frames, int32_t channels) {
    if (testLayoutChannels_ < channels) {
        testLayoutChannels_ = std::min(channels, kMaxInputChannels);
        updateCaptureLayout();
    }
    // As the input thread does: the first push after a retry marks the new capture epoch.
    markCaptureEpoch(hwCaptureEpoch_, captureEpoch_.load(std::memory_order_acquire), /*hardware=*/true);
    if (!recordRing_.pushGather(layoutMsg_.generation, interleaved, channels, frames, layoutMsg_.channel.data(),
            layoutMsg_.count))
        inputDroppedFrames_.fetch_add(frames, std::memory_order_relaxed);
}

// Tests: one lane on `trackId` with this pick, the cursor at the start (no device).
void Engine::beginTestLane(int32_t trackId, int32_t first, int32_t width) {
    captureLanes_.clear();
    CaptureLane lane;
    lane.trackId = trackId;
    lane.first = first;
    lane.width = width;
    lane.channels = width;
    captureLanes_.push_back(std::move(lane));
    captureWritePos_ = 0;
    captureLoop_ = false;
    audioRecordTrackId_ = trackId;
}

bool Engine::audioRecordSelfTest() {
    const int32_t tid = addAudioTrack();
    beginTestLane(tid, 0, 2);
    audioRecordStartBeat_ = 0.0;
    audioRecordSampleRate_ = 44100.0;
    audioRecordLatencyFrames_ = 0;
    audioRecording_ = true; // simulate a take without opening a device

    constexpr int32_t kFrames = 4410; // 0.1 s
    std::vector<float> sig(kFrames * 2);
    for (int32_t i = 0; i < kFrames; ++i) {
        const float v = 0.5f * std::sin(2.0 * 3.14159265358979 * 440.0 * i / 44100.0);
        sig[i * 2] = v;
        sig[i * 2 + 1] = v;
    }
    pushInputFramesForTest(sig.data(), kFrames);
    drainInputQueue();    // ring -> buffer (mimics poll during recording)
    stopAudioRecording(); // materialise the clip

    auto t = findTrackAuthoring(tid);
    if (!t || t->clips.empty() || !t->clips.back().sample) return false;
    const SampleBuffer& sb = *t->clips.back().sample;
    double sum = 0.0;
    for (float s : sb.samples) sum += static_cast<double>(s) * s;
    const double rms = sb.samples.empty() ? 0.0 : std::sqrt(sum / sb.samples.size());
    return sb.frames >= kFrames - 4 && rms > 0.01;
}

bool Engine::sessionAudioRecordSelfTest() {
    const int32_t tid = addAudioTrack();
    const int32_t scene = 0;

    // Begin a session audio take without opening a device (mimic the input ring).
    beginTestLane(tid, 0, 2);
    captureLanes_[0].scene = scene; // a Session lane lands in its slot (specs/086)
    recordSessionTrackId_ = tid;
    recordSessionScene_ = scene;
    recordSessionAudio_ = true;
    audioRecordTrackId_ = tid;
    audioRecordStartBeat_ = 0.0;
    audioRecordSampleRate_ = 44100.0;
    audioRecordLatencyFrames_ = 0;
    audioRecording_ = true;

    constexpr int32_t kFrames = 4410; // 0.1 s
    std::vector<float> sig(kFrames * 2);
    for (int32_t i = 0; i < kFrames; ++i) {
        const float v = 0.5f * std::sin(2.0 * 3.14159265358979 * 440.0 * i / 44100.0);
        sig[i * 2] = v;
        sig[i * 2 + 1] = v;
    }
    pushInputFramesForTest(sig.data(), kFrames);
    drainInputQueue();
    stopSessionRecord(); // -> stopAudioRecording materialises into slot.audio, then launches

    auto t = findTrackAuthoring(tid);
    if (!t || scene >= static_cast<int32_t>(t->sessionSlots.size())) return false;
    const SessionSlot& s = t->sessionSlots[scene];
    if (!s.hasClip || !s.audio.sample || s.lengthBeats <= 0.0) return false;

    // Play the slot back through the looped renderer and confirm it carries signal.
    if (transport_.sampleRate() <= 0) transport_.setSampleRate(44100.0);
    t->sessionPlayer->playing.store(scene, std::memory_order_relaxed);
    t->sessionPlayer->localBeats = 0.0;
    const double spb = transport_.samplesPerBeat();
    std::vector<float> buf(2048 * 2, 0.0f);
    renderSessionAudioSlotRaw(*t, buf.data(), 2048, spb);
    t->sessionPlayer->playing.store(-1, std::memory_order_relaxed);

    double sum = 0.0;
    for (float v : buf) sum += static_cast<double>(v) * v;
    const double rms = std::sqrt(sum / buf.size());
    return rms > 0.01;
}

} // namespace nota
