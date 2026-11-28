// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — lifecycle, audio/MIDI device config, preview, tone/transport setters, immutable-graph publish/clone/undo/reset, post-fader meters. Definitions of the rest of the class live in the sibling Engine_*.cpp slices.

#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "io/IoOps.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/dsp/rt/OutputRouting.h"
#include "model/EngineTypes.h"
#include "render/RenderRoutingPlan.h"
#include "nota/io/audio/AudioConfigStore.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "nota/io/midi/MidiDriverRegistry.h"
#include "nota/pluginhost/PluginHostModule.h"
#include "session/SessionPlayer.h"

#include <algorithm>
#include <atomic>
#include <array>
#include <chrono>
#include <cmath>
#include <thread>
#include <string>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota::detail {

IoOps::IoOps(EngineRuntime& rt)
    : rt_(rt), backend_(io::audio::createAudioOutputStream()), config_(io::audio::loadAudioConfig()),
      midiConfig_(io::midi::loadMidiConfig()), midiInput_(io::midi::createMidiInput()),
      gamepadInput_(std::make_unique<GamepadInput>()) {}

void IoOps::MpeSink::mpeNoteOn(int32_t pitch, float velocity) {
    e->recorder().noteOn(pitch, velocity, source, channel);
}
void IoOps::MpeSink::mpeNoteOff(int32_t pitch) {
    e->recorder().noteOff(pitch, 0.0f, source, channel);
}
void IoOps::MpeSink::mpeNoteOffVelocity(int32_t pitch, float velocity) {
    e->recorder().noteOff(pitch, velocity, source, channel);
}
void IoOps::MpeSink::mpeExpression(int32_t pitch, int32_t dim, float value) {
    e->recorder().noteExpression(pitch, dim, value, source, channel);
}
void IoOps::MpeSink::mpeController(int32_t ctl, float value) {
    e->recorder().controller(ctl, value, source, channel);
}

