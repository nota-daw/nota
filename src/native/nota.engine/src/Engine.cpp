// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — lifecycle, audio/MIDI device config, preview, tone/transport setters, immutable-graph publish/clone/undo/reset, post-fader meters. Definitions of the rest of the class live in the sibling Engine_*.cpp slices.

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
#include "WarpStream.h"   // complete ClipWarpStream for wb_.stream (unique_ptr) destruction

#include <algorithm>
#include <chrono>
#include <cmath>

namespace nota {

Engine::Engine()
    : backend_(createAudioBackend()),
      config_(loadAudioConfig()),          // M7-1: persisted device/rate/buffer
      midiConfig_(loadMidiConfig()),       // M7-2: persisted MIDI input selection
      midiInput_(std::make_unique<MidiInput>()),
      gamepadInput_(std::make_unique<GamepadInput>()),
      authoring_(std::make_shared<Graph>()) {
    authoring_->masterTrack = std::make_shared<Track>(kMasterTrackId, TrackType::Audio);
    scratch_.assign(kMaxBlock * 2, 0.0f);
    clipEnvScratch_.assign(kMaxBlock * 2, 0.0f);
    for (auto& b : returnBus_) b.assign(kMaxBlock * 2, 0.0f); // M6-1 send buses
    for (auto& b : groupBus_)  b.assign(kMaxBlock * 2, 0.0f); // group submix buses
    for (auto& b : routeBus_)        b.assign(kMaxBlock * 2, 0.0f); // Phase B route buses (double-buffered)
    for (auto& b : routeBusNext_)    b.assign(kMaxBlock * 2, 0.0f);
    for (auto& b : routeBusPre_)     b.assign(kMaxBlock * 2, 0.0f); // Phase D pre-FX tap
    for (auto& b : routeBusPreNext_) b.assign(kMaxBlock * 2, 0.0f);
    for (auto& s : routeSlotTrackId_) s.store(-1, std::memory_order_relaxed);
    liveGraph_.store(authoring_.get(), std::memory_order_release);
    retired_.push_back(authoring_);
    installRackPluginFactory();          // lets rack blobs rebuild hosted-plugin children on load
}

Engine::~Engine() { stop(); }

bool Engine::start() {
    if (backend_->isRunning()) return true;
    transport_.setSampleRate(backend_->sampleRate() > 0 ? backend_->sampleRate() : 44100.0);
    BackendConfig bc;
    bc.device          = resolveAudioDeviceId(config_.outputDeviceUid, /*inputScope=*/false);
    bc.sampleRate      = config_.sampleRate;
    bc.bufferFrames    = config_.bufferFrames;
    bc.wasapiExclusive = config_.wasapiExclusive;
    backend_->setXrunCallback([this] { notifyXrun(); }); // device dropouts (M7-8)
    const bool ok = backend_->start([this](float* out, int32_t n) { render(out, n); }, bc);
    if (ok) {
        transport_.setSampleRate(backend_->sampleRate());
        propagateSampleRate(backend_->sampleRate()); // re-prepare every node (incl. master + racks) at the new rate
        reconfigureAllWarpStreams(); // warp transpose + window sizes depend on the device SR
        recomputePdc(); // latencies are known once plugins are prepared
        openMidiInput();
    }
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
        for (auto& d : t.devices)     if (d) d->setSampleRate(sr, kMaxBlock);
        for (auto& m : t.midiEffects) if (m) m->setSampleRate(sr, kMaxBlock);
    };
    for (auto& t : authoring_->tracks) if (t) rate(*t);
    if (authoring_->masterTrack) rate(*authoring_->masterTrack);
    preparedSR_ = sr;   // the offline render re-prepares only when this changes
}

