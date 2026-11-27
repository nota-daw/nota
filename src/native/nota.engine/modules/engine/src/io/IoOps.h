// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IoOps — audio backend lifecycle, audio/MIDI device config, output routing,
// control surfaces, MPE input decoding, gamepad input, and preview/audition.

#pragma once

#include "core/EnginePrelude.h"
#include "io/MidiSurface.h"
#include "model/EngineTypes.h"
#include "recording/RecordingTypes.h"
#include "render/RenderTypes.h"
#include "nota/dsp/rt/OutputRouting.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/IAudioOutputStream.h"
#include "nota/io/gamepad/GamepadInput.h"
#include "nota/io/midi/IMidiInput.h"
#include "nota/io/midi/MpeInput.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <list>
#include <map>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

class EngineRuntime;

class IoOps {
    friend class EngineRuntime;
    friend class RecordingOps;

  public:
    explicit IoOps(EngineRuntime& rt);

    IoOps(const IoOps&) = delete;
    IoOps& operator=(const IoOps&) = delete;

    // --- audio device lifecycle & routing -----------------------------------
    bool start();
    void stop();
    double sampleRate() const { return backend_ ? backend_->sampleRate() : 0.0; }
    bool isRunning() const { return backend_ && backend_->isRunning(); }

    bool setOutputRouting(const OutputRouting& r);
    const OutputRouting& outputRouting() const { return outputRouting_; }
    int32_t outputChannelCount() const;
    std::string outputChannelName(int32_t index) const;
    int32_t outputRoutingState() const;
    void setTestOutputDevice(int32_t channels) { outputTestChannels_ = std::max(0, channels); }

    const AudioConfig& audioConfig() const { return config_; }
    void setAudioOutputDevice(const std::string& uid);
    void setAudioInputDevice(const std::string& uid);
    void setAudioSampleRate(double sr);
    void setAudioBufferFrames(int32_t frames);
    void setAudioWasapiExclusive(bool on);
    bool applyAudioConfig();
    bool audioExclusiveFallback() const { return backend_ ? backend_->exclusiveFallback() : false; }
    bool audioSilentFallback() const { return backend_ ? backend_->silentFallback() : false; }
    int32_t bufferFrames() const { return backend_ ? backend_->bufferFrames() : 0; }

    void setAudioSystem(AudioSystem system);
    void setAsioDriver(const std::string& clsid, const std::string& name);
    void setAsioPair(bool input, int32_t firstChannel);
    AudioSystem activeAudioSystem() const { return activeSystem_; }
    const std::string& audioFallbackReason() const { return audioFallbackReason_; }
    int32_t pollAudioDeviceEvent();
    int32_t audioInputLatencyFrames() const { return backend_ ? backend_->inputLatencyFrames() : 0; }
    int32_t audioOutputLatencyFrames() const { return backend_ ? backend_->outputLatencyFrames() : 0; }
    int32_t driverChannelCount(bool isInput) const { return backend_ ? backend_->driverChannelCount(isInput) : 0; }
    std::string driverChannelName(bool isInput, int32_t index) const {
        return backend_ ? backend_->driverChannelName(isInput, index) : std::string{};
    }
    bool openDriverControlPanel() { return backend_ && backend_->openControlPanel(); }
    bool isDriverControlPanelOpen() const { return backend_ && backend_->isControlPanelOpen(); }

    // --- MIDI device settings & surfaces ------------------------------------
    const MidiConfig& midiConfig() const { return midiConfig_; }
    void setMidiInputEnabled(const std::string& uid, bool enabled);
    bool midiInputEnabled(const std::string& uid) const;
    void setMpe(bool enabled, int32_t bendRange) {
        midiConfig_.mpe = enabled;
        midiConfig_.mpeBendRange = std::clamp(bendRange, 1, 96);
    }
    bool applyMidiConfig();

    int32_t pollControlEvents(ControlEvent* out, int32_t max);
    const std::vector<MidiInput::PortInfo>& midiPorts();
    const std::vector<MidiInput::PortInfo>& midiPortRows() const { return midiPortRows_; }
    int32_t midiSourceOfUid(const std::string& uid) const;
    bool setMidiClaims(const MidiClaimEntry* claims, int32_t count) { return midiSurface_.setClaims(claims, count); }
    void setMidiLearnListening(bool on) { midiSurface_.setLearnListening(on); }
    int32_t pollSurfaceEvents(SurfaceEvent* out, int32_t max) { return midiSurface_.pollEvents(out, max); }
    uint32_t surfaceDropped() const { return midiSurface_.dropped(); }
    int32_t pollMidiSysex(int32_t* outSource, uint8_t* out, int32_t cap) {
        return midiSurface_.pollSysex(outSource, out, cap);
    }
    bool midiSend(const std::string& outputUid, const uint8_t* data, int32_t len);
    int32_t addTestMidiPort(const std::string& name);
    bool injectTestMidi(int32_t source, const uint8_t* data, int32_t len);
    int32_t takeTestMidiSent(int32_t source, uint8_t* out, int32_t cap);
    void pushMidiForTest(int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2) {
        onMidiMessage(source, status, channel, d1, d2);
    }

