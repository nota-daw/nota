// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if !defined(__APPLE__)

#include "gamepad/drivers/GamepadDrivers.h"

namespace nota::io::gamepad {
namespace {

class StubGamepadDriver final : public IGamepadDriver {
  public:
    std::string_view id() const noexcept override { return "stub"; }
    std::string_view displayName() const noexcept override { return "Gamepad Stub"; }
    bool start(GamepadSink& sink) override {
        sink.onPadsChanged({});
        return true;
    }
    void stop() override {}
};

} // namespace

namespace detail {

std::unique_ptr<IGamepadDriver> createPlatformGamepadDriver() {
    return std::make_unique<StubGamepadDriver>();
}

} // namespace detail
} // namespace nota::io::gamepad

#endif