// Connect hardware MIDI controllers (honouring the user's disabled set) — the
// callback pushes into the same lock-free live queue (RT-safe).
void Engine::openMidiInput() {
    midiInput_->open([this](int32_t status, int32_t channel, int32_t d1, int32_t d2) {
        // Route notes to instruments exactly as before.
        if (status == 0x90 && d2 > 0)                          noteOn(d1, d2 / 127.0f);
        else if (status == 0x80 || (status == 0x90 && d2 == 0)) noteOff(d1);
        // Surface CC + note-on to the UI for MIDI-learn (drop silently if the queue
        // is full — learn is best-effort and must never block the MIDI thread).
        if (status == 0xB0)              midiControl_.push({0, channel, d1, d2});
        else if (status == 0x90 && d2 > 0) midiControl_.push({1, channel, d1, d2});
    }, midiConfig_.disabledInputUids);
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

void  Engine::startGamepadInput() { if (gamepadInput_) gamepadInput_->start(); }
void  Engine::stopGamepadInput()  { if (gamepadInput_) gamepadInput_->stop(); }
int32_t Engine::gamepadCount() const { return gamepadInput_ ? gamepadInput_->padCount() : 0; }
void  Engine::gamepadInfo(int32_t i, const char** uid, const char** name) const {
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

void Engine::setAudioOutputDevice(const std::string& uid) { config_.outputDeviceUid = uid; }
void Engine::setAudioInputDevice(const std::string& uid)  { config_.inputDeviceUid  = uid; }
void Engine::setAudioSampleRate(double sr)   { config_.sampleRate   = sr > 0.0 ? sr : 0.0; }
void Engine::setAudioBufferFrames(int32_t f) { config_.bufferFrames = f  > 0   ? f  : 0; }
void Engine::setAudioWasapiExclusive(bool on) { config_.wasapiExclusive = on; }

bool Engine::applyAudioConfig() {
    saveAudioConfig(config_);
    stop();
    // Drop the cached input unit so the next capture re-opens the chosen device.
    if (input_) input_.reset();
    return start();
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

bool Engine::previewFile(const std::string& path) {
    auto buf = decodeAudioFile(path); // message thread only
    if (!buf || buf->empty()) return false;
    // Retire the previous buffer so the audio thread can finish any block that
    // still holds its raw pointer, then publish the new one.
    if (previewHold_) previewRetired_.push_back(std::move(previewHold_));
    previewHold_ = buf;
    previewLive_.store(buf.get(), std::memory_order_release);
    previewRestart_.store(true, std::memory_order_release);
    previewActive_.store(true, std::memory_order_release);
    return true;
}

void Engine::stopPreview() {
    previewActive_.store(false, std::memory_order_release);
}

// Audio thread: mix the audition voice into `out` at its natural pitch.
void Engine::renderPreview(float* out, int32_t numFrames) {
    if (!previewActive_.load(std::memory_order_acquire)) {
        // Safe point to release retired buffers (audio thread not reading them).
        if (!previewRetired_.empty()) previewRetired_.clear();
        return;
    }
    SampleBuffer* sb = previewLive_.load(std::memory_order_acquire);
    if (!sb || sb->empty()) { previewActive_.store(false, std::memory_order_release); return; }
    if (previewRestart_.exchange(false, std::memory_order_acq_rel)) previewPos_ = 0.0;

    const double sr = transport_.sampleRate();
    const double ratio = sr > 0 ? sb->sourceSampleRate / sr : 1.0; // source frames per device frame
    for (int32_t i = 0; i < numFrames; ++i) {
        const int64_t i0 = static_cast<int64_t>(previewPos_);
        if (i0 < 0 || i0 >= sb->frames) { previewActive_.store(false, std::memory_order_release); break; }
        const double frac = previewPos_ - i0;
        float l0, r0, l1, r1;
        sb->readStereo(i0, l0, r0);
        sb->readStereo(i0 + 1, l1, r1);
        out[i * 2]     += static_cast<float>(l0 + (l1 - l0) * frac);
        out[i * 2 + 1] += static_cast<float>(r0 + (r1 - r0) * frac);
        previewPos_ += ratio;
    }
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
    if (previewHold_) previewRetired_.push_back(std::move(previewHold_));
    previewHold_ = buf;
    previewLive_.store(buf.get(), std::memory_order_release);
    previewRestart_.store(true, std::memory_order_release);
    previewActive_.store(true, std::memory_order_release);

    std::vector<float> out(256 * 2, 0.0f);
    renderPreview(out.data(), 256);
    double sum = 0.0;
    for (float v : out) sum += v * (double)v;
    stopPreview();
    return std::sqrt(sum / out.size()) > 1e-4;
}

// --- debug tone ------------------------------------------------------------
void Engine::setToneEnabled(bool enabled) {
    Command c; c.type = CommandType::SetToneEnabled; c.i0 = enabled ? 1 : 0; commands_.push(c);
}
void Engine::setFrequency(float hz) {
    Command c; c.type = CommandType::SetFrequency; c.f0 = hz; commands_.push(c);
}

// --- transport -------------------------------------------------------------

void Engine::playClock() { Command c; c.type = CommandType::TransportPlay; commands_.push(c); }
// Main Play (and arrangement record) roll the Arrangement; session launches use playClock().
void Engine::transportPlay() { arrangementActive_.store(true, std::memory_order_relaxed); playClock(); }
void Engine::transportStop() {
    arrangementActive_.store(false, std::memory_order_relaxed);
    finalizeSessionRecord(false);   // don't lose an in-progress session take on a global stop
    Command c; c.type = CommandType::TransportStop; commands_.push(c);
}
// Hand every track back to the Arrangement: stop all session clips + re-activate the timeline.
void Engine::backToArrangement() {
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (t->sessionPlayer) t->sessionPlayer->requestStop();
    arrangementActive_.store(true, std::memory_order_relaxed);
}
void Engine::setBpm(double bpm) {
    Command c; c.type = CommandType::SetBpm; c.d0 = bpm; commands_.push(c);
    // Warped clips play from an offline cache stretched to the tempo,
    // so a tempo change must rebuild them. Do it on this (authoring) thread with the new
    // spb computed directly — the SetBpm command above only reaches the transport on the
    // audio thread later, so reading transport_.samplesPerBeat() here would be stale.
    const double devSR = transport_.sampleRate();
    if (bpm > 0.0 && devSR > 0.0) reconfigureAllWarpStreams(devSR * 60.0 / bpm, devSR);
}
double Engine::bpm() const { return transport_.bpm(); }
void Engine::setTimeSignature(int num, int denom) {
    Command c; c.type = CommandType::SetTimeSignature; c.i0 = num; c.i1 = denom; commands_.push(c);
}
void Engine::setLoop(bool enabled, double s, double e) {
    loopEnabled_ = enabled && e > s;   // message-thread mirror for the UI
    loopStart_ = s; loopEnd_ = e;
    Command c; c.type = CommandType::SetLoop; c.i0 = enabled ? 1 : 0; c.d0 = s; c.d1 = e; commands_.push(c);
}
void Engine::setMetronome(bool on) { Command c; c.type = CommandType::SetMetronome; c.i0 = on ? 1 : 0; commands_.push(c); }
void Engine::seekBeats(double beat) { Command c; c.type = CommandType::Seek; c.d0 = beat; commands_.push(c); }

// --- snapshot helpers (message thread) -------------------------------------

std::shared_ptr<Track> Engine::findTrackAuthoring(int32_t id) const {
    if (id == kMasterTrackId) return authoring_->masterTrack;   // master effect chain
    for (auto& t : authoring_->tracks) if (t->id() == id) return t;
    return nullptr;
}

std::shared_ptr<Track> Engine::cloneTrack(const Track& t) const {
    auto nt = std::make_shared<Track>(t.id(), t.type());
    nt->setVolume(t.volume()); nt->setPan(t.pan());
    nt->setMute(t.mute());     nt->setSolo(t.solo()); nt->setArmed(t.armed());
    nt->setReturnIndex(t.returnIndex());                       // M6-1
    nt->setGroupId(t.groupId());                               // group membership
    nt->setRecordInputSource(t.recordInputSource());           // internal record routing
    nt->setMidiFromTrackId(t.midiFromTrackId());                   // MIDI routing
    for (int b = 0; b < kMaxReturns; ++b) nt->setSend(b, t.send(b)); // M6-1: send levels
    nt->clips = t.clips;
    nt->midiClips = t.midiClips;
    nt->instrument = t.instrument;   // shared — voice state persists
    nt->midiEffects = t.midiEffects; // shared — arp held/phase state persists
    nt->devices = t.devices;         // shared — DSP state persists (M3)
    nt->pdc = t.pdc;                 // shared — delay-line state persists (M3-7)
    nt->sessionSlots = t.sessionSlots; // Session slots (M5)
    nt->automation = t.automation;   // automation lanes (M9)
    nt->modulators = t.modulators;   // CV modulation sources (Phase 3)
    nt->cvLinks = t.cvLinks;         // CV modulation edges (Phase 3)
    nt->nextModId = t.nextModId;
    nt->sessionPlayer = t.sessionPlayer; // shared — playback state persists (M5-2)
    nt->setFrozen(t.frozen());       // M7: freeze state + buffer (shared by pointer)
    nt->frozenBuf = t.frozenBuf;
    nt->frozenSpb = t.frozenSpb;
    nt->name = t.name;               // UI metadata
    nt->colorIndex = t.colorIndex;
    return nt;
}

void Engine::publishRaw(std::shared_ptr<Graph> g) {
    // Ensure every track has a session slot per scene (M5). Only grows freshly
    // added tracks (already-sized tracks are a no-op), so no race with audio.
    for (auto& t : g->tracks)
        if (static_cast<int32_t>(t->sessionSlots.size()) < g->sceneCount)
            t->sessionSlots.resize(g->sceneCount);
    authoring_ = g;
    retired_.push_back(g);
    liveGraph_.store(g.get(), std::memory_order_release);
    recomputeRouting();   // Phase B: refresh sidechain source-track → route-slot table
}

void Engine::pushUndo() {
    if (!authoring_) return;
    undoStack_.push_back(authoring_);          // authoring_ is immutable after publish
    if (undoStack_.size() > kMaxUndoDepth) undoStack_.erase(undoStack_.begin());
    redoStack_.clear();                         // a new edit invalidates the redo branch
}

void Engine::publish(std::shared_ptr<Graph> g) {
    pushUndo();
    publishRaw(std::move(g));
}

void Engine::republishWithTrackRaw(int32_t trackId, std::shared_ptr<Track> nt) {
    auto g = std::make_shared<Graph>();
    g->sceneCount = authoring_->sceneCount;    // preserve scene count across the swap
    g->masterVolume = authoring_->masterVolume; // carry master automation (M9 follow-up)
    g->masterTrack = (trackId == kMasterTrackId) ? nt : authoring_->masterTrack;  // master chain edit or carry
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks)
        g->tracks.push_back(t->id() == trackId ? nt : t);
    publishRaw(std::move(g));
}

void Engine::republishWithTrack(int32_t trackId, std::shared_ptr<Track> nt) {
    pushUndo();
    republishWithTrackRaw(trackId, std::move(nt));
}

bool Engine::undo() {
    if (undoStack_.empty()) return false;
    redoStack_.push_back(authoring_);          // current state becomes the redo target
    auto g = undoStack_.back();
    undoStack_.pop_back();
    publishRaw(std::move(g));                   // restore without a new checkpoint
    return true;
}

bool Engine::redo() {
    if (redoStack_.empty()) return false;
    undoStack_.push_back(authoring_);
    auto g = redoStack_.back();
    redoStack_.pop_back();
    publishRaw(std::move(g));
    return true;
}

// --- project load (M7-6) ---------------------------------------------------

void Engine::reset() {
    // Stop playback/recording first so the audio thread reads nothing stale.
    transportStop();
    wb_ = {};   // abandon any in-flight warp-cache build (its clip pointer is about to dangle)
    seekBeats(0.0);
    recording_.store(false, std::memory_order_relaxed);
    if (audioRecording_) stopAudioRecording();
    stopPreview();
    // Publish an empty graph (no undo checkpoint — a load is a fresh baseline)
    // and reset the id counter and history. A fresh master effect chain comes with it.
    auto empty = std::make_shared<Graph>();
    empty->masterTrack = std::make_shared<Track>(kMasterTrackId, TrackType::Audio);
    publishRaw(std::move(empty));
    nextTrackId_ = 1;
    undoStack_.clear();
    redoStack_.clear();
}


bool Engine::trackMeter(int32_t trackId, float& peakL, float& peakR,
                        float& rmsL, float& rmsR) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    peakL = t->meterPeakL(); peakR = t->meterPeakR();
    rmsL = t->meterRmsL();   rmsR = t->meterRmsR();
    return true;
}

void Engine::masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    peakL = masterPeakL_.load(std::memory_order_relaxed);
    peakR = masterPeakR_.load(std::memory_order_relaxed);
    rmsL = masterRmsL_.load(std::memory_order_relaxed);
    rmsR = masterRmsR_.load(std::memory_order_relaxed);
}

} // namespace nota
