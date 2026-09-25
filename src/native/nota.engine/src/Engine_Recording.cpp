// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — live MIDI input, track arming, and audio/MIDI recording (M2/M4-3): note on/off, record start/stop, input-queue drain, capture self-tests.

#include "Engine.h"
#include "AudioBackendFactory.h"
#include "AudioFile.h"
#include "Compressor.h"
#include "Delay.h"
#include "Eq.h"
#include "PluginHostBridge.h"
#include "Reverb.h"
#include "Sampler.h"
#include "Synth.h"
#include "Utility.h"

#include <algorithm>
#include <bit>
#include <chrono>
#include <cmath>

namespace nota {

// --- live MIDI, arming, recording ------------------------------------------
void Engine::setTrackArmed(int32_t trackId, bool armed) {
    if (auto t = findTrackAuthoring(trackId)) t->setArmed(armed);
    if (armed) return;
    // Disarming a track that's currently recording finalizes its take (the audio clip is
    // materialised / MIDI capture stops), so toggling its REC button stops the recording.
    if (audioRecording_ && trackId == audioRecordTrackId_) stopAudioRecording();
    if (recording_.load(std::memory_order_relaxed) && trackId == recordTrackId_) {
        recordTrackId_ = -1;
        recordClipIndex_ = -1;
        recording_.store(false, std::memory_order_relaxed);
    }
}
void Engine::setTrackRecordInput(int32_t trackId, int32_t source) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    t->setRecordInputSource(source);
    if (t->monitor()) { recomputeRouting(); updateMonitorInput(); }   // monitored source moved
}
int32_t Engine::trackRecordInput(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->recordInputSource() : 0;
}

// --- live input monitoring ---------------------------------------------------
void Engine::setTrackMonitor(int32_t trackId, bool on) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return;
    t->setMonitor(on);
    recomputeRouting();      // claim / release the source track's route tap
    updateMonitorInput();    // open / close the capture device for a hardware source
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
    if (!input_) input_ = createAudioInput();
    const uint32_t inputDev = resolveAudioDeviceId(config_.inputDeviceUid, /*inputScope=*/true);
    // The input thread only touches the lock-free rings (RT-safe).
    return input_->start([this](const float* s, int32_t n) {
        if (n > inputBlockFrames_.load(std::memory_order_relaxed))
            inputBlockFrames_.store(n, std::memory_order_relaxed);
        if (hwRecordTap_.load(std::memory_order_relaxed)) {
            int64_t dropped = 0;
            for (int32_t i = 0; i < n; ++i)
                if (!inputQueue_.push(InputFrame{ s[i * 2], s[i * 2 + 1] })) ++dropped;
            if (dropped) inputDroppedFrames_.fetch_add(dropped, std::memory_order_relaxed);
        }
        if (hwMonitorTap_.load(std::memory_order_relaxed))
            for (int32_t i = 0; i < n; ++i)
                if (!monitorQueue_.push(InputFrame{ s[i * 2], s[i * 2 + 1] })) break;   // full: the audio thread trims
    }, inputDev);
}

void Engine::releaseAudioInputIfIdle() {
    if (hwRecordTap_.load(std::memory_order_relaxed) || hwMonitorTap_.load(std::memory_order_relaxed)) return;
    if (input_) input_->stop();
}

// Keep the capture device open exactly while some audio track monitors the hardware
// input. Only acts on a change, so a failed open isn't retried on every edit.
void Engine::updateMonitorInput() {
    bool want = false;
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (t->type() == TrackType::Audio && t->monitor() && t->recordInputSource() == 0) { want = true; break; }
    if (want == hwMonitorWanted_) return;
    hwMonitorWanted_ = want;
    if (want) {
        if (ensureAudioInput()) hwMonitorTap_.store(true, std::memory_order_relaxed);
    } else {
        hwMonitorTap_.store(false, std::memory_order_relaxed);
        releaseAudioInputIfIdle();
    }
}

