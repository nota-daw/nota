// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "FakeAsioDriver.h"

#include <algorithm>

namespace nota::asio {

FakeAsioDriver::FakeAsioDriver(Config cfg) : cfg_(std::move(cfg)) {
    outTypes_.assign(static_cast<size_t>(std::max(0, cfg_.outputs)), cfg_.outputType);
    written_.resize(outTypes_.size());
    nonZero_.assign(outTypes_.size(), false);
}

bool FakeAsioDriver::init() {
    if (cfg_.initFails) {
        error_ = "device busy";
        return false;
    }
    initialized_ = true;
    return true;
}

bool FakeAsioDriver::getChannels(int32_t& inputs, int32_t& outputs) {
    if (!initialized_) return false;
    inputs = cfg_.inputs;
    outputs = cfg_.outputs;
    return true;
}

bool FakeAsioDriver::getBufferSize(int32_t& minSize, int32_t& maxSize, int32_t& preferred, int32_t& granularity) {
    minSize = maxSize = preferred = cfg_.bufferFrames;
    granularity = 0;
    return initialized_;
}

bool FakeAsioDriver::canSampleRate(double rate) {
    return std::find(cfg_.sampleRates.begin(), cfg_.sampleRates.end(), rate) != cfg_.sampleRates.end();
}

bool FakeAsioDriver::getSampleRate(double& rate) {
    rate = cfg_.sampleRate;
    return true;
}

bool FakeAsioDriver::setSampleRate(double rate) {
    if (!canSampleRate(rate)) return false;
    cfg_.sampleRate = rate;
    return true;
}

bool FakeAsioDriver::getChannelInfo(ChannelInfo& info) {
    const int32_t count = info.isInput ? cfg_.inputs : cfg_.outputs;
    if (info.index < 0 || info.index >= count) return false;
    info.type = info.isInput ? cfg_.inputType : outTypes_[static_cast<size_t>(info.index)];
    info.name = std::string(info.isInput ? "Fake In " : "Fake Out ") + std::to_string(info.index + 1);
    return true;
}

void FakeAsioDriver::setOutputType(int32_t channel, SampleType type) {
    if (channel >= 0 && channel < static_cast<int32_t>(outTypes_.size())) outTypes_[static_cast<size_t>(channel)] = type;
}

bool FakeAsioDriver::createBuffers(BufferInfo* infos, int32_t count, int32_t bufferSize, const Callbacks& cb) {
    if (!initialized_ || !infos || count <= 0 || bufferSize <= 0) return false;
    cb_ = cb;
    bufferFrames_ = bufferSize;
    infos_.assign(infos, infos + count);
    storage_.assign(static_cast<size_t>(count) * 2, {});
    for (int32_t i = 0; i < count; ++i) {
        auto& bi = infos[i];
        // 8 bytes per sample covers every type; fill with a pattern the host must overwrite.
        for (int half = 0; half < 2; ++half) {
            auto& mem = storage_[static_cast<size_t>(i) * 2 + half];
            mem.assign(static_cast<size_t>(bufferSize) * 8, 0x55);
            bi.buffers[half] = mem.data();
        }
        infos_[static_cast<size_t>(i)] = bi;
    }
    return true;
}

bool FakeAsioDriver::disposeBuffers() {
    infos_.clear();
    storage_.clear();
    return true;
}

bool FakeAsioDriver::start() {
    if (infos_.empty()) return false;
    running_ = true;
    return true;
}

bool FakeAsioDriver::stop() {
    running_ = false;
    return true;
}

bool FakeAsioDriver::getLatencies(int32_t& input, int32_t& output) {
    input = cfg_.inputLatency;
    output = cfg_.outputLatency;
    return true;
}

bool FakeAsioDriver::outputReady() {
    if (!cfg_.supportsOutputReady) return false;
    if (running_) ++outputReadyCalls_;
    return true;
}

void FakeAsioDriver::pump(int32_t count, bool evenIfStopped) {
    if (!cb_.bufferSwitch || silent_ || (!running_ && !evenIfStopped)) return;
    const size_t n = static_cast<size_t>(bufferFrames_);
    for (int32_t s = 0; s < count; ++s) {
        const int32_t half = switches_ & 1;
        std::vector<float> tmp(n);
        for (size_t i = 0; i < infos_.size(); ++i) {
            const BufferInfo& bi = infos_[i];
            void* buf = bi.buffers[half];
            if (bi.isInput) {
                for (size_t f = 0; f < n; ++f)
                    tmp[f] = cfg_.inputSignal ? cfg_.inputSignal(bi.channel, position_ + static_cast<int64_t>(f)) : 0.0f;
                fromFloat(tmp.data(), 1, cfg_.inputType, buf, bufferFrames_);
            } else {
                std::fill_n(static_cast<uint8_t*>(buf), n * 8, uint8_t{0x55}); // stale garbage
            }
        }
        cb_.bufferSwitch(half, position_);
        for (const BufferInfo& bi : infos_) {
            if (bi.isInput) continue;
            const auto ch = static_cast<size_t>(bi.channel);
            const SampleType t = outTypes_[ch];
            const auto* bytes = static_cast<const uint8_t*>(bi.buffers[half]);
            const size_t width = static_cast<size_t>(std::max(1, bytesPerSample(t)));
            for (size_t b = 0; b < n * width; ++b)
                if (bytes[b] != 0) nonZero_[ch] = true;
            toFloat(bi.buffers[half], t, tmp.data(), bufferFrames_, 1);
            written_[ch].insert(written_[ch].end(), tmp.begin(), tmp.end());
        }
        position_ += positionStep_ > 0 ? positionStep_ : bufferFrames_;
        ++switches_;
    }
}

int32_t FakeAsioDriver::sendMessage(int32_t selector, int32_t value) {
    return cb_.message ? cb_.message(selector, value) : 0;
}

void FakeAsioDriver::changeSampleRate(double rate) {
    cfg_.sampleRate = rate;
    if (cb_.sampleRateDidChange) cb_.sampleRateDidChange(rate);
}

} // namespace nota::asio