bool IoOps::start() {
    if (backend_->isRunning()) return true;
    // The audio system the config asks for (specs/049): ASIO only where the build has it.
    const AudioSystem wanted = io::audio::asioSupported() ? config_.audioSystem : AudioSystem::Wasapi;
    // A driver that went silent while running isn't retried by the restart its failure
    // triggers — it could start, stay silent and fail again forever. That restart runs
    // WASAPI; the next apply (the user's) tries ASIO again.
    const std::string skipAsio = std::exchange(asioSkipOnce_, std::string{});
    const bool skipping = wanted == AudioSystem::Asio && !skipAsio.empty();
    const AudioSystem first = skipping ? AudioSystem::Wasapi : wanted;
    if (first != activeSystem_) {
        backend_ = io::audio::createAudioOutputStream(first);
        activeSystem_ = first;
    }
    audioFallbackReason_ = skipping ? skipAsio : std::string{};
    rt_.transport_.setSampleRate(backend_->sampleRate() > 0 ? backend_->sampleRate() : 44100.0);
    BackendConfig bc;
    bc.device = io::audio::resolveAudioDeviceId(config_.outputDeviceUid, /*inputScope=*/false);
    bc.sampleRate = config_.sampleRate;
    bc.bufferFrames = config_.bufferFrames;
    bc.wasapiExclusive = config_.wasapiExclusive;
    bc.asioDriverClsid = config_.asioDriverClsid;
    bc.asioDriverName = config_.asioDriverName;
    bc.asioOutputPair = config_.asioOutputPair;
    bc.asioInputPair = config_.asioInputPair;
    if (skipping) { // the fallback runs on the system default device
        bc = BackendConfig{};
        bc.device = io::audio::resolveAudioDeviceId(std::string{}, /*inputScope=*/false);
    }
    backend_->setXrunCallback([this] { rt_.notifyXrun(); }); // device dropouts (M7-8)
    // The device rate is only known once the backend is open, so the nodes are re-prepared
    // after it starts. Until then the callback must stay out of the graph: prepare()
    // reallocates buffers (and hosted plugins run prepareToPlay) the audio thread would read.
    rt_.renderSuspended_.store(true, std::memory_order_seq_cst);
    // A previous device's callback may still be inside render() — an ASIO driver given up on
    // while a plug-in stalled. Re-preparing the nodes under it would corrupt them: wait for it
    // to leave, and if it never does, stay off rather than run two renders over one graph.
    for (int i = 0; i < 2000 && rt_.rendersInFlight_.load(std::memory_order_seq_cst) > 0; ++i)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    if (rt_.rendersInFlight_.load(std::memory_order_seq_cst) > 0) {
        audioFallbackReason_ = "a plug-in stopped responding inside the audio callback — restart Nota";
        return false;
    }
    bool ok = backend_->start([this](float* main, float* cue, int32_t n) { rt_.render(main, cue, n); }, bc);
    if (!ok && activeSystem_ == AudioSystem::Asio) {
        // FR-011: a missing, busy or broken ASIO driver never leaves Nota silent — run WASAPI
        // on the system default device and say why. The saved choice stays ASIO, so the next
        // apply (or launch) tries the driver again.
        const char* why = backend_->fallbackReason();
        audioFallbackReason_ = why && *why ? why : "the driver didn't start";
        backend_ = io::audio::createAudioOutputStream(AudioSystem::Wasapi);
        activeSystem_ = AudioSystem::Wasapi;
        BackendConfig fallback;
        fallback.device = io::audio::resolveAudioDeviceId(std::string{}, /*inputScope=*/false);
        backend_->setXrunCallback([this] { rt_.notifyXrun(); });
        ok = backend_->start([this](float* main, float* cue, int32_t n) { rt_.render(main, cue, n); }, fallback);
    }
    rt_.monitorSharedClock_.store(ok && backend_->sharesInputClock(), std::memory_order_relaxed);
    if (ok && outputRoutingSet_) backend_->setOutputRouting(outputRouting_); // specs/089
    if (ok) {
        rt_.transport_.setSampleRate(backend_->sampleRate());
        rt_.propagateSampleRate(backend_->sampleRate()); // re-prepare every node (incl. master + racks) at the new rate
        rt_.clips().reconfigureAllWarpStreams();         // warp transpose + window sizes depend on the device SR
        rt_.devices().recomputePdc();                    // latencies are known once plugins are prepared
    }
    // Plug-ins with real-time threads of their own, and the render pool, join the device's IO
    // workgroup (#436).
    void* const wg = ok ? backend_->ioWorkgroup() : nullptr;
    pluginhost::sharedPluginHost().setAudioWorkgroup(wg);
    const int32_t bf = ok ? backend_->bufferFrames() : 0;
    if (ok && bf > rt_.preparedBlockCap_) rt_.ensureBlockCapacity(std::min(bf, kMaxBlock));
    if (rt_.pool_)
        rt_.pool_->setWorkgroup(
            wg, wg && bf > 0 && backend_->sampleRate() > 0 ? 1e9 * bf / backend_->sampleRate() : 0.0);
    rt_.renderSuspended_.store(false, std::memory_order_release);
    if (ok) openMidiInput();
    return ok;
}

// Re-prepare every sound-processing node at the device sample rate. Called on
// start and after a backend restart (device / rate change) so hosted plugins —
// which cache their rate at prepareToPlay — don't keep running at the old rate
// (a rate mismatch makes them play sharp/fast and inharmonic). Racks forward the
// rate to their chain children; the master track lives outside `tracks`, so it
// must be re-rated explicitly.
void IoOps::openMidiInput() {
    for (int32_t i = 0; i < MidiInput::kMaxSources; ++i) {
        mpeSinks_[static_cast<size_t>(i)].e = &rt_;
        mpeSinks_[static_cast<size_t>(i)].source = i;
    }
    for (auto& m : mpeInputs_) m.configure(midiConfig_.mpe, midiConfig_.mpeBendRange);
    midiInput_->open([this](int32_t source, int32_t status, int32_t channel, int32_t d1,
                         int32_t d2) { onMidiMessage(source, status, channel, d1, d2); },
        [this](int32_t source, const uint8_t* data, int32_t len) { onMidiSysex(source, data, len); },
        midiConfig_.disabledInputUids);
    midiSourceUids_ = midiInput_->sourceUids(); // tracks filter by source index (specs/086)
    // Indices move when inputs come and go: every track picks its input up again by uid.
    if (rt_.authoring_)
        for (auto& t : rt_.authoring_->tracks)
            if (t && !t->midiInputUid.empty())
                rt_.recorder().setTrackMidiInputUid(t->id(), t->midiInputUid, t->midiInputChannel());
}

