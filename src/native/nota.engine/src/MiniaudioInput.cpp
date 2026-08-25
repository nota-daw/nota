// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "MiniaudioInput.h"
#include "MiniaudioDevices.h"
#include "nota_miniaudio.h"

namespace nota {

struct MiniaudioInput::Impl {
    ma_device      device{};
    bool           deviceInited = false;
    MiniaudioInput* owner = nullptr;
};

// miniaudio capture thread: hand the interleaved stereo float frames to the owner.
// MUST be real-time safe (no alloc/lock/IO) — the callback only pushes into a
// lock-free ring (AR-6).
static void captureCallback(ma_device* dev, void* /*pOutput*/, const void* pInput, ma_uint32 frameCount) {
    auto* impl = static_cast<MiniaudioInput::Impl*>(dev->pUserData);
    if (impl && impl->owner && impl->owner->cb_)
        impl->owner->cb_(static_cast<const float*>(pInput), static_cast<int32_t>(frameCount));
}

MiniaudioInput::MiniaudioInput() : impl_(new Impl()) { impl_->owner = this; }

MiniaudioInput::~MiniaudioInput() { stop(); delete impl_; }

bool MiniaudioInput::start(InputCallback cb, uint32_t device) {
    if (running_) return true;
    cb_ = std::move(cb);

    ma_device_id id{};
    const bool haveId = notaMiniaudioDeviceIdForIndex(/*inputScope=*/true, device, id);

    ma_device_config config = ma_device_config_init(ma_device_type_capture);
    config.capture.pDeviceID = haveId ? &id : nullptr; // NULL = system default input
    config.capture.format    = ma_format_f32;
    config.capture.channels  = 2;                       // miniaudio downmixes/converts
    config.dataCallback      = &captureCallback;
    config.pUserData         = impl_;

    if (ma_device_init(nullptr, &config, &impl_->device) != MA_SUCCESS)
        return false;
    impl_->deviceInited = true;

    if (ma_device_start(&impl_->device) != MA_SUCCESS) {
        stop();
        return false;
    }

    sampleRate_ = impl_->device.sampleRate;
    running_    = true;
    return true;
}

void MiniaudioInput::stop() {
    if (impl_->deviceInited) {
        ma_device_uninit(&impl_->device);
        impl_->deviceInited = false;
    }
    running_ = false;
}

} // namespace nota
