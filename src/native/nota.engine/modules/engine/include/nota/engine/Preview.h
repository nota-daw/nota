// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota::media {
struct SampleBuffer;
} // namespace nota::media

namespace nota::engine {

class NOTA_ENGINE_CORE_API AuditionSession {
  public:
    static constexpr int32_t kRolling = 1;

    explicit AuditionSession(double sampleRate);
    ~AuditionSession();

    AuditionSession(const AuditionSession&) = delete;
    AuditionSession& operator=(const AuditionSession&) = delete;
    AuditionSession(AuditionSession&&) noexcept;
    AuditionSession& operator=(AuditionSession&&) noexcept;

    bool setInstrument(int32_t kind);
    bool instrumentParam(const std::string& id, float value);
    int32_t addDevice(int32_t kind);
    bool deviceParam(int32_t index, const std::string& name, float value);
    int32_t addMidiEffect(int32_t kind);
    bool midiParam(int32_t index, const std::string& name, float value);
    bool setSamplerSample(const std::string& path, int32_t rootNote = 60);
    bool setMosaicProgram(const std::string& text, int32_t timeoutMs = 20000);
    bool instrumentState(const uint8_t* data, int32_t size);
    bool setSourceFile(const std::string& path, double maxSeconds);
    bool addSourceFrom(const AuditionSession& part, float gain);
    bool useCachedSource(const std::string& key);
    bool cacheSource(const std::string& key, float targetPeak);

    bool useKit();
    int32_t kitAddPad(int32_t note, const std::string& path, float gain, float pan, int32_t choke);
    int32_t kitPadAddDevice(int32_t pad, int32_t kind);
    bool kitPadDeviceParam(int32_t pad, int32_t device, const std::string& name, float value);

    int64_t render(const AuditionNoteSpec* notes, int32_t count, double bpm, double phraseBeats, double maxTailSeconds,
        int32_t flags);
    void cancel();

    int32_t peaks(float* outMinMax, int32_t maxPoints) const;
    double seconds() const;
    std::shared_ptr<media::SampleBuffer> result() const;

  private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace nota::engine

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Preview {
  public:
    Preview() = default;
    explicit Preview(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    bool file(const std::string& path, double startSeconds = 0.0);
    void stop();
    void setLoop(bool on);
    void setGain(float gain);
    double position() const;
    bool active() const;
    int32_t scope(float* out, int32_t count) const;

    void storeAudition(const std::string& key, const engine::AuditionSession& session);
    void storeAudition(const std::string& key, std::shared_ptr<media::SampleBuffer> buf);
    bool cachedAudition(const std::string& key) const;
    bool playCached(const std::string& key, double startSeconds);

    void setAuditionTrack(int32_t trackId);
    void setToneEnabled(bool enabled);
    void setToneFrequency(float hz);
    bool selfTest();

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