void IoOps::onMidiMessage(int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2) {
    midiSurface_.route(
        source, status, channel, d1, d2,
        [&] {
            const size_t s = static_cast<size_t>(std::clamp(source, 0, MidiInput::kMaxSources - 1));
            mpeSinks_[s].e = &rt_; // also before the inputs were ever opened (tests, offline engines)
            mpeSinks_[s].source = static_cast<int32_t>(s);
            mpeSinks_[s].channel = std::clamp(channel, 0, 15);
            mpeInputs_[s].message(status, channel, d1, d2, mpeSinks_[s]);
        },
        // Surface CC + note-on to the UI for MIDI-learn (drop silently if the queue is full —
        // learn is best-effort and must never block the MIDI thread).
        [&](int32_t kind, int32_t ch, int32_t number, int32_t value) {
            rt_.midiControl_.push({kind, ch, number, value});
        });
}

int32_t IoOps::midiSourceOfUid(const std::string& uid) const {
    if (uid.empty()) return -1;
    for (size_t i = 0; i < midiSourceUids_.size(); ++i)
        if (midiSourceUids_[i] == uid) return static_cast<int32_t>(i);
    return -1;
}

void IoOps::onMidiSysex(int32_t source, const uint8_t* data, int32_t len) {
    midiSurface_.pushSysex(source, data, len);
}

const std::vector<MidiInput::PortInfo>& IoOps::midiPorts() {
    midiPortRows_ = midiInput_ ? midiInput_->ports() : std::vector<MidiInput::PortInfo>{};
    for (const auto& t : testMidiPorts_)
        midiPortRows_.push_back({t->source, "test:" + t->name, t->name, "test:" + t->name});
    return midiPortRows_;
}

bool IoOps::midiSend(const std::string& outputUid, const uint8_t* data, int32_t len) {
    if (!data || len <= 0) return false;
    // An Identity Request (F0 7E <id> 06 01 F7) lets the reply in from every input paired
    // with this output, claimed or not (specs/051 research R5).
    for (int32_t i = 0; i + 5 < len; ++i)
        if (data[i] == 0xF0 && data[i + 1] == 0x7E && data[i + 3] == 0x06 && data[i + 4] == 0x01 && data[i + 5] == 0xF7)
            for (const auto& p : midiPorts())
                if (p.outputUid == outputUid) midiSurface_.armIdentity(p.source);
    if (outputUid.rfind("test:", 0) == 0) {
        for (auto& t : testMidiPorts_)
            if ("test:" + t->name == outputUid) {
                t->sent.write(data, static_cast<size_t>(len));
                return true;
            }
        return false;
    }
    auto it = midiOutputs_.find(outputUid);
    if (it == midiOutputs_.end()) {
        auto out = io::midi::createMidiOutput();
        if (!out || !out->open(outputUid)) return false;
        it = midiOutputs_.emplace(outputUid, std::move(out)).first;
    }
    if (it->second->send(data, len)) return true;
    midiOutputs_.erase(it); // unplugged: reopen on the next send
    return false;
}

int32_t IoOps::addTestMidiPort(const std::string& name) {
    if (name.empty()) return -1;
    for (const auto& t : testMidiPorts_)
        if (t->name == name) return t->source;
    // Take the highest source index no real or test port uses.
    std::array<bool, MidiInput::kMaxSources> used{};
    for (const auto& p : midiPorts())
        used[static_cast<size_t>(std::clamp(p.source, 0, MidiInput::kMaxSources - 1))] = true;
    for (int32_t s = MidiInput::kMaxSources - 1; s >= 0; --s) {
        if (used[static_cast<size_t>(s)]) continue;
        auto t = std::make_unique<TestMidiPort>();
        t->name = name;
        t->source = s;
        testMidiPorts_.push_back(std::move(t));
        return s;
    }
    return -1;
}

