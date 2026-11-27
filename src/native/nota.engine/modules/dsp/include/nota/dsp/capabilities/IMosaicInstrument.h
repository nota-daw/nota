// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IMosaicInstrument — optional capability interface for program-based multisample instruments (Mosaic).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/media/SampleBuffer.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::dsp::capabilities {

struct MosaicStatus {
    int32_t state = 0;
    int32_t filesDone = 0;
    int32_t filesTotal = 0;
    int32_t missing = 0;
    int32_t zones = 0;
    int32_t serial = 0;
    int64_t diskDone = 0;
    int64_t diskTotal = 0;
    int64_t ram = 0;
};

class NOTA_DSP_API IMosaicInstrument {
  public:
    virtual ~IMosaicInstrument();

    virtual void setProgram(const std::string& text) = 0;
    virtual std::string programText() const = 0;
    virtual MosaicStatus mosaicStatus() = 0;
    virtual std::shared_ptr<media::SampleBuffer> zoneSample(int32_t zone) const = 0;
    virtual std::vector<std::shared_ptr<media::SampleBuffer>> loadedSamples() const { return {}; }
};

} // namespace nota::dsp::capabilities