// Audio thread: pop this segment's hardware input into monitorHwBuf_. The capture and
// output devices run on separate clocks, so keep a cushion of about one capture block
// queued (re-primed after an underrun) and trim any excess so latency can't build up.
void Engine::pullMonitorInput(int32_t frames) {
    float* dst = monitorHwBuf_.data();
    std::fill_n(dst, frames * 2, 0.0f);
    if (!hwMonitorTap_.load(std::memory_order_relaxed)) { monitorPrimed_ = false; return; }
    const size_t cushion = static_cast<size_t>(std::clamp(inputBlockFrames_.load(std::memory_order_relaxed), 64, 4096));
    size_t avail = monitorQueue_.size();
    const size_t n = static_cast<size_t>(frames);
    InputFrame f;
    if (avail > n + cushion * 2 + 256) {          // drifted / stalled: drop the stale backlog
        for (size_t k = avail - (n + cushion); k > 0 && monitorQueue_.pop(f); --k) {}
        avail = n + cushion;
    }
    if (!monitorPrimed_) {
        if (avail < n + cushion) return;          // still filling the cushion: silence
        monitorPrimed_ = true;
    }
    size_t i = 0;
    for (; i < n && monitorQueue_.pop(f); ++i) { dst[i * 2] = f.l; dst[i * 2 + 1] = f.r; }
    if (i < n) monitorPrimed_ = false;            // underrun: rebuild the cushion
}

