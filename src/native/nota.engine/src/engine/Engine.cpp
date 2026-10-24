// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — lifecycle, audio/MIDI device config, preview, tone/transport setters, immutable-graph publish/clone/undo/reset, post-fader meters. Definitions of the rest of the class live in the sibling Engine_*.cpp slices.

#include "Engine.h"
#include "audio_io/AudioBackendFactory.h"
#include "media/AudioFile.h"
#include "bridge/PluginHostBridge.h"
#include "media/WarpStream.h" // complete ClipWarpStream for wb_.stream (unique_ptr) destruction

#include <algorithm>
#include <cmath>

namespace nota {

Engine::Engine()
    : backend_(createAudioBackend()), config_(loadAudioConfig()), // M7-1: persisted device/rate/buffer
      midiConfig_(loadMidiConfig()),                              // M7-2: persisted MIDI input selection
      midiInput_(std::make_unique<MidiInput>()), gamepadInput_(std::make_unique<GamepadInput>()),
      authoring_(std::make_shared<Graph>()) {
    authoring_->masterTrack = std::make_shared<Track>(kMasterTrackId, TrackType::Audio);
    scratch_.assign(kMaxBlock * 2, 0.0f);
    mixTmp_.assign(kMaxBlock * 2, 0.0f);
    monitorHwBuf_.assign(kMaxBlock * 2, 0.0f);
    clipEnvScratch_.assign(kMaxBlock * 2, 0.0f);
    for (auto& b : returnBus_) b.assign(kMaxBlock * 2, 0.0f); // M6-1 send buses
    for (auto& b : groupBus_) b.assign(kMaxBlock * 2, 0.0f);  // group submix buses
    for (auto& b : routeBus_) b.assign(kMaxBlock * 2, 0.0f);  // Phase B route buses (double-buffered)
    for (auto& b : routeBusNext_) b.assign(kMaxBlock * 2, 0.0f);
    for (auto& b : routeBusPre_) b.assign(kMaxBlock * 2, 0.0f); // Phase D pre-FX tap
    for (auto& b : routeBusPreNext_) b.assign(kMaxBlock * 2, 0.0f);
    for (auto& s : routeSlotTrackId_) s.store(-1, std::memory_order_relaxed);
    ensureRenderScratch(authoring_->tracks.size());
    liveGraph_.store(authoring_.get(), std::memory_order_release);
    retired_.push_back(authoring_);
    installRackPluginFactory(); // lets rack blobs rebuild hosted-plugin children on load
}

Engine::~Engine() {
    cancelWarpJob(); // a background warp build must not outlive the engine
    stop();
    if (input_) input_->stop(); // monitoring can keep the capture device open until teardown
}

bool Engine::start() {
    if (backend_->isRunning()) return true;
    transport_.setSampleRate(backend_->sampleRate() > 0 ? backend_->sampleRate() : 44100.0);
    BackendConfig bc;
    bc.device = resolveAudioDeviceId(config_.outputDeviceUid, /*inputScope=*/false);
    bc.sampleRate = config_.sampleRate;
    bc.bufferFrames = config_.bufferFrames;
    bc.wasapiExclusive = config_.wasapiExclusive;
    backend_->setXrunCallback([this] { notifyXrun(); }); // device dropouts (M7-8)
    // The device rate is only known once the backend is open, so the nodes are re-prepared
    // after it starts. Until then the callback must stay out of the graph: prepare()
    // reallocates buffers (and hosted plugins run prepareToPlay) the audio thread would read.
    renderSuspended_.store(true, std::memory_order_release);
    const bool ok = backend_->start([this](float* out, int32_t n) { render(out, n); }, bc);
    if (ok) {
        transport_.setSampleRate(backend_->sampleRate());
        propagateSampleRate(backend_->sampleRate()); // re-prepare every node (incl. master + racks) at the new rate
        reconfigureAllWarpStreams();                 // warp transpose + window sizes depend on the device SR
        recomputePdc();                              // latencies are known once plugins are prepared
    }
    renderSuspended_.store(false, std::memory_order_release);
    if (ok) openMidiInput();
    return ok;
}

// Re-prepare every sound-processing node at the device sample rate. Called on
// start and after a backend restart (device / rate change) so hosted plugins —
// which cache their rate at prepareToPlay — don't keep running at the old rate
// (a rate mismatch makes them play sharp/fast and inharmonic). Racks forward the
// rate to their chain children; the master track lives outside `tracks`, so it
// must be re-rated explicitly.
void Engine::propagateSampleRate(double sr) {
    if (sr <= 0.0) sr = 44100.0;
    auto rate = [&](Track& t) {
        if (t.instrument) t.instrument->setSampleRate(sr);
        for (auto& d : t.devices)
            if (d) d->setSampleRate(sr, kMaxBlock);
        for (auto& m : t.midiEffects)
            if (m) m->setSampleRate(sr, kMaxBlock);
    };
    for (auto& t : authoring_->tracks)
        if (t) rate(*t);
    if (authoring_->masterTrack) rate(*authoring_->masterTrack);
    if (authoring_) ensureRenderScratch(authoring_->tracks.size());
    preparedSR_ = sr; // the offline render re-prepares only when this changes
}

// Connect hardware MIDI controllers (honouring the user's disabled set). Each source has
// its own MPE decoder (channel state is per controller); notes and expression go into the
// live queue, CC + note-on also to MIDI learn.
void Engine::openMidiInput() {
    mpeSink_.e = this;
    for (auto& m : mpeInputs_) m.configure(midiConfig_.mpe, midiConfig_.mpeBendRange);
    midiInput_->open(
        [this](int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2) {
            mpeInputs_[static_cast<size_t>(std::clamp(source, 0, MidiInput::kMaxSources - 1))].message(
                status, channel, d1, d2, mpeSink_);
            // Surface CC + note-on to the UI for MIDI-learn (drop silently if the queue
            // is full — learn is best-effort and must never block the MIDI thread).
            if (status == 0xB0) midiControl_.push({0, channel, d1, d2});
            else if (status == 0x90 && d2 > 0)
                midiControl_.push({1, channel, d1, d2});
        },
        midiConfig_.disabledInputUids);
}

int32_t Engine::pollControlEvents(ControlEvent* out, int32_t max) {
    if (!out || max <= 0) return 0;
    int32_t n = 0;
    ControlEvent ev;
    while (n < max && midiControl_.pop(ev)) out[n++] = ev;
    return n;
}

// --- gamepad input (live note source) ----------------------------------------
// Polling pass-throughs; the mapping from a pad button to a note happens in the
// UI layer (MainWindow.Gamepad.cs) which then calls noteOn()/noteOff() exactly
// as the computer keyboard does.

void Engine::startGamepadInput() {
    if (gamepadInput_) gamepadInput_->start();
}
void Engine::stopGamepadInput() {
    if (gamepadInput_) gamepadInput_->stop();
}
int32_t Engine::gamepadCount() const {
    return gamepadInput_ ? gamepadInput_->padCount() : 0;
}
void Engine::gamepadInfo(int32_t i, const char** uid, const char** name) const {
    if (gamepadInput_) gamepadInput_->padInfo(i, uid, name);
}
int32_t Engine::pollGamepadEvents(GamepadInput::ButtonEvent* out, int32_t max) {
    return (gamepadInput_ && out && max > 0) ? gamepadInput_->pollEvents(out, max) : 0;
}
int32_t Engine::gamepadAxisValues(int32_t pad, int32_t* out, int32_t max) const {
    return (gamepadInput_ && out && max > 0) ? gamepadInput_->axisValues(pad, out, max) : 0;
}

void Engine::stop() {
    if (midiInput_) midiInput_->close();
    if (backend_) backend_->stop();
}

// --- audio device settings (M7-1) --------------------------------------------
// Setters only mutate the pending config; applyAudioConfig() persists it and
// restarts the backend so the new device/rate/buffer take effect.

void Engine::setAudioOutputDevice(const std::string& uid) {
    config_.outputDeviceUid = uid;
}
void Engine::setAudioInputDevice(const std::string& uid) {
    config_.inputDeviceUid = uid;
}
void Engine::setAudioSampleRate(double sr) {
    config_.sampleRate = sr > 0.0 ? sr : 0.0;
}
void Engine::setAudioBufferFrames(int32_t f) {
    config_.bufferFrames = f > 0 ? f : 0;
}
void Engine::setAudioWasapiExclusive(bool on) {
    config_.wasapiExclusive = on;
}

bool Engine::applyAudioConfig() {
    saveAudioConfig(config_);
    stop();
    // Drop the cached input unit so the next capture re-opens the chosen device.
    if (input_) input_.reset();
    const bool ok = start();
    // Live monitoring held the old capture device open: reopen on the chosen one.
    hwRecordTap_.store(false, std::memory_order_relaxed);
    hwMonitorTap_.store(false, std::memory_order_relaxed);
    hwMonitorWanted_ = false;
    updateMonitorInput();
    return ok;
}

// --- MIDI device settings (M7-2) ---------------------------------------------

void Engine::setMidiInputEnabled(const std::string& uid, bool enabled) {
    auto& disabled = midiConfig_.disabledInputUids;
    auto it = std::find(disabled.begin(), disabled.end(), uid);
    if (enabled) {
        if (it != disabled.end()) disabled.erase(it);
    } else {
        if (it == disabled.end()) disabled.push_back(uid);
    }
}

bool Engine::midiInputEnabled(const std::string& uid) const {
    const auto& disabled = midiConfig_.disabledInputUids;
    return std::find(disabled.begin(), disabled.end(), uid) == disabled.end();
}

bool Engine::applyMidiConfig() {
    saveMidiConfig(midiConfig_);
    // Reconnect the port with the new selection; audio keeps running (the live
    // MIDI queue is untouched), so no backend restart is needed.
    midiInput_->close();
    openMidiInput();
    return true;
}

// --- audio preview / audition (M7-4a) ----------------------------------------

bool Engine::previewFile(const std::string& path, double startSeconds) {
    // Re-auditioning the loaded file (a waveform seek, a replay) skips the decode.
    if (!previewHold_ || previewHold_->empty() || path != previewPath_) {
        auto buf = decodeAudioFile(path); // message thread only
        if (!buf || buf->empty()) return false;
        // Retire the previous buffer so the audio thread can finish any block that
        // still holds its raw pointer, then publish the new one.
        publishPreview(buf);
        previewPath_ = path;
    }
    const double start = startSeconds > 0.0 ? startSeconds : 0.0;
    previewStartSec_.store(start, std::memory_order_relaxed);
    previewPosSec_.store(start, std::memory_order_relaxed); // the UI reads the new spot at once
    previewRestart_.store(true, std::memory_order_release);
    previewActive_.store(true, std::memory_order_release);
    return true;
}

void Engine::stopPreview() {
    previewActive_.store(false, std::memory_order_release);
    reapPreview();
}

void Engine::publishPreview(std::shared_ptr<SampleBuffer> buf) {
    auto old = std::move(previewHold_);
    previewHold_ = std::move(buf);
    previewLive_.store(previewHold_.get(), std::memory_order_release);
    // Stamped after the swap: a render that starts from here on reads the new buffer.
    if (old) previewRetired_.emplace_back(previewEpoch_.load(std::memory_order_acquire), std::move(old));
    reapPreview();
}

void Engine::reapPreview() {
    const uint64_t now = previewEpoch_.load(std::memory_order_acquire);
    previewRetired_.erase(std::remove_if(previewRetired_.begin(), previewRetired_.end(),
                              [now](const auto& r) { return now >= r.first + 2; }),
        previewRetired_.end());
}

// --- preset audition cache ---------------------------------------------------------

namespace {
constexpr size_t kAuditionCacheMax = 32;
constexpr int64_t kAuditionCacheBytes = 96ll << 20;
} // namespace

void Engine::auditionStore(const std::string& key, std::shared_ptr<SampleBuffer> buf) {
    if (key.empty() || !buf || buf->empty()) return;
    auditionCache_.remove_if([&](const auto& e) { return e.first == key; });
    auditionCache_.emplace_front(key, std::move(buf));
    // Trim the least recent past the count / memory budget. The buffer the preview voice
    // holds stays alive through previewHold_ even when it drops out of the cache.
    int64_t bytes = 0;
    size_t kept = 0;
    for (auto it = auditionCache_.begin(); it != auditionCache_.end();) {
        bytes += static_cast<int64_t>(it->second->samples.size() * sizeof(float));
        if (++kept > kAuditionCacheMax || (kept > 1 && bytes > kAuditionCacheBytes)) it = auditionCache_.erase(it);
        else
            ++it;
    }
}

bool Engine::auditionCached(const std::string& key) const {
    for (const auto& e : auditionCache_)
        if (e.first == key) return true;
    return false;
}

bool Engine::previewCached(const std::string& key, double startSeconds) {
    auto it = std::find_if(auditionCache_.begin(), auditionCache_.end(), [&](const auto& e) { return e.first == key; });
    if (it == auditionCache_.end()) return false;
    auditionCache_.splice(auditionCache_.begin(), auditionCache_, it); // most recent
    const auto& buf = auditionCache_.front().second;
    if (previewHold_ != buf) publishPreview(buf);
    previewPath_ = key; // a file preview of any real path decodes afresh
    const double start = startSeconds > 0.0 ? startSeconds : 0.0;
    previewStartSec_.store(start, std::memory_order_relaxed);
    previewPosSec_.store(start, std::memory_order_relaxed);
    previewRestart_.store(true, std::memory_order_release);
    previewActive_.store(true, std::memory_order_release);
    return true;
}

int32_t Engine::previewScope(float* out, int32_t n) const {
    if (!out || n <= 0) return 0;
    n = std::min(n, kPreviewScope);
    const int64_t w = previewScopeW_.load(std::memory_order_acquire);
    for (int32_t i = 0; i < n; ++i)
        out[i] = previewScope_[(w - n + i) & (kPreviewScope - 1)].load(std::memory_order_relaxed);
    return n;
}

// Audio thread: mix the audition voice into `out` at its natural pitch.
void Engine::renderPreview(float* out, int32_t numFrames) {
    // Every call, however it returns, ends one render epoch (see publishPreview).
    struct EpochTick {
        std::atomic<uint64_t>& e;
        ~EpochTick() { e.fetch_add(1, std::memory_order_release); }
    } tick{previewEpoch_};
    if (!previewActive_.load(std::memory_order_acquire)) return;
    SampleBuffer* sb = previewLive_.load(std::memory_order_acquire);
    if (!sb || sb->empty()) {
        previewActive_.store(false, std::memory_order_release);
        return;
    }
    const float target = previewGain_.load(std::memory_order_relaxed);
    if (previewRestart_.exchange(false, std::memory_order_acq_rel)) {
        previewPos_ = previewStartSec_.load(std::memory_order_relaxed) * sb->sourceSampleRate;
        previewGainCur_ = target; // a fresh start jumps straight to the level, no fade-in
    }

    const double sr = transport_.sampleRate();
    const double ratio = sr > 0 ? sb->sourceSampleRate / sr : 1.0; // source frames per device frame
    const bool loop = previewLoop_.load(std::memory_order_relaxed);
    const float g0 = previewGainCur_, dg = numFrames > 0 ? (target - g0) / numFrames : 0.0f;
    const int64_t scopeW = previewScopeW_.load(std::memory_order_relaxed);
    int64_t scopeN = 0;
    for (int32_t i = 0; i < numFrames; ++i) {
        if (loop && previewPos_ >= sb->frames) previewPos_ -= sb->frames;
        const int64_t i0 = static_cast<int64_t>(previewPos_);
        if (i0 < 0 || i0 >= sb->frames) {
            previewActive_.store(false, std::memory_order_release);
            break;
        }
        const double frac = previewPos_ - i0;
        float l0, r0, l1, r1;
        sb->readStereo(i0, l0, r0);
        // Looping, the last frame interpolates into the first instead of into silence.
        sb->readStereo(loop && i0 + 1 >= sb->frames ? 0 : i0 + 1, l1, r1);
        const float g = g0 + dg * i;
        const float l = g * static_cast<float>(l0 + (l1 - l0) * frac);
        const float r = g * static_cast<float>(r0 + (r1 - r0) * frac);
        out[i * 2] += l;
        out[i * 2 + 1] += r;
        previewScope_[(scopeW + i) & (kPreviewScope - 1)].store(0.5f * (l + r), std::memory_order_relaxed);
        ++scopeN;
        previewPos_ += ratio;
    }
    previewScopeW_.store(scopeW + scopeN, std::memory_order_release);
    previewGainCur_ = target;
    previewPosSec_.store(
        sb->sourceSampleRate > 0 ? previewPos_ / sb->sourceSampleRate : 0.0, std::memory_order_relaxed);
}

bool Engine::xrunSelfTest() {
    const int32_t before = xrunCount();
    notifyXrun();
    return xrunCount() == before + 1;
}

bool Engine::previewSelfTest() {
    // Synthetic 0.1 s stereo tone, previewed and rendered offline (no device).
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = 2;
    buf->sourceSampleRate = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    buf->frames = static_cast<int64_t>(buf->sourceSampleRate * 0.1);
    buf->samples.assign(buf->frames * 2, 0.25f);
    publishPreview(buf);
    previewPath_.clear();
    previewStartSec_.store(0.0, std::memory_order_relaxed);
    previewRestart_.store(true, std::memory_order_release);
    previewActive_.store(true, std::memory_order_release);

    const bool loop0 = previewLoop_.load();
    const float gain0 = previewGain_.load();
    previewLoop_.store(false);
    previewGain_.store(1.0f);
    auto rms = [&](int32_t frames) {
        std::vector<float> out(static_cast<size_t>(frames) * 2, 0.0f);
        renderPreview(out.data(), frames);
        double sum = 0.0;
        for (float v : out) sum += v * (double)v;
        return std::sqrt(sum / out.size());
    };
    const double unity = rms(256);
    bool ok = unity > 1e-4;
    // Gain scales the level (a restart jumps straight to it, no ramp).
    previewGain_.store(0.5f);
    previewStartSec_.store(0.0);
    previewRestart_.store(true);
    previewActive_.store(true);
    ok = ok && std::abs(rms(256) - unity * 0.5) < unity * 0.01;
    previewGain_.store(1.0f);
    // A start offset lands there; played once, the voice runs off the end and stops.
    previewStartSec_.store(0.05);
    previewRestart_.store(true);
    previewActive_.store(true);
    rms(256);
    ok = ok && previewPosSec_.load() > 0.05 && previewPosSec_.load() < 0.07;
    for (int b = 0; b < 64 && previewActive_.load(); ++b) rms(256);
    ok = ok && !previewActive_.load();
    // Looped, it keeps sounding well past the buffer's 0.1 s.
    previewLoop_.store(true);
    previewStartSec_.store(0.0);
    previewRestart_.store(true);
    previewActive_.store(true);
    for (int b = 0; b < 64; ++b) rms(256);
    ok = ok && previewActive_.load() && rms(256) > 1e-4;

    stopPreview();
    previewLoop_.store(loop0);
    previewGain_.store(gain0);
    return ok;
}

// --- debug tone ------------------------------------------------------------
void Engine::setToneEnabled(bool enabled) {
    Command c;
    c.type = CommandType::SetToneEnabled;
    c.i0 = enabled ? 1 : 0;
    commands_.push(c);
}
void Engine::setFrequency(float hz) {
    Command c;
    c.type = CommandType::SetFrequency;
    c.f0 = hz;
    commands_.push(c);
}

// --- transport -------------------------------------------------------------

void Engine::playClock() {
    Command c;
    c.type = CommandType::TransportPlay;
    commands_.push(c);
}
// Main Play (and arrangement record) roll the Arrangement; session launches use playClock().
void Engine::transportPlay() {
    arrangementActive_.store(true, std::memory_order_relaxed);
    playClock();
}
// Play preceded by the count-in (when one is set and the transport is stopped). The audio
// thread clicks it out and rolls the transport sample-accurately at its end.
void Engine::transportPlayCountIn() {
    if (uiCountIn_.load(std::memory_order_relaxed) > 0.0) return; // already counting in
    const int32_t bars = countInBars();
    if (bars <= 0 || transport_.uiIsPlaying()) {
        transportPlay();
        return;
    }
    arrangementActive_.store(true, std::memory_order_relaxed);
    startCountIn(bars);
}
void Engine::startCountIn(int32_t bars) {
    captureGate_.store(false, std::memory_order_relaxed);
    // Mirror it now so a poll before the audio thread picks the command up already sees
    // the count-in (Play pressed again = stop, not a second start).
    uiCountIn_.store(static_cast<double>(bars) * transport_.beatsPerBar(), std::memory_order_relaxed);
    Command c;
    c.type = CommandType::TransportPlay;
    c.i0 = bars;
    commands_.push(c);
}
void Engine::transportStop() {
    arrangementActive_.store(false, std::memory_order_relaxed);
    finalizeSessionRecord(false); // don't lose an in-progress session take on a global stop
    // Stop means everything stops, Session clips included (#139). Immediately, not on the
    // next launch boundary: the clock halts with this command, so a quantized stop would
    // never land and the clips would stay launched (and resume on the next Play).
    // Only tracks with a clip launched or queued: applying a stop flushes the track's notes
    // and MIDI effects, which must not touch a track that plays the Arrangement.
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (auto& sp = t->sessionPlayer;
                sp && (sp->playing.load(std::memory_order_relaxed) >= 0 ||
                          sp->pending.load(std::memory_order_relaxed) != SessionPlayer::kNone))
                sp->requestStopNow();
    sceneFollowScene_ = -1;
    Command c;
    c.type = CommandType::TransportStop;
    commands_.push(c);
}
// Hand every track back to the Arrangement: stop all session clips + re-activate the timeline.
void Engine::backToArrangement() {
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (t->sessionPlayer) t->sessionPlayer->requestStop();
    sceneFollowScene_ = -1;
    arrangementActive_.store(true, std::memory_order_relaxed);
}
void Engine::setBpm(double bpm) {
    Command c;
    c.type = CommandType::SetBpm;
    c.d0 = bpm;
    commands_.push(c);
    // Warped clips play from an offline cache stretched to the tempo,
    // so a tempo change must rebuild them. Do it on this (authoring) thread with the new
    // spb computed directly — the SetBpm command above only reaches the transport on the
    // audio thread later, so reading transport_.samplesPerBeat() here would be stale.
    const double devSR = transport_.sampleRate();
    if (bpm > 0.0 && devSR > 0.0) reconfigureAllWarpStreams(devSR * 60.0 / bpm, devSR);
}
double Engine::bpm() const {
    return transport_.bpm();
}
void Engine::setTimeSignature(int num, int denom) {
    Command c;
    c.type = CommandType::SetTimeSignature;
    c.i0 = num;
    c.i1 = denom;
    commands_.push(c);
}
void Engine::setLoop(bool enabled, double s, double e) {
    loopEnabled_ = enabled && e > s; // message-thread mirror for the UI
    loopStart_ = s;
    loopEnd_ = e;
    Command c;
    c.type = CommandType::SetLoop;
    c.i0 = enabled ? 1 : 0;
    c.d0 = s;
    c.d1 = e;
    commands_.push(c);
}
void Engine::setMetronome(bool on) {
    Command c;
    c.type = CommandType::SetMetronome;
    c.i0 = on ? 1 : 0;
    commands_.push(c);
}
void Engine::seekBeats(double beat) {
    Command c;
    c.type = CommandType::Seek;
    c.d0 = beat;
    commands_.push(c);
}

// --- snapshot helpers (message thread) -------------------------------------

std::shared_ptr<Track> Engine::findTrackAuthoring(int32_t id) const {
    if (id == kMasterTrackId) return authoring_->masterTrack; // master effect chain
    for (auto& t : authoring_->tracks)
        if (t->id() == id) return t;
    return nullptr;
}

std::shared_ptr<Track> Engine::cloneTrack(const Track& t) const {
    auto nt = std::make_shared<Track>(t.id(), t.type());
    nt->setVolume(t.volume());
    nt->setPan(t.pan());
    nt->setMute(t.mute());
    nt->setSolo(t.solo());
    nt->setArmed(t.armed());
    nt->setReturnIndex(t.returnIndex());                             // M6-1
    nt->setGroupId(t.groupId());                                     // group membership
    nt->setRecordInputSource(t.recordInputSource());                 // internal record routing
    nt->setMonitor(t.monitor());                                     // live input monitoring
    nt->setMidiFromTrackId(t.midiFromTrackId());                     // MIDI routing
    for (int b = 0; b < kMaxReturns; ++b) nt->setSend(b, t.send(b)); // M6-1: send levels
    nt->clips = t.clips;
    nt->midiClips = t.midiClips;
    nt->instrument = t.instrument;     // shared — voice state persists
    nt->midiEffects = t.midiEffects;   // shared — arp held/phase state persists
    nt->devices = t.devices;           // shared — DSP state persists (M3)
    nt->pdc = t.pdc;                   // shared — delay-line state persists (M3-7)
    nt->sessionSlots = t.sessionSlots; // Session slots (M5)
    nt->automation = t.automation;     // automation lanes (M9)
    nt->modulators = t.modulators;     // CV modulation sources (Phase 3)
    nt->cvLinks = t.cvLinks;           // CV modulation edges (Phase 3)
    nt->nextModId = t.nextModId;
    nt->sessionPlayer = t.sessionPlayer; // shared — playback state persists (M5-2)
    nt->setFrozen(t.frozen());           // M7: freeze state + buffer (shared by pointer)
    nt->frozenBuf = t.frozenBuf;
    nt->frozenSpb = t.frozenSpb;
    nt->name = t.name; // UI metadata
    nt->colorIndex = t.colorIndex;
    return nt;
}

// Message thread: make sure the published render scratch holds `tracks` tracks. Never
// grows the live one (the audio thread may be inside a block using it): builds a new one
// with headroom and publishes it. Runs inside publishRaw (before the live-graph swap, so a
// block that sees the new graph sees its scratch too) and propagateSampleRate.
void Engine::ensureRenderScratch(size_t tracks) {
    if (const RenderScratch* cur = scratchHist_.live(); cur && cur->tracks >= tracks) return;
    const size_t cap = std::max<size_t>(8, (tracks + 7) / 8 * 8); // grow in steps of 8 tracks
    auto s = std::make_shared<RenderScratch>();
    s->tracks = cap;
    s->leafWork.resize(cap);
    s->leafBuf.assign(cap * (size_t)kMaxBlock * 4, 0.0f); // two halves (dry + post) per track
    s->blockMidi.assign(cap, std::vector<MidiEv>(1024));
    s->blockMidiN.assign(cap, 0);
    s->blockExpr.assign(cap, std::vector<ExprEv>(kMaxBlockExpr));
    s->blockExprN.assign(cap, 0);
    s->parIdx.assign(cap, 0);
    scratchHist_.publish(std::move(s));
}

bool Engine::setRenderThreads(int n) {
    if (isRunning()) return false; // the audio thread may be inside dispatch()
    pool_ = std::make_unique<AudioThreadPool>(n < 0 ? -1 : n);
    return true;
}

void Engine::publishRaw(std::shared_ptr<Graph> g) {
    // Ensure every track has a session slot per scene (M5). Only grows freshly
    // added tracks (already-sized tracks are a no-op), so no race with audio.
    for (auto& t : g->tracks)
        if (static_cast<int32_t>(t->sessionSlots.size()) < g->sceneCount) t->sessionSlots.resize(g->sceneCount);
    authoring_ = g;
    retired_.push_back(g);
    if (g) ensureRenderScratch(g->tracks.size());
    liveGraph_.store(g.get(), std::memory_order_release);
    recomputeRouting();   // Phase B: refresh sidechain source-track → route-slot table
    updateMonitorInput(); // a monitoring track may have come or gone (undo, delete, load)
}

void Engine::pushUndo() {
    if (!authoring_) return;
    // Inside an undo group only the first snapshot counts (endUndoGroup keeps the pre-group
    // graph), so later ones aren't stacked: a gesture of many edits (a kit filling a Drum
    // Rack's pads) would otherwise push the older history off the depth limit.
    if (undoGroupDepth_ > 0) {
        for (size_t i = undoGroupBase_; i < undoStack_.size(); ++i)
            if (undoStack_[i].graph) {
                ++undoPushCount_;
                redoStack_.clear();
                return;
            }
    }
    undoStack_.push_back({authoring_, {}}); // authoring_ is immutable after publish
    ++undoPushCount_;
    if (undoStack_.size() > kMaxUndoDepth) {
        undoStack_.erase(undoStack_.begin());
        if (undoGroupDepth_ > 0 && undoGroupBase_ > 0) --undoGroupBase_; // keep the group's anchor
    }
    redoStack_.clear(); // a new edit invalidates the redo branch
}

void Engine::beginUndoGroup() {
    if (undoGroupDepth_++ == 0) undoGroupBase_ = undoStack_.size();
}

void Engine::endUndoGroup() {
    if (undoGroupDepth_ <= 0 || --undoGroupDepth_ > 0) return;
    // Collapse the group into its first entry: the pre-group graph (the first snapshot any
    // entry of the group holds — a mix-only entry holds none) plus all mix changes merged.
    if (undoStack_.size() > undoGroupBase_ + 1) {
        auto first = undoStack_.begin() + static_cast<std::ptrdiff_t>(undoGroupBase_);
        for (auto it = first + 1; it != undoStack_.end(); ++it) {
            if (!first->graph) first->graph = it->graph;
            mergeMix(first->mix, it->mix);
        }
        undoStack_.erase(first + 1, undoStack_.end());
    }
}

void Engine::publish(std::shared_ptr<Graph> g) {
    pushUndo();
    publishRaw(std::move(g));
}

void Engine::republishWithTrackRaw(int32_t trackId, std::shared_ptr<Track> nt) {
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    if (trackId == kMasterTrackId) g->masterTrack = nt; // master chain edit
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) g->tracks.push_back(t->id() == trackId ? nt : t);
    publishRaw(std::move(g));
}

