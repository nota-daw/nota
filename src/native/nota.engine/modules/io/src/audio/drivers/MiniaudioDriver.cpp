// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if defined(_WIN32) || defined(__linux__)

#include "audio/drivers/AudioDrivers.h"
#include "audio/drivers/nota_miniaudio.h"

#include <algorithm>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::audio {
namespace {

std::string idToUid(const ma_device_id& id) {
    const auto* bytes = reinterpret_cast<const unsigned char*>(&id);
    std::string out;
    out.reserve(sizeof(id) * 2);
    char buf[3]{};
    for (std::size_t i = 0; i < sizeof(id); ++i) {
        std::snprintf(buf, sizeof(buf), "%02x", bytes[i]);
        out += buf;
    }
    return out;
}

bool enumerateMiniaudio(bool inputScope, std::vector<ma_device_info>& out) {
    ma_context ctx{};
    if (ma_context_init(nullptr, 0, nullptr, &ctx) != MA_SUCCESS) return false;
    if (ctx.backend == ma_backend_null) {
        ma_context_uninit(&ctx);
        return false;
    }
    ma_device_info* playback = nullptr;
    ma_uint32 playbackCount = 0;
    ma_device_info* capture = nullptr;
    ma_uint32 captureCount = 0;
    const ma_result r = ma_context_get_devices(&ctx, &playback, &playbackCount, &capture, &captureCount);
    if (r == MA_SUCCESS) {
        ma_device_info* src = inputScope ? capture : playback;
        const ma_uint32 n = inputScope ? captureCount : playbackCount;
        out.assign(src, src + n);
    }
    ma_context_uninit(&ctx);
    return r == MA_SUCCESS;
}

bool deviceIdForIndex(bool inputScope, uint32_t oneBasedIndex, ma_device_id& out) {
    if (oneBasedIndex == 0) return false;
    std::vector<ma_device_info> infos;
    if (!enumerateMiniaudio(inputScope, infos)) return false;
    if (oneBasedIndex > infos.size()) return false;
    out = infos[oneBasedIndex - 1].id;
    return true;
}

class MiniaudioOutputStream final : public IAudioOutputStream {
  public:
    MiniaudioOutputStream() = default;
    ~MiniaudioOutputStream() override {
        stop();
        if (nullContextInited_) ma_context_uninit(&nullContext_);
    }

    bool start(RenderCallback render, const OutputStreamConfig& cfg) override {
        if (running_) return true;
        render_ = std::move(render);
        exclusiveFallback_ = false;
        silentFallback_ = false;

        ma_device_id id{};
        const bool haveId = deviceIdForIndex(/*inputScope=*/false, cfg.device, id);

        auto buildConfig = [&](ma_share_mode share, bool useId = true) {
            ma_device_config c = ma_device_config_init(ma_device_type_playback);
            c.playback.pDeviceID = haveId && useId ? &id : nullptr;
            c.playback.format = ma_format_f32;
            c.playback.channels = 0;
            c.playback.shareMode = share;
            c.sampleRate = cfg.sampleRate > 0.0 ? static_cast<ma_uint32>(cfg.sampleRate) : 0;
            c.dataCallback = &dataCallback;
            c.pUserData = this;
            if (cfg.bufferFrames > 0) c.periodSizeInFrames = static_cast<ma_uint32>(cfg.bufferFrames);
            return c;
        };

        const ma_share_mode requested = cfg.wasapiExclusive ? ma_share_mode_exclusive : ma_share_mode_shared;
        ma_device_config config = buildConfig(requested);
        ma_result r = ma_device_init(nullptr, &config, &device_);
        if (r != MA_SUCCESS && requested == ma_share_mode_exclusive) {
            exclusiveFallback_ = true;
            config = buildConfig(ma_share_mode_shared);
            r = ma_device_init(nullptr, &config, &device_);
        }
        if (r != MA_SUCCESS && (haveId || cfg.sampleRate > 0.0 || cfg.bufferFrames > 0)) {
            config = buildConfig(ma_share_mode_shared, /*useId=*/false);
            config.sampleRate = 0;
            config.periodSizeInFrames = 0;
            r = ma_device_init(nullptr, &config, &device_);
        }
        if (r != MA_SUCCESS) {
            if (!nullContextInited_) {
                const ma_backend nullOnly[] = {ma_backend_null};
                nullContextInited_ = ma_context_init(nullOnly, 1, nullptr, &nullContext_) == MA_SUCCESS;
            }
            if (nullContextInited_) {
                config = buildConfig(ma_share_mode_shared, /*useId=*/false);
                r = ma_device_init(&nullContext_, &config, &device_);
            }
        }
        if (r != MA_SUCCESS) return false;

        deviceInited_ = true;
        silentFallback_ = device_.pContext != nullptr && device_.pContext->backend == ma_backend_null;
        if (ma_device_start(&device_) != MA_SUCCESS) {
            stop();
            return false;
        }

        channels_ = std::min<int32_t>(static_cast<int32_t>(device_.playback.channels), kMaxOutputChannels);
        sampleRate_ = device_.sampleRate;
        bufferFrames_ = static_cast<int32_t>(device_.playback.internalPeriodSizeInFrames);
        running_ = true;
        return true;
    }

    void stop() override {
        if (deviceInited_) {
            ma_device_uninit(&device_);
            deviceInited_ = false;
        }
        running_ = false;
    }

    double sampleRate() const override { return sampleRate_; }
    int32_t bufferFrames() const override { return bufferFrames_; }
    bool isRunning() const override { return running_; }
    bool exclusiveFallback() const override { return exclusiveFallback_; }
    bool silentFallback() const override { return silentFallback_; }
    int32_t outputChannelCount() const override { return running_ ? channels_ : 0; }
    void setOutputRouting(const OutputRouting& r) override { stage_.setRouting(r); }