bool IoOps::injectTestMidi(int32_t source, const uint8_t* data, int32_t len) {
    if (!data || len <= 0) return false;
    for (auto& t : testMidiPorts_)
        if (t->source == source) {
            t->parser.feed(
                data, static_cast<size_t>(len),
                [&](int status, int channel, int d1, int d2) { onMidiMessage(source, status, channel, d1, d2); },
                [&](const uint8_t* sx, int32_t n) { onMidiSysex(source, sx, n); });
            return true;
        }
    return false;
}

int32_t IoOps::takeTestMidiSent(int32_t source, uint8_t* out, int32_t cap) {
    for (auto& t : testMidiPorts_)
        if (t->source == source) return t->sent.take(out, cap);
    return 0;
}

int32_t IoOps::pollControlEvents(ControlEvent* out, int32_t max) {
    if (!out || max <= 0) return 0;
    int32_t n = 0;
    ControlEvent ev;
    while (n < max && rt_.midiControl_.pop(ev)) out[n++] = ev;
    return n;
}

// --- gamepad input (live note source) ----------------------------------------
// Polling pass-throughs; the mapping from a pad button to a note happens in the
// UI layer (MainWindow.Gamepad.cs) which then calls noteOn()/noteOff() exactly
// as the computer keyboard does.

void IoOps::startGamepadInput() {
    if (gamepadInput_) gamepadInput_->start();
}
void IoOps::stopGamepadInput() {
    if (gamepadInput_) gamepadInput_->stop();
}
int32_t IoOps::gamepadCount() const {
    return gamepadInput_ ? gamepadInput_->padCount() : 0;
}
void IoOps::gamepadInfo(int32_t i, const char** uid, const char** name) const {
    if (gamepadInput_) gamepadInput_->padInfo(i, uid, name);
}
int32_t IoOps::pollGamepadEvents(GamepadInput::ButtonEvent* out, int32_t max) {
    return (gamepadInput_ && out && max > 0) ? gamepadInput_->pollEvents(out, max) : 0;
}
int32_t IoOps::gamepadAxisValues(int32_t pad, int32_t* out, int32_t max) const {
    return (gamepadInput_ && out && max > 0) ? gamepadInput_->axisValues(pad, out, max) : 0;
}

void IoOps::stop() {
    if (rt_.hybrid_) rt_.hybrid_->setStopped();
    rt_.syncStreamingPlaybackState(false);
    if (midiInput_) midiInput_->close();
    midiOutputs_.clear();
    pluginhost::sharedPluginHost().setAudioWorkgroup(nullptr); // before the device (and its workgroup) goes
    if (rt_.pool_) rt_.pool_->setWorkgroup(nullptr, 0.0);
    if (backend_) backend_->stop();
}

// --- audio device settings (M7-1) --------------------------------------------
// Setters only mutate the pending config; applyAudioConfig() persists it and
// restarts the backend so the new device/rate/buffer take effect.

void IoOps::setAudioOutputDevice(const std::string& uid) {
    config_.outputDeviceUid = uid;
}
void IoOps::setAudioInputDevice(const std::string& uid) {
    config_.inputDeviceUid = uid;
}
void IoOps::setAudioSampleRate(double sr) {
    config_.sampleRate = sr > 0.0 ? sr : 0.0;
}
void IoOps::setAudioBufferFrames(int32_t f) {
    config_.bufferFrames = f > 0 ? f : 0;
}
void IoOps::setAudioWasapiExclusive(bool on) {
    config_.wasapiExclusive = on;
}
int32_t IoOps::pollAudioDeviceEvent() {
    const int32_t ev = backend_ ? backend_->pollDeviceEvent() : 0;
    if (ev == 1 && config_.sampleRate > 0.0 && backend_->deviceChangedRate() > 0.0) {
        // The user changed the rate in the driver's panel: follow the driver from now on
        // ("device default") instead of putting the old saved rate back on reopen.
        config_.sampleRate = 0.0;
        io::audio::saveAudioConfig(config_);
    }
    if (ev == 2 && activeSystem_ == AudioSystem::Asio) {
        const char* why = backend_->fallbackReason();
        asioSkipOnce_ = why && *why ? why : "the driver stopped sending audio";
    }
    return ev;
}

