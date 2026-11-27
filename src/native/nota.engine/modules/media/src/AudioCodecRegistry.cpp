// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/media/AudioCodecRegistry.h"
#include "codecs/PathUtils.h"
#include "nota/media/IAudioEncoder.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/AudioCodecTypes.h"

#include <map>
#include <vector>
#include <utility>
#include <string>
#include <mutex>
#include <memory>

namespace nota::media {

AudioCodecRegistry& AudioCodecRegistry::instance() {
    static AudioCodecRegistry reg;
    return reg;
}

void AudioCodecRegistry::registerDecoder(AudioCodecDescriptor desc, DecoderFactory factory) {
    if (!factory) return;
    std::lock_guard<std::mutex> lk(mutex_);
    for (const auto& rawExt : desc.extensions) {
        const std::string ext = detail::normalizeExtension(rawExt);
        if (!ext.empty()) decodersByExt_[ext] = DecoderEntry{desc, factory};
    }
}

void AudioCodecRegistry::registerEncoder(AudioCodecDescriptor desc, EncoderFactory factory) {
    if (!factory || desc.formatId.empty()) return;
    std::lock_guard<std::mutex> lk(mutex_);
    const std::string key = detail::normalizeExtension(desc.formatId);
    encodersByFormat_[key] = EncoderEntry{std::move(desc), std::move(factory)};
}

bool AudioCodecRegistry::supportsDecodeExtension(const std::string& extOrPath) const {
    registerBuiltinAudioCodecs();
    const std::string ext = detail::normalizeExtension(extOrPath);
    if (ext.empty()) return false;
    std::lock_guard<std::mutex> lk(mutex_);
    return decodersByExt_.find(ext) != decodersByExt_.end();
}

bool AudioCodecRegistry::supportsEncodeFormat(const std::string& formatIdOrPath) const {
    registerBuiltinAudioCodecs();
    const std::string key = detail::normalizeExtension(formatIdOrPath);
    if (key.empty()) return false;
    std::lock_guard<std::mutex> lk(mutex_);
    return encodersByFormat_.find(key) != encodersByFormat_.end();
}

std::unique_ptr<IAudioDecoder> AudioCodecRegistry::openDecoder(const std::string& path) const {
    registerBuiltinAudioCodecs();
    const std::string ext = detail::normalizeExtension(path);
    if (ext.empty()) return nullptr;
    DecoderFactory factory;
    {
        std::lock_guard<std::mutex> lk(mutex_);
        auto it = decodersByExt_.find(ext);
        if (it == decodersByExt_.end()) return nullptr;
        factory = it->second.factory;
    }
    auto dec = factory();
    if (!dec || !dec->open(path)) return nullptr;
    return dec;
}

std::unique_ptr<IAudioEncoder> AudioCodecRegistry::openEncoder(
    const std::string& formatIdOrPath, const std::string& path, const AudioEncodeConfig& config) const {
    registerBuiltinAudioCodecs();
    const std::string key = detail::normalizeExtension(formatIdOrPath);
    if (key.empty()) return nullptr;
    EncoderFactory factory;
    {
        std::lock_guard<std::mutex> lk(mutex_);
        auto it = encodersByFormat_.find(key);
        if (it == encodersByFormat_.end()) return nullptr;
        factory = it->second.factory;
    }
    auto enc = factory();
    if (!enc || !enc->open(path, config)) return nullptr;
    return enc;
}

std::vector<AudioCodecDescriptor> AudioCodecRegistry::listCodecs() const {
    registerBuiltinAudioCodecs();
    std::lock_guard<std::mutex> lk(mutex_);
    std::map<std::string, AudioCodecDescriptor> merged;
    for (const auto& [ext, entry] : decodersByExt_) {
        auto& d = merged[entry.desc.formatId.empty() ? ext : entry.desc.formatId];
        if (d.formatId.empty()) d = entry.desc;
        d.canDecode = true;
    }
    for (const auto& [fmt, entry] : encodersByFormat_) {
        auto& d = merged[entry.desc.formatId.empty() ? fmt : entry.desc.formatId];
        if (d.formatId.empty()) d = entry.desc;
        d.canEncode = true;
    }
    std::vector<AudioCodecDescriptor> out;
    out.reserve(merged.size());
    for (auto& [id, d] : merged) out.push_back(std::move(d));
    return out;
}

} // namespace nota::media
