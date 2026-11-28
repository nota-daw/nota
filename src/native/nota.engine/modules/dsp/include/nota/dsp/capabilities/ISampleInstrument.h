// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ISampleInstrument — optional capability interface for single/granular sample instruments.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/media/SampleBuffer.h"

#include <cstdint>
#include <memory>

namespace nota::dsp::capabilities {

class NOTA_DSP_API ISampleInstrument {
  public:
    virtual ~ISampleInstrument();

    virtual void setSample(std::shared_ptr<media::SampleBuffer> s, int32_t rootNote, bool loop) = 0;
    virtual void setRoot(int32_t rootNote) = 0;
    virtual std::shared_ptr<media::SampleBuffer> sample() const = 0;
    virtual int32_t rootNote() const = 0;
    virtual bool loopEnabled() const = 0;
    virtual float playPosition() const { return -1.0f; }
    virtual int32_t playPositions(float* /*out*/, int32_t /*maxN*/) const { return 0; }
};

} // namespace nota::dsp::capabilities