void IoOps::setAudioSystem(AudioSystem system) {
    if (io::audio::asioSupported()) config_.audioSystem = system;
}
void IoOps::setAsioDriver(const std::string& clsid, const std::string& name) {
    if (!io::audio::asioSupported()) return;
    config_.asioDriverClsid = clsid;
    config_.asioDriverName = name;
}
bool IoOps::setOutputRouting(const OutputRouting& r) {
    if (!dsp::validRouting(r)) return false;
    outputRouting_ = r;
    outputRoutingSet_ = true;
    if (backend_) backend_->setOutputRouting(r);
    return true;
}

int32_t IoOps::outputChannelCount() const {
    if (outputTestChannels_ > 0) return outputTestChannels_;
    return backend_ ? std::min(backend_->outputChannelCount(), io::audio::kMaxOutputChannels) : 0;
}

std::string IoOps::outputChannelName(int32_t index) const {
    if (outputTestChannels_ > 0)
        return index >= 0 && index < outputTestChannels_ ? "Test " + std::to_string(index + 1) : std::string{};
    return backend_ ? backend_->outputChannelName(index) : std::string{};
}

int32_t IoOps::outputRoutingState() const {
    return dsp::resolveRouting(outputRouting_, outputChannelCount()).flags;
}

void IoOps::setAsioPair(bool input, int32_t firstChannel) {
    if (!io::audio::asioSupported()) return;
    (input ? config_.asioInputPair : config_.asioOutputPair) = std::max(0, firstChannel);
}

bool IoOps::applyAudioConfig() {
    io::audio::saveAudioConfig(config_);
    stop();
    // Drop the cached input unit so the next capture re-opens the chosen device.
    if (rt_.input_) rt_.input_.reset();
    rt_.recorder().forgetInputChannels(); // the input device (or its system) may have changed
    const bool ok = start();
    // A rate the ASIO driver refused isn't kept: the driver's own rate runs, and the saved
    // config says "device default" so the next launch doesn't ask again (FR-007).
    if (activeSystem_ == AudioSystem::Asio && backend_->sampleRateRefused() && config_.sampleRate > 0.0) {
        config_.sampleRate = 0.0;
        io::audio::saveAudioConfig(config_);
    }
    // Live monitoring held the old capture device open: reopen on the chosen one.
    rt_.hwRecordTap_.store(false, std::memory_order_relaxed);
    rt_.hwMonitorTap_.store(false, std::memory_order_relaxed);
    rt_.hwMonitorWanted_ = false;
    rt_.recorder().updateMonitorInput();
    return ok;
}

// --- MIDI device settings (M7-2) ---------------------------------------------

void IoOps::setMidiInputEnabled(const std::string& uid, bool enabled) {
    auto& disabled = midiConfig_.disabledInputUids;
    auto it = std::find(disabled.begin(), disabled.end(), uid);
    if (enabled) {
        if (it != disabled.end()) disabled.erase(it);
    } else {
        if (it == disabled.end()) disabled.push_back(uid);
    }
}

bool IoOps::midiInputEnabled(const std::string& uid) const {
    const auto& disabled = midiConfig_.disabledInputUids;
    return std::find(disabled.begin(), disabled.end(), uid) == disabled.end();
}

bool IoOps::applyMidiConfig() {
    io::midi::saveMidiConfig(midiConfig_);
    // Reconnect the port with the new selection; audio keeps running (the live
    // MIDI queue is untouched), so no backend restart is needed.
    midiInput_->close();
    openMidiInput();
    return true;
}

// --- audio preview / audition (M7-4a) ----------------------------------------

