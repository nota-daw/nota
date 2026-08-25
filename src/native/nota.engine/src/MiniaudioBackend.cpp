// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

#include "MiniaudioBackend.h"
#include "MiniaudioDevices.h"
#include "nota_miniaudio.h"

#include <cstring>

namespace nota {

struct MiniaudioBackend::Impl {
    ma_device      device{};
    bool           deviceInited = false;
    RenderCallback render;                 // copied from the backend on start()
};

// miniaudio real-time thread: fill the interleaved stereo float output buffer.
// MUST be real-time safe (no alloc/lock/IO) — same contract as CoreAudio (AR-6).
static void dataCallback(ma_device* dev, void* pOutput, const void* /*pInput*/, ma_uint32 frameCount) {
    auto* impl = static_cast<MiniaudioBackend::Impl*>(dev->pUserData);
    float* out = static_cast<float*>(pOutput);
    if (impl && impl->render)
        impl->render(out, static_cast<int32_t>(frameCount));
    else
        std::memset(pOutput, 0, static_cast<size_t>(frameCount) * 2 * sizeof(float));
}

MiniaudioBackend::MiniaudioBackend() : impl_(new Impl()) {}

MiniaudioBackend::~MiniaudioBackend() {
    stop();
    delete impl_;
}

bool MiniaudioBackend::start(RenderCallback render, const BackendConfig& cfg) {
    if (running_) return true;
    impl_->render = std::move(render);
    exclusiveFallback_ = false;

    ma_device_id id{};
    const bool haveId = notaMiniaudioDeviceIdForIndex(/*inputScope=*/false, cfg.device, id);

    // Build the config once; we re-init if exclusive mode is refused and we
    // have to retry in shared mode. miniaudio returns MA_ACCESS_DENIED (device
    // in use by another exclusive app) or MA_BUSY for exclusive refusals.
    auto buildConfig = [&](ma_share_mode share) {
        ma_device_config c = ma_device_config_init(ma_device_type_playback);
        c.playback.pDeviceID = haveId ? &id : nullptr; // NULL = system default
        c.playback.format    = ma_format_f32;
        c.playback.channels  = 2;                       // interleaved stereo, like the engine
        c.playback.shareMode = share;
        c.sampleRate         = cfg.sampleRate > 0.0 ? static_cast<ma_uint32>(cfg.sampleRate) : 0;
        c.dataCallback       = &dataCallback;
        c.pUserData          = impl_;
        if (cfg.bufferFrames > 0)
            c.periodSizeInFrames = static_cast<ma_uint32>(cfg.bufferFrames);
        return c;
    };

    const ma_share_mode requested = cfg.wasapiExclusive ? ma_share_mode_exclusive
                                                        : ma_share_mode_shared;
    ma_device_config config = buildConfig(requested);
    ma_result r = ma_device_init(nullptr, &config, &impl_->device);
    if (r != MA_SUCCESS && requested == ma_share_mode_exclusive) {
        // Exclusive refused (device busy / not allowed / not supported) —
        // retry in shared mode and flag it so the UI can warn the user.
        exclusiveFallback_ = true;
        config = buildConfig(ma_share_mode_shared);
        r = ma_device_init(nullptr, &config, &impl_->device);
    }
    if (r != MA_SUCCESS) {
        return false;
    }
    impl_->deviceInited = true;

    if (ma_device_start(&impl_->device) != MA_SUCCESS) {
        stop();
        return false;
    }

    // Negotiated values, read back after init.
    sampleRate_   = impl_->device.sampleRate;
    bufferFrames_ = static_cast<int32_t>(impl_->device.playback.internalPeriodSizeInFrames);
    running_      = true;
    return true;
}

void MiniaudioBackend::stop() {
    if (impl_->deviceInited) {
        ma_device_uninit(&impl_->device);
        impl_->deviceInited = false;
    }
    running_ = false;
}

} // namespace nota