    // --- gamepad input ------------------------------------------------------
    void startGamepadInput();
    void stopGamepadInput();
    int32_t gamepadCount() const;
    void gamepadInfo(int32_t i, const char** outUid, const char** outName) const;
    int32_t pollGamepadEvents(GamepadInput::ButtonEvent* out, int32_t max);
    int32_t gamepadAxisValues(int32_t pad, int32_t* out, int32_t max) const;

    // --- preview & audition (M7-4a) -----------------------------------------
    bool previewFile(const std::string& path, double startSeconds = 0.0);
    void stopPreview();
    void setPreviewLoop(bool on) { previewLoop_.store(on, std::memory_order_relaxed); }
    void setPreviewGain(float g) { previewGain_.store(g < 0.0f ? 0.0f : g, std::memory_order_relaxed); }
    double previewPosition() const { return previewPosSec_.load(std::memory_order_relaxed); }
    bool isPreviewActive() const { return previewActive_.load(std::memory_order_relaxed); }
    bool previewSelfTest();
    void auditionStore(const std::string& key, std::shared_ptr<SampleBuffer> buf);
    bool auditionCached(const std::string& key) const;
    bool previewCached(const std::string& key, double startSeconds);
    int32_t previewScope(float* out, int32_t n) const;
    bool xrunSelfTest();

  private:
    struct MpeSink final : MpeInput::Sink {
        EngineRuntime* e = nullptr;
        int32_t source = -1, channel = 0;
        void mpeNoteOn(int32_t pitch, float velocity) override;
        void mpeNoteOff(int32_t pitch) override;
        void mpeNoteOffVelocity(int32_t pitch, float velocity) override;
        void mpeExpression(int32_t pitch, int32_t dim, float value) override;
        void mpeController(int32_t ctl, float value) override;
    };

    void openMidiInput();
    void onMidiMessage(int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2);
    void onMidiSysex(int32_t source, const uint8_t* data, int32_t len);
    void renderPreview(float* out, int32_t numFrames);
    void publishPreview(std::shared_ptr<SampleBuffer> buf);
    void reapPreview();

    EngineRuntime& rt_;

    std::array<MpeSink, MidiInput::kMaxSources> mpeSinks_;
    std::vector<std::string> midiSourceUids_;
    std::array<MpeInput, MidiInput::kMaxSources> mpeInputs_;
    MidiSurface midiSurface_;
    std::map<std::string, std::unique_ptr<MidiOutput>> midiOutputs_;
    std::vector<std::unique_ptr<TestMidiPort>> testMidiPorts_;
    std::vector<MidiInput::PortInfo> midiPortRows_;

    std::unique_ptr<AudioBackend> backend_;
    AudioConfig config_;
    AudioSystem activeSystem_ = AudioSystem::Wasapi;
    std::string audioFallbackReason_;
    std::string asioSkipOnce_;
    MidiConfig midiConfig_;
    std::unique_ptr<MidiInput> midiInput_;
    std::unique_ptr<GamepadInput> gamepadInput_;

    OutputRouting outputRouting_;
    bool outputRoutingSet_ = false;
    int32_t outputTestChannels_ = 0;

    std::shared_ptr<SampleBuffer> previewHold_;
    std::vector<std::pair<uint64_t, std::shared_ptr<SampleBuffer>>> previewRetired_;
    std::atomic<uint64_t> previewEpoch_{0};
    std::atomic<SampleBuffer*> previewLive_{nullptr};
    std::atomic<bool> previewActive_{false};
    std::atomic<bool> previewRestart_{false};
    std::atomic<double> previewStartSec_{0.0};
    std::atomic<bool> previewLoop_{false};
    std::atomic<float> previewGain_{1.0f};
    std::atomic<double> previewPosSec_{0.0};
    std::string previewPath_;
    double previewPos_ = 0.0;
    float previewGainCur_ = 1.0f;
    std::atomic<float> previewScope_[kPreviewScope] = {};
    std::atomic<int64_t> previewScopeW_{0};
    std::list<std::pair<std::string, std::shared_ptr<SampleBuffer>>> auditionCache_;
};

} // namespace nota::detail