void Engine::republishWithTrack(int32_t trackId, std::shared_ptr<Track> nt) {
    pushUndo();
    republishWithTrackRaw(trackId, std::move(nt));
}

// Undo/redo: a snapshot entry swaps the graph (the current state becomes the opposite
// stack's target); then its mix changes write their before (undo) / after (redo) values —
// after the graph, so a restored track gets its value. An entry that changes nothing (all
// of its tracks gone) is passed over, so one call always does something if it can.
bool Engine::undo() {
    while (!undoStack_.empty()) {
        UndoEntry e = std::move(undoStack_.back());
        undoStack_.pop_back();
        bool applied = false;
        redoStack_.push_back({e.graph ? authoring_ : nullptr, e.mix});
        // Restore without a new checkpoint; the restored chains may add or drop a latent
        // device (a removed look-ahead coming back, #109), so compensation follows them.
        if (e.graph) {
            publishRaw(std::move(e.graph));
            recomputePdc();
            applied = true;
        }
        for (const auto& c : e.mix) applied |= applyMix(c, false);
        if (applied) return true;
    }
    return false;
}

bool Engine::redo() {
    while (!redoStack_.empty()) {
        UndoEntry e = std::move(redoStack_.back());
        redoStack_.pop_back();
        bool applied = false;
        undoStack_.push_back({e.graph ? authoring_ : nullptr, e.mix});
        if (e.graph) {
            publishRaw(std::move(e.graph));
            recomputePdc();
            applied = true;
        }
        for (const auto& c : e.mix) applied |= applyMix(c, true);
        if (applied) return true;
    }
    return false;
}