void Engine::pushMonitorFramesForTest(const float* interleavedStereo, int32_t frames) {
    inputTestMode_ = true;
    hwMonitorTap_.store(true, std::memory_order_relaxed);
    for (int32_t i = 0; i < frames; ++i)
        monitorQueue_.push(InputFrame{ interleavedStereo[i * 2], interleavedStereo[i * 2 + 1] });
}
void Engine::setTrackMidiSource(int32_t trackId, int32_t sourceTrackId) {
    if (sourceTrackId == trackId) sourceTrackId = -1;   // no self-routing
    if (auto t = findTrackAuthoring(trackId)) t->setMidiFromTrackId(sourceTrackId);
}
int32_t Engine::trackMidiSource(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? t->midiFromTrackId() : -1;
}
void Engine::noteOn(int32_t pitch, float velocity) {
    MidiEvent e{true, pitch, velocity}; liveMidi_.push(e);
    if (pitch >= 0 && pitch < 128)
        liveHeld_[pitch >> 5].fetch_or(1u << (pitch & 31), std::memory_order_relaxed);
}
void Engine::noteOff(int32_t pitch) {
    MidiEvent e{false, pitch, 0.0f}; liveMidi_.push(e);
    if (pitch >= 0 && pitch < 128)
        liveHeld_[pitch >> 5].fetch_and(~(1u << (pitch & 31)), std::memory_order_relaxed);
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

void Engine::setRecording(bool on) {
    // The record button is also the automation-record switch (M9-C): while it is
    // engaged, moving any control writes that parameter's lane. This is set even
    // when no track is armed for a take — if the UI then pops the button back up
    // it calls us again with false, which clears it.
    setAutomationRecord(on);
    if (!on) {
        recording_.store(false, std::memory_order_relaxed);
        stopAudioRecording(); // materialise if an audio take was rolling
        // If the take started the transport, stop it and rewind to where the
        // take began so re-recording lands in the same place (no drift).
        if (recordStartedTransport_) {
            recordStartedTransport_ = false;
            transportStop();
            seekBeats(recordReturnBeat_);
        }
        return;
    }
    // Record every armed track together: the first armed audio track captures audio
    // (hardware or internal), the first armed instrument track captures MIDI. Both run
    // at once, so arming an instrument + an audio track and hitting REC records both.
    const bool wasPlaying = transport_.uiIsPlaying();
    const double playhead = transport_.uiPositionBeats();
    std::shared_ptr<Track> audioT, instT;
    for (auto& t : authoring_->tracks) {
        if (!t->armed()) continue;
        if (t->type() == TrackType::Audio) { if (!audioT) audioT = t; }
        else if (t->type() == TrackType::Instrument) { if (!instT) instT = t; }
    }
    if (!audioT && !instT) { recording_.store(false, std::memory_order_relaxed); recordStartStatus_ = 1; return; }

    bool audioOk = true;
    if (audioT) audioOk = startAudioRecording(audioT->id());

    if (instT) {
        recordTrackId_ = instT->id();
        // Overdub into a clip that already spans the playhead, else open one there.
        int32_t idx = -1;
        for (int32_t i = 0; i < static_cast<int32_t>(instT->midiClips.size()); ++i) {
            const MidiClip& c = instT->midiClips[i];
            if (playhead >= c.startBeat && playhead < c.startBeat + c.lengthBeats) { idx = i; break; }
        }
        if (idx < 0) {
            auto nt = cloneTrack(*instT);
            MidiClip c; c.startBeat = playhead; c.lengthBeats = 4.0;
            nt->midiClips.push_back(c);
            idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
            republishWithTrack(recordTrackId_, nt);
        }
        recordClipIndex_ = idx;
        recording_.store(true, std::memory_order_relaxed);
    }

    // Unified transport/rewind bookkeeping (the sub-calls above may have set per-call
    // values; make them consistent for the combined take).
    if (!recordSessionAudio_) {
        recordReturnBeat_ = playhead;
        recordStartedTransport_ = !wasPlaying;
    }
    if (!wasPlaying) transportPlay();   // roll so the take advances (idempotent if already rolling)
    recordStartStatus_ = (audioT && !audioOk && !instT) ? 2 : 0;
}

// Decide the capture layout at take start: loop punch if the transport loop is on
// (record start point mapped into the loop), else a linearly growing buffer.
void Engine::beginCaptureLayout() {
    captureLoop_ = false;
    captureWrapped_ = false;
    captureWritePos_ = 0;
    captureOrigin_ = 0;
    if (!recordSessionAudio_ && loopEnabled_ && loopEnd_ > loopStart_)
        activateLoopPunch(audioRecordStartBeat_);
}

// Switch capture into loop punch: a fixed loop-region buffer whose cursor starts at the
// loop-local position of atBeat and wraps at the loop length. Called at record start (loop
// already on) or the first drain after loop is enabled mid-take (discarding the linear
// pre-roll — the point of enabling the loop is to (re)record the loop region).
void Engine::activateLoopPunch(double atBeat) {
    const double devSR = transport_.sampleRate();
    const double fpb = transport_.samplesPerBeat() * (devSR > 0.0 ? audioRecordSampleRate_ / devSR : 1.0);
    const double loopLen = loopEnd_ - loopStart_;
    if (loopLen <= 0.0 || fpb <= 0.0) return;
    captureLoopFrames_ = static_cast<size_t>(std::llround(loopLen * fpb));
    if (captureLoopFrames_ == 0) return;
    double local = atBeat - loopStart_;
    local -= std::floor(local / loopLen) * loopLen;   // wrap into [0, loopLen)
    if (local < 0.0) local = 0.0;
    captureLoopStartBeat_ = loopStart_;
    captureOrigin_ = static_cast<size_t>(std::llround(local * fpb)) % captureLoopFrames_;
    captureWritePos_ = captureOrigin_;
    captureWrapped_ = false;
    audioCaptureBuf_.assign(captureLoopFrames_ * 2, 0.0f);   // fresh loop-region buffer, zeroed
    captureLoop_ = true;
}

double Engine::audioRecordLengthBeats() const {
    if (!audioRecording_) return 0.0;
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    const double sampleSR = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : devSR;
    if (spb <= 0.0 || sampleSR <= 0.0) return 0.0;
    const double frames = static_cast<double>(audioCaptureBuf_.size() / 2);
    return frames * devSR / sampleSR / spb;
}

void Engine::drainInputQueue() {
    // Loop enabled after recording began → switch to loop punch from here.
    if (audioRecording_ && !recordSessionAudio_ && !captureLoop_ && loopEnabled_ && loopEnd_ > loopStart_)
        activateLoopPunch(transport_.uiPositionBeats());

    captureScratch_.clear();
    InputFrame f;
    while (inputQueue_.pop(f)) { captureScratch_.push_back(f.l); captureScratch_.push_back(f.r); }
    // Frames the audio thread had to drop while this drain was stalled (ring full).
    // They are the newest samples — the ones that couldn't fit after the popped batch —
    // so append an equal run of silence *after* the drained frames. That keeps the take's
    // frame count equal to the elapsed transport time: a dropout becomes a bounded local
    // gap instead of shifting (and progressively desyncing) everything recorded after it.
    if (int64_t dropped = inputDroppedFrames_.exchange(0, std::memory_order_relaxed); dropped > 0)
        captureScratch_.insert(captureScratch_.end(), static_cast<size_t>(dropped) * 2, 0.0f);
    const size_t frames = captureScratch_.size() / 2;
    if (frames == 0) return;

    if (captureLoop_ && captureLoopFrames_ > 0) {
        // Loop punch: write into the loop-region buffer, cursor wrapping at the loop length
        // so each pass overwrites the previous in place (frame-count clock → clean seams).
        for (size_t i = 0; i < frames; ++i) {
            const size_t idx = captureWritePos_ * 2;
            audioCaptureBuf_[idx]     = captureScratch_[i * 2];
            audioCaptureBuf_[idx + 1] = captureScratch_[i * 2 + 1];
            if (++captureWritePos_ >= captureLoopFrames_) { captureWritePos_ = 0; captureWrapped_ = true; }
        }
        return;
    }

    // Linear append: the take grows by exactly what was captured.
    const size_t base = captureWritePos_;
    const size_t need = (base + frames) * 2;
    if (audioCaptureBuf_.size() < need) audioCaptureBuf_.resize(need, 0.0f);
    std::copy_n(captureScratch_.data(), frames * 2, audioCaptureBuf_.data() + base * 2);
    captureWritePos_ = base + frames;
}

bool Engine::startAudioRecording(int32_t trackId) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return false;
    audioCaptureBuf_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;
    inputDroppedFrames_.store(0, std::memory_order_relaxed);   // fresh drop accounting per take
    audioRecordTrackId_   = trackId;
    audioRecordStartBeat_ = transport_.uiPositionBeats();

    // Internal resampling: capture another track / send / master output instead of the
    // hardware input. The audio thread feeds the ring from mixGraph; no device is opened.
    const int32_t src = t->recordInputSource();
    if (src != 0) {
        internalRecordSource_.store(src, std::memory_order_relaxed);
        audioRecordSampleRate_ = transport_.sampleRate();
        beginCaptureLayout();
        audioRecording_ = true;
        if (!recordSessionAudio_) {
            recordReturnBeat_ = audioRecordStartBeat_;
            recordStartedTransport_ = !transport_.uiIsPlaying();
        }
        if (!transport_.uiIsPlaying()) transportPlay();
        return true;
    }

    // The device may already be open for monitoring: flush anything a previous take's
    // tap left behind, then start feeding the capture ring.
    if (!ensureAudioInput()) { audioRecordTrackId_ = 0; return false; }
    { InputFrame stale; while (inputQueue_.pop(stale)) {} }
    hwRecordTap_.store(true, std::memory_order_relaxed);
    audioRecordSampleRate_ = input_ && input_->sampleRate() > 0 ? input_->sampleRate() : transport_.sampleRate();
    beginCaptureLayout();
    audioRecording_ = true;
    // Arrangement takes rewind to the take start on stop; session takes manage
    // their own transport via launchSlot, so don't arm the rewind for them.
    if (!recordSessionAudio_) {
        recordReturnBeat_ = audioRecordStartBeat_;
        recordStartedTransport_ = !transport_.uiIsPlaying();
    }
    if (!transport_.uiIsPlaying()) transportPlay();
    return true;
}