  private:
    static void dataCallback(ma_device* dev, void* pOutput, const void*, ma_uint32 frameCount) {
        auto* self = static_cast<MiniaudioOutputStream*>(dev->pUserData);
        float* out = static_cast<float*>(pOutput);
        const int32_t ch = static_cast<int32_t>(dev->playback.channels);
        if (self && self->render_) {
            self->stage_.process(self->render_, out, ch, static_cast<int32_t>(frameCount));
        } else {
            std::memset(
                pOutput, 0, static_cast<std::size_t>(frameCount) * static_cast<std::size_t>(ch) * sizeof(float));
        }
    }

    ma_device device_{};
    bool deviceInited_ = false;
    ma_context nullContext_{};
    bool nullContextInited_ = false;
    RenderCallback render_;
    OutputStage stage_;
    int32_t channels_ = 2;
    double sampleRate_ = 0.0;
    int32_t bufferFrames_ = 0;
    bool running_ = false;
    bool exclusiveFallback_ = false;
    bool silentFallback_ = false;
};

class MiniaudioInputStream final : public IAudioInputStream {
  public:
    MiniaudioInputStream() = default;
    ~MiniaudioInputStream() override { stop(); }

    bool start(InputCallback cb, uint32_t device, double preferredRate) override {
        if (running_) return true;
        cb_ = std::move(cb);

        ma_device_id id{};
        const bool haveId = deviceIdForIndex(/*inputScope=*/true, device, id);

        ma_device_config config = ma_device_config_init(ma_device_type_capture);
        config.capture.pDeviceID = haveId ? &id : nullptr;
        config.capture.format = ma_format_f32;
        config.capture.channels = 0;
        config.sampleRate = preferredRate > 0.0 ? static_cast<ma_uint32>(preferredRate + 0.5) : 0;
        config.dataCallback = &captureCallback;
        config.pUserData = this;

        if (ma_device_init(nullptr, &config, &device_) != MA_SUCCESS) return false;
        deviceInited_ = true;
        if (ma_device_start(&device_) != MA_SUCCESS) {
            stop();
            return false;
        }
        sampleRate_ = device_.sampleRate;
        channels_ = static_cast<int32_t>(device_.capture.channels);
        running_ = true;
        return true;
    }

    void stop() override {
        if (deviceInited_) {
            ma_device_uninit(&device_);
            deviceInited_ = false;
        }
        running_ = false;
    }

    bool isRunning() const override { return running_; }
    double sampleRate() const override { return sampleRate_; }
    int32_t channelCount() const override { return running_ ? std::min(channels_, kMaxInputChannels) : 0; }
    std::string channelName(int32_t) const override { return {}; }

  private:
    static void captureCallback(ma_device* dev, void*, const void* pInput, ma_uint32 frameCount) {
        auto* self = static_cast<MiniaudioInputStream*>(dev->pUserData);
        if (self && self->cb_) {
            self->cb_(static_cast<const float*>(pInput), static_cast<int32_t>(frameCount),
                static_cast<int32_t>(dev->capture.channels));
        }
    }

    ma_device device_{};
    bool deviceInited_ = false;
    InputCallback cb_;
    double sampleRate_ = 0.0;
    int32_t channels_ = 0;
    bool running_ = false;
};

class MiniaudioDriver final : public IAudioDriver {
  public:
    std::string_view id() const noexcept override { return "miniaudio"; }
    std::string_view displayName() const noexcept override {
#if defined(_WIN32)
        return "WASAPI";
#else
        return "PulseAudio / ALSA";
#endif
    }
    DriverCapabilities capabilities() const noexcept override {
#if defined(_WIN32)
        return DriverCapabilities{true, true, false, true, false};
#else
        return DriverCapabilities{true, true, false, false, false};
#endif
    }

    std::vector<AudioDeviceInfo> enumerateDevices(bool inputScope) const override {
        std::vector<AudioDeviceInfo> out;
        std::vector<ma_device_info> infos;
        if (!enumerateMiniaudio(inputScope, infos)) return out;
        for (const auto& info : infos) {
            AudioDeviceInfo d;
            d.uid = idToUid(info.id);
            d.name = info.name[0] ? std::string(info.name) : d.uid;
            out.push_back(std::move(d));
        }
        return out;
    }

    uint32_t resolveDeviceId(std::string_view uid, bool inputScope) const override {
        if (uid.empty()) return 0u;
        std::vector<ma_device_info> infos;
        if (!enumerateMiniaudio(inputScope, infos)) return 0u;
        for (std::size_t i = 0; i < infos.size(); ++i) {
            if (idToUid(infos[i].id) == uid) return static_cast<uint32_t>(i + 1);
        }
        return 0u;
    }

    std::string effectiveDeviceUid(std::string_view uid, bool inputScope) const override {
        std::vector<ma_device_info> infos;
        if (!enumerateMiniaudio(inputScope, infos)) return {};
        for (const auto& info : infos) {
            if (!uid.empty() && idToUid(info.id) == uid) return std::string(uid);
        }
        for (const auto& info : infos) {
            if (info.isDefault) return idToUid(info.id);
        }
        return infos.empty() ? std::string{} : idToUid(infos.front().id);
    }

    std::unique_ptr<IAudioOutputStream> createOutputStream() const override {
        return std::make_unique<MiniaudioOutputStream>();
    }

    std::unique_ptr<IAudioInputStream> createInputStream() const override {
        return std::make_unique<MiniaudioInputStream>();
    }
};

} // namespace

namespace detail {

std::shared_ptr<const IAudioDriver> createMiniaudioDriver() {
    return std::make_shared<MiniaudioDriver>();
}

} // namespace detail
} // namespace nota::io::audio

#endif