// --- mix gestures (specs/009) ----------------------------------------------

namespace {
bool isParamKind(MixKind k) {
    return k == MixKind::DeviceParam || k == MixKind::MidiDeviceParam || k == MixKind::PluginParam;
}
// The CV-modulation target kind (Engine_Modulation) a parameter value step addresses.
int32_t cvKindOf(MixKind k) {
    return k == MixKind::DeviceParam ? 0 : k == MixKind::PluginParam ? 1 : 2;
}
} // namespace

// Does the parameter a value step addresses exist right now? (specs/016: a step whose
// device is gone is skipped, like one whose track is gone.)
bool Engine::paramTargetExists(MixKind kind, int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    if (paramIndex < 0) return false;
    switch (kind) {
    case MixKind::DeviceParam: {
        auto* d = deviceAt(trackId, deviceIndex);
        return d && paramIndex < d->paramCount();
    }
    case MixKind::MidiDeviceParam: {
        auto* m = midiDeviceAt(trackId, deviceIndex);
        return m && paramIndex < m->paramCount();
    }
    case MixKind::PluginParam: return paramIndex < pluginParamCount(trackId, deviceIndex);
    default: return false;
    }
}

bool Engine::mixValue(const MixTarget& t, float& v) const {
    if (t.kind == MixKind::MasterVolume) {
        v = masterVolume();
        return true;
    }
    if (isParamKind(t.kind)) {
        if (!paramTargetExists(t.kind, t.trackId, t.deviceIndex, t.paramIndex)) return false;
        // A CV-modulated parameter: the user edits (and undo restores) the link's base, not
        // the value the modulation is driving right now.
        if (cvBaseValue(cvKindOf(t.kind), t.trackId, t.deviceIndex, t.paramIndex, v)) return true;
        switch (t.kind) {
        case MixKind::DeviceParam: v = deviceGetParam(t.trackId, t.deviceIndex, t.paramIndex); break;
        case MixKind::MidiDeviceParam: v = midiEffectGetParam(t.trackId, t.deviceIndex, t.paramIndex); break;
        default: v = pluginParamGet(t.trackId, t.deviceIndex, t.paramIndex); break;
        }
        return true;
    }
    auto tr = findTrackAuthoring(t.trackId);
    if (!tr) return false;
    v = t.kind == MixKind::TrackVolume ? tr->volume() : tr->pan();
    return true;
}