void Engine::stopAudioRecording() {
    if (!audioRecording_) return;
    audioRecording_ = false;
    internalRecordSource_.store(0, std::memory_order_relaxed);  // stop the mixGraph tap
    hwRecordTap_.store(false, std::memory_order_relaxed);        // stop the capture tap
    releaseAudioInputIfIdle();                                   // keep it open if monitoring
    drainInputQueue(); // pull whatever the input (or mix) thread pushed before it stopped

    const int32_t trackId = audioRecordTrackId_;
    audioRecordTrackId_ = 0;
    if (audioCaptureBuf_.empty() || trackId <= 0) { audioCaptureBuf_.clear(); return; }
    auto old = findTrackAuthoring(trackId);
    if (!old) { audioCaptureBuf_.clear(); return; }

    // Snapshot the loop-punch state before resetting it (used to place the clip below).
    const bool loopTake = captureLoop_;
    const bool loopWrapped = captureWrapped_;
    const size_t loopOrigin = captureOrigin_;
    const size_t loopReached = captureWritePos_;
    const size_t loopFrames = captureLoopFrames_;
    const double loopStartBeat = captureLoopStartBeat_;

    auto sample = std::make_shared<SampleBuffer>();
    sample->channels         = 2;
    sample->frames           = static_cast<int64_t>(audioCaptureBuf_.size() / 2);
    sample->sourceSampleRate = audioRecordSampleRate_ > 0 ? audioRecordSampleRate_ : transport_.sampleRate();
    sample->samples          = std::move(audioCaptureBuf_);
    audioCaptureBuf_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;

    auto nt = cloneTrack(*old);

    // Session-slot take (M5-4): store into the slot with a loop length derived
    // from the captured duration at the current tempo.
    if (recordSessionAudio_ && recordSessionScene_ >= 0) {
        const int32_t scene = recordSessionScene_;
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
        }
        republishWithTrack(trackId, nt);
        return;
    }

    // Place the take and carve whatever it lands on (overwrite/comp). Loop punch: a
    // completed pass fills the loop region [loopStart, loopEnd]; a partial pass is the
    // recorded sub-range. Linear: sits where recording began, spanning the captured length.
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    const double sampleSR = sample->sourceSampleRate > 0 ? sample->sourceSampleRate : devSR;
    const double beatsPerFrame = (spb > 0.0 && sampleSR > 0.0) ? devSR / sampleSR / spb : 0.0;

    AudioClip clip; clip.sample = sample;
    double ns = 0.0, ne = 0.0;
    if (loopTake && loopFrames > 0) {
        if (loopWrapped) {                       // at least one full pass → the whole loop
            clip.startBeat = loopStartBeat;
            ns = loopStartBeat; ne = loopStartBeat + static_cast<double>(loopFrames) * beatsPerFrame;
        } else if (loopReached > loopOrigin) {   // partial pass → just what was recorded
            clip.sourceOffsetFrames = static_cast<double>(loopOrigin);
            clip.lengthFrames = static_cast<int64_t>(loopReached - loopOrigin);
            clip.startBeat = loopStartBeat + static_cast<double>(loopOrigin) * beatsPerFrame;
            ns = clip.startBeat; ne = clip.startBeat + static_cast<double>(loopReached - loopOrigin) * beatsPerFrame;
        } else {
            return;                              // loop armed but nothing captured
        }
    } else {
        clip.startBeat = audioRecordStartBeat_;
        ns = clip.startBeat; ne = clip.startBeat + static_cast<double>(sample->frames) * beatsPerFrame;
    }
    if (ne <= ns) return;
    overwriteAudioClipsInRange(nt->clips, ns, ne);
    nt->clips.push_back(clip);
    republishWithTrack(trackId, nt);
}

