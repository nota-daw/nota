// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AsioBackend.h"

#if defined(_WIN32)

#include "AsioDriverList.h"
#include "ComAsioDriver.h"

namespace nota::asio {

AsioSession& sharedAsioSession() {
    // Never destroyed: releasing a driver joins its COM thread, which must not happen during
    // DLL unload. The engine closes the session when it stops the backend.
    static AsioSession* session = new AsioSession();
    return *session;
}

} // namespace nota::asio

namespace nota {

using asio::AsioSession;
using asio::sharedAsioSession;

AsioBackend::~AsioBackend() {
    stop();
}

bool AsioBackend::start(RenderCallback render, const BackendConfig& cfg) {
    AsioSession& s = sharedAsioSession();
    s.close();
    reason_.clear();

    const auto drivers = asio::enumerateAsioDrivers();
    const asio::AsioDriverInfo* d = asio::findAsioDriver(drivers, cfg.asioDriverClsid, cfg.asioDriverName);
    if (!d && cfg.asioDriverClsid.empty() && cfg.asioDriverName.empty() && !drivers.empty())
        d = &drivers.front(); // ASIO chosen but no driver yet: the first installed one
    if (!d) {
        s.fail(AsioSession::Failure::NotInstalled);
        reason_ = s.failureText();
        return false;
    }

    bool timedOut = false;
    std::string error;
    auto driver = asio::ComAsioDriver::create(d->clsid, timedOut, error);
    if (!driver) {
        s.fail(timedOut ? AsioSession::Failure::Timeout : AsioSession::Failure::LoadFailed, error);
        reason_ = s.failureText();
        return false;
    }

    s.setRenderCallback(std::move(render));
    s.setXrunCallback(xrun_);
    AsioSession::Options opt;
    opt.sampleRate = cfg.sampleRate;
    opt.outputPair = cfg.asioOutputPair;
    opt.inputPair = cfg.asioInputPair;
    if (!s.open(std::move(driver), opt) || !s.start()) {
        reason_ = s.failureText();
        s.close();
        return false;
    }
    return true;
}

void AsioBackend::stop() {
    AsioSession& s = sharedAsioSession();
    if (s.state() != AsioSession::State::Closed) s.close();
}

double AsioBackend::sampleRate() const {
    return sharedAsioSession().sampleRate();
}

int32_t AsioBackend::bufferFrames() const {
    return sharedAsioSession().bufferFrames();
}

bool AsioBackend::isRunning() const {
    return sharedAsioSession().isRunning();
}

int32_t AsioBackend::inputLatencyFrames() const {
    return sharedAsioSession().inputLatency();
}

int32_t AsioBackend::outputLatencyFrames() const {
    return sharedAsioSession().outputLatency();
}

int32_t AsioBackend::pollDeviceEvent() {
    AsioSession& s = sharedAsioSession();
    const int32_t ev = s.pollEvent();
    if (ev == AsioSession::kEventDriverFailed) reason_ = s.failureText(); // for the engine's fallback
    return ev;
}

double AsioBackend::deviceChangedRate() const {
    return sharedAsioSession().driverChangedRate();
}

bool AsioBackend::sampleRateRefused() const {
    return sharedAsioSession().sampleRateRefused();
}

} // namespace nota

#endif // defined(_WIN32)