bool Engine::applyMix(const MixChange& c, bool after) {
    const float v = after ? c.after : c.before;
    if (c.kind == MixKind::MasterVolume) {
        setMasterVolume(v);
        return true;
    }
    if (isParamKind(c.kind)) {
        if (!paramTargetExists(c.kind, c.trackId, c.deviceIndex, c.paramIndex)) return false; // gone: skip
        switch (c.kind) { // the user-facing setters: CV bases, PDC and plugin hosts follow
        case MixKind::DeviceParam: deviceSetParam(c.trackId, c.deviceIndex, c.paramIndex, v); break;
        case MixKind::MidiDeviceParam: midiEffectSetParam(c.trackId, c.deviceIndex, c.paramIndex, v); break;
        default: pluginParamSet(c.trackId, c.deviceIndex, c.paramIndex, v); break;
        }
        return true;
    }
    auto tr = findTrackAuthoring(c.trackId);
    if (!tr) return false; // the track is gone: skip quietly
    if (c.kind == MixKind::TrackVolume) tr->setVolume(v);
    else
        tr->setPan(v);
    return true;
}

// Fold `later` into `into`: per target the older before, the newer after; a target that
// ends where it started is no change at all.
void Engine::mergeMix(std::vector<MixChange>& into, const std::vector<MixChange>& later) {
    for (const auto& c : later) {
        auto it = std::find_if(into.begin(), into.end(), [&](const MixChange& m) {
            return m.kind == c.kind &&
                   (c.kind == MixKind::MasterVolume ||
                       (m.trackId == c.trackId && m.deviceIndex == c.deviceIndex && m.paramIndex == c.paramIndex));
        });
        if (it == into.end()) into.push_back(c);
        else
            it->after = c.after;
    }
    into.erase(
        std::remove_if(into.begin(), into.end(), [](const MixChange& m) { return m.before == m.after; }), into.end());
}

