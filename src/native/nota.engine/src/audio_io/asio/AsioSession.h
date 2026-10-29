// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AsioSession — the one open ASIO driver (specs/049, research R2/R4). It backs both engine
// interfaces on Windows (AsioBackend for output, AsioInput for capture), so recording and
// playback share the driver's single duplex callback and clock. Platform-independent: it
// talks to the driver through AsioDriverApi, so FakeAsioDriver tests it on every platform.
//
// State machine (data-model.md): Closed -> (open) Prepared -> (start) Running; any failure
// -> Failed(reason) with the driver released. Control calls (open/start/stop/close/poll)
// come from one non-real-time thread. The buffer switch is the audio thread: it converts
// the chosen input pair, calls the input callback, renders, converts the result to the
// chosen output pair and zeroes every other output — no allocation, locks or I/O (AR-6).
// asioMessage only sets atomics; the app learns of resets / failures by polling.

#pragma once

#include "AsioDriverApi.h"
#include "audio_io/AudioBackend.h"
#include "audio_io/AudioInput.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace nota::asio {

class AsioSession {
  public:
    enum class State { Closed, Prepared, Running, Failed };
    enum class Failure { None, NotInstalled, LoadFailed, InitFailed, UnsupportedFormat, NoOutputs, Timeout, Stopped };
    // Values of NotaAudioDeviceEvent in the C ABI.
    enum DeviceEvent : int32_t { kEventNone = 0, kEventResetRequested = 1, kEventDriverFailed = 2 };

    struct Options {
        double sampleRate = 0.0; // 0 = keep the driver's rate
        int32_t outputPair = 0;  // first channel of the output pair
        int32_t inputPair = 0;   // first channel of the input pair
    };

    AsioSession() = default;
    ~AsioSession();
    AsioSession(const AsioSession&) = delete;
    AsioSession& operator=(const AsioSession&) = delete;

    // Take a created (loaded) driver and prepare it: init, sample rate, channels, buffers.
    // Prepared on success; Failed (driver released) otherwise.
    bool open(std::unique_ptr<AsioDriverApi> driver, const Options& opt);
    // Record a failure that happened before a driver existed (not installed / load / timeout).
    void fail(Failure why, std::string detail = {});

    // Set before start() (AR-6: never swapped while the driver calls back).
    void setRenderCallback(RenderCallback cb);
    void setXrunCallback(std::function<void()> cb);
    // Register (or, with nullptr, unregister) the capture callback; allowed while running.
    // The pointee must outlive the registration; unregistering waits out a switch in flight
    // and returns false if one never finished — then the pointee must be kept alive for good.
    bool setInputCallback(const InputCallback* cb);

    bool start();
    void stop();
    void close(); // stop, dispose the buffers and release the driver (FR-012)

    State state() const { return state_; }
    bool isRunning() const { return state_ == State::Running; }
    Failure failure() const { return failure_; }
    std::string failureText() const; // human reason, "" without a failure
    const std::string& driverName() const { return driverName_; }

    double sampleRate() const { return sampleRate_; }
    int32_t bufferFrames() const { return bufferFrames_; }
    int32_t inputLatency() const { return inputLatency_; }
    int32_t outputLatency() const { return outputLatency_; }
    bool sampleRateRefused() const { return rateRefused_; } // a requested rate the driver refused
    // The driver now runs at a different rate than the session opened with — set in the
    // driver's own panel. Read when a reset is reported; 0 if the rate didn't change.
    double driverChangedRate() const { return driverRate_; }
    bool hasInputs() const { return inL_ >= 0; }
    // A buffer switch never returned (a stalled plug-in or driver): its state is kept alive
    // and no driver opens again in this process.
    bool wedged() const { return wedged_; }
    const std::vector<ChannelInfo>& channels(bool input) const { return input ? inChannels_ : outChannels_; }
    int32_t outputPair() const { return outL_; }
    int32_t inputPair() const { return inL_; }

    bool openControlPanel();
    bool panelOpen() const { return driver_ && driver_->panelOpen(); }

    // Device event since the last call (cleared on read). Also runs the watchdog: a running
    // driver silent for longer than the timeout moves to Failed(Stopped).
    int32_t pollEvent();
    void setWatchdogTimeout(std::chrono::milliseconds t) { watchdog_ = t; }

    // Driver callbacks (routed here from the static trampolines).
    void bufferSwitch(int32_t index, int64_t samplePosition);
    void sampleRateChanged(double rate);
    int32_t message(int32_t selector, int32_t value);

  private:
    // Everything the buffer switch reads, built by open() and handed to the audio thread by
    // start(). Teardown takes it back only once no switch is running; a switch that never
    // returns keeps it (and the driver) alive for good rather than freeing memory under it.
    struct Io {
        AsioDriverApi* driver = nullptr;
        int32_t frames = 0;
        bool outputReady = false;
        std::vector<BufferInfo> buffers; // every output channel, then the chosen inputs
        std::vector<SampleType> outTypes;
        SampleType inLType = SampleType::Float32LSB, inRType = SampleType::Float32LSB;
        int32_t outL = -1, outR = -1;     // output channels = their indices in buffers; R -1 = mono
        int32_t inLBuf = -1, inRBuf = -1; // indices into buffers (-1 = none / mono)
        std::vector<float> interleaved;   // `frames` stereo frames
        RenderCallback render;            // copied at start(): never swapped under the switch
    };

    void releaseDriver();
    bool failOpen(Failure why, std::string detail);
    bool quiesce(); // detach the audio thread from io_; false if a switch never finished

    std::unique_ptr<AsioDriverApi> driver_;
    std::unique_ptr<Io> io_;
    std::atomic<Io*> live_{nullptr}; // non-null while the buffer switch may run
    bool wedged_ = false;
    State state_ = State::Closed;
    Failure failure_ = Failure::None;
    std::string failureDetail_;
    std::string driverName_;
    bool buffersCreated_ = false;
    bool started_ = false; // driver_->start() succeeded and stop() is still owed

    double sampleRate_ = 0.0;
    int32_t bufferFrames_ = 0;
    int32_t inputLatency_ = 0;
    int32_t outputLatency_ = 0;
    bool rateRefused_ = false;
    double driverRate_ = 0.0; // see driverChangedRate()

    std::vector<ChannelInfo> inChannels_, outChannels_; // for the UI (control side only)
    int32_t outL_ = -1, outR_ = -1;                     // output channel indices (R -1 = mono)
    int32_t inL_ = -1, inR_ = -1;                       // input channel indices (-1 = none / mono)

    RenderCallback render_;
    std::function<void()> xrun_;
    std::atomic<const InputCallback*> input_{nullptr};

    std::atomic<bool> inSwitch_{false};
    std::atomic<bool> resetRequested_{false};
    std::atomic<int64_t> lastTickNs_{0};
    int64_t lastPosition_ = -1; // audio thread only
    std::chrono::milliseconds watchdog_{2000};
};

} // namespace nota::asio
