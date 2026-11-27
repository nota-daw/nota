// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/AudioIoTypes.h"

namespace nota::io::audio {

/// Loads and persists `audio.json` in Nota's configured data directory.
class NOTA_IO_API AudioConfigStore {
  public:
    static AudioConfig load();
    static void save(const AudioConfig& cfg);
};

NOTA_IO_API AudioConfig loadAudioConfig();
NOTA_IO_API void saveAudioConfig(const AudioConfig& cfg);

} // namespace nota::io::audio