// --- Stereo Map measurements (specs/008) ------------------------------------

namespace {
// Level, balance (on the pan scale — the equal-power pan that gives this energy split, so a
// mono source panned to p reads p), correlation, and width = 1 - correlation (0 for any mono
// source wherever it's panned, 1 for unrelated or anti-phase material). specs/008 research R2.
NotaStereoMeasure deriveStereo(int32_t id, float eL, float eR, float eLR) {
    NotaStereoMeasure m{id, -200.0f, 0.0f, 0.0f, 0.0f};
    const double l = std::max(0.0f, eL), r = std::max(0.0f, eR), sum = l + r;
    if (sum > 1e-20) m.level_db = static_cast<float>(10.0 * std::log10(sum * 0.5));
    if (sum > 1e-20)
        m.balance = static_cast<float>(std::atan2(std::sqrt(r), std::sqrt(l)) * (4.0 / 3.14159265358979323846) - 1.0);
    if (l > 1e-12 && r > 1e-12) m.correlation = static_cast<float>(std::clamp(eLR / std::sqrt(l * r), -1.0, 1.0));
    m.width = std::clamp(1.0f - m.correlation, 0.0f, 1.0f);
    return m;
}
} // namespace

int32_t Engine::stereoMeasures(NotaStereoMeasure* out, int32_t cap) const {
    int32_t n = 0;
    auto put = [&](const NotaStereoMeasure& m) {
        if (out && n < cap) out[n] = m;
        ++n;
    };
    if (authoring_)
        for (const auto& t : authoring_->tracks)
            if (t && t->type() != TrackType::Group)
                put(deriveStereo(t->id(), t->stereoL(), t->stereoR(), t->stereoLR()));
    put(deriveStereo(kMasterTrackId, masterStereoL_.load(std::memory_order_relaxed),
        masterStereoR_.load(std::memory_order_relaxed), masterStereoLR_.load(std::memory_order_relaxed)));
    return n;
}