bool IoOps::previewFile(const std::string& path, double startSeconds) {
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

void IoOps::stopPreview() {
    previewActive_.store(false, std::memory_order_release);
    reapPreview();
}

void IoOps::publishPreview(std::shared_ptr<SampleBuffer> buf) {
    auto old = std::move(previewHold_);
    previewHold_ = std::move(buf);
    previewLive_.store(previewHold_.get(), std::memory_order_release);
    // Stamped after the swap: a render that starts from here on reads the new buffer.
    if (old) previewRetired_.emplace_back(previewEpoch_.load(std::memory_order_acquire), std::move(old));
    reapPreview();
}

void IoOps::reapPreview() {
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

void IoOps::auditionStore(const std::string& key, std::shared_ptr<SampleBuffer> buf) {
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

bool IoOps::auditionCached(const std::string& key) const {
    for (const auto& e : auditionCache_)
        if (e.first == key) return true;
    return false;
}

bool IoOps::previewCached(const std::string& key, double startSeconds) {
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

int32_t IoOps::previewScope(float* out, int32_t n) const {
    if (!out || n <= 0) return 0;
    n = std::min(n, kPreviewScope);
    const int64_t w = previewScopeW_.load(std::memory_order_acquire);
    for (int32_t i = 0; i < n; ++i)
        out[i] = previewScope_[(w - n + i) & (kPreviewScope - 1)].load(std::memory_order_relaxed);
    return n;
}

// Audio thread: mix the audition voice into `out` at its natural pitch.
void IoOps::renderPreview(float* out, int32_t numFrames) {
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

    const double sr = rt_.transport_.sampleRate();
    const double ratio = sr > 0 ? sb->sourceSampleRate / sr : 1.0; // source frames per device frame
    const bool loop = previewLoop_.load(std::memory_order_relaxed);
    const float g0 = previewGainCur_, dg = numFrames > 0 ? (target - g0) / static_cast<float>(numFrames) : 0.0f;
    const int64_t scopeW = previewScopeW_.load(std::memory_order_relaxed);
    int64_t scopeN = 0;
    for (int32_t i = 0; i < numFrames; ++i) {
        if (loop && previewPos_ >= static_cast<double>(sb->frames)) previewPos_ -= static_cast<double>(sb->frames);
        const int64_t i0 = static_cast<int64_t>(previewPos_);
        if (i0 < 0 || i0 >= sb->frames) {
            previewActive_.store(false, std::memory_order_release);
            break;
        }
        const double frac = previewPos_ - static_cast<double>(i0);
        float l0, r0, l1, r1;
        sb->readStereo(i0, l0, r0);
        // Looping, the last frame interpolates into the first instead of into silence.
        sb->readStereo(loop && i0 + 1 >= sb->frames ? 0 : i0 + 1, l1, r1);
        const float g = g0 + dg * static_cast<float>(i);
        const float l = g * static_cast<float>(l0 + (l1 - l0) * frac);
        const float r = g * static_cast<float>(r0 + (r1 - r0) * frac);
        out[i * 2] += l;
        out[i * 2 + 1] += r;
        previewScope_[static_cast<size_t>((scopeW + i) & (kPreviewScope - 1))].store(
            0.5f * (l + r), std::memory_order_relaxed);
        ++scopeN;
        previewPos_ += ratio;
    }
    previewScopeW_.store(scopeW + scopeN, std::memory_order_release);
    previewGainCur_ = target;
    previewPosSec_.store(
        sb->sourceSampleRate > 0 ? previewPos_ / sb->sourceSampleRate : 0.0, std::memory_order_relaxed);
}

bool IoOps::xrunSelfTest() {
    const int32_t before = rt_.xrunCount();
    rt_.notifyXrun();
    return rt_.xrunCount() == before + 1;
}

bool IoOps::previewSelfTest() {
    // Synthetic 0.1 s stereo tone, previewed and rendered offline (no device).
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = 2;
    buf->sourceSampleRate = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    buf->frames = static_cast<int64_t>(buf->sourceSampleRate * 0.1);
    buf->samples.assign(static_cast<size_t>(buf->frames) * 2, 0.25f);
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
        for (float v : out) sum += v * static_cast<double>(v);
        return std::sqrt(sum / static_cast<double>(out.size()));
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

} // namespace nota::detail
