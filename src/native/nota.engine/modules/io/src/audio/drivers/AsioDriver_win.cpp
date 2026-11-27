// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if defined(NOTA_HAS_ASIO) && defined(_WIN32)

#include "audio/drivers/AudioDrivers.h"
#include "audio/drivers/ComAsioDriver_win.h"
#include "nota/io/audio/asio/AsioDriverList.h"
#include "nota/io/audio/asio/AsioSession.h"

#include <algorithm>
#include <cstddef>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::audio {
namespace {

class AsioInputStream final : public IAudioInputStream {
  public:
    explicit AsioInputStream(std::shared_ptr<asio::AsioSession> session) : session_(std::move(session)) {}
    ~AsioInputStream() override { stop(); }

    bool start(InputCallback cb, uint32_t, double) override {
        if (registered_) return isRunning();
        if (!session_ || !session_->isRunning() || !session_->hasInputs()) return false;
        cb_ = new InputCallback(std::move(cb));
        session_->setInputCallback(cb_);
        registered_ = true;
        return true;
    }

    void stop() override {
        if (!registered_) return;
        if (session_ && session_->setInputCallback(nullptr)) { delete cb_; }
        registered_ = false;
        cb_ = nullptr;
    }

    bool isRunning() const override { return registered_ && session_ && session_->isRunning(); }
    double sampleRate() const override { return session_ ? session_->sampleRate() : 0.0; }
    int32_t channelCount() const override { return isRunning() ? session_->inputCount() : 0; }
    std::string channelName(int32_t index) const override {
        if (!session_) return {};
        const auto& ins = session_->channels(true);
        if (index < 0 || index >= static_cast<int32_t>(ins.size())) return {};
        return ins[static_cast<std::size_t>(index)].name;
    }

  private:
    std::shared_ptr<asio::AsioSession> session_;
    InputCallback* cb_ = nullptr;
    bool registered_ = false;
};

class AsioOutputStream final : public IAudioOutputStream {
  public:
    AsioOutputStream()
        : session_(new asio::AsioSession(), [](asio::AsioSession* s) {
              // Intentionally avoid joining a stuck COM thread at DLL unload if still open;
              // Engine explicitly closes the stream before shutdown.
              if (s->state() == asio::AsioSession::State::Closed || s->state() == asio::AsioSession::State::Failed) {
                  delete s;
              }
          }) {}

    ~AsioOutputStream() override { stop(); }

    bool start(RenderCallback render, const OutputStreamConfig& cfg) override {
        session_->close();
        reason_.clear();

        const auto drivers = asio::enumerateAsioDrivers();
        const asio::AsioDriverInfo* d = asio::findAsioDriver(drivers, cfg.asioDriverClsid, cfg.asioDriverName);
        if (!d && cfg.asioDriverClsid.empty() && cfg.asioDriverName.empty() && !drivers.empty()) {
            d = &drivers.front();
        }
        if (!d) {
            session_->fail(asio::AsioSession::Failure::NotInstalled);
            reason_ = session_->failureText();
            return false;
        }

        bool timedOut = false;
        std::string error;
        auto driver = asio::ComAsioDriver::create(d->clsid, timedOut, error);
        if (!driver) {
            session_->fail(
                timedOut ? asio::AsioSession::Failure::Timeout : asio::AsioSession::Failure::LoadFailed, error);
            reason_ = session_->failureText();
            return false;
        }

        session_->setRenderCallback(std::move(render));
        session_->setXrunCallback(xrun_);
        asio::AsioSession::Options opt;
        opt.sampleRate = cfg.sampleRate;
        opt.outputPair = cfg.asioOutputPair;
        opt.inputPair = cfg.asioInputPair;
        if (!session_->open(std::move(driver), opt) || !session_->start()) {
            reason_ = session_->failureText();
            session_->close();
            return false;
        }
        return true;
    }

    void stop() override {
        if (session_ && session_->state() != asio::AsioSession::State::Closed) session_->close();
    }

    double sampleRate() const override { return session_->sampleRate(); }
    int32_t bufferFrames() const override { return session_->bufferFrames(); }
    bool isRunning() const override { return session_->isRunning(); }
    void setXrunCallback(XrunCallback cb) override { xrun_ = std::move(cb); }

    int32_t inputLatencyFrames() const override { return session_->inputLatency(); }
    int32_t outputLatencyFrames() const override { return session_->outputLatency(); }
    bool sharesInputClock() const override { return true; }
    int32_t pollDeviceEvent() override {
        const int32_t ev = session_->pollEvent();
        if (ev == asio::AsioSession::kEventDriverFailed) reason_ = session_->failureText();
        return ev;
    }
    const char* fallbackReason() const override { return reason_.c_str(); }
    bool sampleRateRefused() const override { return session_->sampleRateRefused(); }
    double deviceChangedRate() const override { return session_->driverChangedRate(); }

    int32_t outputChannelCount() const override {
        return std::min<int32_t>(static_cast<int32_t>(session_->channels(false).size()), kMaxOutputChannels);
    }
    std::string outputChannelName(int32_t index) const override {
        const auto& ch = session_->channels(false);
        if (index < 0 || index >= static_cast<int32_t>(ch.size())) return {};
        return ch[static_cast<std::size_t>(index)].name;
    }
    void setOutputRouting(const OutputRouting& r) override { session_->setOutputRouting(r); }

    std::unique_ptr<IAudioInputStream> createCoupledInputStream() override {
        return std::make_unique<AsioInputStream>(session_);
    }

    int32_t driverChannelCount(bool isInput) const override {
        return static_cast<int32_t>(session_->channels(isInput).size());
    }
    std::string driverChannelName(bool isInput, int32_t index) const override {
        const auto& ch = session_->channels(isInput);
        if (index < 0 || index >= static_cast<int32_t>(ch.size())) return {};
        return ch[static_cast<std::size_t>(index)].name;
    }
    bool openControlPanel() override { return session_->openControlPanel(); }
    bool isControlPanelOpen() const override { return session_->panelOpen(); }

  private:
    std::shared_ptr<asio::AsioSession> session_;
    XrunCallback xrun_;
    std::string reason_;
};

class AsioDriver final : public IAudioDriver {
  public:
    std::string_view id() const noexcept override { return "asio"; }
    std::string_view displayName() const noexcept override { return "ASIO"; }
    DriverCapabilities capabilities() const noexcept override {
        return DriverCapabilities{true, true, true, false, true};
    }

    std::vector<AudioDeviceInfo> enumerateDevices(bool /*inputScope*/) const override {
        std::vector<AudioDeviceInfo> out;
        for (const auto& d : asio::enumerateAsioDrivers()) { out.push_back(AudioDeviceInfo{d.clsid, d.displayName}); }
        return out;
    }

    uint32_t resolveDeviceId(std::string_view, bool) const override { return 0u; }
    std::string effectiveDeviceUid(std::string_view, bool) const override { return {}; }

    std::unique_ptr<IAudioOutputStream> createOutputStream() const override {
        return std::make_unique<AsioOutputStream>();
    }

    std::unique_ptr<IAudioInputStream> createInputStream() const override {
        // ASIO input always shares the active ASIO output driver's session via createCoupledInputStream().
        return nullptr;
    }
};

} // namespace

namespace detail {

std::shared_ptr<const IAudioDriver> createAsioDriver() {
    return std::make_shared<AsioDriver>();
}

} // namespace detail
} // namespace nota::io::audio

#endif