bool Engine::mixUndoSelfTest() {
    clearUndoHistory();
    const float m0 = masterVolume();
    undoStack_.push_back({nullptr, {{MixKind::MasterVolume, -1, m0, 0.25f}}}); // a valid step…
    setMasterVolume(0.25f);
    undoStack_.push_back({nullptr, {{MixKind::TrackVolume, 0x7fff0001, 1.0f, 0.5f}}}); // …under a dangling one
    const bool undid = undo() && masterVolume() == m0 && undoStack_.empty() && redoStack_.size() == 2;
    const bool redid = redo() && masterVolume() == 0.25f; // the valid step is on top of redo
    const bool rest = !redo() && redoStack_.empty();      // the dangling one applies nothing
    clearUndoHistory();
    setMasterVolume(m0);
    return undid && redid && rest;
}

int32_t Engine::beginMixGesture(const MixTarget* targets, int32_t count) {
    if (!targets || count <= 0) return 0;
    OpenMixGesture g{nextMixGesture_, {}, undoPushCount_, undoStack_.size()};
    for (int32_t i = 0; i < count; ++i) {
        float v = 0.0f;
        if (mixValue(targets[i], v))
            g.targets.push_back(
                {targets[i].kind, targets[i].trackId, v, v, targets[i].deviceIndex, targets[i].paramIndex});
    }
    if (g.targets.empty()) return 0;
    if (++nextMixGesture_ <= 0) nextMixGesture_ = 1;
    mixGestures_.push_back(std::move(g));
    return mixGestures_.back().handle;
}

