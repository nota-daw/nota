// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio device backend abstraction.
//
// M0 ships a CoreAudio implementation. The engine talks only to this interface
// so the device layer stays swappable (e.g. a future JUCE-based backend for the
// Windows/Linux ports, or JUCE for plugin hosting). See ARCHITECTURE.md § Audio engine.

#pragma once

#include <cstdint>
#include <functional>

namespace nota {

// Called from the real-time audio thread. MUST be real-time safe (AR-6):
// no allocation, no locks, no I/O. Writes `numFrames` of interleaved stereo
// float samples into `out` (2 channels).
using RenderCallback = std::function<void(float* out, int32_t numFrames)>;

// Requested device configuration (M7-1). Zero fields mean "use the device
// default": device 0 = system default output, sampleRate 0 / bufferFrames 0 =
// whatever the device is already set to. `device` is a CoreAudio AudioDeviceID
// carried as a plain integer so this header stays free of CoreAudio types.
//
// `wasapiExclusive` (Windows/WASAPI only; ignored on other platforms) asks
// miniaudio to open the device in exclusive mode. The backend MUST fall back to
// shared mode if the device refuses (busy / not allowed / not supported) and
// surface that via the start() result so the UI can explain it.
struct BackendConfig {
    uint32_t device            = 0;
    double   sampleRate        = 0.0;
    int32_t  bufferFrames      = 0;
    bool     wasapiExclusive   = false;
};

class AudioBackend {
public:
    virtual ~AudioBackend() = default;

    // Open the requested output device and begin calling `render` on the audio thread.
    // Returns true on a clean start. `exclusiveFallback()` reports whether an
    // exclusive-mode request was refused by the device and the backend fell back to
    // shared mode (so the UI can warn the user). Read after start() succeeds.
    virtual bool start(RenderCallback render, const BackendConfig& cfg) = 0;

    // True if the last start() requested exclusive mode but had to fall back to
    // shared (device busy / exclusive not supported). False otherwise. Backends
    // that don't model exclusive mode leave it false.
    virtual bool exclusiveFallback() const { return false; }

    // Stop the audio thread and close the device.
    virtual void stop() = 0;

    // Negotiated output sample rate (0 until started).
    virtual double sampleRate() const = 0;

    // Negotiated buffer size in frames (0 until started / unknown).
    virtual int32_t bufferFrames() const = 0;

    // Called (off the RT thread) when the device reports an overload/dropout
    // (M7-8). Must be lightweight — it only bumps a counter. Optional; a backend
    // without dropout signalling leaves it a no-op. Set before start().
    virtual void setXrunCallback(std::function<void()> cb) { (void)cb; }

    virtual bool isRunning() const = 0;
};

} // namespace nota
