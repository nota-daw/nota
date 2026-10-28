// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ComAsioDriver — AsioDriverApi over a real ASIO driver's COM interface (IASIO, from the
// SDK's iasiodrv.h; specs/049, research R3/R4). The driver object lives on a dedicated
// control thread (COINIT_APARTMENTTHREADED, pumping messages for the driver's own windows):
// every call is posted there and waited on with a timeout — 5 s to create / init, 2 s
// otherwise — so a hung driver can't hang Nota (SC-005). After a timeout the driver is
// abandoned: every later call fails fast and the thread is left to the driver.
// outputReady() is the exception: ASIO calls it from inside the buffer switch.

#pragma once

#include "audio_io/asio/AsioDriverApi.h"

#include <future>
#include <memory>
#include <string>
#include <thread>

namespace nota::asio {

class ComAsioDriver final : public AsioDriverApi {
  public:
    // Create the driver registered under `clsid`. nullptr on failure; `timedOut` tells a
    // hung driver from one that refused to load, `error` says why.
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
    bool timedOut() const override;
    bool panelOpen() const override;

    struct Shared; // the control thread's state; outlives this object if the thread is abandoned

  private:
    ComAsioDriver();
    template <class F> bool call(F fn, unsigned timeoutMs);

    std::shared_ptr<Shared> sh_;
    std::future<void> exited_; // ready once the control thread has released the driver
    std::thread thread_;
};

} // namespace nota::asio