void Engine::endMixGesture(int32_t handle) {
    auto it = std::find_if(
        mixGestures_.begin(), mixGestures_.end(), [&](const OpenMixGesture& g) { return g.handle == handle; });
    if (it == mixGestures_.end()) return;
    OpenMixGesture g = std::move(*it);
    mixGestures_.erase(it);
    std::vector<MixChange> changes;
    for (auto& c : g.targets) {
        float v = 0.0f;
        if (!mixValue({c.kind, c.trackId, c.deviceIndex, c.paramIndex}, v)) continue; // gone mid-gesture
        if (v == c.before) continue;                                                  // exact: no net change, no step
        c.after = v;
        changes.push_back(c);
    }
    if (changes.empty()) return;
    // Checkpoints pushed during the gesture (automation recording: the lane's creation and
    // the pre-write state) and this value change are one user action: collapse everything
    // the gesture stacked into its first entry, as an undo group would, and add the values.
    if (undoPushCount_ != g.pushCountAtBegin && !undoStack_.empty()) {
        const size_t base = std::min(g.stackSizeAtBegin, undoStack_.size() - 1);
        auto first = undoStack_.begin() + static_cast<std::ptrdiff_t>(base);
        for (auto it = first + 1; it != undoStack_.end(); ++it) {
            if (!first->graph) first->graph = it->graph;
            mergeMix(first->mix, it->mix);
        }
        undoStack_.erase(first + 1, undoStack_.end());
        mergeMix(first->mix, changes);
        return;
    }
    undoStack_.push_back({nullptr, std::move(changes)});
    if (undoStack_.size() > kMaxUndoDepth) {
        undoStack_.erase(undoStack_.begin());
        if (undoGroupDepth_ > 0 && undoGroupBase_ > 0) --undoGroupBase_;
    }
    redoStack_.clear();
}

