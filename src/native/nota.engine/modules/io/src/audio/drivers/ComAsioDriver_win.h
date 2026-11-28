// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#if defined(_WIN32)

#include "nota/io/audio/asio/AsioDriverApi.h"

#include <future>
#include <memory>
#include <string>
#include <thread>

namespace nota::io::audio::asio {

class ComAsioDriver final : public AsioDriverApi {
  public:
    static std::unique_ptr<ComAsioDriver> create(const std::string& clsid, bool& timedOut, std::string& error);
    ~ComAsioDriver() override;

    bool init() override;
    std::string driverName() override;
    std::string errorMessage() override;
    bool getChannels(int32_t& inputs, int32_t& outputs) override;
    bool getBufferSize(int32_t& minSize, int32_t& maxSize, int32_t& preferred, int32_t& granularity) override;
    bool canSampleRate(double rate) override;
    bool getSampleRate(double& rate) override;
    bool setSampleRate(double rate) override;
    bool getChannelInfo(ChannelInfo& info) override;
    bool createBuffers(BufferInfo* infos, int32_t count, int32_t bufferSize, const Callbacks& cb) override;
    bool disposeBuffers() override;
    bool start() override;
    bool stop() override;
    bool getLatencies(int32_t& input, int32_t& output) override;
    bool controlPanel() override;
    bool outputReady() override;
    bool probeOutputReady() override;
    bool timedOut() const override;
    bool panelOpen() const override;

    struct Shared;
    struct ConstructTag {};
    explicit ComAsioDriver(ConstructTag);

  private:
    template <class F> bool call(F fn, unsigned timeoutMs);

    std::shared_ptr<Shared> sh_;
    std::future<void> exited_;
    std::thread thread_;
};

} // namespace nota::io::audio::asio

#endif
