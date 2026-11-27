// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "codecs/BuiltinCodecs.h"
#include "nota/media/AudioCodecRegistry.h" // IWYU pragma: keep

#include <mutex>

namespace nota::media {

void registerBuiltinAudioCodecs() {
    static std::once_flag once;
    std::call_once(once, [] {
        auto& reg = AudioCodecRegistry::instance();
        detail::registerWavCodec(reg);
        detail::registerFlacCodec(reg);
        detail::registerMp3Codec(reg);
    });
}

} // namespace nota::media