int32_t Engine::audioRecordPeaks(float* outMinMax, int32_t maxPoints) const {
    if (!outMinMax || maxPoints <= 0 || !audioRecording_) return 0;
    const int64_t total = static_cast<int64_t>(audioCaptureBuf_.size() / 2); // frames
    if (total <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, total));
    const int64_t per = total / buckets;
    // Live preview redraws ~30x/s over a growing buffer, so bound the scan: sample
    // at most ~64 frames per bucket. The materialised clip uses full-res peaks.
    const int64_t step = std::max<int64_t>(1, per / 64);
    for (int32_t b = 0; b < buckets; ++b) {
        const int64_t begin = b * per;
        const int64_t end   = begin + per;
        float mn = 1.0f, mx = -1.0f;
        for (int64_t f = begin; f < end; f += step) {
            const float m = 0.5f * (audioCaptureBuf_[f * 2] + audioCaptureBuf_[f * 2 + 1]);
            mn = std::min(mn, m); mx = std::max(mx, m);
        }
        outMinMax[b * 2] = mn; outMinMax[b * 2 + 1] = mx;
    }
    return buckets;
}

void Engine::pushInputFramesForTest(const float* interleavedStereo, int32_t frames) {
    for (int32_t i = 0; i < frames; ++i)
        inputQueue_.push(InputFrame{ interleavedStereo[i * 2], interleavedStereo[i * 2 + 1] });
}

bool Engine::audioRecordSelfTest() {
    const int32_t tid = addAudioTrack();
    audioCaptureBuf_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;
    audioRecordTrackId_    = tid;
    audioRecordStartBeat_  = 0.0;
    audioRecordSampleRate_ = 44100.0;
    audioRecording_        = true;   // simulate a take without opening a device

    constexpr int32_t kFrames = 4410; // 0.1 s
    std::vector<float> sig(kFrames * 2);
    for (int32_t i = 0; i < kFrames; ++i) {
        const float v = 0.5f * std::sin(2.0 * 3.14159265358979 * 440.0 * i / 44100.0);
        sig[i * 2] = v; sig[i * 2 + 1] = v;
    }
    pushInputFramesForTest(sig.data(), kFrames);
    drainInputQueue();       // ring -> buffer (mimics poll during recording)
    stopAudioRecording();    // materialise the clip

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
    audioCaptureBuf_.clear();
    captureWritePos_ = 0;
    captureLoop_ = false;
    recordSessionTrackId_  = tid;
    recordSessionScene_    = scene;
    recordSessionAudio_    = true;
    audioRecordTrackId_    = tid;
    audioRecordStartBeat_  = 0.0;
    audioRecordSampleRate_ = 44100.0;
    audioRecording_        = true;

    constexpr int32_t kFrames = 4410; // 0.1 s
    std::vector<float> sig(kFrames * 2);
    for (int32_t i = 0; i < kFrames; ++i) {
        const float v = 0.5f * std::sin(2.0 * 3.14159265358979 * 440.0 * i / 44100.0);
        sig[i * 2] = v; sig[i * 2 + 1] = v;
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
