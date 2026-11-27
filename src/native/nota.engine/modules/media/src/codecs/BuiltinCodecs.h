// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

namespace nota::media {
class AudioCodecRegistry;
}

namespace nota::media::detail {

void registerWavCodec(AudioCodecRegistry& reg);
void registerFlacCodec(AudioCodecRegistry& reg);
void registerMp3Codec(AudioCodecRegistry& reg);

} // namespace nota::media::detail