// --- project pan law (specs/006) -------------------------------------------

bool Engine::setPanLaw(PanLaw law, bool checkpoint) {
    if (law != PanLaw::Balance && law != PanLaw::Stereo) return false;
    if (!authoring_ || authoring_->panLaw == law) return true;
    auto g = std::make_shared<Graph>(*authoring_); // copy: tracks shared, graph-level fields kept
    g->panLaw = law;
    if (checkpoint) publish(std::move(g));
    else { // load / baseline: no crossfade from the previous project's law
        publishRaw(std::move(g));
        panLawSnap_.store(true, std::memory_order_release);
    }
    return true;
}

// --- arrangement sections (specs/016) ---------------------------------------

void Engine::setSections(std::vector<Section> sections, bool checkpoint) {
    if (!authoring_) return;
    sections.erase(std::remove_if(sections.begin(), sections.end(),
                       [](const Section& s) { return !(s.lengthBeats > 0.0); }), // also drops NaN
        sections.end());
    for (auto& s : sections)
        if (!(s.startBeat > 0.0)) s.startBeat = 0.0;
    std::stable_sort(
        sections.begin(), sections.end(), [](const Section& a, const Section& b) { return a.startBeat < b.startBeat; });
    if (sections == authoring_->sections) return;  // nothing changed: no step
    auto g = std::make_shared<Graph>(*authoring_); // copy: tracks shared, graph-level fields kept
    g->sections = std::move(sections);
    if (checkpoint) publish(std::move(g));
    else
        publishRaw(std::move(g));
}

// --- project load (M7-6) ---------------------------------------------------

void Engine::reset() {
    // Stop playback/recording first so the audio thread reads nothing stale.
    transportStop();
    wb_ = {}; // abandon any in-flight warp-cache build (its clip pointer is about to dangle)
    seekBeats(0.0);
    recording_.store(false, std::memory_order_relaxed);
    if (audioRecording_) stopAudioRecording();
    stopPreview();
    // Drop automation write/override state: its lane indices belong to the old project.
    activeWrites_.clear();
    overrides_.clear();
    autoRecord_.store(false, std::memory_order_relaxed);
    // Publish an empty graph (no undo checkpoint — a load is a fresh baseline)
    // and reset the id counter and history. A fresh master effect chain comes with it.
    auto empty = std::make_shared<Graph>();
    empty->masterTrack = std::make_shared<Track>(kMasterTrackId, TrackType::Audio);
    publishRaw(std::move(empty));
    panLawSnap_.store(true, std::memory_order_release); // a new project takes its law at once
    nextTrackId_ = 1;
    clearUndoHistory();
}

void Engine::clearUndoHistory() {
    // The stacks hold old authoring graphs only — never the live one — and every published
    // graph also sits in retired_ until the audio thread has moved past it, so dropping
    // them here frees nothing the audio thread can still be reading.
    undoStack_.clear();
    redoStack_.clear();
    mixGestures_.clear(); // a gesture can't outlive the history it would land in
    touchGestures_.clear();
    undoGroupDepth_ = 0;
    undoGroupBase_ = 0;
}

bool Engine::trackMeter(int32_t trackId, float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    peakL = t->meterPeakL();
    peakR = t->meterPeakR();
    rmsL = t->meterRmsL();
    rmsR = t->meterRmsR();
    return true;
}

void Engine::masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    peakL = masterPeakL_.load(std::memory_order_relaxed);
    peakR = masterPeakR_.load(std::memory_order_relaxed);
    rmsL = masterRmsL_.load(std::memory_order_relaxed);
    rmsR = masterRmsR_.load(std::memory_order_relaxed);
}

} // namespace nota
