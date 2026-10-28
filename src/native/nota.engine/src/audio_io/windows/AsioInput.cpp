// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AsioInput.h"

#if defined(_WIN32)

#include "AsioBackend.h"

namespace nota {

AsioInput::~AsioInput() {
    stop();
}

bool AsioInput::start(InputCallback cb, uint32_t) {
    if (registered_) return isRunning();
    auto& s = asio::sharedAsioSession();
    if (!s.isRunning() || !s.hasInputs()) return false; // no driver, or one without inputs
    cb_ = new InputCallback(std::move(cb));
    s.setInputCallback(cb_);
    registered_ = true;
    return true;
}

void AsioInput::stop() {
    if (!registered_) return;
    if (asio::sharedAsioSession().setInputCallback(nullptr)) delete cb_; // else a switch may still call it
    registered_ = false;
    cb_ = nullptr;
}

bool AsioInput::isRunning() const {
    return registered_ && asio::sharedAsioSession().isRunning();
}

double AsioInput::sampleRate() const {
    return asio::sharedAsioSession().sampleRate();
}

} // namespace nota

#endif // defined(_WIN32)
